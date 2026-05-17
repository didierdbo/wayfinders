using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services;

/// <summary>
/// Section A autoload (Varn-lock 2026-05-17 + Didier brief 2026-05-17) —
/// authoritative client-side cache of the active emergent missions.
/// Polls <c>GET /api/missions/active</c> via <see cref="ApiClient"/> on
/// every <see cref="WorldSimTick.TickAdvanced"/> signal, replaces its
/// internal cache, and fires <see cref="PendingMissionsChanged"/> when
/// the snapshot differs from the previous one.
///
/// <para>
/// <b>Why an autoload, not a static helper.</b> The Varn-lock §A.3 draft
/// pinned a static helper that projected over <c>GameState.PendingMissions</c>.
/// Didier's 2026-05-17 brief supersedes that : with Tess's backend now
/// holding an authoritative session-scoped MissionStore
/// (commit <c>a01e432</c>), the cleanest model is a client-side autoload
/// that polls the backend's authoritative list. The autoload sits in the
/// scene tree, gets <c>_Ready</c> / <c>_ExitTree</c>, owns a long-lived
/// <see cref="CancellationTokenSource"/> tied to its own lifetime, and
/// re-subscribes / disconnects from <see cref="WorldSimTick"/> cleanly.
/// </para>
///
/// <para>
/// <b>Polling cadence — <see cref="WorldSimTick.TickAdvanced"/>.</b> No
/// independent timer. The store piggybacks on the existing 5-second
/// world-sim tick (configured on the <see cref="WorldSimTick"/> autoload
/// via <c>TickIntervalSeconds</c>). Rationale : the backend's authoritative
/// changes are driven by <c>POST /api/world/tick</c> (mission emergence)
/// and <c>POST /api/world/mission/{resolve,conclude}</c> (mission removal).
/// Polling at the same cadence as the tick guarantees we observe
/// emergence within one tick of it happening, and inherits the cadence
/// override path (a probe scene that bumps <c>TickIntervalSeconds</c>
/// to 0.1 s for a smoke test gets a faster MissionStore poll for free).
/// </para>
///
/// <para>
/// <b>Cache vs <see cref="GameState.PendingMissions"/>.</b> The brief
/// (§A.5) asks <see cref="GameState.PendingMissions"/> to project from
/// <see cref="AllActive"/>. Today, <see cref="GameState.PendingMissions"/>
/// is a mutable <see cref="List{T}"/> that <see cref="WorldSimTick"/>
/// appends to and <c>M1Slice</c> / <c>MissionPanelProbe</c> removes from.
/// Retrofitting it to a computed projection would break those write paths
/// (the next poll would resurrect a mission the player just accepted).
/// Section A scope limits us to the E2 visibility surface ; the dual-source
/// transitional shape is the deliberate scope choice and is flagged for a
/// follow-up unification (probable landing : E2.3+ recruit-panel slice
/// when the M1Slice flow itself migrates to the polled model).
/// </para>
///
/// <para>
/// <b>What this autoload IS the source of truth for.</b>
/// <list type="bullet">
///   <item>E2 marker spawn / despawn — <see cref="GetMissionsForLayer"/>
///         with prefix <c>"e2.halfgate"</c> drives one marker per active
///         mission at the resolved district centroid.</item>
///   <item>E2 marker tooltip composition — <see cref="GetMissionsForPoi"/>
///         with the exact <c>target_poi</c> (e.g.
///         <c>"e2.halfgate.gateway"</c>) yields the DTO(s) the tooltip
///         composer renders.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What this autoload is NOT (yet).</b>
/// <list type="bullet">
///   <item>The source for the E1 Halfgate POI tooltip. That reads
///         <see cref="GameState.PendingMissions"/> directly via
///         <c>IsoMapE1Probe.PopulateMissionsSection</c>. Unchanged this
///         slice.</item>
///   <item>The source for the M1Slice / MissionPanelProbe accept flow.
///         Those mutate <see cref="GameState.PendingMissions"/> directly.
///         Unchanged this slice.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Signal granularity — coarse <see cref="PendingMissionsChanged"/>.</b>
/// One parameterless signal per cache replacement when the snapshot
/// differs (mission added, removed, or content changed). No diff payload
/// in M1 ; the consumer (<c>E2AreaMap</c>) is cheap to re-render
/// (≤6 markers per the M1 cap) and re-projecting from
/// <see cref="GetMissionsForLayer"/> is simpler than reasoning about
/// partial diffs. M2 may introduce a typed delta if the consumer count
/// grows.
/// </para>
///
/// <para>
/// <b>Thread-safety / cancellation.</b> The polling task runs on a
/// thread-pool thread (HTTP continuation via
/// <c>ConfigureAwait(false)</c>). The cache mutation + signal emission
/// hop back to the main thread via <c>CallDeferred</c>, same pattern as
/// <see cref="WorldSimTick.OnMissionEmergedDeferred"/>. The autoload
/// owns a shutdown CTS tied to <c>_ExitTree</c> so any in-flight poll
/// is cancelled cleanly on application exit.
/// </para>
/// </summary>
public partial class MissionStore : Node
{
    /// <summary>
    /// Fired on the main thread when <see cref="AllActive"/> has been
    /// replaced and the new snapshot differs from the previous one.
    /// Consumers re-project from <see cref="GetMissionsForLayer"/> /
    /// <see cref="GetMissionsForPoi"/> at every emission. Coarse-grained
    /// by design (no add/remove deltas) — see the class XML doc for the
    /// rationale.
    /// </summary>
    [Signal]
    public delegate void PendingMissionsChangedEventHandler();

