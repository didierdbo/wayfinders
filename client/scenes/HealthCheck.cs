using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes;

/// <summary>
/// Phase 3 verification surface for L1 and L2. Two buttons:
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

        bool ok;
        try
        {
            ok = await api.CheckHealthAsync();
        }
        catch (System.OperationCanceledException)
        {
            // Scene exited mid-flight. Don't touch the tree.
            return;
        }

        // NOTE: After an await, the continuation may run off the main thread.
        // For a Label.Text assignment in this throwaway scene we get away with
        // direct assignment (Godot does not crash on it in practice for simple
        // property writes), but the real CallDeferred discipline lands in L4
        // when typed DTOs and node mutation start mixing.
        _statusLabel.Text = ok
            ? "Status: OK — API responded {\"status\":\"ok\"}."
            : "Status: FAILED — see Output panel for details.";
        _checkButton.Disabled = false;
    }

    // TEMP-VERIFY: remove in Phase 3 close. L2 verification handler — proves
    // /api/units deserializes into typed UnitDto records via the source-gen
    // context. L5 replaces this with the real roster scene that spawns Unit
    // nodes from server data.
    private async void OnFetchUnitsPressed()
    {
        _fetchUnitsButton.Disabled = true;
        _unitsLabel.Text = "Units: fetching...";

        var api = GetNode<ApiClient>("/root/ApiClient");

        System.Collections.Generic.IReadOnlyList<Services.Dtos.UnitDto> units;
        try
        {
            units = await api.GetUnitsAsync();
        }
        catch (System.OperationCanceledException)
        {
            return;
        }

        // TEMP-VERIFY: these prints are the L2 acceptance signal — they prove
        // the snake_case wire fields landed correctly in the PascalCase
        // record properties. L5 deletes them.
        GD.Print($"[HealthCheck] /api/units returned {units.Count} unit(s):");
        foreach (var dto in units)
        {
            GD.Print($"[HealthCheck] Unit: {dto.DisplayName} HP={dto.MaxHp} Speed={dto.BaseSpeed} Portrait={dto.Portrait}");
        }

        _unitsLabel.Text = units.Count > 0
            ? $"Units: OK — fetched {units.Count} (see Output panel)."
            : "Units: FAILED or empty — see Output panel.";
        _fetchUnitsButton.Disabled = false;
    }
}
