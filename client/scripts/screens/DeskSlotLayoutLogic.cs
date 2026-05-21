using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# layout maths for the J3c-1 static iso desk: where the ~7
/// Company pawns sit on the desk's iso grid. Given nothing but the
/// locked formation shape, it returns the ordered list of
/// <see cref="TileCoordinate"/> slot cells and the slot ↔ cell mapping
/// the Godot-bound <c>GameScreen</c> reads to place one placeholder pawn
/// per slot.
///
/// <para>
/// <b>Why this is its own helper.</b> The desk is the second iso space
/// of the Game Screen Shell (roadmap J3c-1). The <i>placement</i> of the
/// Company formation — which cell is the leader, which cells form the
/// rows behind — is arithmetic over the iso grid, and arithmetic baked
/// into a <c>.tscn</c> or a node's <c>_Ready</c> is invisible until a
/// playtest. Pinning the formation here makes a wrong slot count, a
/// duplicate cell, or a leader that is not foreground surface red in
/// xUnit, the same logic-vs-node split as <see cref="HudLayoutLogic"/>
/// (J3b), <see cref="MapViewportPanLogic"/> (J3a), and
/// <c>IsoProjection</c> (J3-iso).
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> The cell coordinate is a
/// <see cref="TileCoordinate"/> (pure-C# integer pair), never a
/// <c>Godot.Vector2I</c>, so this whole helper is cherry-pickable into
/// the xUnit host that has no GodotSharp — exactly like
/// <see cref="Wayfinders.Client.Services.Iso.IsoCell"/> and
/// <c>AreaGridLogic</c>. The runtime <c>GameScreen</c> converts each
/// <see cref="TileCoordinate"/> to a Godot pixel through the desk
/// board's own <c>IsoProjection</c> at the engine seam.
/// </para>
///
/// <para>
/// <b>Scope — J3c-1 is static.</b> This helper returns a <i>fixed</i>
/// formation. It does not know about selection, drag, or which Company
/// member occupies which slot — that is J3c-2, where the authoritative
/// occupancy lives in <c>GameState</c> (NPC-autonomy lock 2026-05-09:
/// the slot ↔ member binding is game state, not a layout fact). J3c-1
/// only answers "where does the desk draw its placeholder pawns".
/// </para>
///
/// <para>
/// <b>Formation convention — 2:1 iso, leader foreground.</b> The desk
/// board's <c>IsoProjection</c> projects cell <c>(col, row)</c> to a
/// pixel whose screen Y is <c>(col + row) * tileHeight/2</c>: the larger
/// <c>col + row</c>, the <i>lower</i> on screen, i.e. closer to the
/// viewer. The leader (slot 0) therefore sits on the cell with the
/// largest <c>col + row</c> — front-and-centre, foreground-prominent —
/// and the six formation pawns fan out on cells with smaller
/// <c>col + row</c>, reading as "behind" the leader. The screen X spread
/// is <c>(col - row) * tileWidth/2</c>: cells are picked symmetrically
/// around the <c>col == row</c> diagonal so the formation is centred
/// left-to-right on the leader.
/// </para>
/// </summary>
public static class DeskSlotLayoutLogic
{
    /// <summary>
    /// The number of Company slots laid out on the static desk. Seven —
    /// one leader plus a six-pawn formation, matching the mockup. Pinned
    /// by <c>DeskSlotLayoutLogicTests</c> so a drift (a slot added or
    /// dropped) surfaces red, and read by the <c>GameScreen</c> preflight
    /// as the expected count.
    /// </summary>
    public const int CompanySlotCount = 7;

    /// <summary>
    /// Slot index of the Company leader — always slot 0. The leader pawn
    /// renders foreground-prominent (the <c>GameScreen</c> draws it
    /// slightly larger); its cell has the largest <c>col + row</c> so it
    /// projects lowest on screen.
    /// </summary>
    public const int LeaderSlotIndex = 0;

