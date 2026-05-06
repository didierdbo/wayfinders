"""Closed lookup tables for the three renderers.

Every adjective, lane label, and verbal mapping the renderers emit lives
here. Adding a new word is a design change (Varn-approved), not a content
change -- that's the EN-pinned, closed-vocab discipline.

All tables are `Final[Mapping[...]]` so mypy catches accidental mutation.

# Conscious cross-lane duplications (acted by Varn)
#
# The following surface tokens appear in two lanes by design. They are NOT
# bugs or oversights. Do NOT "clean" these without a Varn design review.
#
# - "scree":  Action.Footing ("loose scree") ↔ Context.TerrainPrimary
#             ("broken stone and scree")              (acted 2026-05-01)
# - "rested": Action.Alertness ("alert and rested") ↔ Context.FatigueBucket
#             ("rested")                              (acted 2026-05-06)
# - "marsh":  Action.Footing ("marsh grass") ↔ Context.TerrainPrimary
#             ("marsh and reed")                      (acted 2026-05-06)
#
# Justification (Varn 2026-05-06 §7 Gap B + C): sentence templates and
# syntactic contexts differ strongly enough that the MiniLM encoder keeps
# the embeddings apart (n-gram window is the discriminator). Pattern matches
# the acted "scree" duplication from the Character lock.
"""

from __future__ import annotations

from collections.abc import Mapping
from typing import Final, Literal

# ---------------------------------------------------------------------------
# Character schema vocabularies (Varn - UC1 Descriptor Lanes Lock 2026-05-06)
# ---------------------------------------------------------------------------

# Descriptor lanes -- STR / DEX / WIS rendered as adjective from a 5-bucket
# bucketing of the underlying score. Buckets are: very-low / low / mid /
# high / very-high. Lookup is keyed by bucket label.
#
# Rule (locked 2026-05-06): one adjective per stat, fixed order STR/DEX/WIS,
# no cross-lane vocabulary overlap. Every word belongs to exactly one stat.
# See lock doc sec. 2 for rationale (encoder auditability + no redundant signal).
type DescriptorBucket = Literal["very-low", "low", "mid", "high", "very-high"]

STR_LANES: Final[Mapping[DescriptorBucket, str]] = {
    "very-low": "frail",
    "low": "slender",  # replaces "slight" (leaked DEX signal to encoder)
    "mid": "sturdy",
    "high": "strong-armed",
    "very-high": "mighty",  # replaces "iron-thewed" (archaic; breaks Pratchett tonal DNA)
}

DEX_LANES: Final[Mapping[DescriptorBucket, str]] = {
    "very-low": "clumsy",  # replaces "ponderous" (read as STR-shaped, not DEX)
    "low": "stiff",  # replaces "deliberate" (temperament/WIS word)
    "mid": "steady",  # replaces "quick-handed" (too high for mid bucket)
    "high": "nimble",  # replaces "wiry" (cross-coded STR+DEX; retired entirely)
    "very-high": "preternaturally quick",
}

WIS_LANES: Final[Mapping[DescriptorBucket, str]] = {
    "very-low": "credulous",
    "low": "naive",  # replaces "plain-spoken" (CHA/social word, not WIS)
    "mid": "level-headed",
    "high": "shrewd",
    "very-high": "uncannily wise",  # replaces "uncannily perceptive" ("perceptive" leans INT)
}

# HP condition lane -- bucket of (current_hp / max_hp).
type HpBucket = Literal["unhurt", "scratched", "bleeding", "down"]

HP_BUCKETS: Final[Mapping[HpBucket, str]] = {
    "unhurt": "unhurt",
    "scratched": "scratched",
    "bleeding": "bleeding",
    "down": "barely standing",
}

# Stress condition lane.
type StressBucket = Literal["composed", "tense", "frayed", "near-breaking"]

STRESS_BUCKETS: Final[Mapping[StressBucket, str]] = {
    "composed": "composed",
    "tense": "tense",
    "frayed": "frayed",
    "near-breaking": "near breaking",
}

# Bond verbs -- sign-based mapping (positive / neutral / negative).
type BondSign = Literal["positive", "neutral", "negative"]

BOND_VERBS: Final[Mapping[BondSign, str]] = {
    "positive": "trusts {name} deeply",
    "neutral": "has no strong feeling toward {name}",
    "negative": "is wary of {name}",
}

# ---------------------------------------------------------------------------
# Action schema vocabularies (Varn 2026-05-01 sec. 3)
# ---------------------------------------------------------------------------

# Manner: time-of-day lane. ~8 entries (closed).
type TimeOfDay = Literal[
    "under starlight",
    "at dawn",
    "at noon",
    "in the late afternoon",
    "at dusk",
    "under heavy rain",
    "in the deep of night",
    "at first light",
]

