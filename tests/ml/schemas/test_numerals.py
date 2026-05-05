"""Unit tests for the int_to_words / hours_to_words / ordinal helpers."""

from __future__ import annotations

import pytest
from hypothesis import given
from hypothesis import strategies as st

from wayfinders.ml.schemas.numerals import hours_to_words, int_to_words, ordinal


class TestIntToWords:
    @pytest.mark.parametrize(
        "n,expected",
        [
            (0, "zero"),
            (1, "one"),
            (8, "eight"),
            (12, "twelve"),
            (19, "nineteen"),
            (20, "twenty"),
            (21, "twenty-one"),
            (47, "forty-seven"),
            (90, "ninety"),
            (99, "ninety-nine"),
            (100, "more than a hundred"),
            (1000, "more than a hundred"),
        ],
    )
    def test_canonical_values(self, n: int, expected: str) -> None:
        assert int_to_words(n) == expected

    def test_negative_raises(self) -> None:
        with pytest.raises(ValueError, match="non-negative"):
            int_to_words(-1)

    @given(st.integers(min_value=0, max_value=99))
    def test_in_range_outputs_lowercase_no_whitespace_edges(self, n: int) -> None:
        result = int_to_words(n)
        assert result
        assert result == result.strip()
        assert result == result.lower()

    @given(st.integers(min_value=100, max_value=10_000))
    def test_above_cap_collapses(self, n: int) -> None:
        assert int_to_words(n) == "more than a hundred"


class TestHoursToWords:
    @pytest.mark.parametrize(
        "h,expected",
        [
            (0, "zero"),
            (1, "one"),
            (80, "eighty"),
            (1240, "one thousand two hundred forty"),
        ],
    )
    def test_canonical_values(self, h: int, expected: str) -> None:
        assert hours_to_words(h) == expected

    def test_negative_raises(self) -> None:
        with pytest.raises(ValueError, match="non-negative"):
            hours_to_words(-1)


class TestOrdinal:
    @pytest.mark.parametrize(
        "n,expected",
        [(1, "first"), (2, "second"), (3, "third"), (12, "twelfth")],
    )
    def test_canonical(self, n: int, expected: str) -> None:
        assert ordinal(n) == expected

    @pytest.mark.parametrize("n", [0, 13, 100, -1])
    def test_out_of_range_raises(self, n: int) -> None:
        with pytest.raises(ValueError, match=r"M1 supports 1\.\.12"):
            ordinal(n)
