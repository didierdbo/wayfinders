"""Compare actual C# tokenizer output vs HuggingFace BertTokenizerFast.

Reads:
    - parity/python_reference_v0.1.json                          (prose strings)
    - scripts/probes/csharp_tokenizer_ids.json                   (C# token ids)

Re-tokenizes each prose with HF Fast (the production fixture path Tess uses)
and diffs against C# Microsoft.ML.Tokenizers.BertTokenizer output.

Outputs a verdict and 3 worst-case examples with token-level windows.
"""

from __future__ import annotations

import json
from pathlib import Path

from transformers import BertTokenizerFast

REPO_ROOT = Path(__file__).resolve().parents[2]
FIXTURE = REPO_ROOT / "parity" / "python_reference_v0.1.json"
CSHARP_IDS = REPO_ROOT / "scripts" / "probes" / "csharp_tokenizer_ids.json"
OUT_PATH = REPO_ROOT / "scripts" / "probes" / "l4_csharp_vs_hf_results.json"
MODEL = "sentence-transformers/all-MiniLM-L6-v2"
PROSE_FIELDS = ("prose_character", "prose_action", "prose_context")


def first_div(a: list[int], b: list[int]) -> int | None:
    n = min(len(a), len(b))
    for i in range(n):
        if a[i] != b[i]:
            return i
    if len(a) != len(b):
        return n
    return None


def window(ids: list[int], pos: int, radius: int = 4) -> list[int]:
    return ids[max(0, pos - radius) : min(len(ids), pos + radius + 1)]


def main() -> None:
    fixture = json.loads(FIXTURE.read_text(encoding="utf-8"))
    csharp = json.loads(CSHARP_IDS.read_text(encoding="utf-8"))
    fast = BertTokenizerFast.from_pretrained(MODEL)

    records = fixture["records"]
    results: list[dict] = []
    div_records = 0
    div_fields = 0

    for rec in records:
        rid = rec["id"]
        any_div = False
        for field in PROSE_FIELDS:
            text = rec[field]
            ids_hf = fast(text, truncation=True, max_length=256, add_special_tokens=True)[
                "input_ids"
            ]
            ids_cs = csharp[rid][field]
            agree = ids_hf == ids_cs
            entry: dict = {
                "record_id": rid,
                "field": field,
                "agree": agree,
                "hf_len": len(ids_hf),
                "cs_len": len(ids_cs),
            }
            if not agree:
                div_fields += 1
                any_div = True
                pos = first_div(ids_hf, ids_cs)
                hf_win = window(ids_hf, pos)
                cs_win = window(ids_cs, pos)
                entry["first_div_pos"] = pos
                entry["hf_window_ids"] = hf_win
                entry["cs_window_ids"] = cs_win
                entry["hf_window_tokens"] = [fast.convert_ids_to_tokens(i) for i in hf_win]
                entry["cs_window_tokens"] = [fast.convert_ids_to_tokens(i) for i in cs_win]
                # Also count total mismatches across the two ID lists
                m = min(len(ids_hf), len(ids_cs))
                mismatches = sum(1 for k in range(m) if ids_hf[k] != ids_cs[k])
                mismatches += abs(len(ids_hf) - len(ids_cs))
                entry["total_mismatches"] = mismatches
            results.append(entry)
        if any_div:
            div_records += 1

    # Print summary
    print("=" * 100)
    print("HF Fast (fixture path) vs Microsoft.ML.Tokenizers C# (Rune's L4 path)")
    print("=" * 100)
    print(
        f"{'record_id':<24} {'field':<18} {'agree':<7} {'hf_len':>7} {'cs_len':>7} {'first_div':>10} {'mismatches':>11}"
    )
    print("-" * 100)
    for e in results:
        agree = "YES" if e["agree"] else "NO"
        first = e.get("first_div_pos", "-")
        mm = e.get("total_mismatches", "-")
        print(
            f"{e['record_id']:<24} {e['field']:<18} {agree:<7} {e['hf_len']:>7} {e['cs_len']:>7} {first!s:>10} {mm!s:>11}"
        )
    print("-" * 100)
    print(f"Records with at least one diverging field: {div_records} / {len(records)}")
    print(f"Total diverging fields: {div_fields} / {len(results)}")
    print()

    # Worst examples (sort by total_mismatches desc)
    diverging = [e for e in results if not e["agree"]]
    diverging.sort(key=lambda e: e.get("total_mismatches", 0), reverse=True)

    print("=" * 100)
    print("TOP DIVERGING EXAMPLES")
    print("=" * 100)
    for e in diverging[:6]:
        print(
            f"\n[{e['record_id']} / {e['field']}] first_div={e['first_div_pos']} mismatches={e.get('total_mismatches')}"
        )
        print(f"  hf  ids={e['hf_window_ids']}")
        print(f"      tok={e['hf_window_tokens']}")
        print(f"  cs  ids={e['cs_window_ids']}")
        print(f"      tok={e['cs_window_tokens']}")

    OUT_PATH.write_text(
        json.dumps(
            {
                "fixture": str(FIXTURE.relative_to(REPO_ROOT)),
                "n_records": len(records),
                "records_with_divergence": div_records,
                "field_divergences": div_fields,
                "results": results,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    print(f"\nResults written to: {OUT_PATH}")


if __name__ == "__main__":
    main()
