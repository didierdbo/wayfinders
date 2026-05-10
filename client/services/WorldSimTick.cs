using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services;

/// <summary>
/// World-simulation tick autoload. Drives the M1 mission-emergence
/// loop (substep 4b) :
///
/// <list type="number">
///   <item>Every tick (default 1 wall-clock second), build a
///         <see cref="WorldTickRequestDto"/> from the current
///         <see cref="GameState"/> snapshot.</item>
///   <item>Fire <see cref="ApiClient.TickAsync"/> async (does NOT
///         block <c>_Process</c> — the rendering thread keeps
///         running).</item>
///   <item>When the response carries an
///         <see cref="EmergentMissionDto"/>, marshal back to the
///         main thread, append it to
///         <c>GameState.PendingMissions</c>, and emit
///         <see cref="MissionEmerged"/>.</item>
/// </list>
///
/// <para>
/// <b>Cadence — 1 tick / 5 seconds.</b> Locked by Didier 2026-05-10
/// (memory <c>project_wayfinders_mission_emergence.md</c>) — gives
/// the world-loop a real-second pace and removes the API-burst risk
/// that 1-tick-per-frame would create on a 144 Hz monitor. The
/// cadence math itself lives in
/// <see cref="WorldSimTickCadence"/> (pure C#, xUnit-pinned) ; this
/// autoload is the engine glue (delta source, async fire, scene-tree
/// marshalling).
/// </para>
///
/// <para>
/// <b>Why <c>_Process(delta)</c> + accumulator, not a Godot Timer
/// node.</b> <see cref="WorldSimTickCadence"/> is testable in
/// isolation — its inputs are explicit deltas. A Timer node is
/// engine-bound, fires on the main thread when it pleases, and
/// cannot be exercised in xUnit without spinning up Godot. The
/// accumulator is the same pattern used in tactical-game runtime
/// loops (Battle Brothers, XCOM-style) for that reason. Cost is
/// negligible (a sub-microsecond addition per frame).
/// </para>
///
/// <para>
/// <b>Thread-safety contract.</b>
/// <see cref="ApiClient.TickAsync"/> resumes on a thread-pool thread
/// after the HTTP round-trip (<c>ConfigureAwait(false)</c>). The
/// continuation in <see cref="OnTickResponseAsync"/> mutates
/// <see cref="GameState.PendingMissions"/> and emits a
/// <see cref="Signal"/> — both scene-tree-touching ops. We marshal
/// back to the main thread via
/// <c>CallDeferred</c> + a private dispatch handler. The Godot
/// canonical pattern for "I have a payload, deliver it on the next
/// engine frame" — see lesson L4 / pre-brief §4.7. Without the
/// marshalling, the signal emission is undefined behaviour ("works
/// 99 times then crashes the day before launch").
/// </para>
///
/// <para>
/// <b>Backpressure.</b> If the API takes longer than
/// <see cref="WorldSimTickCadence.IntervalSeconds"/> to respond, a
/// second tick can fire while the first is still in flight. M1
/// strategy : <see cref="_inFlight"/> is a counter ; we cap
/// concurrent in-flight requests at
/// <see cref="MaxConcurrentRequests"/> = 1, dropping (and logging)
/// a tick if the cap is reached. A dropped tick still increments
/// <see cref="WorldSimTickCadence.Tick"/> in the cadence — the world
/// clock advances ; only the API call is skipped. M2 may revisit
/// with a queue if the dropped-tick rate becomes a problem.
/// </para>
///
/// <para>
/// <b>Cancellation discipline.</b> A long-lived
/// <see cref="CancellationTokenSource"/> tied to <c>_ExitTree</c>
/// covers any in-flight tick when the autoload tears down. The
/// <see cref="ApiClient"/> already links the caller token with its
/// own shutdown token, so we get double-fault protection : either
/// side cancelling cancels the request.
/// </para>
/// </summary>
public partial class WorldSimTick : Node
{
    /// <summary>Wall-clock seconds between ticks. Default 5.0
    /// (Didier 2026-05-10, retuned post-4d e2e probe). At 1.0 s the
    /// observable cadence was 1 mission every 5-10 s wall-clock
    /// (server-side fenetre 5-10 ticks Varn-locked, unchanged) —
    /// too dense for observation/jouabilite. 5.0 s stretches the
    /// definition of a tick to 25-50 s wall-clock between missions,
    /// preserving the server lock. Exported so a probe scene can
    /// override (e.g. 0.1 s for a smoke test, or M2 game-design
    /// retune without recompile).</summary>
    [Export] public double TickIntervalSeconds { get; set; } = 5.0;

    /// <summary>Initial seed for the world. The per-tick seed is
    /// derived as <c>WorldSeed * 0x9E3779B1L + tick</c> (golden-ratio
    /// mul) so each tick gets a distinct, reproducible seed.
    /// 0x9E3779B1 is the 32-bit golden-ratio constant — Knuth's
    /// fibonacci hash multiplier — chosen because successive integer
    /// inputs through it produce well-spread outputs (see the C
    /// standard library implementations of <c>djb2_hash</c>'s
    /// cousins). Servers reproduce the same seed given
    /// <c>(WorldSeed, tick)</c>.</summary>
    [Export] public long WorldSeed { get; set; } = 1L;

