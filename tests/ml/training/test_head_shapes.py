"""Sanity tests on the ResolutionHead shape, range, and gradient flow."""

from __future__ import annotations

import torch

from wayfinders.ml.training.head import (
    DELTA_RANGE,
    HEAD_INPUT_DIM,
    ResolutionHead,
)


def _normalize(x: torch.Tensor) -> torch.Tensor:
    return x / x.norm(dim=-1, keepdim=True).clamp_min(1e-8)


class TestShapes:
    def test_head_input_dim_constant(self) -> None:
        assert HEAD_INPUT_DIM == 384 * 3 + 1 == 1153

    def test_forward_output_shape(self) -> None:
        model = ResolutionHead()
        char = _normalize(torch.randn(8, 384))
        action = _normalize(torch.randn(8, 384))
        context = _normalize(torch.randn(8, 384))
        delta = model(char, action, context)
        assert delta.shape == (8, 1)

    def test_param_count_around_313k(self) -> None:
        model = ResolutionHead()
        n_params = sum(p.numel() for p in model.parameters())
        # 1153*256 + 256 + 256*64 + 64 + 64*1 + 1 = 295168 + 256 + 16384 + 64 + 64 + 1 = 311937
        # Pax called it ~313k — we're inside the rounding.
        assert 305_000 <= n_params <= 320_000


class TestOutputRange:
    def test_delta_in_range_extreme_inputs(self) -> None:
        """tanh×5 must keep Δ in [-5, +5] no matter how wild the input is."""
        model = ResolutionHead()
        for scale in (1.0, 10.0, 100.0, 1000.0):
            char = _normalize(torch.randn(64, 384) * scale)
            action = _normalize(torch.randn(64, 384) * scale)
            context = _normalize(torch.randn(64, 384) * scale)
            delta = model(char, action, context)
            assert (delta >= -DELTA_RANGE).all()
            assert (delta <= DELTA_RANGE).all()

    def test_delta_starts_near_zero(self) -> None:
        """Last-layer init biased toward 0 ; freshly-init head should output
        small deltas on average, so the model has no a-priori bias."""
        model = ResolutionHead()
        char = _normalize(torch.randn(256, 384))
        action = _normalize(torch.randn(256, 384))
        context = _normalize(torch.randn(256, 384))
        with torch.no_grad():
            delta = model(char, action, context)
        # Mean magnitude should be modest at init (gain=0.1 on last layer).
        assert delta.abs().mean().item() < 1.0


class TestGradientFlow:
    def test_backward_produces_grads(self) -> None:
        model = ResolutionHead()
        char = _normalize(torch.randn(4, 384))
        action = _normalize(torch.randn(4, 384))
        context = _normalize(torch.randn(4, 384))
        delta = model(char, action, context)
        loss = delta.pow(2).sum()
        loss.backward()
        for name, param in model.named_parameters():
            assert param.grad is not None, f"{name} got no gradient"
            assert torch.isfinite(param.grad).all(), f"{name} has non-finite grad"

    def test_dropout_off_in_eval(self) -> None:
        model = ResolutionHead(dropout=0.5)
        model.eval()
        char = _normalize(torch.randn(16, 384))
        action = _normalize(torch.randn(16, 384))
        context = _normalize(torch.randn(16, 384))
        with torch.no_grad():
            a = model(char, action, context)
            b = model(char, action, context)
        assert torch.allclose(a, b)


class TestDeterminism:
    def test_seeded_init_reproducible(self) -> None:
        torch.manual_seed(42)
        m1 = ResolutionHead()
        torch.manual_seed(42)
        m2 = ResolutionHead()
        for p1, p2 in zip(m1.parameters(), m2.parameters(), strict=True):
            assert torch.allclose(p1, p2)
