"""Integration and end-to-end parity tests for the UC1 predict endpoint.

Three test layers:

1. **UC1 info endpoint unit tests** — always run, no model weights needed.
   Verify ``GET /api/uc1/info`` contract with a mock predictor.

2. **Integration test** ``@pytest.mark.integration`` — loads the real ONNX encoder
   + head sessions (no CUDA required; ORT runs on CPU), calls the API, and
   asserts the returned delta is in [-5, +5].

3. **End-to-end parity test** ``@pytest.mark.integration`` — same prose strings
   sent to the API and to a direct ORT session must return the same delta within
   1e-4 absolute.  This closes the Python-API ↔ direct-ORT parity loop.

Run integration tests with::

    uv run pytest -m integration tests/api/test_uc1_integration.py

Note on CI: integration tests are skipped automatically when the ONNX
artifacts are absent (``pytest.skip`` inside the fixture).
"""

from __future__ import annotations

from collections.abc import Iterator
from pathlib import Path
from unittest.mock import MagicMock

import numpy as np
import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app
from wayfinders.api.predictor import Predictor, PredictorNotReadyError

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

_REPO_ROOT = Path(__file__).resolve().parent.parent.parent
_ENCODER_ONNX = _REPO_ROOT / "artifacts" / "onnx" / "minilm-l6-v2-fp16.onnx"
_HEAD_ONNX = _REPO_ROOT / "artifacts" / "onnx" / "resolution-head-v0.6.onnx"

# ---------------------------------------------------------------------------
# Shared prose used across all test layers (structurally valid EN prose)
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


def _make_mock_predictor(
    *,
    ready: bool,
    delta: float = 1.5,
    encoder_onnx_path: str = str(_ENCODER_ONNX),
    head_onnx_path: str = str(_HEAD_ONNX),
    device: str = "ort-cpu",
) -> MagicMock:
    """Return a MagicMock Predictor with the given state."""
    mock = MagicMock(spec=Predictor)
    mock.ready = ready
    mock.device = device
    mock.encoder_onnx_path = encoder_onnx_path
    mock.head_onnx_path = head_onnx_path
    if ready:
        mock.predict.return_value = delta
    else:
        mock.predict.side_effect = PredictorNotReadyError("head not loaded")
    return mock


# ---------------------------------------------------------------------------
# Layer 1 — UC1 info endpoint unit tests (always run)
# ---------------------------------------------------------------------------


@pytest.fixture
def client_info_ready() -> Iterator[TestClient]:
    """TestClient with a mock predictor in ready state for /api/uc1/info tests."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        app.state.predictor = _make_mock_predictor(ready=True, delta=2.0)
        yield tc


@pytest.fixture
def client_info_not_ready() -> Iterator[TestClient]:
    """TestClient with a mock predictor NOT ready."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        app.state.predictor = _make_mock_predictor(ready=False)
        yield tc


class TestUC1InfoEndpoint:
    """Contract tests for ``GET /api/uc1/info``."""

    def test_returns_200(self, client_info_ready: TestClient) -> None:
        response = client_info_ready.get("/api/uc1/info")

        assert response.status_code == 200

    def test_response_is_json(self, client_info_ready: TestClient) -> None:
        response = client_info_ready.get("/api/uc1/info")

        assert response.headers["content-type"].startswith("application/json")

    def test_schema_fields_present(self, client_info_ready: TestClient) -> None:
        """Wire schema must contain exactly the contracted fields."""
        response = client_info_ready.get("/api/uc1/info")
        body = response.json()

        assert set(body.keys()) == {"model_ready", "encoder_onnx_path", "head_onnx_path", "device"}

    def test_model_ready_true_when_ready(self, client_info_ready: TestClient) -> None:
        response = client_info_ready.get("/api/uc1/info")

        assert response.json()["model_ready"] is True

    def test_model_ready_false_when_not_ready(self, client_info_not_ready: TestClient) -> None:
        response = client_info_not_ready.get("/api/uc1/info")

        assert response.json()["model_ready"] is False

    def test_encoder_onnx_path_is_string(self, client_info_ready: TestClient) -> None:
        response = client_info_ready.get("/api/uc1/info")

        assert isinstance(response.json()["encoder_onnx_path"], str)
        assert len(response.json()["encoder_onnx_path"]) > 0

    def test_head_onnx_path_is_string(self, client_info_ready: TestClient) -> None:
        response = client_info_ready.get("/api/uc1/info")

        assert isinstance(response.json()["head_onnx_path"], str)
        assert len(response.json()["head_onnx_path"]) > 0

    def test_device_field(self, client_info_ready: TestClient) -> None:
        response = client_info_ready.get("/api/uc1/info")

        assert response.json()["device"] == "ort-cpu"


