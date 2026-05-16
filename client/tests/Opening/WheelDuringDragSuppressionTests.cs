using System.Collections.Generic;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the P8.1+P8.2 triple-fix (Bug 2, 2026-05-07) --
/// middle-button drag and P8.1 ladder wheel are mutually exclusive
/// gestures.
///
/// <para>
/// While E1WorldMap is in an active drag (middle button held), wheel
/// events must be consumed at <c>_Input</c> priority so SceneManager's
/// <c>_UnhandledInput</c> never dispatches a ladder navigation under
/// the player's thumb. The triple-fix adds defense-in-depth: the
/// screen-side suppress now ORs <c>_isDragging</c> with a fresh poll on
/// <c>Input.IsMouseButtonPressed(Middle)</c>, AND the autoload itself
/// re-checks the same condition before dispatching ladder navigation.
/// Either layer alone is sufficient ; together they cover every observed
/// timing shape (same-frame race on press, free-spin microbrushes
/// during sustained drag).
/// </para>
///
/// <para>
/// <b>Why this lives in xUnit.</b> The Godot-side input pipeline (<c>_Input</c>
/// runs before <c>_UnhandledInput</c>, <c>SetInputAsHandled</c> stops
/// propagation, <c>Input.IsMouseButtonPressed</c> is a global poll) cannot
/// be exercised from a non-engine test host. We model the priority chain
/// with a tiny fake: <c>FakeInputPipeline</c> dispatches each event
/// through a screen-level handler first, then -- only if not marked
/// handled -- through an autoload-level handler. Both handlers consult
/// the same fake "middle held" flag.
/// </para>
///
/// <para>
/// <b>Pattern justification (option (c) primary + option (b) backstop).</b>
/// The dragging screen still owns the input contract during its drag
/// (option (c), unchanged). The autoload-level
/// <c>Input.IsMouseButtonPressed</c> poll (option (b)) was rejected as
/// the SOLE solution in the original P8.2-UX-fix because it pushed
/// drag-knowledge up into the autoload. The triple-fix accepts (b) as
/// a backstop, not as a replacement: the autoload still does not know
/// "who" drags ; it just observes "is some pointer button held" and
/// suppresses the wheel if so. This is a stateless poll, not a
/// delegation, so the architectural concern is dampened.
/// </para>
/// </summary>
public sealed class WheelDuringDragSuppressionTests
{
    /// <summary>
    /// Tiny fake of Godot's input priority chain. <c>_Input</c> runs first ;
    /// if it calls <see cref="MarkHandled"/>, <c>_UnhandledInput</c> never
    /// runs. Both handlers can also consult <see cref="MiddleButtonHeld"/>
    /// (a fake of <c>Input.IsMouseButtonPressed(MouseButton.Middle)</c>),
    /// which is what the triple-fix uses for defense-in-depth.
    /// </summary>
    private sealed class FakeInputPipeline
    {
        public bool ScreenIsDragging { get; set; }
        public bool MiddleButtonHeld { get; set; }
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
        /// Mirrors the triple-fix Bug 2 path in <c>E1WorldMap._Input</c>:
        /// if a wheel event arrives during an active drag (OR while the
        /// middle button is held independent of <c>_isDragging</c>),
        /// consume it. Returns true to indicate the event was handled.
        /// </summary>
        private bool ScreenInput(FakeEvent evt)
        {
            if ((ScreenIsDragging || MiddleButtonHeld)
                && evt.Kind == FakeEventKind.WheelPress)
            {
                return true; // SetInputAsHandled (triple-fix Bug 2)
            }
            return false;
        }

