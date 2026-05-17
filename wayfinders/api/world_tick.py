"""Mission-emergence logic for ``POST /api/world/tick``.

Implements the M1 cadence-gated emergence algorithm described in
Varn's scope doc (2026-05-10 §6) and Tess's tech decisions (§2):

  1. Cadence gate: a mission window opens every ``_CADENCE_MIN`` to
     ``_CADENCE_MAX`` ticks (default 5-10), determined deterministically
     from ``seed``.  Tick 1 is an unconditional first window (Varn-lock
     2026-05-17 §A.8.D2) — guarantees ≥1 mission before E2 entry in
     BootScene → E1 → click POI → E2 flow.  Ticks outside the window
     return ``None``.

  2. M1 recruit-only mode (Varn-lock 2026-05-17 §A.8.D3):
     In M1 only ``"recruit"`` missions are emitted.  The old softmax over
     ``{scout_route, parley_local, no_emergence}`` is **NOT used** in the
     M1 pipeline — it is preserved in-module for M2 when those types
     re-enter the picker.  The M1 path calls ``_pick_recruit_mission()``
     directly.

  3. M1 recruit picker: ``_pick_recruit_mission()`` selects the next NPC
     from ``M1_RECRUIT_ROSTER`` who does not yet have an active mission in
     the store.  Returns ``None`` if all 3 NPCs are already targeted (cap
     hit).  Order is deterministic: roster declaration sequence
     (kira → dorn → vell) — this is the designed tutorial arc, not RNG.

  4. Mission struct assembly: ``id`` is a UUID4 stable per
     ``(tick, seed)``; ``narrative_hook`` is a server-side f-string
     template; ``eligible_personas`` is computed by
     ``filter_eligible_personas()`` (Varn-lock 2026-05-10 §2).

  5. POI targeting: missions emit ``e2.{region}.{district}`` derived from
     the NPC's home district in ``M1_RECRUIT_ROSTER``.  Only halfgate is
     e2-enabled in M1.

All randomness is seeded from the caller-provided ``seed`` — the server
holds no global RNG state.  Same ``(tick, seed, context_prose, active_npcs)``
always produces the same mission.

Dev flag: ``WAYFINDERS_DEV_MISSION_SEED`` env var.  When set, the cadence
gate is bypassed (any tick is a window).  Free-form string for M1.

OPEN — Varn-flagged items:
  - ``region`` is derived from ``context_prose`` by a heuristic in M1 that
    normalises matches against the Varn-locked RegionId closed lookup and
    falls back to seeded uniform sampling when no match is found.
    M2 will pass it explicitly from WorldState.
  - ``MISSION_TYPE_DISTRICT_AFFINITY`` and the softmax/sampling pipeline are
    **retained but NOT used in M1**.  They will be restored in M2 when
    ``scout_route`` and ``parley_local`` re-enter the picker.
  - Multi-region E2 extension (M2): add entries to VISIBLE_E2_DISTRICTS_BY_REGION
    for brescaille, fendelune, veillemont, roches-closes when their E2 scenes land.
  - Narrative hook templates for recruit type are M1 stubs (f-string server-side).
    Mira-authored Jinja2 templates land in M2 (narrative_hooks_recruit_*.j2).
"""

from __future__ import annotations

import hashlib
import logging
import os
import random
import uuid
from collections.abc import Iterable
from typing import TYPE_CHECKING

from wayfinders.api.recruit_roster import M1_RECRUIT_ROSTER, RecruitRosterEntry
from wayfinders.api.world_tick_models import (
    REGION_IDS,
    EmergenceMissionType,
    EmergentMission,
    PersonaId,
    PoiId,
    RegionId,
    WorldTickRequest,
    WorldTickResponse,
)
from wayfinders.ml.schemas.character import CharacterState
from wayfinders.ml.schemas.vocabularies import DescriptorBucket

