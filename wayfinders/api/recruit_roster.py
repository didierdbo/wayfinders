"""Varn-locked recruit roster for M1 (§A.8.D3, 2026-05-17).

This module defines the closed lookup ``M1_RECRUIT_ROSTER`` and the frozen
``M1_EMITTED_TYPES`` set.  Both are Varn-locked — do not edit without a Varn
ratification step.

``M1_RECRUIT_ROSTER`` maps recruitable NPCs to their home Halfgate district.
The roster order is the designed narrative arc for the M1 tutorial:
  Kira (scout archetype, gateway) → Dorn (civic figure, intramuros)
  → Vell (dockyard outsider, littoral).
The order is deterministic by spec — not tunable without Varn sign-off.

``M1_EMITTED_TYPES`` freezes M1 to a single mission type (recruit).
``scout_route`` and ``parley_local`` remain in ``EmergenceMissionType`` for
M2+ use; they are excluded from the M1 picker by this frozenset.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Final


@dataclass(frozen=True)
class RecruitRosterEntry:
    """A recruitable NPC entry with their home district and portrait key.

    Fields:
        npc_id:         Canonical NPC identifier (matches NpcCatalog on client).
        home_district:  E2 Halfgate district slug where the recruit mission lands.
        portrait_key:   Asset resolver key for the NPC's portrait.  Used by the
                        client tooltip and recruit panel (E2.3 milestone).

    Varn-locked: field additions require Varn ratification.
    """

    npc_id: str
    home_district: str
    portrait_key: str


# ---------------------------------------------------------------------------
# Varn-locked roster (§A.8.D3, 2026-05-17)
# DO NOT edit npc_id, home_district, portrait_key, or roster ORDER without
# a Varn ratification step. The order is the designed narrative arc (M1 tutorial):
#   1. Kira  — scout archetype, gateway (peripheral commerce/traffic district)
#   2. Dorn  — civic figure, intramuros (civic core, guild ledger)
#   3. Vell  — dockyard outsider, littoral (coastal/dockyard district, new M1 addition)
# ---------------------------------------------------------------------------

M1_RECRUIT_ROSTER: Final[tuple[RecruitRosterEntry, ...]] = (
    RecruitRosterEntry(
        npc_id="kira",
        home_district="gateway",
        portrait_key="e4.portrait.kira.calm",
    ),
    RecruitRosterEntry(
        npc_id="dorn",
        home_district="intramuros",
        portrait_key="e4.portrait.dorn.calm",
    ),
    RecruitRosterEntry(
        npc_id="vell",
        home_district="littoral",
        portrait_key="e4.portrait.vell.calm",
    ),
)

# ---------------------------------------------------------------------------
# M1 emitted types — frozen to "recruit" only.
# scout_route and parley_local are M2+ only; they stay in the EmergenceMissionType
# Literal but are excluded from the M1 picker by this frozenset.
# Restore: M2 widens this frozenset and re-enables MISSION_TYPE_DISTRICT_AFFINITY.
# ---------------------------------------------------------------------------

M1_EMITTED_TYPES: Final[frozenset[str]] = frozenset({"recruit"})
