using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Modals;

/// <summary>
/// E1 Options modal. Hosts four player-facing rows: the P8.3-shipped
/// <b>Bouton de pan de la carte</b> dropdown (Middle / Right with the
/// 6 px drag threshold gating the latter), and the J2 stub trio
/// (Volume slider, Plein écran toggle, Langue dropdown) which remain
/// log-only -- no real audio bus, no real fullscreen, no real i18n.
///
/// <para>
/// <b>P8.3 (M3 / Arc 3 / Phase 8.3) -- map pan config widget.</b> The
/// <c>MapPanDropdown</c> is the first Options row with a real, persisted
/// effect in MVP. It binds to <see cref="GameSettings.MapPanButton"/>
/// via <see cref="OptionButton.ItemSelected"/> -- the autoload owns
/// persistence, this modal only translates the dropdown index to the
/// <see cref="MapPanButton"/> enum and back. Cohérent dropdown widget
/// (D-P8.3-04 = OptionButton over RadioButton/CheckButton, see pre-brief
/// §3.4 for rationale). Position: <b>première ligne</b> du ContentSlot
/// (D-P8.3-13) par convention industrie -- les controls viennent avant
/// les audio/display.
/// </para>
///
/// <para>
/// <b>Voix système, pas voix Cadastre (D-P8.3-12).</b> The three new
/// libellés <c>OptionsMapPanLabel</c>, <c>OptionsMapPanMiddle</c>,
/// <c>OptionsMapPanRight</c> are voix S, cohérents avec Volume / Plein
/// écran / Langue. Hors juridiction Varn-lock voix Cadastre. Swap target
/// existe via le .tres en hot-edit si Varn ratifie un pivot voix A
/// post-tests.
/// </para>
///
/// <para>
/// <b>Why log-only and not wired (Pre-brief J2 §3.3, D1 = stub fixé).</b>
/// (a) Real persistence is hors-scope J2.
/// (b) A live fullscreen toggle that mute the user mid-test loses time.
/// (c) Audio bus needs an actual sound asset = J3+ scope.
/// All three controls log a marker prefix <c>[Options stub]</c> so the
/// console-watching dev sees the wiring works without the side-effect.
/// P8.3 inherits the rampe-de-lancement <see cref="GameSettings"/>
/// autoload so the trio can be promoted to real bindings in a future
/// jalon without restructuring the modal -- just extend
/// <see cref="GameSettings"/> and rewire each <c>OnXxxChanged</c>.
/// </para>
///
/// <para>
/// <b>Composition shape (Pre-brief J2 §3.2, D3 = slot-injection).</b>
/// The scene root is this <c>OptionsModal</c> ; it embeds an instance of
/// <see cref="ModalShellScene"/> and pushes its 4 control rows into the
/// shell's <see cref="ModalShellScene.ContentSlot"/> at <c>_Ready</c>.
/// The Close button lives in the shell's footer slot.
/// </para>
/// </summary>
public partial class OptionsModal : Control, IModalOverlay
{
    public string ModalId => "OPTIONS_MODAL";
    public bool CanCloseOnEsc => true;
    public bool CanCloseOnClickOutside => true;

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";

    private ModalShellScene _shell = null!;
    private OptionButton _mapPanDropdown = null!;
    private HSlider _volumeSlider = null!;
    private CheckButton _fullscreenToggle = null!;
    private OptionButton _languageDropdown = null!;
    private Button _closeButton = null!;

    private OpeningStrings _strings = null!;
    private GameSettings? _gameSettings;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();

        _shell = GetNode<ModalShellScene>("ModalShell");

        // The four stub controls and the close button were instanced as
        // pre-built children in the .tscn under named container nodes ;
        // we move them into the shell slots at runtime so the shell stays
        // the only structural authority on layout.
        _mapPanDropdown = GetNode<OptionButton>("Staging/MapPanDropdown");
        _volumeSlider = GetNode<HSlider>("Staging/VolumeSlider");
        _fullscreenToggle = GetNode<CheckButton>("Staging/FullscreenToggle");
        _languageDropdown = GetNode<OptionButton>("Staging/LanguageDropdown");
        _closeButton = GetNode<Button>("Staging/CloseButton");

        _shell.Title = _strings.OptionsTitle;

        // P8.3 -- MapPanButton dropdown (real, persisted via GameSettings).
        // The order of AddItem calls is locked to MapPanButton enum integer
        // order (Middle = 0 first, Right = 1 second) so the dropdown index
        // can cast directly to the enum on selection. Same pattern Godot
        // uses for OptionButton-as-enum-binding -- the int-cast roundtrip
        // is AOT-clean (no reflection on Variant).
        _mapPanDropdown.Clear();
        _mapPanDropdown.AddItem(_strings.OptionsMapPanMiddle); // index 0 = MapPanButton.Middle
        _mapPanDropdown.AddItem(_strings.OptionsMapPanRight);  // index 1 = MapPanButton.Right
        _gameSettings = GetNodeOrNull<GameSettings>("/root/GameSettings");
        _mapPanDropdown.Selected = _gameSettings is not null
            ? (int)_gameSettings.MapPanButton
            : (int)MapPanButton.Middle;

