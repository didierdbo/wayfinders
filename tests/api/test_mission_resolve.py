"""Tests for ``POST /api/world/mission/resolve``.

Test categories:
  1. MissionResolveRequest validation — field types, constraints.
  2. resolve_mission() logic — determinism, idempotence, tag fields.
  3. Edge cases — empty assigned_personas, all three outcomes.
  4. HTTP layer — endpoint returns 200 with expected JSON shape.
  5. PersonaLegacyTagWire.from_tag() conversion.
"""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.mission_resolve import (
    MissionResolveRequest,
    PersonaLegacyTagWire,
    resolve_mission,
)
from wayfinders.api.world_tick_models import PersonaLegacyTag

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _make_request(
    *,
    mission_id: str = "test-mission-001",
    mission_type: str = "scout_route",
    region: str = "fendelune",
    actor_target: str | None = None,
    outcome: str = "success",
    tick: int = 5,
    assigned_personas: tuple[str, ...] = ("kira",),
) -> MissionResolveRequest:
    """Build a minimal MissionResolveRequest for testing."""
    return MissionResolveRequest(
        mission_id=mission_id,
        mission_type=mission_type,  # type: ignore[arg-type]
        region=region,
        actor_target=actor_target,
        outcome=outcome,  # type: ignore[arg-type]
        tick=tick,
        assigned_personas=assigned_personas,
    )


# ---------------------------------------------------------------------------
# 1. MissionResolveRequest validation
# ---------------------------------------------------------------------------


class TestMissionResolveRequestValidation:
    def test_valid_scout_route(self) -> None:
        req = _make_request(mission_type="scout_route", outcome="success")
        assert req.mission_type == "scout_route"
        assert req.outcome == "success"

    def test_valid_parley_local(self) -> None:
        req = _make_request(mission_type="parley_local", outcome="partial")
        assert req.mission_type == "parley_local"
        assert req.outcome == "partial"

    def test_valid_failure_outcome(self) -> None:
        req = _make_request(outcome="failure")
        assert req.outcome == "failure"

    def test_actor_target_defaults_to_none(self) -> None:
        req = _make_request()
        assert req.actor_target is None

    def test_actor_target_can_be_set(self) -> None:
        req = _make_request(actor_target="bourgmestre")
        assert req.actor_target == "bourgmestre"

    def test_assigned_personas_defaults_empty(self) -> None:
        req = _make_request(assigned_personas=())
        assert req.assigned_personas == ()

    def test_tick_must_be_non_negative(self) -> None:
        from pydantic import ValidationError

        with pytest.raises(ValidationError):
            MissionResolveRequest(
                mission_id="m1",
                mission_type="scout_route",  # type: ignore[arg-type]
                region="fendelune",
                outcome="success",  # type: ignore[arg-type]
                tick=-1,
            )

    def test_mission_id_must_not_be_empty(self) -> None:
        from pydantic import ValidationError

        with pytest.raises(ValidationError):
            MissionResolveRequest(
                mission_id="",
                mission_type="scout_route",  # type: ignore[arg-type]
                region="fendelune",
                outcome="success",  # type: ignore[arg-type]
                tick=0,
            )

    def test_region_must_not_be_empty(self) -> None:
        from pydantic import ValidationError

        with pytest.raises(ValidationError):
            MissionResolveRequest(
                mission_id="m1",
                mission_type="scout_route",  # type: ignore[arg-type]
                region="",
                outcome="success",  # type: ignore[arg-type]
                tick=0,
            )

    def test_bad_mission_type_rejected(self) -> None:
        from pydantic import ValidationError

        with pytest.raises(ValidationError):
            MissionResolveRequest(
                mission_id="m1",
                mission_type="ambush",  # type: ignore[arg-type]  # not a valid EmergenceMissionType
                region="fendelune",
                outcome="success",  # type: ignore[arg-type]
                tick=0,
            )

    def test_bad_outcome_rejected(self) -> None:
        from pydantic import ValidationError

        with pytest.raises(ValidationError):
            MissionResolveRequest(
                mission_id="m1",
                mission_type="scout_route",  # type: ignore[arg-type]
                region="fendelune",
                outcome="critical",  # type: ignore[arg-type]  # not a valid ResolutionOutcomeType
                tick=0,
            )

    def test_extra_fields_rejected(self) -> None:
        from pydantic import ValidationError

        with pytest.raises(ValidationError):
            MissionResolveRequest.model_validate(
                {
                    "mission_id": "m1",
                    "mission_type": "scout_route",
                    "region": "fendelune",
                    "outcome": "success",
                    "tick": 0,
                    "unexpected_field": "oops",
                }
            )

    def test_frozen(self) -> None:
        from pydantic import ValidationError

        req = _make_request()
        # Pydantic v2 frozen models raise ValidationError on attempted mutation.
        with pytest.raises((ValidationError, AttributeError)):
            req.tick = 99  # type: ignore[misc]


