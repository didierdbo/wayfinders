"""Tests for ONNX encoder export and parity verification (substep 11).

Three test layers:

1. **Unit tests (fast, no model download)** -- always run.
   Mock the SentenceTransformer and onnxruntime to test module structure,
   manifest schema, and ParityResult dataclass behaviour.

2. **Export + schema tests (slow, requires CUDA + model)** -- ``@pytest.mark.integration``.
   Actually export the encoder to a temp file and verify the ONNX graph
   properties: dynamic axes, output normalization, manifest schema.

3. **Parity test (slow, torch + onnxruntime)** -- ``@pytest.mark.integration``.
   20-prose sub-sample of the 100-prose parity gate from the export contract.
   max_abs_diff < 1e-3 (same gate as the full 100-prose run in export_encoder.py).

Run integration tests with:
    uv run pytest -m integration tests/ml/export/test_encoder_export.py
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import numpy as np
import pytest
import torch

from wayfinders.ml.export.verify import ParityResult, verify_encoder_parity

# ---------------------------------------------------------------------------
# Helpers / fixtures
# ---------------------------------------------------------------------------


def _make_fake_onnx(tmp_path: Path, rows: int = 3) -> tuple[Path, np.ndarray]:
    """Write a fake .onnx file (random bytes) and return path + fake embeddings.

    Used for unit tests that don't run actual ONNX inference but need a
    file to exist at a predictable path.
    """
    p = tmp_path / "fake_encoder.onnx"
    p.write_bytes(b"\x00\x01\x02" * 100)
    embs = np.random.default_rng(0).standard_normal((rows, 384)).astype(np.float32)
    # L2-normalise rows so norm tests pass on the fake data.
    norms = np.linalg.norm(embs, axis=-1, keepdims=True)
    embs = embs / np.clip(norms, 1e-9, None)
    return p, embs


def _make_valid_manifest(onnx_path: Path) -> dict[str, Any]:
    """Return a manifest dict that satisfies the schema checked in tests."""
    import hashlib

    # Use a real 64-char hex sha256 (of empty bytes) as placeholder.
    fake_sha = hashlib.sha256(b"").hexdigest()
    return {
        "model_name": "sentence-transformers/all-MiniLM-L6-v2",
        "version": "v0.1",
        "export_timestamp": "2026-05-06T12:00:00+00:00",
        "encoder_sha256": fake_sha,
        "onnx_sha256": fake_sha,
        "opset_version": 17,
        "dtype": "fp16",
        "input_schema": [
            {"name": "input_ids", "dtype": "int64", "shape": ["batch", "seq_len"]},
            {"name": "attention_mask", "dtype": "int64", "shape": ["batch", "seq_len"]},
            {"name": "token_type_ids", "dtype": "int64", "shape": ["batch", "seq_len"]},
        ],
        "output_schema": [
            {
                "name": "sentence_embedding",
                "dtype": "float16",
                "shape": ["batch", 384],
                "notes": "mean-pooled, L2-normalised; norm ≈ 1.0",
            }
        ],
        "tokenizer_config_path": "/some/path/tokenizer.json",
    }


# ---------------------------------------------------------------------------
# Layer 1 -- Unit tests (fast, no model)
# ---------------------------------------------------------------------------


class TestParityResultDataclass:
    """ParityResult behaves correctly as a frozen dataclass."""

    def test_passed_true_when_no_violations(self) -> None:
        r = ParityResult(
            n_docs=100,
            max_abs_diff=5e-4,
            mean_abs_diff=1e-4,
            cosine_similarity_min=0.9999,
            n_violations=0,
            atol=1e-3,
            passed=True,
        )
        assert r.passed is True

    def test_passed_false_when_violations(self) -> None:
        r = ParityResult(
            n_docs=100,
            max_abs_diff=2e-3,
            mean_abs_diff=5e-4,
            cosine_similarity_min=0.998,
            n_violations=3,
            atol=1e-3,
            passed=False,
        )
        assert r.passed is False
        assert r.n_violations == 3

    def test_frozen(self) -> None:
        r = ParityResult(
            n_docs=1,
            max_abs_diff=0.0,
            mean_abs_diff=0.0,
            cosine_similarity_min=1.0,
            n_violations=0,
            atol=1e-3,
            passed=True,
        )
        with pytest.raises((AttributeError, TypeError)):
            # frozen=True dataclass raises FrozenInstanceError (subclass of AttributeError)
            r.passed = False  # type: ignore[misc]


class TestVerifyEncoderParityValidation:
    """verify_encoder_parity() raises on bad inputs before doing any ML work."""

    def test_raises_on_empty_prose_docs(self, tmp_path: Path) -> None:
        fake_onnx, _ = _make_fake_onnx(tmp_path)

        class _FakeEncoder:
            def encode_batch(self, proses: list[str]) -> torch.Tensor:
                return torch.zeros(len(proses), 384)

        with pytest.raises(ValueError, match="empty"):
            verify_encoder_parity(
                torch_encoder=_FakeEncoder(),
                onnx_path=fake_onnx,
                prose_docs=[],
            )

    def test_raises_if_onnx_missing(self, tmp_path: Path) -> None:
        missing = tmp_path / "nonexistent.onnx"

        class _FakeEncoder:
            def encode_batch(self, proses: list[str]) -> torch.Tensor:
                return torch.zeros(len(proses), 384)

        with pytest.raises(RuntimeError, match="ONNX file not found"):
            verify_encoder_parity(
                torch_encoder=_FakeEncoder(),
                onnx_path=missing,
                prose_docs=["some prose"],
            )


class TestManifestSchema:
    """Manifest JSON must satisfy the schema expected by the export contract."""

    def _check_manifest(self, manifest: dict[str, Any]) -> None:
        """Assert all required keys and types are present."""
        assert "model_name" in manifest
        assert isinstance(manifest["model_name"], str)

        assert "version" in manifest
        assert isinstance(manifest["version"], str)

        assert "export_timestamp" in manifest
        assert isinstance(manifest["export_timestamp"], str)

        # encoder_sha256: either a 64-char hex string or None.
        sha = manifest.get("encoder_sha256")
        if sha is not None:
            assert isinstance(sha, str), "encoder_sha256 must be str or null"
            assert len(sha) == 64, f"encoder_sha256 must be 64 hex chars, got {len(sha)}"

        # onnx_sha256: must be a 64-char hex string (the file exists).
        onnx_sha = manifest["onnx_sha256"]
        assert isinstance(onnx_sha, str)
        assert len(onnx_sha) == 64, f"onnx_sha256 must be 64 hex chars, got {len(onnx_sha)}"

        assert manifest["opset_version"] == 17
        assert manifest["dtype"] == "fp16"

        # input_schema: 3 entries with correct names.
        input_schema = manifest["input_schema"]
        assert isinstance(input_schema, list)
        assert len(input_schema) == 3
        input_names = {s["name"] for s in input_schema}
        assert input_names == {"input_ids", "attention_mask", "token_type_ids"}
        for s in input_schema:
            assert s["dtype"] == "int64"
            assert s["shape"] == ["batch", "seq_len"]

        # output_schema: 1 entry.
        output_schema = manifest["output_schema"]
        assert isinstance(output_schema, list)
        assert len(output_schema) == 1
        out = output_schema[0]
        assert out["name"] == "sentence_embedding"
        assert out["dtype"] == "float16"
        assert out["shape"][0] == "batch"
        assert out["shape"][1] == 384

    def test_valid_manifest_passes_schema(self, tmp_path: Path) -> None:
        m = _make_valid_manifest(tmp_path / "dummy.onnx")
        self._check_manifest(m)

    def test_missing_opset_fails(self, tmp_path: Path) -> None:
        m = _make_valid_manifest(tmp_path / "dummy.onnx")
        del m["opset_version"]
        with pytest.raises(KeyError):
            self._check_manifest(m)

    def test_wrong_dtype_fails(self, tmp_path: Path) -> None:
        m = _make_valid_manifest(tmp_path / "dummy.onnx")
        m["dtype"] = "fp32"
        with pytest.raises(AssertionError):
            self._check_manifest(m)


# ---------------------------------------------------------------------------
# Layer 2 -- Integration tests (slow, real model + CUDA + onnxruntime)
# ---------------------------------------------------------------------------

# Shared ONNX path fixture scoped at session level so the file is exported
# once and reused across all integration tests in this run.


@pytest.fixture(scope="session")
def exported_onnx_path(tmp_path_factory: pytest.TempPathFactory) -> Path:
    """Export the real encoder to a temp .onnx file (once per test session).

    Skips the fixture (and all tests using it) if CUDA is not available.
    """
    if not torch.cuda.is_available():
        pytest.skip("CUDA not available -- skipping encoder ONNX integration tests")

    from wayfinders.ml.export.export_encoder import export_encoder

    out_dir = tmp_path_factory.mktemp("onnx_export")
    onnx_file = out_dir / "minilm-l6-v2-fp16.onnx"
    export_encoder(onnx_file)
    return onnx_file


@pytest.fixture(scope="session")
def exported_manifest(exported_onnx_path: Path) -> dict[str, Any]:
    """Read the manifest written alongside the exported .onnx."""
    manifest_path = exported_onnx_path.with_suffix("").with_suffix(".manifest.json")
    return json.loads(manifest_path.read_text(encoding="utf-8"))


@pytest.fixture(scope="session")
def real_encoder() -> Any:
    """Shared MiniLMEncoder instance (loaded once for the integration session)."""
    if not torch.cuda.is_available():
        pytest.skip("CUDA not available")
    from wayfinders.ml.inference.encoder import MiniLMEncoder

    return MiniLMEncoder()


@pytest.mark.integration
class TestOnnxFileExists:
    """After export, the .onnx file is present at the expected path."""

    def test_onnx_file_exists(self, exported_onnx_path: Path) -> None:
        assert exported_onnx_path.is_file()

    def test_onnx_file_size_in_range(self, exported_onnx_path: Path) -> None:
        # Expected ~45 MB (fp16 MiniLM). Allow 40-55 MB.
        size_mb = exported_onnx_path.stat().st_size / 1e6
        assert 40 <= size_mb <= 55, f"Unexpected ONNX size: {size_mb:.1f} MB"

    def test_manifest_file_exists(self, exported_onnx_path: Path) -> None:
        manifest_path = exported_onnx_path.with_suffix("").with_suffix(".manifest.json")
        assert manifest_path.is_file()


@pytest.mark.integration
class TestManifestSchemaIntegration:
    """Manifest written by export_encoder() satisfies the schema contract."""

    def _check_manifest(self, manifest: dict[str, Any]) -> None:
        assert isinstance(manifest["model_name"], str)
        assert isinstance(manifest["version"], str)
        assert isinstance(manifest["export_timestamp"], str)

        sha = manifest.get("encoder_sha256")
        if sha is not None:
            assert len(sha) == 64, f"encoder_sha256 bad length: {len(sha)}"

        onnx_sha = manifest["onnx_sha256"]
        assert isinstance(onnx_sha, str)
        assert len(onnx_sha) == 64

        assert manifest["opset_version"] == 17
        assert manifest["dtype"] == "fp16"

        input_schema = manifest["input_schema"]
        assert len(input_schema) == 3
        names = {s["name"] for s in input_schema}
        assert names == {"input_ids", "attention_mask", "token_type_ids"}

        out_s = manifest["output_schema"]
        assert len(out_s) == 1
        assert out_s[0]["name"] == "sentence_embedding"
        assert out_s[0]["dtype"] == "float16"
        assert out_s[0]["shape"][1] == 384

    def test_manifest_schema(self, exported_manifest: dict[str, Any]) -> None:
        self._check_manifest(exported_manifest)


@pytest.mark.integration
class TestDynamicAxes:
    """Exported ONNX handles batches of different sizes without recompile."""

    def test_batch_sizes_1_4_16(self, exported_onnx_path: Path) -> None:
        import onnxruntime as ort
        from sentence_transformers import SentenceTransformer

        st = SentenceTransformer("sentence-transformers/all-MiniLM-L6-v2", device="cpu")
        tok = st[0].tokenizer

        sess = ort.InferenceSession(str(exported_onnx_path), providers=["CPUExecutionProvider"])

        prose = "Kira sneaks past the sentry under starlight."
        for batch_n in (1, 4, 16):
            texts = [prose] * batch_n
            enc = tok(texts, return_tensors="pt", padding=True, truncation=True, max_length=256)
            out = sess.run(
                ["sentence_embedding"],
                {
                    "input_ids": enc["input_ids"].numpy(),
                    "attention_mask": enc["attention_mask"].numpy(),
                    "token_type_ids": enc["token_type_ids"].numpy(),
                },
            )[0]
            assert out.shape == (batch_n, 384), f"Wrong shape for batch={batch_n}: {out.shape}"


@pytest.mark.integration
class TestOutputNormalized:
    """ONNX output embeddings are L2-normalized (norm ≈ 1.0)."""

    def test_output_norm_10_proses(self, exported_onnx_path: Path) -> None:
        import onnxruntime as ort
        from sentence_transformers import SentenceTransformer

        proses = [
            "Kira sneaks past two sentries at the watchtower under starlight.",
            "The goblin sentry is drowsy and distracted.",
            "The campaign is holding firm in the eastern marches.",
            "Rain masks the sound of movement on the ridge.",
            "A scout moves along loose scree at dawn.",
            "The party is well-supplied and rested after the long march.",
            "Kira has previously succeeded at stealth approach eight times.",
            "The opposition is alert and searching in the deep forest.",
            "The stakes are dire: the company moves blind into the valley.",
            "A strong wind from the north covers the sound of footsteps.",
        ]
        assert len(proses) == 10

        st = SentenceTransformer("sentence-transformers/all-MiniLM-L6-v2", device="cpu")
        tok = st[0].tokenizer
        enc_inputs = tok(proses, return_tensors="pt", padding=True, truncation=True, max_length=256)

        sess = ort.InferenceSession(str(exported_onnx_path), providers=["CPUExecutionProvider"])
        embs = sess.run(
            ["sentence_embedding"],
            {
                "input_ids": enc_inputs["input_ids"].numpy(),
                "attention_mask": enc_inputs["attention_mask"].numpy(),
                "token_type_ids": enc_inputs["token_type_ids"].numpy(),
            },
        )[0].astype(np.float32)

        norms = np.linalg.norm(embs, axis=-1)
        for i, norm in enumerate(norms):
            assert abs(norm - 1.0) < 1e-3, f"prose[{i}] norm={norm:.6f}, expected ≈1.0"


@pytest.mark.integration
class TestParityTorchVsOnnx:
    """Torch MiniLMEncoder vs ONNX Runtime: max_abs_diff < 1e-3 on 20 proses.

    Uses a 20-prose sub-sample of the 100-prose gate from the export contract
    to keep CI fast.  The full 100-prose run is done by the export script.
    """

    def test_parity_20_proses(self, exported_onnx_path: Path, real_encoder: Any) -> None:
        from wayfinders.ml.data_gen.rollouts import generate_rollouts

        # 20 proses drawn from real rollouts (mix of char / action / context).
        rollouts = list(generate_rollouts(20, seed=999))
        proses: list[str] = []
        for r in rollouts[:7]:
            proses.append(r.prose_character)
        for r in rollouts[7:14]:
            proses.append(r.prose_action)
        for r in rollouts[14:20]:
            proses.append(r.prose_context)
        assert len(proses) == 20

        result = verify_encoder_parity(
            torch_encoder=real_encoder,
            onnx_path=exported_onnx_path,
            prose_docs=proses,
            atol=1e-3,
        )

        assert result.passed, (
            f"Parity FAILED: max_abs_diff={result.max_abs_diff:.6f} "
            f"(gate: <1e-3), violations={result.n_violations}/{result.n_docs}"
        )
        # Also assert the numeric value directly so test failures are readable.
        assert result.max_abs_diff < 1e-3, (
            f"max_abs_diff={result.max_abs_diff:.6f} exceeds 1e-3 gate"
        )
