using System;
using System.Collections.Generic;
using System.Numerics;

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
/// <b>Slice 3.5 iso refactor (M3 / Phase 9 / 2026-05-08).</b> Adds a
/// <see cref="GridProjection"/> parameter to every entry point. The slice
/// 1+2+3 rect math is preserved as <see cref="GridProjection.Rect"/> ; the
/// new <see cref="GridProjection.IsoDiamondDown"/> mode lays cells out as
/// 2:1 diamonds (cellWidth × cellWidth/2) using the standard iso projection
/// classic to RTS / tactical games (Red Blob Games "Hexagonal Grids" §6
/// or Godot iso TileMap docs DiamondDown). The runtime L1 World defaults
/// to iso ; tests pin both modes to keep the math reusable for any future
/// rect grid (E3+ city interior overlays, E4 fiche cards).
/// </para>
///
/// <para>
/// <b>Iso projection convention (locked).</b>
/// <list type="bullet">
///   <item><b>Pivot</b> : center of the diamond. Symmetric with the existing
///         <c>Sprite2D.Centered=true</c> pattern in <see cref="FogTileLayer"/>
///         and simpler hit-test math (a single <c>|dx|/halfW + |dy|/halfH
///         &lt;= 1</c> predicate against the cell center).</item>
///   <item><b>Cell width</b> = <c>cellSizePx</c> (full diamond width along
///         X axis at iso center). <b>Cell height</b> = <c>cellSizePx / 2</c>
///         (2:1 ratio, classic Godot iso DiamondDown). Vertices of the
///         diamond at world-space offsets (±halfW, 0) for E/W and
///         (0, ±halfH) for N/S relative to the cell center.</item>
///   <item><b>Cell→world</b> : center.X = (col − row) × halfW + originX,
///         center.Y = (col + row) × halfH + originY. Origin chosen so the
///         (0,0) cell's top vertex sits at (worldOriginX, 0) — i.e. the
///         leftmost extent of the grid is at X=0, the topmost at Y=0,
///         which keeps the slice 1/2/3 "world image starts at top-left
///         (0,0)" invariant intact. Concretely : originX = (rows−1) ×
///         halfW (push everything right so the leftmost cell's left
///         vertex is at X=0), originY = halfH (push down by half the
///         first row's height so the topmost cell's top vertex is at Y=0).</item>
///   <item><b>World→cell</b> : invert the (col, row) ↔ (worldX, worldY)
///         linear system, then floor to integer. The tile under a world
///         point is the diamond whose iso-cell-coords are the floored
///         continuous values — same trick as Red Blob's "pixel-to-hex"
///         except for diamonds it's a plain 2×2 matrix invert, no
///         rounding edge cases.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a separate grid logic file (rather than baking the math into
/// the component).</b> The exact cell-to-world-position mapping is the
/// load-bearing seam between five different concerns: the fog renderer
/// (livrable 2 of slice 1, refactored at slice 3.5), the knowledge store
/// (livrable 3), the baking script (livrable 4 — needs to compute the
/// same cell rectangles at editor time to quantify pixels, refactored
/// at slice 3.5 livrable 5), the future drill-zoom predicate
/// (Varn §1.4), and the per-tile bitmap API (slice 3.5 livrable 4).
/// Putting the math here means all six consumers agree on cell
/// boundaries — and on iso vs rect mode — by construction.
/// </para>
/// </summary>
public static class FogTileGridLogic
{
    /// <summary>
    /// Compute the grid dimensions (number of cells along each axis)
    /// for an image of given size and a given cell size.
    ///
    /// <para>
    /// <b>Rect mode</b> uses ceiling division so the right and bottom edges
    /// are covered even if <paramref name="imageSize"/> is not an exact
    /// multiple of <paramref name="cellSizePx"/> — the last column / row
    /// of cells extends past the image edge on its outer side.
    /// </para>
    ///
    /// <para>
    /// <b>Iso mode</b> tessellates the image with 2:1 diamonds. The number
    /// of columns × rows is computed so the diamond bounding-box covers
    /// the whole image : <c>cols = ceil(imageW / halfW) + 1</c>,
    /// <c>rows = ceil(imageH / halfH) + 1</c>. The +1 absorbs the iso
    /// "staircase" effect at the four corners (a half-row at top and
    /// bottom is needed for full coverage). Cells outside the image
    /// rectangle are tessellated as well — cheap to spawn (transparent
    /// bitmap or hidden Carton at runtime), simple to reason about,
    /// avoids edge-case logic at the corners. The runtime can drop them
    /// post-hoc if memory becomes a concern, but at MVP scale (510-cell
    /// rect → ~600-cell iso) the cost is invisible.
    /// </para>
    /// </summary>
    public static GridDimensions ComputeGridSize(
        PanVec2 imageSize,
        int cellSizePx,
        GridProjection projection = GridProjection.Rect)
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

