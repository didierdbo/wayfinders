"""Regenerate parity/python_reference_v0.1.json via the ONNX end-to-end path.

Replaces the deprecated ``delta_torch_v0_1`` column (computed against the torch
fp32 encoder) with ``delta_onnx_e2e_v0_1`` (computed against the fp16 ONNX
encoder on CPU EP, which is the production path Rune's C# client follows).

Pipeline (canonical):
    tokenise  -> encoder ONNX fp16 (CPU EP)  -> fp16 embedding
    cast fp32 -> head ONNX fp32   (CPU EP)   -> delta scalar

This is a one-time regen script.  Re-run it if either ONNX artifact changes
(which would require a Pax decision to move encoder or head pin).

Usage:
    uv run python scripts/regenerate_parity_reference.py

References:
    - Coda - L3 Parity Fixture Diagnosis - 2026-05-06.md  (diagnosis + plan)
    - Pax  - L4 Parity Gate Decision    - 2026-05-06.md   (gate values)
    - Phase 6 L3 Closeout (Rune §3)                       (observed values)
"""

from __future__ import annotations

import hashlib
import json
import logging
from pathlib import Path

import numpy as np
import onnxruntime as ort
from sentence_transformers import SentenceTransformer

logging.basicConfig(level=logging.INFO, format="%(message)s")
logger = logging.getLogger(__name__)

REPO_ROOT = Path(__file__).resolve().parent.parent
ENCODER_ONNX = REPO_ROOT / "artifacts" / "onnx" / "minilm-l6-v2-fp16.onnx"
HEAD_ONNX = REPO_ROOT / "artifacts" / "onnx" / "resolution-head-v0.1.onnx"
FIXTURE_PATH = REPO_ROOT / "parity" / "python_reference_v0.1.json"

# Expected sha256 values guard against accidentally using a stale artifact.
EXPECTED_ENCODER_SHA256 = "c9ba7cae1af741d9702daa7e314b287c67bd0d7c8bc68f4315a633973161b769"
EXPECTED_HEAD_SHA256 = "2df84ffb1844fee93f5a32358177ab24804a5fc0c4a5b1d06b478aa2b60b59ec"

# Pre-flight targets (Rune's actual C# measurements, Phase 6 L3 closeout §3).
KIRA_PREFLIGHT: dict[str, float] = {
    "kira_stealth_ridge": +4.27518177,
    "kira_stealth_festival": -1.95026767,
}
PREFLIGHT_ATOL = 1e-7


def sha256_of_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def build_sessions() -> tuple[ort.InferenceSession, ort.InferenceSession]:
    for artifact, expected in [
        (ENCODER_ONNX, EXPECTED_ENCODER_SHA256),
        (HEAD_ONNX, EXPECTED_HEAD_SHA256),
    ]:
        if not artifact.is_file():
            raise SystemExit(f"Artifact not found: {artifact}")
        actual = sha256_of_file(artifact)
        if actual != expected:
            raise SystemExit(
                f"sha256 mismatch for {artifact.name}:\n"
                f"  expected: {expected}\n"
                f"  actual:   {actual}"
            )
        logger.info("sha256 OK: %s", artifact.name)

    enc_sess = ort.InferenceSession(str(ENCODER_ONNX), providers=["CPUExecutionProvider"])
    head_sess = ort.InferenceSession(str(HEAD_ONNX), providers=["CPUExecutionProvider"])
    return enc_sess, head_sess


def build_tokenizer() -> object:
    st_model = SentenceTransformer(
        "sentence-transformers/all-MiniLM-L6-v2",
        device="cpu",
    )
    return st_model[0].tokenizer


def encode(sess: ort.InferenceSession, tokenizer: object, prose: str) -> np.ndarray:
    """Tokenise + run encoder ONNX (CPU EP) -> (1, 384) float32 embedding."""
    enc_inputs = tokenizer(
        [prose],
        return_tensors="pt",
        padding=True,
        truncation=True,
        max_length=256,
    )
    out = sess.run(
        ["sentence_embedding"],
        {
            "input_ids": enc_inputs["input_ids"].numpy(),
            "attention_mask": enc_inputs["attention_mask"].numpy(),
            "token_type_ids": enc_inputs["token_type_ids"].numpy(),
        },
    )[0]
    # out is fp16 (encoder dtype); cast to fp32 before feeding the head.
    return out.astype(np.float32).reshape(1, 384)


def run_head(
    head_sess: ort.InferenceSession,
    char_vec: np.ndarray,
    action_vec: np.ndarray,
    context_vec: np.ndarray,
) -> float:
    """Run head ONNX (CPU EP) on three (1,384) float32 vectors -> scalar delta."""
    delta = head_sess.run(
        ["delta"],
        {
            "char_vec": char_vec,
            "action_vec": action_vec,
            "context_vec": context_vec,
        },
    )[0]
    return float(delta[0, 0])


