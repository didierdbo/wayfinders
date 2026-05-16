using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the 8×8 grid contract and the Halfgate coord-to-district mapping.
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
        // Canonical anchor cell for the central POI footprint.
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
        // land on intramuros cells (the central wall ring). Drift here
        // silently changes the EC6 tooltip text.
        foreach (var cell in AreaGridLogic.CentralPoiFootprint())
        {
            Assert.Equal(DistrictType.Intramuros, AreaGridLogic.ResolveDistrictType(cell));
        }
    }

    [Theory]
    // The 4 corner cells are HinterlandAgri (fields outside the wall).
    [InlineData(0, 0, DistrictType.HinterlandAgri)]
    [InlineData(7, 0, DistrictType.HinterlandAgri)]
    [InlineData(0, 7, DistrictType.HinterlandAgri)]
    [InlineData(7, 7, DistrictType.HinterlandAgri)]
    // The 4 N/S/E/W cardinal axis cells at the outermost row/col are gates/littoral.
    [InlineData(3, 0, DistrictType.Gateway)]
    [InlineData(4, 7, DistrictType.Gateway)]
    [InlineData(0, 3, DistrictType.Littoral)]
    [InlineData(7, 4, DistrictType.Littoral)]
    // Inner ring (row/col 1 or 6) is mostly outskirts.
    [InlineData(1, 1, DistrictType.Outskirts)]
    [InlineData(6, 6, DistrictType.Outskirts)]
    // Wall ring : the (3,1) and (4,1) cells are wall (north wall).
    [InlineData(3, 1, DistrictType.Wall)]
    [InlineData(4, 6, DistrictType.Wall)]
    public void ResolveDistrictType_pins_known_anchor_cells(int col, int row, DistrictType expected)
    {
        Assert.Equal(expected, AreaGridLogic.ResolveDistrictType(new GridCoord(col, row)));
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
        // every possible tooltip district label.
        var districts = AreaGridLogic.AllCells()
            .Select(AreaGridLogic.ResolveDistrictType)
            .ToHashSet();
        Assert.Equal(6, districts.Count);
    }
}
