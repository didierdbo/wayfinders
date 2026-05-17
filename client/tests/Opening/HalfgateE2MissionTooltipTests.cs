using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Section A refactor (Varn-lock 2026-05-17 + Didier brief 2026-05-17) +
/// §A.8.D4 R7 refactor (2026-05-17 strict mode) — pin the E2 mission
/// tooltip composer's contract :
/// <list type="bullet">
///   <item>Header line = district display name.</item>
///   <item>One compact row per mission, format
///         <c>"{NPC display name OR type label} — {narrative hook}"</c>.</item>
///   <item>Recruit-type missions surface the NPC display name from
///         <see cref="NpcCatalog.LookupDisplayName"/> with
///         <see cref="EmergentMissionDto.RecruitTargetNpcId"/> as the
///         input. M2+ non-recruit types fall back to the legacy
///         <see cref="MissionDisplayNames.For"/> mapping.</item>
///   <item>Multiple missions on the same POI land as multiple
///         one-line rows, source order preserved.</item>
///   <item>Empty / null input returns <see cref="string.Empty"/>.</item>
/// </list>
/// </summary>
public sealed class HalfgateE2MissionTooltipTests
{
    // ---- Test helpers ----

    private static EmergentMissionDto Scout(string id, string targetPoi, string hook) =>
        new(
            Id: id,
            Type: WorldTickWireFormat.MissionType.ScoutRoute,
            NarrativeHook: hook,
            EligiblePersonas: new List<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: targetPoi);

    private static EmergentMissionDto Parley(string id, string targetPoi, string hook) =>
        new(
            Id: id,
            Type: WorldTickWireFormat.MissionType.ParleyLocal,
            NarrativeHook: hook,
            EligiblePersonas: new List<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: targetPoi);

    private static EmergentMissionDto Recruit(string id, string targetPoi, string hook, string npcId) =>
        new(
            Id: id,
            Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: hook,
            EligiblePersonas: new List<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: targetPoi,
            RecruitTargetNpcId: npcId);

    // ---- Compose contracts ----

    [Fact]
    public void Compose_with_null_missions_returns_empty()
    {
        // The hover handler relies on the empty-string contract to decide
        // "do not surface a tooltip" (spec : pas de tooltip si pas de
        // mission). A non-empty return here would surface a degenerate
        // header-only tooltip.
        Assert.Equal(string.Empty, HalfgateE2MissionTooltip.Compose("Intramuros", null));
    }

    [Fact]
    public void Compose_with_empty_mission_list_returns_empty()
    {
        Assert.Equal(
            string.Empty,
            HalfgateE2MissionTooltip.Compose(
                "Intramuros",
                System.Array.Empty<EmergentMissionDto>()));
    }

    [Fact]
    public void Compose_with_one_mission_starts_with_district_header()
    {
        var mission = Parley("ml_001", "e2.halfgate.intramuros", "The Cadastre suspects a name.");
        var text = HalfgateE2MissionTooltip.Compose("Intramuros", new[] { mission });

        // Header is the FIRST line, nothing decorative before it.
        Assert.StartsWith("Intramuros\n", text);
    }

    [Fact]
    public void Compose_with_one_mission_includes_label_and_hook()
    {
        var mission = Parley("ml_002", "e2.halfgate.intramuros",
            "The footings, this time, are notarised.");
        var text = HalfgateE2MissionTooltip.Compose("Intramuros", new[] { mission });

        // Wire authoritative content (NarrativeHook) round-trips verbatim.
        Assert.Contains(mission.NarrativeHook, text);
        // The synthesised display label for parley_local landings here.
        Assert.Contains("Parley", text);
    }

    [Fact]
    public void Compose_with_scout_mission_uses_scouting_label()
    {
        var mission = Scout("ml_003", "e2.halfgate.gateway", "Someone has been walking through.");
        var text = HalfgateE2MissionTooltip.Compose("Gateway", new[] { mission });

        // scout_route -> "Scouting" (Varn lookup mirror, M2+ fallback path).
        Assert.Contains("Scouting", text);
    }

