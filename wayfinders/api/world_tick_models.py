"""Pydantic v2 models for the ``POST /api/world/tick`` endpoint.

These are wire-format types — the JSON shape that the Godot+C# client
(Rune's ApiClient) serializes and deserializes.  They live separately
from the in-process dataclass models in :mod:`wayfinders.models` and from
the UC1 resolution models in :mod:`wayfinders.api.models`.

Varn-locked closed lookups (2026-05-10):
  - ``MissionType``: ``scout_route | parley_local``
  - ``DifficultyBucket``: the five stat-lane buckets (``very-low`` … ``very-high``)
  - ``ResolutionOutcomeType``: ``success | partial | failure``

These literals must not be silently edited by any agent; all changes go
through a Varn ratification step.

Field names use snake_case on the wire; the C# client maps them to
PascalCase records via ``System.Text.Json`` naming policy.

Also defines ``PersonaLegacyTag`` — an in-process frozen dataclass (not a
Pydantic model) that records which missions a persona participated in and
what outcome they saw.  Stored in ``GameState.persona_legacy`` (list per
persona).  Not serialised on the wire in M1; M2 will persist it to
WorldState snapshots.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

# ---------------------------------------------------------------------------
# Varn-locked type aliases (2026-05-10) — DO NOT EDIT without Varn ratification
# ---------------------------------------------------------------------------

# Two mission types for M1 MVP slice.  M2+ will extend this literal.
EmergenceMissionType = Literal["scout_route", "parley_local"]

# Five difficulty buckets — reuses the stat-lane bucket vocabulary from
# ``wayfinders.ml.schemas.vocabularies`` (Varn lock 2026-05-06).
# ``mid`` is the only active bucket in M1 (sampler disabled; forced per spec).
DifficultyBucket = Literal["very-low", "low", "mid", "high", "very-high"]

# Three resolution outcome types for M1 MVP.
ResolutionOutcomeType = Literal["success", "partial", "failure"]

# PersonaId: free-form string for M1 (e.g. "kira", "hodge").
# Typed as str rather than a Literal to avoid locking the roster at schema level —
# roster management is Varn's lane.
PersonaId = str

# RegionId: free-form string for M1 (e.g. "halfgate", "ridgepass").
RegionId = str


# ---------------------------------------------------------------------------
# PersonaLegacyTag — in-process dataclass (not wire format)
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class PersonaLegacyTag:
    """Records a single mission outcome for a persona.

    Frozen and hashable — safe to store in sets or use as dict keys.
    Appended to ``GameState.persona_legacy[persona_id]`` by
    ``on_mission_resolved()`` in :mod:`wayfinders.api.mission_legacy`.

    Fields:
        persona_id:    Identifier of the persona who participated.
        mission_id:    Stable UUID-shaped id of the emergent mission.
        mission_type:  Varn-locked closed lookup (scout_route | parley_local).
        region:        Region where the mission took place (free-form M1).
        actor_target:  Optional target name (e.g. NPC name for parley missions).
                       None for scout missions where there is no named target.
        outcome:       Resolution outcome (Varn-locked: success | partial | failure).
        earned_at_tick: The tick at which the mission resolved.

    Idempotence contract: ``on_mission_resolved`` checks for an existing tag
    with the same ``(persona_id, mission_id)`` pair before appending.  Calling
    the hook twice with identical inputs produces only one tag per persona.
    """

    persona_id: PersonaId
    mission_id: str
    mission_type: EmergenceMissionType
    region: RegionId
    actor_target: str | None
    outcome: ResolutionOutcomeType
    earned_at_tick: int


# ---------------------------------------------------------------------------
# Request
# ---------------------------------------------------------------------------


class WorldTickRequest(BaseModel):
    """Input for the ``POST /api/world/tick`` endpoint.

    Minimum-viable M1 WorldState.  Only the fields needed to drive the
    UC1 emergence classifier are included; full WorldState (district
    positions, faction tensions, etc.) is M2.

    ``context_prose`` is the Varn-locked slot from the UC1 resolution
    schema — identical field name, same EN-pinning contract.  Do NOT
    rename or restructure it here.

    ``seed`` is caller-owned: the game-logic layer derives it from the
    world seed and tick index (``hash(world_seed, tick)``) so that
    emergence is reproducible given the same world state.  The server
    never uses a global RNG.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    tick: int = Field(
        ...,
        ge=0,
        description="Current tick index (non-negative). Used for cadence gating.",
    )
    seed: int = Field(
        ...,
        description=(
            "RNG seed for deterministic sampling.  Caller derives this as "
            "``hash(world_seed, tick)`` so that the same (tick, world_seed) always "
            "produces the same outcome.  The server holds no global RNG state."
        ),
    )
    context_prose: str = Field(
        ...,
        min_length=1,
        description=(
            "EN-rendered context prose (render_context() output). "
            "Varn-locked slot 2026-05-01 — same field as UC1 resolution endpoint. "
            "Must be non-empty. EN-pinned regardless of UI locale."
        ),
    )


