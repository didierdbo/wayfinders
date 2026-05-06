"""Why does HF Slow agree with HF Fast but disagree with Microsoft.ML.Tokenizers?

All three claim to be "BERT bert-base-uncased compatible". Yet HF Slow
(BertTokenizer) splits `Mira_0` as ['mira', '_', '0'] while
Microsoft.ML.Tokenizers (with ApplyBasicTokenization=true) keeps `mira_0`
together and WordPieces it as ['mira', '##_', '##0'].

This script inspects the underscore handling in HF's BasicTokenizer to
identify the exact mechanism, so the brief recommends the right knob.
"""

from __future__ import annotations

from transformers import BertTokenizer
from transformers.models.bert.tokenization_bert import (
    BasicTokenizer,
    _is_punctuation,
)

MODEL = "sentence-transformers/all-MiniLM-L6-v2"

slow = BertTokenizer.from_pretrained(MODEL)

# Inspect the raw BasicTokenizer
basic = BasicTokenizer(do_lower_case=True, strip_accents=True, never_split=None)

samples = [
    "Mira_0 the cleric",
    "Renn_0 the quartermaster, Brann_1 the ranger",
    "snake_case identifier",
    "punctuation: comma, semicolon; colon: parens (foo)",
]

print("=" * 80)
print("HF BasicTokenizer output (the splitter that runs BEFORE WordPiece)")
print("=" * 80)
for s in samples:
    tokens = basic.tokenize(s)
    print(f"input  : {s!r}")
    print(f"output : {tokens}")
    print()

print("=" * 80)
print("Full HF Slow tokenize (BasicTokenizer + WordPiece)")
print("=" * 80)
for s in samples:
    tokens = slow.tokenize(s)
    print(f"input  : {s!r}")
    print(f"output : {tokens}")
    print()

# Show that `_` is classified as PUNCTUATION by HF's _is_punctuation
chars_to_test = ["_", "-", ".", ",", ":", ";", "(", "0"]
print("=" * 80)
print("HF _is_punctuation classification")
print("=" * 80)
for c in chars_to_test:
    print(f"  {c!r}: is_punctuation = {_is_punctuation(c)}")
