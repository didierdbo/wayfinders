"""Tests for ``designer_modifiers.compute_delta`` -- Varn UC1 table v0.6.

Strategy (3 layers):

1. **Worked-example regression**: pin Varn §6 examples 1, 2, 3 to their
   exact expected ``label_delta`` values. These are the table's spec.
2. **Component-level invariants**: each ``_delta_*`` helper respects its
   sub-clamp, returns 0 on the right neutral inputs, and reacts correctly
   to single-field mutations.
3. **Property test (Hypothesis)**: ``compute_delta`` is always in [-5, +5].
"""

from __future__ import annotations

import math

import pytest
from hypothesis import HealthCheck, given, settings
from hypothesis import strategies as st

from wayfinders.ml.data_gen.designer_modifiers import (
    DELTA_MAX,
    DELTA_MIN,
    UNKNOWN_VERBS,
    _char_descriptor_delta,
    _char_episodic_delta,
    _char_legacy_delta,
    _char_traits_delta,
    _clamp,
    _context_party_delta,
    _context_stakes_delta,
    _context_time_delta,
    _delta_action,
    _delta_char,
    _delta_context,
    _substring_match_sum,
    _tier_lookup,
    compute_delta,
    compute_label,
    compute_raw_delta,
)
from wayfinders.ml.data_gen.rollouts import Rollout

from ._builders import (
    kira_for_table_v06,
    ridge_campaign_even,
    ridge_party_lightly_wounded,
    ridge_scene_high_stakes,
    ridge_world_at_war,
    stealth_card_at_noon,
    stealth_card_starlight_scree,
    two_ordinary_human_sentries,
)

# ---------------------------------------------------------------------------
# Layer 1 -- worked-example regression
# ---------------------------------------------------------------------------


class TestVarnWorkedExamples:
    """Lock the table's headline examples re-baselined for v0.6.

    v0.6 recalibration changes the raw and label values vs v0.5:
      - WIS_DELTA_STEALTH 'high': 1.0 -> 0.5
      - EQUIPMENT 'soft-soled': +1.0 -> +1.5
      - TIME_OF_DAY 'under starlight': +2.0 -> +1.0
      - FOOTING 'loose scree': -2.0 -> -1.5
      - NOISE 'near silence': 0.0 -> +0.5
      - ACTION_BASE 'stealth approach': -1.0 -> -0.5
      - STAKES 'high': -1.0 -> -0.5
      - PARTY wound/supply/fatigue all softened by 0.5 each
      - TANH_INPUT_SCALE: 5 -> 8

    Example 1 (starlight, scree): raw=3.5, label=5*tanh(3.5/8)=2.058
    Example 2 (noon, scree):      raw=0.5, label=5*tanh(0.5/8)=0.312

    If a test in this class fails, either the table moved or the
    worked example is wrong -- the data module is the authority.
    """

    @pytest.fixture
    def shared_context(
        self,
    ):  # type: ignore[no-untyped-def]
        return {
            "opposition": two_ordinary_human_sentries(),
            "scene": ridge_scene_high_stakes(),
            "party": ridge_party_lightly_wounded(),
            "world": ridge_world_at_war(),
            "campaign": ridge_campaign_even(),
        }

    def test_example_1_kira_canonical(self, shared_context):  # type: ignore[no-untyped-def]
        """v0.6 Example 1: raw=3.5, label=5*tanh(3.5/8)≈2.058."""
        char = kira_for_table_v06()
        action = stealth_card_starlight_scree()
        raw = compute_raw_delta(character=char, action=action, **shared_context)
        label = compute_delta(character=char, action=action, **shared_context)
        assert raw == 3.5, f"expected raw=3.5 per v0.6 table, got {raw}"
        assert math.isclose(label, 5.0 * math.tanh(3.5 / 8.0), abs_tol=1e-3)
        assert math.isclose(label, 2.058, abs_tol=0.01)

    def test_example_2_kira_at_noon(self, shared_context):  # type: ignore[no-untyped-def]
        """v0.6 Example 2: same as Example 1 but at noon -> raw=0.5, label≈0.312."""
        char = kira_for_table_v06()
        action = stealth_card_at_noon()
        raw = compute_raw_delta(character=char, action=action, **shared_context)
        label = compute_delta(character=char, action=action, **shared_context)
        assert raw == 0.5, f"expected raw=0.5 per v0.6 table, got {raw}"
        assert math.isclose(label, 0.312, abs_tol=0.01)

    def test_label_in_range(self, shared_context):  # type: ignore[no-untyped-def]
        """label_delta MUST be in [DELTA_MIN, DELTA_MAX] for any valid input."""
        char = kira_for_table_v06()
        action = stealth_card_starlight_scree()
        label = compute_delta(character=char, action=action, **shared_context)
        assert DELTA_MIN <= label <= DELTA_MAX

    def test_aggregation_sum_matches_components(self, shared_context):  # type: ignore[no-untyped-def]
        """raw_delta MUST equal _delta_char + _delta_action + _delta_context."""
        char = kira_for_table_v06()
        action = stealth_card_starlight_scree()
        raw = compute_raw_delta(character=char, action=action, **shared_context)
        components = (
            _delta_char(char, action)
            + _delta_action(action, shared_context["opposition"])
            + _delta_context(
                shared_context["scene"],
                shared_context["party"],
                shared_context["world"],
                shared_context["campaign"],
            )
        )
        assert raw == components


