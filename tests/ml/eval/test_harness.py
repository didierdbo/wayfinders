"""Tests for wayfinders.ml.eval.harness.

Covers:
  - run_harness returns correct MAE/RMSE for a known mock predictor.
  - Verdict is SHIP when MAE <= 0.4.
  - Verdict is PASS when 0.4 < MAE <= 0.6.
  - Verdict is FAIL when MAE > 0.6.
  - bias_ok flag is set correctly.
  - no_mode_failure flag is set correctly.
  - Residual stats are computed correctly.
  - write_report_json writes parseable JSON.
  - write_items_csv writes expected columns.
  - format_console_summary contains the verdict string.
  - empty items list raises ValueError.
  - Stub dataset passes the architecture gate (MAE <= 0.6) with a near-perfect
    mock predictor -- anchors the contract that a good model ships PASS on the stub.
"""

from __future__ import annotations

import csv
import json
import math
from pathlib import Path

import pytest

from wayfinders.ml.eval.harness import (
    MAE_ARCHITECTURE_GATE,
    MAE_SHIP_GATE,
    HarnessReport,
    format_console_summary,
    run_harness,
    write_items_csv,
    write_report_json,
)
from wayfinders.ml.eval.schema import HandGradedItem, default_stub_path, load_dataset

# ---------------------------------------------------------------------------
# Mock predictors
# ---------------------------------------------------------------------------


class _ConstantPredictor:
    """Predicts a fixed delta for every input."""

    def __init__(self, value: float) -> None:
        self._value = value

    def predict(self, char_prose: str, action_prose: str, context_prose: str) -> float:
        return self._value


class _PerfectPredictor:
    """Returns the expected delta from the item by matching the full prose triple.

    Only works when the items list is provided at construction time.
    Uses (char_prose, action_prose, context_prose) as the composite key to
    avoid collisions when two items share the same char_prose (e.g. the stub
    has Kira appear in multiple scenarios with different actions/contexts).
    """

    def __init__(self, items: list[HandGradedItem]) -> None:
        self._lookup: dict[tuple[str, str, str], float] = {
            (it.char_prose, it.action_prose, it.context_prose): it.expected_delta for it in items
        }

    def predict(self, char_prose: str, action_prose: str, context_prose: str) -> float:
        return self._lookup[(char_prose, action_prose, context_prose)]


class _OffsetPredictor:
    """Adds a fixed offset to the expected delta (non-perfect but calibrated)."""

    def __init__(self, items: list[HandGradedItem], offset: float) -> None:
        self._offset = offset
        self._lookup: dict[tuple[str, str, str], float] = {
            (it.char_prose, it.action_prose, it.context_prose): it.expected_delta for it in items
        }

    def predict(self, char_prose: str, action_prose: str, context_prose: str) -> float:
        return self._lookup[(char_prose, action_prose, context_prose)] + self._offset


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _make_items(expected_deltas: list[float]) -> list[HandGradedItem]:
    """Build synthetic HandGradedItem list with specified expected deltas."""
    return [
        HandGradedItem(
            id=f"item_{i:03d}",
            char_prose=f"Character prose for item {i}.",
            action_prose=f"Action prose for item {i}.",
            context_prose=f"Context prose for item {i}.",
            expected_delta=delta,
            grader_id="tess",
        )
        for i, delta in enumerate(expected_deltas)
    ]


# ---------------------------------------------------------------------------
# MAE/RMSE correctness
# ---------------------------------------------------------------------------


class TestMetricsCorrectness:
    def test_constant_predictor_mae(self) -> None:
        """A predictor that always returns 0 has MAE = mean(|expected_delta|)."""
        deltas = [1.0, 2.0, 3.0, -1.0]
        items = _make_items(deltas)
        expected_mae = sum(abs(d) for d in deltas) / len(deltas)

        report = run_harness(items, _ConstantPredictor(0.0))

        assert abs(report.mae - expected_mae) < 1e-6, (
            f"Expected MAE={expected_mae:.4f}, got {report.mae:.4f}"
        )

    def test_constant_predictor_rmse(self) -> None:
        deltas = [1.0, 2.0, 3.0]
        items = _make_items(deltas)
        # predictor returns 0, so errors are |delta|
        expected_rmse = math.sqrt(sum(d**2 for d in deltas) / len(deltas))

        report = run_harness(items, _ConstantPredictor(0.0))

        assert abs(report.rmse - expected_rmse) < 1e-6

    def test_perfect_predictor_mae_zero(self) -> None:
        deltas = [1.0, -2.0, 3.5, 0.0]
        items = _make_items(deltas)
        report = run_harness(items, _PerfectPredictor(items))
        assert report.mae < 1e-6
        assert report.rmse < 1e-6

    def test_max_abs_error_correct(self) -> None:
        deltas = [1.0, 2.0, 4.0]
        items = _make_items(deltas)
        # predictor returns 0, so abs errors are 1, 2, 4 -- max is 4
        report = run_harness(items, _ConstantPredictor(0.0))
        assert abs(report.max_abs_error - 4.0) < 1e-6

    def test_n_items_matches(self) -> None:
        items = _make_items([1.0, 2.0, 3.0])
        report = run_harness(items, _ConstantPredictor(0.0))
        assert report.n_items == 3


