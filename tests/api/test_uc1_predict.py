"""Tests for ``POST /api/uc1/predict``.

These tests exercise the endpoint contract without loading real model weights.
The predictor is replaced on ``app.state`` after lifespan fires so the tests run
without CUDA, sentence-transformers, or a trained checkpoint.

Fixtures:

  ``client_ready``              — yields (TestClient, MagicMock) in ready state.
  ``client_not_ready``          — yields TestClient with predictor in 503 state.
  ``client_with_boundary_delta`` — parametrized over [-5, 0, +5] boundary deltas.

Pin the wire schema: the C# client deserializes ``delta`` as a float. Any
breaking field rename here ripples into the Godot client repo.
"""

from __future__ import annotations

from collections.abc import Iterator
from unittest.mock import MagicMock

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app
from wayfinders.api.predictor import PredictorNotReadyError

# ---------------------------------------------------------------------------
# Sample prose fixtures (minimal but structurally valid EN prose)
# ---------------------------------------------------------------------------

_CHAR_PROSE = (
    "Kira is a scout of the Ridgewatch company.\n"
    "Kira is strong, agile, and perceptive.\n"
    "Kira is composed."
)
_ACTION_PROSE = (
    "The action is a stealth approach.\n"
    "Kira is the actor.\n"
    "Kira is sneaking past two sentries posted at the watchtower."
)
_CONTEXT_PROSE = (
    "The mission is a reconnaissance for the Ridgewatch company.\n"
    "The mission objective is to map the enemy camp beyond the ridge before dawn.\n"
    "The stakes are high: failure means the company moves blind into the valley."
)

_VALID_PAYLOAD = {
    "char_prose": _CHAR_PROSE,
    "action_prose": _ACTION_PROSE,
    "context_prose": _CONTEXT_PROSE,
}


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _make_predictor(ready: bool, delta: float = 1.5) -> MagicMock:
    """Return a mock Predictor that returns *delta* when ready, or raises when not."""
    mock = MagicMock()
    mock.ready = ready
    mock.device = "cpu"
    if ready:
        mock.predict.return_value = delta
    else:
        mock.predict.side_effect = PredictorNotReadyError("head not loaded")
    return mock


# ---------------------------------------------------------------------------
# Fixtures
#
# Pattern: enter the TestClient context (lifespan fires and loads the real
# Predictor), then overwrite app.state.predictor with our mock.  We yield the
# mock alongside the client so tests can make assertions on it directly without
# re-reading app.state (which avoids any confusion about what is on the state).
# ---------------------------------------------------------------------------


@pytest.fixture
def client_ready() -> Iterator[tuple[TestClient, MagicMock]]:
    """TestClient with a mock predictor in ready state (delta=1.5).

    Yields ``(client, mock_predictor)``.
    """
    with TestClient(app, raise_server_exceptions=True) as tc:
        mock = _make_predictor(ready=True, delta=1.5)
        app.state.predictor = mock
        yield tc, mock


