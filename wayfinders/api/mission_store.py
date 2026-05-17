"""In-process mission store for ``GET /api/missions/active``.

The Wayfinders backend is stateless by architecture (NPC-autonomy lock
2026-05-09): the authoritative ``GameState.PendingMissions`` lives on the
Godot+C# client.  The server holds no persistence across restarts.

However, for M1 the client needs ``GET /api/missions/active`` to bootstrap
its ``MissionStore`` autoload on startup (Varn-lock 2026-05-17 §A).  This
module provides a *session-scoped* (in-process, non-persistent) store that:

  - Records each ``EmergentMission`` emitted by ``POST /api/world/tick``.
  - Removes missions that have been resolved or concluded via
    ``POST /api/world/mission/resolve`` or ``POST /api/world/mission/conclude``.
  - Returns active (non-resolved) missions ordered by spawn tick ascending
    from ``GET /api/missions/active``.

State scope:
  - Per-process only.  Server restart clears all missions.  This matches the
    M1 "no save layer" invariant — the client is the authoritative store; the
    server store is a convenience mirror for the ``/active`` query.
  - Thread-safety: the FastAPI server runs single-process single-thread in
    dev mode (uvicorn --workers 1).  No locking is applied.  M2 (multi-worker)
    will require a shared backend (Redis or Postgres) — track as deferred work.

``MissionStore`` is instantiated once on ``app.state.mission_store`` at
lifespan startup.  All endpoints access it via ``request.app.state.mission_store``.
"""

from __future__ import annotations

import logging

from wayfinders.api.world_tick_models import EmergentMission

logger = logging.getLogger(__name__)


class MissionStore:
    """Session-scoped in-process store for active EmergentMissions.

    Not persistent across server restarts.  Mutations are driven by:
      - ``add(mission)``    — called by POST /api/world/tick when a mission emerges.
      - ``remove(id)``      — called by POST /api/world/mission/resolve and /conclude.
      - ``active()``        — called by GET /api/missions/active.

    The store is keyed by mission id (UUID-shaped stable string).  Ordering
    for ``active()`` is by spawn tick ascending (``mission.seed`` encodes the
    tick implicitly, but we store the tick separately for clarity).
    """

    def __init__(self) -> None:
        # Ordered insertion dict: mission_id → (spawn_tick, EmergentMission).
        # Python dicts preserve insertion order (3.7+); we maintain ascending
        # tick order by always inserting at the correct position via a sort on
        # retrieval (simpler than maintaining a sorted structure for M1 load).
        self._missions: dict[str, tuple[int, EmergentMission]] = {}

    def add(self, mission: EmergentMission, spawn_tick: int) -> None:
        """Record an emergent mission in the store.

        Idempotent: if the mission id already exists, the existing entry is
        replaced (same tick, same mission — safe for retried tick requests).

        Args:
            mission:    The EmergentMission emitted by the emergence engine.
            spawn_tick: The tick at which the mission emerged.  Used for
                        ascending-order retrieval in ``active()``.
        """
        self._missions[mission.id] = (spawn_tick, mission)
        logger.debug(
            "mission_store: added mission %s (type=%s, tick=%d, target_poi=%s)",
            mission.id,
            mission.type,
            spawn_tick,
            mission.target_poi,
        )

    def remove(self, mission_id: str) -> bool:
        """Remove a mission from the active store (resolved or concluded).

        Args:
            mission_id: Stable UUID-shaped id.

        Returns:
            True if the mission was found and removed; False if not present
            (idempotent — safe to call on already-removed ids).
        """
        entry = self._missions.pop(mission_id, None)
        if entry is not None:
            _, mission = entry
            logger.debug(
                "mission_store: removed mission %s (type=%s)",
                mission_id,
                mission.type,
            )
            return True
        logger.debug("mission_store: remove called for unknown id %s (no-op)", mission_id)
        return False

    def active(self) -> list[EmergentMission]:
        """Return all active missions ordered by spawn tick ascending.

        "Active" means present in the store — not yet resolved or concluded.
        The ordering is by spawn tick ascending (deterministic; ties broken by
        insertion order which tracks UUID hash uniqueness).

        Returns:
            Sorted list of EmergentMission objects.  Empty list when no
            missions are active.
        """
        sorted_entries = sorted(self._missions.values(), key=lambda e: e[0])
        return [mission for _, mission in sorted_entries]

    def __len__(self) -> int:
        return len(self._missions)

    def clear(self) -> None:
        """Remove all missions (test helper / future New Game hook)."""
        self._missions.clear()
