using System.Linq;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Services.Iso;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="IsoCell"/> and <see cref="IsoGrid"/> — the
/// layer-2 logical grid of the J3-iso hybrid socle. These pin the
/// placement gate (<see cref="IsoGrid.CanPlaceOccupant"/> /
/// <see cref="IsoGrid.SetOccupant"/>) and the sparse-grid semantics that
/// motivate decision #3 (dictionary, not 2D array).
/// </summary>
public sealed class IsoGridTests
{
    private static TileCoordinate Cell(int col, int row) => new(col, row);

    // ---- IsoCell -----------------------------------------------------

    [Fact]
    public void EmptyWalkable_cell_is_free()
    {
        var cell = IsoCell.EmptyWalkable(Cell(2, 3));
        Assert.True(cell.Walkable);
        Assert.Null(cell.OccupantId);
        Assert.Equal(1, cell.MoveCost);
        Assert.True(cell.IsFree);
        Assert.Equal(Cell(2, 3), cell.Coord);
    }

    [Fact]
    public void Occupied_cell_is_not_free()
    {
        var cell = IsoCell.EmptyWalkable(Cell(0, 0)) with { OccupantId = "kira" };
        Assert.False(cell.IsFree);
    }

    [Fact]
    public void Non_walkable_cell_is_not_free_even_when_empty()
    {
        var cell = IsoCell.EmptyWalkable(Cell(0, 0)) with { Walkable = false };
        Assert.Null(cell.OccupantId);
        Assert.False(cell.IsFree);
    }

    // ---- IsoGrid: construction + sparse semantics --------------------

    [Fact]
    public void BuildRectangle_holds_width_times_height_cells()
    {
        var grid = IsoGrid.BuildRectangle(8, 6);
        Assert.Equal(48, grid.Count);
        Assert.True(grid.HasCell(Cell(0, 0)));
        Assert.True(grid.HasCell(Cell(7, 5)));
    }

    [Fact]
    public void HasCell_is_false_outside_the_built_footprint()
    {
        // The sparse-grid point: an off-footprint coord is simply absent,
        // not a sentinel cell. This is why a dictionary beats a 2D array
        // for an irregular district.
        var grid = IsoGrid.BuildRectangle(4, 4);
        Assert.False(grid.HasCell(Cell(4, 0)));
        Assert.False(grid.HasCell(Cell(-1, 2)));
    }

    [Fact]
    public void GetCell_throws_off_footprint()
        => Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => IsoGrid.BuildRectangle(3, 3).GetCell(Cell(9, 9)));

    [Fact]
    public void TryGetCell_returns_false_off_footprint_without_throwing()
    {
        var grid = IsoGrid.BuildRectangle(3, 3);
        Assert.False(grid.TryGetCell(Cell(9, 9), out var cell));
        Assert.Null(cell);
    }

    [Fact]
    public void Construction_rejects_duplicate_coords()
    {
        var coord = Cell(1, 1);
        var dup = new[]
        {
            IsoCell.EmptyWalkable(coord),
            IsoCell.EmptyWalkable(coord),
        };
        Assert.Throws<System.ArgumentException>(() => new IsoGrid(dup));
    }

    [Fact]
    public void BuildRectangle_rejects_non_positive_dimensions()
        => Assert.Throws<System.ArgumentOutOfRangeException>(
            () => IsoGrid.BuildRectangle(0, 5));

    // ---- IsoGrid: the placement gate ---------------------------------

    [Fact]
    public void CanPlaceOccupant_true_on_a_free_cell()
        => Assert.True(IsoGrid.BuildRectangle(4, 4).CanPlaceOccupant(Cell(2, 2)));

    [Fact]
    public void CanPlaceOccupant_false_off_footprint()
        => Assert.False(IsoGrid.BuildRectangle(4, 4).CanPlaceOccupant(Cell(10, 10)));

    [Fact]
    public void SetOccupant_marks_a_cell_occupied()
    {
        var grid = IsoGrid.BuildRectangle(4, 4);
        var coord = Cell(1, 2);

        var updated = grid.SetOccupant(coord, "kira");

        Assert.Equal("kira", updated.OccupantId);
        Assert.Equal("kira", grid.GetCell(coord).OccupantId);
        Assert.False(grid.GetCell(coord).IsFree);
        Assert.False(grid.CanPlaceOccupant(coord));
    }

    [Fact]
    public void SetOccupant_null_vacates_a_cell()
    {
        var grid = IsoGrid.BuildRectangle(4, 4);
        var coord = Cell(1, 2);
        grid.SetOccupant(coord, "kira");

        grid.SetOccupant(coord, null);

        Assert.Null(grid.GetCell(coord).OccupantId);
        Assert.True(grid.CanPlaceOccupant(coord));
    }

    [Fact]
    public void SetOccupant_throws_when_placing_onto_an_occupied_cell()
    {
        var grid = IsoGrid.BuildRectangle(4, 4);
        var coord = Cell(0, 0);
        grid.SetOccupant(coord, "kira");

        // The grid is the placement authority — it rejects, the call site
        // does not have to remember to check first.
        Assert.Throws<System.InvalidOperationException>(
            () => grid.SetOccupant(coord, "dorn"));
    }

    [Fact]
    public void SetOccupant_throws_off_footprint()
        => Assert.Throws<System.ArgumentOutOfRangeException>(
            () => IsoGrid.BuildRectangle(3, 3).SetOccupant(Cell(9, 9), "kira"));

    [Fact]
    public void AllCells_enumerates_the_whole_footprint()
    {
        var grid = IsoGrid.BuildRectangle(5, 4);
        Assert.Equal(20, grid.AllCells().Count());
        Assert.All(grid.AllCells(), c => Assert.True(c.Walkable));
    }
}
