using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the Varn-locked E2-layer mission authoring (E2.1c brief
/// 2026-05-16) + the AreaGridLogic.DistrictCentroid lookup the render
/// layer reads. Each test anchors one lock so a silent edit surfaces
/// as a red xUnit step.
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
        // (3.5, 3.5) fallback, occluded by Intramuros). The grid layout
        // is Varn-locked too, so this test also pins the contract
        // "every E2 mission's target_district has at least one cell".
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
    public void Cells_of_district_partition_the_grid_into_six_disjoint_sets()
    {
        // The 6 district enumerations must together cover all 64 cells
        // with no duplicates. A drift in the _grid table (a stray cell
        // value) would surface here.
        int total = 0;
        foreach (var d in System.Enum.GetValues<DistrictType>())
        {
            total += AreaGridLogic.CellsOfDistrict(d).Count();
        }
        Assert.Equal(64, total);
    }

    [Theory]
    // Counts derived from the Varn-locked ASCII layout :
    //   Intramuros  12 cells (4 inner + 8 ring)
    //   Wall         8 cells (corner pairs + inner-ring corners)
    //   Gateway      8 cells (N strip 2 + S strip 2 + E + W single gates 4)
    //   Outskirts   12 cells
    //   Hinterland  20 cells (rows 0 + 7 = 12 + corners of rows 1 + 6 = 4 + ... = 20 total)
    //   Littoral     8 cells (col 0 rows 2..5 + col 7 rows 2..5)
    [InlineData(DistrictType.Intramuros,     12)]
    [InlineData(DistrictType.Wall,            8)]
    [InlineData(DistrictType.Gateway,         8)]
    [InlineData(DistrictType.Outskirts,      12)]
    [InlineData(DistrictType.HinterlandAgri, 16)]
    [InlineData(DistrictType.Littoral,        8)]
    public void Cells_of_district_counts_match_the_authoring(DistrictType d, int expectedCount)
    {
        Assert.Equal(expectedCount, AreaGridLogic.CellsOfDistrict(d).Count());
    }

    [Theory]
    [InlineData(DistrictType.Intramuros,     3.5f,         3.5f)]
    [InlineData(DistrictType.Wall,           3.5f,         1.5f)]
    [InlineData(DistrictType.Gateway,        3.5f,         0.0f)]
    [InlineData(DistrictType.Outskirts,      4.0f / 3f,    4.0f / 3f)]
    [InlineData(DistrictType.HinterlandAgri, 1.0f,         0.0f)]
    [InlineData(DistrictType.Littoral,       0.0f,         3.5f)]
    public void District_centroid_anchors_are_locked(
        DistrictType d, float expectedCol, float expectedRow)
    {
        // E2.1c Rune lock : these 6 fractional anchors drive where the
        // POI markers spawn. A silent edit shifts a marker visually.
        var centroid = AreaGridLogic.DistrictCentroid(d);
        Assert.Equal(expectedCol, centroid.Col, 3);
        Assert.Equal(expectedRow, centroid.Row, 3);
    }

    [Fact]
    public void District_centroid_is_total_returns_grid_centre_for_unknown_value()
    {
        // Defensive : a stale persisted int casts to (3.5, 3.5) so the
        // marker still spawns at a visible position (least surprising
        // fallback) while a higher layer logs the typo.
        var centroid = AreaGridLogic.DistrictCentroid((DistrictType)42);
        Assert.Equal(3.5f, centroid.Col);
        Assert.Equal(3.5f, centroid.Row);
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

    // The scope-mode flip itself (E2AreaMap.ScopeMode == "E2.1c") is
    // engine-coupled (lives on a Godot Node partial) and not reachable
    // from this Godot-free xUnit host. It is validated via the F5 smoke
    // checklist + the boot log line emitted by BuildAreaGrid
    // ("scope=E2.1c, ..., e2_poi_markers=3").
}
