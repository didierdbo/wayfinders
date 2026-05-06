using System.Collections.Generic;
using Godot;

namespace Wayfinders.Client.Simulation;

/// <summary>
/// The logical half of the visual/simulation split. Owns the authoritative
/// game state for the tactical map: which cells exist, what terrain they
/// hold, who occupies them.
///
/// <para>
/// <b>Plain C# class, not a <see cref="Node"/>.</b> The simulation grid is
/// data, not a scene-tree citizen. Making it a <see cref="Node"/> would
/// drag in <c>QueueFree</c> discipline, lifecycle hooks, and
/// editor-inspector noise for zero benefit — and would invite the very
/// trap the visual/simulation split is supposed to prevent (state living
/// inside the scene graph). The grid is held as a private field on
/// <see cref="Wayfinders.Client.Scenes.TacticalScene"/>; the scene's
/// lifetime carries it, and ordinary GC reclaims it.
/// </para>
///
/// <para>
/// <b>Visual grid is not the source of truth.</b> Reading
/// <c>TileMapLayer.GetCellTileData(coord)</c> to answer "is this cell
/// passable?" is the textbook backend-reflex trap flagged in the Phase 5
/// pre-brief. The <see cref="TileMapLayer"/> is a render cache; this
/// class is the source of truth. If the two ever drift, the bug is in
/// whoever wrote to one without writing to the other — and the fix is
/// always at the call site, not in this class.
/// </para>
///
/// <para>
/// <b>API surface is intentionally small.</b> <see cref="GetCell"/>,
/// <see cref="SetCell"/>, <see cref="IsInBounds"/>,
/// <see cref="GetAllCells"/> plus <see cref="Width"/> and
/// <see cref="Height"/>. No bulk-mutation primitives, no terrain queries,
/// no pathfinding helpers — those land in their own classes when their
/// callers materialize. YAGNI.
/// </para>
///
/// <para>
/// <b>L4 pathfinding seam.</b> When <see cref="Godot.AStarGrid2D"/> wraps
/// this grid, it will iterate <see cref="GetAllCells"/> once at scene
/// init to seed cost weights from <see cref="Cell.Terrain"/> and skip
/// blocked cells via <see cref="Cell.IsPassable"/>. Occupant-aware
/// pathfinding (cannot path through a friendly unit) reads
/// <see cref="Cell.IsEmpty"/> per query. Both are pre-computed on the
/// record so the pathfinding code stays free of terrain
/// <c>switch</c> blocks.
/// </para>
///
/// <para>
/// <b>AOT-clean.</b> No reflection, no dynamic, no runtime code-gen.
/// <see cref="Dictionary{TKey, TValue}"/> with a value-type key
/// (<see cref="Vector2I"/>) is the right choice for a sparse-friendly
/// keyspace where 24×24 happens to be dense — should the map grow
/// non-rectangular later (a cave with carved-out cells), nothing
/// changes.
/// </para>
/// </summary>
public sealed class SimulationGrid
{
    private readonly Dictionary<Vector2I, Cell> _cells;

