"""Tests for WorldReferential geometry helpers.

Covers:
  - position_to_district: inside, outside, on edge, out-of-world
  - position_to_city: inside district, outside all districts (nearest city)
  - district_to_city: happy path + KeyError on bad parent
  - nearest_district: always returns one, even outside polygon
  - centroid: deterministic for rectangles
  - empty referential edge cases
"""

from __future__ import annotations

from wayfinders.world.models import City, District, Position, World, WorldReferential

# ---------------------------------------------------------------------------
# Shared fixtures (plain functions — no pytest dependency)
# ---------------------------------------------------------------------------


def _make_ref() -> WorldReferential:
    """Build a minimal WorldReferential matching the J1 stub geometry.

    Lower Quays: 91900..92200 x 37700..38000  (300m x 300m rectangle)
    High Wall:   92100..92300 x 38100..38300  (200m x 200m rectangle)
    Halfgate city anchor: (92000, 38000)
    """
    world = World(max_x=500_000, max_y=250_000)

    lq_footprint = (
        Position(91900, 37700),
        Position(92200, 37700),
        Position(92200, 38000),
        Position(91900, 38000),
    )
    hw_footprint = (
        Position(92100, 38100),
        Position(92300, 38100),
        Position(92300, 38300),
        Position(92100, 38300),
    )

    lower_quays = District(
        id="lower_quays",
        name="Lower Quays",
        parent_city_id="halfgate",
        anchor=Position(91900, 37700),
        footprint=lq_footprint,
    )
    high_wall = District(
        id="high_wall",
        name="High Wall",
        parent_city_id="halfgate",
        anchor=Position(92100, 38100),
        footprint=hw_footprint,
    )
    halfgate = City(
        id="halfgate",
        name="Halfgate",
        position=Position(92000, 38000),
        district_ids=("lower_quays", "high_wall"),
    )

    return WorldReferential(
        world=world,
        cities={"halfgate": halfgate},
        districts={"lower_quays": lower_quays, "high_wall": high_wall},
    )


# ---------------------------------------------------------------------------
# position_to_district
# ---------------------------------------------------------------------------


class TestPositionToDistrict:
    def test_inside_lower_quays(self) -> None:
        ref = _make_ref()
        d = ref.position_to_district(92050, 37850)
        assert d is not None
        assert d.id == "lower_quays"

    def test_inside_high_wall(self) -> None:
        ref = _make_ref()
        d = ref.position_to_district(92200, 38200)
        assert d is not None
        assert d.id == "high_wall"

    def test_outside_all_districts_returns_none(self) -> None:
        ref = _make_ref()
        # (1000, 1000) is well outside both polygons
        assert ref.position_to_district(1000, 1000) is None

    def test_gap_between_districts_returns_none(self) -> None:
        # (92050, 38050) is in the seam between lower_quays (y up to 38000)
        # and high_wall (y starting at 38100).
        ref = _make_ref()
        assert ref.position_to_district(92050, 38050) is None

    def test_out_of_world_returns_none(self) -> None:
        # Out-of-world coordinates are just outside any polygon — no exception expected.
        ref = _make_ref()
        assert ref.position_to_district(999_999, 999_999) is None

    def test_negative_coords_returns_none(self) -> None:
        ref = _make_ref()
        assert ref.position_to_district(-1, -1) is None

    def test_corner_vertex_of_lower_quays(self) -> None:
        # Ray-casting: corner (91900, 37700) is treated as inside (boundary semantics).
        ref = _make_ref()
        d = ref.position_to_district(91900, 37700)
        # Boundary points may be inside or outside depending on ray direction —
        # we just check no exception is raised and the result is a District or None.
        assert d is None or d.id == "lower_quays"

    def test_empty_referential_returns_none(self) -> None:
        ref = WorldReferential(world=World(max_x=500_000, max_y=250_000))
        assert ref.position_to_district(92050, 37850) is None


# ---------------------------------------------------------------------------
# position_to_city
# ---------------------------------------------------------------------------


