"""Tests for ``wayfinders.ml.inference.encoder.MiniLMEncoder``.

Three levels:

1. **Unit tests (fast, no model download)** -- marked *default* (always run).
   Mock the ``SentenceTransformer`` with a stub that simulates the API.
   Cover: frozen invariants at construction, shape contracts (single + batch),
   single-vs-batch parity on the stub, error handling on empty input,
   device/fp16 flag, SHA-256 helper.

2. **Integration tests (slow, real model)** -- marked ``@pytest.mark.integration``.
   Skip by default; run with ``uv run pytest -m integration``.
   Cover: real MiniLM loads and produces (384,) embeddings, determinism at
   epsilon, batch == sequential equivalence.

Rationale for the split: downloading MiniLM (~45 MB) on every CI run would
penalise unrelated PRs (schemas, game logic, API).  If the model is already
in the HuggingFace cache (probable on Didier's dev machine), the integration
tests are fast anyway.
"""

from __future__ import annotations

import types
from typing import Any
from unittest.mock import MagicMock, patch

import pytest
import torch

from wayfinders.ml.inference.encoder import (
    ENCODER_EMBEDDING_DIM,
    ENCODER_MODEL_ID,
    MiniLMEncoder,
    _find_weights_file,
    _sha256_file,
)

# ---------------------------------------------------------------------------
# Stub helpers
# ---------------------------------------------------------------------------


def _make_stub_module(embedding_dim: int = ENCODER_EMBEDDING_DIM) -> MagicMock:
    """Return a MagicMock that quacks like a frozen SentenceTransformer.

    The mock:
    - Is in eval() mode (training = False).
    - Has no learnable parameters (parameters() returns []).
    - encode() returns a deterministic torch.Tensor based on input hashes.

    The returned mock is used to replace the real SentenceTransformer via
    patch() so unit tests never touch the network.
    """
    stub = MagicMock()
    stub.training = False

    # parameters() must return an iterator with no items (frozen).
    stub.parameters = MagicMock(return_value=iter([]))

    # modules() must yield at least one item (the stub itself).
    stub.__iter__ = MagicMock(return_value=iter([stub]))
    stub.modules = MagicMock(return_value=iter([stub]))

    # encode() returns a (N, 384) L2-normalised tensor.
    # We base each row on the hash of the input string so same input ->
    # same output (determinism) and different inputs -> different vectors
    # (functional coverage).
    def _stub_encode(
        sentences: list[str],
        convert_to_tensor: bool = True,
        normalize_embeddings: bool = True,
        show_progress_bar: bool = False,
        batch_size: int = 32,
        **_kwargs: Any,
    ) -> torch.Tensor:
        rows = []
        for s in sentences:
            # Seed from string hash -- stable, deterministic.
            seed = hash(s) & 0xFFFFFFFF
            gen = torch.Generator()
            gen.manual_seed(seed)
            vec = torch.randn(embedding_dim, generator=gen)
            if normalize_embeddings:
                vec = vec / vec.norm().clamp_min(1e-8)
            rows.append(vec)
        return torch.stack(rows)

    stub.encode = MagicMock(side_effect=_stub_encode)

    # half() / float() should be no-ops on the stub (returns itself).
    stub.half = MagicMock(return_value=stub)

    return stub


def _make_encoder_with_stub(
    device: str = "cpu",
    stub: MagicMock | None = None,
) -> MiniLMEncoder:
    """Construct a MiniLMEncoder whose SentenceTransformer is replaced by *stub*.

    Patches sentence_transformers.SentenceTransformer so the real model is
    never loaded.

    Also patches _resolve_weights_from_hf_cache to return None so unit tests
    are fully isolated from the real HuggingFace on-disk cache.  Without this,
    any machine (including CI) that has the model in its HF cache will cause
    weights_sha256 to return a real hash rather than None, breaking tests that
    assert the None path.
    """
    if stub is None:
        stub = _make_stub_module()

    st_module = types.ModuleType("sentence_transformers")
    st_module.SentenceTransformer = MagicMock(return_value=stub)  # type: ignore[attr-defined]

    with (
        patch.dict("sys.modules", {"sentence_transformers": st_module}),
        patch(
            "wayfinders.ml.inference.encoder._resolve_weights_from_hf_cache",
            return_value=None,
        ),
    ):
        enc = MiniLMEncoder(device=device)

    return enc


