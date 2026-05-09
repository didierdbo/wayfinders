"""UC1 hand-graded evaluation harness.

This package owns the pipeline that measures model quality against
designer-rated scenarios -- the *real* judge for the M2 ship gate.

Submodules:
  schema   -- Pydantic v2 models for the JSONL dataset format.
  harness  -- Evaluation pipeline: load dataset, run Predictor, compute stats.
  __main__ -- CLI entry point (``python -m wayfinders.ml.eval``).

Dataset location:
  wayfinders/ml/eval/datasets/
    stub_v0.jsonl       -- 15-item synthetic stub (placeholder until Varn delivers)
    hand_graded_v1.jsonl  -- Varn's 200-500 scenario set (drop here when ready)

Gate thresholds (Pax spec + M2 brief):
  MAE <= 0.6  -- architecture gate (PASS)
  MAE <= 0.4  -- ship gate (SHIP)
"""
