from slugify import slugify

from wayfinders.models import Item, Stat

_ITEMS_LIST: list[Item] = [
    Item(
        id=slugify("Sword"),
        name="Sword",
        category="Melee Weapons",
        stat_bonus={Stat.STR: 1},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Hunting Knife"),
        name="Hunting Knife",
        category="Melee Weapons",
        stat_bonus={Stat.DEX: 1},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("War Hammer"),
        name="War Hammer",
        category="Melee Weapons",
        stat_bonus={Stat.STR: 2},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Master-Crafted Blade"),
        name="Master-Crafted Blade",
        category="Melee Weapons",
        stat_bonus={Stat.STR: 2},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Shortbow"),
        name="Shortbow",
        category="Ranged Weapons",
        stat_bonus={Stat.DEX: 1},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Longbow"),
        name="Longbow",
        category="Ranged Weapons",
        stat_bonus={Stat.DEX: 2},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Elven Composite Bow"),
        name="Elven Composite Bow",
        category="Ranged Weapons",
        stat_bonus={Stat.DEX: 3},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Arrows"),
        name="Arrows",
        category="Ranged Weapons",
        stat_bonus={},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Leather Armor"),
        name="Leather Armor",
        category="Armor",
        stat_bonus={},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=1,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Chain Mail"),
        name="Chain Mail",
        category="Armor",
        stat_bonus={},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=2,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Plate Armor"),
        name="Plate Armor",
        category="Armor",
        stat_bonus={},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=3,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Shield"),
        name="Shield",
        category="Shield",
        stat_bonus={},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=2,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Trap Kit"),
        name="Trap Kit",
        category="Lockpick/Tools",
        stat_bonus={Stat.DEX: 1},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Healer's Kit"),
        name="Healer's Kit",
        category="Healer's Supplies",
        stat_bonus={Stat.WIS: 2},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Herb Pouch"),
        name="Herb Pouch",
        category="Healer's Supplies",
        stat_bonus={Stat.WIS: 1},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Walking Staff"),
        name="Walking Staff",
        category="Melee Weapons",
        stat_bonus={Stat.DEX: 1},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Fine Clothes"),
        name="Fine Clothes",
        category="Trade Goods",
        stat_bonus={},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Signet Ring"),
        name="Signet Ring",
        category="Trade Goods",
        stat_bonus={},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
    Item(
        id=slugify("Purse"),
        name="Purse",
        category="Trade Goods",
        stat_bonus={},
        tier=1,  # TODO adjust value
        tags=[],
        armor_value=0,
        uses_until_degradation=5,  # TODO adjust value
        is_provision=False,
    ),
]

ITEMS = {item.id: item for item in _ITEMS_LIST}
