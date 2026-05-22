using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# click-resolution for the J3c-2 interactive desk: given the
/// cell a click landed on and the desk's occupancy snapshot, decide
/// which entity (if any) the click selects (roadmap milestone J3c-2,
/// Rune, 2026-05-22).
///
/// <para>
/// <b>Hit-test is by CELL, not by sprite pixel — and that is Varn
/// decision #4.</b> The roadmap's "hit-test pion = Area2D ou hit-test
/// sprite" line is one valid shape; the desk picks the other, and
/// deliberately. The pawns are layer-3 occupants of an iso grid: one
/// pawn stands on exactly one cell, and the desk-world click has
/// already been resolved to a cell by
/// <c>IsoProjection.WorldToCell</c>. So "which pawn did the player
/// click" is just "which pawn occupies that cell" — a dictionary
/// lookup. This is not a shortcut; it is the right model for an iso
/// board, and it gives Varn decision #4 for free:
/// <list type="bullet">
///   <item><b>Routing by type priority.</b> If two entity kinds ever
///     share a cell (a pawn standing in a building's footprint cell),
///     the selectable character wins over the house — the priority is
///     resolved here by entity kind, not by Y-sort. The desk in J3c-2
///     has one kind (Company pawns), so the priority list is a single
///     entry; the seam is in place for the day a second kind lands on
///     a desk cell.</item>
///   <item><b>A partially-occluded pawn stays clickable.</b> A
///     sprite-pixel hit-test would miss a pawn whose body is drawn
///     behind another pawn or behind a building; a cell hit-test does
///     not — the pawn owns its cell whatever overlaps its pixels on
///     screen. Varn decision #4 calls this out by name ("un perso même
///     partiellement masqué reste cliquable"); the cell model satisfies
///     it structurally.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>This helper does NOT mutate selection.</b> It answers "what would
/// this click select" — a pure query. The authoritative selection
/// state lives in <c>GameState</c> (NPC-autonomy lock 2026-05-09: the
/// client <c>GameState</c> is the authority). <c>GameScreen</c> takes
/// this helper's answer and calls <c>GameState</c>, which emits the
/// selection signal; the pawn view reacts to the signal, it never
/// decides its own selected state.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Cell coords are
/// <see cref="TileCoordinate"/>, occupant ids are <c>string</c> — no
/// <c>Godot.Vector2I</c>, no <c>Node</c>. Cherry-pickable into the
/// xUnit host, same discipline as <see cref="DeskInputRoutingLogic"/>.
/// </para>
/// </summary>
public static class DeskSelectionLogic
{
    /// <summary>
    /// Resolve which Company member a desk click selects.
    ///
    /// <para>
    /// The desk-world click has already been converted to a cell. This
    /// looks the cell up in the occupancy snapshot and returns the
    /// occupant's member id, or <c>null</c> if the cell is empty (a
    /// click on bare desk floor) or off the desk grid. A <c>null</c>
    /// result is a legitimate "clicked nothing" — the caller treats it
    /// as a deselect, not an error.
    /// </para>
    /// </summary>
    /// <param name="clickedCell">
    /// The cell the click landed on, from
    /// <c>IsoProjection.WorldToCell</c> applied to the desk-world point
    /// <see cref="DeskInputRoutingLogic.ScreenToDeskWorld"/> produced.
    /// </param>
    /// <param name="cellOccupants">
    /// The desk occupancy snapshot: cell → member id of the pawn
    /// standing on it. The authoritative copy is the desk grid held by
    /// <c>GameState</c>; this is read-only here.
    /// </param>
    /// <returns>
    /// The selected member id, or <c>null</c> when the click hit no
    /// pawn.
    /// </returns>
    public static string? ResolveClickedMember(
        TileCoordinate clickedCell,
        IReadOnlyDictionary<TileCoordinate, string> cellOccupants)
    {
        System.ArgumentNullException.ThrowIfNull(cellOccupants);

        return cellOccupants.TryGetValue(clickedCell, out var memberId)
            ? memberId
            : null;
    }

    /// <summary>
    /// Decide the next authoritative selection given the current
    /// selection and what a click resolved to. Encodes the desk's
    /// selection rule as a pure transition so the toggle / deselect
    /// behaviour is xUnit-pinned rather than hidden in an event handler:
    /// <list type="bullet">
    ///   <item>Click a pawn that is not selected → select it.</item>
    ///   <item>Click the already-selected pawn → deselect it (toggle
    ///     off — clicking a piece twice releases it, the familiar
    ///     board-game gesture).</item>
    ///   <item>Click bare floor (<paramref name="clickedMemberId"/> is
    ///     <c>null</c>) → deselect whatever was selected.</item>
    /// </list>
    /// </summary>
    /// <param name="currentSelection">
    /// The currently selected member id, or <c>null</c> if nothing is
    /// selected.
    /// </param>
    /// <param name="clickedMemberId">
    /// The member id <see cref="ResolveClickedMember"/> returned, or
    /// <c>null</c> for a click on bare floor.
    /// </param>
    /// <returns>
    /// The member id that should be selected after the click, or
    /// <c>null</c> if the click results in nothing selected. The caller
    /// compares this against <paramref name="currentSelection"/> and
    /// only writes <c>GameState</c> / fires the signal when they
    /// differ — idempotent, same no-op-suppression discipline as
    /// <c>GameState.SetSelectedNpcId</c>.
    /// </returns>
    public static string? NextSelection(
        string? currentSelection,
        string? clickedMemberId)
    {
        if (clickedMemberId is null)
        {
            // Clicked bare floor — deselect.
            return null;
        }

        if (clickedMemberId == currentSelection)
        {
            // Clicked the already-selected pawn — toggle off.
            return null;
        }

        // Clicked a different pawn — select it.
        return clickedMemberId;
    }
}