        if (projection == GridProjection.IsoDiamondDown)
        {
            var halfW = cellSizePx / 2f;
            var halfH = cellSizePx / 4f; // 2:1 ratio: full height is cellSizePx/2, half-height = cellSizePx/4
            // +1 in each direction to absorb the iso "staircase" corners.
            var cols = (int)System.MathF.Ceiling(imageSize.X / halfW) + 1;
            var rows = (int)System.MathF.Ceiling(imageSize.Y / halfH) + 1;
            if (cols < 1) cols = 1;
            if (rows < 1) rows = 1;
            return new GridDimensions(cols, rows);
        }

        var rectCols = (int)System.MathF.Ceiling(imageSize.X / cellSizePx);
        var rectRows = (int)System.MathF.Ceiling(imageSize.Y / cellSizePx);
        if (rectCols < 1) rectCols = 1;
        if (rectRows < 1) rectRows = 1;
        return new GridDimensions(rectCols, rectRows);
    }

    /// <summary>
    /// Compute the world-space top-left position of a given cell.
    ///
    /// <para>
    /// <b>Rect mode</b> : <c>(col × cellSizePx, row × cellSizePx)</c> in
    /// the same coordinate frame as the world map sprite (top-left at
    /// (0, 0), Y down).
    /// </para>
    ///
    /// <para>
    /// <b>Iso mode</b> : the bounding-box top-left of the diamond at
    /// <c>(col, row)</c>. Used by the per-tile bitmap API (slice 3.5
    /// livrable 4) to position a cell-bound texture so its centre
    /// aligns with the diamond centre.
    /// </para>
    /// </summary>
    public static PanVec2 ComputeCellTopLeft(
        GridCoord coord,
        int cellSizePx,
        GridProjection projection = GridProjection.Rect)
    {
        if (projection == GridProjection.IsoDiamondDown)
        {
            var center = ComputeCellCenter(coord, cellSizePx, GridProjection.IsoDiamondDown);
            var halfW = cellSizePx / 2f;
            var halfH = cellSizePx / 4f;
            return new PanVec2(center.X - halfW, center.Y - halfH);
        }
        return new PanVec2(coord.Col * cellSizePx, coord.Row * cellSizePx);
    }

    /// <summary>
    /// Compute the world-space center position of a given cell. The
    /// runtime fog renderer uses a Sprite2D / Polygon2D with
    /// <c>centered=true</c> semantics so this is the value to assign to
    /// <c>position</c>. Centered nodes are easier to author by hand and
    /// easier to flip / scale in place than top-left-anchored ones.
    ///
    /// <para>
    /// <b>Iso mode</b> uses the standard DiamondDown projection :
    /// <c>centerX = (col − row) × halfW + originX</c>,
    /// <c>centerY = (col + row) × halfH + originY</c>. The origin is
    /// chosen so the leftmost cell's leftmost vertex is at X=0, the
    /// topmost cell's topmost vertex at Y=0 — keeps the world-coordinate
    /// frame the same as rect mode (top-left at origin) so the
    /// MapPan2DComponent's camera limits don't need a special case.
    /// </para>
    /// </summary>
    public static PanVec2 ComputeCellCenter(
        GridCoord coord,
        int cellSizePx,
        GridProjection projection = GridProjection.Rect)
    {
        if (projection == GridProjection.IsoDiamondDown)
        {
            var halfW = cellSizePx / 2f;
            var halfH = cellSizePx / 4f;
            // We don't know the row count here, so we take a self-similar
            // origin : the (0,0) diamond's top vertex sits at (0, 0).
            // That puts (0,0) center at (0, halfH) and means cells with
            // (col − row) < 0 land at negative X. For the runtime, this
            // is fine because Configure passes the dimensions and we can
            // post-shift the layer ; for the math here we keep the
            // anchor self-similar so tests are clean. The runtime
            // applies the world-shift in <see cref="ComputeIsoOriginShift"/>.
            return new PanVec2(
                (coord.Col - coord.Row) * halfW,
                (coord.Col + coord.Row) * halfH + halfH);
        }
        var halfCell = cellSizePx / 2f;
        return new PanVec2(
            coord.Col * cellSizePx + halfCell,
            coord.Row * cellSizePx + halfCell);
    }

    public static GridCoord PixelOffsetToTile(
        Vector2 pixelOffsetFromAnchor,
        int cellSizePx,
        GridProjection projection)
    {
        if (projection == GridProjection.IsoDiamondDown)
        {
            var halfW = cellSizePx / 2.0;
            var halfH = cellSizePx / 4.0;

            var x = pixelOffsetFromAnchor.X;
            var y = pixelOffsetFromAnchor.Y - halfH; // ← compensation
            var col = Convert.ToInt32(Math.Round((x / halfW + y / halfH) / 2));
            var row = Convert.ToInt32(Math.Round((y / halfH - x / halfW) / 2));

            return new GridCoord(col, row);
        }
        var c = Convert.ToInt32(Math.Floor(pixelOffsetFromAnchor.X / cellSizePx));
        var r = Convert.ToInt32(Math.Floor(pixelOffsetFromAnchor.Y / cellSizePx));
        return new GridCoord(c, r);
    }

    /// <summary>
    /// Iso-mode origin shift to apply after <see cref="ComputeCellCenter"/>
    /// so the world frame stays "top-left at (0, 0), Y down" like rect
    /// mode. Returns <c>(rows × halfW, 0)</c> — push every cell right by
    /// the rows count so the leftmost iso cell's left vertex is at X=0.
    /// Rect mode returns <c>(0, 0)</c>.
    ///
    /// <para>
    /// The runtime fog renderer adds this shift to every iso cell's
    /// world position (or, equivalently, sets the FogContainer's
    /// position to this shift and keeps cells at their self-similar
    /// coords). Tests cover both forms.
    /// </para>
    /// </summary>
    public static PanVec2 ComputeIsoOriginShift(
        GridDimensions dimensions,
        int cellSizePx,
        GridProjection projection)
    {
        if (projection != GridProjection.IsoDiamondDown) return new PanVec2(0f, 0f);
        var halfW = cellSizePx / 2f;
        // The minimum (col − row) in the grid is at coord (0, rows-1), value -(rows-1).
        // To push everything to X >= 0, shift by +(rows-1) × halfW.
        // We err on the side of (rows × halfW) for one-pixel safety on the leftmost
        // diamond's leftmost vertex (vertex sits at center.X - halfW).
        return new PanVec2(dimensions.Rows * halfW, 0f);
    }

    /// <summary>
    /// Compute the axis-aligned world-space bounds of the full grid -- the
    /// rectangle <c>[0, 0]-[boundsX, boundsY]</c> that contains every cell's
    /// diamond (iso) or square (rect), with the iso origin shift already
    /// applied so the leftmost vertex sits at X=0 and the topmost vertex at
    /// Y=0. Used by <see cref="Wayfinders.Client.Components.MapPan2DComponent"/>
    /// at <c>Configure</c> time to compute Camera2D.LimitRight/Bottom against
    /// the actual world the player can perceive (the fog grid is the world,
    /// not the source-map atlas).
    ///
    /// <para>
    /// <b>Why this exists.</b> Pre-fix, the camera limits tracked the source-map
    /// PNG size (e.g. 2048x1024). At iso projection with cellSize=128, a 2048x1024
    /// source produces a 33x33 grid whose iso bounding box is 4224x2112 -- wider
    /// AND taller than the source on iso's diagonal stretch. With limits set to
    /// the source size, vertical pan stalled because viewport.Y (1080) exceeded
    /// the limit range Y (1024) and <see cref="CameraPanLogic.ClampCameraCenter"/>
    /// snapped to image-center on Y. Tracking the iso grid bounds instead lets
    /// the player pan the entire grid the fog/atlas/drill systems address.
    /// </para>
    ///
    /// <para>
    /// <b>Iso mode bounds.</b>
    /// <list type="bullet">
    ///   <item><b>boundsX</b> = <c>(cols + rows - 1) * halfW</c>. Derivation :
    ///         the rightmost cell is <c>(cols-1, 0)</c> with center.X =
    ///         <c>(cols-1) * halfW + shift.X</c>, where <c>shift.X = rows * halfW</c>.
    ///         Adding halfW for the rightmost vertex gives
    ///         <c>(cols-1) * halfW + rows * halfW + halfW = (cols + rows) * halfW</c>.
    ///         The implementation uses the <c>(rows-1)</c> minus the leftmost
    ///         padding form for symmetry with <see cref="ComputeIsoOriginShift"/> ;
    ///         it lands on the same value.</item>
    ///   <item><b>boundsY</b> = <c>(cols + rows) * halfH</c>. Derivation :
    ///         the bottommost cell is <c>(cols-1, rows-1)</c> with center.Y =
    ///         <c>(cols + rows - 2) * halfH + halfH = (cols + rows - 1) * halfH</c> ;
    ///         the bottom vertex adds halfH to land at <c>(cols + rows) * halfH</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Rect mode bounds.</b> Trivially <c>(cols * cellSizePx, rows * cellSizePx)</c>.
    /// </para>
    /// </summary>
    public static PanVec2 ComputeGridWorldBounds(
        GridDimensions dimensions,
        int cellSizePx,
        GridProjection projection)
    {
        if (cellSizePx <= 0) return new PanVec2(0f, 0f);

        if (projection == GridProjection.IsoDiamondDown)
        {
            var halfW = cellSizePx / 2f;
            var halfH = cellSizePx / 4f;
            var boundsX = (dimensions.Columns + dimensions.Rows) * halfW;
            var boundsY = (dimensions.Columns + dimensions.Rows) * halfH;
            return new PanVec2(boundsX, boundsY);
        }

        return new PanVec2(
            dimensions.Columns * cellSizePx,
            dimensions.Rows * cellSizePx);
    }

    /// <summary>
    /// Hit-test : map a world-space position back to its containing
    /// grid cell. The runtime fog renderer uses this to resolve "cell
    /// under cursor" for the F debug command and the slice 3 drill
    /// candidate seam.
    ///
    /// <para>
    /// <b>Rect mode</b> : floor (worldX/cellSize, worldY/cellSize), bounds
    /// check, return null if outside.
    /// </para>
    ///
    /// <para>
    /// <b>Iso mode</b> : invert the iso projection. Given a world point
    /// <c>(wx, wy)</c> after subtracting the iso origin shift, the
    /// continuous iso coords are <c>continuousCol = wx/(2·halfW) +
    /// (wy − halfH)/(2·halfH)</c> and <c>continuousRow = (wy − halfH)
    /// /(2·halfH) − wx/(2·halfW)</c>. Floor to get integer cell coords ;
    /// then verify the point is actually inside the diamond (not in the
    /// empty triangles between four cells' bounding boxes) by the
    /// canonical predicate <c>|dx|/halfW + |dy|/halfH ≤ 1</c> on the
    /// candidate cell's center.
    /// </para>
    ///
    /// <para>
    /// <b>"Between four diamonds" disambiguation.</b> When the floor lands
    /// in the empty triangle between four neighbouring diamonds, the
    /// hit-test returns the diamond whose center is closest to the
    /// world point. This matches what a player would expect ("I clicked
    /// near that cell, it should win") and avoids a null hole between
    /// cells. The closest-center fallback is bounded — only the four
    /// diamonds whose bounding boxes contain the point are candidates.
    /// </para>
    /// </summary>
    public static GridCoord? WorldPositionToCell(
        PanVec2 worldPosition,
        int cellSizePx,
        GridDimensions dimensions,
        GridProjection projection = GridProjection.Rect)
    {
        if (cellSizePx <= 0) return null;

        if (projection == GridProjection.IsoDiamondDown)
        {
            return WorldPositionToCellIso(worldPosition, cellSizePx, dimensions);
        }

        if (worldPosition.X < 0 || worldPosition.Y < 0) return null;
        var col = (int)(worldPosition.X / cellSizePx);
        var row = (int)(worldPosition.Y / cellSizePx);
        if (col < 0 || col >= dimensions.Columns) return null;
        if (row < 0 || row >= dimensions.Rows) return null;
        return new GridCoord(col, row);
    }

    private static GridCoord? WorldPositionToCellIso(
        PanVec2 worldPosition,
        int cellSizePx,
        GridDimensions dimensions)
    {
        var halfW = cellSizePx / 2f;
        var halfH = cellSizePx / 4f;

        // Subtract the iso origin shift to get back to the self-similar
        // frame where ComputeCellCenter is defined.
        var shift = ComputeIsoOriginShift(dimensions, cellSizePx, GridProjection.IsoDiamondDown);
        var localX = worldPosition.X - shift.X;
        var localY = worldPosition.Y - shift.Y - halfH; // ComputeCellCenter adds +halfH on Y

        // Invert the iso projection :
        //   localX = (col − row) × halfW
        //   localY = (col + row) × halfH
        // → col = localX/(2·halfW) + localY/(2·halfH)
        // → row = localY/(2·halfH) − localX/(2·halfW)
        var continuousCol = localX / (2f * halfW) + localY / (2f * halfH);
        var continuousRow = localY / (2f * halfH) - localX / (2f * halfW);

        var col = (int)System.MathF.Floor(continuousCol);
        var row = (int)System.MathF.Floor(continuousRow);

        // The flooring may put us in the empty triangle between four
        // diamonds. Test the four candidates (col,row), (col+1,row),
        // (col,row+1), (col+1,row+1) and pick the one whose center is
        // closest to the world point AND whose diamond actually contains
        // it (predicate |dx|/halfW + |dy|/halfH ≤ 1).
        var candidates = new[]
        {
            new GridCoord(col, row),
            new GridCoord(col + 1, row),
            new GridCoord(col, row + 1),
            new GridCoord(col + 1, row + 1),
        };

        GridCoord? best = null;
        var bestDistSq = float.MaxValue;
        foreach (var c in candidates)
        {
            if (c.Col < 0 || c.Col >= dimensions.Columns) continue;
            if (c.Row < 0 || c.Row >= dimensions.Rows) continue;

            var center = ComputeCellCenter(c, cellSizePx, GridProjection.IsoDiamondDown);
            // Apply origin shift to compare with worldPosition directly.
            var centerX = center.X + shift.X;
            var centerY = center.Y + shift.Y;
            var dx = worldPosition.X - centerX;
            var dy = worldPosition.Y - centerY;

            // Diamond inclusion predicate. Equality on the boundary counts
            // as "inside" (top-left convention, matches rect mode's
            // boundary inclusion).
            if (System.MathF.Abs(dx) / halfW + System.MathF.Abs(dy) / halfH > 1.0f + IsoBoundaryEpsilon)
            {
                continue; // not in this diamond
            }

            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = c;
            }
        }

        return best;
    }

    /// <summary>
    /// Boundary fudge for the iso diamond inclusion predicate. Without
    /// this, a world point exactly on the diamond edge could fail the
    /// strict <c>≤ 1</c> comparison due to float imprecision. Same role
    /// as <c>ZoomNavLogic.ZoomEpsilon</c>.
    /// </summary>
    public const float IsoBoundaryEpsilon = 1e-4f;

    /// <summary>
    /// Enumerate every cell of the grid in row-major order. Used by the
    /// fog renderer at spawn (one node per yielded coord) and by the
    /// baking script (one palette quantification per cell). The
    /// row-major order keeps spatial locality in the persistence
    /// dictionary.
    ///
    /// <para>
    /// Iso mode emits the same row-major sweep — the (col, row) tuple is
    /// the iso grid index, not a screen-space rectangle position. The
    /// renderer translates each tuple to world coords via
    /// <see cref="ComputeCellCenter"/>.
    /// </para>
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

    /// <summary>
    /// Diamond vertex offsets relative to the cell center, in world units,
    /// for an iso DiamondDown cell of side <paramref name="cellSizePx"/>.
    /// Order : <b>top, right, bottom, left</b> (clockwise starting from
    /// the top vertex). Used by the runtime to build a Polygon2D with the
    /// correct iso shape, and by the per-tile bitmap API to clip an
    /// arbitrary texture to the diamond.
    /// </summary>
    public static (PanVec2 Top, PanVec2 Right, PanVec2 Bottom, PanVec2 Left) IsoDiamondVerticesAroundCenter(
        int cellSizePx)
    {
        var halfW = cellSizePx / 2f;
        var halfH = cellSizePx / 4f;
        return (
            Top:    new PanVec2(0f, -halfH),
            Right:  new PanVec2(halfW, 0f),
            Bottom: new PanVec2(0f, halfH),
            Left:   new PanVec2(-halfW, 0f));
    }

    /// <summary>
    /// True iff a world-space point is inside the diamond of the cell at
    /// <paramref name="coord"/>, using the canonical iso predicate
    /// <c>|dx|/halfW + |dy|/halfH ≤ 1</c>. Used by the
    /// <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/> at
    /// editor time to sample only the pixels that fall inside each
    /// diamond, not the bounding box (slice 3.5 livrable 5).
    /// </summary>
    public static bool IsoCellContainsWorldPoint(
        GridCoord coord,
        PanVec2 worldPoint,
        int cellSizePx,
        GridDimensions dimensions)
    {
        var halfW = cellSizePx / 2f;
        var halfH = cellSizePx / 4f;
        var center = ComputeCellCenter(coord, cellSizePx, GridProjection.IsoDiamondDown);
        var shift = ComputeIsoOriginShift(dimensions, cellSizePx, GridProjection.IsoDiamondDown);
        var dx = worldPoint.X - (center.X + shift.X);
        var dy = worldPoint.Y - (center.Y + shift.Y);
        return System.MathF.Abs(dx) / halfW + System.MathF.Abs(dy) / halfH <= 1.0f + IsoBoundaryEpsilon;
    }

    /// <summary>
    /// Slice 3.6 livrable 4 -- compute the axis-aligned bounding box of
    /// a single cell's diamond (or square in rect mode), expressed in
    /// world coordinates, with the iso origin shift already applied.
    /// Used by <see cref="Wayfinders.Client.Components.FogTileLayer.EnableSourceMapAtlas"/>
    /// to size a Godot <c>AtlasTexture.Region</c> per cell so each cell
    /// samples the correct window of the source world map.
    ///
    /// <para>
    /// <b>Iso mode</b> : center = <c>ComputeCellCenter + ComputeIsoOriginShift</c>,
    /// width = <c>cellSizePx</c>, height = <c>cellSizePx / 2</c>.
    /// The bbox is the bounding rectangle of the 2:1 diamond (vertices at
    /// (±halfW, 0) and (0, ±halfH) around the center). Pixels at the
    /// corners of the bbox sit outside the diamond — but the
    /// <see cref="Godot.Polygon2D"/> shape clips them at render time, so
    /// the visible result is just the diamond's worth of source pixels.
    /// </para>
    ///
    /// <para>
    /// <b>Rect mode</b> : center = <c>ComputeCellCenter</c> (no iso shift),
    /// width = height = <c>cellSizePx</c>. Returned for completeness so
    /// any future rect-grid consumer (E3+ overlay) can call this without
    /// branching on projection.
    /// </para>
    ///
    /// <para>
    /// <b>Pure-C# seam.</b> Returns a <see cref="CellRect"/> (Godot-free),
    /// the runtime translates to <c>Godot.Rect2</c> at the boundary.
    /// Mirrors the existing <see cref="PanVec2"/> / <see cref="GridCoord"/>
    /// pattern so xUnit can pin the math without an engine.
    /// </para>
    /// </summary>
    public static CellRect ComputeCellBoundingBox(
        GridCoord coord,
        int cellSizePx,
        GridDimensions dimensions,
        GridProjection projection = GridProjection.IsoDiamondDown)
    {
        if (projection == GridProjection.IsoDiamondDown)
        {
            var center = ComputeCellCenter(coord, cellSizePx, GridProjection.IsoDiamondDown);
            var shift = ComputeIsoOriginShift(dimensions, cellSizePx, GridProjection.IsoDiamondDown);
            var halfW = cellSizePx / 2f;
            var halfH = cellSizePx / 4f;
            return new CellRect(
                MinX: center.X + shift.X - halfW,
                MinY: center.Y + shift.Y - halfH,
                Width: cellSizePx,
                Height: cellSizePx / 2f);
        }

        // Rect mode : centered cell.
        var rectCenter = ComputeCellCenter(coord, cellSizePx, GridProjection.Rect);
        var half = cellSizePx / 2f;
        return new CellRect(
            MinX: rectCenter.X - half,
            MinY: rectCenter.Y - half,
            Width: cellSizePx,
            Height: cellSizePx);
    }

    /// <summary>
    /// Slice 3.6 hotfix (M3 / Phase 9 / 2026-05-08) -- compute the four UV
    /// vertices of a cell's iso diamond in <b>absolute source-map pixel
    /// coords</b>. Returned in the same order as
    /// <see cref="IsoDiamondVerticesAroundCenter"/> :
    /// top, right, bottom, left.
    ///
    /// <para>
    /// <b>Why this exists.</b> The slice 3.6 first attempt used
    /// <c>AtlasTexture</c> with per-cell <c>Region</c> + UV in region-local
    /// pixel coords (top -> (rW/2, 0), right -> (rW, rH/2), ...). Smoke-test
    /// at E2 revealed every cell sampled the same slice of the source map
    /// (~the top-left corner) regardless of cell coord. Root cause :
    /// <c>Godot.Polygon2D</c> + <c>AtlasTexture</c> in Godot 4 does not
    /// honour the AtlasTexture's <c>Region</c> offset when computing texel
    /// coords from the UV array -- it samples the parent atlas at the
    /// raw UV pixel value as if no region were set. With identical UV per
    /// cell (region-local), every cell pulled the same absolute pixels.
    /// </para>
    ///
    /// <para>
    /// <b>The fix.</b> Drop AtlasTexture entirely. Pass the full source
    /// map to every cell, then assign UV in absolute source-map pixel
    /// coords -- different per cell, derived from the cell center in
    /// source-map coords. Godot 4 Polygon2D samples the whole texture at
    /// those absolute UVs, the diamond shape clips to the diamond vertices,
    /// and each cell shows its own slice of the source map.
    /// </para>
    ///
    /// <para>
    /// <b>Source-map coord frame.</b> Because the runtime invariant is
    /// <c>worldImageSize == sourceMap.GetSize()</c> (the world map sprite's
    /// texture <i>is</i> the source for the atlas), world coords with the
    /// iso origin shift applied are exactly source-map pixel coords. The
    /// cell-center input <paramref name="cellCenterInSourceCoords"/>
    /// expects that value -- conventionally
    /// <c>ComputeCellCenter(coord) + ComputeIsoOriginShift(dimensions)</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Iso vs rect.</b> Iso emits 4 UV at the diamond vertices :
    /// (cx, cy-halfH), (cx+halfW, cy), (cx, cy+halfH), (cx-halfW, cy).
    /// Rect emits 4 UV at the square corners (top-left, top-right,
    /// bottom-right, bottom-left) in absolute source coords. Both modes
    /// match <see cref="FogTileLayer.BuildCellVertices"/> ordering so
    /// <c>Polygon2D.UV[i]</c> aligns with <c>Polygon2D.Polygon[i]</c>.
    /// </para>
    /// </summary>
    public static (PanVec2 Top, PanVec2 Right, PanVec2 Bottom, PanVec2 Left) ComputeIsoCartonUvInSourceCoords(
        PanVec2 cellCenterInSourceCoords,
        int cellSizePx,
        GridProjection projection = GridProjection.IsoDiamondDown)
    {
        var cx = cellCenterInSourceCoords.X;
        var cy = cellCenterInSourceCoords.Y;

        if (projection == GridProjection.IsoDiamondDown)
        {
            var halfW = cellSizePx / 2f;
            var halfH = cellSizePx / 4f;
            return (
                Top:    new PanVec2(cx,         cy - halfH),
                Right:  new PanVec2(cx + halfW, cy),
                Bottom: new PanVec2(cx,         cy + halfH),
                Left:   new PanVec2(cx - halfW, cy));
        }

        // Rect mode : 4 UV at the square corners. Iso "Top/Right/Bottom/Left"
        // names are kept to match the tuple's positional contract -- they
        // map to top-left, top-right, bottom-right, bottom-left here.
        var half = cellSizePx / 2f;
        return (
            Top:    new PanVec2(cx - half, cy - half), // top-left
            Right:  new PanVec2(cx + half, cy - half), // top-right
            Bottom: new PanVec2(cx + half, cy + half), // bottom-right
            Left:   new PanVec2(cx - half, cy + half)); // bottom-left
    }
}

