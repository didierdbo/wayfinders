from tests.conftest import FakeRNG
from wayfinders.models import ActionCard, Background, Character, Item, Stat, Trait
from wayfinders.resolution import compute_modifiers, resolve_action


def test_full_success_with_high_stat(rng):
    # Seed 42 with numpy.random.default_rng produces known sequences.
    # Instead of depending on the exact first roll, set up a scenario
    # where ANY roll 1-9 is full success (stat 14, full success <= 9).
    # Roll the rng, check outcome matches the roll value.
    result = resolve_action(base_stat=14, modifiers=[], rng=rng)
    if result.roll <= 9:
        assert result.outcome == "full_success"
    elif result.roll <= 14:
        assert result.outcome == "partial_success"
    else:
        assert result.outcome == "failure"


def test_kira_scout_example():
    # Kira: DEX 14, Bow +1, Bold +1 (risky), Curious +1 (exploration)
    # Effective DEX should be 17 (14 + 1 + 1 + 1)
    # Full success: roll 1-12, Partial: 13-17, Failure: 18-20
    modifiers = [1, 1, 1]  # equipment, bold, curious
    result = resolve_action(base_stat=14, modifiers=modifiers, rng=FakeRNG(roll=7))
    assert result.effective_stat == 17
    assert result.outcome == "full_success"
    assert result.roll == 7


def test_effective_stat_capped_at_18():
    result = resolve_action(base_stat=16, modifiers=[1, 1, 1], rng=FakeRNG(roll=1))
    assert result.effective_stat == 18  # 16+3=19, clamped to 18


def test_effective_stat_floored_at_1():
    result = resolve_action(base_stat=5, modifiers=[-3, -2, -1], rng=FakeRNG(roll=1))
    assert result.effective_stat == 1  # 5-6=-1, floored to 1
    assert result.outcome == "partial_success"  # roll 1 is always full success when stat >= 6...
    # Wait. With effective_stat=1, full success requires roll <= 1-5 = -4.
    # That means NO roll can be a full success.
    # Partial success requires roll <= 1. Only a roll of 1.
    # This is correct per the spec -- a stat of 1 means almost certain failure.


def test_full_success_roll():
    result = resolve_action(base_stat=10, modifiers=[], rng=FakeRNG(roll=5))
    assert result.outcome == "full_success"


def test_full_partial_roll():
    result = resolve_action(base_stat=10, modifiers=[], rng=FakeRNG(roll=6))
    assert result.outcome == "partial_success"
    result = resolve_action(base_stat=10, modifiers=[], rng=FakeRNG(roll=10))
    assert result.outcome == "partial_success"


def test_failure_roll():
    result = resolve_action(base_stat=10, modifiers=[], rng=FakeRNG(roll=11))
    assert result.outcome == "failure"


def create_character(
    roll_modifier_name: str = "scouting",
    stat: Stat = Stat.STR,
    is_empty: bool = False,
):
    sword_item = Item(
        id="1",
        name="Sword",
        category="weapon",
        stat_bonus={stat: 4},
        tier=1,
        tags=[],
        armor_value=0,
        uses_until_degradation=5,
        is_provision=False,
    )
    cautious_trait = Trait(
        name="Cautious",
        roll_modifier={roll_modifier_name: 1},
        menu_tag_boosted=[],
        menu_tag_suppressed=[],
        stress_modifier=-1,
        vote_tendency="abstain",
        contradicts=["Bold"],
    )
    soldier_char = Character(
        id="1",
        name="",
        stats={Stat.STR: 14, Stat.DEX: 11, Stat.WIS: 8},
        max_hp=30,
        current_hp=30,
        background=Background(
            name="Soldier",
            stat_adjustments={Stat.STR: 2},
            starting_equipment=["sword std"],
            incapabilities=["scholarly"],
        ),
        traits=[cautious_trait],
        inventory=[],
        stress=20,
        current_mission_id=None,
        conditions=["wounded"],
        recovery_clock_ids={},
        mission_history=[],
    )
    if is_empty:
        soldier_char.inventory.clear()
    else:
        soldier_char.inventory.append(sword_item)
    return soldier_char


def create_action():
    action_card = ActionCard(
        id="1",
        name="Test Action",
        description="A test action card",
        location_types=[],
        stat_tested=Stat.STR,
        stat_minimum=10,
        equipment_required=["Sword"],
        stress_tier="any",
        tags=["scouting"],
        clock_segments=2,
        position="safe",
        effect="minor",
        max_characters=4,
        rewards_on_success={"gold": 10},
        consequences_on_failure={"stress": 20},
        consequences_on_partial={},
        default_priority=1,
    )
    return action_card


def test_modifiers():
    action_card = create_action()
    character = create_character()
    modifiers = compute_modifiers(character, action_card, "stressed", 0)
    assert modifiers == [4, -2, 1, -2]

    #  composed stress path
    modifiers = compute_modifiers(character, action_card, "composed", 0)
    assert modifiers == [4, -2, 1, 0]

    #  trait with no matching tag
    character = create_character(roll_modifier_name="happy")
    modifiers = compute_modifiers(character, action_card, "stressed", 0)
    assert modifiers == [4, -2, -2]

    #  item without matching stat bonus
    character = create_character(stat=Stat.DEX)
    modifiers = compute_modifiers(character, action_card, "stressed", 0)
    assert modifiers == [-2, 1, -2]

    # empty character
    character = create_character(is_empty=True)
    modifiers = compute_modifiers(character, action_card, "stressed", 0)
    assert modifiers == [-2, 1, -2]
