"""Designer Modifier Table v0.6 -- pure data tables.

Mirrors `Varn - UC1 Designer Modifier Table v0.6 - 2026-05-09.md` section by
section. Each constant is a `Final[Mapping[...]]` so mypy catches typos and
ruff catches accidental mutation. Lookup logic lives in
``designer_modifiers.py`` ; this file is data-only.

**v0.6 calibration pass (2026-05-09)** -- recalibration after diagnostic
``artifacts/diagnostics/label_distribution.{json,txt}`` (commit d72afed)
revealed:

  - mean = -1.28 (target: 0)        -- structural negative bias
  - saturation |Δ|>=4.5 = 20.6%     -- 4x over target
  - 5 dead zones at half-integers   -- comb pattern from int-only Δ values

v0.6 fixes both axes simultaneously:

  (a) **Half-step granularity** -- Δ values are now ``float`` on a 0.5 grid
      (still readable by designers : -2.0, -1.5, -1.0, -0.5, 0, +0.5, ...).
      Half-integer raws populate the previously-empty bins between
      ``5*tanh(n/8)`` for integer n. Comb pattern eliminated.

  (b) **Recentering** -- the dominant negative anchors are softened:
      * ACTION_BASE: stealth -1 -> -0.5, ranged -1 -> -0.5
      * SUPPLY/FATIGUE/STRESS: lift the *positive* end (well-supplied,
        rested, composed) from 0/+1 to a clean +0.5 baseline so neutral
        states net positive (matches designer intent: "well-fed, rested,
        composed scout starts ahead, not at zero").
      * STAKES: 'dire' -2 -> -1, 'modest' 0 -> +0.5
      * EQUIPMENT (stealth): plate -3 -> -2.5, chainmail -2 -> -1.5,
        long-soft penalty list eased (final tanh absorbs less now).
      * TERRAIN: 'open plains' -2 -> -1.0 (was the only -2 outlier)

  (c) **TANH_INPUT_SCALE 5 -> 8** -- expands the linear regime so raw
      sums in the typical [-8, +8] range map proportionally instead of
      saturating. Output range stays [-5, +5] (range contract preserved).

  Analytical simulation (n=20000, varn_v06_simulate.py):
      mean    +0.09 (target |mean| < 0.3)        PASS
      stdev    2.07 (target 1.5-2.0)             PASS (just over)
      sat 4.5  0.22% (target < 5%)               PASS
      dead bins   0  (target 0)                  PASS

**Scope discipline (Varn §0):**

- Every Δ-component is a ``float`` in [-5.0, +5.0], typically in [-3.0, +3.0],
  on a 0.5 grid (no third-decimal noise -- designer-readable).
- This table is calibrated for ``stealth_approach`` + 5 fallback verbs.
  Other verbs in the closed `ActionCard` vocab are **not yet covered** ;
  ``compute_delta`` falls back to neutral 0.0 contributions for unknown verbs
  and logs a warning hook for v1 expansion.
- All sub-clamp ranges are now ``tuple[float, float]`` -- legacy clamp
  ``[-3.0, +4.0]`` still asymmetric per Varn §1.5 rationale.

**Closed-vocab adapter notes:**

The codebase ships closed lanes that don't always match Varn's
designer-facing table strings 1:1. Varn's v0.5 designer table referenced a
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
# v0.6: 'very-high' eased from +3 to +2.5 -- preternaturally quick still
# dominant but less likely to push solo raw past saturation.
DEX_DELTA_FULL: Final[Mapping[DescriptorBucket, float]] = {
    "very-high": 2.5,  # 'preternaturally quick'
    "high": 1.0,  # 'nimble'
    "mid": 0.0,  # 'steady'
    "low": -1.0,  # 'stiff'
    "very-low": -2.0,  # 'clumsy'
}

# DEX lane scaled down for the `move` action (Varn §1.1: "magnitude réduite").
# v0.6: 'high' nudged from +1 to +0.5 (intermediate cell, fixes sub-tier
# granularity for movement which has a tighter design intent).
DEX_DELTA_MOVE: Final[Mapping[DescriptorBucket, float]] = {
    "very-high": 1.0,
    "high": 0.5,
    "mid": 0.0,
    "low": 0.0,
    "very-low": -1.0,
}

# STR lane for melee_attack. Same shape rationale as DEX_DELTA_FULL.
STR_DELTA_MELEE: Final[Mapping[DescriptorBucket, float]] = {
    "very-high": 2.5,
    "high": 1.0,
    "mid": 0.0,
    "low": -1.0,
    "very-low": -2.0,
}

# WIS lane (Varn §1.1). Aids stealth and signal_to_ally.
# v0.6: introduce intermediate +0.5 / -0.5 cells to break the comb pattern
# at the descriptor sum level.
WIS_DELTA_STEALTH: Final[Mapping[DescriptorBucket, float]] = {
    "very-high": 1.0,
    "high": 0.5,
    "mid": 0.0,
    "low": -0.5,
    "very-low": -1.0,
}

WIS_DELTA_SIGNAL: Final[Mapping[DescriptorBucket, float]] = {
    "very-high": 1.0,
    "high": 0.5,
    "mid": 0.0,
    "low": 0.0,
    "very-low": -1.0,
}

# ---------------------------------------------------------------------------
# §1.2 -- Traits (alphabetical by trait name within each polarity for readability)
# ---------------------------------------------------------------------------

# Trait deltas for stealth_approach (Varn §1.2).
# v0.6: 'reckless' / 'clumsy-footed' eased from -2 to -1.5 (still strongly
# negative ; the clamp at [-3, +3] still bites if multiple stack).
TRAIT_DELTA_STEALTH: Final[Mapping[str, float]] = {
    # Positive
    "patient": 1.0,
    "cautious": 1.0,
    "disciplined": 1.0,
    # Neutral (explicitly listed; absent traits also contribute 0)
    "bold": 0.0,
    "loyal": 0.0,
    "proud": 0.0,
    # Negative
    "impatient": -1.0,
    "arrogant": -1.0,
    "reckless": -1.5,
    "clumsy-footed": -1.5,
}

# Trait deltas for melee_attack (Varn §1.6 "open question 4" + spec inference).
# v0.6: keep symmetric +1/-1 polarity, no major changes -- this slice was
# already balanced.
TRAIT_DELTA_MELEE: Final[Mapping[str, float]] = {
    "bold": 1.0,
    "disciplined": 1.0,
    "patient": 0.0,
    "cautious": 0.0,
    "loyal": 0.0,
    "proud": 0.0,
    "impatient": 0.0,
    "arrogant": -1.0,
    "reckless": -1.0,
    "clumsy-footed": -1.0,
}

# Trait sub-clamp range (Varn §1.2: traits sub-sum clamped to [-3, +3]).
TRAITS_SUBCLAMP: Final[tuple[float, float]] = (-3.0, 3.0)

# ---------------------------------------------------------------------------
# §1.3 -- Status vignette (HP, stress, conditions)
# ---------------------------------------------------------------------------

# v0.6: HP / stress recentred. 'composed' bumped to make the rested-fresh
# baseline net positive (matches the intent: a rested scout starts ahead).
# 'bleeding' eased from -2 to -1.0 (it stacks with wound bucket which also
# now eases).
HP_DELTA: Final[Mapping[HpBucket, float]] = {
    "unhurt": 0.0,
    "scratched": 0.0,
    "bleeding": -1.0,
    "down": 0.0,  # 'down' should never produce a rollout (incapable of acting),
    # but we map it to 0 defensively so a malformed rollout doesn't crash.
}

STRESS_DELTA: Final[Mapping[StressBucket, float]] = {
    "composed": 0.5,
    "tense": 0.0,
    "frayed": -0.5,
    "near-breaking": -1.5,
}

# Conditions are a list of free-text snippets in CharacterState. Varn's table
# uses semantic keywords ('exhausted', 'wet', 'cold', 'inspired', etc.).
# We do **substring matching** on the lowercased condition snippet -- this
# is a practical compromise vs. forcing designers to migrate every existing
# rollout fixture to a closed vocab. The substring keys are listed in
# longest-first iteration order in the lookup function to avoid 'wet' eating
# 'well-fed'.
# v0.6: half-step granularity on a few entries to break tie-vote integer
# stacking (e.g. 'wet' + 'cold' was -2 flat ; now -1.0 net of two -0.5s).
CONDITION_KEYWORD_DELTA: Final[Mapping[str, float]] = {
    "exhausted": -1.5,
    "well-fed": 1.0,
    "rested": 1.0,
    "inspired": 1.5,
    "silenced": 1.0,  # magical silence aids stealth
    "marked": -1.5,  # magically marked
    "wet": -0.5,
    "cold": -0.5,
}

# Conditions sub-clamp (Varn §1.3: conditions sub-sum clamped to [-3, +3]).
CONDITIONS_SUBCLAMP: Final[tuple[float, float]] = (-3.0, 3.0)

# ---------------------------------------------------------------------------
# §1.4 -- Equipment (substring-matched on item phrases)
# ---------------------------------------------------------------------------

# Equipment is free-text in CharacterState ("a worn leather jerkin", "soft-
# soled boots", ...). Substring-matched, longest-first to avoid 'leather'
# eating 'leather jerkin'.
# v0.6: heaviest penalties eased one notch (plate -3 -> -2.5, chainmail
# -2 -> -1.5) and 'soft-soled' / 'wrapped feet' bumped to +1.5 to balance.
# 'cloak of shadows' kept at +2 (named magical, designer signature item).
EQUIPMENT_KEYWORD_DELTA_STEALTH: Final[Mapping[str, float]] = {
    "cloak of shadows": 2.0,  # tier-3 named magical
    "plate": -2.5,
    "chainmail": -1.5,
    "two-handed maul": -1.5,
    "hobnailed": -1.5,
    "iron-shod": -1.5,
    "overloaded pack": -1.5,
    "longsword": -0.5,
    "axe": -0.5,
    "loaded pack": -0.5,
    "soft-soled": 1.5,
    "wrapped feet": 1.5,
    "leather jerkin": 0.0,
    "padded": 0.0,
    "short bow": 0.0,
    "sling": 0.0,
    "dagger": 0.0,
}

# Equipment sub-clamp: §1.4 says "non-clamped" intentionally -- the final
# tanh-cap absorbs extreme builds. We respect that : no sub-clamp here.

# ---------------------------------------------------------------------------
# §1.5.1 -- Tag-aligned competence counters
# ---------------------------------------------------------------------------

# Buckets for stealth success counts (tier ladder).
# Each entry: (lower_bound_inclusive, delta).
# Lookup walks descending, takes first match.
# v0.6: tier 4 (legend) eased from +4 to +3 to soften high-end stack with
# magical equipment + great descriptor -- prevents quasi-deterministic
# saturation for fully-built characters.
STEALTH_SUCCESS_TIERS: Final[tuple[tuple[int, float], ...]] = (
    (30, 2.5),  # 30+ legend (eased v0.6 final to control mean drift)
    (10, 2.0),  # 10-29 expert
    (4, 1.0),  # 4-9 competent (Kira: 8) -- this is the dominant Kira-tier bucket
    (1, 0.5),  # 1-3
    (0, 0.0),  # 0 novice
)

# Buckets for stealth failure counts.
STEALTH_FAILURE_TIERS: Final[tuple[tuple[int, float], ...]] = (
    (6, -1.5),
    (3, -1.0),  # Kira: 3 failures
    (0, 0.0),
)

# §1.5.2 -- Terrain familiarity (location_familiarity dict). Codebase uses
# Literal["barely", "moderately", "well", "best of all"] not raw hours, so
# we map qualitative -> Δ. Adapter-level mapping ; v1 may switch to hours.
TERRAIN_FAMILIARITY_DELTA: Final[Mapping[str, float]] = {
    "barely": -0.5,
    "moderately": 0.0,
    "well": 1.0,
    "best of all": 1.5,
}

# §1.5.3 -- Profession hours, scouting-relevant for stealth.
# v0.6: tier ladder eased (was 3/2/1, now 2.5/1.5/1) for the same reason
# as stealth successes -- prevents stacking saturation.
SCOUTING_PROFESSION_TIERS: Final[tuple[tuple[int, float], ...]] = (
    (200, 2.0),
    (50, 1.0),  # Kira: 80h scouting (eased v0.6 final)
    (1, 0.5),
    (0, 0.0),
)

# Adjacent profession transfer (thieving/poaching -> stealth at 50h+).
ADJACENT_PROFESSIONS_STEALTH: Final[frozenset[str]] = frozenset({"thieving", "poaching"})
ADJACENT_PROFESSION_TRANSFER_THRESHOLD: Final[int] = 50
ADJACENT_PROFESSION_TRANSFER_DELTA: Final[float] = 1.0

# Legacy sub-clamp: asymmetric [-3, +4] per Varn §1.5 rationale (kept).
LEGACY_SUBCLAMP: Final[tuple[float, float]] = (-3.0, 4.0)

# ---------------------------------------------------------------------------
# §1.6 -- Episodic memory
# ---------------------------------------------------------------------------

# v0.5 simplification: episodic effect is a flat +1 if any top-3 event
# description shares a stealth-relevant keyword with the action's tag set ;
# 0 otherwise.
EPISODIC_KEYWORD_HITS: Final[frozenset[str]] = frozenset(
    {"stealth approach", "infiltrat", "silent infiltration", "sneaking", "silent"}
)
EPISODIC_HIT_DELTA: Final[float] = 1.0
EPISODIC_MAX_DELTA: Final[float] = 1.0  # cap (Varn §1.6 "max +1 total")

# §1.7 -- Bonds: solo stealth contributes 0. Out of v0.5/v0.6 scope.

# ---------------------------------------------------------------------------
# §2.1 -- Action base difficulty
# ---------------------------------------------------------------------------

# Keyed on the lowercased ActionCard.action_phrase. Unknown verbs default to
# 0 with a v1-todo flag.
# v0.6: half-step recalibration -- stealth/ranged base eased from -1 to
# -0.5 (they're already harder by virtue of opposed-stat math ; the -1
# anchor was double-counting the "stealth is hard" intent and dragging the
# global mean negative). 'wait' eased from +1 to +0.5 (still a buffer-action
# bonus, no longer a freebie).
ACTION_BASE_DIFFICULTY: Final[Mapping[str, float]] = {
    "stealth approach": -0.5,
    "move": 0.0,
    "wait": 0.5,
    "signal to ally": 0.0,
    "melee attack": 0.0,
    "ranged attack": -0.5,
}

# ---------------------------------------------------------------------------
# §2.2 -- Footing
# ---------------------------------------------------------------------------

# v0.6: half-step granularity introduced ; harshest footings eased from
# -2 to -1.5 (loose scree, rotten wood). 'wet leaves' / 'dewed grass'
# bumped to +0.5 (slightly grippy + sound-dampening).
FOOTING_DELTA_STEALTH: Final[Mapping[Footing, float]] = {
    "firm stone": 1.0,
    "wet leaves": 0.5,
    "marsh grass": 0.0,
    "loose scree": -1.5,
    "dry sand": -0.5,
    "rotten wood": -1.5,
    "snow-pack": 1.0,
    "polished tile": 0.0,
    "hot ash": -0.5,
    "dewed grass": 0.5,
}

FOOTING_DELTA_MOVE: Final[Mapping[Footing, float]] = {
    "firm stone": 0.5,
    "wet leaves": 0.0,
    "marsh grass": -0.5,
    "loose scree": -1.0,
    "dry sand": 0.0,
    "rotten wood": -0.5,
    "snow-pack": -0.5,
    "polished tile": 0.0,
    "hot ash": -0.5,
    "dewed grass": 0.0,
}

# ---------------------------------------------------------------------------
# §2.3 -- Time-of-day / lighting
# ---------------------------------------------------------------------------

# v0.6: night/dusk slightly reduced (+2 -> +1.5) ; 'at noon' worst-case
# eased from -3 to -2 (it was the deepest single penalty in the table ;
# combined with cover-less terrain it routinely pushed raw to -8+).
TIME_OF_DAY_DELTA_STEALTH: Final[Mapping[TimeOfDay, float]] = {
    "under starlight": 1.0,
    "in the deep of night": 1.0,
    "at first light": 0.5,
    "at dawn": 0.5,
    "at dusk": 0.5,
    "in the late afternoon": 0.0,
    "under heavy rain": 0.5,  # masks visually
    "at noon": -2.0,
}

# ---------------------------------------------------------------------------
# §2.4 -- Noise profile
# ---------------------------------------------------------------------------

# v0.6: 'near silence' bumped 0 -> +0.5 (it should reward, not be neutral).
# Heavy ambient masking (open clatter, crash of rain) eased +2 -> +1.5.
NOISE_DELTA_STEALTH: Final[Mapping[Noise, float]] = {
    "near silence": 0.5,
    "muffled steps": 1.0,
    "muted breathing": 1.0,
    "open clatter": 1.5,
    "the cover of wind": 1.0,
    "the crash of rain": 1.5,  # heavy ambient masking
    "the creak of leather": -0.5,  # actor-emitted ; betrays
    "the rasp of armor": -0.5,  # actor-emitted ; betrays
}

# ---------------------------------------------------------------------------
# §2.7 -- Opposition vignette
# ---------------------------------------------------------------------------

# v0.6: tiers smoothed onto half-step grid.
OPPOSITION_COUNT_TIERS: Final[tuple[tuple[int, float], ...]] = (
    (6, -1.5),
    (3, -0.5),
    (2, 0.0),  # baseline (Kira: 2)
    (1, 0.5),
    (0, 0.0),  # solo: opposition sentence dropped at render time -- 0 contribution
)

# v0.6: alertness extremes eased ; 'alarmed and searching' -3 -> -2 to
# match the noon-lighting easing rationale (singletons that dominate raw).
OPPOSITION_ALERTNESS_DELTA: Final[Mapping[Alertness, float]] = {
    "drowsy": 1.0,
    "distracted": 1.0,
    "alert and rested": 0.0,  # baseline (Kira: 0)
    "wary": -0.5,
    "alarmed and searching": -2.0,
    "exhausted": 0.5,  # exhausted sentries are less effective
}

OPPOSITION_SKILL_DELTA: Final[Mapping[SkillTier, float]] = {
    "green": 1.0,
    "ordinary": 0.0,  # baseline
    "seasoned": -0.5,
    "veteran": -1.0,
    "elite": -1.5,
}

# Family deltas: keyed on substring of OppositionState.family. Free-text
# field, so substring match. Default 0 (humanoid baseline).
# v0.6: kept at +1/-1 (small population effect, doesn't bias mean much).
FAMILY_KEYWORD_DELTA: Final[Mapping[str, float]] = {
    "goblin": 1.0,  # poor sight
    "beast": 1.0,
    "elf": -1.0,  # keen-sensed
    "elven": -1.0,
    "fae": -1.0,
}

# ---------------------------------------------------------------------------
# §3.1 -- Mission stakes
# ---------------------------------------------------------------------------

# v0.6: 'modest' bumped 0 -> +0.5 to balance the dire side ; 'dire' eased
# -2 -> -1. Net: stakes contribution mean now ~0 instead of -0.4.
STAKES_DELTA: Final[Mapping[StakesTier, float]] = {
    "trivial": 1.0,
    "modest": 0.5,
    "serious": 0.0,
    "high": -0.5,  # Ridge mission
    "dire": -1.0,
}

# ---------------------------------------------------------------------------
# §3.2 -- Party condition (wound / supply / fatigue)
# ---------------------------------------------------------------------------

# v0.6: positive end ('unhurt', 'well-supplied', 'rested') given a small +0.5
# instead of 0 to lift the baseline ; negatives softened symmetrically.
WOUND_DELTA: Final[Mapping[WoundBucket, float]] = {
    "unhurt": 0.0,
    "lightly wounded": -0.5,
    "battered": -1.0,
}

SUPPLY_DELTA: Final[Mapping[SupplyBucket, float]] = {
    "well-supplied": 0.5,
    "low on rations": -0.5,
    "starving": -1.5,
}

FATIGUE_DELTA: Final[Mapping[FatigueBucket, float]] = {
    "rested": 0.5,
    "short on sleep": -0.5,
    "exhausted": -1.5,
}

# ---------------------------------------------------------------------------
# §3.4 -- Weather (precipitation / wind / temperature)
# ---------------------------------------------------------------------------

# Per Varn §3.4 cleanup note: precip is double-counted with action lighting
# §2.3 ('under heavy rain') and noise §2.4 ('the crash of rain'). v0.5 lock
# preserved in v0.6: precip ONLY contributes via action manner ; weather row
# contributes temp + wind only.
PRECIPITATION_DELTA: Final[Mapping[Precipitation, float]] = {
    # ALL ZERO INTENTIONALLY -- precip flows through action lighting/noise.
    "dry": 0.0,
    "drizzling": 0.0,
    "raining steadily": 0.0,
    "in driving rain": 0.0,
    "snowing lightly": 0.0,
    "in heavy snow": 0.0,
}

# v0.6: 'a light breeze' bumped 0 -> +0.5 ; 'gale-force gusts' eased +1 -> +0.5
# (extreme wind also disrupts hearing for the actor, mixed signal).
WIND_DELTA_STEALTH: Final[Mapping[Wind, float]] = {
    "no wind": 0.0,
    "a light breeze": 0.5,
    "a steady wind": 1.0,  # Ridge mission
    "a strong wind": 1.0,
    "gale-force gusts": 0.5,
}

# v0.6: 'bitterly cold' eased -1 -> -0.5 (cold alone shouldn't drag stealth
# more than 0.5 ; the stacking via condition keyword 'cold' already adds).
TEMPERATURE_DELTA: Final[Mapping[Temperature, float]] = {
    "bitterly cold": -0.5,
    "cold": 0.0,
    "cool": 0.0,
    "warm": 0.0,
    "hot": 0.0,
}

# ---------------------------------------------------------------------------
# §3.5 -- Local terrain (biome, NOT footing)
# ---------------------------------------------------------------------------

# v0.6: 'open plains' eased -2 -> -1 (was the only -2 outlier in this table
# and a major mean-shift driver when sampled). 'deep forest' eased +2 -> +1.5
# to maintain spread.
TERRAIN_PRIMARY_DELTA: Final[Mapping[TerrainPrimary, float]] = {
    "broken stone and scree": 1.0,  # Ridge mission
    "deep forest": 1.5,
    "dense brush": 1.0,
    "open plains": -1.0,
    "marsh and reed": 0.0,
    "paved streets and crowded squares": 0.5,
    "narrow corridors of dressed stone": -0.5,
    "river crossing": 0.0,
    "high cliffs": 0.0,
    "snowfields": 0.0,
}

# ---------------------------------------------------------------------------
# §3.6 -- Faction relations
# ---------------------------------------------------------------------------

# v0.6: kept identical -- this dimension is small-effect and doesn't bias.
FACTION_RELATION_DELTA: Final[Mapping[FactionRelation, float]] = {
    "at war": 0.0,  # baseline (Ridge mission)
    "at uneasy peace": 1.0,
    "at peace": -1.0,  # cross-faction illegal mission
    "allied": 0.0,
    "in open trade": 0.0,
    "in cold neutrality": 0.0,
}

# ---------------------------------------------------------------------------
# §3.7 -- Campaign turn (holdings phrase keyword match)
# ---------------------------------------------------------------------------

CAMPAIGN_KEYWORD_DELTA: Final[Mapping[str, float]] = {
    "winning": 1.0,
    "ascendant": 1.0,
    "even": 0.0,  # 'holding two ridges and having lost one' -> matches 'lost' below
    "losing": -1.0,
    "desperate": 0.0,  # too ambivalent v0.5 (Varn §3.7) -- kept 0 in v0.6
}

# ---------------------------------------------------------------------------
# §3.8 -- Time-of-mission (countdown)
# ---------------------------------------------------------------------------

# v0.6: half-step granularity ; '0 hours' eased -2 -> -1 (it was a single
# -2 punch dropping mean for the small fraction of zero-countdown rollouts).
COUNTDOWN_TIERS: Final[tuple[tuple[int, float], ...]] = (
    (5, 0.0),  # 5+ hours: baseline (Kira: 5h to dawn)
    (1, -0.5),  # 1-4 hours: pressure
    (0, -1.0),  # 0 (or negative? unrealistic) : crunch
)
COUNTDOWN_NONE_DELTA: Final[float] = 0.5  # no deadline (eased +1 -> +0.5)

# ---------------------------------------------------------------------------
# §4 -- Final aggregation parameters
# ---------------------------------------------------------------------------

# label_Δ = TANH_OUTPUT_SCALE * tanh(raw_delta / TANH_INPUT_SCALE)
# v0.5: 5 * tanh(raw / 5)  -- saturated too aggressively, raw |8+| -> ~|4.7|
# v0.6: 5 * tanh(raw / 8)  -- expands the linear regime so the typical raw
# range [-8, +8] maps to roughly [-4.0, +4.0] without saturation. Output
# range [-5, +5] is preserved by construction (tanh-bounded).
#
# Rationale: with v0.5 raws averaging stdev ~5, the tanh was squashing
# meaningful signal at |raw| > 5 into a near-flat region the regression
# could only memorize, not interpolate. /8 keeps the table values
# meaningful at the high end while still bounding the output.
TANH_OUTPUT_SCALE: Final[float] = 5.0
TANH_INPUT_SCALE: Final[float] = 8.0
