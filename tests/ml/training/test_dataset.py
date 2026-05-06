"""Tests for WayfindersDataset.

Covers:
  - Correct item shape and dtype from __getitem__.
  - len() matches the number of labeled rollouts.
  - Deterministic ordering (same rollout list always produces same item at index i).
  - from_rollouts() populates cache and builds dataset without encoder calls.
  - Rollouts with None label_delta are skipped.
  - Missing-cache entry raises RuntimeError with clear message.
"""

from __future__ import annotations

import pytest
import torch

from wayfinders.ml.data_gen.cache import ProseHashCache
from wayfinders.ml.training.dataset import WayfindersDataset

# ---------------------------------------------------------------------------
# Minimal fixtures
# ---------------------------------------------------------------------------


def _make_cache_with_vecs(prose_strs: list[str], dim: int = 384) -> ProseHashCache:
    """Return a ProseHashCache pre-populated with deterministic fake embeddings."""
    from wayfinders.ml.data_gen.cache import _prose_key

    cache = ProseHashCache()
    for prose in prose_strs:
        # Use a seeded random tensor so the same prose always gets the same vec.
        torch.manual_seed(hash(prose) & 0xFFFF_FFFF)
        vec = torch.randn(dim)
        vec = vec / vec.norm()
        cache._store[_prose_key(prose)] = vec
    return cache


def _make_fake_rollouts(n: int = 5) -> list:
    """Return n minimal fake Rollout-like objects via the real generator (no encoder)."""
    from wayfinders.ml.data_gen.rollouts import generate_rollouts

    return list(generate_rollouts(n, seed=42))


# ---------------------------------------------------------------------------
# Shape and dtype tests
# ---------------------------------------------------------------------------


class TestDatasetShapes:
    def test_getitem_returns_four_tensors(self) -> None:
        rollouts = _make_fake_rollouts(4)
        all_prose = [
            p for r in rollouts for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)
        ds = WayfindersDataset(rollouts, cache)

        item = ds[0]
        assert len(item) == 4

    def test_vec_shapes_are_384(self) -> None:
        rollouts = _make_fake_rollouts(4)
        all_prose = [
            p for r in rollouts for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)
        ds = WayfindersDataset(rollouts, cache)

        char_vec, action_vec, context_vec, _label = ds[0]
        assert char_vec.shape == (384,)
        assert action_vec.shape == (384,)
        assert context_vec.shape == (384,)

    def test_label_shape_is_scalar_1(self) -> None:
        rollouts = _make_fake_rollouts(4)
        all_prose = [
            p for r in rollouts for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)
        ds = WayfindersDataset(rollouts, cache)

        _, _, _, label = ds[0]
        assert label.shape == (1,)

    def test_tensors_are_float32(self) -> None:
        rollouts = _make_fake_rollouts(4)
        all_prose = [
            p for r in rollouts for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)
        ds = WayfindersDataset(rollouts, cache)

        char_vec, action_vec, context_vec, label = ds[0]
        assert char_vec.dtype == torch.float32
        assert action_vec.dtype == torch.float32
        assert context_vec.dtype == torch.float32
        assert label.dtype == torch.float32

    def test_tensors_are_on_cpu(self) -> None:
        rollouts = _make_fake_rollouts(4)
        all_prose = [
            p for r in rollouts for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)
        ds = WayfindersDataset(rollouts, cache)

        char_vec, _action_vec, _context_vec, label = ds[0]
        assert char_vec.device.type == "cpu"
        assert label.device.type == "cpu"


# ---------------------------------------------------------------------------
# Length
# ---------------------------------------------------------------------------


class TestDatasetLen:
    def test_len_matches_rollout_count(self) -> None:
        n = 7
        rollouts = _make_fake_rollouts(n)
        all_prose = [
            p for r in rollouts for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)
        ds = WayfindersDataset(rollouts, cache)
        assert len(ds) == n

    def test_len_skips_none_labels(self) -> None:
        """Rollouts with label_delta=None are excluded from the dataset."""
        rollouts = _make_fake_rollouts(6)
        # Patch two rollouts to have None label.

        patched = [
            r.model_copy(update={"label_delta": None}) if i < 2 else r
            for i, r in enumerate(rollouts)
        ]
        all_prose = [
            p
            for r in rollouts  # use original prose to populate cache
            for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)
        ds = WayfindersDataset(patched, cache)
        assert len(ds) == 4


