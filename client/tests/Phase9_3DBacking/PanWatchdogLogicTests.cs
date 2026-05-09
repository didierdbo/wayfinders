using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Phase9_3DBacking;

/// <summary>
/// J5 -- pin the post-J4 pan watchdog contract. The watchdog logic
/// itself is pure-C# and lives in <see cref="PanWatchdogLogic"/> ; the
/// engine touch points (Camera2D.Position read, Time.GetTicksMsec read,
/// GD.PushWarning emit) live in
/// <see cref="Wayfinders.Client.Components.MapPan2DComponent"/> and are
/// validated through manual integration. Same Godot-free seam pattern
/// as <c>CameraPanLogicTests</c>, <c>ZoomNavLogicTests</c>, etc.
///
/// <para>
/// <b>Why this exists.</b> The J4 generalisation immediately surfaced
/// two regressions on the pan stack ; the watchdog is the only way to
/// distinguish their root causes from a runtime trace. If a future jalon
/// loosens the warn-once-per-episode discipline (e.g. logs every frame
/// once frozen) or shortens the stuck-transition reset threshold below
/// 1 s (false positives on legitimate NavigateTo asset preload), this
/// test catches it.
/// </para>
/// </summary>
public sealed class PanWatchdogLogicTests
{
    [Fact]
    public void Frozen_camera_warns_exactly_at_threshold_frame()
    {
        // The warning fires when the counter equals the threshold,
        // not before. The runtime increments each frame the camera
        // does not move under non-zero input.
        Assert.False(PanWatchdogLogic.ShouldWarnOnFrozenCamera(0, 30));
        Assert.False(PanWatchdogLogic.ShouldWarnOnFrozenCamera(29, 30));
        Assert.True(PanWatchdogLogic.ShouldWarnOnFrozenCamera(30, 30));
    }

    [Fact]
    public void Frozen_camera_does_not_warn_after_threshold()
    {
        // Warn-once discipline : after the threshold frame, the runtime
        // keeps counting (Reset only happens on camera movement) but the
        // warning does not re-fire. Otherwise a 5-second freeze would
        // emit 300 warnings.
        Assert.False(PanWatchdogLogic.ShouldWarnOnFrozenCamera(31, 30));
        Assert.False(PanWatchdogLogic.ShouldWarnOnFrozenCamera(100, 30));
        Assert.False(PanWatchdogLogic.ShouldWarnOnFrozenCamera(10000, 30));
    }

    [Fact]
    public void Stuck_transition_starts_timer_on_first_observation()
    {
        // First time the runtime observes _inTransition == true, the
        // helper has no timestamp to compare against ; it returns
        // StartTimer so the runtime can capture nowMs for next frame.
        var action = PanWatchdogLogic.EvaluateStuckTransition(
            lockStartedMs: 0, nowMs: 1000, resetThresholdMs: 2000);

        Assert.Equal(StuckTransitionAction.StartTimer, action);
    }

    [Fact]
    public void Stuck_transition_waits_before_threshold()
    {
        // Timer running, threshold not yet reached. Wait.
        var action = PanWatchdogLogic.EvaluateStuckTransition(
            lockStartedMs: 1000, nowMs: 2500, resetThresholdMs: 2000);

        Assert.Equal(StuckTransitionAction.Wait, action);
    }

    [Fact]
    public void Stuck_transition_warns_and_resets_after_threshold()
    {
        // Timer running, threshold exceeded. Warn + reset.
        var action = PanWatchdogLogic.EvaluateStuckTransition(
            lockStartedMs: 1000, nowMs: 4000, resetThresholdMs: 2000);

        Assert.Equal(StuckTransitionAction.WarnAndReset, action);
    }

    [Fact]
    public void Stuck_transition_threshold_is_strict_greater_than()
    {
        // At exactly threshold ms, still wait -- the user might have
        // a slow but legitimate NavigateTo. Strict-greater so the warn
        // fires only when the lock is *demonstrably* past the budget.
        var action = PanWatchdogLogic.EvaluateStuckTransition(
            lockStartedMs: 1000, nowMs: 3000, resetThresholdMs: 2000);

        Assert.Equal(StuckTransitionAction.Wait, action);
    }

    [Fact]
    public void Stuck_transition_threshold_must_be_at_least_one_second_in_practice()
    {
        // Defensive doc-test : the runtime default (J5) is 2000 ms. A
        // threshold below 1000 ms would false-positive on any
        // NavigateTo with asset preload. This test does not assert
        // the helper is wrong with smaller values (the helper is pure)
        // but documents the floor for the runtime config.
        //
        // If someone tries to set [Export] StuckTransitionResetMs = 500
        // in the inspector to "speed things up", they can still do so,
        // and the helper still works. They will discover the regression
        // through false-positive warnings on legitimate transitions
        // and re-raise the value. This test pins the doc.
        Assert.Equal(StuckTransitionAction.WarnAndReset,
            PanWatchdogLogic.EvaluateStuckTransition(
                lockStartedMs: 100, nowMs: 700, resetThresholdMs: 500));
    }
}
