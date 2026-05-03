using System.Threading;
using Godot;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scenes;

/// <summary>
/// Phase 3 verification surface for L1, L2, L3, and L4. Three buttons:
/// <list type="bullet">
///   <item>Check API health — hits <c>/api/health</c>, flips a label.</item>
///   <item>Fetch units — hits <c>/api/units</c>, prints typed DTOs to the
///   Output panel, summarises count on screen.</item>
///   <item>Cancel pending — fires the per-request <see cref="CancellationTokenSource"/>
///   for the in-flight fetch so the typed <see cref="ApiError.Cancelled"/>
///   path is visible in the UI (L4 verification).</item>
/// </list>
///
/// <para>
/// <b>TEMP-VERIFY:</b> this scene exists to prove the wire end-to-end. It is
/// not the real Main scene of Phase 3 — that ships in L5 and shows the unit
/// roster from <c>GET /api/units</c>. Delete this scene, this script, the
/// Cancel button, and the <c>run/main_scene</c> entry in <c>project.godot</c>
/// at end-of-Phase-3.
/// </para>
///
/// <para>
/// <b>L4 lesson — main-thread marshalling discipline.</b> Godot's scene tree
/// is single-threaded. Every <see cref="Node"/> mutation
/// (<c>Label.Text</c>, <c>Button.Disabled</c>, anything that touches a
/// node's properties or its children) MUST happen on the main thread.
/// </para>
/// <para>
/// In .NET, the continuation of an <c>await</c> resumes on whatever the
/// current <see cref="System.Threading.SynchronizationContext"/> is — and
/// Godot does not install a main-thread context. With
/// <c>ConfigureAwait(false)</c> in <c>ApiClient</c>, the continuation lands
/// on a thread-pool thread. <i>Sometimes</i> the thread happens to be safe
/// for a simple property write; <i>sometimes</i> it isn't. That is the
/// worst kind of bug — works on the dev machine, crashes in a Steam build
/// under heavy GC pressure.
/// </para>
/// <para>
/// The fix is one line: after the HTTP await returns, hop back to the main
/// thread by awaiting the next <c>process_frame</c> signal:
/// <code>await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);</code>
/// The continuation after that line is guaranteed to run on the main
/// thread. From there, mutating scene state is safe.
/// </para>
/// <para>
/// Alternative idiom: wrap each individual mutation in
/// <c>Callable.From(() => label.Text = "...").CallDeferred()</c>. That works
/// but is per-statement noise. The <c>ToSignal(ProcessFrame)</c> approach
/// is one call that re-establishes the main-thread invariant for the rest
/// of the method — easier to read, easier to reason about, easier to teach.
/// </para>
///
/// <para>
/// <b>L4 lesson — cancellation lifecycle.</b> A per-request
/// <see cref="CancellationTokenSource"/> is created on each fetch and stored
/// in <see cref="_pendingCts"/>. The Cancel button fires it. The
/// <c>ApiClient</c> links it with its own shutdown token, so cancellation
/// from the user OR app shutdown both end up as
/// <see cref="ApiError.Cancelled"/>. The CTS is disposed in
/// <see cref="_ExitTree"/> to keep the scene clean if it leaves the tree
/// while a request is in flight.
/// </para>
/// </summary>
public partial class HealthCheck : Control
{
    private Button _checkButton = null!;
    private Label _statusLabel = null!;
    private Button _fetchUnitsButton = null!;
    private Label _unitsLabel = null!;
    private Button _cancelButton = null!;

    /// <summary>
    /// Owns cancellation for whichever async fetch is currently in flight.
    /// Replaced on every press of Check/Fetch; the previous CTS is disposed
    /// to release its handle. Null when nothing is pending.
    ///
    /// <para>
    /// Why a field, not a local: the Cancel button handler runs in a different
    /// invocation than the fetch handler. They communicate through this
    /// shared state — exactly the kind of mutable client-side state the
    /// "stateless-everywhere reflex" callout warned about.
    /// </para>
    /// </summary>
    private CancellationTokenSource? _pendingCts;

