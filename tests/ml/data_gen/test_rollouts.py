"""Tests for ``rollouts.generate_rollouts`` -- determinism, distribution, perf, IO."""

from __future__ import annotations

import json
import time
from pathlib import Path

import pytest
from pydantic import ValidationError

from wayfinders.ml.data_gen.designer_modifiers import DELTA_MAX, DELTA_MIN
from wayfinders.ml.data_gen.policies import MixedPolicy, RandomPolicy, ScriptedPolicy
from wayfinders.ml.data_gen.rollouts import (
    Rollout,
    generate_rollouts,
    load_rollouts_jsonl,
    serialize_rollouts_jsonl,
)


class TestDeterminism:
    def test_same_seed_yields_same_rollouts(self) -> None:
        a = list(generate_rollouts(n=20, seed=42))
        b = list(generate_rollouts(n=20, seed=42))
        # Compare by serialized form (model_dump_json) to assert byte-identity.
        assert [r.model_dump_json() for r in a] == [r.model_dump_json() for r in b]

    def test_different_seed_yields_different_rollouts(self) -> None:
        a = list(generate_rollouts(n=20, seed=42))
        b = list(generate_rollouts(n=20, seed=43))
        # At least one differs (collisions are vanishingly rare on 20 rollouts).
        assert any(
            ra.model_dump_json() != rb.model_dump_json() for ra, rb in zip(a, b, strict=True)
        )

    def test_partial_generation_is_a_prefix(self) -> None:
        """generate_rollouts(5, S) must be the first 5 of generate_rollouts(10, S)."""
        five = list(generate_rollouts(n=5, seed=7))
        ten = list(generate_rollouts(n=10, seed=7))
        for short, long in zip(five, ten[:5], strict=True):
            assert short.model_dump_json() == long.model_dump_json()

    def test_negative_n_raises(self) -> None:
        with pytest.raises(ValueError):
            list(generate_rollouts(n=-1, seed=0))

    def test_zero_n_yields_empty(self) -> None:
        assert list(generate_rollouts(n=0, seed=0)) == []


class TestRolloutShape:
    def test_label_in_range(self) -> None:
        for r in generate_rollouts(n=100, seed=11):
            assert r.label_delta is not None
            assert DELTA_MIN <= r.label_delta <= DELTA_MAX

    def test_prose_strings_nonempty(self) -> None:
        """All three rendered prose strings must be present and non-empty."""
        for r in generate_rollouts(n=20, seed=12):
            assert r.prose_character.strip()
            assert r.prose_action.strip()
            assert r.prose_context.strip()

    def test_rollout_ids_are_sequential_from_zero(self) -> None:
        rollouts = list(generate_rollouts(n=15, seed=13))
        assert [r.rollout_id for r in rollouts] == list(range(15))

    def test_rollouts_are_frozen(self) -> None:
        r = next(iter(generate_rollouts(n=1, seed=0)))
        with pytest.raises(ValidationError):
            r.rollout_id = 99  # type: ignore[misc]


class TestVerbCoverage:
    def test_all_verbs_appear_in_large_sample(self) -> None:
        """500 rollouts should hit every verb at least once given the weights."""
        verbs = {r.action.action_phrase for r in generate_rollouts(n=500, seed=99)}
        expected = {
            "stealth approach",
            "move",
            "wait",
            "signal to ally",
            "melee attack",
            "ranged attack",
        }
        assert expected.issubset(verbs)

    def test_stealth_verb_dominates(self) -> None:
        """~70% stealth weight should produce >50% stealth verbs in 200-sample."""
        rollouts = list(generate_rollouts(n=200, seed=21))
        stealth_count = sum(1 for r in rollouts if r.action.action_phrase == "stealth approach")
        assert stealth_count / 200 > 0.5


class TestLabelDistribution:
    @pytest.mark.xfail(
        reason="v0.5 sampler/table calibration bias toward extreme modifiers ; see issue #13",
        strict=True,
    )
    def test_label_distribution_is_centered(self) -> None:
        """Varn §5: ~60% of |label| in [0, 1.5]. 200 rollouts gives a coarse check."""
        rollouts = list(generate_rollouts(n=200, seed=34))
        labels = [r.label_delta for r in rollouts if r.label_delta is not None]
        assert len(labels) == 200
        in_central_band = sum(1 for label in labels if abs(label) < 1.5)
        # Loose tolerance: distribution sanity, not an exact match. Tightened
        # in M1 substep 9 once 50k rollouts give a real histogram.
        assert in_central_band / 200 > 0.30, (
            f"expected at least 30% of labels in [-1.5, +1.5] ; got {in_central_band / 200:.2%}"
        )

    @pytest.mark.xfail(
        reason="v0.5 sampler/table calibration produces ~20% saturation vs <5% target ; see issue #13",
        strict=True,
    )
    def test_label_saturation_is_rare(self) -> None:
        """Varn §5: <1% of labels should saturate (|label| >= 4.5)."""
        rollouts = list(generate_rollouts(n=500, seed=55))
        labels = [r.label_delta for r in rollouts if r.label_delta is not None]
        saturated = sum(1 for label in labels if abs(label) >= 4.5)
        # 500 samples is small ; allow up to 5% before flagging.
        assert saturated / 500 < 0.05, (
            f"saturation ratio {saturated / 500:.2%} ; check table calibration"
        )


