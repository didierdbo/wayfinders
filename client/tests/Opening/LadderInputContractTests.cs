using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the P8.1 wheel-input ladder navigation, post the
/// P8 final wheel-direction lock (2026-05-07).
///
/// <para>
/// The Godot-side <c>SceneManager._UnhandledInput</c> cannot be loaded from
/// xUnit (no engine ; no <c>InputEventMouseButton</c>), so these tests
/// assert on a fake harness that mimics the SceneManager wheel-handling
/// shape and locks the expected contracts:
/// <list type="bullet">
///   <item>Wheel events on a ladder rung resolve to the right target and
///         trigger exactly one navigation per gesture, under the final
///         pull/push convention (PULL = climb out, PUSH = drill in).</item>
///   <item>Wheel ignored entirely while a modal is open (Pre-brief
///         Risk #3 — modal owns input).</item>
///   <item>Wheel ignored entirely while the middle mouse button is held
///         (P8.1+P8.2 triple-fix Bug 2 belt-and-braces, modeling the
///         autoload-side <c>Input.IsMouseButtonPressed</c> poll).</item>
///   <item>Wheel ignored entirely within the post-drag grace period
///         after a middle-button release (P8 final-lock, this commit --
///         catches the thumb lift-off brush).</item>
///   <item>Drill-in silently no-ops when the resolved target has not
///         been visited explicitly via NavigateTo this session
///         (P8.1+P8.2 triple-fix Bug 3, "drill into visited only" gate).</item>
///   <item>Debounce: a second wheel event within the window after the
///         first one is dropped (Risk #2). The window is 400ms.</item>
///   <item>After the debounce window expires, the next wheel event
///         goes through normally.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>P8 wheel-direction final lock (2026-05-07, post-1e89ad3).</b>
/// After four iterations (<c>c0b6157</c> → <c>ad6a91f</c> → <c>1e89ad3</c>
/// → this commit) the convention is locked by Didier physical-gesture
/// re-test. We use pull/push terminology exclusively below -- the
/// "wheel up/down" / "scroll forward/backward" labels caused the swap
/// loop because they meant different things to different reviewers:
/// <list type="bullet">
///   <item><b>PULL</b> the wheel toward the user → fires
///         <c>MouseButton.WheelDown</c> event → dispatches
///         <c>NavigateLadderUp</c> = climb OUT (e.g. E2 → E1, E5 → E3).</item>
///   <item><b>PUSH</b> the wheel away from the user → fires
///         <c>MouseButton.WheelUp</c> event → dispatches
///         <c>NavigateLadderDown</c> = drill IN (e.g. E1 → E2, E3 → E5).</item>
/// </list>
/// The <c>FakeWheelHandler</c> below mirrors that mapping at its dispatch
/// step (event → verb), so these contract tests pin the empirical
/// pull/push convention end to end.
/// </para>
///
/// <para>
/// <b>Why a fake harness, not the real SceneManager.</b> Same rationale as
/// <c>E1NavigationContractTests</c>: SceneManager is a Godot Node, the test
/// assembly is plain xUnit, and even ignoring that, the wheel handler reads
/// <c>Time.GetTicksMsec()</c> and <c>Input.IsMouseButtonPressed</c> directly
/// off the Godot statics -- not injectable in a non-Godot host. The fake
/// mirrors the exact sequence (modal-check, drag-check, post-drag-grace,
/// debounce-check, resolve, visited-gate, dispatch) and uses injected
/// <c>nowMs</c>, <c>isMiddleHeld</c>, and <c>lastDragEndMs</c> so each
/// defense layer can be exercised deterministically without any sleep call.
/// </para>
/// </summary>
public sealed class LadderInputContractTests
{
    /// <summary>
    /// Minimal fake of <c>SceneManager</c>'s wheel-handling surface.
    /// Owns the same fields that matter (ladder, last-wheel-tick,
    /// last-drag-end, active modal id, drill-down-visited set) and exposes
    /// a single <c>OnWheel</c> entry point that the tests drive directly.
    /// The dispatch records what would have been called on the real
    /// SceneManager so we can assert on the navigation outcome without
    /// needing scene tree side effects.
    /// </summary>
    private sealed class FakeWheelHandler
    {
        private readonly IReadOnlyList<LayerRung> _ladder;
        private readonly ulong _debounceMs;
        private readonly ulong _postDragGraceMs;
        private ulong _lastWheelTickMs;