# ---------------------------------------------------------------------------
# Layer 1 -- Unit tests (fast, default)
# ---------------------------------------------------------------------------


class TestFrozenInvariants:
    """Encoder construction enforces frozen-encoder contract immediately."""

    def test_no_grad_params_after_construction(self) -> None:
        """All parameters must have requires_grad=False after MiniLMEncoder()."""
        enc = _make_encoder_with_stub()
        # The stub has no parameters, so the loop is vacuously satisfied.
        # We verify the contract didn't raise (which it would if stub had
        # requires_grad=True params).
        assert enc is not None

    def test_construction_raises_if_params_have_grad(self) -> None:
        """If a parameter resists freezing (requires_grad stays True), construction raises.

        This simulates a hypothetical SentenceTransformer version where
        requires_grad_(False) on a parameter has no effect -- the hard assert
        in __init__ catches it before the encoder is used for training.
        """
        bad_stub = _make_stub_module()
        # Create a mock parameter whose requires_grad property always returns
        # True (simulates a param that ignores requires_grad_(False)).
        stubborn_param = MagicMock()
        stubborn_param.requires_grad = True
        # requires_grad_() does nothing (returns the mock, not False).
        stubborn_param.requires_grad_ = MagicMock(return_value=stubborn_param)
        # parameters() yields it twice: once for the freeze loop, once for
        # the post-freeze count.  We achieve this with a side_effect list.
        bad_stub.parameters = MagicMock(
            side_effect=[iter([stubborn_param]), iter([stubborn_param])]
        )

        st_module = types.ModuleType("sentence_transformers")
        st_module.SentenceTransformer = MagicMock(return_value=bad_stub)  # type: ignore[attr-defined]

        with (
            patch.dict("sys.modules", {"sentence_transformers": st_module}),
            pytest.raises(RuntimeError, match="requires_grad=True"),
        ):
            MiniLMEncoder(device="cpu")

    def test_construction_raises_if_model_in_train_mode(self) -> None:
        """If the model is in training mode after .eval(), construction raises."""
        bad_stub = _make_stub_module()
        bad_stub.training = True  # simulate a model that ignores .eval()

        st_module = types.ModuleType("sentence_transformers")
        st_module.SentenceTransformer = MagicMock(return_value=bad_stub)  # type: ignore[attr-defined]

        with (
            patch.dict("sys.modules", {"sentence_transformers": st_module}),
            pytest.raises(RuntimeError, match="training mode"),
        ):
            MiniLMEncoder(device="cpu")

    def test_device_property_matches_construction_arg(self) -> None:
        enc = _make_encoder_with_stub(device="cpu")
        assert enc.device == "cpu"

    def test_fp16_false_on_cpu(self) -> None:
        enc = _make_encoder_with_stub(device="cpu")
        assert enc.use_fp16 is False

    def test_fp16_true_on_cuda_stub(self) -> None:
        """If device='cuda', use_fp16 must be True (even on a stubbed encoder)."""
        enc = _make_encoder_with_stub(device="cuda")
        assert enc.use_fp16 is True


class TestShapeContracts:
    """encode() and encode_batch() return the correct tensor shapes."""

    def test_encode_single_shape(self) -> None:
        enc = _make_encoder_with_stub()
        out = enc.encode("A scout moves along loose scree under starlight.")
        assert out.shape == (ENCODER_EMBEDDING_DIM,)

    def test_encode_batch_shape(self) -> None:
        enc = _make_encoder_with_stub()
        proses = [
            "Kira moves along loose scree.",
            "The sentry is drowsy and distracted.",
            "The campaign is winning across the eastern marches.",
        ]
        out = enc.encode_batch(proses)
        assert out.shape == (3, ENCODER_EMBEDDING_DIM)

    def test_encode_batch_single_item(self) -> None:
        enc = _make_encoder_with_stub()
        out = enc.encode_batch(["one sentence"])
        assert out.shape == (1, ENCODER_EMBEDDING_DIM)