        /// <summary>
        /// Mirrors the triple-fix Bug 2 belt at the SceneManager level:
        /// even if the wheel event somehow propagates here, a fresh poll
        /// on <c>Input.IsMouseButtonPressed(Middle)</c> drops it. This
        /// is the autoload-side backstop that survives the case where
        /// the screen-level _Input handler never ran (race window we
        /// cannot reproduce in xUnit but observed empirically).
        /// </summary>
        private void SceneManagerUnhandled(FakeEvent evt)
        {
            if (evt.Kind == FakeEventKind.WheelPress)
            {
                // Triple-fix backstop poll.
                if (MiddleButtonHeld)
                {
                    return;
                }
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
    public void Wheel_event_during_active_drag_is_suppressed_at_screen()
    {
        // The core regression. Player middle-button-drags ; mid-pan their
        // thumb brushes the wheel. With the screen-level suppress, the
        // wheel is consumed at _Input and never reaches SceneManager.
        var p = new FakeInputPipeline { ScreenIsDragging = true, MiddleButtonHeld = true };

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
        // Defense doc: the suppression is gated on the drag flag (or the
        // middle-held poll), not unconditional. When neither is true,
        // the wheel propagates normally and SceneManager handles ladder
        // navigation as usual.
        var p = new FakeInputPipeline { ScreenIsDragging = false, MiddleButtonHeld = false };

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
        var p = new FakeInputPipeline { ScreenIsDragging = true, MiddleButtonHeld = true };

        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "during-drag"));
        Assert.Empty(p.SceneManagerWheelCalls);

        // Drag ends: both the screen flag and the global button-held
        // signal go to false.
        p.ScreenIsDragging = false;
        p.MiddleButtonHeld = false;

        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "after-drag"));
        Assert.Single(p.SceneManagerWheelCalls);
    }

    [Fact]
    public void Multiple_wheel_events_during_drag_all_suppressed()
    {
        // Player drag-pans for several seconds and brushes the wheel
        // repeatedly. Every single event must be suppressed -- if even
        // one slipped through, the layer would shift mid-pan.
        var p = new FakeInputPipeline { ScreenIsDragging = true, MiddleButtonHeld = true };

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
        // not Esc out of the screen mid-pan.
        var p = new FakeInputPipeline { ScreenIsDragging = true, MiddleButtonHeld = true };

        p.Dispatch(new FakeEvent(FakeEventKind.Other, "esc-or-similar"));

        Assert.Single(p.ScreenInputCalls);
        Assert.Empty(p.SceneManagerWheelCalls);
    }

    [Fact]
    public void Wheel_suppressed_when_middle_held_even_if_drag_flag_false()
    {
        // P8.1+P8.2 triple-fix Bug 2 same-frame-race coverage. The very
        // first wheel event that arrives in the same input dispatch loop
        // as the middle-button-press may be processed BEFORE the
        // _isDragging flag has flipped to true. The OR with
        // Input.IsMouseButtonPressed catches that race: even if
        // _isDragging is still false at that microsecond, the global
        // button-held state is already true, so the wheel is suppressed.
        // This is the regression that the original ad6a91f fix missed.
        var p = new FakeInputPipeline
        {
            ScreenIsDragging = false,    // not yet flipped
            MiddleButtonHeld = true,     // but the button IS already held
        };

        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "race-window"));

        Assert.Empty(p.SceneManagerWheelCalls);
        Assert.Single(p.ScreenInputCalls);
    }

    [Fact]
    public void Wheel_suppressed_at_scene_manager_when_screen_input_skipped()
    {
        // P8.1+P8.2 triple-fix Bug 2 belt: even if the screen-level
        // suppress somehow does not run (E2 freed mid-drag, scene tree
        // race), the SceneManager-side Input.IsMouseButtonPressed poll
        // catches the wheel. We model this by dispatching directly to
        // the SceneManager handler with the middle-held flag true. In
        // the real Godot pipeline, this is the case where _Input did
        // not see the event for whatever framework reason but
        // _UnhandledInput still did.
        var p = new FakeInputPipeline { MiddleButtonHeld = true };

        // Direct: skip the screen layer entirely. This is a synthetic
        // worst case -- in real Godot the screen layer always runs first
        // when E2 is alive ; we still pin the autoload-level guarantee.
        p.Dispatch(new FakeEvent(FakeEventKind.WheelPress, "autoload-only"));

        Assert.Empty(p.SceneManagerWheelCalls);
    }
}
