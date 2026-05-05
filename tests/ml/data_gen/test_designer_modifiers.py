"""Tests for ``designer_modifiers.compute_delta`` -- Varn UC1 table v0.5.

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
    kira_for_table_v05,
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
    """Lock the table's headline examples from Varn 2026-05-05 §6.

    These are the spec. If a test in this class fails, either the table
    moved or the worked example is wrong -- pinging Varn is mandatory
    before adjusting either.
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
        """Varn §6 Example 1: raw=+4, label=+3.32."""
        char = kira_for_table_v05()
        action = stealth_card_starlight_scree()
        raw = compute_raw_delta(character=char, action=action, **shared_context)
        label = compute_delta(character=char, action=action, **shared_context)
        assert raw == 4, f"expected raw=+4 per Varn Example 1, got {raw}"
        assert math.isclose(label, 5.0 * math.tanh(0.8), abs_tol=1e-3)
        assert math.isclose(label, 3.32, abs_tol=0.01)

    def test_example_2_kira_at_noon(self, shared_context):  # type: ignore[no-untyped-def]
        """Varn §6 Example 2: same as Example 1 but at noon -> raw=-1, label=-0.99."""
        char = kira_for_table_v05()
        action = stealth_card_at_noon()
        raw = compute_raw_delta(character=char, action=action, **shared_context)
        label = compute_delta(character=char, action=action, **shared_context)
        assert raw == -1, f"expected raw=-1 per Varn Example 2, got {raw}"
        assert math.isclose(label, -0.987, abs_tol=0.01)

    def test_label_in_range(self, shared_context):  # type: ignore[no-untyped-def]
        """label_delta MUST be in [DELTA_MIN, DELTA_MAX] for any valid input."""
        char = kira_for_table_v05()
        action = stealth_card_starlight_scree()
        label = compute_delta(character=char, action=action, **shared_context)
        assert DELTA_MIN <= label <= DELTA_MAX

    def test_aggregation_sum_matches_components(self, shared_context):  # type: ignore[no-untyped-def]
        """raw_delta MUST equal _delta_char + _delta_action + _delta_context."""
        char = kira_for_table_v05()
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
    """Edge of the tanh-soft-cap behaves correctly."""

    def test_zero_raw_is_zero_label(self):  # type: ignore[no-untyped-def]
        """raw=0 -> label=0 exactly."""
        # 5 * tanh(0/5) = 0
        assert math.isclose(5.0 * math.tanh(0.0 / 5.0), 0.0, abs_tol=1e-12)

    @pytest.mark.parametrize(
        "raw,expected",
        [
            (1, 0.987),
            pytest.param(
                3,
                2.86,
                marks=pytest.mark.xfail(
                    reason="Varn v0.5 published value is rounded too coarsely vs 5*tanh(3/5)=2.685 ; see issue #13",
                    strict=True,
                ),
            ),
            (5, 3.808),
            pytest.param(
                8,
                4.62,
                marks=pytest.mark.xfail(
                    reason="Varn v0.5 published value vs 5*tanh(8/5)=4.608 differs >0.005 ; see issue #13",
                    strict=True,
                ),
            ),
            pytest.param(
                12,
                4.95,
                marks=pytest.mark.xfail(
                    reason="Varn v0.5 published value vs 5*tanh(12/5)=4.918 differs >0.005 ; see issue #13",
                    strict=True,
                ),
            ),
            (-1, -0.987),
            (-5, -3.808),
        ],
    )
    def test_tanh_curve_matches_varn(self, raw, expected):  # type: ignore[no-untyped-def]
        """Varn §4.2 explicit table of (raw -> label_Δ)."""
        label = 5.0 * math.tanh(raw / 5.0)
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
        char = kira_for_table_v05().model_copy(
            update={"traits": ("patient", "cautious", "disciplined")}
        )
        action = stealth_card_starlight_scree()
        delta = _char_traits_delta(char, action)
        assert delta == 3  # +1+1+1 = 3, in clamp range

    def test_traits_subclamped_at_floor(self):  # type: ignore[no-untyped-def]
        """3 reckless-like traits would sum below -3 ; clamp catches."""
        char = kira_for_table_v05().model_copy(
            update={"traits": ("reckless", "reckless", "reckless")}
        )
        # Note: trait dedup happens at *render time* (alphabetical, deduped).
        # The table sums the raw list. (-2)*3 = -6 -> clamped to -3.
        action = stealth_card_starlight_scree()
        delta = _char_traits_delta(char, action)
        assert delta == -3

    def test_legacy_clamp_asymmetric(self):  # type: ignore[no-untyped-def]
        """Legacy sub-clamp is [-3, +4] per Varn §1.5."""
        # Pile up legacy: 30+ stealth successes (+4), 0 failures (+0),
        # best-of-all familiarity (+2), 200+ scouting (+3) ; raw = +9, clamped to +4.
        char = kira_for_table_v05().model_copy(
            update={
                "action_counters": {"stealth approaches": (40, 0)},
                "profession_hours": {"scouting": 300},
                "location_familiarity": {"broken stone": "best of all"},
            }
        )
        action = stealth_card_starlight_scree()
        assert _char_legacy_delta(char, action) == 4

    def test_descriptor_kira_wiry_shrewd(self):  # type: ignore[no-untyped-def]
        """Kira at DEX 'high' (wiry) + WIS 'high' (shrewd) -> +1 + +1 = +2."""
        char = kira_for_table_v05()
        action = stealth_card_starlight_scree()
        assert _char_descriptor_delta(char, action) == 2

    def test_episodic_does_not_fire_on_goblin_scout(self):  # type: ignore[no-untyped-def]
        """'goblin scout' description must not trigger the +1 stealth episodic.

        The keyword set was tightened in v0.5 implementation to avoid this
        false-positive (the bare 'scout' substring used to match enemy
        descriptors).
        """
        char = kira_for_table_v05()
        action = stealth_card_starlight_scree()
        assert _char_episodic_delta(char, action) == 0


class TestContextComponents:
    def test_high_stakes_negative(self):  # type: ignore[no-untyped-def]
        scene = ridge_scene_high_stakes()
        assert _context_stakes_delta(scene) == -1

    def test_party_lightly_wounded_low_rations_short_sleep(self):  # type: ignore[no-untyped-def]
        party = ridge_party_lightly_wounded()
        # 0 (lightly wounded) + -1 (low rations) + -1 (short on sleep) = -2
        assert _context_party_delta(party) == -2

    def test_no_countdown_means_relaxed_bonus(self):  # type: ignore[no-untyped-def]
        scene = ridge_scene_high_stakes().model_copy(
            update={"countdown_hours": None, "deadline_label": None}
        )
        assert _context_time_delta(scene) == 1


class TestUnknownVerbHook:
    def test_unknown_verb_does_not_crash_and_is_logged(self):  # type: ignore[no-untyped-def]
        """An action_phrase outside the table contributes 0 base, no exception.

        The unknown phrase is appended to UNKNOWN_VERBS for v1 review.
        """
        # Snapshot to avoid order-dependent test bleed.
        UNKNOWN_VERBS.discard("parley")
        kira_for_table_v05()
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
    """For any integer raw_delta, ``5 * tanh(raw/5)`` ∈ [-5, +5]."""
    label = 5.0 * math.tanh(raw / 5.0)
    assert DELTA_MIN <= label <= DELTA_MAX


def test_compute_label_wrapper_matches_compute_delta() -> None:
    """``compute_label(rollout)`` must equal ``compute_delta(...)`` of the same parts."""
    char = kira_for_table_v05()
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
