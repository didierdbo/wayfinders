using System;
using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="MissionRecruitPanelLogic"/> -- the
/// projection from a wire-format <see cref="EmergentMissionDto"/> to
/// the structured payload the E2.2 recruit panel renders (step 3+4,
/// 2026-05-17). Same separation pattern as
/// <see cref="MissionPanelLogicTests"/> : the engine-bound panel wires
/// Buttons + Labels + the async ApiClient call ; the contract -- header
/// composition, district resolution, action routing -- lives here.
/// </summary>
public sealed class MissionRecruitPanelLogicTests
{
    // --------------------------------------------------------------------
    // Compose -- header / district / hook / npcId for the 3 roster cases.
    // --------------------------------------------------------------------

    [Fact]
    public void Compose_kira_gateway_yields_header_district_hook_npcId()
    {
        var mission = MakeRecruit(
            id: "m-1",
            npc: "kira",
            poi: "e2.halfgate.gateway",
            hook: "Une silhouette attend près des grilles.");

        var payload = MissionRecruitPanelLogic.Compose(mission);

        Assert.Equal("m-1", payload.MissionId);
        Assert.Equal("Recrutement — Kira", payload.HeaderText);
        Assert.Equal("Gateway", payload.DistrictLabel);
        Assert.Equal("Une silhouette attend près des grilles.", payload.NarrativeHook);
        Assert.Equal("kira", payload.NpcId);
    }

    [Fact]
    public void Compose_dorn_intramuros_yields_header_district_hook_npcId()
    {
        var mission = MakeRecruit(
            id: "m-2",
            npc: "dorn",
            poi: "e2.halfgate.intramuros",
            hook: "Un vétéran ronchonne sur le parvis.");

        var payload = MissionRecruitPanelLogic.Compose(mission);

        Assert.Equal("m-2", payload.MissionId);
        Assert.Equal("Recrutement — Dorn", payload.HeaderText);
        Assert.Equal("Intramuros", payload.DistrictLabel);
        Assert.Equal("Un vétéran ronchonne sur le parvis.", payload.NarrativeHook);
        Assert.Equal("dorn", payload.NpcId);
    }

    [Fact]
    public void Compose_vell_littoral_yields_header_district_hook_npcId()
    {
        var mission = MakeRecruit(
            id: "m-3",
            npc: "vell",
            poi: "e2.halfgate.littoral",
            hook: "Une femme assise sur un cordage observe l'horizon.");

        var payload = MissionRecruitPanelLogic.Compose(mission);

        Assert.Equal("m-3", payload.MissionId);
        Assert.Equal("Recrutement — Vell", payload.HeaderText);
        Assert.Equal("Littoral", payload.DistrictLabel);
        Assert.Equal("Une femme assise sur un cordage observe l'horizon.", payload.NarrativeHook);
        Assert.Equal("vell", payload.NpcId);
    }

    // --------------------------------------------------------------------
    // Defensive paths -- missing npc id / malformed target_poi.
    // --------------------------------------------------------------------

    [Fact]
    public void Compose_missing_npc_id_falls_back_to_inconnu_header()
    {
        // M2+ non-recruit type or server bug : RecruitTargetNpcId = null.
        // Header still composes via the NpcCatalog fallback, district
        // still resolves -- the panel must render so the player can
        // decline the corrupt mission out of band.
        var mission = MakeRecruit(
            id: "m-4",
            npc: null,
            poi: "e2.halfgate.gateway",
            hook: "Hook stub.");

        var payload = MissionRecruitPanelLogic.Compose(mission);

        Assert.Equal($"Recrutement — {NpcCatalog.DefaultDisplayName}", payload.HeaderText);
        Assert.Equal("Gateway", payload.DistrictLabel);
        Assert.Equal(string.Empty, payload.NpcId);
    }

