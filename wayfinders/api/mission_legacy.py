"""PersonaLegacyTag hook and char_prose enrichment — M1 scaffolding.

Two public functions:

``on_mission_resolved(mission, outcome, tick, [persona_ids], game_state)``
    Creates a ``PersonaLegacyTag`` for each participating persona and appends
    it to ``game_state.persona_legacy``.  Idempotent: a second call with the
    same ``(persona_id, mission.id)`` pair is a no-op (duplicate guard).

    In M1 the caller passes ``persona_ids`` explicitly because
    ``EmergentMission`` carries no ``assigned_personas`` field yet — that
    field arrives in Etape 4 (Rune's Godot client side).  The parameter is
    optional (defaults to an empty sequence) to make the stub callable without
    Rune's assignment data.

``enrich_char_prose(char_state, legacy_tags) -> str``
    Produces an enriched ``char_prose`` string by appending canonical EN
    sentences derived from ``legacy_tags`` to the base ``render_character()``
    output.  Pure function — does NOT mutate ``char_state`` (which is a
    frozen Pydantic model) or any ``PersonaLegacyTag``.

    NOT wired to ``/api/world/tick`` in M1.  Scaffolded here so M2 can inject
    the result into ``context_prose`` after a Varn-ratified schema change.

Varn locks respected:
  - ``char_prose`` (``CharacterState``) is never mutated — ``enrich_char_prose``
    returns a new string.  (Varn lock 2026-05-01 via feedback_varn_locked_specs.)
  - ``EmergenceMissionType``, ``ResolutionOutcomeType`` are not extended here.
  - EN-pinning: all generated sentences are English, no FR strings.
"""

from __future__ import annotations

import logging
from collections.abc import Sequence

from wayfinders.api.world_tick_models import (
    EmergentMission,
    PersonaId,
    PersonaLegacyTag,
    ResolutionOutcomeType,
)
from wayfinders.ml.schemas.character import CharacterState, render_character
from wayfinders.models import GameState

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Hook: on_mission_resolved
# ---------------------------------------------------------------------------


def on_mission_resolved(
    mission: EmergentMission,
    outcome: ResolutionOutcomeType,
    tick: int,
    game_state: GameState,
    persona_ids: Sequence[PersonaId] = (),
) -> None:
    """Append a ``PersonaLegacyTag`` to ``game_state.persona_legacy`` for each persona.

    Idempotent: if a tag with the same ``(persona_id, mission.id)`` pair
    already exists in ``game_state.persona_legacy[persona_id]``, the tag is
    NOT appended again.  This ensures calling the hook twice with identical
    inputs leaves exactly one tag per persona.

    Args:
        mission:     The resolved ``EmergentMission`` (from world_tick endpoint).
        outcome:     Varn-locked resolution outcome (success | partial | failure).
        tick:        Game tick at which resolution occurred.
        game_state:  Mutable ``GameState`` instance.  Only ``persona_legacy``
                     is written; all other fields are untouched.
        persona_ids: Sequence of persona IDs who participated.  Defaults to
                     empty — M1 stub until Etape 4 wires in assigned personas.
    """
    for persona_id in persona_ids:
        existing = game_state.persona_legacy.get(persona_id, [])

        # Idempotence guard: skip if this mission is already recorded.
        already_recorded = any(tag.mission_id == mission.id for tag in existing)
        if already_recorded:
            logger.debug(
                "[LEGACY] persona=%s mission=%s already recorded — skipping duplicate",
                persona_id,
                mission.id,
            )
            continue

        tag = PersonaLegacyTag(
            persona_id=persona_id,
            mission_id=mission.id,
            mission_type=mission.type,
            region=mission.region,
            actor_target=_extract_actor_target(mission),
            outcome=outcome,
            earned_at_tick=tick,
        )

        if persona_id not in game_state.persona_legacy:
            game_state.persona_legacy[persona_id] = []
        game_state.persona_legacy[persona_id].append(tag)

        logger.info(
            "[LEGACY] persona=%s earned tag mission=%s outcome=%s",
            persona_id,
            mission.id,
            outcome,
        )


def _extract_actor_target(mission: EmergentMission) -> str | None:
    """Derive actor_target from mission narrative_hook for M1.

    For ``parley_local`` missions, extract the target name from
    ``narrative_hook`` when present (heuristic for M1; M2 will pass it
    explicitly from WorldState).  Returns ``None`` for ``scout_route``
    missions and as fallback when no target is identifiable.
    """
    if mission.type == "scout_route":
        return None
    # Heuristic: the narrative hook for parley_local is
    # "A matter of local parley in {region} — a local contact seeks audience."
    # There is no named NPC in M1 templates.  Return None until M2 supplies
    # a target field in the mission struct.
    return None


