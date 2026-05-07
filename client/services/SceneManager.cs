using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton that owns navigation between the Opening Scenario
/// screens (E1-E5) and the modal overlay (E4). Combines a pure
/// <see cref="NavigationStack"/> (testable hors-Godot) with a Godot scene
/// tree orchestration shell.
///
/// <para>
/// <b>Boundaries.</b>
/// <list type="bullet">
///   <item>SceneManager owns: scene instantiation, AddChild/QueueFree,
///         lifecycle method invocation, modal layer, Esc handling,
///         per-navigation cancellation token.</item>
///   <item>SceneManager does NOT own: per-screen UI, content data, art
///         resolution. Those live in IScreen impls and AssetResolver.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Thread-safety contract (Pre-brief Risk #1, locked).</b>
/// Every <c>await</c> in this class that crosses an async boundary is
/// followed by an <c>await ToSignal(GetTree(), ProcessFrame)</c> resync
/// before any scene tree mutation. The pattern is enforced by code review;
/// xUnit cannot catch off-thread tree corruption (no scene tree in tests).
/// </para>
///
/// <para>
/// <b>Modal invariant (Pre-brief Risk #3, locked).</b>
/// At most one active modal at a time in J1. <see cref="OpenModal{T}"/>
/// rejects modal-on-modal with a logged warning rather than throwing.
/// Modals do NOT hide the underlying screen visually — they overlay it
/// (see <see cref="OpenModal"/> body, which never touches host visibility).
/// </para>
///
/// <para>
/// <b>Registration model.</b>
/// Screens are registered as PackedScene resources via
/// <see cref="RegisterScreen{T}"/> and <see cref="RegisterModal{T}"/>.
/// Each Opening Scenario screen scene calls
/// <c>SceneManager.RegisterScreen&lt;E1Title&gt;("E1_TITLE", packedScene)</c>
/// in a future bootstrap step ; for J1 the boot scene does the wiring at
/// startup. Lookup-by-id keeps the manager loosely coupled to concrete
/// screen types — adding E6 in a future jalon does not edit this file.
/// </para>
/// </summary>
public partial class SceneManager : Node
{
    private readonly NavigationStack _stack = new();
    private readonly Dictionary<string, PackedScene> _screenRegistry = new();
    private readonly Dictionary<string, PackedScene> _modalRegistry = new();

    // The live Node behind each entry on the stack. Parallel-indexed by
    // screen id ; we only need one entry per id because the J1 invariant is
    // "no screen appears twice in the stack" (cycles don't happen in our
    // 5-screen graph: E1 -> E2 -> E3 -> E5 is acyclic ; E4 is a modal).
    private readonly Dictionary<string, Node> _liveScreens = new();

    private CanvasLayer _modalLayer = null!;
    private Node? _activeModalNode;
    private IModalOverlay? _activeModal;
    private string? _modalCallerScreenId;

    // Per-navigation cancellation token. Cancelled and recycled on each
    // navigation so a previous in-flight OnEnter cannot race a new one.
    private CancellationTokenSource? _navCts;

    [Signal] public delegate void NavigationStartedEventHandler(string fromScreen, string toScreen);
    [Signal] public delegate void NavigationCompletedEventHandler(string toScreen);
    [Signal] public delegate void ModalOpenedEventHandler(string modalId);
    [Signal] public delegate void ModalClosedEventHandler(string modalId);

    /// <summary>Top-of-stack screen id, or null when stack is empty.</summary>
    public string? CurrentScreenId => _stack.Current?.ScreenId;

    /// <summary>Snapshot of the navigation stack, bottom to top. Used for tests and inspector.</summary>
    public IReadOnlyList<ScreenStackEntry> StackSnapshot => _stack.Snapshot();

    /// <summary>Currently open modal id, or null if none.</summary>
    public string? ActiveModalId => _activeModal?.ModalId;

    public override void _Ready()
    {
        // Modal layer is a CanvasLayer so it draws above the screen layer
        // and is naturally outside the stack tree. CanvasLayer with a layer
        // index above 0 sits on top of the default 2D world.
        _modalLayer = new CanvasLayer { Name = "ModalLayer", Layer = 10 };
        AddChild(_modalLayer);

        GD.Print("[SceneManager] ready");
    }

