"""Training loop scaffolding -- Lightning-free, explicit.

M1 substep 9 fills this in. Locked here: the function signature, the
artefact layout, and the determinism contract (seed everything).
"""

from __future__ import annotations

import json
import random
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import torch
from torch import nn
from torch.utils.data import DataLoader

from wayfinders.ml.training.head import ResolutionHead
from wayfinders.ml.training.loss import HuberWithMonotonicity


@dataclass(frozen=True)
class TrainConfig:
    """Configuration for a training run.

    Frozen so the same config is reproducible across runs.
    """

    rollouts_path: Path
    output_dir: Path
    epochs: int = 50
    batch_size: int = 256
    learning_rate: float = 3e-4
    weight_decay: float = 1e-4
    dropout: float = 0.1
    huber_delta: float = 1.0
    monotonicity_weight: float = 0.1
    seed: int = 42
    device: str = "cuda" if torch.cuda.is_available() else "cpu"


def seed_everything(seed: int) -> None:
    """Seed Python, NumPy, and PyTorch RNGs.

    Note: full bit-reproducibility on GPU also requires
    ``torch.backends.cudnn.deterministic = True`` which trades speed.
    For M1 we accept the speed and trust seed-based reproducibility on
    CPU paths.
    """
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


def train_loop(
    config: TrainConfig,
    train_loader: DataLoader,
    val_loader: DataLoader,
) -> dict[str, float]:
    """Run the full training loop and return final metrics.

    M1 substep 9 fills this in. Scaffold below shows the intended structure.

    Returns:
        dict of summary metrics: ``{'train_loss': ..., 'val_mae': ...,
        'val_loss': ..., 'best_epoch': ...}``.
    """
    seed_everything(config.seed)

    model = ResolutionHead(dropout=config.dropout).to(config.device)
    criterion = HuberWithMonotonicity(
        delta=config.huber_delta,
        monotonicity_weight=config.monotonicity_weight,
    )
    optimizer = torch.optim.AdamW(
        model.parameters(),
        lr=config.learning_rate,
        weight_decay=config.weight_decay,
    )
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(
        optimizer, T_max=config.epochs
    )

    best_val_loss = float("inf")
    best_epoch = -1

    for epoch in range(config.epochs):
        # --- train ---
        model.train()
        # M1 substep 9: iterate train_loader, forward, backward, step,
        # accumulate train loss.
        # --- val ---
        model.eval()
        # M1 substep 9: iterate val_loader under torch.no_grad(),
        # accumulate val_loss and val_mae.
        scheduler.step()

        # M1 substep 9: log metrics, checkpoint best-val.
        _ = (epoch, optimizer, criterion)  # suppress unused warnings until impl

    config.output_dir.mkdir(parents=True, exist_ok=True)
    metrics_path = config.output_dir / "metrics.json"
    metrics = {
        "best_epoch": best_epoch,
        "best_val_loss": best_val_loss,
        # M1 substep 9: real numbers
    }
    metrics_path.write_text(json.dumps(metrics, indent=2))
    return metrics


def _suppress_unused_imports() -> None:  # pragma: no cover
    """Keep imports referenced until M1 substep 9 fleshes out the loop."""
    _ = (nn, ResolutionHead, HuberWithMonotonicity)