# ---------------------------------------------------------------------------
# Enrichment: enrich_char_prose
# ---------------------------------------------------------------------------

# Canonical EN sentence templates per (mission_type, outcome).
# Keys are (mission_type, outcome) pairs.  Varn prose style: declarative,
# past tense, no first-person, no pronouns, name not repeated here (it
# appears in the base char_prose already).
#
# Template variables:
#   {region}       — RegionId (free-form string)
#   {actor_target} — NPC name for parley missions; absent for scout missions
#
# Outcome modifiers:
#   success  → affirmative ("Has scouted …")
#   partial  → hedged ("Has scouted … though the picture remains incomplete.")
#               or "Has parleyed … (uncertain outcome)."
#   failure  → negative ("Attempted to scout … but failed.")

_ENRICH_TEMPLATES: dict[tuple[str, str], str] = {
    # scout_route x outcome
    ("scout_route", "success"): "Has scouted the route to {region}.",
    ("scout_route", "partial"): (
        "Has scouted the route to {region}, though the picture remains incomplete."
    ),
    ("scout_route", "failure"): "Attempted to scout the route to {region} but failed.",
    # parley_local x outcome -- with named target
    ("parley_local", "success"): "Has parleyed with the {actor_target} of {region}.",
    ("parley_local", "partial"): (
        "Has parleyed with the {actor_target} of {region} (uncertain outcome)."
    ),
    ("parley_local", "failure"): (
        "Attempted to parley with the {actor_target} of {region} but the contact refused."
    ),
    # parley_local x outcome -- without named target (actor_target is None)
    ("parley_local_anon", "success"): "Has parleyed with a local contact in {region}.",
    ("parley_local_anon", "partial"): (
        "Has parleyed with a local contact in {region} (uncertain outcome)."
    ),
    ("parley_local_anon", "failure"): (
        "Attempted to parley with a local contact in {region} but the contact refused."
    ),
}


def _render_legacy_sentence(tag: PersonaLegacyTag) -> str:
    """Render one canonical EN sentence for a single ``PersonaLegacyTag``.

    Template selection:
      - scout_route        -> key ``("scout_route", outcome)``
      - parley_local with actor_target  -> key ``("parley_local", outcome)``
      - parley_local without actor_target -> key ``("parley_local_anon", outcome)``
    """
    outcome = tag.outcome

    if tag.mission_type == "parley_local" and tag.actor_target is not None:
        template_key: tuple[str, str] = ("parley_local", outcome)
        return _ENRICH_TEMPLATES[template_key].format(
            region=tag.region,
            actor_target=tag.actor_target,
        )
    elif tag.mission_type == "parley_local":
        template_key = ("parley_local_anon", outcome)
        return _ENRICH_TEMPLATES[template_key].format(region=tag.region)
    else:
        # scout_route (and any future types default to scout_route pattern
        # until M2 extends templates)
        template_key = (tag.mission_type, outcome)
        return _ENRICH_TEMPLATES[template_key].format(region=tag.region)


def enrich_char_prose(
    char_state: CharacterState,
    legacy_tags: list[PersonaLegacyTag],
) -> str:
    """Return an enriched char_prose string with mission-legacy sentences appended.

    Pure function — ``char_state`` is NOT mutated (it is a frozen Pydantic
    model and mutation is Varn-locked).  ``legacy_tags`` are read-only.

    The returned string is a new str: the base ``render_character()`` output
    followed by one canonical EN sentence per tag, each on its own line.

    Tags are rendered in ``earned_at_tick`` ascending order (chronological),
    with ties broken by ``mission_id`` lexicographic order for determinism.

    When ``legacy_tags`` is empty the function returns ``render_character(char_state)``
    unchanged — no trailing newline or empty paragraph is added.

    NOT called from ``/api/world/tick`` in M1.  Scaffolded for M2 integration
    where ``context_prose`` will include legacy sentences after Varn ratifies
    the schema extension.

    Args:
        char_state:   Frozen ``CharacterState`` snapshot.
        legacy_tags:  List of ``PersonaLegacyTag`` for this persona (may be empty).

    Returns:
        str: Enriched character prose, EN-pinned, deterministic.
    """
    base = render_character(char_state)
    if not legacy_tags:
        return base

    # Sort: chronological, then mission_id for determinism on same-tick tags.
    sorted_tags = sorted(legacy_tags, key=lambda t: (t.earned_at_tick, t.mission_id))
    sentences = [_render_legacy_sentence(tag) for tag in sorted_tags]
    return base + "\n" + "\n".join(sentences)
