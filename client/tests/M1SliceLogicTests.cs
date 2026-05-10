// M1 slice (etape 5ab+5c / 2026-05-10) -- pin tests for
// M1SliceLogic. The runtime M1Slice scene wires
// SubViewportContainers, CanvasLayers, the golden 2D proxy, and
// the cross-panel signal ; the contract -- drop decision table,
// hit test, auto-rabat lock, eligibility match, payload shape --
// lives in M1SliceLogic and is pinned here.
//
// What this file pins :
//   1. DecideDropOutcome decision table (5 branches).
//   2. IsCursorOverSlot rect math (inclusive left/top, exclusive
//      right/bottom -- Godot Rect2.HasPoint convention).
//   3. ShouldDispatchAutoRabatOnMissionEvent always returns false
//      (Pattern P6 -- Mission stays fixed, never broadcasts).
//   4. AssignmentEvent payload shape + defensive empty-string throw.
//   5. PersonaAssignedToMissionEventName stable string.
//   6. IsPersonaInEligibleList case-sensitive string match.
//   7. (5c) AppendTagToLegacyMap : create-list-if-missing,
//      multiple-tags-same-persona, defensive throws.
//   8. (5c) PopHeadMission : FIFO semantics, empty-list returns
//      null instead of throwing.
//   9. (5c) ShouldRenderEmptyMissionState : pop-to-empty / pop-to-
//      non-empty / negative-input throw.
//  10. (5c) MissionResolvedEventName stable string.

using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;
using static Wayfinders.Client.Scripts.Screens.M1SliceLogic;

namespace Wayfinders.Client.Tests;

public sealed class M1SliceLogicTests
{
    // ====================================================================
    // DecideDropOutcome -- the 5-branch decision table
    // ====================================================================

    [Fact]
    public void Drop_outside_slot_is_cancelled_missed_slot()
    {
        var d = DecideDropOutcome(
            cursorOverMissionSlot: false,
            missionPresent: true,
            personaAlreadyAssigned: false,
            personaInEligibleList: true);
        Assert.Equal(DropDecision.CancelledMissedSlot, d);
    }

    [Fact]
    public void Drop_outside_slot_takes_priority_over_other_failures()
    {
        // If the cursor is not over the slot, none of the other
        // conditions matter. Catches a regression where the
        // condition order in DecideDropOutcome flips.
        var d = DecideDropOutcome(
            cursorOverMissionSlot: false,
            missionPresent: false,
            personaAlreadyAssigned: true,
            personaInEligibleList: false);
        Assert.Equal(DropDecision.CancelledMissedSlot, d);
    }

    [Fact]
    public void Drop_on_slot_with_no_mission_is_cancelled_no_mission()
    {
        // Defensive : the well-formed runtime hides the slot when
        // PendingMissions is empty, but the helper still has to
        // handle the race where a tick resolved a mission while
        // the proxy was mid-flight.
        var d = DecideDropOutcome(
            cursorOverMissionSlot: true,
            missionPresent: false,
            personaAlreadyAssigned: false,
            personaInEligibleList: true);
        Assert.Equal(DropDecision.CancelledNoMission, d);
    }

    [Fact]
    public void Drop_with_already_assigned_persona_is_cancelled()
    {
        // v1 has no "recall from mission" UI ; double-assigning
        // the same persona is a no-op (cf. CompagniePanelProbe
        // OnPersonaInputEvent's _personasInMission guard).
        var d = DecideDropOutcome(
            cursorOverMissionSlot: true,
            missionPresent: true,
            personaAlreadyAssigned: true,
            personaInEligibleList: true);
        Assert.Equal(DropDecision.CancelledAlreadyAssigned, d);
    }

    [Fact]
    public void Drop_with_not_eligible_persona_is_cancelled()
    {
        // Symmetric with MissionPanelLogic.AffectButtonState
        // VisibleDisabled : if click-Affect would refuse, drag-drop
        // must too. Catches a regression where the drag-drop path
        // accidentally bypasses the EligiblePersonas filter.
        var d = DecideDropOutcome(
            cursorOverMissionSlot: true,
            missionPresent: true,
            personaAlreadyAssigned: false,
            personaInEligibleList: false);
        Assert.Equal(DropDecision.CancelledNotEligible, d);
    }

    [Fact]
    public void Drop_with_all_conditions_satisfied_is_accepted()
    {
        var d = DecideDropOutcome(
            cursorOverMissionSlot: true,
            missionPresent: true,
            personaAlreadyAssigned: false,
            personaInEligibleList: true);
        Assert.Equal(DropDecision.Accepted, d);
    }

