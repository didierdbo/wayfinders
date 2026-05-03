using Godot;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scenes;

/// <summary>
/// Phase 3 verification surface for L1, L2, and L3. Two buttons:
/// <list type="bullet">
///   <item>Check API health — hits <c>/api/health</c>, flips a label.</item>
///   <item>Fetch units — hits <c>/api/units</c>, prints typed DTOs to the
///   Output panel, summarises count on screen.</item>
/// </list>
///
/// <para>
/// <b>TEMP-VERIFY:</b> this scene exists to prove the wire end-to-end. It is
/// not the real Main scene of Phase 3 — that ships in L5 and shows the unit
/// roster from <c>GET /api/units</c>. Delete this scene, this script, and the
/// <c>run/main_scene</c> entry in <c>project.godot</c> at end-of-Phase-3.
/// </para>
///
/// <para>
/// <b>L3 upgrade:</b> handlers now <c>switch</c> over the typed
/// <see cref="Result{T, E}"/> returned by the autoload and surface each
/// <see cref="ApiError"/> variant as a distinct label string. The point of
/// the exercise is that "fetched zero units" and "could not reach the
/// server" no longer collapse to the same UI message — the type system
/// makes the distinction load-bearing.
/// </para>
///
/// <para>
/// Node lookup style: <see cref="GetNode{T}(NodePath)"/> with literal paths
/// rather than <c>[Export]</c> field wiring. Banked from L1 — same reasoning.
/// </para>
/// </summary>
public partial class HealthCheck : Control
{
    private Button _checkButton = null!;
    private Label _statusLabel = null!;
    private Button _fetchUnitsButton = null!;
    private Label _unitsLabel = null!;

    public override void _Ready()
    {
        _checkButton = GetNode<Button>("VBox/CheckButton");
        _statusLabel = GetNode<Label>("VBox/StatusLabel");
        _fetchUnitsButton = GetNode<Button>("VBox/FetchUnitsButton");
        _unitsLabel = GetNode<Label>("VBox/UnitsLabel");

        _statusLabel.Text = "Status: not checked yet.";
        _unitsLabel.Text = "Units: not fetched yet.";

        _checkButton.Pressed += OnCheckPressed;
        _fetchUnitsButton.Pressed += OnFetchUnitsPressed;
    }

    public override void _ExitTree()
    {
        // Disconnection discipline — banked from Phase 2. Even though both
        // publisher and subscriber go away together here, the habit matters.
        _checkButton.Pressed -= OnCheckPressed;
        _fetchUnitsButton.Pressed -= OnFetchUnitsPressed;
    }

    private async void OnCheckPressed()
    {
        _checkButton.Disabled = true;
        _statusLabel.Text = "Status: checking...";

        // Reach the autoload via the scene-tree singleton path. GetNode is
        // the engine-blessed way; the autoload registers a global named child
        // under /root/ApiClient.
        var api = GetNode<ApiClient>("/root/ApiClient");

        var result = await api.CheckHealthAsync();

        // NOTE: After an await, the continuation may run off the main thread.
        // For a Label.Text assignment in this throwaway scene we get away with
        // direct assignment (Godot does not crash on it in practice for simple
        // property writes), but the real CallDeferred discipline lands in L4
        // when typed DTOs and node mutation start mixing.
        //
        // The switch expression below is exhaustive over the sealed Result
        // hierarchy AND the sealed ApiError hierarchy — if a variant is
        // added, the compiler raises a warning (and TreatWarningsAsErrors
        // makes that a build break). The compiler is the test.
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
                "Status: cancelled.",
            _ => "Status: unknown result shape.",
        };
        _checkButton.Disabled = false;
    }

    // TEMP-VERIFY: remove in Phase 3 close. L2/L3 verification handler.
    // L2 proved /api/units deserializes into typed UnitDto records via the
    // source-gen context. L3 upgrades the call site to branch on the typed
    // Result<T, ApiError> instead of conflating empty-success with failure.
    // L5 replaces this with the real roster scene that spawns Unit nodes.
    private async void OnFetchUnitsPressed()
    {
        _fetchUnitsButton.Disabled = true;
        _unitsLabel.Text = "Units: fetching...";

        var api = GetNode<ApiClient>("/root/ApiClient");

        var result = await api.GetUnitsAsync();

        // The whole point of L3: this switch CANNOT collapse "empty list"
        // and "fetch failed" into the same arm. The Success arm requires
        // a list (which may be empty); each Failure arm carries its own
        // ApiError variant and gets its own user-visible message.
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
                "Units: cancelled.",
            _ => "Units: unknown result shape.",
        };
        _fetchUnitsButton.Disabled = false;
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
