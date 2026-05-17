"""Tests for ``POST /api/world/tick``.

Four test categories:

1. **Schema validation** — request and response shapes, field types,
   closed-lookup enforcement.
2. **Determinism** — same ``(tick, seed, context_prose)`` always produces
   the same mission.
3. **Cadence** — over 100 consecutive ticks, missions appear in the
   expected 5-10 tick window rhythm (ratio check).
4. **Type closed lookup** — missions only have ``scout_route`` or
   ``parley_local`` as type; no invented values.

All tests run without real model weights.  The predictor is replaced with
a mock after lifespan fires.  Two mock modes:
  - ``predictor_ready``: returns a fixed delta (used for determinism /
    closed-lookup / schema tests).
  - ``predictor_not_ready``: simulates degraded mode (uniform logits).

Tests for cadence run the engine directly (not via HTTP) to exercise 100
ticks efficiently without HTTP overhead.
"""

from __future__ import annotations

from collections.abc import Iterator
from unittest.mock import MagicMock

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app
from wayfinders.api.predictor import PredictorNotReadyError
from wayfinders.api.world_tick import (
    _CADENCE_MAX,
    _CADENCE_MIN,
    _MISSION_TYPES,
    EmergenceEngine,
    _in_cadence_window,
    _stable_mission_id,
)
from wayfinders.api.world_tick_models import WorldTickRequest

# ---------------------------------------------------------------------------
# Sample prose (structurally valid EN, same style as UC1 predict tests)
# ---------------------------------------------------------------------------

_CONTEXT_PROSE = (
    "The mission is a reconnaissance near Halfgate.\n"
    "The mission objective is to map the road beyond the ridge before dawn.\n"
    "The stakes are high: failure means the company moves blind into the valley."
)

_VALID_REQUEST = {
    "tick": 5,
    "seed": 42,
    "context_prose": _CONTEXT_PROSE,
}


# ---------------------------------------------------------------------------
# Mock helpers
# ---------------------------------------------------------------------------


def _make_predictor(ready: bool, delta: float = 1.5) -> MagicMock:
    """Return a mock Predictor."""
    mock = MagicMock()
    mock.ready = ready
    if ready:
        mock.predict.return_value = delta
    else:
        mock.predict.side_effect = PredictorNotReadyError("head not loaded")
    return mock


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture
def client_ready() -> Iterator[tuple[TestClient, MagicMock]]:
    """TestClient with a mock predictor in ready state (delta=1.5)."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        mock = _make_predictor(ready=True, delta=1.5)
        app.state.predictor = mock
        yield tc, mock


@pytest.fixture
def client_not_ready() -> Iterator[TestClient]:
    """TestClient with predictor not ready — endpoint degrades to uniform sampling."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        app.state.predictor = _make_predictor(ready=False)
        yield tc


# ---------------------------------------------------------------------------
# 1. Schema validation — request
# ---------------------------------------------------------------------------