    /// <summary>
    /// Backing cache. Mutated only on the main thread (via the
    /// <see cref="CallDeferred"/> hop from the polling continuation).
    /// Returned as <see cref="IReadOnlyList{T}"/> via
    /// <see cref="AllActive"/> so consumers cannot accidentally mutate
    /// the source of truth.
    /// </summary>
    private List<EmergentMissionDto> _activeMissions = new();

    /// <summary>
    /// Autoload-scoped CTS. Cancels any in-flight
    /// <see cref="ApiClient.GetActiveMissionsAsync"/> when the application
    /// shuts down. The <see cref="ApiClient"/> already links its own
    /// shutdown CTS into the request, but this is the symmetric
    /// caller-side discipline for screen-scoped CTS hygiene
    /// (memory <c>feedback_godot_rendering_input_traps</c> trap #12) —
    /// the autoload IS the "screen" lifetime here.
    /// </summary>
    private CancellationTokenSource _shutdownCts = null!;

    private ApiClient? _apiClient;
    private WorldSimTick? _worldSimTick;

    /// <summary>
    /// Captured handler reference for the <see cref="WorldSimTick.TickAdvanced"/>
    /// subscription. Stored in a field so <see cref="_ExitTree"/> can
    /// disconnect with the exact same delegate (Risk #1 captured-reference
    /// signal-leak discipline, same shape as every other autoload that
    /// subscribes to a sibling signal).
    /// </summary>
    private WorldSimTick.TickAdvancedEventHandler? _tickAdvancedHandler;

    /// <summary>
    /// Re-entrancy guard. <c>_Process</c>-driven ticks fire faster than
    /// an HTTP round-trip ; without a guard a slow backend would queue
    /// up an unbounded poll backlog. Same backpressure pattern as
    /// <see cref="WorldSimTick"/>'s <c>_inFlight</c> counter, simplified
    /// to a boolean flag since we cap at 1.
    /// </summary>
    private int _pollInFlight;

    /// <summary>
    /// Read-only view of the cached active missions, in the server's
    /// canonical order (tick spawn ascending). Stable between
    /// <see cref="PendingMissionsChanged"/> emissions ; callers may
    /// snapshot the reference safely on the main thread.
    /// </summary>
    public IReadOnlyList<EmergentMissionDto> AllActive => _activeMissions;

