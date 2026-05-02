from wayfinders.models import Background, Stat

BACKGROUNDS_LIST: list[Background] = [
    Background(
        name="Former Soldier",
        stat_adjustments={Stat.STR: 2, Stat.WIS: -1},
        starting_equipment=["sword", "shield", "leather-armor"],
        incapabilities=["Cannot perform delicate/scholarly tasks"],
    ),
    Background(
        name="Traveling Herbalist",
        stat_adjustments={Stat.WIS: 1, Stat.DEX: 1},
        starting_equipment=["healer-s-kit", "herb-pouch", "walking-staff"],
        incapabilities=["Cannot wear heavy armor"],
    ),
    Background(
        name="Disgraced Noble",
        stat_adjustments={Stat.WIS: 2, Stat.STR: -1},
        starting_equipment=["fine-clothes", "signet-ring", "purse"],
        incapabilities=["Refuses manual labor"],
    ),
    Background(
        name="Woodsman",
        stat_adjustments={Stat.DEX: 2, Stat.WIS: -1},
        starting_equipment=["shortbow", "arrows", "hunting-knife", "trap-kit"],
        incapabilities=["Uncomfortable in cities"],
    ),
]

BACKGROUNDS = {background.name: background for background in BACKGROUNDS_LIST}