if TYPE_CHECKING:
    from wayfinders.api.predictor import Predictor

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Cadence constants (M1 spec: 1 mission every 5-10 ticks)
# ---------------------------------------------------------------------------

_CADENCE_MIN: int = 5
_CADENCE_MAX: int = 10

# ---------------------------------------------------------------------------
# Mission classes (Varn-locked)
# ---------------------------------------------------------------------------

# M2+ types — retained in code but NOT emitted in M1 (excluded by M1_EMITTED_TYPES).
# Restored in M2 when the picker widens back to scout_route + parley_local.
_MISSION_TYPES_M2: list[EmergenceMissionType] = ["scout_route", "parley_local"]

# M1 emitted type (single element list; kept for symmetry with M2 multi-type lists).
_MISSION_TYPES: list[EmergenceMissionType] = ["recruit"]

# Prose stubs used when calling the regression head as a proxy classifier.
# These are EN-pinned, minimal, structurally valid for the encoder.
# They must never appear in training data as real rollout prose.
_PROXY_CHAR_PROSE = (
    "An unnamed traveller of the Wayfinders company.\nSteady and unremarkable.\nComposed."
)
_PROXY_ACTION_PROSE = (
    "The action is a survey.\n"
    "The traveller is the actor.\n"
    "The traveller assesses the situation and reports."
)

# ---------------------------------------------------------------------------
# Narrative hook templates (M1 f-string server-side; Varn §9.4)
# ---------------------------------------------------------------------------

_NARRATIVE_HOOKS: dict[EmergenceMissionType, str] = {
    "scout_route": "A matter of {type_display} in {region} — the road ahead is uncharted.",
    "parley_local": "A matter of {type_display} in {region} — a local contact seeks audience.",
}

_TYPE_DISPLAY: dict[EmergenceMissionType, str] = {
    "scout_route": "route scouting",
    "parley_local": "local parley",
}


def _render_narrative_hook(mission_type: EmergenceMissionType, region: str) -> str:
    """Render the M1 f-string narrative hook for a mission type and region."""
    template = _NARRATIVE_HOOKS[mission_type]
    return template.format(type_display=_TYPE_DISPLAY[mission_type], region=region)


# Recruit narrative hook templates (M1 stub; Mira-authored Jinja2 templates land in M2).
# Pratchett-tonal register: dry observation, not heroic announcement.
# DO NOT change without Varn/Mira ratification — these appear in the E2 tooltip.
_RECRUIT_HOOKS: dict[str, str] = {
    "kira": (
        "A gate-watcher who knows everyone's business — "
        "and knows which business is worth knowing about."
    ),
    "dorn": (
        "A man behind a desk who knows whose ledger is short "
        "and whose signature is worth more than the ink."
    ),
    "vell": (
        "Someone at the dockside who has seen every ship come in "
        "and every promise sail back out again."
    ),
}


def _render_recruit_hook(npc_id: str) -> str:
    """Return the M1 narrative hook for a recruit NPC.

    Falls back to a generic template if the NPC id is not in the hook dict
    (forward-compat guard for M2+ NPCs; M1 roster always matches).
    """
    return _RECRUIT_HOOKS.get(
        npc_id,
        "A potential recruit in the area — worth a conversation.",
    )


# ---------------------------------------------------------------------------
# M1 recruit mission picker (§A.8.D3, Varn-lock 2026-05-17)
# ---------------------------------------------------------------------------


