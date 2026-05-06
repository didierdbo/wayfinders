"""Rollout generator -- structured tuples + EN-pinned prose + label_delta.

A ``Rollout`` is one structured tuple ``(character, action, opposition, scene,
party, world, campaign, label_delta)`` plus the three rendered prose strings.

Determinism contract: ``generate_rollouts(n=N, seed=S)`` yields the same
exact sequence of Rollouts on every call. We use a **per-rollout RNG**
seeded from a deterministic hash of ``(seed, rollout_id)`` so that
parallelizing the loop later won't reorder labels.

**Sampling design (Varn §5 distribution targets):**

- Stat lanes biased toward central buckets (mid / high) -- Kira-class
  characters are the prototype. Extreme buckets at low rate.
- Action-counter (success/failure) drawn from a long-tail distribution so
  the legacy contributions span +0..+4 without saturating.
- Verb mix 70% stealth, 30% across the 5 fallback verbs (M1 slice scope).

Serialization: **JSONL** for v0. Trade-offs documented in
``serialize_rollouts_jsonl``.
"""

from __future__ import annotations

import json
import random
from collections.abc import Iterator
from pathlib import Path
from typing import TYPE_CHECKING, Final, Literal

from pydantic import BaseModel, ConfigDict, Field

from wayfinders.ml.data_gen.designer_modifiers import compute_delta

if TYPE_CHECKING:
    from wayfinders.ml.data_gen.policies import Policy

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
    render_action,
    render_character,
    render_context,
)
from wayfinders.ml.schemas.context import FactionStance, PartyMember
from wayfinders.ml.schemas.vocabularies import (
    Alertness,
    DescriptorBucket,
    FactionRelation,
    FatigueBucket,
    Footing,
    HpBucket,
    MissionType,
    Noise,
    Precipitation,
    SkillTier,
    StakesTier,
    StressBucket,
    SupplyBucket,
    Temperature,
    TerrainModifier,
    TerrainPrimary,
    TimeOfDay,
    Watch,
    Wind,
    WindDirection,
    WoundBucket,
)

# ---------------------------------------------------------------------------
# Rollout model
# ---------------------------------------------------------------------------


class Rollout(BaseModel):
    """Structured tuple consumed by labeling + the renderers.

    All sub-states are frozen Pydantic models -- a Rollout is a snapshot,
    not a live object. ``label_delta`` is the authored target ; the rendered
    prose strings (``prose_character``, ``prose_action``, ``prose_context``)
    are the encoder inputs.
    """

    model_config = ConfigDict(frozen=True)

    rollout_id: int
    character: CharacterState
    action: ActionCard
    opposition: OppositionState
    scene: SceneState
    party: PartyState
    world: WorldState
    campaign: CampaignState

    # Authored target, ∈ [-5.0, +5.0]. Optional only at structural-build time.
    label_delta: float | None = Field(default=None, ge=-5.0, le=5.0)

    # Rendered prose strings (EN-pinned). Same Rollout always produces the
    # same triple of strings -- the renderers are pure.
    prose_character: str = ""
    prose_action: str = ""
    prose_context: str = ""

    # Traceability -- records the generation context so a rollout can be
    # replayed or audited later.  Optional at structural-build time (defaults
    # preserve backward compat with older serialized JSONL that lacks them).
    seed: int | None = Field(default=None, description="RNG seed used for this rollout")
    policy_name: str = Field(default="", description="Policy that generated this rollout")


# ---------------------------------------------------------------------------
# Closed-vocab lane tuples for sampling (mirror schemas.vocabularies Literals)
# ---------------------------------------------------------------------------

# Keeping these duplicated-but-explicit (rather than reaching into
# ``__value__`` of the type aliases at runtime) gives mypy a clean story:
# every ``rng.choice(_FOO_LANES)`` returns the precise Literal type.

_DESCRIPTOR_LANES: Final[tuple[DescriptorBucket, ...]] = (
    "very-low",
    "low",
    "mid",
    "high",
    "very-high",
)
_DESCRIPTOR_WEIGHTS: Final[tuple[float, ...]] = (0.05, 0.15, 0.40, 0.30, 0.10)

