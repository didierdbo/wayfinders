from wayfinders.clock import create_recovery_clock, tick_clock
from wayfinders.models import Character, GameState


def apply_condition(gs: GameState, character: Character, condition_name: str) -> None:
    if condition_name in character.conditions:
        return  # idempotent; don't stack duplicates (MVP)
    character.conditions.append(condition_name)
    clock_name = f"{character.id}-{condition_name}"
    clock = create_recovery_clock(clock_name)
    gs.active_recovery_clocks[clock.id] = clock
    character.recovery_clock_ids[condition_name] = clock.id


def resolve_recovery(gs: GameState, character: Character) -> list[str]:
    """Call after ticking recovery clocks. Removes healed conditions."""
    removed = []
    for cond, clock_id in list(character.recovery_clock_ids.items()):
        clock = gs.active_recovery_clocks[clock_id]
        if clock.filled_segments >= clock.total_segments:
            character.conditions.remove(cond)
            del character.recovery_clock_ids[cond]
            del gs.active_recovery_clocks[clock_id]
            removed.append(cond)
    return removed


def tick_recovery_clocks(gs: GameState, character: Character, segments: int = 1) -> None:
    """Advance every recovery clock tied to this character by `segments`."""
    for clock_id in character.recovery_clock_ids.values():
        clock = gs.active_recovery_clocks[clock_id]
        tick_clock(clock, segments)
