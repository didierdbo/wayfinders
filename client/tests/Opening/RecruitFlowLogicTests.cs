// Pin tests for RecruitFlowLogic (Étape C, Rune 2026-05-23, roadmap J8).
//
// What this file pins :
//   1. ResolveRecruitTarget — happy path : recruit mission with a non-null
//      RecruitTargetNpcId returns the NpcId.
//   2. ResolveRecruitTarget — null returns : null mission, non-recruit
//      type, null/empty RecruitTargetNpcId.
//   3. ResolveRecruitTarget — anti-regression : the function never silently
//      returns a value for a non-recruit type (M2+ scout_route /
//      parley_local missions).
//   4. ResolveNextCompanySlot — empty occupants : returns slot 0 (leader)
//      with the canonical leader cell (3,3).
//   5. ResolveNextCompanySlot — leader occupied : returns slot 1 with its
//      canonical cell (3,2).
//   6. ResolveNextCompanySlot — every slot occupied : returns null.
//   7. ResolveNextCompanySlot — scan order pin : fills slots in declaration
//      order (0, 1, 2, ..., 6), not in cell-index order or any other.
//   8. ResolveNextCompanySlot — null arg guards.

using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

public sealed class RecruitFlowLogicTests
{
    // ----- ResolveRecruitTarget -------------------------------------------

    [Fact]
    public void ResolveRecruitTarget_returns_npc_id_for_recruit_mission()
    {
        var mission = NewRecruitMission(npcId: "kira");

        var result = RecruitFlowLogic.ResolveRecruitTarget(mission);

        Assert.Equal("kira", result);
    }

    [Fact]
    public void ResolveRecruitTarget_returns_null_for_null_mission()
    {
        Assert.Null(RecruitFlowLogic.ResolveRecruitTarget(null));
    }

