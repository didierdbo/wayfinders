using System;
using Wayfinders.Client.Scripts.Screens;
using static Wayfinders.Client.Scripts.Screens.CompagniePanelLogic;

namespace Wayfinders.Client.Tests.Phase9_3DBacking;

/// <summary>
/// Pin tests for <see cref="CompagniePanelLogic"/> -- the pure-C# state
/// machine that governs the slide animation, the drag-drop NPC autonomy
/// invariant, and the auto-rabat dispatch of the Compagnie panel probe
/// (Wayfinders 3D Backing Architecture, Jalon 5+ -- "UI panels = iso
/// sub-spaces").
///
/// <para>
/// Three contracts are pinned here -- if any of them drifts, the runtime
/// probe will misbehave in an obvious way (animation stuck, drag-drop
/// re-parents the perso, auto-rabat does not fire when expected). The
/// xUnit tests catch the regression before the playtest.
/// </para>
///
/// <para>
/// <b>What is NOT pinned here.</b> Tween3D timing, SubViewport rendering,
/// Camera2D/Camera3D pair sync, signal connections -- all engine-bound,
/// validated by the canary self-check at probe <c>_Ready</c>
/// (Pattern J).
/// </para>
/// </summary>
public sealed class CompagniePanelLogicTests
{
    // ================================================================
    // Slide state machine
    // ================================================================

    /// <summary>
    /// Closed + ClickHandle -> Sliding(target=Open). The simple "user
    /// pulls the handle to open" path.
    /// </summary>
    [Fact]
    public void Click_handle_when_closed_starts_sliding_toward_open()
    {
        var d = DecideNextSlideState(SlideState.Closed, SlideIntent.ClickHandle);
        Assert.Equal(SlideState.Sliding, d.NextState);
        Assert.Equal(SlideState.Open, d.SlideTarget);
    }

    /// <summary>
    /// Open + ClickHandle -> Sliding(target=Closed). The reverse.
    /// </summary>
    [Fact]
    public void Click_handle_when_open_starts_sliding_toward_closed()
    {
        var d = DecideNextSlideState(SlideState.Open, SlideIntent.ClickHandle);
        Assert.Equal(SlideState.Sliding, d.NextState);
        Assert.Equal(SlideState.Closed, d.SlideTarget);
    }

    /// <summary>
    /// Sliding + ClickHandle -> Sliding(unchanged target). The user can
    /// spam-click the handle ; the in-flight tween must continue toward
    /// its current target, not retrig from scratch each click. This is
    /// the anti-stuck-transition pin -- if the runtime dropped this
    /// invariant, a fast-clicker would freeze the panel halfway open.
    /// </summary>
    [Fact]
    public void Click_handle_during_slide_is_a_noop()
    {
        var d = DecideNextSlideState(
            SlideState.Sliding,
            SlideIntent.ClickHandle,
            inFlightTarget: SlideState.Open);
        Assert.Equal(SlideState.Sliding, d.NextState);
        Assert.Equal(SlideState.Open, d.SlideTarget);
    }

    /// <summary>
    /// Open + AnotherLayerOpened -> Sliding(target=Closed). Auto-rabat
    /// (Spec &#167; "auto-rabat actif" v1).
    /// </summary>
    [Fact]
    public void Auto_rabat_fires_when_open_and_another_layer_opens()
    {
        var d = DecideNextSlideState(SlideState.Open, SlideIntent.AnotherLayerOpened);
        Assert.Equal(SlideState.Sliding, d.NextState);
        Assert.Equal(SlideState.Closed, d.SlideTarget);
    }

    /// <summary>
    /// Closed + AnotherLayerOpened -> Closed. No tween fired (already
    /// closed -- nothing to do).
    /// </summary>
    [Fact]
    public void Auto_rabat_is_a_noop_when_already_closed()
    {
        var d = DecideNextSlideState(SlideState.Closed, SlideIntent.AnotherLayerOpened);
        Assert.Equal(SlideState.Closed, d.NextState);
        Assert.Null(d.SlideTarget);
    }

    /// <summary>
    /// Sliding(toward Open) + AnotherLayerOpened -> Sliding(toward
    /// Closed). The auto-rabat overrides the in-flight slide-in -- the
    /// runtime kills the open-tween and starts a close-tween. Same
    /// kill+start pattern as <c>MapPan2DComponent.ApplyContinuousZoomToTarget</c>.
    /// </summary>
    [Fact]
    public void Auto_rabat_overrides_in_flight_slide_in()
    {
        var d = DecideNextSlideState(
            SlideState.Sliding,
            SlideIntent.AnotherLayerOpened,
            inFlightTarget: SlideState.Open);
        Assert.Equal(SlideState.Sliding, d.NextState);
        Assert.Equal(SlideState.Closed, d.SlideTarget);
    }

