namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// J5 (post-J4 hotfix, 2026-05-09 evening, locked) -- pure-C# state
/// machine for the two pan-stack watchdogs introduced after the J4
/// 3D-backing generalisation surfaced two regressions :
/// <list type="bullet">
///   <item>Vertical pan stopped responding (root cause unknown without
///         a runtime trace -- candidates : InputMap regression, clamp
///         snap-to-center on a tall viewport, stuck transition lock).</item>
///   <item>BackButton click stopped firing after a zoom interaction
///         (root cause : F6-launch shape leaves SceneManager stack
///         empty, NavigateBack early-returns silently).</item>
/// </list>
///
/// <para>
/// <b>Why a pure-C# helper.</b> The runtime <see cref="Wayfinders.Client.Components.MapPan2DComponent"/>
/// reads <c>Camera2D.Position</c> and <c>Time.GetTicksMsec</c>, which
/// pulls the engine into the seam. Splitting the decision (when to log,
/// when to force-reset) from the engine reads (Camera2D, Time) lets
/// xUnit pin the watchdog contract at the same Godot-free seam every
/// other phase 8/9 helper uses (CameraPanLogic, ZoomNavLogic,
/// LadderResolutionLogic). Same trade-off as the rest of the J-series :
/// keep the engine touch points minimal, pin the rules with tests.
/// </para>
///
/// <para>
/// <b>Two distinct state machines.</b> The frozen-camera watchdog and
/// the stuck-transition watchdog have orthogonal triggers (input-driven
/// vs time-driven) and different remediation policies (warn-once vs
/// warn-and-reset). They share no state, so they live as two static
/// methods over independent record-struct inputs. The runtime calls each
/// once per <c>_Process</c> tick and applies the returned action.
/// </para>
/// </summary>
public static class PanWatchdogLogic
{
    /// <summary>
    /// Frozen-camera watchdog : decide whether to log a warning given the
    /// current frozen-frame counter. Returns <c>true</c> on the EXACT
    /// frame the counter hits the threshold (so the runtime emits the
    /// warning once, not every frame from then on).
    /// </summary>
    /// <param name="frozenFrameCount">
    /// Number of consecutive frames where input direction was non-zero
    /// but Camera2D.Position did not change. Reset to 0 by the runtime
    /// whenever the camera moves OR the input direction is zero.
    /// </param>
    /// <param name="threshold">Configured frame threshold (J5 default 30).</param>
    /// <returns>True iff the runtime should log on this frame.</returns>
    public static bool ShouldWarnOnFrozenCamera(int frozenFrameCount, int threshold)
    {
        // Equality, not >=, so the warning fires exactly once. The
        // runtime keeps incrementing the counter; subsequent frames
        // return false. Reset on any camera movement re-arms the
        // warning for the next freeze.
        return frozenFrameCount == threshold;
    }

    /// <summary>
    /// Stuck-transition watchdog : decide whether to force-reset the
    /// <c>_inTransition</c> flag given how long it has been true. The
    /// runtime calls this each frame while the lock is held ; on the
    /// first frame the watchdog observes the lock, it captures the
    /// timestamp ; once enough time has elapsed without a
    /// <c>NotifyTransitionEnded</c> call, it returns
    /// <see cref="StuckTransitionAction.WarnAndReset"/>.
    ///
    /// <para>
    /// The runtime owns the timestamp (passed in as
    /// <paramref name="lockStartedMs"/>) so this helper stays pure :
    /// no static fields, no allocation, idempotent across calls.
    /// </para>
    /// </summary>
    /// <param name="lockStartedMs">
    /// Timestamp (ms since session start) when the runtime first
    /// observed <c>_inTransition == true</c>. <c>0</c> means the lock
    /// is fresh ; the runtime should set its tracking timestamp to
    /// <paramref name="nowMs"/> and try again next frame.
    /// </param>
    /// <param name="nowMs">Current time (ms since session start).</param>
    /// <param name="resetThresholdMs">
    /// How long (ms) the lock can be held before the watchdog forces
    /// a reset. J5 default 2000.
    /// </param>
    /// <returns>The action the runtime should take.</returns>
    public static StuckTransitionAction EvaluateStuckTransition(
        ulong lockStartedMs, ulong nowMs, ulong resetThresholdMs)
    {
        if (lockStartedMs == 0)
        {
            // The runtime hasn't started the timer yet; it should
            // capture nowMs and check again next frame.
            return StuckTransitionAction.StartTimer;
        }

        if (nowMs - lockStartedMs > resetThresholdMs)
        {
            return StuckTransitionAction.WarnAndReset;
        }

        return StuckTransitionAction.Wait;
    }
}

/// <summary>
/// Outcome of <see cref="PanWatchdogLogic.EvaluateStuckTransition"/>.
/// </summary>
public enum StuckTransitionAction
{
    /// <summary>The runtime should capture the current timestamp as the
    /// lock-started time and re-check next frame.</summary>
    StartTimer,

    /// <summary>The watchdog timer is running and has not exceeded the
    /// threshold yet ; the runtime should keep waiting.</summary>
    Wait,

    /// <summary>The lock has exceeded the threshold ; the runtime
    /// should log a warning (once per stuck episode) and force-reset
    /// <c>_inTransition</c> to false.</summary>
    WarnAndReset,
}
