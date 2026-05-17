using System;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# composer for the "occupied confirmed" payload the
/// <c>CompanyPanel</c> applies to its <c>IsoSocketPlaceholder</c>
/// once the player presses Accept and the backend
/// <c>POST /api/missions/&lt;id&gt;/accept</c> round-trip succeeds
/// (E2.2 step 6, Didier-lock 2026-05-17). Mirrors the
/// <see cref="MissionRecruitPanelLogic.Compose"/> pattern -- the
/// Godot-bound panel binds a structured payload built by a Godot-free
/// helper, so xUnit pins the projection independently of any engine
/// runtime.
///
/// <para>
/// <b>What this helper pins.</b>
/// <list type="bullet">
///   <item><see cref="CompanyPanelOccupiedPayload.NpcId"/> = passthrough
///         of the dropped NPC id (drives the socle's child
///         IsoCharacterPlaceholder tint via
///         <see cref="IsoCharacterPaletteLogic"/>).</item>
///   <item><see cref="CompanyPanelOccupiedPayload.DisplayName"/> =
///         resolved via <see cref="NpcCatalog.LookupDisplayName"/>.
///         Renders in the small label under the socle.</item>
///   <item><see cref="CompanyPanelOccupiedPayload.HeaderText"/> =
///         "Compagnie -- 1 membre" once the first perso is recruited.
///         The "0 membre" baseline is the panel's default header
///         ("Compagnie") so the helper does not produce a "0 membre"
///         payload -- empty socle = call <c>SetEmpty</c>, not
///         <see cref="Compose"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Defensive defaults.</b> A null or empty NPC id throws -- the
/// runtime contract is "Compose is called only on a confirmed
/// recruit-accept success, with the NPC id that travelled in the drag
/// payload". A null id at this layer is a contract violation that
/// should never reach prod ; surface it loudly rather than rendering
/// "Inconnu" on the socle (which would look like a successful recruit
/// of a corrupt entry).
/// </para>
/// </summary>
public static class CompanyPanelOccupiedLogic
{
    /// <summary>
    /// Compose the occupied-state payload. M1 has a single socle, so
    /// the member count is always 1 after a successful recruit ; the
    /// helper takes a member count parameter so M2+ (multi-slot
    /// Compagnie panel) extends the header format without a public
    /// API change.
    /// </summary>
    public static CompanyPanelOccupiedPayload Compose(
        string npcId,
        int memberCount = 1)
    {
        if (string.IsNullOrEmpty(npcId))
            throw new ArgumentException("npcId required", nameof(npcId));
        if (memberCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(memberCount),
                "memberCount must be >= 1 for an occupied payload");

        var displayName = NpcCatalog.LookupDisplayName(npcId);
        var noun = memberCount == 1 ? "membre" : "membres";
        var header = $"Compagnie — {memberCount} {noun}";

        return new CompanyPanelOccupiedPayload(
            NpcId: npcId,
            DisplayName: displayName,
            HeaderText: header);
    }

    /// <summary>Baseline header text when the socle is empty. The
    /// CompanyPanel.cs <c>_Ready</c> binds this verbatim ; helper
    /// exposes it as a constant so xUnit can pin "occupied header !=
    /// empty header" symmetrically.</summary>
    public const string EmptyHeaderText = "Compagnie";
}

/// <summary>
/// Structured payload returned by
/// <see cref="CompanyPanelOccupiedLogic.Compose"/>. The Godot-bound
/// <c>CompanyPanel</c> binds <see cref="HeaderText"/> to its header
/// Label, <see cref="DisplayName"/> to a small label under the socle,
/// and feeds <see cref="NpcId"/> to the socle's child
/// IsoCharacterPlaceholder for the unit tint.
/// </summary>
public sealed record CompanyPanelOccupiedPayload(
    string NpcId,
    string DisplayName,
    string HeaderText);
