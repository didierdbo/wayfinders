from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Literal


class Stat(Enum):
    STR = 1
    DEX = 2
    WIS = 3


@dataclass(slots=True)
class Background:
    name: str
    stat_adjustments: dict[Stat, int]
    starting_equipment: list[str]
    incapabilities: list[str]


@dataclass(slots=True)
class Trait:
    name: str
    roll_modifier: dict[Literal["risky", "safe", "loot"], int]
    menu_tag_boosted: list[str]
    menu_tag_suppressed: list[str]
    stress_modifier: int
    vote_tendency: Literal["keep", "risk assessment", "banish on cost"]
    contradicts: list[str]


@dataclass(slots=True)
class Item:
    id: str
    name: str
    category: str
    stat_bonus: dict[Stat, int]
    tier: int
    tags: list[str]
    armor_value: int
    uses_until_degradation: int
    is_provision: bool


@dataclass(slots=True)
class Character:
    id: str
    name: str
    stats: dict[Stat, int]
    max_hp: int
    current_hp: int
    background: Background
    traits: list[Trait]
    inventory: list[Item]
    stress: int
    current_mission_id: str | None
    conditions: list[str]
    recovery_clock_ids: dict[str, str]
    mission_history: list[str]
    backstory_hook: str | None = None
    portrait_id: str | None = None


@dataclass(slots=True)
class Clock:
    id: str
    name: str
    clock_type: str
    total_segments: int
    filled_segments: int
    passive_tick_interval: int | None
    hours_since_last_passive_tick: int


@dataclass(slots=True)
class ActionCard:
    id: str
    name: str
    description: str
    location_types: list[str]
    stat_tested: Stat
    stat_minimum: int | None
    equipment_required: list[str]
    stress_tier: str
    tags: list[str]
    clock_segments: int
    position: str
    effect: str
    max_characters: int
    rewards_on_success: dict[str, object]
    consequences_on_failure: dict[str, object]
    consequences_on_partial: dict[str, object]
    default_priority: int


@dataclass(slots=True)
class Mission:
    id: str
    action_card: ActionCard
    clock: Clock
    assigned_characters: list[str]
    primary_actor_id: str
    hours_elapsed: int
    is_complete: bool
    is_aborted: bool
    support_bonuses: dict[str, int]


@dataclass(slots=True)
class MapNode:
    id: str
    name: str
    node_type: str
    discovery_state: str
    connections: list[str]
    available_action_ids: list[str]
    event_pool: list[str]


@dataclass(slots=True)
class Faction:
    id: str
    name: str
    clock: Clock
    relationship: int


@dataclass(slots=True)
class Party:
    members: list[Character]
    soul_level: int
    cohesion_points: int
    gold: int
    supplies: int
    stash: list[Item]
    identity: str | None
    mission_tag_history: dict[str, int]


@dataclass(slots=True)
class GameState:
    party: Party
    map_nodes: dict[str, MapNode]
    factions: list[Faction]
    active_missions: dict[str, Mission]
    active_threat_clocks: dict[str, Clock]
    active_recovery_clocks: dict[str, Clock]
    game_hour: int
    action_templates: dict[str, ActionCard]
    event_log: list[dict[str, object]]


@dataclass(slots=True)
class Resolution:
    outcome: Literal["full_success", "partial_success", "failure"]
    roll: int
    effective_stat: int
    modifiers: list[int]
