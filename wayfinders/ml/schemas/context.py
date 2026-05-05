"""Context prose renderer -- Varn 2026-05-01 sec. 3.

Schema (11 sentences):
    1. Mission identity       ("The mission is a ridge reconnaissance...")
    2. Mission objective      ("The mission objective is to map...")
    3. Stakes line            ("The stakes are high: failure means...")
    4. Party roster           ("The party is three: Kira the scout, ...")
    5. Party condition        ("The party is lightly wounded, low on rations...")
    6. Region + season + sky  ("The region is the Ridgewatch frontier...")
    7. Weather                ("The weather is cold and dry, with a steady wind...")
    8. Local terrain          ("The local terrain is broken stone and scree...")
    9. Faction relations      ("The Ridgewatch company is at war with the Ashen Hand...")
   10. Campaign turn          ("The wider campaign is in its third season...")
   11. Time-of-mission        ("The hour is the third watch of the night...")

Empty optional sentences (no party, no factions, no deadline) drop silently.

Cadence is owned by the inference layer (stable per scene + named triggers,
Varn sec. 4) -- render_context() itself is pure and stateless. The cache key is
hash(rendered_prose).

Pure function, EN-pinned, no RNG.
"""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from wayfinders.ml.schemas.numerals import int_to_words, ordinal
from wayfinders.ml.schemas.vocabularies import (
    FactionRelation,
    FatigueBucket,
    MissionType,
    Precipitation,
    StakesTier,
    SupplyBucket,
    Temperature,
    TerrainModifier,
    TerrainPrimary,
    Watch,
    Wind,
    WindDirection,
    WoundBucket,
)

# ---------------------------------------------------------------------------
# Sub-models
# ---------------------------------------------------------------------------


class PartyMember(BaseModel):
    model_config = ConfigDict(frozen=True)
    name: str
    char_class: str  # 'scout', 'cleric', 'quartermaster', ...
    initiative_index: int  # used for stable ordering


class FactionStance(BaseModel):
    model_config = ConfigDict(frozen=True)
    other_faction: str
    relation: FactionRelation


class SceneState(BaseModel):
    """Mission-level facts (Varn sec. 3 sentences 1-3, 11)."""

    model_config = ConfigDict(frozen=True)

    mission_type: MissionType
    sponsor_faction: str  # e.g. "Ridgewatch company"
    objective_phrase: str  # e.g. "map the enemy camp beyond the ridge before dawn"
    stakes_tier: StakesTier
    failure_phrase: str  # e.g. "the company moves blind into the valley at first light"

    # Sentence 11 -- time-of-mission.
    watch: Watch
    countdown_hours: int | None = None  # None = no countdown clause
    deadline_label: str | None = None  # e.g. "dawn"


class PartyState(BaseModel):
    """Party roster + aggregated condition (Varn sec. 3 sentences 4-5)."""

    model_config = ConfigDict(frozen=True)

    members: tuple[PartyMember, ...] = Field(default_factory=tuple)
    wound_bucket: WoundBucket = "unhurt"
    supply_bucket: SupplyBucket = "well-supplied"
    fatigue_bucket: FatigueBucket = "rested"


class WorldState(BaseModel):
    """Region + weather + terrain + factions (Varn sec. 3 sentences 6-9)."""

    model_config = ConfigDict(frozen=True)

    region: str  # e.g. "Ridgewatch frontier"
    season: Literal[
        "early spring",
        "late spring",
        "early summer",
        "late summer",
        "early autumn",
        "late autumn",
        "early winter",
        "deep winter",
    ]
    sky_phrase: str  # e.g. "a thin crescent moon", "a full harvest moon"

    temperature: Temperature
    precipitation: Precipitation
    wind: Wind
    wind_direction: WindDirection | None = None  # may be None if wind == "no wind"

    terrain_primary: TerrainPrimary
    terrain_modifier: TerrainModifier | None = None

    faction_stances: tuple[FactionStance, ...] = Field(default_factory=tuple)
    # cap-at-2 lock per Varn sec. 7 option (a)


class CampaignState(BaseModel):
    """Wider campaign turn (Varn sec. 3 sentence 10)."""

    model_config = ConfigDict(frozen=True)

    season_number: int = Field(..., ge=1, le=12)  # 1..12 ; ordinal() supports up to 12
    holdings_phrase: str  # e.g. "holding two ridges and having lost one"


