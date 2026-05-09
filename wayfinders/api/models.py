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
    model_ready: bool = Field(
        ...,
        description=(
            "True when both encoder and head ONNX sessions are loaded and "
            "``/api/uc1/predict`` is operational. "
            "False if either artifact is missing (predict returns 503)."
        ),
    )


class UC1InfoResponse(BaseModel):
    """Debug info for the UC1 inference backend.

    Exposes the ONNX artifact paths and readiness flag so the Godot client
    (and ops) can verify which checkpoint is loaded without calling predict.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    model_ready: bool = Field(
        ..., description="True when both encoder and head ONNX sessions are loaded."
    )
    encoder_onnx_path: str = Field(..., description="Resolved path to the encoder ONNX artifact.")
    head_onnx_path: str = Field(..., description="Resolved path to the head ONNX artifact.")
    device: str = Field(
        ...,
        description=(
            "Runtime descriptor. ``ort-cpu`` for CPUExecutionProvider (current default). "
            "Extended in M2 for GPU providers."
        ),
    )


class UC1PredictRequest(BaseModel):
    """Input for the UC1 resolution prediction endpoint.

    All three prose strings must be EN-rendered (renderer English-pinning
    contract). They are fed directly to the MiniLM encoder; no further
    transformation is performed server-side.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    char_prose: str = Field(
        ...,
        min_length=1,
        description=(
            "EN-rendered character prose (render_character() output). "
            "Must be non-empty. EN-pinned regardless of UI locale."
        ),
    )
    action_prose: str = Field(
        ...,
        min_length=1,
        description=(
            "EN-rendered action prose (render_action() output). Must be non-empty. EN-pinned."
        ),
    )
    context_prose: str = Field(
        ...,
        min_length=1,
        description=(
            "EN-rendered context prose (render_context() output). Must be non-empty. EN-pinned."
        ),
    )


class UC1PredictResponse(BaseModel):
    """Output of the UC1 resolution prediction endpoint.

    ``delta`` is the raw model output in [-5, +5] (tanh x 5, computed
    inside the head graph). The game engine adds this to the base modifier
    to produce the final resolution delta.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    delta: float = Field(
        ...,
        ge=-5.0,
        le=5.0,
        description="Predicted resolution delta in [-5, +5]. tanh-bounded by the head.",
    )
