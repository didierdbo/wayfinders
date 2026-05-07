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
///
/// <para>
/// <b>P8.1 layer ladder (M3 / Arc 3 / Phase 8.1).</b>
/// The mouse wheel triggers <see cref="NavigateLadderUp"/> /
/// <see cref="NavigateLadderDown"/> which resolve a target screen id
/// against the cadastral ladder <c>monde / cité / quartier</c>
/// (<see cref="LadderResolutionLogic.DefaultLadder"/>) and delegate to
/// <see cref="NavigateBack"/> (up) or <see cref="NavigateTo"/> (down).
/// The ladder is data on this autoload, not a member of <see cref="IScreen"/> —
/// individual screens stay agnostic of who is above or below them.
/// Wheel events while a modal is open are ignored (modal owns input).
/// A 200ms debounce on the wheel input mitigates trackpad continuous
/// scroll firing five navigations in a single gesture (Risk #2).
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

    // P8.1 layer ladder. Defaults to monde / cité / quartier (Varn §7.5)
    // matching the MVP screen registration in OpeningBootstrap. Held as
    // a settable list rather than a constant so a future jalon (or a test
    // harness) can swap it without subclassing the autoload.
    private IReadOnlyList<LayerRung> _ladder = LadderResolutionLogic.DefaultLadder;

    // P8.1 wheel debounce. Trackpad continuous scroll fires events at
    // ~60Hz on a single gesture. Without a debounce, one swipe = five
    // ladder navigations in 80ms = E2 -> E3 -> E5 -> E5 -> E5 (the last
    // three being silent no-ops but still spamming _navCts cancellations
    // and animation frames). 200ms is the smallest gap that feels
    // responsive on a real mouse wheel click ; comes from manual testing
    // on the laptop trackpad + a Logitech wheel.
    private const ulong WheelDebounceMs = 200;
    private ulong _lastWheelTickMs;

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

    /// <summary>
    /// Active layer ladder used by <see cref="NavigateLadderUp"/> /
    /// <see cref="NavigateLadderDown"/>. P8.1 default is
    /// <see cref="LadderResolutionLogic.DefaultLadder"/>.
    /// </summary>
    public IReadOnlyList<LayerRung> Ladder => _ladder;

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
    /// P8.1 -- molette UP / climb the ladder (more granular layer to less
    /// granular). Resolves the target via
    /// <see cref="LadderResolutionLogic.ResolveUpTarget"/> and delegates
    /// to <see cref="NavigateBack"/> when there is a target.
    ///
    /// <para>
    /// <b>Why NavigateBack and not NavigateTo on the resolved id.</b>
    /// In MVP, every navigation between E2 / E3 / E5 either goes through
    /// the ladder helpers or through the existing POI-click path, both
    /// of which preserve the invariant `stack spine == ladder spine`.
    /// So popping is correct and also has the right side-effect: the
    /// leaving screen is freed, the previous top is shown again. If a
    /// future jalon introduces a raccourci that violates the spine
    /// alignment (e.g. "drill from E2 directly to E5 skipping E3"), this
    /// method needs revisiting -- captured as Risk #1 in the P8.1 pre-brief.
    /// </para>
    ///
    /// <para>
    /// No-op (silent, debug-logged) when the current screen is the top
    /// rung (E2 monde) or is not part of the ladder at all (E1 title).
    /// </para>
    /// </summary>
    public async Task NavigateLadderUp()
    {
        var target = LadderResolutionLogic.ResolveUpTarget(CurrentScreenId, _ladder);
        if (target is null)
        {
            GD.Print($"[SceneManager] Ladder up no-op (current={CurrentScreenId ?? "(none)"})");
            return;
        }

        await NavigateBack();
    }

    /// <summary>
    /// P8.1 -- molette DOWN / drill into the ladder (less granular layer
    /// to more granular). Resolves the target via
    /// <see cref="LadderResolutionLogic.ResolveDownTarget"/> and delegates
    /// to <see cref="NavigateTo"/> when there is a target.
    ///
    /// <para>
    /// No-op (silent, debug-logged) when the current screen is the bottom
    /// rung (E5 quartier) or is not part of the ladder at all (E1 title).
    /// </para>
    /// </summary>
    public async Task NavigateLadderDown()
    {
        var target = LadderResolutionLogic.ResolveDownTarget(CurrentScreenId, _ladder);
        if (target is null)
        {
            GD.Print($"[SceneManager] Ladder down no-op (current={CurrentScreenId ?? "(none)"})");
            return;
        }

        await NavigateTo(target);
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
    /// Esc key handler + P8.1 mouse wheel ladder navigation. Routes Esc
    /// to CloseModal if a modal is open, else NavigateBack if the stack
    /// has more than 1 entry. Esc on root is a silent no-op (Title screen
    /// owns the Quit confirmation in J2).
    ///
    /// <para>
    /// <b>P8.2-UX-fix wheel direction (Bug 1).</b> Physical wheel forward
    /// ("up") triggers <see cref="NavigateLadderDown"/> (drill in toward
    /// the more granular layer) ; physical wheel backward ("down") triggers
    /// <see cref="NavigateLadderUp"/> (climb out toward the less granular
    /// layer). The names of the navigation methods stay aligned with the
    /// ladder semantics (Up = lower index = top of ladder = climb out) ;
    /// the swap lives at the InputEventMouseButton dispatch site, see body
    /// comment for the rationale (empirical hardware quirk on Win11 +
    /// Logitech wheel).
    /// </para>
    ///
    /// <para>
    /// 200ms debounce mitigates trackpad continuous scroll. Wheel events
    /// are ignored entirely while a modal is open.
    /// </para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
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
            return;
        }

        // P8.1 -- molette ladder navigation. We only react on the press
        // edge ; Godot fires WheelUp/WheelDown as InputEventMouseButton
        // with Pressed = true, then a matching Released the same frame.
        if (@event is InputEventMouseButton mb && mb.Pressed
            && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
        {
            // Modal owns input -- wheel is silently ignored while a modal
            // is open. Defense-doc'd by LadderInputContractTests.
            if (_activeModal is not null)
            {
                return;
            }

            // Trackpad continuous scroll mitigation (Risk #2). On a real
            // mouse wheel, ticks are ~50ms apart on aggressive spinning,
            // so 200ms only blocks the 2nd+3rd ticks of a single flick --
            // intentional, the player gets exactly one navigation per
            // discrete gesture.
            var nowMs = Time.GetTicksMsec();
            if (nowMs - _lastWheelTickMs < WheelDebounceMs)
            {
                return;
            }
            _lastWheelTickMs = nowMs;

            // P8.2-UX-fix (Bug 1) -- empirical wheel direction swap.
            // Didier reported (commit 8295c0d, Win11 + Logitech wheel) that
            // physically scrolling the wheel forward ("up") fired the
            // ladder-down navigation, and vice versa. The original P8.1
            // binding (WheelUp -> NavigateLadderUp, WheelDown -> NavigateLadderDown)
            // read semantically correct -- both LadderResolutionLogic.ResolveUpTarget
            // and the Up/Down naming on NavigateLadderUp/Down are
            // self-consistent (Up = walk toward ladder index 0 = climb out
            // toward less granular ; Down = walk toward higher index = drill
            // in). The empirical mismatch is at the InputEventMouseButton
            // boundary -- the way Godot 4.x surfaces MouseButton.WheelUp/Down
            // on this hardware does not match the player's physical-gesture
            // mental model. The correct fix is here, at the policy site, not
            // in the resolution logic (which stays semantically pure).
            //
            // After fix: physical wheel forward = NavigateLadderUp (climb out
            // E5 -> E3 -> E2) ; physical wheel backward = NavigateLadderDown
            // (drill in E2 -> E3 -> E5). Matches maps / IDE / browser
            // convention as expressed by Didier.
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                _ = NavigateLadderDown();
            }
            else
            {
                _ = NavigateLadderUp();
            }
            GetViewport().SetInputAsHandled();
        }
    }
}
