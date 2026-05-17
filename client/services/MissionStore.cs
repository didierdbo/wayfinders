using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services;

/// <summary>
/// Section A autoload (Varn-lock 2026-05-17 + §A.8.D5 cascade
/// 2026-05-17) — authoritative client-side cache of the active emergent
/// missions. Polls <c>GET /api/missions/active</c> via
/// <see cref="ApiClient"/> on every <see cref="WorldSimTick.TickAdvanced"/>
/// signal, replaces its internal cache, and fires
/// <see cref="PendingMissionsChanged"/> when the snapshot differs from
/// the previous one. Source of truth that <c>GameState.PendingMissions</c>
/// projects over (R4 of the recruit cascade, 2026-05-17).
///
/// <para>
/// <b>§A.8.D5 cascade (R3+R4, 2026-05-17).</b>
/// <see cref="GameState.PendingMissions"/> is now a read-only projection
/// that returns <see cref="AllActive"/> verbatim — the brief retires the
/// independent mutable list on <see cref="GameState"/>. All writers go
/// through this store : the world-tick emergence handler calls
/// <see cref="IngestEmerged"/>, the M1Slice accept / decline paths call
/// <see cref="RemoveById"/> after they POST the matching mission action
/// to the backend. The polling continuation then reconciles the cache
/// against the authoritative server state.
/// </para>
///
/// <para>
/// <b>Why an autoload, not a static helper.</b> The Varn-lock §A.3 draft
/// pinned a static helper that projected over <c>GameState.PendingMissions</c>.
/// Didier's 2026-05-17 brief supersedes that : with Tess's backend now
/// holding an authoritative session-scoped MissionStore
/// (commits <c>a01e432</c> + <c>5131d5f</c>), the cleanest model is a
/// client-side autoload that polls the backend's authoritative list.
/// The autoload sits in the scene tree, gets <c>_Ready</c> /
/// <c>_ExitTree</c>, owns a long-lived
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
/// and the §A.8.D5 action endpoints (accept / decline / conclude / resolve).
/// Polling at the same cadence as the tick guarantees we observe state
/// transitions within one tick of them happening.
/// </para>
///
/// <para>
/// <b>Cap defense (Varn §A.8.D3 belt-and-braces).</b> The server's
/// <c>MissionStore.try_add()</c> enforces the recruit cap N=3 (silent
/// refusal). The client defensively drops the 4th+ recruit mission in
/// both <see cref="IngestEmerged"/> and the polling reconciliation
/// (<see cref="ApplyPollResult"/>). The cap excess is logged at warning
/// level — under normal operation we never hit it ; if we do it surfaces
/// a server bug.
/// </para>
///
/// <para>
/// <b>What this autoload IS the source of truth for.</b>
/// <list type="bullet">
///   <item><see cref="GameState.PendingMissions"/> — read-only projection
///         (§A.8.D5 R4).</item>
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
/// <b>Signal granularity — coarse <see cref="PendingMissionsChanged"/>.</b>
/// One parameterless signal per cache mutation (poll-driven replacement,
/// <see cref="IngestEmerged"/>, <see cref="RemoveById"/>). No diff payload
/// in M1 ; the consumers (<c>E2AreaMap</c>, the M1Slice mission panel)
/// are cheap to re-render and re-projecting from
/// <see cref="GetMissionsForLayer"/> is simpler than reasoning about
/// partial diffs.
/// </para>
///
/// <para>
/// <b>Thread-safety / cancellation.</b> The polling task runs on a
/// thread-pool thread (HTTP continuation via
/// <c>ConfigureAwait(false)</c>). The cache mutation + signal emission
/// hop back to the main thread via <c>CallDeferred</c>, same pattern as
/// <see cref="WorldSimTick.OnMissionEmergedDeferred"/>. The autoload
/// owns a shutdown CTS tied to <c>_ExitTree</c> so any in-flight poll
/// is cancelled cleanly on application exit. <see cref="IngestEmerged"/>
/// and <see cref="RemoveById"/> are MAIN-THREAD only (callers marshal
/// through <c>CallDeferred</c> if they resume off the main thread).
/// </para>
/// </summary>
public partial class MissionStore : Node
{
    /// <summary>
    /// Cap enforced both server-side (Tess commit 5131d5f) and
    /// client-side belt-and-braces. M1 strict recruit-only mode allows
    /// at most one recruit mission per roster NPC ; the roster has 3
    /// entries (kira / dorn / vell). A 4th recruit mission arriving
    /// through any path is dropped + logged at warning level.
    /// </summary>
    internal const int RecruitMissionCap = 3;

