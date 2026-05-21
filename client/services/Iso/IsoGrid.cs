using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services.Iso;

/// <summary>
/// The logical grid (layer 2) of the J3-iso hybrid socle. Holds the
/// authoritative set of <see cref="IsoCell"/> for one district: which
/// cells exist, whether they are walkable, who occupies them.
///
/// <para>
/// <b>Plain C# class, not a <c>Godot.Node</c>.</b> The grid is data, not
/// a scene-tree citizen. Making it a <c>Node</c> would drag in
/// <c>QueueFree</c> discipline and lifecycle hooks for zero benefit, and
/// invite the very trap the hybrid prevents — state living inside the
/// scene graph. The grid is held by reference on the authoritative
/// <c>GameState</c> (NPC-autonomy lock 2026-05-09: the client GameState is
/// the authority); ordinary GC reclaims it. Same shape as
/// <c>Simulation.SimulationGrid</c>.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Cell coords are <see cref="TileCoordinate"/>
/// (pure-C# integer pair), not <c>Godot.Vector2I</c>, so the whole logic
/// layer is cherry-pickable into the xUnit test host. The Godot-bound
/// <c>IsoBoard</c> node converts at the engine edge.
/// </para>
///
/// <para>
/// <b>Decision #3 — <see cref="Dictionary{TKey,TValue}"/>, not
/// <c>IsoCell[,]</c>.</b> Didier delegated this as a pure technical call
/// (build brief 2026-05-21). A <c>Dictionary&lt;TileCoordinate, IsoCell&gt;</c>
/// is chosen over a 2D array for three reasons:
/// <list type="number">
///   <item><b>Districts are not full rectangles.</b> A Halfgate district
///     footprint is an irregular polygon carved out of a bitmap (roadmap
///     §0, the puzzle-fit of <c>lower_quays</c> / <c>high_wall</c>). A 2D
///     array forces every off-shape coordinate to hold a sentinel
///     <c>IsoCell?</c>, pushing null-checks to every call site. A
///     dictionary simply does not contain the holes — "is this a cell?"
///     is one <c>ContainsKey</c>.</item>
///   <item><b>Cost is irrelevant at this scale.</b> A district is hundreds
///     to low-thousands of cells, and the grid is built once at district
///     entry, never per-frame. The <see cref="TileCoordinate"/> hash is a
///     couple of integer ops; it never shows up in a frame budget.</item>
///   <item><b>Consistency.</b> <c>Simulation.SimulationGrid</c> already
///     uses <c>Dictionary&lt;Vector2I, Cell&gt;</c> and its class doc
///     calls out the same sparse-friendly rationale. Two grids, one
///     idiom.</item>
/// </list>
/// The cost — no cache-line locality, no O(1)-without-hash access — does
/// not bite a build-once, navigate-occasionally district grid. If a future
/// dense, hot, rectangular grid appears (tactical combat may want one),
/// that grid picks <c>IsoCell[,]</c> on its own merits; this one does not.
/// </para>
///
/// <para>
/// <b>Visual grid is not the source of truth.</b> The district floor is a
/// single <c>Sprite2D</c> bitmap. Nothing is read back from the renderer
/// to answer "is this cell walkable / occupied". This class answers it.
/// Pitfall #12.
/// </para>
/// </summary>
public sealed class IsoGrid
{
    private readonly Dictionary<TileCoordinate, IsoCell> _cells;

    /// <summary>
    /// Number of cells in the grid. A district footprint count, not a
    /// width × height — the grid is not necessarily rectangular.
    /// </summary>
    public int Count => _cells.Count;

    /// <summary>
    /// Build a grid from an explicit set of cells. Typical callers use
    /// <see cref="BuildRectangle"/> for a placeholder rectangular district
    /// until the real polygon footprint sidecar lands.
    /// </summary>
    /// <param name="cells">
    /// The cells; each is keyed by its own <see cref="IsoCell.Coord"/>.
    /// Duplicate coords throw — a coord must name exactly one cell.
    /// </param>
    public IsoGrid(IEnumerable<IsoCell> cells)
    {
        _cells = new Dictionary<TileCoordinate, IsoCell>();
        foreach (var cell in cells)
        {
            if (!_cells.TryAdd(cell.Coord, cell))
            {
                throw new System.ArgumentException(
                    $"Duplicate cell coord {cell.Coord} in IsoGrid construction.",
                    nameof(cells));
            }
        }
    }

