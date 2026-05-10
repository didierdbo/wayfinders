"""Tests for PersonaLegacyTag, GameState.persona_legacy slot,
on_mission_resolved hook, and enrich_char_prose.

Test categories:
  1. PersonaLegacyTag dataclass — frozen, hashable, field types.
  2. GameState.persona_legacy slot — init empty, append works.
  3. on_mission_resolved hook — tag creation determinism, log output,
     idempotence (calling twice does NOT duplicate).
  4. enrich_char_prose — 6 cases covering 2 mission_types x 3 outcomes plus
     empty-tags pass-through and determinism.
"""

from __future__ import annotations

import logging

import pytest

from wayfinders.api.mission_legacy import enrich_char_prose, on_mission_resolved
from wayfinders.api.world_tick_models import (
    EmergentMission,
    PersonaLegacyTag,
)
from wayfinders.ml.schemas.character import CharacterState
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

# ---------------------------------------------------------------------------
# Shared fixtures
# ---------------------------------------------------------------------------


def _make_mission(
    *,
    mission_id: str = "test-mission-001",
    mission_type: str = "scout_route",
    region: str = "Fendelune",
    seed: int = 42,
) -> EmergentMission:
    """Build a minimal EmergentMission for testing."""
    return EmergentMission(
        id=mission_id,
        type=mission_type,  # type: ignore[arg-type]
        narrative_hook="A matter of route scouting in Fendelune — the road ahead is uncharted.",
        eligible_personas=[],
        difficulty="mid",
        region=region,
        deadline_ticks=None,
        outcome=None,
        seed=seed,
    )


def _make_game_state() -> GameState:
    """Build a minimal GameState with no legacy tags."""
    sword = Item(
        id="sw1",
        name="Sword",
        category="weapon",
        stat_bonus={},
        tier=1,
        tags=[],
        armor_value=0,
        uses_until_degradation=5,
        is_provision=False,
    )
    trait = Trait(
        name="Cautious",
        roll_modifier={"risky": -1},
        menu_tag_boosted=[],
        menu_tag_suppressed=[],
        stress_modifier=-1,
        vote_tendency="abstain",
        contradicts=["Bold"],
    )
    char = Character(
        id="kira",
        name="Kira",
        stats={Stat.STR: 12, Stat.DEX: 14, Stat.WIS: 10},
        max_hp=20,
        current_hp=20,
        background=Background(
            name="Scout",
            stat_adjustments={Stat.DEX: 2},
            starting_equipment=["short bow"],
            incapabilities=[],
        ),
        traits=[trait],
        inventory=[sword],
        stress=0,
        current_mission_id=None,
        conditions=[],
        recovery_clock_ids={},
        mission_history=[],
    )
    party = Party(
        members=[char],
        soul_level=0,
        cohesion_points=0,
        gold=0,
        supplies=10,
        stash=[],
        identity=None,
        mission_tag_history={},
    )
    action = ActionCard(
        id="ac1",
        name="Scout",
        description="Scouting action",
        location_types=[],
        stat_tested=Stat.DEX,
        stat_minimum=None,
        equipment_required=[],
        stress_tier="any",
        tags=["scouting"],
        clock_segments=2,
        position="risky",
        effect="minor",
        max_characters=2,
        rewards_on_success={},
        consequences_on_failure={},
        consequences_on_partial={},
        default_priority=1,
    )
    return GameState(
        party=party,
        map_nodes={
            "1": MapNode(
                id="1",
                name="Fendelune Gate",
                node_type="settlement",
                discovery_state="discovered",
                connections=[],
                available_action_ids=[],
                event_pool=[],
            )
        },
        factions=[],
        active_missions={},
        active_threat_clocks={},
        active_recovery_clocks={},
        game_hour=0,
        action_templates={"sc1": action},
        event_log=[],
        # persona_legacy defaults to {}
    )


def _make_char_state() -> CharacterState:
    """Build a minimal CharacterState for enrich_char_prose tests."""
    return CharacterState(
        name="Kira",
        char_class="scout",
        faction="Ridgewatch company",
        str_bucket="mid",
        dex_bucket="high",
        wis_bucket="mid",
        hp_bucket="unhurt",
        stress_bucket="composed",
    )


# ---------------------------------------------------------------------------
# 1. PersonaLegacyTag dataclass
# ---------------------------------------------------------------------------