@pytest.fixture
def client_not_ready() -> Iterator[TestClient]:
    """TestClient with predictor not ready — /api/uc1/predict returns 503."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        app.state.predictor = _make_predictor(ready=False)
        yield tc


@pytest.fixture(params=[-5.0, 0.0, 5.0])
def client_with_boundary_delta(
    request: pytest.FixtureRequest,
) -> Iterator[tuple[TestClient, float]]:
    """TestClient parametrized over boundary delta values in [-5, 0, +5]."""
    boundary: float = request.param
    with TestClient(app, raise_server_exceptions=True) as tc:
        app.state.predictor = _make_predictor(ready=True, delta=boundary)
        yield tc, boundary


# ---------------------------------------------------------------------------
# Contract / happy-path tests
# ---------------------------------------------------------------------------


class TestUC1PredictHappyPath:
    def test_returns_200(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        response = tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert response.status_code == 200

    def test_response_contains_delta(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        response = tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert "delta" in response.json()

    def test_delta_is_float(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        response = tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert isinstance(response.json()["delta"], float)

    def test_delta_matches_mock_value(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """Pin the mock delta value so a schema regression is visible."""
        tc, _ = client_ready
        response = tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert response.json()["delta"] == pytest.approx(1.5, abs=1e-6)

    def test_response_is_json(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        tc, _ = client_ready
        response = tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert response.headers["content-type"].startswith("application/json")

    def test_response_schema_only_delta_field(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """Wire schema must be exactly {"delta": <float>} — no extra fields."""
        tc, _ = client_ready
        response = tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert set(response.json().keys()) == {"delta"}

    def test_predictor_called_with_correct_prose(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """Endpoint must forward prose strings verbatim to predictor.predict()."""
        tc, mock = client_ready
        tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        mock.predict.assert_called_once_with(
            char_prose=_CHAR_PROSE,
            action_prose=_ACTION_PROSE,
            context_prose=_CONTEXT_PROSE,
        )


# ---------------------------------------------------------------------------
# 503 path — model not ready
# ---------------------------------------------------------------------------


class TestUC1PredictNotReady:
    def test_returns_503_when_not_ready(self, client_not_ready: TestClient) -> None:
        response = client_not_ready.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert response.status_code == 503

    def test_503_body_has_detail(self, client_not_ready: TestClient) -> None:
        response = client_not_ready.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        body = response.json()
        assert "detail" in body
        assert len(body["detail"]) > 0


# ---------------------------------------------------------------------------
# Validation — bad inputs rejected before hitting predictor
# ---------------------------------------------------------------------------


class TestUC1PredictValidation:
    @pytest.mark.parametrize(
        "missing_field",
        ["char_prose", "action_prose", "context_prose"],
    )
    def test_missing_field_returns_422(
        self,
        client_ready: tuple[TestClient, MagicMock],
        missing_field: str,
    ) -> None:
        tc, _ = client_ready
        payload = {k: v for k, v in _VALID_PAYLOAD.items() if k != missing_field}
        response = tc.post("/api/uc1/predict", json=payload)

        assert response.status_code == 422

    @pytest.mark.parametrize(
        "empty_field",
        ["char_prose", "action_prose", "context_prose"],
    )
    def test_empty_string_field_returns_422(
        self,
        client_ready: tuple[TestClient, MagicMock],
        empty_field: str,
    ) -> None:
        tc, _ = client_ready
        payload = dict(_VALID_PAYLOAD)
        payload[empty_field] = ""
        response = tc.post("/api/uc1/predict", json=payload)

        # min_length=1 on the Pydantic model raises a validation error -> 422.
        assert response.status_code == 422

    def test_unknown_field_returns_422(self, client_ready: tuple[TestClient, MagicMock]) -> None:
        """extra='forbid' must reject spurious fields at the boundary."""
        tc, _ = client_ready
        payload = dict(_VALID_PAYLOAD)
        payload["surprise"] = "unexpected"
        response = tc.post("/api/uc1/predict", json=payload)

        assert response.status_code == 422

    def test_predictor_not_called_on_validation_error(
        self, client_ready: tuple[TestClient, MagicMock]
    ) -> None:
        """If input validation fails, the predictor should never be called."""
        tc, mock = client_ready
        bad_payload = {"char_prose": ""}
        tc.post("/api/uc1/predict", json=bad_payload)

        mock.predict.assert_not_called()


# ---------------------------------------------------------------------------
# delta range — boundary values pass Pydantic response validation
# ---------------------------------------------------------------------------


class TestUC1DeltaRange:
    def test_boundary_deltas_accepted(
        self,
        client_with_boundary_delta: tuple[TestClient, float],
    ) -> None:
        """Boundary values in [-5, +5] must pass the response validator."""
        tc, boundary_delta = client_with_boundary_delta
        response = tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert response.status_code == 200
        assert response.json()["delta"] == pytest.approx(boundary_delta, abs=1e-6)
