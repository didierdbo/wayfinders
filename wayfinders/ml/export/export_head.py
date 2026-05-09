"""ONNX export for the trained ResolutionHead (substep 12).

Exports the trained ``ResolutionHead`` checkpoint as a self-contained ONNX
graph.  The head internally computes ``cos(char_vec, action_vec)`` and
concatenates ``[char_vec, action_vec, context_vec, cos]`` → 1153-dim before
the MLP and ``tanh * 5`` output.  Rune does not compute the cosine or apply
any clipping client-side -- it is all in the graph.

Design notes:
  - Precision: fp32 ship default.  The head is ~313k params / ~1.2 MB;
    quantization saves nothing meaningful and adds risk.  fp16 is used for the
    input tensors during export only if CUDA is available (matches training),
    but the ONNX graph itself carries float32 weights.
  - Opset 17: consistent with the encoder export (substep 11).
  - CUDA contract: the function requires CUDA to be available so the exported
    graph comes from a CUDA forward pass.  Silent CPU fallback would diverge
    from the training-time numerical environment.
  - The checkpoint loaded is the best-val checkpoint, not the final epoch.

Output contract (matches Coda - ONNX Export Contract §1.2):
  Inputs:
    char_vec    float32  (batch, 384)   L2-normalised character embedding
    action_vec  float32  (batch, 384)   L2-normalised action embedding
    context_vec float32  (batch, 384)   L2-normalised context embedding
  Output:
    delta       float32  (batch, 1)     Δ ∈ [-5, +5] (tanh * 5 in-graph)
  Opset: 17.

Manifest sidecar:
  Written alongside the .onnx file.  Contains model_name, version,
  export_timestamp, head_sha256, head_pt_sha256, training_run_id,
  val_mae, test_mae, r_squared_test, encoder_sha256 (coupling trace),
  opset_version, dtype, input_schema, output_schema, tanh_scaling.
"""

from __future__ import annotations

import hashlib
import json
import logging
from datetime import UTC, datetime
from pathlib import Path
from typing import Final

import torch
import torch.nn as nn

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

ONNX_OPSET: Final[int] = 17
ONNX_DTYPE: Final[str] = "fp32"
EMBEDDING_DIM: Final[int] = 384
HEAD_INPUT_DIM: Final[int] = EMBEDDING_DIM * 3 + 1  # 1153

# Default paths (relative to repo root -- callers may override).
# Updated to v0.6: retrain PASS, val MAE 0.5657 (gate <=0.6 cleared, 2026-05-09).
DEFAULT_CHECKPOINT_PATH: Final[str] = "artifacts/models/v0.6/head_final.pt"
DEFAULT_METRICS_PATH: Final[str] = "artifacts/models/v0.6/metrics.json"
DEFAULT_TEST_METRICS_PATH: Final[str] = "artifacts/models/v0.6/test_metrics.json"
DEFAULT_ENCODER_MANIFEST_PATH: Final[str] = "artifacts/onnx/minilm-l6-v2-fp16.manifest.json"
DEFAULT_OUT_PATH: Final[str] = "artifacts/onnx/resolution-head-v0.6.onnx"

# Known v0.6 metrics (retrain 2026-05-09, 100 epochs, seed=42).
# v0.1 kept as legacy fallback for callers that still reference the old checkpoint.
_V06_VAL_MAE: Final[float] = 0.5657
_V06_TEST_MAE: Final[float] = 0.8240
_V06_R_SQUARED_TEST: Final[float] = 0.7655

# Legacy v0.1 fallback metrics (kept for backward compat in callers).
_V01_VAL_MAE: Final[float] = 0.9027
_V01_TEST_MAE: Final[float] = 0.9893
_V01_R_SQUARED_TEST: Final[float] = 0.7938


# ---------------------------------------------------------------------------
# Export wrapper
# ---------------------------------------------------------------------------


