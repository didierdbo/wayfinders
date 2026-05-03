namespace Wayfinders.Client.Services;

/// <summary>
/// Sealed discriminated union of failure modes that the FastAPI boundary
/// can produce. Every <c>catch</c> in <see cref="ApiClient"/> maps to one
/// of these variants — no exception bubbles up to scene code, all wire
/// failures are typed.
///
/// <para>
/// <b>Variant set rationale.</b> Held to four cases on purpose. The wire
/// produces a small number of physically distinct outcomes and the UI
/// only needs to react meaningfully to a handful of them; further
/// decomposition (DNS-fail vs connect-refused, 4xx vs 5xx, missing-field
/// vs malformed-JSON) is line-noise until a real consumer asks for it.
/// Resist adding variants until a caller has a use for the distinction.
/// </para>
///
/// <para>
/// <b>Why records not enums.</b> Each variant carries different payload
/// shape (<see cref="ServerError"/> needs status code + body;
/// <see cref="DeserializationError"/> needs a reason string; the others
/// need nothing). Enums force an awkward parallel struct; sealed records
/// model "tag plus payload" cleanly and pattern-match exhaustively.
/// </para>
///
/// <para>
/// <b>Lifecycle.</b> <see cref="Cancelled"/> is included now even though
/// the deeper cancellation discipline lands in L4 — the code path
/// already exists today (the <c>OperationCanceledException</c> branches
/// in <see cref="ApiClient"/>). Modelling it now means L4 is purely
/// behavioural, no new variants.
/// </para>
/// </summary>
public abstract record ApiError
{
    private ApiError() { }

    /// <summary>
    /// Could not reach the server at all: connection refused, DNS failure,
    /// socket timeout, transport-level error. Maps from
    /// <c>HttpRequestException</c> and from
    /// <c>OperationCanceledException</c> raised by <c>HttpClient.Timeout</c>
    /// (i.e. timeout when the caller did not cancel).
    /// </summary>
    public sealed record NotReachable(string Reason) : ApiError;

    /// <summary>
    /// Got an HTTP response, but it was not a 2xx. Carries the status
    /// code and the response body (when available) for diagnostics. The
    /// body is captured because FastAPI returns structured JSON error
    /// payloads on validation failures — caller may want to surface them.
    /// </summary>
    public sealed record ServerError(int StatusCode, string? Body) : ApiError;

    /// <summary>
    /// Got a 2xx response but the body did not deserialize into the
    /// expected DTO. Almost always means the wire schema drifted away
    /// from the C# DTO — Coda's Pydantic model and our record fell out
    /// of sync. This is the integration-time canary.
    /// </summary>
    public sealed record DeserializationError(string Reason) : ApiError;

    /// <summary>
    /// The caller's <c>CancellationToken</c> or the autoload's shutdown
    /// token fired. The scene is expected to silently bail. Modelled as
    /// a <see cref="Result{T, E}.Failure"/> rather than an exception so
    /// callers cannot forget to handle it — the compiler will warn.
    /// </summary>
    public sealed record Cancelled : ApiError;
}