class TestWorldTickRequestSchema:
    def test_missing_tick_returns_422(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        payload = {"seed": 42, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        assert response.status_code == 422

    def test_missing_seed_returns_422(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        payload = {"tick": 5, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        assert response.status_code == 422

    def test_missing_context_prose_returns_422(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        tc, _ = client_ready
        payload = {"tick": 5, "seed": 42}
        response = tc.post("/api/world/tick", json=payload)

        assert response.status_code == 422

    def test_empty_context_prose_returns_422(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        tc, _ = client_ready
        payload = {"tick": 5, "seed": 42, "context_prose": ""}
        response = tc.post("/api/world/tick", json=payload)

        assert response.status_code == 422

    def test_negative_tick_returns_422(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        payload = {"tick": -1, "seed": 42, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        assert response.status_code == 422

    def test_unknown_field_returns_422(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """extra='forbid' must reject spurious fields."""
        tc, _ = client_ready
        payload = dict(_VALID_REQUEST)
        payload["surprise"] = "unexpected"
        response = tc.post("/api/world/tick", json=payload)

        assert response.status_code == 422


# ---------------------------------------------------------------------------
# 2. Schema validation — response
# ---------------------------------------------------------------------------


class TestWorldTickResponseSchema:
    def test_returns_200(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        # tick=5 with seed=42: check whether it's in a cadence window.
        # The test just verifies HTTP 200 regardless of window state.
        response = tc.post("/api/world/tick", json=_VALID_REQUEST)

        assert response.status_code == 200

    def test_response_is_json(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        response = tc.post("/api/world/tick", json=_VALID_REQUEST)

        assert response.headers["content-type"].startswith("application/json")

    def test_response_schema_fields(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """Wire schema must have exactly {mission, tick}."""
        tc, _ = client_ready
        response = tc.post("/api/world/tick", json=_VALID_REQUEST)

        body = response.json()
        assert set(body.keys()) == {"mission", "tick"}

    def test_tick_echoed_in_response(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        response = tc.post("/api/world/tick", json=_VALID_REQUEST)

        assert response.json()["tick"] == _VALID_REQUEST["tick"]

    def test_mission_none_outside_window(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """Ticks outside the cadence window must return mission=None."""
        tc, _ = client_ready
        # Find a tick that is definitely NOT in a window for seed=42.
        seed = 42
        outside_tick = None
        for t in range(1, 200):
            if not _in_cadence_window(t, seed):
                outside_tick = t
                break
        assert outside_tick is not None, "expected to find a non-window tick in [1, 200]"

        payload = {"tick": outside_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        assert response.status_code == 200
        assert response.json()["mission"] is None

    def test_mission_not_none_in_window(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """Ticks inside the cadence window must return a mission (not None)."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = None
        for t in range(0, 200):
            if _in_cadence_window(t, seed):
                in_window_tick = t
                break
        assert in_window_tick is not None

        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        assert response.status_code == 200
        assert response.json()["mission"] is not None

    def test_mission_fields_when_present(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """EmergentMission must have all contracted fields (§A.8.D3: includes recruit_target_npc_id)."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))

        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        expected_keys = {
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
        }
        assert set(mission.keys()) == expected_keys

    def test_mission_difficulty_forced_mid(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """M1 spec: difficulty is always 'mid'."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))

        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        assert mission["difficulty"] == "mid"

    def test_mission_deadline_none(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """M1 spec: deadline_ticks is always None."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))

        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        assert mission["deadline_ticks"] is None

    def test_mission_outcome_none(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """M1 spec: outcome is None at emergence."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))

        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        assert mission["outcome"] is None

    def test_mission_eligible_personas_empty_when_no_company(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """When no company_personas are sent, eligible_personas is empty (option c)."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))

        # No company_personas field → defaults to empty tuple.
        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        assert mission["eligible_personas"] == []

    def test_mission_id_is_uuid_shaped(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """Mission id must be a 36-char UUID-format string."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))

        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        mission_id = mission["id"]
        import uuid

        # Must parse as a valid UUID.
        parsed = uuid.UUID(mission_id)
        assert str(parsed) == mission_id

    def test_narrative_hook_non_empty(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """Narrative hook must be a non-empty string."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))

        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        assert isinstance(mission["narrative_hook"], str)
        assert len(mission["narrative_hook"]) > 0

    def test_mission_seed_echoes_request_seed(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """Mission.seed must equal the request seed (audit trail)."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))

        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}
        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        assert mission["seed"] == seed


# ---------------------------------------------------------------------------
# 3. Determinism — same (tick, seed, context_prose) → same mission
# ---------------------------------------------------------------------------


class TestWorldTickDeterminism:
    def test_same_inputs_same_mission_id(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """Determinism test: reset store between calls so active-NPC state is identical."""
        tc, _ = client_ready
        seed = 99
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))
        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}

        # Reset store before each call to ensure identical active-NPC state.
        from wayfinders.api.app import app
        from wayfinders.api.mission_store import MissionStore

        app.state.mission_store = MissionStore()
        r1 = tc.post("/api/world/tick", json=payload)
        app.state.mission_store = MissionStore()
        r2 = tc.post("/api/world/tick", json=payload)

        assert r1.json()["mission"]["id"] == r2.json()["mission"]["id"]

    def test_same_inputs_same_mission_type(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """Determinism test: reset store between calls so active-NPC state is identical."""
        tc, _ = client_ready
        seed = 99
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))
        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}

        from wayfinders.api.app import app
        from wayfinders.api.mission_store import MissionStore

        app.state.mission_store = MissionStore()
        r1 = tc.post("/api/world/tick", json=payload)
        app.state.mission_store = MissionStore()
        r2 = tc.post("/api/world/tick", json=payload)

        assert r1.json()["mission"]["type"] == r2.json()["mission"]["type"]

    def test_same_inputs_same_narrative_hook(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """Determinism test: reset store between calls so active-NPC state is identical."""
        tc, _ = client_ready
        seed = 99
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))
        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}

        from wayfinders.api.app import app
        from wayfinders.api.mission_store import MissionStore

        app.state.mission_store = MissionStore()
        r1 = tc.post("/api/world/tick", json=payload)
        app.state.mission_store = MissionStore()
        r2 = tc.post("/api/world/tick", json=payload)

        assert r1.json()["mission"]["narrative_hook"] == r2.json()["mission"]["narrative_hook"]

    def test_different_seeds_can_differ(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """Different seeds should be able to produce different outcomes (not locked).

        We pick two seeds where BOTH are in a window at tick 0 (trivially true:
        tick 0 is always in window regardless of seed).  Then we check that at
        least one of the following differs: mission id or mission type.
        This is not guaranteed to differ (both could sample the same type with
        the same delta from the mock), but is a sanity check that seed feeds
        through to the RNG.
        """
        tc, mock = client_ready
        mock.predict.return_value = 1.5

        payload_a = {"tick": 0, "seed": 1, "context_prose": _CONTEXT_PROSE}
        payload_b = {"tick": 0, "seed": 2, "context_prose": _CONTEXT_PROSE}

        r_a = tc.post("/api/world/tick", json=payload_a)
        r_b = tc.post("/api/world/tick", json=payload_b)

        # Both must return a mission (tick 0 is always in window).
        assert r_a.json()["mission"] is not None
        assert r_b.json()["mission"] is not None

        # IDs must differ (different seeds → different stable_mission_id).
        assert r_a.json()["mission"]["id"] != r_b.json()["mission"]["id"]

    def test_stable_mission_id_deterministic(self) -> None:
        """_stable_mission_id must return same UUID for same (tick, seed)."""
        id1 = _stable_mission_id(5, 42)
        id2 = _stable_mission_id(5, 42)

        assert id1 == id2

    def test_stable_mission_id_differs_by_tick(self) -> None:
        """Different ticks → different mission ids."""
        id1 = _stable_mission_id(5, 42)
        id2 = _stable_mission_id(6, 42)

        assert id1 != id2

    def test_stable_mission_id_differs_by_seed(self) -> None:
        """Different seeds → different mission ids."""
        id1 = _stable_mission_id(5, 42)
        id2 = _stable_mission_id(5, 43)

        assert id1 != id2

    def test_engine_determinism_direct(self) -> None:
        """EmergenceEngine.process_tick() is deterministic without HTTP layer.

        Calls the engine directly with a fixed mock predictor.  This tests
        the core logic without HTTP serialization.
        """
        mock = _make_predictor(ready=True, delta=2.0)
        engine = EmergenceEngine(predictor=mock)

        seed = 77
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))
        req = WorldTickRequest(tick=in_window_tick, seed=seed, context_prose=_CONTEXT_PROSE)

        result1 = engine.process_tick(req)
        result2 = engine.process_tick(req)

        assert result1.mission is not None
        assert result2.mission is not None
        assert result1.mission.id == result2.mission.id
        assert result1.mission.type == result2.mission.type


# ---------------------------------------------------------------------------
# 4. Cadence — ratio over 100 ticks
# ---------------------------------------------------------------------------


class TestWorldTickCadence:
    """Test that cadence produces missions within the 5-10 tick window spec.

    These tests run the engine directly to avoid HTTP overhead on 100 ticks.
    """

    def _run_100_ticks(self, seed_base: int, delta: float = 1.5) -> list[bool]:
        """Run 100 ticks and return a bool list (True = mission emerged)."""
        mock = _make_predictor(ready=True, delta=delta)
        engine = EmergenceEngine(predictor=mock)
        results = []
        for tick in range(100):
            # Derive per-tick seed from seed_base and tick (simulates game logic).
            tick_seed = seed_base ^ (tick * 0x1337)
            req = WorldTickRequest(tick=tick, seed=tick_seed, context_prose=_CONTEXT_PROSE)
            resp = engine.process_tick(req)
            results.append(resp.mission is not None)
        return results

    def test_missions_appear_in_cadence_window(self) -> None:
        """Over 100 ticks, missions appear with a frequency consistent with 5-10 ticks.

        Expected: between 100/_CADENCE_MAX and 100/_CADENCE_MIN missions
        (i.e. between 10 and 20 for a [5,10] window).

        Note: since the cadence interval is sampled per tick from seed,
        the actual count depends on the seed.  We allow a wider band
        (factor 2x) to avoid flakiness while still catching broken cadence.
        """
        emerged = self._run_100_ticks(seed_base=12345)
        mission_count = sum(emerged)

        # Lower bound: at most every _CADENCE_MAX ticks → at least 100//_CADENCE_MAX
        # Upper bound: at most every _CADENCE_MIN ticks → at most 100//_CADENCE_MIN
        # Add generous margin (2x) because per-tick seed variation shifts the modulo.
        min_expected = max(1, 100 // _CADENCE_MAX // 2)
        max_expected = min(100, (100 // _CADENCE_MIN) * 2)
        assert min_expected <= mission_count <= max_expected, (
            f"mission_count={mission_count} outside [{min_expected}, {max_expected}] "
            f"for cadence window [{_CADENCE_MIN}, {_CADENCE_MAX}]"
        )

    def test_no_mission_outside_window(self) -> None:
        """Ticks outside the cadence window must never produce a mission.

        We pick a single seed, identify all non-window ticks, and assert
        that none of them produced a mission.
        """
        seed = 42
        mock = _make_predictor(ready=True, delta=1.5)
        engine = EmergenceEngine(predictor=mock)

        for tick in range(100):
            tick_seed = seed ^ (tick * 0x1337)
            in_window = _in_cadence_window(tick, tick_seed)
            if not in_window:
                req = WorldTickRequest(tick=tick, seed=tick_seed, context_prose=_CONTEXT_PROSE)
                resp = engine.process_tick(req)
                assert resp.mission is None, (
                    f"tick={tick} seed={tick_seed}: expected no mission outside window, "
                    f"got mission.type={resp.mission.type if resp.mission else None}"
                )

    def test_mission_always_emerges_in_window(self) -> None:
        """Every tick inside the cadence window must produce a mission.

        In M1 the no_emergence fallback ensures this invariant.
        """
        seed = 42
        mock = _make_predictor(ready=True, delta=0.0)  # delta=0 maximises no_emergence probs
        engine = EmergenceEngine(predictor=mock)

        for tick in range(100):
            tick_seed = seed ^ (tick * 0x1337)
            in_window = _in_cadence_window(tick, tick_seed)
            if in_window:
                req = WorldTickRequest(tick=tick, seed=tick_seed, context_prose=_CONTEXT_PROSE)
                resp = engine.process_tick(req)
                assert resp.mission is not None, (
                    f"tick={tick} seed={tick_seed}: expected mission in window, got None"
                )

    def test_cadence_window_is_deterministic(self) -> None:
        """_in_cadence_window must return the same result for same (tick, seed)."""
        for tick in range(20):
            for seed in [0, 42, 99, 999]:
                r1 = _in_cadence_window(tick, seed)
                r2 = _in_cadence_window(tick, seed)
                assert r1 == r2, f"_in_cadence_window not deterministic at tick={tick} seed={seed}"


# ---------------------------------------------------------------------------
# 5. Closed lookup — mission type always in {scout_route, parley_local}
# ---------------------------------------------------------------------------


class TestWorldTickClosedLookup:
    def test_mission_type_in_closed_set(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """M1 strict mode (§A.8.D3): all emitted missions must be type 'recruit'.

        The store is reset before each tick call to ensure the cap does not
        block emission (3-NPC cap; cap-saturation tested separately).
        """
        from wayfinders.api.app import app
        from wayfinders.api.mission_store import MissionStore

        tc, _ = client_ready
        seed = 7

        for tick in range(0, 20):
            tick_seed = seed ^ (tick * 0xBEEF)
            if not _in_cadence_window(tick, tick_seed):
                continue
            # Reset store so the cap never blocks the first-NPC pick.
            app.state.mission_store = MissionStore()
            payload = {"tick": tick, "seed": tick_seed, "context_prose": _CONTEXT_PROSE}
            response = tc.post("/api/world/tick", json=payload)
            mission = response.json()["mission"]
            # In the window with an empty store, a mission must always emerge.
            assert mission is not None
            assert mission["type"] in _MISSION_TYPES, (
                f"tick={tick}: unexpected mission type {mission['type']!r}; "
                f"must be in {_MISSION_TYPES}"
            )
            assert mission["type"] == "recruit", (
                f"tick={tick}: M1 strict mode — only 'recruit' missions emitted, "
                f"got {mission['type']!r}"
            )

    @pytest.mark.m2_only
    def test_both_m2_mission_types_reachable(self) -> None:
        """M2+ only: scout_route and parley_local must both be reachable.

        This test is skipped in M1 (recruit-only mode per §A.8.D3).
        Restore in M2 when the picker is widened to scout_route + parley_local.
        """
        pytest.skip("M2 only — scout_route / parley_local retired from M1 picker (§A.8.D3)")

    def test_no_emergence_type_never_in_response(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """'no_emergence' must never appear as a mission type in the response.

        In M1 the type is always 'recruit' inside cadence windows (§A.8.D3).
        The old no_emergence fallback is not used; this invariant still holds.
        """
        from wayfinders.api.app import app
        from wayfinders.api.mission_store import MissionStore

        tc, mock = client_ready
        mock.predict.return_value = 0.0

        seed = 42
        for tick in range(20):
            tick_seed = seed ^ (tick * 0xDEAD)
            if not _in_cadence_window(tick, tick_seed):
                continue
            app.state.mission_store = MissionStore()
            payload = {"tick": tick, "seed": tick_seed, "context_prose": _CONTEXT_PROSE}
            response = tc.post("/api/world/tick", json=payload)
            mission = response.json()["mission"]
            assert mission is not None
            assert mission["type"] != "no_emergence", (
                f"tick={tick}: 'no_emergence' must not appear as mission.type"
            )


# ---------------------------------------------------------------------------
# 6. Degraded mode (predictor not ready)
# ---------------------------------------------------------------------------


class TestWorldTickDegradedMode:
    def test_returns_200_when_not_ready(self, client_not_ready: TestClient) -> None:
        """Endpoint must return 200 even when predictor is not ready."""
        response = client_not_ready.post("/api/world/tick", json=_VALID_REQUEST)

        assert response.status_code == 200

    def test_still_produces_valid_response_when_not_ready(
        self, client_not_ready: TestClient
    ) -> None:
        """Degraded mode falls back to uniform sampling — response schema must still be valid."""
        # tick=0 is always in window; check we get a mission.
        payload = {"tick": 0, "seed": 42, "context_prose": _CONTEXT_PROSE}
        response = client_not_ready.post("/api/world/tick", json=payload)

        body = response.json()
        assert set(body.keys()) == {"mission", "tick"}
        # tick 0 is always a cadence window → must have a mission even in degraded mode.
        assert body["mission"] is not None
        assert body["mission"]["type"] in _MISSION_TYPES
