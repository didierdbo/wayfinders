"""Pydantic v2 models for the HTTP boundary.

These are the wire-format types — they exist purely to define the JSON
shape the client (Godot+C#) deserializes into typed DTO records. They
are intentionally separate from the dataclass models in
:mod:`wayfinders.models`, which describe the in-process game state.

Field names use snake_case on the wire; the C# client maps them to
PascalCase records via ``System.Text.Json`` naming policy.
"""

from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field


class Unit(BaseModel):
    """A unit type in the player's roster.

    Schema is the contract with the C# ``UnitData`` record on the client.
    Any change here must be coordinated with the Godot client repo.
    """

    model_config = ConfigDict(
        # Forbid unknown fields on input — catches typos at the boundary.
        extra="forbid",
        # Frozen instances: seed data is immutable, treat it that way.
        frozen=True,
    )

    display_name: str = Field(
        ...,
        min_length=1,
        description="Human-readable unit name shown in UI.",
    )
    max_hp: int = Field(
        ...,
        gt=0,
        description="Maximum hit points. Must be positive.",
    )
    base_speed: int = Field(
        ...,
        gt=0,
        description="Base movement speed in tiles per turn. Must be positive.",
    )
    portrait: str = Field(
        ...,
        min_length=1,
        description="Path or filename of the unit's portrait asset.",
    )


class HealthResponse(BaseModel):
    """Liveness/readiness response."""

    model_config = ConfigDict(extra="forbid", frozen=True)

    status: str = Field(..., description="Service status. ``ok`` when healthy.")
