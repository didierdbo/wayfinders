"""Mission resolution endpoint — ``POST /api/world/mission/resolve``.

.. deprecated::
    Use ``POST /api/world/mission/conclude`` (``mission_conclude.py``) for new code.
    This endpoint requires the client to supply the outcome, which violates the
    server-authority contract for the probability table (Varn-ratified 2026-05-10).
    Kept for backward-compatibility with M1 tests.  Will be removed at M3.


Stateless architecture (ratified 2026-05-10): the server reconstructs an
``EmergentMission`` from the request fields, runs ``on_mission_resolved``
against a *transient* ``GameState``, and returns the produced
``PersonaLegacyTag`` objects in the response for the client to store.

No ``GameState`` is persisted server-side.  The authoritative game state
lives in the Godot client (NPC-autonomy lock 2026-05-09).

Request shape (``MissionResolveRequest``):
    mission_id          str                           Stable UUID from tick
    mission_type        EmergenceMissionType          scout_route | parley_local
    region              RegionId                      Free-form string
    actor_target        str | None                    Named NPC or None
    outcome             ResolutionOutcomeType         success | partial | failure
    tick                int                           Tick at resolution
    assigned_personas   tuple[PersonaId, ...]         Personas assigned

Response shape (``MissionResolveResponse``):
    tags_created        tuple[PersonaLegacyTagWire, ...]   Tags to store client-side

``PersonaLegacyTag`` (dataclass) is *not* directly JSON-serialisable from
Pydantic because it is a stdlib dataclass with ``from __future__ import
annotations``.  A thin ``PersonaLegacyTagWire`` Pydantic model is used for
the wire format so that FastAPI can serialise it cleanly without requiring
the client to deserialise a raw dataclass repr.

Varn-locked types re-used:
    EmergenceMissionType  —  Literal from world_tick_models (Varn 2026-05-10)
    ResolutionOutcomeType —  Literal from world_tick_models (Varn 2026-05-10)
    PersonaId             —  str alias from world_tick_models
    RegionId              —  str alias from world_tick_models

M1 spec note: for M1, all missions assigned to personas resolve with their
provided outcome.  There is no server-side override of the outcome.  The
"all missions succeed after K ticks" cadence is a future game-loop concern,
not an API-layer concern.
"""

from __future__ import annotations

import logging

from pydantic import BaseModel, ConfigDict, Field

from wayfinders.api.mission_legacy import on_mission_resolved
from wayfinders.api.world_tick_models import (
    EmergenceMissionType,
    EmergentMission,
    PersonaId,
    PersonaLegacyTag,
    RegionId,
    ResolutionOutcomeType,
)
from wayfinders.models import (
    ActionCard,
    Background,
    Character,
    GameState,
    MapNode,
    Party,
    Stat,
)

logger = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Wire models — Pydantic for clean JSON serialisation
# ---------------------------------------------------------------------------


