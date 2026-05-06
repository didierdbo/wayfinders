"""Tests for the evaluate() function and EvalMetrics.

Covers:
  - A model that always predicts 0 has MAE = mean(|labels|).
  - R^2 is negative when the model is worse than predicting the mean.
  - Bucket breakdown is computed correctly.
  - Bucket with zero samples has MAE=None.
"""

from __future__ import annotations

import pytest
import torch
from torch.utils.data import DataLoader, TensorDataset

from wayfinders.ml.training.eval import EvalMetrics, evaluate

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _normalize(x: torch.Tensor) -> torch.Tensor:
    return x / x.norm(dim=-1, keepdim=True).clamp_min(1e-8)


def _make_loader(
    labels: torch.Tensor, batch_size: int = 16
) -> DataLoader[tuple[torch.Tensor, ...]]:
    n = len(labels)
    char = _normalize(torch.randn(n, 384))
    action = _normalize(torch.randn(n, 384))
    context = _normalize(torch.randn(n, 384))
    ds: TensorDataset = TensorDataset(char, action, context, labels.unsqueeze(1))
    return DataLoader(ds, batch_size=batch_size, shuffle=False)


class _ConstantModel(torch.nn.Module):
    """Fake model that always predicts ``value`` regardless of input."""

    def __init__(self, value: float = 0.0) -> None:
        super().__init__()
        self._value = value
        # Dummy parameter so the model has something; not used in forward.
        self._p = torch.nn.Parameter(torch.zeros(1))

    def forward(
        self,
        char_vec: torch.Tensor,
        action_vec: torch.Tensor,
        context_vec: torch.Tensor,
    ) -> torch.Tensor:
        return torch.full((char_vec.shape[0], 1), self._value)


# ---------------------------------------------------------------------------
# MAE correctness
# ---------------------------------------------------------------------------


class TestMAECorrectness:
    def test_constant_zero_model_mae(self) -> None:
        """Model that predicts 0.0 should have MAE = mean(|labels|)."""
        torch.manual_seed(0)
        labels = torch.tensor([1.0, 2.0, 3.0, 4.0, 5.0])
        expected_mae = labels.abs().mean().item()

        loader = _make_loader(labels, batch_size=5)
        model = _ConstantModel(0.0)
        model.eval()

        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]

        assert abs(metrics.mae - expected_mae) < 1e-5, (
            f"Expected MAE={expected_mae:.4f}, got {metrics.mae:.4f}"
        )

    def test_perfect_model_mae_is_zero(self) -> None:
        """A model that predicts the label exactly should have MAE=0."""
        torch.manual_seed(1)
        labels = torch.tensor([-3.0, 0.0, 2.0, 4.0])

        # Build a model that returns the exact label. We do this by building
        # a loader where labels are known and patching the forward.
        class PerfectModel(torch.nn.Module):
            """Cheating model: returns labels stored during construction."""

            def __init__(self, exact_labels: torch.Tensor) -> None:
                super().__init__()
                self._labels = exact_labels
                self._idx = 0

            def forward(
                self,
                char_vec: torch.Tensor,
                action_vec: torch.Tensor,
                context_vec: torch.Tensor,
            ) -> torch.Tensor:
                b = char_vec.shape[0]
                out = self._labels[self._idx : self._idx + b].unsqueeze(1)
                self._idx += b
                return out

        loader = _make_loader(labels, batch_size=4)
        model = PerfectModel(labels)
        model.eval()

        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
        assert metrics.mae < 1e-5

    def test_count_matches_dataset_size(self) -> None:
        torch.manual_seed(2)
        n = 40
        labels = torch.randn(n) * 2.0
        loader = _make_loader(labels, batch_size=8)
        model = _ConstantModel(0.0)
        model.eval()
        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
        assert metrics.count == n


# ---------------------------------------------------------------------------
# RMSE
# ---------------------------------------------------------------------------


