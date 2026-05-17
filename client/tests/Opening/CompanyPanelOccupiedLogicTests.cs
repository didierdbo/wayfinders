using System;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="CompanyPanelOccupiedLogic"/> -- the
/// projection from a confirmed recruit-accept (NPC id) to the visual
/// payload the company panel applies post-accept (E2.2 step 6,
/// Didier-lock 2026-05-17). Mirrors the discipline of
/// <see cref="MissionRecruitPanelLogicTests"/>.
/// </summary>
public sealed class CompanyPanelOccupiedLogicTests
{
    [Fact]
    public void Compose_kira_yields_singular_header_and_display_name()
    {
        var payload = CompanyPanelOccupiedLogic.Compose("kira");

        Assert.Equal("kira", payload.NpcId);
        Assert.Equal("Kira", payload.DisplayName);
        Assert.Equal("Compagnie — 1 membre", payload.HeaderText);
    }

    [Fact]
    public void Compose_dorn_yields_dorn_display_name()
    {
        var payload = CompanyPanelOccupiedLogic.Compose("dorn");
        Assert.Equal("Dorn", payload.DisplayName);
        Assert.Equal("Compagnie — 1 membre", payload.HeaderText);
    }

    [Fact]
    public void Compose_vell_yields_vell_display_name()
    {
        var payload = CompanyPanelOccupiedLogic.Compose("vell");
        Assert.Equal("Vell", payload.DisplayName);
    }

    [Fact]
    public void Compose_two_members_pluralizes_header()
    {
        // M2+ multi-slot Compagnie panel : the helper extends to
        // "Compagnie -- 2 membres" without a public API change. Pin
        // the plural form so a future regression that hard-codes
        // "1 membre" surfaces as a red test.
        var payload = CompanyPanelOccupiedLogic.Compose("kira", memberCount: 2);
        Assert.Equal("Compagnie — 2 membres", payload.HeaderText);
    }

    [Fact]
    public void Compose_three_members_keeps_pluralization()
    {
        var payload = CompanyPanelOccupiedLogic.Compose("kira", memberCount: 3);
        Assert.Equal("Compagnie — 3 membres", payload.HeaderText);
    }

    [Fact]
    public void Compose_unknown_npc_falls_back_to_inconnu_display_name()
    {
        // The NpcCatalog default surfaces "Inconnu" -- a panel
        // displaying an "Inconnu" recruit reveals a contract bug at
        // smoke time. Pin the passthrough so a future "filter unknowns"
        // change must update this test deliberately.
        var payload = CompanyPanelOccupiedLogic.Compose("wattan");
        Assert.Equal("wattan", payload.NpcId);
        Assert.Equal(NpcCatalog.DefaultDisplayName, payload.DisplayName);
    }

    [Fact]
    public void Compose_throws_on_null_npc_id()
    {
        Assert.Throws<ArgumentException>(() => CompanyPanelOccupiedLogic.Compose(null!));
    }

    [Fact]
    public void Compose_throws_on_empty_npc_id()
    {
        Assert.Throws<ArgumentException>(() => CompanyPanelOccupiedLogic.Compose(string.Empty));
    }

    [Fact]
    public void Compose_throws_on_zero_member_count()
    {
        // memberCount = 0 means "no occupants" -- not a valid input for
        // the occupied helper. The runtime calls SetEmpty() in that case.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CompanyPanelOccupiedLogic.Compose("kira", memberCount: 0));
    }

    [Fact]
    public void Empty_header_text_is_baseline_string()
    {
        // Pin the baseline so the panel's _Ready binding cannot drift
        // away from the helper's view of the empty header.
        Assert.Equal("Compagnie", CompanyPanelOccupiedLogic.EmptyHeaderText);
    }
}