class MissionResolveRequest(BaseModel):
    """Input for ``POST /api/world/mission/resolve``.

    The client sends the ``EmergentMission`` fields plus the outcome and
    the list of assigned personas.  The server reconstructs the mission,
    runs ``on_mission_resolved``, and returns the created tags.

    ``mission_type`` and ``outcome`` are Varn-locked closed lookups.
    ``assigned_personas`` is empty when the mission was declined — the
    response will be ``tags_created=()``.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    mission_id: str = Field(
        ...,
        min_length=1,
        description=(
            "Stable UUID-shaped id from the EmergentMission that emerged at tick of origin. "
            "Produced by the world-tick endpoint and stored client-side."
        ),
    )
    mission_type: EmergenceMissionType = Field(
        ...,
        description="Varn-locked closed lookup: scout_route | parley_local.",
    )
    region: RegionId = Field(
        ...,
        min_length=1,
        description="Region where the mission took place. Free-form string in M1.",
    )
    actor_target: str | None = Field(
        default=None,
        description=(
            "Named NPC target for parley missions. None for scout missions "
            "and parley missions without a named contact (M1: always None, "
            "no named NPCs in M1 templates)."
        ),
    )
    outcome: ResolutionOutcomeType = Field(
        ...,
        description="Resolution outcome decided by the gameplay layer: success | partial | failure.",
    )
    tick: int = Field(
        ...,
        ge=0,
        description="Game tick at which the mission resolved.",
    )
    assigned_personas: tuple[PersonaId, ...] = Field(
        default=(),
        description=(
            "PersonaIds of personas assigned to and completing this mission. "
            "Empty tuple = mission was declined or no personas participated — "
            "response tags_created will be empty."
        ),
    )


class PersonaLegacyTagWire(BaseModel):
    """Wire format for a single ``PersonaLegacyTag``.

    Mirrors the fields of the ``PersonaLegacyTag`` dataclass (world_tick_models)
    exactly so that the client can reconstruct the dataclass from the JSON.
    The C# client deserialises this as a ``PersonaLegacyTagDto`` record.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    persona_id: PersonaId = Field(..., description="Persona identifier.")
    mission_id: str = Field(..., description="Mission identifier (UUID-shaped stable id).")
    mission_type: EmergenceMissionType = Field(
        ..., description="Varn-locked closed lookup: scout_route | parley_local."
    )
    region: RegionId = Field(..., description="Region where the mission took place.")
    actor_target: str | None = Field(
        default=None,
        description="Named NPC target, or None for scout/anonymous-parley missions.",
    )
    outcome: ResolutionOutcomeType = Field(
        ..., description="Resolution outcome: success | partial | failure."
    )
    earned_at_tick: int = Field(..., ge=0, description="Tick at which the tag was earned.")

    @classmethod
    def from_tag(cls, tag: PersonaLegacyTag) -> PersonaLegacyTagWire:
        """Convert an in-process ``PersonaLegacyTag`` dataclass to the wire model."""
        return cls(
            persona_id=tag.persona_id,
            mission_id=tag.mission_id,
            mission_type=tag.mission_type,
            region=tag.region,
            actor_target=tag.actor_target,
            outcome=tag.outcome,
            earned_at_tick=tag.earned_at_tick,
        )


