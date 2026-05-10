// M2-advance step 2b (2026-05-10) — pin tests for the
// MissionDurationLogic contracts. The runtime M1Slice scene drives
// the tick scan + Label3D suffix refresh + drag-blocking ; the
// data-flow rules ("when is a mission due", "what suffix renders
// for persona X", "how do you build the conclude request from the
// company snapshot") live here and are pinned by xUnit so they
// cannot drift silently.
//
// What this file pins :
//   1. Locked constants : DurationTicks=5, MaxAssignedPersonasPerMission=1
//      (Didier 2026-05-10).
//   2. BuildActiveMission : tick_due derivation + defensive throws
//      (null mission, empty persona, negative tick, multiple
//      personas in M2).
//   3. IsPersonaBusy : bound-to-mission lookup, case-sensitive,
//      empty / null defensive.
//   4. ComputeMissionCountdownSuffix : the [mission X/5] format
//      across the full lifecycle (5 -> 0 -> overdue clamp), the
//      null-when-not-busy fallback case.
//   5. IsMissionDue : >= comparison (catches dropped-tick overshoot).
//   6. ProjectToWireDto : CharacterStateDto.Name -> persona_id
//      mapping + bucket throws.
//   7. BuildConcludeRequest : end-to-end from active mission +
//      company snapshot to the wire DTO ; defensive throw when a
//      persona is missing from the snapshot.

using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests;

public sealed class MissionDurationLogicTests
{
    // ====================================================================
    // Locked constants
    // ====================================================================

    [Fact]
    public void Duration_ticks_is_locked_at_five()
    {
        // Didier 2026-05-10 lock. A future PR that wants to change
        // this (e.g. M3+ "boss missions take 7 ticks") must walk
        // through the memory project_wayfinders_mission_emergence.md
        // and the conclude-server probability table reproducibility
        // ; pinning the constant in xUnit forces that conversation.
        Assert.Equal(5, MissionDurationLogic.DurationTicks);
    }

    [Fact]
    public void Max_assigned_personas_per_mission_is_one_in_M2()
    {
        // M2 commit-binding. M3+ will revisit ; until then, any
        // accidental "let's allow two personas" change must update
        // this pin AND the conclude-server min_length=1 contract.
        Assert.Equal(1, MissionDurationLogic.MaxAssignedPersonasPerMission);
    }

    // ====================================================================
    // BuildActiveMission
    // ====================================================================

    [Fact]
    public void Build_active_mission_derives_tick_due_from_duration_constant()
    {
        var mission = SampleMission();
        var active = MissionDurationLogic.BuildActiveMission(mission, "hodge", tickStarted: 12);

        Assert.Equal(mission, active.Mission);
        Assert.Equal(12, active.TickStarted);
        Assert.Equal(12 + MissionDurationLogic.DurationTicks, active.TickDue);
        Assert.Single(active.AssignedPersonas);
        Assert.Equal("hodge", active.AssignedPersonas[0]);
    }

    [Fact]
    public void Build_active_mission_throws_on_empty_persona()
    {
        // M2 commit-binding : exactly one non-empty persona id.
        // The xUnit pin catches the regression where a future
        // refactor lets an empty string slip through to the conclude
        // request (would 422 at the server, but better to fail at
        // the source).
        Assert.Throws<System.ArgumentException>(
            () => MissionDurationLogic.BuildActiveMission(SampleMission(), "", 0));
        Assert.Throws<System.ArgumentException>(
            () => MissionDurationLogic.BuildActiveMission(SampleMission(), null!, 0));
    }

