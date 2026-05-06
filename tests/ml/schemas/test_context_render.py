"""Tests for the context renderer (M1 substep 4).

Two canonical snapshots:
- Ridge (Varn Context 2026-05-01 sec. 2) -- mission_type updated to
  "reconnaissance" per Varn lock 2026-05-06 Delta 6.
- Festival/Fenmarch (Varn Context 2026-05-01 sec. 6) -- exercises the
  WindDirection "river" landmark and TerrainModifier without leading "with ".
"""

from __future__ import annotations

from tests.ml.schemas._fixtures import (
    festival_campaign,
    festival_party,
    festival_scene,
    festival_world,
    ridge_campaign,
    ridge_party,
    ridge_scene,
    ridge_world,
)
from wayfinders.ml.schemas.context import (
    PartyState,
    render_context,
)

# ---------------------------------------------------------------------------
# Byte-exact snapshots
# ---------------------------------------------------------------------------

_RIDGE_CANONICAL_DOC = (
    "The mission is a reconnaissance for the Ridgewatch company.\n"
    "The mission objective is to map the enemy camp beyond the ridge before dawn.\n"
    "The stakes are high: failure means the company moves blind into the valley at first light.\n"
    "The party is three: Kira the scout, Halden the quartermaster, and Mira the cleric.\n"
    "The party is lightly wounded, low on rations, and short on sleep.\n"
    "The region is the Ridgewatch frontier, in late autumn, under a thin crescent moon.\n"
    "The weather is cold and dry, with a steady wind from the north.\n"
    "The local terrain is broken stone and scree, with sparse pine cover.\n"
    "The Ridgewatch company is at war with the Ashen Hand, and at uneasy peace with the river clans.\n"
    "The wider campaign is in its third season, with the company holding two ridges and having lost one.\n"
    "The hour is the third watch of the night, with five hours until dawn."
)

# Varn Context sec. 6 cross-scene canonical (festival / Fenmarch).
# Key phrases to verify: "with a light breeze from the river" (Delta 5 fix)
# and "festival lanterns strung overhead" (Delta 4 fix, no double-with).
_FESTIVAL_CANONICAL_DOC = (
    "The mission is an infiltration for the river clans.\n"
    "The mission objective is to recover the stolen seal before the festival ends at dusk.\n"
    "The stakes are serious: failure means the seal passes out of the city by morning.\n"
    "The party is two: Kira the scout and Mira the cleric.\n"
    "The party is unhurt, well-supplied, and rested.\n"
    "The region is the city of Fenmarch, in late summer, under a full harvest moon.\n"
    "The weather is warm and dry, with a light breeze from the river.\n"
    "The local terrain is paved streets and crowded squares, with festival lanterns strung overhead.\n"
    # Factions sorted alphabetically by other_faction: Ashen Hand, Ridgewatch company
    "The river clans is at war with the Ashen Hand, and at uneasy peace with the Ridgewatch company.\n"
    "The wider campaign is in its third season, with the company holding the river bend and the markets.\n"
    "The hour is the late afternoon, with three hours until dusk."
)


class TestCanonicalSnapshot:
    def test_ridge_canonical(self) -> None:
        """Byte-exact reproduction of Varn Context 2026-05-01 sec. 2 (Delta 6 applied)."""
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert out == _RIDGE_CANONICAL_DOC

    def test_festival_canonical(self) -> None:
        """Byte-exact reproduction of Varn Context 2026-05-01 sec. 6 cross-scene test.

        Verifies Delta 4 (no double-with in TerrainModifier) and Delta 5
        (landmark WindDirection "river" renders as "from the river").
        """
        out = render_context(
            festival_scene(), festival_party(), festival_world(), festival_campaign()
        )
        assert out == _FESTIVAL_CANONICAL_DOC


class TestDeterminism:
    def test_same_state_same_string(self) -> None:
        scene, party, world, campaign = (
            ridge_scene(),
            ridge_party(),
            ridge_world(),
            ridge_campaign(),
        )
        first = render_context(scene, party, world, campaign)
        for _ in range(100):
            assert render_context(scene, party, world, campaign) == first

    def test_festival_determinism(self) -> None:
        scene, party, world, campaign = (
            festival_scene(),
            festival_party(),
            festival_world(),
            festival_campaign(),
        )
        first = render_context(scene, party, world, campaign)
        for _ in range(100):
            assert render_context(scene, party, world, campaign) == first