    /// <summary>Maximum concurrent in-flight ticks. Beyond this, a
    /// tick is dropped (cadence still advances, API call is skipped).
    /// M1 = 1 ; raise if a future M2 design requires it.</summary>
    private const int MaxConcurrentRequests = 1;

    /// <summary>Default context prose seed for M1. The renderer
    /// integration is M2 ; for M1 the server only requires
    /// non-empty prose to satisfy the Pydantic
    /// <c>min_length=1</c> validator. Once the renderer lands, the
    /// caller of <see cref="BuildContextProse"/> swaps this for a
    /// real <c>render_context()</c> call.</summary>
    private const string M1ContextProseStub =
        "The wayfinders' company moves through the open country.";

    /// <summary>Emitted on the main thread when an
    /// <see cref="EmergentMissionDto"/> lands in
    /// <see cref="GameState.PendingMissions"/>. Subscribers (e.g. the
    /// Mission panel) listen and pop a UI affordance.</summary>
    [Signal]
    public delegate void MissionEmergedEventHandler(string missionId);

    /// <summary>Emitted on the main thread when a tick is processed
    /// (mission or no mission). Useful for HUD debug overlays that
    /// want to show the running tick count.</summary>
    [Signal]
    public delegate void TickAdvancedEventHandler(int tick);

    private WorldSimTickCadence _cadence = null!;
    private CancellationTokenSource _shutdownCts = null!;
    private ApiClient? _apiClient;
    private GameState? _gameState;
    private int _inFlight;

    public override void _Ready()
    {
        _cadence = new WorldSimTickCadence(TickIntervalSeconds);
        _shutdownCts = new CancellationTokenSource();

        // Resolve sibling autoloads via /root. Defensive : if the
        // autoload order changes, GetNodeOrNull returns null and the
        // process gracefully no-ops with a clear log instead of a
        // null-deref crash. The order in project.godot has ApiClient
        // first and WorldSimTick later so this should always succeed,
        // but the canary catches a future re-ordering.
        _apiClient = GetTree()?.Root?.GetNodeOrNull<ApiClient>("ApiClient");
        _gameState = GetTree()?.Root?.GetNodeOrNull<GameState>("GameState");

        if (_apiClient is null)
            GD.PushWarning("[WorldSimTick] ApiClient autoload not found — ticks will run pure-cadence (no API calls)");
        if (_gameState is null)
            GD.PushWarning("[WorldSimTick] GameState autoload not found — ticks will run with empty company snapshot");

        GD.Print($"[WorldSimTick] ready — interval={TickIntervalSeconds}s, world_seed={WorldSeed}");
    }

    public override void _ExitTree()
    {
        // Cancel any in-flight tick. The ApiClient also cancels on
        // its own _ExitTree ; double protection (autoload teardown
        // ordering is engine-defined, do not rely on a particular
        // sequence).
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        GD.Print("[WorldSimTick] disposed");
    }

    public override void _Process(double delta)
    {
        var bumped = _cadence.Advance(delta);
        if (bumped == 0) return;

        // One signal emit per bumped tick — the HUD overlay listens.
        for (var i = 0; i < bumped; i++)
        {
            EmitSignal(SignalName.TickAdvanced, _cadence.Tick - (bumped - 1 - i));
        }

        // Backpressure : drop the API call if we're already at the
        // cap. The world cadence keeps advancing ; only the API is
        // skipped. M1 = cap of 1 in-flight request.
        if (Volatile.Read(ref _inFlight) >= MaxConcurrentRequests)
        {
            GD.Print($"[WorldSimTick] tick={_cadence.Tick} dropped (in-flight={_inFlight}, cap={MaxConcurrentRequests})");
            return;
        }

        if (_apiClient is null) return;

        // Fire-and-track : we increment _inFlight, kick the async
        // call, and never await it inline (would block _Process).
        // The continuation cleans up _inFlight regardless of outcome.
        Interlocked.Increment(ref _inFlight);
        _ = ProcessTickAsync(_cadence.Tick);
    }

