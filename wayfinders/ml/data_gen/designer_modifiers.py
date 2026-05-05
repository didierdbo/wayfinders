"""Designer modifier table -> label_delta.

Implements Varn's UC1 Designer Modifier Table v0.5 (2026-05-05). The label
is **authored**, not learned. The model learns to compress the designer's
intent into a scalar over prose-derived vectors. Designers stay in the
driver's seat (Pax 2026-05-02 sec. 4).

Pipeline (Varn §4):

    delta_char    = sum of Character Δ-components (§1)
                    with sub-clamps on traits / conditions / legacy
    delta_action  = sum of Action Δ-components (§2)  -- no sub-clamps
    delta_context = sum of Context Δ-components (§3) -- no sub-clamps
    raw_delta     = delta_char + delta_action + delta_context
    label_Δ       = 5 * tanh(raw_delta / 5)            -- bounded [-5, +5]

All functions are **pure**: same inputs -> same float, forever. No global
state mutated across calls. (The ``UNKNOWN_VERBS`` set is a debugging
appender ; it does not affect any return value.)
"""

from __future__ import annotations

import math
from typing import Final

from wayfinders.ml.data_gen import designer_modifiers_data as tbl
from wayfinders.ml.schemas import (
    ActionCard,
    CampaignState,
    CharacterState,
    OppositionState,
    PartyState,
    SceneState,
    WorldState,
)
from wayfinders.ml.schemas.vocabularies import (
    DescriptorBucket,
)

# delta output range (locked to Pax sec. 3).
DELTA_MIN: Final[float] = -5.0
DELTA_MAX: Final[float] = 5.0

# Debugging appender -- populated when compute_delta sees an action_phrase
# not in tbl.ACTION_BASE_DIFFICULTY. Inspectable from tests / scripts.
# Mutating this set does NOT affect any compute_delta return value.
UNKNOWN_VERBS: set[str] = set()


# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------


def _clamp(value: int, lo: int, hi: int) -> int:
    """Clamp ``value`` into ``[lo, hi]`` inclusive."""
    return max(lo, min(hi, value))


def _tier_lookup(value: int, tiers: tuple[tuple[int, int], ...]) -> int:
    """Walk ``tiers`` (descending lower-bound) and return the first matching delta.

    ``tiers`` is a tuple of ``(lower_bound_inclusive, delta)`` ordered from
    highest threshold to lowest. A value below all thresholds returns 0.
    """
    for threshold, delta in tiers:
        if value >= threshold:
            return delta
    return 0


def _substring_match_sum(text: str, keyword_table: dict[str, int] | None = None) -> int:
    """Sum deltas for every keyword that appears in ``text`` (lowercased).

    Substring matching is **longest-first** to avoid 'wet' eating 'well-fed'.
    Each keyword fires at most once per text, even if it appears multiple
    times -- intentional (designer counts a status once).

    The ``keyword_table`` argument is required ; it defaults to ``None`` only
    so callers can pass it positionally without a name collision. Passing
    ``None`` raises a ``TypeError`` immediately.
    """
    if keyword_table is None:  # pragma: no cover -- defensive
        raise TypeError("_substring_match_sum requires a keyword_table")
    haystack = text.lower()
    # Sort keys longest-first so 'leather jerkin' matches before 'leather'.
    keys = sorted(keyword_table.keys(), key=len, reverse=True)
    consumed_spans: list[tuple[int, int]] = []
    total = 0
    for key in keys:
        idx = haystack.find(key)
        if idx == -1:
            continue
        end = idx + len(key)
        # Skip if this match is contained inside an already-consumed span
        # (e.g. 'leather' inside 'leather jerkin').
        if any(s_lo <= idx and end <= s_hi for s_lo, s_hi in consumed_spans):
            continue
        consumed_spans.append((idx, end))
        total += keyword_table[key]
    return total


# ---------------------------------------------------------------------------
# §1 -- Character delta
# ---------------------------------------------------------------------------


