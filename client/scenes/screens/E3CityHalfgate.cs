using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E3 City Halfgate — single MVP city, intra-city overview. J1 stub.
///
/// <para>
/// J4 will wire the hotspot system for districts + key buildings + PNJs,
/// reuse <see cref="ApiClient"/> to fetch the roster (Phase Arc 1
/// shipping seam re-deployed in a UI context), and ship the panneau bas
/// Journal + tableau contrats double colonne.
/// </para>
///
/// <para>
/// <b>Three demo buttons in J1:</b> Next (E5 district zoom), OpenE4Modal
/// (E4 fiche overlay over this screen), Back. The OpenE4Modal button is
/// the only way in J1 to exercise the modal Suspend/Resume path.
/// </para>
/// </summary>
public partial class E3CityHalfgate : Control, IScreen
{
    public string ScreenId => "E3_CITY_HALFGATE";

    private Button _toE5Button = null!;
    private Button _openModalButton = null!;
    private Button _backButton = null!;
    private Label _statusLabel = null!;
    private TextureRect _background = null!;

    public override void _Ready()
    {
        _background = GetNode<TextureRect>("Background");
        _toE5Button = GetNode<Button>("VBox/ToE5Button");
        _openModalButton = GetNode<Button>("VBox/OpenModalButton");
        _backButton = GetNode<Button>("VBox/BackButton");
        _statusLabel = GetNode<Label>("VBox/StatusLabel");

        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _background.Texture = assetResolver.Resolve("e3.halfgate.base");

        _toE5Button.Pressed += OnToE5Pressed;
        _openModalButton.Pressed += OnOpenModalPressed;
        _backButton.Pressed += OnBackPressed;
    }

    public override void _ExitTree()
    {
        _toE5Button.Pressed -= OnToE5Pressed;
        _openModalButton.Pressed -= OnOpenModalPressed;
        _backButton.Pressed -= OnBackPressed;
    }

    private async void OnToE5Pressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.NavigateTo("E5_DISTRICT");
    }

    private async void OnOpenModalPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.OpenModal("E4_CHARACTER_SHEET");
    }

    private async void OnBackPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.NavigateBack();
    }

    public Task OnEnter(ScreenContext context, CancellationToken ct)
    {
        var caller = context.CallerScreenId ?? "(boot)";
        _statusLabel.Text = $"E3 HALFGATE — opening scenario stub (from {caller})";
        return Task.CompletedTask;
    }

    public Task OnExit(CancellationToken ct) => Task.CompletedTask;

    public Task OnSuspend(CancellationToken ct)
    {
        // Disable input + visual processing while E4 modal is open.
        ProcessMode = ProcessModeEnum.Disabled;
        Modulate = new Color(0.6f, 0.6f, 0.6f, 1.0f);  // dim under modal
        return Task.CompletedTask;
    }

    public Task OnResume(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Inherit;
        Modulate = Colors.White;
        return Task.CompletedTask;
    }
}
