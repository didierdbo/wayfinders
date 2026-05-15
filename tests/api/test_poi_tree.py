"""Tests for ``GET /api/world/poi_tree``.

Varn-lock 2026-05-15 spec v2 §2.

Test categories:
  1. HTTP layer — 200, JSON content-type, valid JSON payload.
  2. Schema structure — version key present, node entries have required fields.
  3. M1 MVP content — Halfgate e1 node present; no e2/e3 nodes in MVP.
  4. Node field types — display_name str, layer int, parent str|null,
     children list.
  5. PoiId key format — all node keys pass PoiId regex validation.
  6. Cache — two calls return identical payloads (static manifest).
"""

from __future__ import annotations

import re

import pytest
from fastapi.testclient import TestClient
from pydantic import TypeAdapter

from wayfinders.api.app import app
from wayfinders.api.world_tick_models import _POI_ID_PATTERN, PoiId

_poi_id_adapter: TypeAdapter[PoiId] = TypeAdapter(PoiId)

# ---------------------------------------------------------------------------
# Fixture
# ---------------------------------------------------------------------------


@pytest.fixture
def client() -> TestClient:
    """Minimal TestClient — predictor state irrelevant for poi_tree tests."""
    with TestClient(app, raise_server_exceptions=True) as tc:
        yield tc


# ---------------------------------------------------------------------------
# 1. HTTP layer
# ---------------------------------------------------------------------------


class TestPoiTreeHttp:
    def test_returns_200(self, client: TestClient) -> None:
        response = client.get("/api/world/poi_tree")
        assert response.status_code == 200

    def test_content_type_is_json(self, client: TestClient) -> None:
        response = client.get("/api/world/poi_tree")
        assert response.headers["content-type"].startswith("application/json")

    def test_body_is_parseable_json(self, client: TestClient) -> None:
        response = client.get("/api/world/poi_tree")
        # Must not raise.
        payload = response.json()
        assert isinstance(payload, dict)


# ---------------------------------------------------------------------------
# 2. Schema structure
# ---------------------------------------------------------------------------