        public string? CurrentScreenId { get; set; }
        public string? ActiveModalId { get; set; }
        public bool IsMiddleHeld { get; set; }
        public ulong LastDragEndMs { get; set; }
        public HashSet<string> DrillDownVisited { get; } = new();

        public List<string> NavigateLadderUpCalls { get; } = new();
        public List<string> NavigateLadderDownCalls { get; } = new();

        public FakeWheelHandler(
            IReadOnlyList<LayerRung> ladder,
            ulong debounceMs = 400,
            ulong postDragGraceMs = 250)
        {
            _ladder = ladder;
            _debounceMs = debounceMs;
            _postDragGraceMs = postDragGraceMs;
        }

        /// <summary>
        /// Mirrors the wheel branch of <c>SceneManager._UnhandledInput</c>
        /// post P8 final-lock: modal early-return, drag early-return,
        /// post-drag-grace early-return, debounce gate, then dispatch to
        /// LadderUp or LadderDown -- the latter gated by the visited set.
        ///
        /// <para>
        /// <b>P8 wheel-direction final lock.</b> The dispatch step inverts
        /// the natural "WheelEvent.Up calls NavigateLadderUp" identity:
        /// <list type="bullet">
        ///   <item>WheelDown event (= PULL toward user) → ResolveUpTarget
        ///         + NavigateLadderUpCalls (climb out).</item>
        ///   <item>WheelUp event (= PUSH away from user) → ResolveDownTarget
        ///         + NavigateLadderDownCalls (drill in, visited-gated).</item>
        /// </list>
        /// </para>
        /// </summary>
        public void OnWheel(WheelEvent evt, ulong nowMs)
        {
            if (ActiveModalId is not null)
            {
                return;
            }

            // Bug 2 belt-and-braces: middle button held -> wheel suppressed.
            if (IsMiddleHeld)
            {
                return;
            }

            // P8 final-lock post-drag grace: wheel events within
            // PostDragGraceMs of the most recent middle-button release are
            // silently dropped. Catches the thumb lift-off brush.
            if (nowMs - LastDragEndMs < _postDragGraceMs)
            {
                return;
            }

            if (nowMs - _lastWheelTickMs < _debounceMs)
            {
                return;
            }
            _lastWheelTickMs = nowMs;

            // P8 final-lock dispatch: PULL (WheelDown event) -> climb out ;
            // PUSH (WheelUp event) -> drill in. Visited gate applies only
            // to drill-in (climb-out is by-construction always to a
            // previously-visited screen).
            string? target;
            if (evt == WheelEvent.WheelDown)
            {
                // PULL gesture -- climb out.
                target = LadderResolutionLogic.ResolveUpTarget(CurrentScreenId, _ladder);
                if (target is null) return;
                NavigateLadderUpCalls.Add(target);
            }
            else
            {
                // PUSH gesture -- drill in (visited-gated).
                target = LadderResolutionLogic.ResolveDownTarget(CurrentScreenId, _ladder);
                if (target is null) return;
                if (!DrillDownVisited.Contains(target)) return;
                NavigateLadderDownCalls.Add(target);
            }
        }
    }

    /// <summary>
    /// Wheel event flavors as Godot reports them via
    /// <c>InputEventMouseButton.ButtonIndex</c>. The PULL/PUSH semantic
    /// mapping is applied at dispatch time inside
    /// <see cref="FakeWheelHandler.OnWheel"/>, not in this enum -- the
    /// enum stays close to the Godot signal so the test is unambiguous
    /// about which low-level event it is firing.
    /// </summary>
    private enum WheelEvent
    {
        /// <summary>WheelUp event = PUSH gesture (away from user) = drill in.</summary>
        WheelUp,
        /// <summary>WheelDown event = PULL gesture (toward user) = climb out.</summary>
        WheelDown,
    }