def _pick_recruit_mission(
    region: RegionId,
    already_targeted_npc_ids: frozenset[str],
    tick: int,
    seed: int,
) -> EmergentMission | None:
    """Pick the next recruit mission to emit for M1.

    Deterministic order: iterates ``M1_RECRUIT_ROSTER`` in declaration sequence
    (kira → dorn → vell) and picks the first NPC not yet targeted by an active
    mission.  This is the designed M1 tutorial arc — no RNG.

    Returns ``None`` when all 3 NPCs in the roster already have an active
    recruit mission (cap = 3 hit, §A.8.D3).

    POI targeting:
      - If the region has visible E2 districts (halfgate in M1), the target_poi
        is ``e2.{region}.{npc.home_district}`` — the NPC's home district anchors
        the mission to their district on the E2 map.
      - If the region has no visible E2 districts (non-halfgate regions in M1),
        the target_poi falls back to ``e1.{region}`` — same e1 fallback used by
        the old scout_route/parley_local picker for non-halfgate contexts.

    Args:
        region:                   The current world region (RegionId).
        already_targeted_npc_ids: NPC ids that already have an active mission.
                                  Comes from ``MissionStore.active_recruit_npc_ids()``.
        tick:                     Current world tick (for stable mission id).
        seed:                     Caller-provided tick seed (for stable mission id).

    Returns:
        A new ``EmergentMission`` of type ``"recruit"``, or ``None`` if capped.

    Implementation note:
        ``MISSION_TYPE_DISTRICT_AFFINITY`` and ``_pick_target_poi`` are NOT called
        here.  In M1, the district is derived directly from the NPC's
        ``home_district`` in the roster.  ``MISSION_TYPE_DISTRICT_AFFINITY`` is
        retained in module scope for M2 restore when scout_route/parley_local
        re-enter the picker.
    """
    candidates: list[RecruitRosterEntry] = [
        entry for entry in M1_RECRUIT_ROSTER if entry.npc_id not in already_targeted_npc_ids
    ]
    if not candidates:
        logger.debug(
            "world_tick: _pick_recruit_mission — all %d NPCs already targeted, cap hit",
            len(M1_RECRUIT_ROSTER),
        )
        return None

    # Deterministic: first candidate in roster declaration order.
    pick = candidates[0]

    # POI targeting: e2 layer for regions with visible districts, e1 fallback otherwise.
    visible_districts = VISIBLE_E2_DISTRICTS_BY_REGION.get(region, ())
    if visible_districts:
        # NPC home_district is a valid slug for this region's E2 map.
        target_poi: PoiId = f"e2.{region}.{pick.home_district}"
    else:
        # Non-halfgate region in M1: fall back to e1 layer (same as old scout/parley picker).
        target_poi = f"e1.{region}"

    return EmergentMission(
        id=_stable_mission_id(tick, seed),
        type="recruit",
        narrative_hook=_render_recruit_hook(pick.npc_id),
        eligible_personas=(),  # M1: recruit does not gate on personas
        difficulty="mid",
        region=region,
        target_poi=target_poi,
        deadline_ticks=None,
        outcome=None,
        seed=seed,
        recruit_target_npc_id=pick.npc_id,
    )


# ---------------------------------------------------------------------------
# Region extraction (M1 heuristic — M2 will pass region from WorldState)
# ---------------------------------------------------------------------------


