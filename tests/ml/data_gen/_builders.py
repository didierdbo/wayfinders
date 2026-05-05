"""Reusable builders for designer-modifier tests.

We can't use ``tests.ml.schemas._fixtures.kira_canonical_state`` directly for
table verification because that fixture pre-dates Varn's v0.5 table and pins
``dex_bucket='very-high'`` (which the table maps to +3) where Varn's worked
Example 1 places Kira at +2 ('wiry/agile' lane). This module ships fresh
states tuned to the table's worked examples.
"""

from __future__ import annotations

from wayfinders.ml.schemas import (
    ActionCard,
    CampaignState,
    CharacterState,
    EpisodicEvent,
    OppositionState,
    PartyState,
    SceneState,
    WorldState,
)
from wayfinders.ml.schemas.context import FactionStance, PartyMember


def kira_for_table_v05() -> CharacterState:
    """Kira tuned to Varn worked Example 1: descriptors +2, traits -1, legacy +4.

    DEX 'high' (Varn's 'wiry/agile' = +2 in the 5-bucket compression),
    WIS 'high' (+1), traits ('bold','loyal','impatient') = 0+0-1, equipment
    leather + soft-soled = 0+1, legacy stealth 8/3 + scouting 80h + best-of-all
    location familiarity, no episodic stealth match.
    """
    return CharacterState(
        name="Kira",
        char_class="scout",
        faction="Ridgewatch company",
        str_bucket="mid",
        dex_bucket="high",
        wis_bucket="high",
        traits=("bold", "loyal", "impatient"),
        hp_bucket="scratched",
        stress_bucket="tense",
        conditions=(),
        equipment=("a worn leather jerkin", "soft-soled boots", "a short bow she made herself"),
        action_counters={"stealth approaches": (8, 3)},
        profession_hours={"scouting": 80, "at the forge": 1240},
        location_familiarity={"broken stone": "well"},  # +1 terrain familiarity
        # No episodic events that match the tightened keyword set.
        episodic_memory=(
            EpisodicEvent(
                event_id=1,
                salience=10.0,
                description="her first kill, a goblin scout in the Hollowwood",
            ),
        ),
        bonds=(),
    )


def stealth_card_starlight_scree() -> ActionCard:
    """Varn Example 1 action card: stealth_approach, scree, starlight, near silence."""
    return ActionCard(
        action_phrase="stealth approach",
        verb_progressive="sneaking past",
        target_phrase="two sentries posted at the watchtower",
        time_phrase="under starlight",
        footing_phrase="loose scree",
        noise_phrase="near silence",
        intent_phrase="reach the ridge unseen and unheard",
        tag_set=("stealth", "movement", "scouting"),
        opposition_family="humanoids",
        stat_tested="dexterity",
        opposed_stat="perception",
        failure_template="an alarm raised, a fight on disadvantageous ground, or a forced retreat",
    )


def stealth_card_at_noon() -> ActionCard:
    """Varn Example 2: same as Example 1 but at noon -> -3 lighting."""
    base = stealth_card_starlight_scree()
    return base.model_copy(update={"time_phrase": "at noon"})


def two_ordinary_human_sentries() -> OppositionState:
    return OppositionState(
        count=2,
        family="human sentries",
        alertness="alert and rested",
        skill_tier="ordinary",
    )


def ridge_scene_high_stakes() -> SceneState:
    return SceneState(
        mission_type="ridge reconnaissance",
        sponsor_faction="Ridgewatch company",
        objective_phrase="map the enemy camp beyond the ridge before dawn",
        stakes_tier="high",
        failure_phrase="the company moves blind into the valley at first light",
        watch="third watch of the night",
        countdown_hours=5,
        deadline_label="dawn",
    )


def ridge_party_lightly_wounded() -> PartyState:
    return PartyState(
        members=(
            PartyMember(name="Kira", char_class="scout", initiative_index=0),
            PartyMember(name="Halden", char_class="quartermaster", initiative_index=1),
            PartyMember(name="Mira", char_class="cleric", initiative_index=2),
        ),
        wound_bucket="lightly wounded",
        supply_bucket="low on rations",
        fatigue_bucket="short on sleep",
    )


def ridge_world_at_war() -> WorldState:
    return WorldState(
        region="Ridgewatch frontier",
        season="late autumn",
        sky_phrase="a thin crescent moon",
        temperature="cold",
        precipitation="dry",
        wind="a steady wind",
        wind_direction="north",
        terrain_primary="broken stone and scree",
        terrain_modifier="sparse pine cover",
        faction_stances=(FactionStance(other_faction="Ashen Hand", relation="at war"),),
    )


def ridge_campaign_even() -> CampaignState:
    return CampaignState(
        season_number=3,
        # 'lost' substring matches; but 'losing' is the keyword. 'holding' is
        # not in the table -- we want delta_campaign = 0 for the canonical case.
        # Use a phrase that does NOT contain any tracked keyword.
        holdings_phrase="standing firm in the eastern marches",
    )
