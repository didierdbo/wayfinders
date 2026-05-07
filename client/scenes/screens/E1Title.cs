using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E1 Title — game entry point. J1 stub: a label, a "Next" button, and the
/// optional Continuer button greyed when GameState.HasSave is false.
///
/// <para>
/// J2 will replace the placeholder rectangle with the bureau-arpenteur
/// background (key <c>e1.bureau</c>), wire the 4 real menu options
/// (Nouveau dossier, Reprendre, Options, Quitter), and compose the footer
/// in the Cadastre voice. J1 is plumbing only.
/// </para>
///
/// <para>
/// <b>P4 onboarding hook (post-MVP slot):</b> when first-run-ever logic
/// lands (J3+), this is the screen that fires the welcome pop. Slot
/// reserved here as a comment ; no <c>OnboardingPops.RequestShow</c> call
/// in J1 to keep the autoload truly noop.
/// </para>
/// </summary>
public partial class E1Title : Control, IScreen
{
    public string ScreenId => "E1_TITLE";

    private Button _newGameButton = null!;
    private Button _continueButton = null!;
    private Label _statusLabel = null!;
    private TextureRect _background = null!;

    public override void _Ready()
    {
        _background = GetNode<TextureRect>("Background");
        _newGameButton = GetNode<Button>("VBox/NewGameButton");
        _continueButton = GetNode<Button>("VBox/ContinueButton");
        _statusLabel = GetNode<Label>("VBox/StatusLabel");

        // Resolve background asset (J1: will fall back to placeholder
        // colored rect since no e1.bureau PNG ships yet).
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _background.Texture = assetResolver.Resolve("e1.bureau");

        // Continuer state from GameState (J1: always disabled, no save).
        var gameState = GetNode<GameState>("/root/GameState");
        _continueButton.Disabled = !gameState.HasSave;

        _newGameButton.Pressed += OnNewGamePressed;
        _continueButton.Pressed += OnContinuePressed;

        // P4 hook: in J3+ this is where OnboardingPops.RequestShow("welcome")
        // would land. Intentionally not wired in J1.
    }

    public override void _ExitTree()
    {
        // Disconnection discipline (Pre-brief §4.8 piège #2).
        _newGameButton.Pressed -= OnNewGamePressed;
        _continueButton.Pressed -= OnContinuePressed;
    }

    private async void OnNewGamePressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.NavigateTo("E2_WORLD");
    }

    private void OnContinuePressed()
    {
        // J1: greyed when no save, but defensive in case GameState changes.
        var gameState = GetNode<GameState>("/root/GameState");
        if (!gameState.HasSave)
        {
            GD.Print("[E1Title] Continue pressed with no save — ignored");
        }
    }

    public Task OnEnter(ScreenContext context, CancellationToken ct)
    {
        _statusLabel.Text = "E1 TITLE — opening scenario stub";
        return Task.CompletedTask;
    }

    public Task OnExit(CancellationToken ct) => Task.CompletedTask;

    public Task OnSuspend(CancellationToken ct)
    {
        // Disable input when a modal opens above us. J1 has no modal-from-E1
        // path, so this is dead code in J1 but keeps the contract honest
        // for J5 (E4 modal could conceivably be opened from E1 in future).
        ProcessMode = ProcessModeEnum.Disabled;
        return Task.CompletedTask;
    }

    public Task OnResume(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Inherit;
        return Task.CompletedTask;
    }
}