# ---------------------------------------------------------------------------
# Gate verdicts
# ---------------------------------------------------------------------------


class TestGateVerdicts:
    def test_verdict_ship_when_mae_below_ship_gate(self) -> None:
        """MAE clearly below MAE_SHIP_GATE (0.4) -> SHIP."""
        # All items have expected_delta 0; predictor returns 0.3 -> MAE=0.3 < 0.4.
        items = _make_items([0.0, 0.0, 0.0])
        report = run_harness(items, _ConstantPredictor(0.3))
        assert report.mae == pytest.approx(0.3, abs=1e-6)
        assert report.verdict == "SHIP"

    def test_verdict_pass_when_mae_between_gates(self) -> None:
        """0.4 < MAE <= 0.6 -> PASS."""
        items = _make_items([0.0, 0.0, 0.0])
        # predictor returns 0.5, so MAE = 0.5 -> PASS
        report = run_harness(items, _ConstantPredictor(0.5))
        assert report.verdict == "PASS"

    def test_verdict_fail_when_mae_above_architecture_gate(self) -> None:
        """MAE > 0.6 -> FAIL."""
        items = _make_items([0.0, 0.0, 0.0])
        report = run_harness(items, _ConstantPredictor(1.0))
        assert report.verdict == "FAIL"

    def test_verdict_pass_at_architecture_gate(self) -> None:
        """MAE exactly 0.6 -> PASS (not FAIL, boundary inclusive)."""
        items = _make_items([0.0, 0.0, 0.0])
        report = run_harness(items, _ConstantPredictor(MAE_ARCHITECTURE_GATE))
        assert report.verdict == "PASS"


# ---------------------------------------------------------------------------
# Anomaly flags
# ---------------------------------------------------------------------------


class TestAnomalyFlags:
    def test_bias_ok_when_residual_mean_small(self) -> None:
        """Perfect predictor has zero residual mean -> bias_ok=True."""
        deltas = [1.0, 2.0, 3.0]
        items = _make_items(deltas)
        report = run_harness(items, _PerfectPredictor(items))
        assert report.bias_ok is True

    def test_bias_not_ok_when_systematic_offset(self) -> None:
        """Predictor with +0.5 offset has residual mean = 0.5 -> bias_ok=False."""
        deltas = [1.0, 2.0, 3.0]
        items = _make_items(deltas)
        report = run_harness(items, _OffsetPredictor(items, offset=0.5))
        assert abs(report.residual_stats.mean - 0.5) < 1e-5
        assert report.bias_ok is False

    def test_no_mode_failure_when_errors_small(self) -> None:
        """Perfect predictor has max_abs_error=0 -> no_mode_failure=True."""
        deltas = [1.0, 2.0, 3.0]
        items = _make_items(deltas)
        report = run_harness(items, _PerfectPredictor(items))
        assert report.no_mode_failure is True

    def test_mode_failure_flagged_when_large_error(self) -> None:
        """Predictor returning 0 on a +5 label has max abs error = 5 -> flag."""
        items = _make_items([0.0, 0.0, 5.0])
        report = run_harness(items, _ConstantPredictor(0.0))
        assert report.max_abs_error == pytest.approx(5.0, abs=1e-6)
        assert report.no_mode_failure is False


# ---------------------------------------------------------------------------
# Residual statistics
# ---------------------------------------------------------------------------


