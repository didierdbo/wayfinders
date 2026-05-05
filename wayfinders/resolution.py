from typing import Literal, Protocol, cast

from wayfinders.models import ActionCard, Character, Resolution, Stat

STRESS_MODIFIERS = {"composed": 0, "strained": -1, "stressed": -2, "breaking": -3}
CONDITION_MODIFIERS: dict[str, dict[Stat | None, int]] = {
    "wounded": {None: -2},  # None = applies to all stats
    "exhausted": {None: -1},
    "sprained_ankle": {Stat.DEX: -1},  # stat-specific
}


class RollRNG(Protocol):
    """Minimal RNG contract resolve_action depends on.

    Structural: anything with a matching `integers` method satisfies this,
    including numpy.random.Generator and test fakes.
    """

    def integers(self, low: int, high: int) -> int: ...


def resolve_action(
    base_stat: int,
    modifiers: list[int],
    rng: RollRNG,
) -> Resolution:
    effective_stat = max(1, min(18, base_stat + sum(modifiers)))
    roll = int(rng.integers(1, 21))  # [1, 21) = d20
    outcome: Literal["full_success", "partial_success", "failure"]
    if roll <= effective_stat - 5:
        outcome = "full_success"
    elif roll <= effective_stat:
        outcome = "partial_success"
    else:
        outcome = "failure"
    return Resolution(outcome, roll, effective_stat, modifiers)


def compute_modifiers(
    character: Character,
    action: ActionCard,
    stress_state: Literal["composed", "strained", "stressed", "breaking"],
    support_bonus: int = 0,  # from multi-character missions
) -> list[int]:
    """
    Gather all modifiers from equipment, conditions, traits, stress, and support.
    Returns a list of individual modifiers (not a sum).
    """
    modifiers = []

    for item in character.inventory:
        if action.stat_tested in item.stat_bonus:
            modifiers.append(item.stat_bonus[action.stat_tested])

    for condition in character.conditions:
        penalties = CONDITION_MODIFIERS[condition]
        for stat_key, value in penalties.items():
            if stat_key is None or stat_key == action.stat_tested:
                modifiers.append(value)

    for tag in action.tags:
        for trait in character.traits:
            if tag in trait.roll_modifier:
                # tag is checked at runtime; narrow for the strict Literal dict key.
                modifiers.append(trait.roll_modifier[cast(Literal["risky", "safe", "loot"], tag)])

    modifiers.append(STRESS_MODIFIERS[stress_state])

    if support_bonus:
        modifiers.append(support_bonus)

    return modifiers
