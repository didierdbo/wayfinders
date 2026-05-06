"""Sampling policies for rollout generation.

Three policies covering the design space of structured (action, opposition,
scene, party, world, campaign) tuples per Pax 2026-05-02 §4 step 1.

All policies accept a ``random.Random`` instance for determinism -- no module-
level RNG state is touched. The seeding contract is the caller's responsibility
(``generate_rollouts`` provides a per-rollout seed).

Policy hierarchy:
    ``Policy``        -- abstract base (Protocol-style)
    ``RandomPolicy``  -- uniform sampling over each lane; maximal tail coverage
    ``ScriptedPolicy``-- correlated sampling; denser signal on plausible combos
    ``MixedPolicy``   -- weighted blend of the two; default for training sets

Return type of ``sample``:
    ``SampledTuple`` alias:
    ``tuple[ActionCard, OppositionState, SceneState, PartyState, WorldState, CampaignState]``

Character sampling is NOT handled by these policies -- the character is always
drawn from the archetype library inside ``rollouts._build_character`` because
the character depends on the verb (action_phrase) which itself is drawn during
the sample() call.  The contract is:

    verb, action, opposition, scene, party, world, campaign = policy.sample(rng)

and the caller builds the character using ``verb``.
"""

from __future__ import annotations

import random
from typing import Protocol, runtime_checkable