    public override void _Ready()
    {
        _shutdownCts = new CancellationTokenSource();

        // Resolve sibling autoloads. Defensive : if the autoload order
        // shifts and one is missing, the store no-ops gracefully (an
        // E2 mounted before the first poll lands renders zero markers ;
        // the next poll lights up). project.godot lists ApiClient + then
        // WorldSimTick before MissionStore so this should always succeed.
        _apiClient = GetTree()?.Root?.GetNodeOrNull<ApiClient>("ApiClient");
        _worldSimTick = GetTree()?.Root?.GetNodeOrNull<WorldSimTick>("WorldSimTick");

        if (_apiClient is null)
            GD.PushWarning("[MissionStore] ApiClient autoload not found — polling disabled");
        if (_worldSimTick is null)
            GD.PushWarning("[MissionStore] WorldSimTick autoload not found — polling disabled");

        if (_worldSimTick is not null)
        {
            _tickAdvancedHandler = OnTickAdvanced;
            _worldSimTick.TickAdvanced += _tickAdvancedHandler;
        }

        GD.Print(
            $"[MissionStore] ready — api_wired={_apiClient is not null}, " +
            $"tick_wired={_worldSimTick is not null} (polls on TickAdvanced)");
    }

    public override void _ExitTree()
    {
        // Disconnect the TickAdvanced subscription FIRST so a pending
        // tick signal cannot fire mid-teardown and queue another poll.
        if (_worldSimTick is not null && _tickAdvancedHandler is not null)
        {
            _worldSimTick.TickAdvanced -= _tickAdvancedHandler;
        }
        _tickAdvancedHandler = null;

        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        GD.Print("[MissionStore] disposed");
    }

    /// <summary>
    /// <see cref="WorldSimTick.TickAdvanced"/> handler. Kicks one
    /// <see cref="PollAsync"/> call per tick, capped at one in-flight
    /// request to keep the backlog bounded if the backend stalls.
    /// </summary>
    private void OnTickAdvanced(int tick)
    {
        if (_apiClient is null) return;
        if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
        {
            // A previous poll is still pending. Skip this tick — the next
            // one will reconcile. Mirrors WorldSimTick's drop-on-backpressure
            // strategy.
            return;
        }

        _ = PollAsync(tick);
    }

    /// <summary>
    /// Fetch the authoritative active mission list and reconcile the
    /// cache. Returns a <see cref="System.Threading.Tasks.Task"/> for
    /// testability ; the caller (the TickAdvanced handler) ignores it.
    ///
    /// <para>
    /// <b>Marshalling discipline.</b> The <see cref="ApiClient"/>
    /// continuation resumes on a thread-pool thread (every <c>await</c>
    /// inside the client uses <c>ConfigureAwait(false)</c>). The cache
    /// mutation + signal emission MUST happen on the main thread because
    /// signal consumers are scene-tree nodes. We marshal back via
    /// <see cref="GodotObject.CallDeferred(StringName, Variant[])"/> with
    /// the new list packaged into a <see cref="Godot.Collections.Array"/>
    /// of typed string slots (Variant-friendly) ; the deferred handler
    /// re-materialises the DTOs on the main thread. Same pattern as
    /// <see cref="WorldSimTick.OnMissionEmergedDeferred"/>.
    /// </para>
    /// </summary>
    private async System.Threading.Tasks.Task PollAsync(int tick)
    {
        try
        {
            if (_apiClient is null) return;

            var result = await _apiClient
                .GetActiveMissionsAsync(_shutdownCts.Token)
                .ConfigureAwait(false);

            switch (result)
            {
                case Result<IReadOnlyList<EmergentMissionDto>, ApiError>.Success ok:
                    // Wrap the C# list reference in a Callable closure ; the
                    // deferred dispatcher takes the reference verbatim
                    // (no marshalling) and reassigns the cache on the main
                    // thread. Same shape as
                    // PoiTreeService.ApplyLoadResult's Callable.From hop.
                    var snapshot = ok.Value;
                    Callable.From(() => ApplyPollResult(snapshot, tick)).CallDeferred();
                    break;

                case Result<IReadOnlyList<EmergentMissionDto>, ApiError>.Failure fail:
                    // Failure is logged once per occurrence. The cache stays
                    // stale ; the next tick retries. Cancellation during
                    // shutdown is silently dropped (cancellation IS normal
                    // teardown flow).
                    if (fail.Error is not ApiError.Cancelled)
                    {
                        GD.PushWarning(
                            $"[MissionStore] poll failed (tick={tick}) : " +
                            $"{fail.Error.GetType().Name} {FailureMessageOf(fail.Error)}");
                    }
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _pollInFlight, 0);
        }
    }

