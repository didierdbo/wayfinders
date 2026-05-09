"""Tests for ``GET /api/health``.

Schema (locked 2026-05-09, substep 13):
  {"status": "ok", "model_ready": <bool>}

``model_ready`` lets Rune's ApiClient gate on ML availability before calling
``/api/uc1/predict``.  The endpoint always returns HTTP 200 regardless of
model state (liveness vs readiness separation).
"""

from __future__ import annotations

from unittest.mock import MagicMock

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app


@pytest.fixture
def client_model_ready() -> TestClient:
    """TestClient with a mock predictor in ready state."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        mock = MagicMock()
        mock.ready = True
        app.state.predictor = mock
        yield tc


@pytest.fixture
def client_model_not_ready() -> TestClient:
    """TestClient with a mock predictor NOT ready (no ONNX loaded)."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        mock = MagicMock()
        mock.ready = False
        app.state.predictor = mock
        yield tc


def test_health_returns_200(client: TestClient) -> None:
    response = client.get("/api/health")

    assert response.status_code == 200


def test_health_status_is_ok(client: TestClient) -> None:
    response = client.get("/api/health")

    assert response.json()["status"] == "ok"


def test_health_model_ready_field_present(client: TestClient) -> None:
    """model_ready field must be present and be a bool."""
    response = client.get("/api/health")

    body = response.json()
    assert "model_ready" in body
    assert isinstance(body["model_ready"], bool)


def test_health_schema_exact_fields(client: TestClient) -> None:
    """Wire schema must be exactly {status, model_ready} — no extra fields."""
    response = client.get("/api/health")

    assert set(response.json().keys()) == {"status", "model_ready"}


def test_health_model_ready_true_when_ready(client_model_ready: TestClient) -> None:
    response = client_model_ready.get("/api/health")

    assert response.json()["model_ready"] is True


def test_health_model_ready_false_when_not_ready(client_model_not_ready: TestClient) -> None:
    response = client_model_not_ready.get("/api/health")

    assert response.json()["model_ready"] is False


def test_health_response_is_json(client: TestClient) -> None:
    response = client.get("/api/health")

    assert response.headers["content-type"].startswith("application/json")