    /// <summary>
    /// Width of the rectangular grid in cells. Cells with X coords
    /// outside <c>[0, Width)</c> are out of bounds.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Height of the rectangular grid in cells. Cells with Y coords
    /// outside <c>[0, Height)</c> are out of bounds.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Build a grid of the given dimensions, every cell initialized to
    /// <paramref name="defaultCell"/>. Use the static factories
    /// (<see cref="BuildEmpty24x24"/>, <see cref="BuildRidgeMission"/>)
    /// rather than calling this directly when you want a known map shape.
    /// </summary>
    /// <param name="width">Width in cells. Must be positive.</param>
    /// <param name="height">Height in cells. Must be positive.</param>
    /// <param name="defaultCell">
    /// Cell value stamped into every coordinate. Typically
    /// <see cref="Cell.EmptyWalkable"/>.
    /// </param>
    public SimulationGrid(int width, int height, Cell defaultCell)
    {
        Width = width;
        Height = height;
        _cells = new Dictionary<Vector2I, Cell>(capacity: width * height);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                _cells[new Vector2I(x, y)] = defaultCell;
            }
        }
    }

    /// <summary>
    /// Read the cell at <paramref name="coord"/>. Throws
    /// <see cref="KeyNotFoundException"/> if the coord is out of bounds —
    /// callers that might query off-grid coords are expected to gate
    /// with <see cref="IsInBounds"/> first. Failing loud is the right
    /// default; silent <c>null</c> would invite "is this empty or off the
    /// map?" ambiguity at every call site.
    /// </summary>
    /// <param name="coord">Grid coordinate.</param>
    /// <returns>The <see cref="Cell"/> at that coordinate.</returns>
    public Cell GetCell(Vector2I coord) => _cells[coord];

    /// <summary>
    /// Replace the cell at <paramref name="coord"/>. The argument is the
    /// new full <see cref="Cell"/> value — typical call sites use a
    /// <c>with</c>-expression on the previous cell to mutate one field:
    /// <c>grid.SetCell(c, grid.GetCell(c) with { OccupantId = "kira" })</c>.
    /// Throws if <paramref name="coord"/> is out of bounds; same fail-loud
    /// rationale as <see cref="GetCell"/>.
    /// </summary>
    /// <param name="coord">Grid coordinate.</param>
    /// <param name="cell">New cell value.</param>
    public void SetCell(Vector2I coord, Cell cell)
    {
        if (!IsInBounds(coord))
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(coord),
                $"Coord {coord} is outside the {Width}x{Height} grid.");
        }
        _cells[coord] = cell;
    }

    /// <summary>
    /// True if <paramref name="coord"/> is a valid grid coordinate.
    /// Cheap rectangular-bounds check; does not hit the dictionary.
    /// </summary>
    public bool IsInBounds(Vector2I coord) =>
        coord.X >= 0 && coord.X < Width &&
        coord.Y >= 0 && coord.Y < Height;

    /// <summary>
    /// Enumerate every (coord, cell) pair. Order is dictionary
    /// insertion order which, given the constructor's nested loop, is
    /// column-major (X outer, Y inner). Callers that need a specific
    /// order should sort explicitly.
    /// </summary>
    public IEnumerable<KeyValuePair<Vector2I, Cell>> GetAllCells() => _cells;

    /// <summary>
    /// Convenience: a 24×24 grid of empty walkable cells. Useful for
    /// tests and for the L1→L2 smoke check (the simplest grid that
    /// proves the wiring).
    /// </summary>
    public static SimulationGrid BuildEmpty24x24() =>
        new(24, 24, Cell.EmptyWalkable);

    /// <summary>
    /// Build the UC1 ridge-mission map: a 24×24 grid where the southern
    /// half is the open approach corridor (walkable, elevation 0) and a
    /// ridge runs across the upper third with loose-scree slopes,
    /// scattered boulders (<see cref="TerrainType.Blocked"/>), and a few
    /// partial-cover positions Kira can use during the stealth approach.
    ///
    /// <para>
    /// <b>This is L2 placeholder geometry, not the final mission map.</b>
    /// Varn's mission script will lock exact tile placement during the
    /// L3-L5 work; this layout exists so the L2 deliverable shows a
    /// non-uniform map (proves the data layer can hold variation) and so
    /// L4 pathfinding has something interesting to path around. When
    /// Varn locks the real layout, this method gets rewritten — the
    /// shape of the API does not change.
    /// </para>
    ///
    /// <para>
    /// <b>Bands partition on <c>(x + y)</c>, not on <c>y</c>.</b> The
    /// TileSet uses <see cref="Godot.TileSet.TileLayoutEnum.DiamondDown"/>
    /// (see <see cref="Wayfinders.Client.Scenes.TacticalScene"/> class
    /// doc for the rationale around iso-coherent neighbors). With that
    /// layout, a cell at <c>(x, y)</c> projects to screen pixel
    /// <c>((x - y) * TW/2, (x + y) * TH/2)</c> — meaning <i>screen
    /// vertical position is governed by <c>x + y</c>, not by <c>y</c>
    /// alone</i>. Cells of constant <c>y</c> fall along a SE-going
    /// diagonal on screen, and cells of constant <c>x + y</c> fall along
    /// a horizontal line on screen. To keep the ridge mission's intended
    /// silhouette (ridge at the top of the screen, scree slope in the
    /// middle, approach corridor at the bottom), the band partitions
    /// below switch from <c>y &lt; threshold</c> to
    /// <c>x + y &lt; threshold</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Layout (top-of-screen → bottom-of-screen, with TW=64, TH=32):</b>
    /// <list type="bullet">
    ///   <item><c>x + y</c> in <c>[0, 12)</c> — the ridge top: walkable
    ///   plateau at elevation 2, where the two sentinels would post.
    ///   ~25% of the diamond's screen height (12 of 47 anti-diagonals).
    ///   </item>
    ///   <item><c>x + y</c> in <c>[12, 20)</c> — loose-scree slope,
    ///   elevation 1, the noisy approach band. ~17% of screen
    ///   height.</item>
    ///   <item><c>x + y</c> in <c>[20, 47)</c> — open approach corridor,
    ///   elevation 0, walkable. The remaining ~58% of screen height. A
    ///   handful of boulders scattered for visual interest and pathfinding
    ///   fodder.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static SimulationGrid BuildRidgeMission()
    {
        const int width = 24;
        const int height = 24;

        // Anti-diagonal thresholds. Anti-diagonals on a 24×24 grid range
        // from x+y=0 (top corner of diamond) to x+y=46 (bottom corner).
        // 12 and 20 give roughly 25% / 17% / 58% screen-vertical bands —
        // a close match to the previous Stacked layout's 6 / 4 / 14 row
        // proportions.
        const int ridgePlateauThreshold = 12;
        const int looseScreeThreshold = 20;

        var grid = new SimulationGrid(width, height, Cell.EmptyWalkable);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var coord = new Vector2I(x, y);
                int antiDiagonal = x + y;
                Cell cell;

                if (antiDiagonal < ridgePlateauThreshold)
                {
                    // Ridge plateau — elevated walkable ground.
                    cell = new Cell(
                        Terrain: TerrainType.Walkable,
                        OccupantId: null,
                        Elevation: 2,
                        Cover: CoverLevel.None);
                }
                else if (antiDiagonal < looseScreeThreshold)
                {
                    // Slope of loose scree — passable but noisy.
                    cell = new Cell(
                        Terrain: TerrainType.LooseScree,
                        OccupantId: null,
                        Elevation: 1,
                        Cover: CoverLevel.None);
                }
                else
                {
                    // Approach corridor — open ground.
                    cell = Cell.EmptyWalkable;
                }

                grid._cells[coord] = cell;
            }
        }

        // Scatter a handful of fixed boulders for L2-readable variation
        // and L4-pathfinding fodder. Coords are intentionally hand-picked
        // and stable so the smoke test is reproducible. Picked from the
        // approach corridor band (x+y >= 20) so they sit visually on
        // open ground in the lower part of the diamond.
        Vector2I[] boulders =
        {
            new(8, 14), new(15, 11), new(20, 8),
            new(11, 17), new(18, 15), new(22, 12),
        };
        foreach (var b in boulders)
        {
            grid._cells[b] = grid._cells[b] with
            {
                Terrain = TerrainType.Blocked,
            };
        }

        // A pair of partial-cover positions on the scree band — the
        // designer-flavored "Kira can crouch behind these" spots. Picked
        // from the scree band (12 <= x+y < 20).
        Vector2I[] partialCoverSpots =
        {
            new(8, 8), new(11, 6),
        };
        foreach (var p in partialCoverSpots)
        {
            grid._cells[p] = grid._cells[p] with
            {
                Cover = CoverLevel.Partial,
            };
        }

        return grid;
    }
}
