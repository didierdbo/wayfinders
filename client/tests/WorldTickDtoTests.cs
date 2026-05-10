// M1 mission-emergence wire-format pin tests (Etape 4a).
//
// What this file pins :
//   1. The closed-lookup string constants (Varn-locked 2026-05-10) match
//      the server-side Pydantic Literals byte-for-byte. A typo here
//      would silently send "scout-route" or "midd" and the server would
//      422 -- catching it at compile time + xUnit is cheaper than at
//      runtime.
//   2. The DTOs round-trip through the snake_case naming policy that
//      ApiJsonContext applies (UnitDto already pins this for GET /api/units ;
//      we re-pin it for the POST shape because the request side is new).
//   3. EmergentMission.Outcome is null at emergence ; null mission in the
//      response deserializes cleanly (no_emergence path).
//   4. The hyphen quirk in difficulty buckets ("very-low", "very-high")
//      is preserved on the wire -- it is part of the Varn vocabulary
//      lock, not the naming policy.
//
// What this file deliberately does NOT pin :
//   - ApiClient.TickAsync itself. The autoload is Godot-bound (Node,
//     _shutdownCts lifetime) and following the project convention
//     (cf. Wayfinders.Client.Tests.csproj header) we do not spin up a
//     Godot runtime in xUnit. Integration is validated end-to-end with
//     a live uvicorn at étape 4d.
//   - CharacterStateDto / company_personas typed shape -- M1 client
//     posts company_personas: [] always (server default = empty tuple,
//     Varn option (c)). The full mirror is a separate slice.

using System.Collections.Generic;
using System.Text.Json;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests;

/// <summary>
/// Pin tests for the M1 mission-emergence wire format. Every assertion
/// here defends a specific Varn-lock or a specific Pydantic field name
/// on the server side -- if a test fails, the error message points at
/// what drifted.
/// </summary>
public sealed class WorldTickDtoTests
{
    // ====================================================================
    // Closed-lookup constants -- Varn-locked 2026-05-10
    // ====================================================================

    /// <summary>
    /// Mission types match the server-side Literal byte-for-byte. The
    /// hyphen-vs-underscore distinction is the canary -- mission types
    /// use underscores (scout_route, parley_local) ; difficulty buckets
    /// use hyphens (very-low, very-high). Mixing them up is the kind of
    /// bug that ships silently because the strings "look right".
    /// </summary>
    [Fact]
    public void Mission_type_constants_match_server_literal()
    {
        Assert.Equal("scout_route", WorldTickWireFormat.MissionType.ScoutRoute);
        Assert.Equal("parley_local", WorldTickWireFormat.MissionType.ParleyLocal);
    }

    /// <summary>
    /// Difficulty buckets reuse the stat-lane vocabulary
    /// (vocabularies.py:25). very-low and very-high use hyphens, NOT
    /// underscores -- this is Varn-locked, not negotiable. The middle
    /// three are single words.
    /// </summary>
    [Fact]
    public void Difficulty_bucket_constants_match_server_literal()
    {
        Assert.Equal("very-low", WorldTickWireFormat.DifficultyBucket.VeryLow);
        Assert.Equal("low", WorldTickWireFormat.DifficultyBucket.Low);
        Assert.Equal("mid", WorldTickWireFormat.DifficultyBucket.Mid);
        Assert.Equal("high", WorldTickWireFormat.DifficultyBucket.High);
        Assert.Equal("very-high", WorldTickWireFormat.DifficultyBucket.VeryHigh);
    }

    /// <summary>
    /// Resolution outcome strings match the server Literal. M1 only
    /// uses these at resolution time -- at emergence, Outcome is null.
    /// </summary>
    [Fact]
    public void Resolution_outcome_constants_match_server_literal()
    {
        Assert.Equal("success", WorldTickWireFormat.ResolutionOutcome.Success);
        Assert.Equal("partial", WorldTickWireFormat.ResolutionOutcome.Partial);
        Assert.Equal("failure", WorldTickWireFormat.ResolutionOutcome.Failure);
    }

    // ====================================================================
    // WorldTickRequest serialization -- snake_case wire shape
    // ====================================================================