/// <summary>
/// Grid projection mode, slice 3.5 (M3 / Phase 9 / 2026-05-08).
/// <list type="bullet">
///   <item><see cref="Rect"/> -- slice 1+2+3 default. Cells are square
///         <c>cellSizePx × cellSizePx</c> in world space, laid out in a
///         straight rect grid. Hit-test is simple bounded floor division.</item>
///   <item><see cref="IsoDiamondDown"/> -- slice 3.5 default for L1 World.
///         Cells are 2:1 diamonds (<c>cellSizePx</c> wide, <c>cellSizePx/2</c>
///         tall) laid out in a DiamondDown projection. Hit-test inverts
///         the iso transform with a closest-center fallback for the
///         empty triangles between four neighbours.</item>
/// </list>
/// </summary>
public enum GridProjection
{
    /// <summary>Square cells, rect grid. Slice 1+2+3 mode, retained for
    /// E3+ city interiors / E4 fiches / any future overlay that doesn't
    /// need iso projection.</summary>
    Rect,

    /// <summary>2:1 diamond cells, DiamondDown projection. Slice 3.5
    /// default for L1 World fog tiles, matches the iso visual treatment
    /// the player perceives on the master 3840×2160.</summary>
    IsoDiamondDown,
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

/// <summary>
/// Slice 3.6 livrable 4 -- pure-C# axis-aligned rectangle in world
/// coordinates, returned by <see cref="FogTileGridLogic.ComputeCellBoundingBox"/>.
/// Godot-free stand-in for <c>Godot.Rect2</c>, mirrors the
/// <see cref="PanVec2"/> seam pattern so the cell-bbox math is unit-testable
/// without an engine.
/// </summary>
/// <param name="MinX">Top-left X (smallest X value of the rectangle).</param>
/// <param name="MinY">Top-left Y (smallest Y value of the rectangle).</param>
/// <param name="Width">Width in world units (always &gt;= 0).</param>
/// <param name="Height">Height in world units (always &gt;= 0).</param>
public readonly record struct CellRect(float MinX, float MinY, float Width, float Height)
{
    /// <summary>Right edge X coordinate.</summary>
    public float MaxX => MinX + Width;

    /// <summary>Bottom edge Y coordinate.</summary>
    public float MaxY => MinY + Height;

    /// <summary>Center X coordinate.</summary>
    public float CenterX => MinX + Width / 2f;

    /// <summary>Center Y coordinate.</summary>
    public float CenterY => MinY + Height / 2f;
}
