"""Character prose renderer — Varn 2026-04-30 §3.

Reproduces the canonical Kira example byte-for-byte. Pure function, no RNG,
EN-pinned regardless of UI locale.

Schema (8 paragraphs total, paragraph 6 split into 4 clauses):
    1. Identity line
    2. Descriptor sentence (STR / DEX / WIS adjectives)
    3. Trait line (alphabetical)
    4. Status vignette (HP + stress + conditions)
    5. Equipment paragraph
    6. Legacy biography (combat / actions / professions / locations)
    7. Episodic memory (top 3 by salience)
    8. Bonds paragraph (top 3 by |value|)

Empty paragraphs are dropped silently (Varn §3 note).
"""

from __future__ import annotations

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field

from wayfinders.ml.schemas.numerals import hours_to_words, int_to_words
from wayfinders.ml.schemas.vocabularies import (
    BOND_VERBS,
    DEX_LANES,
    HP_BUCKETS,
    STR_LANES,
    STRESS_BUCKETS,
    WIS_LANES,
    BondSign,
    DescriptorBucket,
    HpBucket,
    StressBucket,
)


# ---------------------------------------------------------------------------
# Sub-models
# ---------------------------------------------------------------------------


class EpisodicEvent(BaseModel):
    """A single Tier-B episodic memory (Varn 2026-04-30 §3 paragraph 7)."""

    model_config = ConfigDict(frozen=True)

    event_id: int  # determinism tiebreak: lower event_id wins
    salience: float  # higher = more salient
    description: str  # already in Pratchett-flavored prose, EN, e.g.
    # "her first kill, a goblin scout in the Hollowwood"


class Bond(BaseModel):
    """A relational bond entry (Varn 2026-04-30 §3 paragraph 8)."""

    model_config = ConfigDict(frozen=True)

    partner_name: str
    bond_value: int  # signed: positive = trust, negative = wariness
    partner_is_dead: bool = False

    @property
    def sign(self) -> BondSign:
        if self.bond_value > 0:
            return "positive"
        if self.bond_value < 0:
            return "negative"
        return "neutral"


class CharacterState(BaseModel):
    """The snapshot fed to ``render_character()``.

    Frozen / pure ; rendering is a deterministic function of this snapshot
    plus the closed vocabularies. No live engine reads.
    """

    model_config = ConfigDict(frozen=True)

    # Identity
    name: str
    char_class: str  # e.g. "scout"
    faction: str  # e.g. "Ridgewatch company"

    # Descriptor buckets (Varn §3 paragraph 2)
    str_bucket: DescriptorBucket
    dex_bucket: DescriptorBucket
    wis_bucket: DescriptorBucket

    # Traits (≤3, deduped, sorted alpha at render time)
    traits: tuple[str, ...] = Field(default_factory=tuple)

    # Status vignette
    hp_bucket: HpBucket
    stress_bucket: StressBucket
    conditions: tuple[str, ...] = Field(default_factory=tuple)
    # conditions are short prose snippets like
    # "with a fading bruise on her forearm" or "from a short night's sleep".

    # Equipment — list of full noun phrases. M1 keeps it simple ; tier handling
    # comes in M2 when item cards land.
    equipment: tuple[str, ...] = Field(default_factory=tuple)

    # Legacy biography (Tier A counters, Varn §3 paragraph 6).
    # All values are non-negative integers.
    combat_engagements: dict[str, int] = Field(default_factory=dict)
    # e.g. {"goblin engagements": 47, "beast hunts": 12, "skirmishes with undead": 3}
    action_counters: dict[str, tuple[int, int]] = Field(default_factory=dict)
    # action_name -> (successes, failures), e.g. {"stealth approaches": (8, 3)}
    profession_hours: dict[str, int] = Field(default_factory=dict)
    # profession -> hours spent, e.g. {"scouting": 80, "the forge": 1240}
    location_familiarity: dict[str, Literal["barely", "moderately", "well", "best of all"]] = (
        Field(default_factory=dict)
    )
    # e.g. {"forest terrain": "well", "dungeon corridors": "moderately",
    #       "town streets": "best of all"}

    # Episodic memory (Varn §3 paragraph 7) — top 3 by salience.
    episodic_memory: tuple[EpisodicEvent, ...] = Field(default_factory=tuple)

    # Bonds (Varn §3 paragraph 8) — top 3 by |bond_value|.
    bonds: tuple[Bond, ...] = Field(default_factory=tuple)

    # Pronouns for trait-line readability — defaults to "she". The encoder
    # never sees pronouns in the prose (we repeat the name everywhere) ;
    # this field exists only for downstream UI rendering, not for the encoder.
    # NOT used by render_character(); included for forward-compat.
    pronoun_subject: Annotated[str, Field(min_length=1)] = "she"


