"""Tests for ``_pick_recruit_mission`` (§A.8.D3, Varn-lock 2026-05-17).

Covers:
  - Deterministic order: kira → dorn → vell (roster declaration order).
  - Cap N=3: returns None when all 3 roster NPCs are already targeted.
  - One-per-NPC: never picks a NPC that already has an active mission.
  - Region → e2 POI for halfgate; e1 fallback for other regions.
  - recruit_target_npc_id field is always set.
  - test_recruit_roster_order_deterministic (Varn pin from §A.8.D3).
"""

from __future__ import annotations

from wayfinders.api.world_tick import _pick_recruit_mission
from wayfinders.api.world_tick_models import EmergentMission

# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------


def _pick(
    already: frozenset[str] | None = None,
    region: str = "halfgate",
    tick: int = 1,
    seed: int = 42,
) -> EmergentMission | None:
    return _pick_recruit_mission(
        region=region,  # type: ignore[arg-type]
        already_targeted_npc_ids=already or frozenset(),
        tick=tick,
        seed=seed,
    )


# ---------------------------------------------------------------------------
# 1. Deterministic order (Varn pin: test_recruit_roster_order_deterministic)
# ---------------------------------------------------------------------------


class TestPickRecruitMissionDeterministicOrder:
    def test_first_pick_is_kira(self) -> None:
        """With empty store, first pick must be kira (roster position 0)."""
        mission = _pick(already=frozenset())
        assert mission is not None
        assert mission.recruit_target_npc_id == "kira"

    def test_second_pick_is_dorn_when_kira_targeted(self) -> None:
        """When kira is already targeted, second pick must be dorn (roster position 1)."""
        mission = _pick(already=frozenset({"kira"}))
        assert mission is not None
        assert mission.recruit_target_npc_id == "dorn"

    def test_third_pick_is_vell_when_kira_and_dorn_targeted(self) -> None:
        """When kira and dorn are targeted, third pick must be vell (roster position 2)."""
        mission = _pick(already=frozenset({"kira", "dorn"}))
        assert mission is not None
        assert mission.recruit_target_npc_id == "vell"

    def test_roster_order_deterministic_across_seeds(self) -> None:
        """Same already_targeted set → same pick regardless of seed (no RNG in picker)."""
        for seed in [0, 7, 42, 99, 1337]:
            m_a = _pick(already=frozenset(), seed=seed)
            assert m_a is not None
            assert m_a.recruit_target_npc_id == "kira", (
                f"seed={seed}: expected kira first, got {m_a.recruit_target_npc_id!r}"
            )

    def test_same_inputs_same_mission_id(self) -> None:
        """Same (already_targeted, tick, seed) must produce the same mission id."""
        m1 = _pick(already=frozenset(), tick=5, seed=42)
        m2 = _pick(already=frozenset(), tick=5, seed=42)
        assert m1 is not None
        assert m2 is not None
        assert m1.id == m2.id


# ---------------------------------------------------------------------------
# 2. Cap enforcement: None when all 3 NPCs are already targeted
# ---------------------------------------------------------------------------


class TestPickRecruitMissionCap:
    def test_returns_none_when_all_three_targeted(self) -> None:
        """Returns None when kira, dorn, and vell are all already targeted."""
        mission = _pick(already=frozenset({"kira", "dorn", "vell"}))
        assert mission is None

    def test_returns_none_when_cap_full_any_order(self) -> None:
        """Cap applies regardless of the order the NPC ids appear in the set."""
        for already in [
            frozenset({"kira", "dorn", "vell"}),
            frozenset({"vell", "kira", "dorn"}),
            frozenset({"dorn", "vell", "kira"}),
        ]:
            assert _pick(already=already) is None, f"Expected None for already_targeted={already}"

    def test_returns_mission_when_one_slot_open(self) -> None:
        """As long as at least one NPC is untargeted, a mission is returned."""
        # Only vell is untargeted
        mission = _pick(already=frozenset({"kira", "dorn"}))
        assert mission is not None
        assert mission.recruit_target_npc_id == "vell"

    def test_unknown_npc_id_in_already_targeted_does_not_block(self) -> None:
        """An unknown NPC id in already_targeted must not prevent valid picks."""
        # "zarkon" is not in the roster — the filter should skip it cleanly.
        mission = _pick(already=frozenset({"zarkon"}))
        assert mission is not None
        assert mission.recruit_target_npc_id == "kira"  # first in roster


# ---------------------------------------------------------------------------
# 3. One-per-NPC contract
# ---------------------------------------------------------------------------