    /// <summary>
    /// Main-thread reconciliation : replace the cache and fire the
    /// signal if the snapshot differs. Same idempotence discipline as
    /// <see cref="GameState.SetTileRevealState"/> (no-op silent on
    /// unchanged value).
    ///
    /// <para>
    /// <b>Equality is structural over (Id, TargetPoi).</b> Two snapshots
    /// are "the same" when their mission ids and target POIs line up in
    /// the same order. The server orders by tick spawn ascending so the
    /// order is stable across polls when nothing changes. Mission
    /// content drift on the same id (e.g. a server-side narrative_hook
    /// retune) WOULD be missed by this equality — that is an acceptable
    /// M1 trade-off (mission content is locked at emergence), and a
    /// content hash can be added later if M2 introduces in-flight
    /// content updates.
    /// </para>
    /// </summary>
    private void ApplyPollResult(IReadOnlyList<EmergentMissionDto> snapshot, int tick)
    {
        if (SnapshotMatchesCurrent(snapshot)) return;

        var previousCount = _activeMissions.Count;
        _activeMissions = snapshot.ToList();

        GD.Print(
            $"[MissionStore] cache updated (tick={tick}) : " +
            $"{previousCount} -> {_activeMissions.Count} active mission(s) " +
            $"[{string.Join(", ", _activeMissions.Select(m => m.Id))}]");

        EmitSignal(SignalName.PendingMissionsChanged);
    }

    private bool SnapshotMatchesCurrent(IReadOnlyList<EmergentMissionDto> snapshot)
    {
        if (snapshot.Count != _activeMissions.Count) return false;
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i].Id != _activeMissions[i].Id) return false;
            if (snapshot[i].TargetPoi != _activeMissions[i].TargetPoi) return false;
        }
        return true;
    }

    /// <summary>
    /// Return every active mission whose <see cref="EmergentMissionDto.TargetPoi"/>
    /// equals <paramref name="poiId"/> (exact match). Used by the E2
    /// tooltip composer to fetch the mission(s) anchored on a hovered
    /// marker.
    /// </summary>
    public IReadOnlyList<EmergentMissionDto> GetMissionsForPoi(string poiId)
        => MissionStoreLogic.GetMissionsForPoi(_activeMissions, poiId);

    /// <summary>
    /// Return every active mission whose <see cref="EmergentMissionDto.TargetPoi"/>
    /// starts with <paramref name="layerPrefix"/> (with a trailing dot
    /// separator, OR equals the prefix exactly). E.g.
    /// <c>GetMissionsForLayer("e2.halfgate")</c> yields missions targeting
    /// <c>e2.halfgate.intramuros</c>, <c>e2.halfgate.gateway</c>,
    /// <c>e2.halfgate.littoral</c>, etc. — same hierarchical-id rule as
    /// <see cref="PoiTreeService.IsDescendantOf"/>.
    ///
    /// <para>
    /// <b>NB.</b> This method does NOT consult <see cref="PoiTreeService"/>.
    /// The string-prefix match is the canonical Varn-lock §2 aggregation
    /// rule (the id IS the path), and a freshly-emerged mission targeting
    /// an unknown future POI id still matches its ancestor correctly.
    /// </para>
    /// </summary>
    public IReadOnlyList<EmergentMissionDto> GetMissionsForLayer(string layerPrefix)
        => MissionStoreLogic.GetMissionsForLayer(_activeMissions, layerPrefix);

    private static string FailureMessageOf(ApiError err) => err switch
    {
        ApiError.NotReachable nr => nr.Reason,
        ApiError.ServerError se => $"{se.StatusCode} {se.Body}",
        ApiError.DeserializationError de => de.Reason,
        ApiError.Cancelled => "cancelled",
        _ => "(unknown)"
    };
}