# ---------------------------------------------------------------------------
# Renderer
# ---------------------------------------------------------------------------


def _render_mission_identity(scene: SceneState) -> str:
    return f"The mission is a {scene.mission_type} for the {scene.sponsor_faction}."


def _render_mission_objective(scene: SceneState) -> str:
    return f"The mission objective is to {scene.objective_phrase}."


def _render_stakes(scene: SceneState) -> str:
    return f"The stakes are {scene.stakes_tier}: failure means {scene.failure_phrase}."


def _render_party_roster(party: PartyState) -> str | None:
    if not party.members:
        return None
    sorted_members = sorted(party.members, key=lambda m: m.initiative_index)
    member_clauses = [f"{m.name} the {m.char_class}" for m in sorted_members]
    if len(member_clauses) == 1:
        body = member_clauses[0]
    elif len(member_clauses) == 2:
        body = f"{member_clauses[0]} and {member_clauses[1]}"
    else:
        body = ", ".join(member_clauses[:-1]) + f", and {member_clauses[-1]}"
    return f"The party is {int_to_words(len(sorted_members))}: {body}."


def _render_party_condition(party: PartyState) -> str | None:
    if not party.members:
        return None
    return f"The party is {party.wound_bucket}, {party.supply_bucket}, and {party.fatigue_bucket}."


def _render_region(world: WorldState) -> str:
    return f"The region is the {world.region}, in {world.season}, under {world.sky_phrase}."


def _render_weather(world: WorldState) -> str:
    if world.wind == "no wind":
        return f"The weather is {world.temperature} and {world.precipitation}, with no wind."
    direction = world.wind_direction or "north"
    return (
        f"The weather is {world.temperature} and {world.precipitation}, "
        f"with {world.wind} from the {direction}."
    )


def _render_terrain(world: WorldState) -> str:
    if world.terrain_modifier is None:
        return f"The local terrain is {world.terrain_primary}."
    return f"The local terrain is {world.terrain_primary}, with {world.terrain_modifier}."


def _render_factions(scene: SceneState, world: WorldState) -> str | None:
    """Cap at 2 most-relevant per Varn sec. 7 option (a) lock.

    Sorted alphabetically by `other_faction` within sign group ; cap-at-2 is
    applied by the engine before populating `faction_stances` (the renderer
    trusts the input has already been pruned).
    """
    if not world.faction_stances:
        return None
    stances = sorted(world.faction_stances, key=lambda s: s.other_faction)[:2]
    if len(stances) == 1:
        s = stances[0]
        return f"The {scene.sponsor_faction} is {s.relation} with the {s.other_faction}."
    s1, s2 = stances[0], stances[1]
    return (
        f"The {scene.sponsor_faction} is {s1.relation} with the {s1.other_faction}, "
        f"and {s2.relation} with the {s2.other_faction}."
    )


def _render_campaign(campaign: CampaignState) -> str:
    return (
        f"The wider campaign is in its {ordinal(campaign.season_number)} season, "
        f"with the company {campaign.holdings_phrase}."
    )


def _render_time(scene: SceneState) -> str:
    if scene.countdown_hours is None or scene.deadline_label is None:
        return f"The hour is the {scene.watch}."
    return (
        f"The hour is the {scene.watch}, "
        f"with {int_to_words(scene.countdown_hours)} hours "
        f"until {scene.deadline_label}."
    )


def render_context(
    scene: SceneState,
    party: PartyState,
    world: WorldState,
    campaign: CampaignState,
) -> str:
    """Render scene context to encoder-ready English prose.

    Pure function. Cadence (when to call this vs reuse the cache) is the
    caller's responsibility -- see Varn sec. 4 for stable-per-scene + triggers.
    """
    sentences: list[str] = [
        _render_mission_identity(scene),
        _render_mission_objective(scene),
        _render_stakes(scene),
    ]
    if (roster := _render_party_roster(party)) is not None:
        sentences.append(roster)
    if (cond := _render_party_condition(party)) is not None:
        sentences.append(cond)
    sentences.append(_render_region(world))
    sentences.append(_render_weather(world))
    sentences.append(_render_terrain(world))
    if (factions := _render_factions(scene, world)) is not None:
        sentences.append(factions)
    sentences.append(_render_campaign(campaign))
    sentences.append(_render_time(scene))
    return "\n".join(sentences)
