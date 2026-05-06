"""WayfindersDataset -- PyTorch Dataset over Rollout lists.

Each item is a pre-encoded tuple ready for ResolutionHead.forward():

    (char_vec, action_vec, context_vec, label)

where the three vecs are 384-dim fp32 CPU tensors and label is a scalar
float32 tensor of shape (1,).

The dataset does NOT load the encoder itself -- it requires a pre-built
ProseHashCache that has already populated embeddings for all rollouts in
the list. The recommended pattern:

    1. Build cache via WayfindersDataset.from_rollouts() -- batch-encodes
       all prose strings, populates cache, then constructs the dataset.
    2. On subsequent runs, re-use the saved cache: the dataset loads purely
       from cache with zero encoder calls.

This separation keeps the Dataset pickle-safe for DataLoader workers:
the cache only holds numpy-backed tensors (loaded from .npz) and dicts,
which are safe to fork / copy.

Determinism:
    Items are stored in rollouts-list order. __getitem__(i) always returns
    the item for rollouts[i]. DataLoader shuffling is the caller's job.
"""

from __future__ import annotations

import logging
from typing import TYPE_CHECKING

import torch
from torch.utils.data import Dataset

from wayfinders.ml.data_gen.cache import ProseHashCache, _prose_key

if TYPE_CHECKING:
    from wayfinders.ml.data_gen.rollouts import Rollout
    from wayfinders.ml.inference.encoder import MiniLMEncoder

logger = logging.getLogger(__name__)


def _cache_get(cache: ProseHashCache, prose: str) -> torch.Tensor:
    """Return the cached embedding for *prose*, raising RuntimeError on miss.

    Direct access to cache._store is intentional: ProseHashCache has no
    get() method, and we want to distinguish a genuine miss (bug: caller
    forgot to populate cache) from a hit without calling the encoder.
    """
    key = _prose_key(prose)
    vec = cache._store.get(key)  # private attribute access; intentional (see module docstring)
    if vec is None:
        raise RuntimeError(
            "WayfindersDataset: prose string not found in cache -- "
            "call from_rollouts() to populate the cache before constructing "
            f"the dataset directly. Missing prose (first 80 chars): {prose[:80]!r}"
        )
    return vec


class WayfindersDataset(Dataset[tuple[torch.Tensor, torch.Tensor, torch.Tensor, torch.Tensor]]):
    """PyTorch Dataset wrapping a list of Rollouts + pre-populated ProseHashCache.

    Each item is ``(char_vec, action_vec, context_vec, label)`` where:
      - char_vec, action_vec, context_vec: shape (384,), fp32, CPU
      - label: shape (1,), fp32

    All tensors are on CPU and fp32 regardless of encoder precision.
    The DataLoader pin_memory path and the training loop .to(device) handle
    device/dtype placement at batch time.
    """

    def __init__(
        self,
        rollouts: list[Rollout],
        cache: ProseHashCache,
    ) -> None:
        """
        Args:
            rollouts: List of Rollout objects. All three prose strings must be
                      non-empty and present in ``cache``.
            cache:    A ProseHashCache populated (at minimum) with all prose
                      strings present in ``rollouts``.

        Raises:
            ValueError:   if rollouts is empty or no rollout has a valid label_delta.
            RuntimeError: if a prose string is absent from the cache.
        """
        if not rollouts:
            raise ValueError("WayfindersDataset requires at least one rollout")

        self._char_vecs: list[torch.Tensor] = []
        self._action_vecs: list[torch.Tensor] = []
        self._context_vecs: list[torch.Tensor] = []
        self._labels: list[torch.Tensor] = []

        missing_labels = 0
        for idx, r in enumerate(rollouts):
            if r.label_delta is None:
                missing_labels += 1
                if missing_labels == 1:
                    logger.warning(
                        "WayfindersDataset: rollout %d has no label_delta -- "
                        "items with None label will be skipped",
                        idx,
                    )
                continue

            char_vec = _cache_get(cache, r.prose_character)
            action_vec = _cache_get(cache, r.prose_action)
            context_vec = _cache_get(cache, r.prose_context)

            # Normalise to fp32 CPU for DataLoader compatibility.
            self._char_vecs.append(char_vec.float().cpu())
            self._action_vecs.append(action_vec.float().cpu())
            self._context_vecs.append(context_vec.float().cpu())
            self._labels.append(torch.tensor([r.label_delta], dtype=torch.float32))

        if missing_labels > 0:
            logger.warning(
                "WayfindersDataset: skipped %d rollouts with None label_delta (total kept: %d)",
                missing_labels,
                len(self._labels),
            )

        if not self._labels:
            raise ValueError(
                "WayfindersDataset: no rollouts had a valid label_delta -- "
                "cannot build an empty dataset"
            )

        logger.info("WayfindersDataset: built %d items", len(self._labels))

    # ------------------------------------------------------------------
    # Dataset protocol
    # ------------------------------------------------------------------

    def __len__(self) -> int:
        return len(self._labels)

    def __getitem__(
        self, idx: int
    ) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor, torch.Tensor]:
        return (
            self._char_vecs[idx],
            self._action_vecs[idx],
            self._context_vecs[idx],
            self._labels[idx],
        )

    # ------------------------------------------------------------------
    # Factory
    # ------------------------------------------------------------------

    @classmethod
    def from_rollouts(
        cls,
        rollouts: list[Rollout],
        encoder: MiniLMEncoder,
        cache: ProseHashCache,
    ) -> WayfindersDataset:
        """Build a dataset, populating the cache for any prose strings not yet encoded.

        This is the canonical entry point when the cache may be incomplete
        (e.g. the very first run). The encoder is called for cache misses and
        the results are written to the cache in-place. Subsequent calls with
        the same rollouts are pure cache hits (no encoder calls).

        Args:
            rollouts: List of Rollout objects (label_delta required).
            encoder:  A frozen MiniLMEncoder instance.
            cache:    A ProseHashCache (may be empty; will be populated).

        Returns:
            A fully-constructed WayfindersDataset.
        """
        logger.info(
            "WayfindersDataset.from_rollouts: populating cache for %d rollouts ...",
            len(rollouts),
        )

        # Gather unique prose strings that are not yet in the cache.
        unique_prose: list[str] = []
        seen: set[str] = set()
        for r in rollouts:
            for prose in (r.prose_character, r.prose_action, r.prose_context):
                if prose and prose not in seen:
                    seen.add(prose)
                    key = _prose_key(prose)
                    if cache._store.get(key) is None:
                        unique_prose.append(prose)

        if unique_prose:
            logger.info(
                "WayfindersDataset.from_rollouts: encoding %d unique cache-miss strings",
                len(unique_prose),
            )
            # Encode in chunks to avoid OOM on very large batches.
            chunk_size = 512
            for chunk_start in range(0, len(unique_prose), chunk_size):
                chunk = unique_prose[chunk_start : chunk_start + chunk_size]
                vecs = encoder.encode_batch(chunk)
                for prose, vec in zip(chunk, vecs, strict=True):
                    cache._store[_prose_key(prose)] = vec.float().cpu()
        else:
            logger.info("WayfindersDataset.from_rollouts: all prose strings already cached")

        return cls(rollouts, cache)
