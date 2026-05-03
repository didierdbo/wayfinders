"""FastAPI application — HTTP boundary for the game-logic layer.

Endpoints:

* ``GET /api/health`` — liveness/readiness probe. Returns ``{"status": "ok"}``.
* ``GET /api/units`` — roster of unit types as a JSON array. Schema matches
  the C# ``UnitData`` record on the Godot client side.

Run locally::

    uv run uvicorn wayfinders.api.app:app --reload
"""

from __future__ import annotations

from fastapi import FastAPI

from wayfinders.api.models import HealthResponse, Unit
from wayfinders.api.seed_data import UNITS

app = FastAPI(
    title="Wayfinders Game Logic API",
    description=(
        "HTTP boundary for the wayfinders game-logic layer. "
        "Stateless, language-agnostic — called by the Godot+C# client."
    ),
    version="0.1.0",
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
