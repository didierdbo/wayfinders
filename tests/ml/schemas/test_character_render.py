"""Tests for the character renderer (M1 substep 3).

Determinism is the load-bearing property: same state -> same string, forever.
The canonical reproduction test pins the exact rendered output; if Varn revises
the schema, update _KIRA_CANONICAL_DOC in the same commit as the renderer change.

Known divergence from Varn 2026-04-30 sec. 2: the original example has
"wiry, preternaturally quick, and shrewd" (two DEX adjectives). The locked
one-adjective-per-stat rule maps mid/very-high/high to
"sturdy, preternaturally quick, and shrewd". The fixture and snapshot below
reflect the locked renderer output, not Varn's pre-lock example string.
"""

from __future__ import annotations

import pytest

from tests.ml.schemas._fixtures import kira_canonical_state
from wayfinders.ml.schemas.character import (
    Bond,
    CharacterState,
    EpisodicEvent,
    render_character,
)

# ---------------------------------------------------------------------------
# Byte-exact snapshot — single source of truth for the Kira canonical render.
# Every sentence matches Varn 2026-04-30 sec. 2 except the descriptor sentence
# (documented divergence above). Update this string when the schema changes.
# ---------------------------------------------------------------------------
_KIRA_CANONICAL_DOC = (
    "Kira is a scout of the Ridgewatch company.\n"
    # Descriptor: mid(STR)=sturdy / very-high(DEX)=preternaturally quick / high(WIS)=shrewd
    "Kira is sturdy, preternaturally quick, and shrewd.\n"
    # Traits: sorted alpha from (bold, loyal, impatient)
    "Kira is bold, impatient, and loyal.\n"
    # Status: scratched + condition + tense
    "Kira is scratched, with a fading bruise on her forearm, and tense from a short night's sleep.\n"
    "Kira carries a short bow she made herself and a worn leather jerkin.\n"
    # Legacy biography — pronoun 'She' is the documented exception inside this paragraph
    "She has fought through forty-seven goblin engagements, twelve beast hunts, "
    "and three skirmishes with undead. "
    "She has succeeded at stealth approaches eight times and failed three. "
    "She has spent eighty hours scouting and one thousand two hundred forty hours at the forge. "
    "She knows forest terrain well, dungeon corridors moderately, and town streets best of all.\n"
    "Kira's defining moments include: "
    "her first kill, a goblin scout in the Hollowwood; "
    "the night she lost Brann to ghouls in the Catacombs of Vesh; "
    "the day she spared the bandit captain at Fenmarch crossing.\n"
    # Bonds: Brann(|5|, dead) > Halden(|-3|) > Mira(|0|)
    "Kira trusts Brann deeply, though Brann is dead. "
    "Kira is wary of Halden the quartermaster. "
    "Kira has no strong feeling toward Mira the cleric."
)


# ---------------------------------------------------------------------------
# Required substep-3 named tests
# ---------------------------------------------------------------------------


def test_render_kira_canonical() -> None:
    """Byte-exact reproduction of the Kira canonical document.

    Sentence-by-sentence match against _KIRA_CANONICAL_DOC, which tracks
    Varn 2026-04-30 sec. 2 modulo the locked descriptor-sentence divergence.
    Updating this test requires a corresponding Varn schema change.
    """
    state = kira_canonical_state()
    result = render_character(state)
    sentences_got = result.split("\n")
    sentences_exp = _KIRA_CANONICAL_DOC.split("\n")
    assert sentences_got == sentences_exp, (
        "Canonical Kira render diverged. Check renderer or update snapshot + Varn lock."
    )
    # Belt-and-suspenders: full byte equality
    assert result == _KIRA_CANONICAL_DOC


def test_render_determinism() -> None:
    """100 sequential calls on the same frozen state must return byte-identical output."""
    state = kira_canonical_state()
    first = render_character(state)
    for i in range(99):  # 1 already taken above; total = 100 unique calls
        assert render_character(state) == first, f"Output differed on call {i + 2}"


def test_render_bond_ordering() -> None:
    """Bonds are sorted by |bond_value| desc, then partner_name asc on ties."""
    state = CharacterState(
        name="Zel",
        char_class="warrior",
        faction="Iron Company",
        str_bucket="high",
        dex_bucket="mid",
        wis_bucket="low",
        hp_bucket="unhurt",
        stress_bucket="composed",
        bonds=(
            # Tie on |value|=4 between "Petra" and "Aldric" -> alpha: Aldric first
            Bond(partner_name="Petra", bond_value=4),
            Bond(partner_name="Aldric", bond_value=-4),
            # Highest magnitude
            Bond(partner_name="Vorn", bond_value=-10),
            # Lower magnitude, should be 4th -> dropped (top 3 only)
            Bond(partner_name="Finn", bond_value=2),
        ),
    )
    out = render_character(state)
    vorn_pos = out.index("Vorn")
    aldric_pos = out.index("Aldric")
    petra_pos = out.index("Petra")
    # Vorn (|10|) must appear before Aldric (|4|, alpha tie-winner)
    assert vorn_pos < aldric_pos
    # Aldric (alpha before Petra) must appear before Petra
    assert aldric_pos < petra_pos
    # Finn (|2|, 4th) is dropped
    assert "Finn" not in out