def _char_descriptor_delta(char: CharacterState, action: ActionCard) -> int:
    """§1.1 -- DEX / WIS / STR lane contributions, action-keyed."""
    verb = action.action_phrase.lower()
    dex: DescriptorBucket = char.dex_bucket
    wis: DescriptorBucket = char.wis_bucket
    str_: DescriptorBucket = char.str_bucket

    delta = 0
    if verb in {"stealth approach", "ranged attack"}:
        delta += tbl.DEX_DELTA_FULL[dex]
        if verb == "stealth approach":
            delta += tbl.WIS_DELTA_STEALTH[wis]
    elif verb == "move":
        delta += tbl.DEX_DELTA_MOVE[dex]
    elif verb == "melee attack":
        delta += tbl.STR_DELTA_MELEE[str_]
    elif verb == "signal to ally":
        delta += tbl.WIS_DELTA_SIGNAL[wis]
    elif verb == "wait":
        # Wait tests nothing actively (Varn §1.1).
        pass
    # Unknown verbs silently contribute 0 ; the unknown-verb hook is in
    # _action_base_delta so we don't double-log.
    return delta


def _char_traits_delta(char: CharacterState, action: ActionCard) -> int:
    """§1.2 -- traits sub-sum, clamped to [-3, +3]."""
    verb = action.action_phrase.lower()
    table = tbl.TRAIT_DELTA_STEALTH if verb != "melee attack" else tbl.TRAIT_DELTA_MELEE
    raw = sum(table.get(t.lower(), 0) for t in char.traits)
    return _clamp(raw, *tbl.TRAITS_SUBCLAMP)


def _char_status_delta(char: CharacterState) -> int:
    """§1.3 -- HP + stress (no sub-clamp ; each is a single bucket)."""
    return tbl.HP_DELTA[char.hp_bucket] + tbl.STRESS_DELTA[char.stress_bucket]


def _char_conditions_delta(char: CharacterState) -> int:
    """§1.3 -- conditions sub-sum, clamped to [-3, +3]."""
    if not char.conditions:
        return 0
    blob = " ".join(char.conditions).lower()
    raw = _substring_match_sum(blob, dict(tbl.CONDITION_KEYWORD_DELTA))
    return _clamp(raw, *tbl.CONDITIONS_SUBCLAMP)


def _char_equipment_delta(char: CharacterState, action: ActionCard) -> int:
    """§1.4 -- equipment, no sub-clamp (Varn says final tanh absorbs).

    Currently keyed for stealth ; melee/ranged inherit the same table
    because the dominant signals (armor weight, weapon size) trade off
    similarly across verbs in v0.5. v1 will split per-verb.
    """
    if not char.equipment:
        return 0
    blob = " ".join(char.equipment).lower()
    return _substring_match_sum(blob, dict(tbl.EQUIPMENT_KEYWORD_DELTA_STEALTH))


def _char_legacy_delta(char: CharacterState, action: ActionCard) -> int:
    """§1.5 -- legacy biography. Sub-clamped to [-3, +4] per Varn rationale."""
    raw = 0

    # §1.5.1 -- tag-aligned competence (action_counters keyed by phrase).
    # Codebase fixture uses 'stealth approaches' (plural) ; we try a few
    # canonical pluralizations.
    counter_key_candidates = [
        action.action_phrase + "es",
        action.action_phrase + "s",
        action.action_phrase + "ing",
        action.action_phrase,
    ]
    counters: tuple[int, int] = (0, 0)
    for k in counter_key_candidates:
        if k in char.action_counters:
            counters = char.action_counters[k]
            break
    successes, failures = counters
    raw += _tier_lookup(successes, tbl.STEALTH_SUCCESS_TIERS)
    raw += _tier_lookup(failures, tbl.STEALTH_FAILURE_TIERS)

    # §1.5.2 -- terrain familiarity. The codebase represents this as a
    # qualitative dict (location -> "barely"|"moderately"|"well"|"best of
    # all"). For UC1 stealth on broken-stone-and-scree, the relevant key
    # is loosely 'forest terrain' / 'mountain' ; we take the **best-matching
    # familiarity tier across all locations** as the contribution. This is
    # an adapter compromise vs. Varn's hours-bucketing -- documented for v1.
    if char.location_familiarity:
        best = max(tbl.TERRAIN_FAMILIARITY_DELTA[fam] for fam in char.location_familiarity.values())
        # Only count if the action is terrain-sensitive (stealth, move).
        if action.action_phrase.lower() in {"stealth approach", "move"}:
            raw += best

    # §1.5.3 -- profession hours (scouting and adjacent transfer).
    scouting_hours = char.profession_hours.get("scouting", 0)
    raw += _tier_lookup(scouting_hours, tbl.SCOUTING_PROFESSION_TIERS)
    for prof in tbl.ADJACENT_PROFESSIONS_STEALTH:
        if char.profession_hours.get(prof, 0) >= tbl.ADJACENT_PROFESSION_TRANSFER_THRESHOLD:
            raw += tbl.ADJACENT_PROFESSION_TRANSFER_DELTA
            break  # one transfer max

    return _clamp(raw, *tbl.LEGACY_SUBCLAMP)


