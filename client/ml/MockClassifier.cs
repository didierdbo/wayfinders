using System.Threading;
using System.Threading.Tasks;

namespace Wayfinders.Client.Ml;

/// <summary>
/// L1 stand-in for the eventual <c>OnnxClassifier</c>. Returns Δ=0 for
/// every input; honors cancellation; that is the entire contract.
///
/// <para>
/// <b>Why Δ=0 (and not pseudo-random noise).</b> L1's job is to ship the
/// seam — interface, autoload registration, scene wiring, log surface —
/// not to validate that the encoder produces sensible vectors. With
/// Δ=0, the path-cost reported by <c>PathfindingService</c> stays the
/// authoritative number on screen, and any visible deviation in Phase
/// 6 L4+ is unambiguously caused by the real model arriving. Adding
/// noise here would muddle the diagnostic surface for no gain. L2 will
/// add the first observable variation when the MiniLM encoder smoke
/// test exercises real cosine similarities.
/// </para>
///
/// <para>
/// <b>Why <see cref="Task.FromResult{TResult}(TResult)"/>, not
/// <c>async</c> with no <c>await</c>.</b> A method declared
/// <c>async</c> with a synchronous body still allocates a state machine
/// per call. <see cref="Task.FromResult{TResult}(TResult)"/> reuses a
/// completed task and skips the machinery — cheaper, and signals to
/// the reader that the mock is intentionally synchronous. The interface
/// stays async (callers cannot tell) and the L4 swap to a real async
/// body changes nothing on the call-site.
/// </para>
///
/// <para>
/// <b>Cancellation honoured.</b>
/// <see cref="CancellationToken.ThrowIfCancellationRequested"/> is the
/// idiomatic check at the top of a synchronous-completion mock: if the
/// caller already cancelled (e.g. scene exited between the click and
/// the call), surface the
/// <see cref="System.OperationCanceledException"/> rather than handing
/// back a stale Δ. Mirrors what the real implementation will do at
/// every <c>await</c> boundary.
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
