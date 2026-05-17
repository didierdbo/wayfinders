using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Section A contract pin (Varn-lock 2026-05-17 + Didier brief 2026-05-17)
/// — exercise the pure-C# projection helpers behind the
/// <c>MissionStore</c> autoload. The Godot-bound polling lifecycle (HTTP
/// fetch via <c>ApiClient</c>, <c>CallDeferred</c> marshalling, signal
/// emission) is not exercised here — same separation as
/// <c>MissionPanelLogic</c> / <c>MissionPanelProbe</c>. End-to-end
/// integration is the F6 smoke test.
///
/// <para>
/// Pinned : the hierarchical-id matching rule (exact + prefix), the
/// empty/null defensive paths, the source-order stable enumeration, and
/// the legacy-shape isolation (an <c>e1.halfgate</c>-shape mission does
/// NOT match the <c>e2.halfgate</c> prefix, mirrors the §A.3 step 3 pin
/// from the consolidated spec).
/// </para>
/// </summary>
public sealed class MissionStoreContractTests
{
    private static EmergentMissionDto MakeMission(string id, string targetPoi) =>
        new(
            Id: id,
            Type: WorldTickWireFormat.MissionType.ScoutRoute,
            NarrativeHook: $"Stub hook for {id}.",
            EligiblePersonas: new List<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: targetPoi);

    // ---- GetMissionsForPoi ----

    [Fact]
    public void GetMissionsForPoi_with_null_missions_yields_empty()
    {
        var result = MissionStoreLogic.GetMissionsForPoi(null, "e2.halfgate.gateway");
        Assert.Empty(result);
    }

    [Fact]
    public void GetMissionsForPoi_with_empty_missions_yields_empty()
    {
        var result = MissionStoreLogic.GetMissionsForPoi(
            System.Array.Empty<EmergentMissionDto>(),
            "e2.halfgate.gateway");
        Assert.Empty(result);
    }

    [Fact]
    public void GetMissionsForPoi_with_empty_poiId_yields_empty()
    {
        var missions = new[] { MakeMission("ml_a", "e2.halfgate.gateway") };
        Assert.Empty(MissionStoreLogic.GetMissionsForPoi(missions, ""));
        Assert.Empty(MissionStoreLogic.GetMissionsForPoi(missions, null!));
    }

    [Fact]
    public void GetMissionsForPoi_exact_match_returns_single_mission()
    {
        var missions = new[]
        {
            MakeMission("ml_a", "e2.halfgate.gateway"),
            MakeMission("ml_b", "e2.halfgate.intramuros"),
            MakeMission("ml_c", "e2.halfgate.littoral"),
        };
        var result = MissionStoreLogic.GetMissionsForPoi(missions, "e2.halfgate.gateway");
        Assert.Single(result);
        Assert.Equal("ml_a", result[0].Id);
    }

    [Fact]
    public void GetMissionsForPoi_multiple_missions_same_poi_returns_all_in_source_order()
    {
        // Two simultaneous missions on the same district — the M1 cap
        // allows this when the cadence stacks. The matcher must not
        // dedup or sort ; the server's spawn order is the canonical
        // surface.
        var missions = new[]
        {
            MakeMission("ml_a", "e2.halfgate.gateway"),
            MakeMission("ml_b", "e2.halfgate.gateway"),
            MakeMission("ml_c", "e2.halfgate.intramuros"),
        };
        var result = MissionStoreLogic.GetMissionsForPoi(missions, "e2.halfgate.gateway");
        Assert.Equal(2, result.Count);
        Assert.Equal("ml_a", result[0].Id);
        Assert.Equal("ml_b", result[1].Id);
    }

    [Fact]
    public void GetMissionsForPoi_prefix_match_does_not_count()
    {
        // GetMissionsForPoi is EXACT match only. A mission targeting
        // "e2.halfgate.gateway.subzone" does NOT match the query
        // "e2.halfgate.gateway" (which is what GetMissionsForLayer
        // exists for). Pin the seam between the two helpers.
        var missions = new[]
        {
            MakeMission("ml_a", "e2.halfgate.gateway.subzone"),
        };
        Assert.Empty(MissionStoreLogic.GetMissionsForPoi(missions, "e2.halfgate.gateway"));
    }

    // ---- GetMissionsForLayer ----

    [Fact]
    public void GetMissionsForLayer_with_null_or_empty_input_yields_empty()
    {
        Assert.Empty(MissionStoreLogic.GetMissionsForLayer(null, "e2.halfgate"));
        Assert.Empty(MissionStoreLogic.GetMissionsForLayer(
            System.Array.Empty<EmergentMissionDto>(), "e2.halfgate"));
    }

