// Pin tests for the EmergentMissionDto.RecruitTargetNpcId field
// (Varn §A.8.D3 strict mode 2026-05-17, Tess backend commit 5131d5f).
//
// What this file pins :
//   1. The C# DTO carries RecruitTargetNpcId as a nullable string
//      (positional default = null, backward compat for non-recruit
//      types and pre-§A.8 wire shapes).
//   2. SnakeCaseLower naming policy maps
//      RecruitTargetNpcId <-> recruit_target_npc_id on the wire.
//   3. A recruit-mission payload from the backend deserialises with the
//      NpcId populated.
//   4. A payload WITHOUT the field still deserialises (legacy compat,
//      and non-recruit M2+ types).
//   5. Record value-equality is sensitive to the new field.

using System.Text.Json;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests;

public sealed class EmergentMissionRecruitTargetTests
{
    [Fact]
    public void Dto_constructor_accepts_recruit_target_npc_id()
    {
        var mission = new EmergentMissionDto(
            Id: "ml_kira",
            Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: "Kira watches the gate.",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: "e2.halfgate.gateway",
            RecruitTargetNpcId: "kira");

        Assert.Equal("recruit", mission.Type);
        Assert.Equal("e2.halfgate.gateway", mission.TargetPoi);
        Assert.Equal("kira", mission.RecruitTargetNpcId);
    }

    [Fact]
    public void Dto_constructor_recruit_target_defaults_null_for_legacy_callers()
    {
        // Backward-compat lock : positional construction without
        // RecruitTargetNpcId must still compile (it's the last param
        // with a default null). Legacy scout_route / parley_local
        // missions and pre-§A.8 fixtures keep working.
        var mission = new EmergentMissionDto(
            Id: "ml_legacy",
            Type: WorldTickWireFormat.MissionType.ScoutRoute,
            NarrativeHook: "h",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L);

        Assert.Null(mission.RecruitTargetNpcId);
    }

    [Fact]
    public void Wire_response_recruit_mission_deserialises_npc_id()
    {
        // The wire shape Tess emits post-commit 5131d5f for M1 strict
        // recruit-only mode.
        const string json = """
            {
              "mission": {
                "id": "ml-kira-001",
                "type": "recruit",
                "narrative_hook": "Kira watches the gate, ledger in hand.",
                "eligible_personas": [],
                "difficulty": "mid",
                "region": "halfgate",
                "deadline_ticks": null,
                "outcome": null,
                "seed": 42,
                "target_poi": "e2.halfgate.gateway",
                "recruit_target_npc_id": "kira"
              },
              "tick": 1
            }
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(response);
        Assert.NotNull(response.Mission);
        Assert.Equal("recruit", response.Mission.Type);
        Assert.Equal("e2.halfgate.gateway", response.Mission.TargetPoi);
        Assert.Equal("kira", response.Mission.RecruitTargetNpcId);
    }

    [Fact]
    public void Wire_response_without_recruit_target_npc_id_defaults_null()
    {
        // Legacy / non-recruit wire shape : the field is absent. The
        // C# DTO must still deserialise, with RecruitTargetNpcId = null.
        const string json = """
            {
              "mission": {
                "id": "ml-002",
                "type": "scout_route",
                "narrative_hook": "h",
                "eligible_personas": [],
                "difficulty": "mid",
                "region": "halfgate",
                "deadline_ticks": null,
                "outcome": null,
                "seed": 0,
                "target_poi": "e1.halfgate"
              },
              "tick": 0
            }
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(response);
        Assert.NotNull(response.Mission);
        Assert.Null(response.Mission.RecruitTargetNpcId);
    }

    [Fact]
    public void Wire_round_trip_recruit_target_uses_snake_case()
    {
        var mission = new EmergentMissionDto(
            Id: "x",
            Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: "h",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: "e2.halfgate.littoral",
            RecruitTargetNpcId: "vell");

        var json = JsonSerializer.Serialize(
            mission, ApiJsonContext.Default.EmergentMissionDto);

        Assert.Contains("\"recruit_target_npc_id\":\"vell\"", json);
        // Anti-regression : the PascalCase form must never leak.
        Assert.DoesNotContain("\"RecruitTargetNpcId\":", json);
    }

    [Fact]
    public void Record_value_equality_distinguishes_recruit_target()
    {
        // The DTO is a sealed record — equality is structural. Two
        // missions identical except for RecruitTargetNpcId must NOT
        // compare equal. This matters for MissionStore snapshot
        // diffing (post-poll change detection) and for xUnit pins
        // that assert mission identity by record equality.
        var kira = new EmergentMissionDto(
            Id: "ml_x", Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: "h", EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate", DeadlineTicks: null, Outcome: null, Seed: 0L,
            TargetPoi: "e2.halfgate.gateway", RecruitTargetNpcId: "kira");
        var dorn = kira with { RecruitTargetNpcId = "dorn" };

        Assert.NotEqual(kira, dorn);
        Assert.Equal(kira, kira with { });
    }
}
