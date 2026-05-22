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
  uniform sampling when the predictor is not ready.  Side-effect: adds the
  emitted mission to the session-scoped MissionStore (if a mission emerged).
* ``GET /api/missions/active`` — Active mission list (Varn-lock 2026-05-17 §A).
  Returns ``list[EmergentMission]`` ordered by spawn tick ascending.  Missions
  remain active until resolved or concluded.  Session-scoped (clears on restart).
* ``POST /api/missions/{mission_id}/accept`` — Mark a pending mission as accepted.
  Idempotent (second call is a no-op, returns 200).  Returns 404 if not found.
* ``POST /api/missions/{mission_id}/decline`` — Remove a pending mission from the
  store (player declined).  The NPC may re-spawn in a later cadence window.
  Returns 404 if not found.  409 if already accepted (can't decline after accept).
* ``POST /api/missions/{mission_id}/conclude`` — Mark an accepted mission as concluded
  (success outcome: NPC joins the compagnie, does not re-spawn).  Returns 404 if not
  found.  409 if not in accepted state.
* ``POST /api/missions/{mission_id}/resolve`` — Conclude an accepted mission with an
  explicit typed outcome (success / failure / abandoned).  Distinct from conclude
  so the resolution-outcome type stays a typed contract for the recruit panel.
  Returns 404 if not found.  409 if not in accepted state.
* ``GET /api/world`` — World referential. Returns the full world geometry
  (World bounding box, all Cities with positions, all Districts with polygon
  footprints) loaded from ``assets/world/world.yaml`` via the J1 loader.
  Read-only, static per server version; the client should cache this at startup.
  Returns HTTP 500 if world.yaml is missing or invalid (service is broken).
* ``GET /api/world/poi_tree`` — POI hierarchy manifest. Returns the Varn-locked
  ``poi_tree.json`` (2026-05-15 spec v2 §2). Read-only, static per server
  version; the client should cache this at startup. Structure: flat dict keyed
  by PoiId with display_name, layer, parent, children per node.
* ``POST /api/world/mission/resolve`` — Mission resolution (DEPRECATED — use
  ``/api/world/mission/conclude``). Takes a ``MissionResolveRequest`` (mission
  fields + client-decided outcome + assigned personas) and returns
  ``PersonaLegacyTag`` objects. Kept for backward-compat with M1 tests.
  Side-effect: removes the mission from the MissionStore.
* ``POST /api/world/mission/conclude`` — Mission conclusion. Takes a
  ``MissionConcludeRequest`` (mission metadata + persona stat snapshots + seed
  + tick_due) and returns a server-computed ``MissionConcludeResponse``
  (weighted outcome roll + PersonaLegacyTags + roll_breakdown). One round-trip,
  atomically computed. Always returns 200. Stateless (NPC-autonomy lock
  2026-05-09).  Side-effect: removes the mission from the MissionStore.

Run locally::

    uv run uvicorn wayfinders.api.app:app --reload

To override the default ONNX artifact paths::

    WAYFINDERS_ENCODER_ONNX=artifacts/onnx/minilm-l6-v2-fp16.onnx \\
    WAYFINDERS_HEAD_ONNX=artifacts/onnx/resolution-head-v0.6.onnx \\
        uv run uvicorn wayfinders.api.app:app --reload
"""

from __future__ import annotations

import json
import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field

from wayfinders.api.mission_conclude import (
    MissionConcludeRequest,
    MissionConcludeResponse,
    conclude_mission,
)
from wayfinders.api.mission_resolve import (
    MissionResolveRequest,
    MissionResolveResponse,
    resolve_mission,
)
from wayfinders.api.mission_store import MissionStore
from wayfinders.api.models import (
    HealthResponse,
    UC1InfoResponse,
    UC1PredictRequest,
    UC1PredictResponse,
    Unit,
)
from wayfinders.api.predictor import Predictor, PredictorNotReadyError
from wayfinders.api.seed_data import UNITS
from wayfinders.api.world_referential_models import (
    CityDto,
    DistrictDto,
    PositionDto,
    WorldDto,
    WorldReferentialResponse,
)
from wayfinders.api.world_tick import EmergenceEngine
from wayfinders.api.world_tick_models import EmergentMission, WorldTickRequest, WorldTickResponse
from wayfinders.world.loader import WorldSchemaError, load_world_referential
from wayfinders.world.models import City, District, WorldReferential

logger = logging.getLogger(__name__)

# Path to the static POI tree manifest (Varn spec v2 §2, 2026-05-15).
# Loaded once at startup and held in app.state.poi_tree for all requests.
_POI_TREE_PATH: Path = Path(__file__).parent / "data" / "poi_tree.json"

# world.yaml schema version exposed in GET /api/world responses.
# Must stay in sync with the loader's _SUPPORTED_SCHEMA_VERSION constant.
_WORLD_SCHEMA_VERSION: int = 1


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
    # Load the POI tree manifest once at startup (read-only, static per server version).
    # Stored on app.state so the endpoint never re-reads the file per request.
    logger.info("Lifespan: loading POI tree from %s", _POI_TREE_PATH)
    app.state.poi_tree = json.loads(_POI_TREE_PATH.read_text(encoding="utf-8"))
    logger.info(
        "Lifespan: POI tree loaded — version=%s nodes=%d",
        app.state.poi_tree.get("version", "?"),
        len(app.state.poi_tree) - 1,  # subtract the "version" key
    )
    # Session-scoped mission store (Varn-lock 2026-05-17 §A).
    # Tracks active EmergentMissions across tick/resolve/conclude calls.
    # Not persistent across server restarts (M1 — no save layer).
    app.state.mission_store = MissionStore()
    logger.info("Lifespan: mission store initialised.")
    # Load the world referential once at startup (read-only, static per server version).
    # A missing or invalid world.yaml is a hard startup error — the service cannot
    # serve GET /api/world and will return HTTP 500 on every call if this fails.
    # Stored on app.state so the endpoint never re-loads the file per request.
    logger.info("Lifespan: loading world referential from assets/world/world.yaml")
    try:
        app.state.world_referential = load_world_referential()
        logger.info(
            "Lifespan: world referential loaded — cities=%d districts=%d",
            len(app.state.world_referential.cities),
            len(app.state.world_referential.districts),
        )
    except (WorldSchemaError, FileNotFoundError) as exc:
        logger.error("Lifespan: world referential load failed: %s", exc)
        app.state.world_referential = None
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
       Tick 1 is an unconditional first window (Varn-lock 2026-05-17).
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

    Side-effect: if a mission emerges, it is added to the session-scoped
    ``MissionStore`` so ``GET /api/missions/active`` reflects it immediately.
    """
    predictor: Predictor = request.app.state.predictor
    mission_store: MissionStore = request.app.state.mission_store
    engine = EmergenceEngine(predictor=predictor)
    # Pass already-targeted NPC ids for M1 cap enforcement (§A.8.D3).
    already_targeted = mission_store.active_recruit_npc_ids()
    response = engine.process_tick(payload, already_targeted_npc_ids=already_targeted)
    # Feed emitted mission into the session store via try_add (enforces N=3 cap).
    if response.mission is not None:
        admitted = mission_store.try_add(response.mission, spawn_tick=payload.tick)
        if not admitted:
            # This should not happen: EmergenceEngine already checked the cap via
            # active_recruit_npc_ids().  A race between two concurrent tick calls
            # on the same process (uvicorn --workers 1: not possible) could cause
            # this.  Log at warning and return the mission to the caller anyway —
            # the client-side defensive guard will drop it if it's a 4th recruit.
            logger.warning(
                "world_tick: try_add() refused mission %s after engine emitted it — "
                "possible race (unlikely on single-worker); returning mission to caller",
                response.mission.id,
            )
    return response


@app.get(
    "/api/missions/active",
    response_model=list[EmergentMission],
    tags=["world"],
    summary="Active mission list",
)
def missions_active(request: Request) -> list[EmergentMission]:
    """Return all currently-active emergent missions.

    "Active" means: emitted by POST /api/world/tick and not yet resolved or
    concluded via POST /api/world/mission/resolve or /conclude.

    Ordering: by spawn tick ascending (deterministic; ties broken by insertion
    order which tracks UUID hash uniqueness).

    The client (Rune's MissionStore autoload) calls this at E2 entry to
    bootstrap its local mission state from the server's session store.

    Session-scoped: the store is cleared on server restart.  In production
    the client's GameState.PendingMissions is the authoritative source; this
    endpoint is a convenience mirror for the initial query.

    Always returns HTTP 200.  Returns an empty list when no missions are active.
    """
    mission_store: MissionStore = request.app.state.mission_store
    return mission_store.active()


@app.get(
    "/api/world",
    response_model=WorldReferentialResponse,
    tags=["world"],
    summary="World referential — geometry + cities + districts",
    responses={
        500: {
            "description": (
                "World referential failed to load at startup. "
                "assets/world/world.yaml is missing or invalid."
            )
        }
    },
)
def get_world(request: Request) -> WorldReferentialResponse:
    """Return the full world referential for the Godot client.

    Exposes the world geometry loaded from ``assets/world/world.yaml`` at
    startup via the J1 loader (``load_world_referential``).  The payload is
    static per server version — the Godot client (Rune's ApiClient) should
    cache this at startup.

    Response schema (Varn-locked 2026-05-20):
      - ``schema_version``: mirrors ``world.yaml`` schema_version (currently 1).
      - ``world``: bounding box (max_x=500_000, max_y=250_000 m).
      - ``cities``: all cities with id, name, position, district_ids, bitmap.
      - ``districts``: all districts with id, name, parent_city_id, anchor,
        footprint (polygon vertices in world meters), cell_size_m.

    District footprint polygons are derived from the committed
    ``*.polygon.json`` sidecars (marching-squares on Mira's B/W bitmaps).
    Vertices are ``{x, y}`` in world meters; orientation (CW/CCW) is
    undefined — client code must be orientation-agnostic.

    Returns HTTP 500 if the world referential failed to load at startup
    (world.yaml missing or invalid).  The service cannot recover without
    a restart.
    """
    world_ref: WorldReferential | None = request.app.state.world_referential
    if world_ref is None:
        raise HTTPException(
            status_code=500,
            detail=(
                "World referential is not available. "
                "assets/world/world.yaml failed to load at startup. "
                "Fix the YAML or sidecar files and restart the server."
            ),
        )
    return _build_world_response(world_ref)


def _build_world_response(ref: WorldReferential) -> WorldReferentialResponse:
    """Convert a loaded WorldReferential to its wire-format DTO.

    Sorted by id ascending for deterministic JSON output (makes diffs and
    client caches stable across re-orderings in world.yaml).
    """
    cities: list[CityDto] = [
        _city_to_dto(c) for c in sorted(ref.cities.values(), key=lambda c: c.id)
    ]
    districts: list[DistrictDto] = [
        _district_to_dto(d) for d in sorted(ref.districts.values(), key=lambda d: d.id)
    ]
    return WorldReferentialResponse(
        schema_version=_WORLD_SCHEMA_VERSION,
        world=WorldDto(max_x=ref.world.max_x, max_y=ref.world.max_y),
        cities=cities,
        districts=districts,
    )


def _city_to_dto(city: City) -> CityDto:
    return CityDto(
        id=city.id,
        name=city.name,
        position=PositionDto(x=city.position.x, y=city.position.y),
        district_ids=list(city.district_ids),
        bitmap=city.bitmap,
    )


def _district_to_dto(district: District) -> DistrictDto:
    return DistrictDto(
        id=district.id,
        name=district.name,
        parent_city_id=district.parent_city_id,
        anchor=PositionDto(x=district.anchor.x, y=district.anchor.y),
        footprint=[PositionDto(x=v.x, y=v.y) for v in district.footprint],
        cell_size_m=district.cell_size_m,
    )


@app.get(
    "/api/world/poi_tree",
    tags=["world"],
    summary="POI hierarchy manifest",
    response_class=JSONResponse,
)
def get_poi_tree(request: Request) -> JSONResponse:
    """Return the Varn-locked POI hierarchy manifest.

    Serves the static ``poi_tree.json`` file loaded at startup.  The client
    (Rune's ApiClient) should cache this response — it is identical across
    all requests until the server is restarted with a new manifest version.

    Structure: flat dict keyed by PoiId string.  Each node has:
      - ``display_name``: human-readable name for tooltip display.
      - ``layer``:        int 1-3 (e1 region, e2 district, e3 sub-location).
      - ``parent``:       parent PoiId or null for root nodes.
      - ``children``:     list of child PoiId strings.

    Tooltip aggregation rule (Varn spec v2 §2): missions targeting a POI ``P``
    or any of its descendants (prefix match on PoiId string) should be shown
    in that POI's tooltip.  The client implements this with O(n_missions)
    prefix matching — no graph traversal needed.

    M1: Halfgate e1 only.  M2+ extends to e2/e3 nodes via Varn ratification.

    Always returns HTTP 200.
    """
    return JSONResponse(content=request.app.state.poi_tree)


# ---------------------------------------------------------------------------
# Mission action models (§A.8.D5, Varn-lock 2026-05-17)
# ---------------------------------------------------------------------------


class MissionActionRequest(BaseModel):
    """Empty request body for accept / decline (no payload required)."""

    model_config = ConfigDict(extra="forbid")


class MissionAcceptResponse(BaseModel):
    """Response for POST /api/missions/{mission_id}/accept."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    mission_id: str
    status: str  # "accepted"


class MissionDeclineResponse(BaseModel):
    """Response for POST /api/missions/{mission_id}/decline."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    mission_id: str
    status: str  # "declined"


class MissionConcludeActionRequest(BaseModel):
    """Request body for POST /api/missions/{mission_id}/conclude.

    outcome: "success" | "failure" | "abandoned"
    """

    model_config = ConfigDict(extra="forbid")

    outcome: str = Field(
        ...,
        description="Resolution outcome: success | failure | abandoned.",
    )


class MissionConcludeActionResponse(BaseModel):
    """Response for POST /api/missions/{mission_id}/conclude."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    mission_id: str
    status: str  # "concluded"
    outcome: str


class MissionResolveActionRequest(BaseModel):
    """Request body for POST /api/missions/{mission_id}/resolve.

    resolution_outcome_type: typed closed lookup for recruit resolution.
    """

    model_config = ConfigDict(extra="forbid")

    resolution_outcome_type: str = Field(
        ...,
        description=(
            "Typed resolution outcome for recruit missions. "
            "M1 closed lookup: success | failure | abandoned."
        ),
    )


class MissionResolveActionResponse(BaseModel):
    """Response for POST /api/missions/{mission_id}/resolve."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    mission_id: str
    status: str  # "resolved"
    resolution_outcome_type: str


# ---------------------------------------------------------------------------
# Mission action endpoints (§A.8.D5)
# ---------------------------------------------------------------------------


@app.post(
    "/api/missions/{mission_id}/accept",
    response_model=MissionAcceptResponse,
    tags=["world"],
    summary="Accept a pending mission",
    responses={
        404: {"description": "Mission not found in the active store."},
    },
)
def mission_accept(
    mission_id: str,
    payload: MissionActionRequest,  # empty body, required for POST contract
    request: Request,
) -> MissionAcceptResponse:
    """Mark a pending mission as accepted.

    Idempotent: a second call on an already-accepted mission returns 200 (no-op).
    The mission remains in the active store with its accepted flag set.
    ``GET /api/missions/active`` continues to return it until conclude/resolve removes it.

    Returns 404 if the mission_id is not in the current session store.
    """
    mission_store: MissionStore = request.app.state.mission_store
    found = mission_store.accept(mission_id)
    if not found:
        raise HTTPException(
            status_code=404,
            detail=f"Mission {mission_id!r} not found in the active store.",
        )
    return MissionAcceptResponse(mission_id=mission_id, status="accepted")


@app.post(
    "/api/missions/{mission_id}/decline",
    response_model=MissionDeclineResponse,
    tags=["world"],
    summary="Decline (remove) a pending mission",
    responses={
        404: {"description": "Mission not found in the active store."},
        409: {"description": "Mission is already accepted — cannot decline after accept."},
    },
)
def mission_decline(
    mission_id: str,
    payload: MissionActionRequest,
    request: Request,
) -> MissionDeclineResponse:
    """Remove a pending mission from the active store (player declined).

    The NPC is freed — the emergence engine may re-emit a mission targeting
    the same NPC in a later cadence window.

    Returns 404 if the mission_id is not in the active store.
    Returns 409 if the mission has already been accepted (decline after accept
    is not allowed; use conclude or resolve instead).
    """
    mission_store: MissionStore = request.app.state.mission_store
    if mission_store.get(mission_id) is None:
        raise HTTPException(
            status_code=404,
            detail=f"Mission {mission_id!r} not found in the active store.",
        )
    if mission_store.is_accepted(mission_id):
        raise HTTPException(
            status_code=409,
            detail=(
                f"Mission {mission_id!r} is already accepted. "
                "Use /conclude or /resolve to end an accepted mission."
            ),
        )
    mission_store.remove(mission_id)
    return MissionDeclineResponse(mission_id=mission_id, status="declined")


@app.post(
    "/api/missions/{mission_id}/conclude",
    response_model=MissionConcludeActionResponse,
    tags=["world"],
    summary="Conclude an accepted mission (success/failure/abandoned)",
    responses={
        404: {"description": "Mission not found in the active store."},
        409: {"description": "Mission is not in accepted state."},
    },
)
def mission_conclude_action(
    mission_id: str,
    payload: MissionConcludeActionRequest,
    request: Request,
) -> MissionConcludeActionResponse:
    """Conclude an accepted mission with an outcome.

    On success: the NPC is considered recruited.  Removes the mission from the
    active store (does not re-spawn the NPC in future cadence windows).
    On failure / abandoned: the mission is removed; future behaviour depends on
    game-logic (out of scope for this endpoint — no server-side NPC state in M1).

    Returns 404 if the mission_id is not in the active store.
    Returns 409 if the mission has not been accepted (must accept before conclude).

    Idempotent on identical body: if the mission_id is already gone (previously
    concluded), returns 404 — the client should handle this gracefully.
    """
    mission_store: MissionStore = request.app.state.mission_store
    if mission_store.get(mission_id) is None:
        raise HTTPException(
            status_code=404,
            detail=f"Mission {mission_id!r} not found in the active store.",
        )
    if not mission_store.is_accepted(mission_id):
        raise HTTPException(
            status_code=409,
            detail=(
                f"Mission {mission_id!r} is not in accepted state. Call /accept before /conclude."
            ),
        )
    mission_store.remove(mission_id)
    return MissionConcludeActionResponse(
        mission_id=mission_id,
        status="concluded",
        outcome=payload.outcome,
    )


@app.post(
    "/api/missions/{mission_id}/resolve",
    response_model=MissionResolveActionResponse,
    tags=["world"],
    summary="Resolve an accepted recruit mission with a typed outcome",
    responses={
        404: {"description": "Mission not found in the active store."},
        409: {"description": "Mission is not in accepted state."},
    },
)
def mission_resolve_action(
    mission_id: str,
    payload: MissionResolveActionRequest,
    request: Request,
) -> MissionResolveActionResponse:
    """Resolve an accepted recruit mission with a typed ResolutionOutcomeType.

    Distinct from ``/conclude`` so the resolution-outcome type stays a typed
    contract — the E2.3 recruit panel will read ``resolution_outcome_type``
    to determine panel state (success = NPC joins; failure = declined; etc.).

    For M1 the server does not enforce a closed enum on ``resolution_outcome_type``
    (free-form string); M2 will promote this to a Varn-locked Literal.

    Returns 404 if the mission_id is not in the active store.
    Returns 409 if the mission has not been accepted.
    """
    mission_store: MissionStore = request.app.state.mission_store
    if mission_store.get(mission_id) is None:
        raise HTTPException(
            status_code=404,
            detail=f"Mission {mission_id!r} not found in the active store.",
        )
    if not mission_store.is_accepted(mission_id):
        raise HTTPException(
            status_code=409,
            detail=(
                f"Mission {mission_id!r} is not in accepted state. Call /accept before /resolve."
            ),
        )
    mission_store.remove(mission_id)
    return MissionResolveActionResponse(
        mission_id=mission_id,
        status="resolved",
        resolution_outcome_type=payload.resolution_outcome_type,
    )


@app.post(
    "/api/world/mission/resolve",
    response_model=MissionResolveResponse,
    tags=["world"],
    summary="Mission resolution — produce PersonaLegacyTags",
)
def mission_resolve(payload: MissionResolveRequest, request: Request) -> MissionResolveResponse:
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

    Side-effect: removes the mission from the session-scoped MissionStore so
    ``GET /api/missions/active`` no longer returns it.

    Always returns HTTP 200.
    """
    result = resolve_mission(payload)
    # Remove from session store (idempotent — safe if already absent).
    mission_store: MissionStore = request.app.state.mission_store
    mission_store.remove(payload.mission_id)
    return result


@app.post(
    "/api/world/mission/conclude",
    response_model=MissionConcludeResponse,
    tags=["world"],
    summary="Mission conclusion — server-computed weighted outcome + PersonaLegacyTags",
)
def mission_conclude(payload: MissionConcludeRequest, request: Request) -> MissionConcludeResponse:
    """Conclude a mission: server computes outcome roll + creates PersonaLegacyTags atomically.

    The outcome is NOT supplied by the client.  The server derives it
    deterministically from the persona stat-buckets, mission difficulty,
    and the seed/tick_due pair.

    Roll algorithm (Varn-ratified 2026-05-10):
    - stat lane: scout_route → dex_bucket, parley_local → wis_bucket.
    - delta = ordinal(persona_bucket) - ordinal(difficulty).
    - Roll seed: SHA-256(f"{seed}:{tick_due}:{persona_id}") → first 8 bytes as uint64.
    - Outcome sampled from the probability table (see module docstring).
    - Multi-persona (M3): best persona carries the roll.  M2: single persona.

    Replaces ``/api/world/mission/resolve`` which required the client to
    compute and send the outcome — violating the server-authority contract for
    the probability table.

    ``assigned_personas`` must contain at least one persona (422 if empty —
    client must filter ``decline`` before calling this endpoint).

    Side-effect: removes the mission from the session-scoped MissionStore so
    ``GET /api/missions/active`` no longer returns it.

    Always returns HTTP 200.
    """
    result = conclude_mission(payload)
    # Remove from session store (idempotent — safe if already absent).
    mission_store: MissionStore = request.app.state.mission_store
    mission_store.remove(payload.mission_id)
    return result
