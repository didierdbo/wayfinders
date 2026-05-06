"""L4 Tokenizer Divergence Probe (Coda, 2026-05-06).

Hypothesis (Rune, L4 Closeout §5.3):
    `Microsoft.ML.Tokenizers.BertTokenizer` (C#, BasicTokenizer + WordPiece)
    diverges from HuggingFace `transformers.BertTokenizerFast` (Python, Rust
    backend) on rollout-style prose containing underscore-digit name patterns
    like `Renn_0`, `Brann_1`, `Mira_2`.

Method:
    For each of the 20 prose records in parity/python_reference_v0.1.json,
    tokenize with three tokenizers and compare token IDs:

      A. HF Fast (production fixture path)         = Tess's path
            SentenceTransformer(...)[0].tokenizer
            -> BertTokenizerFast (Rust, "fast")
      B. HF Slow (BertTokenizer, BasicTokenizer)   = closest Python proxy
            transformers.BertTokenizer.from_pretrained(...)
            -> BasicTokenizer + WordPiece (matches MS BertTokenizer behavior)
      C. (optional) Microsoft.ML.Tokenizers via dotnet --

    The C# tokenizer (Microsoft.ML.Tokenizers.BertTokenizer with
    ApplyBasicTokenization=true, LowerCaseBeforeTokenization=true,
    RemoveNonSpacingMarks=true) matches the HF Slow path semantically by
    design (both run BasicTokenizer + WordPiece). So divergence = (A) vs (B).

    For each record we tokenize prose_character / prose_action /
    prose_context and report:
        - whether ids match exactly
        - if not, the first divergence position and a 5-token window around it
        - the surface tokens at that window (decoded individually)

Outputs:
    - summary table (record_id, field, agree?, first_div_pos, surrounding tokens)
    - 3 worst-divergence examples printed verbatim
    - a JSON dump at scripts/probes/l4_tokenizer_divergence_probe_results.json
      for downstream review

Usage:
    cd C:/Users/dbo/Desktop/didierdbo/wayfinders
    uv run python scripts/probes/l4_tokenizer_divergence_probe.py
"""

from __future__ import annotations

import json
from pathlib import Path

from transformers import BertTokenizer, BertTokenizerFast

REPO_ROOT = Path(__file__).resolve().parents[2]
FIXTURE = REPO_ROOT / "parity" / "python_reference_v0.1.json"
OUT_PATH = REPO_ROOT / "scripts" / "probes" / "l4_tokenizer_divergence_probe_results.json"
MODEL = "sentence-transformers/all-MiniLM-L6-v2"

PROSE_FIELDS = ("prose_character", "prose_action", "prose_context")


def first_divergence(a: list[int], b: list[int]) -> int | None:
    """Return the index of the first divergent position, or None if identical."""
    n = min(len(a), len(b))
    for i in range(n):
        if a[i] != b[i]:
            return i
    if len(a) != len(b):
        return n
    return None


def window(ids: list[int], pos: int, radius: int = 4) -> list[int]:
    lo = max(0, pos - radius)
    hi = min(len(ids), pos + radius + 1)
    return ids[lo:hi]


def main() -> None:
    fixture = json.loads(FIXTURE.read_text(encoding="utf-8"))
    records = fixture["records"]

    fast = BertTokenizerFast.from_pretrained(MODEL)
    slow = BertTokenizer.from_pretrained(MODEL)

    print(f"Fast tokenizer class: {type(fast).__name__}")
    print(f"Slow tokenizer class: {type(slow).__name__}")
    print(f"Fast do_lower_case={fast.do_lower_case} | Slow do_lower_case={slow.do_lower_case}")
    print(f"Loaded {len(records)} records from {FIXTURE.name}.\n")

    results: list[dict] = []
    diff_count = 0
    field_diff_count = 0

    for rec in records:
        rec_id = rec["id"]
        rec_diffs = []
        for field in PROSE_FIELDS:
            text = rec[field]
            # match Tess's path exactly (truncation+max_length=256, single string)
            ids_fast = fast(text, truncation=True, max_length=256, add_special_tokens=True)[
                "input_ids"
            ]
            ids_slow = slow(text, truncation=True, max_length=256, add_special_tokens=True)[
                "input_ids"
            ]

            agree = ids_fast == ids_slow
            entry: dict = {
                "record_id": rec_id,
                "field": field,
                "agree": agree,
                "fast_len": len(ids_fast),
                "slow_len": len(ids_slow),
            }
            if not agree:
                field_diff_count += 1
                pos = first_divergence(ids_fast, ids_slow)
                entry["first_div_pos"] = pos
                fast_win = window(ids_fast, pos)
                slow_win = window(ids_slow, pos)
                entry["fast_window_ids"] = fast_win
                entry["slow_window_ids"] = slow_win
                entry["fast_window_tokens"] = [fast.convert_ids_to_tokens(i) for i in fast_win]
                entry["slow_window_tokens"] = [slow.convert_ids_to_tokens(i) for i in slow_win]
            rec_diffs.append(entry)
            results.append(entry)

        any_field_diverged = any(not e["agree"] for e in rec_diffs)
        if any_field_diverged:
            diff_count += 1

    # Print summary
    print("=" * 100)
    print("SUMMARY TABLE")
    print("=" * 100)
    print(
        f"{'record_id':<24} {'field':<18} {'agree':<7} {'fast_len':>9} {'slow_len':>9} {'first_div':>10}"
    )
    print("-" * 100)
    for e in results:
        first = e.get("first_div_pos", "-")
        agree_str = "YES" if e["agree"] else "NO"
        print(
            f"{e['record_id']:<24} {e['field']:<18} {agree_str:<7} {e['fast_len']:>9} {e['slow_len']:>9} {first!s:>10}"
        )
    print("-" * 100)
    print(f"Records with at least one diverging field: {diff_count} / {len(records)}")
    print(f"Total field-level divergences: {field_diff_count} / {len(results)}")
    print()

    # Print 3 worst examples (rollout_017, rollout_013, rollout_002)
    print("=" * 100)
    print("DETAILED EXAMPLES (first 3 diverging entries)")
    print("=" * 100)
    shown = 0
    for e in results:
        if e["agree"]:
            continue
        if shown >= 5:
            break
        shown += 1
        print(f"\n[{e['record_id']} / {e['field']}]")
        print(f"  first divergence at position {e['first_div_pos']}")
        print(f"  fast (HF Fast / fixture path): ids={e['fast_window_ids']}")
        print(f"                                  tok={e['fast_window_tokens']}")
        print(f"  slow (HF Slow / C# proxy):     ids={e['slow_window_ids']}")
        print(f"                                  tok={e['slow_window_tokens']}")

    # Persist results for the brief
    OUT_PATH.write_text(
        json.dumps(
            {
                "fixture": str(FIXTURE.relative_to(REPO_ROOT)),
                "model": MODEL,
                "fast_class": type(fast).__name__,
                "slow_class": type(slow).__name__,
                "n_records": len(records),
                "records_with_divergence": diff_count,
                "field_divergences": field_diff_count,
                "results": results,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    print(f"\nResults written to: {OUT_PATH}")


if __name__ == "__main__":
    main()
