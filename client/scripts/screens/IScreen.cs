using System.Threading;
using System.Threading.Tasks;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Lifecycle contract for a stack-managed screen owned by
/// <see cref="Wayfinders.Client.Services.SceneManager"/>.
///
/// <para>
/// Four methods, not two, because the modal overlay use case (E4 fiche over
/// E3 city) requires Suspend/Resume semantics on the underlying screen. If
/// the only callbacks were Enter/Exit, every modal open would force the host
/// scene to either:
/// <list type="bullet">
///   <item>keep its inputs live and fight the modal — input bug surface, or</item>
///   <item>get torn down and rebuilt on close — losing scroll/zoom/state.</item>
/// </list>
/// Suspend freezes the host while keeping its node tree alive ; Resume wakes
/// it back up without rerunning OnEnter. See pre-brief §4.3.
/// </para>
///
/// <para>
/// <b>Thread-safety note (Pre-brief Risk #1, locked).</b>
/// Implementations may use <c>async/await</c> freely, but any continuation
/// that touches the scene tree post-await must resync to the main thread:
/// <code>await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);</code>
/// before mutating nodes. The autoload <c>SceneManager</c> guarantees this
/// at the orchestration layer ; individual screens are responsible inside
/// their own bodies. xUnit cannot catch this — it relies on code review
/// and the closeout J1 checklist.
/// </para>
///
/// <para>
/// <b>Cancellation.</b> Each lifecycle method receives a CancellationToken
/// linked to the SceneManager's per-navigation token. If the user
/// rapid-clicks E1 -&gt; E2 -&gt; back -&gt; E2, an in-flight OnEnter on E2
/// will see its token cancelled. Implementations must respect it (no
/// touching scene state past a thrown OperationCanceledException).
/// </para>
/// </summary>
public interface IScreen
{
    /// <summary>
    /// Stable identifier for this screen, e.g. "E1_TITLE", "E2_WORLD".
    /// Used in navigation logs, signal payloads, and test assertions.
    /// Must not change at runtime.
    /// </summary>
    string ScreenId { get; }

    /// <summary>
    /// Called when the screen is pushed onto the navigation stack and
    /// becomes the active top. Fetch initial data here, wire up signals,
    /// kick off any deferred work.
    /// </summary>
    Task OnEnter(ScreenContext context, CancellationToken ct);

    /// <summary>
    /// Called when the screen is popped from the stack and is about to be
    /// freed. Disconnect signals, cancel in-flight work, release
    /// references. The underlying Node.QueueFree happens after this
    /// returns.
    /// </summary>
    Task OnExit(CancellationToken ct);

    /// <summary>
    /// Called when a modal overlay opens above this screen. The screen
    /// stays in the tree (alive) but should disable its inputs and pause
    /// its visual updates. Must be cheap — racing rapid modal toggles
    /// is the core risk #3 case.
    /// </summary>
    Task OnSuspend(CancellationToken ct);

    /// <summary>
    /// Called when the modal overlay above this screen closes. The screen
    /// regains input focus and resumes visual updates. Must NOT re-fetch
    /// data or rerun OnEnter — the screen has been alive the whole time.
    /// </summary>
    Task OnResume(CancellationToken ct);
}
