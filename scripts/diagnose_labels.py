"""Label distribution diagnostic for UC1 training data.

Generates a batch of rollouts (default 10k, fast) and analyses the label_delta
distribution produced by the Varn v0.5 designer modifier table.  The output
surfaces the calibration issues flagged as xfail in tests/ml/data_gen/
test_designer_modifiers.py and test_rollouts.py (issue #13).

Produces two artefacts:
  artifacts/diagnostics/label_distribution.json   -- structured data
  artifacts/diagnostics/label_distribution.txt    -- human-readable summary

The JSON report is what Larry/Varn need for the calibration conversation.

Usage:
    uv run python scripts/diagnose_labels.py
    uv run python scripts/diagnose_labels.py --n-rollouts 50000  # full training set
    uv run python scripts/diagnose_labels.py --seed 123
"""

from __future__ import annotations

import argparse
import json
import logging
import statistics
import sys
from pathlib import Path

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s  %(levelname)-8s  %(name)s  %(message)s",
    datefmt="%H:%M:%S",
    stream=sys.stdout,
)
logger = logging.getLogger("diagnose_labels")

REPO_ROOT = Path(__file__).resolve().parent.parent
DIAG_DIR = REPO_ROOT / "artifacts" / "diagnostics"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="UC1 label distribution diagnostic")
    p.add_argument(
        "--n-rollouts",
        type=int,
        default=10_000,
        help="Number of rollouts to generate (default: 10000)",
    )
    p.add_argument(
        "--seed",
        type=int,
        default=42,
        help="RNG seed (default: 42)",
    )
    p.add_argument(
        "--policy",
        choices=["random", "scripted", "mixed"],
        default="mixed",
        help="Sampling policy (default: mixed, matches training)",
    )
    return p.parse_args()


def bucket_histogram(deltas: list[float], n_buckets: int = 20) -> list[dict[str, object]]:
    """Return a histogram over [-5, +5] in n_buckets equal-width bins."""
    lo, hi = -5.0, 5.0
    width = (hi - lo) / n_buckets
    counts = [0] * n_buckets
    for d in deltas:
        idx = min(int((d - lo) / width), n_buckets - 1)
        counts[idx] = counts[idx] + 1
    total = len(deltas)
    result: list[dict[str, object]] = []
    for i, c in enumerate(counts):
        bin_lo = lo + i * width
        bin_hi = bin_lo + width
        result.append(
            {
                "bin_lo": round(bin_lo, 2),
                "bin_hi": round(bin_hi, 2),
                "count": c,
                "pct": round(100.0 * c / total, 2) if total > 0 else 0.0,
            }
        )
    return result


def saturation_rate(deltas: list[float], threshold: float = 4.5) -> float:
    """Fraction of labels where |delta| >= threshold (near-saturation)."""
    return sum(1 for d in deltas if abs(d) >= threshold) / len(deltas) if deltas else 0.0


def char_class_breakdown(
    rollouts: list,  # list[Rollout]
) -> dict[str, dict[str, object]]:
    """Per-char_class label statistics."""
    from collections import defaultdict

    by_class: dict[str, list[float]] = defaultdict(list)
    for r in rollouts:
        char_class = r.character.char_class
        if r.label_delta is not None:
            by_class[char_class].append(r.label_delta)

    result: dict[str, dict[str, object]] = {}
    for char_class, vals in sorted(by_class.items()):
        result[char_class] = {
            "count": len(vals),
            "mean": round(statistics.mean(vals), 4),
            "stdev": round(statistics.stdev(vals) if len(vals) > 1 else 0.0, 4),
            "min": round(min(vals), 4),
            "max": round(max(vals), 4),
            "saturation_pct": round(100.0 * saturation_rate(vals), 2),
        }
    return result


def verb_breakdown(
    rollouts: list,  # list[Rollout]
) -> dict[str, dict[str, object]]:
    """Per-verb label statistics."""
    from collections import defaultdict

    by_verb: dict[str, list[float]] = defaultdict(list)
    for r in rollouts:
        verb = r.action.action_phrase
        if r.label_delta is not None:
            by_verb[verb].append(r.label_delta)

    result: dict[str, dict[str, object]] = {}
    for verb, vals in sorted(by_verb.items()):
        result[verb] = {
            "count": len(vals),
            "pct_of_total": round(100.0 * len(vals) / len(rollouts), 2),
            "mean": round(statistics.mean(vals), 4),
            "stdev": round(statistics.stdev(vals) if len(vals) > 1 else 0.0, 4),
            "saturation_pct": round(100.0 * saturation_rate(vals), 2),
        }
    return result