    public override void _Ready()
    {
        _checkButton = GetNode<Button>("VBox/CheckButton");
        _statusLabel = GetNode<Label>("VBox/StatusLabel");
        _fetchUnitsButton = GetNode<Button>("VBox/FetchUnitsButton");
        _unitsLabel = GetNode<Label>("VBox/UnitsLabel");
        _cancelButton = GetNode<Button>("VBox/CancelButton");

        _statusLabel.Text = "Status: not checked yet.";
        _unitsLabel.Text = "Units: not fetched yet.";
        _cancelButton.Disabled = true;

        _checkButton.Pressed += OnCheckPressed;
        _fetchUnitsButton.Pressed += OnFetchUnitsPressed;
        _cancelButton.Pressed += OnCancelPressed;
    }

    public override void _ExitTree()
    {
        // Disconnection discipline — banked from Phase 2.
        _checkButton.Pressed -= OnCheckPressed;
        _fetchUnitsButton.Pressed -= OnFetchUnitsPressed;
        _cancelButton.Pressed -= OnCancelPressed;

        // Cancel and release any in-flight request before we leave the tree.
        // The autoload's shutdown token will also fire on app exit, but if
        // *just this scene* unloads (scene change, future router) we still
        // want the local fetch to wind down cleanly.
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
        _pendingCts = null;
    }

    private async void OnCheckPressed()
    {
        // Replace any prior CTS — only one fetch should be pending at a time
        // for the verification scene. (Real scenes might queue or coalesce,
        // but that is a feature, not a Phase-3 lesson.)
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
        _pendingCts = new CancellationTokenSource();

        _checkButton.Disabled = true;
        _fetchUnitsButton.Disabled = true;
        _cancelButton.Disabled = false;
        _statusLabel.Text = "Status: checking...";

        // Reach the autoload via the scene-tree singleton path. GetNode is
        // the engine-blessed way; the autoload registers a global named child
        // under /root/ApiClient.
        var api = GetNode<ApiClient>("/root/ApiClient");

        var result = await api.CheckHealthAsync(_pendingCts.Token);

        // === L4 main-thread hop ===
        // The autoload uses ConfigureAwait(false), so the line above resumed
        // on a thread-pool thread. Hop back to main before touching scene
        // state. The line below blocks (asynchronously) until Godot's next
        // process tick, which by definition runs on the main thread.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // === everything below this line is main-thread-safe again ===

        // The switch expression is exhaustive over the sealed Result hierarchy
        // AND the sealed ApiError hierarchy — TreatWarningsAsErrors makes a
        // missed variant a build break. The compiler is the test.
        _statusLabel.Text = result switch
        {
            Result<bool, ApiError>.Success { Value: true } => "Status: OK — API responded {\"status\":\"ok\"}.",
            Result<bool, ApiError>.Success => "Status: FAILED — unexpected health body.",
            Result<bool, ApiError>.Failure { Error: ApiError.NotReachable na } =>
                $"Status: server unreachable ({na.Reason}).",
            Result<bool, ApiError>.Failure { Error: ApiError.ServerError se } =>
                $"Status: server returned {se.StatusCode}.",
            Result<bool, ApiError>.Failure { Error: ApiError.DeserializationError de } =>
                $"Status: could not parse response ({de.Reason}).",
            Result<bool, ApiError>.Failure { Error: ApiError.Cancelled } =>
                "Status: cancelled by user or shutdown.",
            _ => "Status: unknown result shape.",
        };

        _checkButton.Disabled = false;
        _fetchUnitsButton.Disabled = false;
        _cancelButton.Disabled = true;
    }