    /// <summary>
    /// Fired on the main thread when <see cref="AllActive"/> mutates
    /// (poll replaced the snapshot with a different one,
    /// <see cref="IngestEmerged"/> appended a mission, or
    /// <see cref="RemoveById"/> dropped one). Consumers re-project from
    /// <see cref="GetMissionsForLayer"/> / <see cref="GetMissionsForPoi"/>
    /// at every emission. Coarse-grained by design (no add/remove
    /// deltas) — see the class XML doc for the rationale.
    /// </summary>
    [Signal]
    public delegate void PendingMissionsChangedEventHandler();

    /// <summary>
    /// Backing cache. Mutated only on the main thread (via the
    /// <see cref="CallDeferred"/> hop from the polling continuation, or
    /// directly by <see cref="IngestEmerged"/> /
    /// <see cref="RemoveById"/>). Returned as <see cref="IReadOnlyList{T}"/>
    /// via <see cref="AllActive"/> so consumers cannot accidentally mutate
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
    /// §A.8.D5 cascade — ingest a freshly-emerged mission into the cache
    /// without waiting for the next poll. Called by
    /// <see cref="WorldSimTick.OnMissionEmergedDeferred"/> the instant
    /// <c>/api/world/tick</c> returns with a mission, so the marker /
    /// tooltip / E1 reveal pipeline lights up on the SAME engine frame
    /// as the emergence (instead of one polling cycle later).
    ///
    /// <para>
    /// <b>Main-thread only.</b> The caller is responsible for marshalling
    /// via <c>CallDeferred</c> if the response continuation resumed off
    /// the main thread (WorldSimTick.OnMissionEmergedDeferred does this).
    /// </para>
    ///
    /// <para>
    /// <b>Idempotence.</b> If a mission with the same
    /// <see cref="EmergentMissionDto.Id"/> is already in the cache the
    /// call is a silent no-op (Trace log). Handles the rare case where
    /// the tick response races a poll that already absorbed the mission.
    /// </para>
    ///
    /// <para>
    /// <b>Cap defense (Varn §A.8.D3).</b> If accepting this mission
    /// would push the recruit-type count past
    /// <see cref="RecruitMissionCap"/> the mission is DROPPED with a
    /// warning. Server-side <c>try_add</c> already enforces the cap ;
    /// reaching this branch on the client means a server bug or a
    /// race condition.
    /// </para>
    /// </summary>
    public void IngestEmerged(EmergentMissionDto mission)
    {
        if (mission is null) return;

        // Idempotence : second arrival of the same mission id is a no-op.
        for (var i = 0; i < _activeMissions.Count; i++)
        {
            if (_activeMissions[i].Id == mission.Id)
            {
                GD.PrintRaw($"[MissionStore] IngestEmerged id={mission.Id} : already present, no-op\n");
                return;
            }
        }

        // Cap defense : refuse a 4th+ recruit mission.
        if (mission.Type == WorldTickWireFormat.MissionType.Recruit)
        {
            var currentRecruitCount = CountRecruitMissions(_activeMissions);
            if (currentRecruitCount >= RecruitMissionCap)
            {
                GD.PushWarning(
                    $"[MissionStore] IngestEmerged id={mission.Id} npc={mission.RecruitTargetNpcId} : " +
                    $"recruit cap N={RecruitMissionCap} already reached (server bug ?) — dropping");
                return;
            }
        }

        _activeMissions.Add(mission);
        GD.Print(
            $"[MissionStore] ingested id={mission.Id} type={mission.Type} " +
            $"npc={mission.RecruitTargetNpcId ?? "(n/a)"} target_poi={mission.TargetPoi} " +
            $"-> {_activeMissions.Count} active");
        EmitSignal(SignalName.PendingMissionsChanged);
    }

