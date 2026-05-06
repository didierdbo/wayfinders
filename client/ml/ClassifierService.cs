using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Wayfinders.Client.Ml;

/// <summary>
/// Autoload <see cref="Node"/> that owns the current
/// <see cref="IClassifier"/> implementation and exposes a single
/// <see cref="ResolveDeltaAsync"/> entrypoint to scene code (Phase 6 L1).
///
/// <para>
/// <b>Why an autoload, not a static factory or a property-injected
/// service.</b> Godot has no DI container — the scene tree <i>is</i> the
/// dependency graph. Mirroring <see cref="Wayfinders.Client.Services.ApiClient"/>
/// keeps two boundary services (HTTP and ML) shaped the same way:
/// <c>GetNode&lt;ClassifierService&gt;("/root/Classifier")</c> from any
/// scene, no constructor injection, no plumbing. Both autoloads also
/// own a scene-tree-driven <see cref="Node._ExitTree"/> hook for
/// cancellation, which a static class cannot have.
/// </para>
///
/// <para>
/// <b>Why a wrapper Node around an <see cref="IClassifier"/> field, not
/// a Node that <i>is</i> an <see cref="IClassifier"/>.</b> The
/// implementation pick (mock for L1, ONNX-backed for L4) is data, not
/// identity — a one-line swap inside <see cref="_Ready"/>. Keeping
/// <see cref="IClassifier"/> as a plain C# interface implemented by
/// plain C# classes (not a <see cref="Node"/>) means the L4 ONNX
/// implementation can be unit-tested without spinning up a Godot
/// scene tree. The autoload is the bridge between Godot lifetimes and
/// the engine-agnostic ML core.
/// </para>
///
/// <para>
/// <b>Cancellation lifecycle.</b> Same shape as
/// <see cref="Wayfinders.Client.Services.ApiClient"/>: a long-lived
/// shutdown <see cref="CancellationTokenSource"/> tied to the autoload's
/// own <see cref="Node._Ready"/>/<see cref="Node._ExitTree"/>; every
/// <see cref="ResolveDeltaAsync"/> call links the caller's token with
/// the shutdown token so cancellation from either side wins. App exit
/// cancels all in-flight inferences cleanly. Today the mock returns
/// instantly so this is mostly future-proofing for L4, but the
/// boundary is the right place to build it now — once the real ONNX
/// path arrives, callers do not change.
/// </para>
///
/// <para>
/// <b>Thread-safety contract.</b> <see cref="ResolveDeltaAsync"/> does
/// not touch the scene tree. Callers receive the <see cref="float"/> Δ
/// on whatever thread the underlying implementation completed on; if
/// they intend to mutate <see cref="Node"/> state with the result, they
/// must marshal back via
/// <c>await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame)</c>
/// or <see cref="Godot.GodotObject.CallDeferred(StringName, Variant[])"/>.
/// Mock-vs-real makes no difference here; the contract is fixed.
/// </para>
///
/// <para>
/// <b>L4 swap point.</b> When Coda ships the ONNX head and Rune lands
/// the encoder integration (L2 + L3), the only line that changes is
/// the assignment in <see cref="_Ready"/>:
/// <c>_implementation = new MockClassifier();</c> becomes
/// <c>_implementation = new OnnxClassifier(encoderPath, headPath);</c>.
/// Every caller in <see cref="Wayfinders.Client.Scenes.TacticalScene"/>
/// is unaffected. That is the swap-friendliness this whole boundary
/// exists for.
/// </para>
/// </summary>
public partial class ClassifierService : Node
{
    private IClassifier _implementation = null!;
    private CancellationTokenSource _shutdownCts = null!;

    public override void _Ready()
    {
        _shutdownCts = new CancellationTokenSource();

        // L1: ship the boundary with the mock. L4 swaps this single
        // line for `new OnnxClassifier(...)`. The autoload, the
        // shutdown CTS, and every caller stay untouched.
        _implementation = new MockClassifier();

        GD.Print(
            $"[ClassifierService] ready, implementation = " +
            $"{_implementation.GetType().Name}");
    }

    public override void _ExitTree()
    {
        // Cancel any in-flight inference before the autoload tears down.
        // Mirrors ApiClient. Today the mock is instantaneous so nothing
        // is in flight; the discipline is here so L4's 50-100 ms ONNX
        // inferences are properly cancelled when the scene unloads or
        // the app exits.
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();

        GD.Print("[ClassifierService] disposed");
    }

    /// <summary>
    /// Forward to the registered <see cref="IClassifier"/>, linking the
    /// caller's <paramref name="ct"/> with the autoload's shutdown
    /// token.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="System.OperationCanceledException"/> on
    /// cancellation (caller-side or shutdown-side). No
    /// <c>Result&lt;T, E&gt;</c> wrapper here — see
    /// <see cref="IClassifier"/> remarks for the rationale.
    /// </remarks>
    public async Task<float> ResolveDeltaAsync(
        string characterProse,
        string actionProse,
        string contextProse,
        CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, _shutdownCts.Token);

        return await _implementation
            .ResolveDeltaAsync(characterProse, actionProse, contextProse, linkedCts.Token)
            .ConfigureAwait(false);
    }
}
