using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="CompanyPanelRosterLogic"/> -- the
/// pure-C# helper that owns the locked M1 compagnie roster order
/// (kira -> dorn -> vell), the cap (3), the drop-validation gate
/// (matching NpcId + empty), and the header text composition (Varn
/// §A.10, locked 2026-05-18).
///
/// <para>
/// <b>What this suite pins (Godot-free).</b>
/// <list type="bullet">
///   <item>Roster order : slot 0 = kira, slot 1 = dorn, slot 2 = vell
///         (Varn §A.10.D1 + §A.8.D3). Reordering would break the
///         visual contract of "slot 0 reserved for kira".</item>
///   <item>Compagnie cap M1 = 3 (Varn §A.10.D2 ; equals
///         <see cref="CompanyPanelRosterLogic.Slots"/>.Count).</item>
///   <item>Drop gate : accept iff the socle's roster NpcId matches
///         the mission's <c>RecruitTargetNpcId</c> AND the socle is
///         currently empty (Varn §A.10.D5).</item>
///   <item>Highlight gate : same shape as the drop gate -- a slot is
///         highlighted iff its roster NpcId matches the active
///         mission target AND the slot is empty.</item>
///   <item>Header text composition : "Compagnie" / "Compagnie -- 1
///         membre" / "Compagnie -- N membres" with French
///         singular/plural inflection (Varn §A.10.D5).</item>
///   <item>Slot lookup helpers : <see cref="CompanyPanelRosterLogic.NpcIdAtSlot"/>
///         + <see cref="CompanyPanelRosterLogic.SlotIndexOf"/> round-trip
///         per roster entry, return null / -1 for out-of-range / unknown.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this suite does NOT pin.</b> The Godot-bound CompanyPanel
/// scene-tree build, the NpcRegistry subscription, the re-render on
/// NpcStateChanged, and the rim glow visual -- all need the Godot
/// runtime. Validated via the étape 7 smoke checklist (3 socles
/// visible, highlight ciblé, drop ciblé OK, drop wrong-slot rejeté,
/// close+reopen preserves occupancy).
/// </para>
/// </summary>
public sealed class CompanyPanelRosterLogicTests
{
    // ----------------------------------------------------------------
    // R4 test 1 -- the company panel renders three sockets at ready.
    // Pure-C# proxy : assert the roster declares exactly three slots
    // in the locked order. The Godot-bound CompanyPanel iterates this
    // list at _Ready so the scene tree count is derived from this
    // pin. A drift to N=2 or N=4 surfaces here and red-stops the
    // build before the panel is even instantiated.
    // ----------------------------------------------------------------

    [Fact]
    public void Roster_has_three_slots_in_locked_order()
    {
        Assert.Equal(3, CompanyPanelRosterLogic.Slots.Count);
        Assert.Equal("kira", CompanyPanelRosterLogic.Slots[0]);
        Assert.Equal("dorn", CompanyPanelRosterLogic.Slots[1]);
        Assert.Equal("vell", CompanyPanelRosterLogic.Slots[2]);
    }

    [Fact]
    public void Company_cap_m1_equals_roster_length()
    {
        // Single-source the cap so a future M2 widening (e.g. +1 NPC)
        // moves both the roster list and the cap atomically.
        Assert.Equal(3, CompanyPanelRosterLogic.CompanyCapM1);
        Assert.Equal(
            CompanyPanelRosterLogic.Slots.Count,
            CompanyPanelRosterLogic.CompanyCapM1);
    }

    // ----------------------------------------------------------------
    // R4 test 2 -- sockets read recruitment state from npc registry.
    // Proxy via NpcIdAtSlot : the Godot-bound panel reads
    // NpcRegistry.GetState(NpcIdAtSlot(i)) at _Ready ; a wrong slot-to-
    // NpcId mapping would render the wrong character on the wrong socle.
    // ----------------------------------------------------------------

    [Fact]
    public void NpcIdAtSlot_returns_expected_id_per_index()
    {
        Assert.Equal("kira", CompanyPanelRosterLogic.NpcIdAtSlot(0));
        Assert.Equal("dorn", CompanyPanelRosterLogic.NpcIdAtSlot(1));
        Assert.Equal("vell", CompanyPanelRosterLogic.NpcIdAtSlot(2));
    }

