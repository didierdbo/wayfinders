using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helpers for laying out the fog-tile grid over a finite 2D world
/// image (slice 1 / livrable 2 — M3 / L1 World fondations). Same
/// Godot-free seam pattern as <see cref="CameraPanLogic"/> and
/// <see cref="MapPanInputLogic"/>: the runtime translates engine types
/// (<c>Vector2I</c>, <c>Vector2</c>) into <see cref="GridCoord"/> /
/// <see cref="PanVec2"/> at the boundary, the helpers compute integer
/// arithmetic in here, xUnit pins the contract without an engine.
///
/// <para>
/// <b>Slice 1 placeholder cell size.</b> The cell size is a parameter,
/// not a constant — Didier itère encore sur l'image MJ et la grille
/// finale dépend du master final. The runtime currently calls
/// <see cref="ComputeGridSize"/> with 128 px (E2.1 master 3840×2160
/// produces 30×17 cells = 510 fog tiles), which sits in the same
/// granularity ballpark as Varn §5.1 L1 World guidance (40-80 tuiles
/// MVP, but for the slice 1 placeholder we don't need to hit final
/// density — the visual scaffold validates the architecture, the
/// design slicing arrives in slice 2 once Mira's master ships).
/// </para>
///
/// <para>
/// <b>Why a separate grid logic file (rather than baking the math into
/// the component).</b> The exact cell-to-world-position mapping is the
/// load-bearing seam between four different concerns: the fog renderer
/// (livrable 2), the knowledge store (livrable 3), the baking script
/// (livrable 4 — needs to compute the same cell rectangles at editor
/// time to quantify pixels), and the future drill-zoom predicate
/// (Varn §1.4, slice 2+). Putting the math here means all four
/// consumers agree on cell boundaries by construction.
/// </para>
/// </summary>
public static class FogTileGridLogic
{
    /// <summary>
    /// Compute the grid dimensions (number of cells along each axis)
    /// for an image of given size and a given cell size. Uses ceiling
    /// division so the right and bottom edges are covered even if
    /// <paramref name="imageSize"/> is not an exact multiple of
    /// <paramref name="cellSizePx"/> — the last column / row of cells
    /// extends past the image edge on its outer side. The fog renderer
    /// can either clip them at draw time or accept the slight overhang
    /// (visually invisible because the carton sits above the world
    /// image and any overhang lands in the dead zone outside the
    /// camera limits).
    /// </summary>
    /// <param name="imageSize">World-image dimensions in pixels.</param>
    /// <param name="cellSizePx">Side length of a single cell, pixels.</param>
    /// <returns>(columns, rows) count, both &gt;= 1 for any positive image.</returns>
    public static GridDimensions ComputeGridSize(PanVec2 imageSize, int cellSizePx)
    {
        if (cellSizePx <= 0)
        {
            // Defensive: a zero or negative cell size makes no sense.
            // Returning a 1×1 grid is the least-surprise fallback that
            // keeps the runtime allocating one fog tile, never zero
            // (which would silently disable the slice 1 scaffold and
            // be hard to debug).
            return new GridDimensions(1, 1);
        }

        var cols = (int)System.MathF.Ceiling(imageSize.X / cellSizePx);
        var rows = (int)System.MathF.Ceiling(imageSize.Y / cellSizePx);
        if (cols < 1) cols = 1;
        if (rows < 1) rows = 1;
        return new GridDimensions(cols, rows);
    }

    /// <summary>
    /// Compute the world-space top-left position of a given cell.
    /// The cell origin is at <c>(col * cellSizePx, row * cellSizePx)</c>
    /// in the same coordinate frame as the world map sprite (top-left
    /// at (0, 0), Y down, see <see cref="CameraPanLogic"/> coordinate
    /// convention).
    ///
    /// <para>
    /// Used by the fog renderer to position each Sprite2D-per-cell at
    /// spawn, and by the baking script to compute which pixel
    /// rectangle of the master image contributes to each cell's
    /// quantified palette.
    /// </para>
    /// </summary>
    public static PanVec2 ComputeCellTopLeft(GridCoord coord, int cellSizePx)
    {
        return new PanVec2(coord.Col * cellSizePx, coord.Row * cellSizePx);
    }

    /// <summary>
    /// Compute the world-space center position of a given cell. The
    /// runtime fog renderer uses a Sprite2D with <c>centered=true</c>
    /// so this is the value to assign to <c>Sprite2D.Position</c>.
    /// Centered sprites are easier to author by hand and easier to
    /// flip / scale in place than top-left-anchored ones.
    /// </summary>
    public static PanVec2 ComputeCellCenter(GridCoord coord, int cellSizePx)
    {
        var halfCell = cellSizePx / 2f;
        return new PanVec2(
            coord.Col * cellSizePx + halfCell,
            coord.Row * cellSizePx + halfCell);
    }

    /// <summary>
    /// Hit-test : map a world-space position back to its containing
    /// grid cell. Used by the slice 1 debug command (livrable 3) to
    /// resolve the tile under the cursor. Returns null if the position
    /// falls outside the grid bounds — caller decides whether to clamp
    /// or ignore.
    /// </summary>
    public static GridCoord? WorldPositionToCell(
        PanVec2 worldPosition,
        int cellSizePx,
        GridDimensions dimensions)
    {
        if (cellSizePx <= 0) return null;
        if (worldPosition.X < 0 || worldPosition.Y < 0) return null;

        var col = (int)(worldPosition.X / cellSizePx);
        var row = (int)(worldPosition.Y / cellSizePx);

        if (col < 0 || col >= dimensions.Columns) return null;
        if (row < 0 || row >= dimensions.Rows) return null;

        return new GridCoord(col, row);
    }

    /// <summary>
    /// Enumerate every cell of the grid in row-major order. Used by the
    /// fog renderer at spawn (one Sprite2D per yielded coord) and by
    /// the baking script (one palette quantification per cell). The
    /// row-major order keeps spatial locality in the persistence
    /// dictionary, which matters when slice 2 starts iterating
    /// neighbours for the Varn §3.3 chevauchement de carton ~5%.
    /// </summary>
    public static IEnumerable<GridCoord> EnumerateCells(GridDimensions dimensions)
    {
        for (int row = 0; row < dimensions.Rows; row++)
        {
            for (int col = 0; col < dimensions.Columns; col++)
            {
                yield return new GridCoord(col, row);
            }
        }
    }
}

/// <summary>
/// Integer (column, row) coordinate of a fog-tile cell in the grid
/// laid out by <see cref="FogTileGridLogic"/>. Godot-free stand-in for
/// <c>Godot.Vector2I</c> at the pure-logic seam ; the runtime converts
/// at the boundary. The persistence schema (livrable 3) uses this type
/// as the dictionary key so the on-disk format stays Godot-free too.
/// </summary>
/// <param name="Col">Zero-indexed column, growing rightward.</param>
/// <param name="Row">Zero-indexed row, growing downward.</param>
public readonly record struct GridCoord(int Col, int Row);

/// <summary>
/// Grid dimensions returned by <see cref="FogTileGridLogic.ComputeGridSize"/>.
/// Positive on both axes by construction (the helper guards against
/// degenerate cases). Equality semantics make the type easy to assert
/// against in xUnit fixtures.
/// </summary>
/// <param name="Columns">Number of columns, &gt;= 1.</param>
/// <param name="Rows">Number of rows, &gt;= 1.</param>
public readonly record struct GridDimensions(int Columns, int Rows)
{
    /// <summary>Total cell count (columns × rows). Used by the fog renderer to size pools.</summary>
    public int TotalCells => Columns * Rows;
}
