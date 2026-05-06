"""Designer Modifier Table v0.5 -- pure data tables.

Mirrors `Varn - UC1 Designer Modifier Table v0.5 - 2026-05-05.md` section by
section. Each constant is a `Final[Mapping[...]]` so mypy catches typos and
ruff catches accidental mutation. Lookup logic lives in
``designer_modifiers.py`` ; this file is data-only.

**Scope discipline (Varn §0):**

- Every Δ-component is an int in [-5, +5], typically in [-3, +3].
- This table is calibrated for ``stealth_approach`` + 5 fallback verbs.
  Other verbs in the closed `ActionCard` vocab are **not yet covered** ;
  ``compute_delta`` falls back to neutral 0 contributions for unknown verbs
  and logs a warning hook for v1 expansion.

**Closed-vocab adapter notes:**

The codebase ships closed lanes that don't always match Varn's
designer-facing table strings 1:1. Varn's v0.5 designer table references a
6-cell DEX string (including "wiry" -- retired by UC1 Descriptor Lanes Lock
2026-05-06); the locked vocab is 5-bucket only. I map by **bucket *level***
(ordinal position) rather than by string, which keeps the schema authoritative
and the table interpretable. Where Varn lists strings the vocab does not
contain (e.g. "in fog" lighting, "ice" footing), the entry is omitted with a
code comment and v1 either extends the vocab or drops the rule.
"""

from __future__ import annotations

from collections.abc import Mapping
from typing import Final

from wayfinders.ml.schemas.vocabularies import (
    Alertness,
    DescriptorBucket,
    FactionRelation,
    FatigueBucket,
    Footing,
    HpBucket,
    Noise,
    Precipitation,
    SkillTier,
    StakesTier,
    StressBucket,
    SupplyBucket,
    Temperature,
    TerrainPrimary,
    TimeOfDay,
    Wind,
    WoundBucket,
)

# ---------------------------------------------------------------------------
# §1.1 -- Descriptor lanes (DEX / WIS / STR ; STR rendered for melee fallback)
# ---------------------------------------------------------------------------

# DEX lane mapped to stealth_approach / ranged_attack (full magnitude).
# Locked vocab (2026-05-06): clumsy / stiff / steady / nimble / preternaturally quick.
# Varn's v0.5 designer table used a 6-cell DEX string ("wiry/agile" etc.) -- we
# align by bucket *ordinal* (not string), collapse Varn's top two cells onto
# 'very-high' at +3, and map 'nimble' (DEX high) to +1.
DEX_DELTA_FULL: Final[Mapping[DescriptorBucket, int]] = {
    "very-high": 3,  # 'preternaturally quick'
    "high": 1,  # 'nimble' -> Varn's +1 cell (Kira lands here in the table example)
    "mid": 0,  # 'steady' -> Varn 'steady' baseline
    "low": -1,  # 'stiff' -> Varn 'slow-handed'
    "very-low": -2,  # 'clumsy' -> Varn 'clumsy'
}

# DEX lane scaled down for the `move` action (Varn §1.1: "magnitude réduite").
DEX_DELTA_MOVE: Final[Mapping[DescriptorBucket, int]] = {
    "very-high": 1,
    "high": 1,
    "mid": 0,
    "low": 0,
    "very-low": -1,
}

# STR lane for melee_attack. Same 5-to-6 collapse as DEX_DELTA_FULL --
# 'mighty' (very-high) absorbs Varn's '+3'/'+2' top cells, 'strong-armed'
# (high) maps to '+1', 'sturdy' (mid) to '0', etc.
STR_DELTA_MELEE: Final[Mapping[DescriptorBucket, int]] = {
    "very-high": 3,
    "high": 1,
    "mid": 0,
    "low": -1,
    "very-low": -2,
}

# WIS lane (Varn §1.1). Aids stealth and signal_to_ally.
# Varn 3-cell ('shrewd/keen-eyed' +1, 'attentive' 0, 'inattentive/dreamy' -1)
# mapped to 5 buckets -> top-2 buckets give +1, mid is 0, bottom-2 give -1.
WIS_DELTA_STEALTH: Final[Mapping[DescriptorBucket, int]] = {
    "very-high": 1,
    "high": 1,
    "mid": 0,
    "low": -1,
    "very-low": -1,
}

