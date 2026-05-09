"""UC1 inference predictor — single process-level singleton loaded at startup.

The predictor owns the lifecycle of the MiniLMEncoder and ResolutionHead. It is
constructed once inside the FastAPI lifespan hook and stored on ``app.state``.

Design decisions:

  - The encoder is loaded unconditionally (it is always needed and is pure read).
    If sentence-transformers is not installed, the service starts in degraded mode
    and ``predict()`` raises ``PredictorNotReadyError``.

  - The ResolutionHead checkpoint is optional at startup. If the checkpoint path
    is absent or not configured, the predictor enters ``PredictorNotReadyError`` state
    and the ``/api/uc1/predict`` endpoint returns HTTP 503. This allows the API to
    run during development before the first training run completes.

  - Device selection: auto (CUDA if available, else CPU). Can be overridden via
    the ``WAYFINDERS_DEVICE`` environment variable for reproducible dev runs.

  - No per-request encoder re-loading. Embeddings are computed fresh per request
    (no request-level cache) — that is the training server's job. A future
    substep may add an embedding cache keyed on hash(prose).

  - Thread safety: FastAPI uses an async event loop with sync endpoints offloaded
    to a thread pool. ``predict()`` is synchronous and releases the GIL through
    PyTorch; this is safe for concurrent requests. No explicit locking needed for
    a read-only model.

Configuration (environment variables):
  WAYFINDERS_DEVICE       "cpu" | "cuda" | "cuda:0" etc.  Default: auto.
  WAYFINDERS_HEAD_CKPT    Path to a .pt checkpoint file.   Default: unset (503 mode).
"""

from __future__ import annotations

import logging
import os
from pathlib import Path

import torch

logger = logging.getLogger(__name__)


class PredictorNotReadyError(Exception):
    """Raised when predict() is called but the model is not loaded.

    Surfaces as HTTP 503 at the endpoint layer.
    """


class Predictor:
    """Process-level UC1 inference singleton.

    Attributes:
        ready: True when both encoder and head are loaded and predict() is safe to call.
        device: Device string the models are running on.
    """

    def __init__(self) -> None:
        self._encoder: object | None = None  # MiniLMEncoder | None
        self._head: object | None = None  # ResolutionHead | None
        self._device: str = self._resolve_device()
        self.ready: bool = False

        self._load_encoder()
        self._load_head()

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _resolve_device() -> str:
        env = os.environ.get("WAYFINDERS_DEVICE", "").strip()
        if env:
            logger.info("Predictor: device forced by WAYFINDERS_DEVICE=%s", env)
            return env
        device = "cuda" if torch.cuda.is_available() else "cpu"
        logger.info("Predictor: auto device=%s", device)
        return device

    def _load_encoder(self) -> None:
        """Load the frozen MiniLM encoder. Degrades gracefully if unavailable."""
        try:
            from wayfinders.ml.inference.encoder import MiniLMEncoder

            self._encoder = MiniLMEncoder(device=self._device)
            logger.info("Predictor: MiniLMEncoder ready on %s", self._device)
        except ImportError:
            logger.warning(
                "Predictor: sentence-transformers not installed — "
                "API starts in degraded mode (503 on /api/uc1/predict)."
            )
        except Exception as exc:
            logger.error("Predictor: failed to load encoder: %s", exc)

    def _load_head(self) -> None:
        """Load the ResolutionHead from checkpoint. Degrades if checkpoint missing."""
        ckpt_env = os.environ.get("WAYFINDERS_HEAD_CKPT", "").strip()
        if not ckpt_env:
            logger.info(
                "Predictor: WAYFINDERS_HEAD_CKPT not set — "
                "head not loaded (predict() will raise PredictorNotReadyError)."
            )
            return

        ckpt_path = Path(ckpt_env)
        if not ckpt_path.is_file():
            logger.warning("Predictor: checkpoint not found at %s — head not loaded.", ckpt_path)
            return

        try:
            from wayfinders.ml.training.train import load_checkpoint

            head = load_checkpoint(ckpt_path, device=self._device)
            head.eval()
            for p in head.parameters():
                p.requires_grad_(False)
            self._head = head
            logger.info("Predictor: ResolutionHead loaded from %s on %s", ckpt_path, self._device)
            self.ready = True
        except Exception as exc:
            logger.error("Predictor: failed to load head checkpoint %s: %s", ckpt_path, exc)

    @property
    def device(self) -> str:
        return self._device

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
            delta: float in [-5, +5].

        Raises:
            PredictorNotReadyError: if encoder or head are not loaded.
            ValueError: if any prose string is empty.
        """
        if self._encoder is None:
            raise PredictorNotReadyError(
                "MiniLMEncoder is not loaded. "
                "Ensure sentence-transformers is installed (uv sync --all-extras --dev)."
            )
        if self._head is None:
            raise PredictorNotReadyError(
                "ResolutionHead checkpoint is not loaded. "
                "Set WAYFINDERS_HEAD_CKPT to a valid checkpoint path."
            )

        for label, prose in (
            ("char_prose", char_prose),
            ("action_prose", action_prose),
            ("context_prose", context_prose),
        ):
            if not prose or not prose.strip():
                raise ValueError(f"predict(): {label} must be a non-empty string")

        # Type-ignore: the dynamic import means mypy sees object; the runtime
        # types are MiniLMEncoder and ResolutionHead which are duck-compatible.
        from wayfinders.ml.inference.encoder import MiniLMEncoder
        from wayfinders.ml.training.head import ResolutionHead

        encoder: MiniLMEncoder = self._encoder  # type: ignore[assignment]
        head: ResolutionHead = self._head  # type: ignore[assignment]

        with torch.no_grad():
            # Encode. encode_batch returns shape (N, 384) on self._device.
            vecs = encoder.encode_batch([char_prose, action_prose, context_prose])

            # vecs dtype is fp16 on CUDA, fp32 on CPU.
            # The head is always fp32 — upcast if needed.
            if vecs.dtype != torch.float32:
                vecs = vecs.float()

            char_vec = vecs[0].unsqueeze(0)  # (1, 384)
            action_vec = vecs[1].unsqueeze(0)  # (1, 384)
            context_vec = vecs[2].unsqueeze(0)  # (1, 384)

            delta_tensor = head(char_vec, action_vec, context_vec)  # (1, 1)

        return float(delta_tensor.squeeze().item())
