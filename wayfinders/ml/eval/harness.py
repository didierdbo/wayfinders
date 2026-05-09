"""UC1 hand-graded evaluation harness.

Pipeline:
  1. Load dataset (JSONL of HandGradedItem).
  2. For each item call ``Predictor.predict()`` (ONNX Runtime -- same path
     as the live API and the C# client).
  3. Compute MAE, RMSE, max abs error, residual distribution (mean, stdev,
     percentiles 5/25/50/75/95).
  4. Apply gates:
       MAE <= 0.6 -> PASS   (architecture gate, Pax 2026-05-02)
       MAE <= 0.4 -> SHIP   (M2 close gate)
       else       -> FAIL
  5. Apply anomaly flags:
       abs(residual_mean) < 0.1  -- no systematic bias
       max_abs_error <= 2.0       -- no mode failure
  6. Return HarnessReport (Pydantic model).  The caller serialises it to JSON
     and writes a CSV of per-item predictions.

Gate thresholds are defined as module-level constants so tests can reference
them without importing strings.

Design notes:
  - The Predictor is injected as an argument so the harness is testable with
    a mock predictor and does not require live ONNX artifacts in unit tests.
  - All floating-point stats are computed in pure Python + statistics module
    to avoid a mandatory torch/numpy dependency at eval time (the Predictor
    already depends on numpy/onnxruntime; no need to add torch here).
  - The per-item prediction list is written out as CSV so Varn and Didier can
    open it in a spreadsheet without needing any Python tooling.
"""

from __future__ import annotations

import csv
import json
import logging
import math
import statistics
from pathlib import Path
from typing import Literal, Protocol

from pydantic import BaseModel, ConfigDict, Field

from wayfinders.ml.eval.schema import HandGradedItem

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Gate thresholds
# ---------------------------------------------------------------------------

MAE_ARCHITECTURE_GATE: float = 0.6  # PASS gate (M1 ship requirement)
MAE_SHIP_GATE: float = 0.4  # SHIP gate (M2 close requirement)
BIAS_GATE: float = 0.1  # max |mean(residual)| for no-systematic-bias flag
OUTLIER_GATE: float = 2.0  # max abs error above which mode failure is flagged

# Verdict literals
Verdict = Literal["PASS", "FAIL", "SHIP"]


# ---------------------------------------------------------------------------
# Predictor protocol -- allows injection of a mock in tests
# ---------------------------------------------------------------------------


class PredictorProtocol(Protocol):
    """Minimal interface the harness uses from the Predictor."""

    def predict(self, char_prose: str, action_prose: str, context_prose: str) -> float:
        """Run inference and return delta in [-5, +5]."""
        ...


# ---------------------------------------------------------------------------
# Report models
# ---------------------------------------------------------------------------


class ItemResult(BaseModel):
    """Prediction result for a single hand-graded item."""

    model_config = ConfigDict(frozen=True)

    id: str
    expected_delta: float
    predicted_delta: float
    abs_error: float
    residual: float  # predicted - expected


class ResidualStats(BaseModel):
    """Distribution statistics of residuals (predicted - expected)."""

    model_config = ConfigDict(frozen=True)

    mean: float
    stdev: float
    p5: float
    p25: float
    p50: float
    p75: float
    p95: float


