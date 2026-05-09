using System;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Components;

/// <summary>
/// Self-contained 2D pan + camera component (slice 1 / livrable 1 — M3 /
/// L1 World fondations). Hosts the world tree (
/// <see cref="WorldRoot"/> -> <see cref="WorldMapSprite"/>,
/// <see cref="WorldCamera"/>, <see cref="PoiContainer"/>) and owns every
/// gesture that pans or modifies the camera : MMB / RMB drag,
/// ZQSD / WASD / arrow keyboard pan, modal-owns-input gating,
/// wheel-during-drag suppression with defense-in-depth, post-drag grace
/// notification to <see cref="SceneManager"/>.
///
/// <para>
/// <b>Slice 3 extension (M3 / Phase 9 / 2026-05-08).</b> Adds zoom-driven
/// L1 World navigation : the wheel now drives <see cref="Camera2D.Zoom"/>
/// continuously when not at the limits, and at the limits dispatches
/// drill-in (push at <see cref="ZoomMax"/>) or climb-out (pull at
/// <see cref="ZoomMin"/>) candidate signals for the consumer screen to
/// route through <see cref="SceneManager"/>. The screen-level wheel handler
/// runs in <see cref="_Input"/> (priority over autoload's
/// <c>_UnhandledInput</c>) so the slice 3 zoom ladder cohabits cleanly
/// with the Phase 8.1 discrete ladder still active on E1/E3/E4/E5 :
/// when an event is consumed here on L1, SceneManager never sees it ;
/// when L1 is not the current screen, this component is not loaded and
/// SceneManager owns wheel as before.
/// </para>
///
/// <para>
/// <b>Vocabulary discipline (P8 final-lock).</b> The slice 3 wiring uses
/// <see cref="WheelGesture.Pull"/> / <see cref="WheelGesture.Push"/>
/// internally and in console logs. The Godot raw event labels
/// (<c>MouseButton.WheelUp</c> / <c>MouseButton.WheelDown</c>) appear
/// only at exactly one site -- the translation in <see cref="_Input"/>
/// where Godot's event hits the seam. Per the Phase 8 input-traps memo
/// meta-lesson : "ne jamais swapper sans re-validation physique
/// Didier" -- the swap loop's root cause was the Godot label ambiguity,
/// keeping the labels confined to one site eliminates the recurrence.
/// </para>
///
/// <para>
/// <b>Why extract this from E2WorldMap (refactor motivation).</b>
/// Pre-slice-1, the entire pan stack lived inline in
/// <see cref="Wayfinders.Client.Scenes.Screens.E2WorldMap"/>'s
/// <c>_Ready</c> / <c>_Input</c> / <c>_Process</c> (~600 LOC). That
/// scene is the one we extend in slice 1 (livrable 2 fog layer) and
/// will keep extending in slice 2+ (zoom-driven nav transitions,
/// Varn-locked nav redesign §Q5 two-stage wheel pipeline). Inlining
/// every new feature would push E2WorldMap past the maintainable
/// threshold for a single screen file. Extracting the pan stack into
/// a reusable component sets up three downstream wins :
/// <list type="bullet">
///   <item>Slice 3's zoom-driven transitions plug into a single seam
///         (this component) instead of hunting through the screen file.</item>
///   <item>L2 City Halfgate (E3) and L3 District (ex-E5) inherit the
///         same pan grammar by instancing this component, exactly the
///         pattern Varn §5.2 calls for ("grammaire visuelle partagée
///         à 90 % sur les 3 layers").</item>
///   <item>The fog layer (livrable 2) parents under
///         <see cref="WorldRoot"/> automatically by being a child of
///         this component — no plumbing needed at the consumer level.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Compatibility contract — every Phase 8.x invariant preserved.</b>
/// The component is a verbatim translation of the pre-slice-1 inline
/// code in E2WorldMap, with the surface reorganised into typed signals
/// and public methods. Behaviours pinned by the Phase 8 closeouts and
/// the Phase 8 input traps memo (mémoire <c>feedback_godot_rendering_input_traps</c>)
/// remain green :
/// <list type="bullet">
///   <item>CanvasLayer trap (P8.2-fix commit 5a0b470) : the world tree
///         lives directly under this Node2D, not under a CanvasLayer,
///         so the Camera2D's canvas_transform actually affects rendering.</item>
///   <item>Mouse_filter / _Input precedence (P8.2-deep-fix 8295c0d) :
///         every input handler runs in <see cref="_Input"/> not
///         <see cref="_UnhandledInput"/>, so a future Control descendant
///         in the consumer scene cannot silently swallow events.</item>
///   <item>Physical_keycode AZERTY default (P8.2-deep-fix 8295c0d) :
///         the consumer's <c>project.godot</c> still defines
///         <c>ui_pan_left/right/up/down</c> on physical_keycode and the
///         preflight self-check still runs.</item>
///   <item>Wheel-during-drag suppression (P8.2 triple-fix 1e89ad3) :
///         <see cref="_Input"/> consumes wheel events when
///         <c>_isDragging</c> OR either the active pan button is
///         pressed (defense-in-depth across MMB and RMB). Slice 3 keeps
///         this branch FIRST in the wheel handler so a drag-in-progress
///         dominates the new zoom logic.</item>
///   <item>Modal-owns-input (D-P8.2-12) : pan input is silently
///         ignored while <c>SceneManager.ActiveModalId</c> is non-null,
///         and the state machine resets so a drag mid-modal-open does
///         not leak across the modal session. Slice 3 wheel handler
///         honours the same modal gate before any zoom dispatch.</item>
///   <item>Post-drag grace period (commit c70f1ed) : on MMB/RMB
///         release after a real drag, this component calls
///         <c>SceneManager.NotifyDragReleased</c> so the autoload
///         debounces wheel events for 250 ms post-release (lift-off
///         thumb brush mitigation). Slice 3 inherits this behaviour
///         unchanged.</item>
///   <item>Threshold drag for RMB (P8.3 commit e91b73b) : the 6 px
///         threshold lives inside <see cref="MapPanInputLogic"/> and
///         is unchanged ; MMB still skips threshold, RMB still
///         requires cross.</item>
/// </list>
/// The xUnit suites <c>CameraPanLogicTests</c> + <c>MapPanInputLogicTests</c>
/// + <c>InputMapPanActionsTests</c> + <c>WheelDuringDragSuppressionTests</c>
/// + <c>ZoomNavLogicTests</c> stay green by construction — the component
/// delegates to the same pure-C# helpers, so there is no new contract
/// surface for them to catch up with.
/// </para>
/// </summary>
public partial class MapPan2DComponent : Node2D
{
    /// <summary>
    /// ZQSD / WASD / arrow pan speed in world pixels per second
    /// (D-P8.2-05). Carried over from <see cref="Wayfinders.Client.Scenes.Screens.E2WorldMap"/>'s
    /// <c>PanSpeedPxPerSec</c> verbatim — sized so a sustained pan
    /// crosses 3840 px in ~4.8 s. Exported as <c>[Export]</c> so the
    /// E3 / L3 instances Varn §5.2 calls for can override per-layer
    /// (a smaller L3 quartier wants a slower pan).
    /// </summary>
    [Export] public float PanSpeedPxPerSec { get; set; } = 800f;

