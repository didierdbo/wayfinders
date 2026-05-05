"""Rollout generator scaffolding.

A `Rollout` is one structured tuple `(character_state, action_card,
opposition, scene_state, party_state, world_state, campaign_state, label_delta)`.

M1 substep 6 fills in the actual policies and rollout loop. This file
locks the shape and the determinism contract -- same seed -> same rollouts.
"""

from __future__ import annotations

from collections.abc import Iterator

from pydantic import BaseModel, ConfigDict

from wayfinders.ml.schemas import (
    ActionCard,
    CampaignState,
    CharacterState,
    OppositionState,
    PartyState,
    SceneState,
    WorldState,
)


class Rollout(BaseModel):
    """Structured tuple consumed by labeling + the renderers.

    All sub-states are frozen Pydantic models -- a Rollout is a snapshot, not
    a live object.
    """

    model_config = ConfigDict(frozen=True)

    rollout_id: int
    character: CharacterState
    action: ActionCard
    opposition: OppositionState
    scene: SceneState
    party: PartyState
    world: WorldState
    campaign: CampaignState
    # `label_delta` is filled in by data_gen.designer_modifiers.compute_label.
    # Float in [-5, +5]. Optional at construction time so we can build the
    # rollout structurally then label it in a separate pass.
    label_delta: float | None = None


def generate_rollouts(n: int, seed: int) -> Iterator[Rollout]:
    """Lazily yield ``n`` Rollouts deterministically from ``seed``.

    M1 substep 6 implements this. For now, this scaffold raises so callers
    fail loudly until the implementation lands.
    """
    raise NotImplementedError(
        "M1 substep 6 -- implement generate_rollouts(). "
        "Must be deterministic: same (n, seed) -> same iterator output."
    )
