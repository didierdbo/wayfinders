"""Tests for mission position helpers (wayfinders.world.mission_helpers).

Covers:
  - derive_mission_position: e2 district POI, e1 city POI, unknown POI
  - validate_mission_position_invariant: pass, fail, malformed poi
  - fill_missing_position: None → derives, non-None → keeps
"""

from __future__ import annotations

import pytest

from wayfinders.world.mission_helpers import (
    MissionPositionInvariantError,
    derive_mission_position,
    fill_missing_position,
    validate_mission_position_invariant,
)
from wayfinders.world.models import City, District, Position, World, WorldReferential

# ---------------------------------------------------------------------------
# Shared minimal referential
# ---------------------------------------------------------------------------


def _make_ref() -> WorldReferential:
    world = World(max_x=500_000, max_y=250_000)

    lq_footprint = (
        Position(91900, 37700),
        Position(92200, 37700),
        Position(92200, 38000),
        Position(91900, 38000),
    )
    hw_footprint = (
        Position(92100, 38100),
        Position(92300, 38100),
        Position(92300, 38300),
        Position(92100, 38300),
    )

    lower_quays = District(
        id="lower_quays",
        name="Lower Quays",
        parent_city_id="halfgate",
        anchor=Position(91900, 37700),
        footprint=lq_footprint,
    )
    high_wall = District(
        id="high_wall",
        name="High Wall",
        parent_city_id="halfgate",
        anchor=Position(92100, 38100),
        footprint=hw_footprint,
    )
    halfgate = City(
        id="halfgate",
        name="Halfgate",
        position=Position(92000, 38000),
        district_ids=("lower_quays", "high_wall"),
    )

    return WorldReferential(
        world=world,
        cities={"halfgate": halfgate},
        districts={"lower_quays": lower_quays, "high_wall": high_wall},
    )


# ---------------------------------------------------------------------------
# derive_mission_position
# ---------------------------------------------------------------------------


class TestDeriveMissionPosition:
    def test_e2_district_poi_returns_district_centroid(self) -> None:
        ref = _make_ref()
        pos = derive_mission_position("e2.halfgate.lower_quays", ref)
        assert pos is not None
        # Lower Quays centroid = (92050, 37850)
        assert pos == Position(92050, 37850)

    def test_e2_high_wall_returns_centroid(self) -> None:
        ref = _make_ref()
        pos = derive_mission_position("e2.halfgate.high_wall", ref)
        assert pos is not None
        assert pos == Position(92200, 38200)

    def test_e1_city_poi_returns_city_anchor(self) -> None:
        ref = _make_ref()
        pos = derive_mission_position("e1.halfgate", ref)
        assert pos is not None
        assert pos == Position(92000, 38000)

    def test_unknown_city_returns_none(self) -> None:
        ref = _make_ref()
        pos = derive_mission_position("e1.unknowncity", ref)
        assert pos is None

    def test_unknown_district_falls_back_to_city(self) -> None:
        ref = _make_ref()
        pos = derive_mission_position("e2.halfgate.nonexistent", ref)
        # Falls back to city anchor
        assert pos == Position(92000, 38000)

    def test_malformed_poi_returns_none(self) -> None:
        ref = _make_ref()
        pos = derive_mission_position("malformed", ref)
        assert pos is None

    def test_empty_string_returns_none(self) -> None:
        ref = _make_ref()
        pos = derive_mission_position("", ref)
        assert pos is None


# ---------------------------------------------------------------------------
# validate_mission_position_invariant
# ---------------------------------------------------------------------------


class TestValidateMissionPositionInvariant:
    def test_valid_invariant_does_not_raise(self) -> None:
        ref = _make_ref()
        # Position inside lower_quays → city = halfgate; target_poi city = halfgate
        pos = Position(92050, 37850)
        validate_mission_position_invariant(pos, "e2.halfgate.lower_quays", ref)

    def test_city_poi_valid_invariant(self) -> None:
        ref = _make_ref()
        # City anchor — outside all districts but nearest_city = halfgate
        pos = Position(92000, 38000)
        validate_mission_position_invariant(pos, "e1.halfgate", ref)

    def test_position_in_wrong_city_raises(self) -> None:
        ref = _make_ref()
        # Add a second city far away
        far_city = City(
            id="brescaille",
            name="Brescaille",
            position=Position(200_000, 100_000),
            district_ids=(),
        )
        ref.cities["brescaille"] = far_city
        # Position near Brescaille but target_poi claims halfgate — should fail
        pos = Position(200_000, 100_000)  # nearest city is brescaille
        with pytest.raises(MissionPositionInvariantError, match="invariant violated"):
            validate_mission_position_invariant(pos, "e1.halfgate", ref)

    def test_malformed_poi_raises(self) -> None:
        ref = _make_ref()
        pos = Position(92050, 37850)
        with pytest.raises(MissionPositionInvariantError, match="malformed"):
            validate_mission_position_invariant(pos, "malformed", ref)


# ---------------------------------------------------------------------------
# fill_missing_position
# ---------------------------------------------------------------------------


class TestFillMissingPosition:
    def test_none_fills_from_target_poi(self) -> None:
        ref = _make_ref()
        filled = fill_missing_position(None, "e2.halfgate.lower_quays", ref)
        assert filled == Position(92050, 37850)

    def test_existing_position_not_overwritten(self) -> None:
        ref = _make_ref()
        existing = Position(92200, 38200)
        result = fill_missing_position(existing, "e2.halfgate.lower_quays", ref)
        assert result == existing

    def test_none_unknown_poi_returns_none(self) -> None:
        ref = _make_ref()
        result = fill_missing_position(None, "e1.nonexistent", ref)
        assert result is None


# ---------------------------------------------------------------------------
# EmergentMission.position field presence
# ---------------------------------------------------------------------------


class TestEmergentMissionPositionField:
    def test_position_field_defaults_to_none(self) -> None:
        """EmergentMission accepts position=None (the M1 default)."""
        from wayfinders.api.world_tick_models import EmergentMission

        mission = EmergentMission(
            id="test-id",
            type="recruit",
            narrative_hook="Test hook.",
            region="halfgate",
            target_poi="e1.halfgate",
            seed=42,
            position=None,
        )
        assert mission.position is None

    def test_position_field_accepts_position(self) -> None:
        """EmergentMission accepts a Position value."""
        from wayfinders.api.world_tick_models import EmergentMission
        from wayfinders.world.models import Position

        pos = Position(92050, 37850)
        mission = EmergentMission(
            id="test-id",
            type="recruit",
            narrative_hook="Test hook.",
            region="halfgate",
            target_poi="e1.halfgate",
            seed=42,
            position=pos,
        )
        assert mission.position == pos