    /// <summary>Slice 3 -- minimum zoom value (vue d'ensemble).
    /// Smaller numerical values = camera zoomed out further. Default
    /// 0.7 (raised from 0.5 on 2026-05-09 after Didier playtest) = the
    /// world appears at 70 % size.
    ///
    /// <para>
    /// <b>Why 0.7 and not 0.5 / 0.8.</b> At 0.5 the world fits inside
    /// the viewport with ~100 % unused margin around it -- the
    /// peripheral void was visible because the source bitmap stops at
    /// the world edges. Lifting ZoomMin tightens the climb-out floor
    /// so the camera cannot pull back into the void. Two candidate
    /// floors were considered:
    /// <list type="bullet">
    ///   <item><b>0.7</b> -- ratio Default 1.0 -&gt; floor 0.7 = 30 %
    ///         zoom-out, perceptible amplitude for the PULL gesture
    ///         (the player still feels they zoomed out to a vue
    ///         d'ensemble). Source 3840 px wide at 0.7 renders at
    ///         2688 viewport px, slightly wider than the 1920 px
    ///         default viewport so a small pan reach is preserved at
    ///         the floor (matches Varn §5.2 "la vue d'ensemble doit
    ///         rester pannable, pas figée"). Leaves a tampon if the
    ///         bitmap rework Didier flagged ("on ajustera plutôt le
    ///         bitmap pour que le contenu utile reste accessible en
    ///         mode zoom large") undershoots the target useful-area
    ///         ratio.</item>
    ///   <item><b>0.8</b> -- only 20 % zoom-out from Default. The
    ///         PULL gesture loses its sense of amplitude vs Default,
    ///         and the climb-out feedback ("you reached the floor")
    ///         arrives so close to the medium that the floor stops
    ///         feeling like a meaningful threshold. Rejected.</item>
    /// </list>
    /// Per-instance tuning still available via [Export] for L2/L3
    /// (E3 City and E5 District may want a different floor against
    /// their own source bitmaps).
    /// </para>
    /// </summary>
    [Export] public float ZoomMin { get; set; } = 0.7f;

    /// <summary>Slice 3 -- initial zoom value applied at scene entry
    /// (medium). Set as the Camera2D.Zoom on <see cref="Configure"/>.</summary>
    [Export] public float ZoomDefault { get; set; } = 1.0f;

    /// <summary>Slice 3 -- maximum zoom value (détail). Larger
    /// numerical values = camera zoomed in further. Default 2.0 =
    /// the world appears at double size.</summary>
    [Export] public float ZoomMax { get; set; } = 2.0f;

    /// <summary>Slice 3 -- fraction of <see cref="ZoomMax"/> above
    /// which a Push at the cap fires the drill candidate (after
    /// tile knowledge gate). Default 0.95.</summary>
    [Export] public float ZoomInThresholdRatio { get; set; } = 0.95f;

    /// <summary>Slice 3 -- multiplier of <see cref="ZoomMin"/> below
    /// which a Pull at the floor fires the climb candidate. Default
    /// 1.20 -- asymmetric vs zoom-in line per Varn brief slice 3 :
    /// climb requires a more deliberate gesture than drill.</summary>
    [Export] public float ZoomOutThresholdRatio { get; set; } = 1.20f;

    /// <summary>Slice 3 -- amount of zoom delta applied per wheel tick.
    /// 0.15 means each Push lifts Camera2D.Zoom by +0.15 toward
    /// <see cref="ZoomMax"/> ; each Pull drops it by -0.15 toward
    /// <see cref="ZoomMin"/>. Slice 4 (2026-05-08) bumped the default
    /// from 0.10 to 0.15 -- Didier playtest at slice 3 flagged the wheel
    /// continuous zoom as too slow ; 0.15 is ~1.5x more progress per
    /// gesture without losing the smoothness the 0.30 s tween provides.
    /// Per-instance tuning still available via [Export].</summary>
    [Export] public float ZoomStepPerTick { get; set; } = 0.15f;

    /// <summary>Slice 3 -- duration of the per-tick zoom tween, seconds.
    /// 0.30 s = brief slice 3 lock. Tween is killed and replaced if
    /// a new tick arrives mid-animation, so the visible motion is the
    /// running average of recent input rather than a queued sequence.</summary>
    [Export] public float ZoomTweenDurationSec { get; set; } = 0.30f;