class TestResidualStats:
    def test_residual_mean_signed(self) -> None:
        """residual = predicted - expected; with constant predictor 0, mean = -mean(deltas)."""
        deltas = [1.0, 2.0, 3.0]
        items = _make_items(deltas)
        report = run_harness(items, _ConstantPredictor(0.0))
        expected_mean = -(sum(deltas) / len(deltas))  # negative because predictor undershoots
        assert abs(report.residual_stats.mean - expected_mean) < 1e-6

    def test_residual_median_single_item(self) -> None:
        items = _make_items([2.0])
        report = run_harness(items, _ConstantPredictor(0.0))
        # residual = 0 - 2 = -2; median = -2
        assert report.residual_stats.p50 == pytest.approx(-2.0, abs=1e-6)

    def test_stdev_zero_for_constant_residuals(self) -> None:
        """All residuals equal -> stdev = 0."""
        deltas = [1.0, 1.0, 1.0]
        items = _make_items(deltas)
        report = run_harness(items, _ConstantPredictor(0.0))
        assert report.residual_stats.stdev == pytest.approx(0.0, abs=1e-6)


# ---------------------------------------------------------------------------
# Empty items guard
# ---------------------------------------------------------------------------


class TestEmptyItems:
    def test_empty_items_raises(self) -> None:
        with pytest.raises(ValueError, match="empty"):
            run_harness([], _ConstantPredictor(0.0))


# ---------------------------------------------------------------------------
# HarnessReport is a valid Pydantic model
# ---------------------------------------------------------------------------


class TestHarnessReport:
    def test_report_is_pydantic(self) -> None:
        items = _make_items([1.0, -1.0])
        report = run_harness(items, _ConstantPredictor(0.0))
        assert isinstance(report, HarnessReport)

    def test_items_length_matches_n_items(self) -> None:
        items = _make_items([1.0, 2.0, 3.0])
        report = run_harness(items, _ConstantPredictor(0.0))
        assert len(report.items) == report.n_items


# ---------------------------------------------------------------------------
# Output functions
# ---------------------------------------------------------------------------


class TestOutputFunctions:
    def test_write_report_json(self, tmp_path: Path) -> None:
        items = _make_items([1.0, 2.0])
        report = run_harness(items, _ConstantPredictor(0.0))
        out = tmp_path / "report.json"
        write_report_json(report, out)
        assert out.is_file()
        data = json.loads(out.read_text(encoding="utf-8"))
        assert "mae" in data
        assert "verdict" in data
        assert "items" in data

    def test_write_items_csv(self, tmp_path: Path) -> None:
        items = _make_items([1.0, -2.0])
        report = run_harness(items, _ConstantPredictor(0.0))
        out = tmp_path / "items.csv"
        write_items_csv(report, out)
        assert out.is_file()
        rows = list(csv.DictReader(out.open(encoding="utf-8")))
        assert len(rows) == 2
        assert "abs_error" in rows[0]
        assert "predicted_delta" in rows[0]
        # Sorted by abs_error descending -- item with delta=-2 has higher error.
        assert float(rows[0]["abs_error"]) >= float(rows[1]["abs_error"])

    def test_format_console_summary_contains_verdict(self) -> None:
        items = _make_items([0.5])
        report = run_harness(items, _ConstantPredictor(0.0))
        summary = format_console_summary(report)
        assert report.verdict in summary
        assert "OVERALL" in summary


# ---------------------------------------------------------------------------
# Architecture gate contract: stub dataset passes PASS with near-perfect model
# ---------------------------------------------------------------------------


class TestStubArchitectureGate:
    def test_stub_passes_architecture_gate_with_perfect_predictor(self) -> None:
        """A near-perfect predictor on the stub must produce PASS or SHIP.

        This test anchors the contract: the stub's labels are designer-reasonable
        and a model that predicts them correctly clears the 0.6 gate.  If this
        test ever fails it means the stub labels have been corrupted or the
        gate constant changed.
        """
        items = load_dataset(default_stub_path())
        report = run_harness(items, _PerfectPredictor(items))
        assert report.verdict in ("PASS", "SHIP"), (
            f"Perfect predictor should PASS or SHIP on the stub, got {report.verdict} "
            f"(MAE={report.mae:.4f})"
        )
        assert report.mae < 1e-5, "Perfect predictor should have near-zero MAE"

    def test_stub_architecture_gate_threshold_is_correct(self) -> None:
        """MAE_ARCHITECTURE_GATE must be 0.6 per Pax spec."""
        assert MAE_ARCHITECTURE_GATE == 0.6

    def test_stub_ship_gate_threshold_is_correct(self) -> None:
        """MAE_SHIP_GATE must be 0.4 per M2 brief."""
        assert MAE_SHIP_GATE == 0.4