class TestSingleVsBatchParity:
    """encode(s) must match encode_batch([s])[0] exactly on the stub."""

    def test_single_matches_batch_row(self) -> None:
        enc = _make_encoder_with_stub()
        prose = "Kira sneaks past two sentries at the watchtower under starlight."
        single = enc.encode(prose)
        batch = enc.encode_batch([prose])
        # The stub is deterministic: same input -> same embedding.
        assert torch.allclose(single, batch[0])


class TestErrorHandling:
    """Empty inputs raise ValueError immediately."""

    def test_encode_empty_string_raises(self) -> None:
        enc = _make_encoder_with_stub()
        with pytest.raises(ValueError, match="empty string"):
            enc.encode("")

    def test_encode_batch_empty_list_raises(self) -> None:
        enc = _make_encoder_with_stub()
        with pytest.raises(ValueError, match="empty list"):
            enc.encode_batch([])

    def test_encode_batch_empty_item_raises(self) -> None:
        enc = _make_encoder_with_stub()
        with pytest.raises(ValueError, match="proses\\[1\\] is empty"):
            enc.encode_batch(["valid sentence", ""])


class TestSha256Helper:
    """_sha256_file and _find_weights_file behave correctly."""

    def test_sha256_file_is_deterministic(self, tmp_path: pytest.TempPathFactory) -> None:  # type: ignore[type-arg]
        f = tmp_path / "weights.bin"  # type: ignore[operator]
        f.write_bytes(b"\x00\x01\x02" * 1000)
        h1 = _sha256_file(f)
        h2 = _sha256_file(f)
        assert h1 == h2
        assert len(h1) == 64  # 32 bytes hex-encoded

    def test_sha256_file_changes_on_different_content(
        self, tmp_path: pytest.TempPathFactory
    ) -> None:  # type: ignore[type-arg]
        f1 = tmp_path / "a.bin"  # type: ignore[operator]
        f2 = tmp_path / "b.bin"  # type: ignore[operator]
        f1.write_bytes(b"hello")
        f2.write_bytes(b"world")
        assert _sha256_file(f1) != _sha256_file(f2)

    def test_find_weights_prefers_safetensors(self, tmp_path: pytest.TempPathFactory) -> None:  # type: ignore[type-arg]
        model_dir = tmp_path / "model"  # type: ignore[operator]
        model_dir.mkdir()
        sf = model_dir / "model.safetensors"
        sf.write_bytes(b"fake safetensors")
        (model_dir / "pytorch_model.bin").write_bytes(b"fake bin")
        result = _find_weights_file(model_dir)
        assert result == sf

    def test_find_weights_falls_back_to_bin(self, tmp_path: pytest.TempPathFactory) -> None:  # type: ignore[type-arg]
        model_dir = tmp_path / "model"  # type: ignore[operator]
        model_dir.mkdir()
        binfile = model_dir / "pytorch_model.bin"
        binfile.write_bytes(b"fake bin")
        result = _find_weights_file(model_dir)
        assert result == binfile

    def test_find_weights_returns_none_when_absent(self, tmp_path: pytest.TempPathFactory) -> None:  # type: ignore[type-arg]
        model_dir = tmp_path / "model"  # type: ignore[operator]
        model_dir.mkdir()
        result = _find_weights_file(model_dir)
        assert result is None

    def test_encoder_weights_sha256_none_when_no_file(self) -> None:
        """weights_sha256 returns None gracefully when no weights file is found."""
        enc = _make_encoder_with_stub()
        # Stub's internal module has no name_or_path attribute that points to
        # a real directory, so _weights_path stays None.
        assert enc.weights_sha256 is None


class TestConstantsExposed:
    """Public constants have the right values."""

    def test_encoder_model_id(self) -> None:
        assert ENCODER_MODEL_ID == "sentence-transformers/all-MiniLM-L6-v2"

    def test_embedding_dim(self) -> None:
        assert ENCODER_EMBEDDING_DIM == 384


# ---------------------------------------------------------------------------
# Layer 2 -- Integration tests (slow, real model)
# ---------------------------------------------------------------------------
# Skip by default; run with: uv run pytest -m integration
# First run triggers a HuggingFace cache download (~45 MB).
# Subsequent runs are fast (cache hit).
# ---------------------------------------------------------------------------


