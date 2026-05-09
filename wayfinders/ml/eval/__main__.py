"""CLI entry point for the UC1 hand-graded evaluation harness.

Usage:

    # Run on the bundled stub dataset (default):
    uv run python -m wayfinders.ml.eval

    # Run on a custom dataset with a specific checkpoint:
    uv run python -m wayfinders.ml.eval \\
        --dataset wayfinders/ml/eval/datasets/hand_graded_v1.jsonl \\
        --encoder-onnx artifacts/onnx/minilm-l6-v2-fp16.onnx \\
        --head-onnx   artifacts/onnx/resolution-head-v0.6.onnx

    # Override output directory (default: artifacts/eval/):
    uv run python -m wayfinders.ml.eval --output-dir /tmp/eval_run

Outputs written to <output-dir>/:
    eval_report.json     -- Full HarnessReport (all metrics + per-item list).
    eval_items.csv       -- Per-item predictions sorted by abs_error desc.

Exit codes:
    0 -- verdict SHIP or PASS (architecture gate met)
    1 -- verdict FAIL (MAE > 0.6 -- escalate to Larry)
    2 -- dataset or predictor error (file not found, ONNX not loaded, etc.)
"""

from __future__ import annotations

import argparse
import logging
import os
import sys
from pathlib import Path

from wayfinders.ml.eval.harness import (
    format_console_summary,
    run_harness,
    write_items_csv,
    write_report_json,
)
from wayfinders.ml.eval.schema import default_stub_path, load_dataset

logger = logging.getLogger(__name__)

# Repo root: three levels up from this file (wayfinders/ml/eval/__main__.py).
_REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent

_DEFAULT_ENCODER_ONNX = _REPO_ROOT / "artifacts" / "onnx" / "minilm-l6-v2-fp16.onnx"
_DEFAULT_HEAD_ONNX = _REPO_ROOT / "artifacts" / "onnx" / "resolution-head-v0.6.onnx"
_DEFAULT_OUTPUT_DIR = _REPO_ROOT / "artifacts" / "eval"


def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="python -m wayfinders.ml.eval",
        description="UC1 hand-graded evaluation harness.",
    )
    parser.add_argument(
        "--dataset",
        type=Path,
        default=None,
        help=(
            f"Path to the JSONL hand-graded dataset. Default: {default_stub_path()} (bundled stub)."
        ),
    )
    parser.add_argument(
        "--encoder-onnx",
        type=Path,
        default=None,
        help=f"Path to the encoder ONNX file. Default: {_DEFAULT_ENCODER_ONNX}",
    )
    parser.add_argument(
        "--head-onnx",
        type=Path,
        default=None,
        help=f"Path to the head ONNX file. Default: {_DEFAULT_HEAD_ONNX}",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=_DEFAULT_OUTPUT_DIR,
        help=f"Directory to write eval_report.json and eval_items.csv. Default: {_DEFAULT_OUTPUT_DIR}",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Enable DEBUG logging.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    """Run the evaluation harness. Returns an exit code (0=ok, 1=fail, 2=error)."""
    args = _parse_args(argv)

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(levelname)s %(name)s %(message)s",
    )

    # ------------------------------------------------------------------ #
    # Dataset
    # ------------------------------------------------------------------ #
    dataset_path: Path = args.dataset if args.dataset is not None else default_stub_path()

    print(f"Loading dataset: {dataset_path}")
    try:
        items = load_dataset(dataset_path)
    except (FileNotFoundError, ValueError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2

    print(f"  Loaded {len(items)} items.")

    # ------------------------------------------------------------------ #
    # ONNX artifact paths -- respect env vars (same convention as predictor)
    # ------------------------------------------------------------------ #
    encoder_onnx: Path = args.encoder_onnx or Path(
        os.environ.get("WAYFINDERS_ENCODER_ONNX", "") or str(_DEFAULT_ENCODER_ONNX)
    )
    head_onnx: Path = args.head_onnx or Path(
        os.environ.get("WAYFINDERS_HEAD_ONNX", "") or str(_DEFAULT_HEAD_ONNX)
    )

    # ------------------------------------------------------------------ #
    # Predictor
    # ------------------------------------------------------------------ #
    # Set env vars so Predictor picks up the CLI-specified paths.
    if args.encoder_onnx is not None:
        os.environ["WAYFINDERS_ENCODER_ONNX"] = str(encoder_onnx)
    if args.head_onnx is not None:
        os.environ["WAYFINDERS_HEAD_ONNX"] = str(head_onnx)

    from wayfinders.api.predictor import Predictor, PredictorNotReadyError

    print(f"Loading predictor (encoder: {encoder_onnx}, head: {head_onnx}) ...")
    predictor = Predictor()

    if not predictor.ready:
        print(
            "ERROR: Predictor not ready. "
            "Ensure ONNX artifacts exist and onnxruntime/sentence-transformers are installed.\n"
            "  Run: uv run python -m wayfinders.ml.export.export_encoder\n"
            "  Run: uv run python -m wayfinders.ml.export.export_head",
            file=sys.stderr,
        )
        return 2

    # ------------------------------------------------------------------ #
    # Run harness
    # ------------------------------------------------------------------ #
    print("Running harness ...")
    try:
        report = run_harness(items, predictor, dataset_path=str(dataset_path))
    except PredictorNotReadyError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2

    # ------------------------------------------------------------------ #
    # Console output
    # ------------------------------------------------------------------ #
    print(format_console_summary(report))

    # ------------------------------------------------------------------ #
    # Write outputs
    # ------------------------------------------------------------------ #
    output_dir: Path = args.output_dir
    output_dir.mkdir(parents=True, exist_ok=True)

    json_path = output_dir / "eval_report.json"
    csv_path = output_dir / "eval_items.csv"

    write_report_json(report, json_path)
    write_items_csv(report, csv_path)

    print(f"Report  -> {json_path}")
    print(f"Items   -> {csv_path}")

    # ------------------------------------------------------------------ #
    # Exit code
    # ------------------------------------------------------------------ #
    if report.verdict == "FAIL":
        print(
            f"FAIL: MAE={report.mae:.4f} exceeds architecture gate "
            f"({report.mae:.4f} > {0.6}). Escalate to Larry.",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
