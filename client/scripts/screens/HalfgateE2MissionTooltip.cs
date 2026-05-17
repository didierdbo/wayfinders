using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// E2.2 step 1 (2026-05-17) -- compose the tooltip text shown when the
/// player hovers an E2-layer POI marker. Pure-C#, Godot-free, mirrors the
/// <see cref="HalfgateRumorHooks"/> seam : the Godot-bound hover handler
/// in <c>E2AreaMap.AreaGrid.cs</c> calls <see cref="Compose"/> with the
/// list of <see cref="HalfgateE2Mission"/>s anchored on the hovered POI,
/// and feeds the result to <c>HoverTooltipController.RequestTooltip</c>.
///
/// <para>
/// <b>Layout (E2.2 step 1 lock).</b> Header line = district display name,
/// then one bullet per mission with the mission's
/// <see cref="HalfgateE2Mission.DisplayName"/> on its own line followed by
/// the indented <see cref="HalfgateE2Mission.NarrativeHook"/>. Two missions
/// on the same POI are separated by a blank line so the wall of text stays
/// readable.
/// </para>
///
/// <para>
/// <b>Empty-list contract.</b> Returns <see cref="string.Empty"/> when no
/// mission is supplied. The hover handler in <c>E2AreaMap.AreaGrid.cs</c>
/// checks for empty and skips the
/// <c>HoverTooltipController.RequestTooltip</c> call -- satisfies the
/// "no tooltip if no mission available" spec from the E2.2 step 1 brief
/// without surfacing a degenerate "no missions" panel.
/// </para>
///
/// <para>
/// <b>Tonal DNA constraint.</b> The mission strings (DisplayName +
/// NarrativeHook) are authored in <see cref="HalfgateE2MissionAuthoring"/>
/// against the Pratchett-tone budget (pinned by
/// <see cref="Wayfinders.Client.Tests.Opening.HalfgateE2MissionAuthoringTests.Every_narrative_hook_fits_the_pratchett_tone_budget"/>).
/// This composer adds no decoration beyond a leading bullet glyph and the
/// district label header -- the voice belongs to the mission strings.
/// </para>
///
/// <para>
/// <b>Why this lives next to <see cref="HalfgateRumorHooks"/> and not
/// inside the partial.</b> Pure-C#, Godot-free, trivially testable. Same
/// rationale as <see cref="HalfgateRumorHooks.ComposeCellTooltip"/> :
/// the engine-coupled hover handler is a thin adapter ; the prose
/// composition is xUnit-pinned so a silent edit to the layout surfaces
/// as a red step.
/// </para>
/// </summary>
public static class HalfgateE2MissionTooltip
{
    /// <summary>
    /// Bullet glyph prefixed to each mission DisplayName. Plain ASCII
    /// hyphen-bullet keeps the font requirements minimal (no unicode
    /// dependency) and renders cleanly in the parchment-tone
    /// <c>HoverTooltip.tscn</c> Label.
    /// </summary>
    internal const string MissionBullet = "- ";

    /// <summary>
    /// Indent applied to the narrative hook line under each mission
    /// DisplayName. Two spaces line up under the start of the
    /// DisplayName (the bullet glyph + space is 2 chars wide). A tab
    /// would also work but is renderer-dependent in the Label control ;
    /// spaces are safe.
    /// </summary>
    internal const string HookIndent = "  ";

    /// <summary>
    /// Compose the tooltip text for a POI hovering surface.
    /// </summary>
    /// <param name="districtDisplayName">Human-readable header line.
    /// Resolved by the caller via
    /// <see cref="DistrictTypeHelpers.DisplayName"/>. Pre-resolved (rather
    /// than passing the enum) so this composer stays single-responsibility
    /// and the caller controls localisation if it ever lands.</param>
    /// <param name="missions">Missions anchored on the hovered POI. Empty
    /// or null yields <see cref="string.Empty"/>. Iterated in the supplied
    /// order -- the caller (or
    /// <see cref="HalfgateE2MissionAuthoring.All"/>'s static ordering) owns
    /// the sort key.</param>
    /// <returns>Final tooltip text (multi-line), or
    /// <see cref="string.Empty"/> when there is nothing to show.</returns>
    public static string Compose(
        string districtDisplayName,
        IEnumerable<HalfgateE2Mission>? missions)
    {
        if (missions is null) return string.Empty;

        var missionList = missions as IList<HalfgateE2Mission> ?? missions.ToList();
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
            sb.Append(m.DisplayName);
            sb.Append('\n');
            sb.Append(HookIndent);
            sb.Append(m.NarrativeHook);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Convenience filter : pick the subset of <paramref name="missions"/>
    /// whose <see cref="HalfgateE2Mission.PoiId"/> matches the supplied
    /// <paramref name="poiId"/>. Forward-compatible with the E2.2+ shape
    /// where multiple missions can share a POI (e.g. two simultaneous
    /// Gateway missions). At E2.1c the lookup yields exactly one mission
    /// per POI (one mission per district, one marker per district).
    /// </summary>
    /// <param name="missions">Mission set to filter
    /// (<see cref="HalfgateE2MissionAuthoring.All"/> at E2.1c).</param>
    /// <param name="poiId">Stable POI identifier (the
    /// <see cref="HalfgateE2Mission.PoiId"/> the marker was spawned with).</param>
    /// <returns>Missions whose <c>PoiId</c> matches, in source order.</returns>
    public static IEnumerable<HalfgateE2Mission> FindByPoiId(
        IEnumerable<HalfgateE2Mission> missions,
        string poiId)
    {
        if (missions is null) yield break;
        foreach (var m in missions)
        {
            if (m is not null && m.PoiId == poiId) yield return m;
        }
    }
}