    [Fact]
    public void NpcIdAtSlot_returns_null_for_out_of_range_index()
    {
        Assert.Null(CompanyPanelRosterLogic.NpcIdAtSlot(-1));
        Assert.Null(CompanyPanelRosterLogic.NpcIdAtSlot(3));
        Assert.Null(CompanyPanelRosterLogic.NpcIdAtSlot(int.MaxValue));
    }

    [Fact]
    public void SlotIndexOf_round_trips_per_roster_entry()
    {
        Assert.Equal(0, CompanyPanelRosterLogic.SlotIndexOf("kira"));
        Assert.Equal(1, CompanyPanelRosterLogic.SlotIndexOf("dorn"));
        Assert.Equal(2, CompanyPanelRosterLogic.SlotIndexOf("vell"));
    }

    [Fact]
    public void SlotIndexOf_returns_minus_one_for_unknown_or_null()
    {
        Assert.Equal(-1, CompanyPanelRosterLogic.SlotIndexOf("ghost"));
        Assert.Equal(-1, CompanyPanelRosterLogic.SlotIndexOf(null));
        Assert.Equal(-1, CompanyPanelRosterLogic.SlotIndexOf(string.Empty));
    }

    // ----------------------------------------------------------------
    // R4 test 4 -- drop on occupied socle is rejected.
    // ----------------------------------------------------------------

