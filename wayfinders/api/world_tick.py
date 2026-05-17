"""Mission-emergence logic for ``POST /api/world/tick``.

Implements the M1 cadence-gated emergence algorithm described in
Varn's scope doc (2026-05-10 §6) and Tess's tech decisions (§2):

  1. Cadence gate: a mission window opens every ``_CADENCE_MIN`` to
     ``_CADENCE_MAX`` ticks (default 5-10), determined deterministically
     from ``seed``.  Tick 1 is an unconditional first window (Varn-lock
     2026-05-17) — guarantees ≥1 mission before E2 entry in BootScene →
     E1 → click POI → E2 flow.  Ticks outside the window return ``None``.

  2. Softmax over ``{scout_route, parley_local, no_emergence}`` via the
     Predictor (UC1 encoder → concat → MLP head — same backbone).

     M1 approximation: the UC1 head is a regression head (→ delta ∈ [-5,+5]);
     we do not yet have a classification head.  Instead we derive three
     proxy logits from a single predict() call:
       - Run predict() with ``context_prose`` only (char/action stubs).
       - Map the scalar delta → three-class logits (see ``_logits_from_delta``).
     This is explicitly a proxy for M1 and is flagged as technical debt
     for Etape 3 / M2 where a proper classification head will be trained.

  3. M1 no_emergence fallback: if the sampled class is ``no_emergence``
     but we are inside the cadence window, fall back to
     ``argmax({scout_route, parley_local})`` — ensures the end-to-end
     chain always produces a mission within the window.

  4. Mission struct assembly: ``id`` is a UUID4 stable per
     ``(tick, seed)``; ``narrative_hook`` is a server-side f-string
     template; ``eligible_personas`` is computed by
     ``filter_eligible_personas()`` (Varn-lock 2026-05-10 §2).

  5. POI targeting: missions now emit e2.{region}.{district} for regions
     with visible E2 districts (Varn-lock 2026-05-17).  Only halfgate is
     e2-enabled in M1; other regions fall back to e1.{region}.

All randomness is seeded from the caller-provided ``seed`` — the server
holds no global RNG state.  Same ``(tick, seed, context_prose)`` always
produces the same mission.

Dev flag: ``WAYFINDERS_DEV_MISSION_SEED`` env var.  When set, the cadence
gate is bypassed (any tick is a window) and the seed from the var is used
for deterministic mission-type + district selection.  Free-form string for
M1; closed preset lookup may be added at M2.

OPEN — Varn-flagged items:
  - ``region`` is derived from ``context_prose`` by a heuristic in M1 that
    normalises matches against the Varn-locked RegionId closed lookup and
    falls back to seeded uniform sampling when no match is found.
    M2 will pass it explicitly from WorldState.
  - The proxy logit derivation is a M1 approximation.  A proper multi-class
    head (Varn §5 "bolt-on or sibling") is M2 scope.
  - Multi-region E2 extension (M2): add entries to VISIBLE_E2_DISTRICTS_BY_REGION
    for brescaille, fendelune, veillemont, roches-closes when their E2 scenes land.
"""

from __future__ import annotations

import hashlib
import logging
import os
import random
import uuid
from collections.abc import Iterable
from typing import TYPE_CHECKING

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
# Mission classes (Varn-locked 2026-05-10)
# ---------------------------------------------------------------------------

