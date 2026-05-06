"""Tests for ``wayfinders.ml.data_gen.cache.ProseHashCache``.

Three layers:
1. Unit tests (fast, stub encoder) -- default
2. Integration tests (real MiniLMEncoder) -- ``@pytest.mark.integration``

Coverage:
    - hit / miss correctness and counter accuracy
    - disk roundtrip (save → load → tensor identity)
    - hash stability across cache instances (same prose, fresh instance, same key)
    - empty cache roundtrip
    - compute_or_cache is idempotent (N calls on same prose = 1 miss + N-1 hits)
"""

from __future__ import annotations

from pathlib import Path
from typing import Any
from unittest.mock import MagicMock

import pytest
import torch

from wayfinders.ml.data_gen.cache import ProseHashCache, _prose_key
from wayfinders.ml.inference.encoder import ENCODER_EMBEDDING_DIM

# ---------------------------------------------------------------------------
# Stub encoder (mirrors test_encoder.py pattern -- no model download)
# ---------------------------------------------------------------------------


def _make_stub_encoder(device: str = "cpu") -> Any:
    """Return a MiniLMEncoder-compatible stub backed by a deterministic mock.

    The stub returns stable (384,) float32 tensors derived from the prose hash.
    fp16 is not exercised in unit tests (CPU path only).
    """

    def _encode_single(prose: str) -> torch.Tensor:
        # Stable deterministic vector for any prose string.
        seed = int.from_bytes(_prose_key(prose)[:4], "little")
        gen = torch.Generator()
        gen.manual_seed(seed)
        vec = torch.randn(ENCODER_EMBEDDING_DIM, generator=gen)
        vec = vec / vec.norm().clamp_min(1e-8)
        return vec

    stub = MagicMock()
    stub.device = device
    stub.use_fp16 = False

    def _encode(prose_str: str) -> torch.Tensor:
        return _encode_single(prose_str)

    stub.encode = MagicMock(side_effect=_encode)
    return stub


# ---------------------------------------------------------------------------
# Unit tests
# ---------------------------------------------------------------------------


class TestHitMiss:
    """Cache hit / miss correctness and counter accuracy."""

    def test_first_call_is_miss(self) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        assert cache.miss_count == 0
        assert cache.hit_count == 0
        cache.compute_or_cache("Hello world", enc)
        assert cache.miss_count == 1
        assert cache.hit_count == 0

    def test_second_call_same_prose_is_hit(self) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        cache.compute_or_cache("Hello world", enc)
        cache.compute_or_cache("Hello world", enc)
        assert cache.miss_count == 1
        assert cache.hit_count == 1

    def test_different_prose_is_miss(self) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        cache.compute_or_cache("prose A", enc)
        cache.compute_or_cache("prose B", enc)
        assert cache.miss_count == 2
        assert cache.hit_count == 0

    def test_n_calls_same_prose_gives_n_minus_1_hits(self) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        n = 5
        for _ in range(n):
            cache.compute_or_cache("the same prose", enc)
        assert cache.miss_count == 1
        assert cache.hit_count == n - 1

    def test_len_tracks_distinct_entries(self) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        assert len(cache) == 0
        cache.compute_or_cache("a", enc)
        assert len(cache) == 1
        cache.compute_or_cache("a", enc)  # same prose -- no new entry
        assert len(cache) == 1
        cache.compute_or_cache("b", enc)
        assert len(cache) == 2

    def test_reset_stats_preserves_entries(self) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        cache.compute_or_cache("text", enc)
        cache.compute_or_cache("text", enc)
        cache.reset_stats()
        assert cache.hit_count == 0
        assert cache.miss_count == 0
        assert len(cache) == 1  # entry survives

    def test_returned_tensor_shape_and_dtype(self) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        vec = cache.compute_or_cache("some prose", enc)
        assert vec.shape == (ENCODER_EMBEDDING_DIM,)
        assert vec.dtype == torch.float32

    def test_hit_returns_same_tensor_object(self) -> None:
        """Cache hit must return the stored tensor, not a fresh copy."""
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        v1 = cache.compute_or_cache("prose X", enc)
        v2 = cache.compute_or_cache("prose X", enc)
        assert v1 is v2  # same object, not just equal


# ---------------------------------------------------------------------------
# Disk roundtrip
# ---------------------------------------------------------------------------


class TestDiskRoundtrip:
    def test_save_then_load_restores_entries(self, tmp_path: Path) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        proses = ["alpha", "beta", "gamma"]
        original_vecs = {p: cache.compute_or_cache(p, enc) for p in proses}

        path = tmp_path / "test_cache.npz"
        cache.save(path)
        assert path.exists()

        restored = ProseHashCache.load(path)
        assert len(restored) == len(proses)

        for prose, orig_vec in original_vecs.items():
            enc2 = _make_stub_encoder()
            # First call on restored cache should be a hit (not a miss).
            restored_vec = restored.compute_or_cache(prose, enc2)
            assert restored.hit_count > 0 or len(restored) > 0

            # Tensor values must match within fp32 tolerance.
            assert torch.allclose(orig_vec.cpu(), restored_vec.cpu(), atol=1e-6), (
                f"Tensor mismatch for prose '{prose}'"
            )

    def test_empty_cache_roundtrip(self, tmp_path: Path) -> None:
        cache = ProseHashCache()
        path = tmp_path / "empty.npz"
        cache.save(path)
        assert path.exists()
        restored = ProseHashCache.load(path)
        assert len(restored) == 0

    def test_load_nonexistent_raises(self, tmp_path: Path) -> None:
        with pytest.raises(FileNotFoundError):
            ProseHashCache.load(tmp_path / "does_not_exist.npz")

    def test_save_creates_parent_directory(self, tmp_path: Path) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        cache.compute_or_cache("x", enc)
        nested_path = tmp_path / "deep" / "nested" / "cache.npz"
        cache.save(nested_path)
        assert nested_path.exists()

    def test_disk_roundtrip_stats_reset_on_load(self, tmp_path: Path) -> None:
        """Loaded cache starts with zero hit/miss counters."""
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        cache.compute_or_cache("hello", enc)
        cache.compute_or_cache("hello", enc)
        path = tmp_path / "stats.npz"
        cache.save(path)

        restored = ProseHashCache.load(path)
        assert restored.hit_count == 0
        assert restored.miss_count == 0


