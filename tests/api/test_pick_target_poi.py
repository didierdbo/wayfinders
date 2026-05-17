"""Tests for ``_pick_target_poi``, ``VISIBLE_E2_DISTRICTS_BY_REGION``,
``MISSION_TYPE_DISTRICT_AFFINITY``, and the tick==1 cadence gate.

Covers Varn-lock 2026-05-17 §A:
  - Decision 1: anchor at e2 layer for halfgate, e1 fallback for other regions.
  - Decision 3: tick 1 is an unconditional first cadence window.
  - §A.2 Q1 override: affinity ORDER is Varn-locked.
  - Determinism contract: same (seed, mission_type, region) → same target_poi.
  - Affinity distribution: scout_route x halfgate x many seeds → gateway dominant.
  - WAYFINDERS_DEV_MISSION_SEED env var bypass.
"""

from __future__ import annotations

import os
import re
from unittest.mock import MagicMock, patch

from wayfinders.api.world_tick import (
    MISSION_TYPE_DISTRICT_AFFINITY,
    VISIBLE_E2_DISTRICTS_BY_REGION,
    EmergenceEngine,
    _in_cadence_window,
    _pick_target_poi,
)
from wayfinders.api.world_tick_models import WorldTickRequest

# ---------------------------------------------------------------------------
# Sample prose — same style as other test files
# ---------------------------------------------------------------------------

_CONTEXT_HALFGATE = (
    "The mission is a reconnaissance near Halfgate.\n"
    "The mission objective is to map the road beyond the ridge before dawn.\n"
    "The stakes are high: failure means the company moves blind into the valley."
)

_CONTEXT_BRESCAILLE = (
    "The mission is a negotiation near Brescaille.\n"
    "The mission objective is to broker a ceasefire before the winter market.\n"
    "The stakes are moderate: a failed parley delays the company by weeks."
)


# ---------------------------------------------------------------------------
# 1. Varn-locked closed lookup shape
# ---------------------------------------------------------------------------


class TestClosedLookupShape:
    def test_visible_e2_districts_halfgate_has_three_districts(self) -> None:
        """Halfgate must have exactly 3 visible E2 districts (E2.1c lock)."""
        assert "halfgate" in VISIBLE_E2_DISTRICTS_BY_REGION
        assert len(VISIBLE_E2_DISTRICTS_BY_REGION["halfgate"]) == 3

    def test_visible_e2_districts_halfgate_slugs(self) -> None:
        """Halfgate districts must be exactly {intramuros, gateway, littoral}."""
        districts = set(VISIBLE_E2_DISTRICTS_BY_REGION["halfgate"])
        assert districts == {"intramuros", "gateway", "littoral"}

    def test_visible_e2_districts_other_regions_absent(self) -> None:
        """Other M1 regions must NOT be in VISIBLE_E2_DISTRICTS_BY_REGION (M2 scope)."""
        m1_non_halfgate = {"brescaille", "fendelune", "veillemont", "roches-closes"}
        for region in m1_non_halfgate:
            assert region not in VISIBLE_E2_DISTRICTS_BY_REGION, (
                f"{region!r} should not be in VISIBLE_E2_DISTRICTS_BY_REGION in M1"
            )

    def test_affinity_scout_route_order_locked(self) -> None:
        """scout_route affinity order must be gateway-first (Varn-lock Q1 override)."""
        affinity = MISSION_TYPE_DISTRICT_AFFINITY["scout_route"]
        assert affinity[0] == "gateway", (
            f"scout_route must prefer gateway first — got {affinity[0]!r}"
        )

    def test_affinity_parley_local_order_locked(self) -> None:
        """parley_local affinity order must be intramuros-first (Varn-lock Q1 override)."""
        affinity = MISSION_TYPE_DISTRICT_AFFINITY["parley_local"]
        assert affinity[0] == "intramuros", (
            f"parley_local must prefer intramuros first — got {affinity[0]!r}"
        )

    def test_affinity_both_types_present(self) -> None:
        """Both mission types must be in MISSION_TYPE_DISTRICT_AFFINITY."""
        assert "scout_route" in MISSION_TYPE_DISTRICT_AFFINITY
        assert "parley_local" in MISSION_TYPE_DISTRICT_AFFINITY

    def test_affinity_lists_cover_all_visible_districts(self) -> None:
        """Each affinity list must cover all visible districts for halfgate."""
        visible = set(VISIBLE_E2_DISTRICTS_BY_REGION["halfgate"])
        for mtype, affinity in MISSION_TYPE_DISTRICT_AFFINITY.items():
            affinity_set = set(affinity)
            assert visible.issubset(affinity_set), (
                f"{mtype!r} affinity {affinity} does not cover all visible districts {visible}"
            )


