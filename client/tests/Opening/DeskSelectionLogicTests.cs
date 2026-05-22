using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskSelectionLogic"/> — the J3c-2
/// desk-click resolution: which member a clicked cell selects, and the
/// select / toggle-off transition.
///
/// <para>
/// <b>What this suite owns.</b> The cell hit-test (a click on an occupied
/// cell resolves to its member; a click on an empty or off-grid cell
/// resolves to null — Varn decision #4's "hit-test by cell" model), and
/// the selection transition: select a new pawn, toggle off the
/// already-selected pawn, deselect on a bare-floor click. The Godot-bound
/// side (<c>GameScreen._UnhandledInput</c> calling
/// <c>GameState.SetSelectedCompanyMember</c>, the <c>DeskCompanyPawn</c>
/// rendering the highlight off the signal) is validated via the
/// <c>GameScreen.tscn</c> F6 smoke.
/// </para>
/// </summary>
public sealed class DeskSelectionLogicTests
{
    private static IReadOnlyDictionary<TileCoordinate, string> FormationOf(
        params (int col, int row, string member)[] entries)
    {
        var dict = new Dictionary<TileCoordinate, string>();
        foreach (var (col, row, member) in entries)
        {
            dict[new TileCoordinate(col, row)] = member;
        }
        return dict;
    }

    [Fact]
    public void A_click_on_an_occupied_cell_resolves_to_its_member()
    {
        var occupants = FormationOf(
            (3, 3, "company-slot-0"),
            (3, 2, "company-slot-1"));

        var hit = DeskSelectionLogic.ResolveClickedMember(
            new TileCoordinate(3, 2), occupants);

        Assert.Equal("company-slot-1", hit);
    }

    [Fact]
    public void A_click_on_an_empty_cell_resolves_to_null()
    {
        var occupants = FormationOf((3, 3, "company-slot-0"));

        var hit = DeskSelectionLogic.ResolveClickedMember(
            new TileCoordinate(5, 5), occupants);

        // An empty cell is a legitimate "clicked nothing" — null, not a
        // throw. The caller treats it as a deselect / placement target.
        Assert.Null(hit);
    }

    [Fact]
    public void A_click_on_an_off_grid_cell_resolves_to_null()
    {
        var occupants = FormationOf((3, 3, "company-slot-0"));

        var hit = DeskSelectionLogic.ResolveClickedMember(
            new TileCoordinate(-4, 99), occupants);

        Assert.Null(hit);
    }

    [Fact]
    public void Clicking_an_unselected_pawn_selects_it()
    {
        var next = DeskSelectionLogic.NextSelection(
            currentSelection: null,
            clickedMemberId: "company-slot-2");

        Assert.Equal("company-slot-2", next);
    }

    [Fact]
    public void Clicking_a_different_pawn_switches_the_selection()
    {
        var next = DeskSelectionLogic.NextSelection(
            currentSelection: "company-slot-0",
            clickedMemberId: "company-slot-4");

        Assert.Equal("company-slot-4", next);
    }

    [Fact]
    public void Clicking_the_already_selected_pawn_toggles_it_off()
    {
        // Clicking a piece twice releases it — the familiar board-game
        // gesture.
        var next = DeskSelectionLogic.NextSelection(
            currentSelection: "company-slot-3",
            clickedMemberId: "company-slot-3");

        Assert.Null(next);
    }

    [Fact]
    public void Clicking_bare_floor_with_a_selection_deselects()
    {
        var next = DeskSelectionLogic.NextSelection(
            currentSelection: "company-slot-1",
            clickedMemberId: null);

        Assert.Null(next);
    }

    [Fact]
    public void Clicking_bare_floor_with_no_selection_stays_null()
    {
        var next = DeskSelectionLogic.NextSelection(
            currentSelection: null,
            clickedMemberId: null);

        Assert.Null(next);
    }

    [Fact]
    public void ResolveClickedMember_rejects_a_null_occupant_map()
    {
        Assert.Throws<System.ArgumentNullException>(
            () => DeskSelectionLogic.ResolveClickedMember(
                new TileCoordinate(0, 0), null!));
    }
}