def _char_episodic_delta(char: CharacterState, action: ActionCard) -> int:
    """§1.6 -- episodic memory (capped at +1)."""
    if not char.episodic_memory:
        return 0
    # Top-3 by salience (the renderer already ranks ; we trust the input).
    blob = " ".join(e.description for e in char.episodic_memory[:3]).lower()
    if any(kw in blob for kw in tbl.EPISODIC_KEYWORD_HITS) and any(
        t in {"stealth", "scouting", "infiltration"} for t in action.tag_set
    ):
        return min(tbl.EPISODIC_HIT_DELTA, tbl.EPISODIC_MAX_DELTA)
    return 0


def _delta_char(char: CharacterState, action: ActionCard) -> int:
    """§1 aggregate: descriptors + traits-clamped + status + conditions-clamped
    + equipment + legacy-clamped + episodic + bonds-zero.
    """
    return (
        _char_descriptor_delta(char, action)
        + _char_traits_delta(char, action)
        + _char_status_delta(char)
        + _char_conditions_delta(char)
        + _char_equipment_delta(char, action)
        + _char_legacy_delta(char, action)
        + _char_episodic_delta(char, action)
        # Bonds = 0 for UC1 solo stealth (Varn §1.7).
    )


# ---------------------------------------------------------------------------
# §2 -- Action delta
# ---------------------------------------------------------------------------


def _action_base_delta(action: ActionCard) -> int:
    """§2.1 -- base difficulty per verb."""
    verb = action.action_phrase.lower()
    if verb not in tbl.ACTION_BASE_DIFFICULTY:
        UNKNOWN_VERBS.add(verb)
        return 0
    return tbl.ACTION_BASE_DIFFICULTY[verb]


def _action_manner_delta(action: ActionCard) -> int:
    """§2.2 + §2.3 + §2.4 -- footing + lighting + noise."""
    verb = action.action_phrase.lower()
    delta = 0
    if verb == "move":
        delta += tbl.FOOTING_DELTA_MOVE[action.footing_phrase]
    else:
        # Stealth-style footing weighting (full magnitude).
        delta += tbl.FOOTING_DELTA_STEALTH[action.footing_phrase]
    delta += tbl.TIME_OF_DAY_DELTA_STEALTH[action.time_phrase]
    delta += tbl.NOISE_DELTA_STEALTH[action.noise_phrase]
    return delta


def _action_opposition_delta(opposition: OppositionState) -> int:
    """§2.7 -- opposition vignette (count + alertness + skill + family)."""
    if opposition.count <= 0:
        # Solo action -- opposition row contributes 0 (sentence dropped at render).
        return 0
    return (
        _tier_lookup(opposition.count, tbl.OPPOSITION_COUNT_TIERS)
        + tbl.OPPOSITION_ALERTNESS_DELTA[opposition.alertness]
        + tbl.OPPOSITION_SKILL_DELTA[opposition.skill_tier]
        + _substring_match_sum(opposition.family.lower(), dict(tbl.FAMILY_KEYWORD_DELTA))
    )


def _delta_action(action: ActionCard, opposition: OppositionState) -> int:
    """§2 aggregate. No sub-clamps -- final tanh absorbs."""
    return (
        _action_base_delta(action)
        + _action_manner_delta(action)
        + _action_opposition_delta(opposition)
    )


# ---------------------------------------------------------------------------
# §3 -- Context delta
# ---------------------------------------------------------------------------


def _context_stakes_delta(scene: SceneState) -> int:
    return tbl.STAKES_DELTA[scene.stakes_tier]


def _context_party_delta(party: PartyState) -> int:
    """§3.2. If party has no members, contributes 0 (no condition row rendered)."""
    if not party.members:
        return 0
    return (
        tbl.WOUND_DELTA[party.wound_bucket]
        + tbl.SUPPLY_DELTA[party.supply_bucket]
        + tbl.FATIGUE_DELTA[party.fatigue_bucket]
    )


def _context_weather_delta(world: WorldState) -> int:
    """§3.4 -- only temperature + wind. Precip handled via action manner."""
    return (
        tbl.TEMPERATURE_DELTA[world.temperature]
        + tbl.WIND_DELTA_STEALTH[world.wind]
        + tbl.PRECIPITATION_DELTA[world.precipitation]  # all zero by design
    )


