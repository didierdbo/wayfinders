"""Rollout generation + designer-modifier-table label assignment.

Pipeline (Pax 2026-05-02 §4):
    1. Gymnasium env produces structured trajectories.
    2. The three deterministic renderers run on each tuple.
    3. Encoder pass with prose-hash cache.
    4. Designer modifier table assigns label_Δ from structured tuple.
    5. Train/val/test split by character + scene (no leakage).

M1 ships scaffolding for steps 1, 2, 4. Step 3 (encoder + cache) lives in
``inference/encoder.py`` and is wired into ``training/dataset.py``.
"""
