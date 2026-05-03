"""Validation tests for the ``Unit`` Pydantic model.

These guard the boundary contract against silent regressions: a future
change that loosens validation would let bad seed data through, and the
client would break in production rather than at test time.
"""

from __future__ import annotations

import pytest
from pydantic import ValidationError

from wayfinders.api.models import Unit


def test_unit_accepts_valid_payload() -> None:
    unit = Unit(display_name="Scout", max_hp=8, base_speed=6, portrait="icon.svg")

    assert unit.display_name == "Scout"
    assert unit.max_hp == 8
    assert unit.base_speed == 6
    assert unit.portrait == "icon.svg"


@pytest.mark.parametrize("bad_hp", [0, -1, -100])
def test_unit_rejects_non_positive_max_hp(bad_hp: int) -> None:
    with pytest.raises(ValidationError):
        Unit(display_name="Scout", max_hp=bad_hp, base_speed=6, portrait="icon.svg")


@pytest.mark.parametrize("bad_speed", [0, -1])
def test_unit_rejects_non_positive_base_speed(bad_speed: int) -> None:
    with pytest.raises(ValidationError):
        Unit(display_name="Scout", max_hp=8, base_speed=bad_speed, portrait="icon.svg")


def test_unit_rejects_empty_display_name() -> None:
    with pytest.raises(ValidationError):
        Unit(display_name="", max_hp=8, base_speed=6, portrait="icon.svg")


def test_unit_rejects_unknown_fields() -> None:
    """``extra='forbid'`` catches typos and stale client schemas."""
    with pytest.raises(ValidationError):
        Unit.model_validate(
            {
                "display_name": "Scout",
                "max_hp": 8,
                "base_speed": 6,
                "portrait": "icon.svg",
                "armor": 2,  # not in schema
            }
        )


def test_unit_is_frozen() -> None:
    unit = Unit(display_name="Scout", max_hp=8, base_speed=6, portrait="icon.svg")

    with pytest.raises(ValidationError):
        unit.max_hp = 999  # type: ignore[misc]
