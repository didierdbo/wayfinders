using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the P8.1 wheel-input ladder navigation. The Godot-side
/// <c>SceneManager._UnhandledInput</c> cannot be loaded from xUnit (no
/// engine ; no <c>InputEventMouseButton</c>), so these tests assert on a
/// fake harness that mimics the SceneManager wheel-handling shape and
/// locks the expected contracts:
/// <list type="bullet">
///   <item>Wheel up / down on a ladder rung resolves to the right target
///         and triggers exactly one navigation per gesture.</item>
///   <item>Wheel ignored entirely while a modal is open (Pre-brief
///         Risk #3 — modal owns input).</item>
///   <item>Debounce: a second wheel event within the window after the
///         first one is dropped (Pre-brief Risk #2 — trackpad
///         continuous scroll mitigation).</item>
///   <item>After the debounce window expires, the next wheel event
///         goes through normally.</item>
/// </list>
///
/// <para>
/// <b>Why a fake harness, not the real SceneManager.</b> Same rationale
/// as <c>E1NavigationContractTests</c>: SceneManager is a Godot Node, the
/// test assembly is plain xUnit, and even ignoring that, the wheel handler
/// reads <c>Time.GetTicksMsec()</c> directly off the Godot static -- not
/// injectable in a non-Godot host. The fake mirrors the exact sequence
/// (modal-check, debounce-check, resolve, dispatch) and uses an injected
/// <c>nowMs</c> so the debounce can be exercised deterministically without
/// any sleep call.
/// </para>
/// </summary>
public sealed class LadderInputContractTests
{
    /// <summary>
    /// Minimal fake of <c>SceneManager</c>'s wheel-handling surface.
    /// Owns the same fields that matter (ladder, last-wheel-tick,
    /// active modal id) and exposes a single <c>OnWheel</c> entry point
    /// that the tests drive directly. The dispatch records what would
    /// have been called on the real SceneManager so we can assert on
    /// the navigation outcome without needing scene tree side effects.
    /// </summary>
    private sealed class FakeWheelHandler
    {
        private readonly IReadOnlyList<LayerRung> _ladder;
        private readonly ulong _debounceMs;
        private ulong _lastWheelTickMs;

        public string? CurrentScreenId { get; set; }
        public string? ActiveModalId { get; set; }

        public List<string> NavigateLadderUpCalls { get; } = new();
        public List<string> NavigateLadderDownCalls { get; } = new();

        public FakeWheelHandler(
            IReadOnlyList<LayerRung> ladder,
            ulong debounceMs = 200)
        {
            _ladder = ladder;
            _debounceMs = debounceMs;
        }

        /// <summary>
        /// Mirrors the wheel branch of <c>SceneManager._UnhandledInput</c>:
        /// modal early-return, debounce gate, then dispatch to LadderUp
        /// or LadderDown. Records the resolved target into the per-direction
        /// list so the tests can assert on the outcome -- no async, no
        /// scene tree.
        /// </summary>
        public void OnWheel(WheelDirection dir, ulong nowMs)
        {
            if (ActiveModalId is not null)
            {
                return;
            }

            if (nowMs - _lastWheelTickMs < _debounceMs)
            {
                return;
            }
            _lastWheelTickMs = nowMs;

            string? target = dir == WheelDirection.Up
                ? LadderResolutionLogic.ResolveUpTarget(CurrentScreenId, _ladder)
                : LadderResolutionLogic.ResolveDownTarget(CurrentScreenId, _ladder);

            if (target is null)
            {
                // No-op, but the debounce *did* fire -- a "spam wheel up
                // on E2 monde" still consumes the debounce window so we
                // don't get a flurry of debug logs. This matches the
                // SceneManager behaviour: _lastWheelTickMs is set before
                // the resolve call.
                return;
            }

            if (dir == WheelDirection.Up) NavigateLadderUpCalls.Add(target);
            else NavigateLadderDownCalls.Add(target);
        }
    }

    private enum WheelDirection { Up, Down }

    [Fact]
    public void Wheel_down_on_monde_navigates_to_cite()
    {
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E2_WORLD",
        };

        h.OnWheel(WheelDirection.Down, nowMs: 1000);

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E3_CITY_HALFGATE", h.NavigateLadderDownCalls[0]);
        Assert.Empty(h.NavigateLadderUpCalls);
    }

    [Fact]
    public void Wheel_up_on_quartier_navigates_to_cite()
    {
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

        h.OnWheel(WheelDirection.Up, nowMs: 1000);
        h.OnWheel(WheelDirection.Down, nowMs: 2000);

        Assert.Empty(h.NavigateLadderUpCalls);
        Assert.Empty(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_modal_close_then_open_again_resumes_navigation()
    {
        // Closing the modal restores wheel handling. The debounce timer
        // is per-tick, so a wheel event right after modal close is fine
        // (the modal-check runs before the debounce-check, and modal-on
        // didn't update _lastWheelTickMs).
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder)
        {
            CurrentScreenId = "E3_CITY_HALFGATE",
            ActiveModalId = "E4_CHARACTER_SHEET",
        };
        h.OnWheel(WheelDirection.Down, nowMs: 1000);   // dropped (modal)

        h.ActiveModalId = null;                         // user closed E4 with Esc
        h.OnWheel(WheelDirection.Down, nowMs: 1500);    // accepted

        Assert.Single(h.NavigateLadderDownCalls);
        Assert.Equal("E5_DISTRICT", h.NavigateLadderDownCalls[0]);
    }

    [Fact]
    public void Wheel_debounce_drops_second_event_inside_window()
    {
        // Risk #2 defense doc: trackpad continuous scroll fires events
        // every ~16ms. With a 200ms debounce, only the first event of
        // a tight burst makes it through.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 200)
        {
            CurrentScreenId = "E2_WORLD",
        };

        h.OnWheel(WheelDirection.Down, nowMs: 1000);   // accepted
        h.OnWheel(WheelDirection.Down, nowMs: 1050);   // 50ms later -- dropped
        h.OnWheel(WheelDirection.Down, nowMs: 1150);   // 150ms later -- dropped
        h.OnWheel(WheelDirection.Down, nowMs: 1199);   // 199ms later -- dropped

        Assert.Single(h.NavigateLadderDownCalls);
    }

    [Fact]
    public void Wheel_debounce_accepts_event_after_window_expires()
    {
        // Once the debounce window has fully elapsed, the next event
        // goes through. Tests the ">=" boundary at exactly 200ms.
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 200)
        {
            CurrentScreenId = "E2_WORLD",
        };

        h.OnWheel(WheelDirection.Down, nowMs: 1000);   // accepted -> E3
        // After E3, current changes (real SceneManager updates it via
        // NavigateTo). The fake tracks intent, not the live stack -- we
        // simulate the screen change by hand for the next assertion.
        h.CurrentScreenId = "E3_CITY_HALFGATE";

        h.OnWheel(WheelDirection.Down, nowMs: 1200);   // 200ms later -- accepted

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
        var h = new FakeWheelHandler(LadderResolutionLogic.DefaultLadder, debounceMs: 200)
        {
            CurrentScreenId = "E1_TITLE",
        };

        h.OnWheel(WheelDirection.Down, nowMs: 1000);   // resolved to null
        h.OnWheel(WheelDirection.Down, nowMs: 1100);   // dropped by debounce

        Assert.Empty(h.NavigateLadderDownCalls);
    }
}
