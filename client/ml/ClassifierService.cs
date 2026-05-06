using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Wayfinders.Client.Ml;

/// <summary>
/// Autoload <see cref="Node"/> that owns the current
/// <see cref="IClassifier"/> implementation and exposes a single
/// <see cref="ResolveDeltaAsync"/> entrypoint to scene code (Phase 6 L1,
/// production swap landed at L4).
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
/// implementation pick (mock for L1, ONNX-backed at L4) is data, not
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
/// cancels all in-flight inferences cleanly. At L4 the implementation
/// runs three encoder passes plus one head pass per call (~30 ms warm),
/// so the cancellation discipline finally pays off.
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
/// <b>L4 swap point.</b> The two surgical edits compared to L1 are:
/// (1) <see cref="_Ready"/> now globalises the three <c>res://ml/</c>
/// asset paths and instantiates <see cref="OnnxClassifier"/> instead of
/// the mock, and (2) <see cref="_ExitTree"/> disposes the implementation
/// when it is <see cref="IDisposable"/>. Every caller in
/// <see cref="Wayfinders.Client.Scenes.TacticalScene"/> is unaffected --
/// the boundary did its job.
/// </para>
/// </summary>
public partial class ClassifierService : Node
{
    private IClassifier _implementation = null!;
    private CancellationTokenSource _shutdownCts = null!;

    public override void _Ready()
    {
        _shutdownCts = new CancellationTokenSource();

        // L4: compose encoder + head into the production classifier.
        // ProjectSettings.GlobalizePath is the bridge between Godot's
        // res:// URIs and the absolute filesystem paths OnnxClassifier
        // expects. We resolve the paths once here -- inside the
        // classifier itself, the API stays Godot-agnostic and remains
        // unit-testable from xUnit.
        string encoderPath = ProjectSettings.GlobalizePath("res://ml/encoder.onnx");
        string vocabPath = ProjectSettings.GlobalizePath("res://ml/vocab.txt");
        string headPath = ProjectSettings.GlobalizePath("res://ml/resolution-head-v0.1.onnx");

        _implementation = new OnnxClassifier(encoderPath, vocabPath, headPath);

        GD.Print(
            $"[ClassifierService] ready, implementation = " +
            $"{_implementation.GetType().Name}");
    }

    public override void _ExitTree()
    {
        // Cancel any in-flight inference before the autoload tears down,
        // then dispose the implementation if it owns native resources
        // (OnnxClassifier owns two ORT InferenceSessions; MockClassifier
        // does not implement IDisposable, hence the runtime check rather
        // than baking IDisposable into the IClassifier contract -- see
        // L4 brief §11.3).
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();

        if (_implementation is IDisposable disposable)
        {
            disposable.Dispose();
        }

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
