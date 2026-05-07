using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E4 Character Sheet — modal overlay rendered above E3/E5. J1 stub.
///
/// <para>
/// J5 will ship the 4 onglets (Identité, Compétences, Dossier,
/// Caviardages), the iso fiche frame (key <c>e4.frame_fiche</c>), the
/// 3-state Kira portrait (calm/alert/wounded), the visual caviardages
/// (P9 hook surfaced as ColorRect black + Label), and the close-on-X /
/// close-on-click-outside behaviors.
/// </para>
///
/// <para>
/// <b>J1: just a Close button.</b> No tabs, no portrait, no content.
/// Exists to exercise the modal Suspend/Resume contract end-to-end.
/// </para>
///
/// <para>
/// <b>P9 caviardage hook (post-MVP slot):</b> J5 fiche will compose 2
/// static black <c>ColorRect</c> + <c>Label</c> "[Caviardage classe X —
/// accès refusé.]". Slot reserved as a comment ; no
/// <c>RedactionBlock</c> component in J1.
/// </para>
/// </summary>
public partial class E4CharacterSheet : Control, IModalOverlay
{
    public string ModalId => "E4_CHARACTER_SHEET";
    public bool CanCloseOnEsc => true;
    public bool CanCloseOnClickOutside => true;

    private Button _closeButton = null!;
    private Label _statusLabel = null!;

    public override void _Ready()
    {
        _closeButton = GetNode<Button>("Panel/VBox/CloseButton");
        _statusLabel = GetNode<Label>("Panel/VBox/StatusLabel");

        _closeButton.Pressed += OnClosePressed;
    }

    public override void _ExitTree()
    {
        _closeButton.Pressed -= OnClosePressed;
    }

    private async void OnClosePressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.CloseModal();
    }

    public Task OnOpen(ScreenContext context, CancellationToken ct)
    {
        var caller = context.CallerScreenId ?? "(unknown)";
        _statusLabel.Text = $"E4 FICHE (modal) — over {caller}";
        return Task.CompletedTask;
    }

    public Task OnClose(CancellationToken ct) => Task.CompletedTask;
}