class HarnessReport(BaseModel):
    """Full evaluation report from a single harness run.

    Attributes
    ----------
    dataset_path : str
        Path of the dataset file that was evaluated.
    n_items : int
        Number of items evaluated.
    mae : float
        Mean absolute error over all items.
    rmse : float
        Root mean squared error over all items.
    max_abs_error : float
        Worst-case absolute error.
    residual_stats : ResidualStats
        Distribution of (predicted - expected) residuals.
    verdict : Verdict
        "SHIP" if MAE <= 0.4, "PASS" if MAE <= 0.6, "FAIL" otherwise.
    bias_ok : bool
        True when abs(residual mean) < 0.1 (no systematic bias).
    no_mode_failure : bool
        True when max abs error <= 2.0 (no outlier suggesting mode failure).
    items : list[ItemResult]
        Per-item breakdown.
    """

    model_config = ConfigDict(frozen=True)

    dataset_path: str = Field(..., description="Path of the evaluated dataset file.")
    n_items: int = Field(..., description="Number of items evaluated.")
    mae: float = Field(..., description="Mean absolute error.")
    rmse: float = Field(..., description="Root mean squared error.")
    max_abs_error: float = Field(..., description="Maximum absolute error across all items.")
    residual_stats: ResidualStats = Field(..., description="Distribution of residuals.")
    verdict: Verdict = Field(..., description="SHIP | PASS | FAIL gate verdict.")
    bias_ok: bool = Field(..., description="True when abs(residual_mean) < 0.1.")
    no_mode_failure: bool = Field(..., description="True when max_abs_error <= 2.0.")
    items: list[ItemResult] = Field(..., description="Per-item prediction breakdown.")


# ---------------------------------------------------------------------------
# Core evaluation function
# ---------------------------------------------------------------------------


def _percentile(sorted_data: list[float], p: float) -> float:
    """Linear interpolation percentile on a sorted list (0 <= p <= 100)."""
    n = len(sorted_data)
    if n == 1:
        return sorted_data[0]
    idx = p / 100.0 * (n - 1)
    lo = int(idx)
    hi = min(lo + 1, n - 1)
    frac = idx - lo
    return sorted_data[lo] * (1.0 - frac) + sorted_data[hi] * frac


def _verdict(mae: float) -> Verdict:
    if mae <= MAE_SHIP_GATE:
        return "SHIP"
    if mae <= MAE_ARCHITECTURE_GATE:
        return "PASS"
    return "FAIL"


def run_harness(
    items: list[HandGradedItem],
    predictor: PredictorProtocol,
    dataset_path: str = "<in-memory>",
) -> HarnessReport:
    """Evaluate ``predictor`` against ``items`` and return a HarnessReport.

    Args:
        items:        Validated hand-graded items.
        predictor:    Any object implementing PredictorProtocol (Predictor or mock).
        dataset_path: String label for the dataset (used in the report; no I/O).

    Returns:
        HarnessReport with all metrics and per-item results.

    Raises:
        ValueError: if ``items`` is empty.
        Any exception raised by ``predictor.predict()`` propagates without
        being swallowed (fail fast on inference errors).
    """
    if not items:
        raise ValueError("run_harness: items list is empty")

    results: list[ItemResult] = []

    for item in items:
        predicted = predictor.predict(
            char_prose=item.char_prose,
            action_prose=item.action_prose,
            context_prose=item.context_prose,
        )
        abs_err = abs(predicted - item.expected_delta)
        residual = predicted - item.expected_delta
        results.append(
            ItemResult(
                id=item.id,
                expected_delta=item.expected_delta,
                predicted_delta=predicted,
                abs_error=abs_err,
                residual=residual,
            )
        )
        logger.debug(
            "item=%s expected=%.2f predicted=%.2f abs_err=%.3f",
            item.id,
            item.expected_delta,
            predicted,
            abs_err,
        )

    abs_errors = [r.abs_error for r in results]
    residuals = [r.residual for r in results]
    n = len(results)

    mae = sum(abs_errors) / n
    rmse = math.sqrt(sum(e**2 for e in abs_errors) / n)
    max_abs_error = max(abs_errors)

    # Residual distribution
    sorted_res = sorted(residuals)
    res_mean = statistics.mean(residuals)
    res_stdev = statistics.stdev(residuals) if n > 1 else 0.0
    res_stats = ResidualStats(
        mean=res_mean,
        stdev=res_stdev,
        p5=_percentile(sorted_res, 5),
        p25=_percentile(sorted_res, 25),
        p50=_percentile(sorted_res, 50),
        p75=_percentile(sorted_res, 75),
        p95=_percentile(sorted_res, 95),
    )

    verdict = _verdict(mae)
    bias_ok = abs(res_mean) < BIAS_GATE
    no_mode_failure = max_abs_error <= OUTLIER_GATE

    logger.info(
        "Harness: n=%d MAE=%.4f RMSE=%.4f max_abs=%.4f verdict=%s bias_ok=%s no_mode_failure=%s",
        n,
        mae,
        rmse,
        max_abs_error,
        verdict,
        bias_ok,
        no_mode_failure,
    )

    return HarnessReport(
        dataset_path=dataset_path,
        n_items=n,
        mae=mae,
        rmse=rmse,
        max_abs_error=max_abs_error,
        residual_stats=res_stats,
        verdict=verdict,
        bias_ok=bias_ok,
        no_mode_failure=no_mode_failure,
        items=results,
    )


