// Pin tests for the EmergentMissionDto.TargetPoi migration
// (Varn-lock 2026-05-15 v2 §1, Tess backend commit 6d2d2e4).
//
// What this file pins :
//   1. The C# DTO carries TargetPoi alongside Region (backward compat).
//   2. SnakeCaseLower naming policy + explicit [JsonPropertyName] map
//      TargetPoi <-> target_poi on the wire.
//   3. A response payload with target_poi deserializes cleanly and the
//      field is populated.
//   4. A response payload WITHOUT target_poi (legacy Pydantic, M-14-era)
//      still deserializes -- defaulting TargetPoi to "" so old replay
//      logs don't break the test harness.
//   5. The regex shape contract is documented (we don't enforce it
//      client-side -- Pydantic on the server is the validator -- but
//      we pin a sample of each layer level for documentation).

using System.Text.Json;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests;

public sealed class EmergentMissionTargetPoiTests
{
    [Fact]
    public void Dto_constructor_accepts_target_poi_and_keeps_region()
    {
        var mission = new EmergentMissionDto(
            Id: "abc",
            Type: WorldTickWireFormat.MissionType.ScoutRoute,
            NarrativeHook: "h",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: "e1.halfgate");

        Assert.Equal("halfgate", mission.Region);
        Assert.Equal("e1.halfgate", mission.TargetPoi);
    }

    [Fact]
    public void Dto_constructor_target_poi_defaults_empty_for_legacy_callers()
    {
        // Backward-compat lock : positional construction without
        // TargetPoi must still compile (it's the last param with a
        // default). The default is "" so the missions section
        // aggregator excludes the mission cleanly (no false-positive
        // matches against a hovered POI).
        var mission = new EmergentMissionDto(
            Id: "abc",
            Type: WorldTickWireFormat.MissionType.ScoutRoute,
            NarrativeHook: "h",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L);

        Assert.Equal("", mission.TargetPoi);
    }

    [Fact]
    public void Wire_response_with_target_poi_deserializes_field()
    {
        // The wire shape Tess emits post-commit 6d2d2e4.
        const string json = """
            {
              "mission": {
                "id": "abc-123",
                "type": "scout_route",
                "narrative_hook": "Une affaire de scout_route dans halfgate.",
                "eligible_personas": ["hodge"],
                "difficulty": "mid",
                "region": "halfgate",
                "deadline_ticks": null,
                "outcome": null,
                "seed": 99,
                "target_poi": "e1.halfgate"
              },
              "tick": 7
            }
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(response);
        Assert.NotNull(response.Mission);
        Assert.Equal("e1.halfgate", response.Mission.TargetPoi);
        // Region is preserved on the wire alongside target_poi
        // (backward-compat per Varn §1).
        Assert.Equal("halfgate", response.Mission.Region);
    }

    [Fact]
    public void Wire_response_without_target_poi_defaults_to_empty()
    {
        // Legacy wire shape (pre-commit 6d2d2e4). The C# DTO must
        // still deserialize gracefully -- TargetPoi defaults to ""
        // because of the positional default in the record. This
        // matters for replaying M-14-era trace logs against the
        // M-15 client.
        const string json = """
            {
              "mission": {
                "id": "abc-123",
                "type": "scout_route",
                "narrative_hook": "h",
                "eligible_personas": [],
                "difficulty": "mid",
                "region": "halfgate",
                "deadline_ticks": null,
                "outcome": null,
                "seed": 0
              },
              "tick": 0
            }
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(response);
        Assert.NotNull(response.Mission);
        Assert.Equal("", response.Mission.TargetPoi);
    }

    [Fact]
    public void Wire_round_trip_target_poi_uses_snake_case()
    {
        var mission = new EmergentMissionDto(
            Id: "x",
            Type: WorldTickWireFormat.MissionType.ParleyLocal,
            NarrativeHook: "h",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: "e2.halfgate.docks");
        var response = new WorldTickResponseDto(Mission: mission, Tick: 0);

        var json = JsonSerializer.Serialize(
            response, ApiJsonContext.Default.WorldTickResponseDto);

        Assert.Contains("\"target_poi\":\"e2.halfgate.docks\"", json);
        // Anti-regression : the PascalCase form must NEVER leak through.
        Assert.DoesNotContain("\"TargetPoi\":", json);
    }

    [Theory]
    [InlineData("e1.halfgate")]
    [InlineData("e2.halfgate.docks")]
    [InlineData("e3.halfgate.docks.taverne-du-cormoran")]
    [InlineData("e1.veylant")]
    public void Wire_round_trip_preserves_hierarchical_poi_id_at_every_layer(string poiId)
    {
        // The Varn §1 regex ^e[1-3]\.[a-z0-9\-]+(\.[a-z0-9\-]+){0,2}$
        // is enforced server-side. Client-side we just pass through.
        // This theory pins that the SnakeCaseLower policy doesn't
        // mangle the dot separator or the kebab slug at any layer.
        var mission = new EmergentMissionDto(
            Id: "x", Type: WorldTickWireFormat.MissionType.ScoutRoute,
            NarrativeHook: "h",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "x", DeadlineTicks: null, Outcome: null, Seed: 0L,
            TargetPoi: poiId);

        var json = JsonSerializer.Serialize(
            mission, ApiJsonContext.Default.EmergentMissionDto);
        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.EmergentMissionDto);

        Assert.NotNull(parsed);
        Assert.Equal(poiId, parsed.TargetPoi);
    }
}
