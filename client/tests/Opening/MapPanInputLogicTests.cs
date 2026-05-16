using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="MapPanInputLogic"/>. Pins the P8.3
/// (M3 / Arc 3 / Phase 8.3) state-machine semantics: MMB skips the
/// threshold and enters Dragging immediately, RMB enters Tracking and
/// requires a 6 px cross to promote, releases distinguish was-dragging
/// (drag end + grace period) from was-tracking (silent no-op slot for
/// post-MVP context menu), and the full delta from press is preserved
/// across the threshold cross (no 6 px visual jump).
///
/// <para>
/// <b>Coverage strategy.</b>
/// <list type="bullet">
///   <item>Press shape: MMB-direct vs RMB-tracking branches.</item>
///   <item>Threshold boundary: strictly greater (6 px exact stays
///         tracking, anything beyond promotes).</item>
///   <item>Both axes contribute (X-only, Y-only, diagonal cases).</item>
///   <item>Promotion preserves the original press anchor for
///         delta-from-press semantics.</item>
///   <item>Release was-dragging vs was-not-dragging.</item>
///   <item>Reset wipes any state regardless of where the machine sits.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why this lives in xUnit.</b> Same justification as
/// <see cref="LadderResolutionLogicTests"/> and
/// <see cref="CameraPanLogicTests"/>: the state machine is pure-C#
/// (no Godot types reach this seam), so the contract pins here without
/// the engine. The Godot-bound <c>E1WorldMap._Input</c> dispatch is
/// validated via the P8.3 manual checklist (12 steps).
/// </para>
/// </summary>
public sealed class MapPanInputLogicTests
{
    [Fact]
    public void Initial_state_is_idle()
    {
        var logic = new MapPanInputLogic();
        Assert.Equal(MapPanState.Idle, logic.State);
    }

    [Fact]
    public void Mmb_press_enters_dragging_immediately()
    {
        // MMB has no second-purpose -- press + motion = pan from the
        // first pixel. No threshold gate, cohérent with the c70f1ed
        // pre-P8.3 behaviour for backward compat.
        var logic = new MapPanInputLogic();

        var press = logic.OnPress(MapPanButton.Middle, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        Assert.True(press.EnteredDrag);
        Assert.Equal(MapPanState.Dragging, logic.State);
    }

    [Fact]
    public void Rmb_press_enters_tracking_not_dragging()
    {
        // RMB has a second-purpose slot (post-MVP context menu), so the
        // press alone must not commit to a drag. The 6 px threshold gate
        // is what distinguishes click-without-motion (silent slot) from
        // intentional drag-pan.
        var logic = new MapPanInputLogic();

        var press = logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        Assert.False(press.EnteredDrag);
        Assert.Equal(MapPanState.Tracking, logic.State);
    }

    [Fact]
    public void Tracking_motion_below_threshold_stays_tracking()
    {
        // 5 px on a single axis -- below the 6 px sweet-spot. Wrist
        // tremor on a click-without-motion intent must not commit to
        // a drag, otherwise the post-MVP context menu would never be
        // reachable on hardware with a sensitive sensor.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        var motion = logic.OnMotion(new PanVec2(105f, 100f));

        Assert.False(motion.ShouldPan);
        Assert.False(motion.JustPromoted);
        Assert.Equal(MapPanState.Tracking, logic.State);
    }

    [Fact]
    public void Tracking_motion_at_exactly_threshold_stays_tracking()
    {
        // Boundary case: distance exactly 6 px must NOT promote (the
        // condition is strictly >, see MapPanInputLogic.OnMotion). Gives
        // the player one frame of grace at the edge -- defensive against
        // off-by-one perception on free-spin sensors.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        var motion = logic.OnMotion(new PanVec2(106f, 100f)); // exactly 6 px on X-axis

        Assert.False(motion.ShouldPan);
        Assert.Equal(MapPanState.Tracking, logic.State);
    }

    [Fact]
    public void Tracking_motion_above_threshold_promotes_to_dragging()
    {
        // 7 px on X-axis -- past the threshold. Promotion fires, the
        // outcome is JustPromoted=true so the runtime can log the cross
        // event without spamming on every subsequent motion frame.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        var motion = logic.OnMotion(new PanVec2(107f, 100f));

        Assert.True(motion.ShouldPan);
        Assert.True(motion.JustPromoted);
        Assert.Equal(MapPanState.Dragging, logic.State);
    }

    [Fact]
    public void Tracking_diagonal_motion_promotes_when_radial_distance_exceeds_threshold()
    {
        // The threshold uses squared euclidean distance, so a diagonal
        // motion of (5, 5) (radial ~7.07 px) should promote even though
        // each axis stays under 6 px. Pins the radial semantic against
        // a regression to per-axis checks.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        var motion = logic.OnMotion(new PanVec2(105f, 105f));

        Assert.True(motion.ShouldPan);
        Assert.True(motion.JustPromoted);
    }

    [Fact]
    public void Promotion_outcome_carries_press_position_for_full_delta_application()
    {
        // The runtime computes the pan delta as (currentMouse - pressPosition)
        // applied to cameraStart. If the outcome carried the cross point
        // instead of the press point, the camera would jump 6 px on
        // promotion. Pins the delta-from-press anchor.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        var motion = logic.OnMotion(new PanVec2(108f, 100f));

        Assert.Equal(100f, motion.PressPosition.X);
        Assert.Equal(100f, motion.PressPosition.Y);
        Assert.Equal(500f, motion.CameraStart.X);
        Assert.Equal(500f, motion.CameraStart.Y);
    }

    [Fact]
    public void Mmb_motion_pans_continuously_without_threshold_gating()
    {
        // MMB skipped the threshold at press time, so any motion event
        // applies the pan -- even if the cursor has not moved at all
        // (motion event with same position would still be ShouldPan=true,
        // delta zero). Pins the no-threshold semantic for MMB.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Middle, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        var motion = logic.OnMotion(new PanVec2(101f, 100f)); // 1 px movement

        Assert.True(motion.ShouldPan);
        Assert.False(motion.JustPromoted); // already dragging, not a fresh promotion
        Assert.Equal(MapPanState.Dragging, logic.State);
    }

    [Fact]
    public void Subsequent_motion_after_promotion_returns_pan_continue_not_promotion()
    {
        // Once promoted to Dragging, every subsequent motion event must
        // be PanContinue (ShouldPan=true, JustPromoted=false). The
        // runtime gates its "drag begin" log on JustPromoted -- a
        // regression where every frame re-triggered the log would spam
        // the console.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        var promotion = logic.OnMotion(new PanVec2(110f, 100f));
        Assert.True(promotion.JustPromoted);

        var continuation = logic.OnMotion(new PanVec2(120f, 100f));
        Assert.True(continuation.ShouldPan);
        Assert.False(continuation.JustPromoted);
    }

    [Fact]
    public void Tracking_release_is_silent_noop_was_not_dragging()
    {
        // RMB press without crossing threshold = release returns
        // WasDragging=false. The runtime uses this to skip the
        // SceneManager.NotifyDragReleased call (no grace period needed
        // for a non-drag) and to keep the log silent (slot for the
        // post-MVP context menu).
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));
        logic.OnMotion(new PanVec2(102f, 100f)); // below threshold