    /// <summary>
    /// §A.8.D5 cascade — remove a mission from the local cache by id.
    /// Called by M1Slice after a successful POST to
    /// <c>/api/missions/&lt;id&gt;/accept</c> or
    /// <c>/api/missions/&lt;id&gt;/decline</c> to drop the mission from
    /// the projection without waiting for the next poll. The next poll
    /// reconciles the cache against the authoritative server state.
    ///
    /// <para>
    /// <b>Idempotence.</b> Removing a missing id is a silent no-op
    /// (Trace log). Same shape as the
    /// <see cref="System.Collections.Generic.List{T}.RemoveAll(System.Predicate{T})"/>
    /// semantic.
    /// </para>
    ///
    /// <para>
    /// <b>Main-thread only.</b> Callers that resume off the main thread
    /// (HTTP continuations) marshal via <c>CallDeferred</c> first.
    /// </para>
    /// </summary>
    public bool RemoveById(string missionId)
    {
        if (string.IsNullOrEmpty(missionId)) return false;
        for (var i = _activeMissions.Count - 1; i >= 0; i--)
        {
            if (_activeMissions[i].Id == missionId)
            {
                _activeMissions.RemoveAt(i);
                GD.Print(
                    $"[MissionStore] removed id={missionId} " +
                    $"-> {_activeMissions.Count} active");
                EmitSignal(SignalName.PendingMissionsChanged);
                return true;
            }
        }
        GD.PrintRaw($"[MissionStore] RemoveById id={missionId} : not found, no-op\n");
        return false;
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
    ///
    /// <para>
    /// <b>Cap defense (Varn §A.8.D3 belt-and-braces).</b> If the
    /// authoritative server response somehow contains &gt;N=3 recruit
    /// missions the surplus is trimmed locally + a warning is logged.
    /// Under normal operation this is unreachable (server enforces
    /// <c>try_add</c> cap). Defensive against a race or a future test
    /// fixture that bypasses the server cap.
    /// </para>
    /// </summary>
    private void ApplyPollResult(IReadOnlyList<EmergentMissionDto> snapshot, int tick)
    {
        var effective = EnforceRecruitCap(snapshot, tick);

        if (SnapshotMatchesCurrent(effective)) return;

        var previousCount = _activeMissions.Count;
        _activeMissions = effective.ToList();

        GD.Print(
            $"[MissionStore] cache updated (tick={tick}) : " +
            $"{previousCount} -> {_activeMissions.Count} active mission(s) " +
            $"[{string.Join(", ", _activeMissions.Select(m => m.Id))}]");

        EmitSignal(SignalName.PendingMissionsChanged);
    }

    /// <summary>
    /// Trim a poll snapshot down to at most
    /// <see cref="RecruitMissionCap"/> recruit-type missions. Non-recruit
    /// missions pass through untouched. Logs at warning level when the
    /// trim actually happens.
    /// </summary>
    private static IReadOnlyList<EmergentMissionDto> EnforceRecruitCap(
        IReadOnlyList<EmergentMissionDto> snapshot, int tick)
    {
        if (CountRecruitMissions(snapshot) <= RecruitMissionCap) return snapshot;

        var trimmed = new List<EmergentMissionDto>(snapshot.Count);
        var recruitsSoFar = 0;
        var dropped = 0;
        foreach (var mission in snapshot)
        {
            if (mission.Type == WorldTickWireFormat.MissionType.Recruit)
            {
                if (recruitsSoFar >= RecruitMissionCap)
                {
                    dropped++;
                    continue;
                }
                recruitsSoFar++;
            }
            trimmed.Add(mission);
        }
        GD.PushWarning(
            $"[MissionStore] poll (tick={tick}) returned {snapshot.Count} missions " +
            $"with >{RecruitMissionCap} recruit-type — dropping {dropped} surplus " +
            $"(server cap bypass ? race ?)");
        return trimmed;
    }

    private static int CountRecruitMissions(IReadOnlyList<EmergentMissionDto> missions)
    {
        var n = 0;
        for (var i = 0; i < missions.Count; i++)
        {
            if (missions[i].Type == WorldTickWireFormat.MissionType.Recruit) n++;
        }
        return n;
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
