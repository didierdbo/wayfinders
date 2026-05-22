"""Pydantic v2 response DTOs for ``GET /api/world``.

Wire-format types for the world referential endpoint.  The C# client maps
snake_case fields to PascalCase records via ``System.Text.Json`` naming policy.

Schema is Varn-locked (spec 2026-05-20).  Adding fields is additive and
safe; removing or renaming requires Varn ratification.

``schema_version`` mirrors ``world.yaml``'s schema_version so the Godot
client can detect breaking changes without parsing the full payload.
"""

from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field


class PositionDto(BaseModel):
    """A 2D world position in integer meters.

    Invariant: 0 <= x < world.max_x, 0 <= y < world.max_y.
    Validated at load time by the world loader; the API trusts the loaded data.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    x: int = Field(..., description="East-west coordinate in world meters.")
    y: int = Field(..., description="North-south coordinate in world meters.")


class WorldDto(BaseModel):
    """World bounding box dimensions.

    Locked: max_x=500_000 m (500 km), max_y=250_000 m (250 km).
    Valid coordinate range: [0, max_x) x [0, max_y).
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    max_x: int = Field(
        ...,
        gt=0,
        description="World width in meters (east-west). Locked: 500_000.",
    )
    max_y: int = Field(
        ...,
        gt=0,
        description="World height in meters (north-south). Locked: 250_000.",
    )


class CityDto(BaseModel):
    """A city in the world referential.

    ``position`` is the centroid anchor in world meters (used as the city
    marker on layer eM).  The value for Halfgate is a placeholder until Mira
    signs off the coastline render (Varn spec §7).

    ``district_ids``: ordered list of district ids belonging to this city.
    ``bitmap``: optional path to the eM silhouette asset (may be null).
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    id: str = Field(..., min_length=1, description="Stable slug identifier (e.g. 'halfgate').")
    name: str = Field(..., min_length=1, description="Display name (e.g. 'Halfgate').")
    position: PositionDto = Field(..., description="City centroid in world meters.")
    district_ids: list[str] = Field(
        ...,
        description="Ordered list of district ids belonging to this city.",
    )
    bitmap: str | None = Field(
        default=None,
        description=(
            "Optional path to the eM silhouette asset relative to assets/world/. "
            "Null when the asset has not yet been produced."
        ),
    )


class DistrictDto(BaseModel):
    """A district within a city.

    ``anchor``: absolute world meters — SW corner of the district bitmap.
    Used by layer eT to pose the district mesh.

    ``footprint``: ordered polygon vertices in world meters, derived from the
    committed ``*.polygon.json`` sidecar.  At least 3 vertices.  The client
    (Rune) uses this to build the district collision shape and the
    point-in-polygon check for player position updates on layer eT.

    ``cell_size_m``: always 2 m (Varn spec §1.3).  Included so the client
    can assert the invariant rather than hard-coding it.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    id: str = Field(..., min_length=1, description="Stable slug identifier (e.g. 'lower_quays').")
    name: str = Field(..., min_length=1, description="Display name (e.g. 'Lower Quays').")
    parent_city_id: str = Field(
        ...,
        min_length=1,
        description="Id of the city this district belongs to.",
    )
    anchor: PositionDto = Field(
        ...,
        description="SW corner of the district bitmap in absolute world meters.",
    )
    footprint: list[PositionDto] = Field(
        ...,
        min_length=3,
        description=(
            "Ordered polygon vertices in world meters (CW or CCW — client is orientation-agnostic). "
            "Derived from the committed *.polygon.json sidecar. "
            "Used by the eT layer for mesh poser and point-in-polygon checks."
        ),
    )
    cell_size_m: int = Field(
        default=2,
        gt=0,
        description="Grid cell size in meters. Locked at 2 m (Varn spec §1.3).",
    )


class WorldReferentialResponse(BaseModel):
    """Response body for ``GET /api/world``.

    Top-level envelope for the world referential, versioned via
    ``schema_version`` (mirrors world.yaml).  The Godot client should cache
    this response at startup — it is static per server version.

    ``cities``: all cities defined in world.yaml, ordered by id ascending.
    ``districts``: all districts, ordered by id ascending.
    """

    model_config = ConfigDict(extra="forbid", frozen=True)

    schema_version: int = Field(
        ...,
        gt=0,
        description=(
            "Schema version of the underlying world.yaml (currently 1). "
            "Bump on breaking changes. Client should reject unknown versions."
        ),
    )
    world: WorldDto = Field(..., description="World bounding box.")
    cities: list[CityDto] = Field(
        ...,
        description="All cities, ordered by id ascending.",
    )
    districts: list[DistrictDto] = Field(
        ...,
        description="All districts across all cities, ordered by id ascending.",
    )