# Manner: footing lane. ~10 entries (closed).
type Footing = Literal[
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
]

# Manner: noise lane. ~8 entries (closed).
type Noise = Literal[
    "near silence",
    "muffled steps",
    "open clatter",
    "the cover of wind",
    "the crash of rain",
    "muted breathing",
    "the creak of leather",
    "the rasp of armor",
]

# Opposition alertness lane.
type Alertness = Literal[
    "alert and rested",
    "drowsy",
    "alarmed and searching",
    "distracted",
    "exhausted",
    "wary",
]

# Opposition skill tier.
type SkillTier = Literal["green", "ordinary", "seasoned", "veteran", "elite"]

# ---------------------------------------------------------------------------
# Context schema vocabularies (Varn 2026-05-01 sec. 3)
# ---------------------------------------------------------------------------

# Mission archetype. Generic closed-vocab -- regional flavor comes from
# objective_phrase + region fields, not from the mission-type literal.
# "ridge reconnaissance" was retired (Delta 6 -- 2026-05-06): it was a
# region-prefixed example that leaked into the literal; use "reconnaissance"
# and let objective_phrase carry the ridge framing.
type MissionType = Literal[
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
]

# Stakes tier.
# Note: STAKES_PHRASES (identity map) was removed 2026-05-06 -- dead code,
# no callers. If a tier→phrase transform is needed later, add it then (YAGNI).
type StakesTier = Literal["trivial", "modest", "serious", "high", "dire"]

# Party-level wound bucket. 3 buckets (Varn 2026-05-01 §3 lock: "3 buckets each").
# "wounded" dropped (Delta 1 -- 2026-05-06): redundant mid-tier between
# "lightly wounded" and "battered"; canonical fixture uses "lightly wounded".
type WoundBucket = Literal["unhurt", "lightly wounded", "battered"]

# Party-level supply bucket. 3 buckets (same 3-bucket lock as WoundBucket).
# "adequately supplied" dropped (Delta 2 -- 2026-05-06): lukewarm mid-tier;
# three-tier ramp = healthy / strained / collapsing.
type SupplyBucket = Literal[
    "well-supplied",
    "low on rations",
    "starving",
]

# Party-level fatigue bucket. 3 buckets (same 3-bucket lock as WoundBucket).
# "spent" dropped (Delta 3 -- 2026-05-06): synonym-creep with "exhausted";
# three-tier ramp = ok / strained / done.
type FatigueBucket = Literal["rested", "short on sleep", "exhausted"]

# Weather temperature lane.
type Temperature = Literal["bitterly cold", "cold", "cool", "warm", "hot"]

# Weather precipitation lane.
type Precipitation = Literal[
    "dry",
    "drizzling",
    "raining steadily",
    "in driving rain",
    "snowing lightly",
    "in heavy snow",
]

# Weather wind lane.
type Wind = Literal[
    "no wind",
    "a light breeze",
    "a steady wind",
    "a strong wind",
    "gale-force gusts",
]

# Wind directional render. Template: "with {wind} from the {direction}".
# Extended (Delta 5 -- 2026-05-06) beyond cardinals to support landmark-based
# directions. Literals omit leading "the " because the template already emits
# "from the ". New entries: river, sea, mountains, high passes -- enables the
# festival canonical "with a light breeze from the river" (Varn Context §6).
type WindDirection = Literal[
    "north",
    "south",
    "east",
    "west",
    "river",
    "sea",
    "mountains",
    "high passes",
]

# Terrain primary lane.
type TerrainPrimary = Literal[
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
]

# Terrain modifier (sub-clause). Template: "with {modifier}" (inside the
# terrain sentence). Literals must NOT begin with "with " -- that would
# produce a double-"with" bug (e.g. "with with festival lanterns ...").
# 10 entries -- meets Varn's "≈10 modifiers" floor (count verified 2026-05-06).
#
# Delta 4 (2026-05-06): stripped leading "with " from 5 offending entries.
# Gap A closure (2026-05-06): added 3 entries to reach ≈10 floor.
type TerrainModifier = Literal[
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
]

# Faction-relation verbs (paired).
type FactionRelation = Literal[
    "at war",
    "at uneasy peace",
    "at peace",
    "allied",
    "in open trade",
    "in cold neutrality",
]

# Watch-of-day lane (~8 entries).
type Watch = Literal[
    "early morning",
    "late morning",
    "midday",
    "early afternoon",
    "late afternoon",
    "first watch of the night",
    "second watch of the night",
    "third watch of the night",
]
