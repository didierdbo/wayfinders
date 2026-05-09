"""Pydantic v2 schema for the hand-graded evaluation dataset.

Dataset format: JSONL, one JSON object per line.  Each object is a
``HandGradedItem``.

Schema choice rationale:
  - JSONL over CSV: prose strings contain commas, newlines, and quotes --
    CSV escaping is fragile.  JSONL is unambiguous and trivially streamable.
  - Flat fields: no nested objects -- Varn fills the file with a text editor
    or a simple script; no Pydantic import required on the authoring side.
  - ``expected_delta`` is a float in [-5, +5] matching the model output range
    (Pax 2026-05-02 sec. 3 -- tanh x 5 bounded).
  - ``grader_id`` is free-form (e.g. "varn", "didier", "pax").  No enum lock --
    multiple graders are allowed; the harness reports them separately for
    inter-rater agreement audits (M3 stretch).
  - ``notes`` is optional designer rationale.  Ignored by the harness metrics
    but preserved in the output report for human review.

File location convention:
  wayfinders/ml/eval/datasets/<name>.jsonl
  The CLI defaults to stub_v0.jsonl; pass --dataset to override.
"""

from __future__ import annotations

import json
from pathlib import Path

from pydantic import BaseModel, ConfigDict, Field

# Default datasets directory, relative to this file.
_DATASETS_DIR = Path(__file__).parent / "datasets"

# Name of the stub dataset shipped with the codebase.
STUB_DATASET_NAME = "stub_v0.jsonl"


class HandGradedItem(BaseModel):
    """One hand-graded evaluation scenario.

    Fields
    ------
    id : str
        Unique scenario identifier.  Format is free-form; ``varn_NNN`` is the
        convention for Varn's scenarios.  Must be unique within a dataset file.
    char_prose : str
        EN-rendered character prose (``render_character()`` output).
        EN-pinned regardless of UI locale -- the encoder never sees French.
    action_prose : str
        EN-rendered action prose (``render_action()`` output).  EN-pinned.
    context_prose : str
        EN-rendered context prose (``render_context()`` output).  EN-pinned.
    expected_delta : float
        Designer-assigned resolution modifier in [-5, +5].  This is the
        label the model is evaluated against.  Fractional values are
        allowed (e.g. 2.5 for "moderate advantage").
    grader_id : str
        Who assigned ``expected_delta`` (e.g. "varn", "didier").
    notes : str | None
        Optional designer rationale.  Not used in metric computation.
    """

    model_config = ConfigDict(frozen=True, extra="forbid")

    id: str = Field(..., min_length=1, description="Unique scenario identifier.")
    char_prose: str = Field(
        ...,
        min_length=1,
        description="EN-rendered character prose (render_character output). EN-pinned.",
    )
    action_prose: str = Field(
        ...,
        min_length=1,
        description="EN-rendered action prose (render_action output). EN-pinned.",
    )
    context_prose: str = Field(
        ...,
        min_length=1,
        description="EN-rendered context prose (render_context output). EN-pinned.",
    )
    expected_delta: float = Field(
        ...,
        ge=-5.0,
        le=5.0,
        description="Designer-assigned delta in [-5, +5].",
    )
    grader_id: str = Field(
        ...,
        min_length=1,
        description="Grader identifier (e.g. 'varn', 'didier').",
    )
    notes: str | None = Field(
        default=None,
        description="Optional designer rationale. Not used in metric computation.",
    )


def load_dataset(path: Path) -> list[HandGradedItem]:
    """Load and validate a JSONL hand-graded dataset.

    Each non-empty line must be a valid JSON object matching
    ``HandGradedItem``.  Lines starting with ``#`` are treated as
    comments and skipped (non-standard JSONL extension for readability).

    Args:
        path: Path to a ``.jsonl`` file.

    Returns:
        List of validated ``HandGradedItem`` objects.

    Raises:
        FileNotFoundError: if ``path`` does not exist.
        ValueError: if any line fails Pydantic validation or if the dataset
                    contains duplicate ``id`` values.
    """
    if not path.is_file():
        raise FileNotFoundError(f"Dataset file not found: {path}")

    items: list[HandGradedItem] = []
    ids_seen: set[str] = set()

    for lineno, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        try:
            data = json.loads(line)
        except json.JSONDecodeError as exc:
            raise ValueError(f"Dataset {path}: line {lineno} is not valid JSON: {exc}") from exc

        try:
            item = HandGradedItem.model_validate(data)
        except Exception as exc:
            raise ValueError(
                f"Dataset {path}: line {lineno} failed schema validation: {exc}"
            ) from exc

        if item.id in ids_seen:
            raise ValueError(f"Dataset {path}: duplicate id {item.id!r} at line {lineno}")
        ids_seen.add(item.id)
        items.append(item)

    if not items:
        raise ValueError(
            f"Dataset {path}: no items found (file is empty or all lines are comments)"
        )

    return items


def default_stub_path() -> Path:
    """Return the path to the bundled stub dataset."""
    return _DATASETS_DIR / STUB_DATASET_NAME
