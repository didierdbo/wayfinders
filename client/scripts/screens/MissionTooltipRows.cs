using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// E2.2 step 2 (2026-05-17) -- pure-C# structured form of the E2 mission
/// tooltip. Step 1's single-string composer (<see cref="HalfgateE2MissionTooltip"/>)
/// renders the same content into one multi-line string ; step 2 needs each
/// mission rendered as its own <i>interactive row</i> (hover-highlight,
/// cursor=pointing-hand, click). The single-string shape cannot back per-row
/// highlight/click — we need a typed list of rows the Godot-bound builder
/// can iterate to spawn one Button per row.
///
/// <para>
/// <b>Section A refactor (Varn-lock 2026-05-17 + Didier brief 2026-05-17).</b>
/// The builder pivoted from the hardcoded
/// <c>HalfgateE2MissionAuthoring</c> record (now retired) to the
/// wire DTO. The row record's three slots stay (<see cref="MissionTooltipRow.MissionId"/>
/// / <see cref="MissionTooltipRow.DisplayName"/> /
/// <see cref="MissionTooltipRow.NarrativeHook"/>) ; the values are now
/// derived from <see cref="EmergentMissionDto"/> :
/// <list type="bullet">
///   <item><c>MissionId</c> ← <see cref="EmergentMissionDto.Id"/></item>
///   <item><c>DisplayName</c> ← <see cref="MissionDisplayNames.For"/> with
///         <see cref="EmergentMissionDto.Type"/> as the input</item>
///   <item><c>NarrativeHook</c> ← <see cref="EmergentMissionDto.NarrativeHook"/></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Forward-compatibility.</b> The row record carries
/// <see cref="MissionTooltipRow.MissionId"/> so the click handler can
/// dispatch to a mission-panel-slide-in without going back to the
/// authoring lookup. The DisplayName + NarrativeHook are baked in at
/// row-creation time so the Godot-bound builder has zero authoring
/// dependency.
/// </para>
/// </summary>
public static class MissionTooltipRows
{
    /// <summary>
    /// Build the structured tooltip payload for a hovered POI marker.
    /// </summary>
    /// <param name="districtDisplayName">Header line (already
    /// localised/resolved by the caller via
    /// <see cref="DistrictTypeHelpers.DisplayName"/>). Verbatim into
    /// <see cref="MissionTooltipPayload.DistrictDisplayName"/>.</param>
    /// <param name="missions">Missions anchored on the hovered POI.
    /// Iterated in supplied order -- caller (or
    /// <see cref="MissionStore.GetMissionsForPoi"/>'s server-spawn order)
    /// owns the sort key. Null or empty yields a payload with an empty
    /// row list (the Godot-bound caller then suppresses the tooltip
    /// rather than rendering an empty card, matching the step 1
    /// "pas de tooltip si pas de mission" contract).</param>
    public static MissionTooltipPayload Compose(
        string districtDisplayName,
        IEnumerable<EmergentMissionDto>? missions)
    {
        if (missions is null)
        {
            return new MissionTooltipPayload(
                districtDisplayName,
                System.Array.Empty<MissionTooltipRow>());
        }

        var rows = missions
            .Select(m => new MissionTooltipRow(
                MissionId: m.Id,
                DisplayName: MissionDisplayNames.For(m.Type),
                NarrativeHook: m.NarrativeHook))
            .ToList();

        return new MissionTooltipPayload(districtDisplayName, rows);
    }
}

/// <summary>
/// One row in the E2 mission tooltip. Mirrors the subset of
/// <see cref="EmergentMissionDto"/> the tooltip actually surfaces, plus
/// the stable <see cref="MissionId"/> the click handler dispatches on.
/// Pure-data record so the Godot-bound builder can iterate cheaply.
/// </summary>
/// <param name="MissionId">Stable mission identifier (from
/// <see cref="EmergentMissionDto.Id"/>). Forward-routed to the click
/// handler.</param>
/// <param name="DisplayName">Player-facing short label (synthesised from
/// the mission's type via <see cref="MissionDisplayNames"/>, 1 line).</param>
/// <param name="NarrativeHook">Server-rendered narrative hook
/// (Pratchett-tone, indented under the DisplayName in the rendered row).</param>
public sealed record MissionTooltipRow(
    string MissionId,
    string DisplayName,
    string NarrativeHook);

/// <summary>
/// Structured tooltip payload : the district header + the ordered list of
/// rows. The Godot-bound builder reads this to instantiate one
/// <c>Label</c> for the header and one <c>Button</c> per row inside a
/// shared <c>VBoxContainer</c>.
/// </summary>
/// <param name="DistrictDisplayName">Header text rendered at the top of
/// the tooltip panel.</param>
/// <param name="Rows">Ordered mission rows. May be empty -- callers
/// should suppress the tooltip in that case (matches step 1 empty-string
/// contract).</param>
public sealed record MissionTooltipPayload(
    string DistrictDisplayName,
    IReadOnlyList<MissionTooltipRow> Rows);