class TestPickRecruitMissionOnePerNpc:
    def test_kira_targeted_skips_kira(self) -> None:
        mission = _pick(already=frozenset({"kira"}))
        assert mission is not None
        assert mission.recruit_target_npc_id != "kira"

    def test_dorn_targeted_skips_dorn(self) -> None:
        mission = _pick(already=frozenset({"dorn"}))
        assert mission is not None
        assert mission.recruit_target_npc_id != "dorn"
        assert mission.recruit_target_npc_id == "kira"  # kira is first

    def test_vell_targeted_skips_vell(self) -> None:
        mission = _pick(already=frozenset({"vell"}))
        assert mission is not None
        assert mission.recruit_target_npc_id != "vell"
        assert mission.recruit_target_npc_id == "kira"  # kira is still first


# ---------------------------------------------------------------------------
# 4. POI targeting — e2 for halfgate, e1 fallback for other regions
# ---------------------------------------------------------------------------


class TestPickRecruitMissionPOI:
    def test_halfgate_produces_e2_poi(self) -> None:
        """Halfgate region must produce e2.halfgate.{district}."""
        mission = _pick(region="halfgate")
        assert mission is not None
        assert mission.target_poi.startswith("e2.halfgate."), (
            f"expected e2.halfgate.*, got {mission.target_poi!r}"
        )

    def test_halfgate_kira_poi_is_gateway(self) -> None:
        """kira's home district is gateway → poi must be e2.halfgate.gateway."""
        mission = _pick(region="halfgate", already=frozenset())
        assert mission is not None
        assert mission.recruit_target_npc_id == "kira"
        assert mission.target_poi == "e2.halfgate.gateway"

    def test_halfgate_dorn_poi_is_intramuros(self) -> None:
        """dorn's home district is intramuros → poi must be e2.halfgate.intramuros."""
        mission = _pick(region="halfgate", already=frozenset({"kira"}))
        assert mission is not None
        assert mission.recruit_target_npc_id == "dorn"
        assert mission.target_poi == "e2.halfgate.intramuros"

    def test_halfgate_vell_poi_is_littoral(self) -> None:
        """vell's home district is littoral → poi must be e2.halfgate.littoral."""
        mission = _pick(region="halfgate", already=frozenset({"kira", "dorn"}))
        assert mission is not None
        assert mission.recruit_target_npc_id == "vell"
        assert mission.target_poi == "e2.halfgate.littoral"

    def test_brescaille_falls_back_to_e1(self) -> None:
        """Non-halfgate regions have no visible E2 districts → e1 fallback."""
        mission = _pick(region="brescaille")  # type: ignore[arg-type]
        assert mission is not None
        assert mission.target_poi == "e1.brescaille"

    def test_fendelune_falls_back_to_e1(self) -> None:
        mission = _pick(region="fendelune")  # type: ignore[arg-type]
        assert mission is not None
        assert mission.target_poi == "e1.fendelune"

    def test_non_halfgate_still_sets_recruit_target_npc_id(self) -> None:
        """Even for non-halfgate regions, recruit_target_npc_id must be set."""
        mission = _pick(region="brescaille")  # type: ignore[arg-type]
        assert mission is not None
        assert mission.recruit_target_npc_id is not None
        assert mission.recruit_target_npc_id == "kira"


# ---------------------------------------------------------------------------
# 5. Mission type and field contract
# ---------------------------------------------------------------------------


class TestPickRecruitMissionContract:
    def test_mission_type_is_recruit(self) -> None:
        mission = _pick()
        assert mission is not None
        assert mission.type == "recruit"

    def test_recruit_target_npc_id_always_set(self) -> None:
        """recruit_target_npc_id must never be None for a recruit mission."""
        for already in [
            frozenset(),
            frozenset({"kira"}),
            frozenset({"kira", "dorn"}),
        ]:
            mission = _pick(already=already)
            assert mission is not None
            assert mission.recruit_target_npc_id is not None, (
                f"already={already}: recruit_target_npc_id must not be None"
            )

    def test_difficulty_forced_mid(self) -> None:
        mission = _pick()
        assert mission is not None
        assert mission.difficulty == "mid"

    def test_deadline_ticks_is_none(self) -> None:
        mission = _pick()
        assert mission is not None
        assert mission.deadline_ticks is None

    def test_outcome_is_none_at_emergence(self) -> None:
        mission = _pick()
        assert mission is not None
        assert mission.outcome is None

    def test_eligible_personas_is_empty_tuple(self) -> None:
        """Recruit missions do not gate on persona eligibility in M1."""
        mission = _pick()
        assert mission is not None
        assert mission.eligible_personas == ()

    def test_narrative_hook_is_non_empty(self) -> None:
        mission = _pick()
        assert mission is not None
        assert isinstance(mission.narrative_hook, str)
        assert len(mission.narrative_hook) > 0

    def test_region_echoed_in_mission(self) -> None:
        mission = _pick(region="halfgate")
        assert mission is not None
        assert mission.region == "halfgate"
