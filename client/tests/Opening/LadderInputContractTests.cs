using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the P8.1 wheel-input ladder navigation, post the
/// P8.1+P8.2 triple-fix (2026-05-07).
///
/// <para>
/// The Godot-side <c>SceneManager._UnhandledInput</c> cannot be loaded from
/// xUnit (no engine ; no <c>InputEventMouseButton</c>), so these tests
/// assert on a fake harness that mimics the SceneManager wheel-handling
/// shape and locks the expected contracts:
/// <list type="bullet">
///   <item>Wheel up / down on a ladder rung resolves to the right target
///         and triggers exactly one navigation per gesture, under the
///         re-swapped canonical convention (wheel UP = climb out, wheel
///         DOWN = drill in).</item>
///   <item>Wheel ignored entirely while a modal is open (Pre-brief
///         Risk #3 — modal owns input).</item>
///   <item>Wheel ignored entirely while the middle mouse button is held
///         (P8.1+P8.2 triple-fix Bug 2 belt-and-braces, modeling the
///         autoload-side <c>Input.IsMouseButtonPressed</c> poll).</item>
///   <item>Wheel-down silently no-ops when the resolved target has not
///         been visited explicitly via NavigateTo this session
///         (P8.1+P8.2 triple-fix Bug 3, "drill into visited only" gate).</item>
///   <item>Debounce: a second wheel event within the window after the
///         first one is dropped (Risk #2). The window is now 400ms
///         (bumped from 200ms in the triple-fix) -- defense doc'd here.</item>
///   <item>After the debounce window expires, the next wheel event
///         goes through normally.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>P8.1+P8.2 triple-fix (Bug 1, 2026-05-07).</b> The wheel-direction-to-
/// ladder-method mapping was re-swapped at the SceneManager dispatch site
/// after Didier's visual retest. Convention re-locked:
/// <list type="bullet">
///   <item>Physical wheel UP -> NavigateLadderUp (climb out: E5 -> E3 -> E2).</item>
///   <item>Physical wheel DOWN -> NavigateLadderDown (drill in: E2 -> E3 -> E5).</item>
/// </list>
/// The <c>FakeWheelHandler</c> below mirrors that re-swap so these contract
/// tests pin the empirical convention.
/// </para>
///
/// <para>
/// <b>Why a fake harness, not the real SceneManager.</b> Same rationale as
/// <c>E1NavigationContractTests</c>: SceneManager is a Godot Node, the test
/// assembly is plain xUnit, and even ignoring that, the wheel handler reads
/// <c>Time.GetTicksMsec()</c> and <c>Input.IsMouseButtonPressed</c> directly
/// off the Godot statics -- not injectable in a non-Godot host. The fake
/// mirrors the exact sequence (modal-check, drag-check, debounce-check,
/// resolve, visited-gate, dispatch) and uses injected <c>nowMs</c> and
/// <c>isMiddleHeld</c> so each defense layer can be exercised
/// deterministically without any sleep call.
/// </para>
/// </summary>
public sealed class LadderInputContractTests
{
    /// <summary>
    /// Minimal fake of <c>SceneManager</c>'s wheel-handling surface.
    /// Owns the same fields that matter (ladder, last-wheel-tick,
    /// active modal id, drill-down-visited set) and exposes a single
    /// <c>OnWheel</c> entry point that the tests drive directly. The
    /// dispatch records what would have been called on the real
    /// SceneManager so we can assert on the navigation outcome without
    /// needing scene tree side effects.
    /// </summary>
    private sealed class FakeWheelHandler
    {
        private readonly IReadOnlyList<LayerRung> _ladder;
        private readonly ulong _debounceMs;
        private ulong _lastWheelTickMs;

        public string? CurrentScreenId { get; set; }
        public string? ActiveModalId { get; set; }
        public bool IsMiddleHeld { get; set; }
        public HashSet<string> DrillDownVisited { get; } = new();

        public List<string> NavigateLadderUpCalls { get; } = new();
        public List<string> NavigateLadderDownCalls { get; } = new();

        public FakeWheelHandler(
            IReadOnlyList<LayerRung> ladder,
            ulong debounceMs = 400)
        {
            _ladder = ladder;
            _debounceMs = debounceMs;
        }

