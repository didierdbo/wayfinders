"""Training loop for the UC1 ResolutionHead -- Lightning-free, explicit.

Design principles (Pax 2026-05-02 sec. 3, Coda M1 plan substep 9):

  - Single file, single function, no magic. Every step is visible.
  - AdamW + CosineAnnealingLR. No warm-up for M1 (head is tiny).
  - fp16 mixed precision via torch.amp when CUDA is available.
    The encoder is frozen and NOT in the training graph -- the DataLoader
    serves pre-computed fp32 embeddings, so amp only covers the head MLP.
  - Gradient norm logging catches explosions before they corrupt training.
  - Checkpoint: saves best-val-MAE checkpoint to
    ``artifacts/models/checkpoints/head_best.pt``.
    Also saves a ``head_final.pt`` at the end of the last epoch.
  - metrics.json: written to ``output_dir`` after training. Contains the
    full per-epoch train/val curves plus final summary metrics.
  - Early stopping: if val MAE has not improved by more than ``min_delta``
    for ``patience`` consecutive epochs, training halts. Set patience=0 to
    disable (run all epochs).

Determinism contract:
  - seed_everything() is called at the top of train_loop() using config.seed.
  - torch.backends.cudnn.deterministic=True + benchmark=False under CUDA.
  - DataLoader is constructed with a seeded generator for shuffle
    reproducibility (workers=0 avoids fork non-determinism on Windows).

Monotonicity regularizer:
  - The HuberWithMonotonicity loss supports optional (mono_pred_lo,
    mono_pred_hi) pairs. For M1 we do NOT generate those pairs inside the
    DataLoader -- the rollout generator does not produce ordered legacy-delta
    pairs. The regularizer weight is non-zero in TrainConfig so that M2 can
    activate it by passing mono pairs; for M1 it fires as a no-op (both
    arguments are None) and the weight term is 0.
"""

from __future__ import annotations

import json
import logging
import random
from pathlib import Path
from typing import Any

import numpy as np
import pydantic
import torch
from pydantic import BaseModel, ConfigDict, Field
from torch.utils.data import DataLoader

from wayfinders.ml.training.head import ResolutionHead
from wayfinders.ml.training.loss import HuberWithMonotonicity

logger = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------


class TrainConfig(BaseModel):
    """Full configuration for one training run.

    All fields have sensible defaults based on Pax sec. 3 recommendations
    for the 313k-param head on 50k synthetic rollouts.

    Fields:
        epochs:                  Total training epochs.
        batch_size:              Mini-batch size.
        lr:                      Initial AdamW learning rate.
        weight_decay:            AdamW weight decay.
        dropout:                 Dropout probability in ResolutionHead.
        huber_delta:             Huber loss transition point (Pax: 1.0).
        monotonicity_weight:     Weight on the monotonicity regularizer.
                                 No-op in M1 (no mono pairs generated).
        val_every_n_epochs:      Run val eval every N epochs (1 = every epoch).
        checkpoint_every_n_epochs: Save a numbered checkpoint every N epochs
                                   (0 = disable periodic checkpoints, only
                                   save best).
        patience:                Early-stopping patience in epochs. 0 = disabled.
        min_delta:               Minimum val MAE improvement to reset patience.
        device:                  "cuda" or "cpu". Auto-detected by default.
        seed:                    Master RNG seed.
        output_dir:              Directory for metrics.json and final checkpoint.
        checkpoint_dir:          Directory for best and periodic checkpoints.
    """

    model_config = ConfigDict(frozen=True)

    epochs: int = 50
    batch_size: int = 64
    lr: float = 1e-3
    weight_decay: float = 1e-4
    dropout: float = 0.1
    huber_delta: float = 1.0
    monotonicity_weight: float = 0.1
    val_every_n_epochs: int = 1
    checkpoint_every_n_epochs: int = 0
    patience: int = 10
    min_delta: float = 1e-4
    device: str = Field(default_factory=lambda: "cuda" if torch.cuda.is_available() else "cpu")
    seed: int = 42
    output_dir: Path = Path("artifacts/models/v0.1")
    checkpoint_dir: Path = Path("artifacts/models/checkpoints")


# ---------------------------------------------------------------------------
# Determinism helpers
# ---------------------------------------------------------------------------


def seed_everything(seed: int) -> None:
    """Seed Python, NumPy, and PyTorch RNGs for reproducibility.

    Sets CUDA determinism flags when CUDA is available. This trades a small
    amount of GPU throughput for bit-reproducible results across runs with
    the same seed + config + data.
    """
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)
        torch.backends.cudnn.deterministic = True
        torch.backends.cudnn.benchmark = False


# ---------------------------------------------------------------------------
# Result model
# ---------------------------------------------------------------------------


