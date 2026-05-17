"""Tests for MissionStore cap enforcement and new M1 methods (§A.8.D3/D5).

Covers:
  - ``try_add()`` returns False when recruit cap (N=3) is reached.
  - ``try_add()`` returns True and adds when cap not reached.
  - Idempotent replace via ``try_add()`` (same id = always True).
  - ``accept()`` / ``is_accepted()`` / ``get()`` helpers.
  - ``active_recruit_npc_ids()`` projection.
  - Cap does not apply to non-recruit types (forward-compat guard for M2).
"""

from __future__ import annotations

from wayfinders.api.mission_store import RECRUIT_MISSION_CAP, MissionStore
from wayfinders.api.world_tick_models import EmergentMission

# ---------------------------------------------------------------------------
# Helper: minimal valid EmergentMission
# ---------------------------------------------------------------------------


def _make_recruit(
    mission_id: str,
    npc_id: str,
    district: str = "gateway",
    seed: int = 0,
) -> EmergentMission:
    return EmergentMission(
        id=mission_id,
        type="recruit",
        narrative_hook="A recruit hook.",
        eligible_personas=(),
        difficulty="mid",
        region="halfgate",
        target_poi=f"e2.halfgate.{district}",
        deadline_ticks=None,
        outcome=None,
        seed=seed,
        recruit_target_npc_id=npc_id,
    )


def _make_scout(mission_id: str, seed: int = 0) -> EmergentMission:
    """M2+ type for forward-compat cap tests."""
    return EmergentMission(
        id=mission_id,
        type="scout_route",
        narrative_hook="A scout hook.",
        eligible_personas=(),
        difficulty="mid",
        region="halfgate",
        target_poi="e2.halfgate.gateway",
        deadline_ticks=None,
        outcome=None,
        seed=seed,
        recruit_target_npc_id=None,
    )


# ---------------------------------------------------------------------------
# Cap constant
# ---------------------------------------------------------------------------


class TestRecruitMissionCapConstant:
    def test_cap_is_3(self) -> None:
        """RECRUIT_MISSION_CAP must equal 3 (§A.8.D3)."""
        assert RECRUIT_MISSION_CAP == 3


# ---------------------------------------------------------------------------
# try_add — happy path
# ---------------------------------------------------------------------------


class TestTryAdd:
    def test_try_add_first_mission_returns_true(self) -> None:
        store = MissionStore()
        m = _make_recruit("m-1", npc_id="kira")
        result = store.try_add(m, spawn_tick=1)
        assert result is True
        assert len(store) == 1

    def test_try_add_three_missions_all_succeed(self) -> None:
        store = MissionStore()
        assert store.try_add(_make_recruit("m-1", "kira"), spawn_tick=1) is True
        assert (
            store.try_add(_make_recruit("m-2", "dorn", district="intramuros"), spawn_tick=2) is True
        )
        assert (
            store.try_add(_make_recruit("m-3", "vell", district="littoral"), spawn_tick=3) is True
        )
        assert len(store) == 3

    def test_try_add_fourth_recruit_returns_false(self) -> None:
        """Fourth recruit mission must be silently refused (cap = 3)."""
        store = MissionStore()
        store.try_add(_make_recruit("m-1", "kira"), spawn_tick=1)
        store.try_add(_make_recruit("m-2", "dorn", district="intramuros"), spawn_tick=2)
        store.try_add(_make_recruit("m-3", "vell", district="littoral"), spawn_tick=3)
        result = store.try_add(_make_recruit("m-4", "extra_npc", district="gateway"), spawn_tick=4)
        assert result is False
        assert len(store) == 3  # store unchanged

    def test_try_add_cap_does_not_apply_to_accepted_missions(self) -> None:
        """Accepted missions count toward the cap (they are still active)."""
        store = MissionStore()
        m1 = _make_recruit("m-1", "kira")
        m2 = _make_recruit("m-2", "dorn", district="intramuros")
        m3 = _make_recruit("m-3", "vell", district="littoral")
        store.try_add(m1, spawn_tick=1)
        store.try_add(m2, spawn_tick=2)
        store.try_add(m3, spawn_tick=3)
        store.accept("m-1")  # accept one — still in store
        # Cap still full: 3 active (pending + accepted).
        result = store.try_add(_make_recruit("m-4", "extra"), spawn_tick=4)
        assert result is False

    def test_try_add_idempotent_same_id(self) -> None:
        """Replacing an existing id via try_add always returns True (not a cap violation)."""
        store = MissionStore()
        m = _make_recruit("m-dup", "kira")
        assert store.try_add(m, spawn_tick=1) is True
        assert store.try_add(m, spawn_tick=1) is True  # replace — no cap penalty
        assert len(store) == 1

    def test_try_add_cap_freed_after_remove(self) -> None:
        """After removing a mission, the cap slot is freed and try_add succeeds again."""
        store = MissionStore()
        store.try_add(_make_recruit("m-1", "kira"), spawn_tick=1)
        store.try_add(_make_recruit("m-2", "dorn", district="intramuros"), spawn_tick=2)
        store.try_add(_make_recruit("m-3", "vell", district="littoral"), spawn_tick=3)
        store.remove("m-1")  # free one slot
        result = store.try_add(_make_recruit("m-4", "kira", district="gateway"), spawn_tick=4)
        assert result is True
        assert len(store) == 3


