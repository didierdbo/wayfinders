using System;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# composer for the E2.2 mission recruit panel slide-in (step 3
/// + step 4, 2026-05-17). Owns the projection from a wire-format
/// <see cref="EmergentMissionDto"/> to the structured payload the
/// Godot-bound <see cref="Wayfinders.Client.Scenes.Ui.MissionRecruitPanel"/>
/// renders, plus the action-routing decision (Accept vs Decline ->
/// <c>/api/missions/&lt;id&gt;/{accept,decline}</c>).
///
/// <para>
/// <b>Same separation pattern as <see cref="MissionPanelLogic"/>.</b>
/// The legacy <see cref="MissionPanelLogic"/> composes the M1Slice
/// text-only outcome-flow panel (resolve outcome + tags). This new
/// helper composes the §A.8.D5 recruit panel (accept / decline state
/// transitions). Two different concerns, two different DTO surfaces,
/// two different endpoints -- kept as parallel helpers so neither
/// drags the other's contract into its tests.
/// </para>
///
/// <para>
/// <b>What this helper pins (xUnit surface).</b>
/// <list type="bullet">
///   <item>Header text = "Recrutement &#8212; {DisplayName}" derived from
///         <see cref="EmergentMissionDto.RecruitTargetNpcId"/> via
///         <see cref="NpcCatalog.LookupDisplayName"/>.</item>
///   <item>District subtitle = resolved from the
///         <see cref="EmergentMissionDto.TargetPoi"/> third slug segment
///         (e.g. <c>"e2.halfgate.gateway"</c> -> "Gateway") via
///         <see cref="DistrictTypeHelpers.DisplayName"/>.</item>
///   <item>Hook = passthrough of
///         <see cref="EmergentMissionDto.NarrativeHook"/>.</item>
///   <item>NpcId = passthrough of
///         <see cref="EmergentMissionDto.RecruitTargetNpcId"/> (feeds
///         <c>IsoCharacterPlaceholder.SetNpc</c> + the palette).</item>
///   <item>Action routing : <see cref="ResolveActionRoute"/> returns
///         the endpoint label for each button. Trivial today (one
///         endpoint per button) but the pin guards against a future
///         swap that wires Accept onto Decline by mistake.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Defensive defaults.</b> A mission with a missing
/// <see cref="EmergentMissionDto.RecruitTargetNpcId"/> (M2+ non-recruit
/// types, or a server bug) falls back to the
/// <see cref="NpcCatalog.DefaultDisplayName"/> in the header. A mission
/// with a malformed <see cref="EmergentMissionDto.TargetPoi"/> falls
/// back to "Inconnu" in the district subtitle. The panel still renders
/// (no exception bubbles) so the player can decline a corrupt mission
/// out of band.
/// </para>
/// </summary>
public static class MissionRecruitPanelLogic
{
    /// <summary>
    /// District fallback when the <see cref="EmergentMissionDto.TargetPoi"/>
    /// shape does not match the canonical <c>e&lt;layer&gt;.&lt;city&gt;.&lt;district&gt;</c>
    /// 3-segment form. Visible in the UI as "Inconnu" so a corrupt
    /// payload surfaces immediately at smoke time.
    /// </summary>
    public const string UnknownDistrictLabel = "Inconnu";

    /// <summary>Endpoint label returned by
    /// <see cref="ResolveActionRoute"/> for the Accept button.
    /// Matches the §A.8.D5 path suffix exactly so test pins read
    /// against the wire URL directly.</summary>
    public const string AcceptEndpointSuffix = "accept";

    /// <summary>Endpoint label returned by
    /// <see cref="ResolveActionRoute"/> for the Decline button.</summary>
    public const string DeclineEndpointSuffix = "decline";

