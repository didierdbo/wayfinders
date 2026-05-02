import numpy as np

from wayfinders.backgrounds_data import BACKGROUNDS, BACKGROUNDS_LIST
from wayfinders.chargen import create_character, generate_starting_pool, random_background
from wayfinders.traits_data import TRAITS, TRAITS_LIST


def test_character_creation_deterministic():
    rng1 = np.random.default_rng(seed=123)
    rng2 = np.random.default_rng(seed=123)
    c1 = create_character(
        "Kira",
        BACKGROUNDS["Woodsman"],
        [TRAITS["Bold"], TRAITS["Cautious"], TRAITS["Greedy"]],
        rng1,
    )
    c2 = create_character(
        "Kira",
        BACKGROUNDS["Woodsman"],
        [TRAITS["Bold"], TRAITS["Cautious"], TRAITS["Greedy"]],
        rng2,
    )
    assert c1 == c2
    assert c1.stats == c2.stats
    assert c1.max_hp == c2.max_hp
    assert [t.name for t in c1.traits] == [t.name for t in c2.traits]


def test_stats_in_valid_range(rng):
    for _ in range(100):
        c = create_character("test", random_background(rng), TRAITS_LIST, rng)
        for stat_val in c.stats.values():
            assert 3 <= stat_val <= 18


def test_no_contradictory_traits(rng):
    for _ in range(200):
        c = create_character("test", random_background(rng), TRAITS_LIST, rng)
        trait_names = {t.name for t in c.traits}
        for t in c.traits:
            for contra in t.contradicts:
                assert contra not in trait_names


def test_starting_pool(rng):
    chars = generate_starting_pool(10, rng)
    assert len(chars) == len(BACKGROUNDS_LIST)
    assert len({c.background.name for c in chars}) == len(chars)
