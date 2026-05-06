"""Frozen MiniLM encoder wrapper.

Loads ``sentence-transformers/all-MiniLM-L6-v2`` once, freezes every
parameter, and exposes a clean encode API for the UC1 inference pipeline.

Encoder lock (2026-05-05):
  Model: sentence-transformers/all-MiniLM-L6-v2
  Frozen: yes (requires_grad=False, eval mode)
  Precision: fp16 on CUDA, fp32 on CPU (auto-detected)
  Output: L2-normalised 384-dim vector per prose document

Hard invariants (asserted at construction time):
  1. Every parameter has requires_grad=False.
  2. The underlying module is in eval() mode.
  If sentence-transformers ever breaks these (e.g. a new version that sets
  requires_grad=True on load), we want immediate failure, not silent gradient
  leakage into a frozen encoder.

The ``weights_sha256`` property exposes the SHA-256 hash of the primary
weights file (model.safetensors or pytorch_model.bin) for the manifest
sidecar written during ONNX export (substep 12).
"""

from __future__ import annotations

import hashlib
import logging
from pathlib import Path
from typing import Final

import torch

logger = logging.getLogger(__name__)

# Encoder lock -- do not change without updating Pax - ML Encoder Lock
# 2026-05-05.md and coordinating with Rune (ONNX artifact).
ENCODER_MODEL_ID: Final[str] = "sentence-transformers/all-MiniLM-L6-v2"
ENCODER_EMBEDDING_DIM: Final[int] = 384


def _sha256_file(path: Path) -> str:
    """Return the hex SHA-256 digest of the file at *path*."""
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def _resolve_weights_from_hf_cache() -> Path | None:
    """Return the model.safetensors path from the HuggingFace local cache.

    Resolves via ``huggingface_hub.constants.HF_HUB_CACHE`` so it works even
    when ``sentence-transformers v3+`` sets ``auto_model.name_or_path`` to the
    model-ID string rather than a local directory path.  Flagged as a bug in
    substep 7; fixed in M1.11.
    """
    try:
        import huggingface_hub

        cache_root = Path(huggingface_hub.constants.HF_HUB_CACHE)
        model_folder = cache_root / "models--sentence-transformers--all-MiniLM-L6-v2"
        snapshots_dir = model_folder / "snapshots"
        if not snapshots_dir.is_dir():
            return None
        for snapshot in snapshots_dir.iterdir():
            candidate = snapshot / "model.safetensors"
            if candidate.is_file():
                return candidate
    except Exception:  # pragma: no cover -- defensive
        pass
    return None


def _find_weights_file(model_dir: str | Path) -> Path | None:
    """Return the primary weights file inside *model_dir*, or None.

    Prefers .safetensors over .bin (HuggingFace default for recent models).
    Only looks at top-level files; sub-directories (e.g. tokenizer/ in some
    sentence-transformer repos) are skipped.
    """
    base = Path(model_dir)
    for name in ("model.safetensors", "pytorch_model.bin"):
        candidate = base / name
        if candidate.is_file():
            return candidate
    return None


