"""Tests for the PoiId validator and EmergentMission.target_poi field.

Varn-lock 2026-05-15 spec v2 §1.

Test categories:
  1. Valid PoiId values — all three spec examples accepted.
  2. Invalid PoiId values — legacy RegionId strings rejected; other bad formats
     rejected.
  3. EmergentMission.target_poi — field is present on the wire model, accepts
     valid PoiIds, rejects invalid ones.
  4. world_tick integration — /api/world/tick response includes target_poi in
     the mission; value matches e1.{region} pattern for M1 cap.
  5. _region_to_poi_id — all five canonical regions convert to valid e1 PoiIds.
"""

from __future__ import annotations

import re
from collections.abc import Iterator
from unittest.mock import MagicMock

import pytest
from fastapi.testclient import TestClient
from pydantic import TypeAdapter, ValidationError

from wayfinders.api.app import app
from wayfinders.api.world_tick import _in_cadence_window, _region_to_poi_id
from wayfinders.api.world_tick_models import (
    _POI_ID_PATTERN,
    REGION_IDS,
    EmergentMission,
    PoiId,
    RegionId,
)

# ---------------------------------------------------------------------------
# TypeAdapter for direct PoiId validation
# ---------------------------------------------------------------------------

_poi_id_adapter: TypeAdapter[PoiId] = TypeAdapter(PoiId)

# ---------------------------------------------------------------------------
# Constants — from Varn spec v2 §1
# ---------------------------------------------------------------------------

_VALID_POI_IDS = [
    "e1.halfgate",
    "e2.halfgate.docks",
    "e3.halfgate.docks.taverne-du-cormoran",
    # Edge cases still valid
    "e1.brescaille",
    "e1.roches-closes",
    "e2.fendelune.vieille-ville",
    "e3.veillemont.port.entrepot-42",
]

_INVALID_POI_IDS = [
    # Legacy RegionId flat strings (must be rejected)
    "halfgate",
    "brescaille",
    "fendelune",
    "veillemont",
    "roches-closes",
    # Old pre-lock M1 values
    "halfgate-marche",
    "ridgepass",
    "ironshore",
    # Wrong layer prefix
    "e0.halfgate",
    "e4.halfgate",
    "e10.halfgate",
    # Missing slug
    "e1.",
    "e1",
    # Uppercase in slug (must be lowercase per spec)
    "e1.Halfgate",
    "e2.halfgate.Docks",
    # Spaces
    "e1.half gate",
    "e2.half gate.docks",
    # Underscore in slug (spec says kebab/hyphens + alphanumeric only)
    # Actually spec says a-z0-9\- so underscores are invalid
    "e1.half_gate",
    # Too many layers (>3 total segments after e-prefix)
    "e3.halfgate.docks.taverne.back-room",  # 4 slugs = too deep
    # Empty string
    "",
    # No prefix at all
    "halfgate.docks",
    ".halfgate",
]

