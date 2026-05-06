"""Tests for the training loop.

Covers:
  - Single-batch forward + backward (smoke test -- no crash).
  - Training loss decreases over 5 epochs on a mini-dataset (50 rollouts).
  - Best-checkpoint save + load round-trip preserves model state.
  - TrainResult has correct structure.
"""

from __future__ import annotations

from pathlib import Path

import torch
from torch.utils.data import DataLoader, TensorDataset

from wayfinders.ml.training.head import ResolutionHead
from wayfinders.ml.training.train import TrainConfig, TrainResult, load_checkpoint, train_loop

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _normalize(x: torch.Tensor) -> torch.Tensor:
    return x / x.norm(dim=-1, keepdim=True).clamp_min(1e-8)


def _make_tiny_loaders(
    n_train: int = 50,
    n_val: int = 20,
    batch_size: int = 16,
    seed: int = 0,
) -> tuple[DataLoader[tuple[torch.Tensor, ...]], DataLoader[tuple[torch.Tensor, ...]]]:
    """Build deterministic TensorDataset-backed loaders for fast tests.

    Items are (char_vec, action_vec, context_vec, label) -- same schema as
    WayfindersDataset. Labels are simple linear functions of the vecs so the
    head has a learnable signal.
    """
    torch.manual_seed(seed)
    char = _normalize(torch.randn(n_train, 384))
    action = _normalize(torch.randn(n_train, 384))
    context = _normalize(torch.randn(n_train, 384))
    # Labels: linear combo of the first 3 dims of char, capped to [-5, +5].
    labels = torch.tanh(char[:, :3].sum(dim=1, keepdim=True)) * 5.0

    train_ds: TensorDataset = TensorDataset(char, action, context, labels)

    torch.manual_seed(seed + 1)
    char_v = _normalize(torch.randn(n_val, 384))
    action_v = _normalize(torch.randn(n_val, 384))
    context_v = _normalize(torch.randn(n_val, 384))
    labels_v = torch.tanh(char_v[:, :3].sum(dim=1, keepdim=True)) * 5.0
    val_ds: TensorDataset = TensorDataset(char_v, action_v, context_v, labels_v)

    g = torch.Generator()
    g.manual_seed(seed)
    train_loader: DataLoader[tuple[torch.Tensor, ...]] = DataLoader(
        train_ds, batch_size=batch_size, shuffle=True, generator=g
    )
    val_loader: DataLoader[tuple[torch.Tensor, ...]] = DataLoader(
        val_ds, batch_size=batch_size, shuffle=False
    )
    return train_loader, val_loader


# ---------------------------------------------------------------------------
# Smoke: single batch forward + backward
# ---------------------------------------------------------------------------


class TestSingleBatchForwardBackward:
    def test_no_crash_on_one_step(self) -> None:
        """One forward + backward pass should not raise."""
        model = ResolutionHead()
        batch_size = 8
        char = _normalize(torch.randn(batch_size, 384))
        action = _normalize(torch.randn(batch_size, 384))
        context = _normalize(torch.randn(batch_size, 384))
        labels = torch.zeros(batch_size, 1)

        optimizer = torch.optim.AdamW(model.parameters(), lr=1e-3)
        optimizer.zero_grad()
        pred = model(char, action, context)
        loss = (pred - labels).pow(2).mean()
        loss.backward()
        optimizer.step()

        assert torch.isfinite(loss)

    def test_gradients_flow_to_all_params(self) -> None:
        model = ResolutionHead()
        char = _normalize(torch.randn(4, 384))
        action = _normalize(torch.randn(4, 384))
        context = _normalize(torch.randn(4, 384))
        labels = torch.zeros(4, 1)

        pred = model(char, action, context)
        loss = (pred - labels).pow(2).mean()
        loss.backward()

        for name, p in model.named_parameters():
            assert p.grad is not None, f"{name}: no gradient"
            assert torch.isfinite(p.grad).all(), f"{name}: non-finite gradient"


# ---------------------------------------------------------------------------
# Loss decreases
# ---------------------------------------------------------------------------


class TestLossDecreases:
    def test_train_loss_decreases_over_5_epochs(self, tmp_path: Path) -> None:
        """Training loss on a simple synthetic dataset should decrease over 5 epochs."""
        torch.manual_seed(99)
        model = ResolutionHead(dropout=0.0)  # no dropout for determinism
        train_loader, val_loader = _make_tiny_loaders(n_train=50, n_val=20, seed=7)

        config = TrainConfig(
            epochs=5,
            batch_size=16,
            lr=5e-3,
            weight_decay=0.0,
            dropout=0.0,
            patience=0,  # no early stopping
            val_every_n_epochs=1,
            seed=0,
            device="cpu",
            output_dir=tmp_path / "out",
            checkpoint_dir=tmp_path / "ckpt",
        )

        result = train_loop(model, train_loader, val_loader, config)

        assert isinstance(result, TrainResult)
        assert len(result.train_losses) == 5
        # Loss at the end should be lower than at the start.
        assert result.train_losses[-1] < result.train_losses[0], (
            f"Expected loss to decrease: {result.train_losses}"
        )

    def test_val_mae_field_present(self, tmp_path: Path) -> None:
        model = ResolutionHead(dropout=0.0)
        train_loader, val_loader = _make_tiny_loaders(n_train=30, n_val=10, seed=5)
        config = TrainConfig(
            epochs=2,
            batch_size=16,
            lr=1e-3,
            patience=0,
            device="cpu",
            seed=42,
            output_dir=tmp_path / "out",
            checkpoint_dir=tmp_path / "ckpt",
        )
        result = train_loop(model, train_loader, val_loader, config)
        assert result.best_val_mae >= 0.0
        assert result.best_epoch >= 0


