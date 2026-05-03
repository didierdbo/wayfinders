using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;

// Godot ships its own engine-side Godot.HttpClient (the GDScript-style
// HTTPRequest binding). The Wayfinders verdict (Phase 3 brief, locked) is
// **System.Net.Http** — better tooling, testable, AOT-friendly. The alias
// below disambiguates so `using Godot;` doesn't poison the namespace and
// makes the choice visible to every reader of this file.
using HttpClient = System.Net.Http.HttpClient;
using HttpRequestException = System.Net.Http.HttpRequestException;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton wrapping a single, long-lived <see cref="HttpClient"/> for
/// all calls to the FastAPI game-logic service.
///
/// <para>
/// Why an autoload, not a static class: this is a <see cref="Node"/>, so it sits
/// in the scene tree, gets <see cref="Node._Ready"/> and <see cref="Node._ExitTree"/>,
/// and can own scene-tree-shaped lifetime concerns (cancellation on shutdown,
/// disposal on app exit). Static state in C# would survive scene reloads in the
/// editor in awkward ways and has no engine-driven teardown.
/// </para>
///
/// <para>
/// Why one shared <see cref="HttpClient"/>: <c>HttpClient</c> is designed to be
/// long-lived. <c>new HttpClient()</c> per request leaks socket handles and
/// eventually exhausts ephemeral ports under load — a well-known .NET footgun.
/// One instance per app, owned here, is the correct pattern.
/// </para>
///
/// <para>
/// Thread note: this autoload is created on the main thread by the engine.
/// HTTP <c>await</c> continuations may resume on a thread-pool thread.
/// L1 only flips a UI label after the await, but **any future caller that
/// touches the scene tree after an await must use <c>CallDeferred</c> or
/// equivalent**. That discipline lands in L4.
/// </para>
/// </summary>
public partial class ApiClient : Node
{
    /// <summary>
    /// Base URL for the FastAPI service. Hard-coded for L1 — the dev/hosted
    /// seam (config-driven base URL via a <c>Resource</c>) lands later in
    /// Phase 3. Keeping this minimal until that work earns its place.
    /// </summary>
    private const string DefaultBaseUrl = "http://localhost:8000";

    /// <summary>
    /// Default per-request timeout. .NET's stock 100s default is wrong for a
    /// game client — we want to fail fast and let the caller decide whether
    /// to retry or surface an error.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private HttpClient _httpClient = null!;
    private CancellationTokenSource _shutdownCts = null!;

    public override void _Ready()
    {
        _shutdownCts = new CancellationTokenSource();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(DefaultBaseUrl),
            Timeout = DefaultTimeout,
        };

        GD.Print($"[ApiClient] ready, base URL = {DefaultBaseUrl}");
    }

    public override void _ExitTree()
    {
        // Cancel any in-flight requests. Callers awaiting GetFromJsonAsync etc.
        // will observe an OperationCanceledException — they should treat that
        // as "we're shutting down, do nothing."
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();

        // Dispose the client so its underlying HttpMessageHandler closes its
        // connection pool. Cheap belt-and-suspenders; the OS would reclaim on
        // process exit, but explicit shutdown matters once we run inside the
        // editor across many play/stop cycles.
        _httpClient.Dispose();

        GD.Print("[ApiClient] disposed");
    }

    /// <summary>
    /// Hits <c>GET /api/health</c>. Returns <c>true</c> if the service responded
    /// 2xx with body <c>{"status":"ok"}</c>, <c>false</c> on any other outcome
    /// (non-success status, malformed body, network error).
    ///
    /// <para>
    /// Bool return is deliberate L1 scope. L3 introduces a typed
    /// <c>Result&lt;T, ApiError&gt;</c> wrapper when richer error semantics
    /// (retry vs surface vs ignore) start to matter for the units endpoint.
    /// Keeping L1 cheap to read and cheap to throw away.
    /// </para>
    /// </summary>
    /// <param name="ct">
    /// Caller's cancellation token. Linked with the autoload's shutdown token
    /// so app-exit cancels in-flight requests even if the caller forgot to.
    /// </param>
    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        try
        {
            var response = await _httpClient.GetAsync("/api/health", linkedCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                GD.PushWarning($"[ApiClient] /api/health returned {(int)response.StatusCode}");
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

            // Tolerant string match for L1: avoids dragging in System.Text.Json
            // before the source-gen context lands in L2. The contract is small
            // enough (one field, one value) that string contains is fine until
            // the typed-DTO pipeline exists.
            return body.Contains("\"status\"", StringComparison.Ordinal)
                && body.Contains("\"ok\"", StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _shutdownCts.IsCancellationRequested)
        {
            // Caller cancelled, or we're shutting down. Propagate so the
            // caller can decide not to touch the scene tree.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Token was not fired by the caller or by shutdown — must be the
            // HttpClient.Timeout firing. Surface as a non-fatal failure.
            // (TaskCanceledException derives from OperationCanceledException;
            // .NET 8 raises a TimeoutException inner for this case but the
            // outer type is the same.)
            GD.PushWarning($"[ApiClient] /api/health timeout: {ex.Message}");
            return false;
        }
        catch (HttpRequestException ex)
        {
            GD.PushWarning($"[ApiClient] /api/health network error: {ex.Message}");
            return false;
        }
    }
}
