"""Tests for the Varn-locked RegionId closed lookup (ratified 2026-05-10).

Four test categories:

1. **Pydantic validation** — valid canonical values are accepted; any value
   outside the closed lookup raises a 422 on the HTTP layer and a
   ``ValidationError`` on the Pydantic model directly.

2. **Fallback determinism** — ``_extract_region_from_prose`` with an
   unrecognised prose string returns the same ``RegionId`` for the same seed,
   and a valid ``RegionId`` for any seed.

3. **Fallback distribution** — over 1 000 calls with distinct seeds and
   prose that contains no recognised region name, every canonical RegionId
   appears within ±5 % of the uniform expectation (20 %).

4. **Prose extraction** — prose strings that contain a canonical region name
   (case-insensitive after "in/near/at") are correctly matched without
   falling back to the seeded sampler.

These tests never touch a real ONNX model.  All validation paths are
covered via direct model construction and via the FastAPI HTTP layer
(TestClient against the app).
"""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from wayfinders.api.app import app
from wayfinders.api.mission_conclude import MissionConcludeRequest
from wayfinders.api.mission_resolve import MissionResolveRequest
from wayfinders.api.world_tick import _extract_region_from_prose
from wayfinders.api.world_tick_models import (
    REGION_IDS,
    EmergentMission,
    PersonaLegacyTag,
    RegionId,
)

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# All five canonical RegionId values (Varn-lock 2026-05-10)
_CANONICAL: tuple[RegionId, ...] = (
    "halfgate",
    "brescaille",
    "fendelune",
    "veillemont",
    "roches-closes",
)

# Prose that contains no recognisable region name — forces the seeded fallback.
_PROSE_NO_REGION = (
    "The company camps at the edge of the forest.\n"
    "The stars are obscured by heavy cloud.\n"
    "Rain falls steadily on the muddy track."
)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


