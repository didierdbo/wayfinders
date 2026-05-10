"""Mission conclusion endpoint — ``POST /api/world/mission/conclude``.

Stateless architecture (same as mission_resolve): the server computes the
weighted outcome roll + creates PersonaLegacyTags in a transient GameState,
then returns the tags for the client to persist.

Differs from ``/api/world/mission/resolve`` in that the *outcome is not
supplied by the client*: the server computes it deterministically from the
assigned persona stat-buckets, the mission difficulty, and the seed/tick
pair.  The client no longer decides the outcome — it sends the persona
snapshots and the server owns the roll.

## Deterministic hash convention (important for Rune's C# cross-check)

The per-persona roll seed is derived via SHA-256 (not Python's built-in
``hash()``):

    key  = f"{seed}:{tick_due}:{persona_id}"
    digest = hashlib.sha256(key.encode("utf-8")).digest()
    seed_int = int.from_bytes(digest[:8], "big")   # first 8 bytes → uint64
    roll = random.Random(seed_int).random()         # uniform [0, 1)

Why SHA-256 and not ``hash()``:
  - ``hash()`` is randomised per-process via PYTHONHASHSEED; it is NOT
    stable across invocations or across languages.
  - SHA-256 on the UTF-8 string is deterministic everywhere — Python,
    C#, Rust, whatever Rune uses on the client side.

C# equivalent:

    var key = $"{seed}:{tickDue}:{personaId}";
    var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
    ulong seedInt = BinaryPrimitives.ReadUInt64BigEndian(bytes);
    // feed seedInt to a uniform RNG; System.Random(seedInt) uses a different
    // algorithm so for parity Rune should use a portable Xoshiro or just
    // reproduce the float from the same bit pattern.
    //
    // Practical note (M2): the C# client currently trusts the server outcome.
    // The hash convention is documented so Rune CAN verify independently in
    // a future parity test, not because the client re-rolls today.

## Probability table (Varn-ratified 2026-05-10)

    delta ≥ +1  → 80% success / 15% partial / 5%  failure
    delta = 0   → 50% success / 35% partial / 15% failure
    delta = -1  → 20% success / 40% partial / 40% failure
    delta <= -2  → 5%  success / 25% partial / 70% failure

where delta = ordinal(persona_bucket) - ordinal(difficulty_bucket)
      ordinal: very-low=0, low=1, mid=2, high=3, very-high=4

## Stat lane mapping (Varn-ratified 2026-05-10)

    scout_route  → dex_bucket
    parley_local → wis_bucket

## Multi-persona aggregation (M3 design, M2 stub)

M2 spec: single persona only.  The structure is list-ready for M3.
Aggregation rule for M3: max_bucket — the best persona carries the roll.
M2: trivially the single persona.

Varn-locked types re-used without modification:
  EmergenceMissionType, DifficultyBucket, ResolutionOutcomeType,
  PersonaId, RegionId, DescriptorBucket.
"""

from __future__ import annotations

import hashlib
import logging
import random

from pydantic import BaseModel, ConfigDict, Field

from wayfinders.api.mission_legacy import on_mission_resolved
from wayfinders.api.mission_resolve import PersonaLegacyTagWire, _minimal_game_state
from wayfinders.api.world_tick_models import (
    DifficultyBucket,
    EmergenceMissionType,
    EmergentMission,
    PersonaId,
    RegionId,
    ResolutionOutcomeType,
)
from wayfinders.ml.schemas.vocabularies import DescriptorBucket

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Ordinal mapping for DifficultyBucket / DescriptorBucket
# (both share the same 5-bucket vocabulary)
# ---------------------------------------------------------------------------

_BUCKET_ORDINAL: dict[str, int] = {
    "very-low": 0,
    "low": 1,
    "mid": 2,
    "high": 3,
    "very-high": 4,
}

# ---------------------------------------------------------------------------
# Probability table (Varn-ratified 2026-05-10)
# Each entry: (p_success, p_partial, p_failure) — sums to 1.0
# ---------------------------------------------------------------------------

#  delta_key is clamped: delta >= +1 → 1, delta = 0 → 0, delta = -1 → -1,
#  delta <= -2 → -2
_PROB_TABLE: dict[int, tuple[float, float, float]] = {
    1: (0.80, 0.15, 0.05),
    0: (0.50, 0.35, 0.15),
    -1: (0.20, 0.40, 0.40),
    -2: (0.05, 0.25, 0.70),
}