    /// <summary>Slice 3 -- post-transition debounce window, ms. After
    /// a drill or climb, every wheel event during this window is
    /// silently ignored at the <see cref="ZoomNavLogic"/> seam (visited
    /// gate). 250 ms = same horizon as <c>SceneManager.PostDragGraceMs</c>.</summary>
    [Export] public ulong ZoomPostTransitionDebounceMs { get; set; } = 250;

    /// <summary>
    /// Names of the four InputMap actions <see cref="_Process"/> polls.
    /// Held in one place so the preflight self-check (<see cref="_Ready"/>)
    /// can verify each one is registered and emit a console warning if
    /// not — same diagnostic reflex E2WorldMap had pre-extraction.
    /// </summary>
    private static readonly string[] PanActionNames =
    {
        "ui_pan_left", "ui_pan_right", "ui_pan_up", "ui_pan_down",
    };

    /// <summary>Fired when a drag (MMB direct or RMB after threshold) starts.</summary>
    [Signal] public delegate void DragStartedEventHandler();

    /// <summary>Fired when an active drag ends. Not fired for unproductive RMB tracking-only releases.</summary>
    [Signal] public delegate void DragEndedEventHandler();

    /// <summary>
    /// Slice 3 -- emitted when the player has zoomed to <see cref="ZoomMax"/>
    /// and pushed an additional wheel tick over a cell whose tile knowledge
    /// state is at <see cref="TileKnowledgeState.Esquissee"/> or higher.
    /// The consumer (<see cref="Wayfinders.Client.Scenes.Screens.E2WorldMap"/>)
    /// translates this into a <c>SceneManager.NavigateTo</c> call with
    /// the origin coord packed into the <see cref="ScreenContext"/> payload.
    /// Coordinates are passed as <c>Vector2I</c> (Godot-native, one of the
    /// few Godot types Godot accepts in [Signal] delegates), the consumer
    /// converts to <see cref="GridCoord"/> at the boundary.
    /// </summary>
    [Signal] public delegate void DrillRequestedEventHandler(Vector2I coord);

    /// <summary>
    /// Slice 3 -- emitted when the player has zoomed to <see cref="ZoomMax"/>
    /// and pushed an additional wheel tick over a cell whose tile knowledge
    /// state is BELOW <see cref="TileKnowledgeState.Esquissee"/>. The
    /// consumer plays the visual feedback ("tuile veut être vue mais pas
    /// encore") via <see cref="FogTileLayer.PlayDrillBlockedFeedback"/>.
    /// No transition fires.
    /// </summary>
    [Signal] public delegate void DrillBlockedEventHandler(Vector2I coord);

    /// <summary>
    /// Slice 3 -- emitted when the player has zoomed to <see cref="ZoomMin"/>
    /// and pulled an additional wheel tick. The consumer dispatches to
    /// <c>SceneManager.NavigateBack</c> (or, future, <c>NavigateLadderUp</c>
    /// when the ladder spine extends further than two rungs).
    /// </summary>
    [Signal] public delegate void ClimbRequestedEventHandler();

    /// <summary>
    /// Slice 3 -- delegate signature for the consumer-supplied "what cell
    /// is under the cursor right now and what is its knowledge state ?".
    /// The component does not own the fog layer or the knowledge store ;
    /// the consumer wires this in via <see cref="SetDrillTargetResolver"/>
    /// at <c>_Ready</c>. The seam is one-way (no back-pressure, no return
    /// channel beyond the value), so the consumer can rebind it freely
    /// (e.g. when the fog layer's grid dimensions change). Returns null
    /// when the cursor is outside the grid.
    /// </summary>
    /// <param name="cursorWorldPosition">Cursor position in world coords.</param>
    /// <returns>Cell coord + knowledge state pair, or null if outside the grid.</returns>
    public delegate (GridCoord coord, TileKnowledgeState state)? DrillTargetResolverDelegate(
        Vector2 cursorWorldPosition);

    private Node2D _worldRoot = null!;
    private Sprite2D _worldMapSprite = null!;
    private Camera2D _worldCamera = null!;
    private Node2D _poiContainer = null!;

    private bool _isDragging;

    private readonly MapPanInputLogic _panLogic = new();
    private MapPanButton _activePanButton = MapPanButton.Middle;
    private GameSettings? _gameSettings;
    private GameSettings.SettingsChangedEventHandler? _settingsChangedHandler;

    private bool _configured;
    private Vector2 _worldImageSize;

    // Slice 3 -- zoom continuous tween state. At most one zoom tween in
    // flight at a time ; killed and replaced on every new wheel tick.
    // Same discipline as FogTileLayer's per-cell tween bag (slice 2).
    private Tween? _zoomTween;
    private float _targetZoom;

    // Slice 3 -- consumer-supplied resolver for "what cell is under
    // cursor" + tile knowledge. Default returns null so the component
    // is functional without the resolver (drill candidates degrade to
    // DrillBlocked silently when the resolver is absent -- good fallback
    // shape for early bring-up before the consumer wires the fog layer
    // in slice 3 phase 2).
    private DrillTargetResolverDelegate? _drillTargetResolver;

    // Slice 3 -- transition lock. Set to true between the moment we
    // emit DrillRequested / ClimbRequested and the moment the consumer
    // re-enters this screen (or the ScreenContext changes). During the
    // transition window, all input is suppressed (zoom, pan, drill).
    // The consumer signals end-of-transition via NotifyTransitionEnded.
    private bool _inTransition;