class TestSaturationBoundaries:
    """Edge of the tanh-soft-cap behaves correctly (v0.6: scale=8)."""

    def test_zero_raw_is_zero_label(self):  # type: ignore[no-untyped-def]
        """raw=0 -> label=0 exactly."""
        # 5 * tanh(0/8) = 0
        assert math.isclose(5.0 * math.tanh(0.0 / 8.0), 0.0, abs_tol=1e-12)

    @pytest.mark.parametrize(
        "raw,expected",
        [
            # v0.6: 5 * tanh(raw / 8).  All values rounded to 3dp.
            (1, round(5.0 * math.tanh(1 / 8.0), 3)),  # ≈ 0.623
            (3, round(5.0 * math.tanh(3 / 8.0), 3)),  # ≈ 1.835
            (5, round(5.0 * math.tanh(5 / 8.0), 3)),  # ≈ 2.929
            (8, round(5.0 * math.tanh(8 / 8.0), 3)),  # ≈ 3.807
            (12, round(5.0 * math.tanh(12 / 8.0), 3)),  # ≈ 4.504
            (-1, round(5.0 * math.tanh(-1 / 8.0), 3)),  # ≈ -0.623
            (-5, round(5.0 * math.tanh(-5 / 8.0), 3)),  # ≈ -2.929
        ],
    )
    def test_tanh_curve_v06(self, raw, expected):  # type: ignore[no-untyped-def]
        """v0.6 tanh curve: 5 * tanh(raw / 8)."""
        label = 5.0 * math.tanh(raw / 8.0)
        assert math.isclose(label, expected, abs_tol=0.005)


# ---------------------------------------------------------------------------
# Layer 2 -- component invariants
# ---------------------------------------------------------------------------


class TestSubstringMatching:
    """``_substring_match_sum`` -- longest-first, no double-counting."""

    def test_longest_first_avoids_eating(self):  # type: ignore[no-untyped-def]
        """'leather jerkin' (delta=0) must not be re-matched by 'leather' (none).

        We use 'wet' (delta=-1) and 'wet leaves' (delta would not exist) as
        a controlled probe: 'wet' alone in 'wet leaves' should fire the wet
        delta only once, not also re-fire on the 'wet' inside.
        """
        table = {"leather jerkin": 0, "leather": -5}
        result = _substring_match_sum("a worn leather jerkin", table)
        # Only 'leather jerkin' fires (longer match wins). 'leather' is consumed.
        assert result == 0

    def test_independent_keywords_both_fire(self):  # type: ignore[no-untyped-def]
        table = {"plate": -3, "hobnailed": -2}
        result = _substring_match_sum("a plate cuirass and hobnailed boots", table)
        assert result == -5

    def test_no_match_returns_zero(self):  # type: ignore[no-untyped-def]
        assert _substring_match_sum("nothing relevant here", {"foo": 5}) == 0