        /// <summary>
        /// Mirrors the wheel branch of <c>SceneManager._UnhandledInput</c>
        /// post triple-fix: modal early-return, drag early-return, debounce
        /// gate, then dispatch to LadderUp or LadderDown -- the latter
        /// gated by the visited set.
        ///
        /// <para>
        /// <b>P8.1+P8.2 triple-fix Bug 1 (re-swap).</b> Physical wheel UP
        /// fires <c>NavigateLadderUp</c> (climb out) ; physical wheel DOWN
        /// fires <c>NavigateLadderDown</c> (drill in).
        /// </para>
        /// </summary>
        public void OnWheel(WheelDirection dir, ulong nowMs)
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

            if (nowMs - _lastWheelTickMs < _debounceMs)
            {
                return;
            }
            _lastWheelTickMs = nowMs;

            // Triple-fix Bug 1: WheelUp -> climb out (ResolveUpTarget) ;
            // WheelDown -> drill in (ResolveDownTarget).
            string? target = dir == WheelDirection.Up
                ? LadderResolutionLogic.ResolveUpTarget(CurrentScreenId, _ladder)
                : LadderResolutionLogic.ResolveDownTarget(CurrentScreenId, _ladder);

            if (target is null)
            {
                // No-op, but the debounce *did* fire -- a "spam wheel up
                // on E2 monde" still consumes the debounce window so we
                // don't get a flurry of debug logs.
                return;
            }

            // Triple-fix Bug 3: wheel-down (drill) only into visited
            // screens. Wheel-up (climb out) is unrestricted (you cannot
            // climb out into a non-visited screen because the ladder
            // is monotonic upward and you must have come from there).
            if (dir == WheelDirection.Down && !DrillDownVisited.Contains(target))
            {
                return;
            }

