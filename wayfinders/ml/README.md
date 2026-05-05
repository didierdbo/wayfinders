# wayfinders.ml — UC1 Resolution Pipeline

**Status:** M1 scaffold (2026-05-05)
**Owner:** Coda (Python/ML coach) → handed to Didier
**Encoder lock:** `sentence-transformers/all-MiniLM-L6-v2` (frozen, see `Pax - ML Encoder Lock - 2026-05-05.md`)

---

## Hard contract — read before touching this code

### 1. Renderers are EN-pinned. Always.

The three prose renderers (`schemas/character.py`, `schemas/action.py`, `schemas/context.py`)
**emit English text regardless of UI locale**. The UI may display French to the player
through `success_template` / `failure_template` localization tables, but the
encoder always sees English.

**Why:** `all-MiniLM-L6-v2` is English-only. Feeding it French would silently
degrade embedding quality. The model lives in English ; the player-facing text
is localized downstream.

**If you need encoder consumption of non-English prose later** (e.g. v3 free-form
player input), swap the encoder to `paraphrase-multilingual-MiniLM-L12-v2`
(drop-in 384-dim replacement). Do not branch the renderers on locale.

### 2. Renderers are deterministic, byte-stable, pure functions.

Same input state → same output string, byte-for-byte, forever.

- No RNG. No `dict` iteration on insertion order — sort explicitly.
- No live database reads. The renderer takes a snapshot.
- No timestamps. No "now()". The state carries its own time slots.
- No floating-point in the output prose. Numbers go through `int_to_words()`
  (cap at "ninety-nine", then "more than a hundred").

This determinism is what lets us hash-cache embeddings across millions of
rollouts. Break determinism, break the cache, break the training pipeline.

### 3. Encoder is FROZEN. Only the head trains.

Do not call `.train()` on the encoder. Do not unfreeze its parameters. The M1
training loop only updates `ResolutionHead`. Encoder fine-tuning is a v2+
question.

### 4. Tonal palette: Pratchett, not Naheulbeuk.

Templates (`success_template`, `failure_template`, manner phrases, stakes
phrases) live in the Pratchett palette: humor with stakes, dry observation,
never parodic, never gag. See `Game-Visual-Brief-W4-Amendment-Naheulbeuk-Removal.md`.

---

## Module layout

```
wayfinders/ml/
├── schemas/      Pydantic models + EN renderers (Character, Action, Context)
├── data_gen/     Rollout simulator + designer modifier table → labels
├── training/     Head MLP, Huber loss, train loop, eval
├── export/       ONNX export for Rune (encoder fp16 + head fp32)
└── inference/    Frozen encoder wrapper + dev-time FastAPI stub
```

Each subpackage has its own README with deeper context.

---

## How to run things

### Setup (one-time)

```bash
# At repo root
uv sync                              # or: pip install -e .[dev]
uv run pytest tests/ml/              # smoke check
```

### Render a character / action / context doc by hand

```python
from wayfinders.ml.schemas.character import CharacterState, render_character
from wayfinders.ml.schemas.action import ActionCard, OppositionState, render_action
from wayfinders.ml.schemas.context import SceneState, PartyState, WorldState, CampaignState, render_context

# Build a CharacterState (see schemas/character.py for the full Pydantic model)
kira = CharacterState(...)
print(render_character(kira))
```

### Generate rollouts

```bash
uv run python -m wayfinders.ml.data_gen.rollouts --n 50000 --seed 42 --out artifacts/rollouts/v1.parquet
```

### Train the head

```bash
uv run python -m wayfinders.ml.training.train \
    --rollouts artifacts/rollouts/v1.parquet \
    --epochs 50 \
    --batch-size 256 \
    --lr 3e-4 \
    --out artifacts/models/head-v0.1.pt
```

### Export to ONNX

```bash
uv run python -m wayfinders.ml.export.export_encoder \
    --precision fp16 \
    --out artifacts/onnx/minilm-l6-v2-fp16.onnx

uv run python -m wayfinders.ml.export.export_head \
    --checkpoint artifacts/models/head-v0.1.pt \
    --out artifacts/onnx/resolution-head-v0.1.onnx

uv run python -m wayfinders.ml.export.verify \
    --encoder artifacts/onnx/minilm-l6-v2-fp16.onnx \
    --head artifacts/onnx/resolution-head-v0.1.onnx
```

Parity tests must pass before tagging an artefact. See
`Coda - ONNX Export Contract - 2026-05-05.md`.

---

## Hardware notes

Training target: NVIDIA RTX A2000 (4 GB VRAM) + 32 GB system RAM.

- Frozen encoder + 313k-param head fits with margin.
- Batch size 256 for the head is comfortable ; 512 if you want to push.
- Encoder forward at fp16 is ~5-8 ms/doc on the A2000 ; cache aggressively.
- ONNX inference on Rune's Steam target is CPU-bound, not GPU — so build artefacts
  with CPU runtime in mind (no CUDA-only ops in the graph).