    [Fact]
    public void Drop_on_occupied_socle_is_rejected()
    {
        // Slot 0 (kira) is occupied (IsRecruited=true) ; the mission's
        // target is kira ; the drop still rejects because the slot is
        // already filled. Matches Varn §A.10.D3 row 2.
        Assert.False(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 0,
            missionRecruitTargetNpcId: "kira",
            isSocleOccupied: true));
    }

    // ----------------------------------------------------------------
    // R4 test 5 -- drop on wrong target socle is rejected.
    // ----------------------------------------------------------------

    [Fact]
    public void Drop_on_wrong_target_socle_is_rejected()
    {
        // Mission targets dorn but the player drops on kira's socle
        // (slot 0). Reject. Varn §A.10.D5.
        Assert.False(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 0,
            missionRecruitTargetNpcId: "dorn",
            isSocleOccupied: false));

        // Same but on vell's socle.
        Assert.False(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 2,
            missionRecruitTargetNpcId: "dorn",
            isSocleOccupied: false));
    }

    [Fact]
    public void Drop_on_matching_empty_socle_is_accepted()
    {
        // Mission targets kira, slot 0 is kira's slot, slot is empty.
        // Accept. The happy path.
        Assert.True(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 0,
            missionRecruitTargetNpcId: "kira",
            isSocleOccupied: false));

        Assert.True(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 1,
            missionRecruitTargetNpcId: "dorn",
            isSocleOccupied: false));

        Assert.True(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 2,
            missionRecruitTargetNpcId: "vell",
            isSocleOccupied: false));
    }

    [Fact]
    public void Drop_rejects_null_or_empty_mission_target()
    {
        // No active mission target (mission panel closed) -> no drop.
        Assert.False(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 0,
            missionRecruitTargetNpcId: null,
            isSocleOccupied: false));

        Assert.False(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 0,
            missionRecruitTargetNpcId: string.Empty,
            isSocleOccupied: false));
    }

    [Fact]
    public void Drop_rejects_out_of_range_socle_index()
    {
        Assert.False(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: -1,
            missionRecruitTargetNpcId: "kira",
            isSocleOccupied: false));

        Assert.False(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 3,
            missionRecruitTargetNpcId: "kira",
            isSocleOccupied: false));
    }

    // ----------------------------------------------------------------
    // R4 test 6 -- highlight lights socle matching active mission target.
    // ----------------------------------------------------------------

    [Fact]
    public void Highlight_lights_socle_matching_active_mission_target()
    {
        // Mission targets dorn -> slot 1 (dorn) glows ; slots 0 + 2
        // stay quiet. Varn §A.10.D5 slot-targeted highlight.
        Assert.True(CompanyPanelRosterLogic.ShouldHighlightSocle(
            socleIndex: 1,
            activeMissionRecruitTargetNpcId: "dorn",
            isSocleOccupied: false));

        Assert.False(CompanyPanelRosterLogic.ShouldHighlightSocle(
            socleIndex: 0,
            activeMissionRecruitTargetNpcId: "dorn",
            isSocleOccupied: false));

        Assert.False(CompanyPanelRosterLogic.ShouldHighlightSocle(
            socleIndex: 2,
            activeMissionRecruitTargetNpcId: "dorn",
            isSocleOccupied: false));
    }

    [Fact]
    public void Highlight_off_when_slot_is_already_recruited()
    {
        // dorn already recruited -> no point highlighting his slot
        // (the drop would reject anyway). Mirrors the drop gate's
        // "empty" requirement.
        Assert.False(CompanyPanelRosterLogic.ShouldHighlightSocle(
            socleIndex: 1,
            activeMissionRecruitTargetNpcId: "dorn",
            isSocleOccupied: true));
    }

    [Fact]
    public void Highlight_off_when_no_active_mission()
    {
        // No mission panel open -> no slot highlighted, regardless of
        // occupancy.
        for (int i = 0; i < CompanyPanelRosterLogic.CompanyCapM1; i++)
        {
            Assert.False(CompanyPanelRosterLogic.ShouldHighlightSocle(
                socleIndex: i,
                activeMissionRecruitTargetNpcId: null,
                isSocleOccupied: false));
        }
    }

    // ----------------------------------------------------------------
    // Header text composition (Varn §A.10.D5).
    // ----------------------------------------------------------------

    [Fact]
    public void Header_text_zero_recruits_is_baseline()
    {
        // Symmetric with CompanyPanelOccupiedLogic.EmptyHeaderText.
        Assert.Equal(
            CompanyPanelOccupiedLogic.EmptyHeaderText,
            CompanyPanelRosterLogic.ComposeHeaderText(0));
    }

    [Fact]
    public void Header_text_one_recruit_is_singular()
    {
        Assert.Equal("Compagnie — 1 membre", CompanyPanelRosterLogic.ComposeHeaderText(1));
    }

    [Fact]
    public void Header_text_multiple_recruits_is_plural()
    {
        Assert.Equal("Compagnie — 2 membres", CompanyPanelRosterLogic.ComposeHeaderText(2));
        Assert.Equal("Compagnie — 3 membres", CompanyPanelRosterLogic.ComposeHeaderText(3));
    }

    [Fact]
    public void Header_text_negative_falls_back_to_baseline()
    {
        // Defensive : a buggy caller passing a negative count should
        // not crash ; it should fall back to the empty baseline.
        Assert.Equal(
            CompanyPanelOccupiedLogic.EmptyHeaderText,
            CompanyPanelRosterLogic.ComposeHeaderText(-1));
    }

    // ----------------------------------------------------------------
    // R4 test 7 -- backend accept failure does not write IsRecruited.
    // Pure-C# proxy : pin the sequencing rule via the helper's
    // contract -- a drop validates only when the slot is empty, so a
    // failed Accept (= IsRecruited stays false) keeps the slot
    // available for a retry. The Godot-bound MissionRecruitPanel's
    // FireAcceptAsync only calls MutateState on the success branch
    // (no write on failure -> no revert needed). Validated via the
    // étape 7 smoke (toggle FastAPI off, click Accept, observe socle
    // stays empty + no NpcStateChanged log line).
    // ----------------------------------------------------------------

    [Fact]
    public void Failed_accept_leaves_slot_droppable_again()
    {
        // After a failed Accept, the slot stays IsRecruited=false ->
        // the drop gate accepts a retry. Implicit "revert" : we never
        // wrote on failure, nothing to revert.
        Assert.True(CompanyPanelRosterLogic.CanDropOnSocle(
            socleIndex: 0,
            missionRecruitTargetNpcId: "kira",
            isSocleOccupied: false /* failed Accept = registry untouched */));
    }
}
