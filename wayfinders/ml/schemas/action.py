"""Action prose renderer -- Varn 2026-05-01 sec. 3, with sentence 9 dropped per
Pax 2026-05-02 sec. 5 lock.

Schema (9 sentences after sentence-9 drop):
    1. Action headline           ("The action is a stealth approach.")
    2. Actor line                ("Kira is the actor.")
    3. Direct-object clause      ("Kira is sneaking past two sentries...")
    4. Manner clause             ("The deed is performed under starlight...")
    5. Intent clause             ("The intent is to reach the ridge...")
    6. Tag line                  ("The action is tagged as ...")
    7. Stat line                 ("The action tests dexterity, opposed by...")
    8. Opposition vignette       ("The opposition is two human sentries...")
    9. (DROPPED -- was local legacy echo, now in character vector only)
   10. Stakes hint               ("A failed stealth approach typically means...")

Renumbered after the drop: M1 emits 9 sentences total, in order 1, 2, 3, 4,
5, 6, 7, 8, 10 from the original Varn schema.

Pure function, EN-pinned, no RNG.
"""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from wayfinders.ml.schemas.numerals import int_to_words
from wayfinders.ml.schemas.vocabularies import (
    Alertness,
    Footing,
    Noise,
    SkillTier,
    TimeOfDay,
)


# ---------------------------------------------------------------------------
# Sub-models
# ---------------------------------------------------------------------------


class OppositionState(BaseModel):
    """The immediate opposition for this action attempt (Varn sec. 3 paragraph 8)."""

    model_config = ConfigDict(frozen=True)

    count: int  # 0 = solo (paragraph 8 dropped at render time)
    family: str  # e.g. "human sentries", "goblin scouts", "alarm wards"
    alertness: Alertness
    skill_tier: SkillTier


class ActionCard(BaseModel):
    """The deed being attempted, right now, by this character.

    Closed vocabulary on every field. Designer-authored.
    """

    model_config = ConfigDict(frozen=True)

    # Sentence 1 -- verb headline (closed-vocab phrase, ~80 entries).
    action_phrase: str  # e.g. "stealth approach", "frontal assault", "parley"

    # Sentence 3 -- direct-object clause.
    verb_progressive: str  # e.g. "sneaking past", "charging", "speaking with"
    target_phrase: str  # e.g. "two sentries posted at the watchtower"

    # Sentence 4 -- manner.
    time_phrase: TimeOfDay
    footing_phrase: Footing
    noise_phrase: Noise

    # Sentence 5 -- intent.
    intent_phrase: str  # e.g. "reach the ridge unseen and unheard"

    # Sentence 6 -- tag set + opposition family.
    tag_set: tuple[str, ...]  # e.g. ("stealth", "movement", "scouting")
    opposition_family: str  # e.g. "humanoids", "undead", "beasts", "machinery"

    # Sentence 7 -- stat tested + opposed-by stat.
    stat_tested: Literal[
        "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"
    ]
    opposed_stat: Literal[
        "strength", "dexterity", "constitution", "intelligence", "wisdom",
        "charisma", "perception", "armor", "resolve",
    ]

    # Sentence 10 -- failure stakes (slotted from card).
    failure_template: str = Field(
        ...,
        description=(
            "EN-only. Pratchett palette (humor with stakes, never gag). "
            "Rendered verbatim into sentence 10. Example: 'an alarm raised, "
            "a fight on disadvantageous ground, or a forced retreat'."
        ),
    )

    # Optional success template -- UI-side localization key. NOT used by
    # render_action(); kept on the card for downstream display.
    success_template_key: str | None = None


# ---------------------------------------------------------------------------
# Renderer
# ---------------------------------------------------------------------------


def _render_headline(card: ActionCard) -> str:
    return f"The action is a {card.action_phrase}."


def _render_actor(actor_name: str) -> str:
    return f"{actor_name} is the actor."


def _render_direct_object(card: ActionCard, actor_name: str) -> str:
    return f"{actor_name} is {card.verb_progressive} {card.target_phrase}."


def _render_manner(card: ActionCard) -> str:
    return (
        f"The deed is performed {card.time_phrase}, "
        f"on {card.footing_phrase}, "
        f"in {card.noise_phrase}."
    )


def _render_intent(card: ActionCard) -> str:
    return f"The intent is to {card.intent_phrase}."


def _render_tags(card: ActionCard) -> str:
    sorted_tags = sorted(set(t.lower() for t in card.tag_set))
    if len(sorted_tags) == 1:
        body = sorted_tags[0]
    elif len(sorted_tags) == 2:
        body = f"{sorted_tags[0]} and {sorted_tags[1]}"
    else:
        body = ", ".join(sorted_tags[:-1]) + f", and {sorted_tags[-1]}"
    return f"The action is tagged as {body}, and against {card.opposition_family}."


def _render_stats(card: ActionCard) -> str:
    return f"The action tests {card.stat_tested}, opposed by {card.opposed_stat}."


def _render_opposition(opposition: OppositionState) -> str | None:
    if opposition.count <= 0:
        return None
    return (
        f"The opposition is {int_to_words(opposition.count)} {opposition.family}, "
        f"{opposition.alertness}, of {opposition.skill_tier} skill."
    )


def _render_stakes(card: ActionCard) -> str:
    return f"A failed {card.action_phrase} typically means {card.failure_template}."


def render_action(
    card: ActionCard,
    actor_name: str,
    opposition: OppositionState,
) -> str:
    """Render an action attempt to encoder-ready English prose.

    Pure function, byte-stable, EN-pinned. Sentence 9 (local legacy echo)
    is dropped per Pax 2026-05-02 sec. 5 lock.
    """
    sentences: list[str] = [
        _render_headline(card),
        _render_actor(actor_name),
        _render_direct_object(card, actor_name),
        _render_manner(card),
        _render_intent(card),
        _render_tags(card),
        _render_stats(card),
    ]
    if (opp := _render_opposition(opposition)) is not None:
        sentences.append(opp)
    sentences.append(_render_stakes(card))
    return "\n".join(sentences)