    /// <summary>
    /// Request serialises with snake_case field names matching the
    /// Pydantic schema. tick / seed / context_prose / company_personas
    /// are the four Pydantic fields ; if the source-gen drops the
    /// SnakeCaseLower policy this test fails immediately.
    /// </summary>
    [Fact]
    public void Request_serializes_with_snake_case_fields()
    {
        var request = new WorldTickRequestDto(
            Tick: 7,
            Seed: 42L,
            ContextProse: "The forest is quiet.",
            CompanyPersonas: System.Array.Empty<CharacterStateDto>());

        var json = JsonSerializer.Serialize(
            request,
            ApiJsonContext.Default.WorldTickRequestDto);

        Assert.Contains("\"tick\":7", json);
        Assert.Contains("\"seed\":42", json);
        Assert.Contains("\"context_prose\":\"The forest is quiet.\"", json);
        Assert.Contains("\"company_personas\":[]", json);

        // Anti-regression : if PascalCase ever leaks through, FastAPI's
        // extra="forbid" config rejects the payload. Catch it here.
        Assert.DoesNotContain("\"Tick\":", json);
        Assert.DoesNotContain("\"Seed\":", json);
        Assert.DoesNotContain("\"ContextProse\":", json);
        Assert.DoesNotContain("\"CompanyPersonas\":", json);
    }

    /// <summary>
    /// Empty company is the M1 norm. The wire form is the empty array
    /// `[]`, not `null` -- the server defaults to an empty tuple, sending
    /// null would fail Pydantic validation.
    /// </summary>
    [Fact]
    public void Request_with_empty_company_emits_empty_array_not_null()
    {
        var request = new WorldTickRequestDto(
            Tick: 0,
            Seed: 0L,
            ContextProse: "x",
            CompanyPersonas: System.Array.Empty<CharacterStateDto>());

        var json = JsonSerializer.Serialize(
            request,
            ApiJsonContext.Default.WorldTickRequestDto);

        Assert.Contains("\"company_personas\":[]", json);
        Assert.DoesNotContain("\"company_personas\":null", json);
    }

    // ====================================================================
    // WorldTickResponse deserialization -- the no-emergence path
    // ====================================================================

