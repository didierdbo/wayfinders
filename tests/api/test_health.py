"""Tests for ``GET /api/health``."""

from __future__ import annotations

from fastapi.testclient import TestClient


def test_health_returns_ok_status(client: TestClient) -> None:
    response = client.get("/api/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


def test_health_response_is_json(client: TestClient) -> None:
    response = client.get("/api/health")

    assert response.headers["content-type"].startswith("application/json")