class TestSerialization:
    def test_jsonl_roundtrip(self, tmp_path: Path) -> None:
        """Write 50 rollouts, read them back, compare serializations."""
        out_path = tmp_path / "rollouts.jsonl"
        original = list(generate_rollouts(n=50, seed=77))
        n_written = serialize_rollouts_jsonl(iter(original), out_path)
        assert n_written == 50
        loaded = list(load_rollouts_jsonl(out_path))
        assert len(loaded) == 50
        for orig, back in zip(original, loaded, strict=True):
            assert orig.model_dump_json() == back.model_dump_json()

    def test_jsonl_one_rollout_per_line(self, tmp_path: Path) -> None:
        out_path = tmp_path / "rollouts.jsonl"
        serialize_rollouts_jsonl(generate_rollouts(n=10, seed=88), out_path)
        lines = out_path.read_text(encoding="utf-8").splitlines()
        assert len(lines) == 10
        # Each line must be valid JSON parseable as a Rollout.
        for line in lines:
            parsed = json.loads(line)
            Rollout.model_validate(parsed)

    def test_jsonl_creates_parent_directory(self, tmp_path: Path) -> None:
        out_path = tmp_path / "deep" / "nested" / "out.jsonl"
        serialize_rollouts_jsonl(generate_rollouts(n=3, seed=1), out_path)
        assert out_path.exists()


class TestPerformance:
    def test_thousand_rollouts_under_five_seconds(self) -> None:
        """1000 rollouts must complete in under 5s on dev hardware (no encoder)."""
        start = time.perf_counter()
        rollouts = list(generate_rollouts(n=1000, seed=12345))
        elapsed = time.perf_counter() - start
        assert len(rollouts) == 1000
        assert elapsed < 5.0, f"1000 rollouts took {elapsed:.2f}s, expected <5s"

    def test_first_rollout_from_large_generator_under_100ms(self) -> None:
        """next(generate_rollouts(50_000, 0)) must return in < 100ms.

        This validates the generator is lazy -- it must not materialise all
        50k rollouts before yielding the first one.
        """
        start = time.perf_counter()
        r = next(generate_rollouts(50_000, seed=0))
        elapsed = time.perf_counter() - start
        assert r is not None
        assert elapsed < 0.1, (
            f"first rollout from 50k generator took {elapsed * 1000:.1f}ms, expected <100ms"
        )


# ---------------------------------------------------------------------------
# M1.6 additions -- policy integration + traceability fields
# ---------------------------------------------------------------------------


class TestPolicyIntegration:
    """generate_rollouts(n, seed, policy=X) wires the policy into generation."""

    def test_default_policy_is_deterministic(self) -> None:
        """No policy (None) must give same results as before M1.6."""
        a = [r.model_dump_json() for r in generate_rollouts(20, seed=42)]
        b = [r.model_dump_json() for r in generate_rollouts(20, seed=42)]
        assert a == b

    def test_random_policy_determinism(self) -> None:
        pol = RandomPolicy()
        a = [r.model_dump_json() for r in generate_rollouts(20, seed=42, policy=pol)]
        b = [r.model_dump_json() for r in generate_rollouts(20, seed=42, policy=pol)]
        assert a == b

    def test_scripted_policy_determinism(self) -> None:
        pol = ScriptedPolicy()
        a = [r.model_dump_json() for r in generate_rollouts(20, seed=7, policy=pol)]
        b = [r.model_dump_json() for r in generate_rollouts(20, seed=7, policy=pol)]
        assert a == b

    def test_mixed_policy_determinism(self) -> None:
        pol = MixedPolicy()
        a = [r.model_dump_json() for r in generate_rollouts(20, seed=99, policy=pol)]
        b = [r.model_dump_json() for r in generate_rollouts(20, seed=99, policy=pol)]
        assert a == b

    def test_different_policies_produce_different_rollouts(self) -> None:
        """RandomPolicy vs ScriptedPolicy must differ on at least 1 rollout out of 20."""
        n, seed = 20, 42
        rand_r = [r.model_dump_json() for r in generate_rollouts(n, seed, RandomPolicy())]
        scr_r = [r.model_dump_json() for r in generate_rollouts(n, seed, ScriptedPolicy())]
        assert any(a != b for a, b in zip(rand_r, scr_r, strict=True)), (
            "RandomPolicy and ScriptedPolicy produced identical rollouts -- unexpected"
        )

    def test_all_policies_produce_valid_label_range(self) -> None:
        for policy in [None, RandomPolicy(), ScriptedPolicy(), MixedPolicy()]:
            for r in generate_rollouts(50, seed=11, policy=policy):
                assert r.label_delta is not None
                assert DELTA_MIN <= r.label_delta <= DELTA_MAX


class TestTraceabilityFields:
    """Rollout.seed and Rollout.policy_name are populated by generate_rollouts."""

    def test_seed_is_stored_on_rollout(self) -> None:
        for r in generate_rollouts(10, seed=7):
            assert r.seed == 7

    def test_policy_name_default_is_populated(self) -> None:
        for r in generate_rollouts(10, seed=0):
            assert r.policy_name == "default"

    def test_policy_name_matches_policy(self) -> None:
        for r in generate_rollouts(5, seed=0, policy=RandomPolicy()):
            assert r.policy_name == "random"
        for r in generate_rollouts(5, seed=0, policy=ScriptedPolicy()):
            assert r.policy_name == "scripted"
        for r in generate_rollouts(5, seed=0, policy=MixedPolicy()):
            assert r.policy_name == "mixed"

    def test_traceability_fields_survive_jsonl_roundtrip(self, tmp_path: Path) -> None:
        """seed and policy_name must survive serialize → load roundtrip."""
        rollouts = list(generate_rollouts(10, seed=42, policy=ScriptedPolicy()))
        out = tmp_path / "trace.jsonl"
        serialize_rollouts_jsonl(iter(rollouts), out)
        loaded = list(load_rollouts_jsonl(out))
        for orig, back in zip(rollouts, loaded, strict=True):
            assert back.seed == orig.seed
            assert back.policy_name == orig.policy_name
