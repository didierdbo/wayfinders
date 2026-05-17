using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the screen-scoped <see cref="CancellationTokenSource"/> discipline
/// locked by Varn 2026-05-17 (consolidated spec Section C ; I-17). Every
/// screen that fires HTTP MUST own a private <c>_screenCts</c>, create it
/// in <c>_Ready</c>, cancel+dispose it in <c>_ExitTree</c>, and pass
/// <c>_screenCts.Token</c> to every <see cref="Wayfinders.Client.Services.ApiClient"/>
/// await. The contract this test suite pins :
///
/// <list type="bullet">
///   <item>When a screen-scoped CTS is cancelled mid-request, the
///         continuation MUST observe the cancel and MUST NOT mutate the
///         shared state list (<c>GameState.PendingMissions</c> stand-in
///         here).</item>
///   <item>The canonical <c>_ExitTree</c> shape (Cancel then Dispose) is
///         safe : neither call throws, and a second Dispose is idempotent.</item>
///   <item>Linking a screen-scoped token with another (the autoload's
///         shutdown token in production ; a parent token here) lets either
///         side fire cancellation - the same shape <see cref="Wayfinders.Client.Services.ApiClient"/>
///         uses internally via <c>CreateLinkedTokenSource</c>.</item>
/// </list>
///
/// <para>
/// <b>Why pure-C#, no Godot.</b> The real <c>ApiClient</c> and
/// <c>GameState</c> are <see cref="Godot.Node"/> autoloads ; spinning them
/// up needs a Godot runtime the test host does not have (same rationale
/// as <c>ClassifierService</c> / <c>MissionPanelProbe</c>). The contract
/// being pinned here is the cancellation-mid-await shape, which is
/// orthogonal to the Godot bindings. The integration smoke (F6 on
/// <c>E2AreaMap.tscn</c>, hover then ESC ; F6 on <c>M1Slice.tscn</c>,
/// trigger a mission then quit before the conclude POST returns) covers
/// the wired-up case end-to-end.
/// </para>
///
/// <para>
/// <b>Stand-in shape.</b> The test uses a <see cref="TaskCompletionSource{T}"/>
/// wrapped behind a method that takes a <see cref="CancellationToken"/> -
/// exact same surface as <c>ApiClient.ResolveMissionAsync(req, ct)</c>.
/// The stand-in for <c>GameState.PendingMissions</c> is a plain
/// <see cref="List{T}"/> of strings ; the test pins that this list is
/// not mutated when the await throws / returns after cancellation.
/// </para>
/// </summary>
public sealed class ScreenScopedCtsContractTests
{
    /// <summary>
    /// Canonical scenario : a screen-scoped CTS is cancelled while an
    /// HTTP await is in flight. The continuation observes the cancel and
    /// returns early, leaving the shared state untouched. Pin matches
    /// the M1Slice.ResolveAndApply / ConcludeOneAsync guard shape
    /// (Varn-lock 2026-05-17 Section C.3).
    /// </summary>
    [Fact]
    public async Task CancelledCts_DoesNotMutatePendingMissions()
    {
        // ARRANGE : a stand-in for GameState.PendingMissions and a
        // controlled stand-in for ApiClient.PostAsync. The stand-in
        // returns a Task we control via TaskCompletionSource, simulating
        // a long-running HTTP call that has not yet replied.
        var pendingMissions = new List<string>();
        var apiStub = new ApiClientStub();
        var screenCts = new CancellationTokenSource();

        // ACT : the screen's await-and-mutate flow. Mirrors the M1Slice
        // shape : await the typed Result, check IsCancellationRequested
        // before touching the state, only mutate on the happy path.
        async Task ScreenFlow()
        {
            var result = await apiStub.PostAsync(screenCts.Token);

            // Varn-lock 2026-05-17 Section C.3 : the screen MUST check
            // cancellation before mutating shared state.
            if (screenCts.IsCancellationRequested)
            {
                return;
            }

            // Would mutate on success path. In M1Slice this branch
            // appends a tag to GameState.PersonaLegacy and pops the
            // head of GameState.PendingMissions.
            pendingMissions.Add(result);
        }

        var screenTask = ScreenFlow();

        // Cancel mid-flight, then complete the API stub - the order
        // matches the real "screen exits while the HTTP response is
        // about to land" race.
        screenCts.Cancel();
        apiStub.CompleteSuccessfully("mission_intramuros_e2");

        await screenTask;

        // ASSERT : the cancellation guard fired ; pendingMissions
        // stayed empty. The success-path mutation was suppressed.
        Assert.Empty(pendingMissions);
    }