class TestPersonaLegacyTagDataclass:
    def test_frozen(self) -> None:
        """PersonaLegacyTag must be frozen — mutation raises FrozenInstanceError."""
        import dataclasses

        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        with pytest.raises(dataclasses.FrozenInstanceError):
            tag.outcome = "failure"  # type: ignore[misc]  # frozen dataclass raises

    def test_hashable(self) -> None:
        """Frozen dataclasses are hashable — usable in sets and as dict keys."""
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        s: set[PersonaLegacyTag] = {tag}
        assert tag in s

    def test_equal_tags_have_same_hash(self) -> None:
        """Two tags with identical field values must be equal and share hash."""
        tag_a = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        tag_b = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        assert tag_a == tag_b
        assert hash(tag_a) == hash(tag_b)

    def test_field_types(self) -> None:
        """All fields present and of expected types."""
        tag = PersonaLegacyTag(
            persona_id="hodge",
            mission_id="m002",
            mission_type="parley_local",
            region="Brescaille",
            actor_target="bourgmestre",
            outcome="partial",
            earned_at_tick=10,
        )
        assert isinstance(tag.persona_id, str)
        assert isinstance(tag.mission_id, str)
        assert isinstance(tag.mission_type, str)
        assert isinstance(tag.region, str)
        assert isinstance(tag.actor_target, str)
        assert isinstance(tag.outcome, str)
        assert isinstance(tag.earned_at_tick, int)

    def test_actor_target_can_be_none(self) -> None:
        """actor_target is Optional[str] — None is valid for scout missions."""
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m003",
            mission_type="scout_route",
            region="Ridgepass",
            actor_target=None,
            outcome="failure",
            earned_at_tick=3,
        )
        assert tag.actor_target is None

    def test_different_outcome_not_equal(self) -> None:
        """Tags differing only in outcome must not be equal."""
        base = {
            "persona_id": "kira",
            "mission_id": "m001",
            "mission_type": "scout_route",
            "region": "Fendelune",
            "actor_target": None,
            "earned_at_tick": 5,
        }
        tag_success = PersonaLegacyTag(**base, outcome="success")  # type: ignore[arg-type]
        tag_failure = PersonaLegacyTag(**base, outcome="failure")  # type: ignore[arg-type]
        assert tag_success != tag_failure


# ---------------------------------------------------------------------------
# 2. GameState.persona_legacy slot
# ---------------------------------------------------------------------------