    // TEMP-VERIFY: remove in Phase 3 close. L2/L3/L4 verification handler.
    private async void OnFetchUnitsPressed()
    {
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
        _pendingCts = new CancellationTokenSource();

        _checkButton.Disabled = true;
        _fetchUnitsButton.Disabled = true;
        _cancelButton.Disabled = false;
        _unitsLabel.Text = "Units: fetching... (2 s artificial delay — click Cancel to interrupt)";

        var api = GetNode<ApiClient>("/root/ApiClient");

        var result = await api.GetUnitsAsync(_pendingCts.Token);

        // === L4 main-thread hop ===
        // Same discipline as OnCheckPressed: re-establish the main thread
        // before touching scene state.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // === main-thread-safe below this line ===

        // The whole point of L3: this switch CANNOT collapse "empty list"
        // and "fetch failed" into the same arm. Cancellation gets its own
        // visible message — that is L4's verification.
        _unitsLabel.Text = result switch
        {
            Result<System.Collections.Generic.IReadOnlyList<UnitDto>, ApiError>.Success s =>
                HandleUnitsSuccess(s.Value),
            Result<System.Collections.Generic.IReadOnlyList<UnitDto>, ApiError>.Failure { Error: ApiError.NotReachable na } =>
                $"Units: server unreachable ({na.Reason}).",
            Result<System.Collections.Generic.IReadOnlyList<UnitDto>, ApiError>.Failure { Error: ApiError.ServerError se } =>
                $"Units: server returned {se.StatusCode}.",
            Result<System.Collections.Generic.IReadOnlyList<UnitDto>, ApiError>.Failure { Error: ApiError.DeserializationError de } =>
                $"Units: could not parse response ({de.Reason}).",
            Result<System.Collections.Generic.IReadOnlyList<UnitDto>, ApiError>.Failure { Error: ApiError.Cancelled } =>
                "Units: cancelled by user or shutdown.",
            _ => "Units: unknown result shape.",
        };

        _checkButton.Disabled = false;
        _fetchUnitsButton.Disabled = false;
        _cancelButton.Disabled = true;
    }

    /// <summary>
    /// L4 verification handler. Fires the per-request CTS — the awaiting
    /// fetch handler observes <see cref="ApiError.Cancelled"/> and updates
    /// its label to the cancelled-variant message.
    ///
    /// <para>
    /// Note that this handler does NOT itself await anything; it just
    /// signals. The cancellation propagates through the linked token in
    /// <see cref="ApiClient"/>, the <c>HttpClient</c> abandons its request,
    /// the <c>OperationCanceledException</c> catch arm fires, the typed
    /// result is returned, the awaiting fetch handler resumes — all on
    /// the main thread after its own <c>ProcessFrame</c> hop.
    /// </para>
    /// </summary>
    private void OnCancelPressed()
    {
        // No-op if there is nothing in flight. Defensive: the button is
        // disabled in that state, but a stray click during the resume window
        // is possible.
        if (_pendingCts is null)
        {
            return;
        }

        // Cancel the CTS but do NOT dispose it here — the awaiting handler
        // is still using its token. Disposal happens on the next press of
        // Check/Fetch (which replaces the CTS) or in _ExitTree.
        _pendingCts.Cancel();

        // The label flip will happen in the fetch handler after the result
        // arrives. We just disable the button here so the user knows the
        // cancellation has been initiated.
        _cancelButton.Disabled = true;
    }

    /// <summary>
    /// Success-path handler for unit fetch. Note that "empty list" is now a
    /// legitimate, distinct outcome from "fetch failed" — that distinction
    /// is exactly what L3 made the type system carry.
    /// </summary>
    private static string HandleUnitsSuccess(System.Collections.Generic.IReadOnlyList<UnitDto> units)
    {
        // TEMP-VERIFY: these prints are the L2/L3 acceptance signal — they
        // prove the snake_case wire fields landed correctly in the
        // PascalCase record properties, AND that the success path actually
        // returns the deserialized list. L5 deletes them.
        GD.Print($"[HealthCheck] /api/units returned {units.Count} unit(s):");
        foreach (var dto in units)
        {
            GD.Print($"[HealthCheck] Unit: {dto.DisplayName} HP={dto.MaxHp} Speed={dto.BaseSpeed} Portrait={dto.Portrait}");
        }

        return units.Count == 0
            ? "Units: OK — server returned an empty roster."
            : $"Units: OK — fetched {units.Count} (see Output panel).";
    }
}
