"""Tests for ``POST /api/world/reset``.

Verifies the core bug-fix contract:
  - After reset, tick=1 re-emits a mission even when the same backend process
    has already served tick=1 in a previous run.
  - Reset with an empty store returns missions_cleared=0 (idempotent).
  - Reset with N active missions returns missions_cleared=N and empties the store.
  - GET /api/missions/active returns [] immediately after reset.
"""

from __future__ import annotations

from collections.abc import Iterator
from unittest.mock import MagicMock

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app
from wayfinders.api.mission_store import MissionStore

# ---------------------------------------------------------------------------
# Sample prose — EN-pinned halfgate context, tick 1 always in window
# ---------------------------------------------------------------------------

_CONTEXT_HALFGATE = (
    "The mission is a reconnaissance near Halfgate.\n"
    "The mission objective is to map the road beyond the ridge before dawn.\n"
    "The stakes are high: failure means the company moves blind into the valley."
)

_TICK1_REQUEST = {
    "tick": 1,
    "seed": 42,
    "context_prose": _CONTEXT_HALFGATE,
}


# ---------------------------------------------------------------------------
# Mock helpers
# ---------------------------------------------------------------------------


def _make_predictor_ready(delta: float = 1.5) -> MagicMock:
    mock = MagicMock()
    mock.ready = True
    mock.predict.return_value = delta
    return mock


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture
def client() -> Iterator[tuple[TestClient, MissionStore]]:
    """TestClient with a fresh MissionStore and a ready predictor mock."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        app.state.predictor = _make_predictor_ready()
        fresh_store = MissionStore()
        app.state.mission_store = fresh_store
        yield tc, fresh_store


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestWorldReset:
    def test_reset_empty_store_returns_zero(self, client: tuple[TestClient, MissionStore]) -> None:
        """Reset with no active missions returns missions_cleared=0."""
        tc, _ = client
        response = tc.post("/api/world/reset")

        assert response.status_code == 200
        body = response.json()
        assert body["status"] == "reset"
        assert body["missions_cleared"] == 0

    def test_reset_clears_active_missions(self, client: tuple[TestClient, MissionStore]) -> None:
        """After seeding the store, reset empties it and reports the count."""
        tc, store = client
        # Inject a mission directly into the store (avoids HTTP tick round-trip).
        from wayfinders.api.world_tick import _stable_mission_id
        from wayfinders.api.world_tick_models import EmergentMission

        mission = EmergentMission(
            id=_stable_mission_id(1, 42),
            type="recruit",
            narrative_hook="test hook",
            eligible_personas=(),
            difficulty="mid",
            region="halfgate",
            target_poi="e1.halfgate",
            deadline_ticks=None,
            outcome=None,
            seed=42,
            recruit_target_npc_id="kira",
        )
        store.add(mission, spawn_tick=1)
        assert len(store) == 1

        response = tc.post("/api/world/reset")

        assert response.status_code == 200
        body = response.json()
        assert body["status"] == "reset"
        assert body["missions_cleared"] == 1
        assert len(store) == 0

    def test_active_list_empty_after_reset(self, client: tuple[TestClient, MissionStore]) -> None:
        """GET /api/missions/active returns [] immediately after reset."""
        tc, store = client
        from wayfinders.api.world_tick import _stable_mission_id
        from wayfinders.api.world_tick_models import EmergentMission

        mission = EmergentMission(
            id=_stable_mission_id(1, 42),
            type="recruit",
            narrative_hook="test hook",
            eligible_personas=(),
            difficulty="mid",
            region="halfgate",
            target_poi="e1.halfgate",
            deadline_ticks=None,
            outcome=None,
            seed=42,
            recruit_target_npc_id="kira",
        )
        store.add(mission, spawn_tick=1)

        tc.post("/api/world/reset")

        active_response = tc.get("/api/missions/active")
        assert active_response.status_code == 200
        assert active_response.json() == []

    def test_tick1_re_emits_kira_after_reset(self, client: tuple[TestClient, MissionStore]) -> None:
        """Core bug-fix: tick=1 re-emits kira after reset.

        Because _stable_mission_id is keyed on (tick, seed), all tick=1 calls
        share the same mission id.  The store's idempotent-replace path means
        repeated tick=1 calls without intervening resolve/reset oscillate
        between NPCs (kira → dorn → kira → dorn...) rather than accumulating
        three entries and hitting cap.

        Regardless of this oscillation, reset restores the clean-slate
        invariant: the very next tick=1 picks kira (first in roster order)
        and uses the same stable id.  This is the debug-run contract.

        run 1 — tick=1 → kira (store empty)
        run 2 — tick=1 → dorn (kira in store; id collision → replaced)
        reset  — store cleared (1 mission cleared)
        run 3 — tick=1 → kira again (store empty again)
        """
        tc, _ = client

        # Run 1: kira emitted.
        r1 = tc.post("/api/world/tick", json=_TICK1_REQUEST)
        assert r1.status_code == 200
        m1 = r1.json()["mission"]
        assert m1 is not None, "tick=1 must always emit (Varn-lock 2026-05-17)"
        assert m1["recruit_target_npc_id"] == "kira"
        mission_id = m1["id"]

        # Run 2: kira is in the store, so dorn is picked next (oscillation).
        r2 = tc.post("/api/world/tick", json=_TICK1_REQUEST)
        m2 = r2.json()["mission"]
        assert m2 is not None
        assert m2["recruit_target_npc_id"] == "dorn"
        # Same stable id — idempotent replace in the store.
        assert m2["id"] == mission_id

        # Reset: clears the one entry (dorn, same id slot).
        reset_resp = tc.post("/api/world/reset")
        assert reset_resp.json()["status"] == "reset"
        assert reset_resp.json()["missions_cleared"] == 1

        # Run 3: store is empty again → kira is first pick, same stable id.
        r3 = tc.post("/api/world/tick", json=_TICK1_REQUEST)
        assert r3.status_code == 200
        m3 = r3.json()["mission"]
        assert m3 is not None, "tick=1 must re-emit kira after reset"
        assert m3["recruit_target_npc_id"] == "kira"
        assert m3["id"] == mission_id

    def test_reset_response_schema(self, client: tuple[TestClient, MissionStore]) -> None:
        """Wire schema must have exactly {status, missions_cleared}."""
        tc, _ = client
        response = tc.post("/api/world/reset")

        assert response.status_code == 200
        body = response.json()
        assert set(body.keys()) == {"status", "missions_cleared"}
        assert isinstance(body["missions_cleared"], int)