class TestStructure:
    def test_eleven_sentences(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert len(out.split("\n")) == 11

    def test_mission_identity(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert out.startswith("The mission is a reconnaissance for the Ridgewatch company.")

    def test_mission_objective(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert (
            "The mission objective is to map the enemy camp beyond the ridge before dawn."
        ) in out

    def test_stakes(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert (
            "The stakes are high: failure means the company moves blind "
            "into the valley at first light."
        ) in out

    def test_party_roster_initiative_order(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert (
            "The party is three: Kira the scout, Halden the quartermaster, and Mira the cleric."
        ) in out

    def test_party_condition(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert ("The party is lightly wounded, low on rations, and short on sleep.") in out

    def test_region_season_sky(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert (
            "The region is the Ridgewatch frontier, in late autumn, under a thin crescent moon."
        ) in out

    def test_weather_with_wind(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert ("The weather is cold and dry, with a steady wind from the north.") in out

    def test_terrain_with_modifier(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert ("The local terrain is broken stone and scree, with sparse pine cover.") in out

    def test_factions_capped_at_two(self) -> None:
        """Lock Varn sec. 7 option (a) -- cap at 2 most-relevant other factions."""
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        # Sorted alphabetically by partner-faction name: Ashen Hand, river clans
        assert (
            "The Ridgewatch company is at war with the Ashen Hand, "
            "and at uneasy peace with the river clans."
        ) in out

    def test_campaign_turn(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert (
            "The wider campaign is in its third season, "
            "with the company holding two ridges and having lost one."
        ) in out

    def test_time_with_countdown(self) -> None:
        out = render_context(ridge_scene(), ridge_party(), ridge_world(), ridge_campaign())
        assert ("The hour is the third watch of the night, with five hours until dawn.") in out


class TestFestivalScene:
    """Exercises the two canonical fixes: Delta 4 (no double-with) + Delta 5 (landmark wind)."""

    def test_no_double_with_in_terrain(self) -> None:
        out = render_context(
            festival_scene(), festival_party(), festival_world(), festival_campaign()
        )
        terrain_line = next(s for s in out.split("\n") if s.startswith("The local terrain"))
        assert "with with" not in terrain_line
        assert "festival lanterns strung overhead" in terrain_line

    def test_landmark_wind_direction(self) -> None:
        out = render_context(
            festival_scene(), festival_party(), festival_world(), festival_campaign()
        )
        assert "with a light breeze from the river." in out

    def test_festival_eleven_sentences(self) -> None:
        out = render_context(
            festival_scene(), festival_party(), festival_world(), festival_campaign()
        )
        assert len(out.split("\n")) == 11

    def test_an_article_before_vowel_mission_type(self) -> None:
        """mission_type 'infiltration' starts with vowel -> article must be 'an'."""
        out = render_context(
            festival_scene(), festival_party(), festival_world(), festival_campaign()
        )
        assert out.startswith("The mission is an infiltration")


class TestEdgeCases:
    def test_no_party_drops_roster_and_condition(self) -> None:
        empty_party = PartyState(members=())
        out = render_context(ridge_scene(), empty_party, ridge_world(), ridge_campaign())
        assert "The party is" not in out
        # Solo mission still produces a valid prose doc
        assert "The mission is a reconnaissance" in out.split("\n")[0]

    def test_no_factions_drops_relations_sentence(self) -> None:
        peaceful_world = ridge_world().model_copy(update={"faction_stances": ()})
        out = render_context(ridge_scene(), ridge_party(), peaceful_world, ridge_campaign())
        assert "Ridgewatch company is at war" not in out
        assert "Ridgewatch company is at uneasy peace" not in out

    def test_no_countdown_drops_deadline_clause(self) -> None:
        scene_no_countdown = ridge_scene().model_copy(
            update={"countdown_hours": None, "deadline_label": None}
        )
        out = render_context(scene_no_countdown, ridge_party(), ridge_world(), ridge_campaign())
        assert "until" not in out
        assert "The hour is the third watch of the night." in out

    def test_no_wind_omits_direction(self) -> None:
        windless = ridge_world().model_copy(update={"wind": "no wind", "wind_direction": None})
        out = render_context(ridge_scene(), ridge_party(), windless, ridge_campaign())
        assert "The weather is cold and dry, with no wind." in out

    def test_no_terrain_modifier_drops_with_clause(self) -> None:
        bare_world = ridge_world().model_copy(update={"terrain_modifier": None})
        out = render_context(ridge_scene(), ridge_party(), bare_world, ridge_campaign())
        assert "The local terrain is broken stone and scree." in out
        assert "with " not in next(s for s in out.split("\n") if s.startswith("The local terrain"))


class TestSlotLaneIntegrity:
    """Each sentence draws from at most one closed-vocab lane per slot.

    Weather sentence: temperature, precipitation, wind are distinct lane values.
    Terrain sentence: terrain_primary and terrain_modifier are from distinct lanes.
    Party condition: wound_bucket, supply_bucket, fatigue_bucket are distinct lanes.
    """

    def test_weather_sentence_three_distinct_lane_values(self) -> None:
        world = ridge_world()
        out = render_context(ridge_scene(), ridge_party(), world, ridge_campaign())
        weather_line = next(s for s in out.split("\n") if s.startswith("The weather is"))
        assert world.temperature in weather_line
        assert world.precipitation in weather_line
        assert world.wind in weather_line
        # Three distinct lane values (no cross-lane collision)
        assert len({world.temperature, world.precipitation, world.wind}) == 3

    def test_party_condition_three_distinct_buckets(self) -> None:
        party = ridge_party()
        out = render_context(ridge_scene(), party, ridge_world(), ridge_campaign())
        cond_line = next(s for s in out.split("\n") if "lightly wounded" in s)
        assert party.wound_bucket in cond_line
        assert party.supply_bucket in cond_line
        assert party.fatigue_bucket in cond_line
        # Three distinct bucket values
        assert len({party.wound_bucket, party.supply_bucket, party.fatigue_bucket}) == 3

    def test_terrain_sentence_two_distinct_lanes(self) -> None:
        world = ridge_world()
        out = render_context(ridge_scene(), ridge_party(), world, ridge_campaign())
        terrain_line = next(s for s in out.split("\n") if s.startswith("The local terrain"))
        assert world.terrain_primary in terrain_line
        assert world.terrain_modifier is not None
        assert world.terrain_modifier in terrain_line
        # Primary and modifier are distinct values
        assert world.terrain_primary != world.terrain_modifier
