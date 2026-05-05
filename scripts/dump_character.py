"""Serialize a generated starting pool to JSON for the React fixtures."""

from __future__ import annotations

import json
from dataclasses import asdict
from enum import Enum
from pathlib import Path

import numpy as np

from wayfinders.chargen import generate_starting_pool


def jsonify(obj):
    """Recursively coerce dataclass-derived dicts into JSON-safe shapes.

    Handles: Enum keys and values, tuples, sets. Dataclasses themselves are
    already dicts at this point because we call asdict() at the top level.
    """
    if isinstance(obj, dict):
        return {(k.name if isinstance(k, Enum) else k): jsonify(v) for k, v in obj.items()}
    if isinstance(obj, list | tuple | set):
        return [jsonify(v) for v in obj]
    if isinstance(obj, Enum):
        return obj.name
    return obj


def main() -> None:
    rng = np.random.default_rng(seed=42)  # deterministic fixtures
    pool = generate_starting_pool(3, rng)

    payload = [jsonify(asdict(c)) for c in pool]

    out = Path(__file__).resolve().parents[1] / "web" / "src" / "fixtures" / "characters.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(f"Wrote {len(payload)} characters to {out}")


if __name__ == "__main__":
    main()
