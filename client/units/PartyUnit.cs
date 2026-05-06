using Godot;

namespace Wayfinders.Client.Units;

/// <summary>
/// Sim-side identity for a tactical actor on the grid (Phase 5 L5).
///
/// <para>
/// <b>Why a new type instead of reusing <see cref="Wayfinders.Client.Services.Dtos.UnitDto"/>.</b>
/// <see cref="Wayfinders.Client.Services.Dtos.UnitDto"/> is the wire shape — what FastAPI returns,
/// what <c>System.Text.Json</c> source-gen deserializes, what the
/// roster screen renders. <see cref="PartyUnit"/> is what the tactical
/// scene puts on a cell: an Id, a name, a position. Different concerns,
/// different lifetimes (the wire DTO is per-fetch, the party unit lives
/// for the mission). Collapsing them would re-couple Phase 5 to Phase 3
/// and force every tactical change through the HTTP boundary. The
/// L6+ flow is "fetch DTOs → map to PartyUnits with mission-assigned
/// positions"; that mapper has not been written yet because L5 hardcodes
/// the trio.
/// </para>
///
/// <para>
/// <b>Why a new type instead of reusing <see cref="Wayfinders.Client.Data.Units.UnitData"/>.</b>
/// <see cref="Wayfinders.Client.Data.Units.UnitData"/> is the designer-facing
/// <see cref="Resource"/> shape: <c>.tres</c>-authorable, used by the
/// scene-tree <see cref="Unit"/> actor to wire its components. Its
/// concerns are <c>MaxHp</c>, <c>BaseSpeed</c>, <c>Portrait</c> — the
/// stats a designer cares about. <see cref="PartyUnit"/>'s concern is
/// "who is this and where is it standing right now" — the runtime
/// tactical question. Different concerns again. The L6+ flow will hold
/// both: each <see cref="PartyUnit"/> will reference a
/// <see cref="Wayfinders.Client.Data.Units.UnitData"/> for its stats
/// once stats become tactical-relevant (movement budget, HP rendering).
/// </para>
///
/// <para>
/// <b>Iso coordinate convention.</b> <see cref="Position"/> is a
/// <see cref="Vector2I"/> in simulation grid space — the same coord
/// system <see cref="Wayfinders.Client.Simulation.SimulationGrid"/> and
/// <see cref="Wayfinders.Client.Scenes.PathfindingService"/> speak. With
/// the locked <c>TileLayout = DiamondDown</c>, that integer cell coord
/// projects to screen pixel <c>((x - y) * TW/2, (x + y) * TH/2)</c> via
/// <see cref="TileMapLayer.MapToLocal"/>. The simulation does not need
/// to know about pixels; the renderer handles the projection.
/// </para>
///
/// <para>
/// <b>Record, not class.</b> Same rationale as
/// <see cref="Wayfinders.Client.Simulation.Cell"/>: value-equality is
/// useful for tests and diffs, <c>init</c>-only properties prevent
/// accidental mutation, <c>with</c>-expressions give us cheap "this
/// unit but at a new position" updates for L6+ movement. AOT-clean
/// (no reflection on this type).
/// </para>
/// </summary>
/// <param name="Id">
/// Stable identifier. Server-issued in production (UUID-shaped on the
/// FastAPI side); hardcoded for the L5 placeholder trio. Treat as opaque.
/// Matches the shape of <see cref="Wayfinders.Client.Simulation.Cell.OccupantId"/>
/// — that field stores the unit standing on a cell, cross-referenced
/// against this Id.
/// </param>
/// <param name="DisplayName">
/// Human-readable name for UI and logs. Pratchett-flavored canonical
/// trio for the slice: Kira, Brann, Edda. Mira's portrait set will key
/// off this name when sprites land.
/// </param>
/// <param name="Position">
/// Current cell on the simulation grid. Must point at a passable, empty
/// cell at spawn time; the spawn flow in
/// <see cref="Wayfinders.Client.Scenes.TacticalScene"/> writes the
/// <see cref="Wayfinders.Client.Simulation.Cell.OccupantId"/> on this
/// cell to keep the two sides of the visual/simulation split in sync.
/// </param>
public sealed record PartyUnit(
    string Id,
    string DisplayName,
    Vector2I Position
);