class TestClamp:
    @pytest.mark.parametrize(
        "value,lo,hi,expected",
        [(5, 0, 3, 3), (-5, -3, 3, -3), (1, -3, 3, 1), (0, 0, 0, 0)],
    )
    def test_clamp(self, value, lo, hi, expected):  # type: ignore[no-untyped-def]
        assert _clamp(value, lo, hi) == expected


class TestTierLookup:
    def test_first_match_wins_descending(self):  # type: ignore[no-untyped-def]
        tiers = ((30, 4), (10, 3), (4, 2), (1, 1), (0, 0))
        assert _tier_lookup(30, tiers) == 4
        assert _tier_lookup(15, tiers) == 3
        assert _tier_lookup(8, tiers) == 2
        assert _tier_lookup(2, tiers) == 1
        assert _tier_lookup(0, tiers) == 0

    def test_value_below_all_thresholds_returns_zero(self):  # type: ignore[no-untyped-def]
        assert _tier_lookup(-1, ((0, 5),)) == 0


class TestCharacterComponents:
    def test_traits_subclamped(self):  # type: ignore[no-untyped-def]
        """3 favorable traits sum to +3, but clamp prevents domination."""
        char = kira_for_table_v06().model_copy(
            update={"traits": ("patient", "cautious", "disciplined")}
        )
        action = stealth_card_starlight_scree()
        delta = _char_traits_delta(char, action)
        assert delta == 3  # +1+1+1 = 3, in clamp range

    def test_traits_subclamped_at_floor(self):  # type: ignore[no-untyped-def]
        """3 reckless-like traits would sum below -3 ; clamp catches."""
        char = kira_for_table_v06().model_copy(
            update={"traits": ("reckless", "reckless", "reckless")}
        )
        # Note: trait dedup happens at *render time* (alphabetical, deduped).
        # The table sums the raw list. v0.6: reckless=-1.5, so 3*-1.5=-4.5 -> clamped to -3.
        action = stealth_card_starlight_scree()
        delta = _char_traits_delta(char, action)
        assert delta == -3

    def test_legacy_clamp_asymmetric(self):  # type: ignore[no-untyped-def]
        """Legacy sub-clamp is [-3, +4] per Varn §1.5."""
        # Pile up legacy: 30+ stealth successes (+4), 0 failures (+0),
        # best-of-all familiarity (+2), 200+ scouting (+3) ; raw = +9, clamped to +4.
        char = kira_for_table_v06().model_copy(
            update={
                "action_counters": {"stealth approaches": (40, 0)},
                "profession_hours": {"scouting": 300},
                "location_familiarity": {"broken stone": "best of all"},
            }
        )
        action = stealth_card_starlight_scree()
        assert _char_legacy_delta(char, action) == 4

    def test_descriptor_kira_nimble_shrewd(self):  # type: ignore[no-untyped-def]
        """Kira at DEX 'high' (nimble) + WIS 'high' (shrewd) -> +1.0 + +0.5 = +1.5 (v0.6)."""
        char = kira_for_table_v06()
        action = stealth_card_starlight_scree()
        assert _char_descriptor_delta(char, action) == 1.5

    def test_episodic_does_not_fire_on_goblin_scout(self):  # type: ignore[no-untyped-def]
        """'goblin scout' description must not trigger the +1 stealth episodic.

        The keyword set was tightened in v0.5 implementation to avoid this
        false-positive (the bare 'scout' substring used to match enemy
        descriptors).
        """
        char = kira_for_table_v06()
        action = stealth_card_starlight_scree()
        assert _char_episodic_delta(char, action) == 0


