using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Runtime store of <see cref="TileKnowledgeState"/> per grid cell, slice 1
/// MVP scaffold (M3 / L1 World fondations / livrable 3). Holds the
/// <c>Dictionary&lt;GridCoord, TileKnowledgeState&gt;</c> backing map,
/// emits a <see cref="KnowledgeChanged"/> signal on every mutation,
/// exposes a debug toggle (livrable 3 exit criterion).
///
/// <para>
/// <b>Why a Node, not an autoload (slice 1 scope).</b> The slice scope is
/// E2WorldMap only — not E3, not E5, not future tactical scenes — so the
/// store lives as a child of the <see cref="Wayfinders.Client.Components.MapPan2DComponent"/>
/// and dies with it on screen exit. When slice 2+ widens scope to L2/L3
/// (per Varn §6.3 cascade L1→L2→L3) and a save/load lifecycle appears,
/// the store gets promoted to an autoload (or a typed Resource attached
/// to a session-level service). Premature autoloading now would couple
/// the slice 1 scaffold to a lifecycle it doesn't need.
/// </para>
///
/// <para>
/// <b>Persistence shape (slice 1 schema).</b> The dictionary is keyed by
/// <see cref="GridCoord"/> (Godot-free record struct), value is
/// <see cref="TileKnowledgeState"/> (Godot-free enum, integer values
/// pinned). The pair serialises trivially to JSON
/// (<c>{"col":3,"row":7,"state":3}</c> per entry) so the slice 2 save
/// system has a stable shape from day one. The actual save/load is out
/// of slice 1 scope ; <see cref="ToSerialisableEntries"/> exposes the
/// list shape that a <c>System.Text.Json</c> source-gen context would
/// consume when slice 2 lands.
/// </para>
///
/// <para>
/// <b>Default-state semantics.</b> Cells absent from the dictionary are
/// treated as <see cref="TileKnowledgeState.Inconnue"/> by
/// <see cref="GetState"/>. This keeps the dictionary sparse at startup
/// (no need to populate 510 entries with the same default) and lines
/// up with Varn §6.1 "tout le reste à <c>INCONNUE</c>". Only cells
/// whose state has been explicitly mutated via <see cref="SetState"/>
/// occupy a slot.
/// </para>
/// </summary>
public partial class TileKnowledgeStore : Node
{
    /// <summary>
    /// Fired after a cell's state changes via <see cref="SetState"/> or
    /// <see cref="ToggleAtCell"/>. The fog renderer subscribes once at
    /// <c>_Ready</c> and refreshes the matching Sprite2D's visibility on
    /// each emission. Disconnection discipline is the consumer's
    /// responsibility (Rune-coaching Risk #1, signal-leak trap).
    /// </summary>
    /// <param name="col">Zero-indexed grid column of the changed cell.</param>
    /// <param name="row">Zero-indexed grid row of the changed cell.</param>
    /// <param name="newState">Integer cast of the new <see cref="TileKnowledgeState"/>
    /// (Godot's <c>[Signal]</c> binding does not accept enum types directly
    /// in 4.6 ; the consumer casts back at the receiver edge).</param>
    [Signal]
    public delegate void KnowledgeChangedEventHandler(int col, int row, int newState);

    private readonly Dictionary<GridCoord, TileKnowledgeState> _states = new();

    /// <summary>
    /// Read the knowledge state for a cell. Cells that have never been
    /// mutated return <see cref="TileKnowledgeState.Inconnue"/> — the
    /// canonical default per Varn §6.1.
    /// </summary>
    public TileKnowledgeState GetState(GridCoord coord) =>
        _states.TryGetValue(coord, out var state) ? state : TileKnowledgeState.Inconnue;

    /// <summary>
    /// Set the knowledge state for a cell and emit
    /// <see cref="KnowledgeChanged"/> if the value actually changes.
    /// Idempotent: setting a cell to its current state is a silent
    /// no-op (no signal fired, no allocation). Mirrors the
    /// <see cref="GameSettings.SetMapPanButton"/> idempotence pattern
    /// from P8.3 — gratuitous signals are a renderer-frame tax we can
    /// dodge for free.
    /// </summary>
    public void SetState(GridCoord coord, TileKnowledgeState newState)
    {
        var current = GetState(coord);
        if (current == newState) return;

        if (newState == TileKnowledgeState.Inconnue)
        {
            // Keep the dictionary sparse — Inconnue is the implicit
            // default, no need to occupy a slot. This also means a
            // future "reset to default" path simply Removes the entry.
            _states.Remove(coord);
        }
        else
        {
            _states[coord] = newState;
        }
        EmitSignal(SignalName.KnowledgeChanged, coord.Col, coord.Row, (int)newState);
    }

    /// <summary>
    /// Toggle the knowledge state at a cell between
    /// <see cref="TileKnowledgeState.Inconnue"/> and
    /// <see cref="TileKnowledgeState.Levee"/>. Slice 1 livrable 3 exit
    /// criterion: bound to the F key in the runtime debug surface, the
    /// only path that demonstrates the state machine + signal +
    /// renderer end-to-end without slice 2's intermediate states.
    /// </summary>
    public void ToggleAtCell(GridCoord coord)
    {
        var next = TileKnowledgeStateHelpers.Toggle(GetState(coord));
        SetState(coord, next);
    }

    /// <summary>
    /// Snapshot the non-default entries for serialisation. Cells in the
    /// implicit-Inconnue default are omitted. Returned as a list of
    /// records so the slice 2 save system can pipe straight through a
    /// <c>System.Text.Json</c> source-gen context (AOT-safe per the L5a
    /// audit on the client csproj).
    /// </summary>
    public List<TileKnowledgeEntry> ToSerialisableEntries()
    {
        var entries = new List<TileKnowledgeEntry>(_states.Count);
        foreach (var (coord, state) in _states)
        {
            entries.Add(new TileKnowledgeEntry(coord.Col, coord.Row, (int)state));
        }
        return entries;
    }

    /// <summary>
    /// Bulk reset for the slice 1 debug command's "wipe and reseed"
    /// path (handy when iterating the placeholder asset). Single
    /// signal emission per cell, in row-major order, so renderers
    /// see deterministic ordering during a wipe.
    /// </summary>
    public void Clear()
    {
        // Snapshot the keys before mutating ; emitting from inside an
        // iterator over the live dictionary would throw on the first
        // Remove call. Same rule as P8.3 SettingsChanged disconnect.
        var coords = new List<GridCoord>(_states.Keys);
        foreach (var coord in coords)
        {
            SetState(coord, TileKnowledgeState.Inconnue);
        }
    }

    /// <summary>
    /// Cell-count metric for diagnostics. The fog renderer logs this at
    /// preflight so a regression that nukes the state on screen reload
    /// shows up in the console without a debugger session — same
    /// instrumentation reflex as <c>E2WorldMap.DumpInputAndCameraState</c>.
    /// </summary>
    public int NonDefaultEntryCount => _states.Count;
}

/// <summary>
/// Serialisation row for one non-default tile state. Slice 1 introduces
/// the schema ; slice 2 wires it to a <c>System.Text.Json</c> source-gen
/// context once a save/load lifecycle exists. Kept as a plain record
/// (no Godot dependency, no <c>JsonPropertyName</c> attribute yet) so
/// the persistence story stays unblocked when the contract lands.
/// </summary>
/// <param name="Col">Grid column of the cell.</param>
/// <param name="Row">Grid row of the cell.</param>
/// <param name="State">Integer cast of <see cref="TileKnowledgeState"/>.</param>
public readonly record struct TileKnowledgeEntry(int Col, int Row, int State);
