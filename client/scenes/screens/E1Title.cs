using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E1 Title -- game entry point. J2 elevates J1's debug stub to a real
/// composed Title screen: bureau-arpenteur background (key
/// <c>e1.bureau</c>), tampon WAYFINDERS overlay (key <c>e1.logo</c>),
/// 4-button stack on a cardboard frame (key <c>e1.buttons_frame</c>),
/// footer + build line in the Cadastre voice. Gameplay still empty -- E1
/// is an entry point, not a feature.
///
/// <para>
/// <b>Wire-up.</b> The 4 buttons route as follows:
/// <list type="bullet">
///   <item><c>[01] Ouvrir un nouveau registre</c> --
///         <see cref="GetTree"/>.<c>ChangeSceneToFile</c> to
///         <c>IsoMapE1Probe</c> (the e1 boot/flip/POI cinematic). See the
///         "New Game path" paragraph below.</item>
///   <item><c>[02] Reprendre un registre archivé</c> -- disabled when
///         <see cref="GameState.HasSave"/> is false (J2 default), label
///         swaps to <c>OpeningStrings.E1ContinueDisabled</c>.</item>
///   <item><c>[03] Réglages techniques</c> -- opens
///         <c>OPTIONS_MODAL</c> via <see cref="SceneManager.OpenModal"/>.</item>
///   <item><c>[04] Refermer le Cadastre</c> -- opens
///         <c>QUIT_CONFIRM_MODAL</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>New Game path (2026-05-13, Rune).</b> "Nouvelle Partie" now drives
/// the player through the e1 cinematic (boot grid -&gt; 3D flip -&gt; POI
/// Halfgate fade-in -&gt; hover/click) implemented in
/// <c>scenes/scratch/IsoMapE1Probe/IsoMapE1Probe.tscn</c>. The cinematic
/// itself ends with <c>ChangeSceneToFile</c> to E2Stub on POI click. Both
/// hops use <c>ChangeSceneToFile</c> rather than
/// <see cref="SceneManager.NavigateTo"/> because the cinematic is a one-way
/// scripted sequence, not a stack-managed screen : it does not implement
/// <see cref="IScreen"/>, it has no Back/Suspend/Resume semantics, and
/// putting it on the navigation stack would let the wheel ladder or Esc
/// pop the player back into a half-played cinematic. The destination after
/// E2Stub (the real E2 world map) will rejoin the SceneManager flow once
/// the cinematic is replaced by production assets.
/// </para>
///
/// <para>
/// <b>Debug F12 toggle (Pre-brief J2 D4 = raccourci F12).</b>
/// In <see cref="OS.IsDebugBuild"/> only, F12 flips
/// <see cref="GameState.HasSave"/> so the dev can validate the Continuer
/// dégrisé path without implementing a real save system. No-op in
/// release builds.
/// </para>
///
/// <para>
/// <b>Strings come from a Godot <see cref="Resource"/></b>
/// (<c>res://data/opening_strings.tres</c>) so designers can hot-edit
/// copy without recompile, and accents are stored in UTF-8 natively
/// (Pre-brief J2 D5 = Resource .tres).
/// </para>
/// </summary>
public partial class E1Title : Control, IScreen
{
    public string ScreenId => "E1_TITLE";

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";

    // 2026-05-13 (Rune) -- New Game button drives the e1 cinematic scene
    // directly via ChangeSceneToFile. See class docstring "New Game path"
    // for the rationale (cinematic is not a stack-managed screen).
    private const string IsoMapE1ProbeScenePath =
        "res://scenes/scratch/IsoMapE1Probe/IsoMapE1Probe.tscn";

    // Asset keys -- mirror data/asset_keys.json.
    private const string BureauAssetKey = "e1.bureau";
    private const string LogoAssetKey = "e1.logo";
    private const string ButtonsFrameAssetKey = "e1.buttons_frame";

    // Modal ids -- mirror OpeningBootstrap registry.
    private const string OptionsModalId = "OPTIONS_MODAL";
    private const string QuitConfirmModalId = "QUIT_CONFIRM_MODAL";