def _numeric_atol(enc: MiniLMEncoder) -> float:
    """Return the appropriate absolute tolerance for tensor comparisons.

    fp16 on CUDA uses different cuDNN kernel paths for batched vs sequential
    ops (e.g. batched matmul vs sequential einsum variants).  These paths
    accumulate rounding differently, so batch==sequential diverges at ~1e-4
    even when both calls are bit-reproducible on their own.  fp32 (CPU or GPU)
    stays within 1e-5 and we keep that strict threshold to catch real bugs.
    """
    return 1e-3 if enc.use_fp16 else 1e-5


@pytest.mark.integration
class TestIntegrationRealModel:
    """Integration tests that load the real sentence-transformers model."""

    @pytest.fixture(scope="class")
    def enc(self) -> MiniLMEncoder:
        """Shared encoder instance for the integration class (loaded once)."""
        return MiniLMEncoder()

    def test_encode_returns_384_dim(self, enc: MiniLMEncoder) -> None:
        vec = enc.encode("Kira crouches behind a boulder under starlight.")
        assert vec.shape == (ENCODER_EMBEDDING_DIM,)

    def test_encode_batch_returns_correct_shape(self, enc: MiniLMEncoder) -> None:
        proses = [
            "The sentry patrol passes the watchtower.",
            "The campaign is holding firm in the eastern marches.",
            "Rain masks the sound of movement.",
        ]
        batch = enc.encode_batch(proses)
        assert batch.shape == (3, ENCODER_EMBEDDING_DIM)

    def test_encode_is_normalized(self, enc: MiniLMEncoder) -> None:
        """Embeddings must be L2-normalised (norm ≈ 1.0)."""
        vec = enc.encode("Kira moves across broken stone and scree.")
        norm = vec.float().norm().item()
        assert abs(norm - 1.0) < 1e-3

    def test_batch_normalised(self, enc: MiniLMEncoder) -> None:
        rows = enc.encode_batch(["sentence one", "sentence two"])
        for i in range(rows.shape[0]):
            norm = rows[i].float().norm().item()
            assert abs(norm - 1.0) < 1e-3

    def test_determinism_same_prose(self, enc: MiniLMEncoder) -> None:
        """Same prose produces the same embedding on repeated calls (epsilon)."""
        prose = "Kira sneaks past two sentries at the watchtower under starlight."
        a = enc.encode(prose)
        b = enc.encode(prose)
        assert torch.allclose(a.float(), b.float(), atol=_numeric_atol(enc))

    def test_batch_equals_sequential(self, enc: MiniLMEncoder) -> None:
        """encode_batch([s1, s2]) must match [encode(s1), encode(s2)] row-by-row."""
        prose_a = "Kira crouches on loose scree under starlight."
        prose_b = "The goblin sentry is drowsy and distracted."
        batch = enc.encode_batch([prose_a, prose_b])
        single_a = enc.encode(prose_a)
        single_b = enc.encode(prose_b)
        atol = _numeric_atol(enc)
        assert torch.allclose(batch[0].float(), single_a.float(), atol=atol)
        assert torch.allclose(batch[1].float(), single_b.float(), atol=atol)

    def test_no_grad_invariant_holds_after_encode(self, enc: MiniLMEncoder) -> None:
        """Encoding must not accumulate gradients (frozen contract)."""
        prose = "The ridge mission is high stakes."
        vec = enc.encode(prose)
        assert vec.grad_fn is None  # no gradient attached (torch.no_grad context)

    def test_different_prose_different_embedding(self, enc: MiniLMEncoder) -> None:
        """Semantically different sentences should not produce identical embeddings."""
        a = enc.encode("Kira moves quietly through the forest.")
        b = enc.encode("A dragon breathes fire over the castle.")
        # They should not be identical (extremely unlikely for any real encoder).
        assert not torch.allclose(a.float(), b.float())

    def test_fp16_or_fp32_consistent_with_device(self, enc: MiniLMEncoder) -> None:
        """Output dtype must match use_fp16 flag."""
        vec = enc.encode("Test prose.")
        if enc.use_fp16:
            assert vec.dtype == torch.float16
        else:
            assert vec.dtype == torch.float32
