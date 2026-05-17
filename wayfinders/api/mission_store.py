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

Cap enforcement (§A.8.D3, 2026-05-17):
  ``MissionStore`` enforces a hard cap of ``RECRUIT_MISSION_CAP = 3`` active
  recruit missions.  Use ``try_add()`` when the cap matters (returns bool).
  ``add()`` is kept for backward-compat (idempotent replace semantics only —
  no cap check; callers that need cap enforcement must use ``try_add()``).
"""

from __future__ import annotations

import logging

from wayfinders.api.world_tick_models import EmergentMission

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Cap constant (§A.8.D3 Varn-lock 2026-05-17)
# ---------------------------------------------------------------------------

# Maximum number of simultaneously active recruit missions.
# One per roster entry (kira / dorn / vell). Server-authoritative.
# The client enforces defensively (drop + Error log on overflow).
RECRUIT_MISSION_CAP: int = 3


class MissionStore:
    """Session-scoped in-process store for active EmergentMissions.

    Not persistent across server restarts.  Mutations are driven by:
      - ``try_add(mission)`` — called by POST /api/world/tick when a mission
                               emerges; returns False if the recruit cap (N=3)
                               is already reached.
      - ``add(mission)``     — backward-compat wrapper (no cap check); use
                               only for test injection or manual bookkeeping.
      - ``remove(id)``       — called by POST /api/world/mission/resolve and /conclude.
      - ``accept(id)``       — called by POST /api/missions/<id>/accept. Marks the
                               mission accepted (stays in store with flag set).
      - ``active()``         — called by GET /api/missions/active.

    The store is keyed by mission id (UUID-shaped stable string).  Ordering
    for ``active()`` is by spawn tick ascending (``mission.seed`` encodes the
    tick implicitly, but we store the tick separately for clarity).

    Cap enforcement (§A.8.D3):
      ``RECRUIT_MISSION_CAP = 3`` recruit missions maximum.  ``try_add()``
      returns False (silent refusal) when the cap is already reached.  This is
      the server-authoritative gate; the client has a belt-and-braces guard that
      drops and logs at Error level if a 4th recruit mission somehow arrives.
    """

    def __init__(self) -> None:
        # Ordered insertion dict: mission_id → (spawn_tick, EmergentMission, accepted).
        # Python dicts preserve insertion order (3.7+); we maintain ascending
        # tick order by always inserting at the correct position via a sort on
        # retrieval (simpler than maintaining a sorted structure for M1 load).
        # accepted flag: True when the mission has been accepted via /accept endpoint.
        self._missions: dict[str, tuple[int, EmergentMission, bool]] = {}

    # ------------------------------------------------------------------
    # Capacity helpers
    # ------------------------------------------------------------------

    def _count_recruit_active(self) -> int:
        """Count currently active recruit missions (pending + accepted)."""
        return sum(1 for _, mission, _ in self._missions.values() if mission.type == "recruit")

    # ------------------------------------------------------------------
    # Mutation API
    # ------------------------------------------------------------------

    def try_add(self, mission: EmergentMission, spawn_tick: int) -> bool:
        """Record an emergent mission with cap enforcement.

        If the mission is of type "recruit" and ``RECRUIT_MISSION_CAP`` active
        recruit missions are already in the store, this is a **silent refusal**:
        the method returns False and the store is unchanged.

        Idempotent for the same mission_id: if the id already exists (retry),
        the existing entry is replaced and True is returned (not a cap violation).

        Args:
            mission:    The EmergentMission emitted by the emergence engine.
            spawn_tick: The tick at which the mission emerged.

        Returns:
            True  — mission recorded (or already present; idempotent replace).
            False — cap reached; mission was NOT added.
        """
        # Idempotent path: replacing an existing entry is always OK.
        if mission.id in self._missions:
            self._missions[mission.id] = (spawn_tick, mission, False)
            logger.debug(
                "mission_store: replaced existing mission %s (type=%s, tick=%d)",
                mission.id,
                mission.type,
                spawn_tick,
            )
            return True

        # Cap check for recruit type.
        if mission.type == "recruit":
            current = self._count_recruit_active()
            if current >= RECRUIT_MISSION_CAP:
                logger.warning(
                    "mission_store: cap=%d reached (%d active recruit missions) — "
                    "refused mission %s (npc=%s)",
                    RECRUIT_MISSION_CAP,
                    current,
                    mission.id,
                    mission.recruit_target_npc_id,
                )
                return False

        self._missions[mission.id] = (spawn_tick, mission, False)
        logger.debug(
            "mission_store: added mission %s (type=%s, tick=%d, target_poi=%s)",
            mission.id,
            mission.type,
            spawn_tick,
            mission.target_poi,
        )
        return True

    def add(self, mission: EmergentMission, spawn_tick: int) -> None:
        """Record an emergent mission without cap enforcement.

        Idempotent: if the mission id already exists, the existing entry is
        replaced (same tick, same mission — safe for retried tick requests).

        .. note::
            This method bypasses the recruit cap check.  Use ``try_add()``
            for the primary emission path.  ``add()`` is kept for test helpers
            and backward-compat with code that injected missions directly.

        Args:
            mission:    The EmergentMission emitted by the emergence engine.
            spawn_tick: The tick at which the mission emerged.  Used for
                        ascending-order retrieval in ``active()``.
        """
        self._missions[mission.id] = (spawn_tick, mission, False)
        logger.debug(
            "mission_store: add() (no-cap) mission %s (type=%s, tick=%d, target_poi=%s)",
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
            _, mission, _ = entry
            logger.debug(
                "mission_store: removed mission %s (type=%s)",
                mission_id,
                mission.type,
            )
            return True
        logger.debug("mission_store: remove called for unknown id %s (no-op)", mission_id)
        return False

    def accept(self, mission_id: str) -> bool:
        """Mark a mission as accepted (stays in store, flag flipped).

        Idempotent: if already accepted, returns True without changing state.

        Args:
            mission_id: Stable UUID-shaped id.

        Returns:
            True  — mission found and marked accepted (or was already accepted).
            False — mission_id not found.
        """
        entry = self._missions.get(mission_id)
        if entry is None:
            return False
        spawn_tick, mission, _accepted = entry
        self._missions[mission_id] = (spawn_tick, mission, True)
        logger.debug("mission_store: accepted mission %s", mission_id)
        return True

    def is_accepted(self, mission_id: str) -> bool:
        """Return True if the mission is present and has been accepted."""
        entry = self._missions.get(mission_id)
        if entry is None:
            return False
        _, _, accepted = entry
        return accepted

    def get(self, mission_id: str) -> EmergentMission | None:
        """Return the mission if present, else None."""
        entry = self._missions.get(mission_id)
        if entry is None:
            return None
        _, mission, _ = entry
        return mission

    def active(self) -> list[EmergentMission]:
        """Return all active missions ordered by spawn tick ascending.

        "Active" means present in the store — pending or accepted, not yet
        resolved or concluded.  Concluded missions are removed from the store
        by the /conclude and /resolve endpoints.

        Returns:
            Sorted list of EmergentMission objects.  Empty list when no
            missions are active.
        """
        sorted_entries = sorted(self._missions.values(), key=lambda e: e[0])
        return [mission for _, mission, _ in sorted_entries]

    def __len__(self) -> int:
        return len(self._missions)

    def clear(self) -> None:
        """Remove all missions (test helper / future New Game hook)."""
        self._missions.clear()

    # ------------------------------------------------------------------
    # NPC-targeting query (used by _pick_recruit_mission)
    # ------------------------------------------------------------------

    def active_recruit_npc_ids(self) -> frozenset[str]:
        """Return the set of NPC ids that already have an active recruit mission.

        Used by ``_pick_recruit_mission`` to skip already-targeted NPCs.

        Returns:
            Frozenset of npc_id strings (from recruit_target_npc_id field).
        """
        return frozenset(
            mission.recruit_target_npc_id
            for _, mission, _ in self._missions.values()
            if mission.type == "recruit" and mission.recruit_target_npc_id is not None
        )