from wayfinders.ml.schemas import (
    ActionCard,
    CampaignState,
    OppositionState,
    PartyState,
    SceneState,
    WorldState,
)
from wayfinders.ml.schemas.context import FactionStance, PartyMember
from wayfinders.ml.schemas.vocabularies import (
    Alertness,
    FactionRelation,
    FatigueBucket,
    Footing,
    MissionType,
    Noise,
    Precipitation,
    SkillTier,
    StakesTier,
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
# Public type alias
# ---------------------------------------------------------------------------

# (verb_str, ActionCard, OppositionState, SceneState, PartyState, WorldState,
#  CampaignState)
SampledTuple = tuple[
    str, ActionCard, OppositionState, SceneState, PartyState, WorldState, CampaignState
]

# ---------------------------------------------------------------------------
# Policy Protocol
# ---------------------------------------------------------------------------


@runtime_checkable
class Policy(Protocol):
    """Sampling policy -- returns one structured (verb + states) tuple per call.

    Implementors must be deterministic given the same ``rng`` state.
    ``name`` is a short string identifier stored on Rollout for traceability.
    """

    @property
    def name(self) -> str: ...

    def sample(self, rng: random.Random) -> SampledTuple: ...


# ---------------------------------------------------------------------------
# Closed-vocab lane tuples  (mirrors rollouts.py -- kept local so policies.py
# is self-contained and rollouts.py can import from here later if desired)
# ---------------------------------------------------------------------------

_TIME_LANES: tuple[TimeOfDay, ...] = (
    "under starlight",
    "at dawn",
    "at noon",
    "in the late afternoon",
    "at dusk",
    "under heavy rain",
    "in the deep of night",
    "at first light",
)
_FOOTING_LANES: tuple[Footing, ...] = (
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
_NOISE_LANES: tuple[Noise, ...] = (
    "near silence",
    "muffled steps",
    "open clatter",
    "the cover of wind",
    "the crash of rain",
    "muted breathing",
    "the creak of leather",
    "the rasp of armor",
)
_ALERTNESS_LANES: tuple[Alertness, ...] = (
    "alert and rested",
    "drowsy",
    "alarmed and searching",
    "distracted",
    "exhausted",
    "wary",
)
_SKILL_LANES: tuple[SkillTier, ...] = (
    "green",
    "ordinary",
    "seasoned",
    "veteran",
    "elite",
)
_MISSION_LANES: tuple[MissionType, ...] = (
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
_STAKES_LANES: tuple[StakesTier, ...] = (
    "trivial",
    "modest",
    "serious",
    "high",
    "dire",
)
_WATCH_LANES: tuple[Watch, ...] = (
    "early morning",
    "late morning",
    "midday",
    "early afternoon",
    "late afternoon",
    "first watch of the night",
    "second watch of the night",
    "third watch of the night",
)
_WOUND_LANES: tuple[WoundBucket, ...] = (
    "unhurt",
    "lightly wounded",
    "battered",
)
_SUPPLY_LANES: tuple[SupplyBucket, ...] = (
    "well-supplied",
    "low on rations",
    "starving",
)
_FATIGUE_LANES: tuple[FatigueBucket, ...] = (
    "rested",
    "short on sleep",
    "exhausted",
)
_TEMPERATURE_LANES: tuple[Temperature, ...] = (
    "bitterly cold",
    "cold",
    "cool",
    "warm",
    "hot",
)
_PRECIP_LANES: tuple[Precipitation, ...] = (
    "dry",
    "drizzling",
    "raining steadily",
    "in driving rain",
    "snowing lightly",
    "in heavy snow",
)
_WIND_LANES: tuple[Wind, ...] = (
    "no wind",
    "a light breeze",
    "a steady wind",
    "a strong wind",
    "gale-force gusts",
)
_WIND_DIRS: tuple[WindDirection, ...] = ("north", "south", "east", "west")
_TERRAIN_LANES: tuple[TerrainPrimary, ...] = (
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
_TERRAIN_MODIFIER_LANES: tuple[TerrainModifier, ...] = (
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
_FACTION_LANES: tuple[FactionRelation, ...] = (
    "at war",
    "at uneasy peace",
    "at peace",
    "allied",
    "in open trade",
    "in cold neutrality",
)
_SEASON_LANES: tuple[str, ...] = (
    "early spring",
    "late spring",
    "early summer",
    "late summer",
    "early autumn",
    "late autumn",
    "early winter",
    "deep winter",
)

# Verbs (mirrors rollouts.py SUPPORTED_VERBS -- duplicate intentional for
# policy self-containment; the two lists must stay in sync).
_SUPPORTED_VERBS: tuple[str, ...] = (
    "stealth approach",
    "move",
    "wait",
    "signal to ally",
    "melee attack",
    "ranged attack",
)
_VERB_WEIGHTS: tuple[float, ...] = (0.70, 0.06, 0.04, 0.05, 0.08, 0.07)

_VERB_TARGETS: dict[str, tuple[str, ...]] = {
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
_VERB_PROGRESSIVES: dict[str, str] = {
    "stealth approach": "sneaking past",
    "move": "moving",
    "wait": "waiting on",
    "signal to ally": "signaling to",
    "melee attack": "charging",
    "ranged attack": "loosing an arrow at",
}
_VERB_INTENTS: dict[str, str] = {
    "stealth approach": "reach the ridge unseen and unheard",
    "move": "close the distance without losing time",
    "wait": "let the moment pass and conserve initiative",
    "signal to ally": "coordinate the next move without speaking aloud",
    "melee attack": "drop the threat before it can raise an alarm",
    "ranged attack": "neutralize at distance before contact",
}
_VERB_TAG_SETS: dict[str, tuple[str, ...]] = {
    "stealth approach": ("stealth", "movement", "scouting"),
    "move": ("movement",),
    "wait": ("waiting",),
    "signal to ally": ("communication",),
    "melee attack": ("combat", "melee"),
    "ranged attack": ("combat", "ranged"),
}
_VERB_FAILURE_TEMPLATES: dict[str, str] = {
    "stealth approach": "an alarm raised, a fight on disadvantageous ground, or a forced retreat",
    "move": "a wasted turn and ground given up",
    "wait": "the moment lost and initiative passed to the opposition",
    "signal to ally": "a missed cue and the party out of sync",
    "melee attack": "a counter-thrust and ground given up",
    "ranged attack": "a wasted shaft and the position revealed",
}

_VerbStat = str  # "strength" | "dexterity" | ...
_OpposedStat = str  # "perception" | "armor" | ...

_VERB_STATS: dict[str, tuple[str, str]] = {
    "stealth approach": ("dexterity", "perception"),
    "move": ("dexterity", "armor"),
    "wait": ("wisdom", "resolve"),
    "signal to ally": ("wisdom", "perception"),
    "melee attack": ("strength", "armor"),
    "ranged attack": ("dexterity", "armor"),
}
_OPPOSITION_FAMILIES: tuple[str, ...] = (
    "humanoids",
    "goblin scouts",
    "elven rangers",
    "human sentries",
    "wild beasts",
)

# ---------------------------------------------------------------------------
# Shared build helpers (used by both policies)
# ---------------------------------------------------------------------------


def _build_action_from_verb(rng: random.Random, verb: str) -> ActionCard:
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
        stat_tested=stat_tested,  # type: ignore[arg-type]  # closed-vocab, runtime-safe
        opposed_stat=opposed,  # type: ignore[arg-type]  # closed-vocab, runtime-safe
        failure_template=_VERB_FAILURE_TEMPLATES[verb],
    )


def _build_opposition_from_verb(rng: random.Random, verb: str) -> OppositionState:
    count = rng.choice([0, 0, 0, 1, 2]) if verb == "wait" else rng.choice([1, 2, 2, 3, 4, 6])
    return OppositionState(
        count=count,
        family=rng.choice(list(_OPPOSITION_FAMILIES)),
        alertness=rng.choice(list(_ALERTNESS_LANES)),
        skill_tier=rng.choice(list(_SKILL_LANES)),
    )


def _build_scene_uniform(rng: random.Random) -> SceneState:
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


def _build_party_uniform(rng: random.Random) -> PartyState:
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


def _build_world_uniform(rng: random.Random) -> WorldState:
    wind_choice: Wind = rng.choice(list(_WIND_LANES))
    wind_dir: WindDirection | None = (
        None if wind_choice == "no wind" else rng.choice(list(_WIND_DIRS))
    )
    terrain_mod: TerrainModifier | None = (
        rng.choice(list(_TERRAIN_MODIFIER_LANES)) if rng.random() < 0.3 else None
    )
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
        season=rng.choice(list(_SEASON_LANES)),  # type: ignore[arg-type]  # closed-vocab
        sky_phrase=rng.choice(["a thin crescent moon", "a full harvest moon", "a clear sky"]),
        temperature=rng.choice(list(_TEMPERATURE_LANES)),
        precipitation=rng.choice(list(_PRECIP_LANES)),
        wind=wind_choice,
        wind_direction=wind_dir,
        terrain_primary=rng.choice(list(_TERRAIN_LANES)),
        terrain_modifier=terrain_mod,
        faction_stances=stances,
    )


def _build_campaign_uniform(rng: random.Random) -> CampaignState:
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


# ---------------------------------------------------------------------------
# RandomPolicy
# ---------------------------------------------------------------------------


class RandomPolicy:
    """Uniform sampling policy -- each field drawn independently.

    Design intent: maximal coverage of the encoder's input space, including
    the low-probability tails that ScriptedPolicy undersamples. Verb weights
    match the 70/30 stealth-heavy distribution from Varn §5.
    """

    @property
    def name(self) -> str:
        return "random"

    def sample(self, rng: random.Random) -> SampledTuple:
        verb: str = rng.choices(list(_SUPPORTED_VERBS), weights=list(_VERB_WEIGHTS), k=1)[0]
        action = _build_action_from_verb(rng, verb)
        opposition = _build_opposition_from_verb(rng, verb)
        scene = _build_scene_uniform(rng)
        party = _build_party_uniform(rng)
        world = _build_world_uniform(rng)
        campaign = _build_campaign_uniform(rng)
        return verb, action, opposition, scene, party, world, campaign


# ---------------------------------------------------------------------------
# ScriptedPolicy
# ---------------------------------------------------------------------------

# Stealth-favourable time phrases -- night / low-light conditions.
# Drawn from _TIME_LANES subset for the correlated stealth path.
_STEALTH_GOOD_TIMES: tuple[TimeOfDay, ...] = (
    "under starlight",
    "in the deep of night",
    "at first light",
    "at dusk",
)

# Non-alerting opposition alertness states (sentry not yet triggered).
_CALM_ALERTNESS: tuple[Alertness, ...] = (
    "drowsy",
    "distracted",
    "exhausted",
    "wary",
)

# High-pressure alertness (sentry already looking).
_ALARMED_ALERTNESS: tuple[Alertness, ...] = (
    "alert and rested",
    "alarmed and searching",
)

# High-stakes mission types (signals time pressure).
_HIGH_STAKES_MISSIONS: tuple[MissionType, ...] = (
    "reconnaissance",
    "infiltration",
    "ambush",
    "siege",
    "evacuation",
    "rescue",
)

# Stressful party conditions paired with high-stakes missions.
_STRAINED_SUPPLY: tuple[SupplyBucket, ...] = ("low on rations", "starving")
_STRAINED_FATIGUE: tuple[FatigueBucket, ...] = ("short on sleep", "exhausted")

# Night watches (correlates with stealth missions).
_NIGHT_WATCHES: tuple[Watch, ...] = (
    "first watch of the night",
    "second watch of the night",
    "third watch of the night",
)


class ScriptedPolicy:
    """Correlated sampling policy -- plausible situation clusters.

    Correlations encoded:
      - stealth approach → night/low-light time, near-silence noise,
        calm opposition (not already alarmed), night watch
      - melee/ranged attack → alarmed or alert opposition, higher skill tiers
      - high-stakes missions → strained supply/fatigue, countdown active
      - wait actions → solo or no opposition, relaxed party condition

    These correlations increase the density of high-signal training examples
    in the regions the designer actually cares about. Random samples remain
    possible at low weight for each correlated draw (avoids degenerate
    distributions that would prevent the model from learning the contrast).
    """

    @property
    def name(self) -> str:
        return "scripted"

    def sample(self, rng: random.Random) -> SampledTuple:
        verb: str = rng.choices(list(_SUPPORTED_VERBS), weights=list(_VERB_WEIGHTS), k=1)[0]
        action = self._build_action(rng, verb)
        opposition = self._build_opposition(rng, verb)
        scene = self._build_scene(rng, verb)
        party = self._build_party(rng, verb, scene)
        world = self._build_world(rng, verb, scene)
        campaign = _build_campaign_uniform(rng)
        return verb, action, opposition, scene, party, world, campaign

    # ------------------------------------------------------------------
    # Correlated sub-builders
    # ------------------------------------------------------------------

    def _build_action(self, rng: random.Random, verb: str) -> ActionCard:
        stat_tested, opposed = _VERB_STATS[verb]

        if verb == "stealth approach":
            # Night/low-light time strongly preferred for stealth.
            time_phrase: TimeOfDay = (
                rng.choice(list(_STEALTH_GOOD_TIMES))
                if rng.random() < 0.75
                else rng.choice(list(_TIME_LANES))
            )
            # Near-silence preferred for stealth (louder noise = harder).
            _quiet_noises: tuple[Noise, ...] = ("near silence", "muffled steps")
            noise_phrase: Noise = (
                rng.choice(list(_quiet_noises))
                if rng.random() < 0.70
                else rng.choice(list(_NOISE_LANES))
            )
        else:
            time_phrase = rng.choice(list(_TIME_LANES))
            noise_phrase = rng.choice(list(_NOISE_LANES))

        return ActionCard(
            action_phrase=verb,
            verb_progressive=_VERB_PROGRESSIVES[verb],
            target_phrase=rng.choice(list(_VERB_TARGETS[verb])),
            time_phrase=time_phrase,
            footing_phrase=rng.choice(list(_FOOTING_LANES)),
            noise_phrase=noise_phrase,
            intent_phrase=_VERB_INTENTS[verb],
            tag_set=_VERB_TAG_SETS[verb],
            opposition_family=rng.choice(list(_OPPOSITION_FAMILIES)),
            stat_tested=stat_tested,  # type: ignore[arg-type]
            opposed_stat=opposed,  # type: ignore[arg-type]
            failure_template=_VERB_FAILURE_TEMPLATES[verb],
        )

    def _build_opposition(self, rng: random.Random, verb: str) -> OppositionState:
        if verb == "wait":
            count = rng.choice([0, 0, 0, 1, 2])
            # Waiting: opposition usually not alarmed.
            alertness: Alertness = (
                rng.choice(list(_CALM_ALERTNESS))
                if rng.random() < 0.80
                else rng.choice(list(_ALERTNESS_LANES))
            )
        elif verb == "stealth approach":
            count = rng.choice([1, 2, 2, 3, 4, 6])
            # Stealth is far more interesting vs non-alarmed opposition.
            # ~75% calm, ~25% already alarmed (teaches the model the hard case too).
            alertness = (
                rng.choice(list(_CALM_ALERTNESS))
                if rng.random() < 0.75
                else rng.choice(list(_ALARMED_ALERTNESS))
            )
        elif verb in ("melee attack", "ranged attack"):
            count = rng.choice([1, 2, 2, 3, 4, 6])
            # Combat: opposition can be alert or alarmed (fighting is noisier).
            alertness = (
                rng.choice(list(_ALARMED_ALERTNESS))
                if rng.random() < 0.55
                else rng.choice(list(_CALM_ALERTNESS))
            )
        else:
            count = rng.choice([1, 2, 2, 3, 4, 6])
            alertness = rng.choice(list(_ALERTNESS_LANES))

        # Combat verbs skew toward more skilled opponents (higher stakes).
        _veteran_tiers: tuple[SkillTier, ...] = ("ordinary", "seasoned", "veteran", "elite")
        if verb in ("melee attack", "ranged attack") and rng.random() < 0.60:
            skill_tier: SkillTier = rng.choice(list(_veteran_tiers))
        else:
            skill_tier = rng.choice(list(_SKILL_LANES))

        return OppositionState(
            count=count,
            family=rng.choice(list(_OPPOSITION_FAMILIES)),
            alertness=alertness,
            skill_tier=skill_tier,
        )

    def _build_scene(self, rng: random.Random, verb: str) -> SceneState:
        # Stealth/infiltration actions more likely in high-stakes contexts.
        _high_stakes_tiers: tuple[StakesTier, ...] = ("serious", "high", "dire")
        if verb in ("stealth approach", "ranged attack") and rng.random() < 0.65:
            mission_type: MissionType = rng.choice(list(_HIGH_STAKES_MISSIONS))
            stakes_tier: StakesTier = rng.choice(list(_high_stakes_tiers))
            has_countdown = rng.random() < 0.80  # high-stakes usually time-pressured
        else:
            mission_type = rng.choice(list(_MISSION_LANES))
            stakes_tier = rng.choice(list(_STAKES_LANES))
            has_countdown = rng.random() < 0.50

        # Night watch correlates with stealth verbs.
        if verb == "stealth approach" and rng.random() < 0.70:
            watch: Watch = rng.choice(list(_NIGHT_WATCHES))
        else:
            watch = rng.choice(list(_WATCH_LANES))

        return SceneState(
            mission_type=mission_type,
            sponsor_faction="Ridgewatch company",
            objective_phrase="map the enemy camp beyond the ridge before dawn",
            stakes_tier=stakes_tier,
            failure_phrase="the company moves blind into the valley at first light",
            watch=watch,
            countdown_hours=rng.randint(1, 8) if has_countdown else None,
            deadline_label="dawn" if has_countdown else None,
        )

    def _build_party(self, rng: random.Random, verb: str, scene: SceneState) -> PartyState:
        n_members = rng.randint(1, 4)
        members = tuple(
            PartyMember(
                name=rng.choice(["Kira", "Halden", "Mira", "Renn", "Brann"]) + f"_{i}",
                char_class=rng.choice(["scout", "cleric", "quartermaster", "ranger"]),
                initiative_index=i,
            )
            for i in range(n_members)
        )

        # High-stakes missions with countdowns correlate with strained party.
        if scene.stakes_tier in ("high", "dire") and scene.countdown_hours is not None:
            supply: SupplyBucket = (
                rng.choice(list(_STRAINED_SUPPLY)) if rng.random() < 0.65 else "well-supplied"
            )
            fatigue: FatigueBucket = (
                rng.choice(list(_STRAINED_FATIGUE)) if rng.random() < 0.65 else "rested"
            )
        else:
            supply = rng.choice(list(_SUPPLY_LANES))
            fatigue = rng.choice(list(_FATIGUE_LANES))

        return PartyState(
            members=members,
            wound_bucket=rng.choice(list(_WOUND_LANES)),
            supply_bucket=supply,
            fatigue_bucket=fatigue,
        )

    def _build_world(self, rng: random.Random, verb: str, scene: SceneState) -> WorldState:
        # Stealth approach: favour terrain with cover (forest, brush, broken stone).
        _cover_terrain: tuple[TerrainPrimary, ...] = (
            "deep forest",
            "dense brush",
            "broken stone and scree",
        )
        if verb == "stealth approach" and rng.random() < 0.60:
            terrain_primary: TerrainPrimary = rng.choice(list(_cover_terrain))
        else:
            terrain_primary = rng.choice(list(_TERRAIN_LANES))

        wind_choice: Wind = rng.choice(list(_WIND_LANES))
        wind_dir: WindDirection | None = (
            None if wind_choice == "no wind" else rng.choice(list(_WIND_DIRS))
        )
        terrain_mod: TerrainModifier | None = (
            rng.choice(list(_TERRAIN_MODIFIER_LANES)) if rng.random() < 0.3 else None
        )
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
            season=rng.choice(list(_SEASON_LANES)),  # type: ignore[arg-type]
            sky_phrase=rng.choice(["a thin crescent moon", "a full harvest moon", "a clear sky"]),
            temperature=rng.choice(list(_TEMPERATURE_LANES)),
            precipitation=rng.choice(list(_PRECIP_LANES)),
            wind=wind_choice,
            wind_direction=wind_dir,
            terrain_primary=terrain_primary,
            terrain_modifier=terrain_mod,
            faction_stances=stances,
        )


# ---------------------------------------------------------------------------
# MixedPolicy
# ---------------------------------------------------------------------------


class MixedPolicy:
    """Weighted blend of RandomPolicy and ScriptedPolicy.

    Default weights=(0.5, 0.5) gives a 50/50 training mix.  The random
    draw is performed once per ``sample()`` call using the caller's ``rng``
    so the blend is reproducible.

    ``MixedPolicy(weights=(1.0, 0.0))`` is functionally equivalent to
    ``RandomPolicy()`` (same RNG trajectory, since the random weight draw
    consumes one rng.random() call before delegating).
    """

    def __init__(
        self,
        weights: tuple[float, float] = (0.5, 0.5),
    ) -> None:
        if len(weights) != 2:
            raise ValueError("weights must be a 2-tuple (random_weight, scripted_weight)")
        if any(w < 0 for w in weights):
            raise ValueError("weights must be non-negative")
        total = sum(weights)
        if total <= 0:
            raise ValueError("weights must sum to a positive value")
        # Normalise so internal RNG draw is a plain p-threshold.
        self._p_random: float = weights[0] / total
        self._random_policy: RandomPolicy = RandomPolicy()
        self._scripted_policy: ScriptedPolicy = ScriptedPolicy()

    @property
    def name(self) -> str:
        return "mixed"

    def sample(self, rng: random.Random) -> SampledTuple:
        if rng.random() < self._p_random:
            return self._random_policy.sample(rng)
        return self._scripted_policy.sample(rng)
