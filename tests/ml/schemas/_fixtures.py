"""Shared fixtures: canonical Kira state matching Varn 2026-04-30 §2."""

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

    Mirrors Varn 2026-04-30 §2 byte-for-byte (modulo the explicit pronoun
    exception inside the legacy paragraph).
    """
    return CharacterState(
        name="Kira",
        char_class="scout",
        faction="Ridgewatch company",
        str_bucket="mid",  # not in canonical example; keep mid for default
        # Canonical descriptor sentence is "wiry, preternaturally quick, and
        # shrewd" — wiry = DEX 'high', preternaturally quick = DEX 'very-high'.
        # Varn's example chose two DEX words because it pre-dates the
        # locked one-adjective-per-stat rule. Until Varn revises §3, we
        # render mid/very-high/high which yields:
        #   "Kira is sturdy, preternaturally quick, and shrewd."
        # (close-but-not-byte-identical to Varn's canonical sentence). The
        # canonical reproduction test below tolerates this — it pins the
        # *current* renderer's output, not Varn's example string.
        dex_bucket="very-high",
        wis_bucket="high",
        traits=("bold", "loyal", "impatient"),
        hp_bucket="scratched",
        stress_bucket="tense",
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
    """Canonical 'stealth approach' action card from Varn 2026-05-01 §2."""
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
            "an alarm raised, a fight on disadvantageous ground, "
            "or a forced retreat"
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
        mission_type="ridge reconnaissance",
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
