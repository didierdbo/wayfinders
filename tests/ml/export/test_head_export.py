"""Tests for ONNX head export and end-to-end pipeline parity (substep 12).

Three test layers:

1. **Unit tests (fast, no model)** -- always run.
   Manifest schema validation, ParityResult/PipelineParityResult dataclasses,
   input validation.

2. **Head export + schema tests (slow, requires CUDA + model)** -- ``@pytest.mark.integration``.
   Actually export the head from the v0.1 checkpoint, verify ONNX graph
   properties: dynamic batch, output range [-5, +5], manifest schema.

3. **End-to-end parity (slow, torch + onnxruntime)** -- ``@pytest.mark.integration``.
   20-rollout sub-sample of the 100-rollout gate; max_abs_diff < 1e-2.

Run integration tests with:
    uv run pytest -m integration tests/ml/export/test_head_export.py
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

import numpy as np
import pytest
import torch

from wayfinders.ml.export.verify import PipelineParityResult

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

_REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent
# v0.6: retrain PASS, val MAE 0.5657 (gate <=0.6 cleared, 2026-05-09).
_V06_CHECKPOINT = _REPO_ROOT / "artifacts" / "models" / "v0.6" / "head_final.pt"
# Keep v0.1 alias so existing skip/fixture logic still compiles.
_V01_CHECKPOINT = _V06_CHECKPOINT


def _make_valid_head_manifest(onnx_path: Path) -> dict[str, Any]:
    """Return a manifest dict that satisfies the head manifest schema checked in tests."""
    fake_sha = hashlib.sha256(b"").hexdigest()
    return {
        "model_name": "wayfinders.ml.training.head.ResolutionHead",
        "version": "v0.6",
        "export_timestamp": "2026-05-09T12:00:00+00:00",
        "head_sha256": fake_sha,
        "head_pt_sha256": fake_sha,
        "training_run_id": "v0.6-seed42-ep100",
        "val_mae": 0.5657,
        "test_mae": 0.8240,
        "r_squared_test": 0.7655,
        "encoder_sha256": fake_sha,
        "opset_version": 17,
        "dtype": "fp32",
        "input_schema": [
            {"name": "char_vec", "dtype": "float32", "shape": ["batch", 384]},
            {"name": "action_vec", "dtype": "float32", "shape": ["batch", 384]},
            {"name": "context_vec", "dtype": "float32", "shape": ["batch", 384]},
        ],
        "output_schema": [
            {
                "name": "delta",
                "dtype": "float32",
                "shape": ["batch", 1],
                "notes": "Δ ∈ [-5, +5], tanh*5 bounded in-graph; no client-side clipping needed",
            }
        ],
        "tanh_scaling": 5.0,
        "head_input_dim": 1153,
        "notes": "cos(char_vec, action_vec) is computed inside the ONNX graph.",
    }


def _check_head_manifest(manifest: dict[str, Any]) -> None:
    """Assert all required keys and types for the head manifest."""
    assert "model_name" in manifest
    assert isinstance(manifest["model_name"], str)

    assert "version" in manifest
    assert isinstance(manifest["version"], str)

    assert "export_timestamp" in manifest
    assert isinstance(manifest["export_timestamp"], str)

    # head_sha256: 64-char hex string.
    head_sha = manifest.get("head_sha256")
    assert head_sha is not None, "head_sha256 must be present"
    assert isinstance(head_sha, str)
    assert len(head_sha) == 64, f"head_sha256 must be 64 hex chars, got {len(head_sha)}"

    # head_pt_sha256: 64-char hex string.
    pt_sha = manifest.get("head_pt_sha256")
    assert pt_sha is not None, "head_pt_sha256 must be present"
    assert isinstance(pt_sha, str)
    assert len(pt_sha) == 64, f"head_pt_sha256 must be 64 hex chars, got {len(pt_sha)}"

    # val_mae and test_mae must be real floats (not 0.0 placeholders).
    assert "val_mae" in manifest
    val_mae = manifest["val_mae"]
    assert isinstance(val_mae, (int, float))
    assert val_mae > 0.0, "val_mae should be a real trained value, not 0.0"

    assert "test_mae" in manifest
    test_mae = manifest["test_mae"]
    assert isinstance(test_mae, (int, float))
    assert test_mae > 0.0, "test_mae should be a real trained value, not 0.0"

    assert manifest["opset_version"] == 17
    assert manifest["dtype"] == "fp32"
    assert manifest["tanh_scaling"] == 5.0

    # input_schema: 3 entries with correct names.
    input_schema = manifest["input_schema"]
    assert isinstance(input_schema, list)
    assert len(input_schema) == 3
    names = {s["name"] for s in input_schema}
    assert names == {"char_vec", "action_vec", "context_vec"}
    for s in input_schema:
        assert s["dtype"] == "float32"
        assert s["shape"] == ["batch", 384]

    # output_schema: 1 entry.
    output_schema = manifest["output_schema"]
    assert isinstance(output_schema, list)
    assert len(output_schema) == 1
    out = output_schema[0]
    assert out["name"] == "delta"
    assert out["dtype"] == "float32"
    assert out["shape"][0] == "batch"
    assert out["shape"][1] == 1


# ---------------------------------------------------------------------------
# Layer 1 -- Unit tests (fast, no model)
# ---------------------------------------------------------------------------


class TestPipelineParityResultDataclass:
    """PipelineParityResult behaves correctly as a frozen dataclass."""

    def test_passed_true_when_no_violations(self) -> None:
        r = PipelineParityResult(
            n_rollouts=100,
            max_abs_diff=5e-3,
            mean_abs_diff=1e-3,
            violations=0,
            atol=1e-2,
            passed=True,
            delta_torch=np.zeros(100),
            delta_onnx=np.zeros(100),
        )
        assert r.passed is True
        assert r.violations == 0

    def test_passed_false_when_violations(self) -> None:
        r = PipelineParityResult(
            n_rollouts=100,
            max_abs_diff=2e-2,
            mean_abs_diff=5e-3,
            violations=3,
            atol=1e-2,
            passed=False,
            delta_torch=np.ones(100),
            delta_onnx=np.ones(100),
        )
        assert r.passed is False
        assert r.violations == 3

    def test_frozen(self) -> None:
        r = PipelineParityResult(
            n_rollouts=1,
            max_abs_diff=0.0,
            mean_abs_diff=0.0,
            violations=0,
            atol=1e-2,
            passed=True,
            delta_torch=np.zeros(1),
            delta_onnx=np.zeros(1),
        )
        with pytest.raises((AttributeError, TypeError)):
            r.passed = False  # type: ignore[misc]


class TestHeadManifestSchemaUnit:
    """Head manifest must satisfy the schema."""

    def test_valid_manifest_passes(self, tmp_path: Path) -> None:
        m = _make_valid_head_manifest(tmp_path / "dummy.onnx")
        _check_head_manifest(m)  # Should not raise.

    def test_missing_tanh_scaling_raises(self, tmp_path: Path) -> None:
        m = _make_valid_head_manifest(tmp_path / "dummy.onnx")
        del m["tanh_scaling"]
        with pytest.raises(KeyError):
            _check_head_manifest(m)

    def test_wrong_dtype_raises(self, tmp_path: Path) -> None:
        m = _make_valid_head_manifest(tmp_path / "dummy.onnx")
        m["dtype"] = "fp16"
        with pytest.raises(AssertionError):
            _check_head_manifest(m)

    def test_zero_val_mae_raises(self, tmp_path: Path) -> None:
        m = _make_valid_head_manifest(tmp_path / "dummy.onnx")
        m["val_mae"] = 0.0
        with pytest.raises(AssertionError, match="real trained value"):
            _check_head_manifest(m)


# ---------------------------------------------------------------------------
# Layer 2 -- Integration tests (slow, real checkpoint + CUDA + onnxruntime)
# ---------------------------------------------------------------------------


@pytest.fixture(scope="session")
def exported_head_onnx_path(tmp_path_factory: pytest.TempPathFactory) -> Path:
    """Export the v0.6 ResolutionHead checkpoint to a temp .onnx file (once per session).

    Skips all integration tests if CUDA is not available or the checkpoint is missing.
    """
    if not torch.cuda.is_available():
        pytest.skip("CUDA not available -- skipping head ONNX integration tests")

    if not _V06_CHECKPOINT.is_file():
        pytest.skip(f"v0.6 checkpoint not found at {_V06_CHECKPOINT}")

    from wayfinders.ml.export.export_head import export_head

    out_dir = tmp_path_factory.mktemp("head_onnx_export")
    onnx_file = out_dir / "resolution-head-v0.6.onnx"
    export_head(_V06_CHECKPOINT, onnx_file)
    return onnx_file


@pytest.fixture(scope="session")
def exported_head_manifest(exported_head_onnx_path: Path) -> dict[str, Any]:
    """Read the manifest written alongside the exported head .onnx."""
    # Use stem + ".manifest.json" to avoid stripping the version component from
    # names like "resolution-head-v0.1.onnx" where the double .with_suffix() call
    # would incorrectly produce "resolution-head-v0.manifest.json".
    manifest_path = exported_head_onnx_path.parent / (
        exported_head_onnx_path.stem + ".manifest.json"
    )
    return json.loads(manifest_path.read_text(encoding="utf-8"))


@pytest.fixture(scope="session")
def real_encoder_for_head() -> Any:
    """Shared MiniLMEncoder instance for head integration tests."""
    if not torch.cuda.is_available():
        pytest.skip("CUDA not available")
    from wayfinders.ml.inference.encoder import MiniLMEncoder

    return MiniLMEncoder()


@pytest.fixture(scope="session")
def real_head_torch() -> Any:
    """ResolutionHead loaded from v0.6 checkpoint, in eval mode, on CUDA."""
    if not torch.cuda.is_available():
        pytest.skip("CUDA not available")
    if not _V06_CHECKPOINT.is_file():
        pytest.skip(f"v0.6 checkpoint not found at {_V06_CHECKPOINT}")
    from wayfinders.ml.training.head import ResolutionHead

    head = ResolutionHead()
    raw_ckpt = torch.load(_V06_CHECKPOINT, map_location="cuda", weights_only=True)
    if isinstance(raw_ckpt, dict) and "model_state_dict" in raw_ckpt:
        state_dict = raw_ckpt["model_state_dict"]
    else:
        state_dict = raw_ckpt
    head.load_state_dict(state_dict)
    head.eval()
    head.cuda()
    for p in head.parameters():
        p.requires_grad_(False)
    return head


@pytest.mark.integration
class TestHeadOnnxFileExists:
    """After export, the .onnx and manifest files are present."""

    def test_head_onnx_file_exists(self, exported_head_onnx_path: Path) -> None:
        assert exported_head_onnx_path.is_file(), f"ONNX not found: {exported_head_onnx_path}"

    def test_head_manifest_file_exists(self, exported_head_onnx_path: Path) -> None:
        # Use stem + ".manifest.json" to match export_head()'s naming convention
        # (avoid double .with_suffix() which strips the version from "v0.1").
        manifest_path = exported_head_onnx_path.parent / (
            exported_head_onnx_path.stem + ".manifest.json"
        )
        assert manifest_path.is_file(), f"Manifest not found: {manifest_path}"

    def test_head_onnx_size_reasonable(self, exported_head_onnx_path: Path) -> None:
        # Expected ~1.2 MB (fp32 ~313k params). Allow 0.5-5 MB.
        size_mb = exported_head_onnx_path.stat().st_size / 1e6
        assert 0.5 <= size_mb <= 5.0, f"Unexpected head ONNX size: {size_mb:.2f} MB"


@pytest.mark.integration
class TestHeadManifestSchema:
    """Manifest written by export_head() satisfies the schema contract."""

    def test_manifest_schema(self, exported_head_manifest: dict[str, Any]) -> None:
        _check_head_manifest(exported_head_manifest)

    def test_manifest_hashes_are_hex64(self, exported_head_manifest: dict[str, Any]) -> None:
        for key in ("head_sha256", "head_pt_sha256"):
            sha = exported_head_manifest[key]
            assert len(sha) == 64, f"{key} bad length: {len(sha)}"
            # Verify it's valid hex.
            int(sha, 16)

    def test_manifest_val_mae_matches_training(
        self, exported_head_manifest: dict[str, Any]
    ) -> None:
        # v0.6 val MAE 0.5657 (gate <=0.6 cleared). Plausible range: [0.4, 1.0].
        val_mae = exported_head_manifest["val_mae"]
        assert 0.4 <= val_mae <= 1.0, f"val_mae {val_mae} out of plausible range"

    def test_manifest_version_is_v06(self, exported_head_manifest: dict[str, Any]) -> None:
        assert exported_head_manifest["version"] == "v0.6", (
            f"Expected version v0.6, got {exported_head_manifest['version']}"
        )

    def test_manifest_tanh_scaling(self, exported_head_manifest: dict[str, Any]) -> None:
        assert exported_head_manifest["tanh_scaling"] == 5.0


@pytest.mark.integration
class TestHeadDynamicBatch:
    """Exported head accepts batch sizes 1 and 16 without recompile."""

    def test_batch_1_and_16(self, exported_head_onnx_path: Path) -> None:
        import onnxruntime as ort

        sess = ort.InferenceSession(
            str(exported_head_onnx_path), providers=["CPUExecutionProvider"]
        )

        rng = np.random.default_rng(42)
        for batch_n in (1, 16):
            char_np = rng.standard_normal((batch_n, 384)).astype(np.float32)
            action_np = rng.standard_normal((batch_n, 384)).astype(np.float32)
            ctx_np = rng.standard_normal((batch_n, 384)).astype(np.float32)

            # L2-normalise (the head expects normalised inputs from the encoder).
            char_np /= np.linalg.norm(char_np, axis=-1, keepdims=True).clip(1e-9)
            action_np /= np.linalg.norm(action_np, axis=-1, keepdims=True).clip(1e-9)
            ctx_np /= np.linalg.norm(ctx_np, axis=-1, keepdims=True).clip(1e-9)

            out = sess.run(
                ["delta"],
                {"char_vec": char_np, "action_vec": action_np, "context_vec": ctx_np},
            )[0]
            assert out.shape == (batch_n, 1), f"Wrong shape for batch={batch_n}: {out.shape}"


@pytest.mark.integration
class TestHeadOutputRange:
    """ONNX head outputs are bounded to [-5, +5] (tanh*5 is in the graph)."""

    def test_output_range_100_random_inputs(self, exported_head_onnx_path: Path) -> None:
        """For 100 random L2-normalised input triples, delta ∈ [-5, +5]."""
        import onnxruntime as ort

        sess = ort.InferenceSession(
            str(exported_head_onnx_path), providers=["CPUExecutionProvider"]
        )

        rng = np.random.default_rng(7)
        for _ in range(100):
            char_np = rng.standard_normal((1, 384)).astype(np.float32)
            action_np = rng.standard_normal((1, 384)).astype(np.float32)
            ctx_np = rng.standard_normal((1, 384)).astype(np.float32)
            # L2-normalise.
            char_np /= np.linalg.norm(char_np, axis=-1, keepdims=True).clip(1e-9)
            action_np /= np.linalg.norm(action_np, axis=-1, keepdims=True).clip(1e-9)
            ctx_np /= np.linalg.norm(ctx_np, axis=-1, keepdims=True).clip(1e-9)

            out = sess.run(
                ["delta"],
                {"char_vec": char_np, "action_vec": action_np, "context_vec": ctx_np},
            )[0]
            delta_val = float(out.ravel()[0])
            assert -5.0 <= delta_val <= 5.0, f"delta {delta_val} out of [-5, +5]"

    def test_no_nan_or_inf(self, exported_head_onnx_path: Path) -> None:
        """Edge case: near-zero input vectors should not produce NaN or Inf."""
        import onnxruntime as ort

        sess = ort.InferenceSession(
            str(exported_head_onnx_path), providers=["CPUExecutionProvider"]
        )

        # Use tiny near-zero vectors -- after L2-norm these become unit vectors in a
        # dominated direction, but the raw pathological case is worth testing.
        tiny = np.full((1, 384), 1e-7, dtype=np.float32)
        tiny[0, 0] = 1.0  # Ensure the L2-norm is not exactly zero.
        tiny /= np.linalg.norm(tiny, axis=-1, keepdims=True).clip(1e-9)

        out = sess.run(
            ["delta"],
            {"char_vec": tiny, "action_vec": tiny, "context_vec": tiny},
        )[0]
        delta_val = float(out.ravel()[0])
        assert np.isfinite(delta_val), f"Got non-finite delta: {delta_val}"


@pytest.mark.integration
class TestPipelineParityEndToEnd:
    """Torch pipeline vs ONNX pipeline: max_abs_diff < 1e-2 on 20 rollouts.

    Uses a 20-rollout sub-sample of the 100-rollout gate from the export contract
    to keep CI fast.  The full 100-rollout run is done by the export/verify script.
    """

    def test_pipeline_parity_20_rollouts(
        self,
        exported_head_onnx_path: Path,
        real_encoder_for_head: Any,
        real_head_torch: Any,
    ) -> None:
        from wayfinders.ml.data_gen.rollouts import generate_rollouts
        from wayfinders.ml.export.verify import verify_pipeline_end_to_end

        # Encoder ONNX path: use the shipped artifact if present, otherwise skip.
        encoder_onnx_path = _REPO_ROOT / "artifacts" / "onnx" / "minilm-l6-v2-fp16.onnx"
        if not encoder_onnx_path.is_file():
            pytest.skip(f"Encoder ONNX not found at {encoder_onnx_path}")

        rollouts = list(generate_rollouts(20, seed=999))
        assert len(rollouts) == 20

        result = verify_pipeline_end_to_end(
            torch_encoder=real_encoder_for_head,
            torch_head=real_head_torch,
            encoder_onnx_path=encoder_onnx_path,
            head_onnx_path=exported_head_onnx_path,
            rollouts=rollouts,
            atol=1e-2,
        )

        assert result.passed, (
            f"Pipeline parity FAILED: max_abs_diff={result.max_abs_diff:.6f} "
            f"(gate: <1e-2), violations={result.violations}/{result.n_rollouts}\n"
            f"delta_torch={result.delta_torch}\ndelta_onnx={result.delta_onnx}"
        )
        assert result.max_abs_diff < 1e-2, (
            f"max_abs_diff={result.max_abs_diff:.6f} exceeds 1e-2 gate"
        )
