"""Shared fixtures: canonical states matching Varn 2026-04-30 and 2026-05-01 sec. 2/6."""

from __future__ import annotations

from wayfinders.ml.schemas import (
    ActionCard,
    Bond,
    CampaignState,
    CharacterState,
    EpisodicEvent,
    OppositionState,
    PartyState,
    SceneState,
    WorldState,
)
from wayfinders.ml.schemas.context import FactionStance, PartyMember


def kira_canonical_state() -> CharacterState:
    """The Kira state that should render to Varn's canonical 8-paragraph doc.

    Mirrors Varn 2026-04-30 sec. 2 byte-for-byte (modulo the explicit pronoun
    exception inside the legacy paragraph).
    """
    return CharacterState(
        name="Kira",
        char_class="scout",
        faction="Ridgewatch company",
        str_bucket="low",
        # Per UC1 Descriptor Lanes Lock 2026-05-06 sec. 4: Kira is STR low /
        # DEX very-high / WIS high. The canonical descriptor sentence is
        # "Kira is slender, preternaturally quick, and shrewd." -- pinned in
        # test_render_kira_canonical and test_descriptor_sentence_present.
        dex_bucket="very-high",
        wis_bucket="high",
        traits=("bold", "loyal", "impatient"),
        hp_bucket="scratched",
        stress_bucket="tense",
        stress_cause="from a short night's sleep",
        conditions=("with a fading bruise on her forearm",),
        equipment=("a short bow she made herself", "a worn leather jerkin"),
        combat_engagements={
            "goblin engagements": 47,
            "beast hunts": 12,
            "skirmishes with undead": 3,
        },
        action_counters={"stealth approaches": (8, 3)},
        profession_hours={"scouting": 80, "at the forge": 1240},
        location_familiarity={
            "forest terrain": "well",
            "dungeon corridors": "moderately",
            "town streets": "best of all",
        },
        episodic_memory=(
            EpisodicEvent(
                event_id=1,
                salience=10.0,
                description="her first kill, a goblin scout in the Hollowwood",
            ),
            EpisodicEvent(
                event_id=2,
                salience=9.0,
                description="the night she lost Brann to ghouls in the Catacombs of Vesh",
            ),
            EpisodicEvent(
                event_id=3,
                salience=8.0,
                description="the day she spared the bandit captain at Fenmarch crossing",
            ),
        ),
        bonds=(
            Bond(partner_name="Brann", bond_value=5, partner_is_dead=True),
            Bond(partner_name="Halden the quartermaster", bond_value=-3),
            Bond(partner_name="Mira the cleric", bond_value=0),
        ),
    )


def stealth_approach_card() -> ActionCard:
    """Canonical 'stealth approach' action card from Varn 2026-05-01 sec. 2."""
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
        failure_template=(
            "an alarm raised, a fight on disadvantageous ground, or a forced retreat"
        ),
    )


def ridge_opposition() -> OppositionState:
    return OppositionState(
        count=2,
        family="human sentries",
        alertness="alert and rested",
        skill_tier="ordinary",
    )


def ridge_scene() -> SceneState:
    return SceneState(
        mission_type="reconnaissance",
        sponsor_faction="Ridgewatch company",
        objective_phrase="map the enemy camp beyond the ridge before dawn",
        stakes_tier="high",
        failure_phrase="the company moves blind into the valley at first light",
        watch="third watch of the night",
        countdown_hours=5,
        deadline_label="dawn",
    )


def ridge_party() -> PartyState:
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


def ridge_world() -> WorldState:
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
        faction_stances=(
            FactionStance(other_faction="Ashen Hand", relation="at war"),
            FactionStance(other_faction="river clans", relation="at uneasy peace"),
        ),
    )


def ridge_campaign() -> CampaignState:
    return CampaignState(
        season_number=3,
        holdings_phrase="holding two ridges and having lost one",
    )


# ---------------------------------------------------------------------------
# Festival / Fenmarch canonical -- Varn Context 2026-05-01 sec. 6
# (cross-scene differentiation test; exercises WindDirection "river" and
#  TerrainModifier "festival lanterns strung overhead")
# ---------------------------------------------------------------------------


def festival_scene() -> SceneState:
    """Varn Context sec. 6 festival/Fenmarch scene (cross-scene canonical)."""
    return SceneState(
        mission_type="infiltration",
        sponsor_faction="river clans",
        objective_phrase="recover the stolen seal before the festival ends at dusk",
        stakes_tier="serious",
        failure_phrase="the seal passes out of the city by morning",
        watch="late afternoon",
        countdown_hours=3,
        deadline_label="dusk",
    )


def festival_party() -> PartyState:
    return PartyState(
        members=(
            PartyMember(name="Kira", char_class="scout", initiative_index=0),
            PartyMember(name="Mira", char_class="cleric", initiative_index=1),
        ),
        wound_bucket="unhurt",
        supply_bucket="well-supplied",
        fatigue_bucket="rested",
    )


def festival_world() -> WorldState:
    return WorldState(
        region="city of Fenmarch",
        season="late summer",
        sky_phrase="a full harvest moon",
        temperature="warm",
        precipitation="dry",
        wind="a light breeze",
        wind_direction="river",
        terrain_primary="paved streets and crowded squares",
        terrain_modifier="festival lanterns strung overhead",
        faction_stances=(
            # Alphabetical by other_faction: Ashen Hand, Ridgewatch company
            FactionStance(other_faction="Ashen Hand", relation="at war"),
            FactionStance(other_faction="Ridgewatch company", relation="at uneasy peace"),
        ),
    )


def festival_campaign() -> CampaignState:
    return CampaignState(
        season_number=3,
        holdings_phrase="holding the river bend and the markets",
    )
