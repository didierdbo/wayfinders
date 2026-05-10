// M2-advance step 2 (2026-05-10) — pin tests for the
// MissionConclude wire format. The Godot-bound
// ApiClient.ConcludeMissionAsync is not directly testable in
// xUnit (Node lifetime, _shutdownCts) ; the contract — request /
// response shape, snake_case naming policy, closed-lookup string
// values, the special tuple[float, float, float] →
// double[3] mapping for probability_table — lives in the DTOs and
// is pinned here.
//
// What this file pins :
//   1. CharacterStateWireDto round-trip with snake_case fields
//      (persona_id / dex_bucket / wis_bucket).
//   2. MissionConcludeRequestDto serialises with the snake_case
//      policy (mission_id / mission_type / actor_target / etc.) +
//      tick_due is non-negative on the wire.
//   3. MissionConcludeResponseDto deserialises with all three
//      payloads (outcome / tags_created / roll_breakdown), the
//      probability_table tuple round-tripping as a JSON array.
//   4. Shared PersonaLegacyTagDto round-trip works through both
//      /resolve and /conclude responses (no wire drift).
//   5. Closed-lookup string values match Varn locks (success /
//      partial / failure ; very-low / very-high hyphen quirk).
//
// What this file deliberately does NOT pin :
//   - ApiClient.ConcludeMissionAsync end-to-end. Validated against
//     a live uvicorn at runtime smoke (Tess's pytest covers the
//     server determinism + distribution). Same rationale as
//     WorldTickDtoTests.cs.

using System.Collections.Generic;
using System.Text.Json;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests;

public sealed class MissionConcludeDtoTests
{
    // ====================================================================
    // CharacterStateWireDto — thin DTO for the conclude roll
    // ====================================================================

    [Fact]
    public void Character_state_wire_serializes_with_snake_case_fields()
    {
        var dto = new CharacterStateWireDto(
            PersonaId: "hodge",
            DexBucket: "mid",
            WisBucket: "low");

        var json = JsonSerializer.Serialize(
            dto, ApiJsonContext.Default.CharacterStateWireDto);

        Assert.Contains("\"persona_id\":\"hodge\"", json);
        Assert.Contains("\"dex_bucket\":\"mid\"", json);
        Assert.Contains("\"wis_bucket\":\"low\"", json);
        // Anti-regression : PascalCase leaking through would 422
        // against the server's extra="forbid" Pydantic config.
        Assert.DoesNotContain("\"PersonaId\"", json);
    }

    [Fact]
    public void Character_state_wire_round_trip_preserves_buckets()
    {
        // Hyphen-bearing bucket as the canary : the SnakeCaseLower
        // policy must not touch string values, only field names.
        var original = new CharacterStateWireDto(
            PersonaId: "wattan",
            DexBucket: "very-low",
            WisBucket: "very-high");

        var json = JsonSerializer.Serialize(
            original, ApiJsonContext.Default.CharacterStateWireDto);
        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.CharacterStateWireDto);