class TestPoiTreeSchema:
    def test_version_key_present(self, client: TestClient) -> None:
        """poi_tree.json must include a top-level 'version' key."""
        payload = client.get("/api/world/poi_tree").json()
        assert "version" in payload

    def test_version_is_int(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        assert isinstance(payload["version"], int)

    def test_at_least_one_node_besides_version(self, client: TestClient) -> None:
        """Payload must have at least one POI node (besides the version key)."""
        payload = client.get("/api/world/poi_tree").json()
        node_keys = [k for k in payload if k != "version"]
        assert len(node_keys) >= 1

    def test_nodes_have_required_fields(self, client: TestClient) -> None:
        """Each POI node must have display_name, layer, parent, children."""
        payload = client.get("/api/world/poi_tree").json()
        required = {"display_name", "layer", "parent", "children"}
        for key, node in payload.items():
            if key == "version":
                continue
            assert required.issubset(set(node.keys())), (
                f"Node {key!r} missing fields: {required - set(node.keys())}"
            )


# ---------------------------------------------------------------------------
# 3. M1 MVP content — Halfgate e1
# ---------------------------------------------------------------------------


class TestPoiTreeMvpContent:
    def test_halfgate_e1_node_present(self, client: TestClient) -> None:
        """MVP manifest must include e1.halfgate."""
        payload = client.get("/api/world/poi_tree").json()
        assert "e1.halfgate" in payload, "e1.halfgate node missing from poi_tree MVP"

    def test_halfgate_display_name(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        assert payload["e1.halfgate"]["display_name"] == "Halfgate"

    def test_halfgate_layer_is_1(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        assert payload["e1.halfgate"]["layer"] == 1

    def test_halfgate_parent_is_null(self, client: TestClient) -> None:
        """e1 root node has no parent."""
        payload = client.get("/api/world/poi_tree").json()
        assert payload["e1.halfgate"]["parent"] is None

    def test_halfgate_children_is_list(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        assert isinstance(payload["e1.halfgate"]["children"], list)

    def test_no_e2_or_e3_nodes_in_mvp(self, client: TestClient) -> None:
        """M1 MVP: only e1 nodes.  e2/e3 will be added in M2+ via Varn ratification."""
        payload = client.get("/api/world/poi_tree").json()
        for key in payload:
            if key == "version":
                continue
            assert key.startswith("e1."), (
                f"MVP should only have e1 nodes, found {key!r}. "
                "Add e2/e3 nodes via Varn ratification in M2+."
            )


# ---------------------------------------------------------------------------
# 4. Node field types
# ---------------------------------------------------------------------------


class TestPoiTreeNodeTypes:
    def test_display_name_is_str(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        for key, node in payload.items():
            if key == "version":
                continue
            assert isinstance(node["display_name"], str), f"Node {key!r}: display_name must be str"

    def test_layer_is_int(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        for key, node in payload.items():
            if key == "version":
                continue
            assert isinstance(node["layer"], int), f"Node {key!r}: layer must be int"

    def test_layer_in_valid_range(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        for key, node in payload.items():
            if key == "version":
                continue
            assert 1 <= node["layer"] <= 3, (
                f"Node {key!r}: layer={node['layer']} must be 1, 2, or 3"
            )

    def test_parent_is_str_or_null(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        for key, node in payload.items():
            if key == "version":
                continue
            assert node["parent"] is None or isinstance(node["parent"], str), (
                f"Node {key!r}: parent must be str or null"
            )

    def test_children_is_list_of_str(self, client: TestClient) -> None:
        payload = client.get("/api/world/poi_tree").json()
        for key, node in payload.items():
            if key == "version":
                continue
            children = node["children"]
            assert isinstance(children, list), f"Node {key!r}: children must be list"
            for child in children:
                assert isinstance(child, str), f"Node {key!r}: child {child!r} must be str"


# ---------------------------------------------------------------------------
# 5. PoiId key format
# ---------------------------------------------------------------------------


class TestPoiTreeKeyFormat:
    def test_all_node_keys_are_valid_poi_ids(self, client: TestClient) -> None:
        """Every key in the poi_tree (except 'version') must be a valid PoiId."""
        payload = client.get("/api/world/poi_tree").json()
        for key in payload:
            if key == "version":
                continue
            # Must not raise.
            try:
                _poi_id_adapter.validate_python(key)
            except Exception as exc:
                pytest.fail(f"poi_tree key {key!r} is not a valid PoiId: {exc}")

    def test_all_node_keys_match_poi_id_pattern(self, client: TestClient) -> None:
        """Cross-check: all node keys match the raw regex."""
        payload = client.get("/api/world/poi_tree").json()
        for key in payload:
            if key == "version":
                continue
            assert re.match(_POI_ID_PATTERN, key), (
                f"poi_tree key {key!r} does not match PoiId pattern {_POI_ID_PATTERN!r}"
            )

    def test_layer_consistent_with_key_prefix(self, client: TestClient) -> None:
        """The 'layer' field must be consistent with the e{N} prefix of the key."""
        payload = client.get("/api/world/poi_tree").json()
        for key, node in payload.items():
            if key == "version":
                continue
            expected_layer = int(key[1])  # 'e1.halfgate' → 1
            assert node["layer"] == expected_layer, (
                f"Node {key!r}: layer={node['layer']} but key prefix implies {expected_layer}"
            )


# ---------------------------------------------------------------------------
# 6. Cache behaviour — two calls return identical payloads
# ---------------------------------------------------------------------------


class TestPoiTreeCache:
    def test_two_calls_return_identical_payload(self, client: TestClient) -> None:
        """Static manifest: two requests must return bit-identical payloads."""
        r1 = client.get("/api/world/poi_tree").json()
        r2 = client.get("/api/world/poi_tree").json()
        assert r1 == r2

    def test_version_is_consistent_across_calls(self, client: TestClient) -> None:
        r1 = client.get("/api/world/poi_tree").json()
        r2 = client.get("/api/world/poi_tree").json()
        assert r1["version"] == r2["version"]
