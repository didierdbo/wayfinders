"""FastAPI application — HTTP boundary for the game-logic layer.

Endpoints:

* ``GET /api/health`` — liveness/readiness probe. Returns ``{"status": "ok"}``.
* ``GET /api/units`` — roster of unit types as a JSON array. Schema matches
  the C# ``UnitData`` record on the Godot client side.
* ``POST /api/uc1/predict`` — UC1 resolution prediction. Takes three EN-rendered
  prose strings (character, action, context) and returns ``delta`` in [-5, +5].
  Returns HTTP 503 if the model checkpoint is not loaded.

Run locally::

    uv run uvicorn wayfinders.api.app:app --reload

To enable the UC1 predict endpoint, set the checkpoint path::

    WAYFINDERS_HEAD_CKPT=artifacts/models/checkpoints/head_best.pt \\
        uv run uvicorn wayfinders.api.app:app --reload
"""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, Request

from wayfinders.api.models import (
    HealthResponse,
    UC1PredictRequest,
    UC1PredictResponse,
    Unit,
)
from wayfinders.api.predictor import Predictor, PredictorNotReadyError
from wayfinders.api.seed_data import UNITS

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
def get_health() -> HealthResponse:
    """Liveness/readiness check used by the client and CI smoke tests."""
    return HealthResponse(status="ok")


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