# ---------------------------------------------------------------------------
# 2. _pick_target_poi — layer choice
# ---------------------------------------------------------------------------


class TestPickTargetPoi:
    _POI_PATTERN = re.compile(r"^e[1-3]\.[a-z0-9\-]+(\.[a-z0-9\-]+){0,2}$")

    def test_halfgate_returns_e2_layer(self) -> None:
        """halfgate region must produce an e2.* PoiId."""
        poi = _pick_target_poi("scout_route", "halfgate", seed=42)
        assert poi.startswith("e2.halfgate."), f"expected e2.halfgate.*, got {poi!r}"

    def test_halfgate_district_in_visible_set(self) -> None:
        """The picked district must be in VISIBLE_E2_DISTRICTS_BY_REGION['halfgate']."""
        visible = set(VISIBLE_E2_DISTRICTS_BY_REGION["halfgate"])
        for seed in range(20):
            poi = _pick_target_poi("scout_route", "halfgate", seed=seed)
            district = poi.split(".")[2]
            assert district in visible, (
                f"seed={seed}: district {district!r} not in visible set {visible}"
            )

    def test_brescaille_fallback_e1(self) -> None:
        assert _pick_target_poi("scout_route", "brescaille", seed=0) == "e1.brescaille"

    def test_fendelune_fallback_e1(self) -> None:
        assert _pick_target_poi("parley_local", "fendelune", seed=0) == "e1.fendelune"

    def test_veillemont_fallback_e1(self) -> None:
        assert _pick_target_poi("scout_route", "veillemont", seed=0) == "e1.veillemont"

    def test_roches_closes_fallback_e1(self) -> None:
        assert _pick_target_poi("parley_local", "roches-closes", seed=0) == "e1.roches-closes"

    def test_poi_matches_schema_pattern_e2(self) -> None:
        """e2.halfgate.* result must match PoiId regex."""
        poi = _pick_target_poi("parley_local", "halfgate", seed=7)
        assert self._POI_PATTERN.match(poi), f"{poi!r} does not match PoiId regex"

    def test_poi_matches_schema_pattern_e1(self) -> None:
        """e1.brescaille fallback must match PoiId regex."""
        poi = _pick_target_poi("scout_route", "brescaille", seed=7)
        assert self._POI_PATTERN.match(poi), f"{poi!r} does not match PoiId regex"

    def test_determinism_same_inputs_same_poi(self) -> None:
        """Same (seed, mission_type, region) must always yield the same PoiId."""
        for seed in [0, 1, 42, 99, 1337]:
            for mtype in ("scout_route", "parley_local"):
                poi_a = _pick_target_poi(mtype, "halfgate", seed=seed)
                poi_b = _pick_target_poi(mtype, "halfgate", seed=seed)
                assert poi_a == poi_b, (
                    f"seed={seed} mtype={mtype}: non-deterministic {poi_a!r} vs {poi_b!r}"
                )

    def test_different_seeds_can_produce_different_districts(self) -> None:
        """Different seeds should be able to produce different districts for halfgate."""
        # Over enough seeds, we expect to see multiple distinct districts.
        districts_seen: set[str] = set()
        for seed in range(100):
            poi = _pick_target_poi("scout_route", "halfgate", seed=seed)
            districts_seen.add(poi.split(".")[2])
        assert len(districts_seen) > 1, (
            f"Expected multiple distinct districts across 100 seeds, got: {districts_seen}"
        )


# ---------------------------------------------------------------------------
# 3. Affinity distribution — scout_route x halfgate x 100 seeds
# ---------------------------------------------------------------------------


