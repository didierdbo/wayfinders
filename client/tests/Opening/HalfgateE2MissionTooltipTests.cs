using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Section A refactor (Varn-lock 2026-05-17 + Didier brief 2026-05-17) —
/// pin the E2 mission tooltip composer's new DTO-based contract. The
/// hardcoded <c>HalfgateE2Mission</c> record + its three Tutorial entries
/// are retired ; the composer now consumes the wire DTO
/// (<see cref="EmergentMissionDto"/>). The rendered shape stays the same :
/// district header + per-mission bullet block. The DisplayName slot is
/// synthesised from <see cref="EmergentMissionDto.Type"/> via
/// <see cref="MissionDisplayNames"/>.
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
    public void Compose_with_one_mission_includes_type_label_and_hook()
    {
        var mission = Parley("ml_002", "e2.halfgate.intramuros",
            "The footings, this time, are notarised.");
        var text = HalfgateE2MissionTooltip.Compose("Intramuros", new[] { mission });

        // Wire authoritative content (NarrativeHook) round-trips verbatim.
        Assert.Contains(mission.NarrativeHook, text);

        // The synthesised display label for parley_local landings here.
        // Pinned so a silent rename in MissionDisplayNames surfaces red.
        Assert.Contains("Parley", text);
    }

    [Fact]
    public void Compose_with_scout_mission_uses_scouting_label()
    {
        var mission = Scout("ml_003", "e2.halfgate.gateway", "Someone has been walking through.");
        var text = HalfgateE2MissionTooltip.Compose("Gateway", new[] { mission });

        // scout_route -> "Scouting" (Varn lookup mirror).
        Assert.Contains("Scouting", text);
    }

    [Fact]
    public void Compose_with_one_mission_prefixes_display_name_with_bullet()
    {
        var mission = Scout("ml_004", "e2.halfgate.gateway", "Toll-takers, anxious.");
        var text = HalfgateE2MissionTooltip.Compose("Gateway", new[] { mission });

        // Bullet glyph (locked at "- ") must appear immediately before
        // the synthesised display label. A silent change to a different
        // glyph would shift the visual rhythm of the tooltip.
        Assert.Contains(HalfgateE2MissionTooltip.MissionBullet + "Scouting", text);
    }

    [Fact]
    public void Compose_with_one_mission_indents_the_narrative_hook()
    {
        var mission = Parley("ml_005", "e2.halfgate.littoral", "A boat. An hour. A name.");
        var text = HalfgateE2MissionTooltip.Compose("Littoral", new[] { mission });

        // The narrative hook must sit on its own line, indented under the
        // mission DisplayName. The "\n<indent><hook>" substring is the
        // pinned shape.
        Assert.Contains("\n" + HalfgateE2MissionTooltip.HookIndent + mission.NarrativeHook, text);
    }

    [Fact]
    public void Compose_with_two_missions_separates_them_with_a_blank_line()
    {
        // Forward-compatibility check : the M1 cap of 3 visible districts
        // allows multiple missions on the same district. The composer
        // must keep a blank line between missions so the wall of text
        // stays legible.
        var missions = new[]
        {
            Parley("ml_006", "e2.halfgate.intramuros", "The Cadastre, again."),
            Scout("ml_007",  "e2.halfgate.intramuros", "And the road past it."),
        };
        var text = HalfgateE2MissionTooltip.Compose("Intramuros", missions);

        // Both narrative hooks must be present.
        Assert.Contains(missions[0].NarrativeHook, text);
        Assert.Contains(missions[1].NarrativeHook, text);

        // Pin the separator : the second mission's bullet line sits after
        // the first mission's narrative hook + a blank line.
        var separator =
            missions[0].NarrativeHook
            + "\n\n"
            + HalfgateE2MissionTooltip.MissionBullet
            + "Scouting";
        Assert.Contains(separator, text);
    }

    [Fact]
    public void Compose_preserves_mission_order_from_caller()
    {
        // The composer is not allowed to sort. The caller
        // (MissionStore.GetMissionsForPoi's server-spawn order) owns
        // ordering. We pin Scouting BEFORE Parley to surface a sort
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

    // ---- MissionDisplayNames lookup contract ----

    [Theory]
    [InlineData(WorldTickWireFormat.MissionType.ScoutRoute, "Scouting")]
    [InlineData(WorldTickWireFormat.MissionType.ParleyLocal, "Parley")]
    [InlineData("unknown_mission_type", "Mission")]
    [InlineData("", "Mission")]
    public void MissionDisplayNames_For_returns_expected_label(string missionType, string expected)
    {
        Assert.Equal(expected, MissionDisplayNames.For(missionType));
    }
}
