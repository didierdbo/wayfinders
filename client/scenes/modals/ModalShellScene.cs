using Godot;

namespace Wayfinders.Client.Scenes.Modals;

/// <summary>
/// Reusable enveloppe shell for modal overlays. Provides:
/// <list type="bullet">
///   <item>A dim backdrop (<see cref="DimBackground"/>) that catches
///         click-outside events.</item>
///   <item>A centred frame container with header + content slot + footer
///         slot.</item>
///   <item>A bound title <see cref="Label"/> (<see cref="Title"/> property).</item>
///   <item>A close-X <see cref="Button"/> in the header that emits
///         <see cref="CloseRequested"/>.</item>
///   <item>A click-outside detector on the dim backdrop that emits the
///         same signal.</item>
/// </list>
///
/// <para>
/// <b>Why slot-injection (Pre-brief J2 §3.2 D3 = slot-injection).</b>
/// A C# <c>ModalBase : Control</c> base class would force every concrete
/// modal (Options, Quit, future E4 fiche) to inherit from a hand-rolled
/// abstract class -- a Java-shaped reflex that fights the Godot scene
/// tree. With slot-injection, each modal scene <i>contains</i> a
/// <c>ModalShellScene</c> instance and pushes its specific content into
/// the exposed <see cref="ContentSlot"/> / <see cref="FooterSlot"/>
/// containers. Same effect, idiomatic shape, hot-reload-friendly.
/// </para>
///
/// <para>
/// <b>Click-outside detection.</b> The dim backdrop is a
/// <see cref="ColorRect"/> with mouse filter <c>Stop</c> ; clicks on it
/// fire <see cref="Control.GuiInput"/> which emits
/// <see cref="CloseRequested"/>. Clicks on the centred frame are caught
/// by the frame's own mouse filter and never reach the backdrop -- so
/// click-inside-frame stays safe.
/// </para>
///
/// <para>
/// <b>Esc handling lives at the SceneManager level</b> (J1 infra) and
/// keys off <see cref="Wayfinders.Client.Scripts.Screens.IModalOverlay.CanCloseOnEsc"/>.
/// The shell does not try to handle Esc itself -- avoids double-handling.
/// </para>
/// </summary>
public partial class ModalShellScene : Control
{
    [Signal] public delegate void CloseRequestedEventHandler();

    private Label _titleLabel = null!;
    private Button _closeButton = null!;
    private ColorRect _dimBackground = null!;

    /// <summary>Gets or sets the title rendered in the header label.</summary>
    public string Title
    {
        get => _titleLabel?.Text ?? string.Empty;
        set { if (_titleLabel is not null) _titleLabel.Text = value; }
    }

    /// <summary>
    /// Container where the concrete modal injects its body content. Each
    /// modal scene calls <c>shell.ContentSlot.AddChild(myContentSubtree)</c>
    /// during its own _Ready.
    /// </summary>
    public Container ContentSlot { get; private set; } = null!;

    /// <summary>
    /// Container where the concrete modal injects its action buttons (e.g.
    /// Confirm/Cancel for Quit, Close for Options).
    /// </summary>
    public Container FooterSlot { get; private set; } = null!;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("FrameContainer/ModalFrame/VBox/HeaderRow/TitleLabel");
        _closeButton = GetNode<Button>("FrameContainer/ModalFrame/VBox/HeaderRow/CloseButton");
        _dimBackground = GetNode<ColorRect>("DimBackground");

        ContentSlot = GetNode<Container>("FrameContainer/ModalFrame/VBox/ContentSlot");
        FooterSlot = GetNode<Container>("FrameContainer/ModalFrame/VBox/FooterRow");

        _closeButton.Pressed += OnCloseButtonPressed;
        _dimBackground.GuiInput += OnDimBackgroundInput;
    }

    public override void _ExitTree()
    {
        // Disconnection discipline (Pre-brief §4.8 piège #2). Publisher
        // (this shell) is being freed; subscribers go with it but signals
        // are explicitly cleared for symmetry.
        if (_closeButton is not null)
            _closeButton.Pressed -= OnCloseButtonPressed;
        if (_dimBackground is not null)
            _dimBackground.GuiInput -= OnDimBackgroundInput;
    }

    private void OnCloseButtonPressed()
    {
        EmitSignal(SignalName.CloseRequested);
    }

    private void OnDimBackgroundInput(InputEvent @event)
    {
        // Only react to mouse-button-released events on the backdrop. Frame
        // clicks are absorbed by the frame's mouse filter and never reach
        // here -- this is genuinely click-outside.
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            EmitSignal(SignalName.CloseRequested);
        }
    }
}