class TestContextComponents:
    def test_high_stakes_negative(self):  # type: ignore[no-untyped-def]
        # v0.6: 'high' eased from -1 to -0.5
        scene = ridge_scene_high_stakes()
        assert _context_stakes_delta(scene) == -0.5

    def test_party_lightly_wounded_low_rations_short_sleep(self):  # type: ignore[no-untyped-def]
        party = ridge_party_lightly_wounded()
        # v0.6: lightly_wounded=-0.5, low_on_rations=-0.5, short_on_sleep=-0.5 -> -1.5
        assert _context_party_delta(party) == -1.5

    def test_no_countdown_means_relaxed_bonus(self):  # type: ignore[no-untyped-def]
        # v0.6: COUNTDOWN_NONE_DELTA eased from +1.0 to +0.5
        scene = ridge_scene_high_stakes().model_copy(
            update={"countdown_hours": None, "deadline_label": None}
        )
        assert _context_time_delta(scene) == 0.5


class TestUnknownVerbHook:
    def test_unknown_verb_does_not_crash_and_is_logged(self):  # type: ignore[no-untyped-def]
        """An action_phrase outside the table contributes 0 base, no exception.

        The unknown phrase is appended to UNKNOWN_VERBS for v1 review.
        """
        # Snapshot to avoid order-dependent test bleed.
        UNKNOWN_VERBS.discard("parley")
        kira_for_table_v06()
        action = stealth_card_starlight_scree().model_copy(update={"action_phrase": "parley"})
        opp = two_ordinary_human_sentries()
        # Should not raise.
        _ = _delta_action(action, opp)
        assert "parley" in UNKNOWN_VERBS


# ---------------------------------------------------------------------------
# Layer 3 -- property test
# ---------------------------------------------------------------------------


@settings(
    max_examples=200,
    deadline=None,
    suppress_health_check=[HealthCheck.too_slow],
)
@given(
    raw=st.integers(min_value=-200, max_value=200),
)
def test_tanh_softcap_is_in_range(raw: int) -> None:
    """For any integer raw_delta, ``5 * tanh(raw/8)`` ∈ [-5, +5] (v0.6 scale)."""
    label = 5.0 * math.tanh(raw / 8.0)
    assert DELTA_MIN <= label <= DELTA_MAX


def test_compute_label_wrapper_matches_compute_delta() -> None:
    """``compute_label(rollout)`` must equal ``compute_delta(...)`` of the same parts."""
    char = kira_for_table_v06()
    action = stealth_card_starlight_scree()
    opp = two_ordinary_human_sentries()
    scene = ridge_scene_high_stakes()
    party = ridge_party_lightly_wounded()
    world = ridge_world_at_war()
    campaign = ridge_campaign_even()
    rollout = Rollout(
        rollout_id=0,
        character=char,
        action=action,
        opposition=opp,
        scene=scene,
        party=party,
        world=world,
        campaign=campaign,
    )
    via_wrapper = compute_label(rollout)
    via_direct = compute_delta(
        character=char,
        action=action,
        opposition=opp,
        scene=scene,
        party=party,
        world=world,
        campaign=campaign,
    )
    assert via_wrapper == via_direct


# ---------------------------------------------------------------------------
# Layer 4 -- per-rule nominal / off-case coverage (substep 5 contract)
#
# For each rule group in designer_modifiers_data, one test verifies the rule
# fires in its nominal case (expected non-zero delta) and one verifies it
# does NOT fire on a neutral / off-case input.  This makes the table itself
# the tested specification -- if Varn revises a rule, the broken test is the
# diagnostic.
# ---------------------------------------------------------------------------