def _extract_region_from_prose(context_prose: str, seed: int = 0) -> RegionId:
    """Derive a canonical RegionId from context_prose for M1.

    M1 heuristic: look for a word (or hyphenated word) after "in ", "near ",
    "at " and attempt a case-insensitive match against the Varn-locked
    RegionId closed lookup.

    Matching rules:
      1. Extract candidate tokens after "in/near/at" (Title Case or lowercase).
      2. Normalise to lowercase, strip trailing punctuation.
      3. Match against REGION_IDS (e.g. "Halfgate" → "halfgate").
      4. If no match, fall back to seeded uniform sampling over REGION_IDS
         (deterministic: same seed → same fallback region).

    Fallback is seeded from the caller-provided seed (domain-separated with
    0xF11_BEEF) so that the same (context_prose, seed) pair always yields
    the same region — even when the prose contains no recognisable region name.

    This is a M1 heuristic stub.  In M2, region will be passed explicitly
    as a field in WorldState, removing this inference entirely.

    Args:
        context_prose: EN-rendered context prose from the Varn renderer.
        seed:          Caller-provided tick seed (for deterministic fallback).

    Returns:
        A valid ``RegionId`` from the Varn-locked closed lookup.
    """
    import re

    # Capture a single token (possibly hyphenated) after in/near/at.
    # Handles "Halfgate", "Roches-Closes", "roches-closes", etc.
    # A space-separated second word is NOT captured — multi-word region names
    # in this lookup are hyphenated (roches-closes), not space-separated.
    pattern = r"\b(?:in|near|at)\s+([A-Za-z][A-Za-z]*(?:-[A-Za-z]+)*)"
    matches: list[str] = re.findall(pattern, context_prose)

    for raw in matches:
        normalised = raw.lower().strip()
        if normalised in REGION_IDS:
            # REGION_IDS membership check is sufficient for mypy to narrow str → RegionId.
            return normalised

    # No recognisable region in prose — seeded uniform fallback.
    rng = random.Random(seed ^ 0xF11_BEEF)
    chosen: RegionId = rng.choice(REGION_IDS)
    logger.debug(
        "world_tick: no region matched in prose — seeded fallback → %r (seed=%d)",
        chosen,
        seed,
    )
    return chosen


# ---------------------------------------------------------------------------
# Stable mission ID: deterministic from (tick, seed)
# ---------------------------------------------------------------------------


def _stable_mission_id(tick: int, seed: int) -> str:
    """Generate a deterministic UUID4-shaped id from (tick, seed).

    Uses SHA-256 of the byte-packed (tick, seed) pair, takes the first 16
    bytes, and formats as a UUID.  This ensures the same (tick, seed) always
    produces the same id — audit trail preserved across retries.
    """
    raw = hashlib.sha256(
        tick.to_bytes(8, "little") + seed.to_bytes(8, "little", signed=True)
    ).digest()[:16]
    return str(uuid.UUID(bytes=raw))


# ---------------------------------------------------------------------------
# Cadence gate: is this tick inside a mission window?
# ---------------------------------------------------------------------------


def _in_cadence_window(tick: int, seed: int) -> bool:
    """Return True if tick falls inside a cadence window.

    The window interval is sampled from [_CADENCE_MIN, _CADENCE_MAX] using
    the seed.  The window is open when ``tick % interval == 0``.

    Varn-lock 2026-05-17 (Decision 3): tick 1 is an unconditional first window.
    This guarantees ≥1 mission before E2 entry in the BootScene → E1 → click
    POI Halfgate → E2 flow.  Tick 0 is structurally pre-game in the Godot
    lifecycle; tick 1 is the first deterministic post-_Ready frame.

    Default cadence (5-10 ticks) resumes from tick 2 onwards, unchanged from
    M1 spec lock 2026-05-10.

    Determinism: same (tick, seed) → same result.
    """
    # Varn-lock 2026-05-17: tick 1 is an unconditional first window.
    if tick == 1:
        return True
    rng = random.Random(seed ^ 0xDEAD_BEEF)  # domain-separate from mission sampling
    interval = rng.randint(_CADENCE_MIN, _CADENCE_MAX)
    # tick 0 is always a window (0 % anything == 0).
    return tick % interval == 0


# ---------------------------------------------------------------------------
# Proxy logits from regression delta (M1 approximation)
# ---------------------------------------------------------------------------


def _logits_from_delta(delta: float) -> dict[str, float]:
    """Map a UC1 regression delta ∈ [-5, +5] → three-class proxy logits.

    M1 approximation (flagged — see module docstring).  The regression head
    was trained to predict action resolution quality, not mission type.  We
    reuse it as a proxy:

      - delta > 0  → context favours active engagement → scout_route up
      - delta < 0  → context favours social/diplomatic → parley_local up
      - delta ≈ 0  → no strong signal → no_emergence up (but M1 fallback
                      overrides this within the cadence window)

    The mapping is:
      scout_route   logit =  delta  (positive delta boosts scouting)
      parley_local  logit = -delta  (negative delta boosts parlaying)
      no_emergence  logit = -abs(delta) * 0.5 + 0.5  (flat near zero)

    This gives a sensible ordering without requiring a real classifier head.
    """
    return {
        "scout_route": delta,
        "parley_local": -delta,
        "no_emergence": -abs(delta) * 0.5 + 0.5,
    }


