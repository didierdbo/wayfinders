"""FastAPI application — HTTP boundary for the game-logic layer.

Endpoints:

* ``GET /api/health`` — liveness/readiness probe.
  Returns ``{"status": "ok", "model_ready": bool}``.
* ``GET /api/units`` — roster of unit types as a JSON array. Schema matches
  the C# ``UnitData`` record on the Godot client side.
* ``GET /api/uc1/info`` — UC1 model debug info: artifact paths + readiness.
* ``POST /api/uc1/predict`` — UC1 resolution prediction. Takes three EN-rendered
  prose strings (character, action, context) and returns ``delta`` in [-5, +5].
  Returns HTTP 503 if the model ONNX sessions are not loaded.
* ``POST /api/world/tick`` — Mission-emergence tick. Takes a ``WorldTickRequest``
  (tick index, seed, context_prose) and returns a ``WorldTickResponse``
  (emergent mission or None). Always returns 200; degrades gracefully to
  uniform sampling when the predictor is not ready.
* ``POST /api/world/mission/resolve`` — Mission resolution. Takes a
  ``MissionResolveRequest`` (mission fields + outcome + assigned personas)
  and returns a ``MissionResolveResponse`` with ``PersonaLegacyTag`` objects
  for the client to store in its GameState. Always returns 200. Stateless:
  no GameState is persisted server-side (NPC-autonomy lock 2026-05-09).

Run locally::

    uv run uvicorn wayfinders.api.app:app --reload

To override the default ONNX artifact paths::

    WAYFINDERS_ENCODER_ONNX=artifacts/onnx/minilm-l6-v2-fp16.onnx \\
    WAYFINDERS_HEAD_ONNX=artifacts/onnx/resolution-head-v0.6.onnx \\
        uv run uvicorn wayfinders.api.app:app --reload
"""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, Request

from wayfinders.api.mission_resolve import (
    MissionResolveRequest,
    MissionResolveResponse,
    resolve_mission,
)
from wayfinders.api.models import (
    HealthResponse,
    UC1InfoResponse,
    UC1PredictRequest,
    UC1PredictResponse,
    Unit,
)
from wayfinders.api.predictor import Predictor, PredictorNotReadyError
from wayfinders.api.seed_data import UNITS
from wayfinders.api.world_tick import EmergenceEngine
from wayfinders.api.world_tick_models import WorldTickRequest, WorldTickResponse

logger = logging.getLogger(__name__)


@asynccontextmanager
async def _lifespan(app: FastAPI) -> AsyncIterator[None]:
    """Load the ML models once at startup, store on app.state.

    The Predictor constructor is synchronous and blocks the event loop
    briefly on startup. This is acceptable for a dev-facing service; a
    production deployment would run this in a thread pool executor. For M1
    (single-instance dev server) the current approach is the right tradeoff.
    """
    logger.info("Lifespan: loading predictor...")
    app.state.predictor = Predictor()
    if app.state.predictor.ready:
        logger.info("Lifespan: predictor ready (device=%s)", app.state.predictor.device)
    else:
        logger.warning(
            "Lifespan: predictor not ready — "
            "/api/uc1/predict will return 503 until a checkpoint is configured."
        )
    yield
    # Cleanup: nothing to release (encoder/head are Python objects, GC handles them).
    logger.info("Lifespan: shutdown.")


app = FastAPI(
    title="Wayfinders Game Logic API",
    description=(
        "HTTP boundary for the wayfinders game-logic layer. "
        "Stateless, language-agnostic — called by the Godot+C# client."
    ),
    version="0.1.0",
    lifespan=_lifespan,
)


@app.get("/api/health", response_model=HealthResponse, tags=["meta"])
def get_health(request: Request) -> HealthResponse:
    """Liveness/readiness check used by the client and CI smoke tests.

    ``model_ready`` lets Rune's ApiClient gate on ML availability before
    calling ``/api/uc1/predict``.  The endpoint itself always returns 200
    regardless of model state (liveness vs readiness separation).
    """
    predictor: Predictor = request.app.state.predictor
    return HealthResponse(status="ok", model_ready=predictor.ready)


@app.get("/api/uc1/info", response_model=UC1InfoResponse, tags=["uc1"])
def uc1_info(request: Request) -> UC1InfoResponse:
    """Debug info for the UC1 inference backend.

    Returns the resolved ONNX artifact paths, readiness flag, and device
    descriptor.  Intended for Rune's ApiClient startup handshake and ops
    tooling.  Does not trigger inference.
    """
    predictor: Predictor = request.app.state.predictor
    return UC1InfoResponse(
        model_ready=predictor.ready,
        encoder_onnx_path=predictor.encoder_onnx_path,
        head_onnx_path=predictor.head_onnx_path,
        device=predictor.device,
    )


