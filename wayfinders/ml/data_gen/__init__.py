"""Rollout generation + designer-modifier-table label assignment.

Pipeline (Pax 2026-05-02 sec. 4):
    1. Gymnasium env produces structured trajectories.
    2. The three deterministic renderers run on each tuple.
    3. Encoder pass with prose-hash cache.
    4. Designer modifier table assigns label_delta from structured tuple.
    5. Train/val/test split by character + scene (no leakage).

M1 ships:
    - Steps 1, 2, 4 (rollouts + renderers + designer modifiers)
    - Step 3 cache (ProseHashCache in cache.py)
    - Policy layer (RandomPolicy, ScriptedPolicy, MixedPolicy in policies.py)
"""

from wayfinders.ml.data_gen.cache import ProseHashCache
from wayfinders.ml.data_gen.designer_modifiers import (
    DELTA_MAX,
    DELTA_MIN,
    compute_delta,
    compute_label,
)
from wayfinders.ml.data_gen.policies import MixedPolicy, Policy, RandomPolicy, ScriptedPolicy
from wayfinders.ml.data_gen.rollouts import (
    Rollout,
    generate_rollouts,
    load_rollouts_jsonl,
    serialize_rollouts_jsonl,
)

__all__ = [
    "DELTA_MAX",
    "DELTA_MIN",
    "MixedPolicy",
    "Policy",
    "ProseHashCache",
    "RandomPolicy",
    "Rollout",
    "ScriptedPolicy",
    "compute_delta",
    "compute_label",
    "generate_rollouts",
    "load_rollouts_jsonl",
    "serialize_rollouts_jsonl",
]
