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
/// </summary>
public partial class HealthCheck : Control
{
    [Export] private Button _checkButton = null!;
    [Export] private Label _statusLabel = null!;

    public override void _Ready()
    {
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