def _clamp_delta(delta: int) -> int:
    """Clamp delta to the four rows of the probability table."""
    if delta >= 1:
        return 1
    if delta <= -2:
        return -2
    return delta  # 0 or -1


def _derive_roll_seed(seed: int, tick_due: int, persona_id: str) -> int:
    """Derive a deterministic, cross-platform RNG seed for one persona's roll.

    Uses SHA-256 on the UTF-8 key string so the result is identical in any
    language (Python, C#, etc.).  See module docstring for the C# equivalent.

    Args:
        seed:       Mission spawn seed (re-used from EmergentMission.seed).
        tick_due:   Tick at which the mission expires (adds timeline anchor).
        persona_id: Persona identifier string.

    Returns:
        int: A 64-bit unsigned integer suitable as a ``random.Random`` seed.
    """
    key = f"{seed}:{tick_due}:{persona_id}"
    digest = hashlib.sha256(key.encode("utf-8")).digest()
    return int.from_bytes(digest[:8], "big")


def _roll_outcome(
    seed: int,
    tick_due: int,
    persona_id: str,
    delta: int,
) -> tuple[ResolutionOutcomeType, float, tuple[float, float, float]]:
    """Roll an outcome for a single persona.

    Args:
        seed:       Mission spawn seed.
        tick_due:   Mission expiry tick.
        persona_id: Persona identifier.
        delta:      ordinal(persona_bucket) - ordinal(difficulty_bucket).

    Returns:
        (outcome, roll_value, prob_table)
        - outcome:    Varn-locked ResolutionOutcomeType
        - roll_value: The raw random() value in [0, 1) for the debug breakdown
        - prob_table: The (p_success, p_partial, p_failure) row that was applied
    """
    clamped = _clamp_delta(delta)
    p_success, p_partial, p_failure = _PROB_TABLE[clamped]

    rng_seed = _derive_roll_seed(seed, tick_due, persona_id)
    roll = random.Random(rng_seed).random()

    if roll < p_success:
        outcome: ResolutionOutcomeType = "success"
    elif roll < p_success + p_partial:
        outcome = "partial"
    else:
        outcome = "failure"

    return outcome, roll, (p_success, p_partial, p_failure)


# ---------------------------------------------------------------------------
# Request / Response wire models
# ---------------------------------------------------------------------------


class CharacterStateWireDto(BaseModel):
    """Minimal character snapshot needed for the conclude roll.

    Only the stat buckets and persona identity are required — the server
    does not need full CharacterState for the roll.  Keeping this thin
    reduces wire size and avoids coupling the conclude endpoint to the
    full CharacterState schema evolution.

    ``persona_id`` is the stable identifier matching ``PersonaId`` usage
    elsewhere in the API.  The full ``CharacterState`` (for prose rendering)
    is not included here — the conclude endpoint does not invoke renderers.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    persona_id: PersonaId = Field(
        ...,
        min_length=1,
        description="Persona identifier (e.g. 'kira'). Stable across ticks.",
    )
    dex_bucket: DescriptorBucket = Field(
        ...,
        description=(
            "DEX stat bucket (very-low … very-high). Used for scout_route mission stat lane."
        ),
    )
    wis_bucket: DescriptorBucket = Field(
        ...,
        description=(
            "WIS stat bucket (very-low … very-high). Used for parley_local mission stat lane."
        ),
    )


class MissionConcludeRequest(BaseModel):
    """Input for ``POST /api/world/mission/conclude``.

    The client sends the mission metadata, the assigned persona snapshots,
    the mission seed (from EmergentMission.seed), and the tick_due (tick at
    which the mission expires).  The server computes the outcome and returns
    tags + roll breakdown in one round-trip.

    All Varn-locked closed lookups (mission_type, difficulty) are preserved
    without modification.

    M2 constraint: ``assigned_personas`` must contain exactly one persona.
    The structure is a tuple (not a single object) so M3 can extend to
    multi-persona without a breaking schema change.  The endpoint raises 422
    if the tuple is empty — the client must filter ``decline`` before calling.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    mission_id: str = Field(
        ...,
        min_length=1,
        description=(
            "Stable UUID-shaped id from the EmergentMission that emerged at tick of origin."
        ),
    )
    mission_type: EmergenceMissionType = Field(
        ...,
        description="Varn-locked closed lookup: scout_route | parley_local.",
    )
    region: RegionId = Field(
        ...,
        min_length=1,
        description="Region where the mission took place.",
    )
    actor_target: str | None = Field(
        default=None,
        description=(
            "Named NPC target for parley missions. None for scout missions and "
            "anonymous-parley missions."
        ),
    )
    difficulty: DifficultyBucket = Field(
        ...,
        description="Mission difficulty bucket. Varn-locked 5-bucket vocabulary.",
    )
    assigned_personas: tuple[CharacterStateWireDto, ...] = Field(
        ...,
        min_length=1,
        description=(
            "Persona snapshots assigned to this mission. Must not be empty — "
            "the client filters 'decline' before calling /conclude. "
            "M2: exactly one persona. Structure is multi-ready for M3."
        ),
    )
    seed: int = Field(
        ...,
        description=(
            "Mission spawn seed (re-used from EmergentMission.seed). "
            "Combined with tick_due and persona_id to derive the deterministic roll seed."
        ),
    )
    tick_due: int = Field(
        ...,
        ge=0,
        description=(
            "Tick at which the mission expires. Used as a timeline anchor in the "
            "roll seed derivation so two missions with the same spawn seed but "
            "different deadlines produce different roll outcomes."
        ),
    )