    /// <summary>
    /// Build the structured payload the panel renders. Pure projection ;
    /// safe to call on the main thread or from a unit test. The input
    /// <paramref name="mission"/> is the live <see cref="EmergentMissionDto"/>
    /// the player clicked from the tooltip row.
    /// </summary>
    public static MissionRecruitPanelPayload Compose(EmergentMissionDto mission)
    {
        if (mission is null) throw new ArgumentNullException(nameof(mission));

        var npcId = mission.RecruitTargetNpcId;
        var displayName = NpcCatalog.LookupDisplayName(npcId);
        var header = $"Recrutement — {displayName}";

        var district = ResolveDistrictLabel(mission.TargetPoi);

        return new MissionRecruitPanelPayload(
            MissionId: mission.Id,
            HeaderText: header,
            DistrictLabel: district,
            NarrativeHook: mission.NarrativeHook ?? string.Empty,
            NpcId: npcId ?? string.Empty);
    }

    /// <summary>
    /// Resolve the human-readable district label for a canonical
    /// <c>e&lt;layer&gt;.&lt;city&gt;.&lt;district&gt;</c> id. Returns
    /// <see cref="UnknownDistrictLabel"/> on any shape mismatch.
    /// </summary>
    public static string ResolveDistrictLabel(string? targetPoi)
    {
        if (string.IsNullOrEmpty(targetPoi)) return UnknownDistrictLabel;
        var firstDot = targetPoi.IndexOf('.');
        if (firstDot < 0) return UnknownDistrictLabel;
        var secondDot = targetPoi.IndexOf('.', firstDot + 1);
        if (secondDot < 0 || secondDot >= targetPoi.Length - 1) return UnknownDistrictLabel;
        var slug = targetPoi.Substring(secondDot + 1);
        // Slug may contain extra dots (M2+ deeper hierarchies). For M1
        // every district slug is a flat token so a contained dot is a
        // shape mismatch -- collapse to Inconnu.
        if (slug.IndexOf('.') >= 0) return UnknownDistrictLabel;
        return DistrictTypeHelpers.TryParseSlug(slug, out var district)
            ? DistrictTypeHelpers.DisplayName(district)
            : UnknownDistrictLabel;
    }

    /// <summary>
    /// Action routing pin -- the panel's two buttons must route to
    /// distinct endpoints. The pure function exists so a future swap
    /// that wires Accept onto Decline by mistake is caught by a red
    /// test, not by a smoke-test surprise.
    /// </summary>
    public static string ResolveActionRoute(MissionRecruitPanelAction action) => action switch
    {
        MissionRecruitPanelAction.Accept => AcceptEndpointSuffix,
        MissionRecruitPanelAction.Decline => DeclineEndpointSuffix,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "unknown action"),
    };
}

/// <summary>
/// Structured payload returned by
/// <see cref="MissionRecruitPanelLogic.Compose"/>. The Godot-bound
/// <see cref="Wayfinders.Client.Scenes.Ui.MissionRecruitPanel"/>
/// binds <see cref="HeaderText"/> to its header Label,
/// <see cref="DistrictLabel"/> to the subtitle, <see cref="NarrativeHook"/>
/// to the body, and feeds <see cref="NpcId"/> to its
/// <see cref="Wayfinders.Client.Scenes.Ui.IsoCharacterPlaceholder.SetNpc"/>
/// child. <see cref="MissionId"/> is captured so the Accept / Decline
/// handlers can build the action URL without re-fetching the source
/// mission.
/// </summary>
public sealed record MissionRecruitPanelPayload(
    string MissionId,
    string HeaderText,
    string DistrictLabel,
    string NarrativeHook,
    string NpcId
);

/// <summary>
/// Closed lookup mirroring the two §A.8.D5 mission-action state
/// transitions the recruit panel exposes. M2+ may extend (e.g.
/// <c>Conclude</c> when the panel grows a "fin de mission" step) ;
/// the seam is here so the extension is one switch arm + one test pin.
/// </summary>
public enum MissionRecruitPanelAction
{
    Accept = 0,
    Decline = 1,
}
