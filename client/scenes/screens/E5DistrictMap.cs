using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E5 District Map — intra-city zoom. J1 stub.
///
/// <para>
/// At Jalon 5, evaluate factoring with E3CityHalfgate. Concrete-first
/// (pre-brief §4.5) — do NOT pre-abstract a LocationViewScreen base
/// class now. If E5 duplicates E3 at &gt;70% by J5, extract a base then.
/// In J1 they are deliberately two distinct files with no shared code.
/// </para>
///
/// <para>
/// <b>Three demo buttons in J1:</b> OpenE4Modal (proves modal works
/// from E5 too — closing returns to E5, not E3, validating
/// _modalCallerScreenId tracking), Back, and a label.
/// </para>
/// </summary>
public partial class E5DistrictMap : Control, IScreen
{
    public string ScreenId => "E5_DISTRICT";

    private Button _openModalButton = null!;
    private Button _backButton = null!;
    private Label _statusLabel = null!;
    private TextureRect _background = null!;

    public override void _Ready()
    {
        _background = GetNode<TextureRect>("Background");
        _openModalButton = GetNode<Button>("VBox/OpenModalButton");
        _backButton = GetNode<Button>("VBox/BackButton");
        _statusLabel = GetNode<Label>("VBox/StatusLabel");

        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _background.Texture = assetResolver.Resolve("e5.halfgate.district_lower");

        _openModalButton.Pressed += OnOpenModalPressed;
        _backButton.Pressed += OnBackPressed;
    }

    public override void _ExitTree()
    {
        _openModalButton.Pressed -= OnOpenModalPressed;
        _backButton.Pressed -= OnBackPressed;
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
        _statusLabel.Text = $"E5 DISTRICT — opening scenario stub (from {caller})";
        return Task.CompletedTask;
    }

    public Task OnExit(CancellationToken ct) => Task.CompletedTask;

    public Task OnSuspend(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Disabled;
        Modulate = new Color(0.6f, 0.6f, 0.6f, 1.0f);
        return Task.CompletedTask;
    }

    public Task OnResume(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Inherit;
        Modulate = Colors.White;
        return Task.CompletedTask;
    }
}
