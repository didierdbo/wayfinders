"""Integration tests for ``GET /api/world``.

Test categories:
  1. HTTP layer — 200, JSON content-type, parseable body.
  2. Top-level schema — schema_version, world, cities, districts keys present
     with correct types.
  3. World DTO — max_x / max_y match spec (500_000 / 250_000).
  4. MVP content — Halfgate city present with correct position; both M1
     districts present (lower_quays, high_wall).
  5. District geometry — anchor, footprint (>= 3 vertices, all in-bounds),
     cell_size_m == 2.
  6. Parent city cross-reference — each district's parent_city_id resolves
     to a city present in the cities list.
  7. Polygon serialisation — footprint vertices are dicts with int x/y.
  8. Stability — two calls return identical payloads (static, cached).
  9. Error path — 500 when world referential is not loaded (monkeypatch).
"""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app

# ---------------------------------------------------------------------------
# Fixture
# ---------------------------------------------------------------------------


@pytest.fixture()
def client() -> TestClient:
    """TestClient with lifespan (loads world referential at startup)."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        yield tc


# ---------------------------------------------------------------------------
# 1. HTTP layer
# ---------------------------------------------------------------------------


class TestWorldHttp:
    def test_returns_200(self, client: TestClient) -> None:
        response = client.get("/api/world")
        assert response.status_code == 200

    def test_content_type_is_json(self, client: TestClient) -> None:
        response = client.get("/api/world")
        assert response.headers["content-type"].startswith("application/json")

    def test_body_is_parseable_json_dict(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert isinstance(payload, dict)


# ---------------------------------------------------------------------------
# 2. Top-level schema
# ---------------------------------------------------------------------------


class TestWorldTopLevelSchema:
    def test_schema_version_present(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert "schema_version" in payload

    def test_schema_version_is_int(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert isinstance(payload["schema_version"], int)

    def test_schema_version_is_1(self, client: TestClient) -> None:
        """Current world.yaml is schema version 1."""
        payload = client.get("/api/world").json()
        assert payload["schema_version"] == 1

    def test_world_key_present(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert "world" in payload

    def test_cities_key_present_and_list(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert "cities" in payload
        assert isinstance(payload["cities"], list)

    def test_districts_key_present_and_list(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert "districts" in payload
        assert isinstance(payload["districts"], list)


# ---------------------------------------------------------------------------
# 3. World DTO
# ---------------------------------------------------------------------------


class TestWorldDto:
    def test_max_x(self, client: TestClient) -> None:
        """Locked at 500_000 m (Varn spec §1.2)."""
        payload = client.get("/api/world").json()
        assert payload["world"]["max_x"] == 500_000

    def test_max_y(self, client: TestClient) -> None:
        """Locked at 250_000 m (Varn spec §1.2)."""
        payload = client.get("/api/world").json()
        assert payload["world"]["max_y"] == 250_000

    def test_world_fields_are_positive_ints(self, client: TestClient) -> None:
        w = client.get("/api/world").json()["world"]
        assert isinstance(w["max_x"], int) and w["max_x"] > 0
        assert isinstance(w["max_y"], int) and w["max_y"] > 0


# ---------------------------------------------------------------------------
# 4. MVP content — Halfgate + both M1 districts
# ---------------------------------------------------------------------------


class TestWorldMvpContent:
    def _city_by_id(self, payload: dict, city_id: str) -> dict | None:
        return next((c for c in payload["cities"] if c["id"] == city_id), None)

    def _district_by_id(self, payload: dict, district_id: str) -> dict | None:
        return next((d for d in payload["districts"] if d["id"] == district_id), None)

    def test_halfgate_present(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert self._city_by_id(payload, "halfgate") is not None

    def test_halfgate_name(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        city = self._city_by_id(payload, "halfgate")
        assert city is not None
        assert city["name"] == "Halfgate"

    def test_halfgate_position(self, client: TestClient) -> None:
        """Placeholder centroid locked in Varn spec §7: (92000, 38000)."""
        payload = client.get("/api/world").json()
        city = self._city_by_id(payload, "halfgate")
        assert city is not None
        assert city["position"]["x"] == 92_000
        assert city["position"]["y"] == 38_000

    def test_halfgate_has_lower_quays_and_high_wall(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        city = self._city_by_id(payload, "halfgate")
        assert city is not None
        assert "lower_quays" in city["district_ids"]
        assert "high_wall" in city["district_ids"]

    def test_lower_quays_district_present(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert self._district_by_id(payload, "lower_quays") is not None

    def test_high_wall_district_present(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        assert self._district_by_id(payload, "high_wall") is not None

    def test_lower_quays_name(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        d = self._district_by_id(payload, "lower_quays")
        assert d is not None
        assert d["name"] == "Lower Quays"

    def test_high_wall_name(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        d = self._district_by_id(payload, "high_wall")
        assert d is not None
        assert d["name"] == "High Wall"


# ---------------------------------------------------------------------------
# 5. District geometry
# ---------------------------------------------------------------------------


class TestDistrictGeometry:
    def _all_districts(self, client: TestClient) -> list[dict]:
        return client.get("/api/world").json()["districts"]

    def test_each_district_has_anchor(self, client: TestClient) -> None:
        for d in self._all_districts(client):
            assert "anchor" in d, f"District {d['id']!r} missing anchor"
            assert "x" in d["anchor"] and "y" in d["anchor"]

    def test_each_district_has_footprint(self, client: TestClient) -> None:
        for d in self._all_districts(client):
            assert "footprint" in d, f"District {d['id']!r} missing footprint"
            assert isinstance(d["footprint"], list)

    def test_footprint_has_at_least_3_vertices(self, client: TestClient) -> None:
        for d in self._all_districts(client):
            assert len(d["footprint"]) >= 3, (
                f"District {d['id']!r} footprint has {len(d['footprint'])} vertices; need >= 3"
            )

    def test_cell_size_m_is_2(self, client: TestClient) -> None:
        """cell_size_m is locked at 2 m by Varn spec §1.3."""
        for d in self._all_districts(client):
            assert d["cell_size_m"] == 2, (
                f"District {d['id']!r}: cell_size_m={d['cell_size_m']}, expected 2"
            )

    def test_lower_quays_anchor(self, client: TestClient) -> None:
        """Anchor from world.yaml: SW corner at (91900, 37700)."""
        payload = client.get("/api/world").json()
        d = next(x for x in payload["districts"] if x["id"] == "lower_quays")
        assert d["anchor"]["x"] == 91_900
        assert d["anchor"]["y"] == 37_700


# ---------------------------------------------------------------------------
# 6. Parent city cross-reference
# ---------------------------------------------------------------------------


class TestParentCityReference:
    def test_all_district_parent_city_ids_resolve(self, client: TestClient) -> None:
        """Every district's parent_city_id must appear in the cities list."""
        payload = client.get("/api/world").json()
        city_ids = {c["id"] for c in payload["cities"]}
        for d in payload["districts"]:
            assert d["parent_city_id"] in city_ids, (
                f"District {d['id']!r}: parent_city_id={d['parent_city_id']!r} "
                f"not found in cities {city_ids}"
            )

    def test_lower_quays_parent_is_halfgate(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        d = next(x for x in payload["districts"] if x["id"] == "lower_quays")
        assert d["parent_city_id"] == "halfgate"

    def test_high_wall_parent_is_halfgate(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        d = next(x for x in payload["districts"] if x["id"] == "high_wall")
        assert d["parent_city_id"] == "halfgate"


# ---------------------------------------------------------------------------
# 7. Polygon serialisation
# ---------------------------------------------------------------------------


class TestPolygonSerialisation:
    def test_footprint_vertices_are_dicts(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        for d in payload["districts"]:
            for i, v in enumerate(d["footprint"]):
                assert isinstance(v, dict), (
                    f"District {d['id']!r} vertex {i}: expected dict, got {type(v).__name__}"
                )

    def test_footprint_vertices_have_x_y(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        for d in payload["districts"]:
            for i, v in enumerate(d["footprint"]):
                assert "x" in v and "y" in v, (
                    f"District {d['id']!r} vertex {i}: missing x or y — got {v!r}"
                )

    def test_footprint_vertices_are_ints(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        for d in payload["districts"]:
            for i, v in enumerate(d["footprint"]):
                assert isinstance(v["x"], int) and isinstance(v["y"], int), (
                    f"District {d['id']!r} vertex {i}: x/y must be int, got {v!r}"
                )

    def test_footprint_vertices_in_world_bounds(self, client: TestClient) -> None:
        payload = client.get("/api/world").json()
        max_x = payload["world"]["max_x"]
        max_y = payload["world"]["max_y"]
        for d in payload["districts"]:
            for i, v in enumerate(d["footprint"]):
                assert 0 <= v["x"] < max_x, (
                    f"District {d['id']!r} vertex {i}: x={v['x']} out of [0, {max_x})"
                )
                assert 0 <= v["y"] < max_y, (
                    f"District {d['id']!r} vertex {i}: y={v['y']} out of [0, {max_y})"
                )


# ---------------------------------------------------------------------------
# 8. Stability — two calls return identical payloads
# ---------------------------------------------------------------------------


class TestWorldStability:
    def test_two_calls_return_identical_payload(self, client: TestClient) -> None:
        r1 = client.get("/api/world").json()
        r2 = client.get("/api/world").json()
        assert r1 == r2

    def test_cities_are_sorted_by_id(self, client: TestClient) -> None:
        """Endpoint sorts cities by id ascending for stable serialisation."""
        payload = client.get("/api/world").json()
        city_ids = [c["id"] for c in payload["cities"]]
        assert city_ids == sorted(city_ids)

    def test_districts_are_sorted_by_id(self, client: TestClient) -> None:
        """Endpoint sorts districts by id ascending for stable serialisation."""
        payload = client.get("/api/world").json()
        district_ids = [d["id"] for d in payload["districts"]]
        assert district_ids == sorted(district_ids)


# ---------------------------------------------------------------------------
# 9. Error path — 500 when world referential not loaded
# ---------------------------------------------------------------------------


class TestWorldErrorPath:
    def test_500_when_world_referential_is_none(self, client: TestClient) -> None:
        """Simulate a failed startup by patching app.state.world_referential to None."""
        original = client.app.state.world_referential  # type: ignore[attr-defined]
        try:
            client.app.state.world_referential = None  # type: ignore[attr-defined]
            response = client.get("/api/world")
            assert response.status_code == 500
        finally:
            client.app.state.world_referential = original  # type: ignore[attr-defined]

    def test_500_detail_mentions_world_yaml(self, client: TestClient) -> None:
        original = client.app.state.world_referential  # type: ignore[attr-defined]
        try:
            client.app.state.world_referential = None  # type: ignore[attr-defined]
            response = client.get("/api/world")
            detail = response.json().get("detail", "")
            assert "world.yaml" in detail.lower() or "world referential" in detail.lower()
        finally:
            client.app.state.world_referential = original  # type: ignore[attr-defined]