WIS_DELTA_SIGNAL: Final[Mapping[DescriptorBucket, int]] = {
    "very-high": 1,
    "high": 1,
    "mid": 0,
    "low": 0,
    "very-low": -1,
}

# ---------------------------------------------------------------------------
# §1.2 -- Traits (alphabetical by trait name within each polarity for readability)
# ---------------------------------------------------------------------------

# Trait deltas for stealth_approach (Varn §1.2).
TRAIT_DELTA_STEALTH: Final[Mapping[str, int]] = {
    # Positive
    "patient": 1,
    "cautious": 1,
    "disciplined": 1,
    # Neutral (explicitly listed; absent traits also contribute 0)
    "bold": 0,
    "loyal": 0,
    "proud": 0,
    # Negative
    "impatient": -1,
    "arrogant": -1,
    "reckless": -2,
    "clumsy-footed": -2,
}

# Trait deltas for melee_attack (Varn §1.6 "open question 4" + spec inference).
# v0.5: bold/disciplined favored; reckless still penalized (different reason
# than stealth -- in melee, reckless costs control). Documented for v1 review.
TRAIT_DELTA_MELEE: Final[Mapping[str, int]] = {
    "bold": 1,
    "disciplined": 1,
    "patient": 0,
    "cautious": 0,
    "loyal": 0,
    "proud": 0,
    "impatient": 0,
    "arrogant": -1,
    "reckless": -1,
    "clumsy-footed": -1,
}

# Trait sub-clamp range (Varn §1.2: traits sub-sum clamped to [-3, +3]).
TRAITS_SUBCLAMP: Final[tuple[int, int]] = (-3, 3)

# ---------------------------------------------------------------------------
# §1.3 -- Status vignette (HP, stress, conditions)
# ---------------------------------------------------------------------------

HP_DELTA: Final[Mapping[HpBucket, int]] = {
    "unhurt": 0,
    "scratched": 0,
    "bleeding": -2,
    "down": 0,  # 'down' should never produce a rollout (incapable of acting),
    # but we map it to 0 defensively so a malformed rollout doesn't crash.
}

STRESS_DELTA: Final[Mapping[StressBucket, int]] = {
    "composed": 1,
    "tense": 0,
    "frayed": -1,
    "near-breaking": -3,
}

# Conditions are a list of free-text snippets in CharacterState. Varn's table
# uses semantic keywords ('exhausted', 'wet', 'cold', 'inspired', etc.).
# We do **substring matching** on the lowercased condition snippet -- this
# is a practical compromise vs. forcing designers to migrate every existing
# rollout fixture to a closed vocab. The substring keys are listed in
# longest-first iteration order in the lookup function to avoid 'wet' eating
# 'well-fed'.
CONDITION_KEYWORD_DELTA: Final[Mapping[str, int]] = {
    "exhausted": -2,
    "well-fed": 1,
    "rested": 1,
    "inspired": 2,
    "silenced": 1,  # magical silence aids stealth
    "marked": -2,  # magically marked
    "wet": -1,
    "cold": -1,
}

# Conditions sub-clamp (Varn §1.3: conditions sub-sum clamped to [-3, +3]).
CONDITIONS_SUBCLAMP: Final[tuple[int, int]] = (-3, 3)

# ---------------------------------------------------------------------------
# §1.4 -- Equipment (substring-matched on item phrases)
# ---------------------------------------------------------------------------

# Equipment is free-text in CharacterState ("a worn leather jerkin", "soft-
# soled boots", ...). Substring-matched, longest-first to avoid 'leather'
# eating 'leather jerkin'.
EQUIPMENT_KEYWORD_DELTA_STEALTH: Final[Mapping[str, int]] = {
    "cloak of shadows": 2,  # tier-3 named magical
    "plate": -3,
    "chainmail": -2,
    "two-handed maul": -2,
    "hobnailed": -2,
    "iron-shod": -2,
    "overloaded pack": -2,
    "longsword": -1,
    "axe": -1,
    "loaded pack": -1,
    "soft-soled": 1,
    "wrapped feet": 1,
    "leather jerkin": 0,
    "padded": 0,
    "short bow": 0,
    "sling": 0,
    "dagger": 0,
}

