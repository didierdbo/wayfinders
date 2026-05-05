from tests.conftest import FakeRNG
from wayfinders.clock import (
    create_mission_clock,
    create_threat_clock,
    parse_clock_tick,
    tick_clock,
)
from wayfinders.conditions import apply_condition, resolve_recovery, tick_recovery_clocks
from wayfinders.models import ActionCard, Background, Character, GameState, Mission, Party, Stat
from wayfinders.resolution import compute_modifiers, resolve_action


def create_kira_character():
    return Character(
        id="1",
        name="Kira",
        stats={Stat.STR: 11, Stat.DEX: 14, Stat.WIS: 8},
        max_hp=30,
        current_hp=30,
        background=Background(
            name="Soldier",
            stat_adjustments={},
            starting_equipment=[],
            incapabilities=[],
        ),
        traits=[],
        inventory=[],
        stress=20,
        current_mission_id=None,
        conditions=[],
        recovery_clock_ids={},
        mission_history=[],
    )


def create_games_state(character: Character):
    party = Party(
        members=[character],
        soul_level=0,
        cohesion_points=0,
        gold=0,
        supplies=0,
        stash=[],
        identity="Kira Solo",
        mission_tag_history={},
    )
    thornwood_ruin_ac = ActionCard(
        id="1",
        name="Thornwood Ruin",
        description="Thornwood Ruin Test mission",
        location_types=[],
        stat_tested=Stat.DEX,
        stat_minimum=10,
        equipment_required=[],
        stress_tier="any",
        tags=[],
        clock_segments=8,
        position="safe",
        effect="minor",
        max_characters=1,
        rewards_on_success={"gold": 10},
        consequences_on_failure={"threat_clock_tick": "ruins-collapse+2"},
        consequences_on_partial={"apply_condition": "sprained_ankle"},
        default_priority=1,
    )

    clock = create_mission_clock(thornwood_ruin_ac)

    mission = Mission(
        id="kira1",
        action_card=thornwood_ruin_ac,
        clock=clock,
        assigned_characters=[character.name],
        primary_actor_id=character.id,
        hours_elapsed=0,
        is_complete=False,
        is_aborted=False,
        support_bonuses={},
    )
    gs = GameState(
        party=party,
        map_nodes={},
        factions=[],
        active_missions={mission.id: mission},
        active_threat_clocks={},
        active_recovery_clocks={},
        game_hour=0,
        action_templates={},
        event_log=[],
    )
    # Scenario bootstrap: entering the Thornwood Ruins registers its threat.
    # TODO: move into a scenario/location-enter handler later.
    ruins_collapse = create_threat_clock("Ruins Collapse", segments=4)
    gs.active_threat_clocks[ruins_collapse.id] = ruins_collapse

    return gs


# "Explore the Thornwood Ruins" (Mission Clock, 8 segments)

# Kira (DEX 14) assigned solo.
# Hour 1: Rolls 8 (full success). Clock: 2/8.
# Hour 2: Rolls 13 (partial success). Clock: 3/8. Complication: she disturbs a nest of spiders, takes 1 stress.
# Hour 3: Rolls 16 (failure). Clock: 3/8. She hits a dead end, wastes time. Threat clock "Ruins Collapse" ticks +2.
# Hour 4: Rolls 5 (full success). Clock: 5/8.
# Hour 5: Rolls 10 (partial success). Clock: 6/8. She finds an alternate route but sprains her ankle (-1 DEX until recovered).
# Hour 6: Rolls 14 (partial, using reduced DEX 13). Clock: 7/8. More spider trouble. +1 stress.
# Hour 7: Rolls 3 (full success). Clock: 8/8. Mission complete. Kira discovers the ruins' central chamber, finds a map fragment and an old sword. +2 stress total from the mission. -1 DEX until she rests (recovery clock starts).


