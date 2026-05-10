"""Mission-emergence logic for ``POST /api/world/tick``.

Implements the M1 cadence-gated emergence algorithm described in
Varn's scope doc (2026-05-10 §6) and Tess's tech decisions (§2):

  1. Cadence gate: a mission window opens every ``_CADENCE_MIN`` to
     ``_CADENCE_MAX`` ticks (default 5-10), determined deterministically
     from ``seed``.  Ticks outside the window return ``None``.

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
     template; ``eligible_personas`` is always ``[]`` in M1 (stub,
     flagged for Varn ratification before Etape 3).

All randomness is seeded from the caller-provided ``seed`` — the server
holds no global RNG state.  Same ``(tick, seed, context_prose)`` always
produces the same mission.

OPEN — Varn-flagged items:
  - ``eligible_personas`` filter (DEX/WIS tag match) not spec'd in detail.
    Stub is ``[]`` until Varn ratifies the filter rule.
  - ``region`` is derived from ``context_prose`` by a simple heuristic in M1.
    M2 will pass it explicitly from WorldState.
  - The proxy logit derivation is a M1 approximation.  A proper multi-class
    head (Varn §5 "bolt-on or sibling") is M2 scope.
"""

from __future__ import annotations

import hashlib
import logging
import random
import uuid
from typing import TYPE_CHECKING

from wayfinders.api.world_tick_models import (
    EmergenceMissionType,
    EmergentMission,
    WorldTickRequest,
    WorldTickResponse,
)

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


def _extract_region_from_prose(context_prose: str) -> str:
    """Derive a region label from context_prose for M1.

    M1 heuristic: look for a capitalised proper noun after "in ", "near ",
    "at " — common patterns in the Varn context renderer output.
    Falls back to ``"the region"`` if no match is found.

    This is a heuristic stub.  In M2, region will be passed explicitly
    as a field in WorldState, removing this inference entirely.
    """
    import re

    # Look for "in <Title Case word>" / "near <Title Case word>" / "at <Title Case word>"
    # The region prose renders names like "Halfgate", "Ridgepass", "Ironshore".
    pattern = r"\b(?:in|near|at)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)"
    matches: list[str] = re.findall(pattern, context_prose)
    if matches:
        return matches[0]
    return "the region"


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
    the seed.  The window is open when ``tick % interval == 0`` (first tick
    is always a window since ``0 % anything == 0``).

    Determinism: same (tick, seed) → same result.
    """
    rng = random.Random(seed ^ 0xDEAD_BEEF)  # domain-separate from mission sampling
    interval = rng.randint(_CADENCE_MIN, _CADENCE_MAX)
    # tick 0 is always a window (first tick bootstraps the chain).
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

        # --- 1. Cadence gate ---
        if not _in_cadence_window(tick, seed):
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
        region = _extract_region_from_prose(context_prose)
        mission = EmergentMission(
            id=_stable_mission_id(tick, seed),
            type=mission_type,
            narrative_hook=_render_narrative_hook(mission_type, region),
            eligible_personas=[],  # M1 stub — flagged for Varn (Etape 3)
            difficulty="mid",  # forced M1 (sampler disabled per spec §6)
            region=region,
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