class TestRMSE:
    def test_rmse_consistent_with_mae(self) -> None:
        """RMSE >= MAE always (Jensen's inequality / root of mean squares)."""
        torch.manual_seed(3)
        labels = torch.randn(30) * 3.0
        loader = _make_loader(labels)
        model = _ConstantModel(0.0)
        model.eval()
        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
        assert metrics.rmse >= metrics.mae - 1e-5


# ---------------------------------------------------------------------------
# R^2
# ---------------------------------------------------------------------------


class TestRSquared:
    def test_r_squared_negative_for_constant_zero_vs_nonzero_labels(self) -> None:
        """A model predicting 0 on labels concentrated far from 0 should have R^2 < 0."""
        # Labels all positive and far from 0 -- predicting 0 is worse than the mean.
        labels = torch.tensor([4.0, 4.5, 5.0, 4.8, 4.2])
        loader = _make_loader(labels, batch_size=5)
        model = _ConstantModel(0.0)
        model.eval()
        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
        assert metrics.r_squared < 0.0, (
            f"Expected R^2 < 0 for constant-zero model on positive labels, got {metrics.r_squared}"
        )

    def test_r_squared_at_most_1(self) -> None:
        """R^2 must never exceed 1.0."""
        torch.manual_seed(4)
        labels = torch.randn(20) * 2.0
        loader = _make_loader(labels)
        model = _ConstantModel(0.0)
        model.eval()
        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
        assert metrics.r_squared <= 1.0 + 1e-5


# ---------------------------------------------------------------------------
# Bucket breakdown
# ---------------------------------------------------------------------------


class TestBucketBreakdown:
    def test_bucket_keys_cover_full_range(self) -> None:
        """Bucket dict should have exactly 10 keys covering [-5, +5]."""
        labels = torch.linspace(-4.9, 4.9, 50)
        loader = _make_loader(labels, batch_size=50)
        model = _ConstantModel(0.0)
        model.eval()
        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
        assert len(metrics.mae_by_bucket) == 10

    def test_bucket_none_when_empty(self) -> None:
        """Buckets with no samples should have MAE=None, not 0."""
        # All labels in [0, 1) -- buckets far away should be None.
        labels = torch.full((20,), 0.5)
        loader = _make_loader(labels, batch_size=20)
        model = _ConstantModel(0.0)
        model.eval()
        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
        # The [-5,-4) bucket should be None.
        assert metrics.mae_by_bucket["[-5,-4)"] is None

    def test_bucket_mae_positive_for_filled_bucket(self) -> None:
        """A bucket that has samples should have non-negative MAE."""
        labels = torch.tensor([1.5, 1.8, 2.0, 2.2])
        loader = _make_loader(labels, batch_size=4)
        model = _ConstantModel(0.0)
        model.eval()
        metrics = evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
        # The [1,2) bucket should have positive MAE (constant 0 vs ~2).
        bucket_val = metrics.mae_by_bucket.get("[1,2)")
        assert bucket_val is not None
        assert bucket_val > 0.0


# ---------------------------------------------------------------------------
# EvalMetrics is a valid Pydantic model
# ---------------------------------------------------------------------------


class TestEvalMetricsModel:
    def test_eval_metrics_is_pydantic(self) -> None:
        m = EvalMetrics(
            mae=0.5,
            rmse=0.7,
            r_squared=0.3,
            mae_by_bucket={"[0,1)": 0.4, "[-1,0)": None},
            count=100,
        )
        assert m.mae == 0.5
        assert m.count == 100
        assert m.mae_by_bucket["[-1,0)"] is None

    def test_empty_dataloader_raises(self) -> None:
        from torch.utils.data import DataLoader

        empty_ds: TensorDataset = TensorDataset(
            torch.zeros(0, 384),
            torch.zeros(0, 384),
            torch.zeros(0, 384),
            torch.zeros(0, 1),
        )
        loader: DataLoader[tuple[torch.Tensor, ...]] = DataLoader(empty_ds, batch_size=16)
        model = _ConstantModel(0.0)
        model.eval()
        with pytest.raises(ValueError, match="empty dataloader"):
            evaluate(model, loader, device="cpu")  # type: ignore[arg-type]