    private TextureRect _bureauSprite = null!;
    private TextureRect _logoTampon = null!;
    private TextureRect _buttonsFrame = null!;

    private Label _titleLabel = null!;
    private Label _subtitleLabel = null!;
    private Label _editionLabel = null!;
    private Label _footerLabel = null!;
    private Label _buildLabel = null!;

    private Button _newGameButton = null!;
    private Button _continueButton = null!;
    private Button _optionsButton = null!;
    private Button _quitButton = null!;

    private OpeningStrings _strings = null!;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();

        _bureauSprite = GetNode<TextureRect>("BackgroundLayer/BureauSprite");
        _logoTampon = GetNode<TextureRect>("DecorationLayer/LogoContainer/LogoTampon");
        _buttonsFrame = GetNode<TextureRect>("ButtonsLayer/ButtonsFrame");

        _titleLabel = GetNode<Label>("DecorationLayer/LogoContainer/TitleLabel");
        _subtitleLabel = GetNode<Label>("DecorationLayer/LogoContainer/SubtitleLabel");
        _editionLabel = GetNode<Label>("DecorationLayer/LogoContainer/EditionLabel");
        _footerLabel = GetNode<Label>("FooterLayer/FooterLabel");
        _buildLabel = GetNode<Label>("FooterLayer/BuildLabel");

        _newGameButton = GetNode<Button>("ButtonsLayer/ButtonsFrame/ButtonsList/NewGameButton");
        _continueButton = GetNode<Button>("ButtonsLayer/ButtonsFrame/ButtonsList/ContinueButton");
        _optionsButton = GetNode<Button>("ButtonsLayer/ButtonsFrame/ButtonsList/OptionsButton");
        _quitButton = GetNode<Button>("ButtonsLayer/ButtonsFrame/ButtonsList/QuitButton");

        // Asset textures -- AssetResolver returns a placeholder if the file
        // is missing, so this never null-refs even on a fresh checkout.
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _bureauSprite.Texture = assetResolver.Resolve(BureauAssetKey);
        _logoTampon.Texture = assetResolver.Resolve(LogoAssetKey);
        _buttonsFrame.Texture = assetResolver.Resolve(ButtonsFrameAssetKey);

        // Strings -- single point of swap when Varn revises the .tres.
        _titleLabel.Text = _strings.E1Title;
        _subtitleLabel.Text = _strings.E1Subtitle;
        _editionLabel.Text = _strings.E1Edition;
        _footerLabel.Text = _strings.E1Footer;
        _buildLabel.Text = _strings.E1Build;

        _newGameButton.Text = _strings.E1NewGame;
        _optionsButton.Text = _strings.E1Options;
        _quitButton.Text = _strings.E1Quit;

        // Continue gating -- label flips depending on HasSave.
        ApplyContinueGating();

        _newGameButton.Pressed += OnNewGamePressed;
        _continueButton.Pressed += OnContinuePressed;
        _optionsButton.Pressed += OnOptionsPressed;
        _quitButton.Pressed += OnQuitPressed;

