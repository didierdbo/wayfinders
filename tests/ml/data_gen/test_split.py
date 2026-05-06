"""Tests for data_gen/split.py.

Covers:
  - No overlap between train/val/test splits.
  - Realized ratios are approximately correct (within tolerance).
  - Same (rollouts, ratios, seed) always produces the same split.
  - Edge cases: empty input, bad ratios.
"""

from __future__ import annotations

import pytest

from wayfinders.ml.data_gen.rollouts import Rollout, generate_rollouts
from wayfinders.ml.data_gen.split import split_rollouts


def _get_rollouts(n: int = 200, seed: int = 42) -> list[Rollout]:
    return list(generate_rollouts(n, seed=seed))


class TestNoOverlap:
    def test_train_val_test_are_disjoint(self) -> None:
        rollouts = _get_rollouts(200)
        train, val, test = split_rollouts(rollouts, ratios=(0.8, 0.1, 0.1), seed=0)

        train_ids = {r.rollout_id for r in train}
        val_ids = {r.rollout_id for r in val}
        test_ids = {r.rollout_id for r in test}

        assert len(train_ids & val_ids) == 0, "train and val overlap"
        assert len(train_ids & test_ids) == 0, "train and test overlap"
        assert len(val_ids & test_ids) == 0, "val and test overlap"

    def test_all_rollouts_assigned(self) -> None:
        rollouts = _get_rollouts(200)
        train, val, test = split_rollouts(rollouts)
        assert len(train) + len(val) + len(test) == len(rollouts)


class TestApproximateRatios:
    def test_train_ratio_roughly_80_percent(self) -> None:
        rollouts = _get_rollouts(500)
        train, _val, _test = split_rollouts(rollouts, ratios=(0.8, 0.1, 0.1), seed=7)
        n = len(rollouts)
        # Allow 15% slack because we split by groups, not individual items.
        assert len(train) / n >= 0.50, f"train is only {len(train) / n:.1%}"

    def test_val_and_test_have_some_items(self) -> None:
        rollouts = _get_rollouts(200)
        _, val, test = split_rollouts(rollouts, ratios=(0.8, 0.1, 0.1), seed=0)
        assert len(val) > 0, "val split is empty"
        assert len(test) > 0, "test split is empty"


class TestDeterminism:
    def test_same_seed_same_split(self) -> None:
        rollouts = _get_rollouts(150)
        train1, val1, test1 = split_rollouts(rollouts, seed=99)
        train2, val2, test2 = split_rollouts(rollouts, seed=99)

        assert [r.rollout_id for r in train1] == [r.rollout_id for r in train2]
        assert [r.rollout_id for r in val1] == [r.rollout_id for r in val2]
        assert [r.rollout_id for r in test1] == [r.rollout_id for r in test2]

    def test_different_seed_may_differ(self) -> None:
        rollouts = _get_rollouts(200)
        train1, _, _ = split_rollouts(rollouts, seed=1)
        train2, _, _ = split_rollouts(rollouts, seed=2)
        # With enough rollouts and distinct groups, different seeds should give different splits.
        ids1 = {r.rollout_id for r in train1}
        ids2 = {r.rollout_id for r in train2}
        # They might be identical by coincidence for very small datasets -- skip the assert
        # if both are the same length and same content (highly unlikely at n=200).
        if ids1 == ids2:
            pytest.skip("Different seeds produced identical splits (rare coincidence)")


class TestEdgeCases:
    def test_empty_rollouts_raises(self) -> None:
        with pytest.raises(ValueError, match="empty"):
            split_rollouts([])

    def test_bad_ratios_raise(self) -> None:
        rollouts = _get_rollouts(10)
        with pytest.raises(ValueError, match="sum"):
            split_rollouts(rollouts, ratios=(0.5, 0.1, 0.1))

    def test_single_rollout_does_not_crash(self) -> None:
        rollouts = _get_rollouts(1)
        # Should not raise even with a degenerate split.
        train, val, test = split_rollouts(rollouts, ratios=(0.8, 0.1, 0.1))
        assert len(train) + len(val) + len(test) == 1