    // ====================================================================
    // IsCursorOverSlot -- rect math inclusive/exclusive bounds
    // ====================================================================

    [Fact]
    public void Cursor_inside_slot_is_a_hit()
    {
        var slot = new Rect(Left: 100, Top: 200, Width: 50, Height: 80);
        Assert.True(IsCursorOverSlot(new Point(125, 240), slot));
    }

    [Fact]
    public void Cursor_on_left_top_corner_is_a_hit()
    {
        // Inclusive on left/top -- pixel exactly at (left, top)
        // counts as "inside".
        var slot = new Rect(Left: 100, Top: 200, Width: 50, Height: 80);
        Assert.True(IsCursorOverSlot(new Point(100, 200), slot));
    }

    [Fact]
    public void Cursor_on_right_edge_is_not_a_hit()
    {
        // Exclusive on right -- pixel at (left+width, ...) is
        // outside. Matches Godot Rect2.HasPoint convention.
        var slot = new Rect(Left: 100, Top: 200, Width: 50, Height: 80);
        Assert.False(IsCursorOverSlot(new Point(150, 240), slot));
    }

    [Fact]
    public void Cursor_on_bottom_edge_is_not_a_hit()
    {
        // Exclusive on bottom -- pixel at (..., top+height) is
        // outside.
        var slot = new Rect(Left: 100, Top: 200, Width: 50, Height: 80);
        Assert.False(IsCursorOverSlot(new Point(125, 280), slot));
    }

    [Fact]
    public void Cursor_far_outside_is_not_a_hit()
    {
        var slot = new Rect(Left: 100, Top: 200, Width: 50, Height: 80);
        Assert.False(IsCursorOverSlot(new Point(0, 0), slot));
        Assert.False(IsCursorOverSlot(new Point(1000, 1000), slot));
    }

    // ====================================================================
    // Pattern P6 lock : Mission never broadcasts auto-rabat
    // ====================================================================

    [Fact]
    public void Mission_new_content_does_not_trigger_compagnie_auto_rabat()
    {
        // Pattern P6 (Didier 2026-05-10) : Mission stays anchored
        // right, never opens-as-an-event, never broadcasts
        // OtherLayerOpened to Compagnie. Locked numerically here
        // so a future "wire up auto-rabat everywhere" refactor
        // breaks the test.
        Assert.False(
            ShouldDispatchAutoRabatOnMissionEvent(
                MissionPanelEvent.NewMissionRendered));
    }

    // ====================================================================
    // AssignmentEvent payload shape + defensive throws
    // ====================================================================

    [Fact]
    public void Assignment_event_carries_persona_and_mission_ids()
    {
        var ev = BuildAssignmentEvent("hodge", "abc-123");
        Assert.Equal("hodge", ev.PersonaId);
        Assert.Equal("abc-123", ev.MissionId);
    }

    [Fact]
    public void Assignment_event_rejects_empty_persona_id()
    {
        Assert.Throws<System.ArgumentException>(
            () => BuildAssignmentEvent("", "abc-123"));
        Assert.Throws<System.ArgumentException>(
            () => BuildAssignmentEvent(null!, "abc-123"));
    }

    [Fact]
    public void Assignment_event_rejects_empty_mission_id()
    {
        Assert.Throws<System.ArgumentException>(
            () => BuildAssignmentEvent("hodge", ""));
        Assert.Throws<System.ArgumentException>(
            () => BuildAssignmentEvent("hodge", null!));
    }

    [Fact]
    public void Persona_assigned_to_mission_event_name_is_stable()
    {
        // Stable string the runtime emits as a Godot signal ;
        // consumers (Mission panel, future analytics) subscribe
        // by this exact name. Pinned so a rename in M1Slice.cs
        // alone fails the test.
        Assert.Equal("persona_assigned_to_mission",
            PersonaAssignedToMissionEventName);
    }

    // ====================================================================
    // IsPersonaInEligibleList -- case-sensitive match
    // ====================================================================

    [Fact]
    public void Eligible_list_finds_present_persona()
    {
        IReadOnlyList<string> eligible = new[] { "hodge", "mirelle" };
        Assert.True(IsPersonaInEligibleList("hodge", eligible));
        Assert.True(IsPersonaInEligibleList("mirelle", eligible));
    }

    [Fact]
    public void Eligible_list_misses_absent_persona()
    {
        IReadOnlyList<string> eligible = new[] { "hodge", "mirelle" };
        Assert.False(IsPersonaInEligibleList("wattan", eligible));
    }

