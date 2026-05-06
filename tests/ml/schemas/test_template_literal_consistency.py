"""Template-vs-literal consistency tests (Varn pre-check §5, 2026-05-06).

Audit pattern: does any closed-vocab literal start with a function word that
the render template already emits? If so, the renderer would produce a
double-function-word bug (e.g. "with with festival lanterns strung overhead").

Covers all three schemas (Character, Action, Context) and all lanes that are
composed inside a template string. This test class is the automated equivalent
of Varn's §5 audit recommendation: "render every literal in template, assert
no double-words".

The test does NOT render full documents -- it checks the surface form of each
literal against the known template prefix for its slot. Faster and more
targeted than a full-render sweep.
"""

from __future__ import annotations

from typing import get_args

import pytest

from wayfinders.ml.schemas.vocabularies import (
    Alertness,
    FactionRelation,
    FatigueBucket,
    Footing,
    MissionType,
    Noise,
    Precipitation,
    SkillTier,
    StakesTier,
    SupplyBucket,
    Temperature,
    TerrainModifier,
    TerrainPrimary,
    TimeOfDay,
    Watch,
    Wind,
    WindDirection,
    WoundBucket,
)

# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------


def _get_literals(lane: object) -> tuple[str, ...]:
    """Extract string literals from a TypeAliasType (PEP 695 `type X = Literal[...]`).

    Python 3.12 `type` statement creates `TypeAliasType`; `get_args()` alone
    returns () on those -- must go through `.__value__` first.
    """
    if hasattr(lane, "__value__"):
        return get_args(lane.__value__)  # type: ignore[union-attr]
    return get_args(lane)  # type: ignore[arg-type]


def _check_no_leading_function_word(
    lane_name: str,
    literals: tuple[str, ...],
    banned_prefixes: tuple[str, ...],
) -> None:
    """Assert no literal in the lane starts with any of the banned_prefixes."""
    violations: list[str] = []
    for lit in literals:
        for prefix in banned_prefixes:
            if lit.startswith(prefix):
                violations.append(f"  {lane_name!r}: {lit!r} starts with {prefix!r}")
    assert not violations, "Template-literal mismatch in lane(s):\n" + "\n".join(violations)


# ---------------------------------------------------------------------------
# TerrainModifier -- template: "with {modifier}" inside terrain sentence
# ---------------------------------------------------------------------------
# The terrain sentence template is:
#   "The local terrain is {primary}, with {modifier}."
# So any TerrainModifier literal that starts with "with " would produce
# "with with ..." (the Delta 4 bug class). Banned prefix: "with ".


class TestTerrainModifierLiterals:
    def test_no_literal_starts_with_with(self) -> None:
        """TerrainModifier literals must not start with 'with ' (template already emits it)."""
        literals = _get_literals(TerrainModifier)
        _check_no_leading_function_word(
            "TerrainModifier",
            literals,
            ("with ",),
        )

    def test_count_is_ten(self) -> None:
        """Varn §3 floor: ≈10 modifiers. Gap A closure raised count to exactly 10."""
        assert len(_get_literals(TerrainModifier)) == 10


# ---------------------------------------------------------------------------
# WindDirection -- template: "from the {direction}" inside weather sentence
# ---------------------------------------------------------------------------
# The weather sentence template is:
#   "The weather is {temp} and {precip}, with {wind} from the {direction}."
# So any WindDirection literal that starts with "the " would produce
# "from the the river". Banned prefix: "the ".


class TestWindDirectionLiterals:
    def test_no_literal_starts_with_the(self) -> None:
        """WindDirection literals must not start with 'the ' (template emits 'from the ')."""
        literals = _get_literals(WindDirection)
        _check_no_leading_function_word(
            "WindDirection",
            literals,
            ("the ",),
        )


# ---------------------------------------------------------------------------
# Manner lanes (Action) -- template: "on {footing}", "in {noise}"
# ---------------------------------------------------------------------------
# Sentence 4 template:
#   "The deed is performed {time_phrase}, on {footing_phrase}, in {noise_phrase}."
# Footing literals must not start with "on "; Noise literals must not start
# with "in ". TimeOfDay is used verbatim as {time_phrase} -- no leading word
# collision there, but assert no double-"performed" as a forward guard.


