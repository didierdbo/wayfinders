using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="MissionRecruitPanelStateLogic"/> -- the
/// panel-scoped 2-state machine that drives the E2.2 step 6 drag&amp;drop
/// visual feedback (perso silhouette greying + Accept button highlight).
/// Six pins cover the full transition table ; two pins cover the
/// state-to-visual projection (greyed-out + highlight).
/// </summary>
public sealed class MissionRecruitPanelStateLogicTests
{
    // --------------------------------------------------------------------
    // Transition table -- pin every (state, event) pair the spec defines.
    // --------------------------------------------------------------------

    [Fact]
    public void Initial_plus_dropped_yields_dropped_ready()
    {
        var next = MissionRecruitPanelStateLogic.Next(
            MissionRecruitPanelState.Initial,
            MissionRecruitPanelEvent.CharacterDroppedOnSocle);
        Assert.Equal(MissionRecruitPanelState.DroppedReadyToValidate, next);
    }

    [Fact]
    public void Initial_plus_returned_stays_initial_noop()
    {
        // Defensive : a return-from-socle event arriving in Initial state
        // is a no-op (the perso was never on the socle to begin with).
        var next = MissionRecruitPanelStateLogic.Next(
            MissionRecruitPanelState.Initial,
            MissionRecruitPanelEvent.CharacterReturnedFromSocle);
        Assert.Equal(MissionRecruitPanelState.Initial, next);
    }

    [Fact]
    public void Dropped_plus_dropped_is_idempotent()
    {
        // Idempotent : a second drop event (e.g. drag-and-drop cycle
        // without an intermediate release) keeps the panel in dropped.
        var next = MissionRecruitPanelStateLogic.Next(
            MissionRecruitPanelState.DroppedReadyToValidate,
            MissionRecruitPanelEvent.CharacterDroppedOnSocle);
        Assert.Equal(MissionRecruitPanelState.DroppedReadyToValidate, next);
    }

    [Fact]
    public void Dropped_plus_returned_yields_initial()
    {
        var next = MissionRecruitPanelStateLogic.Next(
            MissionRecruitPanelState.DroppedReadyToValidate,
            MissionRecruitPanelEvent.CharacterReturnedFromSocle);
        Assert.Equal(MissionRecruitPanelState.Initial, next);
    }

    [Fact]
    public void Panel_opened_resets_to_initial_from_initial()
    {
        var next = MissionRecruitPanelStateLogic.Next(
            MissionRecruitPanelState.Initial,
            MissionRecruitPanelEvent.PanelOpened);
        Assert.Equal(MissionRecruitPanelState.Initial, next);
    }

    [Fact]
    public void Panel_opened_resets_to_initial_from_dropped()
    {
        // Reopening the panel with a different mission must wipe the
        // dropped state -- the previous mission's perso is not on the
        // current Compagnie socle.
        var next = MissionRecruitPanelStateLogic.Next(
            MissionRecruitPanelState.DroppedReadyToValidate,
            MissionRecruitPanelEvent.PanelOpened);
        Assert.Equal(MissionRecruitPanelState.Initial, next);
    }

    [Fact]
    public void Panel_closed_resets_to_initial_from_initial()
    {
        var next = MissionRecruitPanelStateLogic.Next(
            MissionRecruitPanelState.Initial,
            MissionRecruitPanelEvent.PanelClosed);
        Assert.Equal(MissionRecruitPanelState.Initial, next);
    }

    [Fact]
    public void Panel_closed_resets_to_initial_from_dropped()
    {
        // Closing the panel mid-drop (e.g. X clicked) must wipe the
        // dropped state so the next ShowForMission starts fresh.
        var next = MissionRecruitPanelStateLogic.Next(
            MissionRecruitPanelState.DroppedReadyToValidate,
            MissionRecruitPanelEvent.PanelClosed);
        Assert.Equal(MissionRecruitPanelState.Initial, next);
    }

    // --------------------------------------------------------------------
    // State-to-visual projection -- pin the truth table.
    // --------------------------------------------------------------------

    [Fact]
    public void Should_grey_out_is_true_only_in_dropped_state()
    {
        Assert.False(MissionRecruitPanelStateLogic.ShouldGreyOutCharacterPlaceholder(
            MissionRecruitPanelState.Initial));
        Assert.True(MissionRecruitPanelStateLogic.ShouldGreyOutCharacterPlaceholder(
            MissionRecruitPanelState.DroppedReadyToValidate));
    }

    [Fact]
    public void Should_highlight_accept_is_true_only_in_dropped_state()
    {
        Assert.False(MissionRecruitPanelStateLogic.ShouldHighlightAcceptButton(
            MissionRecruitPanelState.Initial));
        Assert.True(MissionRecruitPanelStateLogic.ShouldHighlightAcceptButton(
            MissionRecruitPanelState.DroppedReadyToValidate));
    }
}