    /// <summary>
    /// True if a cell exists at <paramref name="coord"/>. Because the grid
    /// is sparse, this is "is this part of the district", not a
    /// rectangular bounds check.
    /// </summary>
    public bool HasCell(TileCoordinate coord) => _cells.ContainsKey(coord);

    /// <summary>
    /// Read the cell at <paramref name="coord"/>. Throws
    /// <see cref="KeyNotFoundException"/> if no cell exists there — callers
    /// that might query an off-footprint coord gate with
    /// <see cref="HasCell"/> first. Failing loud is the right default;
    /// silent <c>null</c> invites "empty or off-district?" ambiguity at
    /// every call site (same rationale as <c>SimulationGrid.GetCell</c>).
    /// </summary>
    public IsoCell GetCell(TileCoordinate coord) => _cells[coord];

    /// <summary>
    /// Try to read the cell at <paramref name="coord"/>. Returns false and
    /// a <c>null</c> cell when the coord is off-footprint — the no-throw
    /// path for callers (picking, hover) that legitimately probe coords
    /// that may not exist.
    /// </summary>
    public bool TryGetCell(TileCoordinate coord, out IsoCell? cell)
        => _cells.TryGetValue(coord, out cell);

    /// <summary>
    /// True if a layer-3 entity can be placed on <paramref name="coord"/>:
    /// the cell exists and is <see cref="IsoCell.IsFree"/>. The single
    /// placement gate of the socle. Off-footprint coords return false
    /// (you cannot place outside the district).
    /// </summary>
    public bool CanPlaceOccupant(TileCoordinate coord)
        => _cells.TryGetValue(coord, out var cell) && cell.IsFree;

    /// <summary>
    /// Set (or clear, with <c>null</c>) the occupant of a cell and return
    /// the updated cell. Throws if the coord is off-footprint, or if
    /// placing a non-null occupant onto a cell that is not
    /// <see cref="IsoCell.IsFree"/> — placement validity is gated here, not
    /// hoped for at the call site.
    ///
    /// <para>
    /// This is a logic-layer mutation only. It does not move any Node. A
    /// drag-drop in the UI is a <i>tactical-intent signal</i>; the
    /// authority (GameState, holding this grid) calls
    /// <see cref="SetOccupant"/>, and only then does the layer-3 entity
    /// view animate toward the new cell (NPC-autonomy lock 2026-05-09).
    /// </para>
    /// </summary>
    /// <param name="coord">Cell to update.</param>
    /// <param name="occupantId">
    /// New occupant id, or <c>null</c> to vacate the cell.
    /// </param>
    public IsoCell SetOccupant(TileCoordinate coord, string? occupantId)
    {
        if (!_cells.TryGetValue(coord, out var cell))
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(coord),
                $"No cell at {coord}; cannot set occupant.");
        }
        if (occupantId is not null && !cell.IsFree)
        {
            throw new System.InvalidOperationException(
                $"Cell {coord} is not free (walkable={cell.Walkable}, " +
                $"occupant={cell.OccupantId ?? "null"}); cannot place " +
                $"'{occupantId}'.");
        }

        var updated = cell with { OccupantId = occupantId };
        _cells[coord] = updated;
        return updated;
    }

    /// <summary>
    /// Enumerate every cell. Order is dictionary insertion order; callers
    /// needing a specific order sort explicitly.
    /// </summary>
    public IEnumerable<IsoCell> AllCells() => _cells.Values;

    /// <summary>
    /// Placeholder factory: a full <paramref name="width"/> ×
    /// <paramref name="height"/> rectangle of empty walkable cells, coords
    /// <c>(0,0)</c> to <c>(width-1, height-1)</c>. Stand-in for a real
    /// district footprint until the polygon sidecar (roadmap J2 / Mira)
    /// lands — the grid API does not change when the real shape arrives,
    /// only this builder is swapped for a sidecar-driven one.
    /// </summary>
    public static IsoGrid BuildRectangle(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(width),
                $"Rectangle grid needs positive dimensions; got {width}x{height}.");
        }

        var cells = new List<IsoCell>(width * height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                cells.Add(IsoCell.EmptyWalkable(new TileCoordinate(x, y)));
            }
        }
        return new IsoGrid(cells);
    }
}