# Equipment sub-clamp: §1.4 says "non-clamped" intentionally -- the final
# tanh-cap absorbs extreme builds. We respect that : no sub-clamp here.

# ---------------------------------------------------------------------------
# §1.5.1 -- Tag-aligned competence counters
# ---------------------------------------------------------------------------

# Buckets for stealth success counts (tier ladder).
# Each entry: (lower_bound_inclusive, delta).
# Lookup walks descending, takes first match.
STEALTH_SUCCESS_TIERS: Final[tuple[tuple[int, int], ...]] = (
    (30, 4),  # 30+ legend
    (10, 3),  # 10-29 expert
    (4, 2),  # 4-9 competent (Kira: 8)
    (1, 1),  # 1-3
    (0, 0),  # 0 novice
)

# Buckets for stealth failure counts.
STEALTH_FAILURE_TIERS: Final[tuple[tuple[int, int], ...]] = (
    (6, -2),
    (3, -1),  # Kira: 3 failures
    (0, 0),
)

# §1.5.2 -- Terrain familiarity (location_familiarity dict). Codebase uses
# Literal["barely", "moderately", "well", "best of all"] not raw hours, so
# we map qualitative -> Δ. Adapter-level mapping ; v1 may switch to hours.
TERRAIN_FAMILIARITY_DELTA: Final[Mapping[str, int]] = {
    "barely": -1,
    "moderately": 0,
    "well": 1,
    "best of all": 2,
}

# §1.5.3 -- Profession hours, scouting-relevant for stealth.
SCOUTING_PROFESSION_TIERS: Final[tuple[tuple[int, int], ...]] = (
    (200, 3),
    (50, 2),  # Kira: 80h scouting
    (1, 1),
    (0, 0),
)

# Adjacent profession transfer (thieving/poaching -> stealth at 50h+).
ADJACENT_PROFESSIONS_STEALTH: Final[frozenset[str]] = frozenset({"thieving", "poaching"})
ADJACENT_PROFESSION_TRANSFER_THRESHOLD: Final[int] = 50
ADJACENT_PROFESSION_TRANSFER_DELTA: Final[int] = 1

# Legacy sub-clamp: asymmetric [-3, +4] per Varn §1.5 rationale.
LEGACY_SUBCLAMP: Final[tuple[int, int]] = (-3, 4)

# ---------------------------------------------------------------------------
# §1.6 -- Episodic memory
# ---------------------------------------------------------------------------

# v0.5 simplification: episodic effect is a flat +1 if any top-3 event
# description shares a stealth-relevant keyword with the action's tag set ;
# 0 otherwise. Trauma flag and legendary flag are not in the EpisodicEvent
# schema yet -- v1 adds them.
# Multi-character substrings, chosen to be specific enough to avoid generic
# false-hits like 'goblin scout' triggering on a bare 'scout'. Keywords reflect
# stealth-domain *acts* (verbs/qualities), not enemy descriptors.
EPISODIC_KEYWORD_HITS: Final[frozenset[str]] = frozenset(
    {"stealth approach", "infiltrat", "silent infiltration", "sneaking", "silent"}
)
EPISODIC_HIT_DELTA: Final[int] = 1
EPISODIC_MAX_DELTA: Final[int] = 1  # cap (Varn §1.6 "max +1 total")

# §1.7 -- Bonds: solo stealth contributes 0. Out of v0.5 scope.

# ---------------------------------------------------------------------------
# §2.1 -- Action base difficulty
# ---------------------------------------------------------------------------

# Keyed on the lowercased ActionCard.action_phrase. Unknown verbs default to
# 0 with a v1-todo flag (compute_delta logs the unknown phrase to a
# module-level set, see designer_modifiers.UNKNOWN_VERBS).
ACTION_BASE_DIFFICULTY: Final[Mapping[str, int]] = {
    "stealth approach": -1,
    "move": 0,
    "wait": 1,
    "signal to ally": 0,
    "melee attack": 0,
    "ranged attack": -1,
}

# ---------------------------------------------------------------------------
# §2.2 -- Footing
# ---------------------------------------------------------------------------

