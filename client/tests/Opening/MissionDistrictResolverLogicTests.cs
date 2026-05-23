using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin tests for <see cref="MissionDistrictResolverLogic"/> — the
/// client-side cascade that turns
/// <see cref="EmergentMissionDto.TargetPoi"/> into a district id, with
/// the M1 first-of-city fallback for recruit slugs absent from
/// <c>world.yaml</c> (Étape B, Rune, 2026-05-23). The resolver is the
/// load-bearing seam of the eM→eT bascule : a regression here would
/// silently no-op every click.
/// </summary>
public sealed class MissionDistrictResolverLogicTests
{
    // The MVP referential — Halfgate with two world.yaml districts.
    private static readonly WorldCityDto Halfgate = new(
        Id: "halfgate",
        Name: "Halfgate",
        Position: new WorldPositionDto(92000, 38000),
        DistrictIds: new[] { "lower_quays", "high_wall" },
        Bitmap: null);

    private static readonly WorldDistrictDto LowerQuays = new(
        Id: "lower_quays",
        Name: "Lower Quays",
        ParentCityId: "halfgate",
        Anchor: new WorldPositionDto(91900, 37700),
        Footprint: System.Array.Empty<WorldPositionDto>(),
        CellSizeM: 2);

    private static readonly WorldDistrictDto HighWall = new(
        Id: "high_wall",
        Name: "High Wall",
        ParentCityId: "halfgate",
        Anchor: new WorldPositionDto(92000, 37700),
        Footprint: System.Array.Empty<WorldPositionDto>(),
        CellSizeM: 2);

    private static readonly IReadOnlyList<WorldCityDto> Cities = new[] { Halfgate };
    private static readonly IReadOnlyList<WorldDistrictDto> Districts = new[] { LowerQuays, HighWall };

    private static EmergentMissionDto MissionWith(string targetPoi) => new(
        Id: "mid-test",
        Type: "recruit",
        NarrativeHook: "ignored",
        EligiblePersonas: System.Array.Empty<string>(),
        Difficulty: "medium",
        Region: "halfgate",
        DeadlineTicks: null,
        Outcome: null,
        Seed: 0L,
        TargetPoi: targetPoi,
        RecruitTargetNpcId: null);

    // --- Exact match -----------------------------------------------------

    [Fact]
    public void ResolveDistrict_returns_exact_match_when_third_segment_matches_world_yaml()
    {
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            MissionWith("e2.halfgate.lower_quays"), Cities, Districts);

        Assert.Equal("lower_quays", outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.ExactMatch,
            outcome.Method);
    }

    [Fact]
    public void ResolveDistrict_returns_exact_match_for_high_wall()
    {
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            MissionWith("e2.halfgate.high_wall"), Cities, Districts);

        Assert.Equal("high_wall", outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.ExactMatch,
            outcome.Method);
    }

    // --- First-of-city fallback (the M1 norm) ----------------------------

    [Theory]
    [InlineData("e2.halfgate.gateway")]
    [InlineData("e2.halfgate.intramuros")]
    [InlineData("e2.halfgate.littoral")]
    public void ResolveDistrict_falls_back_to_first_of_city_for_recruit_slugs(string targetPoi)
    {
        // The M1 case : `gateway`/`intramuros`/`littoral` live in the
        // recruit roster, not in world.yaml. The resolver must take
        // halfgate's first DistrictIds entry, lower_quays.
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            MissionWith(targetPoi), Cities, Districts);

        Assert.Equal("lower_quays", outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.FirstOfCityFallback,
            outcome.Method);
    }

    [Fact]
    public void ResolveDistrict_falls_back_for_city_level_target_poi_no_third_segment()
    {
        // "e1.halfgate" has no 3ʳᵈ segment — fall through to first-of-city.
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            MissionWith("e1.halfgate"), Cities, Districts);

        Assert.Equal("lower_quays", outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.FirstOfCityFallback,
            outcome.Method);
    }

    // --- Failures --------------------------------------------------------

    [Fact]
    public void ResolveDistrict_returns_failure_when_target_poi_unparseable()
    {
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            MissionWith(""), Cities, Districts);

        Assert.Null(outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.Failure,
            outcome.Method);
    }

    [Fact]
    public void ResolveDistrict_returns_failure_when_city_unknown()
    {
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            MissionWith("e2.northport.guildhall"), Cities, Districts);

        Assert.Null(outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.Failure,
            outcome.Method);
    }

    [Fact]
    public void ResolveDistrict_returns_failure_when_districts_list_empty()
    {
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            MissionWith("e2.halfgate.lower_quays"),
            Cities,
            System.Array.Empty<WorldDistrictDto>());

        Assert.Null(outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.Failure,
            outcome.Method);
    }

    [Fact]
    public void ResolveDistrict_returns_failure_when_mission_null()
    {
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            null, Cities, Districts);

        Assert.Null(outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.Failure,
            outcome.Method);
    }

    [Fact]
    public void ResolveDistrict_returns_failure_when_cross_city_district_id_match()
    {
        // Defensive : a district id that matches "lower_quays" but
        // belongs to a different city must NOT be returned for halfgate.
        var northport = new WorldCityDto(
            Id: "northport",
            Name: "Northport",
            Position: new WorldPositionDto(200000, 100000),
            DistrictIds: new[] { "lower_quays" }, // same slug, different city
            Bitmap: null);
        var northportLowerQuays = new WorldDistrictDto(
            Id: "lower_quays",
            Name: "Lower Quays (Northport)",
            ParentCityId: "northport",
            Anchor: new WorldPositionDto(0, 0),
            Footprint: System.Array.Empty<WorldPositionDto>(),
            CellSizeM: 2);

        var cities = new[] { Halfgate, northport };
        var districts = new[] { LowerQuays, HighWall, northportLowerQuays };

        // Halfgate mission asks for "lower_quays" — must resolve to
        // halfgate's lower_quays, not northport's, by the parent-city
        // filter.
        var outcome = MissionDistrictResolverLogic.ResolveDistrict(
            MissionWith("e2.halfgate.lower_quays"), cities, districts);

        Assert.Equal("lower_quays", outcome.DistrictId);
        Assert.Equal(
            MissionDistrictResolverLogic.ResolutionMethod.ExactMatch,
            outcome.Method);
    }

    // --- ExtractThirdSegment --------------------------------------------

    [Theory]
    [InlineData("e2.halfgate.lower_quays", "lower_quays")]
    [InlineData("e2.halfgate.gateway", "gateway")]
    [InlineData("e3.halfgate.docks.taverne", "docks")]
    public void ExtractThirdSegment_returns_third_segment(string targetPoi, string expected)
    {
        Assert.Equal(expected, MissionDistrictResolverLogic.ExtractThirdSegment(targetPoi));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e1.halfgate")]   // 2 segments only
    [InlineData("halfgate")]      // 1 segment
    [InlineData("xx.halfgate.gateway")] // invalid layer prefix
    public void ExtractThirdSegment_returns_null_for_short_or_invalid_input(string? targetPoi)
    {
        Assert.Null(MissionDistrictResolverLogic.ExtractThirdSegment(targetPoi));
    }
}
