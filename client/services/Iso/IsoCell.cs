using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services.Iso;

/// <summary>
/// Purely-logical model of one cell of an isometric district grid. The
/// data half of the J3-iso hybrid socle (design doc
/// <c>rune-iso-rendering-design-2026-05-20.md</c> §2.3, decision #1
/// validated by Didier 2026-05-21).
///
/// <para>
/// <b>Zero visual.</b> This record holds no texture, no sprite, no
/// "PlanFlat". The district floor is a single large Blender bitmap shown
/// by one <c>Sprite2D</c> (layer 1 of the hybrid). An <see cref="IsoCell"/>
/// is <i>not</i> a piece of that floor — it is the interaction / logic map
/// laid on top of it. Keeping pixels out of this type is the load-bearing
/// "simulation grid vs visual grid" discipline (Rune pitfall #12).
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> The cell coordinate is a
/// <see cref="TileCoordinate"/> (pure-C# integer pair), not a
/// <c>Godot.Vector2I</c>, so this whole logic layer is cherry-pickable
/// into the xUnit test host that has no GodotSharp — the same seam
/// discipline as <c>FogTileGridLogic</c> / <c>NpcRuntimeState</c>. The
/// Godot-bound <c>IsoBoard</c> node converts <c>Vector2I</c> ↔
/// <see cref="TileCoordinate"/> at the engine edge.
/// </para>
///
/// <para>
/// <b>Distinct from <see cref="Wayfinders.Client.Simulation.Cell"/>.</b>
/// <c>Simulation.Cell</c> models the <i>tactical combat</i> grid (terrain,
/// elevation, cover — Phase 5). <see cref="IsoCell"/> models the
/// <i>district navigation</i> grid (who can stand where, who occupies a
/// cell). Two grids, two lifetimes, two systems — deliberately not merged.
/// </para>
///
/// <para>
/// <b>Flat world, MVP.</b> No elevation field — the MVP world is flat
/// (roadmap §0bis, Didier 2026-05-21). The record shape means adding an
/// <c>int Elevation = 0</c> later is purely additive and breaks no call
/// site; we do not write it today (no speculative scaffolding).
/// </para>
///
/// <para>
/// <b>Record, not struct.</b> Same rationale as
/// <see cref="Wayfinders.Client.Simulation.Cell"/>: a district grid is a
/// few hundred to a few thousand cells, allocation pressure is irrelevant,
/// and reference semantics avoid the value-type copy footgun where
/// mutating a returned cell silently updates nothing. Value-equality and
/// <c>with</c>-expressions make <see cref="IsoGrid.SetOccupant"/> concise.
/// </para>
/// </summary>
/// <param name="Coord">
/// Grid coordinate of this cell. Stored on the record so a cell carried
/// out of <see cref="IsoGrid"/> still knows where it lives.
/// </param>
/// <param name="Walkable">
/// True if terrain here is passable. Derived district data — a wall, a
/// building footprint, or water sets this false. Pathfinding (future J5+)
/// reads it; the socle itself does not move anything.
/// </param>
/// <param name="MoveCost">
/// Movement cost to enter this cell, for future pathfinding. MVP default
/// is 1 (uniform terrain). Kept now so the day pathfinding lands the
/// field already exists.
/// </param>
/// <param name="OccupantId">
/// Server / GameState id of the entity standing on this cell, or
/// <c>null</c> if empty. Points at a layer-3 entity (a character pawn, a
/// building anchor); the entity itself is a separate Node, never owned by
/// this record. <c>string</c> (not <c>int</c>) because the wire ids are
/// moving toward UUID-shaped strings — the cell stays opaque to the shape.
/// </param>
public sealed record IsoCell(
    TileCoordinate Coord,
    bool Walkable,
    int MoveCost,
    string? OccupantId)
{
    /// <summary>
    /// A walkable, unoccupied, cost-1 cell at <paramref name="coord"/>.
    /// The most common cell shape — cheaper to read at the call site that
    /// stamps a whole district in a loop than the four-argument
    /// constructor.
    /// </summary>
    public static IsoCell EmptyWalkable(TileCoordinate coord) =>
        new(coord, Walkable: true, MoveCost: 1, OccupantId: null);

    /// <summary>
    /// True if a layer-3 entity can be placed here: terrain is walkable
    /// and no one already occupies the cell. The single placement rule of
    /// the J3-iso socle; richer rules (zone-of-control, faction) land with
    /// their callers, not now.
    /// </summary>
    public bool IsFree => Walkable && OccupantId is null;
}