        // J2 stubs (unchanged) -- single-entry dropdown for Langue, default
        // Volume slider, default Fullscreen toggle.
        _languageDropdown.Clear();
        _languageDropdown.AddItem(_strings.OptionsLanguageFrench);
        _languageDropdown.Selected = 0;

        // Volume slider J2 stub: 0..100, default 80, no real audio bus.
        _volumeSlider.MinValue = 0.0;
        _volumeSlider.MaxValue = 100.0;
        _volumeSlider.Step = 1.0;
        _volumeSlider.Value = 80.0;
        _volumeSlider.CustomMinimumSize = new Vector2(220, 0);

        // Build labelled rows: [Label] [Control] for each. P8.3 D-P8.3-13
        // puts MapPanButton in the first slot -- controls before audio /
        // display, convention industrie (Steam settings, CK3, XCOM 2).
        _shell.ContentSlot.AddChild(BuildLabelledRow(_strings.OptionsMapPanLabel, _mapPanDropdown));
        _shell.ContentSlot.AddChild(BuildLabelledRow(_strings.OptionsVolume, _volumeSlider));
        _shell.ContentSlot.AddChild(BuildLabelledRow(_strings.OptionsFullscreen, _fullscreenToggle));
        _shell.ContentSlot.AddChild(BuildLabelledRow(_strings.OptionsLanguage, _languageDropdown));

        _closeButton.Text = _strings.OptionsClose;
        _shell.FooterSlot.AddChild(_closeButton);

        // Wire signals. MapPanDropdown is real (forwards to GameSettings) ;
        // the J2 trio remain log-only stubs.
        _mapPanDropdown.ItemSelected += OnMapPanSelected;
        _volumeSlider.ValueChanged += OnVolumeChanged;
        _fullscreenToggle.Toggled += OnFullscreenToggled;
        _languageDropdown.ItemSelected += OnLanguageSelected;
        _closeButton.Pressed += OnClosePressed;
        _shell.CloseRequested += OnShellCloseRequested;
    }

    public override void _ExitTree()
    {
        // Disconnection discipline (Pre-brief §4.8 piège #2). Order matches
        // the wire-up so visual diff stays clean.
        if (_mapPanDropdown is not null) _mapPanDropdown.ItemSelected -= OnMapPanSelected;
        if (_volumeSlider is not null) _volumeSlider.ValueChanged -= OnVolumeChanged;
        if (_fullscreenToggle is not null) _fullscreenToggle.Toggled -= OnFullscreenToggled;
        if (_languageDropdown is not null) _languageDropdown.ItemSelected -= OnLanguageSelected;
        if (_closeButton is not null) _closeButton.Pressed -= OnClosePressed;
        if (_shell is not null) _shell.CloseRequested -= OnShellCloseRequested;
    }

    public Task OnOpen(ScreenContext context, CancellationToken ct) => Task.CompletedTask;
    public Task OnClose(CancellationToken ct) => Task.CompletedTask;

    private static HBoxContainer BuildLabelledRow(string labelText, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var label = new Label
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(180, 0)
        };
        row.AddChild(label);
        // Note: the control was previously parented under "Staging" ; Godot
        // will reparent automatically when AddChild is called and the node
        // already has a parent (RemoveChild + AddChild equivalent).
        if (control.GetParent() is { } existingParent)
        {
            existingParent.RemoveChild(control);
        }
        row.AddChild(control);
        return row;
    }

    /// <summary>
    /// P8.3 real handler -- forwards the dropdown selection to the
    /// <see cref="GameSettings"/> autoload, which persists to
    /// <c>user://settings.cfg</c> and emits <c>SettingsChanged</c> for
    /// <see cref="Wayfinders.Client.Scenes.Screens.E1WorldMap"/> to pick
    /// up the change without a screen reload (effet immédiat, exit
    /// criterion §1.2.3 of the pre-brief).
    /// </summary>
    private void OnMapPanSelected(long index)
    {
        if (_gameSettings is null)
        {
            GD.PushWarning("[OptionsModal] GameSettings autoload missing -- map pan preference change ignored");
            return;
        }
        // Index ranges 0..1 by construction (we Clear + AddItem(2) above).
        // A defensive clamp keeps a future re-ordering accident safe.
        var button = index switch
        {
            (long)MapPanButton.Right => MapPanButton.Right,
            _ => MapPanButton.Middle,
        };
        _gameSettings.MapPanButton = button;
    }

    private void OnVolumeChanged(double value)
    {
        // J2 stub -- real audio bus pending, see Pre-brief §3.3.
        GD.Print($"[Options stub] volume = {value:F0}");
    }

    private void OnFullscreenToggled(bool pressed)
    {
        // J2 stub -- intentionally NOT calling DisplayServer.WindowSetMode
        // to avoid disorienting the user mid-playtest (Pre-brief Risk §3.3).
        GD.Print($"[Options stub] fullscreen = {pressed}");
    }

    private void OnLanguageSelected(long index)
    {
        // J2 stub -- i18n pipeline is post-MVP (Varn §7.13).
        GD.Print($"[Options stub] language index = {index}");
    }

    private void OnClosePressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        _ = sceneManager.CloseModal();
    }

    private void OnShellCloseRequested()
    {
        // X button or click-outside: same intent as Close.
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        _ = sceneManager.CloseModal();
    }
}