# 'down' excluded -- a downed character cannot act, so should not produce a rollout.
_HP_LANES: Final[tuple[HpBucket, ...]] = ("unhurt", "scratched", "bleeding")
_STRESS_LANES: Final[tuple[StressBucket, ...]] = (
    "composed",
    "tense",
    "frayed",
    "near-breaking",
)

_TIME_LANES: Final[tuple[TimeOfDay, ...]] = (
    "under starlight",
    "at dawn",
    "at noon",
    "in the late afternoon",
    "at dusk",
    "under heavy rain",
    "in the deep of night",
    "at first light",
)
_FOOTING_LANES: Final[tuple[Footing, ...]] = (
    "loose scree",
    "firm stone",
    "wet leaves",
    "dry sand",
    "marsh grass",
    "snow-pack",
    "polished tile",
    "rotten wood",
    "hot ash",
    "dewed grass",
)
_NOISE_LANES: Final[tuple[Noise, ...]] = (
    "near silence",
    "muffled steps",
    "open clatter",
    "the cover of wind",
    "the crash of rain",
    "muted breathing",
    "the creak of leather",
    "the rasp of armor",
)
_ALERTNESS_LANES: Final[tuple[Alertness, ...]] = (
    "alert and rested",
    "drowsy",
    "alarmed and searching",
    "distracted",
    "exhausted",
    "wary",
)
_SKILL_LANES: Final[tuple[SkillTier, ...]] = (
    "green",
    "ordinary",
    "seasoned",
    "veteran",
    "elite",
)

_MISSION_LANES: Final[tuple[MissionType, ...]] = (
    "reconnaissance",
    "infiltration",
    "escort",
    "ambush",
    "parley",
    "recovery",
    "raid",
    "siege",
    "evacuation",
    "scouting patrol",
    "supply run",
    "rescue",
)
_STAKES_LANES: Final[tuple[StakesTier, ...]] = (
    "trivial",
    "modest",
    "serious",
    "high",
    "dire",
)
_WATCH_LANES: Final[tuple[Watch, ...]] = (
    "early morning",
    "late morning",
    "midday",
    "early afternoon",
    "late afternoon",
    "first watch of the night",
    "second watch of the night",
    "third watch of the night",
)
_WOUND_LANES: Final[tuple[WoundBucket, ...]] = (
    "unhurt",
    "lightly wounded",
    "battered",
)
_SUPPLY_LANES: Final[tuple[SupplyBucket, ...]] = (
    "well-supplied",
    "low on rations",
    "starving",
)
_FATIGUE_LANES: Final[tuple[FatigueBucket, ...]] = (
    "rested",
    "short on sleep",
    "exhausted",
)
_TEMPERATURE_LANES: Final[tuple[Temperature, ...]] = (
    "bitterly cold",
    "cold",
    "cool",
    "warm",
    "hot",
)
_PRECIP_LANES: Final[tuple[Precipitation, ...]] = (
    "dry",
    "drizzling",
    "raining steadily",
    "in driving rain",
    "snowing lightly",
    "in heavy snow",
)
_WIND_LANES: Final[tuple[Wind, ...]] = (
    "no wind",
    "a light breeze",
    "a steady wind",
    "a strong wind",
    "gale-force gusts",
)
_WIND_DIRS: Final[tuple[WindDirection, ...]] = ("north", "south", "east", "west")
_TERRAIN_LANES: Final[tuple[TerrainPrimary, ...]] = (
    "broken stone and scree",
    "deep forest",
    "dense brush",
    "open plains",
    "marsh and reed",
    "paved streets and crowded squares",
    "narrow corridors of dressed stone",
    "river crossing",
    "high cliffs",
    "snowfields",
)
_TERRAIN_MODIFIER_LANES: Final[tuple[TerrainModifier, ...]] = (
    "sparse pine cover",
    "thick canopy overhead",
    "festival lanterns strung overhead",
    "old battlements above",
    "ruined walls flanking the path",
    "shallow water underfoot",
    "wind-scoured boulders",
    "dense undergrowth",
    "crowded rooftops above",
    "frozen ground underfoot",
)
_FACTION_LANES: Final[tuple[FactionRelation, ...]] = (
    "at war",
    "at uneasy peace",
    "at peace",
    "allied",
    "in open trade",
    "in cold neutrality",
)
_SEASON_LANES: Final[
    tuple[
        Literal[
            "early spring",
            "late spring",
            "early summer",
            "late summer",
            "early autumn",
            "late autumn",
            "early winter",
            "deep winter",
        ],
        ...,
    ]
] = (
    "early spring",
    "late spring",
    "early summer",
    "late summer",
    "early autumn",
    "late autumn",
    "early winter",
    "deep winter",
)
_FAMILIARITY_LANES: Final[tuple[Literal["barely", "moderately", "well", "best of all"], ...]] = (
    "barely",
    "moderately",
    "well",
    "best of all",
)