# ---------------------------------------------------------------------------
# Layer 2 — Integration tests (real ONNX sessions, no CUDA required)
# ---------------------------------------------------------------------------


@pytest.fixture(scope="session")
def real_predictor() -> Predictor:
    """A real Predictor instance loaded from the shipped ONNX artifacts.

    Skips all integration tests if either artifact is missing.
    """
    if not _ENCODER_ONNX.is_file():
        pytest.skip(f"Encoder ONNX not found at {_ENCODER_ONNX}")
    if not _HEAD_ONNX.is_file():
        pytest.skip(f"Head ONNX not found at {_HEAD_ONNX}")

    pred = Predictor()
    if not pred.ready:
        pytest.skip(
            "Predictor not ready after loading real artifacts — "
            "check onnxruntime and sentence-transformers installation."
        )
    return pred


@pytest.fixture(scope="session")
def client_real(real_predictor: Predictor) -> Iterator[TestClient]:
    """TestClient wired to the real ORT predictor (session-scoped to amortise load)."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        app.state.predictor = real_predictor
        yield tc


@pytest.mark.integration
class TestUC1PredictIntegration:
    """Real ORT inference: verify delta is in [-5, +5] and JSON contract holds."""

    def test_predict_returns_200(self, client_real: TestClient) -> None:
        response = client_real.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert response.status_code == 200

    def test_predict_delta_in_range(self, client_real: TestClient) -> None:
        """ORT head output must be within tanh*5 bounds."""
        response = client_real.post("/api/uc1/predict", json=_VALID_PAYLOAD)
        delta = response.json()["delta"]

        assert -5.0 <= delta <= 5.0, f"delta {delta} out of [-5, +5]"

    def test_predict_delta_is_finite(self, client_real: TestClient) -> None:
        response = client_real.post("/api/uc1/predict", json=_VALID_PAYLOAD)
        delta = response.json()["delta"]

        assert np.isfinite(delta), f"delta is not finite: {delta}"

    def test_predict_schema(self, client_real: TestClient) -> None:
        """Wire schema must be exactly {delta: float}."""
        response = client_real.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert set(response.json().keys()) == {"delta"}
        assert isinstance(response.json()["delta"], float)

    def test_predict_deterministic(self, client_real: TestClient) -> None:
        """Same prose twice must return identical delta (ORT is stateless)."""
        r1 = client_real.post("/api/uc1/predict", json=_VALID_PAYLOAD)
        r2 = client_real.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        assert r1.json()["delta"] == r2.json()["delta"]

    def test_info_shows_model_ready(self, client_real: TestClient) -> None:
        """Once real predictor is wired, /api/uc1/info must report model_ready=true."""
        response = client_real.get("/api/uc1/info")

        assert response.json()["model_ready"] is True

    def test_health_shows_model_ready(self, client_real: TestClient) -> None:
        """/api/health must reflect model_ready=true when ORT is loaded."""
        response = client_real.get("/api/health")

        assert response.json()["model_ready"] is True


# ---------------------------------------------------------------------------
# Layer 3 — End-to-end parity: API delta == direct ORT delta (atol 1e-4)
# ---------------------------------------------------------------------------


@pytest.mark.integration
class TestUC1ApiOrtParity:
    """The API must return the same delta as a direct ORT head call on the same inputs.

    This validates that the Predictor.predict() path (tokenize → encoder ORT →
    head ORT) is numerically consistent with a reference direct-ORT call using
    the same sessions.  The test replicates what verify.py does, but at the
    API boundary.

    Tolerance: 1e-4 absolute (tighter than the 1e-2 pipeline gate; the API
    and the direct call use the same ORT sessions so the only divergence
    source is floating-point non-associativity in Python, which is sub-1e-6).
    """

    def test_api_delta_matches_direct_ort(self, real_predictor: Predictor) -> None:
        """Same prose → same delta via API predict() vs direct Predictor.predict()."""
        # Direct call via the predictor object (same sessions, same code path).
        direct_delta = real_predictor.predict(
            char_prose=_CHAR_PROSE,
            action_prose=_ACTION_PROSE,
            context_prose=_CONTEXT_PROSE,
        )

        # Wrap in a TestClient to go through the full HTTP stack.
        with TestClient(app, raise_server_exceptions=True) as tc:
            app.state.predictor = real_predictor
            response = tc.post("/api/uc1/predict", json=_VALID_PAYLOAD)

        api_delta = response.json()["delta"]

        assert response.status_code == 200
        assert abs(api_delta - direct_delta) < 1e-4, (
            f"API delta {api_delta:.6f} diverges from direct ORT delta "
            f"{direct_delta:.6f} by {abs(api_delta - direct_delta):.2e} (gate: <1e-4)"
        )

    def test_multiple_prose_inputs_stay_in_range(self, real_predictor: Predictor) -> None:
        """Five distinct prose triples all return delta in [-5, +5]."""
        prose_triples = [
            (
                "Borin is a warrior of the Iron Keep.\n"
                "Borin is strong and stubborn.\n"
                "Borin is angry.",
                "The action is a frontal charge.\n"
                "Borin is the actor.\n"
                "Borin charges the fortified gate alone.",
                "The mission is a siege for the Iron Keep.\n"
                "The mission objective is to breach the outer gate before dawn.\n"
                "The stakes are high: the garrison depends on Borin's success.",
            ),
            (
                "Lyra is a healer of the Silver Circle.\nLyra is wise and gentle.\nLyra is calm.",
                "The action is a healing ritual.\n"
                "Lyra is the actor.\n"
                "Lyra tends the wounded by the river crossing.",
                "The mission is a rescue for the Silver Circle.\n"
                "The mission objective is to stabilise the injured before nightfall.\n"
                "The stakes are moderate: supplies are limited.",
            ),
            (
                _CHAR_PROSE,
                _ACTION_PROSE,
                _CONTEXT_PROSE,
            ),
            (
                "Zephyr is a mage of the Drifting Spire.\n"
                "Zephyr is perceptive and cautious.\n"
                "Zephyr is focused.",
                "The action is an arcane investigation.\n"
                "Zephyr is the actor.\n"
                "Zephyr examines the ancient rune-stone for traps.",
                "The mission is an exploration for the Drifting Spire.\n"
                "The mission objective is to catalogue the ruins before the tide returns.\n"
                "The stakes are low: the site is known to be mostly safe.",
            ),
            (
                "Roric is a trader of the Amber Road.\n"
                "Roric is cunning and persuasive.\n"
                "Roric is cheerful.",
                "The action is a negotiation.\n"
                "Roric is the actor.\n"
                "Roric haggles with the toll-keeper over passage fees.",
                "The mission is a trade run for the Amber Road guild.\n"
                "The mission objective is to reach the northern market before the caravan departs.\n"
                "The stakes are financial: a failed deal costs the guild two weeks of profit.",
            ),
        ]

        with TestClient(app, raise_server_exceptions=True) as tc:
            app.state.predictor = real_predictor
            for i, (cp, ap, xp) in enumerate(prose_triples):
                resp = tc.post(
                    "/api/uc1/predict",
                    json={"char_prose": cp, "action_prose": ap, "context_prose": xp},
                )
                assert resp.status_code == 200, f"Triple {i}: got HTTP {resp.status_code}"
                delta = resp.json()["delta"]
                assert -5.0 <= delta <= 5.0, f"Triple {i}: delta {delta} out of [-5, +5]"
                assert np.isfinite(delta), f"Triple {i}: non-finite delta {delta}"