def main() -> None:
    args = parse_args()

    from wayfinders.ml.data_gen.policies import MixedPolicy, RandomPolicy, ScriptedPolicy
    from wayfinders.ml.data_gen.rollouts import generate_rollouts

    policy_map = {
        "random": RandomPolicy(),
        "scripted": ScriptedPolicy(),
        "mixed": MixedPolicy(),
    }
    policy = policy_map[args.policy]

    logger.info(
        "Generating %d rollouts (seed=%d, policy=%s) ...",
        args.n_rollouts,
        args.seed,
        args.policy,
    )
    rollouts = list(generate_rollouts(args.n_rollouts, seed=args.seed, policy=policy))
    logger.info("Generated %d rollouts", len(rollouts))

    deltas = [r.label_delta for r in rollouts if r.label_delta is not None]
    n = len(deltas)

    if n == 0:
        logger.error("No labelled rollouts -- aborting")
        sys.exit(1)

    # ------------------------------------------------------------------
    # Compute summary statistics
    # ------------------------------------------------------------------
    mean_delta = statistics.mean(deltas)
    stdev_delta = statistics.stdev(deltas) if n > 1 else 0.0
    median_delta = statistics.median(deltas)
    min_delta = min(deltas)
    max_delta = max(deltas)

    sat_rate_45 = saturation_rate(deltas, threshold=4.5)
    sat_rate_40 = saturation_rate(deltas, threshold=4.0)
    sat_rate_30 = saturation_rate(deltas, threshold=3.0)

    # Centering check: how far is the mean from 0?
    center_bias = abs(mean_delta)

    # Bucket breakdown: negative / near-zero / positive
    n_negative = sum(1 for d in deltas if d < -0.5)
    n_near_zero = sum(1 for d in deltas if -0.5 <= d <= 0.5)
    n_positive = sum(1 for d in deltas if d > 0.5)

    histogram = bucket_histogram(deltas, n_buckets=20)
    arch_stats = char_class_breakdown(rollouts)
    verb_stats = verb_breakdown(rollouts)

    # ------------------------------------------------------------------
    # Gate evaluation (Varn v0.5 targets from xfail tests)
    # ------------------------------------------------------------------
    # Target: saturation < 5% at |delta| >= 4.5, mean close to 0, stdev reasonable.
    sat_gate_pass = sat_rate_45 < 0.05
    center_gate_pass = center_bias < 0.3
    stdev_gate_pass = 1.0 <= stdev_delta <= 3.0

    gates: dict[str, object] = {
        "saturation_lt_5pct_at_4.5": {
            "pass": sat_gate_pass,
            "actual_pct": round(100.0 * sat_rate_45, 2),
            "target_pct": 5.0,
        },
        "mean_centered_within_0.3": {
            "pass": center_gate_pass,
            "actual_mean": round(mean_delta, 4),
            "target_max_abs": 0.3,
        },
        "stdev_in_range_1.0_3.0": {
            "pass": stdev_gate_pass,
            "actual_stdev": round(stdev_delta, 4),
            "target_range": [1.0, 3.0],
        },
    }

    all_gates_pass = sat_gate_pass and center_gate_pass and stdev_gate_pass

    # ------------------------------------------------------------------
    # Assemble JSON report
    # ------------------------------------------------------------------
    report: dict[str, object] = {
        "version": "Varn UC1 Designer Modifier Table v0.6",
        "n_rollouts": n,
        "policy": args.policy,
        "seed": args.seed,
        "summary": {
            "mean": round(mean_delta, 4),
            "stdev": round(stdev_delta, 4),
            "median": round(median_delta, 4),
            "min": round(min_delta, 4),
            "max": round(max_delta, 4),
            "saturation_pct_at_4.5": round(100.0 * sat_rate_45, 2),
            "saturation_pct_at_4.0": round(100.0 * sat_rate_40, 2),
            "saturation_pct_at_3.0": round(100.0 * sat_rate_30, 2),
            "pct_negative": round(100.0 * n_negative / n, 2),
            "pct_near_zero": round(100.0 * n_near_zero / n, 2),
            "pct_positive": round(100.0 * n_positive / n, 2),
        },
        "gates": gates,
        "overall_gate": "PASS" if all_gates_pass else "FAIL",
        "histogram_20bins": histogram,
        "char_class_breakdown": arch_stats,
        "verb_breakdown": verb_stats,
        "interpretation": (
            "A well-calibrated modifier table should have: saturation < 5% at |Δ|≥4.5, "
            "mean near 0 (labels balanced between positive and negative), "
            "stdev in [1.0, 3.0] (spread across the meaningful range without "
            "clustering at extremes). If saturation is high or distribution is "
            "skewed, the model will struggle to learn the middle ground and MAE "
            "will plateau well above the 0.6 ship gate."
        ),
    }

    # ------------------------------------------------------------------
    # Write artefacts
    # ------------------------------------------------------------------
    DIAG_DIR.mkdir(parents=True, exist_ok=True)
    json_path = DIAG_DIR / "label_distribution.json"
    txt_path = DIAG_DIR / "label_distribution.txt"

    json_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    logger.info("JSON report written: %s", json_path)

    # Human-readable summary
    lines: list[str] = [
        "UC1 Label Distribution Diagnostic",
        "=" * 50,
        f"Config: {n} rollouts, policy={args.policy}, seed={args.seed}",
        f"Version: {report['version']}",
        "",
        "SUMMARY",
        "-" * 30,
        f"  mean:    {mean_delta:+.4f}  (target: near 0)",
        f"  stdev:   {stdev_delta:.4f}  (target: 1.0-3.0)",
        f"  median:  {median_delta:+.4f}",
        f"  range:   [{min_delta:+.4f}, {max_delta:+.4f}]",
        "",
        f"  saturation |Δ|>=4.5:  {100 * sat_rate_45:.1f}%  (target: <5%)",
        f"  saturation |Δ|>=4.0:  {100 * sat_rate_40:.1f}%",
        f"  saturation |Δ|>=3.0:  {100 * sat_rate_30:.1f}%",
        "",
        f"  negative (Δ<-0.5): {100 * n_negative / n:.1f}%",
        f"  near-zero:         {100 * n_near_zero / n:.1f}%",
        f"  positive (Δ>+0.5): {100 * n_positive / n:.1f}%",
        "",
        "GATES",
        "-" * 30,
    ]
    for gate_name, gate_val in gates.items():
        assert isinstance(gate_val, dict)
        status = "PASS" if gate_val["pass"] else "FAIL"
        lines.append(f"  [{status}] {gate_name}")

    lines.extend(
        [
            "",
            f"OVERALL: {report['overall_gate']}",
            "",
            "VERB BREAKDOWN",
            "-" * 30,
        ]
    )
    for verb, vs in verb_stats.items():
        assert isinstance(vs, dict)
        lines.append(
            f"  {verb:<22} n={vs['count']:>5} ({vs['pct_of_total']:.1f}%) "
            f"mean={vs['mean']:+.2f} sat={vs['saturation_pct']:.1f}%"
        )

    lines.extend(
        [
            "",
            "CHAR CLASS BREAKDOWN",
            "-" * 30,
        ]
    )
    for char_class, av in arch_stats.items():
        assert isinstance(av, dict)
        lines.append(
            f"  {char_class:<25} n={av['count']:>5}  "
            f"mean={av['mean']:+.2f}  stdev={av['stdev']:.2f}  sat={av['saturation_pct']:.1f}%"
        )

    lines.extend(
        [
            "",
            "HISTOGRAM (20 equal bins over [-5, +5])",
            "-" * 50,
        ]
    )
    for bin_entry in histogram:
        assert isinstance(bin_entry, dict)
        bar_len = int(bin_entry["pct"] / 100.0 * 40)  # type: ignore[operator]
        bar = "#" * bar_len
        lines.append(
            f"  [{bin_entry['bin_lo']:+.1f}, {bin_entry['bin_hi']:+.1f})  "
            f"{bin_entry['count']!s:>6}  ({bin_entry['pct']:>5.1f}%)  {bar}"
        )

    txt_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    logger.info("Text report written: %s", txt_path)

    # Print summary to stdout so it shows up in Larry's session.
    print("\n" + "\n".join(lines))


if __name__ == "__main__":
    main()