    [Fact]
    public void Eligible_list_match_is_case_sensitive()
    {
        // Persona ids are canonical-case ; a case-insensitive
        // match would mask a bug where the Compagnie panel
        // canonicalises differently from the server's
        // EligiblePersonas list.
        IReadOnlyList<string> eligible = new[] { "hodge" };
        Assert.False(IsPersonaInEligibleList("Hodge", eligible));
        Assert.False(IsPersonaInEligibleList("HODGE", eligible));
    }

    [Fact]
    public void Eligible_list_handles_empty_inputs()
    {
        Assert.False(IsPersonaInEligibleList("hodge", System.Array.Empty<string>()));
        Assert.False(IsPersonaInEligibleList("", new[] { "hodge" }));
        Assert.False(IsPersonaInEligibleList(null!, new[] { "hodge" }));
    }

    // ====================================================================
    // 5c -- AppendTagToLegacyMap
    // ====================================================================

    [Fact]
    public void Append_tag_creates_list_for_first_persona_appearance()
    {
        // First time we see persona "hodge" : the helper must
        // allocate a fresh List<PersonaLegacyTagDto> under that
        // key and seed it with the tag.
        var map = new Dictionary<string, List<PersonaLegacyTagDto>>();
        var tag = SampleTag(personaId: "hodge", missionId: "m1");

        AppendTagToLegacyMap(map, tag);

        Assert.True(map.ContainsKey("hodge"));
        Assert.Single(map["hodge"]);
        Assert.Equal("m1", map["hodge"][0].MissionId);
    }

    [Fact]
    public void Append_tag_appends_to_existing_persona_list()
    {
        // Second tag for "hodge" must end up in the same list,
        // preserving insertion order. Mirrors the runtime case
        // where a persona resolves multiple missions across a
        // session.
        var map = new Dictionary<string, List<PersonaLegacyTagDto>>();
        AppendTagToLegacyMap(map, SampleTag(personaId: "hodge", missionId: "m1"));
        AppendTagToLegacyMap(map, SampleTag(personaId: "hodge", missionId: "m2"));

        Assert.Single(map);
        Assert.Equal(2, map["hodge"].Count);
        Assert.Equal("m1", map["hodge"][0].MissionId);
        Assert.Equal("m2", map["hodge"][1].MissionId);
    }

    [Fact]
    public void Append_tag_keeps_per_persona_lists_independent()
    {
        // Two personas, one tag each : each persona has their own
        // list, no cross-contamination. Catches a regression where
        // the helper accidentally shares one list across keys.
        var map = new Dictionary<string, List<PersonaLegacyTagDto>>();
        AppendTagToLegacyMap(map, SampleTag(personaId: "hodge", missionId: "m1"));
        AppendTagToLegacyMap(map, SampleTag(personaId: "mirelle", missionId: "m2"));

        Assert.Equal(2, map.Count);
        Assert.Single(map["hodge"]);
        Assert.Single(map["mirelle"]);
        Assert.Equal("m1", map["hodge"][0].MissionId);
        Assert.Equal("m2", map["mirelle"][0].MissionId);
    }

    [Fact]
    public void Append_tag_does_not_dedup_same_persona_mission_pair()
    {
        // M1 simple impl : append blindly. The natural key
        // (persona_id, mission_id) is enforced server-side inside
        // ONE resolve call ; cross-call dedup (e.g. retrying a
        // transient ApiError.NotReachable) is M2 work. This test
        // pins the M1 contract -- if a future change adds dedup
        // here, the test breaks and forces the conversation.
        var map = new Dictionary<string, List<PersonaLegacyTagDto>>();
        AppendTagToLegacyMap(map, SampleTag(personaId: "hodge", missionId: "m1"));
        AppendTagToLegacyMap(map, SampleTag(personaId: "hodge", missionId: "m1"));

        Assert.Equal(2, map["hodge"].Count);
    }

