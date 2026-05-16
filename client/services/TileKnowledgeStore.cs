using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Runtime store of <see cref="TileKnowledgeLadder"/> per grid cell, slice 1
/// MVP scaffold (M3 / L1 World fondations / livrable 3) extended at slice 2
/// with cycle-next / cycle-previous / set-all-to-Levée debug commands.
/// Holds the <c>Dictionary&lt;GridCoord, TileKnowledgeLadder&gt;</c> backing
/// map, emits a <see cref="KnowledgeChanged"/> signal on every mutation,
/// exposes ladder-walking debug helpers (slice 2 livrable 4 exit criterion).
///
/// <para>
/// <b>Why a Node, not an autoload (slice 1 scope, preserved at slice 2).</b>
/// The store lives as a child of the <see cref="Wayfinders.Client.Components.MapPan2DComponent"/>
/// and dies with it on screen exit. When slice 3+ widens scope to L2/L3
/// (per Varn §6.3 cascade L1→L2→L3) and a save/load lifecycle appears,
/// the store gets promoted to an autoload (or a typed Resource attached
/// to a session-level service). Premature autoloading now would couple
/// the slice 2 scaffold to a lifecycle it doesn't need.
/// </para>
///
/// <para>
/// <b>Persistence shape.</b> The dictionary is keyed by
/// <see cref="GridCoord"/> (Godot-free record struct), value is
/// <see cref="TileKnowledgeLadder"/> (Godot-free enum, integer values
/// pinned). The pair serialises trivially to JSON
/// (<c>{"col":3,"row":7,"state":3}</c> per entry).
/// </para>
///
/// <para>
/// <b>Default-state semantics.</b> Cells absent from the dictionary are
/// treated as <see cref="TileKnowledgeLadder.Inconnue"/> by
/// <see cref="GetState"/>. This keeps the dictionary sparse at startup
/// and lines up with Varn §6.1 "tout le reste à <c>INCONNUE</c>". Only
/// cells whose state has been explicitly mutated occupy a slot.
/// </para>
/// </summary>
public partial class TileKnowledgeStore : Node
{
    /// <summary>
    /// Fired after a cell's state changes via <see cref="SetState"/>,
    /// <see cref="ToggleAtCell"/>, <see cref="CycleNextAtCell"/>, or any
    /// of the bulk debug helpers. The fog renderer subscribes once at
    /// <c>_Ready</c> and refreshes the matching Sprite2D's visibility on
    /// each emission. Disconnection discipline is the consumer's
    /// responsibility (Rune-coaching Risk #1, signal-leak trap).
    /// </summary>
    /// <param name="col">Zero-indexed grid column of the changed cell.</param>
    /// <param name="row">Zero-indexed grid row of the changed cell.</param>
    /// <param name="newState">Integer cast of the new <see cref="TileKnowledgeLadder"/>
    /// (Godot's <c>[Signal]</c> binding does not accept enum types directly
    /// in 4.6 ; the consumer casts back at the receiver edge).</param>
    [Signal]
    public delegate void KnowledgeChangedEventHandler(int col, int row, int newState);

    private readonly Dictionary<GridCoord, TileKnowledgeLadder> _states = new();

    /// <summary>
    /// Read the knowledge state for a cell. Cells that have never been
    /// mutated return <see cref="TileKnowledgeLadder.Inconnue"/> — the
    /// canonical default per Varn §6.1.
    /// </summary>
    public TileKnowledgeLadder GetState(GridCoord coord) =>
        _states.TryGetValue(coord, out var state) ? state : TileKnowledgeLadder.Inconnue;

    /// <summary>
    /// Set the knowledge state for a cell and emit
    /// <see cref="KnowledgeChanged"/> if the value actually changes.
    /// Idempotent: setting a cell to its current state is a silent
    /// no-op (no signal fired, no allocation).
    /// </summary>
    public void SetState(GridCoord coord, TileKnowledgeLadder newState)
    {
        var current = GetState(coord);
        if (current == newState) return;

        if (newState == TileKnowledgeLadder.Inconnue)
        {
            // Keep the dictionary sparse — Inconnue is the implicit
            // default, no need to occupy a slot.
            _states.Remove(coord);
        }
        else
        {
            _states[coord] = newState;
        }
        EmitSignal(SignalName.KnowledgeChanged, coord.Col, coord.Row, (int)newState);
    }