FOOTING_DELTA_STEALTH: Final[Mapping[Footing, int]] = {
    "firm stone": 1,
    "wet leaves": 0,
    "marsh grass": 0,  # codebase substitute for Varn 'dry grass'
    "loose scree": -2,
    "dry sand": -1,  # codebase substitute for 'gravel path'
    "rotten wood": -2,  # codebase substitute for 'dry twigs' (cracking sound)
    "snow-pack": 1,  # 'snow' in Varn -- masks but tracks ; net +1 per Varn
    "polished tile": 0,  # codebase substitute for 'paved street'
    "hot ash": -1,  # codebase substitute for 'gravel path' analogue
    "dewed grass": 0,
}

FOOTING_DELTA_MOVE: Final[Mapping[Footing, int]] = {
    "firm stone": 0,
    "wet leaves": 0,
    "marsh grass": -1,
    "loose scree": -1,
    "dry sand": 0,
    "rotten wood": 0,
    "snow-pack": -1,
    "polished tile": 0,
    "hot ash": -1,
    "dewed grass": 0,
}

# ---------------------------------------------------------------------------
# §2.3 -- Time-of-day / lighting
# ---------------------------------------------------------------------------

TIME_OF_DAY_DELTA_STEALTH: Final[Mapping[TimeOfDay, int]] = {
    "under starlight": 2,
    "in the deep of night": 2,  # codebase has this ; treat as starlight tier
    "at first light": 1,
    "at dawn": 1,
    "at dusk": 1,
    "in the late afternoon": 0,
    "under heavy rain": 1,  # masks visually
    "at noon": -3,
}

# ---------------------------------------------------------------------------
# §2.4 -- Noise profile
# ---------------------------------------------------------------------------

NOISE_DELTA_STEALTH: Final[Mapping[Noise, int]] = {
    "near silence": 0,
    "muffled steps": 1,
    "muted breathing": 1,
    "open clatter": 2,
    "the cover of wind": 1,
    "the crash of rain": 2,  # heavy ambient masking
    "the creak of leather": -1,  # actor-emitted ; betrays
    "the rasp of armor": -1,  # actor-emitted ; betrays
}

# ---------------------------------------------------------------------------
# §2.7 -- Opposition vignette
# ---------------------------------------------------------------------------

# Count words ("one", "two", "three", "five"...) -> Δ. We get the count as
# an int from OppositionState.count, so we tier on the int directly.
OPPOSITION_COUNT_TIERS: Final[tuple[tuple[int, int], ...]] = (
    (6, -2),
    (3, -1),
    (2, 0),  # baseline (Kira: 2)
    (1, 1),
    (0, 0),  # solo: opposition sentence dropped at render time -- 0 contribution
)

OPPOSITION_ALERTNESS_DELTA: Final[Mapping[Alertness, int]] = {
    "drowsy": 2,
    "distracted": 2,
    "alert and rested": 0,  # baseline (Kira: 0)
    "wary": -1,
    "alarmed and searching": -3,
    "exhausted": 1,  # exhausted sentries are less effective
}

OPPOSITION_SKILL_DELTA: Final[Mapping[SkillTier, int]] = {
    "green": 1,
    "ordinary": 0,  # baseline
    "seasoned": -1,
    "veteran": -1,
    "elite": -2,
}

# Family deltas: keyed on substring of OppositionState.family. Free-text
# field, so substring match. Default 0 (humanoid baseline).
FAMILY_KEYWORD_DELTA: Final[Mapping[str, int]] = {
    "goblin": 1,  # poor sight
    "beast": 1,
    "elf": -1,  # keen-sensed
    "elven": -1,
    "fae": -1,
}

# ---------------------------------------------------------------------------
# §3.1 -- Mission stakes
# ---------------------------------------------------------------------------

STAKES_DELTA: Final[Mapping[StakesTier, int]] = {
    "trivial": 1,
    "modest": 0,
    "serious": 0,
    "high": -1,  # Ridge mission
    "dire": -2,
}

# ---------------------------------------------------------------------------
# §3.2 -- Party condition (wound / supply / fatigue)
# ---------------------------------------------------------------------------

WOUND_DELTA: Final[Mapping[WoundBucket, int]] = {
    "unhurt": 0,
    "lightly wounded": 0,
    "wounded": -1,
    "battered": -2,
}