class TrainResult(BaseModel):
    """Summary of a completed training run."""

    model_config = ConfigDict(frozen=True)

    best_epoch: int
    best_val_mae: float
    final_train_loss: float
    final_val_loss: float
    final_val_mae: float
    epochs_run: int
    train_losses: list[float]
    val_losses: list[float]
    val_maes: list[float]
    config: dict[str, Any]


# ---------------------------------------------------------------------------
# Training loop
# ---------------------------------------------------------------------------


def train_loop(
    model: ResolutionHead,
    train_loader: DataLoader[Any],
    val_loader: DataLoader[Any],
    config: TrainConfig,
) -> TrainResult:
    """Run the full training loop.

    Args:
        model:        A ResolutionHead instance (will be moved to config.device).
        train_loader: DataLoader for training data.
        val_loader:   DataLoader for validation data.
        config:       TrainConfig instance controlling all hyperparameters.

    Returns:
        TrainResult with per-epoch curves and final metrics.

    Side effects:
        - Writes ``config.checkpoint_dir / head_best.pt`` whenever a new
          best val MAE is observed.
        - Writes ``config.output_dir / head_final.pt`` at end of training.
        - Writes ``config.output_dir / metrics.json``.
    """
    seed_everything(config.seed)
    device = config.device

    model = model.to(device)
    criterion = HuberWithMonotonicity(
        delta=config.huber_delta,
        monotonicity_weight=config.monotonicity_weight,
    )
    optimizer = torch.optim.AdamW(
        model.parameters(),
        lr=config.lr,
        weight_decay=config.weight_decay,
    )
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=config.epochs)

    # Mixed precision scaler -- no-op when not on CUDA.
    use_amp = device.startswith("cuda")
    scaler = torch.amp.GradScaler(device, enabled=use_amp)  # type: ignore[attr-defined]

    config.checkpoint_dir.mkdir(parents=True, exist_ok=True)
    config.output_dir.mkdir(parents=True, exist_ok=True)

    best_val_mae = float("inf")
    best_epoch = -1
    best_checkpoint_path = config.checkpoint_dir / "head_best.pt"

    train_losses: list[float] = []
    val_losses: list[float] = []
    val_maes: list[float] = []

    logger.info(
        "train_loop: start | device=%s fp16=%s epochs=%d batch=%d lr=%.2e",
        device,
        use_amp,
        config.epochs,
        config.batch_size,
        config.lr,
    )

    patience_counter = 0
    stopped_early = False

    for epoch in range(config.epochs):
        # ------------------------------------------------------------------
        # Training pass
        # ------------------------------------------------------------------
        model.train()
        epoch_train_loss = 0.0
        n_train_batches = 0

        for batch in train_loader:
            char_vec, action_vec, context_vec, label = batch
            char_vec = char_vec.to(device)
            action_vec = action_vec.to(device)
            context_vec = context_vec.to(device)
            label = label.to(device)

            optimizer.zero_grad()

            with torch.amp.autocast(device_type=device, enabled=use_amp):  # type: ignore[attr-defined]
                pred = model(char_vec, action_vec, context_vec)
                loss = criterion(pred, label)

            scaler.scale(loss).backward()

            # Gradient clipping (max norm = 1.0) before unscale + step.
            scaler.unscale_(optimizer)
            grad_norm = torch.nn.utils.clip_grad_norm_(model.parameters(), max_norm=1.0)

            scaler.step(optimizer)
            scaler.update()

            epoch_train_loss += loss.item()
            n_train_batches += 1

        avg_train_loss = epoch_train_loss / max(n_train_batches, 1)
        train_losses.append(avg_train_loss)

        # ------------------------------------------------------------------
        # Validation pass
        # ------------------------------------------------------------------
        run_val = (epoch % config.val_every_n_epochs == 0) or (epoch == config.epochs - 1)
        if run_val:
            model.eval()
            epoch_val_loss = 0.0
            n_val_batches = 0
            abs_errors: list[torch.Tensor] = []

            with torch.no_grad():
                for batch in val_loader:
                    char_vec, action_vec, context_vec, label = batch
                    char_vec = char_vec.to(device)
                    action_vec = action_vec.to(device)
                    context_vec = context_vec.to(device)
                    label = label.to(device)

                    with torch.amp.autocast(device_type=device, enabled=use_amp):  # type: ignore[attr-defined]
                        pred = model(char_vec, action_vec, context_vec)
                        vloss = criterion(pred, label)

                    epoch_val_loss += vloss.item()
                    abs_errors.append((pred - label).abs().cpu())
                    n_val_batches += 1

            avg_val_loss = epoch_val_loss / max(n_val_batches, 1)
            val_mae = torch.cat(abs_errors).mean().item() if abs_errors else float("inf")
        else:
            # Reuse last known values so curves list lengths stay aligned.
            avg_val_loss = val_losses[-1] if val_losses else float("inf")
            val_mae = val_maes[-1] if val_maes else float("inf")

        val_losses.append(avg_val_loss)
        val_maes.append(val_mae)

        scheduler.step()

        # ------------------------------------------------------------------
        # Logging
        # ------------------------------------------------------------------
        logger.info(
            "epoch %03d/%03d | train_loss=%.4f | val_loss=%.4f | val_MAE=%.4f | "
            "grad_norm=%.3f | lr=%.2e",
            epoch + 1,
            config.epochs,
            avg_train_loss,
            avg_val_loss,
            val_mae,
            grad_norm.item() if isinstance(grad_norm, torch.Tensor) else grad_norm,
            scheduler.get_last_lr()[0],
        )

        # ------------------------------------------------------------------
        # Checkpoint: best val MAE
        # ------------------------------------------------------------------
        if val_mae < best_val_mae - config.min_delta:
            best_val_mae = val_mae
            best_epoch = epoch
            patience_counter = 0
            torch.save(
                {
                    "model_state_dict": model.state_dict(),
                    "config": config.model_dump(mode="json"),
                    "metrics": {
                        "epoch": epoch,
                        "val_mae": val_mae,
                        "val_loss": avg_val_loss,
                        "train_loss": avg_train_loss,
                    },
                },
                best_checkpoint_path,
            )
            logger.info("  ** new best checkpoint: val_MAE=%.4f (epoch %d)", val_mae, epoch + 1)
        else:
            if run_val:
                patience_counter += 1

        # Periodic checkpoint.
        if config.checkpoint_every_n_epochs > 0 and (
            (epoch + 1) % config.checkpoint_every_n_epochs == 0
        ):
            periodic_path = config.checkpoint_dir / f"head_epoch_{epoch + 1:04d}.pt"
            torch.save(
                {"model_state_dict": model.state_dict(), "epoch": epoch},
                periodic_path,
            )

        # ------------------------------------------------------------------
        # Early stopping
        # ------------------------------------------------------------------
        if config.patience > 0 and patience_counter >= config.patience:
            logger.info(
                "Early stopping at epoch %d (patience=%d, best_val_MAE=%.4f)",
                epoch + 1,
                config.patience,
                best_val_mae,
            )
            stopped_early = True
            break

    epochs_run = (
        next(
            (i for i in range(config.epochs) if i == config.epochs - 1 or stopped_early),
            config.epochs - 1,
        )
        + 1
    )
    # Simpler: count actual appended losses.
    epochs_run = len(train_losses)

    # ------------------------------------------------------------------
    # Final checkpoint
    # ------------------------------------------------------------------
    final_path = config.output_dir / "head_final.pt"
    torch.save(
        {
            "model_state_dict": model.state_dict(),
            "config": config.model_dump(mode="json"),
            "metrics": {
                "epochs_run": epochs_run,
                "best_epoch": best_epoch,
                "best_val_mae": best_val_mae,
                "final_train_loss": train_losses[-1],
                "final_val_loss": val_losses[-1],
                "final_val_mae": val_maes[-1],
            },
        },
        final_path,
    )

    # ------------------------------------------------------------------
    # metrics.json
    # ------------------------------------------------------------------
    result = TrainResult(
        best_epoch=best_epoch,
        best_val_mae=best_val_mae,
        final_train_loss=train_losses[-1],
        final_val_loss=val_losses[-1],
        final_val_mae=val_maes[-1],
        epochs_run=epochs_run,
        train_losses=train_losses,
        val_losses=val_losses,
        val_maes=val_maes,
        config=config.model_dump(mode="json"),
    )
    metrics_path = config.output_dir / "metrics.json"
    metrics_path.write_text(
        json.dumps(result.model_dump(mode="json"), indent=2),
        encoding="utf-8",
    )
    logger.info(
        "train_loop: done | epochs_run=%d | best_val_MAE=%.4f (epoch %d) | metrics -> %s",
        epochs_run,
        best_val_mae,
        best_epoch + 1,
        metrics_path,
    )

    return result


# ---------------------------------------------------------------------------
# Checkpoint loading utility
# ---------------------------------------------------------------------------


def load_checkpoint(checkpoint_path: Path, device: str = "cpu") -> ResolutionHead:
    """Load a ResolutionHead from a checkpoint written by train_loop.

    Args:
        checkpoint_path: Path to a ``.pt`` file written by train_loop.
        device:          Device to load the model onto.

    Returns:
        A ResolutionHead with weights restored from the checkpoint.
    """
    ckpt = torch.load(checkpoint_path, map_location=device, weights_only=True)
    config_dict: dict[str, Any] = ckpt.get("config", {})
    dropout: float = float(config_dict.get("dropout", 0.1))
    model = ResolutionHead(dropout=dropout)
    model.load_state_dict(ckpt["model_state_dict"])
    model.to(device)
    return model


# Silence the pydantic import for lint (it's used transitively via BaseModel).
_keep: Any = pydantic