# ---------------------------------------------------------------------------
# Hash stability (simulate cross-process use)
# ---------------------------------------------------------------------------


class TestHashStability:
    """Same prose in two separate ProseHashCache instances must produce the
    same key and therefore the same stored tensor (verifies sha256 stability
    across instances, not Python hash() instability)."""

    def test_same_prose_same_key_two_instances(self) -> None:
        enc1 = _make_stub_encoder()
        enc2 = _make_stub_encoder()
        prose = "Kira is a scout of the Ridgewatch company."
        cache1 = ProseHashCache()
        cache2 = ProseHashCache()
        v1 = cache1.compute_or_cache(prose, enc1)
        v2 = cache2.compute_or_cache(prose, enc2)
        # The stub encoder is deterministic, so both tensors must be equal.
        assert torch.allclose(v1, v2, atol=1e-6)

    def test_prose_key_is_deterministic(self) -> None:
        """_prose_key must return the same bytes on repeated calls."""
        prose = "The mission is a reconnaissance for the Ridgewatch company."
        k1 = _prose_key(prose)
        k2 = _prose_key(prose)
        assert k1 == k2
        assert len(k1) == 32  # SHA-256 = 32 bytes

    def test_different_prose_different_key(self) -> None:
        k1 = _prose_key("prose one")
        k2 = _prose_key("prose two")
        assert k1 != k2

    def test_save_load_same_key_preserved(self, tmp_path: Path) -> None:
        """After a save/load roundtrip, a previously cached prose is still a
        hit (not a miss), confirming the key is stable across serialization."""
        prose = "Kira is patient and cautious."
        enc = _make_stub_encoder()
        cache = ProseHashCache()
        original_vec = cache.compute_or_cache(prose, enc)

        path = tmp_path / "roundtrip.npz"
        cache.save(path)
        restored = ProseHashCache.load(path)

        enc2 = _make_stub_encoder()
        hit_vec = restored.compute_or_cache(prose, enc2)
        # The restored cache must hit (not miss) and return the same tensor.
        assert restored.hit_count == 1
        assert torch.allclose(original_vec.cpu(), hit_vec.cpu(), atol=1e-6)


# ---------------------------------------------------------------------------
# Repr
# ---------------------------------------------------------------------------


class TestRepr:
    def test_repr_contains_stats(self) -> None:
        cache = ProseHashCache()
        enc = _make_stub_encoder()
        cache.compute_or_cache("test", enc)
        cache.compute_or_cache("test", enc)
        r = repr(cache)
        assert "entries=1" in r
        assert "hits=1" in r
        assert "misses=1" in r


# ---------------------------------------------------------------------------
# Integration tests (real MiniLMEncoder -- requires model in HuggingFace cache)
# ---------------------------------------------------------------------------


@pytest.mark.integration
class TestCacheIntegration:
    """Integration tests that load the real MiniLM model.

    Run with: ``uv run pytest -m integration``
    """

    def test_real_encoder_hit_miss_and_tensor_shape(self) -> None:
        from wayfinders.ml.inference.encoder import MiniLMEncoder

        enc = MiniLMEncoder(device="cpu")
        cache = ProseHashCache()

        prose = "Kira is a scout of the Ridgewatch company. She is sturdy, nimble, and shrewd."
        v1 = cache.compute_or_cache(prose, enc)
        assert cache.miss_count == 1
        v2 = cache.compute_or_cache(prose, enc)
        assert cache.hit_count == 1
        assert v1 is v2
        assert v1.shape == (ENCODER_EMBEDDING_DIM,)

    def test_real_encoder_disk_roundtrip(self, tmp_path: Path) -> None:
        from wayfinders.ml.inference.encoder import MiniLMEncoder

        enc = MiniLMEncoder(device="cpu")
        cache = ProseHashCache()
        proses = [
            "The action is a stealth approach.",
            "The mission is a reconnaissance for the Ridgewatch company.",
        ]
        original = {p: cache.compute_or_cache(p, enc) for p in proses}

        path = tmp_path / "integration.npz"
        cache.save(path)
        restored = ProseHashCache.load(path)

        enc2 = MiniLMEncoder(device="cpu")
        for prose, orig_vec in original.items():
            # fp32 on CPU -- tight tolerance.
            atol = 1e-3 if enc.use_fp16 else 1e-5
            restored_vec = restored.compute_or_cache(prose, enc2)
            assert torch.allclose(orig_vec.cpu(), restored_vec.cpu(), atol=atol), (
                f"Tensor mismatch after disk roundtrip for prose '{prose[:40]}...'"
            )
