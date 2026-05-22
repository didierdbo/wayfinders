"""Tests for world.yaml loader (wayfinders.world.loader).

Covers:
  - Happy-path load from the real assets/world/world.yaml + sidecars.
  - Schema version rejection.
  - Missing required fields.
  - Out-of-bounds positions.
  - Missing sidecar.
  - Malformed sidecar.
"""

from __future__ import annotations

import json
import textwrap
from pathlib import Path

import pytest
import yaml

from wayfinders.world.loader import WorldSchemaError, load_world_referential
from wayfinders.world.models import Position

# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture()
def minimal_yaml_world(tmp_path: Path) -> tuple[Path, Path]:
    """Write a minimal valid world.yaml + two stub sidecars in tmp_path."""
    assets_dir = tmp_path / "assets" / "world"
    halfgate_dir = assets_dir / "cities" / "halfgate"
    halfgate_dir.mkdir(parents=True)

    # Sidecar stubs
    lq_sidecar = {
        "_stub": True,
        "vertices": [
            {"x": 91900, "y": 37700},
            {"x": 92200, "y": 37700},
            {"x": 92200, "y": 38000},
            {"x": 91900, "y": 38000},
        ],
    }
    hw_sidecar = {
        "_stub": True,
        "vertices": [
            {"x": 92100, "y": 38100},
            {"x": 92300, "y": 38100},
            {"x": 92300, "y": 38300},
            {"x": 92100, "y": 38300},
        ],
    }
    (halfgate_dir / "lower_quays_mask.polygon.json").write_text(
        json.dumps(lq_sidecar), encoding="utf-8"
    )
    (halfgate_dir / "high_wall_mask.polygon.json").write_text(
        json.dumps(hw_sidecar), encoding="utf-8"
    )

    world_yaml_content = textwrap.dedent(
        """\
        schema_version: 1
        world:
          max_x: 500000
          max_y: 250000
        cities:
          - id: halfgate
            name: Halfgate
            position: {x: 92000, y: 38000}
            districts:
              - lower_quays
              - high_wall
        districts:
          - id: lower_quays
            name: Lower Quays
            parent_city: halfgate
            anchor: {x: 91900, y: 37700}
            footprint_bitmap: cities/halfgate/lower_quays_mask.png
            cell_size_m: 2
            footprint_polygon: cities/halfgate/lower_quays_mask.polygon.json
          - id: high_wall
            name: High Wall
            parent_city: halfgate
            anchor: {x: 92100, y: 38100}
            footprint_bitmap: cities/halfgate/high_wall_mask.png
            cell_size_m: 2
            footprint_polygon: cities/halfgate/high_wall_mask.polygon.json
        """
    )
    world_yaml = assets_dir / "world.yaml"
    world_yaml.write_text(world_yaml_content, encoding="utf-8")

    return world_yaml, assets_dir


# ---------------------------------------------------------------------------
# Happy-path load
# ---------------------------------------------------------------------------


class TestLoaderHappyPath:
    def test_loads_from_real_assets(self) -> None:
        """Load from the committed assets/world/world.yaml + stub sidecars."""
        ref = load_world_referential()
        assert ref.world.max_x == 500_000
        assert ref.world.max_y == 250_000
        assert "halfgate" in ref.cities
        assert "lower_quays" in ref.districts
        assert "high_wall" in ref.districts

    def test_halfgate_position(self) -> None:
        ref = load_world_referential()
        city = ref.cities["halfgate"]
        assert city.position == Position(92000, 38000)

    def test_lower_quays_anchor(self) -> None:
        ref = load_world_referential()
        d = ref.districts["lower_quays"]
        assert d.anchor == Position(91900, 37700)

    def test_lower_quays_footprint_is_valid_polygon(self) -> None:
        """The committed sidecar is a valid closed polygon.

        J2 (2026-05-22) replaced the J1 stub rectangle (4 vertices) with the
        real bitmap-derived L-shape footprint produced by
        tools/extract_district_polygon.py.  An L-shape has 6 corners, so this
        test asserts the *contract* (>= 3 vertices, all in world bounds) rather
        than a vertex count frozen on the obsolete stub.
        """
        ref = load_world_referential()
        d = ref.districts["lower_quays"]
        assert len(d.footprint) >= 3
        for v in d.footprint:
            assert ref.world.contains(v.x, v.y)

    def test_district_parent_city(self) -> None:
        ref = load_world_referential()
        assert ref.districts["lower_quays"].parent_city_id == "halfgate"
        assert ref.districts["high_wall"].parent_city_id == "halfgate"

    def test_city_district_ids(self) -> None:
        ref = load_world_referential()
        city = ref.cities["halfgate"]
        assert "lower_quays" in city.district_ids
        assert "high_wall" in city.district_ids

    def test_load_from_tmp(self, minimal_yaml_world: tuple[Path, Path]) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        ref = load_world_referential(world_yaml, assets_dir)
        assert ref.world.max_x == 500_000
        assert len(ref.cities) == 1
        assert len(ref.districts) == 2


