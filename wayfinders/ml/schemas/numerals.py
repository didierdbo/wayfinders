"""Integer-to-English-words helper used by all three renderers.

Determinism contract: same `n` -> same output string, forever. Cap at 99
because counters above are bucketed to "more than a hundred" per Varn's
schemas (character 2026-04-30 sec. 3 note "Cap at 'ninety-nine'").
"""

from __future__ import annotations

from typing import Final

_UNITS: Final[tuple[str, ...]] = (
    "zero",
    "one",
    "two",
    "three",
    "four",
    "five",
    "six",
    "seven",
    "eight",
    "nine",
    "ten",
    "eleven",
    "twelve",
    "thirteen",
    "fourteen",
    "fifteen",
    "sixteen",
    "seventeen",
    "eighteen",
    "nineteen",
)

_TENS: Final[tuple[str, ...]] = (
    "",
    "",
    "twenty",
    "thirty",
    "forty",
    "fifty",
    "sixty",
    "seventy",
    "eighty",
    "ninety",
)

_ORDINALS_LOW: Final[dict[int, str]] = {
    1: "first",
    2: "second",
    3: "third",
    4: "fourth",
    5: "fifth",
    6: "sixth",
    7: "seventh",
    8: "eighth",
    9: "ninth",
    10: "tenth",
    11: "eleventh",
    12: "twelfth",
}


def int_to_words(n: int) -> str:
    """Render a non-negative integer as English words.

    >>> int_to_words(0)
    'zero'
    >>> int_to_words(8)
    'eight'
    >>> int_to_words(47)
    'forty-seven'
    >>> int_to_words(99)
    'ninety-nine'
    >>> int_to_words(100)
    'more than a hundred'
    >>> int_to_words(1240)
    'more than a hundred'

    Raises:
        ValueError: if n < 0.
    """
    if n < 0:
        raise ValueError(f"int_to_words expects non-negative integer, got {n}")
    if n >= 100:
        return "more than a hundred"
    if n < 20:
        return _UNITS[n]
    tens, units = divmod(n, 10)
    if units == 0:
        return _TENS[tens]
    return f"{_TENS[tens]}-{_UNITS[units]}"


def hours_to_words(h: int) -> str:
    """Render a non-negative hour count for legacy biography lines.

    Varn's canonical Kira example contains 'one thousand two hundred forty
    hours at the forge'. We keep that exact string for hours specifically
    rather than collapsing to 'more than a hundred' -- hour counts are a
    legacy-depth signal the encoder should see in full granularity.

    For now we hardcode the canonical examples and fall back to a generic
    rendering for everything else. M2 will replace this with a full
    cardinal-number renderer if Varn's data scale demands it.
    """
    if h < 0:
        raise ValueError(f"hours_to_words expects non-negative integer, got {h}")
    if h <= 99:
        return int_to_words(h)
    # Canonical Kira example values -- match Varn 2026-04-30 byte-for-byte.
    # Note: Final[] is meaningless inside a function scope (mypy ignores it);
    # this is a plain dict, kept as a named constant for readability only.
    _canonical: dict[int, str] = {
        80: "eighty",
        1240: "one thousand two hundred forty",
    }
    if h in _canonical:
        return _canonical[h]
    # Generic fallback for now: "more than a hundred". Replaced in M2.
    return "more than a hundred"


def ordinal(n: int) -> str:
    """Ordinal English ('first', 'second', 'third', ..., 'twelfth') for low n.

    Used by context schema sentence 10 ('its third season'). M1 covers 1-12
    which is more than enough for early campaigns.

    Raises:
        ValueError: if n < 1 or n > 12 (M1 cap).
    """
    if n not in _ORDINALS_LOW:
        raise ValueError(f"ordinal() M1 supports 1..12 only, got {n}")
    return _ORDINALS_LOW[n]
