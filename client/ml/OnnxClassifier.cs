using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wayfinders.Client.Ml;

/// <summary>
/// Phase 6 L4 deliverable: the production <see cref="IClassifier"/> that
/// composes the L2 <see cref="MiniLmEncoder"/> and the L3 <see cref="ResolutionHead"/>
/// into a single async inference path, ready to be installed in the
/// <see cref="ClassifierService"/> autoload.
///
/// <para>
/// <b>Composition, not inheritance.</b> <see cref="OnnxClassifier"/> does
/// not extend either inner class. It owns one encoder and one head as
/// fields, the same shape Godot teaches for scenes -- a <c>Unit</c> owns
/// a <c>HealthComponent</c> and a <c>MovementComponent</c>, it is not a
/// "kind of" anything. Asking "what does this class own" is the right
/// axis here, not "what is it a subtype of."
/// </para>
///
/// <para>
/// <b>One encoder, one head, one lifetime.</b> Both ORT
/// <c>InferenceSession</c> instances are constructed eagerly in this
/// classifier's constructor and disposed in <see cref="Dispose"/>.
/// Failures (file missing, ONNX shape mismatch) surface during the
/// autoload's <c>_Ready</c>, not on first inference. The autoload
/// disposes this classifier in <c>_ExitTree</c>, which transitively
/// disposes the encoder and the head. Linear ownership chain, no leak
/// surface -- the same lesson L2/L3 taught about native ORT memory.
/// </para>
///
/// <para>
/// <b>Async surface, sync core.</b> <see cref="MiniLmEncoder.Encode"/> and
/// <see cref="ResolutionHead.RunHead"/> are pure CPU work and synchronous.
/// One <see cref="ResolveDeltaAsync"/> call runs <b>three</b> encoder
/// passes (character, action, context prose) plus one head pass. Worst-case
/// warm latency lands around 30 ms -- two frames at 60 Hz. Wrapping the
/// work in <see cref="Task.Run(System.Func{float}, CancellationToken)"/>
/// moves it off the Godot main thread; the <c>await</c> in
/// <c>TacticalScene.ResolveAndLogDeltaAsync</c> already uses
/// <c>ConfigureAwait(false)</c>, so the continuation does not bounce
/// back. Mock-vs-real makes no difference at the call-site -- the L1
/// boundary did its job.
/// </para>
///
/// <para>
/// <b>Cancellation granularity.</b> ORT's CPU EP does not honour token
/// plumbing inside <c>session.Run</c>; the best we can do is check the
/// token between the four work blocks (three encodes + one head). That
/// gives roughly 10 ms cancellation granularity, which is fine for the
/// "scene unloaded mid-inference" semantic the autoload's shutdown CTS
/// implements. Token-aware ORT calls are explicitly out of scope.
/// </para>
///
/// <para>
/// <b>Engine-agnostic.</b> The constructor takes three absolute filesystem
/// paths. The autoload globalises <c>res://</c> paths once at
/// <c>_Ready</c>; the test harness passes plain filesystem paths derived
/// from the repo root. Nothing inside <see cref="OnnxClassifier"/> calls
/// into the Godot runtime, which is exactly what makes it unit-testable
/// from xUnit without spinning up a scene tree.
/// </para>
///
/// <para>
/// <b>L4 does not pre-optimise.</b> Three sequential encoder passes is
/// the simplest correct shape; if profiling at L5 (or in TacticalScene
/// production) shows hot-path pressure, options are (a) a prose-string
/// hash cache, (b) batched <c>(3, seq_len)</c> encoder calls, or (c)
/// three parallel encoder sessions. None are needed today; the parity
/// gate is the L4 milestone, not throughput.
/// </para>
/// </summary>
public sealed class OnnxClassifier : IClassifier, IDisposable
{
    private readonly MiniLmEncoder _encoder;
    private readonly ResolutionHead _head;
    private bool _disposed;

