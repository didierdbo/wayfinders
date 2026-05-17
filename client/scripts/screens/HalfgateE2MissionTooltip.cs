using System.Collections.Generic;
using System.Linq;
using System.Text;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// E2.2 step 1 (2026-05-17) -- compose the tooltip text shown when the
/// player hovers an E2-layer POI marker. Pure-C#, Godot-free, mirrors the
/// <see cref="HalfgateRumorHooks"/> seam : the Godot-bound hover handler
/// in <c>E2AreaMap.AreaGrid.cs</c> calls <see cref="Compose"/> with the
/// list of <see cref="EmergentMissionDto"/>s anchored on the hovered POI
/// and feeds the result to <c>HoverTooltipController.RequestTooltip</c>.
///
/// <para>
/// <b>Section A refactor (Varn-lock 2026-05-17 + Didier brief 2026-05-17).</b>
/// The composer pivoted from the hardcoded
/// <c>HalfgateE2MissionAuthoring</c> record (now retired) to the
/// wire DTO. The rendered shape is unchanged : district header + per-mission
/// (display name + narrative hook) bullet block. The DisplayName slot is
/// synthesised from <see cref="EmergentMissionDto.Type"/> via
/// <see cref="MissionDisplayNames"/> (the wire schema does NOT carry a
/// DisplayName field — the server's authoritative content is
/// <see cref="EmergentMissionDto.NarrativeHook"/>, and a short type-derived
/// label keeps the player's reading rhythm).
/// </para>
///
/// <para>
/// <b>Layout.</b> Header line = district display name, then one bullet per
/// mission with the synthesised <see cref="MissionDisplayNames.For"/> label
/// on its own line followed by the indented
/// <see cref="EmergentMissionDto.NarrativeHook"/>. Two missions on the
/// same POI are separated by a blank line so the wall of text stays
/// readable.
/// </para>
///
/// <para>
/// <b>Empty-list contract.</b> Returns <see cref="string.Empty"/> when no
/// mission is supplied. The hover handler in <c>E2AreaMap.AreaGrid.cs</c>
/// checks for empty and skips the
/// <c>HoverTooltipController.RequestTooltip</c> call -- satisfies the
/// "no tooltip if no mission available" spec without surfacing a
/// degenerate "no missions" panel.
/// </para>
///
/// <para>
/// <b>Tonal DNA constraint.</b> The narrative-hook prose is authored
/// server-side under the Pratchett-tone budget (60-140 char, ends with a
/// period, no parodic register) — the eval harness pins it. The composer
/// adds no decoration beyond a leading bullet glyph and the district label
/// header ; the voice belongs to the mission strings.
/// </para>
/// </summary>
public static class HalfgateE2MissionTooltip
{
    /// <summary>
    /// Bullet glyph prefixed to each mission display label. Plain ASCII
    /// hyphen-bullet keeps the font requirements minimal (no unicode
    /// dependency) and renders cleanly in the parchment-tone
    /// <c>HoverTooltip.tscn</c> Label.
    /// </summary>
    internal const string MissionBullet = "- ";

    /// <summary>
    /// Indent applied to the narrative hook line under each mission's
    /// display label. Two spaces line up under the start of the label
    /// (the bullet glyph + space is 2 chars wide). A tab would also work
    /// but is renderer-dependent in the Label control ; spaces are safe.
    /// </summary>
    internal const string HookIndent = "  ";

    /// <summary>
    /// Compose the tooltip text for a POI hovering surface from the wire
    /// DTO list.
    /// </summary>
    /// <param name="districtDisplayName">Human-readable header line.
    /// Resolved by the caller via
    /// <see cref="DistrictTypeHelpers.DisplayName"/>. Pre-resolved (rather
    /// than passing the enum) so this composer stays single-responsibility
    /// and the caller controls localisation if it ever lands.</param>
    /// <param name="missions">Missions anchored on the hovered POI.
    /// Empty or null yields <see cref="string.Empty"/>. Iterated in the
    /// supplied order — the caller (or
    /// <see cref="MissionStore.GetMissionsForPoi"/>'s server-spawn order)
    /// owns the sort key.</param>
    /// <returns>Final tooltip text (multi-line), or
    /// <see cref="string.Empty"/> when there is nothing to show.</returns>
    public static string Compose(
        string districtDisplayName,
        IEnumerable<EmergentMissionDto>? missions)
    {
        if (missions is null) return string.Empty;

        var missionList = missions as IList<EmergentMissionDto> ?? missions.ToList();
        if (missionList.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.Append(districtDisplayName);

        for (int i = 0; i < missionList.Count; i++)
        {
            var m = missionList[i];
            // Blank line between missions for readability ; a single blank
            // between header + first mission for visual separation of the
            // header from the body.
            sb.Append('\n');
            sb.Append('\n');
            sb.Append(MissionBullet);
            sb.Append(MissionDisplayNames.For(m.Type));
            sb.Append('\n');
            sb.Append(HookIndent);
            sb.Append(m.NarrativeHook);
        }

        return sb.ToString();
    }
}

/// <summary>
/// Synthesise a short, player-facing label for an <see cref="EmergentMissionDto"/>
/// from its <see cref="EmergentMissionDto.Type"/> closed-lookup value.
/// The wire schema does NOT carry a DisplayName field ; the server's
/// authoritative content is the long-form
/// <see cref="EmergentMissionDto.NarrativeHook"/>. The label here gives
/// the eye a one-word anchor above the prose so the tooltip reads like
/// a quest log entry rather than a wall of paragraph.
///
/// <para>
/// <b>Locked label set (Varn lookup mirror).</b> The two M1 mission types
/// (<see cref="WorldTickWireFormat.MissionType.ScoutRoute"/> /
/// <see cref="WorldTickWireFormat.MissionType.ParleyLocal"/>) get one
/// label each. Adding a future mission type on the wire (M2 grammar
/// expansion) lands an <c>InlineData</c> entry here ; an unmapped value
/// falls back to a generic <c>"Mission"</c> label so a wire/spec drift
/// surfaces visibly but does not crash the tooltip.
/// </para>
/// </summary>
public static class MissionDisplayNames
{
    /// <summary>
    /// Resolve a mission-type slug to its short player-facing label.
    /// </summary>
    public static string For(string missionType) => missionType switch
    {
        WorldTickWireFormat.MissionType.ScoutRoute => "Scouting",
        WorldTickWireFormat.MissionType.ParleyLocal => "Parley",
        _ => "Mission",
    };
}
