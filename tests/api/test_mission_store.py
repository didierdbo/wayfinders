"""Tests for ``MissionStore`` (``wayfinders.api.mission_store``).

Covers:
  - Empty store returns empty list.
  - add() then active() returns the mission.
  - remove() then active() no longer returns the mission.
  - Ordering by spawn tick ascending.
  - Idempotent add (same id replaces).
  - Idempotent remove (absent id is a no-op, returns False).
  - clear() empties the store.
  - len() reflects the store size.
"""

from __future__ import annotations

from wayfinders.api.mission_store import MissionStore
from wayfinders.api.world_tick_models import EmergentMission

# ---------------------------------------------------------------------------
# Helper: minimal valid EmergentMission fixture
# ---------------------------------------------------------------------------


def _make_mission(
    mission_id: str,
    mission_type: str = "scout_route",
    target_poi: str = "e2.halfgate.gateway",
    seed: int = 0,
) -> EmergentMission:
    return EmergentMission(
        id=mission_id,
        type=mission_type,
        narrative_hook="A matter of route scouting in halfgate — the road ahead is uncharted.",
        eligible_personas=(),
        difficulty="mid",
        region="halfgate",
        target_poi=target_poi,
        deadline_ticks=None,
        outcome=None,
        seed=seed,
    )


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestMissionStoreEmpty:
    def test_active_empty_on_init(self) -> None:
        store = MissionStore()
        assert store.active() == []

    def test_len_zero_on_init(self) -> None:
        store = MissionStore()
        assert len(store) == 0


class TestMissionStoreAdd:
    def test_add_then_active_returns_mission(self) -> None:
        store = MissionStore()
        m = _make_mission("mission-1")
        store.add(m, spawn_tick=5)
        active = store.active()
        assert len(active) == 1
        assert active[0].id == "mission-1"

    def test_add_increments_len(self) -> None:
        store = MissionStore()
        store.add(_make_mission("mission-a"), spawn_tick=1)
        assert len(store) == 1
        store.add(_make_mission("mission-b"), spawn_tick=2)
        assert len(store) == 2

    def test_add_idempotent_same_id(self) -> None:
        """Adding the same id twice replaces the entry (idempotent)."""
        store = MissionStore()
        m = _make_mission("mission-dup", mission_type="scout_route")
        store.add(m, spawn_tick=5)
        m2 = _make_mission("mission-dup", mission_type="parley_local")
        store.add(m2, spawn_tick=5)
        # Still only one entry.
        assert len(store) == 1
        active = store.active()
        assert active[0].type == "parley_local"  # second write wins


class TestMissionStoreRemove:
    def test_remove_after_add_makes_active_empty(self) -> None:
        store = MissionStore()
        store.add(_make_mission("mission-r"), spawn_tick=3)
        removed = store.remove("mission-r")
        assert removed is True
        assert store.active() == []

    def test_remove_returns_false_for_unknown_id(self) -> None:
        store = MissionStore()
        assert store.remove("no-such-id") is False

    def test_remove_idempotent(self) -> None:
        """Removing an already-removed id returns False and does not raise."""
        store = MissionStore()
        store.add(_make_mission("mission-x"), spawn_tick=1)
        assert store.remove("mission-x") is True
        assert store.remove("mission-x") is False

    def test_remove_only_removes_target(self) -> None:
        """Removing one mission must not affect others."""
        store = MissionStore()
        store.add(_make_mission("m-keep"), spawn_tick=1)
        store.add(_make_mission("m-remove"), spawn_tick=2)
        store.remove("m-remove")
        active = store.active()
        assert len(active) == 1
        assert active[0].id == "m-keep"


class TestMissionStoreOrdering:
    def test_active_ordered_by_spawn_tick_ascending(self) -> None:
        """Missions must be returned in ascending spawn_tick order."""
        store = MissionStore()
        # Insert in reverse tick order.
        store.add(_make_mission("m-tick-10"), spawn_tick=10)
        store.add(_make_mission("m-tick-1"), spawn_tick=1)
        store.add(_make_mission("m-tick-5"), spawn_tick=5)
        active = store.active()
        ticks_in_order = [m.id for m in active]
        assert ticks_in_order == ["m-tick-1", "m-tick-5", "m-tick-10"]

    def test_active_tick_1_first(self) -> None:
        """tick=1 mission (Varn-lock first-window) must appear first in the list."""
        store = MissionStore()
        store.add(_make_mission("late"), spawn_tick=7)
        store.add(_make_mission("early"), spawn_tick=1)
        active = store.active()
        assert active[0].id == "early"


class TestMissionStoreClear:
    def test_clear_empties_store(self) -> None:
        store = MissionStore()
        store.add(_make_mission("m1"), spawn_tick=1)
        store.add(_make_mission("m2"), spawn_tick=2)
        store.clear()
        assert len(store) == 0
        assert store.active() == []


class TestMissionStoreActiveFilter:
    def test_resolved_mission_not_in_active(self) -> None:
        """After remove (resolution), mission must not appear in active()."""
        store = MissionStore()
        store.add(_make_mission("resolved-mission"), spawn_tick=3)
        store.remove("resolved-mission")
        ids = {m.id for m in store.active()}
        assert "resolved-mission" not in ids

    def test_multiple_missions_all_appear(self) -> None:
        store = MissionStore()
        for i in range(5):
            store.add(_make_mission(f"m-{i}"), spawn_tick=i + 1)
        assert len(store.active()) == 5

    def test_target_poi_preserved_through_store(self) -> None:
        """target_poi stored via add() must come out unchanged from active()."""
        store = MissionStore()
        m = _make_mission("poi-test", target_poi="e2.halfgate.intramuros")
        store.add(m, spawn_tick=1)
        assert store.active()[0].target_poi == "e2.halfgate.intramuros"
