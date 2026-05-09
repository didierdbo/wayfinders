"""UC1 inference predictor — ONNX Runtime singleton loaded at startup.

The predictor owns the lifecycle of the encoder and head ONNX sessions.
Both sessions are loaded once inside the FastAPI lifespan hook and stored
on ``app.state``.

Design decisions:

  - ONNX Runtime (ORT) is used for both encoder and head inference.
    This is the production inference path — identical to what the C# client
    (Rune's ApiClient) executes.  Using ORT here closes the Python/C# parity
    loop and eliminates the torch runtime as a hard dependency at serve time.

  - The encoder ONNX session (fp16, CPUExecutionProvider) takes tokenizer
    output (input_ids, attention_mask, token_type_ids) and returns fp16
    sentence_embedding (batch, 384).  The tokenizer is loaded from
    sentence-transformers (same library used during training/export) so
    tokenization is bit-identical to the export-time reference.

  - The head ONNX session (fp32, CPUExecutionProvider) takes three (1, 384)
    float32 vectors (char_vec, action_vec, context_vec) and returns delta (1, 1).

  - Degraded mode: if either ONNX file is missing or ORT fails to load it,
    the predictor starts in not-ready state and ``predict()`` raises
    ``PredictorNotReadyError``.  The ``/api/uc1/predict`` endpoint surfaces
    this as HTTP 503.  This allows the API to run during development before
    the first export has been run.

  - Thread safety: ORT sessions are stateless per-run and thread-safe for
    concurrent Run() calls with separate IO bindings.  No explicit locking
    is needed.

Configuration (environment variables):
  WAYFINDERS_ENCODER_ONNX  Path to minilm-l6-v2-fp16.onnx.
                           Default: artifacts/onnx/minilm-l6-v2-fp16.onnx
                           relative to repo root (auto-detected from file location).
  WAYFINDERS_HEAD_ONNX     Path to resolution-head-v0.6.onnx.
                           Default: artifacts/onnx/resolution-head-v0.6.onnx
                           relative to repo root.
  WAYFINDERS_HEAD_CKPT     Legacy env var (substeps 3-12 torch path). No longer
                           used by the predictor; kept in docs so old run scripts
                           surface a clear deprecation rather than silent mismatch.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

# Repo root: two levels up from this file (wayfinders/api/predictor.py).
_REPO_ROOT = Path(__file__).resolve().parent.parent.parent

# Default ONNX artifact paths (relative to repo root).
_DEFAULT_ENCODER_ONNX = _REPO_ROOT / "artifacts" / "onnx" / "minilm-l6-v2-fp16.onnx"
_DEFAULT_HEAD_ONNX = _REPO_ROOT / "artifacts" / "onnx" / "resolution-head-v0.6.onnx"

# Tokenizer config: max_length must match the export-time contract (substep 11).
_TOKENIZER_MAX_LENGTH = 256


class PredictorNotReadyError(Exception):
    """Raised when predict() is called but the model is not loaded.

    Surfaces as HTTP 503 at the endpoint layer.
    """


class Predictor:
    """Process-level UC1 inference singleton — ONNX Runtime backend.

    Both the encoder and head run as ORT InferenceSession objects.
    The tokenizer (sentence-transformers) is loaded once alongside the
    encoder session — it provides input_ids/attention_mask/token_type_ids
    for the fp16 encoder ONNX graph.

    Attributes:
        ready: True when both encoder and head sessions are loaded.
        device: Always "ort-cpu" for the ORT path (sessions run on CPUExecutionProvider).
        encoder_onnx_path: Resolved path to the encoder ONNX artifact.
        head_onnx_path: Resolved path to the head ONNX artifact.
    """

    def __init__(self) -> None:
        self._enc_sess: Any | None = None  # ort.InferenceSession | None
        self._head_sess: Any | None = None  # ort.InferenceSession | None
        self._tokenizer: Any | None = None  # PreTrainedTokenizer | None
        self.ready: bool = False

        # Resolved artifact paths (set during load; used by /api/uc1/info).
        self._encoder_onnx_path: Path = self._resolve_path(
            "WAYFINDERS_ENCODER_ONNX", _DEFAULT_ENCODER_ONNX
        )
        self._head_onnx_path: Path = self._resolve_path("WAYFINDERS_HEAD_ONNX", _DEFAULT_HEAD_ONNX)

        self._load_encoder()
        self._load_head()

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _resolve_path(env_var: str, default: Path) -> Path:
        """Return the path from *env_var* if set, otherwise *default*."""
        env = os.environ.get(env_var, "").strip()
        if env:
            logger.info("Predictor: %s overridden to %s", env_var, env)
            return Path(env)
        return default

    def _load_encoder(self) -> None:
        """Load the encoder ONNX session + tokenizer. Degrades gracefully if unavailable."""
        try:
            import onnxruntime as ort
        except ImportError:
            logger.warning(
                "Predictor: onnxruntime not installed — "
                "API starts in degraded mode (503 on /api/uc1/predict). "
                "Run: uv sync --all-extras --dev"
            )
            return

        if not self._encoder_onnx_path.is_file():
            logger.warning(
                "Predictor: encoder ONNX not found at %s — "
                "run `uv run python -m wayfinders.ml.export.export_encoder` first.",
                self._encoder_onnx_path,
            )
            return

        try:
            from sentence_transformers import SentenceTransformer

            # Load tokenizer only (no GPU; model weights not needed for tokenization).
            # sentence-transformers caches the model so this is fast on repeat startups.
            _st = SentenceTransformer(
                "sentence-transformers/all-MiniLM-L6-v2",
                device="cpu",
            )
            self._tokenizer = _st[0].tokenizer
            logger.info("Predictor: tokenizer loaded")
        except ImportError:
            logger.warning(
                "Predictor: sentence-transformers not installed — API starts in degraded mode."
            )
            return
        except Exception as exc:
            logger.error("Predictor: failed to load tokenizer: %s", exc)
            return

        try:
            self._enc_sess = ort.InferenceSession(
                str(self._encoder_onnx_path),
                providers=["CPUExecutionProvider"],
            )
            logger.info("Predictor: encoder ORT session ready (%s)", self._encoder_onnx_path)
        except Exception as exc:
            logger.error("Predictor: failed to create encoder ORT session: %s", exc)
            self._tokenizer = None

    def _load_head(self) -> None:
        """Load the head ONNX session. Degrades if file is missing or ORT unavailable."""
        if self._enc_sess is None:
            # No point loading the head if encoder failed.
            return

        try:
            import onnxruntime as ort
        except ImportError:
            return  # already logged in _load_encoder

        if not self._head_onnx_path.is_file():
            logger.warning(
                "Predictor: head ONNX not found at %s — "
                "run `uv run python -m wayfinders.ml.export.export_head` first.",
                self._head_onnx_path,
            )
            return

        try:
            self._head_sess = ort.InferenceSession(
                str(self._head_onnx_path),
                providers=["CPUExecutionProvider"],
            )
            logger.info("Predictor: head ORT session ready (%s)", self._head_onnx_path)
            self.ready = True
        except Exception as exc:
            logger.error("Predictor: failed to create head ORT session: %s", exc)

    @property
    def device(self) -> str:
        """Device descriptor for /api/uc1/info. ORT runs on CPU."""
        return "ort-cpu"

    @property
    def encoder_onnx_path(self) -> str:
        return str(self._encoder_onnx_path)

    @property
    def head_onnx_path(self) -> str:
        return str(self._head_onnx_path)

    # ------------------------------------------------------------------
    # Encoding helper
    # ------------------------------------------------------------------

    def _encode_ort(self, proses: list[str]) -> np.ndarray:
        """Tokenize *proses* and run the encoder ONNX session.

        Returns float32 ndarray of shape (N, 384) — upcast from fp16 output.

        Args:
            proses: Non-empty list of English prose strings.

        Precondition: _tokenizer and _enc_sess are not None (caller must guard).
        """
        # Narrow away None so mypy can see the concrete Any type for the call sites.
        assert self._tokenizer is not None, "_encode_ort called with tokenizer=None"
        assert self._enc_sess is not None, "_encode_ort called with enc_sess=None"

        enc_inputs = self._tokenizer(
            proses,
            return_tensors="pt",
            padding=True,
            truncation=True,
            max_length=_TOKENIZER_MAX_LENGTH,
        )
        raw: np.ndarray = self._enc_sess.run(
            ["sentence_embedding"],
            {
                "input_ids": enc_inputs["input_ids"].numpy(),
                "attention_mask": enc_inputs["attention_mask"].numpy(),
                "token_type_ids": enc_inputs["token_type_ids"].numpy(),
            },
        )[0]
        # Encoder output is fp16; upcast to fp32 before feeding the head.
        return raw.astype(np.float32)  # (N, 384)

    # ------------------------------------------------------------------
    # Public inference API
    # ------------------------------------------------------------------

    def predict(
        self,
        char_prose: str,
        action_prose: str,
        context_prose: str,
    ) -> float:
        """Run UC1 inference and return delta in [-5, +5].

        Args:
            char_prose:    EN-rendered character prose (render_character output).
            action_prose:  EN-rendered action prose (render_action output).
            context_prose: EN-rendered context prose (render_context output).

        Returns:
            delta: float in [-5, +5] (tanh*5 bounded inside the head ONNX graph).

        Raises:
            PredictorNotReadyError: if encoder or head sessions are not loaded.
            ValueError: if any prose string is empty.
        """
        if self._enc_sess is None or self._tokenizer is None:
            raise PredictorNotReadyError(
                "Encoder ONNX session is not loaded. "
                "Ensure onnxruntime and sentence-transformers are installed "
                "and WAYFINDERS_ENCODER_ONNX points to a valid .onnx file."
            )
        if self._head_sess is None:
            raise PredictorNotReadyError(
                "Head ONNX session is not loaded. "
                "Ensure WAYFINDERS_HEAD_ONNX points to a valid .onnx file."
            )

        for label, prose in (
            ("char_prose", char_prose),
            ("action_prose", action_prose),
            ("context_prose", context_prose),
        ):
            if not prose or not prose.strip():
                raise ValueError(f"predict(): {label} must be a non-empty string")

        # Encode all three proses in a single ORT call (batch=3).
        embs = self._encode_ort([char_prose, action_prose, context_prose])  # (3, 384) float32

        char_np = embs[0:1]  # (1, 384)
        action_np = embs[1:2]  # (1, 384)
        ctx_np = embs[2:3]  # (1, 384)

        # Head ONNX session: inputs are three (1, 384) float32 vectors;
        # output is delta (1, 1) float32, already tanh*5-bounded.
        delta_raw: np.ndarray = self._head_sess.run(
            ["delta"],
            {
                "char_vec": char_np,
                "action_vec": action_np,
                "context_vec": ctx_np,
            },
        )[0]  # (1, 1) float32

        return float(delta_raw.ravel()[0])
