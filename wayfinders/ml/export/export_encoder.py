"""ONNX export for the frozen MiniLM encoder (substep 11).

Exports ``sentence-transformers/all-MiniLM-L6-v2`` as a self-contained ONNX
graph that performs mean-pooling + L2-normalisation internally, so the Rune C#
client needs zero post-processing on the embedding output.

Design choice -- ``torch.onnx.export`` over ``optimum.exporters.onnx``:
  The ONNX Export Contract (§1.1) explicitly states the tokenizer is NOT in
  the ONNX graph.  The main added value of ``optimum`` is bundling tokenizer
  pre-processing in the graph; without that, it is a heavy optional dependency
  that adds no functional benefit here.  ``torch.onnx.export`` with a thin
  ``_MiniLMExportWrapper`` module is simpler, traceable, and sufficient.
  If opset constraints or operator coverage gaps emerge in M2 (e.g. for the
  head export), revisit ``optimum``.

CUDA fp16 contract:
  The wrapper is exported from a CUDA fp16 forward pass.  If CUDA is not
  available at export time this function raises ``RuntimeError`` immediately
  rather than silently falling back to CPU -- a CPU-exported fp16 graph would
  produce different numerical behaviour than the CUDA-trained encoder.

Output contract (matches Coda - ONNX Export Contract ss1.1):
  Inputs:  input_ids (int64, batch x seq), attention_mask (int64, batch x seq),
           token_type_ids (int64, batch x seq) -- all dynamic on both dims.
  Output:  sentence_embedding (float16, batch x 384) -- mean-pooled, L2-normed.
  Opset:   17.

Manifest sidecar:
  Written alongside the .onnx file. Contains model_id, export_timestamp,
  encoder_sha256 (from local HuggingFace cache), onnx_sha256, opset_version,
  dtype, input_schema, output_schema, tokenizer_config_path.
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
import torch.nn.functional as F  # noqa: N812 -- PyTorch canonical alias

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Constants (mirrors encoder.py -- kept explicit to avoid circular import)
# ---------------------------------------------------------------------------

ENCODER_MODEL_ID: Final[str] = "sentence-transformers/all-MiniLM-L6-v2"
ONNX_OPSET: Final[int] = 17
ONNX_DTYPE: Final[str] = "fp16"
EMBEDDING_DIM: Final[int] = 384
MAX_SEQ_LEN: Final[int] = 256

# ---------------------------------------------------------------------------
# Export wrapper (not exported publicly -- used only during export)
# ---------------------------------------------------------------------------


class _MiniLMExportWrapper(nn.Module):
    """Thin wrapper that adds mean-pooling + L2-norm on top of BertModel.

    Designed for ``torch.onnx.export``.  The graph contains:
      1. BertModel forward (tokenizer pre-processing is client-side, not here).
      2. Mask-aware mean-pool over token embeddings.
      3. L2-normalisation to unit norm.

    All operations run in the dtype of the model weights (fp16 on CUDA).
    The mask cast uses ``to(token_embs.dtype)`` rather than a hardcoded dtype
    so the trace is dtype-agnostic and the same wrapper can be used for a
    fp32 reference export if needed.
    """

    def __init__(self, bert_model: nn.Module) -> None:
        super().__init__()
        self.bert = bert_model

    def forward(
        self,
        input_ids: torch.Tensor,
        attention_mask: torch.Tensor,
        token_type_ids: torch.Tensor,
    ) -> torch.Tensor:
        """
        Args:
            input_ids:      (batch, seq_len) int64
            attention_mask: (batch, seq_len) int64 -- 1 = real, 0 = padding
            token_type_ids: (batch, seq_len) int64 -- all zeros for single-sentence

        Returns:
            sentence_embedding: (batch, 384) float16 -- L2-normalised
        """
        out = self.bert(
            input_ids=input_ids,
            attention_mask=attention_mask,
            token_type_ids=token_type_ids,
        )
        # out.last_hidden_state: (batch, seq_len, 384)
        token_embs: torch.Tensor = out.last_hidden_state

        # Mask-aware mean pool -- expand mask to embedding dim, cast to model dtype.
        mask = attention_mask.unsqueeze(-1).to(token_embs.dtype)  # (batch, seq_len, 1)
        summed = (token_embs * mask).sum(dim=1)  # (batch, 384)
        count = mask.sum(dim=1).clamp(min=1e-9)  # (batch, 1)
        mean_emb = summed / count  # (batch, 384)

        # L2-normalise to unit sphere (matching sentence-transformers' normalize_embeddings=True).
        return F.normalize(mean_emb, p=2, dim=-1)  # (batch, 384)


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


def _resolve_encoder_weights_path() -> Path | None:
    """Return the local path of model.safetensors in the HuggingFace cache.

    Resolution strategy:
      1. Use ``huggingface_hub.constants.HF_HUB_CACHE`` to find the cache root.
      2. Walk ``models--sentence-transformers--all-MiniLM-L6-v2/snapshots/``
         and return the first ``model.safetensors`` found.

    This avoids relying on ``bert.name_or_path`` which returns the model-ID
    string (``"sentence-transformers/all-MiniLM-L6-v2"``) rather than a local
    path in sentence-transformers v3+.  Flagged as the substep-7 regression;
    this is the M1.11 fix.

    Returns:
        Path to model.safetensors, or None if the cache layout is unexpected.
    """
    try:
        import huggingface_hub

        cache_root = Path(huggingface_hub.constants.HF_HUB_CACHE)
        model_folder = cache_root / "models--sentence-transformers--all-MiniLM-L6-v2"
        snapshots_dir = model_folder / "snapshots"
        if not snapshots_dir.is_dir():
            logger.warning("HF cache snapshots dir not found: %s", snapshots_dir)
            return None
        for snapshot in snapshots_dir.iterdir():
            candidate = snapshot / "model.safetensors"
            if candidate.is_file():
                return candidate
    except Exception as exc:  # pragma: no cover -- defensive
        logger.warning("Could not resolve encoder weights path: %s", exc)
    return None


# ---------------------------------------------------------------------------
# Public export function
# ---------------------------------------------------------------------------


def export_encoder(
    out_path: Path,
    *,
    manifest_path: Path | None = None,
) -> Path:
    """Export the frozen MiniLM encoder to ONNX (fp16, opset 17).

    Args:
        out_path:      Destination path for the .onnx file.
                       Parent directories are created if needed.
        manifest_path: Optional path for the manifest JSON sidecar.
                       Defaults to ``out_path.with_suffix('.manifest.json')``.

    Returns:
        out_path (for convenience in scripts).

    Raises:
        RuntimeError: if CUDA is not available (fp16 CUDA contract violated).

    Side effects:
        - Writes the .onnx file to *out_path*.
        - Writes the manifest JSON sidecar to *manifest_path*.
        - Logs progress at INFO level.
    """
    if not torch.cuda.is_available():
        raise RuntimeError(
            "export_encoder() requires CUDA. "
            "fp16 ONNX must be exported from a CUDA fp16 forward pass; "
            "silent CPU fallback would violate the numerical contract."
        )

    if manifest_path is None:
        manifest_path = out_path.with_suffix("").with_suffix(".manifest.json")
        # e.g. minilm-l6-v2-fp16.manifest.json alongside the .onnx

    out_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.parent.mkdir(parents=True, exist_ok=True)

    logger.info("Loading %s for ONNX export (CUDA fp16)...", ENCODER_MODEL_ID)

    # Deferred import: sentence-transformers is an optional extra.
    try:
        from sentence_transformers import SentenceTransformer
    except ImportError as exc:  # pragma: no cover
        raise ImportError(
            "sentence-transformers is required for export_encoder(). uv sync --all-extras --dev"
        ) from exc

    st_model = SentenceTransformer(ENCODER_MODEL_ID, device="cuda")
    st_model.eval()
    for p in st_model.parameters():
        p.requires_grad_(False)
    st_model.half()

    transformer_module = st_model[0]
    bert = transformer_module.auto_model
    bert.eval()

    wrapper = _MiniLMExportWrapper(bert)
    wrapper.eval()

    # Dummy inputs for tracing -- batch=1, seq_len=13 (short example).
    tokenizer = transformer_module.tokenizer
    dummy_texts = ["Kira sneaks across loose scree under starlight."]
    dummy_inputs = tokenizer(
        dummy_texts,
        return_tensors="pt",
        padding=True,
        truncation=True,
        max_length=MAX_SEQ_LEN,
    )
    input_ids = dummy_inputs["input_ids"].cuda()
    attention_mask = dummy_inputs["attention_mask"].cuda()
    token_type_ids = dummy_inputs["token_type_ids"].cuda()

    logger.info("Exporting to %s (opset %d, fp16)...", out_path, ONNX_OPSET)

    with torch.no_grad():
        torch.onnx.export(
            wrapper,
            (input_ids, attention_mask, token_type_ids),
            str(out_path),
            input_names=["input_ids", "attention_mask", "token_type_ids"],
            output_names=["sentence_embedding"],
            dynamic_axes={
                "input_ids": {0: "batch", 1: "seq_len"},
                "attention_mask": {0: "batch", 1: "seq_len"},
                "token_type_ids": {0: "batch", 1: "seq_len"},
                "sentence_embedding": {0: "batch"},
            },
            opset_version=ONNX_OPSET,
            do_constant_folding=True,
        )

    onnx_size = out_path.stat().st_size
    logger.info("ONNX export OK: %s (%.1f MB)", out_path, onnx_size / 1e6)

    # ------------------------------------------------------------------
    # Manifest sidecar
    # ------------------------------------------------------------------

    encoder_sha: str | None = None
    weights_path = _resolve_encoder_weights_path()
    if weights_path is not None:
        logger.info("Hashing encoder weights: %s", weights_path)
        encoder_sha = _sha256_file(weights_path)
    else:
        logger.warning(
            "encoder_sha256: weights file not found in HF cache -- "
            "manifest will contain null. Flag for M2."
        )

    onnx_sha = _sha256_file(out_path)

    # Tokenizer path relative to the snapshot directory (for Rune).
    tokenizer_rel_path: str | None = None
    if weights_path is not None:
        tok_path = weights_path.parent / "tokenizer.json"
        if tok_path.is_file():
            tokenizer_rel_path = str(tok_path).replace("\\", "/")

    manifest: dict[str, object] = {
        "model_name": ENCODER_MODEL_ID,
        "version": "v0.1",
        "export_timestamp": datetime.now(UTC).isoformat(),
        "encoder_sha256": encoder_sha,
        "onnx_sha256": onnx_sha,
        "opset_version": ONNX_OPSET,
        "dtype": ONNX_DTYPE,
        "input_schema": [
            {"name": "input_ids", "dtype": "int64", "shape": ["batch", "seq_len"]},
            {"name": "attention_mask", "dtype": "int64", "shape": ["batch", "seq_len"]},
            {"name": "token_type_ids", "dtype": "int64", "shape": ["batch", "seq_len"]},
        ],
        "output_schema": [
            {
                "name": "sentence_embedding",
                "dtype": "float16",
                "shape": ["batch", EMBEDDING_DIM],
                "notes": "mean-pooled, L2-normalised; norm ≈ 1.0",
            }
        ],
        "tokenizer_config_path": tokenizer_rel_path,
    }

    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    logger.info("Manifest written: %s", manifest_path)

    return out_path


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    import sys

    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")

    out = Path("artifacts/onnx/minilm-l6-v2-fp16.onnx")
    if len(sys.argv) > 1:
        out = Path(sys.argv[1])

    result = export_encoder(out)
    print(f"Exported: {result} ({result.stat().st_size / 1e6:.1f} MB)")
