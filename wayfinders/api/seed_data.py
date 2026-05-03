"""Static seed data for the HTTP boundary.

Seed values are mirrored from the Godot client's
``data/units/{scout,warden,archer}.tres`` resources so the API and the
client agree on the roster during Phase 3 of the Godot client rollout.

Persistence (database, content pipeline, hot reload) lands in a later
PR; for now the seed is a frozen tuple at module import time.
"""

from __future__ import annotations

from wayfinders.api.models import Unit

UNITS: tuple[Unit, ...] = (
    Unit(display_name="Scout", max_hp=8, base_speed=6, portrait="icon.svg"),
    Unit(display_name="Warden", max_hp=16, base_speed=3, portrait="icon.svg"),
    Unit(display_name="Archer", max_hp=6, base_speed=5, portrait="icon.svg"),
)
