using System.Collections.Generic;
using System.Linq;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// E2.2 step 2 (2026-05-17) -- pure-C# structured form of the E2 mission
/// tooltip. Step 1 collapsed every mission for a POI into a single
/// multi-line string via <see cref="HalfgateE2MissionTooltip.Compose"/> ;
/// step 2 needs each mission rendered as its own <i>interactive row</i>
/// (hover-highlight, cursor=pointing-hand, future click). The single-string
/// shape cannot back per-row highlight/click -- we need a typed list of
/// rows the Godot-bound builder can iterate to spawn one Button per row.
///
/// <para>
/// <b>Why a NEW helper and not a refactor of
/// <see cref="HalfgateE2MissionTooltip"/>.</b> The step 1 composer is
/// pinned by 11 xUnit tests as the canonical "compose a single tooltip
/// string for a list of missions" surface. The 5 OTHER callers of
/// <c>HoverTooltipController.RequestTooltip</c> (E1 POI, E2 cell, E2 NPC
/// portrait, E3 POI, plus the legacy E2 dispatch path) still consume a
/// single string -- they have no per-row interactive shape and do not need
/// one this slice. Keeping <see cref="HalfgateE2MissionTooltip"/> stable
/// preserves those tests AND those callers ; the new structured surface
/// lives next to it for the one caller (E2 POI marker hover) that needs
/// rows. When E3 or another screen later needs interactive rows, this
/// helper is reused -- pas avant.
/// </para>
///
/// <para>
/// <b>Forward-compatibility.</b> The row record carries
/// <see cref="MissionTooltipRow.MissionId"/> so the future step 3 click
/// handler can dispatch to a mission-panel-slide-in without going back to
/// the authoring lookup. The DisplayName + NarrativeHook are baked in at
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
    /// <see cref="HalfgateE2MissionAuthoring.All"/>'s static ordering)
    /// owns the sort key. Null or empty yields a payload with an empty
    /// row list (the Godot-bound caller then suppresses the tooltip
    /// rather than rendering an empty card, matching the step 1
    /// "pas de tooltip si pas de mission" contract).</param>
    public static MissionTooltipPayload Compose(
        string districtDisplayName,
        IEnumerable<HalfgateE2Mission>? missions)
    {
        if (missions is null)
        {
            return new MissionTooltipPayload(
                districtDisplayName,
                System.Array.Empty<MissionTooltipRow>());
        }

        var rows = missions
            .Select(m => new MissionTooltipRow(
                MissionId: m.MissionId,
                DisplayName: m.DisplayName,
                NarrativeHook: m.NarrativeHook))
            .ToList();

        return new MissionTooltipPayload(districtDisplayName, rows);
    }
}

/// <summary>
/// One row in the E2 mission tooltip. Mirrors the subset of
/// <see cref="HalfgateE2Mission"/> the tooltip actually surfaces, plus the
/// stable <see cref="MissionId"/> the future click handler will dispatch
/// on. Pure-data record so the Godot-bound builder can iterate cheaply.
/// </summary>
/// <param name="MissionId">Stable mission identifier. Forward-routed to
/// the step 3 click handler.</param>
/// <param name="DisplayName">Player-facing title (Pratchett-tone, 1
/// line).</param>
/// <param name="NarrativeHook">Single-line rumor hook (Pratchett-tone,
/// indented under the DisplayName in the rendered row).</param>
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
