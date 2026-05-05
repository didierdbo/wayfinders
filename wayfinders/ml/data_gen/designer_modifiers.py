"""Designer modifier table -> label_delta.

The label is **authored**, not learned. The model learns to compress the
designer's intent into a scalar over prose-derived vectors. Designers stay
in the driver's seat (Pax 2026-05-02 sec. 4).

M1 substep 5 fills in real rules. This file scaffolds the API and provides
a tiny seed table for end-to-end pipeline testing.
"""

from __future__ import annotations

from typing import Final

from wayfinders.ml.data_gen.rollouts import Rollout

# delta output range (locked to Pax sec. 3).
DELTA_MIN: Final[float] = -5.0
DELTA_MAX: Final[float] = 5.0


def compute_label(rollout: Rollout) -> float:
    """Apply the designer modifier table to a structured rollout.

    Returns a label_delta in [-5, +5].

    M1 seed implementation: a placeholder rule set that uses character
    legacy + action tag overlap as the dominant signal. Real designer
    table from Varn lands in M1 substep 5.

    The function is **pure**: same rollout -> same label, forever.
    """
    char = rollout.character
    action = rollout.action
    party = rollout.party

    delta = 0.0

    # Tag-aligned competence: count successful prior attempts at this action
    # family. +0.5 per past success, -0.3 per past failure, capped at +/-3.
    successes_failures = char.action_counters.get(action.action_phrase + "es", None)
    if successes_failures is None:
        # Try the canonical 'stealth approaches' form for Varn's example.
        successes_failures = char.action_counters.get(action.action_phrase + "s", (0, 0))
    successes, failures = successes_failures
    competence = 0.5 * successes - 0.3 * failures
    delta += max(-3.0, min(3.0, competence))

    # Party fatigue penalty: tired parties resolve worse on dexterity-tested
    # actions (Pratchett-spirit ; designers may revise).
    if action.stat_tested == "dexterity" and party.fatigue_bucket in {"exhausted", "spent"}:
        delta -= 1.0

    # Manner mismatch penalty: 'loose scree' under 'open clatter' is hard for
    # stealth ; modest -1 if action is stealth-tagged.
    if "stealth" in action.tag_set and action.noise_phrase == "open clatter":
        delta -= 1.5

    # Manner alignment bonus: 'firm stone' under 'near silence' on stealth.
    if (
        "stealth" in action.tag_set
        and action.footing_phrase == "firm stone"
        and action.noise_phrase == "near silence"
    ):
        delta += 0.8

    return max(DELTA_MIN, min(DELTA_MAX, delta))
