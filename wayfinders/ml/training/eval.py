"""Evaluation metrics for the UC1 resolution head.

``evaluate()`` runs the model in eval mode over a DataLoader and returns
an ``EvalMetrics`` Pydantic model containing MAE, RMSE, R^2, per-bucket MAE,
and total sample count.

Design notes:

  - Bucket edges are fixed at 1.0-unit intervals over [-5, +5] (11 bins).
    This matches the designer modifier table's integer-ish label distribution
    and gives a clean per-tier breakdown (e.g. "strong negative zone" vs
    "neutral zone").

  - R^2 is the coefficient of determination, not Pearson's r^2.
    R^2 < 0 means the model is worse than always predicting the mean --
    a useful sanity-check for freshly-initialised or badly-overfit heads.

  - All tensors are moved to CPU before metric accumulation so the function
    works regardless of the training device.

  - The model is NOT put into eval mode by this function -- the caller is
    responsible. This keeps ``evaluate()`` usable in tests where the caller
    controls the mode.
"""

from __future__ import annotations

import itertools
import logging
from typing import Any

import torch
from pydantic import BaseModel, ConfigDict
from torch.utils.data import DataLoader

from wayfinders.ml.training.head import ResolutionHead

logger = logging.getLogger(__name__)

# Bucket edges for label_delta ∈ [-5, +5] at 1.0-unit intervals.
# 11 bins: [-5,-4), [-4,-3), ..., [-1,0), [0,+1), ..., [+4,+5]
_BUCKET_EDGES: tuple[float, ...] = tuple(float(v) for v in range(-5, 6))
_BUCKET_LABELS: tuple[str, ...] = tuple(
    f"[{lo:.0f},{hi:.0f})" if hi < 5 else f"[{lo:.0f},{hi:.0f}]"
    for lo, hi in itertools.pairwise(_BUCKET_EDGES)
)


class EvalMetrics(BaseModel):
    """Evaluation metrics from a single pass over an eval DataLoader.

    Attributes:
        mae:           Mean absolute error over all samples.
        rmse:          Root mean squared error over all samples.
        r_squared:     Coefficient of determination (R^2). Negative means
                       model is worse than predicting the mean.
        mae_by_bucket: Per-bucket MAE keyed by bucket label string.
                       Buckets with zero samples have MAE = None.
        count:         Total number of samples evaluated.
    """

    model_config = ConfigDict(frozen=True)

    mae: float
    rmse: float
    r_squared: float
    mae_by_bucket: dict[str, float | None]
    count: int


def evaluate(
    model: ResolutionHead,
    dataloader: DataLoader[Any],
    device: str,
) -> EvalMetrics:
    """Run a full evaluation pass and return EvalMetrics.

    The model's current mode (train vs eval) is left unchanged by this
    function. The caller should call ``model.eval()`` before invoking if
    dropout and batchnorm should be disabled.

    Args:
        model:      ResolutionHead instance (any device, any mode).
        dataloader: DataLoader yielding (char_vec, action_vec, context_vec, label)
                    batches. Labels shape: (batch, 1).
        device:     Device string to move batches to ("cpu", "cuda", etc.).

    Returns:
        EvalMetrics Pydantic model.
    """
    all_preds: list[torch.Tensor] = []
    all_targets: list[torch.Tensor] = []

    with torch.no_grad():
        for batch in dataloader:
            char_vec, action_vec, context_vec, label = batch
            char_vec = char_vec.to(device)
            action_vec = action_vec.to(device)
            context_vec = context_vec.to(device)
            label = label.to(device)

            pred = model(char_vec, action_vec, context_vec)  # (batch, 1)
            all_preds.append(pred.cpu())
            all_targets.append(label.cpu())

    if not all_preds:
        raise ValueError("evaluate() received an empty dataloader -- no samples to evaluate")

    preds = torch.cat(all_preds, dim=0).squeeze(1)  # (N,)
    targets = torch.cat(all_targets, dim=0).squeeze(1)  # (N,)
    n = len(preds)

    abs_errors = (preds - targets).abs()
    mae = abs_errors.mean().item()
    rmse = ((preds - targets).pow(2).mean().sqrt()).item()

    target_mean = targets.mean()
    ss_res = (targets - preds).pow(2).sum().item()
    ss_tot = (targets - target_mean).pow(2).sum().item()
    r_squared = 1.0 - ss_res / ss_tot if ss_tot > 0 else 0.0

    # Per-bucket MAE.
    mae_by_bucket: dict[str, float | None] = {}
    for bucket_label, (lo, hi) in zip(
        _BUCKET_LABELS, itertools.pairwise(_BUCKET_EDGES), strict=True
    ):
        if hi == _BUCKET_EDGES[-1]:
            # Last bucket is closed on the right.
            mask = (targets >= lo) & (targets <= hi)
        else:
            mask = (targets >= lo) & (targets < hi)
        if mask.sum().item() == 0:
            mae_by_bucket[bucket_label] = None
        else:
            mae_by_bucket[bucket_label] = abs_errors[mask].mean().item()

    logger.info(
        "evaluate: n=%d MAE=%.4f RMSE=%.4f R2=%.4f",
        n,
        mae,
        rmse,
        r_squared,
    )

    return EvalMetrics(
        mae=mae,
        rmse=rmse,
        r_squared=r_squared,
        mae_by_bucket=mae_by_bucket,
        count=n,
    )
