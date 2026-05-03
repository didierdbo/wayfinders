"""Tests for ``GET /api/units``.

These tests pin the wire schema. The C# client's ``UnitData`` record
deserializes against this exact JSON shape, so any breaking change here
ripples into the Godot client repo.
"""

from __future__ import annotations

from fastapi.testclient import TestClient

EXPECTED_FIELDS = {"display_name", "max_hp", "base_speed", "portrait"}
EXPECTED_DISPLAY_NAMES = ["Scout", "Warden", "Archer"]


def test_units_returns_seed_roster(client: TestClient) -> None:
    response = client.get("/api/units")

    assert response.status_code == 200
    payload = response.json()

    assert isinstance(payload, list), "Response must be a bare JSON array."
    assert len(payload) == 3
    assert [u["display_name"] for u in payload] == EXPECTED_DISPLAY_NAMES


def test_units_schema_matches_csharp_dto(client: TestClient) -> None:
    """Pin snake_case field names — the C# client maps these to PascalCase."""
    response = client.get("/api/units")

    payload = response.json()
    for unit in payload:
        assert set(unit.keys()) == EXPECTED_FIELDS


def test_units_field_types(client: TestClient) -> None:
    response = client.get("/api/units")

    for unit in response.json():
        assert isinstance(unit["display_name"], str)
        assert isinstance(unit["max_hp"], int)
        assert isinstance(unit["base_speed"], int)
        assert isinstance(unit["portrait"], str)


def test_scout_seed_values(client: TestClient) -> None:
    """Spot-check one unit end-to-end so a rename in seed_data fails loudly."""
    response = client.get("/api/units")

    scout = next(u for u in response.json() if u["display_name"] == "Scout")
    assert scout == {
        "display_name": "Scout",
        "max_hp": 8,
        "base_speed": 6,
        "portrait": "icon.svg",
    }