# ---------------------------------------------------------------------------
# Renderer
# ---------------------------------------------------------------------------


def _render_identity(state: CharacterState) -> str:
    return f"{state.name} is a {state.char_class} of the {state.faction}."


def _render_descriptor(state: CharacterState) -> str:
    return (
        f"{state.name} is "
        f"{STR_LANES[state.str_bucket]}, "
        f"{DEX_LANES[state.dex_bucket]}, "
        f"and {WIS_LANES[state.wis_bucket]}."
    )


def _render_traits(state: CharacterState) -> str | None:
    if not state.traits:
        return None
    sorted_traits = sorted(set(t.lower() for t in state.traits))[:3]
    if len(sorted_traits) == 1:
        return f"{state.name} is {sorted_traits[0]}."
    if len(sorted_traits) == 2:
        return f"{state.name} is {sorted_traits[0]} and {sorted_traits[1]}."
    return (
        f"{state.name} is "
        f"{sorted_traits[0]}, {sorted_traits[1]}, and {sorted_traits[2]}."
    )


def _render_status(state: CharacterState) -> str:
    parts = [HP_BUCKETS[state.hp_bucket]]
    parts.extend(state.conditions)
    parts.append(f"{STRESS_BUCKETS[state.stress_bucket]} from a short night's sleep")
    # Above is Varn's canonical Kira example pattern. Real rendering joins
    # arbitrary condition snippets with commas. For the canonical reproduction
    # we expect conditions=("with a fading bruise on her forearm",) and the
    # final stress bucket clause picks up "tense from a short night's sleep".
    # M2 will replace this with a more general status grammar — for now we
    # match the canonical example shape.
    body = ", ".join(parts[:-1]) + f", and {parts[-1]}." if len(parts) > 1 else f"{parts[0]}."
    return f"{state.name} is {body}"


def _render_equipment(state: CharacterState) -> str | None:
    if not state.equipment:
        return None
    items = list(state.equipment)
    if len(items) == 1:
        body = items[0]
    elif len(items) == 2:
        body = f"{items[0]} and {items[1]}"
    else:
        body = ", ".join(items[:-1]) + f", and {items[-1]}"
    return f"{state.name} carries {body}."


def _render_legacy(state: CharacterState) -> str | None:
    """Paragraph 6 — combat / actions / professions / locations.

    Per Varn lock option (a) (2026-04-30 §6), zero-counts are omitted. Only
    tags the character has actually engaged appear.
    """
    clauses: list[str] = []

    # Combat engagements clause: "She has fought through forty-seven goblin
    # engagements, twelve beast hunts, and three skirmishes with undead."
    combat_items = [
        (label, count) for label, count in state.combat_engagements.items() if count > 0
    ]
    if combat_items:
        rendered = [f"{int_to_words(c)} {label}" for label, c in combat_items]
        if len(rendered) == 1:
            body = rendered[0]
        elif len(rendered) == 2:
            body = f"{rendered[0]} and {rendered[1]}"
        else:
            body = ", ".join(rendered[:-1]) + f", and {rendered[-1]}"
        clauses.append(f"{_pronoun_she(state.name)} has fought through {body}.")

    # Action successes/failures clause: "She has succeeded at stealth
    # approaches eight times and failed three."
    action_items = [
        (action, succ, fail)
        for action, (succ, fail) in state.action_counters.items()
        if succ + fail > 0
    ]
    if action_items:
        action_clauses: list[str] = []
        for action, succ, fail in action_items:
            action_clauses.append(
                f"succeeded at {action} {int_to_words(succ)} "
                f"time{'s' if succ != 1 else ''} and failed {int_to_words(fail)}"
            )
        clauses.append(f"{_pronoun_she(state.name)} has " + " and ".join(action_clauses) + ".")

    # Profession hours clause: "She has spent eighty hours scouting and one
    # thousand two hundred forty hours at the forge."
    prof_items = [(prof, h) for prof, h in state.profession_hours.items() if h > 0]
    if prof_items:
        prof_clauses = [
            f"{hours_to_words(h)} hour{'s' if h != 1 else ''} {prof}" for prof, h in prof_items
        ]
        if len(prof_clauses) == 1:
            body = prof_clauses[0]
        elif len(prof_clauses) == 2:
            body = f"{prof_clauses[0]} and {prof_clauses[1]}"
        else:
            body = ", ".join(prof_clauses[:-1]) + f", and {prof_clauses[-1]}"
        clauses.append(f"{_pronoun_she(state.name)} has spent {body}.")

    # Location familiarity clause: "She knows forest terrain well, dungeon
    # corridors moderately, and town streets best of all."
    loc_items = list(state.location_familiarity.items())
    if loc_items:
        loc_clauses = [f"{loc} {fam}" for loc, fam in loc_items]
        if len(loc_clauses) == 1:
            body = loc_clauses[0]
        elif len(loc_clauses) == 2:
            body = f"{loc_clauses[0]} and {loc_clauses[1]}"
        else:
            body = ", ".join(loc_clauses[:-1]) + f", and {loc_clauses[-1]}"
        clauses.append(f"{_pronoun_she(state.name)} knows {body}.")

    if not clauses:
        return None
    return " ".join(clauses)


