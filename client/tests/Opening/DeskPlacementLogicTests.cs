using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Services.Iso;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskPlacementLogic"/> — the J3c-2
/// placement-intent verdict: given a "move this member to this cell"
/// intent, decide whether the authority may honour it.
///
/// <para>
/// <b>What this suite owns.</b> The five verdicts of the closed
/// <see cref="DeskPlacementLogic.PlacementVerdict"/> set:
/// <list type="bullet">
///   <item><c>Accepted</c> — a free, walkable, on-grid target.</item>
///   <item><c>NoOp</c> — the target is the member's own current
///     cell.</item>
///   <item><c>OffGrid</c> — the target cell does not exist.</item>
///   <item><c>NotOccupiable</c> — the target carries a wall
///     (<c>IsoCell.Walkable</c> false).</item>
///   <item><c>Occupied</c> — another member already stands there.</item>
/// </list>
/// The Godot-bound side (<c>GameState.RequestCompanyMemberPlacement</c>
/// mutating the grid and emitting <c>CompanyMemberPlacementConfirmed</c>,
/// the <c>DeskCompanyPawn</c> tweening) is validated via the
/// <c>GameScreen.tscn</c> F6 smoke.
/// </para>
/// </summary>
public sealed class DeskPlacementLogicTests
{
    /// <summary>
    /// A 4×4 all-walkable desk grid with the given cells pre-occupied —
    /// the J3c-2 placeholder desk shape (the real desk grid is also
    /// all-walkable; the wall case is exercised by
    /// <see cref="GridWithWallAt"/>).
    /// </summary>
    private static IsoGrid WalkableGridWith(
        params (int col, int row, string member)[] occupied)
    {
        var grid = IsoGrid.BuildRectangle(4, 4);
        foreach (var (col, row, member) in occupied)
        {
            grid.SetOccupant(new TileCoordinate(col, row), member);
        }
        return grid;
    }

    /// <summary>
    /// A 4×4 desk grid where one cell carries a wall (non-walkable) — so
    /// the <c>NotOccupiable</c> rule is enforced the day a real desk grid
    /// carries walls, even though the J3c-2 placeholder grid does not.
    /// </summary>
    private static IsoGrid GridWithWallAt(int wallCol, int wallRow)
    {
        var cells = new List<IsoCell>();
        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                var coord = new TileCoordinate(col, row);
                bool isWall = col == wallCol && row == wallRow;
                cells.Add(new IsoCell(
                    coord,
                    Walkable: !isWall,
                    MoveCost: 1,
                    OccupantId: null));
            }
        }
        return new IsoGrid(cells);
    }

    [Fact]
    public void A_free_walkable_on_grid_target_is_accepted()
    {
        var grid = WalkableGridWith((1, 1, "company-slot-0"));

        var verdict = DeskPlacementLogic.Evaluate(
            grid, "company-slot-0", new TileCoordinate(2, 2));

        Assert.Equal(DeskPlacementLogic.PlacementVerdict.Accepted, verdict);
    }

    [Fact]
    public void Moving_onto_the_members_own_cell_is_a_no_op()
    {
        // The member stands on (1,1) and the intent targets (1,1) — the
        // move moves nothing. NoOp, not Occupied: the cell IS occupied,
        // but by this same member.
        var grid = WalkableGridWith((1, 1, "company-slot-0"));

        var verdict = DeskPlacementLogic.Evaluate(
            grid, "company-slot-0", new TileCoordinate(1, 1));

        Assert.Equal(DeskPlacementLogic.PlacementVerdict.NoOp, verdict);
    }

    [Fact]
    public void A_target_occupied_by_another_member_is_rejected()
    {
        // (2,2) is taken by slot-1; slot-0 cannot stack on it.
        var grid = WalkableGridWith(
            (1, 1, "company-slot-0"),
            (2, 2, "company-slot-1"));

        var verdict = DeskPlacementLogic.Evaluate(
            grid, "company-slot-0", new TileCoordinate(2, 2));

        Assert.Equal(DeskPlacementLogic.PlacementVerdict.Occupied, verdict);
    }

    [Fact]
    public void An_off_grid_target_is_rejected()
    {
        var grid = WalkableGridWith((1, 1, "company-slot-0"));

        var verdict = DeskPlacementLogic.Evaluate(
            grid, "company-slot-0", new TileCoordinate(99, -3));

        Assert.Equal(DeskPlacementLogic.PlacementVerdict.OffGrid, verdict);
    }

    [Fact]
    public void A_wall_cell_is_not_occupiable()
    {
        // (3,0) carries a wall — not walkable, so not occupiable, even
        // though it is free.
        var grid = GridWithWallAt(3, 0);

        var verdict = DeskPlacementLogic.Evaluate(
            grid, "company-slot-0", new TileCoordinate(3, 0));

        Assert.Equal(
            DeskPlacementLogic.PlacementVerdict.NotOccupiable, verdict);
    }

    [Fact]
    public void A_free_walkable_cell_on_a_grid_that_has_walls_is_accepted()
    {
        // The wall is at (3,0); placing on (1,2) is still fine.
        var grid = GridWithWallAt(3, 0);

        var verdict = DeskPlacementLogic.Evaluate(
            grid, "company-slot-0", new TileCoordinate(1, 2));

        Assert.Equal(DeskPlacementLogic.PlacementVerdict.Accepted, verdict);
    }

    [Fact]
    public void Occupancy_is_checked_before_terrain()
    {
        // A cell that is BOTH occupied by another member AND would be a
        // wall: occupancy wins the verdict (the player sees "taken", and
        // the rejection is the same outcome either way). Pin the
        // precedence so a refactor cannot flip it to NotOccupiable.
        var cells = new List<IsoCell>
        {
            new(new TileCoordinate(0, 0), Walkable: true, MoveCost: 1, OccupantId: null),
            // (1,0) is a wall AND occupied — degenerate but pins precedence.
            new(new TileCoordinate(1, 0), Walkable: false, MoveCost: 1,
                OccupantId: "company-slot-9"),
        };
        var grid = new IsoGrid(cells);

        var verdict = DeskPlacementLogic.Evaluate(
            grid, "company-slot-0", new TileCoordinate(1, 0));

        Assert.Equal(DeskPlacementLogic.PlacementVerdict.Occupied, verdict);
    }

    [Fact]
    public void Evaluate_rejects_a_null_grid()
    {
        Assert.Throws<System.ArgumentNullException>(
            () => DeskPlacementLogic.Evaluate(
                null!, "company-slot-0", new TileCoordinate(0, 0)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Evaluate_rejects_a_missing_member_id(string? badMemberId)
    {
        var grid = WalkableGridWith();

        // ThrowsAny: null routes through ArgumentNullException (a subtype
        // of ArgumentException), empty through ArgumentException itself.
        Assert.ThrowsAny<System.ArgumentException>(
            () => DeskPlacementLogic.Evaluate(
                grid, badMemberId!, new TileCoordinate(0, 0)));
    }
}
