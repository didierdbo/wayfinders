from wayfinders.models import (
    ActionCard,
    Background,
    Character,
    GameState,
    Item,
    MapNode,
    Party,
    Stat,
    Trait,
)


def create_new_games_state():
    sword_item = Item(
        id="1",
        name="Sword",
        category="weapon",
        stat_bonus={},
        tier=1,
        tags=[],
        armor_value=0,
        uses_until_degradation=5,
        is_provision=False,
    )
    cautious_trait = Trait(
        name="Cautious",
        roll_modifier={"risky": -1},
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
        inventory=[sword_item],
        stress=20,
        current_mission_id=None,
        conditions=[],
        recovery_clock_ids={},
        mission_history=[],
    )

    party = Party(
        members=[soldier_char],
        soul_level=0,
        cohesion_points=0,
        gold=0,
        supplies=0,
        stash=[],
        identity="Canan Solo",
        mission_tag_history={},
    )

    map_node = MapNode(
        id="1",
        name="Starting Node",
        node_type="settlement",
        discovery_state="discovered",
        connections=[],
        available_action_ids=[],
        event_pool=[],
    )
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

    return GameState(
        party=party,
        map_nodes={"1": map_node},
        factions=[],
        active_missions={},
        active_threat_clocks={},
        active_recovery_clocks={},
        game_hour=0,
        action_templates={"intro": action_card},
        event_log=[],
    )


def test_create_simple_game_stat():
    gs1 = create_new_games_state()
    gs2 = create_new_games_state()

    # Assert it round-trips through dataclass equality.
    assert gs1 == gs2
