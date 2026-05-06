"""Tests for the action renderer (M1 substep 4).

Sentence 9 (local legacy echo) is dropped per Pax 2026-05-02 sec. 5 lock --
the action renderer emits 9 sentences, not 10.

Snapshot canonical (_KIRA_STEALTH_CANONICAL_DOC) is byte-pinned to Varn
2026-05-01 sec. 2, with sentence 9 removed per the Pax lock.
"""

from __future__ import annotations

import pytest

from tests.ml.schemas._fixtures import (
    ridge_opposition,
    stealth_approach_card,
)
from wayfinders.ml.schemas.action import OppositionState, render_action

# ---------------------------------------------------------------------------
# Byte-exact snapshot -- single source of truth for the Kira stealth canonical.
# Matches Varn 2026-05-01 sec. 2 with sentence 9 (local legacy echo) removed
# per Pax 2026-05-02 sec. 5 lock. Update when schema changes.
# Note: manner footing is "loose scree" not "a loose scree slope" -- the
# ActionCard.footing_phrase slot carries the bare modifier, not the full NP.
# ---------------------------------------------------------------------------
_KIRA_STEALTH_CANONICAL_DOC = (
    "The action is a stealth approach.\n"
    "Kira is the actor.\n"
    "Kira is sneaking past two sentries posted at the watchtower.\n"
    "The deed is performed under starlight, on loose scree, in near silence.\n"
    "The intent is to reach the ridge unseen and unheard.\n"
    # tags sorted alphabetically: movement, scouting, stealth
    "The action is tagged as movement, scouting, and stealth, and against humanoids.\n"
    "The action tests dexterity, opposed by perception.\n"
    "The opposition is two human sentries, alert and rested, of ordinary skill.\n"
    "A failed stealth approach typically means an alarm raised, "
    "a fight on disadvantageous ground, or a forced retreat."
)


class TestCanonicalSnapshot:
    def test_kira_stealth_canonical(self) -> None:
        """Byte-exact reproduction of Varn 2026-05-01 sec. 2 (sentence 9 dropped)."""
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert out == _KIRA_STEALTH_CANONICAL_DOC


class TestDeterminism:
    def test_same_inputs_same_string(self) -> None:
        card = stealth_approach_card()
        opp = ridge_opposition()
        first = render_action(card, "Kira", opp)
        for _ in range(100):
            assert render_action(card, "Kira", opp) == first


class TestStructure:
    def test_sentence_count_is_nine(self) -> None:
        """Sentence 9 (local legacy echo) is dropped per Pax sec. 5 lock."""
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        # 9 sentences = 9 newline-separated lines
        assert len(out.split("\n")) == 9

    def test_headline_first(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert out.startswith("The action is a stealth approach.")

    def test_actor_line(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert "\nKira is the actor." in out

    def test_direct_object(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert "Kira is sneaking past two sentries posted at the watchtower." in out

    def test_manner_clause(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert ("The deed is performed under starlight, on loose scree, in near silence.") in out

    def test_intent_clause(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert "The intent is to reach the ridge unseen and unheard." in out

    def test_tag_line_alphabetical(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        # tags = (stealth, movement, scouting) -> sorted = (movement, scouting, stealth)
        assert (
            "The action is tagged as movement, scouting, and stealth, and against humanoids."
        ) in out

    def test_stat_line(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert "The action tests dexterity, opposed by perception." in out

    def test_opposition_vignette(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert ("The opposition is two human sentries, alert and rested, of ordinary skill.") in out

    def test_stakes_hint(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert (
            "A failed stealth approach typically means an alarm raised, "
            "a fight on disadvantageous ground, or a forced retreat."
        ) in out

    def test_no_local_legacy_echo(self) -> None:
        """Sentence 9 dropped -- no 'succeeded eight times' inside action prose."""
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert "succeeded" not in out
        assert "crossed scree" not in out
        assert "spotted" not in out


class TestEdgeCases:
    def test_solo_action_drops_opposition_sentence(self) -> None:
        opp = OppositionState(count=0, family="none", alertness="drowsy", skill_tier="green")
        out = render_action(stealth_approach_card(), "Kira", opp)
        # 8 sentences when opposition drops (9 - 1)
        assert len(out.split("\n")) == 8
        assert "The opposition" not in out


class TestSlotLaneIntegrity:
    """Each sentence draws from at most one manner lane; no lane appears twice.

    Sentence 4 template: "The deed is performed {time}, on {footing}, in {noise}."
    Each slot maps to exactly one lane. The three manner-lane values must be
    distinct tokens and must each appear exactly once in the manner sentence.
    """

    def test_manner_sentence_has_three_distinct_lane_values(self) -> None:
        card = stealth_approach_card()
        opp = ridge_opposition()
        out = render_action(card, "Kira", opp)
        manner_line = next(s for s in out.split("\n") if s.startswith("The deed is performed"))
        # Each lane value appears exactly once in the manner sentence
        assert manner_line.count(card.time_phrase) == 1
        assert manner_line.count(card.footing_phrase) == 1
        assert manner_line.count(card.noise_phrase) == 1
        # All three values are distinct (no cross-lane collision)
        assert len({card.time_phrase, card.footing_phrase, card.noise_phrase}) == 3

    def test_stat_sentence_has_two_distinct_stats(self) -> None:
        card = stealth_approach_card()
        opp = ridge_opposition()
        out = render_action(card, "Kira", opp)
        stat_line = next(s for s in out.split("\n") if s.startswith("The action tests"))
        assert card.stat_tested in stat_line
        assert card.opposed_stat in stat_line
        # stat_tested and opposed_stat must differ (can't test a stat against itself)
        assert card.stat_tested != card.opposed_stat

    def test_opposition_sentence_has_distinct_alertness_and_skill(self) -> None:
        card = stealth_approach_card()
        opp = ridge_opposition()
        out = render_action(card, "Kira", opp)
        opp_line = next(s for s in out.split("\n") if s.startswith("The opposition is"))
        assert opp.alertness in opp_line
        assert opp.skill_tier in opp_line
        # The two lane values (alertness, skill_tier) are distinct tokens
        assert opp.alertness != opp.skill_tier


@pytest.mark.parametrize("actor", ["Kira", "Brann", "Halden the quartermaster"])
def test_no_pronouns(actor: str) -> None:
    """Action prose must repeat the actor name, never use pronouns."""
    out = render_action(stealth_approach_card(), actor, ridge_opposition())
    # Spot-check: the actor name appears at least twice (actor line + DO clause)
    assert out.count(actor) >= 2
    # No standalone 'he' / 'she' / 'they' ; they wouldn't appear given the
    # template structure but assert anyway as a regression guard.
    for pronoun in (" he ", " she ", " they "):
        assert pronoun not in f" {out} "
