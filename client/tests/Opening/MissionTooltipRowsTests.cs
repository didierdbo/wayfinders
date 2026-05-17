using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Section A refactor (Varn-lock 2026-05-17 + Didier brief 2026-05-17) —
/// pin the structured tooltip payload composer with the new DTO-based
/// input. The Godot-bound row rendering (PanelContainer + VBox + per-row
/// Button) is smoke-tested visually via F6 on E2AreaMap.tscn ; this xUnit
/// suite pins the pure-C# contract :
/// <list type="bullet">
///   <item>The district display name round-trips verbatim into the
///         payload header.</item>
///   <item>Each mission becomes exactly one row, preserving order.</item>
///   <item>MissionId / DisplayName / NarrativeHook round-trip per row —
///         MissionId from <see cref="EmergentMissionDto.Id"/>, DisplayName
///         synthesised via <see cref="MissionDisplayNames.For"/> from
///         <see cref="EmergentMissionDto.Type"/>, NarrativeHook from
///         <see cref="EmergentMissionDto.NarrativeHook"/>.</item>
///   <item>Null or empty mission input yields an empty row list (so the
///         caller can suppress the tooltip, matching the empty contract).</item>
/// </list>
/// </summary>
public sealed class MissionTooltipRowsTests
{
    private static EmergentMissionDto Make(
        string id,
        string type,
        string targetPoi,
        string hook) =>
        new(
            Id: id,
            Type: type,
            NarrativeHook: hook,
            EligiblePersonas: new List<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: targetPoi);

    [Fact]
    public void Compose_with_null_missions_yields_empty_rows_with_header_preserved()
    {
        // Empty payload contract : header preserved so the caller can log
        // "I tried to show a tooltip for Intramuros but no mission" ; Rows
        // is empty so the renderer suppresses the panel.
        var payload = MissionTooltipRows.Compose("Intramuros", missions: null);

        Assert.Equal("Intramuros", payload.DistrictDisplayName);
        Assert.Empty(payload.Rows);
    }

    [Fact]
    public void Compose_with_empty_mission_list_yields_empty_rows()
    {
        var payload = MissionTooltipRows.Compose(
            "Gateway",
            System.Array.Empty<EmergentMissionDto>());

        Assert.Equal("Gateway", payload.DistrictDisplayName);
        Assert.Empty(payload.Rows);
    }

    [Fact]
    public void Compose_with_one_mission_yields_one_row_with_all_fields_round_tripped()
    {
        var mission = Make(
            "ml_intramuros_parley_001",
            WorldTickWireFormat.MissionType.ParleyLocal,
            "e2.halfgate.intramuros",
            "The Cadastre suspects a name. The footings, this time, are notarised.");
        var payload = MissionTooltipRows.Compose("Intramuros", new[] { mission });

        Assert.Equal("Intramuros", payload.DistrictDisplayName);
        Assert.Single(payload.Rows);

        var row = payload.Rows[0];
        // MissionId is the forward-compat seam for the click handler ;
        // pin that every slot arrives intact (no truncation, no
        // paraphrasing). DisplayName synthesised from mission.Type.
        Assert.Equal(mission.Id, row.MissionId);
        Assert.Equal("Parley", row.DisplayName);
        Assert.Equal(mission.NarrativeHook, row.NarrativeHook);
    }

    [Fact]
    public void Compose_preserves_mission_order_from_caller()
    {
        // The composer is not allowed to sort. The caller
        // (MissionStore.GetMissionsForPoi's server-spawn order) owns
        // ordering. Mirror the composer order-preservation pin : Scout
        // BEFORE Parley surfaces any silent sort regression.
        var input = new[]
        {
            Make("ml_a", WorldTickWireFormat.MissionType.ScoutRoute,
                "e2.halfgate.gateway", "First : the road."),
            Make("ml_b", WorldTickWireFormat.MissionType.ParleyLocal,
                "e2.halfgate.gateway", "Second : the people."),
        };
        var payload = MissionTooltipRows.Compose("Gateway", input);

        Assert.Equal(2, payload.Rows.Count);
        Assert.Equal(input[0].Id, payload.Rows[0].MissionId);
        Assert.Equal(input[1].Id, payload.Rows[1].MissionId);
    }

    [Fact]
    public void Compose_with_three_missions_yields_three_rows()
    {
        // M1 cap allows multiple missions per district (e.g. one
        // emergence per cadence window can stack on the same district
        // if affinity ordering favours it). The row count must follow
        // the input count without dedup, cap, or filter.
        var input = new[]
        {
            Make("ml_x", WorldTickWireFormat.MissionType.ScoutRoute,
                "e2.halfgate.intramuros", "First."),
            Make("ml_y", WorldTickWireFormat.MissionType.ParleyLocal,
                "e2.halfgate.intramuros", "Second."),
            Make("ml_z", WorldTickWireFormat.MissionType.ScoutRoute,
                "e2.halfgate.intramuros", "Third."),
        };
        var payload = MissionTooltipRows.Compose("Intramuros", input);

        Assert.Equal(3, payload.Rows.Count);
        Assert.Equal(
            input.Select(m => m.Id).ToList(),
            payload.Rows.Select(r => r.MissionId).ToList());
    }
}
