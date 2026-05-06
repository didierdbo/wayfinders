using System.Threading;
using System.Threading.Tasks;
using Wayfinders.Client.Ml;

namespace Wayfinders.Client.Tests.Doubles;

/// <summary>
/// Test double for <see cref="IClassifier"/>. Returns Δ=0 for every input;
/// honours cancellation; that is the entire contract.
///
/// <para>
/// <b>Provenance.</b> This class shipped first as the L1 stand-in for the
/// eventual <c>OnnxClassifier</c>; at L4 the autoload swapped to the real
/// ONNX-backed classifier and this file moved from <c>client/ml/</c> to
/// <c>client/tests/Doubles/</c>. Keeping it as a test-only double rather
/// than deleting it pays off for any future scene-level integration test
/// that wants to inject a deterministic <see cref="IClassifier"/> without
/// loading 50 MB of ONNX assets -- the original Phase 6 L1 lesson stays
/// visible in code, in the right project.
/// </para>
///
/// <para>
/// <b>Why Δ=0 (and not pseudo-random noise).</b> A constant return makes
/// any visible deviation in tests unambiguously caused by the real model
/// arriving (or by a bug elsewhere on the path). Noise would muddle the
/// diagnostic surface for no gain.
/// </para>
///
/// <para>
/// <b>Why <see cref="Task.FromResult{TResult}(TResult)"/>, not
/// <c>async</c> with no <c>await</c>.</b> A method declared <c>async</c>
/// with a synchronous body still allocates a state machine per call.
/// <see cref="Task.FromResult{TResult}(TResult)"/> reuses a completed
/// task and skips the machinery -- cheaper, and signals to the reader
/// that the mock is intentionally synchronous. The interface stays async
/// (callers cannot tell) and a future test that wants to await this
/// double behaves identically to awaiting <c>OnnxClassifier</c>.
/// </para>
///
/// <para>
/// <b>Cancellation honoured.</b>
/// <see cref="CancellationToken.ThrowIfCancellationRequested"/> is the
/// idiomatic check at the top of a synchronous-completion mock: if the
/// caller already cancelled, surface the
/// <see cref="System.OperationCanceledException"/> rather than handing
/// back a stale Δ. Mirrors what <see cref="OnnxClassifier"/> does at
/// every cancellation boundary.
/// </para>
///
/// <para>
/// <b>Not <see cref="System.IDisposable"/> on purpose.</b> The
/// <see cref="IClassifier"/> interface is lifecycle-agnostic (L4 brief
/// §11.3); only implementations that own native resources opt in. The
/// autoload's <c>_ExitTree</c> uses a runtime <c>is IDisposable</c>
/// check, which lets this mock stay free of an empty <c>Dispose()</c>
/// stub.
/// </para>
/// </summary>
public sealed class MockClassifier : IClassifier
{
    /// <inheritdoc />
    public Task<float> ResolveDeltaAsync(
        string characterProse,
        string actionProse,
        string contextProse,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(0f);
    }
}
