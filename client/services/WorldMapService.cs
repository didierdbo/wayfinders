using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton holding the cached world referential — the J5
/// boot-time consumer of <c>GET /api/world</c> (server endpoint
/// <c>wayfinders/api/app.py</c>, Tess commit 55487d7).
///
/// <para>
/// <b>Why an autoload (and why this one, not a method on GameScreen).</b>
/// The world referential is exactly the same shape of data as the POI tree
/// (<see cref="PoiTreeService"/>) : referential, static per server version,
/// fetched once at boot, read-only during gameplay. It deserves the same
/// lifetime — an autoload that sits in the scene tree, gets <c>_Ready</c> /
/// <c>_ExitTree</c>, and survives scene reloads. <c>GameScreen</c> reads the
/// cached <see cref="World"/> on its own <c>_Ready</c> ; if the
/// eM→eT navigation later swaps the maquette content, the new scene reads
/// the same cache without a second HTTP round-trip. Putting the fetch in
/// <c>GameScreen</c> would re-hit the endpoint on every map (re)load — the
/// <c>new HttpClient()</c>-per-request mistake's cousin.
/// </para>
///
/// <para>
/// <b>Boot timing (the J5 marker bug, fixed 2026-05-22).</b> The fetch is
/// fired fire-and-forget by <see cref="OpeningBootstrap"/> ; the cache
/// assignment lands via <c>CallDeferred</c> <i>after</i> the whole
/// <c>GameScreen</c> subtree — including <c>HalfgateMarkerLayer</c> — has
/// finished its <c>_Ready</c> pass. A consumer that reads <see cref="World"/>
/// exactly once at <c>_Ready</c> therefore always sees an empty cache and
/// places nothing. The fix is the <see cref="WorldLoaded"/> signal :
/// consumers that must react to the cache filling connect to it, and also
/// handle the already-loaded fast path (cache warm before they subscribe).
/// This mirrors the POI-tree consumer discipline — read the referential
/// when it arrives, not once at boot.
/// </para>
///
/// <para>
/// <b>Graceful degrade.</b> If the HTTP load fails (server down at boot,
/// network glitch, schema-version mismatch), the autoload keeps
/// <see cref="World"/> null and <see cref="LoadFailed"/> true, logs a
/// warning, and still emits <see cref="WorldLoaded"/> so waiting consumers
/// stop waiting. <c>GameScreen</c> handles the null case : the eM mesh still
/// renders (it is a static asset, not server data) but no city marker is
/// placed. MVP behaviour — the map is visible, the marker waits for the
/// next boot with the backend up. No crash, no exception to scene code.
/// </para>
///
/// <para>
/// <b>Thread-safety (trap #4 / #13).</b> The fetch is async ; the cache
/// assignment AND the <see cref="WorldLoaded"/> emission happen on the main
/// thread via a deferred call from the continuation — never directly off
/// the awaited (possibly thread-pool) continuation. Public reads after boot
/// are pure field reads, safe from <c>_Ready</c> / <c>_Process</c> / input
/// handlers without further marshalling. The autoload never touches
/// scene-tree state itself.
/// </para>
/// </summary>
public partial class WorldMapService : Node
{
    private CancellationTokenSource _shutdownCts = null!;

    /// <summary>
    /// Emitted exactly once, on the main thread, when the boot load has
    /// completed — <b>whether it succeeded or failed</b>. This is the
    /// timing seam J5 was missing : the world referential is loaded
    /// fire-and-forget at boot, and <see cref="ApplyLoadResult"/> lands via
    /// <c>CallDeferred</c> <i>after</i> <c>GameScreen</c> and every child
    /// (including <c>HalfgateMarkerLayer</c>) have finished their
    /// <c>_Ready</c> pass. A consumer that read <see cref="World"/> once at
    /// <c>_Ready</c> would always see an empty cache. Consumers that need to
    /// react to the cache filling — the J5 city marker — connect to this
    /// signal instead, and additionally handle the already-loaded case
    /// (cache warm before they subscribe) so there is no race either way.
    ///
    /// <para>
    /// <b>Why a Godot <c>[Signal]</c> and not a C# <c>event</c>.</b> The
    /// publisher is an autoload <see cref="Node"/> and the subscriber
    /// (<c>HalfgateMarkerLayer</c>) is a scene node with a real lifetime ;
    /// a Godot signal auto-disconnects when the subscriber is freed, which
    /// is exactly the discipline a cross-lifetime publisher/subscriber pair
    /// needs (trap #10). It also keeps the seam inspectable in the editor's
    /// Node dock. Fired after the cache fields are assigned, so a handler
    /// reading <see cref="World"/> / <see cref="LoadFailed"/> sees the
    /// final state.
    /// </para>
    /// </summary>
    [Signal]
    public delegate void WorldLoadedEventHandler();

    /// <summary>
    /// The cached world referential, or <c>null</c> until a successful load
    /// completes (or permanently null if the boot load failed). Read-only
    /// after the load — never mutated.
    /// </summary>
    public WorldReferentialResponse? World { get; private set; }