def main() -> None:
    if not FIXTURE_PATH.is_file():
        raise SystemExit(f"Fixture not found: {FIXTURE_PATH}")

    enc_sess, head_sess = build_sessions()
    tokenizer = build_tokenizer()

    fixture = json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))
    records = fixture["records"]
    logger.info("Loaded %d records from fixture.", len(records))

    # --- Pre-flight: validate Kira cases against Rune's measured actuals ---
    logger.info(
        "Pre-flight: checking Kira cases against Rune L3 actuals (atol=%.0e) ...", PREFLIGHT_ATOL
    )
    record_map = {r["id"]: r for r in records}
    for case_id, expected_val in KIRA_PREFLIGHT.items():
        rec = record_map[case_id]
        char_vec = encode(enc_sess, tokenizer, rec["prose_character"])
        action_vec = encode(enc_sess, tokenizer, rec["prose_action"])
        context_vec = encode(enc_sess, tokenizer, rec["prose_context"])
        val = run_head(head_sess, char_vec, action_vec, context_vec)
        diff = abs(val - expected_val)
        status = "OK" if diff <= PREFLIGHT_ATOL else "FAIL"
        logger.info(
            "  [%s] %s: got=%.8f expected=%.8f diff=%.2e",
            status,
            case_id,
            val,
            expected_val,
            diff,
        )
        if diff > PREFLIGHT_ATOL:
            raise SystemExit(
                f"Pre-flight FAILED for {case_id}: diff={diff:.2e} > atol={PREFLIGHT_ATOL:.0e}. "
                "Stop -- something upstream is wrong. Ping Coda."
            )
    logger.info("Pre-flight passed. Proceeding with full 20-record regen.")

    # --- Full regen ---
    encoder_sha = sha256_of_file(ENCODER_ONNX)
    head_sha = sha256_of_file(HEAD_ONNX)

    old_deltas: dict[str, float] = {}
    new_records = []
    for rec in records:
        case_id = rec["id"]
        char_vec = encode(enc_sess, tokenizer, rec["prose_character"])
        action_vec = encode(enc_sess, tokenizer, rec["prose_action"])
        context_vec = encode(enc_sess, tokenizer, rec["prose_context"])
        new_delta = run_head(head_sess, char_vec, action_vec, context_vec)

        old_delta = rec.get("delta_torch_v0_1")
        old_deltas[case_id] = old_delta  # keep for comparison logging
        diff_vs_old = abs(new_delta - old_delta) if old_delta is not None else float("nan")
        logger.info(
            "  %s: onnx=%.8f  torch=%.8f  diff=%.4e",
            case_id,
            new_delta,
            old_delta if old_delta is not None else float("nan"),
            diff_vs_old,
        )

        new_rec = {k: v for k, v in rec.items() if k != "delta_torch_v0_1"}
        new_rec["delta_onnx_e2e_v0_1"] = new_delta
        new_records.append(new_rec)

    # --- Build updated fixture ---
    new_fixture = {
        "schema_version": fixture["schema_version"],
        "description": (
            "Reference deltas from the production ONNX pipeline: "
            "encoder fp16 ONNX (CPU EP) -> head fp32 ONNX (CPU EP). "
            "Rune's C# implementation must match within atol=1e-4 (head-only) "
            "and atol=5e-2 (end-to-end composed). "
            "See Pax L4 Parity Gate Decision 2026-05-06 for the three-layer gate spec."
        ),
        "pipeline_version": fixture["pipeline_version"],
        "head_sha256": fixture["head_sha256"],
        "expected_parity_atol": 0.0001,  # head-only gate (Pax L4 decision)
        "expected_e2e_atol": 0.05,  # end-to-end gate (Pax L4 decision, fp16 envelope)
        "pipeline_provenance": {
            "encoder_onnx": "artifacts/onnx/minilm-l6-v2-fp16.onnx",
            "encoder_onnx_sha256": encoder_sha,
            "head_onnx": "artifacts/onnx/resolution-head-v0.1.onnx",
            "head_onnx_sha256": head_sha,
            "generator_script": "scripts/regenerate_parity_reference.py",
            "encoder_provider": "CPUExecutionProvider",
            "head_provider": "CPUExecutionProvider",
            "notes": (
                "Replaces delta_torch_v0_1 (torch fp32 encoder path, wrong baseline). "
                "See Coda - L3 Parity Fixture Diagnosis - 2026-05-06.md."
            ),
        },
        "records": new_records,
    }

    FIXTURE_PATH.write_text(json.dumps(new_fixture, indent=2), encoding="utf-8")
    logger.info("\nWrote %s (%d records).", FIXTURE_PATH, len(new_records))
    logger.info("Column renamed: delta_torch_v0_1 -> delta_onnx_e2e_v0_1")

    # --- Summary table ---
    logger.info("\n%-35s %12s %12s %12s", "record_id", "onnx_e2e", "torch_v0_1", "diff")
    logger.info("-" * 75)
    max_diff = 0.0
    for rec, new_rec in zip(records, new_records, strict=True):
        old = rec.get("delta_torch_v0_1", float("nan"))
        new = new_rec["delta_onnx_e2e_v0_1"]
        diff = abs(new - old)
        max_diff = max(max_diff, diff)
        logger.info("%-35s %12.6f %12.6f %12.4e", rec["id"], new, old, diff)
    logger.info("-" * 75)
    logger.info("max |onnx - torch| across 20 records: %.4e", max_diff)


if __name__ == "__main__":
    main()