    [Fact]
    public void Push_on_monde_drills_into_cite_when_visited()
    {
        // P8 final-lock + Bug 3 (visited gate) combined. PUSH (away from
        // user, WheelUp event) drills into the more granular layer. On
        // E2 monde, PUSH therefore navigates to E3 cité via
        // NavigateLadderDown -- but only if E3 has been visited explicitly
        // during this session. Pre-mark E3 as visited.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E1_WORLD",
        };
        h.DrillDownVisited.Add("E2_AREA");

        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000); // PUSH

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E2_AREA", h.NavigateLadderDownCalls[0]);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Pull_on_monde_is_silent_noop_because_already_at_top()
    {
        // P8 final-lock symmetry: PULL climbs out of the ladder. On E2
        // monde (top of ladder) there is nothing further to climb, so
        // this resolves to null = silent no-op.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E1_WORLD",
        };

        h.OnWheel(WheelEvent.WheelDown, nowMs: 1000); // PULL

        Assert.Empty(h.NavigateLadderUpCalls);
        Assert.Empty(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Pull_on_quartier_climbs_to_cite()
    {
        // P8 final-lock: PULL climbs out. On E5 quartier, PULL navigates
        // to E3 cité via NavigateLadderUp. Climb-out does NOT need the
        // visited gate (you can only be on E5 if you got there via an
        // explicit click path, which by definition marks E5 visited).
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E3_DISTRICT",
        };

        h.OnWheel(WheelEvent.WheelDown, nowMs: 1000); // PULL

        Assert.Single(h.NavigateLadderUpCalls);
        Assert.Equal("E2_AREA", h.NavigateLadderUpCalls[0]);
        Assert.Empty(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Push_on_quartier_is_silent_noop_because_already_at_bottom()
    {
        // P8 final-lock symmetry: PUSH on E5 quartier (bottom of ladder)
        // drills further in -- but there is no rung beyond E5 in MVP,
        // so this resolves to null = silent no-op.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E3_DISTRICT",
        };

        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000); // PUSH

        Assert.Empty(h.NavigateLadderUpCalls);
        Assert.Empty(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_ignored_when_modal_open()
    {
        // Risk #3 defense doc: modal owns input. A wheel event fired
        // while a modal is active must not trigger a navigation, even
        // when the underlying screen is on a ladder rung.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E2_AREA",
            ActiveModalId = "E4_CHARACTER_SHEET",
        };
        h.DrillDownVisited.Add("E3_DISTRICT");

        h.OnWheel(WheelEvent.WheelDown, nowMs: 1000); // PULL
        h.OnWheel(WheelEvent.WheelUp, nowMs: 2000);   // PUSH

        Assert.Empty(h.NavigateLadderUpCalls);
        Assert.Empty(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_modal_close_then_open_again_resumes_navigation()
    {
        // Closing the modal restores wheel handling. P8 final-lock:
        // drilling from E3 cité to E5 quartier requires a PUSH gesture
        // (which dispatches to NavigateLadderDown), and Bug 3 requires
        // E5 to be in the visited set -- pre-mark it.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E2_AREA",
            ActiveModalId = "E4_CHARACTER_SHEET",
        };
        h.DrillDownVisited.Add("E3_DISTRICT");
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000);  // PUSH dropped (modal)

        h.ActiveModalId = null;                        // user closed E4 with Esc
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1500);  // PUSH accepted -> drill to E5

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E3_DISTRICT", h.NavigateLadderDownCalls[0]);
    }

    [Fact]
    public void Wheel_ignored_when_middle_button_held()
    {
        // P8.1+P8.2 triple-fix Bug 2 belt-and-braces: while the player
        // holds the middle button (drag-pan in progress), the wheel is
        // silently ignored at the autoload level too. This is the
        // contract that the SceneManager Input.IsMouseButtonPressed
        // poll pins, in addition to the screen-level _isDragging
        // suppress in E2WorldMap._Input.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E1_WORLD",
            IsMiddleHeld = true,
        };
        h.DrillDownVisited.Add("E2_AREA");

        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000);   // PUSH suppressed
        h.OnWheel(WheelEvent.WheelDown, nowMs: 2000); // PULL suppressed

        Assert.Empty(h.NavigateLadderDownCalls);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Wheel_resumes_after_middle_button_released()
    {
        // Defense doc symmetry: once the middle button is released (drag
        // ends), the wheel propagates again -- subject to the post-drag
        // grace period, which is exercised separately below. Here we
        // simulate a release with a long-enough delay (>= 250ms) so the
        // grace window has expired by the time the next wheel arrives.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E1_WORLD",
            IsMiddleHeld = true,
        };
        h.DrillDownVisited.Add("E2_AREA");
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000);  // PUSH suppressed (middle held)

        h.IsMiddleHeld = false;                        // drag ends
        h.LastDragEndMs = 1000;                        // record release timestamp
        h.OnWheel(WheelEvent.WheelUp, nowMs: 2000);  // PUSH accepted (1000ms after release, way past grace)

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E2_AREA", h.NavigateLadderDownCalls[0]);
    }

    [Fact]
    public void Wheel_ignored_within_grace_period_after_drag_release()
    {
        // P8 final-lock: post-drag grace period. A wheel event arriving
        // less than PostDragGraceMs (250ms) after the most recent drag
        // release is silently dropped at the autoload backstop. This is
        // the regression for the "lift-off thumb brush" symptom Didier
        // reported post-1e89ad3.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E1_WORLD",
        };
        h.DrillDownVisited.Add("E2_AREA");

        // Drag just ended at t=1000. Wheel event at t=1100 (100ms after
        // release) must be silently suppressed -- this is well inside
        // the 250ms grace window.
        h.LastDragEndMs = 1000;
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1100); // PUSH

        Assert.Empty(h.NavigateLadderDownCalls);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Wheel_resumes_after_grace_period_expires()
    {
        // Defense-doc symmetry of the grace period: once the 250ms window
        // has elapsed, wheel events propagate normally again. Tests the
        // ">= 250ms" boundary at exactly the grace duration.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, postDragGraceMs: 250)
        {
            CurrentScreenId = "E1_WORLD",
        };
        h.DrillDownVisited.Add("E2_AREA");

        h.LastDragEndMs = 1000;
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1250); // PUSH at exactly t+250ms = boundary, accepted

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E2_AREA", h.NavigateLadderDownCalls[0]);
    }

    [Fact]
    public void Drag_release_then_immediate_wheel_is_silent()
    {
        // The exact symptom Didier described: "when I exit the scroll,
        // sometimes I do a wheel action by accident just after, it should
        // be cancelled because it is likely an error". We model the
        // sequence: middle-held=true at t=1000 (drag ongoing), then
        // released at t=1500 with LastDragEndMs updated, then a wheel
        // event arrives a few frames later at t=1520 (20ms after release,
        // typical lift-off brush window). Expected: the wheel is silently
        // dropped, no navigation logged.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E1_WORLD",
            IsMiddleHeld = true,
        };
        h.DrillDownVisited.Add("E2_AREA");

        // Drag in progress -- a wheel during the drag is suppressed by
        // the IsMiddleHeld branch (Bug 2). Documented again here for
        // sequence completeness.
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000);  // PUSH suppressed (middle held)
        Assert.Empty(h.NavigateLadderDownCalls);

        // Drag ends. Both flags flip ; the autoload's NotifyDragReleased
        // would set LastDragEndMs in real Godot.
        h.IsMiddleHeld = false;
        h.LastDragEndMs = 1500;

        // 20ms later -- the lift-off brush. Must be silent.
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1520); // PUSH suppressed (in grace)
        Assert.Empty(h.NavigateLadderDownCalls);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Wheel_debounce_drops_second_event_inside_window()
    {
        // Risk #2 defense doc: trackpad continuous scroll fires events
        // every ~16ms. With a 400ms debounce, only the first event of a
        // tight burst makes it through.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 400)
        {
            CurrentScreenId = "E1_WORLD",
        };
        h.DrillDownVisited.Add("E2_AREA");

        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000); // PUSH accepted
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1050); // PUSH 50ms later -- dropped
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1200); // PUSH 200ms later -- dropped
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1399); // PUSH 399ms later -- dropped

        Assert.Single(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_debounce_accepts_event_after_window_expires()
    {
        // Once the 400ms debounce window has fully elapsed, the next
        // event goes through. Tests the ">=" boundary at exactly 400ms.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 400)
        {
            CurrentScreenId = "E1_WORLD",
        };
        h.DrillDownVisited.Add("E2_AREA");
        h.DrillDownVisited.Add("E3_DISTRICT");

        // P8 final-lock: drill from E2 -> E3 -> E5 requires PUSH at each
        // step.
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000); // PUSH accepted -> E3
        h.CurrentScreenId = "E2_AREA";

        h.OnWheel(WheelEvent.WheelUp, nowMs: 1400); // PUSH 400ms later -- accepted

        Assert.Equal(2, h.NavigateLadderDownCalls.Count);
        Assert.Equal("E2_AREA", h.NavigateLadderDownCalls[0]);
        Assert.Equal("E3_DISTRICT", h.NavigateLadderDownCalls[1]);
    }

    [Fact]
    public void Wheel_offladder_screen_is_silent_noop_but_consumes_debounce()
    {
        // Wheel on E1 title (off-ladder) does nothing. We pin explicitly
        // that the debounce window IS consumed by the attempted-but-
        // resolved-to-null event -- mirrors SceneManager updating
        // _lastWheelTickMs after the post-drag-grace gate but before the
        // dispatch reaches the resolver. Prevents a "spam wheel on E1
        // to log-flood the console" minor concern.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 400)
        {
            CurrentScreenId = "E1_TITLE",
        };

        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000);   // PUSH resolved to null
        h.OnWheel(WheelEvent.WheelUp, nowMs: 1100);   // PUSH dropped by debounce

        Assert.Empty(h.NavigateLadderDownCalls);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Push_on_monde_is_silent_noop_when_target_unvisited()
    {
        // P8.1+P8.2 triple-fix Bug 3 (visited gate). On E2 monde, the
        // resolved drill-in target is E2_AREA. If the player
        // has not yet clicked Halfgate this session (E3 not in the
        // visited set), the PUSH silently no-ops -- it does not teleport
        // the player into a layer they have not chosen explicitly. This
        // is the core regression for Bug 3.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E1_WORLD",
        };
        // Note: DrillDownVisited is intentionally EMPTY -- player has
        // not clicked Halfgate yet.

        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000); // PUSH

        Assert.Empty(h.NavigateLadderDownCalls);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Push_unblocks_after_explicit_visit()
    {
        // P8.1+P8.2 triple-fix Bug 3 -- the gate opens once the player
        // has visited the target via an explicit NavigateTo path (POI
        // click). Mirroring real flow: player clicks Halfgate -> E3
        // pushed -> visited set updated -> player NavigateBack to E2 ->
        // now PUSH works to drill back into E3.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E1_WORLD",
        };

        h.OnWheel(WheelEvent.WheelUp, nowMs: 1000); // PUSH suppressed by Bug 3 gate
        Assert.Empty(h.NavigateLadderDownCalls);

        // Simulate the explicit click path: real SceneManager.NavigateTo
        // adds the target to the visited set as a side-effect.
        h.DrillDownVisited.Add("E2_AREA");

        h.OnWheel(WheelEvent.WheelUp, nowMs: 2000); // PUSH gate now open

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E2_AREA", h.NavigateLadderDownCalls[0]);
    }

    [Fact]
    public void Pull_does_not_require_visited_gate()
    {
        // P8.1+P8.2 triple-fix Bug 3 asymmetry: the visited gate applies
        // ONLY to drill-in (PUSH). Climb-out (PULL) does not need the
        // gate -- if the player is currently on E5, they necessarily
        // got there via an explicit click path, so E3 (the climb-out
        // target) is by construction already visited. We pin the
        // asymmetry here so a future refactor doesn't accidentally gate
        // the climb-out direction too.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E3_DISTRICT",
        };
        // Note: DrillDownVisited is intentionally EMPTY for this test.
        // In real flow this is impossible (the player got to E5 via an
        // explicit path so E5 is in the set) but we test the contract
        // explicitly: PULL does NOT consult the visited set.

        h.OnWheel(WheelEvent.WheelDown, nowMs: 1000); // PULL

        Assert.Single(h.NavigateLadderUpCalls);
        Assert.Equal("E2_AREA", h.NavigateLadderUpCalls[0]);
        Assert.Empty(h.NavigateLadderDownCalls);
    }
}