            if (dir == WheelDirection.Up) NavigateLadderUpCalls.Add(target);
            else NavigateLadderDownCalls.Add(target);
        }
    }

    private enum WheelDirection { Up, Down }

    [Fact]
    public void Wheel_down_on_monde_drills_into_cite_when_visited()
    {
        // Triple-fix Bug 1 (re-swap) + Bug 3 (visited gate) combined.
        // Physical wheel DOWN drills into the more granular layer. On E2
        // monde, wheel DOWN therefore navigates to E3 cité via
        // NavigateLadderDown -- but only if E3 has been visited
        // explicitly during this session. Here we pre-mark E3 as visited
        // (simulating "Didier clicked Halfgate at least once") so the
        // gate opens.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E2_WORLD",
        };
        h.DrillDownVisited.Add("E3_CITY_HALFGATE");

        h.OnWheel(WheelDirection.Down, nowMs: 1000);

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E3_CITY_HALFGATE", h.NavigateLadderDownCalls[0]);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Wheel_up_on_monde_is_silent_noop_because_already_at_top()
    {
        // Triple-fix Bug 1 symmetry: physical wheel UP climbs out of the
        // ladder. On E2 monde (top of ladder) there is nothing further
        // to climb, so this resolves to null = silent no-op.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E2_WORLD",
        };

        h.OnWheel(WheelDirection.Up, nowMs: 1000);

        Assert.Empty(h.NavigateLadderUpCalls);
        Assert.Empty(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_up_on_quartier_climbs_to_cite()
    {
        // Triple-fix Bug 1: physical wheel UP climbs out. On E5 quartier,
        // wheel UP navigates to E3 cité via NavigateLadderUp. Climb-out
        // does NOT need the visited gate (you can only be on E5 if you
        // got there via an explicit click path, which by definition
        // marks E5 visited).
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E5_DISTRICT",
        };

        h.OnWheel(WheelDirection.Up, nowMs: 1000);

        Assert.Single(h.NavigateLadderUpCalls);
        Assert.Equal("E3_CITY_HALFGATE", h.NavigateLadderUpCalls[0]);
        Assert.Empty(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_down_on_quartier_is_silent_noop_because_already_at_bottom()
    {
        // Triple-fix Bug 1 symmetry: wheel DOWN on E5 quartier (bottom
        // of ladder) drills further in -- but there is no rung beyond
        // E5 in MVP, so this resolves to null = silent no-op.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E5_DISTRICT",
        };

        h.OnWheel(WheelDirection.Down, nowMs: 1000);

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
            CurrentScreenId = "E3_CITY_HALFGATE",
            ActiveModalId = "E4_CHARACTER_SHEET",
        };
        h.DrillDownVisited.Add("E5_DISTRICT");

        h.OnWheel(WheelDirection.Up, nowMs: 1000);
        h.OnWheel(WheelDirection.Down, nowMs: 2000);

        Assert.Empty(h.NavigateLadderUpCalls);
        Assert.Empty(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_modal_close_then_open_again_resumes_navigation()
    {
        // Closing the modal restores wheel handling. Triple-fix Bug 1:
        // drilling from E3 cité to E5 quartier requires physical wheel
        // DOWN (which dispatches to NavigateLadderDown after re-swap),
        // and Bug 3 requires E5 to be in the visited set -- we pre-mark
        // it (simulating "Didier opened the marché district at least
        // once before the modal").
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E3_CITY_HALFGATE",
            ActiveModalId = "E4_CHARACTER_SHEET",
        };
        h.DrillDownVisited.Add("E5_DISTRICT");
        h.OnWheel(WheelDirection.Down, nowMs: 1000);    // dropped (modal)

        h.ActiveModalId = null;                          // user closed E4 with Esc
        h.OnWheel(WheelDirection.Down, nowMs: 1500);    // accepted -> drill to E5

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E5_DISTRICT", h.NavigateLadderDownCalls[0]);
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
            CurrentScreenId = "E2_WORLD",
            IsMiddleHeld = true,
        };
        h.DrillDownVisited.Add("E3_CITY_HALFGATE");

        h.OnWheel(WheelDirection.Down, nowMs: 1000);
        h.OnWheel(WheelDirection.Up, nowMs: 2000);

        Assert.Empty(h.NavigateLadderDownCalls);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Wheel_resumes_after_middle_button_released()
    {
        // Defense doc symmetry: once the middle button is released (drag
        // ends), the wheel propagates again. No sticky suppression at
        // the autoload site (mirrors the screen-side _isDragging flip
        // back to false on release).
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E2_WORLD",
            IsMiddleHeld = true,
        };
        h.DrillDownVisited.Add("E3_CITY_HALFGATE");
        h.OnWheel(WheelDirection.Down, nowMs: 1000);    // suppressed

        h.IsMiddleHeld = false;                          // drag ends
        h.OnWheel(WheelDirection.Down, nowMs: 2000);    // accepted -> E3

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E3_CITY_HALFGATE", h.NavigateLadderDownCalls[0]);
    }

    [Fact]
    public void Wheel_debounce_drops_second_event_inside_window()
    {
        // Risk #2 defense doc: trackpad continuous scroll fires events
        // every ~16ms. With a 400ms debounce (triple-fix bump from 200ms),
        // only the first event of a tight burst makes it through.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 400)
        {
            CurrentScreenId = "E2_WORLD",
        };
        h.DrillDownVisited.Add("E3_CITY_HALFGATE");

        h.OnWheel(WheelDirection.Down, nowMs: 1000);    // accepted
        h.OnWheel(WheelDirection.Down, nowMs: 1050);    // 50ms later -- dropped
        h.OnWheel(WheelDirection.Down, nowMs: 1200);    // 200ms later -- dropped (was accepted pre-bump)
        h.OnWheel(WheelDirection.Down, nowMs: 1399);    // 399ms later -- dropped

        Assert.Single(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_debounce_accepts_event_after_window_expires()
    {
        // Once the 400ms debounce window has fully elapsed, the next
        // event goes through. Tests the ">=" boundary at exactly 400ms.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 400)
        {
            CurrentScreenId = "E2_WORLD",
        };
        h.DrillDownVisited.Add("E3_CITY_HALFGATE");
        h.DrillDownVisited.Add("E5_DISTRICT");

        // Triple-fix Bug 1: drill from E2 -> E3 -> E5 requires physical
        // wheel DOWN at each step.
        h.OnWheel(WheelDirection.Down, nowMs: 1000);    // accepted -> E3
        h.CurrentScreenId = "E3_CITY_HALFGATE";

        h.OnWheel(WheelDirection.Down, nowMs: 1400);    // 400ms later -- accepted

        Assert.Equal(2, h.NavigateLadderDownCalls.Count);
        Assert.Equal("E3_CITY_HALFGATE", h.NavigateLadderDownCalls[0]);
        Assert.Equal("E5_DISTRICT", h.NavigateLadderDownCalls[1]);
    }

    [Fact]
    public void Wheel_offladder_screen_is_silent_noop_but_consumes_debounce()
    {
        // Wheel on E1 title (off-ladder) does nothing. We document
        // explicitly that the debounce window IS consumed by the
        // attempted-but-resolved-to-null event -- mirrors SceneManager
        // setting _lastWheelTickMs before the resolve call. Prevents
        // a "spam wheel on E1 to log-flood the console" minor concern.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 400)
        {
            CurrentScreenId = "E1_TITLE",
        };

        h.OnWheel(WheelDirection.Down, nowMs: 1000);    // resolved to null
        h.OnWheel(WheelDirection.Down, nowMs: 1100);    // dropped by debounce

        Assert.Empty(h.NavigateLadderDownCalls);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Wheel_down_on_monde_is_silent_noop_when_target_unvisited()
    {
        // P8.1+P8.2 triple-fix Bug 3 (visited gate). On E2 monde, the
        // resolved wheel-down target is E3_CITY_HALFGATE. If the player
        // has not yet clicked Halfgate this session (E3 not in the
        // visited set), the wheel-down silently no-ops -- it does not
        // teleport the player into a layer they have not chosen
        // explicitly. This is the core regression for Bug 3.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E2_WORLD",
        };
        // Note: DrillDownVisited is intentionally EMPTY -- player has
        // not clicked Halfgate yet.

        h.OnWheel(WheelDirection.Down, nowMs: 1000);

        Assert.Empty(h.NavigateLadderDownCalls);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Wheel_down_unblocks_after_explicit_visit()
    {
        // P8.1+P8.2 triple-fix Bug 3 -- the gate opens once the player
        // has visited the target via an explicit NavigateTo path (POI
        // click). Mirroring real flow: player clicks Halfgate -> E3
        // pushed -> visited set updated -> player NavigateBack to E2 ->
        // now wheel-down works to drill back into E3.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E2_WORLD",
        };

        h.OnWheel(WheelDirection.Down, nowMs: 1000);     // suppressed by Bug 3 gate
        Assert.Empty(h.NavigateLadderDownCalls);

        // Simulate the explicit click path: real SceneManager.NavigateTo
        // adds the target to the visited set as a side-effect.
        h.DrillDownVisited.Add("E3_CITY_HALFGATE");

        h.OnWheel(WheelDirection.Down, nowMs: 2000);     // gate now open

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E3_CITY_HALFGATE", h.NavigateLadderDownCalls[0]);
    }

    [Fact]
    public void Wheel_up_does_not_require_visited_gate()
    {
        // P8.1+P8.2 triple-fix Bug 3 asymmetry: the visited gate applies
        // ONLY to wheel-down (drill in). Wheel-up (climb out) does not
        // need the gate -- if the player is currently on E5, they
        // necessarily got there via an explicit click path, so E3 (the
        // climb-out target) is by construction already visited. We pin
        // the asymmetry here so a future refactor doesn't accidentally
        // gate the climb-out direction too.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E5_DISTRICT",
        };
        // Note: DrillDownVisited is intentionally EMPTY for this test.
        // In real flow this is impossible (the player got to E5 via an
        // explicit path so E5 is in the set) but we test the contract
        // explicitly: wheel-up does NOT consult the visited set.

        h.OnWheel(WheelDirection.Up, nowMs: 1000);

        Assert.Single(h.NavigateLadderUpCalls);
        Assert.Equal("E3_CITY_HALFGATE", h.NavigateLadderUpCalls[0]);
        Assert.Empty(h.NavigateLadderDownCalls);
    }
}
