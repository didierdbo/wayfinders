namespace Wayfinders.Client.Services;

/// <summary>
/// Discriminated union representing the outcome of an operation that can
/// either succeed with a <typeparamref name="T"/> value or fail with an
/// <typeparamref name="E"/> error. Modelled as a sealed record hierarchy so
/// the C# compiler can enforce exhaustiveness on <c>switch</c> expressions:
/// if a caller adds a new variant later, every existing match site warns
/// (and with <c>TreatWarningsAsErrors</c>, errors) until updated.
///
/// <para>
/// <b>Design principle — make illegal states unrepresentable.</b> Before
/// L3, <see cref="ApiClient.GetUnitsAsync"/> conflated "server returned
/// an empty roster" with "fetch failed" by returning an empty list in
/// both cases. The bug surface that creates is the kind that ships: the
/// UI shows "no units" when the server is on fire, and nobody notices.
/// A typed <c>Result&lt;T, ApiError&gt;</c> moves that distinction into
/// the type system. Callers <i>cannot</i> reach the success value without
/// first acknowledging the failure case — the compiler is the test.
/// </para>
///
/// <para>
/// <b>Why generic in <typeparamref name="E"/>:</b> the wire boundary uses
/// <see cref="ApiError"/>, but the same shape will be useful for other
/// boundaries later (save/load, Steamworks, etc.) with their own error
/// types. Pay the 1 type parameter; reuse the pattern.
/// </para>
///
/// <para>
/// <b>Java reflex callout:</b> Vavr's <c>Either&lt;L, R&gt;</c> is the
/// closest analogue. The .NET-native version is sealed records + pattern
/// matching — no library, no monadic chaining DSL, just data and a
/// <c>switch</c>. Idiomatic .NET 8, AOT-clean, and reads exactly like
/// the runtime cases you care about.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the success value.</typeparam>
/// <typeparam name="E">Type of the failure value.</typeparam>
public abstract record Result<T, E>
{
    // Private constructor closes the hierarchy: only the nested types
    // below can derive. Combined with `sealed record`, this gives the
    // compiler everything it needs for exhaustiveness checking.
    private Result() { }

    /// <summary>The success case. Holds the produced value.</summary>
    public sealed record Success(T Value) : Result<T, E>;

    /// <summary>The failure case. Holds the typed error.</summary>
    public sealed record Failure(E Error) : Result<T, E>;
}

/// <summary>
/// Non-generic factory helpers for <see cref="Result{T, E}"/>. Lets call
/// sites write <c>Result.Ok(value)</c> and <c>Result.Fail&lt;T, ApiError&gt;(err)</c>
/// instead of the ceremonious <c>new Result&lt;T, ApiError&gt;.Success(value)</c>.
///
/// <para>
/// Type inference reaches all the way through <c>Ok</c> from the value's
/// type; <c>Fail</c> needs an explicit <c>T</c> because the success type
/// is not present at the call site.
/// </para>
/// </summary>
public static class Result
{
    public static Result<T, E> Ok<T, E>(T value) => new Result<T, E>.Success(value);

    public static Result<T, E> Fail<T, E>(E error) => new Result<T, E>.Failure(error);
}