    /// <summary>
    /// The locked Company formation, as iso grid cells, in slot order.
    /// Slot 0 is the leader (front-centre); slots 1–6 are the formation
    /// rows behind it.
    ///
    /// <para>
    /// The cells sit on a small block of the desk grid, kept well clear
    /// of cell (0,0) so the whole formation has positive coordinates and
    /// frames cleanly under the <c>DeskCamera2D</c>. Two rows behind the
    /// leader, symmetric around the <c>col == row</c> diagonal:
    /// <list type="bullet">
    ///   <item>Slot 0 — leader, <c>(3,3)</c>, <c>col+row = 6</c> (lowest
    ///     on screen, foreground).</item>
    ///   <item>Slots 1–3 — middle row, <c>col+row = 5</c> (one step
    ///     back): <c>(3,2)</c> centre-ish, <c>(4,1)</c> and <c>(2,3)</c>
    ///     flanking.</item>
    ///   <item>Slots 4–6 — back row, <c>col+row = 4</c> (two steps
    ///     back): <c>(2,2)</c> centre, <c>(3,1)</c> and <c>(1,3)</c>
    ///     flanking.</item>
    /// </list>
    /// Every cell is distinct; the leader is the unique
    /// largest-<c>col+row</c> cell. Both invariants are xUnit-pinned.
    /// </para>
    /// </summary>
    private static readonly TileCoordinate[] SlotCells =
    {
        new(3, 3), // slot 0 — leader, front-centre
        new(3, 2), // slot 1 — middle row
        new(4, 1), // slot 2 — middle row, right flank
        new(2, 3), // slot 3 — middle row, left flank
        new(2, 2), // slot 4 — back row, centre
        new(3, 1), // slot 5 — back row, right flank
        new(1, 3), // slot 6 — back row, left flank
    };

    /// <summary>
    /// The ordered list of Company slot cells. Index <c>i</c> is the cell
    /// for slot <c>i</c>; slot 0 is the leader. Returns a fresh list each
    /// call so a caller cannot mutate the locked formation.
    /// </summary>
    public static IReadOnlyList<TileCoordinate> CompanySlotCells()
        => new List<TileCoordinate>(SlotCells);

    /// <summary>
    /// The iso grid cell for one slot. Throws
    /// <see cref="System.ArgumentOutOfRangeException"/> for an index
    /// outside <c>[0, <see cref="CompanySlotCount"/>)</c> — a bad slot
    /// index is a caller bug, not a silent default.
    /// </summary>
    public static TileCoordinate CellForSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCells.Length)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(slotIndex),
                $"Slot index {slotIndex} is outside [0, {CompanySlotCount}).");
        }
        return SlotCells[slotIndex];
    }

    /// <summary>
    /// True if <paramref name="slotIndex"/> is the leader slot. The
    /// <c>GameScreen</c> reads this to render the leader pawn
    /// foreground-prominent (a touch larger than the formation pawns).
    /// </summary>
    public static bool IsLeaderSlot(int slotIndex) => slotIndex == LeaderSlotIndex;

    /// <summary>
    /// The integer-rounded centroid cell of a slot block, used to park
    /// the immobile <c>DeskCamera2D</c> so the whole formation is framed.
    /// Pure averaging of the cell coordinates; the desk camera is set
    /// once from this and never moved (the desk does not pan — only the
    /// maquette does, roadmap J3c-1).
    /// </summary>
    /// <param name="cells">
    /// The slot cells to frame. Typically <see cref="CompanySlotCells"/>;
    /// taken as a parameter so the helper stays a pure function and is
    /// trivially testable.
    /// </param>
    public static TileCoordinate SlotBlockCentroidCell(IReadOnlyList<TileCoordinate> cells)
    {
        if (cells is null || cells.Count == 0)
        {
            throw new System.ArgumentException(
                "Cannot take the centroid of an empty slot list.", nameof(cells));
        }

        long sumCol = 0;
        long sumRow = 0;
        foreach (var cell in cells)
        {
            sumCol += cell.Col;
            sumRow += cell.Row;
        }

        // Integer rounding (round-half-up) — the camera is parked on a
        // cell centre, sub-cell precision is not needed.
        int col = (int)((sumCol + cells.Count / 2) / cells.Count);
        int row = (int)((sumRow + cells.Count / 2) / cells.Count);
        return new TileCoordinate(col, row);
    }
}