    [Fact]
    public void Append_tag_throws_on_null_map()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            AppendTagToLegacyMap(null!, SampleTag()));
    }

    [Fact]
    public void Append_tag_throws_on_null_tag()
    {
        var map = new Dictionary<string, List<PersonaLegacyTagDto>>();
        Assert.Throws<System.ArgumentNullException>(() =>
            AppendTagToLegacyMap(map, null!));
    }

    [Fact]
    public void Append_tag_throws_on_empty_persona_id()
    {
        // Defensive : a tag with an empty persona id is a
        // contract violation. Throwing surfaces it loudly rather
        // than poisoning the dictionary with an "" key.
        var map = new Dictionary<string, List<PersonaLegacyTagDto>>();
        Assert.Throws<System.ArgumentException>(() =>
            AppendTagToLegacyMap(map, SampleTag(personaId: "")));
    }

    // ====================================================================
    // 5c -- PopHeadMission FIFO + empty-list defence
    // ====================================================================

    [Fact]
    public void Pop_head_removes_first_mission_and_returns_its_id()
    {
        // FIFO semantics : the head (index 0) is what gets popped,
        // and its id comes back so the runtime can log / correlate.
        var pending = new List<EmergentMissionDto>
        {
            SampleMission(id: "m1"),
            SampleMission(id: "m2"),
            SampleMission(id: "m3"),
        };

        var popped = PopHeadMission(pending);

        Assert.Equal("m1", popped);
        Assert.Equal(2, pending.Count);
        Assert.Equal("m2", pending[0].Id);
        Assert.Equal("m3", pending[1].Id);
    }

    [Fact]
    public void Pop_head_returns_null_on_empty_list()
    {
        // Defensive contract : the runtime calls this AFTER a
        // resolve round-trip succeeded, so the queue should be
        // non-empty -- but a concurrent tick could have popped
        // it (e.g. if a future M2 feature auto-resolves stale
        // missions). Returning null is graceful ; throwing
        // would crash a successful resolve.
        var pending = new List<EmergentMissionDto>();
        var popped = PopHeadMission(pending);

        Assert.Null(popped);
        Assert.Empty(pending);
    }

    [Fact]
    public void Pop_head_with_single_mission_empties_the_queue()
    {
        var pending = new List<EmergentMissionDto> { SampleMission(id: "only") };
        var popped = PopHeadMission(pending);

        Assert.Equal("only", popped);
        Assert.Empty(pending);
    }

    [Fact]
    public void Pop_head_throws_on_null_list()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            PopHeadMission(null!));
    }

    // ====================================================================
    // 5c -- ShouldRenderEmptyMissionState
    // ====================================================================

    [Fact]
    public void Empty_state_required_when_no_pending_missions()
    {
        // After PopHeadMission empties the queue, the panel
        // must flip to "(none — waiting for tick cadence)".
        Assert.True(ShouldRenderEmptyMissionState(0));
    }

    [Fact]
    public void Empty_state_not_required_when_one_or_more_pending_missions()
    {
        // Multiple missions queue : the runtime renders the
        // head, not the empty state.
        Assert.False(ShouldRenderEmptyMissionState(1));
        Assert.False(ShouldRenderEmptyMissionState(5));
    }

    [Fact]
    public void Empty_state_throws_on_negative_count()
    {
        // Defensive : a negative pending count means the caller
        // counted wrong (or List.Count returned a corrupted
        // value). Surface it as a contract violation rather than
        // returning a potentially misleading bool.
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            ShouldRenderEmptyMissionState(-1));
    }

    // ====================================================================
    // 5c -- MissionResolved event name pin
    // ====================================================================

    [Fact]
    public void Mission_resolved_event_name_is_stable()
    {
        // Stable string the runtime emits after the resolve
        // pipeline completes (tags appended, head popped). 5d
        // visualisation + future analytics subscribe by this
        // exact name -- a rename in M1Slice.cs alone breaks the
        // test, not silently drops listeners.
        Assert.Equal("mission_resolved", MissionResolvedEventName);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static PersonaLegacyTagDto SampleTag(
        string personaId = "hodge",
        string missionId = "m1",
        string missionType = "scout_route",
        string region = "halfgate",
        string outcome = "success",
        int earnedAtTick = 5)
    {
        return new PersonaLegacyTagDto(
            PersonaId: personaId,
            MissionId: missionId,
            MissionType: missionType,
            Region: region,
            ActorTarget: null,
            Outcome: outcome,
            EarnedAtTick: earnedAtTick);
    }

    private static EmergentMissionDto SampleMission(
        string id = "abc-123",
        string type = "scout_route",
        string region = "halfgate",
        string difficulty = "mid",
        IReadOnlyList<string>? eligible = null)
    {
        return new EmergentMissionDto(
            Id: id,
            Type: type,
            NarrativeHook: "Une affaire de scout_route dans halfgate.",
            EligiblePersonas: eligible ?? new[] { "hodge", "mirelle" },
            Difficulty: difficulty,
            Region: region,
            DeadlineTicks: null,
            Outcome: null,
            Seed: 42L);
    }
}