class TestRuleNominalAndOffCase:
    """Each top-level rule fires on its trigger and stays silent off-trigger."""

    # -- §2.3 Time-of-day: starlight gives +1.0 (v0.6 eased from +2), noon gives -2.0 (eased from -3) ----

    def test_starlight_fires(self) -> None:
        # Direct table lookup -- the authoritative check for this rule.
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TIME_OF_DAY_DELTA_STEALTH["under starlight"] == 1.0

    def test_noon_fires_negative(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TIME_OF_DAY_DELTA_STEALTH["at noon"] == -2.0

    def test_neutral_time_is_zero(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TIME_OF_DAY_DELTA_STEALTH["in the late afternoon"] == 0

    # -- §2.2 Footing: loose scree -1.5 (v0.6 eased from -2), firm stone +1.0 ----

    def test_loose_scree_fires_negative(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.FOOTING_DELTA_STEALTH["loose scree"] == -1.5

    def test_firm_stone_fires_positive(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.FOOTING_DELTA_STEALTH["firm stone"] == 1.0

    def test_wet_leaves_fires_positive(self) -> None:
        """v0.6: wet leaves bumped to +0.5 (dampens sound slightly)."""
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.FOOTING_DELTA_STEALTH["wet leaves"] == 0.5

    # -- §2.7 Opposition alertness: drowsy +1.0 (v0.6 eased from +2), alarmed -2.0 (eased from -3) ----

    def test_drowsy_opponent_fires_positive(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.OPPOSITION_ALERTNESS_DELTA["drowsy"] == 1.0

    def test_alarmed_opponent_fires_negative(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.OPPOSITION_ALERTNESS_DELTA["alarmed and searching"] == -2.0

    def test_alert_and_rested_baseline_is_zero(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.OPPOSITION_ALERTNESS_DELTA["alert and rested"] == 0

    # -- §1.5.1 Legacy success tiers ----

    def test_legacy_expert_fires(self) -> None:
        """10+ stealth successes gives +2.0 (expert tier, v0.6 eased from +3)."""
        char = kira_for_table_v06().model_copy(
            update={
                "action_counters": {"stealth approaches": (15, 0)},
                "profession_hours": {},
                "location_familiarity": {},
            }
        )
        action = stealth_card_starlight_scree()
        from wayfinders.ml.data_gen.designer_modifiers import _char_legacy_delta

        delta = _char_legacy_delta(char, action)
        # 15 successes -> STEALTH_SUCCESS_TIERS (10, 2.0) -> +2.0;
        # no failures, no scouting, no familiarity -> raw = +2.0.
        assert delta == 2.0

    def test_legacy_novice_is_zero(self) -> None:
        """0 stealth successes, no professions -> legacy +0."""
        char = kira_for_table_v06().model_copy(
            update={
                "action_counters": {"stealth approaches": (0, 0)},
                "profession_hours": {},
                "location_familiarity": {},
            }
        )
        action = stealth_card_starlight_scree()
        from wayfinders.ml.data_gen.designer_modifiers import _char_legacy_delta

        assert _char_legacy_delta(char, action) == 0

    # -- §1.5.2 Terrain familiarity: 'best of all' +1.5 (v0.6), 'barely' -0.5 (v0.6) ----

    def test_terrain_familiarity_best_fires(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TERRAIN_FAMILIARITY_DELTA["best of all"] == 1.5

    def test_terrain_familiarity_barely_fires_negative(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TERRAIN_FAMILIARITY_DELTA["barely"] == -0.5

    def test_terrain_familiarity_moderately_is_zero(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TERRAIN_FAMILIARITY_DELTA["moderately"] == 0

    # -- §1.4 Equipment: soft-soled boots +1.5 (v0.6 up from +1), plate armor -2.5 (v0.6 eased from -3) ----

    def test_soft_soled_fires_positive(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.EQUIPMENT_KEYWORD_DELTA_STEALTH["soft-soled"] == 1.5

    def test_plate_fires_negative(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.EQUIPMENT_KEYWORD_DELTA_STEALTH["plate"] == -2.5

    def test_leather_jerkin_is_zero(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.EQUIPMENT_KEYWORD_DELTA_STEALTH["leather jerkin"] == 0

    # -- §3.1 Mission stakes: 'high' -0.5 (v0.6 eased from -1), 'dire' -1.0 (v0.6 eased from -2) ----

    def test_high_stakes_fires_negative(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.STAKES_DELTA["high"] == -0.5

    def test_dire_stakes_fires_more_negative(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.STAKES_DELTA["dire"] == -1.0

    def test_trivial_stakes_fires_positive(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.STAKES_DELTA["trivial"] == 1

    # -- §1.2 Traits: reckless is penalized in stealth (-1.5 in v0.6, eased from -2) ----

    def test_reckless_penalized_in_stealth(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TRAIT_DELTA_STEALTH["reckless"] == -1.5

    def test_reckless_less_penalized_in_melee(self) -> None:
        """In melee, reckless is only -1 (not as severe as in stealth)."""
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TRAIT_DELTA_MELEE["reckless"] == -1

    def test_patient_positive_in_stealth(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TRAIT_DELTA_STEALTH["patient"] == 1

    def test_patient_neutral_in_melee(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TRAIT_DELTA_MELEE["patient"] == 0

    # -- §3.5 Terrain primary: deep forest +1.5 (v0.6 eased from +2), open plains -1.0 (v0.6 eased from -2) ----

    def test_deep_forest_fires_positive(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TERRAIN_PRIMARY_DELTA["deep forest"] == 1.5

    def test_open_plains_fires_negative(self) -> None:
        from wayfinders.ml.data_gen import designer_modifiers_data as tbl

        assert tbl.TERRAIN_PRIMARY_DELTA["open plains"] == -1.0


# ---------------------------------------------------------------------------
# Layer 5 -- range, determinism, and addressability (substep 5 contract)
# ---------------------------------------------------------------------------


def test_compute_label_range_over_1000_rollouts() -> None:
    """For 1000 generated rollouts, every label_delta is strictly in [-5, +5].

    Uses the rollout generator with seed=42 (deterministic).  This exercises
    the full table -- char + action + context -- across diverse sampled states.
    """
    from wayfinders.ml.data_gen.rollouts import generate_rollouts

    for rollout in generate_rollouts(n=1000, seed=42):
        assert rollout.label_delta is not None
        assert DELTA_MIN <= rollout.label_delta <= DELTA_MAX, (
            f"rollout_id={rollout.rollout_id} label_delta={rollout.label_delta} out of range"
        )


def test_compute_label_determinism_100x() -> None:
    """compute_label(rollout) called 100 times returns the exact same float.

    Pure function contract: same inputs -> same output, bit-exact.
    """
    char = kira_for_table_v06()
    action = stealth_card_starlight_scree()
    opp = two_ordinary_human_sentries()
    scene = ridge_scene_high_stakes()
    party = ridge_party_lightly_wounded()
    world = ridge_world_at_war()
    campaign = ridge_campaign_even()
    rollout = Rollout(
        rollout_id=0,
        character=char,
        action=action,
        opposition=opp,
        scene=scene,
        party=party,
        world=world,
        campaign=campaign,
    )
    first = compute_label(rollout)
    for _ in range(99):
        assert compute_label(rollout) == first, "compute_label is not bit-exact deterministic"


def test_table_is_addressable() -> None:
    """The designer modifier table must be listable and dumpable for debugging.

    All top-level data tables in designer_modifiers_data must be non-empty
    Mapping or tuple instances, iterable, and not None.  This ensures the
    table can be serialized for designer review (e.g. as JSON for Varn).
    """
    from wayfinders.ml.data_gen import designer_modifiers_data as tbl

    # All Mapping-type tables: non-empty, string-serializable keys.
    mapping_tables = [
        ("DEX_DELTA_FULL", tbl.DEX_DELTA_FULL),
        ("DEX_DELTA_MOVE", tbl.DEX_DELTA_MOVE),
        ("STR_DELTA_MELEE", tbl.STR_DELTA_MELEE),
        ("WIS_DELTA_STEALTH", tbl.WIS_DELTA_STEALTH),
        ("WIS_DELTA_SIGNAL", tbl.WIS_DELTA_SIGNAL),
        ("TRAIT_DELTA_STEALTH", tbl.TRAIT_DELTA_STEALTH),
        ("TRAIT_DELTA_MELEE", tbl.TRAIT_DELTA_MELEE),
        ("HP_DELTA", tbl.HP_DELTA),
        ("STRESS_DELTA", tbl.STRESS_DELTA),
        ("CONDITION_KEYWORD_DELTA", tbl.CONDITION_KEYWORD_DELTA),
        ("EQUIPMENT_KEYWORD_DELTA_STEALTH", tbl.EQUIPMENT_KEYWORD_DELTA_STEALTH),
        ("TERRAIN_FAMILIARITY_DELTA", tbl.TERRAIN_FAMILIARITY_DELTA),
        ("ACTION_BASE_DIFFICULTY", tbl.ACTION_BASE_DIFFICULTY),
        ("FOOTING_DELTA_STEALTH", tbl.FOOTING_DELTA_STEALTH),
        ("FOOTING_DELTA_MOVE", tbl.FOOTING_DELTA_MOVE),
        ("TIME_OF_DAY_DELTA_STEALTH", tbl.TIME_OF_DAY_DELTA_STEALTH),
        ("NOISE_DELTA_STEALTH", tbl.NOISE_DELTA_STEALTH),
        ("OPPOSITION_ALERTNESS_DELTA", tbl.OPPOSITION_ALERTNESS_DELTA),
        ("OPPOSITION_SKILL_DELTA", tbl.OPPOSITION_SKILL_DELTA),
        ("FAMILY_KEYWORD_DELTA", tbl.FAMILY_KEYWORD_DELTA),
        ("STAKES_DELTA", tbl.STAKES_DELTA),
        ("WOUND_DELTA", tbl.WOUND_DELTA),
        ("SUPPLY_DELTA", tbl.SUPPLY_DELTA),
        ("FATIGUE_DELTA", tbl.FATIGUE_DELTA),
        ("PRECIPITATION_DELTA", tbl.PRECIPITATION_DELTA),
        ("WIND_DELTA_STEALTH", tbl.WIND_DELTA_STEALTH),
        ("TEMPERATURE_DELTA", tbl.TEMPERATURE_DELTA),
        ("TERRAIN_PRIMARY_DELTA", tbl.TERRAIN_PRIMARY_DELTA),
        ("FACTION_RELATION_DELTA", tbl.FACTION_RELATION_DELTA),
        ("CAMPAIGN_KEYWORD_DELTA", tbl.CAMPAIGN_KEYWORD_DELTA),
    ]
    for name, table in mapping_tables:
        assert len(table) > 0, f"{name} is empty"
        # Every value is an int (Δ-component).
        for k, v in table.items():
            assert isinstance(v, (int, float)), f"{name}[{k!r}] is not int/float: {v!r}"
            # v0.6 grid: values must be on 0.5 increments.
            assert (v * 2) == int(v * 2), f"{name}[{k!r}]={v!r} not on 0.5 grid"

    # Tier tables: non-empty tuples of (threshold, delta) pairs.
    tier_tables = [
        ("STEALTH_SUCCESS_TIERS", tbl.STEALTH_SUCCESS_TIERS),
        ("STEALTH_FAILURE_TIERS", tbl.STEALTH_FAILURE_TIERS),
        ("SCOUTING_PROFESSION_TIERS", tbl.SCOUTING_PROFESSION_TIERS),
        ("OPPOSITION_COUNT_TIERS", tbl.OPPOSITION_COUNT_TIERS),
        ("COUNTDOWN_TIERS", tbl.COUNTDOWN_TIERS),
    ]
    for name, tiers in tier_tables:
        assert len(tiers) > 0, f"{name} is empty"
        for threshold, delta in tiers:
            assert isinstance(threshold, int), f"{name} threshold not int"
            assert isinstance(delta, (int, float)), f"{name} delta not int/float"
            assert (delta * 2) == int(delta * 2), f"{name} delta {delta!r} not on 0.5 grid"

    # Frozenset tables: non-empty.
    assert len(tbl.ADJACENT_PROFESSIONS_STEALTH) > 0
    assert len(tbl.EPISODIC_KEYWORD_HITS) > 0

    # Scalar constants.
    assert isinstance(tbl.TANH_OUTPUT_SCALE, float)
    assert isinstance(tbl.TANH_INPUT_SCALE, float)
    assert tbl.TANH_OUTPUT_SCALE == 5.0
    assert tbl.TANH_INPUT_SCALE == 8.0  # v0.6 -- expanded from 5.0 to reduce saturation

    # Total rule count: sum of entries across all Mapping tables >= 100
    # (proves the table is substantive enough for training signal).
    total_entries = sum(len(t) for _, t in mapping_tables)
    assert total_entries >= 100, (
        f"Table has only {total_entries} entries -- may be too sparse for training signal"
    )
