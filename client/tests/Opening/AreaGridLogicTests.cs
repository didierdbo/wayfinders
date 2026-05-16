using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the 8×8 grid contract and the Halfgate coord-to-district mapping.
///
/// <para>
/// <b>E2.1c profond refonte (2026-05-16, Didier lock).</b> The grid is
/// now modelled as 6 districts with Varn-locked irregular cell lists
/// (Intramuros 11, Wall 8, Gateway 4, Outskirts 6, HinterlandAgri 4,
/// Littoral 6 = 39 attributed cells). The remaining 25 cells are
/// unattributed neutral cells that fall through the defensive
/// <see cref="AreaGridLogic.ResolveDistrictType"/> path and surface as
/// <see cref="DistrictType.Outskirts"/> (so CellsOfDistrict(Outskirts)
/// returns 31 cells = 6 authored + 25 fallback). The tests pin the
/// per-district cell lists, the 39/25 split, and the new 16-zone POI
/// centroid contract.
/// </para>
/// </summary>
public sealed class AreaGridLogicTests
{
    [Fact]
    public void Grid_size_is_locked_at_eight()
    {
        // E2.1 brief lock : 8×8 = 64 cells. Bump would mean the placeholder
        // tile asset set + the reveal-shader grid_size uniform would need
        // to be re-validated.
        Assert.Equal(8, AreaGridLogic.GridSize);
    }

    [Fact]
    public void Zone_partition_is_locked_at_four_by_four()
    {
        // E2.1c profond refonte : the 8×8 grid is partitioned into 4×4
        // zones of 2×2 cells each, giving 16 zone-centres available to
        // the DistrictCentroid lookup. Any drift here changes the
        // centroid math (CentroidCol = 2*zCol+1.0) and shifts every POI
        // marker visually.
        Assert.Equal(2, AreaGridLogic.ZoneSize);
        Assert.Equal(4, AreaGridLogic.ZonesPerAxis);
    }

