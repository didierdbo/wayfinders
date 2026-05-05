using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Simulation;

namespace Wayfinders.Client.Scenes;

/// <summary>
/// Pathfinding service that wraps Godot's <see cref="AStarGrid2D"/> over a
/// <see cref="SimulationGrid"/> (Phase 5 L4).
///
/// <para>
/// <b>Plain C# class, not a <see cref="Node"/>.</b> Same rationale as
/// <see cref="SimulationGrid"/>: this is a service over data, not a
/// scene-tree citizen. <see cref="AStarGrid2D"/> is itself a
/// <see cref="RefCounted"/> (so the GC handles its lifetime once we drop
/// our reference), which is exactly what we want — the service is
/// constructed once in <see cref="TacticalScene._Ready"/>, lives for the
/// scene's lifetime, and goes away when the scene exits the tree.
/// </para>
///
/// <para>
/// <b>Source of truth stays in <see cref="SimulationGrid"/>.</b> The
/// <see cref="AStarGrid2D"/> instance is a <i>derived cache</i> seeded from
/// the simulation. If the simulation changes (a boulder is destroyed, a
/// unit blocks a cell, terrain mutates), call <see cref="Invalidate"/> to
/// rebuild — never poke <see cref="AStarGrid2D"/> directly. This keeps the
/// "visual / simulation / pathfinding" trio rigorously one-way: simulation
/// is upstream, the other two are downstream.
/// </para>
///
/// <para>
/// <b>Cost model.</b> Two-tier:
/// <list type="bullet">
///   <item><b>Hard wall</b>: <see cref="Cell.IsPassable"/> false →
///   <see cref="AStarGrid2D.SetPointSolid"/>. Pathfinding skips entirely.
///   This is the <see cref="TerrainType.Blocked"/> case.</item>
///   <item><b>Soft cost</b>: passable cells get a per-cell weight scale via
///   <see cref="AStarGrid2D.SetPointWeightScale"/>. Walkable=1.0,
///   LooseScree=2.0. Loose scree is twice as expensive as open ground —
///   reflects the noise/footing penalty in gameplay terms. Numbers are
///   placeholder-grade for L4; they are not yet calibrated against unit
///   movement budgets (that happens in L5+ when units actually have a
///   movement stat).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Diagonal policy.</b>
/// <see cref="AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles"/>: diagonals
/// allowed in open ground, blocked when either of the two adjacent
/// orthogonal cells is solid. This is the standard tactical-RPG choice —
/// it prevents units from squeezing diagonally between two boulders, which
/// would look wrong on screen and play wrong (a diagonal step "through" a
/// solid corner). Battle Brothers uses a hex grid and sidesteps the
/// question; XCOM uses the same "no corner-cutting" rule we are picking
/// here.
/// </para>
///
/// <para>
/// <b>Heuristic.</b> Default Manhattan; the engine picks the right one for
/// the diagonal mode automatically. We do not override.
/// </para>
///
/// <para>
/// <b>AOT-clean.</b> <see cref="AStarGrid2D"/> is engine-native, no
/// reflection on our side, no source-gen needed.
/// </para>
/// </summary>
public sealed class PathfindingService
{
    private readonly SimulationGrid _grid;
    private readonly AStarGrid2D _astar;

    /// <summary>
    /// Soft-cost weight scale applied to <see cref="TerrainType.Walkable"/>
    /// cells. The baseline; everything else is expressed relative to this.
    /// </summary>
    public const float WalkableWeight = 1.0f;

    /// <summary>
    /// Soft-cost weight scale applied to <see cref="TerrainType.LooseScree"/>
    /// cells. Twice the cost of open ground. Placeholder-grade for L4 —
    /// expect this to move once L5+ unit movement budgets land and we can
    /// calibrate against actual gameplay tempo.
    /// </summary>
    public const float LooseScreeWeight = 2.0f;

    /// <summary>
    /// Build the service over <paramref name="grid"/> and seed the
    /// <see cref="AStarGrid2D"/> from current cell state. The grid
    /// reference is held for the service's lifetime; callers must call
    /// <see cref="Invalidate"/> after mutating the simulation grid.
    /// </summary>
    public PathfindingService(SimulationGrid grid)
    {
        _grid = grid;
        _astar = new AStarGrid2D
        {
            Region = new Rect2I(0, 0, grid.Width, grid.Height),
            CellSize = Vector2.One,
            DiagonalMode = AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles,
        };
        // Update() must be called after configuring Region/CellSize/etc.
        // and before any SetPointSolid / SetPointWeightScale calls — it
        // allocates the per-cell state. Calling SetPointSolid before
        // Update() is a silent no-op against the (then-empty) point set,
        // a textbook L4 footgun.
        _astar.Update();
        SeedFromGrid();
    }