    [Fact]
    public void GetMissionsForLayer_with_empty_prefix_yields_empty()
    {
        var missions = new[] { MakeMission("ml_a", "e2.halfgate.gateway") };
        Assert.Empty(MissionStoreLogic.GetMissionsForLayer(missions, ""));
        Assert.Empty(MissionStoreLogic.GetMissionsForLayer(missions, null!));
    }

    [Fact]
    public void GetMissionsForLayer_returns_descendants_of_prefix()
    {
        var missions = new[]
        {
            MakeMission("ml_a", "e2.halfgate.gateway"),
            MakeMission("ml_b", "e2.halfgate.intramuros"),
            MakeMission("ml_c", "e2.halfgate.littoral"),
        };
        var result = MissionStoreLogic.GetMissionsForLayer(missions, "e2.halfgate");
        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "ml_a", "ml_b", "ml_c" }, result.Select(m => m.Id));
    }

    [Fact]
    public void GetMissionsForLayer_includes_exact_match()
    {
        // The prefix matcher MUST also match the prefix itself (i.e.
        // a mission targeting "e2.halfgate" exactly counts as a member
        // of the "e2.halfgate" layer). Mirrors PoiTreeService.IsDescendantOf
        // contract.
        var missions = new[]
        {
            MakeMission("ml_root", "e2.halfgate"),
            MakeMission("ml_child", "e2.halfgate.gateway"),
        };
        var result = MissionStoreLogic.GetMissionsForLayer(missions, "e2.halfgate");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetMissionsForLayer_excludes_legacy_e1_shape()
    {
        // A legacy "e1.halfgate" mission must NOT surface under the
        // "e2.halfgate" prefix — the layer-prefix is the explicit
        // isolation seam. Mirrors §A.3 step 3 of the consolidated spec.
        var missions = new[]
        {
            MakeMission("ml_legacy", "e1.halfgate"),
            MakeMission("ml_modern", "e2.halfgate.gateway"),
        };
        var result = MissionStoreLogic.GetMissionsForLayer(missions, "e2.halfgate");
        Assert.Single(result);
        Assert.Equal("ml_modern", result[0].Id);
    }

    [Fact]
    public void GetMissionsForLayer_excludes_sibling_prefixes()
    {
        // A mission targeting "e2.halfgate2.x" (hypothetical sibling
        // region with a longer slug) must NOT match the prefix
        // "e2.halfgate". The dot separator is enforced — String.StartsWith
        // alone would falsely match "halfgate2".
        var missions = new[]
        {
            MakeMission("ml_sibling", "e2.halfgate2.x"),
            MakeMission("ml_real", "e2.halfgate.gateway"),
        };
        var result = MissionStoreLogic.GetMissionsForLayer(missions, "e2.halfgate");
        Assert.Single(result);
        Assert.Equal("ml_real", result[0].Id);
    }

    [Fact]
    public void GetMissionsForLayer_two_missions_same_district_both_surface()
    {
        // §A.3 step 4 of the consolidated spec : two missions on the
        // same district both surface via the layer prefix. The composer
        // then handles the multi-row tooltip.
        var missions = new[]
        {
            MakeMission("ml_a", "e2.halfgate.gateway"),
            MakeMission("ml_b", "e2.halfgate.gateway"),
        };
        var result = MissionStoreLogic.GetMissionsForLayer(missions, "e2.halfgate");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetMissionsForLayer_preserves_source_order()
    {
        var missions = new[]
        {
            MakeMission("ml_c", "e2.halfgate.littoral"),
            MakeMission("ml_a", "e2.halfgate.gateway"),
            MakeMission("ml_b", "e2.halfgate.intramuros"),
        };
        var result = MissionStoreLogic.GetMissionsForLayer(missions, "e2.halfgate");
        Assert.Equal(new[] { "ml_c", "ml_a", "ml_b" }, result.Select(m => m.Id));
    }

    [Fact]
    public void GetMissionsForLayer_skips_missions_with_empty_target_poi()
    {
        // Defensive — a mission with TargetPoi="" (the legacy default
        // value before the §1 PoiId schema lock) MUST be skipped, not
        // matched as "starts with anything".
        var missions = new[]
        {
            MakeMission("ml_blank", ""),
            MakeMission("ml_real",  "e2.halfgate.gateway"),
        };
        var result = MissionStoreLogic.GetMissionsForLayer(missions, "e2.halfgate");
        Assert.Single(result);
        Assert.Equal("ml_real", result[0].Id);
    }
}
