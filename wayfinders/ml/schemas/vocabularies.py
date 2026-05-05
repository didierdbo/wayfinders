"""Closed lookup tables for the three renderers.

Every adjective, lane label, and verbal mapping the renderers emit lives
here. Adding a new word is a design change (Varn-approved), not a content
change -- that's the EN-pinned, closed-vocab discipline.

All tables are `Final[Mapping[...]]` so mypy catches accidental mutation.
"""

from __future__ import annotations

from collections.abc import Mapping
from typing import Final, Literal

# ---------------------------------------------------------------------------
# Character schema vocabularies (Varn 2026-04-30 sec. 3)
# ---------------------------------------------------------------------------

# Descriptor lanes -- STR / DEX / WIS rendered as adjective from a 5-bucket
# bucketing of the underlying score. Buckets are: very-low / low / mid /
# high / very-high. Lookup is keyed by bucket label.
type DescriptorBucket = Literal["very-low", "low", "mid", "high", "very-high"]

STR_LANES: Final[Mapping[DescriptorBucket, str]] = {
    "very-low": "frail",
    "low": "slight",
    "mid": "sturdy",
    "high": "strong-armed",
    "very-high": "iron-thewed",
}

DEX_LANES: Final[Mapping[DescriptorBucket, str]] = {
    "very-low": "ponderous",
    "low": "deliberate",
    "mid": "quick-handed",
    "high": "wiry",
    "very-high": "preternaturally quick",
}

WIS_LANES: Final[Mapping[DescriptorBucket, str]] = {
    "very-low": "credulous",
    "low": "plain-spoken",
    "mid": "level-headed",
    "high": "shrewd",
    "very-high": "uncannily perceptive",
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

# Mission archetype.
type MissionType = Literal[
    "ridge reconnaissance",
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
type StakesTier = Literal["trivial", "modest", "serious", "high", "dire"]

STAKES_PHRASES: Final[Mapping[StakesTier, str]] = {
    "trivial": "trivial",
    "modest": "modest",
    "serious": "serious",
    "high": "high",
    "dire": "dire",
}

# Party-level wound bucket.
type WoundBucket = Literal["unhurt", "lightly wounded", "wounded", "battered"]

# Party-level supply bucket.
type SupplyBucket = Literal[
    "well-supplied",
    "adequately supplied",
    "low on rations",
    "starving",
]

# Party-level fatigue bucket.
type FatigueBucket = Literal["rested", "short on sleep", "exhausted", "spent"]

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

# Wind directional render (sentence template uses "with {wind} from the {direction}").
type WindDirection = Literal["north", "south", "east", "west"]

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

# Terrain modifier (sub-clause).
type TerrainModifier = Literal[
    "sparse pine cover",
    "thick canopy overhead",
    "with festival lanterns strung overhead",
    "with old battlements above",
    "with ruined walls flanking the path",
    "with shallow water underfoot",
    "with wind-scoured boulders",
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
