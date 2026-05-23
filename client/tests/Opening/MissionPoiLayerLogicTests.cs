using System;
using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin tests for <see cref="MissionPoiLayerLogic"/> — the three pure
/// helpers backing <see cref="MissionPoiLayer"/> (Étape A, Rune,
/// 2026-05-23) :
/// <list type="number">
///   <item><see cref="MissionPoiLayerLogic.DiffMissions"/> — reconcile
///         add / remove / survive between two mission-id snapshots.</item>
///   <item><see cref="MissionPoiLayerLogic.ClusterSpiralOffset"/> +
///         <see cref="MissionPoiLayerLogic.AssignClusterIndices"/> —
///         spread sibling pins on a deterministic golden-angle spiral.</item>
///   <item><see cref="MissionPoiLayerLogic.PickClosestPin"/> —
///         closest-wins hit-test inside the per-pin pick radius.</item>
/// </list>
/// </summary>
public sealed class MissionPoiLayerLogicTests
{
    private static EmergentMissionDto MakeMission(string id, string targetPoi)
    {
        return new EmergentMissionDto(
            Id: id,
            Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: "stub",
            EligiblePersonas: Array.Empty<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0,
            TargetPoi: targetPoi,
            RecruitTargetNpcId: "kira");
    }

    // --- DiffMissions ---------------------------------------------------

