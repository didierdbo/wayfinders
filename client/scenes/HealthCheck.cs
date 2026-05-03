using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes;

/// <summary>
/// L1 verification surface. Single button, single label. Press the button,
/// the label tells you whether the FastAPI service is reachable.
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
/// rather than <c>[Export]</c> field wiring. The earlier <c>[Export] Label</c>
/// shape required the Godot Inspector to populate the field; a hand-edited
/// <c>.tscn</c> assigning a <c>NodePath(...)</c> string to a <c>Label</c>-typed
/// field does not resolve, leaving the field null and crashing
/// <c>_Ready</c>. For a four-node throwaway scene, direct <c>GetNode</c> is
/// the smaller, more robust shape.
/// </para>
/// </summary>
public partial class HealthCheck : Control
{
    private Button _checkButton = null!;
    private Label _statusLabel = null!;

    public override void _Ready()
    {
        _checkButton = GetNode<Button>("VBox/CheckButton");
        _statusLabel = GetNode<Label>("VBox/StatusLabel");

        _statusLabel.Text = "Status: not checked yet.";
        _checkButton.Pressed += OnCheckPressed;
    }

    public override void _ExitTree()
    {
        // Disconnection discipline — banked from Phase 2. Even though both
        // publisher and subscriber go away together here, the habit matters.
        _checkButton.Pressed -= OnCheckPressed;
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
}
