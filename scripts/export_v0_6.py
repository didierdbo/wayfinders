"""Substep 11+12: ONNX export v0.6 + Python vs ORT parity CSV.

Substep 11:
  - Exports the v0.6 ResolutionHead checkpoint to fp32 ONNX (opset 17).
  - Writes artifacts/onnx/resolution-head-v0.6.onnx + manifest sidecar.

Substep 12:
  - Runs 100-rollout Python-vs-ORT parity check (same as the export contract gate).
  - Writes artifacts/onnx/parity-v0.6.csv with per-rollout delta diffs.
  - Fails hard (exit 1) if max_abs_diff >= 1e-4 (the architecture parity gate).

Usage:
    uv run python scripts/export_v0_6.py
    uv run python scripts/export_v0_6.py --n-parity 20   # quick smoke

The parity CSV columns:
  rollout_idx, delta_torch, delta_onnx, abs_diff, within_1e4
"""

from __future__ import annotations

import argparse
import csv
import logging
import sys
from pathlib import Path

import torch

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s  %(levelname)-8s  %(name)s  %(message)s",
    datefmt="%H:%M:%S",
    stream=sys.stdout,
)
logger = logging.getLogger("export_v0_6")

REPO_ROOT = Path(__file__).resolve().parent.parent
CHECKPOINT_V06 = REPO_ROOT / "artifacts" / "models" / "v0.6" / "head_final.pt"
METRICS_V06 = REPO_ROOT / "artifacts" / "models" / "v0.6" / "metrics.json"
TEST_METRICS_V06 = REPO_ROOT / "artifacts" / "models" / "v0.6" / "test_metrics.json"
ENCODER_MANIFEST = REPO_ROOT / "artifacts" / "onnx" / "minilm-l6-v2-fp16.manifest.json"
ENCODER_ONNX = REPO_ROOT / "artifacts" / "onnx" / "minilm-l6-v2-fp16.onnx"
HEAD_ONNX_OUT = REPO_ROOT / "artifacts" / "onnx" / "resolution-head-v0.6.onnx"
PARITY_CSV_OUT = REPO_ROOT / "artifacts" / "onnx" / "parity-v0.6.csv"

# Parity gate: Python vs ORT must agree to within 1e-4 on Δ.
PARITY_ATOL = 1e-4


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Export UC1 head v0.6 to ONNX + parity CSV")
    p.add_argument(
        "--n-parity",
        type=int,
        default=100,
        help="Number of rollouts for parity check (default: 100)",
    )
    p.add_argument(
        "--seed",
        type=int,
        default=777,
        help="Seed for parity rollout generation (different from training seed)",
    )
    p.add_argument(
        "--skip-parity",
        action="store_true",
        help="Skip the parity check (only export ONNX)",
    )
    return p.parse_args()


