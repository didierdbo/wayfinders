using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin tests for <see cref="MissionPositionResolverLogic"/> — the
/// client-side derivation that turns
/// <see cref="EmergentMissionDto.TargetPoi"/> into a world-metre
/// position via the cached <c>WorldMapService</c> referential
/// (Étape A, Rune, 2026-05-23).
///
/// <para>
/// Two layers under test : the <see cref="MissionPositionResolverLogic.ExtractCitySlug"/>
/// parser (canonical hierarchical-id format) and the
/// <see cref="MissionPositionResolverLogic.ResolveForCity"/> lookup
/// (city slug -> <see cref="WorldCityDto.Position"/>). The parser is the
/// risk surface — a regression there silently mis-places every mission
/// POI on the eM map.
/// </para>
/// </summary>
public sealed class MissionPositionResolverLogicTests
{
    // --- ExtractCitySlug ------------------------------------------------

    [Theory]
    [InlineData("e1.halfgate", "halfgate")]
    [InlineData("e2.halfgate.gateway", "halfgate")]
    [InlineData("e2.halfgate.intramuros", "halfgate")]
    [InlineData("e2.halfgate.littoral", "halfgate")]
    [InlineData("e3.halfgate.docks.taverne-du-cormoran", "halfgate")]
    [InlineData("e2.northport.guildhall", "northport")]
    public void ExtractCitySlug_returns_second_segment_for_valid_target_poi(
        string targetPoi, string expectedSlug)
    {
        Assert.Equal(expectedSlug, MissionPositionResolverLogic.ExtractCitySlug(targetPoi));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("halfgate")]              // no dot
    [InlineData("e1")]                    // no dot
    [InlineData(".halfgate")]             // empty layer
    [InlineData("e1.")]                   // empty city
    [InlineData("x1.halfgate")]           // wrong layer letter
    [InlineData("eX.halfgate")]           // wrong layer digit (not 0-9)
    [InlineData("e12.halfgate")]          // layer too long (locked to e[1-9])
    [InlineData("e1.halfgate.gateway.too.long.id")]  // 6 segments — let parser still pick city
    public void ExtractCitySlug_returns_null_for_malformed_input(string? targetPoi)
    {
        // The "too long" case is intentionally allowed by our parser
        // (the regex validation is server-side) — we only care about
        // segment 2, so e1.halfgate.gateway.too.long.id RESOLVES to
        // "halfgate". Override that one case here.
        var expected = targetPoi == "e1.halfgate.gateway.too.long.id" ? "halfgate" : null;
        Assert.Equal(expected, MissionPositionResolverLogic.ExtractCitySlug(targetPoi));
    }

    // --- BuildCityIndex -------------------------------------------------

    [Fact]
    public void BuildCityIndex_returns_empty_for_null_input()
    {
        var idx = MissionPositionResolverLogic.BuildCityIndex(null);
        Assert.Empty(idx);
    }

    [Fact]
    public void BuildCityIndex_returns_empty_for_empty_list()
    {
        var idx = MissionPositionResolverLogic.BuildCityIndex(new List<WorldCityDto>());
        Assert.Empty(idx);
    }

    [Fact]
    public void BuildCityIndex_indexes_by_city_id_and_first_occurrence_wins()
    {
        var halfgate = new WorldCityDto(
            "halfgate", "Halfgate",
            new WorldPositionDto(92000, 38000),
            new[] { "lower_quays" }, null);
        var dup = new WorldCityDto(
            "halfgate", "Halfgate Duplicate",
            new WorldPositionDto(0, 0),
            System.Array.Empty<string>(), null);

        var idx = MissionPositionResolverLogic.BuildCityIndex(new[] { halfgate, dup });
        Assert.Single(idx);
        Assert.Same(halfgate, idx["halfgate"]);
    }

    // --- ResolveForCity -------------------------------------------------

    private static readonly IReadOnlyDictionary<string, WorldCityDto> _halfgateOnly =
        MissionPositionResolverLogic.BuildCityIndex(new[]
        {
            new WorldCityDto(
                "halfgate", "Halfgate",
                new WorldPositionDto(92000, 38000),
                new[] { "lower_quays" }, null),
        });

    private static EmergentMissionDto MakeMission(string targetPoi, string id = "M1")
    {
        return new EmergentMissionDto(
            Id: id,
            Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: "stub",
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0,
            TargetPoi: targetPoi,
            RecruitTargetNpcId: "kira");
    }

    [Fact]
    public void ResolveForCity_returns_city_position_for_valid_mission()
    {
        var mission = MakeMission("e2.halfgate.gateway");
        var pos = MissionPositionResolverLogic.ResolveForCity(mission, _halfgateOnly);
        Assert.NotNull(pos);
        Assert.Equal(92000, pos!.X);
        Assert.Equal(38000, pos!.Y);
    }

    [Fact]
    public void ResolveForCity_returns_same_position_for_e1_fallback_target_poi()
    {
        // M1 emits "e1.{region}" for regions without visible E2 districts.
        // Same lookup path, same answer.
        var mission = MakeMission("e1.halfgate");
        var pos = MissionPositionResolverLogic.ResolveForCity(mission, _halfgateOnly);
        Assert.NotNull(pos);
        Assert.Equal(92000, pos!.X);
    }

    [Fact]
    public void ResolveForCity_returns_null_for_unknown_city()
    {
        var mission = MakeMission("e2.northport.guildhall");
        var pos = MissionPositionResolverLogic.ResolveForCity(mission, _halfgateOnly);
        Assert.Null(pos);
    }

    [Fact]
    public void ResolveForCity_returns_null_for_malformed_target_poi()
    {
        var mission = MakeMission("not_a_poi");
        Assert.Null(MissionPositionResolverLogic.ResolveForCity(mission, _halfgateOnly));
    }

    [Fact]
    public void ResolveForCity_returns_null_for_null_mission()
    {
        Assert.Null(MissionPositionResolverLogic.ResolveForCity(null, _halfgateOnly));
    }

    [Fact]
    public void ResolveForCity_returns_null_for_empty_referential()
    {
        var mission = MakeMission("e2.halfgate.gateway");
        var emptyIdx = MissionPositionResolverLogic.BuildCityIndex(
            System.Array.Empty<WorldCityDto>());
        Assert.Null(MissionPositionResolverLogic.ResolveForCity(mission, emptyIdx));
    }

    [Fact]
    public void ResolveForCity_three_M1_recruit_missions_all_land_on_halfgate_centroid()
    {
        // The M1 norm : kira / dorn / vell all target Halfgate districts.
        // All three resolve to the same city centroid (the cluster-spiral
        // is what spreads them visually — see MissionPoiLayerLogicTests).
        var missions = new[]
        {
            MakeMission("e2.halfgate.gateway", "M_kira"),
            MakeMission("e2.halfgate.intramuros", "M_dorn"),
            MakeMission("e2.halfgate.littoral", "M_vell"),
        };
        foreach (var m in missions)
        {
            var pos = MissionPositionResolverLogic.ResolveForCity(m, _halfgateOnly);
            Assert.NotNull(pos);
            Assert.Equal(92000, pos!.X);
            Assert.Equal(38000, pos!.Y);
        }
    }

    // --- ToWorldMetres seam --------------------------------------------

    [Fact]
    public void ToWorldMetres_packs_position_into_sys_vec2()
    {
        var v = MissionPositionResolverLogic.ToWorldMetres(new WorldPositionDto(12, -34));
        Assert.Equal(12f, v.X);
        Assert.Equal(-34f, v.Y);
    }
}