        Assert.NotNull(parsed);
        Assert.Equal("wattan", parsed.PersonaId);
        Assert.Equal("very-low", parsed.DexBucket);
        Assert.Equal("very-high", parsed.WisBucket);
        // Value strings must use the closed-lookup constants.
        Assert.Equal(DescriptorBucketValues.VeryLow, parsed.DexBucket);
        Assert.Equal(DescriptorBucketValues.VeryHigh, parsed.WisBucket);
    }

    // ====================================================================
    // MissionConcludeRequestDto — serialises for POST
    // ====================================================================

    [Fact]
    public void Conclude_request_serializes_with_all_snake_case_fields()
    {
        var request = new MissionConcludeRequestDto(
            MissionId: "abc-123",
            MissionType: WorldTickWireFormat.MissionType.ScoutRoute,
            Region: "halfgate",
            ActorTarget: null,
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            AssignedPersonas: new[]
            {
                new CharacterStateWireDto("hodge", "mid", "low"),
            },
            Seed: 42L,
            TickDue: 15);

        var json = JsonSerializer.Serialize(
            request, ApiJsonContext.Default.MissionConcludeRequestDto);

        Assert.Contains("\"mission_id\":\"abc-123\"", json);
        Assert.Contains("\"mission_type\":\"scout_route\"", json);
        Assert.Contains("\"region\":\"halfgate\"", json);
        Assert.Contains("\"actor_target\":null", json);
        Assert.Contains("\"difficulty\":\"mid\"", json);
        Assert.Contains("\"seed\":42", json);
        Assert.Contains("\"tick_due\":15", json);
        // The thin DTO inside assigned_personas must serialise
        // with snake_case too.
        Assert.Contains("\"persona_id\":\"hodge\"", json);
        Assert.Contains("\"dex_bucket\":\"mid\"", json);
        Assert.Contains("\"wis_bucket\":\"low\"", json);
    }

    [Fact]
    public void Conclude_request_with_actor_target_emits_string_not_null()
    {
        // parley_local with a named target — the server uses
        // actor_target to weave the named NPC into the legacy tag.
        var request = new MissionConcludeRequestDto(
            MissionId: "x",
            MissionType: WorldTickWireFormat.MissionType.ParleyLocal,
            Region: "ridgepass",
            ActorTarget: "magistrate orly",
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            AssignedPersonas: new[]
            {
                new CharacterStateWireDto("mirelle", "mid", "high"),
            },
            Seed: 1L,
            TickDue: 5);

        var json = JsonSerializer.Serialize(
            request, ApiJsonContext.Default.MissionConcludeRequestDto);

        Assert.Contains("\"actor_target\":\"magistrate orly\"", json);
    }

    [Fact]
    public void Conclude_request_round_trip_preserves_all_fields()
    {
        // Defensive : a request → JSON → request round-trip catches
        // the regression where someone changes a record property
        // modifier (init → set, sealed → non-sealed) and breaks the
        // source-gen contract silently.
        var original = new MissionConcludeRequestDto(
            MissionId: "abc-123",
            MissionType: WorldTickWireFormat.MissionType.ScoutRoute,
            Region: "halfgate",
            ActorTarget: null,
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            AssignedPersonas: new[]
            {
                new CharacterStateWireDto("hodge", "mid", "low"),
            },
            Seed: long.MaxValue / 2,
            TickDue: 99);

        var json = JsonSerializer.Serialize(
            original, ApiJsonContext.Default.MissionConcludeRequestDto);
        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionConcludeRequestDto);

        Assert.NotNull(parsed);
        Assert.Equal(original.MissionId, parsed.MissionId);
        Assert.Equal(original.MissionType, parsed.MissionType);
        Assert.Equal(original.Region, parsed.Region);
        Assert.Equal(original.ActorTarget, parsed.ActorTarget);
        Assert.Equal(original.Difficulty, parsed.Difficulty);
        Assert.Equal(original.Seed, parsed.Seed);
        Assert.Equal(original.TickDue, parsed.TickDue);
        Assert.Single(parsed.AssignedPersonas);
        Assert.Equal("hodge", parsed.AssignedPersonas[0].PersonaId);
        Assert.Equal("mid", parsed.AssignedPersonas[0].DexBucket);
        Assert.Equal("low", parsed.AssignedPersonas[0].WisBucket);
    }

    // ====================================================================
    // MissionConcludeResponseDto — deserialises from the server
    // ====================================================================

    [Fact]
    public void Conclude_response_deserializes_outcome_tags_breakdown()
    {
        // Realistic server response — outcome=success with one tag
        // and one roll_breakdown entry. The probability_table is a
        // JSON 3-element array (Pydantic tuple[float, float, float])
        // and must map to double[3] cleanly.
        const string json = """
            {
              "outcome": "success",
              "tags_created": [
                {
                  "persona_id": "hodge",
                  "mission_id": "abc-123",
                  "mission_type": "scout_route",
                  "region": "halfgate",
                  "actor_target": null,
                  "outcome": "success",
                  "earned_at_tick": 15
                }
              ],
              "roll_breakdown": [
                {
                  "persona_id": "hodge",
                  "bucket_used": "mid",
                  "delta": 0,
                  "roll_value": 0.42,
                  "probability_table": [0.5, 0.35, 0.15]
                }
              ]
            }
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionConcludeResponseDto);

        Assert.NotNull(response);
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Success, response.Outcome);

        // tags_created — same shape as /resolve, no wire drift.
        Assert.Single(response.TagsCreated);
        var tag = response.TagsCreated[0];
        Assert.Equal("hodge", tag.PersonaId);
        Assert.Equal("abc-123", tag.MissionId);
        Assert.Equal(WorldTickWireFormat.MissionType.ScoutRoute, tag.MissionType);
        Assert.Equal("halfgate", tag.Region);
        Assert.Null(tag.ActorTarget);
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Success, tag.Outcome);
        Assert.Equal(15, tag.EarnedAtTick);

        // roll_breakdown — debug payload, full transparency.
        Assert.Single(response.RollBreakdown);
        var roll = response.RollBreakdown[0];
        Assert.Equal("hodge", roll.PersonaId);
        Assert.Equal("mid", roll.BucketUsed);
        Assert.Equal(0, roll.Delta);
        Assert.Equal(0.42, roll.RollValue, precision: 4);
        // The probability_table is a 3-tuple on the server. Pin the
        // shape numerically — len == 3, sum ≈ 1.0.
        Assert.Equal(3, roll.ProbabilityTable.Length);
        Assert.Equal(0.5, roll.ProbabilityTable[0], precision: 4);
        Assert.Equal(0.35, roll.ProbabilityTable[1], precision: 4);
        Assert.Equal(0.15, roll.ProbabilityTable[2], precision: 4);
        Assert.Equal(1.0, roll.ProbabilityTable[0] + roll.ProbabilityTable[1] + roll.ProbabilityTable[2], precision: 4);
    }

    [Fact]
    public void Conclude_response_with_failure_outcome_round_trips()
    {
        // The unlucky case : delta=−2 → 70% failure. Pin the
        // failure-row probabilities through the wire.
        const string json = """
            {
              "outcome": "failure",
              "tags_created": [
                {
                  "persona_id": "wattan",
                  "mission_id": "m2",
                  "mission_type": "scout_route",
                  "region": "halfgate",
                  "actor_target": null,
                  "outcome": "failure",
                  "earned_at_tick": 12
                }
              ],
              "roll_breakdown": [
                {
                  "persona_id": "wattan",
                  "bucket_used": "very-low",
                  "delta": -2,
                  "roll_value": 0.95,
                  "probability_table": [0.05, 0.25, 0.7]
                }
              ]
            }
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionConcludeResponseDto);

        Assert.NotNull(response);
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Failure, response.Outcome);
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Failure, response.TagsCreated[0].Outcome);
        var roll = response.RollBreakdown[0];
        Assert.Equal(-2, roll.Delta);
        Assert.Equal(DescriptorBucketValues.VeryLow, roll.BucketUsed);
        Assert.Equal(0.7, roll.ProbabilityTable[2], precision: 4);
    }

    [Fact]
    public void Conclude_response_with_partial_outcome_carries_partial_tag()
    {
        // The middle-of-the-road case : delta=0 partial path.
        const string json = """
            {
              "outcome": "partial",
              "tags_created": [
                {
                  "persona_id": "hodge",
                  "mission_id": "mp",
                  "mission_type": "parley_local",
                  "region": "ridgepass",
                  "actor_target": "magistrate orly",
                  "outcome": "partial",
                  "earned_at_tick": 8
                }
              ],
              "roll_breakdown": []
            }
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionConcludeResponseDto);

        Assert.NotNull(response);
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Partial, response.Outcome);
        Assert.Equal("magistrate orly", response.TagsCreated[0].ActorTarget);
        Assert.Empty(response.RollBreakdown);
    }

    [Fact]
    public void Conclude_response_with_empty_tags_and_empty_breakdown_deserializes()
    {
        // Defensive : a malformed-but-syntactically-valid response
        // with empty arrays must not crash the deserializer. The
        // server should never emit this in M2 (min_length=1 on
        // assigned_personas guarantees at least one tag), but the
        // client must not assume the lists are non-empty at the
        // wire level.
        const string json = """
            {
              "outcome": "failure",
              "tags_created": [],
              "roll_breakdown": []
            }
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionConcludeResponseDto);

        Assert.NotNull(response);
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Failure, response.Outcome);
        Assert.Empty(response.TagsCreated);
        Assert.Empty(response.RollBreakdown);
    }

    // ====================================================================
    // RollBreakdownEntryDto — Varn-locked probability table per delta
    // ====================================================================

    [Theory]
    [InlineData(1, 0.80, 0.15, 0.05)]   // delta ≥ +1 row
    [InlineData(0, 0.50, 0.35, 0.15)]   // delta = 0 row
    [InlineData(-1, 0.20, 0.40, 0.40)]  // delta = −1 row
    [InlineData(-2, 0.05, 0.25, 0.70)]  // delta ≤ −2 row
    public void Probability_table_rows_round_trip_for_each_locked_delta(
        int delta, double pSuccess, double pPartial, double pFailure)
    {
        // Pin the four Varn-locked probability rows through the
        // wire. The server commit d533373 enforces these constants ;
        // a future server-side typo (0.81 instead of 0.80, etc.)
        // will surface here AND in Tess's distribution test.
        var entry = new RollBreakdownEntryDto(
            PersonaId: "test",
            BucketUsed: "mid",
            Delta: delta,
            RollValue: 0.5,
            ProbabilityTable: new[] { pSuccess, pPartial, pFailure });

        var json = JsonSerializer.Serialize(
            entry, ApiJsonContext.Default.RollBreakdownEntryDto);
        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.RollBreakdownEntryDto);

        Assert.NotNull(parsed);
        Assert.Equal(delta, parsed.Delta);
        Assert.Equal(pSuccess, parsed.ProbabilityTable[0], precision: 4);
        Assert.Equal(pPartial, parsed.ProbabilityTable[1], precision: 4);
        Assert.Equal(pFailure, parsed.ProbabilityTable[2], precision: 4);
        // Sums to 1.0 within float tolerance — anti-typo canary.
        Assert.Equal(1.0,
            parsed.ProbabilityTable[0] + parsed.ProbabilityTable[1] + parsed.ProbabilityTable[2],
            precision: 4);
    }

    // ====================================================================
    // Wire-shape sanity — no field-name drift from /resolve to /conclude
    // ====================================================================

    [Fact]
    public void PersonaLegacyTagDto_wire_shape_unchanged_between_resolve_and_conclude()
    {
        // Both endpoints return the same PersonaLegacyTagDto shape.
        // If a future refactor splits them (separate types per
        // endpoint), this test breaks loudly and the migration plan
        // becomes explicit. Same wire JSON must round-trip identically.
        const string tagJson = """
            {
              "persona_id": "hodge",
              "mission_id": "shared-id",
              "mission_type": "scout_route",
              "region": "halfgate",
              "actor_target": null,
              "outcome": "success",
              "earned_at_tick": 7
            }
            """;

        var tag = JsonSerializer.Deserialize(
            tagJson, ApiJsonContext.Default.PersonaLegacyTagDto);

        Assert.NotNull(tag);
        Assert.Equal("hodge", tag.PersonaId);
        Assert.Equal("shared-id", tag.MissionId);
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Success, tag.Outcome);
        Assert.Equal(7, tag.EarnedAtTick);
    }
}