    [Fact]
    public void All_cells_yields_sixty_four_distinct_coords()
    {
        var cells = AreaGridLogic.AllCells().ToList();
        Assert.Equal(64, cells.Count);
        Assert.Equal(64, cells.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(7, 7, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(8, 0, false)]
    [InlineData(0, 8, false)]
    [InlineData(8, 8, false)]
    public void IsInBounds_distinguishes_inside_from_outside(int col, int row, bool expected)
    {
        Assert.Equal(expected, AreaGridLogic.IsInBounds(new GridCoord(col, row)));
    }

    [Fact]
    public void Centre_is_at_four_four()
    {
        // Canonical anchor cell for the central POI footprint. (4,4) sits
        // inside the Intramuros 11-cell body at E2.1c profond refonte.
        Assert.Equal(new GridCoord(4, 4), AreaGridLogic.Centre);
    }

    [Fact]
    public void Central_poi_footprint_covers_the_four_intramuros_cells_around_centre()
    {
        var footprint = AreaGridLogic.CentralPoiFootprint();
        Assert.Equal(4, footprint.Count);
        Assert.Contains(new GridCoord(3, 3), footprint);
        Assert.Contains(new GridCoord(4, 3), footprint);
        Assert.Contains(new GridCoord(3, 4), footprint);
        Assert.Contains(new GridCoord(4, 4), footprint);
    }

    [Fact]
    public void Central_footprint_cells_resolve_to_intramuros()
    {
        // A4.2 lock : the mission tuto pop_effect.tiles_to_partial must
        // land on Intramuros cells (= the Intramuros 11-cell body at
        // E2.1c profond refonte). Drift here silently changes the EC6
        // tooltip text.
        foreach (var cell in AreaGridLogic.CentralPoiFootprint())
        {
            Assert.Equal(DistrictType.Intramuros, AreaGridLogic.ResolveDistrictType(cell));
        }
    }

    [Theory]
    // Sample cells per district from the Varn-locked authoring
    // (see AreaGridLogic.DistrictCellsTable). Each district contributes
    // 2 sample cells : one NW-corner anchor + one extremity.
    //
    // Intramuros (11 cells) -- 3×3 core cols 2-4 rows 2-4 + bulge (3,5)(4,5).
    [InlineData(2, 2, DistrictType.Intramuros)]
    [InlineData(4, 5, DistrictType.Intramuros)]
    // Wall (8 cells) -- L-shaped ring east + south.
    [InlineData(5, 3, DistrictType.Wall)]
    [InlineData(5, 6, DistrictType.Wall)]
    // Gateway (4 cells) -- north strip.
    [InlineData(3, 0, DistrictType.Gateway)]
    [InlineData(4, 1, DistrictType.Gateway)]
    // Outskirts (6 cells) -- SW faubourg cols 1-2 rows 5-7.
    [InlineData(1, 5, DistrictType.Outskirts)]
    [InlineData(2, 7, DistrictType.Outskirts)]
    // HinterlandAgri (4 cells) -- NE field cluster.
    [InlineData(5, 1, DistrictType.HinterlandAgri)]
    [InlineData(6, 2, DistrictType.HinterlandAgri)]
    // Littoral (6 cells) -- west coast strip + midriff bulge.
    [InlineData(0, 2, DistrictType.Littoral)]
    [InlineData(1, 4, DistrictType.Littoral)]
    public void ResolveDistrictType_pins_each_district_cell(int col, int row, DistrictType expected)
    {
        Assert.Equal(expected, AreaGridLogic.ResolveDistrictType(new GridCoord(col, row)));
    }

    [Theory]
    // Sample "space-between-quartiers" cells (the 25 unattributed). The
    // critical case is (5, 0) : it sits outside every district list but
    // ResolveDistrictType returns Outskirts as its defensive fallback,
    // which would collide with the legit Outskirts cells if a caller
    // relied on the enum alone. IsInDistrict must return false here.
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(0, 7)]
    [InlineData(7, 7)]
    [InlineData(7, 4)]
    [InlineData(5, 0)]
    [InlineData(1, 1)]
    [InlineData(0, 6)]
    public void ResolveDistrictType_falls_back_to_outskirts_for_unattributed_cells(int col, int row)
    {
        Assert.Equal(DistrictType.Outskirts, AreaGridLogic.ResolveDistrictType(new GridCoord(col, row)));
    }

    [Fact]
    public void ResolveDistrictType_is_total_returns_outskirts_for_out_of_bounds()
    {
        // Defensive contract : the helper does not throw at the grid
        // edge so the hover-tooltip path can be lazy about bounds checks.
        Assert.Equal(DistrictType.Outskirts, AreaGridLogic.ResolveDistrictType(new GridCoord(-1, 0)));
        Assert.Equal(DistrictType.Outskirts, AreaGridLogic.ResolveDistrictType(new GridCoord(8, 8)));
    }

    [Fact]
    public void All_six_district_types_appear_in_the_grid()
    {
        // E2.1 acceptance criterion : the 8×8 grid exercises every
        // value of the closed lookup so hover on any cell can return
        // every possible tooltip district label. At E2.1c profond refonte
        // each of the 6 districts owns ≥ 4 explicit cells.
        var districts = AreaGridLogic.AllCells()
            .Select(AreaGridLogic.ResolveDistrictType)
            .ToHashSet();
        Assert.Equal(6, districts.Count);
    }

    /// <summary>
    /// E2.1c profond refonte (2026-05-16, Didier lock). The
    /// <see cref="AreaGridLogic.IsInDistrict"/> helper is the explicit
    /// seam the E2AreaMap tile spawn loop uses to discriminate the 39
    /// district cells (which render per-district iso bitmaps) from the 25
    /// unattributed cells (which render the new dedicated neutral
    /// bitmap). Pinning the helper here surfaces a silent drift in the
    /// <c>DistrictCellsTable</c> as a red xUnit step, BEFORE the visual
    /// smoke test catches it via a misrendered cell.
    /// </summary>
    [Theory]
    // One cell per district (covers all 6 districts).
    [InlineData(3, 3)]  // Intramuros core
    [InlineData(5, 3)]  // Wall east flank
    [InlineData(3, 0)]  // Gateway
    [InlineData(1, 5)]  // Outskirts NW corner
    [InlineData(5, 1)]  // HinterlandAgri NW corner
    [InlineData(0, 3)]  // Littoral west strip
    // Tip cells of each district (extremities).
    [InlineData(4, 5)]  // Intramuros south bulge
    [InlineData(3, 6)]  // Wall south flank
    [InlineData(4, 0)]  // Gateway NE corner
    [InlineData(2, 7)]  // Outskirts SE corner
    [InlineData(6, 2)]  // HinterlandAgri SE corner
    [InlineData(1, 4)]  // Littoral midriff bulge
    public void IsInDistrict_returns_true_for_every_district_cell(int col, int row)
    {
        Assert.True(AreaGridLogic.IsInDistrict(new GridCoord(col, row)));
    }

    [Theory]
    // Sample "space between quartiers" cells (the 25 unattributed). The
    // critical case is (5, 0) : it sits outside every district list but
    // ResolveDistrictType returns Outskirts as its defensive fallback,
    // which would collide with the legit Outskirts cells if a caller
    // relied on the enum alone. IsInDistrict must return false here.
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(0, 7)]
    [InlineData(7, 7)]
    [InlineData(7, 4)]
    [InlineData(5, 0)]
    [InlineData(1, 1)]
    [InlineData(0, 6)]
    public void IsInDistrict_returns_false_for_unattributed_cells(int col, int row)
    {
        Assert.False(AreaGridLogic.IsInDistrict(new GridCoord(col, row)));
    }

