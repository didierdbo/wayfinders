using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="FogTileGridLogic"/> (slice 1 / livrable
/// 2 — M3 / L1 World fondations / 2026-05-08). Pins the cell-to-world
/// mapping that three downstream consumers share : the runtime fog
/// renderer, the editor-time palette baking script, and the slice 1
/// debug toggle. If the math drifts in any of those sites, the
/// renderer and the bake disagree on cell boundaries and the
/// quantified palette ends up off-grid.
///
/// <para>
/// <b>Coverage strategy.</b>
/// <list type="bullet">
///   <item>E2.1 master dimensions (3840×2160 at 128 px) — the canonical
///         slice 1 case. Locked numbers so the runtime, the bake, and
///         the tests all agree.</item>
///   <item>Non-multiple image dimensions (ceiling division produces an
///         overhang cell on right + bottom).</item>
///   <item>Hit-test world-position-to-cell against grid bounds (
///         negative positions, beyond-the-grid positions, exact
///         boundary positions).</item>
///   <item>Defense : zero / negative cell size returns a 1×1 grid
///         instead of dividing by zero.</item>
///   <item>Cell enumeration produces row-major order with the right
///         cardinality.</item>
/// </list>
/// </para>
/// </summary>
public sealed class FogTileGridLogicTests
{
    private static readonly PanVec2 E2ImageSize = new(3840f, 2160f);
    private const int Slice1CellSize = 128;

    [Fact]
    public void E2_master_at_128px_produces_30_by_17_grid()
    {
        // Slice 1 canonical lock. If this number changes, the bake
        // and the renderer must be re-aligned in the same diff.
        var dimensions = FogTileGridLogic.ComputeGridSize(E2ImageSize, Slice1CellSize);
        Assert.Equal(30, dimensions.Columns);
        Assert.Equal(17, dimensions.Rows);  // 2160 / 128 = 16.875 -> 17 (ceiling)
        Assert.Equal(510, dimensions.TotalCells);
    }

    [Fact]
    public void Non_multiple_image_size_uses_ceiling_division()
    {
        // 100×100 image at 30 px cells → ceil(100/30) = 4 cells per axis.
        var dimensions = FogTileGridLogic.ComputeGridSize(new PanVec2(100f, 100f), 30);
        Assert.Equal(4, dimensions.Columns);
        Assert.Equal(4, dimensions.Rows);
    }

    [Fact]
    public void Multiple_image_size_uses_exact_division()
    {
        var dimensions = FogTileGridLogic.ComputeGridSize(new PanVec2(640f, 480f), 32);
        Assert.Equal(20, dimensions.Columns);  // 640 / 32 = 20
        Assert.Equal(15, dimensions.Rows);     // 480 / 32 = 15
    }

    [Fact]
    public void Zero_cell_size_falls_back_to_one_by_one()
    {
        // Defensive : a zero or negative cell size never divides by
        // zero. The fallback 1×1 means the runtime spawns a single
        // fog tile (visible, debuggable) instead of silently
        // disabling the layer.
        var dimensions = FogTileGridLogic.ComputeGridSize(E2ImageSize, 0);
        Assert.Equal(1, dimensions.Columns);
        Assert.Equal(1, dimensions.Rows);
    }

    [Fact]
    public void Negative_cell_size_falls_back_to_one_by_one()
    {
        var dimensions = FogTileGridLogic.ComputeGridSize(E2ImageSize, -42);
        Assert.Equal(1, dimensions.Columns);
        Assert.Equal(1, dimensions.Rows);
    }

    [Fact]
    public void Cell_top_left_is_col_times_size_row_times_size()
    {
        var topLeft = FogTileGridLogic.ComputeCellTopLeft(new GridCoord(3, 5), 100);
        Assert.Equal(300f, topLeft.X);
        Assert.Equal(500f, topLeft.Y);
    }

    [Fact]
    public void Cell_center_is_offset_by_half_cell()
    {
        var center = FogTileGridLogic.ComputeCellCenter(new GridCoord(3, 5), 100);
        Assert.Equal(350f, center.X);
        Assert.Equal(550f, center.Y);
    }

    [Fact]
    public void World_position_inside_grid_resolves_to_owning_cell()
    {
        // Position (300, 500) sits at the top-left corner of cell (3, 5)
        // when CellSize=100 — the cell that "owns" it (top-left
        // inclusive convention).
        var dimensions = new GridDimensions(10, 10);
        var coord = FogTileGridLogic.WorldPositionToCell(new PanVec2(350f, 550f), 100, dimensions);
        Assert.NotNull(coord);
        Assert.Equal(3, coord!.Value.Col);
        Assert.Equal(5, coord.Value.Row);
    }

    [Fact]
    public void World_position_at_exact_cell_boundary_belongs_to_lower_cell()
    {
        // Pinning convention : a position exactly on the gridline is
        // the top-left of the next cell, not the bottom-right of the
        // previous one. Convention matters because the bake and the
        // renderer must agree.
        var dimensions = new GridDimensions(10, 10);
        var coord = FogTileGridLogic.WorldPositionToCell(new PanVec2(300f, 500f), 100, dimensions);
        Assert.NotNull(coord);
        Assert.Equal(3, coord!.Value.Col);
        Assert.Equal(5, coord.Value.Row);
    }

    [Fact]
    public void World_position_negative_returns_null()
    {
        var dimensions = new GridDimensions(10, 10);
        Assert.Null(FogTileGridLogic.WorldPositionToCell(new PanVec2(-1f, 50f), 100, dimensions));
        Assert.Null(FogTileGridLogic.WorldPositionToCell(new PanVec2(50f, -1f), 100, dimensions));
    }

    [Fact]
    public void World_position_beyond_grid_returns_null()
    {
        var dimensions = new GridDimensions(10, 10);
        // Grid is 10×10 cells × 100 px = 1000×1000 world units.
        Assert.Null(FogTileGridLogic.WorldPositionToCell(new PanVec2(1000.1f, 500f), 100, dimensions));
        Assert.Null(FogTileGridLogic.WorldPositionToCell(new PanVec2(500f, 1000.1f), 100, dimensions));
    }

    [Fact]
    public void Enumerate_cells_yields_total_count_in_row_major_order()
    {
        var dimensions = new GridDimensions(3, 2);
        var cells = FogTileGridLogic.EnumerateCells(dimensions).ToList();
        Assert.Equal(6, cells.Count);
        // Row-major : (0,0), (1,0), (2,0), (0,1), (1,1), (2,1)
        Assert.Equal(new GridCoord(0, 0), cells[0]);
        Assert.Equal(new GridCoord(1, 0), cells[1]);
        Assert.Equal(new GridCoord(2, 0), cells[2]);
        Assert.Equal(new GridCoord(0, 1), cells[3]);
        Assert.Equal(new GridCoord(1, 1), cells[4]);
        Assert.Equal(new GridCoord(2, 1), cells[5]);
    }

    [Fact]
    public void Enumerate_cells_at_e2_slice_1_yields_510_entries()
    {
        var dimensions = FogTileGridLogic.ComputeGridSize(E2ImageSize, Slice1CellSize);
        var cells = FogTileGridLogic.EnumerateCells(dimensions).ToList();
        Assert.Equal(510, cells.Count);
        // First and last cell coords pinned for sanity.
        Assert.Equal(new GridCoord(0, 0), cells.First());
        Assert.Equal(new GridCoord(29, 16), cells.Last());
    }
}