    [Fact]
    public void Compose_recruit_mission_uses_npc_display_name_not_type_label()
    {
        // §A.8.D4 R7 — recruit missions surface the NPC display name.
        // Kira / Dorn / Vell from the roster, NOT the generic
        // "Recrutement" type label.
        var mission = Recruit("ml_kira", "e2.halfgate.gateway",
            "Kira watches the gate, ledger in hand.", npcId: "kira");
        var text = HalfgateE2MissionTooltip.Compose("Gateway", new[] { mission });

        Assert.Contains("Kira", text);
        // Anti-regression : the generic Recrutement type label must NOT
        // leak when the NPC name is available.
        Assert.DoesNotContain("Recrutement", text);
    }

    [Fact]
    public void Compose_recruit_mission_with_unknown_npc_id_falls_back_to_inconnu()
    {
        // Defensive : if the server somehow emits a recruit mission with
        // an unknown NpcId, the NpcCatalog default ("Inconnu") surfaces.
        // Visible signal that something drifted.
        var mission = Recruit("ml_x", "e2.halfgate.littoral", "h", npcId: "not_in_roster");
        var text = HalfgateE2MissionTooltip.Compose("Littoral", new[] { mission });

        Assert.Contains(NpcCatalog.DefaultDisplayName, text);
    }

    [Fact]
    public void Compose_recruit_mission_with_null_npc_id_falls_back_to_type_label()
    {
        // Even more defensive : a recruit mission with null
        // RecruitTargetNpcId (would be a server bug in M1 strict mode)
        // falls back to the type-slug label so the row still renders.
        var mission = new EmergentMissionDto(
            Id: "ml_y",
            Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: "h",
            EligiblePersonas: new List<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: "e2.halfgate.gateway",
            RecruitTargetNpcId: null);
        var text = HalfgateE2MissionTooltip.Compose("Gateway", new[] { mission });

        // Falls back to MissionDisplayNames.For("recruit") = "Recrutement".
        Assert.Contains("Recrutement", text);
    }

    [Fact]
    public void Compose_uses_em_dash_separator_between_label_and_hook()
    {
        // §A.8.D4 R7 — compact one-liner format : "{label} — {hook}".
        // Em-dash + spaces is the locked separator (RowSeparator).
        var mission = Recruit("ml_dorn", "e2.halfgate.intramuros",
            "Dorn keeps the ledger straight.", npcId: "dorn");
        var text = HalfgateE2MissionTooltip.Compose("Intramuros", new[] { mission });

        // Pin the exact compact-row shape : Dorn + RowSeparator + hook.
        Assert.Contains("Dorn" + HalfgateE2MissionTooltip.RowSeparator + mission.NarrativeHook, text);
    }

    [Fact]
    public void Compose_with_three_recruit_missions_renders_one_row_per_npc()
    {
        // §A.8.D3 + §A.8.D4 — full roster active. Three rows, three
        // NPCs, one per line in source order.
        var missions = new[]
        {
            Recruit("ml_kira", "e2.halfgate.gateway",    "Hook for Kira.",    npcId: "kira"),
            Recruit("ml_dorn", "e2.halfgate.intramuros", "Hook for Dorn.",    npcId: "dorn"),
            Recruit("ml_vell", "e2.halfgate.littoral",   "Hook for Vell.",    npcId: "vell"),
        };
        // Note : in practice each mission lands on its own POI, so the
        // composer is only called with one mission per district. This
        // test pins the multi-row shape against the (rare M2+) case of
        // multiple missions on the same district.
        var text = HalfgateE2MissionTooltip.Compose("Gateway", missions);

        Assert.Contains("Kira", text);
        Assert.Contains("Dorn", text);
        Assert.Contains("Vell", text);
        Assert.Contains(missions[0].NarrativeHook, text);
        Assert.Contains(missions[1].NarrativeHook, text);
        Assert.Contains(missions[2].NarrativeHook, text);
    }

