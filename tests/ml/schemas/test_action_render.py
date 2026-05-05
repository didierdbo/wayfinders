"""Tests for the action renderer.

Sentence 9 (local legacy echo) is dropped per Pax 2026-05-02 §5 lock —
the action renderer emits 9 sentences, not 10.
"""

from __future__ import annotations

import pytest

from wayfinders.ml.schemas.action import OppositionState, render_action
from tests.ml.schemas._fixtures import (
    ridge_opposition,
    stealth_approach_card,
)


class TestDeterminism:
    def test_same_inputs_same_string(self) -> None:
        card = stealth_approach_card()
        opp = ridge_opposition()
        first = render_action(card, "Kira", opp)
        for _ in range(50):
            assert render_action(card, "Kira", opp) == first


class TestStructure:
    def test_sentence_count_is_nine(self) -> None:
        """Sentence 9 (local legacy echo) is dropped per Pax §5 lock."""
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
        assert (
            "The deed is performed under starlight, "
            "on loose scree, in near silence."
        ) in out

    def test_intent_clause(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert "The intent is to reach the ridge unseen and unheard." in out

    def test_tag_line_alphabetical(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        # tags = (stealth, movement, scouting) → sorted = (movement, scouting, stealth)
        assert (
            "The action is tagged as movement, scouting, and stealth, "
            "and against humanoids."
        ) in out

    def test_stat_line(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert "The action tests dexterity, opposed by perception." in out

    def test_opposition_vignette(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert (
            "The opposition is two human sentries, alert and rested, "
            "of ordinary skill."
        ) in out

    def test_stakes_hint(self) -> None:
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert (
            "A failed stealth approach typically means an alarm raised, "
            "a fight on disadvantageous ground, or a forced retreat."
        ) in out

    def test_no_local_legacy_echo(self) -> None:
        """Sentence 9 dropped — no 'succeeded eight times' inside action prose."""
        out = render_action(stealth_approach_card(), "Kira", ridge_opposition())
        assert "succeeded" not in out
        assert "crossed scree" not in out
        assert "spotted" not in out


class TestEdgeCases:
    def test_solo_action_drops_opposition_sentence(self) -> None:
        opp = OppositionState(
            count=0, family="none", alertness="drowsy", skill_tier="green"
        )
        out = render_action(stealth_approach_card(), "Kira", opp)
        # 8 sentences when opposition drops (9 - 1)
        assert len(out.split("\n")) == 8
        assert "The opposition" not in out


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
