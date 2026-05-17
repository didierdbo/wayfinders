using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="IsoSocketDropLogic"/> -- the pure-C# shape
/// validation for the E2.2 step 6 drag payload flowing between the
/// recruit panel's IsoCharacterPlaceholder (source) and the company
/// panel's IsoSocketPlaceholder (target). The truth table covers the
/// seven reject paths + the accept path ; the reverse-direction helper
/// gets symmetric coverage.
/// </summary>
public sealed class IsoSocketDropLogicTests
{
    // The M1 roster Varn §A.8.D3 lock 2026-05-17. The runtime feeds
    // the same set via NpcCatalog ; tests pin the literal contents so
    // a future silent edit of either side breaks the pin.
    private static readonly IReadOnlySet<string> RosterM1 =
        new HashSet<string> { "kira", "dorn", "vell" };

    // --------------------------------------------------------------------
    // CanAccept -- drop on the company socle (forward path).
    // --------------------------------------------------------------------

    [Fact]
    public void Accepts_well_formed_recruit_panel_drop_with_known_npc()
    {
        var payload = new DragPayload(
            NpcId: "kira",
            MissionId: "m-1",
            Source: IsoSocketDropLogic.DragSourceRecruitPanel);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.Accept,
            IsoSocketDropLogic.CanAccept(payload, RosterM1));
    }

    [Fact]
    public void Rejects_null_payload()
    {
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.RejectMalformedPayload,
            IsoSocketDropLogic.CanAccept(null, RosterM1));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_missing_npc_id(string? npcId)
    {
        var payload = new DragPayload(
            NpcId: npcId!,
            MissionId: "m-1",
            Source: IsoSocketDropLogic.DragSourceRecruitPanel);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.RejectMissingNpcId,
            IsoSocketDropLogic.CanAccept(payload, RosterM1));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_missing_source(string? source)
    {
        var payload = new DragPayload(
            NpcId: "kira",
            MissionId: "m-1",
            Source: source!);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.RejectMissingSource,
            IsoSocketDropLogic.CanAccept(payload, RosterM1));
    }

    [Theory]
    [InlineData("roster_panel")]
    [InlineData("tactical_scene")]
    [InlineData("RECRUIT_PANEL")]  // case-sensitive
    public void Rejects_unknown_source_string(string source)
    {
        var payload = new DragPayload(
            NpcId: "kira",
            MissionId: "m-1",
            Source: source);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.RejectUnknownSource,
            IsoSocketDropLogic.CanAccept(payload, RosterM1));
    }

    [Fact]
    public void Rejects_self_drop_from_company_socle()
    {
        // The forward path (drop on socle) refuses a drag that
        // originated on the socle. Reverse-drag-back is the
        // IsoCharacterPlaceholder's job, not the socle's.
        var payload = new DragPayload(
            NpcId: "kira",
            MissionId: "m-1",
            Source: IsoSocketDropLogic.DragSourceCompanySocle);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.RejectSelfDrop,
            IsoSocketDropLogic.CanAccept(payload, RosterM1));
    }

    [Theory]
    [InlineData("wattan")]
    [InlineData("Kira")]      // case-sensitive
    [InlineData("kira ")]     // trailing whitespace
    public void Rejects_unknown_npc_id(string npcId)
    {
        var payload = new DragPayload(
            NpcId: npcId,
            MissionId: "m-1",
            Source: IsoSocketDropLogic.DragSourceRecruitPanel);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.RejectUnknownNpc,
            IsoSocketDropLogic.CanAccept(payload, RosterM1));
    }

    // --------------------------------------------------------------------
    // CanAcceptReverse -- drop back on the recruit panel placeholder.
    // --------------------------------------------------------------------

    [Fact]
    public void Reverse_accepts_well_formed_company_socle_drop()
    {
        var payload = new DragPayload(
            NpcId: "dorn",
            MissionId: "m-2",
            Source: IsoSocketDropLogic.DragSourceCompanySocle);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.Accept,
            IsoSocketDropLogic.CanAcceptReverse(payload, RosterM1));
    }

    [Fact]
    public void Reverse_rejects_self_drop_from_recruit_panel()
    {
        // Dropping the recruit panel's own placeholder onto itself is
        // a no-op the reverse path must refuse.
        var payload = new DragPayload(
            NpcId: "kira",
            MissionId: "m-1",
            Source: IsoSocketDropLogic.DragSourceRecruitPanel);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.RejectSelfDrop,
            IsoSocketDropLogic.CanAcceptReverse(payload, RosterM1));
    }

    [Fact]
    public void Reverse_rejects_unknown_npc()
    {
        var payload = new DragPayload(
            NpcId: "wattan",
            MissionId: "m-1",
            Source: IsoSocketDropLogic.DragSourceCompanySocle);
        Assert.Equal(
            IsoSocketDropLogic.DropDecision.RejectUnknownNpc,
            IsoSocketDropLogic.CanAcceptReverse(payload, RosterM1));
    }

    // --------------------------------------------------------------------
    // Payload key constants -- snake_case wire-format pin.
    // --------------------------------------------------------------------

    [Fact]
    public void Payload_keys_are_snake_case_wire_strings()
    {
        // Belt-and-braces : the snake_case keys are the wire contract
        // between the Godot-side _GetDragData / _CanDropData /
        // _DropData overrides and the pure-C# validation. A typo
        // breaks the boundary silently ; pin the literals.
        Assert.Equal("npc_id", IsoSocketDropLogic.PayloadKeyNpcId);
        Assert.Equal("mission_id", IsoSocketDropLogic.PayloadKeyMissionId);
        Assert.Equal("source", IsoSocketDropLogic.PayloadKeySource);
    }

    [Fact]
    public void Drag_source_constants_are_snake_case_wire_strings()
    {
        Assert.Equal("recruit_panel", IsoSocketDropLogic.DragSourceRecruitPanel);
        Assert.Equal("company_socle", IsoSocketDropLogic.DragSourceCompanySocle);
    }
}