def test_thornwood_ruin_mission():
    kira = create_kira_character()
    gs = create_games_state(kira)

    rolls_per_hour = {1: 8, 2: 13, 3: 16, 4: 5, 5: 10, 6: 13, 7: 3}
    expected_outcomes = {
        1: "full_success",
        2: "partial_success",
        3: "failure",
        4: "full_success",
        5: "partial_success",
        6: "partial_success",
        7: "full_success",
    }

    for hour in range(1, 9):
        print("hour", hour)
        for _, mission in gs.active_missions.items():
            is_complete = tick_clock(mission.clock, 1)
            if not is_complete:
                member = next(x for x in gs.party.members if x.id == mission.primary_actor_id)
                mods = compute_modifiers(member, mission.action_card, "composed")
                print("mods", mods)
                result = resolve_action(
                    base_stat=member.stats[mission.action_card.stat_tested],
                    modifiers=mods,
                    rng=FakeRNG(roll=rolls_per_hour[hour]),
                )
                print("outcome", result.outcome)
                assert result.outcome == expected_outcomes[hour]
                if hour == 6:
                    assert result.effective_stat == 13
            else:
                mission.is_complete = True

            if (
                result.outcome == "failure"
                and "threat_clock_tick" in mission.action_card.consequences_on_failure
            ):
                threat_clock_name, segment = parse_clock_tick(
                    mission.action_card.consequences_on_failure["threat_clock_tick"]
                )
                tick_clock(gs.active_threat_clocks[threat_clock_name], segment)
            elif (
                result.outcome == "partial_success"
                and "apply_condition" in mission.action_card.consequences_on_partial
            ):
                apply_condition(
                    gs, member, mission.action_card.consequences_on_partial["apply_condition"]
                )

    # ---- Post-mission recovery (Hours 8-11) ----
    # Mission complete; Kira still has sprained_ankle.
    assert "sprained_ankle" in kira.conditions
    assert len(gs.active_recovery_clocks) == 1
    recovery_clock_id = kira.recovery_clock_ids["sprained_ankle"]
    assert gs.active_recovery_clocks[recovery_clock_id].filled_segments == 0

    # Rest is auto-success: +1 segment per hour, no roll.
    # Clock is 4 segments, so 4 hours of rest fills it.
    for hour in range(8, 12):
        tick_recovery_clocks(gs, kira, segments=1)
        removed = resolve_recovery(gs, kira)
        if hour < 11:
            assert removed == []
            assert "sprained_ankle" in kira.conditions
        else:
            assert removed == ["sprained_ankle"]

    # Post-conditions: condition gone, mapping gone, clock deleted.
    assert "sprained_ankle" not in kira.conditions
    assert "sprained_ankle" not in kira.recovery_clock_ids
    assert recovery_clock_id not in gs.active_recovery_clocks
    assert gs.active_recovery_clocks == {}


def create_alden_character():
    return Character(
        id="2",
        name="Alden",
        stats={Stat.STR: 11, Stat.DEX: 10, Stat.WIS: 14},
        max_hp=30,
        current_hp=30,
        background=Background(
            name="Soldier",
            stat_adjustments={},
            starting_equipment=[],
            incapabilities=[],
        ),
        traits=[],
        inventory=[],
        stress=20,
        current_mission_id=None,
        conditions=[],
        recovery_clock_ids={},
        mission_history=[],
    )


def create_games_state_two(character1: Character, character2: Character):
    party = Party(
        members=[character1, character2],
        soul_level=0,
        cohesion_points=0,
        gold=0,
        supplies=0,
        stash=[],
        identity="Kira and Alden",
        mission_tag_history={},
    )
    thornwood_ruin_ac = ActionCard(
        id="1",
        name="Thornwood Ruin",
        description="Thornwood Ruin Test mission",
        location_types=[],
        stat_tested=Stat.DEX,
        stat_minimum=10,
        equipment_required=[],
        stress_tier="any",
        tags=[],
        clock_segments=8,
        position="safe",
        effect="minor",
        max_characters=1,
        rewards_on_success={"gold": 10},
        consequences_on_failure={"threat_clock_tick": "ruins-collapse+2"},
        consequences_on_partial={"apply_condition": "sprained_ankle"},
        default_priority=1,
    )

    clock = create_mission_clock(thornwood_ruin_ac)

    mission = Mission(
        id="kira1",
        action_card=thornwood_ruin_ac,
        clock=clock,
        assigned_characters=[character1.name, character2.name],
        primary_actor_id=character1.id,
        hours_elapsed=0,
        is_complete=False,
        is_aborted=False,
        support_bonuses={},
    )
    gs = GameState(
        party=party,
        map_nodes={},
        factions=[],
        active_missions={mission.id: mission},
        active_threat_clocks={},
        active_recovery_clocks={},
        game_hour=0,
        action_templates={},
        event_log=[],
    )
    # Scenario bootstrap: entering the Thornwood Ruins registers its threat.
    # TODO: move into a scenario/location-enter handler later.
    ruins_collapse = create_threat_clock("Ruins Collapse", segments=4)
    gs.active_threat_clocks[ruins_collapse.id] = ruins_collapse

    return gs


def test_tend_wounds_recovery():
    # Two-character party; Kira sprained, Alden tends her.
    kira = create_kira_character()
    alden = create_alden_character()  # WIS 14
    gs = create_games_state_two(kira, alden)
    apply_condition(gs, kira, "sprained_ankle")

    # Tend Wounds rolls WIS. Deterministic rolls: success, success, failure, success.
    rolls = [5, 7, 19, 6]  # vs effective_stat 14 → 3 successes, 1 failure
    for roll in rolls:
        result = resolve_action(
            base_stat=alden.stats[Stat.WIS], modifiers=[0], rng=FakeRNG(roll=roll)
        )
        if result.outcome != "failure":
            tick_recovery_clocks(gs, kira, segments=1)
        resolve_recovery(gs, kira)

    # 3 successes → clock at 3/4, still sprained.
    assert "sprained_ankle" in kira.conditions
    tick_recovery_clocks(gs, kira, 1)
    assert resolve_recovery(gs, kira) == ["sprained_ankle"]
    assert kira.conditions == []
