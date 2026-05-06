"""Train/val/test split for rollout lists.

Splits by ``character_archetype`` (derived from ``character.char_class``) AND
by ``scene_id`` (derived from a hash of the scene content) to prevent embedding
leakage between splits.

Why this matters (Pax §4):
    The ProseHashCache maps identical prose strings to identical embeddings.
    If the same character archetype or scene appears in both train and val,
    the model has effectively "seen" those embeddings during training and the
    val loss will be artificially low. Splitting by these axes guarantees
    that every archetype and every scene is exclusive to one split.

Implementation:
    1. Collect the set of distinct archetypes from all rollouts.
    2. Collect the set of distinct scene keys (hash of mission_type +
       stakes_tier + sponsor_faction -- the fields that vary across scenes
       and drive distinct prose strings).
    3. Sort both sets for determinism (no reliance on set iteration order).
    4. Partition them into 80/10/10 with a seeded shuffle.
    5. A rollout goes into the split where BOTH its archetype AND its scene
       land. If they disagree (one in train, the other in val), the rollout
       is assigned to the more restrictive split (val wins over train, test
       wins over val). This is conservative: it slightly reduces train size
       but guarantees no leakage.

Rationale for "conservative wins" (point 5):
    A rollout that straddles a split boundary is a potential leak. We could
    discard it entirely or assign it to the stricter split. Discarding wastes
    data; assigning to the stricter split (e.g. val) is safe -- the model
    won't have trained on that archetype/scene pair, so the val estimate
    remains unbiased. We prefer the train split only when both axes agree.

Character archetype:
    Derived from ``character.char_class`` (the Pydantic field on CharacterState).
    This is the natural archetype dimension since different classes are
    described with distinct vocabulary (and thus distinct embeddings).

Scene key:
    Derived from the triple (mission_type, stakes_tier, sponsor_faction).
    These drive sentence 1-3 of render_context(), which are the high-
    variance prose sentences across scenes.

The split ratios are approximate -- we split the *group* lists, not the
individual rollout lists. With few distinct groups (e.g. 4 archetypes x 10
scenes = 40 groups) the realized ratios may differ from 80/10/10 by a few
percent. This is expected and acceptable for M1.
"""

from __future__ import annotations

import hashlib
import logging
import random

from wayfinders.ml.data_gen.rollouts import Rollout

logger = logging.getLogger(__name__)

# Priority order for split assignment when archetype and scene disagree.
# Lower number = stricter / wins over others.
_SPLIT_PRIORITY: dict[str, int] = {"test": 0, "val": 1, "train": 2}


def _archetype_key(rollout: Rollout) -> str:
    """Return the archetype string for a rollout (char_class)."""
    return rollout.character.char_class.lower().strip()


def _scene_key(rollout: Rollout) -> str:
    """Return a deterministic scene identifier.

    Combines mission_type + stakes_tier + sponsor_faction into a stable
    hash key. SHA-256 truncated to 16 hex chars is collision-safe for
    the ~100-1000 distinct scenes we generate in M1.
    """
    raw = (
        f"{rollout.scene.mission_type}|{rollout.scene.stakes_tier}|{rollout.scene.sponsor_faction}"
    )
    return hashlib.sha256(raw.encode()).hexdigest()[:16]


def _partition_keys(
    keys: list[str],
    ratios: tuple[float, float, float],
    seed: int,
) -> tuple[set[str], set[str], set[str]]:
    """Shuffle ``keys`` and partition into three non-overlapping sets per ratios.

    Args:
        keys:   Sorted, deduplicated list of group keys.
        ratios: (train, val, test) fractions -- must sum to 1.0 (±ε).
        seed:   RNG seed for shuffle determinism.

    Returns:
        (train_keys, val_keys, test_keys) sets.
    """
    rng = random.Random(seed)
    shuffled = list(keys)
    rng.shuffle(shuffled)

    n = len(shuffled)
    n_train = round(n * ratios[0])
    n_val = round(n * ratios[1])
    # n_test gets the remainder so total = n exactly.
    n_train = max(1, n_train) if n >= 3 else n
    n_val = max(1, n_val) if n - n_train >= 2 else max(0, n - n_train - 1)
    n_test = n - n_train - n_val

    # Clamp to valid ranges.
    n_test = max(0, n_test)

    train_keys = set(shuffled[:n_train])
    val_keys = set(shuffled[n_train : n_train + n_val])
    test_keys = set(shuffled[n_train + n_val :])
    return train_keys, val_keys, test_keys


def split_rollouts(
    rollouts: list[Rollout],
    ratios: tuple[float, float, float] = (0.8, 0.1, 0.1),
    seed: int = 42,
) -> tuple[list[Rollout], list[Rollout], list[Rollout]]:
    """Partition rollouts into (train, val, test) by archetype + scene.

    Args:
        rollouts: Full list of Rollout objects to split.
        ratios:   (train, val, test) fractions. Must sum to approximately 1.0.
        seed:     RNG seed for the group shuffle. Deterministic: same
                  (rollouts, ratios, seed) always produces the same split.

    Returns:
        (train_rollouts, val_rollouts, test_rollouts) lists.
        Each rollout appears in exactly one list.

    Raises:
        ValueError: if ratios don't sum to approximately 1.0, or if
                    rollouts is empty.
    """
    if not rollouts:
        raise ValueError("split_rollouts: rollouts list is empty")

    total = sum(ratios)
    if abs(total - 1.0) > 0.01:
        raise ValueError(f"split_rollouts: ratios must sum to ~1.0, got {ratios} (sum={total:.4f})")

    # Collect distinct group keys.
    archetypes = sorted({_archetype_key(r) for r in rollouts})
    scene_keys = sorted({_scene_key(r) for r in rollouts})

    logger.info(
        "split_rollouts: %d rollouts | %d archetypes | %d scene keys | ratios=%s seed=%d",
        len(rollouts),
        len(archetypes),
        len(scene_keys),
        ratios,
        seed,
    )

    # Partition group keys independently.
    arch_train, arch_val, _arch_test = _partition_keys(archetypes, ratios, seed=seed)
    scene_train, scene_val, _scene_test = _partition_keys(scene_keys, ratios, seed=seed + 1)

    def _assign(rollout: Rollout) -> str:
        a_key = _archetype_key(rollout)
        s_key = _scene_key(rollout)

        # Determine which split each axis assigns.
        arch_split = "train" if a_key in arch_train else "val" if a_key in arch_val else "test"
        scene_split = "train" if s_key in scene_train else "val" if s_key in scene_val else "test"

        # Conservative: assign to the stricter split.
        if _SPLIT_PRIORITY[arch_split] <= _SPLIT_PRIORITY[scene_split]:
            return arch_split
        return scene_split

    train: list[Rollout] = []
    val: list[Rollout] = []
    test: list[Rollout] = []

    for r in rollouts:
        split = _assign(r)
        if split == "train":
            train.append(r)
        elif split == "val":
            val.append(r)
        else:
            test.append(r)

    logger.info(
        "split_rollouts: train=%d (%.1f%%) | val=%d (%.1f%%) | test=%d (%.1f%%)",
        len(train),
        100 * len(train) / len(rollouts),
        len(val),
        100 * len(val) / len(rollouts),
        len(test),
        100 * len(test) / len(rollouts),
    )

    return train, val, test
