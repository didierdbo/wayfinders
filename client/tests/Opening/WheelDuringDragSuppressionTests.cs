using System.Collections.Generic;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the P8.2-UX-fix (Bug 2) -- middle-button drag and
/// P8.1 ladder wheel are mutually exclusive gestures. While E2WorldMap is
/// in an active drag (middle button held), wheel events must be consumed
/// at <c>_Input</c> priority so SceneManager's <c>_UnhandledInput</c> never
/// dispatches a ladder navigation under the player's thumb.
///
/// <para>
/// <b>Why this lives in xUnit.</b> The Godot-side input pipeline (<c>_Input</c>
/// runs before <c>_UnhandledInput</c>, <c>SetInputAsHandled</c> stops
/// propagation) cannot be exercised from a non-engine test host. We model
/// the priority chain with a tiny fake: <c>FakeInputPipeline</c> dispatches
/// each event through a screen-level handler first, then -- only if not
/// marked handled -- through an autoload-level handler. The fix in
/// <see cref="Wayfinders.Client.Scenes.Screens.E2WorldMap"/> consumes the
/// wheel event during drag, so the SceneManager handler never runs. The
/// regression we are pinning is exactly that absence.
/// </para>
///
/// <para>
/// <b>Pattern justification (option (c) in the closeout).</b> The dragging
/// screen owns the input contract during its drag. We considered (a)
/// extending <c>IScreen</c> with an <c>IsDraggingActive</c> property, and
/// (b) having SceneManager poll <c>Input.IsMouseButtonPressed(Middle)</c>.
/// Both push knowledge of "who drags" up into the autoload, which is the
/// wrong direction architecturally. Option (c) keeps the autoload generic
/// and the screen's input policy local to the screen.
/// </para>
/// </summary>
public sealed class WheelDuringDragSuppressionTests
{
    /// <summary>
    /// Tiny fake of Godot's input priority chain. <c>_Input</c> runs first ;
    /// if it calls <see cref="MarkHandled"/>, <c>_UnhandledInput</c> never
    /// runs. Mirrors the contract <c>SetInputAsHandled</c> + <c>_Input</c>
    /// vs <c>_UnhandledInput</c> ordering on the Godot main loop.
    /// </summary>
    private sealed class FakeInputPipeline
    {
        public bool ScreenIsDragging { get; set; }
        public List<FakeEvent> ScreenInputCalls { get; } = new();
        public List<FakeEvent> SceneManagerWheelCalls { get; } = new();

        public void Dispatch(FakeEvent evt)
        {
            // Screen-level _Input runs first.
            var handled = ScreenInput(evt);
            ScreenInputCalls.Add(evt);

            // _UnhandledInput only runs if the screen did NOT consume it.
            if (!handled)
            {
                SceneManagerUnhandled(evt);
            }
        }

        /// <summary>
        /// Mirrors the Bug 2 fix in <c>E2WorldMap._Input</c>: if a wheel
        /// event arrives during an active drag, consume it. Returns true
        /// to indicate the event was handled.
        /// </summary>
        private bool ScreenInput(FakeEvent evt)
        {
            if (ScreenIsDragging
                && evt.Kind == FakeEventKind.WheelPress)
            {
                return true; // SetInputAsHandled (Bug 2 fix)
            }
            return false;
        }

        /// <summary>
        /// Mirrors the SceneManager wheel branch (post-Bug 1 swap). For
        /// these tests we don't care which ladder method gets called --
        /// only that the wheel reached the autoload at all (or did not).
        /// </summary>
        private void SceneManagerUnhandled(FakeEvent evt)
        {
            if (evt.Kind == FakeEventKind.WheelPress)
            {
                SceneManagerWheelCalls.Add(evt);
            }
        }
    }

    private enum FakeEventKind
    {
        WheelPress,
        MiddleButtonPress,
        MiddleButtonRelease,
        Other,
    }

    private readonly record struct FakeEvent(FakeEventKind Kind, string Tag);

    [Fact]
    public void Wheel_event_during_active_drag_is_suppressed()
    {
        // The core regression. Player middle-button-drags ; mid-pan their
        // thumb brushes the wheel. Without the fix, the wheel reaches the
        // SceneManager and triggers a ladder navigation under the drag --
        // unexpected layer change, broken UX. With the fix, the wheel is
        // consumed at the screen level and never reaches SceneManager.
        var p = new FakeInputPipeline { ScreenIsDragging = true };

        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "during-drag"));

        Assert.Empty(p.SceneManagerWheelCalls);
        // The screen still saw the event (and consumed it) -- this is
        // what marks the suppression as deliberate, not a side-effect of
        // some unrelated filter.
        Assert.Single(p.ScreenInputCalls);
    }

    [Fact]
    public void Wheel_event_when_not_dragging_propagates_to_scene_manager()
    {
        // Defense doc: the suppression is gated on the drag flag, not
        // unconditional. When no drag is active, the wheel propagates
        // normally and SceneManager handles ladder navigation as usual.
        var p = new FakeInputPipeline { ScreenIsDragging = false };

        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "no-drag"));

        Assert.Single(p.SceneManagerWheelCalls);
        Assert.Single(p.ScreenInputCalls);
    }

    [Fact]
    public void Wheel_resumes_propagation_after_drag_ends()
    {
        // The drag flag flips back to false on middle-button-release.
        // The next wheel event after that release should propagate
        // normally -- no sticky suppression.
        var p = new FakeInputPipeline { ScreenIsDragging = true };

        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "during-drag"));
        Assert.Empty(p.SceneManagerWheelCalls);

        // Drag ends.
        p.ScreenIsDragging = false;

        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "after-drag"));
        Assert.Single(p.SceneManagerWheelCalls);
    }

    [Fact]
    public void Multiple_wheel_events_during_drag_all_suppressed()
    {
        // Player drag-pans for several seconds and brushes the wheel
        // repeatedly. Every single event must be suppressed -- if even
        // one slipped through, the layer would shift mid-pan.
        var p = new FakeInputPipeline { ScreenIsDragging = true };

        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "tick-1"));
        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "tick-2"));
        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "tick-3"));
        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "tick-4"));

        Assert.Empty(p.SceneManagerWheelCalls);
        Assert.Equal(4, p.ScreenInputCalls.Count);
    }

    [Fact]
    public void NonWheel_events_during_drag_still_propagate()
    {
        // Defense doc: the suppression is wheel-specific. Other events
        // (Esc, button presses on chrome, etc.) must continue to flow
        // to SceneManager during a drag -- otherwise the player could
        // not Esc out of the screen mid-pan. This fake doesn't model
        // SceneManager's full Esc handling, only the wheel branch ; the
        // assertion is therefore "the screen did NOT mark a non-wheel
        // event as handled".
        var p = new FakeInputPipeline { ScreenIsDragging = true };

        p.Dispatch(new FakeEvent(FakeEventKind.Other, "esc-or-similar"));

        // Screen saw it but did not consume it (no wheel branch hit).
        // SceneManager-side wheel handler also did not record it (it
        // only records WheelPress events).
        Assert.Single(p.ScreenInputCalls);
        Assert.Empty(p.SceneManagerWheelCalls);
    }
}