# ---------------------------------------------------------------------------
# Schema version rejection
# ---------------------------------------------------------------------------


class TestSchemaVersionRejection:
    def test_wrong_version_raises(
        self, minimal_yaml_world: tuple[Path, Path], tmp_path: Path
    ) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        raw = yaml.safe_load(world_yaml.read_text())
        raw["schema_version"] = 99
        bad_yaml = tmp_path / "bad_version.yaml"
        bad_yaml.write_text(yaml.dump(raw), encoding="utf-8")
        with pytest.raises(WorldSchemaError, match="schema_version"):
            load_world_referential(bad_yaml, assets_dir)

    def test_missing_version_raises(
        self, minimal_yaml_world: tuple[Path, Path], tmp_path: Path
    ) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        raw = yaml.safe_load(world_yaml.read_text())
        del raw["schema_version"]
        bad_yaml = tmp_path / "no_version.yaml"
        bad_yaml.write_text(yaml.dump(raw), encoding="utf-8")
        with pytest.raises(WorldSchemaError, match="schema_version"):
            load_world_referential(bad_yaml, assets_dir)


# ---------------------------------------------------------------------------
# Missing required fields
# ---------------------------------------------------------------------------


class TestMissingFields:
    def test_missing_world_section_raises(
        self, minimal_yaml_world: tuple[Path, Path], tmp_path: Path
    ) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        raw = yaml.safe_load(world_yaml.read_text())
        del raw["world"]
        bad_yaml = tmp_path / "no_world.yaml"
        bad_yaml.write_text(yaml.dump(raw), encoding="utf-8")
        with pytest.raises(WorldSchemaError, match="'world'"):
            load_world_referential(bad_yaml, assets_dir)

    def test_city_references_unknown_district_raises(
        self, minimal_yaml_world: tuple[Path, Path], tmp_path: Path
    ) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        raw = yaml.safe_load(world_yaml.read_text())
        raw["cities"][0]["districts"].append("nonexistent_district")
        bad_yaml = tmp_path / "bad_ref.yaml"
        bad_yaml.write_text(yaml.dump(raw), encoding="utf-8")
        with pytest.raises(WorldSchemaError, match="nonexistent_district"):
            load_world_referential(bad_yaml, assets_dir)


# ---------------------------------------------------------------------------
# Out-of-bounds position
# ---------------------------------------------------------------------------


class TestOutOfBoundsPosition:
    def test_city_position_out_of_bounds_raises(
        self, minimal_yaml_world: tuple[Path, Path], tmp_path: Path
    ) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        raw = yaml.safe_load(world_yaml.read_text())
        raw["cities"][0]["position"] = {"x": 999_999, "y": 38000}
        bad_yaml = tmp_path / "oob_city.yaml"
        bad_yaml.write_text(yaml.dump(raw), encoding="utf-8")
        with pytest.raises(WorldSchemaError, match="outside world bounds"):
            load_world_referential(bad_yaml, assets_dir)


# ---------------------------------------------------------------------------
# Missing sidecar
# ---------------------------------------------------------------------------


class TestMissingSidecar:
    def test_missing_sidecar_raises(
        self, minimal_yaml_world: tuple[Path, Path], tmp_path: Path
    ) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        raw = yaml.safe_load(world_yaml.read_text())
        raw["districts"][0]["footprint_polygon"] = "cities/halfgate/does_not_exist.polygon.json"
        bad_yaml = tmp_path / "missing_sidecar.yaml"
        bad_yaml.write_text(yaml.dump(raw), encoding="utf-8")
        with pytest.raises(WorldSchemaError, match="not found"):
            load_world_referential(bad_yaml, assets_dir)


# ---------------------------------------------------------------------------
# Malformed sidecar
# ---------------------------------------------------------------------------


class TestMalformedSidecar:
    def test_sidecar_with_too_few_vertices_raises(
        self, minimal_yaml_world: tuple[Path, Path]
    ) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        bad_sidecar = assets_dir / "cities" / "halfgate" / "lower_quays_mask.polygon.json"
        bad_sidecar.write_text(
            json.dumps({"vertices": [{"x": 91900, "y": 37700}, {"x": 92200, "y": 37700}]}),
            encoding="utf-8",
        )
        with pytest.raises(WorldSchemaError, match="at least 3"):
            load_world_referential(world_yaml, assets_dir)

    def test_sidecar_vertex_out_of_bounds_raises(
        self, minimal_yaml_world: tuple[Path, Path]
    ) -> None:
        world_yaml, assets_dir = minimal_yaml_world
        bad_sidecar = assets_dir / "cities" / "halfgate" / "lower_quays_mask.polygon.json"
        bad_sidecar.write_text(
            json.dumps(
                {
                    "vertices": [
                        {"x": 91900, "y": 37700},
                        {"x": 92200, "y": 37700},
                        {"x": 999_999, "y": 38000},  # out of bounds
                    ]
                }
            ),
            encoding="utf-8",
        )
        with pytest.raises(WorldSchemaError, match="outside world bounds"):
            load_world_referential(world_yaml, assets_dir)