# ---------------------------------------------------------------------------
# Determinism
# ---------------------------------------------------------------------------


class TestDatasetDeterminism:
    def test_same_rollouts_same_items(self) -> None:
        rollouts = _make_fake_rollouts(5)
        all_prose = [
            p for r in rollouts for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)

        ds1 = WayfindersDataset(rollouts, cache)
        ds2 = WayfindersDataset(rollouts, cache)

        for i in range(len(ds1)):
            cv1, av1, xv1, lb1 = ds1[i]
            cv2, av2, xv2, lb2 = ds2[i]
            assert torch.allclose(cv1, cv2)
            assert torch.allclose(av1, av2)
            assert torch.allclose(xv1, xv2)
            assert torch.allclose(lb1, lb2)

    def test_ordering_is_rollout_order(self) -> None:
        """Item i in the dataset corresponds to rollout i."""
        rollouts = _make_fake_rollouts(5)
        all_prose = [
            p for r in rollouts for p in (r.prose_character, r.prose_action, r.prose_context)
        ]
        cache = _make_cache_with_vecs(all_prose)
        ds = WayfindersDataset(rollouts, cache)

        for i, r in enumerate(rollouts):
            _, _, _, label = ds[i]
            assert abs(label.item() - (r.label_delta or 0.0)) < 1e-5


# ---------------------------------------------------------------------------
# from_rollouts factory
# ---------------------------------------------------------------------------


class TestFromRollouts:
    def test_from_rollouts_populates_cache(self) -> None:
        """from_rollouts should call encoder for cache misses and store results."""
        rollouts = _make_fake_rollouts(3)
        cache = ProseHashCache()
        assert len(cache) == 0

        # Build a fake encoder that returns zero vecs (fast, deterministic).
        class FakeEncoder:
            def encode_batch(self, proses: list[str]) -> torch.Tensor:
                return torch.zeros(len(proses), 384)

            def encode(self, prose: str) -> torch.Tensor:
                return torch.zeros(384)

        enc = FakeEncoder()
        ds = WayfindersDataset.from_rollouts(rollouts, enc, cache)  # type: ignore[arg-type]
        assert len(cache) > 0
        assert len(ds) == len(rollouts)

    def test_from_rollouts_second_call_no_encode(self) -> None:
        """Second from_rollouts call with same rollouts should produce zero encoder calls."""
        rollouts = _make_fake_rollouts(3)
        cache = ProseHashCache()

        encode_call_count = [0]

        class CountingEncoder:
            def encode_batch(self, proses: list[str]) -> torch.Tensor:
                encode_call_count[0] += len(proses)
                return torch.zeros(len(proses), 384)

            def encode(self, prose: str) -> torch.Tensor:
                encode_call_count[0] += 1
                return torch.zeros(384)

        enc = CountingEncoder()
        WayfindersDataset.from_rollouts(rollouts, enc, cache)  # type: ignore[arg-type]
        # Confirm first call actually encoded something (sanity-check on the encoder).
        assert encode_call_count[0] > 0

        # Second call -- cache should already be populated.
        encode_call_count[0] = 0
        WayfindersDataset.from_rollouts(rollouts, enc, cache)  # type: ignore[arg-type]
        assert encode_call_count[0] == 0, (
            f"Second from_rollouts made {encode_call_count[0]} encoder calls; expected 0"
        )


# ---------------------------------------------------------------------------
# Error cases
# ---------------------------------------------------------------------------


class TestDatasetErrors:
    def test_missing_cache_entry_raises(self) -> None:
        """Constructing a dataset with prose not in cache raises RuntimeError."""
        rollouts = _make_fake_rollouts(2)
        empty_cache = ProseHashCache()

        with pytest.raises(RuntimeError, match="not found in cache"):
            WayfindersDataset(rollouts, empty_cache)

    def test_empty_rollouts_raises(self) -> None:
        cache = ProseHashCache()
        with pytest.raises(ValueError, match="at least one rollout"):
            WayfindersDataset([], cache)