    [Fact]
    public void Build_active_mission_throws_on_negative_tick()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", -1));
    }

    [Fact]
    public void Build_active_mission_throws_on_null_mission()
    {
        Assert.Throws<System.ArgumentNullException>(
            () => MissionDurationLogic.BuildActiveMission(null!, "hodge", 0));
    }

    // ====================================================================
    // IsPersonaBusy
    // ====================================================================

    [Fact]
    public void Is_persona_busy_returns_false_for_empty_active_list()
    {
        Assert.False(MissionDurationLogic.IsPersonaBusy(
            "hodge", System.Array.Empty<ActiveMissionDto>()));
    }

    [Fact]
    public void Is_persona_busy_returns_true_when_persona_is_in_a_mission()
    {
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(id: "m1"), "hodge", 0),
            MissionDurationLogic.BuildActiveMission(SampleMission(id: "m2"), "mirelle", 1),
        };
        Assert.True(MissionDurationLogic.IsPersonaBusy("hodge", active));
        Assert.True(MissionDurationLogic.IsPersonaBusy("mirelle", active));
    }

    [Fact]
    public void Is_persona_busy_returns_false_for_persona_not_in_any_mission()
    {
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(id: "m1"), "hodge", 0),
        };
        Assert.False(MissionDurationLogic.IsPersonaBusy("wattan", active));
    }

    [Fact]
    public void Is_persona_busy_is_case_sensitive()
    {
        // Same convention as IsPersonaInEligibleList — the canonical
        // persona id is lowercase ; mixed case must not silently
        // match. A future regression that adds a case-insensitive
        // comparison would let a typo on the UI side bypass the
        // busy check.
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", 0),
        };
        Assert.False(MissionDurationLogic.IsPersonaBusy("Hodge", active));
        Assert.False(MissionDurationLogic.IsPersonaBusy("HODGE", active));
    }

    [Fact]
    public void Is_persona_busy_handles_null_and_empty_persona_id_gracefully()
    {
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", 0),
        };
        Assert.False(MissionDurationLogic.IsPersonaBusy(null!, active));
        Assert.False(MissionDurationLogic.IsPersonaBusy("", active));
    }

    // ====================================================================
    // ComputeMissionCountdownSuffix — the [mission X/5] format
    // ====================================================================

    [Fact]
    public void Compute_countdown_suffix_returns_null_when_persona_not_in_active()
    {
        // Caller falls back to the legacy-tag suffix when null.
        // This branch must produce null, NOT empty string — a
        // future regression that returns "" would silently overwrite
        // the legacy suffix on every refresh.
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", 0),
        };
        Assert.Null(MissionDurationLogic.ComputeMissionCountdownSuffix("wattan", active, 0));
    }

    [Fact]
    public void Compute_countdown_suffix_just_started_renders_5_over_5()
    {
        // tick_started = 10, tick_due = 15, currentTick = 10 → 5/5.
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10),
        };
        var suffix = MissionDurationLogic.ComputeMissionCountdownSuffix("hodge", active, 10);
        Assert.Equal(" [mission 5/5]", suffix);
    }

    [Fact]
    public void Compute_countdown_suffix_midway_renders_3_over_5()
    {
        // tick_started = 10, tick_due = 15, currentTick = 12 → 3/5.
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10),
        };
        var suffix = MissionDurationLogic.ComputeMissionCountdownSuffix("hodge", active, 12);
        Assert.Equal(" [mission 3/5]", suffix);
    }

    [Fact]
    public void Compute_countdown_suffix_one_tick_remaining_renders_1_over_5()
    {
        // Last tick before conclude.
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10),
        };
        var suffix = MissionDurationLogic.ComputeMissionCountdownSuffix("hodge", active, 14);
        Assert.Equal(" [mission 1/5]", suffix);
    }

    [Fact]
    public void Compute_countdown_suffix_at_due_renders_0_over_5()
    {
        // currentTick == TickDue : the conclude pipeline is about
        // to fire. Suffix shows 0/5 transiently.
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10),
        };
        var suffix = MissionDurationLogic.ComputeMissionCountdownSuffix("hodge", active, 15);
        Assert.Equal(" [mission 0/5]", suffix);
    }

    [Fact]
    public void Compute_countdown_suffix_overdue_clamps_to_0()
    {
        // Dropped-tick scenario : currentTick > TickDue. Clamp to 0,
        // never render negative — spec is "count down to zero, then
        // conclude".
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10),
        };
        var suffix = MissionDurationLogic.ComputeMissionCountdownSuffix("hodge", active, 20);
        Assert.Equal(" [mission 0/5]", suffix);
    }

    [Fact]
    public void Compute_countdown_suffix_uses_duration_constant_in_denominator()
    {
        // Defensive : the /5 denominator must come from
        // MissionDurationLogic.DurationTicks, not be a magic literal.
        // If a future change bumps the constant, the format string
        // updates with it — pin via concatenation rather than a
        // hard-coded "/5".
        var active = new[]
        {
            MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10),
        };
        var suffix = MissionDurationLogic.ComputeMissionCountdownSuffix("hodge", active, 12);
        Assert.NotNull(suffix);
        Assert.EndsWith($"/{MissionDurationLogic.DurationTicks}]", suffix);
    }

    // ====================================================================
    // IsMissionDue — comparison operator pinned numerically
    // ====================================================================

    [Fact]
    public void Mission_is_not_due_one_tick_before_tick_due()
    {
        var active = MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10);
        // tick_due = 15, currentTick = 14 → not due.
        Assert.False(MissionDurationLogic.IsMissionDue(active, 14));
    }

    [Fact]
    public void Mission_is_due_exactly_at_tick_due()
    {
        var active = MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10);
        Assert.True(MissionDurationLogic.IsMissionDue(active, 15));
    }

    [Fact]
    public void Mission_is_due_when_overdue_dropped_tick_scenario()
    {
        // The dropped-tick scenario : if WorldSimTick's backpressure
        // skips a tick, currentTick can jump past TickDue without
        // hitting it exactly. The >= comparison catches that.
        var active = MissionDurationLogic.BuildActiveMission(SampleMission(), "hodge", tickStarted: 10);
        Assert.True(MissionDurationLogic.IsMissionDue(active, 17));
        Assert.True(MissionDurationLogic.IsMissionDue(active, 100));
    }

    [Fact]
    public void Mission_due_throws_on_null_mission()
    {
        Assert.Throws<System.ArgumentNullException>(
            () => MissionDurationLogic.IsMissionDue(null!, 0));
    }

    // ====================================================================
    // ProjectToWireDto — CharacterStateDto -> CharacterStateWireDto
    // ====================================================================

    [Fact]
    public void Project_to_wire_dto_maps_name_to_persona_id_and_keeps_buckets()
    {
        var persona = SamplePersona(name: "hodge", dex: "mid", wis: "low");
        var wire = MissionDurationLogic.ProjectToWireDto(persona);

        Assert.Equal("hodge", wire.PersonaId);
        Assert.Equal("mid", wire.DexBucket);
        Assert.Equal("low", wire.WisBucket);
    }

    [Fact]
    public void Project_to_wire_dto_preserves_hyphen_buckets()
    {
        // Defensive against a regression that lower-cases or
        // hyphen-strips the bucket values.
        var persona = SamplePersona(name: "wattan", dex: "very-low", wis: "very-high");
        var wire = MissionDurationLogic.ProjectToWireDto(persona);

        Assert.Equal(DescriptorBucketValues.VeryLow, wire.DexBucket);
        Assert.Equal(DescriptorBucketValues.VeryHigh, wire.WisBucket);
    }

    [Fact]
    public void Project_to_wire_dto_throws_on_empty_name()
    {
        var persona = SamplePersona(name: "");
        Assert.Throws<System.ArgumentException>(
            () => MissionDurationLogic.ProjectToWireDto(persona));
    }

    [Fact]
    public void Project_to_wire_dto_throws_on_empty_buckets()
    {
        // The conclude server's Pydantic enforces non-empty bucket
        // values via the closed Literal ; we mirror the contract
        // here so a malformed CharacterStateDto fails at the source
        // with a clear message rather than 422-ing at the server
        // with a less actionable message.
        var personaNoDex = SamplePersona(dex: "");
        var personaNoWis = SamplePersona(wis: "");
        Assert.Throws<System.ArgumentException>(
            () => MissionDurationLogic.ProjectToWireDto(personaNoDex));
        Assert.Throws<System.ArgumentException>(
            () => MissionDurationLogic.ProjectToWireDto(personaNoWis));
    }

    // ====================================================================
    // BuildConcludeRequest — end-to-end from ActiveMission + company
    // ====================================================================

    [Fact]
    public void Build_conclude_request_carries_mission_metadata_and_projected_personas()
    {
        var mission = SampleMission(
            id: "m-001",
            type: WorldTickWireFormat.MissionType.ScoutRoute,
            region: "halfgate",
            difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            seed: 42L);
        var active = MissionDurationLogic.BuildActiveMission(mission, "hodge", tickStarted: 10);

        var company = new Dictionary<string, CharacterStateDto>
        {
            ["hodge"] = SamplePersona(name: "hodge", dex: "mid", wis: "low"),
        };

        var request = MissionDurationLogic.BuildConcludeRequest(active, company);

        Assert.Equal("m-001", request.MissionId);
        Assert.Equal(WorldTickWireFormat.MissionType.ScoutRoute, request.MissionType);
        Assert.Equal("halfgate", request.Region);
        Assert.Null(request.ActorTarget); // M1 templates have no named target.
        Assert.Equal(WorldTickWireFormat.DifficultyBucket.Mid, request.Difficulty);
        Assert.Equal(42L, request.Seed);
        // tick_due = tick_started + DurationTicks = 10 + 5 = 15.
        Assert.Equal(15, request.TickDue);

        Assert.Single(request.AssignedPersonas);
        var p = request.AssignedPersonas[0];
        Assert.Equal("hodge", p.PersonaId);
        Assert.Equal("mid", p.DexBucket);
        Assert.Equal("low", p.WisBucket);
    }

    [Fact]
    public void Build_conclude_request_throws_when_persona_missing_from_company()
    {
        // Defensive : the runtime guarantees personas are in the
        // company snapshot at the time the active mission was
        // created, but a tick race or a save-load could in theory
        // remove a persona between accept and conclude. Throw at
        // the source rather than send a malformed request.
        var mission = SampleMission();
        var active = MissionDurationLogic.BuildActiveMission(mission, "ghost-id", tickStarted: 0);
        var emptyCompany = new Dictionary<string, CharacterStateDto>();

        Assert.Throws<System.InvalidOperationException>(
            () => MissionDurationLogic.BuildConcludeRequest(active, emptyCompany));
    }

    [Fact]
    public void Build_conclude_request_throws_on_empty_assigned_personas()
    {
        // The server enforces min_length=1 ; throw at the source.
        // Note : BuildActiveMission already prevents construction
        // with empty assigned, but the helper accepts an
        // ActiveMissionDto from any source so we still defend the
        // direct call.
        var mission = SampleMission();
        var malformed = new ActiveMissionDto(
            Mission: mission,
            TickStarted: 0,
            TickDue: 5,
            AssignedPersonas: System.Array.Empty<string>());
        var company = new Dictionary<string, CharacterStateDto>();

        Assert.Throws<System.ArgumentException>(
            () => MissionDurationLogic.BuildConcludeRequest(malformed, company));
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static EmergentMissionDto SampleMission(
        string id = "m-001",
        string type = "scout_route",
        string region = "halfgate",
        string difficulty = "mid",
        long seed = 1L)
    {
        return new EmergentMissionDto(
            Id: id,
            Type: type,
            NarrativeHook: "Une affaire de scout_route dans halfgate.",
            EligiblePersonas: new[] { "hodge", "mirelle" },
            Difficulty: difficulty,
            Region: region,
            DeadlineTicks: null,
            Outcome: null,
            Seed: seed);
    }

    private static CharacterStateDto SamplePersona(
        string name = "hodge",
        string dex = "mid",
        string wis = "mid")
    {
        return new CharacterStateDto
        {
            Name = name,
            CharClass = "scout",
            Faction = "Wayfinders",
            StrBucket = "mid",
            DexBucket = dex,
            WisBucket = wis,
            HpBucket = "unhurt",
            StressBucket = "composed",
        };
    }
}
