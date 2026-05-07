using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Modals;

/// <summary>
/// Quit confirmation modal. Two buttons: Confirmer (sync
/// <see cref="SceneTree.Quit"/>) and Annuler (close modal).
/// Esc and click-outside close the modal (Annuler implicit).
///
/// <para>
/// <b>Why sync Quit (Pre-brief J2 D2 = sync GetTree().Quit()).</b>
/// The engine tears down on Quit ; awaiting any cleanup is racing the
/// teardown. Synchronous call is correct here. If we ever need a real
/// pre-quit save flush, we add an async hook before the Quit call ;
/// J2 has no save state to flush.
/// </para>
///
/// <para>
/// <b>SceneTree boundary for testability.</b> The
/// <see cref="ConfirmQuitInvoked"/> signal fires on Confirm <i>before</i>
/// the engine teardown. xUnit tests that drive the modal via fakes can
/// observe the signal without triggering a real <see cref="SceneTree.Quit"/>.
/// </para>
/// </summary>
public partial class QuitConfirmModal : Control, IModalOverlay
{
    public string ModalId => "QUIT_CONFIRM_MODAL";
    public bool CanCloseOnEsc => true;
    public bool CanCloseOnClickOutside => true;

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";

    [Signal] public delegate void ConfirmQuitInvokedEventHandler();

    private ModalShellScene _shell = null!;
    private Label _bodyLabel = null!;
    private Button _confirmButton = null!;
    private Button _cancelButton = null!;

    private OpeningStrings _strings = null!;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();

        _shell = GetNode<ModalShellScene>("ModalShell");
        _bodyLabel = GetNode<Label>("Staging/BodyLabel");
        _confirmButton = GetNode<Button>("Staging/ConfirmButton");
        _cancelButton = GetNode<Button>("Staging/CancelButton");

        _shell.Title = _strings.QuitTitle;

        _bodyLabel.Text = _strings.QuitBody;
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bodyLabel.CustomMinimumSize = new Vector2(480, 0);

        // Reparent the staged children into the shell slots.
        if (_bodyLabel.GetParent() is { } bodyParent) bodyParent.RemoveChild(_bodyLabel);
        _shell.ContentSlot.AddChild(_bodyLabel);

        _confirmButton.Text = _strings.Confirm;
        _cancelButton.Text = _strings.Cancel;
        if (_cancelButton.GetParent() is { } cancelParent) cancelParent.RemoveChild(_cancelButton);
        if (_confirmButton.GetParent() is { } confirmParent) confirmParent.RemoveChild(_confirmButton);
        // Cancel first (left), Confirm second (right) -- Cancel is the
        // safer destination for misclicks.
        _shell.FooterSlot.AddChild(_cancelButton);
        _shell.FooterSlot.AddChild(_confirmButton);

        _confirmButton.Pressed += OnConfirmPressed;
        _cancelButton.Pressed += OnCancelPressed;
        _shell.CloseRequested += OnShellCloseRequested;
    }

    public override void _ExitTree()
    {
        if (_confirmButton is not null) _confirmButton.Pressed -= OnConfirmPressed;
        if (_cancelButton is not null) _cancelButton.Pressed -= OnCancelPressed;
        if (_shell is not null) _shell.CloseRequested -= OnShellCloseRequested;
    }

    public Task OnOpen(ScreenContext context, CancellationToken ct) => Task.CompletedTask;
    public Task OnClose(CancellationToken ct) => Task.CompletedTask;

    private void OnConfirmPressed()
    {
        // Emit before quitting so observers (tests, telemetry) can react.
        EmitSignal(SignalName.ConfirmQuitInvoked);
        GD.Print("[QuitConfirmModal] Confirmer -> GetTree().Quit()");
        GetTree().Quit();
    }

    private void OnCancelPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        _ = sceneManager.CloseModal();
    }

    private void OnShellCloseRequested()
    {
        // X button or click-outside = Cancel implicit.
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        _ = sceneManager.CloseModal();
    }
}
