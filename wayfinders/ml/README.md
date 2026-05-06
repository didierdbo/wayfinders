# wayfinders.ml -- UC1 Resolution Pipeline

**Status:** M1 complete (2026-05-06) -- 13/13 substeps done, v0.1 artefacts ready for Rune Phase 6.
**Encoder lock:** `sentence-transformers/all-MiniLM-L6-v2` (frozen, EN-pinned -- see below)

---

## Hard contracts -- read before touching this code

### 1. Encoder lock

**Model:** `sentence-transformers/all-MiniLM-L6-v2`. Frozen for v0.5/v1.
Do not change without updating `Pax - ML Encoder Lock - 2026-05-05.md` and
coordinating with Rune (ONNX artifact would need to be regenerated).

### 2. Approach B (locked 2026-05-01)

```
char_vec  (384) -+
action_vec(384) -+-> concat (1152) + cos(char,action) (1) = 1153 dims
context_vec(384)-+
                  -> Linear(1153,256) -> GELU -> Dropout(0.1)
                  -> Linear(256,64) -> GELU
                  -> Linear(64,1) -> tanh * 5
                  = delta in [-5, +5]
```

~313k params. Only the head trains. Encoder is frozen.

### 3. Renderers are EN-pinned. Always.

The three prose renderers (`schemas/character.py`, `schemas/action.py`,
`schemas/context.py`) emit English text regardless of UI locale. The encoder
is English-only (`all-MiniLM-L6-v2`). Feeding French would silently degrade
embedding quality. The UI localizes downstream; the encoder always sees English.

### 4. Renderers are deterministic, byte-stable, pure functions.

Same input state -> same output string, byte-for-byte, forever.

- No RNG. No dict iteration on insertion order (sort explicitly).
- No live database reads. Renderers take a snapshot.
- No timestamps. No `now()`. The state carries its own time slots.
- No floating-point in output prose (numbers go through `int_to_words()`).

This determinism is what makes hash-caching embeddings across millions of
rollouts valid. Break determinism -> break the cache -> break the training pipeline.

### 5. Loss: Huber (delta=1.0) + monotonicity regularizer

See `training/loss.py`. The monotonicity regularizer is a penalty term on a
held-out probe set (if you add one positive legacy event, delta should not
decrease). Not a hard constraint -- it catches the embarrassing failure mode
where the model learns a non-monotone competence response.

### 6. Train/val/test split is by character_archetype + scene_id, never random.

Random split leaks via cached embeddings. See `data_gen/split.py`.

### 7. Determinism contract

Training is bit-reproducible from `(seed, ruleset_version, encoder_version)`.

### 8. v1 ship gate: MAE <= 0.6 on Varn's 200-500-scenario hand-graded eval set.

M1 v0.1 achieves val MAE=0.9027 on synthetic rollouts (YELLOW gate -- pipeline
validated, quality gate pending Varn hand-graded eval set in M2). The v0.1
artefacts are shippable to Rune for Phase 6 pipeline integration, not for
production quality gate.

---

## Module layout

```
wayfinders/ml/
├── schemas/      Pydantic models + EN renderers (Character, Action, Context)
├── data_gen/     Rollout simulator + designer modifier table -> labels
├── training/     Head MLP, Huber loss, train loop, eval
├── export/       ONNX export for Rune (encoder fp16 + head fp32)
└── inference/    Frozen encoder wrapper + dev-time FastAPI stub
```

---

## Setup

```bash
# At repo root
uv sync --all-extras --dev
```

**Never** `uv sync --extra ml` or any single-extra variant -- that silently drops other extras.

Verify CUDA:

```bash
python -c "import torch; print(torch.__version__, torch.cuda.is_available())"
# expected: 2.6.0+cu124 True
```

If `False`, the torch cu124 wheel is installed but CUDA driver is not accessible.
See `wayfinders/ml/README.md` hardware notes below.

---

## Run training local

### 1. Generate rollouts + populate embedding cache + train head

```bash
uv run python scripts/train_v0_1.py
```

This does all of: generate 50k rollouts (seed=42), encode all prose (caches to
`artifacts/cache/embeddings.npz`), split 80/10/10 by archetype+scene, train
50 epochs, write artefacts.

First run populates the embedding cache: ~15-20 min on RTX A2000 fp16 (1.5M
encoder calls, ~300k unique after dedup). Subsequent runs with `--no-cache`
skipped reuse the cache and take ~2-3 min for training only.

```bash
# Quick smoke test (5k rollouts, 10 epochs)
uv run python scripts/train_v0_1.py --n-rollouts 5000 --epochs 10
```

Artefacts written to `artifacts/models/v0.1/`:
- `head_final.pt` -- final epoch checkpoint (wrapped dict with model_state_dict)
- `metrics.json` -- train/val curves, best val MAE, config
- `test_metrics.json` -- held-out test MAE, RMSE, R^2, per-bucket breakdown
- `gate.json` -- PASS/YELLOW/RED decision + val MAE

---

## Export ONNX

Run from repo root:

```bash
# Encoder (fp16, ~45 MB)
uv run python -m wayfinders.ml.export.export_encoder
# Output: artifacts/onnx/minilm-l6-v2-fp16.onnx
#         artifacts/onnx/minilm-l6-v2-fp16.manifest.json

# Head (fp32, ~1.25 MB)
uv run python -m wayfinders.ml.export.export_head
# Output: artifacts/onnx/resolution-head-v0.1.onnx
#         artifacts/onnx/resolution-head-v0.1.manifest.json
```