class RollBreakdownEntry(BaseModel):
    """Per-persona roll debug information.

    Included in the response for transparency and client-side cross-checking.
    Not used by the game-logic layer for any gameplay decision — the
    ``outcome`` field in ``MissionConcludeResponse`` is the authoritative result.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    persona_id: PersonaId = Field(..., description="Persona this entry describes.")
    bucket_used: DescriptorBucket = Field(
        ...,
        description="The stat bucket selected for this persona (dex or wis, per mission type).",
    )
    delta: int = Field(
        ...,
        description=(
            "ordinal(persona_bucket) - ordinal(difficulty). "
            "Clamped to [-2, +1] for table lookup but raw value is reported here."
        ),
    )
    roll_value: float = Field(
        ...,
        ge=0.0,
        lt=1.0,
        description="Raw random() value in [0, 1) used for the outcome determination.",
    )
    probability_table: tuple[float, float, float] = Field(
        ...,
        description="(p_success, p_partial, p_failure) row applied for this persona's delta.",
    )


class MissionConcludeResponse(BaseModel):
    """Output of ``POST /api/world/mission/conclude``.

    ``outcome`` is the Varn-locked resolution outcome computed by the server.
    ``tags_created`` are the PersonaLegacyTag records for the client to store.
    ``roll_breakdown`` is per-persona debug data.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    outcome: ResolutionOutcomeType = Field(
        ...,
        description="Server-computed resolution outcome: success | partial | failure.",
    )
    tags_created: tuple[PersonaLegacyTagWire, ...] = Field(
        default=(),
        description=(
            "PersonaLegacyTag records created for each assigned persona. "
            "Store client-side in GameState.PersonaLegacy[personaId]."
        ),
    )
    roll_breakdown: list[RollBreakdownEntry] = Field(
        default_factory=list,
        description=(
            "Per-persona roll debug data. "
            "Includes bucket_used, delta, roll_value, probability_table for each persona."
        ),
    )


# ---------------------------------------------------------------------------
# Core logic
# ---------------------------------------------------------------------------


def _aggregate_outcome(
    per_persona_outcomes: list[tuple[str, ResolutionOutcomeType]],
) -> ResolutionOutcomeType:
    """Aggregate per-persona outcomes for M3 multi-persona support.

    M2: exactly one persona, so the single outcome is returned directly.
    M3 rule (spec): max_bucket — the best persona carries the roll.
    Translated to outcome: success > partial > failure (best wins).

    Args:
        per_persona_outcomes: list of (persona_id, outcome) pairs.

    Returns:
        The aggregated ResolutionOutcomeType.
    """
    outcome_rank: dict[ResolutionOutcomeType, int] = {
        "success": 2,
        "partial": 1,
        "failure": 0,
    }
    best: ResolutionOutcomeType = "failure"
    for _, outcome in per_persona_outcomes:
        if outcome_rank[outcome] > outcome_rank[best]:
            best = outcome
    return best


