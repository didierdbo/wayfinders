using System.Text.Json;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// R8 of the recruit cascade — wire-shape pins for the §A.8.D5
/// mission-action DTOs. Mirrors the Pydantic models on the FastAPI
/// side (Tess commit 5131d5f) :
/// <list type="bullet">
///   <item>POST /api/missions/&lt;id&gt;/accept — empty request, status
///         response.</item>
///   <item>POST /api/missions/&lt;id&gt;/decline — empty request, status
///         response.</item>
///   <item>POST /api/missions/&lt;id&gt;/conclude — outcome request,
///         outcome-echoed response.</item>
///   <item>POST /api/missions/&lt;id&gt;/resolve —
///         resolution_outcome_type request + response.</item>
/// </list>
///
/// <para>
/// Pinned : the snake_case field names on the wire match Tess's
/// Pydantic schemas, source-gen serialisation round-trips cleanly,
/// and the closed-lookup constants (outcome / resolution_outcome_type)
/// align with the server's <c>Literal</c> vocabularies.
/// </para>
/// </summary>
public sealed class MissionActionDtosTests
{
    [Fact]
    public void MissionActionEmptyRequest_serialises_as_empty_object()
    {
        // Pydantic MissionActionRequest(extra="forbid") accepts {} only.
        var json = JsonSerializer.Serialize(
            new MissionActionEmptyRequestDto(),
            ApiJsonContext.Default.MissionActionEmptyRequestDto);
        Assert.Equal("{}", json);
    }

    [Fact]
    public void MissionAcceptResponse_deserialises_status_field()
    {
        const string json = """
            {"mission_id": "ml-001", "status": "accepted"}
            """;
        var dto = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionAcceptResponseDto);
        Assert.NotNull(dto);
        Assert.Equal("ml-001", dto.MissionId);
        Assert.Equal("accepted", dto.Status);
    }

    [Fact]
    public void MissionDeclineResponse_deserialises_status_field()
    {
        const string json = """
            {"mission_id": "ml-002", "status": "declined"}
            """;
        var dto = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionDeclineResponseDto);
        Assert.NotNull(dto);
        Assert.Equal("ml-002", dto.MissionId);
        Assert.Equal("declined", dto.Status);
    }

    [Fact]
    public void MissionConcludeActionRequest_serialises_with_outcome_field()
    {
        var dto = new MissionConcludeActionRequestDto(
            MissionActionWireFormat.MissionConcludeOutcome.Success);
        var json = JsonSerializer.Serialize(
            dto, ApiJsonContext.Default.MissionConcludeActionRequestDto);
        Assert.Contains("\"outcome\":\"success\"", json);
        // Anti-regression : PascalCase must NEVER leak through.
        Assert.DoesNotContain("\"Outcome\":", json);
    }

    [Fact]
    public void MissionConcludeActionResponse_deserialises_three_fields()
    {
        const string json = """
            {"mission_id": "ml-003", "status": "concluded", "outcome": "success"}
            """;
        var dto = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionConcludeActionResponseDto);
        Assert.NotNull(dto);
        Assert.Equal("ml-003", dto.MissionId);
        Assert.Equal("concluded", dto.Status);
        Assert.Equal("success", dto.Outcome);
    }

    [Fact]
    public void MissionResolveActionRequest_serialises_snake_case()
    {
        var dto = new MissionResolveActionRequestDto(
            MissionActionWireFormat.MissionResolutionOutcomeType.Success);
        var json = JsonSerializer.Serialize(
            dto, ApiJsonContext.Default.MissionResolveActionRequestDto);
        // Wire field is snake_case : resolution_outcome_type
        Assert.Contains("\"resolution_outcome_type\":\"success\"", json);
        Assert.DoesNotContain("\"ResolutionOutcomeType\":", json);
    }

    [Fact]
    public void MissionResolveActionResponse_round_trips()
    {
        var dto = new MissionResolveActionResponseDto(
            "ml-004", "resolved",
            MissionActionWireFormat.MissionResolutionOutcomeType.Success);
        var json = JsonSerializer.Serialize(
            dto, ApiJsonContext.Default.MissionResolveActionResponseDto);
        var parsed = JsonSerializer.Deserialize(
            json, ApiJsonContext.Default.MissionResolveActionResponseDto);
        Assert.NotNull(parsed);
        Assert.Equal(dto, parsed);
    }

    [Theory]
    [InlineData("success", MissionActionWireFormat.MissionConcludeOutcome.Success)]
    [InlineData("failure", MissionActionWireFormat.MissionConcludeOutcome.Failure)]
    [InlineData("abandoned", MissionActionWireFormat.MissionConcludeOutcome.Abandoned)]
    public void MissionConcludeOutcome_constants_match_server_literal(string wire, string constant)
    {
        // Pin the closed-lookup string values against Pydantic
        // Literal["success", "failure", "abandoned"] on the server.
        Assert.Equal(wire, constant);
    }

    [Theory]
    [InlineData("success", MissionActionWireFormat.MissionResolutionOutcomeType.Success)]
    [InlineData("failure", MissionActionWireFormat.MissionResolutionOutcomeType.Failure)]
    [InlineData("abandoned", MissionActionWireFormat.MissionResolutionOutcomeType.Abandoned)]
    public void MissionResolutionOutcomeType_constants_match_server_literal(string wire, string constant)
    {
        Assert.Equal(wire, constant);
    }
}