# ---------------------------------------------------------------------------
# Output helpers
# ---------------------------------------------------------------------------


def write_report_json(report: HarnessReport, output_path: Path) -> None:
    """Write the HarnessReport to a JSON file.

    Args:
        report:      HarnessReport to serialise.
        output_path: Destination file path.  Parent directory must exist.
    """
    output_path.write_text(
        json.dumps(report.model_dump(), indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    logger.info("Report written to %s", output_path)


def write_items_csv(report: HarnessReport, output_path: Path) -> None:
    """Write the per-item prediction table to a CSV file.

    Columns: id, expected_delta, predicted_delta, abs_error, residual.
    Sorted by abs_error descending (worst errors first) for easy review.

    Args:
        report:      HarnessReport with per-item results.
        output_path: Destination .csv path.  Parent directory must exist.
    """
    sorted_items = sorted(report.items, key=lambda r: r.abs_error, reverse=True)
    with output_path.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(
            fh,
            fieldnames=["id", "expected_delta", "predicted_delta", "abs_error", "residual"],
        )
        writer.writeheader()
        for r in sorted_items:
            writer.writerow(
                {
                    "id": r.id,
                    "expected_delta": f"{r.expected_delta:.4f}",
                    "predicted_delta": f"{r.predicted_delta:.4f}",
                    "abs_error": f"{r.abs_error:.4f}",
                    "residual": f"{r.residual:.4f}",
                }
            )
    logger.info("Items CSV written to %s", output_path)


def format_console_summary(report: HarnessReport) -> str:
    """Return a human-readable console summary string.

    This is printed to stdout by the CLI and is the primary signal for
    Varn / Didier to read during eval runs.
    """
    rs = report.residual_stats
    lines = [
        "",
        "=" * 60,
        f"  UC1 Hand-Graded Evaluation -- {report.dataset_path}",
        "=" * 60,
        f"  Items evaluated : {report.n_items}",
        f"  MAE             : {report.mae:.4f}",
        f"  RMSE            : {report.rmse:.4f}",
        f"  Max abs error   : {report.max_abs_error:.4f}",
        "",
        "  Residual distribution (predicted - expected):",
        f"    mean   = {rs.mean:+.4f}",
        f"    stdev  =  {rs.stdev:.4f}",
        f"    p5/p95 = [{rs.p5:+.3f}, {rs.p95:+.3f}]",
        f"    p25/75 = [{rs.p25:+.3f}, {rs.p75:+.3f}]",
        f"    median = {rs.p50:+.4f}",
        "",
        f"  Bias check      : {'OK  (|mean| < 0.1)' if report.bias_ok else 'WARN (|mean| >= 0.1 -- systematic bias)'}",
        f"  Mode failure    : {'none (max_abs <= 2.0)' if report.no_mode_failure else 'WARN (max_abs > 2.0 -- check outlier items)'}",
        "",
        f"  OVERALL: {report.verdict}",
        "=" * 60,
        "",
    ]
    return "\n".join(lines)