    // Slice 3 -- last action returned by ZoomNavLogic, surfaced for the
    // HUD overlay (livrable 5). Updated on every wheel event (or kept
    // stale across non-wheel frames -- the HUD reads on every _Process
    // tick).
    private ZoomNavAction _lastZoomAction = ZoomNavAction.None;
    private ulong _lastTransitionTimeMs;

    /// <summary>The world container Node2D. Consumers spawn world-space
    /// children (entities, fog, decals) under this node so they pan with
    /// the camera for free.</summary>
    public Node2D WorldRoot => _worldRoot;

    /// <summary>The world map sprite. Top-left at (0, 0), centered=false.
    /// Texture is set by the consumer via <see cref="Configure"/>.</summary>
    public Sprite2D WorldMapSprite => _worldMapSprite;

    /// <summary>The world camera. Limits and initial position are
    /// configured by <see cref="Configure"/> ; consumers should not
    /// mutate Position directly except for snap-to-target use cases.</summary>
    public Camera2D WorldCamera => _worldCamera;

    /// <summary>The POI container. Pre-positioned under the world tree
    /// so POI hotspots spawned as children pan with the camera.</summary>
    public Node2D PoiContainer => _poiContainer;

    /// <summary>The configured world image dimensions, for downstream
    /// consumers (e.g. <see cref="FogTileLayer"/> sizes its grid from
    /// here). Returns <c>Vector2.Zero</c> before <see cref="Configure"/>.</summary>
    public Vector2 WorldImageSize => _worldImageSize;

    /// <summary>True iff a drag is currently active. Diagnostic surface
    /// for consumers and tests ; the component drives its own gating
    /// internally and does not require the consumer to observe this
    /// flag.</summary>
    public bool IsDragging => _isDragging;

    /// <summary>Slice 3 -- current Camera2D.Zoom uniform scalar, exposed
    /// for the HUD overlay.</summary>
    public float CurrentZoom => _worldCamera.Zoom.X;

    /// <summary>Slice 3 -- last action returned by <see cref="ZoomNavLogic"/>
    /// on the most recent wheel input, exposed for the HUD overlay
    /// (livrable 5). <see cref="ZoomNavAction.None"/> until the first
    /// wheel event arrives.</summary>
    public ZoomNavAction LastZoomAction => _lastZoomAction;

    /// <summary>Slice 3 -- timestamp of the most recent drill or climb,
    /// for HUD debounce countdown computation. 0 if no transition has
    /// happened this session.</summary>
    public ulong LastTransitionTimeMs => _lastTransitionTimeMs;

    /// <summary>Slice 3 -- true iff the post-transition debounce window
    /// is currently active (HUD diagnostic). Computed against the same
    /// thresholds the runtime feeds into <see cref="ZoomNavLogic"/>.</summary>
    public bool IsDebouncing
    {
        get
        {
            if (_lastTransitionTimeMs == 0) return false;
            var nowMs = Time.GetTicksMsec();
            return nowMs - _lastTransitionTimeMs < ZoomPostTransitionDebounceMs;
        }
    }

    /// <summary>Slice 3 -- true iff a drill or climb transition is in
    /// flight (input lock). The consumer ends the lock by calling
    /// <see cref="NotifyTransitionEnded"/>.</summary>
    public bool IsInTransition => _inTransition;

    public override void _Ready()
    {
        // Resolve the in-scene tree the .tscn provides. The component
        // tscn lays out :
        //   MapPan2DComponent (Node2D, this script)
        //     WorldRoot (Node2D)
        //       WorldMapSprite (Sprite2D, centered=false)
        //       WorldCamera (Camera2D, current=true)
        //       PoiContainer (Node2D)
        _worldRoot = GetNode<Node2D>("WorldRoot");
        _worldMapSprite = GetNode<Sprite2D>("WorldRoot/WorldMapSprite");
        _worldCamera = GetNode<Camera2D>("WorldRoot/WorldCamera");
        _poiContainer = GetNode<Node2D>("WorldRoot/PoiContainer");

        // Subscribe to GameSettings.SettingsChanged so a flip in the
        // Options modal takes effect on the next event without a scene
        // reload. Captured-reference disconnect at _ExitTree.
        _gameSettings = GetNodeOrNull<GameSettings>("/root/GameSettings");
        if (_gameSettings is not null)
        {
            _activePanButton = _gameSettings.MapPanButton;
            _settingsChangedHandler = OnSettingsChanged;
            _gameSettings.SettingsChanged += _settingsChangedHandler;
        }
    }

    /// <summary>
    /// Frame the camera around the supplied world image and center on a
    /// specified initial position. Idempotent : safe to call again if
    /// the consumer wants to repoint the camera (e.g. on a return-to-
    /// world-map navigation that should re-anchor on the player's
    /// last known POI).
    ///
    /// <para>
    /// Slice 3 : also resets <see cref="Camera2D.Zoom"/> to
    /// <see cref="ZoomDefault"/>. The transition lock is cleared so a
    /// repeat-Configure (e.g. the consumer re-points after a
    /// climb-return) leaves the component ready to accept input.
    /// </para>
    /// </summary>
    /// <param name="worldImageTexture">Texture for the world map sprite.</param>
    /// <param name="initialCameraCenter">World-space position the camera
    /// should center on after configure.</param>
    public void Configure(Texture2D worldImageTexture, Vector2 initialCameraCenter)
    {
        _worldMapSprite.Texture = worldImageTexture;
        _worldImageSize = worldImageTexture.GetSize();

        _worldCamera.LimitLeft = 0;
        _worldCamera.LimitTop = 0;
        _worldCamera.LimitRight = (int)_worldImageSize.X;
        _worldCamera.LimitBottom = (int)_worldImageSize.Y;
        _worldCamera.MakeCurrent();

        // Slice 3 -- zoom defaults applied here. Direct write (no
        // tween) because Configure runs at scene entry, before any
        // input ; tweening from 0/uninitialised would flicker.
        _targetZoom = ZoomDefault;
        _worldCamera.Zoom = new Vector2(ZoomDefault, ZoomDefault);

        // Clamp the requested initial center against the limits so the
        // consumer can pass a POI position blindly without recomputing
        // valid camera ranges.
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var clamped = CameraPanLogic.ClampCameraCenter(
            new PanVec2(initialCameraCenter.X, initialCameraCenter.Y),
            new PanVec2(_worldImageSize.X, _worldImageSize.Y),
            new PanVec2(viewportSize.X, viewportSize.Y));
        _worldCamera.Position = new Vector2(clamped.X, clamped.Y);

        // Slice 3 -- clear transition lock + last action diagnostic.
        // Last transition timestamp survives Configure so a re-entry
        // after a quick climb-then-Configure-again chain still respects
        // the debounce window.
        _inTransition = false;
        _lastZoomAction = ZoomNavAction.None;

        _configured = true;
        DumpPreflightState();
    }

