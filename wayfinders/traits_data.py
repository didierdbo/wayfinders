from wayfinders.models import Trait

TRAITS_LIST: list[Trait] = [
    Trait(
        name="Bold",
        roll_modifier={"risky": 1},
        menu_tag_boosted=[],
        menu_tag_suppressed=[],
        stress_modifier=0,
        vote_tendency="keep",
        contradicts=["Cautious"],
    ),
    Trait(
        name="Cautious",
        roll_modifier={"safe": 1},
        menu_tag_boosted=[],
        menu_tag_suppressed=["risky"],
        stress_modifier=0,
        vote_tendency="risk assessment",
        contradicts=["Bold"],
    ),
    Trait(
        name="Greedy",
        roll_modifier={"loot": 1},
        menu_tag_boosted=["loot", "trade"],
        menu_tag_suppressed=[],
        stress_modifier=0,
        vote_tendency="banish on cost",
        contradicts=[],
    ),
]


TRAITS = {trait.name: trait for trait in TRAITS_LIST}