    /// <summary>
    /// Recompute the <see cref="AStarGrid2D"/> state from the current
    /// <see cref="SimulationGrid"/>. Call after any mutation to the grid
    /// that pathfinding cares about (terrain change, blocked-by-occupant
    /// change). Cheap for 24×24; if the grid grows large enough that the
    /// full rebuild is felt in the frame budget, switch to per-cell
    /// surgical updates — but measure first.
    /// </summary>
    public void Invalidate() => SeedFromGrid();

    /// <summary>
    /// Find a path from <paramref name="from"/> to <paramref name="to"/>.
    /// Returns the sequence of cell coordinates the path visits, including
    /// both endpoints. Empty array if no path exists, or if either endpoint
    /// is out of bounds or solid.
    ///
    /// <para>
    /// <b>Endpoint validation.</b> A pathfinding query against a solid
    /// endpoint is almost always a caller bug (e.g., the click-to-cell
    /// surface let the user pick a boulder as a destination). We return
    /// empty rather than throwing because the caller is interactive code
    /// and "no path" is a perfectly valid UI state to render. The caller
    /// decides whether to print, beep, or ignore.
    /// </para>
    ///
    /// <para>
    /// <b>Marshalling note.</b> <see cref="AStarGrid2D.GetIdPath"/> returns
    /// <see cref="Godot.Collections.Array{T}"/> (a marshalled wrapper over
    /// the engine's C++ <c>PackedVector2iArray</c>), <i>not</i> a plain
    /// <see cref="Vector2I"/> array. We copy into a managed
    /// <see cref="Vector2I"/>[] here so callers can iterate it freely
    /// without paying marshalling overhead per access. The cost of the
    /// copy is one allocation plus N writes for the path length — at 24×24
    /// max-length-couple-dozen, this is in the noise.
    /// </para>
    /// </summary>
    public Vector2I[] FindPath(Vector2I from, Vector2I to)
    {
        if (!_grid.IsInBounds(from) || !_grid.IsInBounds(to))
        {
            return System.Array.Empty<Vector2I>();
        }
        if (_astar.IsPointSolid(from) || _astar.IsPointSolid(to))
        {
            return System.Array.Empty<Vector2I>();
        }

        Godot.Collections.Array<Vector2I> godotPath = _astar.GetIdPath(from, to);
        var managed = new Vector2I[godotPath.Count];
        for (int i = 0; i < godotPath.Count; i++)
        {
            managed[i] = godotPath[i];
        }
        return managed;
    }

    /// <summary>
    /// Total movement cost of <paramref name="path"/> as the sum of each
    /// step's terrain weight. Diagonal steps count their full weight (we
    /// do not multiply by sqrt(2)) — the player-facing cost model is
    /// "one tile, one cost", same as Battle Brothers and XCOM. The
    /// underlying A* heuristic already handles diagonal distance correctly
    /// for the search; this method is for display/debug.
    ///
    /// <para>
    /// The first cell (the <c>from</c> endpoint) is not counted — you do
    /// not pay to stand where you already are. If <paramref name="path"/>
    /// has fewer than 2 entries, returns 0.
    /// </para>
    /// </summary>
    public float ComputePathCost(IReadOnlyList<Vector2I> path)
    {
        if (path.Count < 2)
        {
            return 0f;
        }

        float total = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            total += WeightFor(_grid.GetCell(path[i]).Terrain);
        }
        return total;
    }

    private void SeedFromGrid()
    {
        foreach (var (coord, cell) in _grid.GetAllCells())
        {
            if (!cell.IsPassable)
            {
                _astar.SetPointSolid(coord, true);
            }
            else
            {
                _astar.SetPointSolid(coord, false);
                _astar.SetPointWeightScale(coord, WeightFor(cell.Terrain));
            }
        }
    }

    private static float WeightFor(TerrainType terrain) => terrain switch
    {
        TerrainType.Walkable => WalkableWeight,
        TerrainType.LooseScree => LooseScreeWeight,
        // Blocked never reaches here (gated by IsPassable), but the switch
        // must be exhaustive for the compiler. A high sentinel weight
        // would be misleading; we just return walkable as a no-op default.
        TerrainType.Blocked => WalkableWeight,
        _ => WalkableWeight,
    };
}
