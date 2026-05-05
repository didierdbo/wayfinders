"""Prose renderers -- the only place engine state becomes encoder input.

All three renderers (character, action, context) are pure functions that emit
English text from structured Pydantic models. See ../README.md for the
EN-pinned contract.
"""

from wayfinders.ml.schemas.action import (
    ActionCard,
    OppositionState,
    render_action,
)
from wayfinders.ml.schemas.character import (
    Bond,
    CharacterState,
    EpisodicEvent,
    render_character,
)
from wayfinders.ml.schemas.context import (
    CampaignState,
    PartyState,
    SceneState,
    WorldState,
    render_context,
)

__all__ = [
    "ActionCard",
    "Bond",
    "CampaignState",
    "CharacterState",
    "EpisodicEvent",
    "OppositionState",
    "PartyState",
    "SceneState",
    "WorldState",
    "render_action",
    "render_character",
    "render_context",
]
