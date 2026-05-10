"""Tests for ``filter_eligible_personas`` (Varn-lock 2026-05-10 §2).

Covers:
  1. Discriminant canary — dex_bucket=very-high NOT eligible for scout_route mid.
  2. All mission-type → stat-lane ratios: scout_route and parley_local.
  3. Boundary and window correctness for all five difficulty buckets.
  4. Edge case: empty company → returns ().
  5. Edge case: missing/invalid bucket → hard fail (ValueError).
  6. Return order: PersonaId ascending, deterministic.
  7. Deduplication: duplicate PersonaId → first occurrence wins, no duplicates in output.

Persona construction uses minimal CharacterState fields. The filter only reads
``name`` (as PersonaId) and the relevant bucket attribute.
"""

from __future__ import annotations

import pytest

from wayfinders.api.world_tick import filter_eligible_personas
from wayfinders.ml.schemas.character import CharacterState

# ---------------------------------------------------------------------------
# Minimal CharacterState builder — only fields the filter needs.
# All other CharacterState fields use their defaults.
# ---------------------------------------------------------------------------


def _make_persona(
    name: str,
    *,
    dex_bucket: str = "mid",
    wis_bucket: str = "mid",
    str_bucket: str = "mid",
) -> CharacterState:
    """Build a minimal CharacterState for filter tests."""
    return CharacterState(
        name=name,
        char_class="scout",
        faction="Ridgewatch company",
        str_bucket=str_bucket,  # type: ignore[arg-type]
        dex_bucket=dex_bucket,  # type: ignore[arg-type]
        wis_bucket=wis_bucket,  # type: ignore[arg-type]
        hp_bucket="unhurt",
        stress_bucket="composed",
    )


# ---------------------------------------------------------------------------
# 1. Discriminant canary (Varn §7 mandatory test)
#    Kira DEX very-high must NOT be eligible for scout_route mid.
#    mid window = {low, mid, high} — very-high is outside.
# ---------------------------------------------------------------------------


class TestDiscriminantCanary:
    def test_very_high_dex_not_eligible_for_scout_route_mid(self) -> None:
        """Core canary: Kira DEX=very-high is not eligible for scout_route mid.

        mid ±1 = {low, mid, high}.  very-high is outside the window.
        This test is the primary guard that the rule is ±1, not an open
        upper threshold.  If this test passes with very-high eligible,
        something is wrong with the window arithmetic.
        """
        kira = _make_persona("kira", dex_bucket="very-high")
        result = filter_eligible_personas("scout_route", "mid", [kira])
        assert result == (), (
            f"Expected () — kira DEX=very-high is outside mid±1 window {{low,mid,high}}, "
            f"got {result!r}"
        )

    def test_very_high_dex_not_eligible_persona_b_dex_mid_is(self) -> None:
        """Discriminant with mixed company: kira excluded, persona_b included."""
        kira = _make_persona("kira", dex_bucket="very-high")
        persona_b = _make_persona("persona_b", dex_bucket="mid")
        result = filter_eligible_personas("scout_route", "mid", [kira, persona_b])
        assert result == ("persona_b",), (
            f"Expected ('persona_b',) — kira out, persona_b in. Got {result!r}"
        )


# ---------------------------------------------------------------------------
# 2. Mission-type → stat-lane ratios
#    scout_route  → dex_bucket
#    parley_local → wis_bucket
# ---------------------------------------------------------------------------


