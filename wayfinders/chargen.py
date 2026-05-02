import numpy as np
from slugify import slugify

from wayfinders.backgrounds_data import BACKGROUNDS_LIST
from wayfinders.items_data import ITEMS
from wayfinders.models import Background, Character, Item, Stat, Trait
from wayfinders.traits_data import TRAITS, TRAITS_LIST


def random_background(rng: np.random.Generator) -> Background:
    return BACKGROUNDS_LIST[rng.integers(low=0, high=len(BACKGROUNDS_LIST))]


def generate_stats(rng: np.random.Generator) -> dict[Stat, int]:
    stats: dict[Stat, int] = {}
    for stat in Stat:
        # Roll 3d6 and sum
        rolls = rng.integers(low=1, high=7, size=3)  # high=7 to include 6
        total = int(np.sum(rolls))
        stats[stat] = total
    return stats


def adjust_stats(background: Background, stats: dict[Stat, int]) -> None:
    for stat, value in background.stat_adjustments.items():
        stats[stat] += value


def clamp_stats(stats: dict[Stat, int]) -> None:
    for stat, value in stats.items():
        stats[stat] = max(3, min(value, 18))


def generate_traits(trait_pool: list[Trait], rng: np.random.Generator) -> list[Trait]:
    traits: list[Trait] = []
    contradicts: list[Trait] = []
    while len(traits) < 2:
        trait = trait_pool[rng.integers(low=0, high=len(trait_pool))]
        if trait not in traits and trait not in contradicts:
            traits.append(trait)
            for tc in trait.contradicts:
                contradicts.append(TRAITS[tc])
    return traits


def create_character(
    name: str,
    background: Background,
    trait_pool: list[Trait],
    rng: np.random.Generator,
) -> Character:
    """
    Full character creation per Section 4 of the spec:
    1. Roll 3d6 for each stat
    2. Apply background stat adjustments
    3. Clamp stats to [3, 18]
    4. Roll 1d6 for HP
    5. Select 2 random traits (no contradictions)
    6. Assign starting equipment from background
    7. Initialize stress=0, empty conditions, etc.
    """

    # 1. Roll 3d6 for each stat
    stats: dict[Stat, int] = generate_stats(rng)

    # 2. Apply background stat adjustments
    adjust_stats(background, stats)

    # 3. Clamp stats to [3, 18]
    clamp_stats(stats)

    # 4. Roll 1d6 for HP
    hp: int = int(rng.integers(low=1, high=7))

    # 5. Select 2 random traits (no contradictions)
    traits = generate_traits(trait_pool, rng)

    # 6. Assign starting equipment from background
    items: list[Item] = []
    for item_name in background.starting_equipment:
        items.append(ITEMS[item_name])

    # 7. Initialize stress=0, empty conditions, etc.
    stress: int = 0
    conditions: list[str] = []
    current_mission_id = None
    recovery_clock_ids: dict[str, str] = {}
    mission_history: list[str] = []
    backstory_hook = None
    portrait_id = None

    return Character(
        id=slugify(name),
        name=name,
        stats=stats,
        max_hp=hp,
        current_hp=hp,
        background=background,
        traits=traits,
        inventory=items,
        stress=stress,
        current_mission_id=current_mission_id,
        conditions=conditions,
        recovery_clock_ids=recovery_clock_ids,
        mission_history=mission_history,
        backstory_hook=backstory_hook,
        portrait_id=portrait_id,
    )


def generate_starting_pool(n: int, rng: np.random.Generator) -> list[Character]:
    backgrounds: list[Background] = []
    chars: list[Character] = []
    bg: Background | None = None
    for i in range(n):
        while not bg or bg in backgrounds:
            bg = random_background(rng)
        backgrounds.append(bg)
        chars.append(create_character(f"Starting-Pool-{i + 1}", bg, TRAITS_LIST, rng))
        if len(chars) == len(BACKGROUNDS_LIST):
            break
    return chars
