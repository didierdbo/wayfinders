"""Mission position helpers — world referential and mission struct.

Implements the M1 contracts from Varn spec 2026-05-20 §6:

1. ``derive_mission_position(target_poi, ref)``
   Derives the ``position`` field from the centroid of the district that
   ``target_poi`` resolves to.  M1 rule: the target_poi is a PoiId such as
   ``e1.halfgate`` or ``e2.halfgate.lower_quays``.  The district slug is the
   last segment when the PoiId has >= 3 segments; for e1-layer PoiIds the
   position falls back to the city anchor.

2. ``validate_mission_position_invariant(position, target_poi, ref)``
   Varn spec §6.3 invariant: if both ``position`` and ``target_poi`` are set,
   then ``position_to_city(position) == target_poi.city_slug``.
   Raises ``MissionPositionInvariantError`` on mismatch.

3. ``fill_missing_position(mission, ref)``
   One-way migration fill (spec §6.3): if ``position`` is None and
   ``target_poi`` resolves to a district, fill ``position`` from the
   district centroid.  Returns the filled Position or None.

These helpers are called by the emergence engine (world_tick.py) and by
the loader at deserialization time.  They do not live inside the Pydantic
model to keep the wire-format types dependency-free from the world module.
"""

from __future__ import annotations

import logging

from wayfinders.world.models import Position, WorldReferential

logger = logging.getLogger(__name__)


class MissionPositionInvariantError(ValueError):
    """Raised when position and target_poi point to different cities.

    Varn spec §6.3: if both fields are set,
    ``position_to_city(position) == target_poi.city_slug`` must hold.
    """


def derive_mission_position(target_poi: str, ref: WorldReferential) -> Position | None:
    """Derive mission position from target_poi and world referential.

    Resolution logic (M1):
      - target_poi format: ``e{layer}.{city_slug}[.{district_slug}]``
      - If the poi has a district segment (>= 3 parts after splitting on '.',
        with the layer prefix counted), look up the district by district_slug
        and return its centroid.
      - If the poi is city-level only (2 parts), return the city anchor position.
      - If neither resolves, return None (wilderness or unknown poi).

    Args:
        target_poi: PoiId string (e.g. ``e1.halfgate``,
            ``e2.halfgate.lower_quays``).
        ref: Loaded WorldReferential.

    Returns:
        A Position in world meters, or None if the poi does not resolve.
    """
    parts = target_poi.split(".")
    # parts[0] = "e1"|"e2"|"e3", parts[1] = city_slug, parts[2] = district_slug (optional)
    if len(parts) < 2:
        logger.warning("derive_mission_position: malformed target_poi %r", target_poi)
        return None

    city_slug = parts[1]

    if len(parts) >= 3:
        district_slug = parts[2]
        district = ref.districts.get(district_slug)
        if district is not None:
            return district.centroid
        # District slug not in referential — fall through to city.
        logger.warning(
            "derive_mission_position: district %r not found in referential, "
            "falling back to city %r",
            district_slug,
            city_slug,
        )

    city = ref.cities.get(city_slug)
    if city is not None:
        return city.position

    logger.warning(
        "derive_mission_position: city %r not found in referential for poi %r",
        city_slug,
        target_poi,
    )
    return None


def validate_mission_position_invariant(
    position: Position,
    target_poi: str,
    ref: WorldReferential,
) -> None:
    """Assert the §6.3 invariant: position_to_city(position) == target_poi city slug.

    Called at emergence and at deserialization when both fields are set.

    Args:
        position: The mission position in world meters.
        target_poi: The PoiId string.
        ref: Loaded WorldReferential.

    Raises:
        MissionPositionInvariantError: If the invariant is violated.
    """
    parts = target_poi.split(".")
    if len(parts) < 2:
        raise MissionPositionInvariantError(
            f"Cannot validate invariant: malformed target_poi {target_poi!r}."
        )

    expected_city_slug = parts[1]
    resolved_city = ref.position_to_city(position.x, position.y)
    resolved_city_id = resolved_city.id if resolved_city is not None else None

    if resolved_city_id != expected_city_slug:
        raise MissionPositionInvariantError(
            f"Mission position invariant violated: "
            f"position_to_city({position.x}, {position.y}) = {resolved_city_id!r} "
            f"but target_poi city slug = {expected_city_slug!r}. "
            f"target_poi={target_poi!r}. "
            "Check that position was derived from the correct district centroid."
        )


def fill_missing_position(
    position: Position | None,
    target_poi: str,
    ref: WorldReferential,
) -> Position | None:
    """One-way migration fill: derive position from target_poi if position is None.

    Varn spec §6.3 migration contract: if an existing mission has no position
    but has a target_poi that resolves, fill position from the district centroid
    and persist.  This is a one-way fill — never overwrites an existing position.

    Args:
        position: Current position field (None to fill, non-None to keep as-is).
        target_poi: PoiId string.
        ref: Loaded WorldReferential.

    Returns:
        The existing position (if non-None), the derived position (if resolution
        succeeded), or None (if neither target_poi nor city resolves).
    """
    if position is not None:
        return position
    return derive_mission_position(target_poi, ref)