class MissionResolveResponse(BaseModel):
    """Output of ``POST /api/world/mission/resolve``.

    ``tags_created`` is the tuple of ``PersonaLegacyTag`` objects created
    by the resolution hook.  The client stores them in its ``GameState``
    (Godot-side ``GameState.PersonaLegacy``).

    Empty tuple when ``assigned_personas`` was empty (declined mission) or
    when no tags were produced (no personas participated).
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    tags_created: tuple[PersonaLegacyTagWire, ...] = Field(
        default=(),
        description=(
            "PersonaLegacyTag records created during this resolution. "
            "Store client-side in GameState.PersonaLegacy[personaId]. "
            "Empty when assigned_personas is empty (declined mission)."
        ),
    )


# ---------------------------------------------------------------------------
# Resolution logic
# ---------------------------------------------------------------------------


def _minimal_game_state() -> GameState:
    """Build a minimal *transient* GameState for the resolution hook.

    The server is stateless — this GameState lives for the duration of the
    request and is discarded.  Only ``persona_legacy`` is read by the hook;
    the rest of the fields are filled with structurally valid stubs.

    The action_templates dict requires at least one entry for GameState
    to be structurally valid (slot constraint from models.py).
    """
    stub_action = ActionCard(
        id="_stub",
        name="stub",
        description="",
        location_types=[],
        stat_tested=Stat.DEX,
        stat_minimum=None,
        equipment_required=[],
        stress_tier="any",
        tags=[],
        clock_segments=0,
        position="safe",
        effect="none",
        max_characters=0,
        rewards_on_success={},
        consequences_on_failure={},
        consequences_on_partial={},
        default_priority=0,
    )
    stub_char = Character(
        id="_stub",
        name="_stub",
        stats={Stat.STR: 10, Stat.DEX: 10, Stat.WIS: 10},
        max_hp=1,
        current_hp=1,
        background=Background(
            name="_stub",
            stat_adjustments={},
            starting_equipment=[],
            incapabilities=[],
        ),
        traits=[],
        inventory=[],
        stress=0,
        current_mission_id=None,
        conditions=[],
        recovery_clock_ids={},
        mission_history=[],
    )
    stub_party = Party(
        members=[stub_char],
        soul_level=0,
        cohesion_points=0,
        gold=0,
        supplies=0,
        stash=[],
        identity=None,
        mission_tag_history={},
    )
    return GameState(
        party=stub_party,
        map_nodes={
            "_stub": MapNode(
                id="_stub",
                name="_stub",
                node_type="settlement",
                discovery_state="discovered",
                connections=[],
                available_action_ids=[],
                event_pool=[],
            )
        },
        factions=[],
        active_missions={},
        active_threat_clocks={},
        active_recovery_clocks={},
        game_hour=0,
        action_templates={"_stub": stub_action},
        event_log=[],
    )


def resolve_mission(payload: MissionResolveRequest) -> MissionResolveResponse:
    """Execute mission resolution and return the created ``PersonaLegacyTag`` objects.

    1. Reconstruct an ``EmergentMission`` from the request fields.
    2. Create a transient ``GameState`` (discarded after the call).
    3. Call ``on_mission_resolved`` — idempotent by design.
    4. Collect the tags from ``game_state.persona_legacy``.
    5. Convert to ``PersonaLegacyTagWire`` and return.

    Idempotence: because ``on_mission_resolved`` is idempotent (same
    ``(persona_id, mission_id)`` pair produces at most one tag), calling
    this endpoint twice with the same payload produces the same tags.
    The transient GameState starts empty each call, so the idempotence
    guard inside ``on_mission_resolved`` fires on the *second* append
    within a single call only (not across calls).  Across calls, the
    client is responsible for deduplication using ``(persona_id, mission_id)``
    as the natural key when storing in its GameState.

    Args:
        payload: Validated ``MissionResolveRequest`` from the HTTP layer.

    Returns:
        ``MissionResolveResponse`` with ``tags_created`` populated.
    """
    # Reconstruct the EmergentMission from the request fields.
    # narrative_hook is not needed for resolution logic (not stored in the tag);
    # we supply a minimal valid string.
    mission = EmergentMission(
        id=payload.mission_id,
        type=payload.mission_type,
        narrative_hook=f"Resolved: {payload.mission_type} in {payload.region}.",
        eligible_personas=(),
        difficulty="mid",  # M1: forced; not stored in the tag
        region=payload.region,
        deadline_ticks=None,
        outcome=payload.outcome,
        seed=0,  # seed is not used by on_mission_resolved; placeholder
    )

    # Transient GameState — discarded after this request.
    game_state = _minimal_game_state()

    on_mission_resolved(
        mission=mission,
        outcome=payload.outcome,
        tick=payload.tick,
        game_state=game_state,
        persona_ids=list(payload.assigned_personas),
    )

    # Collect all tags produced (one per unique persona_id in assigned_personas).
    wire_tags: list[PersonaLegacyTagWire] = []
    for persona_id in payload.assigned_personas:
        tags = game_state.persona_legacy.get(persona_id, [])
        for tag in tags:
            wire_tags.append(PersonaLegacyTagWire.from_tag(tag))

    logger.info(
        "[RESOLVE] mission=%s outcome=%s tick=%d personas=%s tags_created=%d",
        payload.mission_id,
        payload.outcome,
        payload.tick,
        list(payload.assigned_personas),
        len(wire_tags),
    )

    return MissionResolveResponse(tags_created=tuple(wire_tags))
