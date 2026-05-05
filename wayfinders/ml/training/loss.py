"""Huber loss + monotonicity regularizer.

Pax 2026-05-02 sec. 3 -- Huber (delta=1.0) tames designer-table outliers without
ignoring them. Monotonicity reg catches the embarrassing failure mode where
adding a positive legacy event would *decrease* delta.
"""

from __future__ import annotations

import torch
from torch import nn


class HuberWithMonotonicity(nn.Module):
    """Huber loss with optional monotonicity penalty term.

    Args:
        delta: Huber transition point (default 1.0).
        monotonicity_weight: scalar weight on the monotonicity regularizer.
            0.0 disables. Default 0.1.
    """

    def __init__(self, delta: float = 1.0, monotonicity_weight: float = 0.1) -> None:
        super().__init__()
        self.huber = nn.HuberLoss(delta=delta, reduction="mean")
        self.monotonicity_weight = monotonicity_weight

    def forward(
        self,
        pred: torch.Tensor,
        target: torch.Tensor,
        mono_pred_lo: torch.Tensor | None = None,
        mono_pred_hi: torch.Tensor | None = None,
    ) -> torch.Tensor:
        """
        Args:
            pred, target: shape (batch, 1).
            mono_pred_lo, mono_pred_hi: optional, shape (batch_mono, 1) each.
                For each pair (lo, hi), `lo` was rendered with N positive
                legacy events and `hi` with N+1 (one more positive event).
                We penalize ``relu(lo - hi)``: predictions should weakly
                increase as positive legacy grows.
        """
        loss = self.huber(pred, target)
        if mono_pred_lo is not None and mono_pred_hi is not None and self.monotonicity_weight > 0:
            violation = torch.relu(mono_pred_lo - mono_pred_hi).mean()
            loss = loss + self.monotonicity_weight * violation
        return loss
