"""HTTP boundary for the wayfinders game-logic layer.

This package exposes the game-logic rule engine over HTTP so that
language-agnostic clients (Godot+C# now, Java multiplayer service later)
can call in without coupling to Python types.

The FastAPI app is built in :mod:`wayfinders.api.app`. Run it with::

    uv run uvicorn wayfinders.api.app:app --reload
"""

from wayfinders.api.app import app

__all__ = ["app"]