        // P4 hook: in J3+ this is where OnboardingPops.RequestShow("welcome")
        // would land. Intentionally not wired in J2.
    }

    public override void _ExitTree()
    {
        // Disconnection discipline (Pre-brief §4.8 piège #2).
        if (_newGameButton is not null) _newGameButton.Pressed -= OnNewGamePressed;
        if (_continueButton is not null) _continueButton.Pressed -= OnContinuePressed;
        if (_optionsButton is not null) _optionsButton.Pressed -= OnOptionsPressed;
        if (_quitButton is not null) _quitButton.Pressed -= OnQuitPressed;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // F12 debug toggle -- HasSave flip, debug builds only.
        if (!OS.IsDebugBuild()) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } keyEvent) return;
        if (keyEvent.Keycode != Key.F12) return;

        var gameState = GetNode<GameState>("/root/GameState");
        gameState.HasSave = !gameState.HasSave;
        GD.Print($"[E1Title][debug] F12 -> GameState.HasSave = {gameState.HasSave}");
        ApplyContinueGating();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Update the Continuer button disabled state and label based on
    /// <see cref="GameState.HasSave"/>. Extracted so the F12 dev toggle
    /// can re-apply without duplicating logic.
    /// </summary>
    private void ApplyContinueGating()
    {
        var gameState = GetNode<GameState>("/root/GameState");
        var result = ContinueGatingLogic.Compute(
            hasSave: gameState.HasSave,
            enabledLabel: _strings.E1Continue,
            disabledLabel: _strings.E1ContinueDisabled);
        _continueButton.Disabled = result.Disabled;
        _continueButton.Text = result.Label;
    }

    private async void OnNewGamePressed()
    {
        // J1 fix (2026-05-15, Rune -- re-introduced after rollback) -- two-step hop
        // to IsoMapE1Probe.
        //
        // E1Title is instantiated under the SceneManager autoload
        // (NavigateTo does AddChild(screenNode)) ; the GetTree()
        // CurrentScene at this point is the empty BootScene placeholder
        // under /root/BootScene. A naive ChangeSceneToFile would swap
        // only the placeholder, leaving E1Title's CanvasLayer subtree
        // alive and rendering over the new cinematic scene. Symptom :
        // "je clique [01] Ouvrir un nouveau registre et l'écran ne
        // change pas" -- the e1 cinematic actually runs (preflights,
        // flip, POI fade-in all loggués OK) but E1Title's CanvasLayer
        // occludes it. Fix : await SceneManager.TearDownAllScreens()
        // first so the autoload's screen tree is freed, then
        // ChangeSceneToFile. The SceneManager registrations
        // (PackedScenes) are preserved for a future return to the
        // Opening flow in Jalon 2+.
        //
        // 2026-05-13 (Rune) -- route Nouvelle Partie through the e1 cinematic
        // (IsoMapE1Probe) instead of jumping straight to E2_WORLD. The
        // cinematic ends with its own ChangeSceneToFile to E2Stub on POI
        // click. See class docstring "New Game path" for the rationale.
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.TearDownAllScreens();

        GD.Print($"[E1Title] Nouvelle Partie -- ChangeSceneToFile({IsoMapE1ProbeScenePath})");
        var err = GetTree().ChangeSceneToFile(IsoMapE1ProbeScenePath);
        if (err != Error.Ok)
        {
            GD.PushError($"[E1Title] ChangeSceneToFile failed with error {err} -- {IsoMapE1ProbeScenePath} introuvable ?");
        }
    }

    private void OnContinuePressed()
    {
        // J2: defensive -- the button is disabled when HasSave is false, but
        // a buggy debug path could fire. Continuer has no destination in J2
        // (J3+ scope: load last visited screen).
        var gameState = GetNode<GameState>("/root/GameState");
        if (!gameState.HasSave)
        {
            GD.Print("[E1Title] Continue pressed with no save -- ignored");
            return;
        }
        GD.Print("[E1Title] Continue clicked but save load is J3+ scope");
    }

    private async void OnOptionsPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.OpenModal(OptionsModalId);
    }

    private async void OnQuitPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.OpenModal(QuitConfirmModalId);
    }

    public Task OnEnter(ScreenContext context, CancellationToken ct)
    {
        // Re-apply gating on every enter (e.g. after Continue is implemented
        // and HasSave flips between sessions). Cheap and correct.
        ApplyContinueGating();
        return Task.CompletedTask;
    }

    public Task OnExit(CancellationToken ct) => Task.CompletedTask;

    public Task OnSuspend(CancellationToken ct)
    {
        // Disable input when a modal opens above us (Options or Quit in J2).
        ProcessMode = ProcessModeEnum.Disabled;
        return Task.CompletedTask;
    }

    public Task OnResume(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Inherit;
        // Re-apply gating in case F12 was pressed while a modal owned focus.
        ApplyContinueGating();
        return Task.CompletedTask;
    }
}
