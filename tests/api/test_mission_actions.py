"""Tests for mission action endpoints (§A.8.D5, Varn-lock 2026-05-17).

Endpoints covered:
  POST /api/missions/{mission_id}/accept
  POST /api/missions/{mission_id}/decline
  POST /api/missions/{mission_id}/conclude
  POST /api/missions/{mission_id}/resolve

For each:
  - 200 happy path.
  - 404 when mission_id unknown.
  - 409 for illegal state transitions.
  - Idempotency: accept is a no-op on second call (200).
  - Effect on /api/missions/active.
"""

from __future__ import annotations

from collections.abc import Iterator

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app
from wayfinders.api.mission_store import MissionStore
from wayfinders.api.world_tick_models import EmergentMission

# ---------------------------------------------------------------------------
# Fixture
# ---------------------------------------------------------------------------


@pytest.fixture
def client_with_store() -> Iterator[tuple[TestClient, MissionStore]]:
    """TestClient with a fresh MissionStore for test isolation."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        fresh_store = MissionStore()
        app.state.mission_store = fresh_store
        yield tc, fresh_store


def _make_recruit(
    mission_id: str,
    npc_id: str = "kira",
    district: str = "gateway",
    seed: int = 42,
) -> EmergentMission:
    return EmergentMission(
        id=mission_id,
        type="recruit",
        narrative_hook="A recruit hook.",
        eligible_personas=(),
        difficulty="mid",
        region="halfgate",
        target_poi=f"e2.halfgate.{district}",
        deadline_ticks=None,
        outcome=None,
        seed=seed,
        recruit_target_npc_id=npc_id,
    )


# ---------------------------------------------------------------------------
# POST /api/missions/{mission_id}/accept
# ---------------------------------------------------------------------------


class TestMissionAccept:
    def test_accept_returns_200(self, client_with_store: tuple[TestClient, MissionStore]) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-accept-1"), spawn_tick=1)
        resp = tc.post("/api/missions/m-accept-1/accept", json={})
        assert resp.status_code == 200

    def test_accept_response_body(self, client_with_store: tuple[TestClient, MissionStore]) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-accept-2"), spawn_tick=1)
        resp = tc.post("/api/missions/m-accept-2/accept", json={})
        body = resp.json()
        assert body["mission_id"] == "m-accept-2"
        assert body["status"] == "accepted"

    def test_accept_404_unknown_id(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, _ = client_with_store
        resp = tc.post("/api/missions/no-such-id/accept", json={})
        assert resp.status_code == 404

    def test_accept_idempotent_200_on_second_call(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Second accept on same mission must return 200 (no-op, not 409)."""
        tc, store = client_with_store
        store.add(_make_recruit("m-accept-idem"), spawn_tick=1)
        r1 = tc.post("/api/missions/m-accept-idem/accept", json={})
        r2 = tc.post("/api/missions/m-accept-idem/accept", json={})
        assert r1.status_code == 200
        assert r2.status_code == 200

    def test_accept_mission_remains_in_active(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Accepted mission must still appear in /api/missions/active."""
        tc, store = client_with_store
        store.add(_make_recruit("m-accept-active"), spawn_tick=1)
        tc.post("/api/missions/m-accept-active/accept", json={})
        active = tc.get("/api/missions/active").json()
        ids = {m["id"] for m in active}
        assert "m-accept-active" in ids


# ---------------------------------------------------------------------------
# POST /api/missions/{mission_id}/decline
# ---------------------------------------------------------------------------


class TestMissionDecline:
    def test_decline_returns_200(self, client_with_store: tuple[TestClient, MissionStore]) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-decline-1"), spawn_tick=1)
        resp = tc.post("/api/missions/m-decline-1/decline", json={})
        assert resp.status_code == 200

    def test_decline_response_body(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-decline-2"), spawn_tick=1)
        resp = tc.post("/api/missions/m-decline-2/decline", json={})
        body = resp.json()
        assert body["mission_id"] == "m-decline-2"
        assert body["status"] == "declined"

    def test_decline_404_unknown_id(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, _ = client_with_store
        resp = tc.post("/api/missions/no-such/decline", json={})
        assert resp.status_code == 404

    def test_decline_removes_mission_from_active(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """After decline, mission must no longer appear in /api/missions/active."""
        tc, store = client_with_store
        store.add(_make_recruit("m-decline-gone"), spawn_tick=1)
        tc.post("/api/missions/m-decline-gone/decline", json={})
        active = tc.get("/api/missions/active").json()
        ids = {m["id"] for m in active}
        assert "m-decline-gone" not in ids

    def test_decline_409_after_accept(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Cannot decline a mission that has already been accepted."""
        tc, store = client_with_store
        store.add(_make_recruit("m-decline-accepted"), spawn_tick=1)
        tc.post("/api/missions/m-decline-accepted/accept", json={})
        resp = tc.post("/api/missions/m-decline-accepted/decline", json={})
        assert resp.status_code == 409

    def test_decline_does_not_affect_other_missions(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-keep", "dorn", district="intramuros"), spawn_tick=1)
        store.add(_make_recruit("m-decline-other"), spawn_tick=2)
        tc.post("/api/missions/m-decline-other/decline", json={})
        active = tc.get("/api/missions/active").json()
        assert len(active) == 1
        assert active[0]["id"] == "m-keep"


# ---------------------------------------------------------------------------
# POST /api/missions/{mission_id}/conclude
# ---------------------------------------------------------------------------


class TestMissionConclude:
    def test_conclude_returns_200(self, client_with_store: tuple[TestClient, MissionStore]) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-conclude-1"), spawn_tick=1)
        store.accept("m-conclude-1")
        resp = tc.post(
            "/api/missions/m-conclude-1/conclude",
            json={"outcome": "success"},
        )
        assert resp.status_code == 200

    def test_conclude_response_body(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-conclude-2"), spawn_tick=1)
        store.accept("m-conclude-2")
        resp = tc.post(
            "/api/missions/m-conclude-2/conclude",
            json={"outcome": "failure"},
        )
        body = resp.json()
        assert body["mission_id"] == "m-conclude-2"
        assert body["status"] == "concluded"
        assert body["outcome"] == "failure"

    def test_conclude_404_unknown(self, client_with_store: tuple[TestClient, MissionStore]) -> None:
        tc, _ = client_with_store
        resp = tc.post("/api/missions/no-such/conclude", json={"outcome": "success"})
        assert resp.status_code == 404

    def test_conclude_409_if_not_accepted(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """conclude requires accept first — 409 if still pending."""
        tc, store = client_with_store
        store.add(_make_recruit("m-conclude-not-accepted"), spawn_tick=1)
        resp = tc.post(
            "/api/missions/m-conclude-not-accepted/conclude",
            json={"outcome": "success"},
        )
        assert resp.status_code == 409

    def test_conclude_removes_from_active(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-conclude-gone"), spawn_tick=1)
        store.accept("m-conclude-gone")
        tc.post("/api/missions/m-conclude-gone/conclude", json={"outcome": "success"})
        active = tc.get("/api/missions/active").json()
        ids = {m["id"] for m in active}
        assert "m-conclude-gone" not in ids

    def test_conclude_all_outcomes(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """success / failure / abandoned all return 200."""
        tc, store = client_with_store
        for i, outcome in enumerate(["success", "failure", "abandoned"]):
            mid = f"m-conclude-outcome-{i}"
            store.add(_make_recruit(mid, district="gateway"), spawn_tick=i + 1)
            store.accept(mid)
            resp = tc.post(f"/api/missions/{mid}/conclude", json={"outcome": outcome})
            assert resp.status_code == 200, (
                f"outcome={outcome}: expected 200, got {resp.status_code}"
            )


# ---------------------------------------------------------------------------
# POST /api/missions/{mission_id}/resolve
# ---------------------------------------------------------------------------


class TestMissionResolve:
    def test_resolve_returns_200(self, client_with_store: tuple[TestClient, MissionStore]) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-resolve-1"), spawn_tick=1)
        store.accept("m-resolve-1")
        resp = tc.post(
            "/api/missions/m-resolve-1/resolve",
            json={"resolution_outcome_type": "success"},
        )
        assert resp.status_code == 200

    def test_resolve_response_body(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-resolve-2"), spawn_tick=1)
        store.accept("m-resolve-2")
        resp = tc.post(
            "/api/missions/m-resolve-2/resolve",
            json={"resolution_outcome_type": "failure"},
        )
        body = resp.json()
        assert body["mission_id"] == "m-resolve-2"
        assert body["status"] == "resolved"
        assert body["resolution_outcome_type"] == "failure"

    def test_resolve_404_unknown(self, client_with_store: tuple[TestClient, MissionStore]) -> None:
        tc, _ = client_with_store
        resp = tc.post(
            "/api/missions/no-such/resolve",
            json={"resolution_outcome_type": "success"},
        )
        assert resp.status_code == 404

    def test_resolve_409_if_not_accepted(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """resolve requires accept first — 409 if still pending."""
        tc, store = client_with_store
        store.add(_make_recruit("m-resolve-not-accepted"), spawn_tick=1)
        resp = tc.post(
            "/api/missions/m-resolve-not-accepted/resolve",
            json={"resolution_outcome_type": "success"},
        )
        assert resp.status_code == 409

    def test_resolve_removes_from_active(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-resolve-gone"), spawn_tick=1)
        store.accept("m-resolve-gone")
        tc.post(
            "/api/missions/m-resolve-gone/resolve",
            json={"resolution_outcome_type": "success"},
        )
        active = tc.get("/api/missions/active").json()
        ids = {m["id"] for m in active}
        assert "m-resolve-gone" not in ids

    def test_resolve_outcome_types(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """success / failure / abandoned all return 200."""
        tc, store = client_with_store
        for i, rot in enumerate(["success", "failure", "abandoned"]):
            mid = f"m-resolve-rot-{i}"
            store.add(_make_recruit(mid, district="gateway"), spawn_tick=i + 1)
            store.accept(mid)
            resp = tc.post(
                f"/api/missions/{mid}/resolve",
                json={"resolution_outcome_type": rot},
            )
            assert resp.status_code == 200, f"rot={rot}: expected 200, got {resp.status_code}"


# ---------------------------------------------------------------------------
# State machine: accept → conclude / resolve chain
# ---------------------------------------------------------------------------


class TestMissionStateMachine:
    def test_full_accept_conclude_chain(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Happy path: decline or accept a pending mission, then conclude it."""
        tc, store = client_with_store
        store.add(_make_recruit("m-chain-1"), spawn_tick=1)
        # Accept.
        assert tc.post("/api/missions/m-chain-1/accept", json={}).status_code == 200
        # Conclude.
        assert (
            tc.post("/api/missions/m-chain-1/conclude", json={"outcome": "success"}).status_code
            == 200
        )
        # Gone from active.
        assert "m-chain-1" not in {m["id"] for m in tc.get("/api/missions/active").json()}

    def test_full_accept_resolve_chain(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-chain-2", district="intramuros", npc_id="dorn"), spawn_tick=1)
        assert tc.post("/api/missions/m-chain-2/accept", json={}).status_code == 200
        assert (
            tc.post(
                "/api/missions/m-chain-2/resolve",
                json={"resolution_outcome_type": "success"},
            ).status_code
            == 200
        )
        assert "m-chain-2" not in {m["id"] for m in tc.get("/api/missions/active").json()}

    def test_decline_without_accept(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Decline a pending mission directly (no accept needed)."""
        tc, store = client_with_store
        store.add(_make_recruit("m-chain-decline"), spawn_tick=1)
        assert tc.post("/api/missions/m-chain-decline/decline", json={}).status_code == 200
        assert "m-chain-decline" not in {m["id"] for m in tc.get("/api/missions/active").json()}

    def test_conclude_without_accept_is_409(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        """Conclude skipping accept must be rejected (§A.8.D5)."""
        tc, store = client_with_store
        store.add(_make_recruit("m-bad-conclude"), spawn_tick=1)
        resp = tc.post("/api/missions/m-bad-conclude/conclude", json={"outcome": "success"})
        assert resp.status_code == 409

    def test_resolve_without_accept_is_409(
        self, client_with_store: tuple[TestClient, MissionStore]
    ) -> None:
        tc, store = client_with_store
        store.add(_make_recruit("m-bad-resolve"), spawn_tick=1)
        resp = tc.post(
            "/api/missions/m-bad-resolve/resolve",
            json={"resolution_outcome_type": "success"},
        )
        assert resp.status_code == 409
