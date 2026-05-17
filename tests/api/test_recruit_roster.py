"""Tests for the M1 recruit roster closed lookup (§A.8.D3, Varn-lock 2026-05-17).

Covers:
  - ``M1_RECRUIT_ROSTER``: 3 entries, distinct NpcIds, Varn-locked districts.
  - ``M1_EMITTED_TYPES``: contains only "recruit" for M1.
  - ``RecruitRosterEntry``: frozen dataclass contract.
  - Portrait key format contract.
  - Order lock: kira → dorn → vell (narrative arc).
"""

from __future__ import annotations

from wayfinders.api.recruit_roster import (
    M1_EMITTED_TYPES,
    M1_RECRUIT_ROSTER,
)


class TestRecruitRosterShape:
    def test_roster_has_three_entries(self) -> None:
        """M1 cap = 3 NPCs (one per distinct Halfgate district)."""
        assert len(M1_RECRUIT_ROSTER) == 3

    def test_npc_ids_are_distinct(self) -> None:
        """Each entry must have a unique npc_id."""
        ids = [entry.npc_id for entry in M1_RECRUIT_ROSTER]
        assert len(ids) == len(set(ids)), f"duplicate npc_ids: {ids}"

    def test_home_districts_are_distinct(self) -> None:
        """Each NPC must be anchored to a distinct district (one mission per district)."""
        districts = [entry.home_district for entry in M1_RECRUIT_ROSTER]
        assert len(districts) == len(set(districts)), f"duplicate districts: {districts}"

    def test_districts_are_varn_locked_set(self) -> None:
        """Districts must be exactly {gateway, intramuros, littoral} (E2.1c lock)."""
        districts = {entry.home_district for entry in M1_RECRUIT_ROSTER}
        expected = {"gateway", "intramuros", "littoral"}
        assert districts == expected, f"expected {expected}, got {districts}"

    def test_kira_first_in_roster_order(self) -> None:
        """kira must be the first entry (designed narrative arc: scout archetype first)."""
        assert M1_RECRUIT_ROSTER[0].npc_id == "kira", (
            f"expected kira first, got {M1_RECRUIT_ROSTER[0].npc_id!r}"
        )

    def test_dorn_second_in_roster_order(self) -> None:
        """dorn must be second (civic figure, intramuros)."""
        assert M1_RECRUIT_ROSTER[1].npc_id == "dorn", (
            f"expected dorn second, got {M1_RECRUIT_ROSTER[1].npc_id!r}"
        )

    def test_vell_third_in_roster_order(self) -> None:
        """vell must be third (dockyard outsider, littoral)."""
        assert M1_RECRUIT_ROSTER[2].npc_id == "vell", (
            f"expected vell third, got {M1_RECRUIT_ROSTER[2].npc_id!r}"
        )


class TestRecruitRosterNpcDistricts:
    def test_kira_district_is_gateway(self) -> None:
        """kira home district must be gateway (Varn-lock §A.8.D3)."""
        kira = next(e for e in M1_RECRUIT_ROSTER if e.npc_id == "kira")
        assert kira.home_district == "gateway"

    def test_dorn_district_is_intramuros(self) -> None:
        """dorn home district must be intramuros (Varn-lock §A.8.D3)."""
        dorn = next(e for e in M1_RECRUIT_ROSTER if e.npc_id == "dorn")
        assert dorn.home_district == "intramuros"

    def test_vell_district_is_littoral(self) -> None:
        """vell home district must be littoral (Varn-lock §A.8.D3)."""
        vell = next(e for e in M1_RECRUIT_ROSTER if e.npc_id == "vell")
        assert vell.home_district == "littoral"


class TestRecruitRosterPortraitKeys:
    def test_kira_portrait_key(self) -> None:
        kira = next(e for e in M1_RECRUIT_ROSTER if e.npc_id == "kira")
        assert kira.portrait_key == "e4.portrait.kira.calm"

    def test_dorn_portrait_key(self) -> None:
        dorn = next(e for e in M1_RECRUIT_ROSTER if e.npc_id == "dorn")
        assert dorn.portrait_key == "e4.portrait.dorn.calm"

    def test_vell_portrait_key(self) -> None:
        vell = next(e for e in M1_RECRUIT_ROSTER if e.npc_id == "vell")
        assert vell.portrait_key == "e4.portrait.vell.calm"

    def test_portrait_key_format(self) -> None:
        """Portrait keys must follow the e4.portrait.{npc_id}.{state} format."""
        import re

        pattern = re.compile(r"^e4\.portrait\.[a-z]+\.[a-z]+$")
        for entry in M1_RECRUIT_ROSTER:
            assert pattern.match(entry.portrait_key), (
                f"{entry.npc_id}: portrait_key {entry.portrait_key!r} "
                f"does not match expected format 'e4.portrait.<npc>.<state>'"
            )


class TestRecruitRosterImmutability:
    def test_roster_entry_is_frozen(self) -> None:
        """RecruitRosterEntry must be immutable (frozen dataclass)."""
        entry = M1_RECRUIT_ROSTER[0]
        try:
            entry.npc_id = "tampered"  # type: ignore[misc]
            raise AssertionError("Expected FrozenInstanceError — roster entry must be frozen")
        except (AttributeError, TypeError):
            pass  # frozen dataclass raises one of these on assignment

    def test_roster_is_tuple(self) -> None:
        """M1_RECRUIT_ROSTER must be a tuple (immutable, ordered)."""
        assert isinstance(M1_RECRUIT_ROSTER, tuple)


class TestM1EmittedTypes:
    def test_m1_emitted_types_contains_only_recruit(self) -> None:
        """M1_EMITTED_TYPES must contain only 'recruit' (§A.8.D3 lock)."""
        assert frozenset({"recruit"}) == M1_EMITTED_TYPES

    def test_m1_emitted_types_is_frozenset(self) -> None:
        """M1_EMITTED_TYPES must be a frozenset (immutable)."""
        assert isinstance(M1_EMITTED_TYPES, frozenset)

    def test_scout_route_not_in_m1_emitted(self) -> None:
        """scout_route must NOT be in M1_EMITTED_TYPES (M2+ only)."""
        assert "scout_route" not in M1_EMITTED_TYPES

    def test_parley_local_not_in_m1_emitted(self) -> None:
        """parley_local must NOT be in M1_EMITTED_TYPES (M2+ only)."""
        assert "parley_local" not in M1_EMITTED_TYPES