@pytest.fixture
def client() -> TestClient:
    """Minimal FastAPI TestClient — predictor state is irrelevant for these tests."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        yield tc


def _make_conclude_payload(region: str) -> dict:
    return {
        "mission_id": "region-test-001",
        "mission_type": "scout_route",
        "region": region,
        "difficulty": "mid",
        "assigned_personas": [{"persona_id": "kira", "dex_bucket": "mid", "wis_bucket": "mid"}],
        "seed": 42,
        "tick_due": 10,
    }


def _make_resolve_payload(region: str) -> dict:
    return {
        "mission_id": "region-test-001",
        "mission_type": "scout_route",
        "region": region,
        "outcome": "success",
        "tick": 5,
        "assigned_personas": ["kira"],
    }


# ---------------------------------------------------------------------------
# 1. Pydantic validation — valid canonical values accepted
# ---------------------------------------------------------------------------


class TestRegionIdValidation:
    @pytest.mark.parametrize("region", _CANONICAL)
    def test_canonical_value_accepted_in_emergent_mission(self, region: RegionId) -> None:
        """All five canonical RegionId values must be accepted by EmergentMission."""
        mission = EmergentMission(
            id="test-mission",
            type="scout_route",
            narrative_hook="test hook",
            eligible_personas=(),
            difficulty="mid",
            region=region,
            deadline_ticks=None,
            outcome=None,
            seed=0,
        )
        assert mission.region == region

    @pytest.mark.parametrize("region", _CANONICAL)
    def test_canonical_value_accepted_in_persona_legacy_tag(self, region: RegionId) -> None:
        """All five canonical RegionId values must be accepted by PersonaLegacyTag."""
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m-001",
            mission_type="scout_route",
            region=region,
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        assert tag.region == region

    @pytest.mark.parametrize("region", _CANONICAL)
    def test_canonical_value_accepted_in_mission_conclude_request(self, region: RegionId) -> None:
        """All five canonical RegionId values accepted in MissionConcludeRequest."""
        req = MissionConcludeRequest(
            mission_id="m-val-001",
            mission_type="scout_route",
            region=region,
            difficulty="mid",
            assigned_personas=(
                __import__(
                    "wayfinders.api.mission_conclude",
                    fromlist=["CharacterStateWireDto"],
                ).CharacterStateWireDto(
                    persona_id="kira",
                    dex_bucket="mid",
                    wis_bucket="mid",
                ),
            ),
            seed=1,
            tick_due=5,
        )
        assert req.region == region

    @pytest.mark.parametrize("region", _CANONICAL)
    def test_canonical_value_accepted_in_mission_resolve_request(self, region: RegionId) -> None:
        """All five canonical RegionId values accepted in MissionResolveRequest."""
        req = MissionResolveRequest(
            mission_id="m-val-002",
            mission_type="scout_route",
            region=region,
            outcome="success",
            tick=0,
        )
        assert req.region == region

    @pytest.mark.parametrize(
        "invalid_region",
        [
            "ridgepass",  # was used in M1 pre-lock; not canonical
            "ironshore",  # was used in M1 pre-lock; not canonical
            "vauldrun",  # was used in M1 pre-lock; not canonical
            "the region",  # old fallback default; not canonical
            "Halfgate",  # correct name, wrong case (closed lookup is lowercase)
            "Fendelune",  # correct name, wrong case
            "BRESCAILLE",  # all-caps; not canonical
            "",  # empty string; never valid
            "unknown",  # arbitrary string
        ],
    )
    def test_invalid_region_rejected_by_mission_resolve_request(self, invalid_region: str) -> None:
        """Values outside the closed lookup raise ValidationError."""
        with pytest.raises(ValidationError):
            MissionResolveRequest(
                mission_id="m1",
                mission_type="scout_route",  # type: ignore[arg-type]
                region=invalid_region,  # type: ignore[arg-type]
                outcome="success",  # type: ignore[arg-type]
                tick=0,
            )

    @pytest.mark.parametrize(
        "invalid_region",
        [
            "ridgepass",
            "ironshore",
            "the region",
            "Halfgate",
            "",
        ],
    )
    def test_invalid_region_returns_422_on_conclude_endpoint(
        self, client: TestClient, invalid_region: str
    ) -> None:
        """HTTP /conclude endpoint returns 422 for non-canonical region values."""
        resp = client.post(
            "/api/world/mission/conclude", json=_make_conclude_payload(invalid_region)
        )
        assert resp.status_code == 422, (
            f"Expected 422 for region={invalid_region!r}, got {resp.status_code}"
        )

    @pytest.mark.parametrize(
        "invalid_region",
        [
            "ridgepass",
            "ironshore",
            "the region",
            "Halfgate",
            "",
        ],
    )
    def test_invalid_region_returns_422_on_resolve_endpoint(
        self, client: TestClient, invalid_region: str
    ) -> None:
        """HTTP /resolve endpoint returns 422 for non-canonical region values."""
        resp = client.post("/api/world/mission/resolve", json=_make_resolve_payload(invalid_region))
        assert resp.status_code == 422, (
            f"Expected 422 for region={invalid_region!r}, got {resp.status_code}"
        )


# ---------------------------------------------------------------------------
# 2. Fallback determinism
# ---------------------------------------------------------------------------


class TestRegionFallbackDeterminism:
    def test_same_prose_same_seed_same_region(self) -> None:
        """_extract_region_from_prose is deterministic: same inputs → same output."""
        r1 = _extract_region_from_prose(_PROSE_NO_REGION, seed=12345)
        r2 = _extract_region_from_prose(_PROSE_NO_REGION, seed=12345)
        assert r1 == r2

    def test_fallback_always_returns_canonical_value(self) -> None:
        """Seeded fallback must always return a value in REGION_IDS."""
        for seed in range(200):
            region = _extract_region_from_prose(_PROSE_NO_REGION, seed=seed)
            assert region in REGION_IDS, (
                f"seed={seed}: fallback returned {region!r}, not in REGION_IDS"
            )

    def test_different_seeds_can_produce_different_regions(self) -> None:
        """Different seeds should produce different regions (statistically).

        Over 200 seeds, we expect at least 2 distinct regions from the fallback.
        The probability that all 200 seeds produce the same region under uniform
        sampling is (1/5)^199 ≈ 0, so this test is not flaky.
        """
        regions = {_extract_region_from_prose(_PROSE_NO_REGION, seed=s) for s in range(200)}
        assert len(regions) > 1, "Expected multiple distinct regions across 200 seeds"

    def test_fallback_default_seed_is_deterministic(self) -> None:
        """Default seed=0 must produce the same region across calls."""
        r1 = _extract_region_from_prose(_PROSE_NO_REGION)
        r2 = _extract_region_from_prose(_PROSE_NO_REGION)
        assert r1 == r2
        assert r1 in REGION_IDS


# ---------------------------------------------------------------------------
# 3. Fallback distribution — uniform ±5% over 1 000 ticks
# ---------------------------------------------------------------------------


class TestRegionFallbackDistribution:
    def test_fallback_distribution_uniform_over_1000_seeds(self) -> None:
        """Over 1 000 distinct seeds with no-match prose, each of the 5 regions
        must appear between 15 % and 25 % of the time (uniform ±5 pp).

        If the RNG were biased toward a subset of regions, this test would catch it.
        """
        n = 1000
        counts: dict[str, int] = dict.fromkeys(REGION_IDS, 0)

        for seed in range(n):
            region = _extract_region_from_prose(_PROSE_NO_REGION, seed=seed)
            assert region in counts, f"Unexpected region {region!r} at seed={seed}"
            counts[region] += 1

        expected_frac = 1.0 / len(REGION_IDS)  # 0.20
        tolerance = 0.05  # ±5 percentage points

        for region, count in counts.items():
            actual_frac = count / n
            assert abs(actual_frac - expected_frac) <= tolerance, (
                f"Region {region!r}: {count}/{n} = {actual_frac:.3f}, "
                f"expected {expected_frac:.3f} ±{tolerance:.3f}. "
                f"Full distribution: {counts}"
            )


# ---------------------------------------------------------------------------
# 4. Prose extraction — canonical names correctly matched
# ---------------------------------------------------------------------------


class TestRegionProseExtraction:
    @pytest.mark.parametrize(
        "prose, expected",
        [
            # "in <region>" patterns
            ("The company travels in halfgate on the old road.", "halfgate"),
            ("A matter in brescaille — the road is uncharted.", "brescaille"),
            ("The mission objective is in fendelune before dawn.", "fendelune"),
            ("Scouts report movement near veillemont.", "veillemont"),
            ("The camp is at roches-closes near the ridge.", "roches-closes"),
            # Title-case in prose — renderer may capitalise; extractor normalises
            ("The mission is near Halfgate.", "halfgate"),
            ("A parley in Brescaille.", "brescaille"),
            ("Movement spotted near Fendelune.", "fendelune"),
            ("The road leads at Veillemont.", "veillemont"),
        ],
    )
    def test_prose_extraction_matches_canonical(self, prose: str, expected: RegionId) -> None:
        """Prose containing a canonical region name must extract that region."""
        result = _extract_region_from_prose(prose, seed=0)
        assert result == expected, f"Prose {prose!r}: expected {expected!r}, got {result!r}"

    def test_unrecognised_prose_returns_valid_region(self) -> None:
        """Prose with no recognisable region must still return a valid RegionId."""
        prose = "The road winds through an unnamed pass."
        result = _extract_region_from_prose(prose, seed=99)
        assert result in REGION_IDS

    def test_extraction_is_case_insensitive(self) -> None:
        """Extractor normalises Title Case, ALL CAPS, etc. to lowercase lookup."""
        for canonical in _CANONICAL:
            title_prose = f"The company camps near {canonical.title()}."
            result = _extract_region_from_prose(title_prose, seed=0)
            assert result == canonical, (
                f"Title case {canonical.title()!r} not matched to {canonical!r}"
            )

    def test_first_match_wins(self) -> None:
        """When multiple regions appear after in/near/at, the first match is returned."""
        # halfgate appears after "in", brescaille after "near" — halfgate wins.
        prose = "The company halts in halfgate and scouts near brescaille."
        result = _extract_region_from_prose(prose, seed=0)
        assert result == "halfgate"

    def test_roches_closes_hyphen_preserved(self) -> None:
        """roches-closes (hyphenated) must match correctly."""
        prose = "A scout departs from roches-closes at dawn."
        result = _extract_region_from_prose(prose, seed=0)
        assert result == "roches-closes"