    /// <summary>
    /// Slice 3 -- consumer hook for the "what cell is under the cursor
    /// + its knowledge state" lookup. The consumer (<see cref="Wayfinders.Client.Scenes.Screens.E2WorldMap"/>)
    /// supplies this at <c>_Ready</c> after wiring its fog layer and
    /// knowledge store ; the component invokes the delegate every time
    /// a wheel event reaches the cap-Push branch and needs to evaluate
    /// the drill gate. Pass null to detach (the component then degrades
    /// to <see cref="ZoomNavAction.DrillBlocked"/> for every cap-Push
    /// -- safe fallback when the consumer hasn't wired a fog layer).
    /// </summary>
    public void SetDrillTargetResolver(DrillTargetResolverDelegate? resolver)
    {
        _drillTargetResolver = resolver;
    }

    /// <summary>
    /// Slice 3 -- consumer hook to release the transition input lock
    /// after a drill or climb has navigated. The consumer calls this
    /// once the SceneManager.NavigateTo / NavigateBack task has
    /// completed, OR if the consumer decides to abort the transition
    /// (the component cannot tell on its own). Idempotent.
    /// </summary>
    public void NotifyTransitionEnded()
    {
        _inTransition = false;
    }

    public override void _ExitTree()
    {
        if (_gameSettings is not null && _settingsChangedHandler is not null)
        {
            _gameSettings.SettingsChanged -= _settingsChangedHandler;
        }
        _gameSettings = null;
        _settingsChangedHandler = null;

        // Slice 3 -- kill any in-flight zoom tween so it does not
        // continue mutating a freed Camera2D after the screen exits.
        // Same discipline as FogTileLayer.DespawnAll for cell tweens.
        if (_zoomTween is not null && _zoomTween.IsValid())
        {
            _zoomTween.Kill();
        }
        _zoomTween = null;

        _drillTargetResolver = null;
    }

    public override void _Process(double delta)
    {
        if (!_configured) return;
        if (IsModalOpen()) return;
        if (_inTransition) return; // slice 3 -- input lock during transition

        var dir = CameraPanLogic.ResolvePanDirection(
            left:  Input.IsActionPressed("ui_pan_left"),
            right: Input.IsActionPressed("ui_pan_right"),
            up:    Input.IsActionPressed("ui_pan_up"),
            down:  Input.IsActionPressed("ui_pan_down"));

        if (dir.X == 0f && dir.Y == 0f) return;

        var current = new PanVec2(_worldCamera.Position.X, _worldCamera.Position.Y);
        var imageSize = new PanVec2(_worldImageSize.X, _worldImageSize.Y);
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var viewport = new PanVec2(viewportSize.X, viewportSize.Y);

        var advanced = CameraPanLogic.AdvanceCameraCenter(
            current, dir, PanSpeedPxPerSec, (float)delta, imageSize, viewport);

        _worldCamera.Position = new Vector2(advanced.X, advanced.Y);
    }

