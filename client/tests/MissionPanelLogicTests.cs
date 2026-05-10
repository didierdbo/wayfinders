// M1 mission-emergence (etape 4c) — pin tests for the Mission panel
// decision helper. The Godot-bound MissionPanelProbe wires Buttons,
// Labels, and the async ApiClient call ; the contract — Affect-button
// state machine, resolve-request shape, status text format — lives in
// MissionPanelLogic and is pinned here.
//
// What this file pins :
//   1. AffectButtonState transitions for the 6 cases (no mission /
//      mission + no selection / mission + selection-eligible /
//      mission + selection-NOT-eligible).
//   2. DeclineButtonState : enabled iff a mission is present.
//   3. BuildAffectRequest produces a valid payload with the persona
//      in AssignedPersonas, M1 outcome stub = success.
//   4. BuildAffectRequest throws on empty personaId (defensive).
//   5. BuildDeclineRequest produces empty AssignedPersonas (the
//      server-side declined-mission path).
//   6. Status text formatting handles the empty-eligible Varn-(c)
//      case + the singular vs plural legacy-tag toast.

using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;
using static Wayfinders.Client.Scripts.Screens.MissionPanelLogic;

namespace Wayfinders.Client.Tests;

public sealed class MissionPanelLogicTests
{
    // ====================================================================
    // AffectButtonState transitions
    // ====================================================================

    [Fact]
    public void Affect_button_hidden_when_no_mission()
    {
        var state = DecideAffectButtonState(mission: null, selectedPersonaId: null);
        Assert.Equal(AffectButtonState.Hidden, state);
    }

    [Fact]
    public void Affect_button_hidden_when_no_mission_even_with_selection()
    {
        // Defensive : a stale selection without a mission. The UI
        // probe should never reach this, but the helper must
        // collapse to Hidden cleanly.
        var state = DecideAffectButtonState(mission: null, selectedPersonaId: "kira");
        Assert.Equal(AffectButtonState.Hidden, state);
    }

    [Fact]
    public void Affect_button_disabled_when_mission_but_no_selection()
    {
        var state = DecideAffectButtonState(SampleMission(), selectedPersonaId: null);
        Assert.Equal(AffectButtonState.VisibleDisabled, state);
    }

    [Fact]
    public void Affect_button_disabled_when_mission_but_empty_string_selection()
    {
        // null and "" must both be treated as "no selection".
        var state = DecideAffectButtonState(SampleMission(), selectedPersonaId: "");
        Assert.Equal(AffectButtonState.VisibleDisabled, state);
    }

    [Fact]
    public void Affect_button_enabled_when_selection_in_eligible_personas()
    {
        var mission = SampleMission(eligible: new[] { "kira", "hodge" });
        var state = DecideAffectButtonState(mission, selectedPersonaId: "kira");
        Assert.Equal(AffectButtonState.Enabled, state);
    }

    [Fact]
    public void Affect_button_disabled_when_selection_not_in_eligible_personas()
    {
        // Defensive : if the UI gets out of sync (a tick mutated the
        // mission while the player was clicking), don't post a
        // request the server will reject.
        var mission = SampleMission(eligible: new[] { "kira", "hodge" });
        var state = DecideAffectButtonState(mission, selectedPersonaId: "wattan");
        Assert.Equal(AffectButtonState.VisibleDisabled, state);
    }

    [Fact]
    public void Affect_button_disabled_when_no_eligible_personas()
    {
        // Varn option (c) : mission emerges with empty eligible.
        // The Décliner path is the only valid action ; Affect stays
        // disabled regardless of selection (there's nothing to
        // select).
        var mission = SampleMission(eligible: System.Array.Empty<string>());
        Assert.Equal(AffectButtonState.VisibleDisabled,
            DecideAffectButtonState(mission, selectedPersonaId: null));
        Assert.Equal(AffectButtonState.VisibleDisabled,
            DecideAffectButtonState(mission, selectedPersonaId: "kira"));
    }

    // ====================================================================
    // DeclineButtonState transitions
    // ====================================================================

    [Fact]
    public void Decline_button_hidden_when_no_mission()
    {
        Assert.Equal(DeclineButtonState.Hidden, DecideDeclineButtonState(mission: null));
    }

    [Fact]
    public void Decline_button_enabled_when_mission_present_regardless_of_eligibility()
    {
        Assert.Equal(DeclineButtonState.Enabled,
            DecideDeclineButtonState(SampleMission(eligible: new[] { "kira" })));
        Assert.Equal(DeclineButtonState.Enabled,
            DecideDeclineButtonState(SampleMission(eligible: System.Array.Empty<string>())));
    }