def conclude_mission(payload: MissionConcludeRequest) -> MissionConcludeResponse:
    """Compute weighted outcome roll + create PersonaLegacyTags in one round-trip.

    Algorithm:
    1. For each persona in ``assigned_personas``:
       a. Map mission_type → stat bucket (dex or wis).
       b. Compute delta = ordinal(persona_bucket) - ordinal(difficulty).
       c. Derive deterministic roll seed from (seed, tick_due, persona_id)
          via SHA-256 (cross-platform stable — see module docstring).
       d. Sample outcome from the probability table for the clamped delta.
    2. Aggregate per-persona outcomes: max wins (best persona carries the roll).
       M2: trivial (single persona).
    3. Reconstruct an EmergentMission, call on_mission_resolved in a transient
       GameState, collect the produced PersonaLegacyTags.
    4. Return outcome + tags + roll_breakdown.

    Args:
        payload: Validated ``MissionConcludeRequest``.

    Returns:
        ``MissionConcludeResponse`` with outcome, tags, and roll debug data.

    Raises:
        ValueError: If ``assigned_personas`` is empty (should be caught by
                    Pydantic min_length=1 before reaching this function).
        KeyError:   If a mission_type has no stat-lane mapping (spec error).
    """
    if not payload.assigned_personas:
        # Should not be reached — Pydantic min_length=1 fires first.
        raise ValueError("assigned_personas must not be empty")

    difficulty_ordinal = _BUCKET_ORDINAL[payload.difficulty]

    per_persona_outcomes: list[tuple[str, ResolutionOutcomeType]] = []
    breakdown_entries: list[RollBreakdownEntry] = []

    for persona_dto in payload.assigned_personas:
        bucket: DescriptorBucket = _stat_bucket_for_mission_from_dto(
            payload.mission_type, persona_dto
        )
        bucket_ordinal = _BUCKET_ORDINAL[bucket]
        delta = bucket_ordinal - difficulty_ordinal

        outcome, roll_value, prob_table = _roll_outcome(
            seed=payload.seed,
            tick_due=payload.tick_due,
            persona_id=persona_dto.persona_id,
            delta=delta,
        )

        per_persona_outcomes.append((persona_dto.persona_id, outcome))
        breakdown_entries.append(
            RollBreakdownEntry(
                persona_id=persona_dto.persona_id,
                bucket_used=bucket,
                delta=delta,
                roll_value=roll_value,
                probability_table=prob_table,
            )
        )

    # Aggregate: best persona carries the roll (M3-ready; M2 trivially single).
    final_outcome = _aggregate_outcome(per_persona_outcomes)

    # Reconstruct EmergentMission for the on_mission_resolved hook.
    mission = EmergentMission(
        id=payload.mission_id,
        type=payload.mission_type,
        narrative_hook=f"Concluded: {payload.mission_type} in {payload.region}.",
        eligible_personas=tuple(p.persona_id for p in payload.assigned_personas),
        difficulty=payload.difficulty,
        region=payload.region,
        deadline_ticks=None,
        outcome=final_outcome,
        seed=payload.seed,
    )

    game_state = _minimal_game_state()
    persona_ids = [p.persona_id for p in payload.assigned_personas]

    on_mission_resolved(
        mission=mission,
        outcome=final_outcome,
        tick=payload.tick_due,
        game_state=game_state,
        persona_ids=persona_ids,
    )

    # Collect tags.
    wire_tags: list[PersonaLegacyTagWire] = []
    for persona_id in persona_ids:
        for tag in game_state.persona_legacy.get(persona_id, []):
            wire_tags.append(PersonaLegacyTagWire.from_tag(tag))

    logger.info(
        "[CONCLUDE] mission=%s difficulty=%s outcome=%s tick_due=%d personas=%s tags=%d",
        payload.mission_id,
        payload.difficulty,
        final_outcome,
        payload.tick_due,
        persona_ids,
        len(wire_tags),
    )

    return MissionConcludeResponse(
        outcome=final_outcome,
        tags_created=tuple(wire_tags),
        roll_breakdown=breakdown_entries,
    )


def _stat_bucket_for_mission_from_dto(
    mission_type: EmergenceMissionType,
    persona_dto: CharacterStateWireDto,
) -> DescriptorBucket:
    """Select the relevant stat bucket from a CharacterStateWireDto.

    scout_route  → dex_bucket
    parley_local → wis_bucket
    """
    lane_map: dict[str, DescriptorBucket] = {
        "scout_route": persona_dto.dex_bucket,
        "parley_local": persona_dto.wis_bucket,
    }
    if mission_type not in lane_map:
        raise KeyError(
            f"No stat-lane mapping for mission_type={mission_type!r}. "
            "Add entry and ratify with Varn."
        )
    return lane_map[mission_type]
