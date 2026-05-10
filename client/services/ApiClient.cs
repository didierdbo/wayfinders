using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Services.Dtos;

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
/// <b>Thread-safety contract.</b> This autoload deliberately does not touch
/// the scene tree from any of its async methods. <c>ConfigureAwait(false)</c>
/// is used on every <c>await</c> below to make that explicit: the autoload
/// is happy to resume on a thread-pool thread because it never reads or
/// writes <see cref="Node"/> state. Callers, on the other hand, almost
/// always want to mutate scene state with the result — <b>they</b> are
/// responsible for marshalling back to the main thread (see L4 lesson:
/// <c>await ToSignal(GetTree(), ProcessFrame)</c> after the result lands).
/// This separation keeps the boundary clean: the autoload is a pure async
/// producer, the scene is the impure consumer.
/// </para>
///
/// <para>
/// <b>Cancellation lifecycle (L4).</b> The autoload owns a long-lived
/// <see cref="CancellationTokenSource"/> tied to its own
/// <see cref="Node._Ready"/>/<see cref="Node._ExitTree"/> lifetime. Every
/// request links the caller's <see cref="CancellationToken"/> with that
/// shutdown token via
/// <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, CancellationToken)"/>,
/// so cancellation from <i>either</i> side wins:
/// <list type="bullet">
///   <item>Caller cancels (e.g. scene unloads) — the per-request token fires.</item>
///   <item>App is shutting down — <see cref="Node._ExitTree"/> fires the autoload token.</item>
///   <item>Per-request HTTP timeout fires — surfaces as a different exception path.</item>
/// </list>
/// In every case the typed result is <see cref="ApiError.Cancelled"/> or
/// <see cref="ApiError.NotReachable"/>; no exception bubbles out.
/// </para>
///
/// <para>
/// <b>Public results.</b> Methods return <see cref="Result{T, E}"/> with
/// <see cref="ApiError"/> as the failure type — see <c>Result.cs</c> and
/// <c>ApiError.cs</c> for the rationale (L3 lesson).
/// </para>
/// </summary>
public partial class ApiClient : Node
{
    /// <summary>
    /// Base URL for the FastAPI service. Hard-coded for L1 — the dev/hosted
    /// seam (config-driven base URL via a <c>Resource</c>) lands later in
    /// Phase 3+. Keeping this minimal until that work earns its place.
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
        // L4 cancellation lifecycle: cancel any in-flight requests before we
        // dispose the HttpClient. Each linked token in CheckHealthAsync /
        // GetUnitsAsync observes this and the catch arms map it to
        // ApiError.Cancelled. Without this hop, an awaiting scene could try
        // to touch a disposed HttpClient or a freed Node and crash on exit.
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
    /// Hits <c>GET /api/health</c>. Returns a typed result:
    /// <see cref="Result{T, E}.Success"/> with <c>true</c> when the service
    /// responded 2xx with body <c>{"status":"ok"}</c>, otherwise
    /// <see cref="Result{T, E}.Failure"/> with the matching
    /// <see cref="ApiError"/> variant.
    ///
    /// <para>
    /// The success payload is <c>bool</c> rather than a typed
    /// <c>HealthStatus</c> DTO — the endpoint carries one bit of meaning
    /// today. If the contract grows (version, build hash, dependency
    /// statuses) we promote to a record at that point.
    /// </para>
    /// </summary>
    /// <param name="ct">
    /// Caller's cancellation token. Linked with the autoload's shutdown token
    /// so app-exit cancels in-flight requests even if the caller forgot to.
    /// </param>
    public async Task<Result<bool, ApiError>> CheckHealthAsync(CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        try
        {
            var response = await _httpClient.GetAsync("/api/health", linkedCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string? body = null;
                try
                {
                    body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort body capture for diagnostics. If reading
                    // the error body itself fails, surface the status code
                    // alone — that is still actionable.
                }
                GD.PushWarning($"[ApiClient] /api/health returned {(int)response.StatusCode}");
                return Result.Fail<bool, ApiError>(new ApiError.ServerError((int)response.StatusCode, body));
            }

            var okBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

            // Tolerant string match: avoids dragging /api/health into the
            // source-gen JSON context for one boolean. The contract is small
            // enough (one field, one value) that string contains is fine.
            var ok = okBody.Contains("\"status\"", StringComparison.Ordinal)
                  && okBody.Contains("\"ok\"", StringComparison.Ordinal);

            if (!ok)
            {
                // Got 2xx but the body shape is wrong — that's a deserialization
                // failure in spirit even though we did not call a parser.
                return Result.Fail<bool, ApiError>(
                    new ApiError.DeserializationError("health body did not contain {\"status\":\"ok\"}"));
            }

            return Result.Ok<bool, ApiError>(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _shutdownCts.IsCancellationRequested)
        {
            // Caller cancelled, or we're shutting down. Typed result, no throw.
            return Result.Fail<bool, ApiError>(new ApiError.Cancelled());
        }
        catch (OperationCanceledException ex)
        {
            // Token was not fired by the caller or by shutdown — must be the
            // HttpClient.Timeout firing. Maps to NotReachable: from the
            // caller's perspective the server effectively was not there.
            GD.PushWarning($"[ApiClient] /api/health timeout: {ex.Message}");
            return Result.Fail<bool, ApiError>(new ApiError.NotReachable($"timeout: {ex.Message}"));
        }
        catch (HttpRequestException ex)
        {
            GD.PushWarning($"[ApiClient] /api/health network error: {ex.Message}");
            return Result.Fail<bool, ApiError>(new ApiError.NotReachable(ex.Message));
        }
    }

    /// <summary>
    /// Hits <c>GET /api/units</c> and deserializes the response into a typed
    /// list of <see cref="UnitDto"/> via the source-generated
    /// <see cref="ApiJsonContext"/>.
    ///
    /// <para>
    /// Returns <see cref="Result{T, E}.Success"/> with the deserialized list
    /// (which may legitimately be empty — that is no longer conflated with
    /// failure!) or <see cref="Result{T, E}.Failure"/> with the matching
    /// <see cref="ApiError"/> variant.
    /// </para>
    ///
    /// <para>
    /// <b>L4 cancellation contract.</b> If the linked token fires (caller
    /// cancel OR autoload shutdown) anywhere during the request — DNS,
    /// connect, headers, body, JSON read — the call returns
    /// <see cref="ApiError.Cancelled"/>. The caller does not need to catch
    /// anything.
    /// </para>
    /// </summary>
    /// <param name="ct">
    /// Caller's cancellation token. Linked with the autoload's shutdown token.
    /// </param>
    public async Task<Result<IReadOnlyList<UnitDto>, ApiError>> GetUnitsAsync(CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        try
        {
            // GetFromJsonAsync overload with a JsonTypeInfo<T> is the AOT-safe
            // path: it goes through source-gen rather than reflection. The
            // generated property name on the context follows the type's identifier,
            // so `IList<UnitDto>` becomes `IListUnitDto`.
            var units = await _httpClient
                .GetFromJsonAsync(
                    "/api/units",
                    ApiJsonContext.Default.IListUnitDto,
                    linkedCts.Token)
                .ConfigureAwait(false);

            // GetFromJsonAsync returns null when the response body is the
            // literal JSON `null` — should not happen for /api/units (FastAPI
            // returns `[]` at worst), but defending against it costs nothing.
            // Treat null as a deserialization failure rather than an empty
            // list: the contract is "list of units," null is off-contract.
            if (units is null)
            {
                return Result.Fail<IReadOnlyList<UnitDto>, ApiError>(
                    new ApiError.DeserializationError("/api/units returned JSON null instead of an array"));
            }

            return Result.Ok<IReadOnlyList<UnitDto>, ApiError>((IReadOnlyList<UnitDto>)units);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _shutdownCts.IsCancellationRequested)
        {
            return Result.Fail<IReadOnlyList<UnitDto>, ApiError>(new ApiError.Cancelled());
        }
        catch (OperationCanceledException ex)
        {
            GD.PushWarning($"[ApiClient] /api/units timeout: {ex.Message}");
            return Result.Fail<IReadOnlyList<UnitDto>, ApiError>(
                new ApiError.NotReachable($"timeout: {ex.Message}"));
        }
        catch (HttpRequestException ex)
        {
            GD.PushWarning($"[ApiClient] /api/units network error: {ex.Message}");
            return Result.Fail<IReadOnlyList<UnitDto>, ApiError>(new ApiError.NotReachable(ex.Message));
        }
        catch (System.Net.Http.HttpIOException ex)
        {
            // GetFromJsonAsync surfaces some transport faults via HttpIOException
            // (subclass of IOException, .NET 8). Treat as NotReachable.
            GD.PushWarning($"[ApiClient] /api/units transport error: {ex.Message}");
            return Result.Fail<IReadOnlyList<UnitDto>, ApiError>(new ApiError.NotReachable(ex.Message));
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Malformed JSON, missing required field, or wire/DTO drift.
            // This is the integration-time signal that Coda's schema
            // changed and our DTO needs an update.
            GD.PushWarning($"[ApiClient] /api/units JSON parse error: {ex.Message}");
            return Result.Fail<IReadOnlyList<UnitDto>, ApiError>(
                new ApiError.DeserializationError(ex.Message));
        }
    }

    /// <summary>
    /// Hits <c>POST /api/world/tick</c> with a
    /// <see cref="WorldTickRequestDto"/> payload and deserializes the
    /// response into a <see cref="WorldTickResponseDto"/>. This is the
    /// M1 mission-emergence boundary call (Etape 4a, server schema in
    /// <c>wayfinders/api/world_tick_models.py</c>).
    ///
    /// <para>
    /// <b>What this method does.</b> Serialises the request via the
    /// source-generated <see cref="ApiJsonContext"/>, posts it as
    /// <c>application/json</c>, reads the response body via the same
    /// context, and surfaces the typed
    /// <see cref="WorldTickResponseDto"/> in
    /// <see cref="Result{T, E}.Success"/>. <see cref="ApiError"/>
    /// variants on failure mirror <see cref="GetUnitsAsync"/> exactly :
    /// every <c>catch</c> arm has a typed result, no exception escapes
    /// to scene code.
    /// </para>
    ///
    /// <para>
    /// <b>Empty company on the wire.</b> M1
    /// <see cref="WorldTickRequestDto.CompanyPersonas"/> is always
    /// <c>[]</c> (see
    /// <see cref="Wayfinders.Client.Services.Dtos.WorldTickDtos"/>
    /// rationale). The server treats <c>[]</c> as Varn option (c)
    /// "compagnie vide → mission émerge quand même" — the response will
    /// still carry an emergent mission within a cadence window, with
    /// <see cref="EmergentMissionDto.EligiblePersonas"/> empty. Client
    /// renders "aucun perso éligible — décliner ?" in that case.
    /// </para>
    ///
    /// <para>
    /// <b>Thread-safety / cancellation.</b> Same contract as the rest of
    /// this class : the linked token covers caller-cancel + shutdown,
    /// <c>ConfigureAwait(false)</c> on every <c>await</c>, the autoload
    /// never touches scene state. The caller is responsible for
    /// marshalling back to the main thread before mutating scene state
    /// with the response.
    /// </para>
    /// </summary>
    /// <param name="request">
    /// The tick payload. Caller derives <see cref="WorldTickRequestDto.Seed"/>
    /// from <c>(world_seed, tick)</c> for reproducibility.
    /// </param>
    /// <param name="ct">
    /// Caller's cancellation token. Linked with the autoload's shutdown
    /// token.
    /// </param>
    public async Task<Result<WorldTickResponseDto, ApiError>> TickAsync(
        WorldTickRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        try
        {
            // PostAsJsonAsync with an explicit JsonTypeInfo<T> uses the
            // source-gen path (AOT-safe). Same overload as the GET-side
            // GetFromJsonAsync(... JsonTypeInfo, ct ...).
            var response = await _httpClient
                .PostAsJsonAsync(
                    "/api/world/tick",
                    request,
                    ApiJsonContext.Default.WorldTickRequestDto,
                    linkedCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string? body = null;
                try
                {
                    body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort body capture ; if it fails, the status
                    // code alone is still actionable. FastAPI returns
                    // structured 422 payloads on validation failures —
                    // capturing the body lets callers surface the field
                    // that drifted.
                }
                GD.PushWarning($"[ApiClient] /api/world/tick returned {(int)response.StatusCode}");
                return Result.Fail<WorldTickResponseDto, ApiError>(
                    new ApiError.ServerError((int)response.StatusCode, body));
            }

            var tickResponse = await response.Content
                .ReadFromJsonAsync(
                    ApiJsonContext.Default.WorldTickResponseDto,
                    linkedCts.Token)
                .ConfigureAwait(false);

            // ReadFromJsonAsync returns null only when the body is the
            // literal JSON `null`. The server schema declares the response
            // as a non-Optional Pydantic model — null body is off-contract,
            // surface it as deserialization failure rather than letting
            // it propagate as a NullReferenceException to scene code.
            if (tickResponse is null)
            {
                return Result.Fail<WorldTickResponseDto, ApiError>(
                    new ApiError.DeserializationError(
                        "/api/world/tick returned JSON null instead of a WorldTickResponse object"));
            }

            return Result.Ok<WorldTickResponseDto, ApiError>(tickResponse);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _shutdownCts.IsCancellationRequested)
        {
            return Result.Fail<WorldTickResponseDto, ApiError>(new ApiError.Cancelled());
        }
        catch (OperationCanceledException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world/tick timeout: {ex.Message}");
            return Result.Fail<WorldTickResponseDto, ApiError>(
                new ApiError.NotReachable($"timeout: {ex.Message}"));
        }
        catch (HttpRequestException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world/tick network error: {ex.Message}");
            return Result.Fail<WorldTickResponseDto, ApiError>(new ApiError.NotReachable(ex.Message));
        }
        catch (System.Net.Http.HttpIOException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world/tick transport error: {ex.Message}");
            return Result.Fail<WorldTickResponseDto, ApiError>(new ApiError.NotReachable(ex.Message));
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Malformed JSON, missing required field, or wire/DTO drift —
            // the integration-time canary that Coda's schema moved without
            // the C# DTO catching up. M1 special case to watch :
            // `eligible_personas` is now non-empty after Varn-lock 2026-05-10
            // §1 ; if Coda extends EmergentMission with a new required
            // field this is where it shows up first.
            GD.PushWarning($"[ApiClient] /api/world/tick JSON parse error: {ex.Message}");
            return Result.Fail<WorldTickResponseDto, ApiError>(
                new ApiError.DeserializationError(ex.Message));
        }
    }

    /// <summary>
    /// Hits <c>POST /api/world/mission/resolve</c> with a
    /// <see cref="MissionResolveRequestDto"/> payload and deserialises
    /// the response into a <see cref="MissionResolveResponseDto"/>.
    /// M1 substep 4b boundary call (server schema in
    /// <c>wayfinders/api/mission_resolve.py</c>).
    ///
    /// <para>
    /// <b>What this method does.</b> Same discipline as
    /// <see cref="TickAsync"/> : source-gen JSON for both directions,
    /// linked CTS for cancellation, <c>ConfigureAwait(false)</c> on every
    /// <c>await</c>, no scene-state touch in the autoload. Every catch
    /// arm produces a typed <see cref="ApiError"/> ; nothing escapes.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotence contract.</b> The server is stateless ; reposting
    /// the same payload after a transient
    /// <see cref="ApiError.NotReachable"/> produces the same
    /// <see cref="MissionResolveResponseDto.TagsCreated"/>. The client
    /// is responsible for deduplication when storing tags into
    /// <c>GameState.PersonaLegacy</c> using
    /// <c>(persona_id, mission_id)</c> as the natural key. M1 simple
    /// impl : append blindly ; M2 may revisit if duplicate-tag noise
    /// shows up in a Steam build.
    /// </para>
    ///
    /// <para>
    /// <b>Empty assigned_personas.</b> When the player declined the
    /// mission (or no persona was eligible — Varn option (c)), the
    /// caller posts an empty
    /// <see cref="MissionResolveRequestDto.AssignedPersonas"/>. The
    /// server returns
    /// <see cref="MissionResolveResponseDto.TagsCreated"/> = empty.
    /// Always 200 ; not an error.
    /// </para>
    /// </summary>
    /// <param name="request">The mission-resolve payload. Caller is
    /// responsible for filling Outcome (M1 stub : always
    /// <see cref="WorldTickWireFormat.ResolutionOutcome.Success"/>).</param>
    /// <param name="ct">Caller's cancellation token. Linked with the
    /// autoload's shutdown token.</param>
    public async Task<Result<MissionResolveResponseDto, ApiError>> ResolveMissionAsync(
        MissionResolveRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        try
        {
            var response = await _httpClient
                .PostAsJsonAsync(
                    "/api/world/mission/resolve",
                    request,
                    ApiJsonContext.Default.MissionResolveRequestDto,
                    linkedCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string? body = null;
                try
                {
                    body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort body capture ; if it fails, the
                    // status code alone is still actionable. Pydantic
                    // v2 returns structured 422 payloads on validation
                    // failures — capturing the body lets the caller
                    // surface which field drifted.
                }
                GD.PushWarning($"[ApiClient] /api/world/mission/resolve returned {(int)response.StatusCode}");
                return Result.Fail<MissionResolveResponseDto, ApiError>(
                    new ApiError.ServerError((int)response.StatusCode, body));
            }

            var resolveResponse = await response.Content
                .ReadFromJsonAsync(
                    ApiJsonContext.Default.MissionResolveResponseDto,
                    linkedCts.Token)
                .ConfigureAwait(false);

            if (resolveResponse is null)
            {
                return Result.Fail<MissionResolveResponseDto, ApiError>(
                    new ApiError.DeserializationError(
                        "/api/world/mission/resolve returned JSON null instead of a MissionResolveResponse object"));
            }

            return Result.Ok<MissionResolveResponseDto, ApiError>(resolveResponse);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _shutdownCts.IsCancellationRequested)
        {
            return Result.Fail<MissionResolveResponseDto, ApiError>(new ApiError.Cancelled());
        }
        catch (OperationCanceledException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world/mission/resolve timeout: {ex.Message}");
            return Result.Fail<MissionResolveResponseDto, ApiError>(
                new ApiError.NotReachable($"timeout: {ex.Message}"));
        }
        catch (HttpRequestException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world/mission/resolve network error: {ex.Message}");
            return Result.Fail<MissionResolveResponseDto, ApiError>(new ApiError.NotReachable(ex.Message));
        }
        catch (System.Net.Http.HttpIOException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world/mission/resolve transport error: {ex.Message}");
            return Result.Fail<MissionResolveResponseDto, ApiError>(new ApiError.NotReachable(ex.Message));
        }
        catch (System.Text.Json.JsonException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world/mission/resolve JSON parse error: {ex.Message}");
            return Result.Fail<MissionResolveResponseDto, ApiError>(
                new ApiError.DeserializationError(ex.Message));
        }
    }
}
