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

    // ====================================================================
    // Slice 3.5 (M3 / Phase 9 / 2026-05-08) -- iso DiamondDown projection.
    // The slice 3.5 brief locks the iso shape: cells are 2:1 diamonds
    // (cellWidth x cellWidth/2), pivot at cell center, hit-test via the
    // canonical |dx|/halfW + |dy|/halfH <= 1 predicate, world frame
    // origin shifted so the leftmost diamond's left vertex sits at X=0.
    // These tests pin the math without engine, mirroring the slice 1
    // rect coverage strategy.
    // ====================================================================

    [Fact]
    public void Iso_compute_grid_size_returns_diamond_tessellation_count()
    {
        // E2.1 master 3840x2160 at 128 px iso : halfW=64, halfH=32.
        // ceil(3840/64) + 1 = 60 + 1 = 61 cols ; ceil(2160/32) + 1 =
        // 68 + 1 = 69 rows. The +1 absorbs the iso staircase corners.
        var dimensions = FogTileGridLogic.ComputeGridSize(
            E2ImageSize, Slice1CellSize, GridProjection.IsoDiamondDown);
        Assert.Equal(61, dimensions.Columns);
        Assert.Equal(69, dimensions.Rows);
    }

    [Fact]
    public void Iso_compute_grid_size_zero_cell_size_falls_back_to_one_by_one()
    {
        var dimensions = FogTileGridLogic.ComputeGridSize(
            E2ImageSize, 0, GridProjection.IsoDiamondDown);
        Assert.Equal(1, dimensions.Columns);
        Assert.Equal(1, dimensions.Rows);
    }

    [Fact]
    public void Iso_cell_center_at_origin_is_self_similar_anchor()
    {
        // Pre-shift: cell (0,0)'s center sits at (0, halfH). The +halfH
        // on Y comes from the projection so the (0,0) diamond's TOP
        // vertex is exactly at world (0, 0) before the row-shift.
        var center = FogTileGridLogic.ComputeCellCenter(
            new GridCoord(0, 0), 128, GridProjection.IsoDiamondDown);
        Assert.Equal(0f, center.X, precision: 4);
        Assert.Equal(32f, center.Y, precision: 4); // halfH = 128/4 = 32
    }

    [Fact]
    public void Iso_cell_center_grows_southeast_for_col_increment()
    {
        // Column +1 from (0,0) -> center moves +halfW on X, +halfH on Y.
        // (Standard DiamondDown projection: +col goes south-east.)
        var center = FogTileGridLogic.ComputeCellCenter(
            new GridCoord(1, 0), 128, GridProjection.IsoDiamondDown);
        Assert.Equal(64f, center.X, precision: 4);
        Assert.Equal(32f + 32f, center.Y, precision: 4);
    }

    [Fact]
    public void Iso_cell_center_grows_southwest_for_row_increment()
    {
        // Row +1 from (0,0) -> center moves -halfW on X, +halfH on Y.
        // +row goes south-west.
        var center = FogTileGridLogic.ComputeCellCenter(
            new GridCoord(0, 1), 128, GridProjection.IsoDiamondDown);
        Assert.Equal(-64f, center.X, precision: 4);
        Assert.Equal(64f, center.Y, precision: 4);
    }

    [Fact]
    public void Iso_origin_shift_pushes_leftmost_diamond_to_x_zero()
    {
        // Without the shift, cell (0, rows-1) lives at negative X (the
        // (col - row) component is negative). The shift adds rows*halfW
        // so the leftmost vertex of (0, rows-1) lands at X=0.
        var dimensions = new GridDimensions(10, 10);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dimensions, 128, GridProjection.IsoDiamondDown);
        Assert.Equal(10 * 64f, shift.X, precision: 4);
        Assert.Equal(0f, shift.Y, precision: 4);
    }

    [Fact]
    public void Iso_origin_shift_zero_in_rect_mode()
    {
        // Rect mode keeps origin at (0,0) -- no shift is needed.
        var dimensions = new GridDimensions(10, 10);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dimensions, 128, GridProjection.Rect);
        Assert.Equal(0f, shift.X, precision: 4);
        Assert.Equal(0f, shift.Y, precision: 4);
    }

    [Fact]
    public void Iso_world_position_at_cell_center_resolves_back_to_cell()
    {
        // Round-trip property : take a cell, compute its world center
        // (with the origin shift applied), feed back to WorldPositionToCell,
        // expect the same cell back. Repeat for several cells across
        // the grid.
        var dimensions = new GridDimensions(20, 20);
        var coords = new[]
        {
            new GridCoord(0, 0),
            new GridCoord(5, 3),
            new GridCoord(10, 10),
            new GridCoord(19, 19),
            new GridCoord(0, 19),
            new GridCoord(19, 0),
        };
        foreach (var coord in coords)
        {
            var center = FogTileGridLogic.ComputeCellCenter(coord, 128, GridProjection.IsoDiamondDown);
            var shift = FogTileGridLogic.ComputeIsoOriginShift(dimensions, 128, GridProjection.IsoDiamondDown);
            var worldPos = new PanVec2(center.X + shift.X, center.Y + shift.Y);
            var resolved = FogTileGridLogic.WorldPositionToCell(
                worldPos, 128, dimensions, GridProjection.IsoDiamondDown);
            Assert.NotNull(resolved);
            Assert.Equal(coord, resolved!.Value);
        }
    }

    [Fact]
    public void Iso_world_position_outside_grid_returns_null()
    {
        var dimensions = new GridDimensions(10, 10);
        // Way outside any reasonable diamond bounding box.
        var outside = FogTileGridLogic.WorldPositionToCell(
            new PanVec2(-10000f, -10000f), 128, dimensions, GridProjection.IsoDiamondDown);
        Assert.Null(outside);
    }

    [Fact]
    public void Iso_world_position_in_far_corner_outside_grid_returns_null()
    {
        var dimensions = new GridDimensions(10, 10);
        // Far past the grid : cell (15,15) is outside a 10x10 grid.
        var beyondCenter = FogTileGridLogic.ComputeCellCenter(
            new GridCoord(15, 15), 128, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dimensions, 128, GridProjection.IsoDiamondDown);
        var worldPos = new PanVec2(beyondCenter.X + shift.X, beyondCenter.Y + shift.Y);
        var resolved = FogTileGridLogic.WorldPositionToCell(
            worldPos, 128, dimensions, GridProjection.IsoDiamondDown);
        Assert.Null(resolved);
    }

    [Fact]
    public void Iso_hit_test_at_top_vertex_resolves_deterministically_to_neighbour_center()
    {
        // Iso half-step subtlety : in DiamondDown, the top vertex of
        // cell (col, row) at world (cx, cy-halfH) is exactly the CENTER
        // of cell (col-1, row-1) (because that neighbour's center is at
        // ((col-1)-(row-1))*halfW + originX = (col-row)*halfW = cx, and
        // ((col-1)+(row-1))*halfH + halfH = (col+row-2)*halfH + halfH =
        // (col+row)*halfH - halfH = cy - halfH). The closest-center
        // disambiguation therefore picks (col-1, row-1) for the top
        // vertex, which is correct iso behaviour.
        var dimensions = new GridDimensions(20, 20);
        var coord = new GridCoord(5, 5);
        var center = FogTileGridLogic.ComputeCellCenter(coord, 128, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(dimensions, 128, GridProjection.IsoDiamondDown);
        var worldPos = new PanVec2(center.X + shift.X, center.Y + shift.Y - 32f);

        var resolved = FogTileGridLogic.WorldPositionToCell(
            worldPos, 128, dimensions, GridProjection.IsoDiamondDown);
        Assert.NotNull(resolved);
        Assert.Equal(new GridCoord(coord.Col - 1, coord.Row - 1), resolved!.Value);
    }

    [Fact]
    public void Iso_hit_test_slightly_inside_diamond_picks_owning_cell()
    {
        // 1 px below the top vertex -> well inside the diamond of (5,5).
        // No ambiguity, must resolve to (5,5).
        var dimensions = new GridDimensions(20, 20);
        var coord = new GridCoord(5, 5);
        var center = FogTileGridLogic.ComputeCellCenter(coord, 128, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(dimensions, 128, GridProjection.IsoDiamondDown);
        var worldPos = new PanVec2(center.X + shift.X, center.Y + shift.Y - 31f);

        var resolved = FogTileGridLogic.WorldPositionToCell(
            worldPos, 128, dimensions, GridProjection.IsoDiamondDown);
        Assert.NotNull(resolved);
        Assert.Equal(coord, resolved!.Value);
    }

    [Fact]
    public void Iso_hit_test_between_four_diamonds_picks_closest_center()
    {
        // Perturb 0.1 px toward (5,5)'s center so that diamond is
        // unambiguously closest. The hit-test must return (5,5).
        var dimensions = new GridDimensions(20, 20);
        var coordA = new GridCoord(5, 5);
        var centerA = FogTileGridLogic.ComputeCellCenter(coordA, 128, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(dimensions, 128, GridProjection.IsoDiamondDown);

        var worldPos = new PanVec2(centerA.X + shift.X - 0.1f, centerA.Y + shift.Y - 0.1f);
        var resolved = FogTileGridLogic.WorldPositionToCell(
            worldPos, 128, dimensions, GridProjection.IsoDiamondDown);
        Assert.NotNull(resolved);
        Assert.Equal(coordA, resolved!.Value);
    }

    [Fact]
    public void Iso_diamond_vertices_match_2_to_1_aspect_ratio()
    {
        var (top, right, bottom, left) =
            FogTileGridLogic.IsoDiamondVerticesAroundCenter(128);
        // 2:1 ratio : diamond width = 128 px, height = 64 px, so
        // halfW = 64, halfH = 32.
        Assert.Equal(0f, top.X, precision: 4);
        Assert.Equal(-32f, top.Y, precision: 4);
        Assert.Equal(64f, right.X, precision: 4);
        Assert.Equal(0f, right.Y, precision: 4);
        Assert.Equal(0f, bottom.X, precision: 4);
        Assert.Equal(32f, bottom.Y, precision: 4);
        Assert.Equal(-64f, left.X, precision: 4);
        Assert.Equal(0f, left.Y, precision: 4);
    }

    [Fact]
    public void Iso_cell_contains_world_point_inside_diamond_returns_true()
    {
        var dimensions = new GridDimensions(20, 20);
        var coord = new GridCoord(5, 5);
        var center = FogTileGridLogic.ComputeCellCenter(coord, 128, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(dimensions, 128, GridProjection.IsoDiamondDown);
        var pointAtCenter = new PanVec2(center.X + shift.X, center.Y + shift.Y);

        Assert.True(FogTileGridLogic.IsoCellContainsWorldPoint(
            coord, pointAtCenter, 128, dimensions));
    }

    [Fact]
    public void Iso_cell_contains_world_point_outside_diamond_returns_false()
    {
        // Pick a point in the bbox corner of cell (5,5) -- inside
        // the bounding rectangle but outside the diamond.
        var dimensions = new GridDimensions(20, 20);
        var coord = new GridCoord(5, 5);
        var center = FogTileGridLogic.ComputeCellCenter(coord, 128, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(dimensions, 128, GridProjection.IsoDiamondDown);
        // bbox top-left corner is at (cx - halfW, cy - halfH) -- predicate
        // |dx|/halfW + |dy|/halfH = 1+1 = 2 > 1, outside.
        var pointAtCorner = new PanVec2(
            center.X + shift.X - 64f + 0.5f,
            center.Y + shift.Y - 32f + 0.5f);

        Assert.False(FogTileGridLogic.IsoCellContainsWorldPoint(
            coord, pointAtCorner, 128, dimensions));
    }

    [Fact]
    public void Enumerate_cells_yields_total_count_independent_of_projection()
    {
        // Cell enumeration does not depend on projection -- it walks
        // the (col, row) tuple space row-major. The grid math callers
        // that use the projection argument live elsewhere ; enumerate
        // is projection-agnostic by design.
        var dimensions = new GridDimensions(7, 5);
        var cells = System.Linq.Enumerable.ToList(FogTileGridLogic.EnumerateCells(dimensions));
        Assert.Equal(35, cells.Count);
        Assert.Equal(new GridCoord(0, 0), cells[0]);
        Assert.Equal(new GridCoord(6, 4), cells[34]);
    }

    [Fact]
    public void Iso_compute_cell_top_left_returns_bbox_top_left_of_diamond()
    {
        var coord = new GridCoord(3, 2);
        var topLeft = FogTileGridLogic.ComputeCellTopLeft(
            coord, 128, GridProjection.IsoDiamondDown);
        var center = FogTileGridLogic.ComputeCellCenter(coord, 128, GridProjection.IsoDiamondDown);
        Assert.Equal(center.X - 64f, topLeft.X, precision: 4);
        Assert.Equal(center.Y - 32f, topLeft.Y, precision: 4);
    }

    [Fact]
    public void Iso_default_argument_keeps_rect_mode_for_back_compat()
    {
        // Slice 1 + 2 + 3 callers omit the projection argument and
        // should continue to receive rect math (back-compat invariant).
        // E2 master 3840x2160 at 128 -> rect 30x17 (slice 1 lock).
        var dimensions = FogTileGridLogic.ComputeGridSize(E2ImageSize, Slice1CellSize);
        Assert.Equal(30, dimensions.Columns);
        Assert.Equal(17, dimensions.Rows);

        // Cell center default -> rect math.
        var center = FogTileGridLogic.ComputeCellCenter(new GridCoord(3, 5), 100);
        Assert.Equal(350f, center.X);
        Assert.Equal(550f, center.Y);
    }
}
