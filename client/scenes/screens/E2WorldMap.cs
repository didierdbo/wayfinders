using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E2 World Map — region-level overview. J1 stub.
///
/// <para>
/// J3 will introduce the hotspot system (Area2D vs TextureButton call
/// re-evaluated then), grey out non-Halfgate POI cities with diegetic
/// pops, wire the panneaux rétractables P6, and resolve the
/// <c>e2.world_map</c> background.
/// </para>
///
/// <para>
/// <b>P3 fog hook (post-MVP slot):</b> this screen is the primary
/// consumer of <see cref="KnowledgeMatrix"/> when fog ships. The hotspot
/// renderer in J3+ will read fog level per location and render
/// accordingly. Slot reserved as a comment ; no
/// <see cref="KnowledgeMatrix"/> calls in J1.
/// </para>
/// </summary>
public partial class E2WorldMap : Control, IScreen
{
    public string ScreenId => "E2_WORLD";

    private Button _nextButton = null!;
    private Button _backButton = null!;
    private Label _statusLabel = null!;
    private TextureRect _background = null!;

    public override void _Ready()
    {
        _background = GetNode<TextureRect>("Background");
        _nextButton = GetNode<Button>("VBox/NextButton");
        _backButton = GetNode<Button>("VBox/BackButton");
        _statusLabel = GetNode<Label>("VBox/StatusLabel");

        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _background.Texture = assetResolver.Resolve("e2.world_map");

        _nextButton.Pressed += OnNextPressed;
        _backButton.Pressed += OnBackPressed;

        // P3 hook: in J3+, hotspot rendering reads from
        // KnowledgeMatrix.GetFogLevel(cityId). Not wired in J1.
        // P6 hook: panneaux rétractables instantiated from a
        // RetractablePanel scene. Not wired in J1.
    }

    public override void _ExitTree()
    {
        _nextButton.Pressed -= OnNextPressed;
        _backButton.Pressed -= OnBackPressed;
    }

    private async void OnNextPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.NavigateTo("E3_CITY_HALFGATE");
    }

    private async void OnBackPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.NavigateBack();
    }

    public Task OnEnter(ScreenContext context, CancellationToken ct)
    {
        var caller = context.CallerScreenId ?? "(boot)";
        _statusLabel.Text = $"E2 WORLD — opening scenario stub (from {caller})";
        return Task.CompletedTask;
    }

    public Task OnExit(CancellationToken ct) => Task.CompletedTask;

    public Task OnSuspend(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Disabled;
        return Task.CompletedTask;
    }

    public Task OnResume(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Inherit;
        return Task.CompletedTask;
    }
}
