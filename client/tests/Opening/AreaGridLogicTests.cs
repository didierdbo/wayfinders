using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the 8×8 grid contract and the Halfgate coord-to-district mapping.
///
/// <para>
/// <b>E2.1c refonte (2026-05-16, Didier lock).</b> The grid is now modelled
/// as 6 disjoint 2×2 district blocks (24 cells total) ; the remaining 40
/// cells are unattributed and fall through the defensive
/// <see cref="AreaGridLogic.ResolveDistrictType"/> path which returns
/// <see cref="DistrictType.Outskirts"/>. The tests pin the 6 blocks and
/// the fallback contract -- the legacy cardinal-symmetric ring is gone.
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
        // Canonical anchor cell for the central POI footprint. At E2.1c
        // the Intramuros 2×2 block is anchored at (3,3) and covers
        // (3,3)(4,3)(3,4)(4,4) -- (4,4) is the SE corner of that block.
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
        // land on Intramuros cells (= the Intramuros 2×2 block at E2.1c).
        // Drift here silently changes the EC6 tooltip text.
        foreach (var cell in AreaGridLogic.CentralPoiFootprint())
        {
            Assert.Equal(DistrictType.Intramuros, AreaGridLogic.ResolveDistrictType(cell));
        }
    }

    [Theory]
    // The 4 cells of each of the 6 district 2×2 blocks (E2.1c refonte).
    // Intramuros block (3,3)(4,3)(3,4)(4,4) -- centre of grid.
    [InlineData(3, 3, DistrictType.Intramuros)]
    [InlineData(4, 3, DistrictType.Intramuros)]
    [InlineData(3, 4, DistrictType.Intramuros)]
    [InlineData(4, 4, DistrictType.Intramuros)]
    // Wall block (3,5)(4,5)(3,6)(4,6) -- south of Intramuros.
    [InlineData(3, 5, DistrictType.Wall)]
    [InlineData(4, 6, DistrictType.Wall)]
    // Gateway block (3,0)(4,0)(3,1)(4,1) -- north strip.
    [InlineData(3, 0, DistrictType.Gateway)]
    [InlineData(4, 1, DistrictType.Gateway)]
    // Outskirts block (1,5)(2,5)(1,6)(2,6) -- SW faubourg.
    [InlineData(1, 5, DistrictType.Outskirts)]
    [InlineData(2, 6, DistrictType.Outskirts)]
    // HinterlandAgri block (5,1)(6,1)(5,2)(6,2) -- NE fields.
    [InlineData(5, 1, DistrictType.HinterlandAgri)]
    [InlineData(6, 2, DistrictType.HinterlandAgri)]
    // Littoral block (0,3)(1,3)(0,4)(1,4) -- west coast.
    [InlineData(0, 3, DistrictType.Littoral)]
    [InlineData(1, 4, DistrictType.Littoral)]
    public void ResolveDistrictType_pins_each_block_cell(int col, int row, DistrictType expected)
    {
        Assert.Equal(expected, AreaGridLogic.ResolveDistrictType(new GridCoord(col, row)));
    }

    [Theory]
    // Unattributed cells (40 of the 64) fall through to the Outskirts
    // defensive default per the E2.1c refonte contract. A handful of
    // anchor coords to pin the "space between quartiers" semantics.
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(0, 7)]
    [InlineData(7, 7)]
    [InlineData(5, 5)]
    [InlineData(2, 2)]
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
        // every possible tooltip district label. At E2.1c each of the
        // 6 districts owns a 2×2 block, so all 6 values are present.
        var districts = AreaGridLogic.AllCells()
            .Select(AreaGridLogic.ResolveDistrictType)
            .ToHashSet();
        Assert.Equal(6, districts.Count);
    }
}
