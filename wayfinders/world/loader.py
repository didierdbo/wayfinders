"""Loader for world.yaml and district polygon sidecars.

Reads ``assets/world/world.yaml`` (canonical world geometry source of truth,
Varn-locked schema v1).  Resolves and loads the ``*.polygon.json`` sidecar
for each district.  Returns a fully hydrated ``WorldReferential``.

Schema contract:
  - ``schema_version: 1`` is the only supported version.  Unknown versions
    raise ``WorldSchemaError`` hard (no fallback — callers must migrate).
  - Polygon sidecar paths in ``world.yaml`` are relative to the ``assets/world/``
    directory.
  - If a sidecar is missing or malformed, a ``WorldSchemaError`` is raised.

Stub note:
  J1 sidecars are hand-written stubs (``_stub: true`` in the JSON).  The loader
  treats them as valid polygon data.  Replace at J2 with Mira's bitmap-derived
  sidecars produced by ``tools/extract_district_polygon.py``.
"""

from __future__ import annotations

import json
from pathlib import Path

import yaml

from wayfinders.world.models import City, District, Position, World, WorldReferential

# Default path — relative to the repo root (the package is installed editable
# so Path(__file__) resolves inside wayfinders/world/).
_REPO_ROOT = Path(__file__).parent.parent.parent
_DEFAULT_WORLD_YAML = _REPO_ROOT / "assets" / "world" / "world.yaml"
_DEFAULT_ASSETS_WORLD = _REPO_ROOT / "assets" / "world"

_SUPPORTED_SCHEMA_VERSION = 1


class WorldSchemaError(ValueError):
    """Raised when world.yaml or a sidecar fails schema validation."""


def load_world_referential(
    world_yaml_path: Path | None = None,
    assets_world_dir: Path | None = None,
) -> WorldReferential:
    """Load and return a fully hydrated WorldReferential.

    Args:
        world_yaml_path: Path to ``world.yaml``.  Defaults to
            ``<repo_root>/assets/world/world.yaml``.
        assets_world_dir: Root for resolving relative sidecar paths stored
            in ``world.yaml``.  Defaults to
            ``<repo_root>/assets/world/``.

    Returns:
        A ``WorldReferential`` with all cities and districts loaded.

    Raises:
        WorldSchemaError: On schema version mismatch, missing required fields,
            missing sidecar files, or malformed polygon data.
        FileNotFoundError: If ``world_yaml_path`` does not exist.
    """
    yaml_path = world_yaml_path or _DEFAULT_WORLD_YAML
    assets_dir = assets_world_dir or _DEFAULT_ASSETS_WORLD

    with yaml_path.open(encoding="utf-8") as fh:
        raw = yaml.safe_load(fh)

    if not isinstance(raw, dict):
        raise WorldSchemaError(f"world.yaml root must be a mapping, got {type(raw).__name__}")

    _check_schema_version(raw)

    world = _load_world(raw)
    cities_list, districts_list = _load_cities_and_districts(raw, assets_dir, world)

    cities = {c.id: c for c in cities_list}
    districts = {d.id: d for d in districts_list}

    return WorldReferential(world=world, cities=cities, districts=districts)


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------


def _check_schema_version(raw: dict[str, object]) -> None:
    version = raw.get("schema_version")
    if version != _SUPPORTED_SCHEMA_VERSION:
        raise WorldSchemaError(
            f"Unsupported world.yaml schema_version: {version!r}. "
            f"Expected {_SUPPORTED_SCHEMA_VERSION}."
        )


def _load_world(raw: dict[str, object]) -> World:
    world_raw = raw.get("world")
    if not isinstance(world_raw, dict):
        raise WorldSchemaError("world.yaml missing 'world' mapping.")
    max_x = _require_positive_int(world_raw, "max_x", "world")
    max_y = _require_positive_int(world_raw, "max_y", "world")
    return World(max_x=max_x, max_y=max_y)


def _load_cities_and_districts(
    raw: dict[str, object],
    assets_dir: Path,
    world: World,
) -> tuple[list[City], list[District]]:
    raw_cities = raw.get("cities") or []
    raw_districts = raw.get("districts") or []

    if not isinstance(raw_cities, list):
        raise WorldSchemaError("world.yaml 'cities' must be a list.")
    if not isinstance(raw_districts, list):
        raise WorldSchemaError("world.yaml 'districts' must be a list.")

    # Index districts by id first so we can validate city → district references.
    districts_by_id: dict[str, District] = {}
    for d_raw in raw_districts:
        if not isinstance(d_raw, dict):
            raise WorldSchemaError(f"District entry must be a mapping, got {type(d_raw).__name__}")
        district = _load_district(d_raw, assets_dir, world)
        if district.id in districts_by_id:
            raise WorldSchemaError(f"Duplicate district id: {district.id!r}")
        districts_by_id[district.id] = district

    cities: list[City] = []
    for c_raw in raw_cities:
        if not isinstance(c_raw, dict):
            raise WorldSchemaError(f"City entry must be a mapping, got {type(c_raw).__name__}")
        city = _load_city(c_raw, world, districts_by_id)
        cities.append(city)

    return cities, list(districts_by_id.values())


