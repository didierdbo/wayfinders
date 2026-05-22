"""World referential module.

Provides the data model (World, City, District, Position) and the four
geometry helpers (position_to_district, position_to_city, district_to_city,
nearest_district) locked by Varn spec 2026-05-20.

Typical usage:
    from wayfinders.world import load_world_referential, WorldReferential
    ref = load_world_referential()
    district = ref.position_to_district(92050, 37850)
"""

from wayfinders.world.loader import WorldSchemaError, load_world_referential
from wayfinders.world.mission_helpers import (
    MissionPositionInvariantError,
    derive_mission_position,
    fill_missing_position,
    validate_mission_position_invariant,
)
from wayfinders.world.models import City, District, Position, World, WorldReferential

__all__ = [
    "City",
    "District",
    "MissionPositionInvariantError",
    "Position",
    "World",
    "WorldReferential",
    "WorldSchemaError",
    "derive_mission_position",
    "fill_missing_position",
    "load_world_referential",
    "validate_mission_position_invariant",
]