class TestGameStatePersonaLegacySlot:
    def test_init_empty(self) -> None:
        """persona_legacy defaults to an empty dict on new GameState."""
        gs = _make_game_state()
        assert gs.persona_legacy == {}

    def test_append_single_tag(self) -> None:
        """Appending a tag to a new persona creates the list and stores the tag."""
        gs = _make_game_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        gs.persona_legacy["kira"] = [tag]
        assert len(gs.persona_legacy["kira"]) == 1
        assert gs.persona_legacy["kira"][0] == tag

    def test_append_multiple_tags_same_persona(self) -> None:
        """Multiple tags for the same persona accumulate in order."""
        gs = _make_game_state()
        tag1 = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        tag2 = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m002",
            mission_type="parley_local",
            region="Brescaille",
            actor_target=None,
            outcome="partial",
            earned_at_tick=10,
        )
        gs.persona_legacy["kira"] = [tag1, tag2]
        assert len(gs.persona_legacy["kira"]) == 2
        assert gs.persona_legacy["kira"][1].mission_id == "m002"

    def test_separate_personas_independent(self) -> None:
        """Tags for different personas are stored independently."""
        gs = _make_game_state()
        kira_tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        hodge_tag = PersonaLegacyTag(
            persona_id="hodge",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        gs.persona_legacy["kira"] = [kira_tag]
        gs.persona_legacy["hodge"] = [hodge_tag]
        assert "kira" in gs.persona_legacy
        assert "hodge" in gs.persona_legacy
        assert gs.persona_legacy["kira"][0].persona_id == "kira"
        assert gs.persona_legacy["hodge"][0].persona_id == "hodge"

    def test_game_state_instances_do_not_share_legacy(self) -> None:
        """Two separate GameState instances must have independent persona_legacy dicts."""
        gs1 = _make_game_state()
        gs2 = _make_game_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        gs1.persona_legacy["kira"] = [tag]
        # gs2 must be unaffected.
        assert gs2.persona_legacy == {}


# ---------------------------------------------------------------------------
# 3. on_mission_resolved hook
# ---------------------------------------------------------------------------


class TestOnMissionResolved:
    def test_creates_tag_for_single_persona(self) -> None:
        """Hook creates exactly one tag for a single persona."""
        gs = _make_game_state()
        mission = _make_mission()
        on_mission_resolved(mission, "success", tick=5, game_state=gs, persona_ids=["kira"])

        assert "kira" in gs.persona_legacy
        assert len(gs.persona_legacy["kira"]) == 1

    def test_tag_fields_are_deterministic(self) -> None:
        """Tag fields exactly match the mission and outcome arguments."""
        gs = _make_game_state()
        mission = _make_mission(
            mission_id="m-abc",
            mission_type="parley_local",
            region="Brescaille",
        )
        on_mission_resolved(mission, "partial", tick=7, game_state=gs, persona_ids=["hodge"])

        tag = gs.persona_legacy["hodge"][0]
        assert tag.persona_id == "hodge"
        assert tag.mission_id == "m-abc"
        assert tag.mission_type == "parley_local"
        assert tag.region == "Brescaille"
        assert tag.outcome == "partial"
        assert tag.earned_at_tick == 7

    def test_creates_tags_for_multiple_personas(self) -> None:
        """Hook creates one tag per persona in the provided list."""
        gs = _make_game_state()
        mission = _make_mission()
        on_mission_resolved(
            mission, "failure", tick=3, game_state=gs, persona_ids=["kira", "hodge"]
        )

        assert "kira" in gs.persona_legacy
        assert "hodge" in gs.persona_legacy
        assert gs.persona_legacy["kira"][0].outcome == "failure"
        assert gs.persona_legacy["hodge"][0].outcome == "failure"

    def test_no_tags_when_persona_ids_empty(self) -> None:
        """Default empty persona_ids produces no tags."""
        gs = _make_game_state()
        mission = _make_mission()
        on_mission_resolved(mission, "success", tick=5, game_state=gs)

        assert gs.persona_legacy == {}

    def test_idempotence_same_call_twice(self) -> None:
        """Calling the hook twice with same (persona_id, mission.id) adds only one tag."""
        gs = _make_game_state()
        mission = _make_mission(mission_id="m-idem")
        on_mission_resolved(mission, "success", tick=5, game_state=gs, persona_ids=["kira"])
        on_mission_resolved(mission, "success", tick=5, game_state=gs, persona_ids=["kira"])

        assert len(gs.persona_legacy["kira"]) == 1

    def test_idempotence_different_tick_same_mission(self) -> None:
        """Second call with same mission_id but different tick is still a no-op per persona."""
        gs = _make_game_state()
        mission = _make_mission(mission_id="m-idem")
        on_mission_resolved(mission, "success", tick=5, game_state=gs, persona_ids=["kira"])
        # Simulate a late duplicate call (e.g. retry) at a different tick.
        on_mission_resolved(mission, "success", tick=99, game_state=gs, persona_ids=["kira"])

        assert len(gs.persona_legacy["kira"]) == 1
        # The original tick must be preserved.
        assert gs.persona_legacy["kira"][0].earned_at_tick == 5

    def test_different_missions_accumulate(self) -> None:
        """Two different missions for the same persona produce two tags."""
        gs = _make_game_state()
        m1 = _make_mission(mission_id="m-001")
        m2 = _make_mission(mission_id="m-002", region="Ironshore")
        on_mission_resolved(m1, "success", tick=5, game_state=gs, persona_ids=["kira"])
        on_mission_resolved(m2, "partial", tick=10, game_state=gs, persona_ids=["kira"])

        assert len(gs.persona_legacy["kira"]) == 2
        assert gs.persona_legacy["kira"][0].mission_id == "m-001"
        assert gs.persona_legacy["kira"][1].mission_id == "m-002"

    def test_log_info_emitted_on_success(self, caplog: pytest.LogCaptureFixture) -> None:
        """Hook emits an INFO log with [LEGACY] prefix on tag creation."""
        gs = _make_game_state()
        mission = _make_mission(mission_id="m-log")
        with caplog.at_level(logging.INFO, logger="wayfinders.api.mission_legacy"):
            on_mission_resolved(mission, "success", tick=5, game_state=gs, persona_ids=["kira"])

        # Must contain [LEGACY] and persona id in the log output.
        assert any("[LEGACY]" in r.message and "kira" in r.message for r in caplog.records)

    def test_log_not_emitted_on_duplicate(self, caplog: pytest.LogCaptureFixture) -> None:
        """Duplicate hook call must NOT emit an INFO [LEGACY] tag-earned log."""
        gs = _make_game_state()
        mission = _make_mission(mission_id="m-dup")
        on_mission_resolved(mission, "success", tick=5, game_state=gs, persona_ids=["kira"])

        # Clear records from the first call.
        caplog.clear()

        with caplog.at_level(logging.DEBUG, logger="wayfinders.api.mission_legacy"):
            on_mission_resolved(mission, "success", tick=5, game_state=gs, persona_ids=["kira"])

        # INFO records with "earned tag" must NOT appear (only a DEBUG skip log).
        earned_logs = [
            r for r in caplog.records if r.levelno == logging.INFO and "earned tag" in r.message
        ]
        assert earned_logs == []


# ---------------------------------------------------------------------------
# 4. enrich_char_prose
# ---------------------------------------------------------------------------


class TestEnrichCharProse:
    def test_empty_tags_returns_base_prose(self) -> None:
        """No legacy tags → output equals render_character() unchanged."""
        from wayfinders.ml.schemas.character import render_character

        char = _make_char_state()
        result = enrich_char_prose(char, [])
        assert result == render_character(char)

    def test_scout_success(self) -> None:
        """scout_route + success → 'Has scouted the route to Fendelune.'"""
        char = _make_char_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        result = enrich_char_prose(char, [tag])
        assert "Has scouted the route to Fendelune." in result

    def test_scout_partial(self) -> None:
        """scout_route + partial → hedged sentence."""
        char = _make_char_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m002",
            mission_type="scout_route",
            region="Ridgepass",
            actor_target=None,
            outcome="partial",
            earned_at_tick=8,
        )
        result = enrich_char_prose(char, [tag])
        assert "Ridgepass" in result
        assert "incomplete" in result

    def test_scout_failure(self) -> None:
        """scout_route + failure → 'Attempted to scout … but failed.'"""
        char = _make_char_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m003",
            mission_type="scout_route",
            region="Ironshore",
            actor_target=None,
            outcome="failure",
            earned_at_tick=12,
        )
        result = enrich_char_prose(char, [tag])
        assert "Attempted to scout the route to Ironshore but failed." in result

    def test_parley_with_target_partial(self) -> None:
        """parley_local + actor_target + partial → '(uncertain outcome)' sentence."""
        char = _make_char_state()
        tag = PersonaLegacyTag(
            persona_id="hodge",
            mission_id="m004",
            mission_type="parley_local",
            region="Brescaille",
            actor_target="bourgmestre",
            outcome="partial",
            earned_at_tick=6,
        )
        result = enrich_char_prose(char, [tag])
        assert "bourgmestre" in result
        assert "Brescaille" in result
        assert "uncertain outcome" in result

    def test_parley_anon_success(self) -> None:
        """parley_local + actor_target=None + success → 'local contact' sentence."""
        char = _make_char_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m005",
            mission_type="parley_local",
            region="Halfgate",
            actor_target=None,
            outcome="success",
            earned_at_tick=4,
        )
        result = enrich_char_prose(char, [tag])
        assert "local contact" in result
        assert "Halfgate" in result

    def test_parley_anon_failure(self) -> None:
        """parley_local + actor_target=None + failure → 'contact refused' sentence."""
        char = _make_char_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m006",
            mission_type="parley_local",
            region="Vauldrun",
            actor_target=None,
            outcome="failure",
            earned_at_tick=9,
        )
        result = enrich_char_prose(char, [tag])
        assert "Vauldrun" in result
        assert "refused" in result

    def test_multiple_tags_chronological_order(self) -> None:
        """Multiple tags render in earned_at_tick ascending order."""
        char = _make_char_state()
        tag_early = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=3,
        )
        tag_late = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m002",
            mission_type="scout_route",
            region="Ridgepass",
            actor_target=None,
            outcome="partial",
            earned_at_tick=10,
        )
        # Pass in reverse order — output must still be chronological.
        result = enrich_char_prose(char, [tag_late, tag_early])
        fendelune_pos = result.index("Fendelune")
        ridgepass_pos = result.index("Ridgepass")
        assert fendelune_pos < ridgepass_pos, (
            "Earlier tick (Fendelune) must appear before later tick (Ridgepass)"
        )

    def test_deterministic_same_tags_same_output(self) -> None:
        """enrich_char_prose is deterministic — same inputs produce same string."""
        char = _make_char_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        r1 = enrich_char_prose(char, [tag])
        r2 = enrich_char_prose(char, [tag])
        assert r1 == r2

    def test_char_state_not_mutated(self) -> None:
        """char_state fields must be identical after enrich_char_prose call."""
        char = _make_char_state()
        original_name = char.name
        original_class = char.char_class
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        enrich_char_prose(char, [tag])
        assert char.name == original_name
        assert char.char_class == original_class

    def test_output_starts_with_base_prose(self) -> None:
        """Enriched prose must start with the unmodified base character prose."""
        from wayfinders.ml.schemas.character import render_character

        char = _make_char_state()
        tag = PersonaLegacyTag(
            persona_id="kira",
            mission_id="m001",
            mission_type="scout_route",
            region="Fendelune",
            actor_target=None,
            outcome="success",
            earned_at_tick=5,
        )
        result = enrich_char_prose(char, [tag])
        base = render_character(char)
        assert result.startswith(base)