# ---------------------------------------------------------------------------
# 2. resolve_mission() logic — determinism and idempotence
# ---------------------------------------------------------------------------


class TestResolveMissionLogic:
    def test_creates_tag_for_single_persona(self) -> None:
        """Single persona → exactly one tag in the response."""
        req = _make_request(assigned_personas=("kira",))
        resp = resolve_mission(req)
        assert len(resp.tags_created) == 1
        assert resp.tags_created[0].persona_id == "kira"

    def test_creates_tags_for_multiple_personas(self) -> None:
        """Multiple personas → one tag per persona, in order."""
        req = _make_request(assigned_personas=("kira", "hodge"))
        resp = resolve_mission(req)
        assert len(resp.tags_created) == 2
        persona_ids = [t.persona_id for t in resp.tags_created]
        assert "kira" in persona_ids
        assert "hodge" in persona_ids

    def test_tag_fields_match_request(self) -> None:
        """Tag fields exactly mirror the request inputs."""
        req = _make_request(
            mission_id="m-det-001",
            mission_type="parley_local",
            region="brescaille",
            actor_target="bourgmestre",
            outcome="partial",
            tick=7,
            assigned_personas=("hodge",),
        )
        resp = resolve_mission(req)
        assert len(resp.tags_created) == 1
        tag = resp.tags_created[0]
        assert tag.persona_id == "hodge"
        assert tag.mission_id == "m-det-001"
        assert tag.mission_type == "parley_local"
        assert tag.region == "brescaille"
        assert tag.actor_target is None  # M1: _extract_actor_target always returns None
        assert tag.outcome == "partial"
        assert tag.earned_at_tick == 7

    def test_determinism_same_input_same_output(self) -> None:
        """Same request → identical response, twice in a row."""
        req = _make_request(
            mission_id="m-det-002",
            outcome="success",
            tick=10,
            assigned_personas=("kira",),
        )
        resp1 = resolve_mission(req)
        resp2 = resolve_mission(req)
        assert resp1.tags_created == resp2.tags_created

    def test_idempotence_same_call_same_tags(self) -> None:
        """Calling resolve_mission twice produces the same tags (not doubled).

        Each call gets a fresh transient GameState, so the idempotence guard
        inside on_mission_resolved cannot fire across calls.  Across calls,
        the invariant is that the same inputs produce the same outputs
        (determinism), not that a second call is a no-op at the store level.
        That deduplication is the client's responsibility using
        (persona_id, mission_id) as natural key.
        """
        req = _make_request(assigned_personas=("kira",))
        resp1 = resolve_mission(req)
        resp2 = resolve_mission(req)
        # Each call produces exactly one tag — not zero, not two.
        assert len(resp1.tags_created) == 1
        assert len(resp2.tags_created) == 1
        # Tags are equal.
        assert resp1.tags_created[0] == resp2.tags_created[0]

    def test_different_mission_ids_produce_different_tags(self) -> None:
        req1 = _make_request(mission_id="m-001", assigned_personas=("kira",))
        req2 = _make_request(mission_id="m-002", assigned_personas=("kira",))
        resp1 = resolve_mission(req1)
        resp2 = resolve_mission(req2)
        assert resp1.tags_created[0].mission_id == "m-001"
        assert resp2.tags_created[0].mission_id == "m-002"

    def test_different_outcomes_produce_different_tags(self) -> None:
        req_success = _make_request(outcome="success", assigned_personas=("kira",))
        req_failure = _make_request(outcome="failure", assigned_personas=("kira",))
        resp_success = resolve_mission(req_success)
        resp_failure = resolve_mission(req_failure)
        assert resp_success.tags_created[0].outcome == "success"
        assert resp_failure.tags_created[0].outcome == "failure"
        assert resp_success.tags_created[0] != resp_failure.tags_created[0]


