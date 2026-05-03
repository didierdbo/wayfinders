"""Shared fixtures for API-layer tests."""

from __future__ import annotations

from collections.abc import Iterator

import pytest
from fastapi.testclient import TestClient

from wayfinders.api.app import app


@pytest.fixture
def client() -> Iterator[TestClient]:
    """A FastAPI ``TestClient`` bound to the production app instance.

    Using a context manager triggers app startup/shutdown lifecycle,
    which is a no-op today but keeps the door open for lifespan hooks.
    """
    with TestClient(app) as test_client:
        yield test_client