@app.get("/api/units", response_model=list[Unit], tags=["roster"])
def list_units() -> list[Unit]:
    """Return the roster of available unit types.

    The list is currently a static seed — persistence and content
    pipeline will arrive in a follow-up PR.
    """
    return list(UNITS)


@app.post(
    "/api/uc1/predict",
    response_model=UC1PredictResponse,
    tags=["uc1"],
    summary="UC1 resolution prediction",
    responses={
        503: {
            "description": (
                "Model not ready. The encoder or head checkpoint is not loaded. "
                "Set WAYFINDERS_HEAD_CKPT to a valid checkpoint path and restart."
            )
        }
    },
)
def uc1_predict(payload: UC1PredictRequest, request: Request) -> UC1PredictResponse:
    """Predict resolution delta for a UC1 action attempt.

    All prose strings must be EN-rendered (Renderer English-pinning contract).
    The endpoint does not render prose itself — that is the game engine's job.

    Returns ``delta`` in [-5, +5]. The game engine adds this to the base
    modifier to produce the final resolution delta for the scene.

    Returns HTTP 503 if the model checkpoint is not configured or failed to
    load at startup.
    """
    predictor: Predictor = request.app.state.predictor
    try:
        delta = predictor.predict(
            char_prose=payload.char_prose,
            action_prose=payload.action_prose,
            context_prose=payload.context_prose,
        )
    except PredictorNotReadyError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc

    return UC1PredictResponse(delta=delta)


@app.post(
    "/api/world/tick",
    response_model=WorldTickResponse,
    tags=["world"],
    summary="Mission-emergence world tick",
)
def world_tick(payload: WorldTickRequest, request: Request) -> WorldTickResponse:
    """Process a world tick and return an emergent mission (or None).

    Implements the M1 cadence-gated emergence algorithm:

    1. Cadence gate: a mission window opens every 5-10 ticks (seed-driven).
       Ticks outside the window return ``mission=None``.
    2. Within a window: UC1 proxy logits → softmax → seed-seeded sampling
       over ``{scout_route, parley_local, no_emergence}``.
    3. M1 fallback: ``no_emergence`` within a window → ``argmax`` of the
       two active types (ensures the chain always produces a mission within
       the window, per spec §6).

    Always returns HTTP 200.  When the predictor is not ready the engine
    falls back to uniform sampling — the endpoint does not gate on model
    availability (unlike ``/api/uc1/predict`` which returns 503).

    All prose must be EN-rendered (Renderer English-pinning contract).
    The ``seed`` must be caller-derived from ``(world_seed, tick)`` — the
    server holds no global RNG state.  Same ``(tick, seed, context_prose)``
    always returns the same mission.
    """
    predictor: Predictor = request.app.state.predictor
    engine = EmergenceEngine(predictor=predictor)
    return engine.process_tick(payload)


@app.post(
    "/api/world/mission/resolve",
    response_model=MissionResolveResponse,
    tags=["world"],
    summary="Mission resolution — produce PersonaLegacyTags",
)
def mission_resolve(payload: MissionResolveRequest) -> MissionResolveResponse:
    """Resolve a mission and return the ``PersonaLegacyTag`` objects for the client.

    Stateless: no ``GameState`` is persisted server-side.  The server
    reconstructs the ``EmergentMission`` from the request fields, runs the
    ``on_mission_resolved`` hook against a transient ``GameState``, and
    returns the produced tags.  The client stores them in its own Godot-side
    ``GameState.PersonaLegacy`` (NPC-autonomy lock 2026-05-09).

    Idempotence: same ``(mission_id, outcome, tick, assigned_personas)``
    always produces the same tags.  The client is responsible for
    deduplication across retries using ``(persona_id, mission_id)`` as the
    natural key.

    Edge cases:
    - ``assigned_personas=[]``: mission was declined or no personas participated.
      Returns ``tags_created=[]``. Always 200.
    - ``assigned_personas`` with persona names not matching any filter:
      tags are still created (the resolution hook does not re-filter eligibility;
      that is the gameplay layer's responsibility).

    Always returns HTTP 200.
    """
    return resolve_mission(payload)