SUPPLY_DELTA: Final[Mapping[SupplyBucket, int]] = {
    "well-supplied": 0,
    "adequately supplied": 0,
    "low on rations": -1,
    "starving": -2,
}

FATIGUE_DELTA: Final[Mapping[FatigueBucket, int]] = {
    "rested": 1,
    "short on sleep": -1,
    "exhausted": -2,
    "spent": -2,
}

# ---------------------------------------------------------------------------
# §3.4 -- Weather (precipitation / wind / temperature)
# ---------------------------------------------------------------------------

# Per Varn §3.4 cleanup note: precip is double-counted with action lighting
# §2.3 ('under heavy rain') and noise §2.4 ('the crash of rain'). v0.5 lock:
# precip ONLY contributes via action manner ; weather row contributes
# temp + wind only.
PRECIPITATION_DELTA: Final[Mapping[Precipitation, int]] = {
    # ALL ZERO INTENTIONALLY -- precip flows through action lighting/noise.
    "dry": 0,
    "drizzling": 0,
    "raining steadily": 0,
    "in driving rain": 0,
    "snowing lightly": 0,
    "in heavy snow": 0,
}

WIND_DELTA_STEALTH: Final[Mapping[Wind, int]] = {
    "no wind": 0,
    "a light breeze": 0,
    "a steady wind": 1,  # Ridge mission
    "a strong wind": 1,
    "gale-force gusts": 1,
}

TEMPERATURE_DELTA: Final[Mapping[Temperature, int]] = {
    "bitterly cold": -1,
    "cold": 0,
    "cool": 0,
    "warm": 0,
    "hot": 0,
}

# ---------------------------------------------------------------------------
# §3.5 -- Local terrain (biome, NOT footing)
# ---------------------------------------------------------------------------

TERRAIN_PRIMARY_DELTA: Final[Mapping[TerrainPrimary, int]] = {
    "broken stone and scree": 1,  # Ridge mission
    "deep forest": 2,
    "dense brush": 1,
    "open plains": -2,
    "marsh and reed": 0,
    "paved streets and crowded squares": 1,
    "narrow corridors of dressed stone": -1,
    "river crossing": 0,
    "high cliffs": 0,
    "snowfields": 0,
}

# ---------------------------------------------------------------------------
# §3.6 -- Faction relations
# ---------------------------------------------------------------------------

FACTION_RELATION_DELTA: Final[Mapping[FactionRelation, int]] = {
    "at war": 0,  # baseline (Ridge mission)
    "at uneasy peace": 1,
    "at peace": -1,  # cross-faction illegal mission
    "allied": 0,
    "in open trade": 0,
    "in cold neutrality": 0,
}

# ---------------------------------------------------------------------------
# §3.7 -- Campaign turn (holdings phrase keyword match)
# ---------------------------------------------------------------------------

# CampaignState.holdings_phrase is free-text; we keyword-match.
CAMPAIGN_KEYWORD_DELTA: Final[Mapping[str, int]] = {
    "winning": 1,
    "ascendant": 1,
    "even": 0,  # 'holding two ridges and having lost one' -> matches 'lost' below
    "losing": -1,
    "desperate": 0,  # too ambivalent v0.5 (Varn §3.7)
}

# ---------------------------------------------------------------------------
# §3.8 -- Time-of-mission (countdown)
# ---------------------------------------------------------------------------

# countdown_hours is an Optional[int] on SceneState. None means 'no deadline'.
# Tiers walk descending, take first match.
COUNTDOWN_TIERS: Final[tuple[tuple[int, int], ...]] = (
    (5, 0),  # 5+ hours: baseline (Kira: 5h to dawn)
    (1, -1),  # 1-4 hours: pressure
    (0, -2),  # 0 (or negative? unrealistic) : crunch
)
COUNTDOWN_NONE_DELTA: Final[int] = 1  # no deadline

# ---------------------------------------------------------------------------
# §4 -- Final aggregation parameters
# ---------------------------------------------------------------------------

# label_Δ = TANH_OUTPUT_SCALE * tanh(raw_delta / TANH_INPUT_SCALE)
# Varn §4.2 lock: 5 * tanh(raw / 5).
TANH_OUTPUT_SCALE: Final[float] = 5.0
TANH_INPUT_SCALE: Final[float] = 5.0