        var release = logic.OnRelease();

        Assert.False(release.WasDragging);
        Assert.Equal(MapPanState.Idle, logic.State);
    }

    [Fact]
    public void Dragging_release_returns_was_dragging_true()
    {
        // The drag end case fires NotifyDragReleased (post-drag grace
        // period) and logs the end. WasDragging=true is the gate.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Middle, new PanVec2(100f, 100f), new PanVec2(500f, 500f));
        logic.OnMotion(new PanVec2(150f, 100f));

        var release = logic.OnRelease();

        Assert.True(release.WasDragging);
        Assert.Equal(MapPanState.Idle, logic.State);
    }

    [Fact]
    public void Mmb_press_then_immediate_release_returns_was_dragging_true()
    {
        // Edge: MMB-press without motion still committed to Dragging at
        // press time (no threshold), so a release without ever moving
        // ends a drag. WasDragging=true. The runtime fires the grace
        // period anyway -- benign, the post-drag wheel-suppress is a
        // 250ms window and a no-motion drag is effectively a click that
        // does not navigate anyway.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Middle, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        var release = logic.OnRelease();

        Assert.True(release.WasDragging);
    }

    [Fact]
    public void Reset_during_tracking_clears_state()
    {
        // Modal-open path or settings-flip path: reset must wipe the
        // Tracking state so a release event after the reset does not
        // see WasDragging=true.
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Right, new PanVec2(100f, 100f), new PanVec2(500f, 500f));

        logic.Reset();

        Assert.Equal(MapPanState.Idle, logic.State);
        var release = logic.OnRelease();
        Assert.False(release.WasDragging);
    }

    [Fact]
    public void Reset_during_dragging_clears_state()
    {
        // D-P8.3-11: settings flip mid-drag must cut the gesture cleanly.
        // After reset, a release event sees WasDragging=false (no
        // NotifyDragReleased fires on a reset-cancelled drag).
        var logic = new MapPanInputLogic();
        logic.OnPress(MapPanButton.Middle, new PanVec2(100f, 100f), new PanVec2(500f, 500f));
        logic.OnMotion(new PanVec2(150f, 100f));
        Assert.Equal(MapPanState.Dragging, logic.State);

        logic.Reset();

        Assert.Equal(MapPanState.Idle, logic.State);
        var release = logic.OnRelease();
        Assert.False(release.WasDragging);
    }

    [Fact]
    public void Idle_motion_returns_noop()
    {
        // Defense doc: a motion event without a preceding press is
        // dropped silently. No state change, no pan dispatch. Pins the
        // "Idle is sticky to motion" invariant -- prevents a future
        // refactor from accidentally starting a drag on bare motion.
        var logic = new MapPanInputLogic();

        var motion = logic.OnMotion(new PanVec2(200f, 200f));

        Assert.False(motion.ShouldPan);
        Assert.Equal(MapPanState.Idle, logic.State);
    }

    [Fact]
    public void Threshold_constant_is_six_pixels()
    {
        // Locked Pax 2026-05-07 at 6 px. If a future change wants to
        // tune this, the test must be updated deliberately and a new
        // Pax research note attached. Hard-coded value here is the
        // canary for an accidental swap in the implementation.
        Assert.Equal(6f, MapPanInputLogic.DragThresholdPx);
    }
}