Both require CUDA (`torch.cuda.is_available() == True`). The CUDA requirement is
intentional: a CPU-exported graph would diverge numerically from the CUDA
training environment.

---

## Verify parity

```bash
# Encoder-only parity (torch CUDA fp16 vs ORT CPU fp32)
# Expected: max_abs_diff < 1e-3 on 100 prose docs
uv run python -c "
from pathlib import Path
from wayfinders.ml.inference.encoder import MiniLMEncoder
from wayfinders.ml.data_gen.rollouts import generate_rollouts
from wayfinders.ml.export.verify import verify_encoder_parity

enc = MiniLMEncoder()
rollouts = list(generate_rollouts(100, seed=999))
proses = [r.prose_character for r in rollouts]
result = verify_encoder_parity(enc, Path('artifacts/onnx/minilm-l6-v2-fp16.onnx'), proses)
print(result)
"

# End-to-end pipeline parity (ONNX encoder -> torch head vs ONNX encoder -> ONNX head)
# Expected: max_abs_diff < 1e-2 on 100 rollouts
uv run python -c "
import torch
from pathlib import Path
from wayfinders.ml.inference.encoder import MiniLMEncoder
from wayfinders.ml.training.head import ResolutionHead
from wayfinders.ml.data_gen.rollouts import generate_rollouts
from wayfinders.ml.export.verify import verify_pipeline_end_to_end

enc = MiniLMEncoder()
head = ResolutionHead()
raw = torch.load('artifacts/models/v0.1/head_final.pt', map_location='cuda', weights_only=True)
head.load_state_dict(raw['model_state_dict'])
head.eval(); head.cuda()

rollouts = list(generate_rollouts(100, seed=999))
result = verify_pipeline_end_to_end(
    torch_encoder=enc, torch_head=head,
    encoder_onnx_path=Path('artifacts/onnx/minilm-l6-v2-fp16.onnx'),
    head_onnx_path=Path('artifacts/onnx/resolution-head-v0.1.onnx'),
    rollouts=rollouts,
)
print(result)
"
```

**Tolerances:**
- Encoder: `max_abs_diff < 1e-3` on 384-dim embedding vectors.
- End-to-end pipeline: `max_abs_diff < 1e-2` on delta scalars.

Note: the pipeline parity test uses the ONNX encoder (fp32 CPU) for both paths.
This isolates head export parity from the fp16/fp32 encoder precision gap (which
is ~1.8e-4 per element, already tested by the encoder-parity check). See
`verify_pipeline_end_to_end()` docstring for the design rationale.

---

## Versioning convention for artefacts

- File naming: `vMAJOR.MINOR` in the file name (e.g. `resolution-head-v0.1.onnx`).
- Manifest sidecar: `.manifest.json` alongside every `.onnx` file.
  Contains: sha256 of both the ONNX file and the source checkpoint,
  training metrics, encoder coupling trace, input/output schema.
- **MINOR bump:** retrained on new data, same I/O schema. Drop-in replacement for Rune.
- **MAJOR bump:** I/O schema change (new input vector, new output shape, different delta range).
  Breaking change for Rune -- requires coordinated update.
- Encoder version follows HF revision. We do not bump unless the model changes.

---

## M1 status (current)

**13/13 substeps complete. M1 closed.**

| Artefact | Path | Size | sha256 (first 16) |
|---|---|---|---|
| Encoder ONNX fp16 | `artifacts/onnx/minilm-l6-v2-fp16.onnx` | ~45 MB | `c9ba7cae1af741d9...` |
| Head ONNX fp32 | `artifacts/onnx/resolution-head-v0.1.onnx` | 1.25 MB | `2df84ffb1844fee9...` |
| Parity fixture | `parity/python_reference_v0.1.json` | ~42 KB | 20 canonical cases |

v0.1 metrics (synthetic rollouts, seed=42):
- val MAE: 0.9027 (YELLOW -- above 0.8 gate, below 1.0 escalation threshold)
- test MAE: 0.9893
- test R^2: 0.7938

**Quality gate (MAE <= 0.6 on Varn hand-graded eval set) is M2**, not M1.
The v0.1 artefacts are shippable to Rune for Phase 6 pipeline integration.

---

## Hardware notes

Training target: NVIDIA RTX A2000 (4 GB VRAM) + 32 GB system RAM.

- Frozen encoder + 313k-param head fits with margin.
- Batch size 64 for training is comfortable; 256 is fine if embedding cache is warm.
- Encoder forward at fp16 is ~5-8 ms/doc on the A2000; cache aggressively.
- ONNX inference on Rune's Steam target is CPU-bound (no CUDA-only ops in the graph).

`torch` is pinned to the PyTorch **cu124** index in `pyproject.toml` via
`[tool.uv.sources]`. This prevents `uv sync` from silently pulling the `+cpu`
build that PyPI serves by default on Windows.

---

## Hard contract reference

- Encoder lock: `Pax - ML Encoder Lock - 2026-05-05.md`
- Architecture spec: `Pax - UC1 Classifier Architecture - 2026-05-02.md`
- ONNX export contract: `Coda - ONNX Export Contract - 2026-05-05.md`
- Prose schemas: `Varn - UC1 Character/Action/Context Prose Schema` (2026-04-30, 2026-05-01)