def _load_city(
    raw: dict[str, object],
    world: World,
    districts_by_id: dict[str, District],
) -> City:
    city_id = _require_str(raw, "id", "city")
    name = _require_str(raw, "name", f"city '{city_id}'")
    pos_raw = raw.get("position")
    position = _load_position(pos_raw, f"city '{city_id}'", world)
    bitmap = raw.get("bitmap")
    bitmap_str = str(bitmap) if bitmap is not None else None

    district_ids_raw = raw.get("districts") or []
    if not isinstance(district_ids_raw, list):
        raise WorldSchemaError(f"City '{city_id}' districts must be a list.")
    district_ids: list[str] = []
    for did in district_ids_raw:
        if not isinstance(did, str):
            raise WorldSchemaError(
                f"City '{city_id}' district id must be a string, got {type(did).__name__}"
            )
        if did not in districts_by_id:
            raise WorldSchemaError(
                f"City '{city_id}' references unknown district id {did!r}. "
                "District must be defined in the 'districts' list."
            )
        district_ids.append(did)

    return City(
        id=city_id,
        name=name,
        position=position,
        district_ids=tuple(district_ids),
        bitmap=bitmap_str,
    )


def _load_district(raw: dict[str, object], assets_dir: Path, world: World) -> District:
    district_id = _require_str(raw, "id", "district")
    name = _require_str(raw, "name", f"district '{district_id}'")
    parent_city_id = _require_str(raw, "parent_city", f"district '{district_id}'")
    anchor_raw = raw.get("anchor")
    anchor = _load_position(anchor_raw, f"district '{district_id}' anchor", world)
    cell_size_m_raw = raw.get("cell_size_m", 2)
    if not isinstance(cell_size_m_raw, int) or cell_size_m_raw <= 0:
        raise WorldSchemaError(
            f"District '{district_id}' cell_size_m must be a positive int, got {cell_size_m_raw!r}"
        )

    # Load polygon from sidecar.
    sidecar_key = raw.get("footprint_polygon")
    if not isinstance(sidecar_key, str) or not sidecar_key:
        raise WorldSchemaError(
            f"District '{district_id}' missing 'footprint_polygon' path. "
            "J1 stubs must include a hand-written sidecar path."
        )
    sidecar_path = assets_dir / sidecar_key
    footprint = _load_polygon_sidecar(sidecar_path, district_id, world)

    return District(
        id=district_id,
        name=name,
        parent_city_id=parent_city_id,
        anchor=anchor,
        footprint=footprint,
        cell_size_m=cell_size_m_raw,
    )


def _load_polygon_sidecar(
    sidecar_path: Path, district_id: str, world: World
) -> tuple[Position, ...]:
    """Load and validate a *.polygon.json sidecar file.

    The sidecar contains a ``vertices`` array of ``{x, y}`` objects in
    world meters.  The ``_stub`` key (if present and True) is accepted
    without warning — stubs are valid J1 data.
    """
    if not sidecar_path.exists():
        raise WorldSchemaError(
            f"District '{district_id}' polygon sidecar not found: {sidecar_path}. "
            "Run tools/extract_district_polygon.py or provide a J1 stub."
        )

    with sidecar_path.open(encoding="utf-8") as fh:
        raw = json.load(fh)

    if not isinstance(raw, dict):
        raise WorldSchemaError(
            f"District '{district_id}' sidecar {sidecar_path} must be a JSON object."
        )

    vertices_raw = raw.get("vertices")
    if not isinstance(vertices_raw, list):
        raise WorldSchemaError(
            f"District '{district_id}' sidecar {sidecar_path} missing 'vertices' list."
        )

    vertices: list[Position] = []
    for i, v in enumerate(vertices_raw):
        if not isinstance(v, dict):
            raise WorldSchemaError(
                f"District '{district_id}' sidecar vertex {i} must be a dict "
                f"with 'x' and 'y', got {type(v).__name__}"
            )
        vx = v.get("x")
        vy = v.get("y")
        if not isinstance(vx, int) or not isinstance(vy, int):
            raise WorldSchemaError(
                f"District '{district_id}' sidecar vertex {i}: "
                f"x and y must be integers, got x={vx!r} y={vy!r}"
            )
        if not world.contains(vx, vy):
            raise WorldSchemaError(
                f"District '{district_id}' sidecar vertex {i} ({vx}, {vy}) "
                f"is outside world bounds [0, {world.max_x}) x [0, {world.max_y})."
            )
        vertices.append(Position(vx, vy))

    if len(vertices) < 3:
        raise WorldSchemaError(
            f"District '{district_id}' polygon has {len(vertices)} vertices; at least 3 required."
        )

    return tuple(vertices)


def _load_position(raw: object, context: str, world: World) -> Position:
    if not isinstance(raw, dict):
        raise WorldSchemaError(
            f"{context}: position must be a mapping with 'x' and 'y', got {type(raw).__name__}"
        )
    x = raw.get("x")
    y = raw.get("y")
    if not isinstance(x, int) or not isinstance(y, int):
        raise WorldSchemaError(f"{context}: position x and y must be integers, got x={x!r} y={y!r}")
    if not world.contains(x, y):
        raise WorldSchemaError(
            f"{context}: position ({x}, {y}) is outside world bounds "
            f"[0, {world.max_x}) x [0, {world.max_y})."
        )
    return Position(x, y)


def _require_str(raw: dict[str, object], key: str, context: str) -> str:
    val = raw.get(key)
    if not isinstance(val, str) or not val:
        raise WorldSchemaError(f"{context}: '{key}' must be a non-empty string, got {val!r}")
    return val


def _require_positive_int(raw: dict[str, object], key: str, context: str) -> int:
    val = raw.get(key)
    if not isinstance(val, int) or val <= 0:
        raise WorldSchemaError(f"{context}: '{key}' must be a positive int, got {val!r}")
    return val