    public override void _Input(InputEvent @event)
    {
        if (!_configured) return;

        // P8.2 triple-fix wheel-during-drag suppression. Defense-in-depth
        // across MMB and RMB regardless of which is currently active —
        // a stale press of the inactive button is a benign suppression,
        // never a false-negative on the active one. See pre-extraction
        // E2WorldMap class doc paragraph "P8.1+P8.2 triple-fix".
        //
        // KEEP THIS BRANCH FIRST in slice 3 -- a drag in progress
        // dominates any wheel-zoom intent. If the player is panning
        // with MMB held and brushes the wheel, we silently swallow,
        // exactly as in slice 2.
        if ((_isDragging
             || Input.IsMouseButtonPressed(MouseButton.Middle)
             || Input.IsMouseButtonPressed(MouseButton.Right))
            && @event is InputEventMouseButton wheel
            && wheel.Pressed
            && (wheel.ButtonIndex == MouseButton.WheelUp
                || wheel.ButtonIndex == MouseButton.WheelDown))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        // Slice 3 -- modal + transition gates. Modal owns input
        // (P8.1 invariant). Transition lock prevents new wheel input
        // while a drill / climb is still in flight (the consumer ends
        // the lock via NotifyTransitionEnded).
        if (@event is InputEventMouseButton wheelEvent
            && wheelEvent.Pressed
            && (wheelEvent.ButtonIndex == MouseButton.WheelUp
                || wheelEvent.ButtonIndex == MouseButton.WheelDown))
        {
            if (IsModalOpen() || _inTransition)
            {
                // Don't mark handled -- modal-open path is the
                // SceneManager's own concern, the autoload also
                // respects the modal gate (and ignores during transitions
                // because we don't notify it of zoom-driven nav state).
                // Wait actually : we DO need to mark handled here so
                // SceneManager._UnhandledInput's discrete ladder dispatch
                // does not fire while we're already mid-drill -- the
                // brief is explicit on this. Mark handled.
                GetViewport().SetInputAsHandled();
                return;
            }

            // Slice 3 -- consume on L1 World, do not let SceneManager
            // run its discrete-ladder _UnhandledInput. The screen-level
            // _Input precedence guarantees this works ; SceneManager's
            // wheel handler will not see this event because we mark
            // it handled below at the end of every dispatched branch.
            HandleWheelEvent(wheelEvent);
            GetViewport().SetInputAsHandled();
            return;
        }

        var activeButtonGodot = _activePanButton == MapPanButton.Middle
            ? MouseButton.Middle
            : MouseButton.Right;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == activeButtonGodot)
        {
            if (IsModalOpen())
            {
                _panLogic.Reset();
                if (_isDragging)
                {
                    _isDragging = false;
                    EmitSignal(SignalName.DragEnded);
                }
                return;
            }

            if (mb.Pressed)
            {
                var press = _panLogic.OnPress(
                    _activePanButton,
                    new PanVec2(mb.Position.X, mb.Position.Y),
                    new PanVec2(_worldCamera.Position.X, _worldCamera.Position.Y));

                if (press.EnteredDrag)
                {
                    _isDragging = true;
                    EmitSignal(SignalName.DragStarted);
                    GD.Print(
                        $"[MapPan2DComponent] {_activePanButton}-drag begin (no-threshold) " +
                        $"at viewport {mb.Position}, camera {_worldCamera.Position}");
                }
                // RMB-Tracking path : silent until threshold cross. The
                // post-MVP context-menu slot lives here.
            }
            else
            {
                var release = _panLogic.OnRelease();
                if (release.WasDragging)
                {
                    _isDragging = false;
                    var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
                    sceneManager?.NotifyDragReleased();
                    EmitSignal(SignalName.DragEnded);
                    GD.Print(
                        $"[MapPan2DComponent] {_activePanButton}-drag end, " +
                        $"camera now {_worldCamera.Position}");
                }
                // Tracking-only release (RMB no-cross-threshold) is silent.
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion mm)
        {
            var motion = _panLogic.OnMotion(new PanVec2(mm.Position.X, mm.Position.Y));
            if (!motion.ShouldPan) return;

            if (motion.JustPromoted)
            {
                _isDragging = true;
                EmitSignal(SignalName.DragStarted);
                GD.Print(
                    $"[MapPan2DComponent] {_activePanButton}-drag begin " +
                    $"(cross threshold {MapPanInputLogic.DragThresholdPx}px) " +
                    $"at viewport {mm.Position}, camera {_worldCamera.Position}");
            }

            // 1:1 inverse from press position (NOT cross point) — same
            // delta-from-press semantic the pre-extraction code used.
            var delta = mm.Position - new Vector2(motion.PressPosition.X, motion.PressPosition.Y);
            var desired = new Vector2(motion.CameraStart.X, motion.CameraStart.Y) - delta;

            var imageSize = new PanVec2(_worldImageSize.X, _worldImageSize.Y);
            var viewportSize = GetViewport().GetVisibleRect().Size;
            var viewport = new PanVec2(viewportSize.X, viewportSize.Y);
            var clamped = CameraPanLogic.ClampCameraCenter(
                new PanVec2(desired.X, desired.Y), imageSize, viewport);

            _worldCamera.Position = new Vector2(clamped.X, clamped.Y);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Slice 3 -- core wheel dispatch on L1 World. Translates the raw
    /// Godot event to a <see cref="WheelGesture"/> at this single seam
    /// (P8 final-lock meta-lesson : the WheelUp/WheelDown labels are
    /// mentioned exactly here and nowhere else), evaluates
    /// <see cref="ZoomNavLogic"/>, and dispatches to the appropriate
    /// branch : continuous zoom tween, drill candidate signal, drill
    /// blocked signal, climb candidate signal, or silent debounce.
    /// </summary>
    private void HandleWheelEvent(InputEventMouseButton wheelEvent)
    {
        // Translation seam : Godot's MouseButton.WheelUp/Down event
        // labels become WheelGesture.Push/Pull. This mapping is the
        // P8 final-lock convention (validated 2026-05-07 by Didier
        // physical-gesture re-test on Logitech hardware) :
        //
        //   PULL  (geste vers le poignet)   -> MouseButton.WheelDown event
        //   PUSH  (geste vers les phalanges)-> MouseButton.WheelUp   event
        //
        // The labels appear EXACTLY in this method body and the SceneManager's
        // _UnhandledInput. Anywhere else in the slice 3 codebase, including
        // ZoomNavLogic, the tests, signals, console logs, and class docs,
        // we use the pull/push vocabulary only. See class XML doc
        // "Vocabulary discipline" paragraph.
        var gesture = wheelEvent.ButtonIndex == MouseButton.WheelUp
            ? WheelGesture.Push
            : WheelGesture.Pull;

        var nowMs = Time.GetTicksMsec();
        var thresholds = BuildThresholds();

        // Tile knowledge gate : resolve the cell + state under the cursor.
        // Default to Inconnue if the consumer hasn't wired the resolver.
        var cellAtCursor = ResolveCellAtCursor(out var stateAtCursor);

        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: _worldCamera.Zoom.X,
            gesture: gesture,
            tileKnowledgeAtCursor: stateAtCursor,
            lastTransitionTimeMs: _lastTransitionTimeMs,
            currentTimeMs: nowMs,
            thresholds: thresholds);

        _lastZoomAction = outcome.Action;

        switch (outcome.Action)
        {
            case ZoomNavAction.ZoomContinuous:
                ApplyContinuousZoom(gesture, wheelEvent.Position);
                break;

            case ZoomNavAction.DrillCandidate when cellAtCursor is { } drillCoord:
                _inTransition = true;
                _lastTransitionTimeMs = nowMs;
                EmitSignal(SignalName.DrillRequested, new Vector2I(drillCoord.Col, drillCoord.Row));
                GD.Print(
                    $"[MapPan2DComponent] DrillRequested at cell ({drillCoord.Col},{drillCoord.Row}), " +
                    $"state={stateAtCursor}, zoom={_worldCamera.Zoom.X:F3}");
                break;

            case ZoomNavAction.DrillCandidate:
                // Drill candidate fired but resolver returned null --
                // cursor outside the grid. Silent no-op (we don't
                // navigate into nowhere).
                GD.Print(
                    "[MapPan2DComponent] DrillCandidate suppressed: cursor outside grid");
                break;

            case ZoomNavAction.DrillBlocked when cellAtCursor is { } blockedCoord:
                _lastTransitionTimeMs = nowMs; // record for debounce hold
                EmitSignal(SignalName.DrillBlocked, new Vector2I(blockedCoord.Col, blockedCoord.Row));
                GD.Print(
                    $"[MapPan2DComponent] DrillBlocked at cell ({blockedCoord.Col},{blockedCoord.Row}), " +
                    $"state={stateAtCursor}");
                break;

            case ZoomNavAction.DrillBlocked:
                GD.Print("[MapPan2DComponent] DrillBlocked: cursor outside grid (no feedback)");
                break;

            case ZoomNavAction.ClimbCandidate:
                _inTransition = true;
                _lastTransitionTimeMs = nowMs;
                EmitSignal(SignalName.ClimbRequested);
                GD.Print($"[MapPan2DComponent] ClimbRequested at zoom={_worldCamera.Zoom.X:F3}");
                break;

            case ZoomNavAction.Debounced:
                // Silent no-op -- expected during the 250 ms post-transition
                // window. Logged at trace level only via the lastZoomAction
                // surface (HUD overlay shows it in real time).
                break;

            case ZoomNavAction.None:
            default:
                break;
        }
    }

    /// <summary>
    /// Slice 3 -- tween Camera2D.Zoom by one step toward
    /// <see cref="ZoomMin"/> or <see cref="ZoomMax"/>, with the zoom
    /// pivot anchored under the cursor (Google Maps / RTS classic). The
    /// pivot adjustment is computed before the tween starts and the
    /// camera position is offset in the same tween for a smooth combined
    /// motion -- without this, a wheel zoom from a corner would drift
    /// the focal point toward the camera center, which feels broken on
    /// a tactical map where players want to drill on a specific tile.
    /// </summary>
    private void ApplyContinuousZoom(WheelGesture gesture, Vector2 wheelEventViewportPos)
    {
        var step = gesture == WheelGesture.Push ? ZoomStepPerTick : -ZoomStepPerTick;
        var nextZoom = ClampZoom(_worldCamera.Zoom.X + step);
        if (System.MathF.Abs(nextZoom - _targetZoom) < ZoomNavLogic.ZoomEpsilon)
        {
            // Already at or asymptotically at target -- nothing to do.
            // Avoid scheduling a no-motion tween that would still emit
            // a frame of work.
            return;
        }
        _targetZoom = nextZoom;

        // Cursor-anchored zoom math.
        //
        // We want the world point currently under the cursor to remain
        // under the cursor after the zoom completes. Camera2D.Zoom
        // semantics (Godot 4) : the visible region around camera.Position
        // is (viewport / zoom) wide. A higher zoom value = the same
        // viewport shows a smaller world region around camera.Position.
        //
        // Let cursorViewport be the cursor's viewport-space position
        // (px from top-left of the viewport). Let zoomBefore = current
        // zoom, zoomAfter = next zoom. The world position under the
        // cursor before the zoom is :
        //
        //   worldBefore = camera.Position + (cursorViewport - viewport/2) / zoomBefore
        //
        // We want worldAfter == worldBefore, i.e. :
        //
        //   camera.NewPosition + (cursorViewport - viewport/2) / zoomAfter
        //     == camera.Position + (cursorViewport - viewport/2) / zoomBefore
        //
        //   camera.NewPosition = camera.Position
        //                      + (cursorViewport - viewport/2) * (1/zoomBefore - 1/zoomAfter)
        //
        // We clamp the resulting position so the camera does not slide
        // past the limits when zooming near an edge.
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var viewportCenter = viewportSize / 2f;
        var cursorOffsetFromCenter = wheelEventViewportPos - viewportCenter;
        var zoomBefore = _worldCamera.Zoom.X;
        var zoomAfter = nextZoom;
        var positionDelta = cursorOffsetFromCenter * (1f / zoomBefore - 1f / zoomAfter);

        var desiredPosition = _worldCamera.Position + positionDelta;
        var clamped = CameraPanLogic.ClampCameraCenter(
            new PanVec2(desiredPosition.X, desiredPosition.Y),
            new PanVec2(_worldImageSize.X, _worldImageSize.Y),
            new PanVec2(viewportSize.X, viewportSize.Y));
        var clampedPosition = new Vector2(clamped.X, clamped.Y);

        // Tween discipline -- kill any in-flight zoom tween before
        // scheduling a new one. The visible motion is the latest target,
        // not a queue ; rapid wheel ticks merge into a single smooth
        // motion to the latest cumulative target.
        if (_zoomTween is not null && _zoomTween.IsValid())
        {
            _zoomTween.Kill();
        }
        _zoomTween = CreateTween();
        _zoomTween.SetParallel(true);
        _zoomTween.TweenProperty(
                _worldCamera,
                "zoom",
                new Vector2(nextZoom, nextZoom),
                ZoomTweenDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        _zoomTween.TweenProperty(
                _worldCamera,
                "position",
                clampedPosition,
                ZoomTweenDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);

        GD.Print(
            $"[MapPan2DComponent] zoom continuous {gesture}: " +
            $"{zoomBefore:F3} -> {zoomAfter:F3} (cursor anchor at {wheelEventViewportPos})");
    }

    /// <summary>Slice 3 -- clamp a candidate zoom value to the
    /// <see cref="ZoomMin"/> / <see cref="ZoomMax"/> range.</summary>
    private float ClampZoom(float candidate)
    {
        if (candidate < ZoomMin) return ZoomMin;
        if (candidate > ZoomMax) return ZoomMax;
        return candidate;
    }

    /// <summary>Slice 3 -- build the threshold record from the [Export]
    /// fields. Allocated per wheel event ; the record is a struct so
    /// no GC pressure.</summary>
    private ZoomThresholds BuildThresholds() => new(
        ZoomMin: ZoomMin,
        ZoomDefault: ZoomDefault,
        ZoomMax: ZoomMax,
        ZoomInThresholdRatio: ZoomInThresholdRatio,
        ZoomOutThresholdRatio: ZoomOutThresholdRatio,
        PostTransitionDebounceMs: ZoomPostTransitionDebounceMs);

    /// <summary>
    /// Slice 3 -- compute the cell under the cursor in world coordinates,
    /// and look up its knowledge state via the consumer-supplied
    /// resolver. Returns null if the cursor is outside the grid OR if
    /// no resolver is wired (in which case the state is assumed Inconnue,
    /// which makes drill candidates degrade to DrillBlocked safely).
    /// </summary>
    private GridCoord? ResolveCellAtCursor(out TileKnowledgeState state)
    {
        state = TileKnowledgeState.Inconnue;
        if (_drillTargetResolver is null)
        {
            return null;
        }

        var viewport = GetViewport();
        var mouseViewportPos = viewport.GetMousePosition();
        var canvasTransform = viewport.GetCanvasTransform();
        var cursorWorld = canvasTransform.AffineInverse() * mouseViewportPos;

        var resolved = _drillTargetResolver(cursorWorld);
        if (resolved is null) return null;

        state = resolved.Value.state;
        return resolved.Value.coord;
    }

    private void OnSettingsChanged()
    {
        if (_gameSettings is null) return;
        var newButton = _gameSettings.MapPanButton;
        if (newButton == _activePanButton) return;
        _activePanButton = newButton;
        _panLogic.Reset();
        if (_isDragging)
        {
            _isDragging = false;
            EmitSignal(SignalName.DragEnded);
        }
        GD.Print($"[MapPan2DComponent] active pan button now {_activePanButton}, state machine reset");
    }

    private bool IsModalOpen()
    {
        var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
        return sceneManager?.ActiveModalId is not null;
    }

    /// <summary>
    /// Mirror of E2WorldMap's pre-extraction <c>DumpInputAndCameraState</c>
    /// : a one-shot console dump of every state dimension the pan stack
    /// touches, fired from <see cref="Configure"/>. Cheap, allocation-
    /// free in the hot path. The phase-8 input traps memo (mémoire
    /// <c>feedback_godot_rendering_input_traps</c>) explicitly calls out
    /// this preflight as mandatory — silently broken InputMap entries
    /// were the second-most painful regression of phase 8.
    ///
    /// <para>
    /// Slice 3 extension : also dumps the zoom thresholds the wheel
    /// pipeline runs against, so a Configure-time misconfiguration
    /// (e.g. ZoomMax &lt; ZoomMin from a tscn override) is visible
    /// immediately on the console.
    /// </para>
    /// </summary>
    private void DumpPreflightState()
    {
        GD.Print(
            $"[MapPan2DComponent] preflight: WorldCamera Current={_worldCamera.IsCurrent()} " +
            $"Position={_worldCamera.Position} Zoom={_worldCamera.Zoom} " +
            $"Limits=[({_worldCamera.LimitLeft},{_worldCamera.LimitTop})-" +
            $"({_worldCamera.LimitRight},{_worldCamera.LimitBottom})]");
        GD.Print(
            $"[MapPan2DComponent] preflight: ImageSize={_worldImageSize} " +
            $"ActivePanButton={_activePanButton}");
        GD.Print(
            $"[MapPan2DComponent] preflight: zoom thresholds " +
            $"[Min={ZoomMin}, Default={ZoomDefault}, Max={ZoomMax}] " +
            $"InLine={(ZoomMax * ZoomInThresholdRatio):F3} " +
            $"OutLine={(ZoomMin * ZoomOutThresholdRatio):F3} " +
            $"step={ZoomStepPerTick} tweenSec={ZoomTweenDurationSec} " +
            $"debounceMs={ZoomPostTransitionDebounceMs}");

        var hasLeft  = InputMap.HasAction(PanActionNames[0]);
        var hasRight = InputMap.HasAction(PanActionNames[1]);
        var hasUp    = InputMap.HasAction(PanActionNames[2]);
        var hasDown  = InputMap.HasAction(PanActionNames[3]);
        GD.Print(
            $"[MapPan2DComponent] preflight: InputMap " +
            $"{PanActionNames[0]}={hasLeft} " +
            $"{PanActionNames[1]}={hasRight} " +
            $"{PanActionNames[2]}={hasUp} " +
            $"{PanActionNames[3]}={hasDown}");
        if (!(hasLeft && hasRight && hasUp && hasDown))
        {
            GD.PushWarning(
                "[MapPan2DComponent] preflight: at least one ui_pan_* action is missing from InputMap. " +
                "ZQSD pan will silently no-op until project.godot is fixed.");
        }
    }
}