def _context_terrain_delta(world: WorldState) -> int:
    """§3.5 -- biome only. Footing handled in action manner."""
    return tbl.TERRAIN_PRIMARY_DELTA[world.terrain_primary]


def _context_faction_delta(world: WorldState) -> int:
    """§3.6 -- relation against the **first** faction stance.

    The renderer caps at 2 stances ; the table only nominates a single relation
    per scene (the actor's stance toward the opposition's faction). We pick
    the first stance after the renderer's alphabetical ordering as the
    canonical-against-target. v1 will introduce an explicit
    target_faction field on ActionCard to remove this ambiguity.
    """
    if not world.faction_stances:
        return 0
    first = sorted(world.faction_stances, key=lambda s: s.other_faction)[0]
    return tbl.FACTION_RELATION_DELTA[first.relation]


def _context_campaign_delta(campaign: CampaignState) -> int:
    """§3.7 -- keyword on holdings_phrase."""
    return _substring_match_sum(campaign.holdings_phrase.lower(), dict(tbl.CAMPAIGN_KEYWORD_DELTA))


def _context_time_delta(scene: SceneState) -> int:
    """§3.8 -- countdown_hours."""
    if scene.countdown_hours is None:
        return tbl.COUNTDOWN_NONE_DELTA
    return _tier_lookup(scene.countdown_hours, tbl.COUNTDOWN_TIERS)


def _delta_context(
    scene: SceneState,
    party: PartyState,
    world: WorldState,
    campaign: CampaignState,
) -> int:
    return (
        _context_stakes_delta(scene)
        + _context_party_delta(party)
        + _context_weather_delta(world)
        + _context_terrain_delta(world)
        + _context_faction_delta(world)
        + _context_campaign_delta(campaign)
        + _context_time_delta(scene)
    )


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


def compute_delta(
    character: CharacterState,
    action: ActionCard,
    opposition: OppositionState,
    scene: SceneState,
    party: PartyState,
    world: WorldState,
    campaign: CampaignState,
) -> float:
    """Apply Varn's UC1 Designer Modifier Table v0.5 to a structured rollout.

    Returns ``label_delta`` ∈ [-5.0, +5.0]. Pure function -- same inputs ->
    same float, forever.

    The full pipeline is documented in module docstring + Varn §4. Briefly:

        raw = delta_char + delta_action + delta_context
        label_delta = 5 * tanh(raw / 5)

    The intermediate sums and the raw_delta are recoverable via the
    private ``_delta_*`` helpers if a debug overlay or unit test wants
    to introspect contributions.
    """
    raw = (
        _delta_char(character, action)
        + _delta_action(action, opposition)
        + _delta_context(scene, party, world, campaign)
    )
    label = tbl.TANH_OUTPUT_SCALE * math.tanh(raw / tbl.TANH_INPUT_SCALE)
    # Belt-and-braces clamp: tanh is bounded by construction, but float
    # arithmetic could nudge ±5.0 by 1 ULP -- keep the contract literal.
    return max(DELTA_MIN, min(DELTA_MAX, label))


def compute_raw_delta(
    character: CharacterState,
    action: ActionCard,
    opposition: OppositionState,
    scene: SceneState,
    party: PartyState,
    world: WorldState,
    campaign: CampaignState,
) -> int:
    """Return the pre-tanh ``raw_delta`` (sum of all Δ-components).

    Useful for tests and the debug overlay (Rune). Not used in training.
    """
    return (
        _delta_char(character, action)
        + _delta_action(action, opposition)
        + _delta_context(scene, party, world, campaign)
    )


def compute_label(rollout: object) -> float:
    """Backwards-compatible wrapper accepting a ``Rollout`` (Pydantic).

    Kept so downstream code that already calls ``compute_label(rollout)``
    keeps working. Internal callers should prefer ``compute_delta`` with
    explicit arguments for clarity.
    """
    # Lazy import to avoid a circular dep with rollouts.py.
    from wayfinders.ml.data_gen.rollouts import Rollout

    if not isinstance(rollout, Rollout):  # pragma: no cover -- defensive
        raise TypeError(f"compute_label expects a Rollout, got {type(rollout).__name__}")
    return compute_delta(
        character=rollout.character,
        action=rollout.action,
        opposition=rollout.opposition,
        scene=rollout.scene,
        party=rollout.party,
        world=rollout.world,
        campaign=rollout.campaign,
    )