class TestAffinityDistribution:
    def test_scout_route_halfgate_all_districts_reachable(self) -> None:
        """scout_route x halfgate: all three districts must be reachable across seeds.

        The seeded RNG draws uniformly from the eligible set (affinity ∩ visible).
        All three are eligible, so all three must appear over enough seeds.
        """
        districts_seen: set[str] = set()
        for seed in range(100):
            poi = _pick_target_poi("scout_route", "halfgate", seed=seed)
            districts_seen.add(poi.split(".")[-1])
        expected = {"gateway", "littoral", "intramuros"}
        assert districts_seen == expected, (
            f"scout_route: expected all districts reachable, got {districts_seen}"
        )

    def test_parley_local_halfgate_all_districts_reachable(self) -> None:
        """parley_local x halfgate: all three districts must be reachable across seeds."""
        districts_seen: set[str] = set()
        for seed in range(100):
            poi = _pick_target_poi("parley_local", "halfgate", seed=seed)
            districts_seen.add(poi.split(".")[-1])
        expected = {"gateway", "littoral", "intramuros"}
        assert districts_seen == expected, (
            f"parley_local: expected all districts reachable, got {districts_seen}"
        )

    def test_scout_route_non_intramuros_majority(self) -> None:
        """scout_route x halfgate: gateway+littoral combined must outnumber intramuros.

        The affinity order places gateway and littoral first — as the preferred
        peripheral/transit zones for scout_route.  The seeded RNG is uniform
        over all 3 eligible, so gateway+littoral should appear ~67% together.
        """
        non_intramuros = sum(
            1
            for seed in range(100)
            if not _pick_target_poi("scout_route", "halfgate", seed=seed).endswith(".intramuros")
        )
        assert non_intramuros >= 50, (
            f"scout_route: gateway+littoral appeared {non_intramuros}/100 times "
            f"(expected ≥ 50% combined)"
        )

    def test_parley_local_non_littoral_majority(self) -> None:
        """parley_local x halfgate: intramuros+gateway combined must outnumber littoral.

        The affinity order places intramuros and gateway first for parley_local
        (civic/named zones).  Combined, they should appear ~67%.
        """
        non_littoral = sum(
            1
            for seed in range(100)
            if not _pick_target_poi("parley_local", "halfgate", seed=seed).endswith(".littoral")
        )
        assert non_littoral >= 50, (
            f"parley_local: intramuros+gateway appeared {non_littoral}/100 times "
            f"(expected ≥ 50% combined)"
        )


# ---------------------------------------------------------------------------
# 4. Cadence gate — tick 1 unconditional window (Varn-lock 2026-05-17)
# ---------------------------------------------------------------------------


class TestCadenceTick1:
    def test_tick_1_is_always_in_window(self) -> None:
        """tick==1 must be in cadence window for every seed."""
        for seed in [0, 1, 7, 42, 99, 999, 0xDEAD_BEEF, 0xCAFE_BABE]:
            assert _in_cadence_window(1, seed), (
                f"seed={seed}: tick 1 must be an unconditional cadence window"
            )

    def test_tick_0_still_in_window(self) -> None:
        """tick 0 must still be in window (0 % interval == 0 for any interval)."""
        for seed in [0, 42, 99]:
            assert _in_cadence_window(0, seed), (
                f"seed={seed}: tick 0 must be in window (0 modulo rule)"
            )

    def test_tick_2_onwards_follows_normal_cadence(self) -> None:
        """For tick >= 2, cadence follows the 5-10 modulo rule (not forced)."""
        # With seed=0, the cadence interval is deterministic — find a tick > 1
        # that is NOT in the window for any seed (proves tick 1 special-casing
        # is the only forced exception).
        seed = 42
        non_window_found = False
        for tick in range(2, 200):
            if not _in_cadence_window(tick, seed):
                non_window_found = True
                break
        assert non_window_found, (
            "Expected to find a tick >= 2 outside the cadence window for seed=42"
        )

    def test_tick_1_engine_produces_mission(self) -> None:
        """EmergenceEngine at tick=1 must produce a mission (unconditional window)."""
        mock = MagicMock()
        mock.ready = True
        mock.predict.return_value = 1.5
        engine = EmergenceEngine(predictor=mock)

        req = WorldTickRequest(
            tick=1,
            seed=42,
            context_prose=_CONTEXT_HALFGATE,
        )
        resp = engine.process_tick(req)

        assert resp.mission is not None, "tick=1 must produce a mission (unconditional window)"
        assert resp.mission.type in ("scout_route", "parley_local")

    def test_tick_1_target_poi_is_e2_for_halfgate(self) -> None:
        """At tick=1, a halfgate context must produce an e2.halfgate.* target_poi."""
        mock = MagicMock()
        mock.ready = True
        mock.predict.return_value = 1.5
        engine = EmergenceEngine(predictor=mock)

        req = WorldTickRequest(
            tick=1,
            seed=42,
            context_prose=_CONTEXT_HALFGATE,
        )
        resp = engine.process_tick(req)

        assert resp.mission is not None
        assert resp.mission.target_poi.startswith("e2.halfgate."), (
            f"expected e2.halfgate.*, got {resp.mission.target_poi!r}"
        )

    def test_tick_1_target_poi_is_e1_for_brescaille(self) -> None:
        """At tick=1, a brescaille context must fall back to e1.brescaille."""
        mock = MagicMock()
        mock.ready = True
        mock.predict.return_value = 1.5
        engine = EmergenceEngine(predictor=mock)

        req = WorldTickRequest(
            tick=1,
            seed=42,
            context_prose=_CONTEXT_BRESCAILLE,
        )
        resp = engine.process_tick(req)

        assert resp.mission is not None
        assert resp.mission.target_poi == "e1.brescaille", (
            f"expected e1.brescaille, got {resp.mission.target_poi!r}"
        )