class TestMissionStatLaneMapping:
    def test_scout_route_reads_dex_bucket(self) -> None:
        """scout_route eligibility is determined by dex_bucket, not wis_bucket."""
        # persona: dex=mid (in window), wis=very-high (would be out if wis were used)
        persona = _make_persona("archer", dex_bucket="mid", wis_bucket="very-high")
        result = filter_eligible_personas("scout_route", "mid", [persona])
        assert result == ("archer",), (
            "scout_route mid should use dex_bucket. archer DEX=mid is eligible."
        )

    def test_scout_route_excludes_on_dex_not_wis(self) -> None:
        """scout_route exclusion is via dex_bucket; good wis doesn't help."""
        # persona: dex=very-high (out of mid window), wis=mid (would be in if wis were used)
        persona = _make_persona("warden", dex_bucket="very-high", wis_bucket="mid")
        result = filter_eligible_personas("scout_route", "mid", [persona])
        assert result == (), (
            "scout_route mid: warden DEX=very-high is outside window. wis_bucket irrelevant."
        )

    def test_parley_local_reads_wis_bucket(self) -> None:
        """parley_local eligibility is determined by wis_bucket, not dex_bucket."""
        # persona: wis=mid (in window), dex=very-high (would be out if dex were used)
        persona = _make_persona("sage", wis_bucket="mid", dex_bucket="very-high")
        result = filter_eligible_personas("parley_local", "mid", [persona])
        assert result == ("sage",), (
            "parley_local mid should use wis_bucket. sage WIS=mid is eligible."
        )

    def test_parley_local_excludes_on_wis_not_dex(self) -> None:
        """parley_local exclusion is via wis_bucket; good dex doesn't help."""
        # persona: wis=very-high (out of mid window), dex=mid (would be in if dex were used)
        persona = _make_persona("rogue", wis_bucket="very-high", dex_bucket="mid")
        result = filter_eligible_personas("parley_local", "mid", [persona])
        assert result == (), (
            "parley_local mid: rogue WIS=very-high is outside window. dex_bucket irrelevant."
        )


# ---------------------------------------------------------------------------
# 3. Window correctness across all five difficulty buckets
# ---------------------------------------------------------------------------


class TestWindowBoundaries:
    """Verify ±1 window for every difficulty_bucket value.

    Table from Varn spec §2:
      very-low → {very-low, low}
      low      → {very-low, low, mid}
      mid      → {low, mid, high}
      high     → {mid, high, very-high}
      very-high → {high, very-high}
    """

    def _run(
        self,
        mission_type: str,
        difficulty: str,
        bucket_attr: str,
    ) -> dict[str, bool]:
        """Run filter for each DescriptorBucket value and return eligibility map."""
        all_buckets = ("very-low", "low", "mid", "high", "very-high")
        results = {}
        for b in all_buckets:
            kwargs = {bucket_attr: b}
            persona = _make_persona(f"persona_{b}", **kwargs)  # type: ignore[arg-type]
            eligible = filter_eligible_personas(  # type: ignore[arg-type]
                mission_type,  # type: ignore[arg-type]
                difficulty,  # type: ignore[arg-type]
                [persona],
            )
            results[b] = len(eligible) > 0
        return results

    def test_scout_route_difficulty_very_low(self) -> None:
        result = self._run("scout_route", "very-low", "dex_bucket")
        assert result == {
            "very-low": True,
            "low": True,
            "mid": False,
            "high": False,
            "very-high": False,
        }

    def test_scout_route_difficulty_low(self) -> None:
        result = self._run("scout_route", "low", "dex_bucket")
        assert result == {
            "very-low": True,
            "low": True,
            "mid": True,
            "high": False,
            "very-high": False,
        }

    def test_scout_route_difficulty_mid(self) -> None:
        result = self._run("scout_route", "mid", "dex_bucket")
        assert result == {
            "very-low": False,
            "low": True,
            "mid": True,
            "high": True,
            "very-high": False,
        }

    def test_scout_route_difficulty_high(self) -> None:
        result = self._run("scout_route", "high", "dex_bucket")
        assert result == {
            "very-low": False,
            "low": False,
            "mid": True,
            "high": True,
            "very-high": True,
        }

    def test_scout_route_difficulty_very_high(self) -> None:
        result = self._run("scout_route", "very-high", "dex_bucket")
        assert result == {
            "very-low": False,
            "low": False,
            "mid": False,
            "high": True,
            "very-high": True,
        }

    def test_parley_local_difficulty_mid(self) -> None:
        result = self._run("parley_local", "mid", "wis_bucket")
        assert result == {
            "very-low": False,
            "low": True,
            "mid": True,
            "high": True,
            "very-high": False,
        }


