using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the E2.2 step 2 structured tooltip payload composer. The Godot-bound
/// row rendering (PanelContainer + VBox + per-row Button with hover
/// highlight + pointing-hand cursor) is smoke-tested visually via F6 on
/// E2AreaMap.tscn ; this xUnit suite pins the pure-C# contract :
/// <list type="bullet">
///   <item>The district display name round-trips verbatim into the
///         payload header.</item>
///   <item>Each mission becomes exactly one row, preserving order.</item>
///   <item>MissionId / DisplayName / NarrativeHook round-trip per row --
///         the row record carries everything the Godot builder needs
///         without going back to the authoring lookup.</item>
///   <item>Null or empty mission input yields an empty row list (so the
///         caller can suppress the tooltip, matching the step 1 empty
///         contract).</item>
/// </list>
/// </summary>
public sealed class MissionTooltipRowsTests
{
    [Fact]
    public void Compose_with_null_missions_yields_empty_rows_with_header_preserved()
    {
        // Empty payload contract : header preserved so the caller can log
        // "I tried to show a tooltip for Intramuros but no mission" if it
        // wants to, but Rows is empty so the renderer suppresses the
        // panel rather than painting a degenerate header-only card.
        var payload = MissionTooltipRows.Compose("Intramuros", missions: null);

        Assert.Equal("Intramuros", payload.DistrictDisplayName);
        Assert.Empty(payload.Rows);
    }

    [Fact]
    public void Compose_with_empty_mission_list_yields_empty_rows()
    {
        var payload = MissionTooltipRows.Compose(
            "Gateway",
            System.Array.Empty<HalfgateE2Mission>());

        Assert.Equal("Gateway", payload.DistrictDisplayName);
        Assert.Empty(payload.Rows);
    }

    [Fact]
    public void Compose_with_one_mission_yields_one_row_with_all_fields_round_tripped()
    {
        var mission = HalfgateE2MissionAuthoring.TutorialIntramuros;
        var payload = MissionTooltipRows.Compose("Intramuros", new[] { mission });

        Assert.Equal("Intramuros", payload.DistrictDisplayName);
        Assert.Single(payload.Rows);

        var row = payload.Rows[0];
        // The row record is the Godot builder's only authoring dep ; pin
        // that every field arrives intact (no truncation, no
        // paraphrasing). MissionId is the forward-compat seam for the
        // step 3 click handler.
        Assert.Equal(mission.MissionId, row.MissionId);
        Assert.Equal(mission.DisplayName, row.DisplayName);
        Assert.Equal(mission.NarrativeHook, row.NarrativeHook);
    }

    [Fact]
    public void Compose_preserves_mission_order_from_caller()
    {
        // The composer is not allowed to sort. The caller
        // (HalfgateE2MissionAuthoring.All static order, or a future
        // priority-sorted upstream) owns ordering. Mirror the step 1
        // composer's order-preservation test : Littoral BEFORE Intramuros
        // surfaces any silent sort regression.
        var input = new[]
        {
            HalfgateE2MissionAuthoring.TutorialLittoral,
            HalfgateE2MissionAuthoring.TutorialIntramuros,
        };
        var payload = MissionTooltipRows.Compose("Halfgate", input);

        Assert.Equal(2, payload.Rows.Count);
        Assert.Equal(input[0].MissionId, payload.Rows[0].MissionId);
        Assert.Equal(input[1].MissionId, payload.Rows[1].MissionId);
    }

    [Fact]
    public void Compose_with_three_missions_yields_three_rows()
    {
        // E2.1c invariant : the Halfgate mock set has 3 missions (one per
        // active POI). Forward to E2.2+ where multiple missions per POI
        // may land : the row count must follow the input count without
        // dedup, cap, or filter.
        var payload = MissionTooltipRows.Compose(
            "Halfgate",
            HalfgateE2MissionAuthoring.All);

        Assert.Equal(3, payload.Rows.Count);
        Assert.Equal(
            HalfgateE2MissionAuthoring.All.Select(m => m.MissionId).ToList(),
            payload.Rows.Select(r => r.MissionId).ToList());
    }
}
