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
///   <item>Slice 2's zoom-driven transitions plug into a single seam
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
///         pressed (defense-in-depth across MMB and RMB).</item>
///   <item>Modal-owns-input (D-P8.2-12) : pan input is silently
///         ignored while <c>SceneManager.ActiveModalId</c> is non-null,
///         and the state machine resets so a drag mid-modal-open does
///         not leak across the modal session.</item>
///   <item>Post-drag grace period (commit c70f1ed) : on MMB/RMB
///         release after a real drag, this component calls
///         <c>SceneManager.NotifyDragReleased</c> so the autoload
///         debounces wheel events for 250 ms post-release (lift-off
///         thumb brush mitigation).</item>
///   <item>Threshold drag for RMB (P8.3 commit e91b73b) : the 6 px
///         threshold lives inside <see cref="MapPanInputLogic"/> and
///         is unchanged ; MMB still skips threshold, RMB still
///         requires cross.</item>
/// </list>
/// The xUnit suites <c>CameraPanLogicTests</c> + <c>MapPanInputLogicTests</c>
/// + <c>InputMapPanActionsTests</c> + <c>WheelDuringDragSuppressionTests</c>
/// stay green by construction — the component delegates to the same
/// pure-C# helpers, so there is no new contract surface for them to
/// catch up with.
/// </para>
///
/// <para>
/// <b>Public API surface (the seam consumers depend on).</b>
/// <list type="bullet">
///   <item><see cref="Configure"/> sets the world image texture, frames
///         the camera limits, and centers on a specified initial world
///         position. Called by the consumer at <c>_Ready</c>.</item>
///   <item><see cref="WorldRoot"/> / <see cref="WorldMapSprite"/> /
///         <see cref="WorldCamera"/> / <see cref="PoiContainer"/>
///         expose the world tree so consumers can spawn POIs,
///         entities, fog tiles, etc. as children of the right
///         subtree without poking node paths.</item>
///   <item><see cref="WorldImageSize"/> reports the configured image
///         dimensions for downstream consumers (the fog layer needs
///         it to size the grid).</item>
///   <item><see cref="DragStarted"/> / <see cref="DragEnded"/> signals
///         let the consumer react to gesture state without polling
///         (e.g. close a tooltip on drag start). Slice 1 doesn't yet
///         consume these but exposing them keeps the refactor cost
///         contained when slice 2 wires the zoom-driven nav layer
///         transitions.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a Node2D, not a Control.</b> This component owns a Camera2D
/// and a Sprite2D, both of which are Node2D-rooted by design. Hosting
/// them under a Control would force a coordinate-system marshalling
/// layer (Control space ≠ world space), and the existing audit
/// already warned about the CanvasLayer trap. A Node2D root is the
/// shortest path that keeps the Camera2D in the same canvas_transform
/// as the world it pans. The consumer scene root can still be a
/// Control (E2WorldMap is — anchored to the viewport for chrome
/// reasons) ; this component sits under it as a sibling to the
/// CanvasLayer chrome, and the world coords flow naturally because
/// Control + Node2D children mix without coord conversion as long as
/// the Control has identity transform (which E2WorldMap does).
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

        // Clamp the requested initial center against the limits so the
        // consumer can pass a POI position blindly without recomputing
        // valid camera ranges.
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var clamped = CameraPanLogic.ClampCameraCenter(
            new PanVec2(initialCameraCenter.X, initialCameraCenter.Y),
            new PanVec2(_worldImageSize.X, _worldImageSize.Y),
            new PanVec2(viewportSize.X, viewportSize.Y));
        _worldCamera.Position = new Vector2(clamped.X, clamped.Y);

        _configured = true;
        DumpPreflightState();
    }

    public override void _ExitTree()
    {
        if (_gameSettings is not null && _settingsChangedHandler is not null)
        {
            _gameSettings.SettingsChanged -= _settingsChangedHandler;
        }
        _gameSettings = null;
        _settingsChangedHandler = null;
    }

    public override void _Process(double delta)
    {
        if (!_configured) return;
        if (IsModalOpen()) return;

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
    /// </summary>
    private void DumpPreflightState()
    {
        GD.Print(
            $"[MapPan2DComponent] preflight: WorldCamera Current={_worldCamera.IsCurrent()} " +
            $"Position={_worldCamera.Position} " +
            $"Limits=[({_worldCamera.LimitLeft},{_worldCamera.LimitTop})-" +
            $"({_worldCamera.LimitRight},{_worldCamera.LimitBottom})]");
        GD.Print(
            $"[MapPan2DComponent] preflight: ImageSize={_worldImageSize} " +
            $"ActivePanButton={_activePanButton}");

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
