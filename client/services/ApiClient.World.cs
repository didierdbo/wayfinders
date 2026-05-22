using System;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Services.Dtos;
using HttpRequestException = System.Net.Http.HttpRequestException;

namespace Wayfinders.Client.Services;

/// <summary>
/// J5 partial of <see cref="ApiClient"/> — the world-referential boundary
/// call (<c>GET /api/world</c>).
///
/// <para>
/// <b>Why a partial file, not another method in ApiClient.cs.</b>
/// <c>ApiClient.cs</c> is already ~1240 lines of mission-emergence boundary
/// methods. The world referential is a self-contained, static-per-session
/// endpoint with a schema-version gate the mission methods do not have ;
/// keeping it in its own partial keeps the diff reviewable and the concern
/// isolated. The class stays one autoload — <c>partial</c> is a compile-time
/// split only ; the <c>_httpClient</c> and <c>_shutdownCts</c> fields
/// declared in <c>ApiClient.cs</c> are shared into this file.
/// </para>
///
/// <para>
/// <b>What is different from the other ApiClient methods.</b> Same linked-CTS
/// cancellation, same <c>ConfigureAwait(false)</c>, same typed
/// <see cref="Result{T, E}"/> with <see cref="ApiError"/> on every catch arm.
/// The one extra step is the <see cref="SupportedWorldSchemaVersion"/> gate :
/// the world referential is versioned, and a mismatch is a hard
/// <see cref="ApiError.DeserializationError"/> — the client refuses to
/// half-parse a future breaking change. This is the integration-time canary
/// if Coda bumps <c>world.yaml</c>'s schema without the C# DTO catching up.
/// </para>
/// </summary>
public partial class ApiClient
{
    /// <summary>
    /// The single <c>world.yaml</c> schema version this client build
    /// understands. Mirrors <c>_WORLD_SCHEMA_VERSION</c> on the FastAPI side
    /// (<c>wayfinders/api/app.py</c>). When Coda bumps the schema in a
    /// breaking way, this constant moves in the same PR that updates the
    /// <see cref="WorldReferentialResponse"/> DTO — and
    /// <see cref="GetWorldAsync"/> rejects every other version loudly until
    /// then.
    /// </summary>
    public const int SupportedWorldSchemaVersion = 1;

    /// <summary>
    /// Hits <c>GET /api/world</c> and deserialises the response into a
    /// <see cref="WorldReferentialResponse"/> via the source-generated
    /// <see cref="ApiJsonContext"/>. J5 boundary call (server endpoint in
    /// <c>wayfinders/api/app.py</c>, Tess commit 55487d7).
    ///
    /// <para>
    /// <b>schema_version gate.</b> On a 2xx response the parsed
    /// <see cref="WorldReferentialResponse.SchemaVersion"/> is checked
    /// against <see cref="SupportedWorldSchemaVersion"/>. A mismatch returns
    /// <see cref="ApiError.DeserializationError"/> — NOT a success with a
    /// possibly-misread payload. The world referential drives map geometry ;
    /// silently mis-parsing it would put cities at the wrong pixels with no
    /// crash, the worst kind of bug. Failing here is the loud, correct
    /// outcome.
    /// </para>
    ///
    /// <para>
    /// <b>Result semantics.</b> Mirrors <see cref="GetUnitsAsync"/> exactly :
    /// every catch arm yields a typed <see cref="ApiError"/>, nothing escapes
    /// to scene code. The caller (<c>WorldMapService</c>) marshals back to
    /// the main thread before mutating any scene state with the result.
    /// </para>
    /// </summary>
    /// <param name="ct">Caller's cancellation token. Linked with the
    /// autoload's shutdown token so app-exit cancels an in-flight boot
    /// fetch.</param>
    public async Task<Result<WorldReferentialResponse, ApiError>> GetWorldAsync(
        CancellationToken ct = default)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        try
        {
            var world = await _httpClient
                .GetFromJsonAsync(
                    "/api/world",
                    ApiJsonContext.Default.WorldReferentialResponse,
                    linkedCts.Token)
                .ConfigureAwait(false);

            if (world is null)
            {
                return Result.Fail<WorldReferentialResponse, ApiError>(
                    new ApiError.DeserializationError(
                        "/api/world returned JSON null instead of a "
                        + "WorldReferentialResponse object"));
            }

            if (world.SchemaVersion != SupportedWorldSchemaVersion)
            {
                // Loud rejection. A version drift means world.yaml moved
                // in a way the C# DTO has not caught up with — half-parsing
                // it would put map markers at silently-wrong pixels.
                GD.PushError(
                    $"[ApiClient] /api/world schema_version "
                    + $"{world.SchemaVersion} is not supported "
                    + $"(client understands {SupportedWorldSchemaVersion}). "
                    + $"Coda bumped world.yaml — the C# DTO must be updated.");
                return Result.Fail<WorldReferentialResponse, ApiError>(
                    new ApiError.DeserializationError(
                        $"/api/world schema_version {world.SchemaVersion} "
                        + $"unsupported; client understands "
                        + $"{SupportedWorldSchemaVersion}"));
            }

            return Result.Ok<WorldReferentialResponse, ApiError>(world);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested || _shutdownCts.IsCancellationRequested)
        {
            return Result.Fail<WorldReferentialResponse, ApiError>(
                new ApiError.Cancelled());
        }
        catch (OperationCanceledException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world timeout: {ex.Message}");
            return Result.Fail<WorldReferentialResponse, ApiError>(
                new ApiError.NotReachable($"timeout: {ex.Message}"));
        }
        catch (HttpRequestException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world network error: {ex.Message}");
            return Result.Fail<WorldReferentialResponse, ApiError>(
                new ApiError.NotReachable(ex.Message));
        }
        catch (System.Net.Http.HttpIOException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world transport error: {ex.Message}");
            return Result.Fail<WorldReferentialResponse, ApiError>(
                new ApiError.NotReachable(ex.Message));
        }
        catch (System.Text.Json.JsonException ex)
        {
            GD.PushWarning($"[ApiClient] /api/world JSON parse error: {ex.Message}");
            return Result.Fail<WorldReferentialResponse, ApiError>(
                new ApiError.DeserializationError(ex.Message));
        }
    }
}
