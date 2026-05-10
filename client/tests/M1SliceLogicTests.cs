// M1 slice (etape 5ab / 2026-05-10) -- pin tests for
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

using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
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
}