    /// <summary>
    /// The canonical _ExitTree shape : Cancel followed immediately by
    /// Dispose. Neither call throws when the source had been used (was
    /// linked, had registered callbacks, etc.). Pin captures the M1Slice
    /// _ExitTree teardown contract.
    /// </summary>
    [Fact]
    public void Cancel_Then_Dispose_DoesNotThrow()
    {
        var cts = new CancellationTokenSource();

        // Touch the token so the source has registered state to clean
        // up (a no-op token never exercises the dispose-after-cancel
        // path that has historically been the source of edge bugs in
        // earlier .NET versions).
        _ = cts.Token.Register(() => { });

        cts.Cancel();
        cts.Dispose();
    }

    /// <summary>
    /// Disposing a non-cancelled source (clean _ExitTree before any
    /// HTTP await fires) does not throw. The early-exit branch matters
    /// for screens that mount, render, and tear down without ever
    /// hitting a button that would have triggered an HTTP call.
    /// </summary>
    [Fact]
    public void Dispose_Without_Cancel_DoesNotThrow()
    {
        var cts = new CancellationTokenSource();
        cts.Dispose();
    }

    /// <summary>
    /// Calling Cancel on a disposed source throws
    /// <see cref="ObjectDisposedException"/>. The canonical _ExitTree
    /// order (Cancel before Dispose) avoids this trap ; the test
    /// pin makes the trap visible so a future refactor that swaps
    /// the order fails loudly here.
    /// </summary>
    [Fact]
    public void Cancel_After_Dispose_Throws_ObjectDisposed()
    {
        var cts = new CancellationTokenSource();
        cts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cts.Cancel());
    }

    /// <summary>
    /// Linking the screen-scoped token with a parent token (the
    /// autoload's shutdown token in production) gives the same
    /// "either side wins" semantics that ApiClient uses internally
    /// via <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, CancellationToken)"/>.
    /// Pin the property : cancelling the parent cancels the linked
    /// token even if the screen-side source was never cancelled.
    /// </summary>
    [Fact]
    public void LinkedCts_Propagates_ParentCancellation_To_ScreenSide()
    {
        var parentCts = new CancellationTokenSource(); // app shutdown
        var screenCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            screenCts.Token, parentCts.Token);

        Assert.False(linked.IsCancellationRequested);

        parentCts.Cancel();

        Assert.True(linked.IsCancellationRequested);
        Assert.False(screenCts.IsCancellationRequested,
            "screen-side source stayed live ; the linkage is one-way (parent -> linked).");
    }

    /// <summary>
    /// Symmetric : cancelling the screen-side source cancels the
    /// linked token without disturbing the parent. The autoload
    /// shutdown token stays live across scene changes ; only the
    /// per-scene token flips. Pin the property explicitly so a
    /// regression in CreateLinkedTokenSource semantics surfaces here.
    /// </summary>
    [Fact]
    public void LinkedCts_Propagates_ScreenCancellation_Without_DisturbingParent()
    {
        var parentCts = new CancellationTokenSource();
        var screenCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            screenCts.Token, parentCts.Token);

        screenCts.Cancel();

        Assert.True(linked.IsCancellationRequested);
        Assert.False(parentCts.IsCancellationRequested);
    }

    /// <summary>
    /// Pin the "Cancel during an awaiter" path. The await on a
    /// <see cref="TaskCompletionSource{T}"/> we control will not
    /// surface OperationCanceledException unless we explicitly wire
    /// the token through - same shape as ApiClient, which catches the
    /// OperationCanceledException internally and returns ApiError.Cancelled.
    /// The screen-side contract is : check IsCancellationRequested ;
    /// do NOT rely on exception flow for the cancel path.
    /// </summary>
    [Fact]
    public async Task CancelledCts_AwaiterDoesNotLeakException_When_StubReturnsNormally()
    {
        var apiStub = new ApiClientStub();
        var screenCts = new CancellationTokenSource();

        var screenTask = apiStub.PostAsync(screenCts.Token);

        screenCts.Cancel();
        apiStub.CompleteSuccessfully("payload");

        // The stub mirrors ApiClient.ResolveMissionAsync : returns
        // normally with a typed value rather than throwing on cancel.
        // The screen is responsible for checking the token, not for
        // catching exceptions.
        var result = await screenTask;
        Assert.Equal("payload", result);
    }

    // -----------------------------------------------------------------
    // Stand-in stubs - pure-C#, Godot-free
    // -----------------------------------------------------------------

    /// <summary>
    /// Minimal stand-in for ApiClient's HTTP-call surface. Returns a
    /// Task that the test controls via <see cref="CompleteSuccessfully"/>.
    /// Mirrors the real method signature (one CancellationToken
    /// parameter) so the production guard pattern works against it
    /// unchanged.
    /// </summary>
    private sealed class ApiClientStub
    {
        private readonly TaskCompletionSource<string> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> PostAsync(CancellationToken ct) => _tcs.Task;

        public void CompleteSuccessfully(string payload) => _tcs.TrySetResult(payload);
    }
}