    /// <summary>
    /// `mission: null` is the no_emergence path (cadence-gated). The
    /// client must accept `null` for the Mission field and not throw --
    /// otherwise the WorldSimTick that polls every tick would crash on
    /// every between-window response.
    /// </summary>
    [Fact]
    public void Response_with_null_mission_deserializes_cleanly()
    {
        const string json = """{"mission":null,"tick":3}""";

        var response = JsonSerializer.Deserialize(
            json,
            ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(response);
        Assert.Null(response.Mission);
        Assert.Equal(3, response.Tick);
    }

    /// <summary>
    /// The happy path : a fully-populated emergent mission. Pins every
    /// closed-lookup field plus the snake_case mapping for narrative_hook
    /// / eligible_personas / deadline_ticks. M1 invariants : difficulty
    /// = mid, deadline_ticks = null, outcome = null at emergence.
    /// </summary>
    [Fact]
    public void Response_with_emerged_mission_deserializes_all_fields()
    {
        const string json = """
            {
              "mission": {
                "id": "abc-123",
                "type": "scout_route",
                "narrative_hook": "Une affaire de scout_route dans halfgate.",
                "eligible_personas": ["hodge", "mirelle"],
                "difficulty": "mid",
                "region": "halfgate",
                "deadline_ticks": null,
                "outcome": null,
                "seed": 99
              },
              "tick": 7
            }
            """;

        var response = JsonSerializer.Deserialize(
            json,
            ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(response);
        Assert.Equal(7, response.Tick);

        var mission = response.Mission;
        Assert.NotNull(mission);
        Assert.Equal("abc-123", mission.Id);
        Assert.Equal(WorldTickWireFormat.MissionType.ScoutRoute, mission.Type);
        Assert.Equal("Une affaire de scout_route dans halfgate.", mission.NarrativeHook);
        Assert.Equal(2, mission.EligiblePersonas.Count);
        Assert.Equal("hodge", mission.EligiblePersonas[0]);
        Assert.Equal("mirelle", mission.EligiblePersonas[1]);
        Assert.Equal(WorldTickWireFormat.DifficultyBucket.Mid, mission.Difficulty);
        Assert.Equal("halfgate", mission.Region);
        Assert.Null(mission.DeadlineTicks);
        Assert.Null(mission.Outcome);
        Assert.Equal(99L, mission.Seed);
    }

    /// <summary>
    /// Empty eligible_personas is Varn option (c) : the mission emerges,
    /// the client renders "aucun perso éligible — décliner ?". The wire
    /// form is `[]`, not `null` -- test that the C# side accepts the
    /// empty list cleanly.
    /// </summary>
    [Fact]
    public void Response_with_empty_eligible_personas_deserializes_to_empty_list()
    {
        const string json = """
            {
              "mission": {
                "id": "x",
                "type": "parley_local",
                "narrative_hook": "h",
                "eligible_personas": [],
                "difficulty": "mid",
                "region": "r",
                "deadline_ticks": null,
                "outcome": null,
                "seed": 1
              },
              "tick": 0
            }
            """;

        var response = JsonSerializer.Deserialize(
            json,
            ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(response);
        Assert.NotNull(response.Mission);
        Assert.Empty(response.Mission.EligiblePersonas);
    }

    /// <summary>
    /// The hyphen-bearing difficulty buckets must round-trip without
    /// being mangled by the SnakeCaseLower policy (the policy applies to
    /// field names, not to string values -- but a regression that
    /// applied it to values would silently turn "very-low" into
    /// "very_low" and the server would reject it). Pin the wire value
    /// for the two hyphen-bearing buckets to catch that drift.
    /// </summary>
    [Fact]
    public void Response_preserves_hyphen_in_difficulty_buckets()
    {
        const string jsonVeryLow = """
            {"mission":{"id":"x","type":"scout_route","narrative_hook":"h",
            "eligible_personas":[],"difficulty":"very-low","region":"r",
            "deadline_ticks":null,"outcome":null,"seed":0},"tick":0}
            """;
        const string jsonVeryHigh = """
            {"mission":{"id":"x","type":"scout_route","narrative_hook":"h",
            "eligible_personas":[],"difficulty":"very-high","region":"r",
            "deadline_ticks":null,"outcome":null,"seed":0},"tick":0}
            """;

        var rLow = JsonSerializer.Deserialize(
            jsonVeryLow, ApiJsonContext.Default.WorldTickResponseDto);
        var rHigh = JsonSerializer.Deserialize(
            jsonVeryHigh, ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(rLow);
        Assert.NotNull(rLow.Mission);
        Assert.Equal("very-low", rLow.Mission.Difficulty);
        Assert.Equal(WorldTickWireFormat.DifficultyBucket.VeryLow, rLow.Mission.Difficulty);

        Assert.NotNull(rHigh);
        Assert.NotNull(rHigh.Mission);
        Assert.Equal("very-high", rHigh.Mission.Difficulty);
        Assert.Equal(WorldTickWireFormat.DifficultyBucket.VeryHigh, rHigh.Mission.Difficulty);
    }

    /// <summary>
    /// Resolution outcome is settable post-emergence. The Outcome field
    /// is mirrored as nullable string ; the closed-lookup constants
    /// match the server Literal. Pin the round-trip for a non-null
    /// value to catch the case where someone refactors Outcome to an
    /// enum and breaks the wire mapping.
    /// </summary>
    [Fact]
    public void Response_carries_resolution_outcome_when_set()
    {
        const string json = """
            {"mission":{"id":"x","type":"scout_route","narrative_hook":"h",
            "eligible_personas":[],"difficulty":"mid","region":"r",
            "deadline_ticks":null,"outcome":"success","seed":0},"tick":0}
            """;

        var response = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.WorldTickResponseDto);

        Assert.NotNull(response);
        Assert.NotNull(response.Mission);
        Assert.Equal(WorldTickWireFormat.ResolutionOutcome.Success, response.Mission.Outcome);
    }

    // ====================================================================
    // Round-trip identity -- request serialise then parse back
    // ====================================================================

    /// <summary>
    /// Request -> JSON -> Request round-trip preserves every field.
    /// Catches the regression where someone changes a property
    /// modifier (init-only -> set, sealed -> non-sealed) and breaks the
    /// source-gen contract silently.
    /// </summary>
    [Fact]
    public void Request_round_trip_preserves_all_fields()
    {
        var original = new WorldTickRequestDto(
            Tick: 42,
            Seed: long.MaxValue,
            ContextProse: "le brouillard se lève sur la vallée",
            CompanyPersonas: System.Array.Empty<CharacterStateDto>());

        var json = JsonSerializer.Serialize(
            original,
            ApiJsonContext.Default.WorldTickRequestDto);
        var parsed = JsonSerializer.Deserialize(
            json,
            ApiJsonContext.Default.WorldTickRequestDto);

        Assert.NotNull(parsed);
        Assert.Equal(original.Tick, parsed.Tick);
        Assert.Equal(original.Seed, parsed.Seed);
        Assert.Equal(original.ContextProse, parsed.ContextProse);
        Assert.Empty(parsed.CompanyPersonas);
    }
}
