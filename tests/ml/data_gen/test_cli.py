"""Tests for ``python -m wayfinders.ml.data_gen`` CLI."""

from __future__ import annotations

from pathlib import Path

import pytest

from wayfinders.ml.data_gen.__main__ import main


def test_cli_writes_jsonl(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    out = tmp_path / "rollouts.jsonl"
    rc = main(["--n", "5", "--seed", "42", "--out", str(out)])
    assert rc == 0
    captured = capsys.readouterr()
    assert "Wrote 5 rollouts" in captured.out
    assert out.exists()
    lines = out.read_text(encoding="utf-8").splitlines()
    assert len(lines) == 5


def test_cli_negative_n_errors(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    out = tmp_path / "out.jsonl"
    rc = main(["--n", "-1", "--seed", "0", "--out", str(out)])
    assert rc == 2
    captured = capsys.readouterr()
    assert "must be non-negative" in captured.err


def test_cli_default_n_seed(tmp_path: Path) -> None:
    """Defaults: n=1000, seed=42. We don't write 1000 in tests; just check parse."""
    out = tmp_path / "out.jsonl"
    rc = main(["--n", "1", "--out", str(out)])
    assert rc == 0
    assert out.exists()