    public override void _ExitTree()
    {
        _navCts?.Cancel();
        _navCts?.Dispose();
        _navCts = null;
        GD.Print("[SceneManager] disposed");
    }

    /// <summary>
    /// Register a screen scene by id. Call this once per screen during
    /// bootstrap. Idempotent — re-registering the same id with a new
    /// scene replaces the old binding (useful for hot-reload in dev).
    /// </summary>
    public void RegisterScreen(string screenId, PackedScene scene)
    {
        _screenRegistry[screenId] = scene;
    }

    /// <summary>Register a modal overlay scene by id. See <see cref="RegisterScreen"/>.</summary>
    public void RegisterModal(string modalId, PackedScene scene)
    {
        _modalRegistry[modalId] = scene;
    }

    /// <summary>
    /// Push a new screen on top of the stack. The previous top, if any,
    /// stays alive in the tree but receives no further input until popped
    /// or until this new screen is popped off.
    ///
    /// <para>
    /// <b>J1 simplification.</b> Previous screens stay loaded (not freed)
    /// because the Opening Scenario flow is small. If memory pressure
    /// matters in J3+ on a long playthrough we revisit — for J1 it is the
    /// simpler shape and it preserves their state for free on Pop.
    /// </para>
    /// </summary>
    public async Task NavigateTo(string screenId, ScreenContext? context = null)
    {
        var ctx = context ?? ScreenContext.Empty;
        ctx = ctx with { CallerScreenId = CurrentScreenId };

        if (!_screenRegistry.TryGetValue(screenId, out var packedScene))
        {
            GD.PushError($"[SceneManager] NavigateTo: screen '{screenId}' not registered");
            return;
        }

        var fromScreenId = CurrentScreenId ?? "(none)";
        EmitSignal(SignalName.NavigationStarted, fromScreenId, screenId);

        // Recycle the per-navigation cancellation token.
        _navCts?.Cancel();
        _navCts?.Dispose();
        _navCts = new CancellationTokenSource();
        var ct = _navCts.Token;

        // Hide the previous top if any. We do not call OnExit because it
        // is still in the stack — it just loses focus. Visibility off
        // keeps the input layer clean and prevents the previous screen
        // from rendering behind the new one.
        if (_stack.Current is { } prev && _liveScreens.TryGetValue(prev.ScreenId, out var prevNode))
        {
            SetScreenVisibility(prevNode, visible: false);
        }

        // Instantiate, configure, attach. Add as child of *this* autoload
        // so the screen sits beneath the modal CanvasLayer in the tree.
        var screenNode = packedScene.Instantiate();
        AddChild(screenNode);
        _liveScreens[screenId] = screenNode;
        _stack.Push(new ScreenStackEntry(screenId, ctx));

        if (screenNode is IScreen screenImpl)
        {
            await screenImpl.OnEnter(ctx, ct).ConfigureAwait(false);
            // Risk #1 mitigation: resync to main thread before emitting
            // the completion signal (signal emission touches the tree).
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        else
        {
            GD.PushWarning($"[SceneManager] '{screenId}' scene root does not implement IScreen — lifecycle skipped");
        }

        EmitSignal(SignalName.NavigationCompleted, screenId);
    }

    /// <summary>
    /// Pop the current screen, returning to the previous one. No-op if
    /// the stack has 1 or 0 entries (Esc on root is silent — pre-brief
    /// §5 Esc handler routes that case to focus the Quit button).
    /// </summary>
    public async Task NavigateBack()
    {
        if (_stack.Count <= 1)
        {
            // Root or empty — caller (the Esc handler) decides what to do.
            return;
        }

        _navCts?.Cancel();
        _navCts?.Dispose();
        _navCts = new CancellationTokenSource();
        var ct = _navCts.Token;

        var leavingEntry = _stack.Pop()!;
        EmitSignal(SignalName.NavigationStarted, leavingEntry.ScreenId, _stack.Current!.ScreenId);

        if (_liveScreens.TryGetValue(leavingEntry.ScreenId, out var leavingNode))
        {
            if (leavingNode is IScreen screenImpl)
            {
                await screenImpl.OnExit(ct).ConfigureAwait(false);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            leavingNode.QueueFree();
            _liveScreens.Remove(leavingEntry.ScreenId);
        }

        // Show the previous top — reciprocal of the Hide on Push.
        var newTop = _stack.Current!;
        if (_liveScreens.TryGetValue(newTop.ScreenId, out var newTopNode))
        {
            SetScreenVisibility(newTopNode, visible: true);
        }

        EmitSignal(SignalName.NavigationCompleted, newTop.ScreenId);
    }

    /// <summary>
    /// Toggle the visibility of a screen subtree. Handles the Godot trap
    /// where <c>CanvasLayer</c> children render <i>independently of their
    /// parent's <c>Visible</c></c> — so setting <c>Visible = false</c> on
    /// a <c>Control</c> root does NOT hide content authored under
    /// <c>CanvasLayer</c> children. We must walk down and toggle every
    /// <see cref="CanvasLayer"/> descendant explicitly.
    ///
    /// <para>
    /// <b>Why this matters.</b> Every Opening Scenario screen (E1-E5)
    /// authors its visible content under named <c>CanvasLayer</c> nodes
    /// (BackgroundLayer, DecorationLayer, ButtonsLayer, …). A naive
    /// <c>root.Visible = false</c> leaves all of those rendering, which
    /// surfaced as the J3 bug "menu de départ reste ouvert après clic
    /// pour aller en e2" (E1 still drawn behind E2).
    /// </para>
    ///
    /// <para>
    /// <b>Why not toggle a single ancestor CanvasLayer.</b> The screen
    /// node itself is added directly under the SceneManager autoload — we
    /// could wrap each push in a fresh <c>CanvasLayer</c>, but that
    /// changes the addressing of <c>/root/SceneManager/&lt;ScreenName&gt;</c>
    /// every screen's <c>GetNode</c> calls implicitly rely on. The
    /// recursive walk is the smaller blast radius and it is correct for
    /// any future scene shape (including ones that mix CanvasLayer and
    /// plain Control subtrees).
    /// </para>
    ///
    /// <para>
    /// <b>Out of scope.</b> Modals do not call this — they rely on the
    /// SceneManager's <c>_modalLayer</c> being on top, with the host
    /// screen left visible underneath (pre-brief modal invariant).
    /// </para>
    /// </summary>
    private static void SetScreenVisibility(Node root, bool visible)
    {
        if (root is CanvasItem rootCanvasItem)
        {
            rootCanvasItem.Visible = visible;
        }

        foreach (var canvasLayer in FindCanvasLayerDescendants(root))
        {
            canvasLayer.Visible = visible;
        }
    }

    /// <summary>
    /// Yield every <see cref="CanvasLayer"/> descendant of <paramref name="root"/>,
    /// including nested ones. Stops the recursion at each CanvasLayer (its
    /// own <c>Visible</c> propagates to its CanvasItem children, so we
    /// don't need to descend further).
    /// </summary>
    private static IEnumerable<CanvasLayer> FindCanvasLayerDescendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is CanvasLayer layer)
            {
                yield return layer;
                // No need to descend into a CanvasLayer's CanvasItem
                // children: toggling layer.Visible already hides them.
                // But a nested CanvasLayer would render independently —
                // walk those.
                foreach (var nested in FindNestedCanvasLayers(layer))
                    yield return nested;
            }
            else
            {
                foreach (var nested in FindCanvasLayerDescendants(child))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<CanvasLayer> FindNestedCanvasLayers(CanvasLayer parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is CanvasLayer nested)
            {
                yield return nested;
                foreach (var deeper in FindNestedCanvasLayers(nested))
                    yield return deeper;
            }
            else
            {
                foreach (var deeper in FindCanvasLayerDescendants(child))
                    yield return deeper;
            }
        }
    }