    /// <summary>
    /// Slice 1 toggle helper, retained at slice 2 for back-compat.
    /// Cycles between <see cref="TileKnowledgeLadder.Inconnue"/> and
    /// <see cref="TileKnowledgeLadder.Levee"/>. Slice 2 debug surface uses
    /// <see cref="CycleNextAtCell"/> / <see cref="CyclePreviousAtCell"/>
    /// instead.
    /// </summary>
    public void ToggleAtCell(GridCoord coord)
    {
        var next = TileKnowledgeLadderHelpers.Toggle(GetState(coord));
        SetState(coord, next);
    }

    /// <summary>
    /// Slice 2 livrable 4 — bare F key handler. Walks the cell at
    /// <paramref name="coord"/> one step forward through the ladder
    /// (<c>Inconnue → Pressentie → Esquissée → Levée → Scellée → Inconnue</c>).
    /// </summary>
    public void CycleNextAtCell(GridCoord coord)
    {
        var next = TileKnowledgeLadderHelpers.CycleNext(GetState(coord));
        SetState(coord, next);
    }

    /// <summary>
    /// Slice 2 livrable 4 — Shift+F handler. Walks the cell at
    /// <paramref name="coord"/> one step backward through the ladder.
    /// </summary>
    public void CyclePreviousAtCell(GridCoord coord)
    {
        var next = TileKnowledgeLadderHelpers.CyclePrevious(GetState(coord));
        SetState(coord, next);
    }

    /// <summary>
    /// Snapshot the non-default entries for serialisation.
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
    /// Bulk reset for the debug command's "wipe and reseed" path
    /// (Ctrl+F at slice 2). Snapshot keys before mutating because
    /// <see cref="SetState"/>'s remove path would otherwise invalidate
    /// the live iterator.
    /// </summary>
    public void Clear()
    {
        var coords = new List<GridCoord>(_states.Keys);
        foreach (var coord in coords)
        {
            SetState(coord, TileKnowledgeLadder.Inconnue);
        }
    }

    /// <summary>
    /// Slice 2 livrable 4 — Alt+F handler. Flip every cell of the grid
    /// to <see cref="TileKnowledgeLadder.Levee"/>. Iterates the supplied
    /// dimensions rather than the dictionary (which is sparse and would
    /// miss cells still at the default Inconnue) so the operation is
    /// deterministic regardless of how many cells have already been
    /// touched. Single signal emission per cell that actually changes
    /// state — cells already at Levée are silent no-ops via the
    /// idempotence guard in <see cref="SetState"/>.
    ///
    /// <para>
    /// <b>Performance note (livrable 3 acceptance — 50-100 cells animating
    /// simultaneously).</b> The 510-cell grid emits one
    /// <see cref="KnowledgeChanged"/> per cell that flips, which the
    /// renderer translates into one tween per cell. The renderer caps
    /// active tweens by killing any prior tween on the same cell before
    /// scheduling a new one, so even an Alt+F repeated rapidly does not
    /// stack.
    /// </para>
    /// </summary>
    public void SetAllToLevee(GridDimensions dimensions)
    {
        foreach (var coord in FogTileGridLogic.EnumerateCells(dimensions))
        {
            SetState(coord, TileKnowledgeLadder.Levee);
        }
    }

    /// <summary>
    /// Cell-count metric for diagnostics.
    /// </summary>
    public int NonDefaultEntryCount => _states.Count;
}

/// <summary>
/// Serialisation row for one non-default tile state. Plain record (no
/// Godot dependency, no <c>JsonPropertyName</c> attribute yet) so the
/// persistence story stays unblocked when the contract lands.
/// </summary>
/// <param name="Col">Grid column of the cell.</param>
/// <param name="Row">Grid row of the cell.</param>
/// <param name="State">Integer cast of <see cref="TileKnowledgeLadder"/>.</param>
public readonly record struct TileKnowledgeEntry(int Col, int Row, int State);