    // ====================================================================
    // BuildAffectRequest payload shape
    // ====================================================================

    [Fact]
    public void Build_affect_request_carries_persona_in_assigned_list()
    {
        var mission = SampleMission(id: "abc-123");
        var req = BuildAffectRequest(mission, assignedPersonaId: "kira", currentTick: 42);

        Assert.Equal("abc-123", req.MissionId);
        Assert.Equal(WorldTickWireFormat.MissionType.ScoutRoute, req.MissionType);
        Assert.Equal("halfgate", req.Region);
        Assert.Null(req.ActorTarget);
        // M1 stub : success.
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Success, req.Outcome);
        Assert.Equal(42, req.Tick);
        Assert.Single(req.AssignedPersonas);
        Assert.Equal("kira", req.AssignedPersonas[0]);
    }

    [Fact]
    public void Build_affect_request_throws_on_empty_persona()
    {
        var mission = SampleMission();
        Assert.Throws<System.ArgumentException>(
            () => BuildAffectRequest(mission, assignedPersonaId: "", currentTick: 0));
        Assert.Throws<System.ArgumentException>(
            () => BuildAffectRequest(mission, assignedPersonaId: null!, currentTick: 0));
    }

    // ====================================================================
    // BuildDeclineRequest — empty assigned, failure outcome
    // ====================================================================

    [Fact]
    public void Build_decline_request_has_empty_assigned_personas()
    {
        var mission = SampleMission(id: "abc-123");
        var req = BuildDeclineRequest(mission, currentTick: 17);

        Assert.Equal("abc-123", req.MissionId);
        Assert.Equal(17, req.Tick);
        Assert.Empty(req.AssignedPersonas);
        // Decline outcome on the wire : "failure" (recorded in legacy
        // for the (eventual) persona narrative ; M1 has no listeners
        // for it yet but the convention is there).
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Failure, req.Outcome);
    }

    // ====================================================================
    // Format helpers — the M1 text-only panel rendering
    // ====================================================================

    [Fact]
    public void Format_mission_header_includes_type_region_difficulty()
    {
        var mission = SampleMission(
            type: WorldTickWireFormat.MissionType.ParleyLocal,
            region: "ridgepass",
            difficulty: WorldTickWireFormat.DifficultyBucket.Mid);
        var header = FormatMissionHeader(mission);
        Assert.Contains("parley_local", header);
        Assert.Contains("ridgepass", header);
        Assert.Contains("mid", header);
    }

    [Fact]
    public void Format_eligible_personas_handles_empty_with_decline_hint()
    {
        var mission = SampleMission(eligible: System.Array.Empty<string>());
        var line = FormatEligiblePersonas(mission);
        // The Varn option (c) hint : "no eligible — decline ?"
        Assert.Contains("Aucun", line);
        Assert.Contains("éligible", line);
    }

    [Fact]
    public void Format_eligible_personas_lists_names()
    {
        var mission = SampleMission(eligible: new[] { "kira", "hodge", "mirelle" });
        var line = FormatEligiblePersonas(mission);
        Assert.Contains("kira", line);
        Assert.Contains("hodge", line);
        Assert.Contains("mirelle", line);
    }

    [Fact]
    public void Format_legacy_tag_toast_handles_zero_one_two_plus()
    {
        // 0 tags : the declined-mission path.
        var t0 = FormatLegacyTagToast(System.Array.Empty<PersonaLegacyTagDto>());
        Assert.Contains("none", t0);

        // 1 tag : single line with persona + region.
        var t1 = FormatLegacyTagToast(new[]
        {
            new PersonaLegacyTagDto("kira", "m1", "scout_route", "halfgate", null, "success", 5),
        });
        Assert.Contains("kira", t1);
        Assert.Contains("scout_route", t1);
        Assert.Contains("halfgate", t1);

        // 2 tags : count + list.
        var t2 = FormatLegacyTagToast(new[]
        {
            new PersonaLegacyTagDto("kira", "m1", "scout_route", "halfgate", null, "success", 5),
            new PersonaLegacyTagDto("hodge", "m1", "scout_route", "halfgate", null, "success", 5),
        });
        Assert.Contains("2", t2);
        Assert.Contains("kira", t2);
        Assert.Contains("hodge", t2);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

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
            EligiblePersonas: eligible ?? new[] { "kira", "hodge" },
            Difficulty: difficulty,
            Region: region,
            DeadlineTicks: null,
            Outcome: null,
            Seed: 42L);
    }
}