def _softmax(logits: dict[str, float]) -> dict[str, float]:
    """Numerically stable softmax over a dict of logits."""
    import math

    max_l = max(logits.values())
    exps = {k: math.exp(v - max_l) for k, v in logits.items()}
    total = sum(exps.values())
    return {k: v / total for k, v in exps.items()}


def _sample_class(probs: dict[str, float], rng: random.Random) -> str:
    """Sample a class label from a probability dict using the provided RNG."""
    classes = list(probs.keys())
    weights = [probs[c] for c in classes]
    return rng.choices(classes, weights=weights, k=1)[0]


# ---------------------------------------------------------------------------
# Eligible-persona filter (Varn-lock 2026-05-10 §2)
# ---------------------------------------------------------------------------

# Canonical order of DescriptorBucket values — used for ±1 window arithmetic.
# Varn-lock 2026-04-30 / 2026-05-10: tirets, not underscores.
_BUCKET_ORDER: tuple[DescriptorBucket, ...] = ("very-low", "low", "mid", "high", "very-high")
_BUCKET_INDEX: dict[DescriptorBucket, int] = {b: i for i, b in enumerate(_BUCKET_ORDER)}

# Mapping from EmergenceMissionType to the CharacterState attribute it interrogates.
# Varn-lock 2026-05-10 §2 table — any extension goes through Varn.
_MISSION_STAT_LANE: dict[EmergenceMissionType, str] = {
    "scout_route": "dex_bucket",
    "parley_local": "wis_bucket",
}


def filter_eligible_personas(
    mission_type: EmergenceMissionType,
    difficulty_bucket: DescriptorBucket,
    company_personas: Iterable[CharacterState],
) -> tuple[PersonaId, ...]:
    """Return PersonaIds eligible for ``mission_type`` at ``difficulty_bucket``.

    Eligibility rule (Varn-lock 2026-05-10 §2): a persona is eligible iff
    its stat bucket for the mission's stat-lane is within ±1 of
    ``difficulty_bucket`` in canonical order
    ``very-low < low < mid < high < very-high``.

    Args:
        mission_type:       ``scout_route`` or ``parley_local`` (M1 closed lookup).
        difficulty_bucket:  Difficulty of the emerging mission.
        company_personas:   Iterable of CharacterState snapshots (from the
                            Godot client via WorldTickRequest.company_personas).

    Returns:
        Tuple of PersonaId strings, sorted ascending, deduplicated, deterministic.

    Raises:
        KeyError:   If ``mission_type`` is not in the M1 stat-lane mapping.
                    (Forward-compat guard — unknown future types fail loudly.)
        ValueError: If a persona's stat bucket is missing or invalid.
                    (Schema contract: dex_bucket / wis_bucket are non-optional
                    per Varn-lock 2026-04-30; corruption → hard fail, not skip.)
    """
    # KeyError is intentional if mission_type is not mapped (see docstring).
    stat_attr = _MISSION_STAT_LANE[mission_type]

    diff_idx = _BUCKET_INDEX[difficulty_bucket]
    window_min = max(0, diff_idx - 1)
    window_max = min(len(_BUCKET_ORDER) - 1, diff_idx + 1)

    seen: set[PersonaId] = set()
    eligible: list[PersonaId] = []

    for persona in company_personas:
        persona_id: PersonaId = persona.name  # PersonaId is the persona's name in M1

        # Deduplication: first occurrence wins (stable order = GameState.compagnie order).
        if persona_id in seen:
            continue
        seen.add(persona_id)

        # Hard fail on missing/invalid bucket — schema contract violation.
        raw_bucket = getattr(persona, stat_attr, None)
        if raw_bucket is None:
            raise ValueError(
                f"Persona {persona_id!r}: attribute {stat_attr!r} is None — "
                f"expected a DescriptorBucket (Varn-lock 2026-04-30)."
            )
        if raw_bucket not in _BUCKET_INDEX:
            raise ValueError(
                f"Persona {persona_id!r}: {stat_attr}={raw_bucket!r} is not a valid "
                f"DescriptorBucket. Valid values: {list(_BUCKET_ORDER)}."
            )

        # raw_bucket is a valid DescriptorBucket — membership confirmed by the check above.
        persona_idx = _BUCKET_INDEX[raw_bucket]
        if window_min <= persona_idx <= window_max:
            eligible.append(persona_id)

    # Deterministic: PersonaId ascending (Varn-lock 2026-05-10 §3).
    return tuple(sorted(eligible))