# ---------------------------------------------------------------------------
# Verb-driven sampling primitives
# ---------------------------------------------------------------------------

# Verbs in the slice (Varn MVP scope). Stealth-heavy mix.
SUPPORTED_VERBS: Final[tuple[str, ...]] = (
    "stealth approach",
    "move",
    "wait",
    "signal to ally",
    "melee attack",
    "ranged attack",
)
VERB_WEIGHTS: Final[tuple[float, ...]] = (0.70, 0.06, 0.04, 0.05, 0.08, 0.07)

_VERB_TARGETS: Final[dict[str, tuple[str, ...]]] = {
    "stealth approach": (
        "two sentries posted at the watchtower",
        "the enemy supply tent",
        "a goblin scout perched on the ridge",
    ),
    "move": (
        "across the open ground to the next outcrop",
        "down the gully toward the river",
        "back to cover behind the boulder line",
    ),
    "wait": (
        "for the next watch rotation to pass",
        "for the storm to settle",
        "until the signal fire is lit",
    ),
    "signal to ally": (
        "Halden, posted on the ridge",
        "Mira, holding the rear",
        "the scout pair on the far slope",
    ),
    "melee attack": (
        "the nearest sentry with a drawn dagger",
        "the goblin captain at the head of the patrol",
        "the wounded raider blocking the path",
    ),
    "ranged attack": (
        "the lookout silhouetted against the moon",
        "the goblin signaler before he can blow the horn",
        "the supply wagon's torch-bearer",
    ),
}

_VERB_PROGRESSIVES: Final[dict[str, str]] = {
    "stealth approach": "sneaking past",
    "move": "moving",
    "wait": "waiting on",
    "signal to ally": "signaling to",
    "melee attack": "charging",
    "ranged attack": "loosing an arrow at",
}

_VERB_INTENTS: Final[dict[str, str]] = {
    "stealth approach": "reach the ridge unseen and unheard",
    "move": "close the distance without losing time",
    "wait": "let the moment pass and conserve initiative",
    "signal to ally": "coordinate the next move without speaking aloud",
    "melee attack": "drop the threat before it can raise an alarm",
    "ranged attack": "neutralize at distance before contact",
}

_VERB_TAG_SETS: Final[dict[str, tuple[str, ...]]] = {
    "stealth approach": ("stealth", "movement", "scouting"),
    "move": ("movement",),
    "wait": ("waiting",),
    "signal to ally": ("communication",),
    "melee attack": ("combat", "melee"),
    "ranged attack": ("combat", "ranged"),
}

_OPPOSITION_FAMILIES: Final[tuple[str, ...]] = (
    "humanoids",
    "goblin scouts",
    "elven rangers",
    "human sentries",
    "wild beasts",
)

_VerbStat = Literal["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"]
_OpposedStat = Literal[
    "strength",
    "dexterity",
    "constitution",
    "intelligence",
    "wisdom",
    "charisma",
    "perception",
    "armor",
    "resolve",
]