_CONTEXT_PROSE = (
    "The mission is a reconnaissance near Halfgate.\n"
    "The mission objective is to map the road beyond the ridge before dawn.\n"
    "The stakes are high: failure means the company moves blind into the valley."
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture
def client_ready() -> Iterator[tuple[TestClient, MagicMock]]:
    """TestClient with a mock predictor in ready state."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        mock = MagicMock()
        mock.ready = True
        mock.predict.return_value = 1.5
        app.state.predictor = mock
        yield tc, mock


# ---------------------------------------------------------------------------
# 1. Valid PoiId values
# ---------------------------------------------------------------------------


class TestPoiIdValid:
    @pytest.mark.parametrize("poi_id", _VALID_POI_IDS)
    def test_valid_poi_id_accepted_by_adapter(self, poi_id: str) -> None:
        """TypeAdapter[PoiId] accepts all spec-valid PoiId strings."""
        validated = _poi_id_adapter.validate_python(poi_id)
        assert validated == poi_id

    @pytest.mark.parametrize("poi_id", _VALID_POI_IDS)
    def test_valid_poi_id_matches_regex_directly(self, poi_id: str) -> None:
        """Cross-check: all valid PoiIds match the raw regex pattern."""
        assert re.match(_POI_ID_PATTERN, poi_id), (
            f"PoiId {poi_id!r} should match pattern {_POI_ID_PATTERN!r}"
        )

    def test_spec_example_e1(self) -> None:
        """Spec example 1: e1.halfgate."""
        assert _poi_id_adapter.validate_python("e1.halfgate") == "e1.halfgate"

    def test_spec_example_e2(self) -> None:
        """Spec example 2: e2.halfgate.docks."""
        assert _poi_id_adapter.validate_python("e2.halfgate.docks") == "e2.halfgate.docks"

    def test_spec_example_e3(self) -> None:
        """Spec example 3: e3.halfgate.docks.taverne-du-cormoran."""
        poi = "e3.halfgate.docks.taverne-du-cormoran"
        assert _poi_id_adapter.validate_python(poi) == poi


# ---------------------------------------------------------------------------
# 2. Invalid PoiId values
# ---------------------------------------------------------------------------


class TestPoiIdInvalid:
    @pytest.mark.parametrize("poi_id", _INVALID_POI_IDS)
    def test_invalid_poi_id_rejected_by_adapter(self, poi_id: str) -> None:
        """TypeAdapter[PoiId] rejects all non-conforming strings."""
        with pytest.raises(ValidationError):
            _poi_id_adapter.validate_python(poi_id)

    @pytest.mark.parametrize("poi_id", _INVALID_POI_IDS)
    def test_invalid_poi_id_does_not_match_regex(self, poi_id: str) -> None:
        """Cross-check: all invalid PoiIds do not match the raw regex."""
        assert not re.match(_POI_ID_PATTERN, poi_id), (
            f"PoiId {poi_id!r} should NOT match pattern {_POI_ID_PATTERN!r}"
        )


# ---------------------------------------------------------------------------
# 3. EmergentMission.target_poi field
# ---------------------------------------------------------------------------


class TestEmergentMissionTargetPoi:
    def _make_mission(self, target_poi: str, region: RegionId = "halfgate") -> EmergentMission:
        return EmergentMission(
            id="test-mission",
            type="scout_route",
            narrative_hook="A test hook.",
            eligible_personas=(),
            difficulty="mid",
            region=region,
            target_poi=target_poi,  # type: ignore[arg-type]
            deadline_ticks=None,
            outcome=None,
            seed=0,
        )

    def test_valid_target_poi_accepted(self) -> None:
        """EmergentMission accepts a valid PoiId in target_poi."""
        mission = self._make_mission("e1.halfgate")
        assert mission.target_poi == "e1.halfgate"

    def test_spec_example_e1_accepted(self) -> None:
        mission = self._make_mission("e1.halfgate")
        assert mission.target_poi == "e1.halfgate"

    def test_spec_example_e2_accepted(self) -> None:
        mission = self._make_mission("e2.halfgate.docks")
        assert mission.target_poi == "e2.halfgate.docks"

    def test_spec_example_e3_accepted(self) -> None:
        mission = self._make_mission("e3.halfgate.docks.taverne-du-cormoran")
        assert mission.target_poi == "e3.halfgate.docks.taverne-du-cormoran"

    @pytest.mark.parametrize(
        "bad",
        [
            "halfgate",  # legacy flat RegionId — must be rejected
            "halfgate-marche",  # old pre-lock format
            "ridgepass",
            "",
            "e0.halfgate",
            "Halfgate",
        ],
    )
    def test_invalid_target_poi_rejected(self, bad: str) -> None:
        """EmergentMission raises ValidationError for non-PoiId target_poi."""
        with pytest.raises(ValidationError):
            self._make_mission(bad)

    def test_target_poi_present_in_model_fields(self) -> None:
        """target_poi is part of EmergentMission's field set."""
        assert "target_poi" in EmergentMission.model_fields

    def test_region_still_present_alongside_target_poi(self) -> None:
        """region (RegionId) coexists with target_poi for backward compat."""
        mission = self._make_mission("e1.brescaille", region="brescaille")
        assert mission.region == "brescaille"
        assert mission.target_poi == "e1.brescaille"


# ---------------------------------------------------------------------------
# 4. world_tick integration — target_poi in HTTP response
# ---------------------------------------------------------------------------


class TestWorldTickTargetPoi:
    def test_mission_has_target_poi_field(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """EmergentMission returned by /api/world/tick includes target_poi."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))
        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}

        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        assert "target_poi" in mission

    def test_target_poi_matches_e1_pattern(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """M1 cap: target_poi must always be an e1 PoiId (e1.{region})."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))
        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}

        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        poi = mission["target_poi"]
        assert re.match(r"^e1\.[a-z0-9\-]+$", poi), f"M1 target_poi must be e1 layer, got {poi!r}"

    def test_target_poi_region_consistency(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """target_poi must be e1.{region} — consistent with the region field."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))
        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}

        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        assert mission["target_poi"] == f"e1.{mission['region']}"

    def test_target_poi_valid_poi_id(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """target_poi in response must pass PoiId validation."""
        tc, _ = client_ready
        seed = 42
        in_window_tick = next(t for t in range(200) if _in_cadence_window(t, seed))
        payload = {"tick": in_window_tick, "seed": seed, "context_prose": _CONTEXT_PROSE}

        response = tc.post("/api/world/tick", json=payload)

        mission = response.json()["mission"]
        assert mission is not None
        # Must not raise.
        _poi_id_adapter.validate_python(mission["target_poi"])


# ---------------------------------------------------------------------------
# 5. _region_to_poi_id — all five canonical regions
# ---------------------------------------------------------------------------


class TestRegionToPoiId:
    @pytest.mark.parametrize("region", REGION_IDS)
    def test_all_regions_produce_valid_poi_id(self, region: RegionId) -> None:
        """_region_to_poi_id produces a valid PoiId for every canonical RegionId."""
        poi = _region_to_poi_id(region)
        # Must not raise.
        _poi_id_adapter.validate_python(poi)

    @pytest.mark.parametrize("region", REGION_IDS)
    def test_all_regions_produce_e1_poi_id(self, region: RegionId) -> None:
        """M1 cap: all conversions must yield an e1 layer PoiId."""
        poi = _region_to_poi_id(region)
        assert poi.startswith("e1."), f"Expected e1 layer for {region!r}, got {poi!r}"

    @pytest.mark.parametrize("region", REGION_IDS)
    def test_region_embedded_in_poi_id(self, region: RegionId) -> None:
        """The region slug must appear as the e1 segment of the PoiId."""
        poi = _region_to_poi_id(region)
        assert poi == f"e1.{region}"

    def test_roches_closes_hyphen_preserved(self) -> None:
        """roches-closes hyphen must survive the conversion."""
        poi = _region_to_poi_id("roches-closes")
        assert poi == "e1.roches-closes"
        _poi_id_adapter.validate_python(poi)
