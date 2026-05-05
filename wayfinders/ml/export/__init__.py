"""ONNX export for Rune (Phase 6, C# runtime).

Two artefacts ship per release:
    - ``minilm-l6-v2-{precision}.onnx`` — frozen MiniLM encoder
    - ``resolution-head-v{MAJOR}.{MINOR}.onnx`` — trained head

See ``Coda - ONNX Export Contract - 2026-05-05.md`` for the full input/output
schema, manifest format, parity test gates, and tokenizer contract.
"""
