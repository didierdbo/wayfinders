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
/// wire DTO. The rendered shape stays compact : district header on
/// one line, then one row per mission.
/// </para>
///
/// <para>
/// <b>§A.8.D4 R7 refactor (Varn-lock 2026-05-17 strict mode).</b> The
/// row format changes : the previous "two-line bullet block" (display
/// name on its own line, narrative hook indented under it) is
/// replaced by the §A.8.D4 compact one-liner :
/// <c>{NPC name} — {narrative hook}</c>. For recruit-type missions the
/// NPC name comes from <see cref="NpcCatalog.LookupDisplayName"/> with
/// <see cref="EmergentMissionDto.RecruitTargetNpcId"/> as the input
/// (kira → "Kira", dorn → "Dorn", vell → "Vell"). For M2+
/// non-recruit types the label falls back to the legacy
/// <see cref="MissionDisplayNames.For"/> type-slug mapping.
/// </para>
///
/// <para>
/// <b>Layout.</b>
/// <list type="bullet">
///   <item>Line 1 : district display name (header).</item>
///   <item>Blank line.</item>
///   <item>One line per mission, format
///         <c>"{NPC display name} — {narrative hook}"</c>. Multiple
///         missions land as multiple lines, source order preserved.</item>
/// </list>
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
/// adds no decoration beyond the NPC name + em-dash + the hook ; the
/// voice belongs to the mission strings.
/// </para>
/// </summary>
public static class HalfgateE2MissionTooltip
{
    /// <summary>
    /// Em-dash separator between the NPC display name and the
    /// narrative hook on each compact row. Plain " — " (U+2014 EM
    /// DASH with one space on each side) renders cleanly in the
    /// parchment-tone <c>HoverTooltip.tscn</c> Label.
    /// </summary>
    internal const string RowSeparator = " — ";

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
        sb.Append('\n');

        for (int i = 0; i < missionList.Count; i++)
        {
            var m = missionList[i];
            sb.Append('\n');
            sb.Append(MissionTooltipRows.ResolveDisplayName(m));
            sb.Append(RowSeparator);
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
/// <see cref="EmergentMissionDto.NarrativeHook"/>. This helper survives
/// the §A.8.D4 R7 refactor as the M2+ fallback path :
/// <see cref="MissionTooltipRows.ResolveDisplayName"/> calls it only
/// for non-recruit missions (or recruit missions with a missing
/// <c>recruit_target_npc_id</c>, which the server's M1 contract
/// forbids).
///
/// <para>
/// <b>Locked label set (Varn lookup mirror).</b> Three M1 mission types
/// (<see cref="WorldTickWireFormat.MissionType.Recruit"/> /
/// <see cref="WorldTickWireFormat.MissionType.ScoutRoute"/> /
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
        WorldTickWireFormat.MissionType.Recruit => "Recrutement",
        WorldTickWireFormat.MissionType.ScoutRoute => "Scouting",
        WorldTickWireFormat.MissionType.ParleyLocal => "Parley",
        _ => "Mission",
    };
}