# ---------------------------------------------------------------------------
# 3. Edge cases
# ---------------------------------------------------------------------------


class TestResolveMissionEdgeCases:
    def test_empty_assigned_personas_declined_mission(self) -> None:
        """Declined mission (empty assigned_personas) → empty tags_created."""
        req = _make_request(assigned_personas=())
        resp = resolve_mission(req)
        assert resp.tags_created == ()

    def test_empty_assigned_personas_returns_200_not_error(self, client: TestClient) -> None:
        """HTTP endpoint returns 200 even when assigned_personas is empty."""
        payload = {
            "mission_id": "m-declined",
            "mission_type": "scout_route",
            "region": "fendelune",
            "outcome": "failure",
            "tick": 3,
            "assigned_personas": [],
        }
        resp = client.post("/api/world/mission/resolve", json=payload)
        assert resp.status_code == 200
        data = resp.json()
        assert data["tags_created"] == []

    def test_all_three_outcomes(self) -> None:
        """All three outcomes (success, partial, failure) produce a tag."""
        for outcome in ("success", "partial", "failure"):
            req = _make_request(
                outcome=outcome,  # type: ignore[arg-type]
                assigned_personas=("kira",),
            )
            resp = resolve_mission(req)
            assert len(resp.tags_created) == 1
            assert resp.tags_created[0].outcome == outcome

    def test_both_mission_types(self) -> None:
        """Both mission types (scout_route, parley_local) produce a tag."""
        for mtype in ("scout_route", "parley_local"):
            req = _make_request(
                mission_type=mtype,  # type: ignore[arg-type]
                assigned_personas=("kira",),
            )
            resp = resolve_mission(req)
            assert len(resp.tags_created) == 1
            assert resp.tags_created[0].mission_type == mtype

    def test_tick_zero_is_valid(self) -> None:
        """Tick 0 (game start) is a valid resolution tick."""
        req = _make_request(tick=0, assigned_personas=("kira",))
        resp = resolve_mission(req)
        assert resp.tags_created[0].earned_at_tick == 0

    def test_large_persona_count(self) -> None:
        """Many personas assigned — one tag per persona."""
        personas = tuple(f"persona_{i}" for i in range(10))
        req = _make_request(assigned_personas=personas)
        resp = resolve_mission(req)
        assert len(resp.tags_created) == 10
        produced_ids = {t.persona_id for t in resp.tags_created}
        assert produced_ids == set(personas)


# ---------------------------------------------------------------------------
# 4. HTTP layer tests
# ---------------------------------------------------------------------------