# ---------------------------------------------------------------------------
# Checkpoint round-trip
# ---------------------------------------------------------------------------


class TestCheckpointRoundTrip:
    def test_best_checkpoint_save_and_load(self, tmp_path: Path) -> None:
        """Checkpoint saved during training can be loaded; weights are identical."""
        torch.manual_seed(0)
        model = ResolutionHead(dropout=0.0)
        train_loader, val_loader = _make_tiny_loaders(n_train=30, n_val=10, seed=1)

        config = TrainConfig(
            epochs=3,
            batch_size=16,
            lr=1e-3,
            patience=0,
            device="cpu",
            seed=0,
            output_dir=tmp_path / "out",
            checkpoint_dir=tmp_path / "ckpt",
        )
        train_loop(model, train_loader, val_loader, config)

        best_ckpt = tmp_path / "ckpt" / "head_best.pt"
        assert best_ckpt.exists(), "head_best.pt not written"

        loaded = load_checkpoint(best_ckpt, device="cpu")

        # The loaded model's parameters must exactly match the checkpoint.
        ckpt_data = torch.load(best_ckpt, map_location="cpu", weights_only=True)
        for name, param in loaded.named_parameters():
            saved = ckpt_data["model_state_dict"][name]
            assert torch.allclose(param, saved), f"param {name} mismatch after round-trip"

    def test_metrics_json_written(self, tmp_path: Path) -> None:
        import json

        model = ResolutionHead(dropout=0.0)
        train_loader, val_loader = _make_tiny_loaders(n_train=20, n_val=8, seed=2)
        config = TrainConfig(
            epochs=2,
            batch_size=8,
            lr=1e-3,
            patience=0,
            device="cpu",
            seed=0,
            output_dir=tmp_path / "out",
            checkpoint_dir=tmp_path / "ckpt",
        )
        train_loop(model, train_loader, val_loader, config)
        metrics_path = tmp_path / "out" / "metrics.json"
        assert metrics_path.exists()
        data = json.loads(metrics_path.read_text())
        assert "best_val_mae" in data
        assert "train_losses" in data
        assert "val_maes" in data

    def test_final_checkpoint_written(self, tmp_path: Path) -> None:
        model = ResolutionHead(dropout=0.0)
        train_loader, val_loader = _make_tiny_loaders(n_train=20, n_val=8, seed=3)
        config = TrainConfig(
            epochs=2,
            batch_size=8,
            lr=1e-3,
            patience=0,
            device="cpu",
            seed=0,
            output_dir=tmp_path / "out",
            checkpoint_dir=tmp_path / "ckpt",
        )
        train_loop(model, train_loader, val_loader, config)
        final_path = tmp_path / "out" / "head_final.pt"
        assert final_path.exists()


# ---------------------------------------------------------------------------
# Early stopping
# ---------------------------------------------------------------------------


class TestEarlyStopping:
    def test_early_stop_cuts_epochs_short(self, tmp_path: Path) -> None:
        """With patience=2 and a plateau, fewer than max epochs should run."""
        model = ResolutionHead(dropout=0.0)
        # Build a loader where labels are all zero -- model converges fast, then plateaus.
        torch.manual_seed(55)
        n = 30
        char = _normalize(torch.randn(n, 384))
        action = _normalize(torch.randn(n, 384))
        context = _normalize(torch.randn(n, 384))
        labels = torch.zeros(n, 1)
        from torch.utils.data import TensorDataset

        ds: TensorDataset = TensorDataset(char, action, context, labels)
        loader: DataLoader[tuple[torch.Tensor, ...]] = DataLoader(ds, batch_size=n)

        config = TrainConfig(
            epochs=50,
            batch_size=n,
            lr=1e-3,
            patience=3,
            min_delta=0.0,
            device="cpu",
            seed=0,
            output_dir=tmp_path / "out",
            checkpoint_dir=tmp_path / "ckpt",
        )
        result = train_loop(model, loader, loader, config)
        assert result.epochs_run < 50, (
            f"Expected early stopping to trigger before 50 epochs, got {result.epochs_run}"
        )
