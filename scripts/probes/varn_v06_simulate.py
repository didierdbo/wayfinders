# ruff: noqa: N806
"""Varn v0.6 calibration analytical simulation.

Independent of the rollout pipeline -- samples the modifier-table tiers
directly using the same vocab weights that policies.py applies, then computes
raw_delta and label_delta to estimate the v0.6 distribution before retraining.

Run:
    uv run python scripts/probes/varn_v06_simulate.py
"""

from __future__ import annotations

import math
import random
import statistics

# v0.5 verb weights (mirrors policies._VERB_WEIGHTS).
VERBS = ["stealth approach", "move", "wait", "signal to ally", "melee attack", "ranged attack"]
VERB_W = [0.70, 0.06, 0.04, 0.05, 0.08, 0.07]

# ----- v0.5 BASELINE -----
ACTION_BASE_V05 = {
    "stealth approach": -1,
    "move": 0,
    "wait": 1,
    "signal to ally": 0,
    "melee attack": 0,
    "ranged attack": -1,
}
DEX_FULL_V05 = {"very-high": 3, "high": 1, "mid": 0, "low": -1, "very-low": -2}
DEX_MOVE_V05 = {"very-high": 1, "high": 1, "mid": 0, "low": 0, "very-low": -1}
STR_MELEE_V05 = {"very-high": 3, "high": 1, "mid": 0, "low": -1, "very-low": -2}
WIS_STEALTH_V05 = {"very-high": 1, "high": 1, "mid": 0, "low": -1, "very-low": -1}
WIS_SIGNAL_V05 = {"very-high": 1, "high": 1, "mid": 0, "low": 0, "very-low": -1}
HP_V05 = {"unhurt": 0, "scratched": 0, "bleeding": -2, "down": 0}
STRESS_V05 = {"composed": 1, "tense": 0, "frayed": -1, "near-breaking": -3}
WOUND_V05 = {"unhurt": 0, "lightly wounded": 0, "battered": -2}
SUPPLY_V05 = {"well-supplied": 0, "low on rations": -1, "starving": -2}
FATIGUE_V05 = {"rested": 1, "short on sleep": -1, "exhausted": -2}
STAKES_V05 = {"trivial": 1, "modest": 0, "serious": 0, "high": -1, "dire": -2}
TEMP_V05 = {"bitterly cold": -1, "cold": 0, "cool": 0, "warm": 0, "hot": 0}
WIND_V05 = {
    "no wind": 0,
    "a light breeze": 0,
    "a steady wind": 1,
    "a strong wind": 1,
    "gale-force gusts": 1,
}
TERRAIN_V05 = {
    "broken stone and scree": 1,
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
FOOTING_STEALTH_V05 = {
    "firm stone": 1,
    "wet leaves": 0,
    "marsh grass": 0,
    "loose scree": -2,
    "dry sand": -1,
    "rotten wood": -2,
    "snow-pack": 1,
    "polished tile": 0,
    "hot ash": -1,
    "dewed grass": 0,
}
FOOTING_MOVE_V05 = {
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
TIME_V05 = {
    "under starlight": 2,
    "in the deep of night": 2,
    "at first light": 1,
    "at dawn": 1,
    "at dusk": 1,
    "in the late afternoon": 0,
    "under heavy rain": 1,
    "at noon": -3,
}
NOISE_V05 = {
    "near silence": 0,
    "muffled steps": 1,
    "muted breathing": 1,
    "open clatter": 2,
    "the cover of wind": 1,
    "the crash of rain": 2,
    "the creak of leather": -1,
    "the rasp of armor": -1,
}
ALERT_V05 = {
    "drowsy": 2,
    "distracted": 2,
    "alert and rested": 0,
    "wary": -1,
    "alarmed and searching": -3,
    "exhausted": 1,
}
SKILL_V05 = {"green": 1, "ordinary": 0, "seasoned": -1, "veteran": -1, "elite": -2}
COUNT_TIERS_V05 = [(6, -2), (3, -1), (2, 0), (1, 1), (0, 0)]
TANH_IN_V05 = 5.0
TANH_OUT_V05 = 5.0

# ----- v0.6 PROPOSAL -----
# Strategy:
#   (a) Add half-step values where the design tolerates fractional weight ;
#       this kills the comb pattern by pushing some raw sums onto half-integer
#       positions, populating the dead bins.
#   (b) Recenter the systematically negative anchors:
#       - ACTION_BASE: stealth -1 -> -0.5, ranged -1 -> -0.5
#       - SUPPLY/FATIGUE: less brutal asymmetry
#       - STAKES: less negative skew
#   (c) Drop TANH_INPUT_SCALE from 5 to 8 to expand the linear regime and
#       **collapse saturation** (raw needs ~|24| now to reach |delta| 4.5).
#       Output scale stays at 5 -- range contract preserved.
ACTION_BASE_V06 = {
    "stealth approach": -0.5,
    "move": 0.0,
    "wait": 0.5,
    "signal to ally": 0.0,
    "melee attack": 0.0,
    "ranged attack": -0.5,
}
# Descriptors: keep monotonic shape ; soften extremes a touch.
DEX_FULL_V06 = {"very-high": 2.5, "high": 1.0, "mid": 0.0, "low": -1.0, "very-low": -2.0}
DEX_MOVE_V06 = {"very-high": 1.0, "high": 0.5, "mid": 0.0, "low": 0.0, "very-low": -1.0}
STR_MELEE_V06 = {"very-high": 2.5, "high": 1.0, "mid": 0.0, "low": -1.0, "very-low": -2.0}
WIS_STEALTH_V06 = {"very-high": 1.0, "high": 0.5, "mid": 0.0, "low": -0.5, "very-low": -1.0}
WIS_SIGNAL_V06 = {"very-high": 1.0, "high": 0.5, "mid": 0.0, "low": 0.0, "very-low": -1.0}
HP_V06 = {"unhurt": 0.0, "scratched": -0.5, "bleeding": -1.5, "down": 0.0}
STRESS_V06 = {"composed": 0.5, "tense": 0.0, "frayed": -0.5, "near-breaking": -2.0}
WOUND_V06 = {"unhurt": 0.0, "lightly wounded": -0.5, "battered": -1.5}
# Recenter: bring the means toward zero by softening the negative tail.
SUPPLY_V06 = {"well-supplied": 0.5, "low on rations": -0.5, "starving": -1.5}
FATIGUE_V06 = {"rested": 0.5, "short on sleep": -0.5, "exhausted": -1.5}
STAKES_V06 = {"trivial": 1.0, "modest": 0.5, "serious": 0.0, "high": -0.5, "dire": -1.5}
TEMP_V06 = {"bitterly cold": -1.0, "cold": -0.5, "cool": 0.0, "warm": 0.0, "hot": 0.0}
WIND_V06 = {
    "no wind": 0.0,
    "a light breeze": 0.5,
    "a steady wind": 1.0,
    "a strong wind": 1.0,
    "gale-force gusts": 0.5,
}
# Terrain: open plains was a large negative outlier ; soften.
TERRAIN_V06 = {
    "broken stone and scree": 1.0,
    "deep forest": 1.5,
    "dense brush": 1.0,
    "open plains": -1.5,
    "marsh and reed": 0.0,
    "paved streets and crowded squares": 0.5,
    "narrow corridors of dressed stone": -0.5,
    "river crossing": 0.0,
    "high cliffs": 0.0,
    "snowfields": 0.0,
}
FOOTING_STEALTH_V06 = {
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
FOOTING_MOVE_V06 = {
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
TIME_V06 = {
    "under starlight": 1.5,
    "in the deep of night": 1.5,
    "at first light": 1.0,
    "at dawn": 1.0,
    "at dusk": 1.0,
    "in the late afternoon": 0.0,
    "under heavy rain": 1.0,
    "at noon": -2.0,
}
NOISE_V06 = {
    "near silence": 0.5,
    "muffled steps": 1.0,
    "muted breathing": 1.0,
    "open clatter": 1.5,
    "the cover of wind": 1.0,
    "the crash of rain": 1.5,
    "the creak of leather": -0.5,
    "the rasp of armor": -0.5,
}
ALERT_V06 = {
    "drowsy": 1.5,
    "distracted": 1.5,
    "alert and rested": 0.0,
    "wary": -0.5,
    "alarmed and searching": -2.0,
    "exhausted": 1.0,
}
SKILL_V06 = {"green": 1.0, "ordinary": 0.0, "seasoned": -0.5, "veteran": -1.0, "elite": -1.5}
COUNT_TIERS_V06 = [(6, -1.5), (3, -0.5), (2, 0.0), (1, 0.5), (0, 0.0)]
TANH_IN_V06 = 8.0
TANH_OUT_V06 = 5.0


def _tier(value, tiers):
    for thr, d in tiers:
        if value >= thr:
            return d
    return 0


def sample_raw(rng, version):
    """Approximate raw_delta sampling using independent uniform draws."""
    if version == "v05":
        ACT = ACTION_BASE_V05
        DEXF = DEX_FULL_V05
        DEXM = DEX_MOVE_V05
        STR_ = STR_MELEE_V05
        WS = WIS_STEALTH_V05
        WSI = WIS_SIGNAL_V05
        HP = HP_V05
        STR2 = STRESS_V05
        WD = WOUND_V05
        SP = SUPPLY_V05
        FT = FATIGUE_V05
        STK = STAKES_V05
        TMP = TEMP_V05
        WIN = WIND_V05
        TER = TERRAIN_V05
        FS = FOOTING_STEALTH_V05
        FM = FOOTING_MOVE_V05
        TM = TIME_V05
        NO = NOISE_V05
        AL = ALERT_V05
        SK = SKILL_V05
        CT = COUNT_TIERS_V05
    else:
        ACT = ACTION_BASE_V06
        DEXF = DEX_FULL_V06
        DEXM = DEX_MOVE_V06
        STR_ = STR_MELEE_V06
        WS = WIS_STEALTH_V06
        WSI = WIS_SIGNAL_V06
        HP = HP_V06
        STR2 = STRESS_V06
        WD = WOUND_V06
        SP = SUPPLY_V06
        FT = FATIGUE_V06
        STK = STAKES_V06
        TMP = TEMP_V06
        WIN = WIND_V06
        TER = TERRAIN_V06
        FS = FOOTING_STEALTH_V06
        FM = FOOTING_MOVE_V06
        TM = TIME_V06
        NO = NOISE_V06
        AL = ALERT_V06
        SK = SKILL_V06
        CT = COUNT_TIERS_V06

    verb = rng.choices(VERBS, weights=VERB_W, k=1)[0]
    raw = ACT[verb]

    # Descriptors (uniform over 5 buckets ; chargen is roughly uniform).
    dex = rng.choice(list(DEXF.keys()))
    wis = rng.choice(list(WS.keys()))
    str_ = rng.choice(list(STR_.keys()))
    if verb in ("stealth approach", "ranged attack"):
        raw += DEXF[dex]
        if verb == "stealth approach":
            raw += WS[wis]
    elif verb == "move":
        raw += DEXM[dex]
    elif verb == "melee attack":
        raw += STR_[str_]
    elif verb == "signal to ally":
        raw += WSI[wis]

    # Status -- HP/stress (rough uniform with 'down' rare).
    hp = rng.choices(list(HP.keys()), weights=[3, 3, 2, 0.1])[0]
    raw += HP[hp]
    raw += STR2[rng.choice(list(STR2.keys()))]

    # Conditions (50% chance of one keyword from a small set ; small effect).
    if rng.random() < 0.4:
        raw += rng.choice([1, 1, 1, -1, -1, -2, 2])  # exhausted/inspired etc.

    # Equipment (substring matched on free-text ; approx mean -0.7 stdev 1.4
    # for stealth-relevant items in v0.5 ; we keep this constant across
    # versions because we did not change equipment table in this pass).
    raw += rng.gauss(-0.7, 1.4)

    # Legacy biography (action_counters + scouting hours + familiarity).
    # In v0.5 the legacy clamp is [-3, +4], mean ~+0.5 in mixed policy. Keep.
    legacy = rng.choices([0, 1, 2, 3, 4, -1, -2], weights=[3, 3, 2, 1, 1, 1, 0.5])[0]
    raw += legacy

    # Episodic +1 with low probability.
    if rng.random() < 0.10:
        raw += 1

    # Action manner.
    raw += FS[rng.choice(list(FS.keys()))] if verb != "move" else FM[rng.choice(list(FM.keys()))]
    raw += TM[rng.choice(list(TM.keys()))]
    raw += NO[rng.choice(list(NO.keys()))]

    # Opposition.
    count = rng.choices([0, 1, 2, 3, 4, 6], weights=[1, 2, 3, 2, 1, 1])[0]
    if count > 0:
        raw += _tier(count, CT)
        raw += AL[rng.choice(list(AL.keys()))]
        raw += SK[rng.choice(list(SK.keys()))]

    # Context.
    raw += STK[rng.choice(list(STK.keys()))]
    if rng.random() < 0.85:  # has party
        raw += WD[rng.choice(list(WD.keys()))]
        raw += SP[rng.choice(list(SP.keys()))]
        raw += FT[rng.choice(list(FT.keys()))]
    raw += TMP[rng.choice(list(TMP.keys()))]
    raw += WIN[rng.choice(list(WIN.keys()))]
    raw += TER[rng.choice(list(TER.keys()))]
    # Faction relation small contribution skipped (mean ~0).
    # Countdown.
    if rng.random() < 0.7:
        cd = rng.randint(0, 8)
        raw += 0 if cd >= 5 else (-1 if cd >= 1 else -2)
    else:
        raw += 1  # no deadline

    return raw


def report(version, n=20000):
    rng = random.Random(42)
    raws = [sample_raw(rng, version) for _ in range(n)]
    if version == "v05":
        scale_in, scale_out = TANH_IN_V05, TANH_OUT_V05
    else:
        scale_in, scale_out = TANH_IN_V06, TANH_OUT_V06
    labels = [scale_out * math.tanh(r / scale_in) for r in raws]
    raws_mean = statistics.mean(raws)
    raws_std = statistics.stdev(raws)
    m = statistics.mean(labels)
    s = statistics.stdev(labels)
    sat45 = sum(1 for x in labels if abs(x) >= 4.5) / len(labels)
    sat40 = sum(1 for x in labels if abs(x) >= 4.0) / len(labels)
    sat30 = sum(1 for x in labels if abs(x) >= 3.0) / len(labels)

    # Histogram in 20 bins over [-5, 5].
    bins = [0] * 20
    for x in labels:
        idx = min(int((x + 5) / 0.5), 19)
        bins[idx] += 1

    print(f"\n=== {version.upper()} (n={n}) ===")
    print(f"  raw    mean={raws_mean:+.3f}  stdev={raws_std:.3f}")
    print(f"  label  mean={m:+.3f}  stdev={s:.3f}")
    print(
        f"  sat |D|>=4.5: {sat45 * 100:.2f}%   |D|>=4.0: {sat40 * 100:.2f}%   |D|>=3.0: {sat30 * 100:.2f}%"
    )
    print("  histogram:")
    for i, c in enumerate(bins):
        lo = -5 + i * 0.5
        hi = lo + 0.5
        bar = "#" * int(c / n * 200)
        print(f"    [{lo:+.1f}, {hi:+.1f})  {c:5d}  ({c / n * 100:5.2f}%)  {bar}")


if __name__ == "__main__":
    report("v05")
    report("v06")
