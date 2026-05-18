using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helper that owns the locked M1 compagnie roster order (Varn
/// §A.10.D1 + §A.8.D3, 2026-05-17/18). The CompanyPanel renders one
/// iso socket per entry, in this exact order : slot 0 = kira, slot 1 =
/// dorn, slot 2 = vell. Order matches the §A.8.D3 mission-emergence
/// roster so the panel's visual layout mirrors the narrative onboarding
/// arc (peripheral commerce → civic core → dockyard outsider).
///
/// <para>
/// <b>Why a pure-C# helper, not a Godot resource.</b> Same separation
/// pattern as <see cref="NpcCatalog"/> + <see cref="CompanyPanelOccupiedLogic"/> :
/// the data + the lookup logic stay engine-free so xUnit pins the
/// contract without spinning a Godot runtime. The Godot-bound
/// <c>CompanyPanel</c> consumes the slot list at <c>_Ready</c>.
/// </para>
///
/// <para>
/// <b>What this helper pins.</b>
/// <list type="bullet">
///   <item>The exact ordered roster (kira → dorn → vell). Reordering
///         would break Varn §A.10.D1's "slot 0 reserved for kira"
///         visual contract.</item>
///   <item>The compagnie cap (3 for M1, matches §A.10.D2). The cap
///         is derived from <see cref="Slots"/>.Count so a single-source
///         edit (M2 widening) fans out without divergence.</item>
///   <item>The drop-validation gate :
///         <see cref="CanDropOnSocle"/> returns true iff the target
///         socle's roster NpcId matches the mission's
///         <c>RecruitTargetNpcId</c> AND the socle is currently empty
///         (Varn §A.10.D5).</item>
///   <item>The highlight gate :
///         <see cref="ShouldHighlightSocle"/> returns true iff a
///         mission targeting that socle's NpcId is currently open AND
///         the socle is empty (Varn §A.10.D5 slot-targeted highlight).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Header count contract (Varn §A.10.D5).</b>
/// <see cref="ComposeHeaderText"/> renders the live member count with
/// French singular/plural inflection : "Compagnie", "Compagnie -- 1 membre",
/// "Compagnie -- N membres" (N >= 2). The zero-recruit baseline is the
/// same string as <see cref="CompanyPanelOccupiedLogic.EmptyHeaderText"/>,
/// pinned symmetrically so a future drift surfaces red.
/// </para>
/// </summary>
public static class CompanyPanelRosterLogic
{
    /// <summary>
    /// Ordered M1 roster (Varn §A.8.D3 lock). Slot index = list index.
    /// <list type="bullet">
    ///   <item>Slot 0 : "kira" (gateway / peripheral commerce).</item>
    ///   <item>Slot 1 : "dorn" (intramuros / civic core).</item>
    ///   <item>Slot 2 : "vell" (littoral / dockyard outsider).</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<string> Slots = new[]
    {
        "kira",
        "dorn",
        "vell",
    };

    /// <summary>M1 compagnie capacity. Equals <see cref="Slots"/>.Count.
    /// Spec lock §A.10.D2.</summary>
    public static int CompanyCapM1 => Slots.Count;

    /// <summary>
    /// Decide whether a mission's recruit target is allowed to land on
    /// the given socle index. Varn §A.10.D5 rule : accept iff the
    /// socle's roster NpcId matches the mission's
    /// <paramref name="missionRecruitTargetNpcId"/> AND the socle is
    /// currently empty (= the NPC is not yet recruited).
    /// </summary>
    /// <param name="socleIndex">0..<see cref="CompanyCapM1"/>-1.</param>
    /// <param name="missionRecruitTargetNpcId">The mission's
    /// <c>RecruitTargetNpcId</c> field. Null / empty rejects.</param>
    /// <param name="isSocleOccupied">Truth from
    /// <c>NpcRegistry.GetState(slotNpcId)?.IsRecruited == true</c>.</param>
    public static bool CanDropOnSocle(
        int socleIndex,
        string? missionRecruitTargetNpcId,
        bool isSocleOccupied)
    {
        if (socleIndex < 0 || socleIndex >= Slots.Count) return false;
        if (string.IsNullOrEmpty(missionRecruitTargetNpcId)) return false;
        if (isSocleOccupied) return false;
        return Slots[socleIndex] == missionRecruitTargetNpcId;
    }

    /// <summary>
    /// Decide whether the given socle should be visually highlighted
    /// (terracotta tint) because a mission panel is currently targeting
    /// it. Varn §A.10.D5 slot-targeted highlight. Same gates as
    /// <see cref="CanDropOnSocle"/> : matching NpcId + empty.
    /// </summary>
    /// <param name="socleIndex">0..<see cref="CompanyCapM1"/>-1.</param>
    /// <param name="activeMissionRecruitTargetNpcId">The currently-open
    /// mission panel's <c>RecruitTargetNpcId</c>. Null if no mission
    /// panel is open ; that case returns false for every socle.</param>
    /// <param name="isSocleOccupied">Truth from NpcRegistry.</param>
    public static bool ShouldHighlightSocle(
        int socleIndex,
        string? activeMissionRecruitTargetNpcId,
        bool isSocleOccupied)
        => CanDropOnSocle(socleIndex, activeMissionRecruitTargetNpcId, isSocleOccupied);

    /// <summary>
    /// Compose the header text from the live recruit count. Varn
    /// §A.10.D5 :
    /// <list type="bullet">
    ///   <item>0 -> "Compagnie" (baseline).</item>
    ///   <item>1 -> "Compagnie -- 1 membre" (singular).</item>
    ///   <item>N (>=2) -> "Compagnie -- N membres" (plural).</item>
    /// </list>
    /// </summary>
    public static string ComposeHeaderText(int recruitedCount)
    {
        if (recruitedCount <= 0) return CompanyPanelOccupiedLogic.EmptyHeaderText;
        var noun = recruitedCount == 1 ? "membre" : "membres";
        return $"Compagnie — {recruitedCount} {noun}";
    }

    /// <summary>
    /// Roster NpcId at the given socle index, or null if out-of-range.
    /// </summary>
    public static string? NpcIdAtSlot(int socleIndex)
        => (socleIndex < 0 || socleIndex >= Slots.Count) ? null : Slots[socleIndex];

    /// <summary>
    /// Slot index of the given NpcId in the roster, or -1 if not in
    /// roster. Used by R2 to translate a mission's
    /// <c>RecruitTargetNpcId</c> into the socle index to highlight.
    /// </summary>
    public static int SlotIndexOf(string? npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return -1;
        for (int i = 0; i < Slots.Count; i++)
        {
            if (Slots[i] == npcId) return i;
        }
        return -1;
    }
}