class MiniLMEncoder:
    """Frozen sentence encoder wrapping ``sentence-transformers/all-MiniLM-L6-v2``.

    The encoder is loaded once at construction time.  All model parameters
    are frozen (requires_grad=False) and the module is kept in eval() mode
    for the entire lifetime of this object.  No gradient state is accumulated.

    Precision:
        CUDA available  →  fp16 (both the model and output tensors)
        CPU only        →  fp32

    Usage::

        enc = MiniLMEncoder()
        vec = enc.encode("Kira sneaks across loose scree under starlight.")
        # vec.shape == (384,), L2-normalised, on the encoder's device
        batch = enc.encode_batch(["prose 1", "prose 2"])
        # batch.shape == (2, 384)
    """

    def __init__(self, device: str | None = None) -> None:
        """Load and freeze the encoder.

        Args:
            device: Force a specific device string (``"cpu"``, ``"cuda"``).
                    Default ``None`` auto-detects: CUDA if available, else CPU.
        """
        # Deferred import: sentence-transformers is an optional extra.
        # Import error surfaces at construction time with a clear message.
        try:
            from sentence_transformers import SentenceTransformer
        except ImportError as exc:  # pragma: no cover -- integration path
            raise ImportError(
                "sentence-transformers is required for MiniLMEncoder. "
                "Install the ml extra: uv sync --all-extras --dev"
            ) from exc

        if device is None:
            device = "cuda" if torch.cuda.is_available() else "cpu"
        self._device: str = device
        self._use_fp16: bool = device.startswith("cuda")

        logger.info(
            "Loading %s on %s (fp16=%s)",
            ENCODER_MODEL_ID,
            device,
            self._use_fp16,
        )

        self._model: SentenceTransformer = SentenceTransformer(
            ENCODER_MODEL_ID,
            device=device,
        )

        # Freeze: no gradients, eval mode.
        self._model.eval()
        for param in self._model.parameters():
            param.requires_grad_(False)

        if self._use_fp16:
            self._model.half()

        # Hard asserts: if the model violates the frozen invariant, fail
        # loudly rather than silently corrupting the frozen encoder state.
        n_grad = sum(1 for p in self._model.parameters() if p.requires_grad)
        if n_grad > 0:
            raise RuntimeError(
                f"MiniLMEncoder: {n_grad} parameter(s) still have requires_grad=True "
                "after freezing. The encoder lock invariant is violated."
            )
        if self._model.training:
            raise RuntimeError(
                "MiniLMEncoder: underlying module is in training mode after .eval(). "
                "The encoder lock invariant is violated."
            )

        # Locate weights file for SHA-256 manifest (may be None if the model
        # lives inside a non-standard directory layout).
        #
        # M1.11 fix: sentence-transformers v3+ sets `auto_model.name_or_path`
        # to the model-ID string ("sentence-transformers/all-MiniLM-L6-v2"),
        # not a local path, so `Path(raw_path).is_dir()` is always False.
        # Resolution: walk the HuggingFace cache via huggingface_hub directly.
        self._weights_path: Path | None = _resolve_weights_from_hf_cache()
        if self._weights_path is None:
            # Secondary fallback: try the name_or_path attribute in case the
            # model was loaded from a local directory rather than the HF cache.
            try:
                first_module = next(iter(self._model.modules()))
                raw_path: str | None = getattr(
                    getattr(first_module, "auto_model", None),
                    "name_or_path",
                    None,
                )
                if raw_path and Path(raw_path).is_dir():
                    self._weights_path = _find_weights_file(raw_path)
            except StopIteration:  # pragma: no cover -- defensive
                pass

        logger.info(
            "MiniLMEncoder ready. device=%s fp16=%s weights=%s",
            self._device,
            self._use_fp16,
            self._weights_path,
        )

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    @property
    def device(self) -> str:
        """Device string the encoder is running on (``"cpu"`` or ``"cuda"``)."""
        return self._device

    @property
    def use_fp16(self) -> bool:
        """True if the encoder is running in fp16 (CUDA path)."""
        return self._use_fp16

    @property
    def weights_sha256(self) -> str | None:
        """SHA-256 hex digest of the primary weights file, or None if not found.

        Used to populate the manifest sidecar during ONNX export (substep 12).
        """
        if self._weights_path is None:
            return None
        return _sha256_file(self._weights_path)

    def encode(self, prose: str) -> torch.Tensor:
        """Encode a single prose document.

        Args:
            prose: English prose string (EN-pinned renderer output).

        Returns:
            Tensor of shape ``(384,)`` on ``self.device``.
            L2-normalised, dtype matches ``use_fp16`` (float16 or float32).

        Raises:
            ValueError: if *prose* is empty.
        """
        if not prose:
            raise ValueError("encode() received an empty string")
        batch = self.encode_batch([prose])
        return batch[0]

    def encode_batch(self, proses: list[str]) -> torch.Tensor:
        """Encode a batch of prose documents.

        Args:
            proses: List of English prose strings.  Must be non-empty.

        Returns:
            Tensor of shape ``(N, 384)`` on ``self.device``.
            L2-normalised rows, dtype matches ``use_fp16``.

        Raises:
            ValueError: if *proses* is empty or contains an empty string.
        """
        if not proses:
            raise ValueError("encode_batch() received an empty list")
        for i, s in enumerate(proses):
            if not s:
                raise ValueError(f"encode_batch(): proses[{i}] is empty")

        with torch.no_grad():
            # sentence-transformers encode() with convert_to_tensor=True returns
            # a torch.Tensor.  The stubs are covered by ignore_missing_imports in
            # pyproject.toml so mypy treats the return as Any; the explicit
            # annotation below locks the type for our code.
            embeddings: torch.Tensor = self._model.encode(
                proses,
                convert_to_tensor=True,
                normalize_embeddings=True,
                show_progress_bar=False,
                batch_size=max(1, len(proses)),
            )

        # Ensure correct precision matches the fp16 flag.
        embeddings = embeddings.half() if self._use_fp16 else embeddings.float()
        return embeddings