    [Theory]
    [InlineData(WorldTickWireFormat.MissionType.ScoutRoute)]
    [InlineData(WorldTickWireFormat.MissionType.ParleyLocal)]
    public void ResolveRecruitTarget_returns_null_for_non_recruit_type(string missionType)
    {
        // Even if RecruitTargetNpcId is populated, the type gate refuses
        // to expose it — a scout_route mission has no recruit target by
        // design, populating that field would be a server bug we do not
        // amplify on the client.
        var mission = NewRecruitMission(npcId: "kira") with { Type = missionType };

        var result = RecruitFlowLogic.ResolveRecruitTarget(mission);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveRecruitTarget_returns_null_for_missing_npc_id(string? npcId)
    {
        var mission = NewRecruitMission(npcId: "kira") with { RecruitTargetNpcId = npcId };

        var result = RecruitFlowLogic.ResolveRecruitTarget(mission);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("kira")]
    [InlineData("dorn")]
    [InlineData("vell")]
    public void ResolveRecruitTarget_covers_M1_roster(string npcId)
    {
        // The three M1 recruitable NPCs. Pinned so a roster bump (M2
        // adds new NPCs) goes through this test deliberately.
        var mission = NewRecruitMission(npcId: npcId);

        var result = RecruitFlowLogic.ResolveRecruitTarget(mission);

        Assert.Equal(npcId, result);
    }

    // ----- ResolveNextCompanySlot -----------------------------------------

    [Fact]
    public void ResolveNextCompanySlot_empty_occupants_returns_slot_zero_leader()
    {
        var slotCells = DeskSlotLayoutLogic.CompanySlotCells();
        var occupants = new Dictionary<TileCoordinate, string>();

        var result = RecruitFlowLogic.ResolveNextCompanySlot(slotCells, occupants);

        Assert.NotNull(result);
        Assert.Equal(0, result.SlotIndex);
        Assert.Equal(new TileCoordinate(3, 3), result.Cell);
    }

    [Fact]
    public void ResolveNextCompanySlot_leader_occupied_returns_slot_one()
    {
        var slotCells = DeskSlotLayoutLogic.CompanySlotCells();
        var occupants = new Dictionary<TileCoordinate, string>
        {
            [new TileCoordinate(3, 3)] = "kira", // leader cell
        };

        var result = RecruitFlowLogic.ResolveNextCompanySlot(slotCells, occupants);

        Assert.NotNull(result);
        Assert.Equal(1, result.SlotIndex);
        Assert.Equal(new TileCoordinate(3, 2), result.Cell);
    }

    [Fact]
    public void ResolveNextCompanySlot_every_slot_occupied_returns_null()
    {
        var slotCells = DeskSlotLayoutLogic.CompanySlotCells();
        var occupants = new Dictionary<TileCoordinate, string>();
        for (int i = 0; i < slotCells.Count; i++)
        {
            occupants[slotCells[i]] = $"npc{i}";
        }

        var result = RecruitFlowLogic.ResolveNextCompanySlot(slotCells, occupants);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveNextCompanySlot_scan_order_fills_slots_in_declaration_order()
    {
        // The locked formation order is :
        //   slot 0 = (3,3) — leader
        //   slot 1 = (3,2)
        //   slot 2 = (4,1)
        //   slot 3 = (2,3)
        //   slot 4 = (2,2)
        //   slot 5 = (3,1)
        //   slot 6 = (1,3)
        // Pin that ResolveNextCompanySlot returns them in THIS order when
        // we recruit one at a time. A naive scan that walked cells by
        // (col, row) lexicographic order would resolve to (1,3) first
        // — the formation-order discipline is the load-bearing decision.
        var slotCells = DeskSlotLayoutLogic.CompanySlotCells();
        var occupants = new Dictionary<TileCoordinate, string>();
        var expectedCells = new[]
        {
            new TileCoordinate(3, 3),
            new TileCoordinate(3, 2),
            new TileCoordinate(4, 1),
            new TileCoordinate(2, 3),
            new TileCoordinate(2, 2),
            new TileCoordinate(3, 1),
            new TileCoordinate(1, 3),
        };

        for (int i = 0; i < expectedCells.Length; i++)
        {
            var result = RecruitFlowLogic.ResolveNextCompanySlot(slotCells, occupants);
            Assert.NotNull(result);
            Assert.Equal(i, result.SlotIndex);
            Assert.Equal(expectedCells[i], result.Cell);
            occupants[result.Cell] = $"npc{i}";
        }
    }

    [Fact]
    public void ResolveNextCompanySlot_null_slot_cells_throws()
    {
        var occupants = new Dictionary<TileCoordinate, string>();
        Assert.Throws<System.ArgumentNullException>(() =>
            RecruitFlowLogic.ResolveNextCompanySlot(null!, occupants));
    }

    [Fact]
    public void ResolveNextCompanySlot_null_occupants_throws()
    {
        var slotCells = DeskSlotLayoutLogic.CompanySlotCells();
        Assert.Throws<System.ArgumentNullException>(() =>
            RecruitFlowLogic.ResolveNextCompanySlot(slotCells, null!));
    }

    [Fact]
    public void ResolveNextCompanySlot_skips_occupied_cells_in_middle_of_scan()
    {
        // The authority writes occupancy as we recruit ; if slot 0 + 2
        // are occupied (no slot 1 — a synthetic scenario but defensive),
        // the next free is slot 1, NOT slot 3. The scan does not bail at
        // the first occupied slot.
        var slotCells = DeskSlotLayoutLogic.CompanySlotCells();
        var occupants = new Dictionary<TileCoordinate, string>
        {
            [new TileCoordinate(3, 3)] = "kira", // slot 0
            [new TileCoordinate(4, 1)] = "dorn", // slot 2
        };

        var result = RecruitFlowLogic.ResolveNextCompanySlot(slotCells, occupants);

        Assert.NotNull(result);
        Assert.Equal(1, result.SlotIndex);
        Assert.Equal(new TileCoordinate(3, 2), result.Cell);
    }

    // ----- Fixture helper -------------------------------------------------

    private static EmergentMissionDto NewRecruitMission(string npcId) => new(
        Id: "ml-test",
        Type: WorldTickWireFormat.MissionType.Recruit,
        NarrativeHook: "fixture",
        EligiblePersonas: System.Array.Empty<string>(),
        Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
        Region: "halfgate",
        DeadlineTicks: null,
        Outcome: null,
        Seed: 0L,
        TargetPoi: "e2.halfgate.gateway",
        RecruitTargetNpcId: npcId);
}