_VERB_STATS: Final[dict[str, tuple[_VerbStat, _OpposedStat]]] = {
    "stealth approach": ("dexterity", "perception"),
    "move": ("dexterity", "armor"),
    "wait": ("wisdom", "resolve"),
    "signal to ally": ("wisdom", "perception"),
    "melee attack": ("strength", "armor"),
    "ranged attack": ("dexterity", "armor"),
}

_VERB_FAILURE_TEMPLATES: Final[dict[str, str]] = {
    "stealth approach": ("an alarm raised, a fight on disadvantageous ground, or a forced retreat"),
    "move": "a wasted turn and ground given up",
    "wait": "the moment lost and initiative passed to the opposition",
    "signal to ally": "a missed cue and the party out of sync",
    "melee attack": "a counter-thrust and ground given up",
    "ranged attack": "a wasted shaft and the position revealed",
}

_CONDITION_POOL: Final[tuple[str, ...]] = (
    "with a fading bruise on her forearm",
    "well-fed from a hot breakfast",
    "rested after a long sleep",
    "wet from the river crossing",
    "cold from the long watch",
    "inspired by the captain's words",
    "exhausted from three nights of forced march",
    "marked by an enemy seer",
    "silenced by a friendly hex",
)

_EQUIPMENT_POOL: Final[tuple[str, ...]] = (
    "a worn leather jerkin",
    "a padded coat",
    "a chainmail hauberk",
    "a plate cuirass",
    "a cloak of shadows",
    "soft-soled boots",
    "wrapped feet",
    "hobnailed boots",
    "iron-shod boots",
    "a short bow she made herself",
    "a sling and pouch of stones",
    "a long dagger",
    "a longsword at her hip",
    "a heavy axe",
    "a two-handed maul",
    "a light pack",
    "a loaded pack",
    "an overloaded pack",
)

_TRAIT_POOL: Final[tuple[str, ...]] = (
    "patient",
    "cautious",
    "disciplined",
    "bold",
    "loyal",
    "proud",
    "impatient",
    "arrogant",
    "reckless",
    "clumsy-footed",
)

_LOCATIONS: Final[tuple[str, ...]] = (
    "forest terrain",
    "dungeon corridors",
    "town streets",
    "broken stone",
    "river crossings",
)

_EPISODIC_DESCRIPTIONS: Final[tuple[str, ...]] = (
    "her first kill, a goblin scout in the Hollowwood",
    "the night she lost Brann to ghouls in the Catacombs of Vesh",
    "the day she spared the bandit captain at Fenmarch crossing",
    "the silent infiltration of the Ashen Hand camp",
    "her first stealth approach against an elven patrol",
    "the long stand at Dunmoor when the supply lines failed",
)


# ---------------------------------------------------------------------------
# Lane samplers (deterministic RNG, no module state)
# ---------------------------------------------------------------------------


def _sample_descriptor(rng: random.Random) -> DescriptorBucket:
    return rng.choices(list(_DESCRIPTOR_LANES), weights=list(_DESCRIPTOR_WEIGHTS), k=1)[0]


def _sample_traits(rng: random.Random) -> tuple[str, ...]:
    n = rng.choice([1, 2, 3])
    return tuple(rng.sample(list(_TRAIT_POOL), k=n))


def _sample_equipment(rng: random.Random) -> tuple[str, ...]:
    n = rng.randint(1, 4)
    return tuple(rng.sample(list(_EQUIPMENT_POOL), k=n))


def _sample_conditions(rng: random.Random) -> tuple[str, ...]:
    if rng.random() < 0.4:
        return ()
    n = rng.choice([1, 2])
    return tuple(rng.sample(list(_CONDITION_POOL), k=n))


