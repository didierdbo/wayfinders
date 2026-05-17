# halfgate_topdown_plan.py

Procedural generator for the Halfgate top-down B&W architect plan (Wayfinders E2.1d).

Produces a single square PNG — black ink on white — from a hardcoded 8x8 district grid.
No AI, no generative models: everything is deterministic Pillow linework seeded by `--seed`.

## Quick start

```
# Default: 4096px, seed 42, output to Owner's Inbox
uv run --with pillow python tools/asset_generators/halfgate_topdown_plan.py

# Faster iteration
uv run --with pillow python tools/asset_generators/halfgate_topdown_plan.py \
    --resolution 2048 --seed 42 --show-grid

# Custom output path
uv run --with pillow python tools/asset_generators/halfgate_topdown_plan.py \
    --resolution 4096 --seed 7 \
    --output C:/path/to/halfgate.png
```

## CLI options

| Option | Default | Notes |
|---|---|---|
| `--resolution N` | 4096 | Must be divisible by 8. Each cell = N/8 px. |
| `--seed N` | 42 | Seeds all per-cell RNG. Fully reproducible. |
| `--output PATH` | Owner's Inbox/.../halfgate-topdown-procedural-v1.png | Override output path. |
| `--show-grid` | off | Overlay light gray 8x8 grid lines (debug). |

## District map (8x8, hardcoded)

```
        col 0   1   2   3   4   5   6   7
row 0   H   H   O   O   U   U   U   U     north: fields + outskirts
row 1   H   H   O   W   W   U   U   U
row 2   O   W   W   I   I   W   U   U
row 3   O   W   I   I   I   W   W   U     intramuros + wall ring
row 4   W   I   I   I   I   I   W   U
row 5   W   I   I   I   I   W   U   U
row 6   G   G   W   W   W   U   U   L     SW: gateway
row 7   G   G   U   U   L   L   L   L     south + SE: littoral
```

Legend: `I` Intramuros, `W` Wall, `G` Gateway, `O` Outskirts, `H` HinterlandAgri, `L` Littoral, `U` Urban.

## Architecture

- One `render_<district>()` function per district type.
- Two transverse passes after the per-cell loop:
  - `draw_continuous_coastline()` — single sinuous line across all L cells per column.
  - `draw_hedgerows()` — dotted hedge lines at H-cell boundaries.
- `draw_compass()` and `draw_scale_bar()` drawn last.

All per-cell RNG uses `cell_rng(seed, col, row)` — determinism is guaranteed
from `(seed, GRID)`.  Change `--seed` to vary layout without touching code.

## Known v1 limitations

- Intramuros buildings are solid-filled black squares — no internal courtyard detail.
- Gateway portcullis bars are only in the north row (row 6); the south row (row 7) shows
  bridge deck cross-planks only.
- Coastline is column-grouped (vertical strips per L column); if L cells are isolated
  in different columns, each gets its own line — acceptable for the current layout.
- No street network drawn inside Intramuros (alleys are implied by building gaps).
- Compass font falls back to Pillow default if `arial.ttf` is not on PATH.

## Default output path

```
C:\Users\dbo\Desktop\PKA\Owner's Inbox\Media\wayfinders\halfgate-topdown-v1\halfgate-topdown-procedural-v1.png
```