class _HeadExportWrapper(nn.Module):
    """Thin wrapper that exposes three separate 384-dim inputs for ONNX export.

    The ResolutionHead ``forward()`` already computes cos(char, action) internally
    and concatenates the 1153-dim vector.  This wrapper just forwards the three
    vectors; the graph captures the full computation including cos and tanh*5.
    """

    def __init__(self, head: nn.Module) -> None:
        super().__init__()
        self.head = head

    def forward(
        self,
        char_vec: torch.Tensor,
        action_vec: torch.Tensor,
        context_vec: torch.Tensor,
    ) -> torch.Tensor:
        result: torch.Tensor = self.head(char_vec, action_vec, context_vec)
        return result


# ---------------------------------------------------------------------------
# SHA-256 helpers
# ---------------------------------------------------------------------------


def _sha256_file(path: Path) -> str:
    """Return hex SHA-256 digest of *path* (streaming, 64 KB blocks)."""
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


# ---------------------------------------------------------------------------
# Public export function
# ---------------------------------------------------------------------------


def export_head(
    checkpoint_path: Path,
    out_path: Path,
    *,
    manifest_path: Path | None = None,
    metrics_path: Path | None = None,
    test_metrics_path: Path | None = None,
    encoder_manifest_path: Path | None = None,
) -> Path:
    """Export the trained ResolutionHead to ONNX (fp32, opset 17).

    Args:
        checkpoint_path:       Path to the trained head checkpoint (.pt file,
                               state_dict saved by train_loop).
        out_path:              Destination path for the .onnx file.
                               Parent directories are created if needed.
        manifest_path:         Optional path for the manifest JSON sidecar.
                               Defaults to ``out_path.with_suffix('.manifest.json')``.
        metrics_path:          Optional path to metrics.json from the training run
                               (used to extract training_run_id and val_mae).
                               Falls back to DEFAULT_METRICS_PATH relative to repo root.
        test_metrics_path:     Optional path to test_metrics.json from the training run.
        encoder_manifest_path: Optional path to the encoder manifest (substep 11)
                               for tracing the encoder↔head coupling in the manifest.

    Returns:
        out_path (for convenience in scripts).

    Raises:
        RuntimeError: if CUDA is not available (CUDA contract violated).
        FileNotFoundError: if checkpoint_path does not exist.

    Side effects:
        - Writes the .onnx file to *out_path*.
        - Writes the manifest JSON sidecar to *manifest_path*.
        - Logs progress at INFO level.
    """
    if not torch.cuda.is_available():
        raise RuntimeError(
            "export_head() requires CUDA. "
            "The head must be exported from the same CUDA environment it was trained in; "
            "silent CPU fallback would violate the numerical contract."
        )

    if not checkpoint_path.is_file():
        raise FileNotFoundError(f"Checkpoint not found: {checkpoint_path}")

    if manifest_path is None:
        # Replace the .onnx extension with .manifest.json.
        # Cannot use .with_suffix("").with_suffix(".manifest.json") because that would
        # strip two suffix components for names like "resolution-head-v0.1.onnx"
        # (stripping ".onnx" gives "resolution-head-v0.1", then stripping ".1" gives
        # "resolution-head-v0", which is wrong).
        manifest_path = out_path.parent / (out_path.stem + ".manifest.json")

    out_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.parent.mkdir(parents=True, exist_ok=True)

    # ------------------------------------------------------------------
    # Load ResolutionHead from checkpoint
    # ------------------------------------------------------------------
    from wayfinders.ml.training.head import ResolutionHead

    logger.info("Loading ResolutionHead from checkpoint: %s", checkpoint_path)
    head = ResolutionHead()
    raw_ckpt = torch.load(checkpoint_path, map_location="cuda", weights_only=True)
    # The train loop saves a dict with 'model_state_dict', 'config', 'metrics'.
    # Support both the wrapped dict format and a bare state_dict for flexibility.
    if isinstance(raw_ckpt, dict) and "model_state_dict" in raw_ckpt:
        state_dict = raw_ckpt["model_state_dict"]
        logger.info("Unwrapped model_state_dict from checkpoint dict")
    else:
        state_dict = raw_ckpt
    head.load_state_dict(state_dict)
    head.eval()
    head.cuda()
    # Freeze -- we are exporting, not training.
    for p in head.parameters():
        p.requires_grad_(False)

    logger.info(
        "ResolutionHead loaded. Parameter count: %d", sum(p.numel() for p in head.parameters())
    )

    wrapper = _HeadExportWrapper(head)
    wrapper.eval()

    # ------------------------------------------------------------------
    # Dummy inputs for tracing: batch=1, fp32 on CUDA
    # (The head is fp32 -- no half() here; see module docstring.)
    # ------------------------------------------------------------------
    dummy_char = torch.randn(1, EMBEDDING_DIM, device="cuda", dtype=torch.float32)
    dummy_action = torch.randn(1, EMBEDDING_DIM, device="cuda", dtype=torch.float32)
    dummy_context = torch.randn(1, EMBEDDING_DIM, device="cuda", dtype=torch.float32)

    # L2-normalise dummy inputs to match the runtime contract (encoder output is
    # always L2-normalised; the head receives normalised vectors).
    dummy_char = torch.nn.functional.normalize(dummy_char, p=2, dim=-1)
    dummy_action = torch.nn.functional.normalize(dummy_action, p=2, dim=-1)
    dummy_context = torch.nn.functional.normalize(dummy_context, p=2, dim=-1)

    logger.info("Exporting to %s (opset %d, %s)...", out_path, ONNX_OPSET, ONNX_DTYPE)

    with torch.no_grad():
        torch.onnx.export(
            wrapper,
            (dummy_char, dummy_action, dummy_context),
            str(out_path),
            input_names=["char_vec", "action_vec", "context_vec"],
            output_names=["delta"],
            dynamic_axes={
                "char_vec": {0: "batch"},
                "action_vec": {0: "batch"},
                "context_vec": {0: "batch"},
                "delta": {0: "batch"},
            },
            opset_version=ONNX_OPSET,
            do_constant_folding=True,
        )

    onnx_size = out_path.stat().st_size
    logger.info("ONNX export OK: %s (%.2f MB)", out_path, onnx_size / 1e6)

    # ------------------------------------------------------------------
    # SHA-256 of both the ONNX file and the source checkpoint
    # ------------------------------------------------------------------
    head_onnx_sha = _sha256_file(out_path)
    head_pt_sha = _sha256_file(checkpoint_path)

    # ------------------------------------------------------------------
    # Extract training metadata from metrics.json if present
    # ------------------------------------------------------------------
    training_run_id: str = ""
    val_mae: float = _V06_VAL_MAE
    test_mae: float = _V06_TEST_MAE
    r_squared_test: float = _V06_R_SQUARED_TEST

    if metrics_path is None:
        # Try default relative to repo root (two levels up from this file).
        repo_root = Path(__file__).resolve().parent.parent.parent.parent
        metrics_path = repo_root / DEFAULT_METRICS_PATH

    if metrics_path.is_file():
        try:
            metrics_data = json.loads(metrics_path.read_text(encoding="utf-8"))
            # Compose a run_id from seed + a stable token if no explicit id.
            config = metrics_data.get("config", {})
            seed = config.get("seed", 42)
            epochs = metrics_data.get("epochs_run", 100)
            # Derive version from output_dir stored in config if available.
            output_dir_str = str(config.get("output_dir", "v0.6"))
            ver = (
                "v0.6"
                if "v0.6" in output_dir_str
                else ("v0.1" if "v0.1" in output_dir_str else "vX")
            )
            training_run_id = f"{ver}-seed{seed}-ep{epochs}"
            val_mae_raw = metrics_data.get("best_val_mae")
            if val_mae_raw is not None:
                val_mae = float(val_mae_raw)
            logger.info(
                "Loaded training metadata: run_id=%s val_mae=%.4f", training_run_id, val_mae
            )
        except Exception as exc:
            logger.warning("Could not parse metrics.json: %s", exc)
    else:
        logger.warning("metrics.json not found at %s -- using fallback values", metrics_path)
        training_run_id = "v0.6-seed42-ep100"

    if test_metrics_path is None:
        repo_root = Path(__file__).resolve().parent.parent.parent.parent
        test_metrics_path = repo_root / DEFAULT_TEST_METRICS_PATH

    if test_metrics_path.is_file():
        try:
            test_data = json.loads(test_metrics_path.read_text(encoding="utf-8"))
            test_mae_raw = test_data.get("mae")
            r2_raw = test_data.get("r_squared")
            if test_mae_raw is not None:
                test_mae = float(test_mae_raw)
            if r2_raw is not None:
                r_squared_test = float(r2_raw)
        except Exception as exc:
            logger.warning("Could not parse test_metrics.json: %s", exc)

    # ------------------------------------------------------------------
    # Pull encoder_sha256 from the substep-11 encoder manifest for coupling trace
    # ------------------------------------------------------------------
    encoder_sha: str | None = None
    if encoder_manifest_path is None:
        repo_root = Path(__file__).resolve().parent.parent.parent.parent
        encoder_manifest_path = repo_root / DEFAULT_ENCODER_MANIFEST_PATH

    if encoder_manifest_path.is_file():
        try:
            enc_manifest = json.loads(encoder_manifest_path.read_text(encoding="utf-8"))
            encoder_sha = enc_manifest.get("onnx_sha256")
            logger.info("Pulled encoder onnx_sha256 from manifest: %s...", str(encoder_sha)[:16])
        except Exception as exc:
            logger.warning("Could not read encoder manifest: %s", exc)
    else:
        logger.warning(
            "Encoder manifest not found at %s -- encoder_sha256 will be null in head manifest",
            encoder_manifest_path,
        )

    # ------------------------------------------------------------------
    # Manifest sidecar
    # ------------------------------------------------------------------
    manifest: dict[str, object] = {
        "model_name": "wayfinders.ml.training.head.ResolutionHead",
        "version": "v0.6",
        "export_timestamp": datetime.now(UTC).isoformat(),
        "head_sha256": head_onnx_sha,
        "head_pt_sha256": head_pt_sha,
        "training_run_id": training_run_id,
        "val_mae": val_mae,
        "test_mae": test_mae,
        "r_squared_test": r_squared_test,
        "encoder_sha256": encoder_sha,
        "opset_version": ONNX_OPSET,
        "dtype": ONNX_DTYPE,
        "input_schema": [
            {
                "name": "char_vec",
                "dtype": "float32",
                "shape": ["batch", EMBEDDING_DIM],
                "notes": "L2-normalised character prose embedding from encoder",
            },
            {
                "name": "action_vec",
                "dtype": "float32",
                "shape": ["batch", EMBEDDING_DIM],
                "notes": "L2-normalised action prose embedding from encoder",
            },
            {
                "name": "context_vec",
                "dtype": "float32",
                "shape": ["batch", EMBEDDING_DIM],
                "notes": "L2-normalised context prose embedding from encoder",
            },
        ],
        "output_schema": [
            {
                "name": "delta",
                "dtype": "float32",
                "shape": ["batch", 1],
                "notes": "delta in [-5, +5], tanh*5 bounded in-graph; no client-side clipping needed",
            }
        ],
        "tanh_scaling": 5.0,
        "head_input_dim": HEAD_INPUT_DIM,
        "notes": (
            "cos(char_vec, action_vec) is computed inside the ONNX graph. "
            "Rune passes three 384-dim float32 vectors; the graph returns Δ directly."
        ),
    }

    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    logger.info("Manifest written: %s", manifest_path)
    logger.info(
        "head_sha256=%s..., head_pt_sha256=%s...",
        head_onnx_sha[:16],
        head_pt_sha[:16],
    )

    return out_path


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    import sys

    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")

    repo_root = Path(__file__).resolve().parent.parent.parent.parent
    checkpoint = repo_root / DEFAULT_CHECKPOINT_PATH
    out = repo_root / DEFAULT_OUT_PATH

    if len(sys.argv) > 1:
        checkpoint = Path(sys.argv[1])
    if len(sys.argv) > 2:
        out = Path(sys.argv[2])

    result = export_head(checkpoint, out)
    print(f"Exported: {result} ({result.stat().st_size / 1e6:.2f} MB)")