# ---------------------------------------------------------------------------
# 4. Edge case: empty company
# ---------------------------------------------------------------------------


class TestEmptyCompany:
    def test_empty_company_returns_empty_tuple(self) -> None:
        """Empty company → () — mission still emerges (option c)."""
        result = filter_eligible_personas("scout_route", "mid", [])
        assert result == ()

    def test_empty_company_parley_local(self) -> None:
        result = filter_eligible_personas("parley_local", "very-high", [])
        assert result == ()


# ---------------------------------------------------------------------------
# 5. Edge case: bucket missing / invalid → hard fail
# ---------------------------------------------------------------------------


class TestBucketMissingHardFail:
    def test_invalid_bucket_value_raises_value_error(self) -> None:
        """A persona with an invalid bucket value (data corruption) must raise ValueError.

        We cannot set an invalid DescriptorBucket via Pydantic validation, so we
        use object.__setattr__ to bypass the frozen model and inject a bad value.
        This simulates upstream data corruption that bypassed schema validation.
        """
        persona = _make_persona("corrupted", dex_bucket="mid")
        # Bypass Pydantic frozen model to inject a bad value.
        object.__setattr__(persona, "dex_bucket", "ultra-high")  # not a valid bucket

        with pytest.raises(ValueError, match="not a valid DescriptorBucket"):
            filter_eligible_personas("scout_route", "mid", [persona])

    def test_none_bucket_raises_value_error(self) -> None:
        """A persona with None bucket (schema contract violation) must raise ValueError."""
        persona = _make_persona("null_dex", dex_bucket="mid")
        object.__setattr__(persona, "dex_bucket", None)

        with pytest.raises(ValueError, match="is None"):
            filter_eligible_personas("scout_route", "mid", [persona])


# ---------------------------------------------------------------------------
# 6. Return order: PersonaId ascending
# ---------------------------------------------------------------------------


class TestReturnOrder:
    def test_eligible_personas_sorted_ascending(self) -> None:
        """Eligible PersonaIds must be sorted ascending, regardless of input order."""
        zara = _make_persona("zara", dex_bucket="mid")
        ada = _make_persona("ada", dex_bucket="low")
        mira = _make_persona("mira", dex_bucket="high")
        # All three are in mid±1 = {low, mid, high}.
        result = filter_eligible_personas("scout_route", "mid", [zara, ada, mira])
        assert result == ("ada", "mira", "zara"), (
            f"Expected sorted ascending (ada, mira, zara). Got {result!r}"
        )

    def test_single_eligible_no_sort_issue(self) -> None:
        persona = _make_persona("brynn", dex_bucket="low")
        result = filter_eligible_personas("scout_route", "mid", [persona])
        assert result == ("brynn",)


# ---------------------------------------------------------------------------
# 7. Deduplication
# ---------------------------------------------------------------------------


class TestDeduplication:
    def test_duplicate_persona_id_deduplicated(self) -> None:
        """Duplicate PersonaId → one entry in output (first occurrence wins)."""
        p1 = _make_persona("kira", dex_bucket="mid")
        p2 = _make_persona("kira", dex_bucket="mid")  # same name, same bucket
        result = filter_eligible_personas("scout_route", "mid", [p1, p2])
        assert result == ("kira",), f"Expected ('kira',) not ('kira', 'kira'). Got {result!r}"

    def test_duplicate_not_eligible_still_deduplicated(self) -> None:
        """Duplicate ineligible persona → still only processed once (no double raise)."""
        p1 = _make_persona("nomad", dex_bucket="very-high")
        p2 = _make_persona("nomad", dex_bucket="very-high")
        result = filter_eligible_personas("scout_route", "mid", [p1, p2])
        assert result == ()