# ---------------------------------------------------------------------------
# 5. WAYFINDERS_DEV_MISSION_SEED env var
# ---------------------------------------------------------------------------


class TestDevSeedEnvVar:
    def test_dev_seed_bypasses_cadence(self) -> None:
        """With WAYFINDERS_DEV_MISSION_SEED set, every tick must produce a mission."""
        mock = MagicMock()
        mock.ready = True
        mock.predict.return_value = 0.5
        engine = EmergenceEngine(predictor=mock)

        # Pick ticks that would normally be outside the cadence window.
        seed = 42
        outside_ticks = [t for t in range(2, 100) if not _in_cadence_window(t, seed)]
        assert len(outside_ticks) >= 10, "Need enough non-window ticks to test bypass"

        with patch.dict(os.environ, {"WAYFINDERS_DEV_MISSION_SEED": "test_bypass"}):
            for tick in outside_ticks[:10]:
                req = WorldTickRequest(
                    tick=tick,
                    seed=seed,
                    context_prose=_CONTEXT_HALFGATE,
                )
                resp = engine.process_tick(req)
                assert resp.mission is not None, (
                    f"tick={tick}: WAYFINDERS_DEV_MISSION_SEED should bypass cadence gate"
                )

    def test_dev_seed_deterministic(self) -> None:
        """With same WAYFINDERS_DEV_MISSION_SEED, output must be deterministic."""
        mock = MagicMock()
        mock.ready = True
        mock.predict.return_value = 1.0
        engine = EmergenceEngine(predictor=mock)

        req = WorldTickRequest(tick=3, seed=7, context_prose=_CONTEXT_HALFGATE)

        with patch.dict(os.environ, {"WAYFINDERS_DEV_MISSION_SEED": "my_dev_seed"}):
            resp1 = engine.process_tick(req)
            resp2 = engine.process_tick(req)

        assert resp1.mission is not None
        assert resp2.mission is not None
        assert resp1.mission.id == resp2.mission.id
        assert resp1.mission.type == resp2.mission.type
        assert resp1.mission.target_poi == resp2.mission.target_poi

    def test_no_dev_seed_normal_cadence_preserved(self) -> None:
        """Without WAYFINDERS_DEV_MISSION_SEED, normal cadence applies."""
        mock = MagicMock()
        mock.ready = True
        mock.predict.return_value = 0.5
        engine = EmergenceEngine(predictor=mock)

        # Ensure the env var is absent.
        env_clean = {k: v for k, v in os.environ.items() if k != "WAYFINDERS_DEV_MISSION_SEED"}
        with patch.dict(os.environ, env_clean, clear=True):
            seed = 42
            non_window_ticks = [t for t in range(2, 50) if not _in_cadence_window(t, seed)]
            assert len(non_window_ticks) >= 5

            for tick in non_window_ticks[:5]:
                req = WorldTickRequest(
                    tick=tick,
                    seed=seed,
                    context_prose=_CONTEXT_HALFGATE,
                )
                resp = engine.process_tick(req)
                assert resp.mission is None, (
                    f"tick={tick}: expected no mission outside window without dev seed"
                )

    def test_different_dev_seed_values_produce_missions(self) -> None:
        """Different free-form WAYFINDERS_DEV_MISSION_SEED strings all produce missions."""
        mock = MagicMock()
        mock.ready = True
        mock.predict.return_value = 1.5
        engine = EmergenceEngine(predictor=mock)

        # Use a tick that is normally outside the cadence window.
        seed = 42
        outside_ticks = [t for t in range(2, 50) if not _in_cadence_window(t, seed)]
        tick = outside_ticks[0]
        req = WorldTickRequest(tick=tick, seed=seed, context_prose=_CONTEXT_HALFGATE)

        dev_values = ["halfgate_e2_tuto", "debug_run_1", "seed=99", "some_string", "42"]
        for dev_val in dev_values:
            with patch.dict(os.environ, {"WAYFINDERS_DEV_MISSION_SEED": dev_val}):
                resp = engine.process_tick(req)
                assert resp.mission is not None, (
                    f"WAYFINDERS_DEV_MISSION_SEED={dev_val!r}: expected mission"
                )
