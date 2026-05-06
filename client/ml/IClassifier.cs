using System.Threading;
using System.Threading.Tasks;

namespace Wayfinders.Client.Ml;

/// <summary>
/// The single seam between the Wayfinders client and the UC1 ML
/// classifier (Phase 6 L1).
///
/// <para>
/// <b>Why a one-method interface.</b> The interface exists for a single
/// purpose: let Phase 6 L1 ship the boundary today (with
/// <see cref="MockClassifier"/> returning Δ=0), and let Phase 6 L4 swap
/// in the real ONNX-backed implementation later without touching any
/// caller. One method, one return value, one cancellation token —
/// nothing else belongs here. Resist adding "warmup" or "version"
/// methods until L5 actually needs them; an interface that grows ahead
/// of its implementations is taste-debt we will pay back the wrong way.
/// </para>
///
/// <para>
/// <b>Why async.</b> The L4 implementation will run a MiniLM encode pass
/// (~25–50 ms warm, ~200 ms cold on a Steam-class laptop) followed by a
/// head MLP inference (~1 ms). 50 ms on the main thread is three frames
/// at 60 Hz — visible jank. Even though <see cref="MockClassifier"/>
/// could return synchronously today, locking the API as async <i>now</i>
/// avoids contaminating every caller with an async-over-sync rewrite at
/// L4. Same lesson Phase 3 taught for <c>ApiClient</c>: signatures locked
/// against the real seam, mocks adapt, not the other way around. The
/// mock pays the price of a single
/// <see cref="Task.FromResult{TResult}(TResult)"/>; cheap.
/// </para>
///
/// <para>
/// <b>Δ semantics (Pax UC1 architecture §3, encoder lock §3).</b> The
/// returned scalar is the ML contribution to the d20 modifier stack for
/// the current action — fixed range <c>[-5, +5]</c>, already
/// tanh-scaled on the head's output, no further normalization on the
/// C# side. Sign convention: positive Δ means the prose context is
/// <i>favorable</i> to the action (well-suited character, helpful
/// terrain), negative Δ means unfavorable. Phase 7 will add Δ to the
/// modifier stack; Phase 6 stops at "got Δ, logged Δ."
/// </para>
///
/// <para>
/// <b>Cancellation contract.</b> Mirrors <c>ApiClient</c>: callers pass
/// their own <see cref="CancellationToken"/>; implementations are
/// expected to also link with a long-lived shutdown token (the
/// autoload's) so app exit cancels in-flight inferences. The mock
/// honors the token by checking it before returning. Throwing
/// <see cref="System.OperationCanceledException"/> is the contract on
/// cancellation — no <c>Result&lt;T, E&gt;</c> wrapper here, because
/// "ML inference cancelled" is not a domain failure the way an API
/// error is; it is a control-flow event. If a caller mishandles it,
/// the typed exception surfaces in tests and logs immediately.
/// </para>
///
/// <para>
/// <b>Main-thread discipline.</b> Implementations <b>do not</b> touch
/// the scene tree from inside <see cref="ResolveDeltaAsync"/>. Like
/// <c>ApiClient</c>, the classifier is a pure async producer; the
/// caller is responsible for marshalling back to the main thread before
/// using the result on any <see cref="Godot.Node"/>. Using
/// <c>ConfigureAwait(false)</c> inside the implementation makes that
/// explicit.
/// </para>
///
/// <para>
/// <b>AOT-Steam discipline.</b> Methods on this interface use only
/// concrete value types and <see cref="string"/>. No reflection, no
/// generic type parameters that would force the JIT — the L5 AOT audit
/// has nothing to find here.
/// </para>
/// </summary>
public interface IClassifier
{
    /// <summary>
    /// Compute the ML Δ for one (character, action, context) tuple of
    /// rendered prose strings.
    /// </summary>
    /// <param name="characterProse">
    /// Prose snapshot of the acting character: identity, traits,
    /// condition. Comes from the UC1 character renderer (server-side
    /// at L4, hardcoded stubs at L1). Closed-vocab Wayfinders English.
    /// </param>
    /// <param name="actionProse">
    /// Prose snapshot of the action being attempted: shape, aim,
    /// posture. Comes from the UC1 action renderer.
    /// </param>
    /// <param name="contextProse">
    /// Prose snapshot of the surrounding scene: terrain, light,
    /// opposition. Comes from the UC1 context renderer.
    /// </param>
    /// <param name="ct">
    /// Caller's cancellation token. Implementations are expected to
    /// link with a shutdown token; cancellation throws
    /// <see cref="System.OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// A scalar Δ in <c>[-5, +5]</c>. The mock returns <c>0f</c>
    /// unconditionally; the real implementation will return the head's
    /// tanh-scaled output.
    /// </returns>
    Task<float> ResolveDeltaAsync(
        string characterProse,
        string actionProse,
        string contextProse,
        CancellationToken ct = default);
}
