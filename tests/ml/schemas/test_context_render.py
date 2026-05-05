"""Tests for the context renderer."""

from __future__ import annotations

from tests.ml.schemas._fixtures import (
    ridge_campaign,
    ridge_party,
    ridge_scene,
    ridge_world,
)
from wayfinders.ml.schemas.context import (
    PartyState,
    render_context,
)


class TestDeterminism:
    def test_same_state_same_string(self) -> None:
        scene, party, world, campaign = (
            ridge_scene(),
            ridge_party(),
            ridge_world(),
            ridge_campaign(),
        )
        first = render_context(scene, party, world, campaign)
        for _ in range(50):
            assert render_context(scene, party, world, campaign) == first


class TestStructure:
    def test_eleven_sentences(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert len(out.split("\n")) == 11

    def test_mission_identity(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert out.startswith(
            "The mission is a ridge reconnaissance for the Ridgewatch company."
        )

    def test_mission_objective(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The mission objective is to map the enemy camp beyond the ridge before dawn."
        ) in out

    def test_stakes(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The stakes are high: failure means the company moves blind "
            "into the valley at first light."
        ) in out

    def test_party_roster_initiative_order(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The party is three: Kira the scout, Halden the quartermaster, "
            "and Mira the cleric."
        ) in out

    def test_party_condition(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The party is lightly wounded, low on rations, and short on sleep."
        ) in out

    def test_region_season_sky(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The region is the Ridgewatch frontier, in late autumn, "
            "under a thin crescent moon."
        ) in out

    def test_weather_with_wind(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The weather is cold and dry, with a steady wind from the north."
        ) in out

    def test_terrain_with_modifier(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The local terrain is broken stone and scree, with sparse pine cover."
        ) in out

    def test_factions_capped_at_two(self) -> None:
        """Lock Varn sec. 7 option (a) -- cap at 2 most-relevant other factions."""
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        # Sorted alphabetically by partner-faction name: Ashen Hand, river clans
        assert (
            "The Ridgewatch company is at war with the Ashen Hand, "
            "and at uneasy peace with the river clans."
        ) in out

    def test_campaign_turn(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The wider campaign is in its third season, "
            "with the company holding two ridges and having lost one."
        ) in out

    def test_time_with_countdown(self) -> None:
        out = render_context(
            ridge_scene(), ridge_party(), ridge_world(), ridge_campaign()
        )
        assert (
            "The hour is the third watch of the night, with five hours until dawn."
        ) in out


class TestEdgeCases:
    def test_no_party_drops_roster_and_condition(self) -> None:
        empty_party = PartyState(members=())
        out = render_context(ridge_scene(), empty_party, ridge_world(), ridge_campaign())
        assert "The party is" not in out
        # Solo mission still produces a valid prose doc
        assert "The mission is a ridge reconnaissance" in out.split("\n")[0]

    def test_no_factions_drops_relations_sentence(self) -> None:
        peaceful_world = ridge_world().model_copy(update={"faction_stances": ()})
        out = render_context(ridge_scene(), ridge_party(), peaceful_world, ridge_campaign())
        assert "Ridgewatch company is at war" not in out
        assert "Ridgewatch company is at uneasy peace" not in out

    def test_no_countdown_drops_deadline_clause(self) -> None:
        scene_no_countdown = ridge_scene().model_copy(
            update={"countdown_hours": None, "deadline_label": None}
        )
        out = render_context(
            scene_no_countdown, ridge_party(), ridge_world(), ridge_campaign()
        )
        assert "until" not in out
        assert "The hour is the third watch of the night." in out

    def test_no_wind_omits_direction(self) -> None:
        windless = ridge_world().model_copy(
            update={"wind": "no wind", "wind_direction": None}
        )
        out = render_context(ridge_scene(), ridge_party(), windless, ridge_campaign())
        assert "The weather is cold and dry, with no wind." in out
