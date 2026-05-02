from wayfinders.clock import (
    check_passive_tick,
    create_mission_clock,
    create_recovery_clock,
    create_threat_clock,
    tick_clock,
)
from wayfinders.models import (
    ActionCard,
    Clock,
    Stat,
)


def test_clock_completes_when_filled():
    clock = Clock(
        id="m1",
        name="Scout",
        clock_type="mission",
        total_segments=6,
        filled_segments=0,
        passive_tick_interval=None,
        hours_since_last_passive_tick=0,
    )
    assert not tick_clock(clock, 2)  # 2/6
    assert not tick_clock(clock, 2)  # 4/6
    assert tick_clock(clock, 2)  # 6/6, completes
    assert clock.filled_segments == 6


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
        clock_segments=4,
        position="safe",
        effect="minor",
        max_characters=4,
        rewards_on_success={"gold": 10},
        consequences_on_failure={"stress": 20},
        consequences_on_partial={},
        default_priority=1,
    )
    return action_card


def test_clock_does_not_overflow():
    action_card = create_action()
    clock = create_mission_clock(action_card)
    tick_clock(clock, 3)  # 3/4
    tick_clock(clock, 3)  # should be 4/4, not 6/4
    assert clock.filled_segments == 4


def test_threat_clock_passive_tick():
    clock = create_threat_clock("goblin_raid", segments=6)
    # Simulate 7 hours passing, one hour at a time
    total_passive_ticks = 0
    for _ in range(7):
        total_passive_ticks += check_passive_tick(clock, hours_elapsed=1)
    assert total_passive_ticks == 1  # one tick at hour 6
    assert clock.filled_segments == 1

    clock = create_threat_clock("goblin_raid", segments=6)
    assert check_passive_tick(clock, hours_elapsed=18) == 3

    clock = create_recovery_clock("rest")
    assert check_passive_tick(clock, hours_elapsed=18) == 0

    clock = create_threat_clock("goblin_raid", segments=8)
    clock.filled_segments = 6
    assert check_passive_tick(clock, hours_elapsed=24) == 2