class TestMissionResolveHTTP:
    def test_post_returns_200(self, client: TestClient) -> None:
        payload = {
            "mission_id": "http-test-001",
            "mission_type": "scout_route",
            "region": "fendelune",
            "outcome": "success",
            "tick": 5,
            "assigned_personas": ["kira"],
        }
        resp = client.post("/api/world/mission/resolve", json=payload)
        assert resp.status_code == 200

    def test_response_has_tags_created_field(self, client: TestClient) -> None:
        payload = {
            "mission_id": "http-test-002",
            "mission_type": "parley_local",
            "region": "brescaille",
            "outcome": "partial",
            "tick": 7,
            "assigned_personas": ["hodge"],
        }
        resp = client.post("/api/world/mission/resolve", json=payload)
        data = resp.json()
        assert "tags_created" in data
        assert isinstance(data["tags_created"], list)

    def test_response_tag_fields_correct(self, client: TestClient) -> None:
        payload = {
            "mission_id": "http-test-003",
            "mission_type": "scout_route",
            "region": "veillemont",
            "actor_target": None,
            "outcome": "failure",
            "tick": 12,
            "assigned_personas": ["kira"],
        }
        resp = client.post("/api/world/mission/resolve", json=payload)
        data = resp.json()
        assert len(data["tags_created"]) == 1
        tag = data["tags_created"][0]
        assert tag["persona_id"] == "kira"
        assert tag["mission_id"] == "http-test-003"
        assert tag["mission_type"] == "scout_route"
        assert tag["region"] == "veillemont"
        assert tag["outcome"] == "failure"
        assert tag["earned_at_tick"] == 12

    def test_bad_mission_type_returns_422(self, client: TestClient) -> None:
        payload = {
            "mission_id": "http-test-004",
            "mission_type": "ambush",  # not a valid EmergenceMissionType
            "region": "fendelune",
            "outcome": "success",
            "tick": 1,
            "assigned_personas": ["kira"],
        }
        resp = client.post("/api/world/mission/resolve", json=payload)
        assert resp.status_code == 422

    def test_bad_outcome_returns_422(self, client: TestClient) -> None:
        payload = {
            "mission_id": "http-test-005",
            "mission_type": "scout_route",
            "region": "fendelune",
            "outcome": "critical",  # not valid
            "tick": 1,
            "assigned_personas": ["kira"],
        }
        resp = client.post("/api/world/mission/resolve", json=payload)
        assert resp.status_code == 422

    def test_negative_tick_returns_422(self, client: TestClient) -> None:
        payload = {
            "mission_id": "http-test-006",
            "mission_type": "scout_route",
            "region": "fendelune",
            "outcome": "success",
            "tick": -1,
            "assigned_personas": ["kira"],
        }
        resp = client.post("/api/world/mission/resolve", json=payload)
        assert resp.status_code == 422

    def test_determinism_across_http_calls(self, client: TestClient) -> None:
        """Same payload twice → identical tags_created both times."""
        payload = {
            "mission_id": "http-det-001",
            "mission_type": "scout_route",
            "region": "roches-closes",
            "outcome": "success",
            "tick": 15,
            "assigned_personas": ["kira", "hodge"],
        }
        resp1 = client.post("/api/world/mission/resolve", json=payload)
        resp2 = client.post("/api/world/mission/resolve", json=payload)
        assert resp1.json()["tags_created"] == resp2.json()["tags_created"]

    def test_multiple_personas_http(self, client: TestClient) -> None:
        payload = {
            "mission_id": "http-multi-001",
            "mission_type": "parley_local",
            "region": "halfgate",
            "outcome": "success",
            "tick": 20,
            "assigned_personas": ["kira", "hodge", "peleth"],
        }
        resp = client.post("/api/world/mission/resolve", json=payload)
        data = resp.json()
        assert len(data["tags_created"]) == 3
        ids = {t["persona_id"] for t in data["tags_created"]}
        assert ids == {"kira", "hodge", "peleth"}


# ---------------------------------------------------------------------------
# 5. PersonaLegacyTagWire.from_tag() conversion
# ---------------------------------------------------------------------------


class TestPersonaLegacyTagWireConversion:
    def test_from_tag_all_fields(self) -> None:
        """from_tag() copies all fields from the dataclass to the Pydantic model."""
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m-conv-001",
            mission_type="scout_route",
            region="fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        wire = PersonaLegacyTagWire.from_tag(tag)
        assert wire.persona_id == tag.persona_id
        assert wire.mission_id == tag.mission_id
        assert wire.mission_type == tag.mission_type
        assert wire.region == tag.region
        assert wire.actor_target == tag.actor_target
        assert wire.outcome == tag.outcome
        assert wire.earned_at_tick == tag.earned_at_tick

    def test_from_tag_with_actor_target(self) -> None:
        tag = PersonaLegacyTag(
            persona_id="hodge",
            mission_id="m-conv-002",
            mission_type="parley_local",
            region="brescaille",
            actor_target="bourgmestre",
            outcome="partial",
            earned_at_tick=10,
        )
        wire = PersonaLegacyTagWire.from_tag(tag)
        assert wire.actor_target == "bourgmestre"

    def test_wire_is_json_serialisable(self) -> None:
        """PersonaLegacyTagWire can be serialised to a dict (JSON-ready)."""
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m-conv-003",
            mission_type="scout_route",
            region="veillemont",
            actor_target=None,
            outcome="failure",
            earned_at_tick=3,
        )
        wire = PersonaLegacyTagWire.from_tag(tag)
        d = wire.model_dump()
        assert isinstance(d, dict)
        assert d["persona_id"] == "kira"
        assert d["actor_target"] is None

    def test_two_wires_from_same_tag_are_equal(self) -> None:
        """Two wire models created from the same dataclass tag must be equal."""
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m-conv-004",
            mission_type="scout_route",
            region="fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=7,
        )
        w1 = PersonaLegacyTagWire.from_tag(tag)
        w2 = PersonaLegacyTagWire.from_tag(tag)
        assert w1 == w2
