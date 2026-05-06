"""Dump reference encoder embeddings for the first two parity-fixture cases.

Phase 6 L2 deliverable, Rune side. The committed parity fixture
(``parity/python_reference_v0.1.json``) only contains end-to-end deltas, not
encoder embeddings. C# encoder-only parity validation needs the 384-dim
reference vectors.

This script reuses Tess's existing ONNX encoder + the canonical sentence-
transformers tokenizer to produce a derived artifact:

    parity/encoder_embeddings_v0.1.json

Six records: 2 cases (kira_stealth_ridge, kira_stealth_festival) x 3 docs
(character, action, context).

Run once, commit the JSON. Re-run if the encoder ONNX file ever changes
(would only happen if the encoder pin moves, which Pax has locked for
the foreseeable future per encoder-lock §3).

Usage:

    uv run python scripts/dump_parity_embeddings.py
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
PARITY_FIXTURE = REPO_ROOT / "parity" / "python_reference_v0.1.json"
OUT_PATH = REPO_ROOT / "parity" / "encoder_embeddings_v0.1.json"

# Cases to dump -- the first two of the fixture (Kira canonical, per Tess §5.1).
CASE_IDS = ["kira_stealth_ridge", "kira_stealth_festival"]


def sha256_of_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> None:
    if not ENCODER_ONNX.is_file():
        raise SystemExit(f"Encoder ONNX not found: {ENCODER_ONNX}")
    if not PARITY_FIXTURE.is_file():
        raise SystemExit(f"Parity fixture not found: {PARITY_FIXTURE}")

    encoder_sha = sha256_of_file(ENCODER_ONNX)
    logger.info("Encoder ONNX sha256: %s", encoder_sha)

    fixture = json.loads(PARITY_FIXTURE.read_text(encoding="utf-8"))
    fixture_records = {rec["id"]: rec for rec in fixture["records"]}

    # Build session + tokenizer (mirrors verify.py).
    sess = ort.InferenceSession(
        str(ENCODER_ONNX),
        providers=["CPUExecutionProvider"],
    )
    st_model = SentenceTransformer(
        "sentence-transformers/all-MiniLM-L6-v2",
        device="cpu",
    )
    tokenizer = st_model[0].tokenizer

    out_records = []
    for case_id in CASE_IDS:
        if case_id not in fixture_records:
            raise SystemExit(f"Fixture missing case id: {case_id}")
        rec = fixture_records[case_id]
        for doc_role in ("character", "action", "context"):
            prose = rec[f"prose_{doc_role}"]
            enc_inputs = tokenizer(
                [prose],
                return_tensors="pt",
                padding=True,
                truncation=True,
                max_length=256,
            )
            ort_out = sess.run(
                ["sentence_embedding"],
                {
                    "input_ids": enc_inputs["input_ids"].numpy(),
                    "attention_mask": enc_inputs["attention_mask"].numpy(),
                    "token_type_ids": enc_inputs["token_type_ids"].numpy(),
                },
            )[0]
            emb_fp32 = ort_out.astype(np.float32).reshape(-1)  # (384,)
            assert emb_fp32.shape == (384,), f"unexpected emb shape: {emb_fp32.shape}"
            norm = float(np.linalg.norm(emb_fp32))
            logger.info(
                "  %s/%s: norm=%.6f token_count=%d",
                case_id,
                doc_role,
                norm,
                int(enc_inputs["input_ids"].shape[1]),
            )
            out_records.append(
                {
                    "id": case_id,
                    "doc": doc_role,
                    "prose": prose,
                    "token_count": int(enc_inputs["input_ids"].shape[1]),
                    "embedding": [float(x) for x in emb_fp32.tolist()],
                    "norm": norm,
                }
            )

    # Also dump tokenizer-IDs reference for the synthetic accent test.
    # These two strings are the C# NFD/accent verification vector.
    accent_tests = [
        "Senna a deja traverse la riviere.",  # ASCII control
        "Senna a déjà traversé la rivière.",  # accented
        "L'eclaireur resiste a l'attaque.",
        "L'éclaireur résiste à l'attaque.",
    ]
    accent_records = []
    for text in accent_tests:
        enc = tokenizer(
            [text],
            return_tensors="pt",
            padding=True,
            truncation=True,
            max_length=256,
        )
        ids = enc["input_ids"].numpy().reshape(-1).tolist()
        accent_records.append(
            {
                "text": text,
                "token_ids": [int(x) for x in ids],
            }
        )
        logger.info("  accent-test '%s' -> %d tokens", text, len(ids))

    out = {
        "schema_version": "1.0",
        "description": "Reference encoder embeddings for Rune C# parity (L2). "
        "Six records (2 fixture cases x 3 prose docs) + synthetic accent token-ID reference.",
        "encoder_onnx_sha256": encoder_sha,
        "atol": 1.0e-3,
        "embeddings": out_records,
        "accent_token_ids_reference": accent_records,
    }

    OUT_PATH.write_text(json.dumps(out, indent=2), encoding="utf-8")
    logger.info(
        "Wrote %s (%d embeddings, %d accent records)",
        OUT_PATH,
        len(out_records),
        len(accent_records),
    )


if __name__ == "__main__":
    main()
