"""Tests for wayfinders.ml.eval.schema.

Covers:
  - HandGradedItem validates valid data.
  - HandGradedItem rejects invalid fields.
  - load_dataset loads the bundled stub without error.
  - load_dataset raises on missing file.
  - load_dataset raises on malformed JSON line.
  - load_dataset raises on schema validation failure.
  - load_dataset raises on duplicate IDs.
  - load_dataset raises on empty file.
  - default_stub_path() returns an existing file.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from pydantic import ValidationError

from wayfinders.ml.eval.schema import HandGradedItem, default_stub_path, load_dataset

# ---------------------------------------------------------------------------
# HandGradedItem unit tests
# ---------------------------------------------------------------------------


class TestHandGradedItem:
    def test_valid_item(self) -> None:
        item = HandGradedItem(
            id="test_001",
            char_prose="Kira is a scout.",
            action_prose="Kira steals across loose scree.",
            context_prose="The ridge is dark and cold.",
            expected_delta=3.0,
            grader_id="varn",
        )
        assert item.id == "test_001"
        assert item.expected_delta == 3.0
        assert item.notes is None

    def test_notes_optional(self) -> None:
        item = HandGradedItem(
            id="x",
            char_prose="A",
            action_prose="B",
            context_prose="C",
            expected_delta=0.0,
            grader_id="tess",
            notes="rationale",
        )
        assert item.notes == "rationale"

    def test_delta_bounds_positive(self) -> None:
        item = HandGradedItem(
            id="x",
            char_prose="A",
            action_prose="B",
            context_prose="C",
            expected_delta=5.0,
            grader_id="varn",
        )
        assert item.expected_delta == 5.0

    def test_delta_bounds_negative(self) -> None:
        item = HandGradedItem(
            id="x",
            char_prose="A",
            action_prose="B",
            context_prose="C",
            expected_delta=-5.0,
            grader_id="varn",
        )
        assert item.expected_delta == -5.0

    def test_delta_out_of_range_raises(self) -> None:
        with pytest.raises(ValidationError):
            HandGradedItem(
                id="x",
                char_prose="A",
                action_prose="B",
                context_prose="C",
                expected_delta=5.1,
                grader_id="varn",
            )

    def test_delta_below_range_raises(self) -> None:
        with pytest.raises(ValidationError):
            HandGradedItem(
                id="x",
                char_prose="A",
                action_prose="B",
                context_prose="C",
                expected_delta=-5.1,
                grader_id="varn",
            )

    def test_empty_id_raises(self) -> None:
        with pytest.raises(ValidationError):
            HandGradedItem(
                id="",
                char_prose="A",
                action_prose="B",
                context_prose="C",
                expected_delta=1.0,
                grader_id="varn",
            )

    def test_empty_prose_raises(self) -> None:
        with pytest.raises(ValidationError):
            HandGradedItem(
                id="x",
                char_prose="",
                action_prose="B",
                context_prose="C",
                expected_delta=1.0,
                grader_id="varn",
            )

    def test_extra_field_raises(self) -> None:
        """extra='forbid' is set -- unknown fields must be rejected."""
        with pytest.raises(ValidationError):
            HandGradedItem(
                id="x",
                char_prose="A",
                action_prose="B",
                context_prose="C",
                expected_delta=1.0,
                grader_id="varn",
                unknown_field="oops",  # type: ignore[call-arg]
            )

    def test_frozen(self) -> None:
        item = HandGradedItem(
            id="x",
            char_prose="A",
            action_prose="B",
            context_prose="C",
            expected_delta=1.0,
            grader_id="varn",
        )
        with pytest.raises(ValidationError):
            item.id = "mutated"  # type: ignore[misc]


# ---------------------------------------------------------------------------
# load_dataset tests
# ---------------------------------------------------------------------------


def _write_jsonl(path: Path, lines: list[str]) -> None:
    path.write_text("\n".join(lines), encoding="utf-8")


class TestLoadDataset:
    def test_stub_loads_without_error(self) -> None:
        """The bundled stub dataset must load cleanly."""
        items = load_dataset(default_stub_path())
        assert len(items) >= 10, "Stub should have at least 10 items"

    def test_stub_ids_are_unique(self) -> None:
        items = load_dataset(default_stub_path())
        ids = [it.id for it in items]
        assert len(ids) == len(set(ids)), "Stub dataset has duplicate IDs"

    def test_stub_deltas_in_range(self) -> None:
        items = load_dataset(default_stub_path())
        for it in items:
            assert -5.0 <= it.expected_delta <= 5.0, (
                f"Item {it.id} has expected_delta={it.expected_delta} out of [-5, 5]"
            )

    def test_file_not_found_raises(self, tmp_path: Path) -> None:
        with pytest.raises(FileNotFoundError):
            load_dataset(tmp_path / "nonexistent.jsonl")

    def test_malformed_json_raises(self, tmp_path: Path) -> None:
        p = tmp_path / "bad.jsonl"
        _write_jsonl(p, ['{"id": "x", "broken":'])
        with pytest.raises(ValueError, match="not valid JSON"):
            load_dataset(p)

    def test_schema_validation_failure_raises(self, tmp_path: Path) -> None:
        p = tmp_path / "bad_schema.jsonl"
        row = json.dumps(
            {
                "id": "x",
                "char_prose": "A",
                "action_prose": "B",
                "context_prose": "C",
                "expected_delta": 99.0,  # out of range
                "grader_id": "tess",
            }
        )
        _write_jsonl(p, [row])
        with pytest.raises(ValueError, match="schema validation"):
            load_dataset(p)

    def test_duplicate_id_raises(self, tmp_path: Path) -> None:
        p = tmp_path / "dup.jsonl"
        row = json.dumps(
            {
                "id": "dup_001",
                "char_prose": "A",
                "action_prose": "B",
                "context_prose": "C",
                "expected_delta": 1.0,
                "grader_id": "tess",
            }
        )
        _write_jsonl(p, [row, row])
        with pytest.raises(ValueError, match="duplicate id"):
            load_dataset(p)

    def test_empty_file_raises(self, tmp_path: Path) -> None:
        p = tmp_path / "empty.jsonl"
        p.write_text("", encoding="utf-8")
        with pytest.raises(ValueError, match="no items found"):
            load_dataset(p)

    def test_comment_lines_skipped(self, tmp_path: Path) -> None:
        p = tmp_path / "comments.jsonl"
        row = json.dumps(
            {
                "id": "item_001",
                "char_prose": "A",
                "action_prose": "B",
                "context_prose": "C",
                "expected_delta": 1.0,
                "grader_id": "tess",
            }
        )
        _write_jsonl(p, ["# this is a comment", row, "# another comment"])
        items = load_dataset(p)
        assert len(items) == 1
        assert items[0].id == "item_001"

    def test_blank_lines_skipped(self, tmp_path: Path) -> None:
        p = tmp_path / "blanks.jsonl"
        row = json.dumps(
            {
                "id": "item_001",
                "char_prose": "A",
                "action_prose": "B",
                "context_prose": "C",
                "expected_delta": 1.0,
                "grader_id": "tess",
            }
        )
        content = f"\n\n{row}\n\n"
        p.write_text(content, encoding="utf-8")
        items = load_dataset(p)
        assert len(items) == 1


# ---------------------------------------------------------------------------
# default_stub_path
# ---------------------------------------------------------------------------


class TestDefaultStubPath:
    def test_returns_existing_file(self) -> None:
        path = default_stub_path()
        assert path.is_file(), f"Stub dataset not found at {path}"

    def test_is_jsonl(self) -> None:
        assert default_stub_path().suffix == ".jsonl"
