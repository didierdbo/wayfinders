"""Parity verification: torch MiniLMEncoder vs ONNX Runtime inference.

The parity gate (per ONNX Export Contract §6) is:
  max_abs_diff < 1e-3 over 100 real prose documents.

This module is also used at test time (integration test layer) on a 20-prose
sub-sample to keep CI fast.

Public API::

    result = verify_encoder_parity(
        torch_encoder=enc,
        onnx_path=Path("artifacts/onnx/minilm-l6-v2-fp16.onnx"),
        prose_docs=list_of_100_strings,
        atol=1e-3,
    )
    assert result.max_abs_diff < 1e-3
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from pathlib import Path
from typing import Protocol

import numpy as np
import torch

logger = logging.getLogger(__name__)


class _EncoderProtocol(Protocol):
    """Structural type for objects accepted as ``torch_encoder``.

    Any object with an ``encode_batch(list[str]) -> torch.Tensor`` method
    satisfies this protocol -- includes ``MiniLMEncoder`` and test stubs.
    """

    def encode_batch(self, proses: list[str]) -> torch.Tensor: ...


@dataclass(frozen=True)
class ParityResult:
    """Summary of a torch-vs-onnxruntime parity check.

    Attributes:
        n_docs:          Number of prose documents tested.
        max_abs_diff:    Maximum per-element absolute difference over all docs.
        mean_abs_diff:   Mean per-element absolute difference.
        cosine_similarity_min: Minimum cosine similarity between torch and ORT
                         outputs (1.0 = identical direction, lower = diverging).
        n_violations:    Count of documents whose *any* element exceeded *atol*.
        atol:            The tolerance used for counting violations.
        passed:          True iff n_violations == 0 (all docs within atol).
    """

    n_docs: int
    max_abs_diff: float
    mean_abs_diff: float
    cosine_similarity_min: float
    n_violations: int
    atol: float
    passed: bool


def verify_encoder_parity(
    torch_encoder: _EncoderProtocol,
    onnx_path: Path,
    prose_docs: list[str],
    atol: float = 1e-3,
) -> ParityResult:
    """Compare torch MiniLMEncoder output vs onnxruntime on every prose doc.

    Args:
        torch_encoder: A ``MiniLMEncoder`` instance (or any object with an
                       ``encode_batch(list[str]) -> torch.Tensor`` method).
        onnx_path:     Path to the exported ``.onnx`` file.
        prose_docs:    List of English prose strings. Non-empty, each non-empty.
        atol:          Per-element absolute tolerance for violation counting.
                       Default 1e-3 matches the ONNX Export Contract §6 gate.

    Returns:
        ``ParityResult`` with per-element diff statistics.

    Raises:
        ValueError:  if *prose_docs* is empty.
        RuntimeError: if the ONNX file does not exist.
        ImportError: if onnxruntime is not installed.
    """
    if not prose_docs:
        raise ValueError("prose_docs must not be empty")
    if not onnx_path.is_file():
        raise RuntimeError(f"ONNX file not found: {onnx_path}")

    try:
        import onnxruntime as ort
    except ImportError as exc:  # pragma: no cover
        raise ImportError(
            "onnxruntime is required for verify_encoder_parity(). uv sync --all-extras --dev"
        ) from exc

    try:
        from sentence_transformers import SentenceTransformer
    except ImportError as exc:  # pragma: no cover
        raise ImportError(
            "sentence-transformers is required for verify_encoder_parity(). "
            "uv sync --all-extras --dev"
        ) from exc

    logger.info("Parity check: %d docs, atol=%g, onnx=%s", len(prose_docs), atol, onnx_path)

    # ------------------------------------------------------------------
    # Torch reference embeddings
    # ------------------------------------------------------------------
    # torch_encoder.encode_batch() returns (N, 384) tensor, fp16 or fp32.
    with torch.no_grad():
        torch_embs: torch.Tensor = torch_encoder.encode_batch(prose_docs)
    torch_np = torch_embs.float().cpu().numpy()  # (N, 384) float32

    # ------------------------------------------------------------------
    # ONNX Runtime embeddings
    # ------------------------------------------------------------------
    # We need to tokenize the same way the encoder's underlying tokenizer does.
    # Re-use the sentence-transformers tokenizer for reference (this is exactly
    # what the C# client must replicate -- see tokenizer contract in the doc).
    st_model = SentenceTransformer(
        "sentence-transformers/all-MiniLM-L6-v2",
        device="cpu",
    )
    tokenizer = st_model[0].tokenizer

    sess = ort.InferenceSession(
        str(onnx_path),
        providers=["CPUExecutionProvider"],
    )

    ort_rows: list[np.ndarray] = []
    # Process in batches of 16 to stay memory-friendly.
    batch_size = 16
    for start in range(0, len(prose_docs), batch_size):
        batch_texts = prose_docs[start : start + batch_size]
        enc_inputs = tokenizer(
            batch_texts,
            return_tensors="pt",
            padding=True,
            truncation=True,
            max_length=256,
        )
        ort_batch = sess.run(
            ["sentence_embedding"],
            {
                "input_ids": enc_inputs["input_ids"].numpy(),
                "attention_mask": enc_inputs["attention_mask"].numpy(),
                "token_type_ids": enc_inputs["token_type_ids"].numpy(),
            },
        )[0]  # (batch, 384) float16
        ort_rows.append(ort_batch.astype(np.float32))

    ort_np = np.concatenate(ort_rows, axis=0)  # (N, 384) float32

    # ------------------------------------------------------------------
    # Statistics
    # ------------------------------------------------------------------
    diff = np.abs(torch_np - ort_np)  # (N, 384)
    max_diff = float(diff.max())
    mean_diff = float(diff.mean())

    # Per-doc cosine similarity between torch and ORT output vectors.
    # Both are already L2-normalised, so cos = dot product.
    cos_per_doc = (torch_np * ort_np).sum(axis=-1)  # (N,)
    cos_min = float(cos_per_doc.min())

    # Count docs where any element exceeds atol.
    per_doc_max = diff.max(axis=-1)  # (N,)
    n_violations = int((per_doc_max > atol).sum())

    result = ParityResult(
        n_docs=len(prose_docs),
        max_abs_diff=max_diff,
        mean_abs_diff=mean_diff,
        cosine_similarity_min=cos_min,
        n_violations=n_violations,
        atol=atol,
        passed=(n_violations == 0),
    )

    level = logging.INFO if result.passed else logging.ERROR
    logger.log(
        level,
        "Parity result: max_abs_diff=%.6f mean=%.6f cos_min=%.6f violations=%d/%d passed=%s",
        max_diff,
        mean_diff,
        cos_min,
        n_violations,
        len(prose_docs),
        result.passed,
    )

    return result