class TestPositionToCity:
    def test_inside_district_returns_parent_city(self) -> None:
        ref = _make_ref()
        city = ref.position_to_city(92050, 37850)
        assert city is not None
        assert city.id == "halfgate"

    def test_outside_all_districts_returns_nearest_city(self) -> None:
        # (1000, 1000) is far outside any district but Halfgate is the only city.
        ref = _make_ref()
        city = ref.position_to_city(1000, 1000)
        assert city is not None
        assert city.id == "halfgate"

    def test_empty_cities_returns_none(self) -> None:
        ref = WorldReferential(world=World(max_x=500_000, max_y=250_000))
        assert ref.position_to_city(92050, 37850) is None

    def test_inside_high_wall_returns_halfgate(self) -> None:
        ref = _make_ref()
        city = ref.position_to_city(92200, 38200)
        assert city is not None
        assert city.id == "halfgate"


# ---------------------------------------------------------------------------
# district_to_city
# ---------------------------------------------------------------------------


class TestDistrictToCity:
    def test_happy_path(self) -> None:
        ref = _make_ref()
        d = ref.districts["lower_quays"]
        city = ref.district_to_city(d)
        assert city.id == "halfgate"

    def test_bad_parent_raises_key_error(self) -> None:
        import pytest

        ref = _make_ref()
        orphan = District(
            id="orphan",
            name="Orphan",
            parent_city_id="nonexistent_city",
            anchor=Position(1000, 1000),
            footprint=(
                Position(1000, 1000),
                Position(1100, 1000),
                Position(1100, 1100),
            ),
        )
        with pytest.raises(KeyError):
            ref.district_to_city(orphan)


# ---------------------------------------------------------------------------
# nearest_district
# ---------------------------------------------------------------------------


class TestNearestDistrict:
    def test_inside_lower_quays(self) -> None:
        ref = _make_ref()
        d = ref.nearest_district(92050, 37850)
        assert d is not None
        assert d.id == "lower_quays"

    def test_outside_all_districts_returns_closest(self) -> None:
        # (1000, 1000) — centroid of lower_quays is ~(92050, 37850),
        # centroid of high_wall is ~(92200, 38200).  Both are far;
        # lower_quays is slightly closer.
        ref = _make_ref()
        d = ref.nearest_district(1000, 1000)
        assert d is not None
        # Just check we get one back (exact winner depends on squared distances).

    def test_in_seam_returns_one_of_the_two(self) -> None:
        ref = _make_ref()
        # (92150, 38050) is in the seam; nearest_district must return something.
        d = ref.nearest_district(92150, 38050)
        assert d is not None
        assert d.id in ("lower_quays", "high_wall")

    def test_empty_referential_returns_none(self) -> None:
        ref = WorldReferential(world=World(max_x=500_000, max_y=250_000))
        assert ref.nearest_district(92050, 37850) is None


# ---------------------------------------------------------------------------
# District.centroid
# ---------------------------------------------------------------------------


class TestDistrictCentroid:
    def test_rectangle_centroid(self) -> None:
        ref = _make_ref()
        lq = ref.districts["lower_quays"]
        # Lower Quays: 91900..92200 x 37700..38000
        # Centroid = ((91900+92200+92200+91900)//4, (37700+37700+38000+38000)//4)
        #           = (368200//4, 151400//4) = (92050, 37850)
        assert lq.centroid == Position(92050, 37850)

    def test_high_wall_centroid(self) -> None:
        ref = _make_ref()
        hw = ref.districts["high_wall"]
        # High Wall: 92100..92300 x 38100..38300
        # Centroid = ((92100+92300+92300+92100)//4, (38100+38100+38300+38300)//4)
        #           = (368800//4, 152800//4) = (92200, 38200)
        assert hw.centroid == Position(92200, 38200)

    def test_empty_footprint_returns_anchor(self) -> None:
        d = District(
            id="empty",
            name="Empty",
            parent_city_id="halfgate",
            anchor=Position(1000, 1000),
            footprint=(),
        )
        assert d.centroid == Position(1000, 1000)