# ---------------------------------------------------------------------------
# PoiId helpers — layer-and-district-aware picker (Varn-lock 2026-05-17)
# ---------------------------------------------------------------------------

# Varn-locked 2026-05-17 (Decision 1 §A.2).
# Maps RegionId → ordered tuple of district slugs visible as E2 markers in M1.
# M1 cap: only halfgate has E2-visible districts (3 districts, E2.1c lock).
# Extension to other regions is M2 scope (add an entry per region when the
# corresponding E2 scene lands).
# DO NOT edit the slug set or their order without a Varn ratification step.
VISIBLE_E2_DISTRICTS_BY_REGION: dict[str, tuple[str, ...]] = {
    "halfgate": ("intramuros", "gateway", "littoral"),
    # brescaille, fendelune, veillemont, roches-closes: M2 scope.
}

# Varn-locked 2026-05-17 (Decision 1 §A.2, Q1 override: ORDER is locked).
# Maps EmergenceMissionType → ordered preference over district slugs.
# The ORDER is the affinity distribution (narrative grounding):
#   scout_route  favours peripheral/transit zones → gateway first.
#   parley_local favours civic/named zones → intramuros first.
# Tone-tuning belongs in narrative_hook prose, not in target selection.
# DO NOT reorder or change slug set without a Varn ratification step.
MISSION_TYPE_DISTRICT_AFFINITY: dict[str, tuple[str, ...]] = {
    "scout_route": ("gateway", "littoral", "intramuros"),
    "parley_local": ("intramuros", "gateway", "littoral"),
}


def _pick_target_poi(
    mission_type: EmergenceMissionType,
    region: RegionId,
    seed: int,
) -> PoiId:
    """Pick the layer-and-district-aware target_poi for an emergent mission.

    Rule (Varn-lock 2026-05-17 §A):
      - If the region has visible E2 districts, pick one from the
        mission_type's affinity list (intersected with the visible set),
        seeded deterministically from ``seed``.
        Returns ``e2.{region}.{slug}``.
      - Otherwise (M1: any region other than halfgate), fall back to
        ``e1.{region}``.

    Determinism: same (seed, mission_type, region) → same target_poi.
    The affinity order is the distribution anchor; the seeded RNG breaks
    ties when multiple districts are eligible.

    Args:
        mission_type: Varn-locked EmergenceMissionType.
        region:       Varn-locked RegionId.
        seed:         Caller-provided tick seed (domain-separated internally).

    Returns:
        A valid PoiId at the appropriate layer.
    """
    visible = VISIBLE_E2_DISTRICTS_BY_REGION.get(region, ())
    if not visible:
        # M1: region has no E2 markers — cap at e1 layer.
        return f"e1.{region}"
    affinity = MISSION_TYPE_DISTRICT_AFFINITY[mission_type]
    eligible = tuple(d for d in affinity if d in visible)
    # Domain-separate from cadence and sampling RNGs.
    rng = random.Random(seed ^ 0xE2_AFF1)
    chosen = rng.choice(eligible)
    return f"e2.{region}.{chosen}"