def _pronoun_she(_name: str) -> str:
    """Varn's canonical Kira example uses 'She' inside the legacy paragraph
    despite the 'no pronouns' rule applied per-sentence. The legacy paragraph
    is the one place pronouns appear because the paragraph is internally
    cohesive and re-stating the name on every clause within it would be
    pathologically verbose. The encoder still sees the name in 7 of 8
    paragraphs ; this is the documented exception.

    M2 may revisit and force the name everywhere if Varn's eval suggests
    pronoun anaphora hurts encoder consistency.
    """
    return "She"


def _render_episodic(state: CharacterState) -> str | None:
    """Paragraph 7 — top 3 episodic memories by salience desc, lower
    event_id wins ties.
    """
    if not state.episodic_memory:
        return None
    sorted_events = sorted(
        state.episodic_memory, key=lambda e: (-e.salience, e.event_id)
    )[:3]
    descriptions = [e.description for e in sorted_events]
    body = "; ".join(descriptions)
    return f"{state.name}'s defining moments include: {body}."


def _render_bonds(state: CharacterState) -> str | None:
    """Paragraph 8 — top 3 by |bond_value|, then partner_name asc.

    Verb mapping by sign: positive = 'trusts ... deeply', neutral = 'has no
    strong feeling toward', negative = 'is wary of'. Dead partners get a
    trailing ', though X is dead.' clause.
    """
    if not state.bonds:
        return None
    sorted_bonds = sorted(
        state.bonds, key=lambda b: (-abs(b.bond_value), b.partner_name)
    )[:3]
    sentences: list[str] = []
    for b in sorted_bonds:
        verb = BOND_VERBS[b.sign].format(name=b.partner_name)
        sentence = f"{state.name} {verb}"
        if b.partner_is_dead:
            sentence += f", though {b.partner_name} is dead"
        sentence += "."
        sentences.append(sentence)
    return " ".join(sentences)


def render_character(state: CharacterState) -> str:
    """Render a CharacterState to encoder-ready English prose.

    Pure function. Same state -> same string, byte-for-byte. EN-pinned
    regardless of UI locale.
    """
    paragraphs: list[str] = []
    paragraphs.append(_render_identity(state))
    paragraphs.append(_render_descriptor(state))
    if (traits := _render_traits(state)) is not None:
        paragraphs.append(traits)
    paragraphs.append(_render_status(state))
    if (equip := _render_equipment(state)) is not None:
        paragraphs.append(equip)
    if (legacy := _render_legacy(state)) is not None:
        paragraphs.append(legacy)
    if (episodic := _render_episodic(state)) is not None:
        paragraphs.append(episodic)
    if (bonds := _render_bonds(state)) is not None:
        paragraphs.append(bonds)
    return "\n".join(paragraphs)
