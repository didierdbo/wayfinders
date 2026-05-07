using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Modals;

/// <summary>
/// E1 Options stub modal. Three controls (Volume slider, Plein écran
/// toggle, Langue dropdown) wired to log-only handlers -- no real audio
/// bus, no real fullscreen mode change, no real i18n. J2 stub.
///
/// <para>
/// <b>Why log-only and not wired (Pre-brief J2 §3.3, D1 = stub fixé).</b>
/// (a) Real persistence is hors-scope J2.
/// (b) A live fullscreen toggle that mute the user mid-test loses time.
/// (c) Audio bus needs an actual sound asset = J3+ scope.
/// All three controls log a marker prefix <c>[Options stub]</c> so the
/// console-watching dev sees the wiring works without the side-effect.
/// </para>
///
/// <para>
/// <b>Composition shape (Pre-brief J2 §3.2, D3 = slot-injection).</b>
/// The scene root is this <c>OptionsModal</c> ; it embeds an instance of
/// <see cref="ModalShellScene"/> and pushes its 3 control rows into the
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
    private HSlider _volumeSlider = null!;
    private CheckButton _fullscreenToggle = null!;
    private OptionButton _languageDropdown = null!;
    private Button _closeButton = null!;

    private OpeningStrings _strings = null!;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();

        _shell = GetNode<ModalShellScene>("ModalShell");

        // The three stub controls and the close button were instanced as
        // pre-built children in the .tscn under named container nodes ;
        // we move them into the shell slots at runtime so the shell stays
        // the only structural authority on layout.
        _volumeSlider = GetNode<HSlider>("Staging/VolumeSlider");
        _fullscreenToggle = GetNode<CheckButton>("Staging/FullscreenToggle");
        _languageDropdown = GetNode<OptionButton>("Staging/LanguageDropdown");
        _closeButton = GetNode<Button>("Staging/CloseButton");

        _shell.Title = _strings.OptionsTitle;

        // Fill the dropdown -- one entry only in J2.
        _languageDropdown.Clear();
        _languageDropdown.AddItem(_strings.OptionsLanguageFrench);
        _languageDropdown.Selected = 0;

        // Volume slider J2 stub: 0..100, default 80, no real audio bus.
        _volumeSlider.MinValue = 0.0;
        _volumeSlider.MaxValue = 100.0;
        _volumeSlider.Step = 1.0;
        _volumeSlider.Value = 80.0;
        _volumeSlider.CustomMinimumSize = new Vector2(220, 0);

        // Build labelled rows: [Label] [Control] for each.
        _shell.ContentSlot.AddChild(BuildLabelledRow(_strings.OptionsVolume, _volumeSlider));
        _shell.ContentSlot.AddChild(BuildLabelledRow(_strings.OptionsFullscreen, _fullscreenToggle));
        _shell.ContentSlot.AddChild(BuildLabelledRow(_strings.OptionsLanguage, _languageDropdown));

        _closeButton.Text = _strings.OptionsClose;
        _shell.FooterSlot.AddChild(_closeButton);

        // Wire signals (stubs only -- log on change, no side-effect).
        _volumeSlider.ValueChanged += OnVolumeChanged;
        _fullscreenToggle.Toggled += OnFullscreenToggled;
        _languageDropdown.ItemSelected += OnLanguageSelected;
        _closeButton.Pressed += OnClosePressed;
        _shell.CloseRequested += OnShellCloseRequested;
    }

    public override void _ExitTree()
    {
        // Disconnection discipline (Pre-brief §4.8 piège #2).
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
