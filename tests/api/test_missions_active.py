"""Tests for ``GET /api/missions/active``.

Three scenarios from the spec:
  (a) Empty initial list.
  (b) After spawning 1 mission via POST /api/world/tick, it appears.
  (c) After resolving it via POST /api/world/mission/resolve, it's gone.

Also covers ordering (tick ascending) and the full EmergentMission wire schema.
"""

from __future__ import annotations

from collections.abc import Iterator
from unittest.mock import MagicMock

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app
from wayfinders.api.mission_store import MissionStore
from wayfinders.api.world_tick import _in_cadence_window

# ---------------------------------------------------------------------------
# Sample prose (EN-pinned, halfgate context)
# ---------------------------------------------------------------------------

_CONTEXT_HALFGATE = (
    "The mission is a reconnaissance near Halfgate.\n"
    "The mission objective is to map the road beyond the ridge before dawn.\n"
    "The stakes are high: failure means the company moves blind into the valley."
)


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
def client_with_store() -> Iterator[tuple[TestClient, MissionStore]]:
    """TestClient with a fresh MissionStore + ready predictor mock."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        app.state.predictor = _make_predictor_ready()
        # Replace the store with a fresh one for test isolation.
        fresh_store = MissionStore()
        app.state.mission_store = fresh_store
        yield tc, fresh_store


# ---------------------------------------------------------------------------
# (a) Empty initial list
# ---------------------------------------------------------------------------


class TestMissionsActiveEmpty:
    def test_empty_on_fresh_store(self, client_with_store: tuple[TestClient, MissionStore]) -> None:
        tc, _ = client_with_store
        response = tc.get("/api/missions/active")
        assert response.status_code == 200
        assert response.json() == []

    def test_returns_200_when_empty(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, _ = client_with_store
        response = tc.get("/api/missions/active")
        assert response.status_code == 200

    def test_returns_json_content_type(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, _ = client_with_store
        response = tc.get("/api/missions/active")
        assert response.headers["content-type"].startswith("application/json")


# ---------------------------------------------------------------------------
# (b) After spawning 1 mission, it appears in /api/missions/active
# ---------------------------------------------------------------------------


class TestMissionsActiveAfterSpawn:
    def test_mission_appears_after_tick(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, _ = client_with_store
        # Use tick=1 (unconditional cadence window per Varn-lock 2026-05-17).
        payload = {"tick": 1, "seed": 42, "context_prose": _CONTEXT_HALFGATE}
        tick_resp = tc.post("/api/world/tick", json=payload)
        assert tick_resp.status_code == 200
        mission = tick_resp.json()["mission"]
        assert mission is not None  # tick=1 must always produce a mission

        # Now check /api/missions/active.
        active_resp = tc.get("/api/missions/active")
        assert active_resp.status_code == 200
        active = active_resp.json()
        assert len(active) == 1
        assert active[0]["id"] == mission["id"]

    def test_spawned_mission_has_e2_target_poi_for_halfgate(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Halfgate context at tick=1 must produce e2.halfgate.* target_poi."""
        tc, _ = client_with_store
        tc.post("/api/world/tick", json={"tick": 1, "seed": 7, "context_prose": _CONTEXT_HALFGATE})
        active = tc.get("/api/missions/active").json()
        assert len(active) == 1
        assert active[0]["target_poi"].startswith("e2.halfgate.")

    def test_spawned_mission_fields_complete(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Active mission must have all EmergentMission fields (§A.8.D3: includes recruit_target_npc_id)."""
        tc, _ = client_with_store
        tc.post("/api/world/tick", json={"tick": 1, "seed": 99, "context_prose": _CONTEXT_HALFGATE})
        active = tc.get("/api/missions/active").json()
        assert len(active) == 1
        mission = active[0]
        expected_fields = {
            "id",
            "type",
            "narrative_hook",
            "eligible_personas",
            "difficulty",
            "region",
            "target_poi",
            "deadline_ticks",
            "outcome",
            "seed",
            "recruit_target_npc_id",  # §A.8.D3 Varn-lock 2026-05-17
            "position",  # Varn-locked field (spec 2026-05-20 §6) — added in J1
        }
        assert set(mission.keys()) == expected_fields

    def test_no_mission_outside_window_not_in_active(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """A tick outside the cadence window must not add a mission to /active."""
        tc, _ = client_with_store
        seed = 42
        # Find a tick outside the window (must be >= 2 since tick 1 is forced).
        outside_tick = next(t for t in range(2, 200) if not _in_cadence_window(t, seed))
        tc.post(
            "/api/world/tick",
            json={"tick": outside_tick, "seed": seed, "context_prose": _CONTEXT_HALFGATE},
        )
        active = tc.get("/api/missions/active").json()
        assert active == []

    def test_two_missions_both_appear(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Two ticks in cadence window must produce two missions in /active."""
        tc, _ = client_with_store
        seed_a, seed_b = 10, 20
        # tick=1 is always a window for any seed.
        tc.post(
            "/api/world/tick", json={"tick": 1, "seed": seed_a, "context_prose": _CONTEXT_HALFGATE}
        )
        # Find another window tick for seed_b.
        window_tick_b = next(t for t in range(2, 200) if _in_cadence_window(t, seed_b))
        tc.post(
            "/api/world/tick",
            json={"tick": window_tick_b, "seed": seed_b, "context_prose": _CONTEXT_HALFGATE},
        )
        active = tc.get("/api/missions/active").json()
        assert len(active) == 2

    def test_active_ordered_by_spawn_tick_ascending(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Missions must appear in spawn tick ascending order."""
        tc, store = client_with_store
        # Directly inject missions with known ticks into the store for ordering test.
        from wayfinders.api.world_tick_models import EmergentMission

        m_late = EmergentMission(
            id="mission-late",
            type="scout_route",
            narrative_hook="A late mission.",
            eligible_personas=(),
            difficulty="mid",
            region="halfgate",
            target_poi="e2.halfgate.gateway",
            deadline_ticks=None,
            outcome=None,
            seed=99,
        )
        m_early = EmergentMission(
            id="mission-early",
            type="parley_local",
            narrative_hook="An early mission.",
            eligible_personas=(),
            difficulty="mid",
            region="halfgate",
            target_poi="e2.halfgate.intramuros",
            deadline_ticks=None,
            outcome=None,
            seed=1,
        )
        store.add(m_late, spawn_tick=10)
        store.add(m_early, spawn_tick=1)

        active = tc.get("/api/missions/active").json()
        assert len(active) == 2
        assert active[0]["id"] == "mission-early"
        assert active[1]["id"] == "mission-late"


# ---------------------------------------------------------------------------
# (c) After resolve, mission is gone from /api/missions/active
# ---------------------------------------------------------------------------


class TestMissionsActiveAfterResolve:
    def test_resolved_mission_not_in_active(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """After resolving a mission, it must no longer appear in /active."""
        tc, _ = client_with_store
        # Spawn a mission at tick=1.
        tick_resp = tc.post(
            "/api/world/tick",
            json={"tick": 1, "seed": 42, "context_prose": _CONTEXT_HALFGATE},
        )
        mission = tick_resp.json()["mission"]
        assert mission is not None

        # Resolve it.
        resolve_payload = {
            "mission_id": mission["id"],
            "mission_type": mission["type"],
            "region": mission["region"],
            "outcome": "success",
            "tick": 5,
        }
        resolve_resp = tc.post("/api/world/mission/resolve", json=resolve_payload)
        assert resolve_resp.status_code == 200

        # It must be gone from /active.
        active = tc.get("/api/missions/active").json()
        mission_ids = {m["id"] for m in active}
        assert mission["id"] not in mission_ids

    def test_concluded_mission_not_in_active(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """After concluding a mission, it must no longer appear in /active."""
        from wayfinders.api.world_tick_models import EmergentMission

        tc, store = client_with_store

        # Inject a mission directly (conclude endpoint has stricter requirements).
        m = EmergentMission(
            id="conclude-test",
            type="scout_route",
            narrative_hook="A route scouting mission.",
            eligible_personas=(),
            difficulty="mid",
            region="halfgate",
            target_poi="e2.halfgate.gateway",
            deadline_ticks=None,
            outcome=None,
            seed=55,
        )
        store.add(m, spawn_tick=1)
        assert len(tc.get("/api/missions/active").json()) == 1

        # Conclude it (provide a persona snapshot for conclude endpoint).
        # CharacterStateWireDto requires: persona_id, dex_bucket, wis_bucket.
        conclude_payload = {
            "mission_id": "conclude-test",
            "mission_type": "scout_route",
            "region": "halfgate",
            "difficulty": "mid",
            "seed": 55,
            "tick_due": 10,
            "assigned_personas": [
                {
                    "persona_id": "kira",
                    "dex_bucket": "high",
                    "wis_bucket": "mid",
                }
            ],
        }
        conclude_resp = tc.post("/api/world/mission/conclude", json=conclude_payload)
        assert conclude_resp.status_code == 200

        # Must be gone from /active.
        active = tc.get("/api/missions/active").json()
        mission_ids = {m["id"] for m in active}
        assert "conclude-test" not in mission_ids

    def test_resolve_removes_only_target_mission(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Resolving one mission must not affect others in /active."""
        tc, store = client_with_store
        from wayfinders.api.world_tick_models import EmergentMission

        m1 = EmergentMission(
            id="keep-me",
            type="parley_local",
            narrative_hook="Keep this.",
            eligible_personas=(),
            difficulty="mid",
            region="halfgate",
            target_poi="e2.halfgate.intramuros",
            deadline_ticks=None,
            outcome=None,
            seed=1,
        )
        m2 = EmergentMission(
            id="remove-me",
            type="scout_route",
            narrative_hook="Remove this.",
            eligible_personas=(),
            difficulty="mid",
            region="halfgate",
            target_poi="e2.halfgate.gateway",
            deadline_ticks=None,
            outcome=None,
            seed=2,
        )
        store.add(m1, spawn_tick=1)
        store.add(m2, spawn_tick=2)

        resolve_payload = {
            "mission_id": "remove-me",
            "mission_type": "scout_route",
            "region": "halfgate",
            "outcome": "success",
            "tick": 5,
        }
        tc.post("/api/world/mission/resolve", json=resolve_payload)

        active = tc.get("/api/missions/active").json()
        assert len(active) == 1
        assert active[0]["id"] == "keep-me"
