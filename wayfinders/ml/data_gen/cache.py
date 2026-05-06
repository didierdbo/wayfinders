"""Prose-hash embedding cache for the UC1 rollout pipeline.

The encoder (MiniLMEncoder) is frozen -- same prose string always produces
the same embedding.  The cache amortises the encoder cost when the same
prose appears in multiple rollouts (a near-certainty at 50k+ scale, since
the closed-vocabulary renderers produce a finite set of distinct strings).

Key design decisions:

sha256 vs Python hash():
    Python's ``hash()`` on strings is randomised by PYTHONHASHSEED across
    processes.  The cache is disk-persistent; loading it in a new process
    with a different seed would map the same prose string to a different
    key, corrupting the cache.  SHA-256 is stable, deterministic, and
    collision-safe (2^256 birthday bound >> 50k strings).

npz vs parquet:
    50k embeddings x 384 dims x 2 bytes (fp16) ~= 38 MB.  ``numpy.savez``
    handles this natively with zero dependencies beyond numpy.  Parquet
    would add pyarrow and per-row metadata overhead for no gain at M1
    scale.  If the cache grows to millions of entries (M2+) or we need
    per-entry metadata (timestamp, encoder version), switch to parquet
    then (YAGNI).

fp16 vs fp32 on disk:
    The cache stores tensors in the dtype the encoder actually returned
    (fp16 on CUDA, fp32 on CPU).  Downcasting at write time would silently
    degrade CPU-computed embeddings.  The load path restores the original
    dtype by reading the npz array dtype.

Default path:
    ``artifacts/cache/embeddings.npz`` (relative to caller's cwd, typically
    the repo root).  The ``artifacts/`` directory is in .gitignore --
    never commit cache files.
"""

from __future__ import annotations

import hashlib
import logging
from pathlib import Path
from typing import TYPE_CHECKING

import numpy as np
import torch

if TYPE_CHECKING:
    from wayfinders.ml.inference.encoder import MiniLMEncoder

logger = logging.getLogger(__name__)

# Default disk path (relative to repo root).
DEFAULT_CACHE_PATH: Path = Path("artifacts/cache/embeddings.npz")


def _prose_key(prose: str) -> bytes:
    """Return the SHA-256 digest of *prose* as raw bytes.

    This is the canonical cache key.  Using raw bytes (32 bytes) rather
    than hex strings (64 chars) halves the key size in memory.
    """
    return hashlib.sha256(prose.encode("utf-8")).digest()


class ProseHashCache:
    """In-memory + on-disk cache for prose → embedding lookups.

    Thread-safety: **not** thread-safe.  The training loop is single-
    threaded (DataLoader workers copy the cache object, so each worker has
    its own independent instance -- no sharing needed).

    Usage::

        cache = ProseHashCache()
        enc = MiniLMEncoder()
        vec = cache.compute_or_cache("Kira is a scout...", enc)

        cache.save(Path("artifacts/cache/embeddings.npz"))
        restored = ProseHashCache.load(Path("artifacts/cache/embeddings.npz"))
    """

    def __init__(self) -> None:
        self._store: dict[bytes, torch.Tensor] = {}
        self._hits: int = 0
        self._misses: int = 0

    # ------------------------------------------------------------------
    # Core API
    # ------------------------------------------------------------------

    def compute_or_cache(self, prose: str, encoder: MiniLMEncoder) -> torch.Tensor:
        """Return the embedding for *prose*, encoding it on first encounter.

        On a cache hit the stored tensor is returned directly (no copy --
        callers must not mutate the returned tensor).  On a miss the encoder
        is called, the result is stored, and the tensor is returned.

        Args:
            prose:   EN-pinned prose string.  Must be non-empty.
            encoder: Frozen MiniLMEncoder instance.

        Returns:
            Tensor of shape ``(384,)`` on the encoder's device, in the
            encoder's dtype (fp16 on CUDA, fp32 on CPU).
        """
        key = _prose_key(prose)
        if key in self._store:
            self._hits += 1
            return self._store[key]

        self._misses += 1
        vec = encoder.encode(prose)
        self._store[key] = vec
        return vec

    # ------------------------------------------------------------------
    # Persistence
    # ------------------------------------------------------------------

    def save(self, path: Path = DEFAULT_CACHE_PATH) -> None:
        """Serialise the cache to *path* as a ``.npz`` archive.

        The archive contains two arrays:
          - ``keys``:   ``(N, 32)`` uint8 -- raw SHA-256 digests
          - ``vectors``: ``(N, D)`` float16 or float32 -- embeddings

        An empty cache writes an archive with zero-row arrays (shape
        ``(0, 32)`` and ``(0, 384)``), so the load path is always valid.

        The parent directory is created if absent.
        """
        path.parent.mkdir(parents=True, exist_ok=True)

        n = len(self._store)
        if n == 0:
            # Write zero-row arrays so load() can always reconstruct.
            keys_arr = np.zeros((0, 32), dtype=np.uint8)
            # We don't know the target dtype here; default to float32.
            vecs_arr = np.zeros((0, 384), dtype=np.float32)
            np.savez(str(path), keys=keys_arr, vectors=vecs_arr)
            logger.debug("ProseHashCache.save: wrote empty cache to %s", path)
            return

        keys_list = list(self._store.keys())
        vecs_list = [self._store[k] for k in keys_list]

        keys_arr = np.frombuffer(b"".join(keys_list), dtype=np.uint8).reshape(n, 32)

        # Move tensors to CPU before converting to numpy.
        vecs_cpu = torch.stack([v.cpu() for v in vecs_list])
        vecs_arr = vecs_cpu.numpy()

        np.savez(str(path), keys=keys_arr, vectors=vecs_arr)
        logger.debug(
            "ProseHashCache.save: wrote %d entries (%s dtype) to %s",
            n,
            vecs_arr.dtype,
            path,
        )

    @classmethod
    def load(cls, path: Path = DEFAULT_CACHE_PATH) -> ProseHashCache:
        """Deserialise a cache written by :meth:`save`.

        Args:
            path: Path to the ``.npz`` file.

        Returns:
            A new ``ProseHashCache`` populated from *path*.

        Raises:
            FileNotFoundError: if *path* does not exist.
        """
        if not path.exists():
            raise FileNotFoundError(f"ProseHashCache.load: no file at {path}")

        data = np.load(str(path))
        keys_arr: np.ndarray = data["keys"]  # (N, 32) uint8
        vecs_arr: np.ndarray = data["vectors"]  # (N, D) float32 or float16

        cache = cls()
        for i in range(len(keys_arr)):
            key = keys_arr[i].tobytes()
            vec = torch.from_numpy(vecs_arr[i].copy())  # copy() avoids np strides issues
            cache._store[key] = vec

        logger.debug(
            "ProseHashCache.load: loaded %d entries (%s dtype) from %s",
            len(cache._store),
            vecs_arr.dtype,
            path,
        )
        return cache

    # ------------------------------------------------------------------
    # Stats + dunder helpers
    # ------------------------------------------------------------------

    def __len__(self) -> int:
        """Number of distinct prose strings currently in the cache."""
        return len(self._store)

    @property
    def hit_count(self) -> int:
        """Total cache hits since construction (or last manual reset)."""
        return self._hits

    @property
    def miss_count(self) -> int:
        """Total cache misses since construction (or last manual reset)."""
        return self._misses

    def reset_stats(self) -> None:
        """Reset hit/miss counters without clearing the stored embeddings."""
        self._hits = 0
        self._misses = 0

    def __repr__(self) -> str:
        return f"ProseHashCache(entries={len(self)}, hits={self._hits}, misses={self._misses})"