def main() -> None:
    args = parse_args()

    # ------------------------------------------------------------------
    # CUDA invariant
    # ------------------------------------------------------------------
    if not torch.cuda.is_available():
        logger.error(
            "CUDA not available. export_head() requires CUDA (numerical contract). "
            "Verify torch cu124 build: torch.version.cuda=%s",
            getattr(torch.version, "cuda", "None"),
        )
        sys.exit(1)

    logger.info("torch %s | CUDA available | device: cuda", torch.__version__)

    # ------------------------------------------------------------------
    # Substep 11: ONNX export
    # ------------------------------------------------------------------
    logger.info("=" * 60)
    logger.info("SUBSTEP 11 -- ONNX export v0.6")
    logger.info("  checkpoint : %s", CHECKPOINT_V06)
    logger.info("  output     : %s", HEAD_ONNX_OUT)

    if not CHECKPOINT_V06.is_file():
        logger.error("v0.6 checkpoint not found: %s", CHECKPOINT_V06)
        sys.exit(1)

    from wayfinders.ml.export.export_head import export_head

    onnx_path = export_head(
        checkpoint_path=CHECKPOINT_V06,
        out_path=HEAD_ONNX_OUT,
        metrics_path=METRICS_V06,
        test_metrics_path=TEST_METRICS_V06,
        encoder_manifest_path=ENCODER_MANIFEST,
    )
    size_mb = onnx_path.stat().st_size / 1e6
    logger.info("ONNX export OK: %s (%.2f MB)", onnx_path, size_mb)

    manifest_path = onnx_path.parent / (onnx_path.stem + ".manifest.json")
    logger.info("Manifest written: %s", manifest_path)

    # ------------------------------------------------------------------
    # Substep 12: Parity CSV
    # ------------------------------------------------------------------
    if args.skip_parity:
        logger.info("--skip-parity: skipping substep 12")
        return

    logger.info("=" * 60)
    logger.info(
        "SUBSTEP 12 -- Python vs ORT parity (%d rollouts, atol=%g)", args.n_parity, PARITY_ATOL
    )

    if not ENCODER_ONNX.is_file():
        logger.error("Encoder ONNX not found: %s", ENCODER_ONNX)
        sys.exit(1)

    # Load encoder (used in torch path; ORT path uses the ONNX encoder session)
    from wayfinders.ml.inference.encoder import MiniLMEncoder

    encoder = MiniLMEncoder(device="cuda")
    logger.info("MiniLMEncoder loaded (fp16=%s)", encoder.use_fp16)

    # Load head from checkpoint for torch path
    from wayfinders.ml.training.head import ResolutionHead

    head = ResolutionHead()
    raw_ckpt = torch.load(CHECKPOINT_V06, map_location="cuda", weights_only=True)
    if isinstance(raw_ckpt, dict) and "model_state_dict" in raw_ckpt:
        state_dict = raw_ckpt["model_state_dict"]
    else:
        state_dict = raw_ckpt
    head.load_state_dict(state_dict)
    head.eval()
    head.cuda()
    for p in head.parameters():
        p.requires_grad_(False)
    logger.info("ResolutionHead loaded from checkpoint")

    # Generate rollouts (seed different from training to test generalization)
    from wayfinders.ml.data_gen.rollouts import generate_rollouts

    logger.info("Generating %d rollouts (seed=%d) ...", args.n_parity, args.seed)
    rollouts = list(generate_rollouts(args.n_parity, seed=args.seed))
    logger.info("Generated %d rollouts", len(rollouts))

    # Run parity check
    from wayfinders.ml.export.verify import verify_pipeline_end_to_end

    result = verify_pipeline_end_to_end(
        torch_encoder=encoder,
        torch_head=head,
        encoder_onnx_path=ENCODER_ONNX,
        head_onnx_path=onnx_path,
        rollouts=rollouts,
        atol=PARITY_ATOL,
    )

    # Write CSV
    PARITY_CSV_OUT.parent.mkdir(parents=True, exist_ok=True)
    with PARITY_CSV_OUT.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.writer(fh)
        writer.writerow(["rollout_idx", "delta_torch", "delta_onnx", "abs_diff", "within_1e4"])
        for i, (dt, do) in enumerate(zip(result.delta_torch, result.delta_onnx, strict=True)):
            abs_d = abs(float(dt) - float(do))
            writer.writerow([i, float(dt), float(do), abs_d, abs_d < PARITY_ATOL])

    logger.info("Parity CSV written: %s", PARITY_CSV_OUT)

    # Gate report
    logger.info("=" * 60)
    logger.info("PARITY GATE v0.6 (atol=%g)", PARITY_ATOL)
    logger.info("  n_rollouts    : %d", result.n_rollouts)
    logger.info("  max_abs_diff  : %.2e", result.max_abs_diff)
    logger.info("  mean_abs_diff : %.2e", result.mean_abs_diff)
    logger.info("  violations    : %d / %d", result.violations, result.n_rollouts)
    logger.info("  passed        : %s", result.passed)
    logger.info("=" * 60)

    if not result.passed:
        logger.error(
            "PARITY GATE FAILED: max_abs_diff=%.2e exceeds atol=%.2e. "
            "Check for fp16 quantization or ONNX conversion regression. "
            "Escalating to Larry.",
            result.max_abs_diff,
            PARITY_ATOL,
        )
        sys.exit(1)

    logger.info("Parity PASS. Substeps 11 + 12 complete.")


if __name__ == "__main__":
    main()
