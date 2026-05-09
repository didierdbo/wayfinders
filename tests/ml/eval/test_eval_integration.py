"""Integration test: full harness pipeline against the live ONNX predictor.

Requires:
  - onnxruntime installed
  - sentence-transformers installed
  - artifacts/onnx/minilm-l6-v2-fp16.onnx exists
  - artifacts/onnx/resolution-head-v0.6.onnx exists

Marked with @pytest.mark.integration so they are skipped in CI unless
explicitly invoked with ``pytest -m integration``.

This test is the primary green signal for the M2 slice eval gate:
  - It loads the stub dataset.
  - It runs the live ONNX predictor.
  - It verifies the verdict is PASS or SHIP (MAE <= 0.6).
  - It escalation-prints a clear message if FAIL (MAE > 0.6).

If this test FAILS, stop and escalate to Larry before pushing.
"""

from __future__ import annotations

import logging

import pytest

from wayfinders.ml.eval.harness import (
    MAE_ARCHITECTURE_GATE,
    format_console_summary,
    run_harness,
)
from wayfinders.ml.eval.schema import default_stub_path, load_dataset

logger = logging.getLogger(__name__)


@pytest.mark.integration
class TestHarnessOnStubWithLivePredictor:
    def test_live_predictor_passes_architecture_gate_on_stub(self) -> None:
        """The v0.6 ONNX predictor must achieve MAE <= 0.6 on the stub dataset.

        This is the primary M2 architecture gate check.  If it fails,
        the checkpoint is not fit for production -- escalate to Larry.
        """
        from wayfinders.api.predictor import Predictor

        predictor = Predictor()
        if not predictor.ready:
            pytest.skip("Predictor not ready -- ONNX artifacts missing, skipping integration test")

        items = load_dataset(default_stub_path())
        report = run_harness(items, predictor, dataset_path=str(default_stub_path()))

        # Print the summary to the test output for visibility.
        print(format_console_summary(report))

        assert report.verdict in ("PASS", "SHIP"), (
            f"ESCALATE: v0.6 checkpoint FAILS the architecture gate on stub dataset.\n"
            f"MAE={report.mae:.4f} > {MAE_ARCHITECTURE_GATE} (gate).\n"
            f"Check the predictor or the stub labels. Do not push until resolved."
        )

        logger.info(
            "Integration eval: n=%d MAE=%.4f RMSE=%.4f verdict=%s",
            report.n_items,
            report.mae,
            report.rmse,
            report.verdict,
        )
