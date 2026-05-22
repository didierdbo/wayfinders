"""World referential data model.

Locked by Varn spec 2026-05-20.  Do not add fields or change semantics
without a Varn ratification step.

Coordinate system:
  - Unit: meters (integer).
  - Origin (0, 0): south-west corner, X east, Y north.
  - Valid range: 0 <= x < world.max_x, 0 <= y < world.max_y.

Polygon contract:
  - District.footprint is a list of (x, y) vertices in world meters.
  - Vertices are given in order (CW or CCW — helpers are orientation-agnostic).
  - Loaded from the committed *.polygon.json sidecar next to the bitmap mask.
  - Stubs (J1): hand-written rectangles.  Replace at J2 with Mira's bitmaps
    processed through tools/extract_district_polygon.py.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import NamedTuple


class Position(NamedTuple):
    """A 2D world position in meters (integer).

    Invariant: 0 <= x < world.max_x, 0 <= y < world.max_y.
    Validation is done at write time (loader + mission helpers).
    """

    x: int
    y: int


@dataclass(frozen=True)
class World:
    """World bounding box.  Locked: max_x=500_000, max_y=250_000."""

    max_x: int  # 500_000 m east-west
    max_y: int  # 250_000 m north-south

    def contains(self, x: int, y: int) -> bool:
        """Return True if (x, y) is inside the world bounds (exclusive upper)."""
        return 0 <= x < self.max_x and 0 <= y < self.max_y


@dataclass(frozen=True)
class City:
    """A city in the world referential.

    position: centroid anchor in world meters.  The Halfgate value
    (92000, 38000) is a placeholder until Mira signs off the eM mesh
    coastline render (Varn spec §7).

    district_ids: ordered list of district ids that belong to this city.
    """

    id: str
    name: str
    position: Position
    district_ids: tuple[str, ...]
    bitmap: str | None = None  # optional eM silhouette asset path


@dataclass(frozen=True)
class District:
    """A district within a city.

    anchor: absolute world meters — SW corner of the district bitmap.
    footprint: polygon vertices in world meters (derived from bitmap sidecar).
    cell_size_m: locked at 2 m (Varn spec §1.3).

    The centroid is computed lazily from the footprint polygon (used for
    mission position derivation when target_poi resolves to this district).
    """

    id: str
    name: str
    parent_city_id: str
    anchor: Position
    footprint: tuple[Position, ...]  # ordered polygon vertices in world meters
    cell_size_m: int = 2  # locked: always 2 m

    @property
    def centroid(self) -> Position:
        """Arithmetic centroid of the footprint polygon vertices.

        For convex polygons this is a good interior point.  For non-convex
        polygons it may fall outside — Mira's bitmap-derived polygons are
        expected to be close to convex for typical district shapes.
        """
        if not self.footprint:
            return self.anchor
        cx = sum(v.x for v in self.footprint) // len(self.footprint)
        cy = sum(v.y for v in self.footprint) // len(self.footprint)
        return Position(cx, cy)


@dataclass
class WorldReferential:
    """Loaded world model with geometry helpers.

    Holds all cities and districts indexed for fast lookup.
    Produced by ``load_world_referential()`` in :mod:`wayfinders.world.loader`.

    The four helpers implement the contracts locked by Varn spec §2.3:
      - position_to_district: strict (None if outside any polygon)
      - position_to_city: lenient (nearest city if outside all polygons)
      - district_to_city: trivial parent lookup
      - nearest_district: always returns one (closest centroid)
    """

    world: World
    cities: dict[str, City] = field(default_factory=dict)
    districts: dict[str, District] = field(default_factory=dict)

    # ------------------------------------------------------------------
    # Geometry helpers
    # ------------------------------------------------------------------

    def position_to_district(self, x: int, y: int) -> District | None:
        """Strict point-in-polygon lookup.

        Returns the District whose footprint polygon contains (x, y), or
        None if (x, y) is outside every district polygon (wilderness).

        Uses the ray-casting algorithm (Jordan curve theorem).  Works for
        both convex and non-convex simple polygons.  Orientation-agnostic.

        The spec deliberately returns None for out-of-bounds coordinates —
        callers must validate bounds separately if needed.
        """
        for district in self.districts.values():
            if _point_in_polygon(x, y, district.footprint):
                return district
        return None

    def position_to_city(self, x: int, y: int) -> City | None:
        """Lenient city lookup.

        If (x, y) is inside a district polygon: returns that district's
        parent city.

        If (x, y) is outside every district polygon: returns the nearest
        city by Euclidean distance to city.position.  This is the
        "perso bouge de 2m hors-quartier → rattaché à Halfgate" semantic
        locked by Varn spec §2.3.

        Returns None only if the cities dictionary is empty.
        """
        district = self.position_to_district(x, y)
        if district is not None:
            return self.cities.get(district.parent_city_id)

        # Outside all districts — return nearest city by centroid distance.
        return _nearest_city(x, y, self.cities)

    def district_to_city(self, district: District) -> City:
        """Trivial parent lookup.

        Raises KeyError if the district references a city not in this referential
        (data integrity error — should not happen with a well-formed world.yaml).
        """
        return self.cities[district.parent_city_id]

    def nearest_district(self, x: int, y: int) -> District | None:
        """Lenient district lookup — always returns one if any districts exist.

        Even if (x, y) is outside every polygon, returns the district whose
        centroid is closest (Euclidean distance).

        Used by the eT layer when the player walks off a polygon edge
        ("attach to nearest district" semantic — Varn spec §2.3).

        Returns None only if the districts dictionary is empty.
        """
        if not self.districts:
            return None
        return min(
            self.districts.values(),
            key=lambda d: _dist2(x, y, d.centroid.x, d.centroid.y),
        )


# ---------------------------------------------------------------------------
# Internal geometry utilities
# ---------------------------------------------------------------------------


def _point_in_polygon(x: int, y: int, polygon: tuple[Position, ...]) -> bool:
    """Ray-casting point-in-polygon test.

    Returns True if the point (x, y) is strictly inside the polygon defined
    by the ordered vertex list.  Points exactly on an edge are treated as
    inside (consistent with grid-edge footprints where the boundary is shared).

    Empty or degenerate polygons (< 3 vertices) always return False.
    """
    n = len(polygon)
    if n < 3:
        return False

    inside = False
    j = n - 1
    for i in range(n):
        xi, yi = polygon[i].x, polygon[i].y
        xj, yj = polygon[j].x, polygon[j].y

        # Check if the ray from (x, y) going right crosses edge (j -> i).
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i

    return inside


def _dist2(x1: int, y1: int, x2: int, y2: int) -> int:
    """Squared Euclidean distance (avoids sqrt for comparison only)."""
    return (x1 - x2) ** 2 + (y1 - y2) ** 2


def _nearest_city(x: int, y: int, cities: dict[str, City]) -> City | None:
    """Return the city with the smallest Euclidean distance to (x, y)."""
    if not cities:
        return None
    return min(
        cities.values(),
        key=lambda c: _dist2(x, y, c.position.x, c.position.y),
    )