# ---------------------------------------------------------------------------
# Response
# ---------------------------------------------------------------------------


class EmergentMission(BaseModel):
    """A mission that has emerged at the current tick.

    Fields are Varn-locked (2026-05-10).  Immutable once created — the
    game-logic layer stores it in ``GameState`` and creates a separate
    ``MissionAssignment`` object when the player acts on it.

    M1 constraints (see spec):
      - ``difficulty`` is always ``mid`` (sampler disabled in M1).
      - ``deadline_ticks`` is always ``None`` (no timer in M1).
      - ``outcome`` is always ``None`` at emergence (set at resolution).
      - ``eligible_personas`` is always ``[]`` in M1 stub —
        persona-eligibility filtering is Etape 3 / M2 scope.
        Flagged for Varn: the filtering rule (match DEX/WIS tags to mission
        type) is not yet spec'd in enough detail to implement in M1.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    id: str = Field(
        ...,
        min_length=1,
        description="UUID4 generated at tick of emergence — stable identifier for GameState.",
    )
    type: EmergenceMissionType = Field(
        ...,
        description="Varn-locked closed lookup: scout_route | parley_local.",
    )
    narrative_hook: str = Field(
        ...,
        min_length=1,
        description=(
            "1-2 line narrative hook rendered server-side. "
            "M1: f-string template (Varn §9.4 design decision). "
            "M2+: UC1 prose renderer or sibling model."
        ),
    )
    eligible_personas: list[PersonaId] = Field(
        default_factory=list,
        description=(
            "Persona IDs eligible to be assigned to this mission. "
            "M1 stub: always [] — eligibility filter (DEX/WIS tag match) "
            "is Etape 3 scope.  OPEN: Varn has not yet spec'd the filter rule "
            "in detail — flag for ratification before Etape 3 implementation."
        ),
    )
    difficulty: DifficultyBucket = Field(
        default="mid",
        description=(
            "Difficulty bucket. Varn-locked to 5 buckets (reuses stat-lane vocab). "
            "M1: forced to 'mid' (sampler disabled per spec §6)."
        ),
    )
    region: RegionId = Field(
        ...,
        min_length=1,
        description="Region where the mission takes place. Free-form string in M1.",
    )
    deadline_ticks: int | None = Field(
        default=None,
        description="Deadline in ticks from emergence. None in M1 (no timer).",
    )
    outcome: ResolutionOutcomeType | None = Field(
        default=None,
        description=(
            "Resolution outcome. None at emergence — set by the resolution endpoint "
            "or game-logic layer when the mission concludes."
        ),
    )
    seed: int = Field(
        ...,
        description="The spawn seed from the tick that produced this mission. Audit trail.",
    )


class WorldTickResponse(BaseModel):
    """Response from ``POST /api/world/tick``.

    ``mission`` is ``None`` when no mission emerges this tick (``no_emergence``
    outcome after cadence gating).

    In M1, with the no_emergence sampler disabled and cadence forced to
    every 5-10 ticks, ``None`` is returned only between cadence windows.
    Once inside the window, the fallback to ``argmax({scout_route, parley_local})``
    ensures a mission always emerges.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    mission: EmergentMission | None = Field(
        ...,
        description=(
            "The emergent mission for this tick, or None if no mission emerges. "
            "None is returned between cadence windows (ticks not in the 5-10 window). "
            "Never None within a cadence window (no_emergence fallback active in M1)."
        ),
    )
    tick: int = Field(
        ...,
        ge=0,
        description="Echo of the request tick — allows the client to correlate async responses.",
    )
