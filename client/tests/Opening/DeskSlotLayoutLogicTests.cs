using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskSlotLayoutLogic"/> — the J3c-1 static
/// iso desk slot formation. Pins the contract that decides which iso grid
/// cell each of the ~7 Company pawns sits on, the leader slot, and the
/// centroid the immobile <c>DeskCamera2D</c> is parked on.
///
/// <para>
/// <b>What this suite owns.</b> Only the formation maths: a fixed count
/// of 7 slots, all cells distinct, the leader on the unique
/// largest-<c>col+row</c> cell (so it projects foreground / lowest on
/// screen), and the integer centroid of the slot block. The Godot-bound
/// pawn placement (instancing <c>DeskCompanyPawn</c>, converting cells to
/// pixels through the desk board's <c>IsoProjection</c>) lives in
/// <c>GameScreen.ConfigureDesk</c> and is validated via the
/// <c>GameScreen.tscn</c> F6 smoke.
/// </para>
/// </summary>
public sealed class DeskSlotLayoutLogicTests
{
    [Fact]
    public void Company_has_exactly_seven_slots()
    {
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        Assert.Equal(7, slots.Count);
        Assert.Equal(DeskSlotLayoutLogic.CompanySlotCount, slots.Count);
    }

    [Fact]
    public void Every_slot_cell_is_distinct()
    {
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // A duplicated cell would stack two pawns on the same spot —
        // pin the formation has seven separate cells.
        var unique = new HashSet<TileCoordinate>(slots);
        Assert.Equal(slots.Count, unique.Count);
    }

    [Fact]
    public void Leader_is_slot_zero()
    {
        Assert.Equal(0, DeskSlotLayoutLogic.LeaderSlotIndex);
        Assert.True(DeskSlotLayoutLogic.IsLeaderSlot(0));
        Assert.False(DeskSlotLayoutLogic.IsLeaderSlot(1));
        Assert.False(DeskSlotLayoutLogic.IsLeaderSlot(6));
    }

    [Fact]
    public void Leader_sits_on_the_unique_lowest_on_screen_cell()
    {
        // The 2:1 iso projection puts a cell with larger (col + row)
        // lower on screen — closer to the viewer. The leader must be the
        // single foreground-most pawn, so its cell has the strictly
        // largest col + row of the whole formation.
        var slots = DeskSlotLayoutLogic.CompanySlotCells();
        var leader = slots[DeskSlotLayoutLogic.LeaderSlotIndex];
        int leaderDepth = leader.Col + leader.Row;

        for (int i = 0; i < slots.Count; i++)
        {
            if (i == DeskSlotLayoutLogic.LeaderSlotIndex)
            {
                continue;
            }
            int otherDepth = slots[i].Col + slots[i].Row;
            Assert.True(
                otherDepth < leaderDepth,
                $"Slot {i} (col+row={otherDepth}) is not behind the leader " +
                $"(col+row={leaderDepth}).");
        }
    }

    [Fact]
    public void All_slot_cells_have_non_negative_coordinates()
    {
        // The formation is kept clear of cell (0,0) so it frames cleanly
        // under the desk camera with positive coordinates.
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        foreach (var cell in slots)
        {
            Assert.True(cell.Col >= 0, $"Cell {cell} has a negative column.");
            Assert.True(cell.Row >= 0, $"Cell {cell} has a negative row.");
        }
    }

    [Fact]
    public void CellForSlot_matches_the_slot_list()
    {
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        for (int i = 0; i < slots.Count; i++)
        {
            Assert.Equal(slots[i], DeskSlotLayoutLogic.CellForSlot(i));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(99)]
    public void CellForSlot_rejects_an_out_of_range_index(int badIndex)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => DeskSlotLayoutLogic.CellForSlot(badIndex));
    }

    [Fact]
    public void Centroid_of_the_company_block_is_inside_the_block_bounds()
    {
        var slots = DeskSlotLayoutLogic.CompanySlotCells();
        var centroid = DeskSlotLayoutLogic.SlotBlockCentroidCell(slots);

        // The camera-park centroid must land inside the bounding box of
        // the slot cells so the whole formation is framed.
        int minCol = int.MaxValue, maxCol = int.MinValue;
        int minRow = int.MaxValue, maxRow = int.MinValue;
        foreach (var c in slots)
        {
            minCol = System.Math.Min(minCol, c.Col);
            maxCol = System.Math.Max(maxCol, c.Col);
            minRow = System.Math.Min(minRow, c.Row);
            maxRow = System.Math.Max(maxRow, c.Row);
        }

        Assert.InRange(centroid.Col, minCol, maxCol);
        Assert.InRange(centroid.Row, minRow, maxRow);
    }

    [Fact]
    public void Centroid_of_a_single_cell_is_that_cell()
    {
        var centroid = DeskSlotLayoutLogic.SlotBlockCentroidCell(
            new List<TileCoordinate> { new(5, 7) });

        Assert.Equal(new TileCoordinate(5, 7), centroid);
    }

    [Fact]
    public void Centroid_rejects_an_empty_slot_list()
    {
        Assert.Throws<System.ArgumentException>(
            () => DeskSlotLayoutLogic.SlotBlockCentroidCell(
                new List<TileCoordinate>()));
    }

    [Fact]
    public void CompanySlotCells_returns_a_fresh_list_each_call()
    {
        // The locked formation must not be mutable through the returned
        // list — pin that two calls hand back independent collections.
        var first = DeskSlotLayoutLogic.CompanySlotCells();
        var second = DeskSlotLayoutLogic.CompanySlotCells();

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }
}