    [Fact]
    public void Compose_preserves_mission_order_from_caller()
    {
        // The composer is not allowed to sort. The caller
        // (MissionStore.GetMissionsForPoi's server-spawn order) owns
        // ordering. Pin Scouting BEFORE Parley to surface a sort
        // regression.
        var missions = new[]
        {
            Scout("ml_008",  "e2.halfgate.gateway", "First : the road."),
            Parley("ml_009", "e2.halfgate.gateway", "Second : the people."),
        };
        var text = HalfgateE2MissionTooltip.Compose("Gateway", missions);

        var scoutIdx = text.IndexOf("Scouting");
        var parleyIdx = text.IndexOf("Parley");

        Assert.True(scoutIdx >= 0);
        Assert.True(parleyIdx >= 0);
        Assert.True(scoutIdx < parleyIdx,
            $"Mission order was not preserved : Scouting idx={scoutIdx} " +
            $"should be before Parley idx={parleyIdx}.");
    }

    [Fact]
    public void Compose_lines_break_between_rows_so_multiple_missions_each_have_their_own_line()
    {
        // §A.8.D4 — one row per mission per line. The newline before
        // each row is the visual separator.
        var missions = new[]
        {
            Recruit("ml_k", "e2.halfgate.gateway", "h-kira", npcId: "kira"),
            Recruit("ml_d", "e2.halfgate.gateway", "h-dorn", npcId: "dorn"),
        };
        var text = HalfgateE2MissionTooltip.Compose("Gateway", missions);

        // Both rows must be present, and the second one is preceded by
        // a newline (= its own line).
        Assert.Contains("\nKira" + HalfgateE2MissionTooltip.RowSeparator + "h-kira", text);
        Assert.Contains("\nDorn" + HalfgateE2MissionTooltip.RowSeparator + "h-dorn", text);
    }

    // ---- MissionDisplayNames lookup contract ----

    [Theory]
    [InlineData(WorldTickWireFormat.MissionType.Recruit, "Recrutement")]
    [InlineData(WorldTickWireFormat.MissionType.ScoutRoute, "Scouting")]
    [InlineData(WorldTickWireFormat.MissionType.ParleyLocal, "Parley")]
    [InlineData("unknown_mission_type", "Mission")]
    [InlineData("", "Mission")]
    public void MissionDisplayNames_For_returns_expected_label(string missionType, string expected)
    {
        Assert.Equal(expected, MissionDisplayNames.For(missionType));
    }

    // ---- MissionTooltipRows.ResolveDisplayName seam ----

    [Fact]
    public void MissionTooltipRows_ResolveDisplayName_recruit_returns_npc_name()
    {
        var mission = Recruit("x", "e2.halfgate.gateway", "h", npcId: "kira");
        Assert.Equal("Kira", MissionTooltipRows.ResolveDisplayName(mission));
    }

    [Fact]
    public void MissionTooltipRows_ResolveDisplayName_scout_returns_scouting_label()
    {
        var mission = Scout("x", "e2.halfgate.gateway", "h");
        Assert.Equal("Scouting", MissionTooltipRows.ResolveDisplayName(mission));
    }

    [Fact]
    public void MissionTooltipRows_Compose_recruit_row_uses_npc_name()
    {
        var mission = Recruit("ml_vell", "e2.halfgate.littoral", "Vell hook.", npcId: "vell");
        var payload = MissionTooltipRows.Compose("Littoral", new[] { mission });

        Assert.Single(payload.Rows);
        Assert.Equal("Vell", payload.Rows[0].DisplayName);
        Assert.Equal("Vell hook.", payload.Rows[0].NarrativeHook);
        Assert.Equal("ml_vell", payload.Rows[0].MissionId);
    }
}
