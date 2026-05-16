using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the Varn-locked E2-layer mission authoring (E2.1c brief
/// 2026-05-16) + the AreaGridLogic.DistrictCentroid lookup the render
/// layer reads. Each test anchors one lock so a silent edit surfaces
/// as a red xUnit step.
///
/// <para>
/// <b>E2.1c profond refonte (2026-05-16, Didier lock).</b> The grid
/// dropped the 2×2 block constraint and switched to per-district
/// irregular cell lists (39 attributed + 25 neutral). The centroid
/// anchors moved from "corner intersection of the 2×2 block" to "one of
/// 16 axis-aligned zone-centres" (= <c>(2*zCol + 1.0, 2*zRow + 1.0)</c>
/// for some integer zCol, zRow in [0, 3]). The picked zone is guaranteed
/// to contain at least one cell of the target district.
/// </para>
/// </summary>
public sealed class HalfgateE2MissionAuthoringTests
{
    // ---- Mission ids + count ----

    [Fact]
    public void Three_e2_missions_are_authored()
    {
        // E2.1c brief : 2-3 missions ; we land at 3 (Intramuros + Gateway +
        // Littoral) to exercise three visually-distinct district zones.
        Assert.Equal(3, HalfgateE2MissionAuthoring.All.Count);
    }

    [Fact]
    public void Mission_ids_are_locked_and_distinct()
    {
        Assert.Equal("mission_tuto_intramuros_e2", HalfgateE2MissionAuthoring.IntramurosMissionId);
        Assert.Equal("mission_tuto_gateway_e2",    HalfgateE2MissionAuthoring.GatewayMissionId);
        Assert.Equal("mission_tuto_littoral_e2",   HalfgateE2MissionAuthoring.LittoralMissionId);

        var ids = HalfgateE2MissionAuthoring.All.Select(m => m.MissionId).ToList();
        Assert.Equal(3, ids.Distinct().Count());
    }

    // ---- A4.7 schema fields per mission ----

    [Theory]
    [InlineData("mission_tuto_intramuros_e2", "halfgate.intramuros", DistrictType.Intramuros)]
    [InlineData("mission_tuto_gateway_e2",    "halfgate.gateway",    DistrictType.Gateway)]
    [InlineData("mission_tuto_littoral_e2",   "halfgate.littoral",   DistrictType.Littoral)]
    public void Each_mission_locks_poi_id_and_target_district(
        string missionId, string expectedPoiId, DistrictType expectedDistrict)
    {
        var mission = HalfgateE2MissionAuthoring.All.Single(m => m.MissionId == missionId);
        Assert.Equal(expectedPoiId, mission.PoiId);
        Assert.Equal(expectedDistrict, mission.TargetDistrict);
    }

    [Fact]
    public void Every_mission_lives_at_the_e2_poi_layer()
    {
        // A4.7 lock : poi_layer = e2 for every entry of this authoring
        // file (the file IS the E2 mission set). A drift here would
        // either mean a misplaced E1 mission OR a future E3 layer
        // leaking into this authoring -- both surface here.
        foreach (var m in HalfgateE2MissionAuthoring.All)
        {
            Assert.Equal(RevealLayer.E2, m.PoiLayer);
        }
    }

    [Fact]
    public void Every_mission_has_empty_pop_effect_tiles_to_partial_layer()
    {
        // E2.1c brief lock : the E2 cells are already at Partial via the
        // E1 tuto's R3 projection. Re-applying a per-district partial
        // flip would re-write cells another mission owns, breaking the
        // "one mission owns one reveal" invariant E2.2 will need.
        foreach (var m in HalfgateE2MissionAuthoring.All)
        {
            Assert.NotNull(m.PopEffectTilesToPartialLayer);
            Assert.Empty(m.PopEffectTilesToPartialLayer);
        }
    }

    [Fact]
    public void Every_mission_locks_success_effect_reveal_rule_to_none()
    {
        // E2.1c brief lock : no success flow yet (recruit lands at E2.2).
        // The schema slot is filled with the None value so the closed-
        // lookup discipline is preserved. E2.2 will flip to ByRecruitedNpc
        // -- this test then needs an explicit edit to reflect the new
        // lock.
        foreach (var m in HalfgateE2MissionAuthoring.All)
        {
            Assert.Equal(MissionRevealRule.None, m.SuccessEffectRevealRule);
        }
    }

    [Fact]
    public void Every_mission_target_district_actually_exists_in_the_grid()
    {
        // Sanity guard : a mission cannot target a district that has no
        // cells in the grid (the marker would land at the defensive
        // fallback). At E2.1c profond refonte every district has ≥ 4
        // authored cells.
        foreach (var m in HalfgateE2MissionAuthoring.All)
        {
            var cells = AreaGridLogic.CellsOfDistrict(m.TargetDistrict).ToList();
            Assert.NotEmpty(cells);
        }
    }

    [Fact]
    public void Every_narrative_hook_fits_the_pratchett_tone_budget()
    {
        // Tonal DNA constraint (relaxed at E2.1c since hooks are not
        // surfaced yet) : 40-200 chars, single line, ends with a period.
        foreach (var m in HalfgateE2MissionAuthoring.All)
        {
            Assert.InRange(m.NarrativeHook.Length, 40, 200);
            Assert.DoesNotContain('\n', m.NarrativeHook);
            Assert.EndsWith(".", m.NarrativeHook);
        }
    }

    // ---- AreaGridLogic.CellsOfDistrict + DistrictCentroid -----

    [Fact]
    public void Cells_of_district_partition_the_grid_into_sixty_four()
    {
        // The 6 district enumerations must together cover all 64 cells
        // with no duplicates. At E2.1c profond refonte the partition is :
        //   - Intramuros     = 11
        //   - Wall           =  8
        //   - Gateway        =  4
        //   - HinterlandAgri =  4
        //   - Littoral       =  6
        //   - Outskirts      =  6 authored + 25 fallback = 31
        //   - total          = 11 + 8 + 4 + 4 + 6 + 31    = 64
        // A drift in the DistrictCellsTable (a misplaced cell) would
        // shift cells from Outskirts (fallback) into another district
        // and the sum would still be 64 -- but the per-district counts
        // below catch that.
        int total = 0;
        foreach (var d in System.Enum.GetValues<DistrictType>())
        {
            total += AreaGridLogic.CellsOfDistrict(d).Count();
        }
        Assert.Equal(64, total);
    }

    [Theory]
    // E2.1c profond refonte counts.
    // Outskirts is 31 = 6 authored cells + 25 unattributed cells that
    // fall through ResolveDistrictType's defensive Outskirts default.
    [InlineData(DistrictType.Intramuros,     11)]
    [InlineData(DistrictType.Wall,            8)]
    [InlineData(DistrictType.Gateway,         4)]
    [InlineData(DistrictType.Outskirts,      31)]
    [InlineData(DistrictType.HinterlandAgri,  4)]
    [InlineData(DistrictType.Littoral,        6)]
    public void Cells_of_district_counts_match_the_authoring(DistrictType d, int expectedCount)
    {
        Assert.Equal(expectedCount, AreaGridLogic.CellsOfDistrict(d).Count());
    }

    [Theory]
    // E2.1c profond refonte zone-centre centroids :
    //   Intramuros     -> zone (1,1) -> (3.0, 3.0)   the centre of the centre
    //   Wall           -> zone (2,2) -> (5.0, 5.0)   SE corner of the ring
    //   Gateway        -> zone (1,0) -> (3.0, 1.0)   N strip
    //   Outskirts      -> zone (0,2) -> (1.0, 5.0)   NW corner of the SW faubourg
    //   HinterlandAgri -> zone (2,0) -> (5.0, 1.0)   NE field cluster
    //   Littoral       -> zone (0,1) -> (1.0, 3.0)   densest Littoral zone
    [InlineData(DistrictType.Intramuros,     3.0f, 3.0f)]
    [InlineData(DistrictType.Wall,           5.0f, 5.0f)]
    [InlineData(DistrictType.Gateway,        3.0f, 1.0f)]
    [InlineData(DistrictType.Outskirts,      1.0f, 5.0f)]
    [InlineData(DistrictType.HinterlandAgri, 5.0f, 1.0f)]
    [InlineData(DistrictType.Littoral,       1.0f, 3.0f)]
    public void District_centroid_anchors_are_locked(
        DistrictType d, float expectedCol, float expectedRow)
    {
        // E2.1c profond refonte Didier lock : these 6 fractional anchors
        // drive where the POI markers spawn (zone-centre on the 4×4
        // lattice). A silent edit shifts a marker visually.
        var centroid = AreaGridLogic.DistrictCentroid(d);
        Assert.Equal(expectedCol, centroid.Col, 3);
        Assert.Equal(expectedRow, centroid.Row, 3);
    }

    [Fact]
    public void District_centroid_is_total_returns_zone_one_one_for_unknown_value()
    {
        // Defensive : a stale persisted int returns zone (1,1)'s centroid
        // (3.0, 3.0) -- the most central position on the 16-slot lattice
        // -- so the marker still spawns at a visible position (least
        // surprising fallback) while a higher layer logs the typo.
        var centroid = AreaGridLogic.DistrictCentroid((DistrictType)42);
        Assert.Equal(3.0f, centroid.Col);
        Assert.Equal(3.0f, centroid.Row);
    }

    [Theory]
    [InlineData(DistrictType.Intramuros)]
    [InlineData(DistrictType.Wall)]
    [InlineData(DistrictType.Gateway)]
    [InlineData(DistrictType.Outskirts)]
    [InlineData(DistrictType.HinterlandAgri)]
    [InlineData(DistrictType.Littoral)]
    public void District_centroid_lands_inside_the_grid_bounds(DistrictType d)
    {
        // Pin the soft invariant : every centroid sits inside [0, 7]
        // for both col and row. An out-of-range anchor would spawn the
        // marker off-screen or outside the camera bounds.
        var centroid = AreaGridLogic.DistrictCentroid(d);
        Assert.InRange(centroid.Col, 0.0f, 7.0f);
        Assert.InRange(centroid.Row, 0.0f, 7.0f);
    }

    [Theory]
    [InlineData(DistrictType.Intramuros)]
    [InlineData(DistrictType.Wall)]
    [InlineData(DistrictType.Gateway)]
    [InlineData(DistrictType.Outskirts)]
    [InlineData(DistrictType.HinterlandAgri)]
    [InlineData(DistrictType.Littoral)]
    public void District_centroid_lands_on_a_zone_centre(DistrictType d)
    {
        // E2.1c profond refonte invariant : the centroid is GUARANTEED
        // to sit on one of the 16 zone-centres. Decompose centroid into
        // (zCol, zRow) integers and verify it lines up with
        // (2*zCol + 1, 2*zRow + 1).
        var centroid = AreaGridLogic.DistrictCentroid(d);
        // Subtract 1 then divide by ZoneSize. If the result is not an
        // integer in [0, ZonesPerAxis), the centroid is off-lattice.
        var rawZCol = (centroid.Col - 1.0f) / AreaGridLogic.ZoneSize;
        var rawZRow = (centroid.Row - 1.0f) / AreaGridLogic.ZoneSize;
        // Integer check : the float should be very close to its int cast.
        Assert.Equal(rawZCol, (int)rawZCol, 3);
        Assert.Equal(rawZRow, (int)rawZRow, 3);
        Assert.InRange((int)rawZCol, 0, AreaGridLogic.ZonesPerAxis - 1);
        Assert.InRange((int)rawZRow, 0, AreaGridLogic.ZonesPerAxis - 1);
    }

    [Theory]
    [InlineData(DistrictType.Intramuros)]
    [InlineData(DistrictType.Wall)]
    [InlineData(DistrictType.Gateway)]
    [InlineData(DistrictType.Outskirts)]
    [InlineData(DistrictType.HinterlandAgri)]
    [InlineData(DistrictType.Littoral)]
    public void District_centroid_zone_contains_at_least_one_cell_of_district(DistrictType d)
    {
        // E2.1c profond refonte invariant (Didier brief 2026-05-16) :
        // the zone picked by DistrictCentroid must contain AT LEAST ONE
        // cell of the target district. A bad pick (no district cells in
        // the zone) would land the POI marker on cells of a different
        // district, breaking the narrative anchor.
        var centroid = AreaGridLogic.DistrictCentroid(d);
        var zCol = (int)((centroid.Col - 1.0f) / AreaGridLogic.ZoneSize);
        var zRow = (int)((centroid.Row - 1.0f) / AreaGridLogic.ZoneSize);

        var zoneCells = AreaGridLogic.CellsOfZone(zCol, zRow).ToList();
        Assert.Equal(4, zoneCells.Count);

        // Use IsInDistrict to avoid the Outskirts fallback collision :
        // the zone may contain a neutral cell that ResolveDistrictType
        // returns as Outskirts, which would spuriously satisfy the
        // contract for the Outskirts district.
        bool found = false;
        foreach (var c in zoneCells)
        {
            if (AreaGridLogic.IsInDistrict(c)
                && AreaGridLogic.ResolveDistrictType(c) == d)
            {
                found = true;
                break;
            }
        }
        Assert.True(found,
            $"Zone ({zCol},{zRow}) chosen for district {d} contains no " +
            $"authored cells of that district -- the marker would land " +
            $"visually adrift from the district body.");
    }

    // The scope-mode flip itself (E2AreaMap.ScopeMode == "E2.1c") is
    // engine-coupled (lives on a Godot Node partial) and not reachable
    // from this Godot-free xUnit host. It is validated via the F5 smoke
    // checklist + the boot log line emitted by BuildAreaGrid
    // ("scope=E2.1c, tiles=64 (district=39, neutral=25), ..., e2_poi_markers=3").
}