    /// <summary>
    /// True once <see cref="LoadAsync"/> has run to completion, whether it
    /// succeeded or failed. Callers waiting for the cache poll this together
    /// with <see cref="World"/> != null. A consumer subscribing to
    /// <see cref="WorldLoaded"/> after the load already completed checks
    /// this to fire its handler immediately (the already-loaded fast path).
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// True if the boot load completed but produced no usable referential
    /// (HTTP failure, schema-version mismatch). When this is true,
    /// <see cref="World"/> is null and <see cref="IsLoaded"/> is true.
    /// </summary>
    public bool LoadFailed { get; private set; }

    public override void _Ready()
    {
        _shutdownCts = new CancellationTokenSource();
        GD.Print("[WorldMapService] ready (cache empty, awaiting boot load)");
    }

    public override void _ExitTree()
    {
        // Cancel any in-flight boot fetch before disposing. The linked
        // token in LoadAsync observes this; ApiClient maps it to
        // ApiError.Cancelled — no exception, no touch of a freed node.
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        GD.Print("[WorldMapService] disposed");
    }

    /// <summary>
    /// Fire-and-forget boot load. Called by <see cref="OpeningBootstrap"/>
    /// after the autoloads are stable — same boot slot as
    /// <see cref="PoiTreeService.LoadAsync"/>. Idempotent : a second call
    /// replaces the cache (useful for an editor hot-reload of the backend
    /// in dev) and emits <see cref="WorldLoaded"/> again.
    ///
    /// <para>
    /// <b>Error handling.</b> Every failure path logs and leaves the cache
    /// null with <see cref="LoadFailed"/> true ; the autoload never throws
    /// to the caller. <c>GameScreen</c> must handle <see cref="World"/>
    /// being null.
    /// </para>
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var apiClient = GetNodeOrNull<ApiClient>("/root/ApiClient");
        if (apiClient is null)
        {
            GD.PushWarning(
                "[WorldMapService] ApiClient autoload missing — skipping "
                + "world referential load");
            return;
        }

        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        var result = await apiClient
            .GetWorldAsync(linkedCts.Token)
            .ConfigureAwait(false);

        // Marshal back to the main thread before mutating the cache and
        // emitting WorldLoaded — HalfgateMarkerLayer may already be
        // subscribed and will place a marker straight off the signal.
        // Callable.From + lambda capture lets the WorldReferentialResponse
        // (a plain C# record, NOT a Godot Variant type) cross the deferred
        // boundary by reference, bypassing the engine marshaller — same
        // pattern as PoiTreeService.ApplyLoadResult.
        Callable.From(() => ApplyLoadResult(result)).CallDeferred();
    }

    private void ApplyLoadResult(Result<WorldReferentialResponse, ApiError> result)
    {
        IsLoaded = true;

        switch (result)
        {
            case Result<WorldReferentialResponse, ApiError>.Success ok:
                LoadFailed = false;
                World = ok.Value;
                GD.Print(
                    $"[WorldMapService] loaded world referential: "
                    + $"schema_version={ok.Value.SchemaVersion}, "
                    + $"world={ok.Value.World.MaxX}x{ok.Value.World.MaxY} m, "
                    + $"{ok.Value.Cities.Count} cities, "
                    + $"{ok.Value.Districts.Count} districts");
                break;

            case Result<WorldReferentialResponse, ApiError>.Failure fail:
                LoadFailed = true;
                World = null;
                GD.PushWarning(
                    $"[WorldMapService] /api/world load failed: "
                    + $"{fail.Error.GetType().Name} {DescribeError(fail.Error)} "
                    + $"— map renders without a city marker until next boot "
                    + $"with the backend up");
                break;
        }

        // Notify consumers AFTER the cache fields are final. A handler
        // reading World / LoadFailed off this signal sees the settled
        // state. Emitted on both the success and failure path so a
        // consumer waiting for the load (HalfgateMarkerLayer) stops
        // waiting either way — on failure it simply degrades gracefully.
        EmitSignal(SignalName.WorldLoaded);
    }

    /// <summary>
    /// Flattens an <see cref="ApiError"/> variant into a one-line diagnostic
    /// string. Same shape as <c>MissionStore.FailureMessageOf</c> — the
    /// boundary's typed errors carry payloads of different shapes, so the
    /// log line is built per-variant.
    /// </summary>
    private static string DescribeError(ApiError error) => error switch
    {
        ApiError.NotReachable nr => nr.Reason,
        ApiError.ServerError se => $"{se.StatusCode} {se.Body}",
        ApiError.DeserializationError de => de.Reason,
        ApiError.Cancelled => "cancelled",
        _ => "unknown",
    };

    /// <summary>
    /// Returns the cached city with the given slug id, or <c>null</c> if the
    /// referential has not loaded or the id is unknown. Convenience over a
    /// raw <see cref="World"/> LINQ at every call site (the J5 Halfgate
    /// marker, the future eM→eT navigation resolver).
    /// </summary>
    public WorldCityDto? FindCity(string cityId)
    {
        if (string.IsNullOrEmpty(cityId) || World is null)
        {
            return null;
        }
        return World.Cities.FirstOrDefault(c => c.Id == cityId);
    }
}