    /// <summary>
    /// Single-tick async pipeline : build the request, call the
    /// server, marshal the response back to the main thread.
    ///
    /// <para>
    /// Returns <see cref="Task"/> for testability later (e.g. a
    /// debug button that fires one tick on demand can <c>await</c>
    /// it). The fire-and-forget call from <see cref="_Process"/>
    /// ignores the returned task ; that is intentional.
    /// </para>
    /// </summary>
    private async Task ProcessTickAsync(int tick)
    {
        try
        {
            if (_apiClient is null) return;

            var request = BuildTickRequest(tick);
            GD.Print($"[TICK] tick={tick} seed={request.Seed} personas={request.CompanyPersonas.Count}");

            var result = await _apiClient.TickAsync(request, _shutdownCts.Token).ConfigureAwait(false);

            // Marshalling back to the main thread happens INSIDE the
            // dispatch handler — CallDeferred takes a method name +
            // args and runs the method on the main thread on the
            // next engine frame. We can't use CallDeferred(method, X)
            // with a non-Godot type (Result is a custom record), so
            // we route the two real cases through a pair of
            // CallDeferred-friendly handlers.
            switch (result)
            {
                case Result<WorldTickResponseDto, ApiError>.Success ok:
                    if (ok.Value.Mission is { } mission)
                    {
                        // Marshal the mission shape via a typed
                        // payload. CallDeferred accepts Variant args ;
                        // we package the mission into a Godot
                        // Dictionary so the deferred handler can
                        // re-hydrate the C# record without crossing
                        // the marshalling boundary on a non-Godot
                        // type.
                        CallDeferred(MethodName.OnMissionEmergedDeferred,
                            mission.Id,
                            mission.Type,
                            mission.NarrativeHook,
                            mission.Region,
                            mission.Difficulty,
                            mission.Seed,
                            new Godot.Collections.Array<string>(mission.EligiblePersonas));
                    }
                    break;
                case Result<WorldTickResponseDto, ApiError>.Failure fail:
                    CallDeferred(MethodName.OnTickFailedDeferred,
                        fail.Error.GetType().Name,
                        FailureMessageOf(fail.Error));
                    break;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    /// <summary>
    /// Build a <see cref="WorldTickRequestDto"/> from the current
    /// game state. M1 stub : the company snapshot is empty until
    /// the company source lands (the Compagnie panel probe holds
    /// scratch personas ; production
    /// <see cref="GameState"/> will own the canonical roster).
    ///
    /// <para>
    /// The seed derivation pins the cross-tick reproducibility
    /// contract : same <c>(WorldSeed, tick)</c> pair always yields
    /// the same emergent mission. The 32-bit golden-ratio constant
    /// 0x9E3779B1 is a well-known fibonacci-hash multiplier (Knuth)
    /// — successive integer inputs map to well-spread outputs.
    /// </para>
    /// </summary>
    private WorldTickRequestDto BuildTickRequest(int tick)
    {
        var seed = unchecked(WorldSeed * 0x9E3779B1L + tick);
        var personas = ReadCompanyPersonasSnapshot();
        return new WorldTickRequestDto(
            Tick: tick,
            Seed: seed,
            ContextProse: BuildContextProse(),
            CompanyPersonas: personas);
    }

    /// <summary>
    /// Read the typed company snapshot from
    /// <see cref="GameState.CompanyPersonas"/>. M1 returns an empty
    /// list until the real roster is wired in (4c+). The hook is
    /// here so 4c just needs to populate the GameState slot.
    /// </summary>
    private IReadOnlyList<CharacterStateDto> ReadCompanyPersonasSnapshot()
    {
        if (_gameState is null) return System.Array.Empty<CharacterStateDto>();
        // GameState exposes a typed list — defensive copy via .ToArray()
        // so the list passed to the wire is immutable from the caller's
        // perspective (no concurrent mutation between Build and Send).
        return _gameState.CompanyPersonas.ToArray();
    }

    /// <summary>
    /// Build the EN-rendered world context prose. M1 stub : a fixed
    /// non-empty string ; M2 plugs the real <c>render_context()</c>
    /// equivalent in.
    /// </summary>
    private static string BuildContextProse() => M1ContextProseStub;

    /// <summary>
    /// Main-thread handler for an emerged mission. Mutates
    /// <see cref="GameState.PendingMissions"/> and emits the
    /// <see cref="MissionEmerged"/> signal. Called via
    /// <see cref="GodotObject.CallDeferred"/> from the async
    /// continuation — the engine guarantees this runs on the main
    /// thread on the next process frame.
    /// </summary>
    private void OnMissionEmergedDeferred(
        string id,
        string type,
        string narrativeHook,
        string region,
        string difficulty,
        long seed,
        Godot.Collections.Array<string> eligiblePersonas)
    {
        var mission = new EmergentMissionDto(
            Id: id,
            Type: type,
            NarrativeHook: narrativeHook,
            EligiblePersonas: eligiblePersonas.ToArray(),
            Difficulty: difficulty,
            Region: region,
            DeadlineTicks: null,
            Outcome: null,
            Seed: seed);

        if (_gameState is not null)
            _gameState.PendingMissions.Add(mission);

        GD.Print($"[MISSION SPAWN] tick={_cadence.Tick} id={mission.Id} type={mission.Type} region={mission.Region} eligible=[{string.Join(",", mission.EligiblePersonas)}]");
        EmitSignal(SignalName.MissionEmerged, mission.Id);
    }

    /// <summary>
    /// Main-thread handler for a tick failure. M1 strategy : log
    /// once and move on — the next tick will retry implicitly.
    /// </summary>
    private void OnTickFailedDeferred(string errorVariant, string message)
    {
        GD.PushWarning($"[WorldSimTick] tick failed : {errorVariant} {message}");
    }

    private static string FailureMessageOf(ApiError err) => err switch
    {
        ApiError.NotReachable nr => nr.Reason,
        ApiError.ServerError se => $"{se.StatusCode} {se.Body}",
        ApiError.DeserializationError de => de.Reason,
        ApiError.Cancelled => "cancelled",
        _ => "(unknown)"
    };
}