    [Fact]
    public void IsInDistrict_returns_false_for_out_of_bounds()
    {
        // Defensive contract : like ResolveDistrictType, the helper is
        // total at the grid edge. Out-of-bounds returns false (= "do not
        // route to a district bitmap"), matching the spawn-loop semantics
        // (out-of-bounds cells don't get spawned in the first place, but
        // the helper has to stay total so the seam survives lazy callers).
        Assert.False(AreaGridLogic.IsInDistrict(new GridCoord(-1, 0)));
        Assert.False(AreaGridLogic.IsInDistrict(new GridCoord(0, -1)));
        Assert.False(AreaGridLogic.IsInDistrict(new GridCoord(8, 0)));
        Assert.False(AreaGridLogic.IsInDistrict(new GridCoord(0, 8)));
        Assert.False(AreaGridLogic.IsInDistrict(new GridCoord(8, 8)));
    }

    [Fact]
    public void IsInDistrict_covers_exactly_thirty_nine_cells()
    {
        // E2.1c profond refonte authoring : Intramuros 11 + Wall 8 +
        // Gateway 4 + Outskirts 6 + HinterlandAgri 4 + Littoral 6 = 39.
        // The 25 unattributed cells = 64 - 39. If a district's cell list
        // changes the total, this test catches it before the visual ratio
        // Didier locked drifts at runtime.
        var count = AreaGridLogic.AllCells()
            .Count(AreaGridLogic.IsInDistrict);
        Assert.Equal(39, count);
    }

    [Fact]
    public void Unattributed_neutral_cells_total_twenty_five()
    {
        // Direct counterpart of IsInDistrict_covers_exactly_thirty_nine_cells.
        // The render layer routes these 25 cells to the new neutral iso
        // bitmap (wf_e2_grid_neutral_256x132.png), distinct from BOTH the
        // 6 per-district iso bitmaps AND the E1 fog bitmap.
        var count = AreaGridLogic.AllCells()
            .Count(c => !AreaGridLogic.IsInDistrict(c));
        Assert.Equal(25, count);
    }

    [Fact]
    public void Cells_of_zone_returns_the_four_cells_of_the_zone()
    {
        // Zone (0,0) covers (0,0)(1,0)(0,1)(1,1).
        var zone00 = AreaGridLogic.CellsOfZone(0, 0).ToList();
        Assert.Equal(4, zone00.Count);
        Assert.Contains(new GridCoord(0, 0), zone00);
        Assert.Contains(new GridCoord(1, 0), zone00);
        Assert.Contains(new GridCoord(0, 1), zone00);
        Assert.Contains(new GridCoord(1, 1), zone00);

        // Zone (3,3) covers (6,6)(7,6)(6,7)(7,7).
        var zone33 = AreaGridLogic.CellsOfZone(3, 3).ToList();
        Assert.Equal(4, zone33.Count);
        Assert.Contains(new GridCoord(6, 6), zone33);
        Assert.Contains(new GridCoord(7, 7), zone33);

        // Zone (1,1) covers (2,2)(3,2)(2,3)(3,3) -- all Intramuros at
        // E2.1c profond refonte, exercising the centroid-zone-eligibility
        // invariant from the other test class.
        var zone11 = AreaGridLogic.CellsOfZone(1, 1).ToList();
        Assert.Equal(4, zone11.Count);
        Assert.Contains(new GridCoord(2, 2), zone11);
        Assert.Contains(new GridCoord(3, 3), zone11);
    }

    [Fact]
    public void Cells_of_zone_returns_empty_for_out_of_range_zone()
    {
        // Defensive contract : zCol / zRow outside [0..3] yields an
        // empty enumeration so the caller can be lazy about bounds.
        Assert.Empty(AreaGridLogic.CellsOfZone(-1, 0));
        Assert.Empty(AreaGridLogic.CellsOfZone(0, -1));
        Assert.Empty(AreaGridLogic.CellsOfZone(4, 0));
        Assert.Empty(AreaGridLogic.CellsOfZone(0, 4));
    }
}