    [Fact]
    public void Compose_unknown_npc_id_falls_back_to_inconnu_header()
    {
        var mission = MakeRecruit(
            id: "m-5",
            npc: "wattan",
            poi: "e2.halfgate.gateway",
            hook: "Hook stub.");

        var payload = MissionRecruitPanelLogic.Compose(mission);

        Assert.Equal($"Recrutement — {NpcCatalog.DefaultDisplayName}", payload.HeaderText);
        // Unknown npc id still passes through verbatim -- the panel
        // uses it for the placeholder fallback tint (parchment).
        Assert.Equal("wattan", payload.NpcId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("e2")]
    [InlineData("e2.halfgate")]
    [InlineData("e2.halfgate.")]
    [InlineData("nonsense")]
    public void Compose_malformed_target_poi_yields_inconnu_district(string poi)
    {
        var mission = MakeRecruit("m-x", "kira", poi, "Stub.");
        var payload = MissionRecruitPanelLogic.Compose(mission);
        Assert.Equal(MissionRecruitPanelLogic.UnknownDistrictLabel, payload.DistrictLabel);
    }

    [Fact]
    public void Compose_unknown_district_slug_yields_inconnu_district()
    {
        // Slug not in DistrictTypeHelpers.TryParseSlug -> Inconnu.
        var mission = MakeRecruit("m-x", "kira", "e2.halfgate.attic", "Stub.");
        var payload = MissionRecruitPanelLogic.Compose(mission);
        Assert.Equal(MissionRecruitPanelLogic.UnknownDistrictLabel, payload.DistrictLabel);
    }

    [Fact]
    public void Compose_throws_on_null_mission()
    {
        Assert.Throws<ArgumentNullException>(() => MissionRecruitPanelLogic.Compose(null!));
    }

    // --------------------------------------------------------------------
    // Action routing -- Accept -> "accept" endpoint, Decline -> "decline".
    // Guards against a future swap that accidentally crosses the wires.
    // --------------------------------------------------------------------

    [Fact]
    public void Accept_routes_to_accept_endpoint_suffix()
    {
        Assert.Equal("accept",
            MissionRecruitPanelLogic.ResolveActionRoute(MissionRecruitPanelAction.Accept));
    }

    [Fact]
    public void Decline_routes_to_decline_endpoint_suffix()
    {
        Assert.Equal("decline",
            MissionRecruitPanelLogic.ResolveActionRoute(MissionRecruitPanelAction.Decline));
    }

    [Fact]
    public void Action_route_constants_match_endpoint_path_suffixes()
    {
        // Belt-and-braces : the constants are themselves the spec ; if
        // someone "fixes" them to a different casing the §A.8.D5
        // endpoint URL drifts silently. Pin the literal value.
        Assert.Equal("accept", MissionRecruitPanelLogic.AcceptEndpointSuffix);
        Assert.Equal("decline", MissionRecruitPanelLogic.DeclineEndpointSuffix);
    }

    // --------------------------------------------------------------------
    // ResolveDistrictLabel -- direct pin for the helper used by Compose.
    // --------------------------------------------------------------------

    [Theory]
    [InlineData("e2.halfgate.gateway", "Gateway")]
    [InlineData("e2.halfgate.intramuros", "Intramuros")]
    [InlineData("e2.halfgate.littoral", "Littoral")]
    [InlineData("e2.halfgate.outskirts", "Outskirts")]
    [InlineData("e2.halfgate.wall", "Wall")]
    [InlineData("e2.halfgate.hinterland_agri", "Hinterland")]
    public void Resolve_district_label_for_known_slugs(string poi, string expected)
    {
        Assert.Equal(expected, MissionRecruitPanelLogic.ResolveDistrictLabel(poi));
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private static EmergentMissionDto MakeRecruit(
        string id,
        string? npc,
        string poi,
        string hook)
    {
        return new EmergentMissionDto(
            Id: id,
            Type: WorldTickWireFormat.MissionType.Recruit,
            NarrativeHook: hook,
            EligiblePersonas: new List<string>(),
            Difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: poi,
            RecruitTargetNpcId: npc);
    }
}