    /// <summary>
    /// Open a modal overlay above the current screen. The current screen
    /// receives <see cref="IScreen.OnSuspend"/> immediately before the
    /// modal receives <see cref="IModalOverlay.OnOpen"/>.
    ///
    /// <para>
    /// Rejects modal-on-modal with a warning (J1 invariant, Risk #3).
    /// </para>
    ///
    /// <para>
    /// <b>Modal invariant (locked).</b> This method does NOT toggle the
    /// host screen's visibility. The modal CanvasLayer (<c>_modalLayer</c>,
    /// layer 10) renders above the screen layer naturally, and the host
    /// stays visible underneath. <see cref="IScreen.OnSuspend"/> only
    /// disables host input/processing, not its render. If a future jalon
    /// needs a fully-blocking modal, that is a new affordance, not a
    /// change here.
    /// </para>
    /// </summary>
    public async Task OpenModal(string modalId, ScreenContext? context = null)
    {
        if (_activeModal is not null)
        {
            GD.PushWarning($"[SceneManager] OpenModal('{modalId}') refused: modal '{_activeModal.ModalId}' already open");
            return;
        }

        if (!_modalRegistry.TryGetValue(modalId, out var packedScene))
        {
            GD.PushError($"[SceneManager] OpenModal: modal '{modalId}' not registered");
            return;
        }

        var ctx = (context ?? ScreenContext.Empty) with { CallerScreenId = CurrentScreenId };
        _modalCallerScreenId = CurrentScreenId;

        _navCts?.Cancel();
        _navCts?.Dispose();
        _navCts = new CancellationTokenSource();
        var ct = _navCts.Token;

        // Suspend the current screen first so it stops listening to input.
        // Note: we deliberately do NOT call SetScreenVisibility here — the
        // modal sits on top via _modalLayer, and the host must stay
        // rendered underneath (pre-brief modal invariant).
        if (CurrentScreenId is { } currentId
            && _liveScreens.TryGetValue(currentId, out var hostNode)
            && hostNode is IScreen hostImpl)
        {
            await hostImpl.OnSuspend(ct).ConfigureAwait(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        _activeModalNode = packedScene.Instantiate();
        _modalLayer.AddChild(_activeModalNode);

        if (_activeModalNode is IModalOverlay modalImpl)
        {
            _activeModal = modalImpl;
            await modalImpl.OnOpen(ctx, ct).ConfigureAwait(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            EmitSignal(SignalName.ModalOpened, modalId);
        }
        else
        {
            GD.PushWarning($"[SceneManager] modal '{modalId}' scene root does not implement IModalOverlay");
        }
    }

    /// <summary>Close the active modal. No-op if none open.</summary>
    public async Task CloseModal()
    {
        if (_activeModal is null || _activeModalNode is null)
            return;

        _navCts?.Cancel();
        _navCts?.Dispose();
        _navCts = new CancellationTokenSource();
        var ct = _navCts.Token;

        var modalId = _activeModal.ModalId;
        await _activeModal.OnClose(ct).ConfigureAwait(false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        _activeModalNode.QueueFree();
        _activeModalNode = null;
        _activeModal = null;
        EmitSignal(SignalName.ModalClosed, modalId);

        // Resume the host screen.
        if (_modalCallerScreenId is { } callerId
            && _liveScreens.TryGetValue(callerId, out var hostNode)
            && hostNode is IScreen hostImpl)
        {
            await hostImpl.OnResume(ct).ConfigureAwait(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _modalCallerScreenId = null;
    }

    /// <summary>
    /// Esc key handler. Routes to CloseModal if a modal is open, else
    /// NavigateBack if the stack has more than 1 entry. Esc on root is
    /// a silent no-op (Title screen owns the Quit confirmation in J2).
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
            return;

        if (_activeModal is not null && _activeModal.CanCloseOnEsc)
        {
            // Fire-and-forget intentional: input handlers are not async.
            // Cancellation in CloseModal handles robustness if Esc is
            // spammed (each call recycles _navCts).
            _ = CloseModal();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_stack.Count > 1)
        {
            _ = NavigateBack();
            GetViewport().SetInputAsHandled();
        }
    }
}
