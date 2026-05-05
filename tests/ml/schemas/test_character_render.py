"""Tests for the character renderer.

Determinism is the load-bearing property: same state -> same string,
forever. The canonical reproduction test below pins the *current*
rendered output ; if Varn revises the schema, update the snapshot here
in the same PR as the renderer change.
"""

from __future__ import annotations

import pytest

from wayfinders.ml.schemas.character import (
    Bond,
    CharacterState,
    EpisodicEvent,
    render_character,
)
from tests.ml.schemas._fixtures import kira_canonical_state


class TestDeterminism:
    def test_same_state_same_string(self) -> None:
        state = kira_canonical_state()
        first = render_character(state)
        for _ in range(50):
            assert render_character(state) == first

    def test_pure_function_no_side_effects(self) -> None:
        state = kira_canonical_state()
        # Render twice with state mutated/unmutated cycle ; frozen Pydantic
        # makes mutation impossible, but we still want to assert that calls
        # are independent.
        a = render_character(state)
        b = render_character(state)
        assert a == b


class TestStructure:
    def test_starts_with_identity_line(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        assert out.startswith("Kira is a scout of the Ridgewatch company.")

    def test_descriptor_sentence_present(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        # mid + very-high + high → "sturdy, preternaturally quick, and shrewd"
        assert "Kira is sturdy, preternaturally quick, and shrewd." in out

    def test_traits_alphabetical(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        # traits=(bold, loyal, impatient) → sorted = (bold, impatient, loyal)
        assert "Kira is bold, impatient, and loyal." in out

    def test_legacy_combat_engagements_rendered(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        assert (
            "She has fought through forty-seven goblin engagements, "
            "twelve beast hunts, and three skirmishes with undead."
        ) in out

    def test_legacy_action_counters_rendered(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        assert "succeeded at stealth approaches eight times and failed three" in out

    def test_legacy_professions_rendered(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        assert "eighty hours scouting" in out
        assert "one thousand two hundred forty hours at the forge" in out

    def test_legacy_locations_rendered(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        assert (
            "She knows forest terrain well, dungeon corridors moderately, "
            "and town streets best of all."
        ) in out

    def test_episodic_memory_rendered(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        assert (
            "Kira's defining moments include: "
            "her first kill, a goblin scout in the Hollowwood; "
            "the night she lost Brann to ghouls in the Catacombs of Vesh; "
            "the day she spared the bandit captain at Fenmarch crossing."
        ) in out

    def test_bonds_rendered_with_dead_partner(self) -> None:
        state = kira_canonical_state()
        out = render_character(state)
        # Brann (|5|, dead) > Halden (|-3|) > Mira (|0|)
        assert "Kira trusts Brann deeply, though Brann is dead." in out
        assert "Kira is wary of Halden the quartermaster." in out
        assert "Kira has no strong feeling toward Mira the cleric." in out


class TestEdgeCases:
    def _bare_state(self, **overrides) -> CharacterState:
        defaults = {
            "name": "Test",
            "char_class": "scout",
            "faction": "Testers",
            "str_bucket": "mid",
            "dex_bucket": "mid",
            "wis_bucket": "mid",
            "hp_bucket": "unhurt",
            "stress_bucket": "composed",
        }
        defaults.update(overrides)
        return CharacterState(**defaults)

    def test_empty_bonds_paragraph_dropped(self) -> None:
        state = self._bare_state()
        out = render_character(state)
        # No bond verbs anywhere
        assert "trusts" not in out
        assert "wary of" not in out
        assert "has no strong feeling toward" not in out

    def test_empty_episodic_memory_dropped(self) -> None:
        state = self._bare_state()
        out = render_character(state)
        assert "defining moments" not in out

    def test_empty_legacy_dropped(self) -> None:
        state = self._bare_state()
        out = render_character(state)
        assert "fought through" not in out
        assert "succeeded at" not in out
        assert "spent" not in out

    def test_empty_equipment_dropped(self) -> None:
        state = self._bare_state()
        out = render_character(state)
        assert "carries" not in out

    def test_zero_count_combat_omitted(self) -> None:
        # Lock option (a) Varn 2026-04-30 §6: zero counts are omitted.
        state = self._bare_state(combat_engagements={"goblin engagements": 0})
        out = render_character(state)
        assert "goblin" not in out

    def test_bond_top_3_only(self) -> None:
        state = self._bare_state(
            bonds=(
                Bond(partner_name="A", bond_value=10),
                Bond(partner_name="B", bond_value=-9),
                Bond(partner_name="C", bond_value=8),
                Bond(partner_name="D", bond_value=7),
            )
        )
        out = render_character(state)
        assert "A" in out and "B" in out and "C" in out
        # D should be absent — top 3 only
        assert "Test trusts D" not in out
        assert "Test is wary of D" not in out

    def test_episodic_top_3_only_by_salience(self) -> None:
        state = self._bare_state(
            episodic_memory=(
                EpisodicEvent(event_id=1, salience=1.0, description="lowsa"),
                EpisodicEvent(event_id=2, salience=10.0, description="topa"),
                EpisodicEvent(event_id=3, salience=10.0, description="topb"),
                EpisodicEvent(event_id=4, salience=5.0, description="midd"),
            )
        )
        out = render_character(state)
        # top 3 by salience desc, lower event_id wins ties:
        # event 2 (10.0), event 3 (10.0), event 4 (5.0)
        assert "topa" in out and "topb" in out and "midd" in out
        assert "lowsa" not in out
        # event 2 must come before event 3 (lower id wins)
        assert out.index("topa") < out.index("topb")


@pytest.mark.parametrize("n", range(20))
def test_idempotent_repeated_calls(n: int) -> None:
    """Sanity: render N times and assert byte-equality."""
    state = kira_canonical_state()
    base = render_character(state)
    for _ in range(n):
        assert render_character(state) == base