def _region_to_poi_id(region: RegionId) -> PoiId:
    """Convert a Varn-locked RegionId to its e1 PoiId equivalent.

    Kept for backward compatibility with ``mission_resolve.py`` which
    reconstructs the target_poi from a ``RegionId`` on the wire.  Resolution
    and conclude payloads carry ``region`` for backward compat (Varn-lock
    2026-05-10); the resolved PoiId from the original emergent mission is not
    resent on the wire.

    New code should call ``_pick_target_poi`` instead.

    Args:
        region: A valid RegionId from the Varn-locked closed lookup.

    Returns:
        A valid PoiId string, e.g. ``e1.halfgate`` for ``halfgate``.
    """
    return f"e1.{region}"


# ---------------------------------------------------------------------------
# Public API: EmergenceEngine
# ---------------------------------------------------------------------------


class EmergenceEngine:
    """Stateless mission-emergence engine for ``POST /api/world/tick``.

    M1 mode: emits recruit-only missions via ``_pick_recruit_mission()``
    (§A.8.D3, Varn-lock 2026-05-17).  The UC1 Predictor is NOT used for
    mission-type selection in M1; the softmax/sampling pipeline is preserved
    for M2 when scout_route/parley_local re-enter the picker.

    In M1, the engine requires access to the ``MissionStore`` to check which
    NPCs are already targeted (cap enforcement).  Pass it via ``process_tick()``.

    Args:
        predictor: The process-level Predictor instance (from app.state).
                   Not used for mission-type selection in M1.  Kept for
                   eligibility-persona filtering (filter_eligible_personas).
                   May be None or not-ready; degrades gracefully.
    """

    def __init__(self, predictor: Predictor | None = None) -> None:
        self._predictor = predictor

    def process_tick(
        self,
        request: WorldTickRequest,
        already_targeted_npc_ids: frozenset[str] | None = None,
    ) -> WorldTickResponse:
        """Process a world tick and return an emergence response.

        M1 algorithm (§A.8.D3, Varn-lock 2026-05-17):
          1. Cadence gate — if not in window, return ``mission=None``.
             Dev flag: if WAYFINDERS_DEV_MISSION_SEED is set, bypass.
          2. Derive region from prose (M1 heuristic).
          3. Call ``_pick_recruit_mission()`` — returns None if cap hit.
          4. Return the mission (or None if capped).

        The old softmax/sampling pipeline (steps 2-4 in the original
        docstring) is NOT used in M1.  It is preserved in-module for M2.

        Args:
            request:                 Validated WorldTickRequest from HTTP layer.
            already_targeted_npc_ids: NPC ids with active recruit missions.
                                     Comes from MissionStore.active_recruit_npc_ids().
                                     Defaults to empty frozenset when called
                                     without a store (test compatibility).

        Returns:
            WorldTickResponse with mission set or None.
        """
        tick = request.tick
        seed = request.seed
        context_prose = request.context_prose

        if already_targeted_npc_ids is None:
            already_targeted_npc_ids = frozenset()

        # --- Dev seed override (WAYFINDERS_DEV_MISSION_SEED) ---
        # When set: bypass cadence gate, use the env-var string as an
        # additional seed component for reproducible dev/visual-regression
        # work.  The value is hashed to an integer via SHA-256 to avoid
        # assuming it is numeric (free-form M1 convention per Varn-lock
        # 2026-05-17 Q4).
        dev_seed_raw = os.environ.get("WAYFINDERS_DEV_MISSION_SEED", "")
        dev_seed_active = bool(dev_seed_raw)
        if dev_seed_active:
            # Mix the env-var string into the seed for deterministic output.
            # Mask to int64 range so _stable_mission_id (signed=True 8-byte pack)
            # does not overflow regardless of the env-var content.
            dev_seed_bytes = hashlib.sha256(dev_seed_raw.encode()).digest()[:8]
            dev_seed_int = int.from_bytes(dev_seed_bytes, "little") & 0x7FFF_FFFF_FFFF_FFFF
            seed = (seed ^ dev_seed_int) & 0x7FFF_FFFF_FFFF_FFFF
            logger.debug(
                "world_tick: WAYFINDERS_DEV_MISSION_SEED=%r active — cadence bypassed, "
                "seed mixed to %d",
                dev_seed_raw,
                seed,
            )

        # --- 1. Cadence gate ---
        in_window = dev_seed_active or _in_cadence_window(tick, seed)
        if not in_window:
            logger.debug("world_tick: tick=%d seed=%d — outside cadence window", tick, seed)
            return WorldTickResponse(mission=None, tick=tick)

        # --- 2. Derive region (M1 heuristic; M2 will receive it from WorldState) ---
        region = _extract_region_from_prose(context_prose, seed=seed)

        # --- 3. M1 recruit picker (§A.8.D3) ---
        # NOTE: MISSION_TYPE_DISTRICT_AFFINITY and _pick_target_poi are NOT called here.
        # In M1 the district is derived from the NPC's home_district in M1_RECRUIT_ROSTER.
        # MISSION_TYPE_DISTRICT_AFFINITY is retained for M2 restore (scout/parley types).
        mission = _pick_recruit_mission(
            region=region,
            already_targeted_npc_ids=already_targeted_npc_ids,
            tick=tick,
            seed=seed,
        )

        if mission is None:
            # All 3 NPCs already targeted — cap hit, no new mission this window.
            logger.debug(
                "world_tick: tick=%d seed=%d — recruit cap hit, no mission emitted",
                tick,
                seed,
            )
            return WorldTickResponse(mission=None, tick=tick)

        logger.info(
            "world_tick: tick=%d seed=%d → mission %s type=%s npc=%s region=%s poi=%s",
            tick,
            seed,
            mission.id,
            mission.type,
            mission.recruit_target_npc_id,
            mission.region,
            mission.target_poi,
        )

        return WorldTickResponse(mission=mission, tick=tick)

    # ------------------------------------------------------------------
    # Internal helpers (M2 softmax pipeline — NOT used in M1)
    # ------------------------------------------------------------------

    def _get_logits(self, context_prose: str, seed: int) -> dict[str, float]:
        """Return three-class proxy logits from the predictor or uniform fallback.

        NOT called in M1 (recruit-only, no logit-based type selection).
        Preserved for M2 restore of scout_route / parley_local picker.

        If the predictor is not ready, returns uniform logits (each class
        equally likely).  This allows the endpoint to function during
        development before the ONNX artifacts are available.
        """
        predictor = self._predictor
        if predictor is None or not predictor.ready:
            logger.warning(
                "world_tick: predictor not ready — using uniform logits (M2 degraded mode)"
            )
            return {"scout_route": 0.0, "parley_local": 0.0, "no_emergence": 0.0}

        try:
            # M2 approximation: use regression head as a proxy classifier.
            # Context-only call: char/action stubs are minimal valid EN prose.
            # The delta encodes the context's "action quality" signal, which
            # we map to mission-type logits via _logits_from_delta.
            delta = predictor.predict(
                char_prose=_PROXY_CHAR_PROSE,
                action_prose=_PROXY_ACTION_PROSE,
                context_prose=context_prose,
            )
            return _logits_from_delta(delta)
        except Exception as exc:
            logger.error(
                "world_tick: predictor.predict() failed: %s — falling back to uniform", exc
            )
            return {"scout_route": 0.0, "parley_local": 0.0, "no_emergence": 0.0}