    [Fact]
    public void DiffMissions_empty_inputs_yield_empty_diff()
    {
        var diff = MissionPoiLayerLogic.DiffMissions(null, null);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Surviving);
    }

    [Fact]
    public void DiffMissions_initial_load_marks_everything_added()
    {
        var snapshot = new[]
        {
            MakeMission("M1", "e2.halfgate.gateway"),
            MakeMission("M2", "e2.halfgate.intramuros"),
            MakeMission("M3", "e2.halfgate.littoral"),
        };
        var diff = MissionPoiLayerLogic.DiffMissions(Array.Empty<string>(), snapshot);
        Assert.Equal(new[] { "M1", "M2", "M3" }, diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Surviving);
    }

    [Fact]
    public void DiffMissions_unchanged_snapshot_marks_everything_surviving()
    {
        var snapshot = new[] { MakeMission("M1", "e2.halfgate.gateway") };
        var diff = MissionPoiLayerLogic.DiffMissions(new[] { "M1" }, snapshot);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Equal(new[] { "M1" }, diff.Surviving);
    }

    [Fact]
    public void DiffMissions_remove_one_mission()
    {
        var snapshot = new[] { MakeMission("M1", "e2.halfgate.gateway") };
        var diff = MissionPoiLayerLogic.DiffMissions(new[] { "M1", "M2", "M3" }, snapshot);
        Assert.Empty(diff.Added);
        Assert.Equal(new[] { "M2", "M3" }, diff.Removed);
        Assert.Equal(new[] { "M1" }, diff.Surviving);
    }

    [Fact]
    public void DiffMissions_add_remove_and_survive_mixed()
    {
        var snapshot = new[]
        {
            MakeMission("M2", "e2.halfgate.intramuros"),
            MakeMission("M4", "e2.halfgate.littoral"),
        };
        var diff = MissionPoiLayerLogic.DiffMissions(new[] { "M1", "M2", "M3" }, snapshot);
        Assert.Equal(new[] { "M4" }, diff.Added);
        Assert.Equal(new[] { "M1", "M3" }, diff.Removed);
        Assert.Equal(new[] { "M2" }, diff.Surviving);
    }

    [Fact]
    public void DiffMissions_skips_null_and_empty_ids()
    {
        var snapshot = new[]
        {
            MakeMission("", "e2.halfgate.gateway"), // empty id, skipped
            MakeMission("M1", "e2.halfgate.gateway"),
        };
        var diff = MissionPoiLayerLogic.DiffMissions(null, snapshot);
        Assert.Equal(new[] { "M1" }, diff.Added);
    }

    [Fact]
    public void DiffMissions_dedupes_repeated_ids_in_snapshot()
    {
        var snapshot = new[]
        {
            MakeMission("M1", "e2.halfgate.gateway"),
            MakeMission("M1", "e2.halfgate.gateway"), // duplicate
        };
        var diff = MissionPoiLayerLogic.DiffMissions(null, snapshot);
        Assert.Equal(new[] { "M1" }, diff.Added);
    }

    // --- ClusterSpiralOffset -------------------------------------------

    [Fact]
    public void ClusterSpiralOffset_index_zero_is_origin()
    {
        var offset = MissionPoiLayerLogic.ClusterSpiralOffset(0);
        Assert.Equal(SysVec2.Zero, offset);
    }

    [Fact]
    public void ClusterSpiralOffset_index_one_is_at_base_radius()
    {
        var offset = MissionPoiLayerLogic.ClusterSpiralOffset(1, baseRadiusPx: 14f);
        var magnitude = MathF.Sqrt(offset.X * offset.X + offset.Y * offset.Y);
        Assert.Equal(14f, magnitude, 3);
    }

    [Fact]
    public void ClusterSpiralOffset_grows_radius_with_sqrt_index()
    {
        // r = base * sqrt(i) => r(4) = 2 * base.
        var o4 = MissionPoiLayerLogic.ClusterSpiralOffset(4, baseRadiusPx: 10f);
        var r4 = MathF.Sqrt(o4.X * o4.X + o4.Y * o4.Y);
        Assert.Equal(20f, r4, 3);
    }

    [Fact]
    public void ClusterSpiralOffset_three_M1_pins_are_pairwise_distinct_and_close()
    {
        var o0 = MissionPoiLayerLogic.ClusterSpiralOffset(0);
        var o1 = MissionPoiLayerLogic.ClusterSpiralOffset(1, baseRadiusPx: 14f);
        var o2 = MissionPoiLayerLogic.ClusterSpiralOffset(2, baseRadiusPx: 14f);
        Assert.NotEqual(o0, o1);
        Assert.NotEqual(o0, o2);
        Assert.NotEqual(o1, o2);
        // All three within a 30 px circle of the base position (~visually
        // clustered "in Halfgate" on the 2048-wide eM mesh).
        foreach (var o in new[] { o1, o2 })
        {
            Assert.True(MathF.Sqrt(o.X * o.X + o.Y * o.Y) < 30f);
        }
    }

    [Fact]
    public void ClusterSpiralOffset_is_deterministic()
    {
        var a = MissionPoiLayerLogic.ClusterSpiralOffset(7, baseRadiusPx: 14f);
        var b = MissionPoiLayerLogic.ClusterSpiralOffset(7, baseRadiusPx: 14f);
        Assert.Equal(a, b);
    }

    // --- AssignClusterIndices ------------------------------------------

    [Fact]
    public void AssignClusterIndices_M1_three_recruit_missions_indexed_by_id_ascending()
    {
        var missions = new[]
        {
            MakeMission("M_vell", "e2.halfgate.littoral"),
            MakeMission("M_kira", "e2.halfgate.gateway"),
            MakeMission("M_dorn", "e2.halfgate.intramuros"),
        };
        var indexed = MissionPoiLayerLogic.AssignClusterIndices(missions);
        // All three resolve to "halfgate" ; sorted by id ascending :
        //   M_dorn (0), M_kira (1), M_vell (2).
        var byId = indexed.ToDictionary(x => x.Mission.Id, x => x.IndexInCluster);
        Assert.Equal(0, byId["M_dorn"]);
        Assert.Equal(1, byId["M_kira"]);
        Assert.Equal(2, byId["M_vell"]);
        Assert.All(indexed, t => Assert.Equal("halfgate", t.CitySlug));
    }

    [Fact]
    public void AssignClusterIndices_skips_missions_with_unresolvable_target_poi()
    {
        var missions = new[]
        {
            MakeMission("M1", "e2.halfgate.gateway"),
            MakeMission("M_bad", "not_a_poi"),
        };
        var indexed = MissionPoiLayerLogic.AssignClusterIndices(missions);
        Assert.Single(indexed);
        Assert.Equal("M1", indexed[0].Mission.Id);
    }

    [Fact]
    public void AssignClusterIndices_buckets_per_city()
    {
        var missions = new[]
        {
            MakeMission("MA", "e2.halfgate.gateway"),
            MakeMission("MB", "e2.northport.guildhall"),
            MakeMission("MC", "e2.halfgate.littoral"),
        };
        var indexed = MissionPoiLayerLogic.AssignClusterIndices(missions);
        var byCity = indexed
            .GroupBy(t => t.CitySlug)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Mission.Id).OrderBy(s => s).ToList());
        Assert.Equal(new[] { "MA", "MC" }, byCity["halfgate"]);
        Assert.Equal(new[] { "MB" }, byCity["northport"]);

        // Each city group is 0-indexed independently.
        foreach (var bucket in indexed.GroupBy(t => t.CitySlug))
        {
            var orderedIndices = bucket.Select(t => t.IndexInCluster).OrderBy(i => i).ToArray();
            Assert.Equal(0, orderedIndices[0]);
        }
    }

    [Fact]
    public void AssignClusterIndices_empty_input_yields_empty_output()
    {
        Assert.Empty(MissionPoiLayerLogic.AssignClusterIndices(null));
        Assert.Empty(MissionPoiLayerLogic.AssignClusterIndices(Array.Empty<EmergentMissionDto>()));
    }

    // --- PickClosestPin ------------------------------------------------

    [Fact]
    public void PickClosestPin_returns_null_for_empty_input()
    {
        Assert.Null(MissionPoiLayerLogic.PickClosestPin(
            SysVec2.Zero, Array.Empty<(string, SysVec2)>(), 22f));
    }

    [Fact]
    public void PickClosestPin_returns_pin_when_click_lands_on_centre()
    {
        var pins = new[]
        {
            ("M1", new SysVec2(100f, 100f)),
            ("M2", new SysVec2(200f, 100f)),
        };
        Assert.Equal("M1", MissionPoiLayerLogic.PickClosestPin(
            new SysVec2(100f, 100f), pins, 22f));
        Assert.Equal("M2", MissionPoiLayerLogic.PickClosestPin(
            new SysVec2(200f, 100f), pins, 22f));
    }

    [Fact]
    public void PickClosestPin_returns_null_when_click_is_outside_every_radius()
    {
        var pins = new[] { ("M1", new SysVec2(100f, 100f)) };
        // 30 px away, radius 22 => miss.
        Assert.Null(MissionPoiLayerLogic.PickClosestPin(
            new SysVec2(130f, 100f), pins, 22f));
    }

    [Fact]
    public void PickClosestPin_resolves_overlapping_pins_to_the_closer_centre()
    {
        // Two pins close together — the click is closer to M2.
        var pins = new[]
        {
            ("M1", new SysVec2(100f, 100f)),
            ("M2", new SysVec2(110f, 100f)),
        };
        Assert.Equal("M2", MissionPoiLayerLogic.PickClosestPin(
            new SysVec2(108f, 100f), pins, 22f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void PickClosestPin_returns_null_for_non_positive_radius(float r)
    {
        var pins = new[] { ("M1", new SysVec2(100f, 100f)) };
        Assert.Null(MissionPoiLayerLogic.PickClosestPin(
            new SysVec2(100f, 100f), pins, r));
    }

    [Fact]
    public void PickClosestPin_skips_empty_ids()
    {
        var pins = new[]
        {
            ("", new SysVec2(100f, 100f)),
            ("M2", new SysVec2(200f, 100f)),
        };
        Assert.Equal("M2", MissionPoiLayerLogic.PickClosestPin(
            new SysVec2(100f, 100f), pins, 9999f));
    }
}
