namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Internal record that pairs a target screen identifier with its context.
/// Used by <see cref="Wayfinders.Client.Services.SceneManager"/> as the
/// queue payload for navigation requests so we can serialize back-pressure
/// (rapid clicks) cleanly in a future jalon.
///
/// <para>
/// <b>J1 status.</b> Currently not queued — navigations execute
/// synchronously on the calling frame. The record exists so we have a
/// shape to extend from in J3+ if back-pressure becomes a real concern
/// (it will the moment scene transitions get fade animations and a user
/// can click while a fade is mid-flight).
/// </para>
/// </summary>
public sealed record NavigationRequest(
    string TargetScreenId,
    ScreenContext Context,
    NavigationKind Kind);

/// <summary>
/// Distinguishes the three navigation operations the SceneManager
/// supports. Modeled as an enum rather than separate methods so the
/// "what kind of navigation is this" decision is data, not control flow.
/// </summary>
public enum NavigationKind
{
    /// <summary>Push a new screen on top of the stack.</summary>
    Push,

    /// <summary>Pop the current screen, returning to the previous one.</summary>
    Pop,

    /// <summary>Pop everything down to the named screen and replace the top.</summary>
    Replace,
}
