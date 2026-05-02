from slugify import slugify

from wayfinders.models import ActionCard, Clock


def tick_clock(clock: Clock, segments: int) -> bool:
    """
    Advance a clock by `segments`. Returns True if the clock just completed.
    Clamps filled_segments to total_segments (no overflow).
    """
    clock.filled_segments = min(clock.total_segments, clock.filled_segments + segments)
    return clock.filled_segments == clock.total_segments


def check_passive_tick(clock: Clock, hours_elapsed: int) -> int:
    """
    For clocks with passive ticking (threat clocks: every 6 hours).
    Returns the number of passive ticks that should fire given
    hours_elapsed since last check. Updates the clock's internal
    counter.
    """
    if clock.passive_tick_interval is None:
        return 0
    clock.hours_since_last_passive_tick += hours_elapsed
    ticks_fired = 0
    while clock.hours_since_last_passive_tick >= clock.passive_tick_interval:
        clock.hours_since_last_passive_tick -= clock.passive_tick_interval
        if clock.filled_segments < clock.total_segments:
            ticks_fired += 1
        tick_clock(clock, 1)

    return ticks_fired


def create_mission_clock(action_card: ActionCard) -> Clock:
    """Create a mission clock from an action card's segment count."""
    clock = Clock(
        id=action_card.id,
        name=action_card.name,
        clock_type="mission",
        total_segments=action_card.clock_segments,
        filled_segments=0,
        passive_tick_interval=None,
        hours_since_last_passive_tick=0,
    )
    return clock


def create_threat_clock(name: str, segments: int) -> Clock:
    """Create a threat clock with 6-hour passive ticking."""
    clock = Clock(
        id=slugify(name),
        name=name,
        clock_type="threat",
        total_segments=segments,
        filled_segments=0,
        passive_tick_interval=6,
        hours_since_last_passive_tick=0,
    )
    return clock


def create_recovery_clock(name: str) -> Clock:
    """Create a 4-segment recovery clock (no passive ticking)."""
    clock = Clock(
        id=slugify(name),
        name=name,
        clock_type="recovery",
        total_segments=4,
        filled_segments=0,
        passive_tick_interval=None,
        hours_since_last_passive_tick=0,
    )
    return clock


def parse_clock_tick(consequence: str) -> tuple[str, int]:
    splitted = consequence.split("+")
    return splitted[0], int(splitted[1])