def _sample_action_counters(rng: random.Random, verb: str) -> dict[str, tuple[int, int]]:
    """Long-tail distribution of (successes, failures) for the chosen verb.

    Most rollouts have <5 successes ; a tail extends to 30+ to exercise the
    legacy +4 tier. Failures bias smaller than successes.
    """
    base = rng.random()
    if base < 0.3:
        successes = rng.randint(0, 1)
    elif base < 0.65:
        successes = rng.randint(1, 4)
    elif base < 0.90:
        successes = rng.randint(4, 12)
    else:
        successes = rng.randint(15, 40)
    failures = rng.randint(0, max(1, successes // 3))
    plural_key = verb + ("es" if not verb.endswith("e") else "s")
    return {plural_key: (successes, failures)}


def _sample_profession_hours(rng: random.Random) -> dict[str, int]:
    out: dict[str, int] = {}
    if rng.random() < 0.7:
        out["scouting"] = rng.choice([0, 20, 80, 150, 300])
    if rng.random() < 0.3:
        out["thieving"] = rng.choice([0, 30, 80])
    if rng.random() < 0.5:
        out["at the forge"] = rng.choice([0, 100, 600, 1240])
    return out


def _sample_episodic(rng: random.Random) -> tuple[EpisodicEvent, ...]:
    n = rng.choice([0, 1, 2, 3])
    if n == 0:
        return ()
    descriptions = rng.sample(list(_EPISODIC_DESCRIPTIONS), k=n)
    salience_floor = 5.0
    return tuple(
        EpisodicEvent(
            event_id=i + 1,
            salience=salience_floor + rng.uniform(0.0, 5.0),
            description=desc,
        )
        for i, desc in enumerate(descriptions)
    )


def _sample_bonds(rng: random.Random) -> tuple[Bond, ...]:
    n = rng.choice([0, 1, 2])
    if n == 0:
        return ()
    names = rng.sample(["Brann", "Halden", "Mira", "Renn", "Senna"], k=n)
    return tuple(
        Bond(
            partner_name=name,
            bond_value=rng.choice([-3, -1, 1, 3, 5]),
            partner_is_dead=rng.random() < 0.1,
        )
        for name in names
    )


# ---------------------------------------------------------------------------
# Sub-state builders
# ---------------------------------------------------------------------------


def _build_character(rng: random.Random, verb: str) -> CharacterState:
    name = rng.choice(["Kira", "Edda", "Renn", "Senna", "Brann", "Halden"])
    locations_for_char = rng.sample(list(_LOCATIONS), k=rng.randint(0, 3))
    return CharacterState(
        name=name,
        char_class=rng.choice(["scout", "ranger", "rogue", "skirmisher"]),
        faction="Ridgewatch company",
        str_bucket=_sample_descriptor(rng),
        dex_bucket=_sample_descriptor(rng),
        wis_bucket=_sample_descriptor(rng),
        traits=_sample_traits(rng),
        hp_bucket=rng.choice(list(_HP_LANES)),
        stress_bucket=rng.choice(list(_STRESS_LANES)),
        conditions=_sample_conditions(rng),
        equipment=_sample_equipment(rng),
        combat_engagements={},
        action_counters=_sample_action_counters(rng, verb),
        profession_hours=_sample_profession_hours(rng),
        location_familiarity={
            loc: rng.choice(list(_FAMILIARITY_LANES)) for loc in locations_for_char
        },
        episodic_memory=_sample_episodic(rng),
        bonds=_sample_bonds(rng),
    )


def _build_action(rng: random.Random, verb: str) -> ActionCard:
    stat_tested, opposed = _VERB_STATS[verb]
    return ActionCard(
        action_phrase=verb,
        verb_progressive=_VERB_PROGRESSIVES[verb],
        target_phrase=rng.choice(list(_VERB_TARGETS[verb])),
        time_phrase=rng.choice(list(_TIME_LANES)),
        footing_phrase=rng.choice(list(_FOOTING_LANES)),
        noise_phrase=rng.choice(list(_NOISE_LANES)),
        intent_phrase=_VERB_INTENTS[verb],
        tag_set=_VERB_TAG_SETS[verb],
        opposition_family=rng.choice(list(_OPPOSITION_FAMILIES)),
        stat_tested=stat_tested,
        opposed_stat=opposed,
        failure_template=_VERB_FAILURE_TEMPLATES[verb],
    )


def _build_opposition(rng: random.Random, verb: str) -> OppositionState:
    count = rng.choice([0, 0, 0, 1, 2]) if verb == "wait" else rng.choice([1, 2, 2, 3, 4, 6])
    return OppositionState(
        count=count,
        family=rng.choice(list(_OPPOSITION_FAMILIES)),
        alertness=rng.choice(list(_ALERTNESS_LANES)),
        skill_tier=rng.choice(list(_SKILL_LANES)),
    )


def _build_scene(rng: random.Random) -> SceneState:
    has_countdown = rng.random() < 0.7
    return SceneState(
        mission_type=rng.choice(list(_MISSION_LANES)),
        sponsor_faction="Ridgewatch company",
        objective_phrase="map the enemy camp beyond the ridge before dawn",
        stakes_tier=rng.choice(list(_STAKES_LANES)),
        failure_phrase="the company moves blind into the valley at first light",
        watch=rng.choice(list(_WATCH_LANES)),
        countdown_hours=rng.randint(0, 8) if has_countdown else None,
        deadline_label="dawn" if has_countdown else None,
    )


def _build_party(rng: random.Random) -> PartyState:
    n_members = rng.randint(1, 4)
    members = tuple(
        PartyMember(
            name=rng.choice(["Kira", "Halden", "Mira", "Renn", "Brann"]) + f"_{i}",
            char_class=rng.choice(["scout", "cleric", "quartermaster", "ranger"]),
            initiative_index=i,
        )
        for i in range(n_members)
    )
    return PartyState(
        members=members,
        wound_bucket=rng.choice(list(_WOUND_LANES)),
        supply_bucket=rng.choice(list(_SUPPLY_LANES)),
        fatigue_bucket=rng.choice(list(_FATIGUE_LANES)),
    )


def _build_world(rng: random.Random) -> WorldState:
    wind_choice: Wind = rng.choice(list(_WIND_LANES))
    wind_dir: WindDirection | None
    wind_dir = None if wind_choice == "no wind" else rng.choice(list(_WIND_DIRS))
    terrain_mod: TerrainModifier | None
    terrain_mod = rng.choice(list(_TERRAIN_MODIFIER_LANES)) if rng.random() < 0.3 else None
    n_stances = rng.choice([0, 1, 2])
    stances = tuple(
        FactionStance(
            other_faction=rng.choice(["Ashen Hand", "river clans", "iron syndicate"]),
            relation=rng.choice(list(_FACTION_LANES)),
        )
        for _ in range(n_stances)
    )
    return WorldState(
        region="Ridgewatch frontier",
        season=rng.choice(list(_SEASON_LANES)),
        sky_phrase=rng.choice(["a thin crescent moon", "a full harvest moon", "a clear sky"]),
        temperature=rng.choice(list(_TEMPERATURE_LANES)),
        precipitation=rng.choice(list(_PRECIP_LANES)),
        wind=wind_choice,
        wind_direction=wind_dir,
        terrain_primary=rng.choice(list(_TERRAIN_LANES)),
        terrain_modifier=terrain_mod,
        faction_stances=stances,
    )


def _build_campaign(rng: random.Random) -> CampaignState:
    return CampaignState(
        season_number=rng.randint(1, 12),
        holdings_phrase=rng.choice(
            [
                "winning across the eastern marches",
                "holding two ridges and having lost one",
                "losing ground in the southern passes",
                "ascendant after the spring offensive",
            ]
        ),
    )


def _build_rollout(
    rollout_id: int,
    rng: random.Random,
    policy: Policy | None = None,
    *,
    seed: int | None = None,
) -> Rollout:
    """Construct one Rollout, deterministic per ``rng``.

    When ``policy`` is not None the action/opposition/scene/party/world/campaign
    tuple is drawn via ``policy.sample(rng)``.  When it is None the legacy
    flat samplers are used (backward-compatible path, same RNG trajectory as
    before this change).

    The fixed sub-build order below is part of the determinism contract --
    changing it would change the RNG trajectory and break replayability of
    existing seeds.
    """
    if policy is not None:
        verb, action, opposition, scene, party, world, campaign = policy.sample(rng)
        char = _build_character(rng, verb)
        policy_name = policy.name
    else:
        verb = rng.choices(list(SUPPORTED_VERBS), weights=list(VERB_WEIGHTS), k=1)[0]
        char = _build_character(rng, verb)
        action = _build_action(rng, verb)
        opposition = _build_opposition(rng, verb)
        scene = _build_scene(rng)
        party = _build_party(rng)
        world = _build_world(rng)
        campaign = _build_campaign(rng)
        policy_name = "default"

    label = compute_delta(
        character=char,
        action=action,
        opposition=opposition,
        scene=scene,
        party=party,
        world=world,
        campaign=campaign,
    )
    prose_c = render_character(char)
    prose_a = render_action(action, actor_name=char.name, opposition=opposition)
    prose_x = render_context(scene, party, world, campaign)
    return Rollout(
        rollout_id=rollout_id,
        character=char,
        action=action,
        opposition=opposition,
        scene=scene,
        party=party,
        world=world,
        campaign=campaign,
        label_delta=label,
        prose_character=prose_c,
        prose_action=prose_a,
        prose_context=prose_x,
        seed=seed,
        policy_name=policy_name,
    )


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


def generate_rollouts(
    n: int,
    seed: int,
    policy: Policy | None = None,
) -> Iterator[Rollout]:
    """Lazily yield ``n`` Rollouts deterministically from ``seed``.

    Determinism: each rollout uses an independent ``random.Random`` seeded
    from ``(seed * 2654435761 + rollout_id) & 0xFFFFFFFF`` (Knuth multiplicative
    hash). Partial generation, parallel evaluation, or replays all yield the
    same Rollout for a given ``(seed, rollout_id)``.

    Same ``(n, seed, policy)`` -> same iterator output, byte-for-byte.

    When ``policy`` is None the legacy default flat sampler is used (same
    RNG trajectory as substep 2 / PR #14 -- no behavioral change for existing
    callers).  Pass a ``Policy`` instance to use correlated or mixed sampling.
    """
    if n < 0:
        raise ValueError(f"n must be non-negative, got {n}")
    for rollout_id in range(n):
        sub_seed = (seed * 2654435761 + rollout_id) & 0xFFFFFFFF
        rng = random.Random(sub_seed)
        yield _build_rollout(rollout_id, rng, policy=policy, seed=seed)


def serialize_rollouts_jsonl(rollouts: Iterator[Rollout], out_path: Path) -> int:
    """Write rollouts to ``out_path`` as JSONL ; return the count written.

    Format choice -- **JSONL** for v0:

    - **Pros:** human-diffable, streaming-friendly, no extra dep beyond
      ``pydantic.BaseModel.model_dump_json``, easy to inspect with
      ``head`` / ``jq``. At ~3 KB/rollout x 50k rollouts ~= 150 MB, fits
      comfortably on dev machines.
    - **Cons:** ~2x larger than parquet ; slower load for the trainer (parses
      JSON per row). Acceptable for M1 -- training spends most of its time
      in the encoder anyway.
    - **Upgrade path:** when 50k rollouts x multiple seeds saturates disk or
      trainer load time, swap this for parquet via ``pyarrow``. Rollout is
      already Pydantic, so ``pyarrow.Table.from_pylist([r.model_dump() for r
      in rollouts])`` is the one-liner.
    """
    out_path.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with out_path.open("w", encoding="utf-8") as f:
        for r in rollouts:
            f.write(r.model_dump_json())
            f.write("\n")
            count += 1
    return count


def load_rollouts_jsonl(in_path: Path) -> Iterator[Rollout]:
    """Yield Rollouts from a JSONL file written by ``serialize_rollouts_jsonl``."""
    with in_path.open(encoding="utf-8") as f:
        for raw_line in f:
            line = raw_line.strip()
            if not line:
                continue
            yield Rollout.model_validate(json.loads(line))