_MISSION_TYPES: list[EmergenceMissionType] = ["scout_route", "parley_local"]

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

    Uses the UC1 Predictor as a proxy classifier (M1 approximation).
    All state is derived from the request; the engine holds no mutable
    state between calls.

    Args:
        predictor: The process-level Predictor instance (from app.state).
                   May be None or not-ready; the engine degrades gracefully
                   (uniform random over mission types) in that case.
    """

    def __init__(self, predictor: Predictor | None = None) -> None:
        self._predictor = predictor

    def process_tick(self, request: WorldTickRequest) -> WorldTickResponse:
        """Process a world tick and return an emergence response.

        Algorithm:
          1. Cadence gate — if not in window, return ``mission=None``.
             Dev flag: if WAYFINDERS_DEV_MISSION_SEED is set, bypass the
             cadence gate (any tick is a window).
          2. Get proxy logits via predictor (or uniform if not ready).
          3. Softmax + seed-seeded sampling.
          4. M1 fallback: if sampled == no_emergence, use argmax of
             {scout_route, parley_local}.
          5. Assemble EmergentMission struct and return.

        Args:
            request: Validated WorldTickRequest from the HTTP layer.

        Returns:
            WorldTickResponse with mission set or None.
        """
        tick = request.tick
        seed = request.seed
        context_prose = request.context_prose

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

        # --- 2. Proxy logits ---
        logits = self._get_logits(context_prose, seed)

        # --- 3. Softmax + sampling ---
        probs = _softmax(logits)
        rng = random.Random(seed ^ 0xCAFE_BABE)  # domain-separate from cadence RNG
        sampled = _sample_class(probs, rng)

        logger.debug(
            "world_tick: tick=%d seed=%d probs=%s sampled=%s",
            tick,
            seed,
            {k: f"{v:.3f}" for k, v in probs.items()},
            sampled,
        )

        # --- 4. M1 no_emergence fallback ---
        if sampled == "no_emergence":
            # Within the window we always emit a mission (M1 spec §6).
            # Fall back to argmax of the two active mission types.
            mission_probs = {mt: probs[mt] for mt in _MISSION_TYPES}
            sampled = max(mission_probs, key=lambda k: mission_probs[k])
            logger.debug("world_tick: tick=%d — no_emergence fallback → %s", tick, sampled)

        mission_type: EmergenceMissionType = sampled  # type: ignore[assignment]
        # Narrow type: sampled is always in _MISSION_TYPES after fallback.
        assert mission_type in _MISSION_TYPES, f"unexpected mission type: {mission_type!r}"

        # --- 5. Assemble mission ---
        region = _extract_region_from_prose(context_prose, seed=seed)
        # Varn-lock 2026-05-17: use _pick_target_poi (e2 layer for halfgate,
        # e1 fallback for other regions in M1).
        target_poi = _pick_target_poi(mission_type, region, seed)
        difficulty: DescriptorBucket = "mid"  # forced M1 (sampler disabled per spec §6)
        eligible = filter_eligible_personas(
            mission_type=mission_type,
            difficulty_bucket=difficulty,
            company_personas=request.company_personas,
        )
        mission = EmergentMission(
            id=_stable_mission_id(tick, seed),
            type=mission_type,
            narrative_hook=_render_narrative_hook(mission_type, region),
            eligible_personas=eligible,
            difficulty=difficulty,
            region=region,
            target_poi=target_poi,
            deadline_ticks=None,  # M1 — no timer
            outcome=None,  # set at resolution, not at emergence
            seed=seed,
        )

        logger.info(
            "world_tick: tick=%d seed=%d → mission %s type=%s region=%s",
            tick,
            seed,
            mission.id,
            mission.type,
            mission.region,
        )

        return WorldTickResponse(mission=mission, tick=tick)

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _get_logits(self, context_prose: str, seed: int) -> dict[str, float]:
        """Return three-class proxy logits from the predictor or uniform fallback.

        If the predictor is not ready, returns uniform logits (each class
        equally likely).  This allows the endpoint to function during
        development before the ONNX artifacts are available.
        """
        predictor = self._predictor
        if predictor is None or not predictor.ready:
            logger.warning(
                "world_tick: predictor not ready — using uniform logits (M1 degraded mode)"
            )
            return {"scout_route": 0.0, "parley_local": 0.0, "no_emergence": 0.0}

        try:
            # M1 approximation: use regression head as a proxy classifier.
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
