using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Services.Iso;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# placement-intent evaluation for the J3c-2 interactive desk:
/// given a "place this member on this cell" intent, decide whether the
/// authoritative <c>GameState</c> may honour it (roadmap milestone
/// J3c-2, Rune, 2026-05-22).
///
/// <para>
/// <b>Where this sits in the tactical-intent flow (NPC-autonomy lock
/// 2026-05-09).</b> A desk click that targets a destination cell is a
/// <i>tactical intent</i>, not a relocation. The flow is strict:
/// <list type="number">
///   <item>The player clicks a destination cell while a pawn is
///     selected. <c>GameScreen</c> emits
///     <c>CompanyMemberPlacementRequested(memberId, targetCol,
///     targetRow)</c> — an <i>intent signal</i>. The pawn does not
///     move.</item>
///   <item><c>GameState</c> (the authority) receives the intent and
///     calls <see cref="Evaluate"/> against its own desk grid.</item>
///   <item>If the verdict is <see cref="PlacementVerdict.Accepted"/>,
///     <c>GameState</c> mutates the grid (clears the source cell's
///     <c>OccupantId</c>, sets the target cell's) and emits a
///     <i>confirmation</i> signal.</item>
///   <item>Only then does the pawn view animate toward the new cell —
///     in reaction to the confirmation, never to the click.</item>
/// </list>
/// This helper owns step 2's decision. It is a pure function: it reads
/// the grid, it never writes it. The write is <c>GameState</c>'s, and
/// it happens through <c>IsoGrid.SetOccupant</c>, which gates placement
/// validity a second time — belt and braces, the authority does not
/// trust an intent it did not re-check.
/// </para>
///
/// <para>
/// <b>The placement rule (roadmap §0bis + J3c-2 spec).</b> A pawn may
/// occupy a target cell when all of:
/// <list type="bullet">
///   <item>The target cell exists on the desk grid (off-grid clicks are
///     rejected — you cannot place a pawn outside the board).</item>
///   <item>The target cell is occupiable — <c>IsoCell.Walkable</c> is
///     true. A cell carrying a wall is not occupiable. (J3c-2 desk
///     placeholder grid is all-walkable; the wall case is pinned by
///     tests against a hand-built grid so the rule is enforced the day
///     a real desk grid carries walls.)</item>
///   <item>The target cell is free — no other member already occupies
///     it. A second pawn cannot stack on an occupied cell.</item>
/// </list>
/// A move onto the pawn's <i>own</i> current cell is a degenerate
/// no-op: it is reported as <see cref="PlacementVerdict.NoOp"/> so the
/// caller can skip the grid mutation and the confirmation signal
/// entirely (no spurious "moved" event for a move that moved nothing).
/// </para>
///
/// <para>
/// <b>Why a verdict enum, not a bool.</b> "Rejected" and "no-op" are
/// different outcomes — a rejected intent may want a UI nudge ("that
/// cell is taken"), a no-op is silent. A bare bool would collapse them.
/// Same rationale as a <c>Result</c>-shaped return: the caller branches
/// on a closed set, the compiler checks the switch.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Reads <see cref="IsoGrid"/> /
/// <see cref="IsoCell"/> / <see cref="TileCoordinate"/> — the Godot-free
/// logic layer. Cherry-pickable into the xUnit host, same discipline as
/// <see cref="DeskInputRoutingLogic"/> and <see cref="DeskSelectionLogic"/>.
/// </para>
/// </summary>
public static class DeskPlacementLogic
{
    /// <summary>
    /// The outcome of evaluating a placement intent. A closed set —
    /// callers <c>switch</c> on it and the compiler checks the arms.
    /// </summary>
    public enum PlacementVerdict
    {
        /// <summary>
        /// The intent is valid: the authority may move the member to
        /// the target cell and emit a confirmation. The target exists,
        /// is occupiable, and is free.
        /// </summary>
        Accepted,

        /// <summary>
        /// The target cell is the member's current cell — the move
        /// moves nothing. The caller skips the grid mutation and the
        /// confirmation signal.
        /// </summary>
        NoOp,

        /// <summary>
        /// The target cell does not exist on the desk grid (an off-grid
        /// click). The authority does nothing.
        /// </summary>
        OffGrid,

        /// <summary>
        /// The target cell exists but is not occupiable — it carries a
        /// wall (<c>IsoCell.Walkable</c> is false). The authority does
        /// nothing.
        /// </summary>
        NotOccupiable,

        /// <summary>
        /// The target cell exists and is occupiable but another member
        /// already stands on it. The authority does nothing — pawns do
        /// not stack.
        /// </summary>
        Occupied,
    }

    /// <summary>
    /// Evaluate a placement intent against the desk grid.
    /// </summary>
    /// <param name="grid">
    /// The desk's layer-2 logical grid — the authoritative copy held by
    /// <c>GameState</c>. Read only here.
    /// </param>
    /// <param name="movingMemberId">
    /// The member the intent wants to move. Used to detect the
    /// move-onto-own-cell no-op: if the target cell is already occupied
    /// by <i>this</i> member, the verdict is <see cref="PlacementVerdict.NoOp"/>,
    /// not <see cref="PlacementVerdict.Occupied"/>.
    /// </param>
    /// <param name="targetCell">The cell the intent targets.</param>
    /// <returns>The placement verdict.</returns>
    public static PlacementVerdict Evaluate(
        IsoGrid grid,
        string movingMemberId,
        TileCoordinate targetCell)
    {
        System.ArgumentNullException.ThrowIfNull(grid);
        System.ArgumentException.ThrowIfNullOrEmpty(movingMemberId);

        if (!grid.TryGetCell(targetCell, out var cell) || cell is null)
        {
            return PlacementVerdict.OffGrid;
        }

        // The target cell is occupied — by whom decides no-op vs reject.
        if (cell.OccupantId is not null)
        {
            return cell.OccupantId == movingMemberId
                ? PlacementVerdict.NoOp
                : PlacementVerdict.Occupied;
        }

        // The cell is free; the only remaining gate is the terrain.
        return cell.Walkable
            ? PlacementVerdict.Accepted
            : PlacementVerdict.NotOccupiable;
    }
}