class TestActionMannerLiterals:
    def test_footing_no_leading_on(self) -> None:
        literals = _get_literals(Footing)
        _check_no_leading_function_word("Footing", literals, ("on ",))

    def test_noise_no_leading_in(self) -> None:
        literals = _get_literals(Noise)
        _check_no_leading_function_word("Noise", literals, ("in ",))

    def test_time_of_day_no_leading_performed(self) -> None:
        """Template: 'performed {time_phrase}' -- time_phrase should not start with 'performed'."""
        literals = _get_literals(TimeOfDay)
        _check_no_leading_function_word("TimeOfDay", literals, ("performed ",))


# ---------------------------------------------------------------------------
# Party condition lanes (Context) -- template:
#   "The party is {wound}, {supply}, and {fatigue}."
# No literal should start with "and " (template emits it before fatigue slot).
# ---------------------------------------------------------------------------


class TestPartyConditionLiterals:
    def test_wound_no_leading_and(self) -> None:
        literals = _get_literals(WoundBucket)
        _check_no_leading_function_word("WoundBucket", literals, ("and ",))

    def test_supply_no_leading_and(self) -> None:
        literals = _get_literals(SupplyBucket)
        _check_no_leading_function_word("SupplyBucket", literals, ("and ",))

    def test_fatigue_no_leading_and(self) -> None:
        literals = _get_literals(FatigueBucket)
        _check_no_leading_function_word("FatigueBucket", literals, ("and ",))

    def test_wound_bucket_count_is_three(self) -> None:
        """Varn 2026-05-01 §3 lock: 3 buckets each for wound/supply/fatigue."""
        assert len(_get_literals(WoundBucket)) == 3

    def test_supply_bucket_count_is_three(self) -> None:
        assert len(_get_literals(SupplyBucket)) == 3

    def test_fatigue_bucket_count_is_three(self) -> None:
        assert len(_get_literals(FatigueBucket)) == 3


# ---------------------------------------------------------------------------
# Weather lanes (Context)
# Template: "The weather is {temp} and {precip}, with {wind} from the {dir}."
# No literal should start with "and " or "with ".
# ---------------------------------------------------------------------------


class TestWeatherLaneLiterals:
    def test_temperature_no_leading_and(self) -> None:
        literals = _get_literals(Temperature)
        _check_no_leading_function_word("Temperature", literals, ("and ",))

    def test_precipitation_no_leading_with(self) -> None:
        # Precipitation slot is directly after "and " in the template
        literals = _get_literals(Precipitation)
        _check_no_leading_function_word("Precipitation", literals, ("with ",))

    def test_wind_no_leading_with(self) -> None:
        # Wind slot is inside "with {wind} from the {dir}"
        literals = _get_literals(Wind)
        _check_no_leading_function_word("Wind", literals, ("with ",))


# ---------------------------------------------------------------------------
# Faction relation -- template: "{faction} is {relation} with {other_faction}"
# No FactionRelation literal should start with "with ".
# ---------------------------------------------------------------------------


class TestFactionRelationLiterals:
    def test_no_leading_with(self) -> None:
        literals = _get_literals(FactionRelation)
        _check_no_leading_function_word("FactionRelation", literals, ("with ",))


# ---------------------------------------------------------------------------
# Broad sweep: no literal in ANY context lane starts with "with " (the most
# common template collision word). Defense-in-depth per Varn §5 recommendation.
# Uses _get_literals() throughout to handle PEP 695 TypeAliasType.
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "lane_name,lane_type",
    [
        ("TimeOfDay", TimeOfDay),
        ("Footing", Footing),
        ("Noise", Noise),
        ("Alertness", Alertness),
        ("SkillTier", SkillTier),
        ("MissionType", MissionType),
        ("StakesTier", StakesTier),
        ("WoundBucket", WoundBucket),
        ("SupplyBucket", SupplyBucket),
        ("FatigueBucket", FatigueBucket),
        ("Temperature", Temperature),
        ("Precipitation", Precipitation),
        ("Wind", Wind),
        ("WindDirection", WindDirection),
        ("TerrainPrimary", TerrainPrimary),
        ("TerrainModifier", TerrainModifier),
        ("FactionRelation", FactionRelation),
        ("Watch", Watch),
    ],
)
def test_no_literal_starts_with_with_in_any_lane(lane_name: str, lane_type: object) -> None:
    """No closed-vocab literal in any lane starts with 'with ' (the most common template word).

    This is not exhaustive per-template (see above classes), but catches the
    Delta-4 bug class universally: if a literal starts with 'with ' it almost
    certainly collides with a template that already emits 'with '.
    """
    literals = _get_literals(lane_type)
    _check_no_leading_function_word(lane_name, literals, ("with ",))
