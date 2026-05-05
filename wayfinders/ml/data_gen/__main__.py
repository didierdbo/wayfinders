"""CLI entry point: ``python -m wayfinders.ml.data_gen --n 10000 --seed 42 --out rollouts.jsonl``."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from wayfinders.ml.data_gen.rollouts import generate_rollouts, serialize_rollouts_jsonl


def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="python -m wayfinders.ml.data_gen",
        description=(
            "Generate UC1 Rollouts (structured + EN-pinned prose + label_delta) "
            "and write them as JSONL. Deterministic from --seed."
        ),
    )
    parser.add_argument(
        "--n",
        type=int,
        default=1000,
        help="Number of rollouts to generate (default: 1000).",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=42,
        help="RNG seed (default: 42). Same (n, seed) -> same output bytes.",
    )
    parser.add_argument(
        "--out",
        type=Path,
        required=True,
        help="Output JSONL path. Parent directory is created if missing.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = _parse_args(argv)
    if args.n < 0:
        print(f"error: --n must be non-negative, got {args.n}", file=sys.stderr)
        return 2
    rollouts = generate_rollouts(n=args.n, seed=args.seed)
    written = serialize_rollouts_jsonl(rollouts, args.out)
    print(f"Wrote {written} rollouts to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