    /// <summary>
    /// Sliding + TweenFinished(target=Open) -> Open. The animation
    /// completes ; the panel settles at the target endpoint.
    /// </summary>
    [Fact]
    public void Tween_finished_settles_to_target()
    {
        var d = DecideNextSlideState(
            SlideState.Sliding,
            SlideIntent.TweenFinished,
            inFlightTarget: SlideState.Open);
        Assert.Equal(SlideState.Open, d.NextState);
        Assert.Null(d.SlideTarget);
    }

    /// <summary>
    /// Sliding + TweenFinished(target=Closed) -> Closed. Reverse settle.
    /// </summary>
    [Fact]
    public void Tween_finished_settles_to_closed_when_closing()
    {
        var d = DecideNextSlideState(
            SlideState.Sliding,
            SlideIntent.TweenFinished,
            inFlightTarget: SlideState.Closed);
        Assert.Equal(SlideState.Closed, d.NextState);
        Assert.Null(d.SlideTarget);
    }

    /// <summary>
    /// Closed + TweenFinished -> Closed. Defensive : a stray Finished
    /// signal (e.g. from a tween we killed mid-flight then a delayed
    /// Finished fires) must NOT clobber Closed back into Sliding.
    /// </summary>
    [Fact]
    public void Stray_tween_finished_when_closed_is_a_noop()
    {
        var d = DecideNextSlideState(SlideState.Closed, SlideIntent.TweenFinished);
        Assert.Equal(SlideState.Closed, d.NextState);
        Assert.Null(d.SlideTarget);
    }

    // ================================================================
    // Drag-drop NPC autonomy invariant
    // ================================================================

    /// <summary>
    /// The drag-drop event payload pin -- if the field shape drifts
    /// (e.g. someone renames PersonaId to "PersoId" or adds a "Position"
    /// field that hides the autonomy intent), this catches it.
    /// </summary>
    [Fact]
    public void Assign_to_mission_event_carries_persona_id_and_slot_id()
    {
        var ev = ProduceAssignToMissionEvent("hodge", "mission_slot_1");
        Assert.Equal("hodge", ev.PersonaId);
        Assert.Equal("mission_slot_1", ev.MissionSlotId);
    }

    /// <summary>
    /// The event name is a stable string the runtime emits as a Godot
    /// signal ; consumers (future Mission panel probe, Compagnie status
    /// indicator) subscribe by this exact name. Pinned so a rename in
    /// one place fails the test, not silently drops the listener.
    /// </summary>
    [Fact]
    public void Assign_to_mission_event_name_is_stable()
    {
        Assert.Equal("perso_assigned_to_mission", AssignToMissionEventName);
    }

    /// <summary>
    /// Empty persona id is a contract violation -- catches the regression
    /// where the runtime fires a drag-drop signal with an empty payload
    /// because the perso resolution failed silently. Better to throw than
    /// to leak a malformed event onto the bus.
    /// </summary>
    [Fact]
    public void Assign_to_mission_event_rejects_empty_persona_id()
    {
        Assert.Throws<ArgumentException>(
            () => ProduceAssignToMissionEvent("", "mission_slot_1"));
    }

    /// <summary>Empty slot id rejected for the same reason.</summary>
    [Fact]
    public void Assign_to_mission_event_rejects_empty_slot_id()
    {
        Assert.Throws<ArgumentException>(
            () => ProduceAssignToMissionEvent("hodge", ""));
    }

    // ================================================================
    // Personas placeholder shape
    // ================================================================

    /// <summary>
    /// The v1 placeholder personas are Varn-aligned : Hodge / Mirelle /
    /// Wattan -- NOT Kira/Lyra/Borr (which are Mira MJ-prompt
    /// placeholders, see Mockup spec footnote on persona identity).
    /// Catches a regression where someone wires up the runtime persona
    /// list from the Mira mockup prose instead of the spec's persona
    /// model.
    /// </summary>
    [Fact]
    public void Default_personas_are_varn_aligned_three_member_set()
    {
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (var p in DefaultPersonas) ids.Add(p.Id);
        Assert.Equal(3, ids.Count);
        Assert.Contains("hodge", ids);
        Assert.Contains("mirelle", ids);
        Assert.Contains("wattan", ids);
    }

    /// <summary>
    /// Three distinct tints for the 3 v1 cubes -- visual diff in the
    /// probe. Catches the regression where every cube gets the same
    /// tint and Didier cannot tell Hodge from Wattan during the
    /// playtest.
    /// </summary>
    [Fact]
    public void Default_personas_have_three_distinct_tints()
    {
        var tints = new System.Collections.Generic.HashSet<PersonaTint>();
        foreach (var p in DefaultPersonas) tints.Add(p.Tint);
        Assert.Equal(3, tints.Count);
    }

    /// <summary>
    /// Each persona has a non-empty short status string. Catches the
    /// regression where the runtime renders "Hodge -- " (empty status)
    /// because the placeholder shape was shipped with a null/empty
    /// status field.
    /// </summary>
    [Fact]
    public void Default_personas_have_non_empty_short_status()
    {
        foreach (var p in DefaultPersonas)
        {
            Assert.False(string.IsNullOrEmpty(p.ShortStatus),
                $"persona {p.Id} has empty ShortStatus");
        }
    }
}