# ---------------------------------------------------------------------------
# Cap does not block non-recruit types (forward-compat)
# ---------------------------------------------------------------------------


class TestTryAddNonRecruitBypassesCap:
    def test_scout_route_ignores_recruit_cap(self) -> None:
        """scout_route missions are not counted against the recruit cap."""
        store = MissionStore()
        # Fill recruit cap.
        store.try_add(_make_recruit("r-1", "kira"), spawn_tick=1)
        store.try_add(_make_recruit("r-2", "dorn", district="intramuros"), spawn_tick=2)
        store.try_add(_make_recruit("r-3", "vell", district="littoral"), spawn_tick=3)
        # A scout_route mission must still be admitted.
        result = store.try_add(_make_scout("s-1"), spawn_tick=4)
        assert result is True
        assert len(store) == 4


# ---------------------------------------------------------------------------
# accept / is_accepted / get
# ---------------------------------------------------------------------------


class TestMissionStoreAccept:
    def test_accept_returns_true_for_known_mission(self) -> None:
        store = MissionStore()
        store.add(_make_recruit("m-1", "kira"), spawn_tick=1)
        assert store.accept("m-1") is True

    def test_accept_returns_false_for_unknown_mission(self) -> None:
        store = MissionStore()
        assert store.accept("no-such-id") is False

    def test_accept_sets_accepted_flag(self) -> None:
        store = MissionStore()
        store.add(_make_recruit("m-1", "kira"), spawn_tick=1)
        assert store.is_accepted("m-1") is False
        store.accept("m-1")
        assert store.is_accepted("m-1") is True

    def test_accept_is_idempotent(self) -> None:
        """Calling accept twice must return True both times (no-op on second call)."""
        store = MissionStore()
        store.add(_make_recruit("m-1", "kira"), spawn_tick=1)
        assert store.accept("m-1") is True
        assert store.accept("m-1") is True
        assert store.is_accepted("m-1") is True

    def test_accepted_mission_still_in_active(self) -> None:
        """Accepted missions must still appear in active() (not removed)."""
        store = MissionStore()
        store.add(_make_recruit("m-1", "kira"), spawn_tick=1)
        store.accept("m-1")
        active = store.active()
        assert len(active) == 1
        assert active[0].id == "m-1"

    def test_is_accepted_false_for_unknown(self) -> None:
        store = MissionStore()
        assert store.is_accepted("no-such") is False


# ---------------------------------------------------------------------------
# active_recruit_npc_ids
# ---------------------------------------------------------------------------


class TestActiveRecruitNpcIds:
    def test_empty_store_returns_empty_frozenset(self) -> None:
        store = MissionStore()
        assert store.active_recruit_npc_ids() == frozenset()

    def test_returns_npc_ids_of_active_recruits(self) -> None:
        store = MissionStore()
        store.add(_make_recruit("m-1", "kira"), spawn_tick=1)
        store.add(_make_recruit("m-2", "dorn", district="intramuros"), spawn_tick=2)
        ids = store.active_recruit_npc_ids()
        assert ids == frozenset({"kira", "dorn"})

    def test_accepted_recruits_included(self) -> None:
        """Accepted missions are still active — their NPC ids must be in the projection."""
        store = MissionStore()
        store.add(_make_recruit("m-1", "kira"), spawn_tick=1)
        store.accept("m-1")
        assert "kira" in store.active_recruit_npc_ids()

    def test_removed_recruit_not_included(self) -> None:
        store = MissionStore()
        store.add(_make_recruit("m-1", "kira"), spawn_tick=1)
        store.remove("m-1")
        assert store.active_recruit_npc_ids() == frozenset()

    def test_non_recruit_types_not_included(self) -> None:
        """scout_route npc_id (None) must not appear in active_recruit_npc_ids."""
        store = MissionStore()
        store.add(_make_scout("s-1"), spawn_tick=1)
        assert store.active_recruit_npc_ids() == frozenset()


# ---------------------------------------------------------------------------
# get()
# ---------------------------------------------------------------------------


class TestMissionStoreGet:
    def test_get_returns_mission_when_present(self) -> None:
        store = MissionStore()
        m = _make_recruit("m-1", "kira")
        store.add(m, spawn_tick=1)
        result = store.get("m-1")
        assert result is not None
        assert result.id == "m-1"

    def test_get_returns_none_when_absent(self) -> None:
        store = MissionStore()
        assert store.get("no-such-id") is None