    /// <summary>
    /// Construct the classifier. All three files are required; missing
    /// files throw at construction time so failures surface during the
    /// autoload's <c>_Ready</c>, not on first inference.
    /// </summary>
    /// <param name="encoderOnnxPath">
    /// Absolute path to the encoder ONNX (typically
    /// <c>client/ml/encoder.onnx</c>, ~45 MB fp16 -- shipped via release
    /// asset, not committed).
    /// </param>
    /// <param name="vocabPath">
    /// Absolute path to <c>client/ml/vocab.txt</c> (BERT-uncased
    /// WordPiece vocabulary, 30522 lines).
    /// </param>
    /// <param name="headOnnxPath">
    /// Absolute path to the head ONNX (typically
    /// <c>client/ml/resolution-head-v0.1.onnx</c>, ~3 MB fp32 -- committed
    /// to the repo, see Phase 6 L4 brief).
    /// </param>
    /// <exception cref="System.IO.FileNotFoundException">
    /// Propagated from <see cref="MiniLmEncoder"/> or
    /// <see cref="ResolutionHead"/> if any required file is missing. If
    /// encoder construction succeeds and head construction fails, the
    /// already-constructed encoder is disposed before re-throwing so we
    /// do not leak a native session on a partially-built classifier.
    /// </exception>
    public OnnxClassifier(string encoderOnnxPath, string vocabPath, string headOnnxPath)
    {
        // Order matters: build the encoder first because it is the heavier
        // session (~200 ms cold-start vs sub-ms for the head). If the head
        // fails to construct, dispose the encoder before throwing -- a
        // half-built classifier would leak its native ORT memory otherwise,
        // which is the same lesson L2/L3 taught about IDisposable on
        // InferenceSession.
        _encoder = new MiniLmEncoder(encoderOnnxPath, vocabPath);
        try
        {
            _head = new ResolutionHead(headOnnxPath);
        }
        catch
        {
            _encoder.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Behaviour:
    /// <list type="number">
    /// <item>Throws <see cref="ObjectDisposedException"/> if
    /// <see cref="Dispose"/> has been called.</item>
    /// <item>Honours pre-cancellation synchronously (before
    /// <see cref="Task.Run(System.Func{float}, CancellationToken)"/> is
    /// even dispatched).</item>
    /// <item>Inside the work block, checks the token between the four
    /// inference steps (three encodes + one head) so a mid-flight
    /// cancellation aborts before the next ORT call.</item>
    /// </list>
    /// </para>
    /// <para>
    /// On cancellation, <see cref="OperationCanceledException"/> propagates
    /// through the awaited task, matching the contract documented on
    /// <see cref="IClassifier"/>.
    /// </para>
    /// </remarks>
    public Task<float> ResolveDeltaAsync(
        string characterProse,
        string actionProse,
        string contextProse,
        CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OnnxClassifier));
        }
        // Pre-cancellation check: surface OCE on the caller's thread before
        // we pay for a Task.Run dispatch on an already-doomed inference.
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            // Three encoder passes. The graph is single-threaded
            // (ORT_SEQUENTIAL) and the session is reused across all three
            // calls -- no per-call setup cost beyond the ORT input tensors.
            // Sequential, not batched, on purpose: batching the three prose
            // strings into one (3, seq_len) call would force per-string
            // padding on the C# side and require an encoder graph that
            // honours attention_mask correctly across batch elements; the
            // L2 contract is single-string only. Sub-30 ms warm at single-
            // string is well within the 60 Hz envelope, so the simpler
            // shape ships first. Optimisation options live in §7 risk 3 of
            // the L4 brief, all targeted for L5 if profiling demands.
            ct.ThrowIfCancellationRequested();
            float[] charVec = _encoder.Encode(characterProse);

            ct.ThrowIfCancellationRequested();
            float[] actionVec = _encoder.Encode(actionProse);

            ct.ThrowIfCancellationRequested();
            float[] contextVec = _encoder.Encode(contextProse);

            ct.ThrowIfCancellationRequested();
            return _head.RunHead(charVec, actionVec, contextVec);
        }, ct);
    }

    /// <summary>
    /// Dispose both owned ORT sessions. Safe to call multiple times.
    /// </summary>
    /// <remarks>
    /// Dispose order is head-then-encoder (reverse of construction).
    /// Either order is correct because the two sessions hold no shared
    /// native resources, but reverse-of-construction is the convention
    /// readers expect when chasing a leak.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _head.Dispose();
        _encoder.Dispose();
        _disposed = true;
    }
}