def test_render_empty_bonds_omitted() -> None:
    """No bond paragraph emitted when bonds is empty."""
    state = CharacterState(
        name="Empty",
        char_class="cleric",
        faction="No One",
        str_bucket="mid",
        dex_bucket="mid",
        wis_bucket="mid",
        hp_bucket="unhurt",
        stress_bucket="composed",
    )
    out = render_character(state)
    assert "trusts" not in out
    assert "wary of" not in out
    assert "has no strong feeling toward" not in out


def test_render_legacy_zero_counts_omitted() -> None:
    """Option (a) from Varn 2026-04-30 sec. 6: zero-count tags are silently dropped.

    A character with zero combat engagements should not have a 'fought through'
    clause; the paragraph is omitted entirely when all legacy counters are zero.
    """
    state = CharacterState(
        name="Novice",
        char_class="scout",
        faction="Ridgewatch company",
        str_bucket="mid",
        dex_bucket="mid",
        wis_bucket="mid",
        hp_bucket="unhurt",
        stress_bucket="composed",
        combat_engagements={"goblin engagements": 0, "beast hunts": 0},
        action_counters={"stealth approaches": (0, 0)},
        profession_hours={"scouting": 0},
        location_familiarity={},
    )
    out = render_character(state)
    assert "fought through" not in out
    assert "goblin" not in out
    assert "succeeded at" not in out
    assert "spent" not in out


# ---------------------------------------------------------------------------
# Structural / regression tests
# ---------------------------------------------------------------------------


class TestDeterminism:
    def test_same_state_same_string(self) -> None:
        state = kira_canonical_state()
        first = render_character(state)
        for _ in range(50):
            assert render_character(state) == first

    def test_pure_function_no_side_effects(self) -> None:
        """Frozen Pydantic + pure function: two independent calls must agree."""
        state = kira_canonical_state()
        assert render_character(state) == render_character(state)


class TestStructure:
    def test_starts_with_identity_line(self) -> None:
        out = render_character(kira_canonical_state())
        assert out.startswith("Kira is a scout of the Ridgewatch company.")

    def test_descriptor_sentence_present(self) -> None:
        out = render_character(kira_canonical_state())
        # mid(STR) / very-high(DEX) / high(WIS)
        assert "Kira is sturdy, preternaturally quick, and shrewd." in out

    def test_traits_alphabetical(self) -> None:
        out = render_character(kira_canonical_state())
        # (bold, loyal, impatient) -> sorted: bold, impatient, loyal
        assert "Kira is bold, impatient, and loyal." in out

    def test_legacy_combat_engagements_rendered(self) -> None:
        out = render_character(kira_canonical_state())
        assert (
            "She has fought through forty-seven goblin engagements, "
            "twelve beast hunts, and three skirmishes with undead."
        ) in out

    def test_legacy_action_counters_rendered(self) -> None:
        out = render_character(kira_canonical_state())
        assert "succeeded at stealth approaches eight times and failed three" in out

    def test_legacy_professions_rendered(self) -> None:
        out = render_character(kira_canonical_state())
        assert "eighty hours scouting" in out
        assert "one thousand two hundred forty hours at the forge" in out

    def test_legacy_locations_rendered(self) -> None:
        out = render_character(kira_canonical_state())
        assert (
            "She knows forest terrain well, dungeon corridors moderately, "
            "and town streets best of all."
        ) in out

    def test_episodic_memory_rendered(self) -> None:
        out = render_character(kira_canonical_state())
        assert (
            "Kira's defining moments include: "
            "her first kill, a goblin scout in the Hollowwood; "
            "the night she lost Brann to ghouls in the Catacombs of Vesh; "
            "the day she spared the bandit captain at Fenmarch crossing."
        ) in out

    def test_bonds_rendered_with_dead_partner(self) -> None:
        out = render_character(kira_canonical_state())
        assert "Kira trusts Brann deeply, though Brann is dead." in out
        assert "Kira is wary of Halden the quartermaster." in out
        assert "Kira has no strong feeling toward Mira the cleric." in out


class TestEdgeCases:
    def _bare_state(self, **overrides: object) -> CharacterState:
        defaults: dict[str, object] = {
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
        return CharacterState(**defaults)  # type: ignore[arg-type]

    def test_empty_bonds_paragraph_dropped(self) -> None:
        out = render_character(self._bare_state())
        assert "trusts" not in out
        assert "wary of" not in out
        assert "has no strong feeling toward" not in out

    def test_empty_episodic_memory_dropped(self) -> None:
        out = render_character(self._bare_state())
        assert "defining moments" not in out

    def test_empty_legacy_dropped(self) -> None:
        out = render_character(self._bare_state())
        assert "fought through" not in out
        assert "succeeded at" not in out
        assert "spent" not in out

    def test_empty_equipment_dropped(self) -> None:
        out = render_character(self._bare_state())
        assert "carries" not in out

    def test_zero_count_combat_omitted(self) -> None:
        # Lock option (a) Varn 2026-04-30 sec. 6: zero counts are omitted.
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
        # D is 4th by |value| — must be absent
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
        # Top 3 by salience desc; lower event_id wins ties (event 2 before event 3)
        assert "topa" in out and "topb" in out and "midd" in out
        assert "lowsa" not in out
        assert out.index("topa") < out.index("topb")


@pytest.mark.parametrize("n", range(20))
def test_idempotent_repeated_calls(n: int) -> None:
    """Sanity: render N times and assert byte-equality."""
    state = kira_canonical_state()
    base = render_character(state)
    for _ in range(n):
        assert render_character(state) == base
