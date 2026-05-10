using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
using static Wayfinders.Client.Scripts.Screens.CompagniePanelLogic;
using static Wayfinders.Client.Scripts.Screens.M1SliceLogic;

namespace Wayfinders.Client.Scenes.Play;

/// <summary>
/// M1 slice 5ab (2026-05-10) — first composite playable scene
/// where the Compagnie panel and the Mission panel coexist in
/// one runtime tree. This is the seed of the M1 ship-state UI :
/// a sliding Compagnie sub-iso panel on the LEFT, a fixed
/// text-only Mission panel on the RIGHT, with cross-panel
/// drag-drop wired through the new
/// <see cref="M1SliceLogic"/> contract.
///
/// <para>
/// <b>Why a single composite scene rather than instancing the
/// two probes.</b> The scratch probes (CompagniePanelProbe,
/// MissionPanelProbe) each ship a self-contained canary
/// preflight, BgRect background, and full-screen layout that
/// would collide if instanced side-by-side. The M1 slice is
/// the first time these patterns ship together for real, so we
/// inline a clean, focused scene that reuses the patterns
/// (Pattern P1-P4 SubViewport sub-space, Pattern P5 cross-panel
/// drag-drop, Pattern P6 fixed Mission) without the scratch
/// scaffolding. Probes stay around for isolated debugging ;
/// this scene is the production-shaped runtime.
/// </para>
///
/// <para>
/// <b>Layout (Didier-locked 2026-05-10).</b>
/// <list type="bullet">
///   <item>Compagnie panel : left side, slides in from off-screen
///         when handle clicked (Pattern P2). 560 x 720 px panel
///         when open ; only the brown handle (40 px) pokes out
///         when closed.</item>
///   <item>Mission panel : right side, FIXED (never slides,
///         never auto-rabats). 600 x 800 px panel anchored to
///         the right edge with a 40 px margin. Pattern P6 lock :
///         it never emits AnotherLayerOpened, so opening
///         Compagnie does not flick Mission anywhere.</item>
///   <item>Drop slot : a Control on Mission's left edge sized to
///         match the persona-cube footprint when projected to
///         screen, used as the drag-drop target. Visual : a
///         dashed-border rectangle that highlights when a proxy
///         is hovering it.</item>
///   <item>Cross-panel proxy : a golden ColorRect (40 x 40 px)
///         on a TOP CanvasLayer (layer=20, above both panels)
///         that follows the cursor while a drag is in progress
///         (Pattern P5).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Drag-drop flow (Pattern P5 implementation).</b>
/// <list type="number">
///   <item>Player presses LMB on a persona Area3D inside the
///         Compagnie SubViewport. The Compagnie sub-scene
///         captures the click and emits its existing
///         PersoAssignedToMission signal (kept for compatibility
///         with the click-only Compagnie probe path) PLUS a new
///         <see cref="OnPersonaPressed"/> bridge call that
///         starts the M1Slice cross-panel drag.</item>
///   <item>M1Slice spawns the golden 2D proxy at the persona's
///         screen-projected position (Camera3D.UnprojectPosition
///         from inside the SubViewport, then offset by the
///         SubViewportContainer's GlobalPosition).</item>
///   <item>While LMB is held, _Input updates the proxy.Position
///         to follow the cursor. The Mission slot Control's
///         GetGlobalRect() is the hit-test target.</item>
///   <item>On LMB release, <see cref="M1SliceLogic.DecideDropOutcome"/>
///         classifies the drop. Accepted : emit
///         <see cref="PersonaAssignedToMission"/> signal +
///         flip the persona's [mission] indicator + the Mission
///         panel's existing OnAffectPressed pipeline takes over.
///         Cancelled : free the proxy with a snap-back tween.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>NPC autonomy invariant (locked 2026-05-09).</b>
/// The persona Node3D inside the Compagnie SubViewport is NEVER
/// re-parented during a drag-drop. The proxy is a throwaway 2D
/// visual ; the GameState authoritative state is mutated via
/// the cross-panel signal handler. Same contract as the
/// CompagniePanelProbe Pattern P3, generalised to cross-panel.
/// </para>
///
/// <para>
/// <b>Mission emergence loop.</b> WorldSimTick autoload is
/// already running (project.godot autoload list). The Mission
/// panel subscribes to its MissionEmerged signal and renders the
/// head of GameState.PendingMissions, identical to the existing
/// MissionPanelProbe wiring. The M1 slice contributes nothing
/// new to the tick loop : it is the consumer of the existing
/// 4a-4d emergence chain.
/// </para>
///
/// <para>
/// <b>Strict isolation.</b>
/// <c>metadata/skip_opening_bootstrap = true</c> on the root
/// prevents the OpeningBootstrap autoload from injecting the
/// cartographer's office over the slice (lesson 1bis from
/// <c>feedback_godot_rendering_input_traps.md</c>).
/// </para>
/// </summary>
public partial class M1Slice : Node2D
{
    /// <summary>
    /// Slide animation duration on the Compagnie SubViewportContainer
    /// (Pattern P2 refinement : the .Position tween is on the parent-screen
    /// Container, not the inner Node3D).
    /// </summary>
    [Export] public float CompagnieSlideDurationSec { get; set; } = 0.4f;

    /// <summary>
    /// Closed / open X positions for the Compagnie SubViewportContainer.
    /// Closed = panel hides off-screen to the left, only the handle pokes out.
    /// </summary>
    [Export] public float CompagnieClosedXPx { get; set; } = -560f;

    /// <summary>Open X for the Compagnie SubViewportContainer. 0 = flush left.</summary>
    [Export] public float CompagnieOpenXPx { get; set; } = 0f;

    // ---- Compagnie SubViewport bindings.
    [Export] public NodePath CompagnieContainerPath { get; set; } = "CompagnieRoot/SubViewportContainer";
    [Export] public NodePath CompagnieSubViewportPath { get; set; } = "CompagnieRoot/SubViewportContainer/SubViewport";
    [Export] public NodePath CompagnieSubSpaceRoot3DPath { get; set; } = "CompagnieRoot/SubViewportContainer/SubViewport/SubSpaceRoot3D";
    [Export] public NodePath CompagnieCamera3DPath { get; set; } = "CompagnieRoot/SubViewportContainer/SubViewport/SubSpaceRoot3D/SubSpaceCamera3D";
    [Export] public NodePath CompagniePersonasContainerPath { get; set; } = "CompagnieRoot/SubViewportContainer/SubViewport/SubSpaceRoot3D/Personas";
    [Export] public NodePath CompagnieHandlePath { get; set; } = "CompagnieRoot/Handle";

    // ---- Mission panel bindings.
    [Export] public NodePath MissionHeaderLabelPath { get; set; } = "MissionRoot/Panel/HeaderLabel";
    [Export] public NodePath MissionHookLabelPath { get; set; } = "MissionRoot/Panel/HookLabel";
    [Export] public NodePath MissionEligibleLabelPath { get; set; } = "MissionRoot/Panel/EligibleLabel";
    [Export] public NodePath MissionDropSlotPath { get; set; } = "MissionRoot/Panel/DropSlot";
    [Export] public NodePath MissionAffectButtonPath { get; set; } = "MissionRoot/Panel/AffectButton";
    [Export] public NodePath MissionDeclineButtonPath { get; set; } = "MissionRoot/Panel/DeclineButton";
    [Export] public NodePath MissionToastLabelPath { get; set; } = "MissionRoot/Panel/ToastLabel";
    [Export] public NodePath MissionQueueStatusLabelPath { get; set; } = "MissionRoot/Panel/QueueStatusLabel";
    [Export] public NodePath MissionTickLabelPath { get; set; } = "MissionRoot/Panel/TickLabel";

    // ---- Drag-drop proxy CanvasLayer.
    [Export] public NodePath DragProxyLayerPath { get; set; } = "DragProxyLayer";
    [Export] public NodePath DragProxyRectPath { get; set; } = "DragProxyLayer/ProxyRect";

    // ---- Status / log labels.
    [Export] public NodePath StatusLabelPath { get; set; } = "OverlayUI/StatusLabel";
    [Export] public NodePath LiveLogLabelPath { get; set; } = "OverlayUI/LiveLogLabel";

    private const float PersonaSpacingWorldUnits = 96f;
    private const float Label3DPixelSize = 0.5f;
    private const int LiveLogMaxLines = 8;

    /// <summary>Cross-panel drag-drop signal — the Mission panel
    /// pipeline subscribes to this and routes through its existing
    /// resolve flow. Production-shaped : same pattern as
    /// CompagniePanelLogic.AssignToMissionEventName, scoped to
    /// the M1 slice.</summary>
    [Signal] public delegate void PersonaAssignedToMissionEventHandler(string personaId, string missionId);

    private SubViewportContainer? _compagnieContainer;
    private SubViewport? _compagnieSubViewport;
    private Node3D? _compagnieRoot3D;
    private Camera3D? _compagnieCamera3D;
    private Node3D? _personasContainer;
    private Control? _compagnieHandle;

    private Label? _missionHeader;
    private Label? _missionHook;
    private Label? _missionEligible;
    private Control? _dropSlot;
    private Button? _affectButton;
    private Button? _declineButton;
    private Label? _missionToast;
    private Label? _queueStatusLabel;
    private Label? _tickLabel;

    private CanvasLayer? _dragProxyLayer;
    private ColorRect? _dragProxyRect;

    private Label? _statusLabel;
    private Label? _liveLogLabel;

    // Engine-bound autoloads.
    private GameState? _gameState;
    private ApiClient? _apiClient;
    private WorldSimTick? _worldSimTick;

    // Slide state for the Compagnie panel — Compagnie's slide machine
    // is reused via CompagniePanelLogic (Pattern P2).
    private SlideState _compagnieSlideState = SlideState.Closed;
    private SlideState? _compagnieInFlightTarget;
    private Tween? _compagnieSlideTween;

    // Persona registry — Node3D + Label3D + Area3D per id.
    private readonly Dictionary<string, PersonaVisual> _personaVisuals = new();
    private readonly HashSet<string> _personasInMission = new();

    // Drag state — only one drag at a time (no multi-touch in M1).
    private string? _dragPersonaId;

    // Live log buffer.
    private readonly Queue<string> _liveLog = new();

    // Mission-panel state (mirrors MissionPanelProbe.cs trimmed).
    private string? _selectedPersonaId; // for the Affect button backup path (5c)
    private int _currentTick;

    public override void _EnterTree()
    {
        GD.Print("***** [M1 SLICE] _EnterTree -- composite Compagnie+Mission scene loading *****");
    }

    public override void _Ready()
    {
        GD.Print("***** [M1 SLICE] _Ready ENTERED -- starting boot sequence *****");

        ResolveBindings();
        EnsureCompagnieWorld3D();
        BuildCompagnieSubSpaceContents();
        ApplyCompagnieContainerPosition();
        WireCompagnieHandle();
        WireMissionPanel();
        WireWorldSimTick();
        SeedCompany();
        RunPreflight();
        UpdateStatusLabel();
        RenderEmptyMissionPanel();

        Log("M1 slice ready : click HANDLE to open Compagnie ; press-drag a persona, release over the Mission slot.");
    }

    public override void _ExitTree()
    {
        if (_compagnieHandle is not null)
            _compagnieHandle.GuiInput -= OnHandleGuiInput;
        if (_worldSimTick is not null)
        {
            _worldSimTick.MissionEmerged -= OnMissionEmerged;
            _worldSimTick.TickAdvanced -= OnTickAdvanced;
        }
        if (_affectButton is not null) _affectButton.Pressed -= OnAffectPressed;
        if (_declineButton is not null) _declineButton.Pressed -= OnDeclinePressed;

        foreach (var pv in _personaVisuals.Values)
        {
            if (pv.Area3D is not null && pv.AreaInputHandler is not null)
                pv.Area3D.InputEvent -= pv.AreaInputHandler;
        }
    }

    // ====================================================================
    // Binding resolution + canary preflight (Pattern J, M1 slice flavour)
    // ====================================================================

    private void ResolveBindings()
    {
        _compagnieContainer = GetNodeOrNull<SubViewportContainer>(CompagnieContainerPath);
        _compagnieSubViewport = GetNodeOrNull<SubViewport>(CompagnieSubViewportPath);
        _compagnieRoot3D = GetNodeOrNull<Node3D>(CompagnieSubSpaceRoot3DPath);
        _compagnieCamera3D = GetNodeOrNull<Camera3D>(CompagnieCamera3DPath);
        _personasContainer = GetNodeOrNull<Node3D>(CompagniePersonasContainerPath);
        _compagnieHandle = GetNodeOrNull<Control>(CompagnieHandlePath);

        _missionHeader = GetNodeOrNull<Label>(MissionHeaderLabelPath);
        _missionHook = GetNodeOrNull<Label>(MissionHookLabelPath);
        _missionEligible = GetNodeOrNull<Label>(MissionEligibleLabelPath);
        _dropSlot = GetNodeOrNull<Control>(MissionDropSlotPath);
        _affectButton = GetNodeOrNull<Button>(MissionAffectButtonPath);
        _declineButton = GetNodeOrNull<Button>(MissionDeclineButtonPath);
        _missionToast = GetNodeOrNull<Label>(MissionToastLabelPath);
        _queueStatusLabel = GetNodeOrNull<Label>(MissionQueueStatusLabelPath);
        _tickLabel = GetNodeOrNull<Label>(MissionTickLabelPath);

        _dragProxyLayer = GetNodeOrNull<CanvasLayer>(DragProxyLayerPath);
        _dragProxyRect = GetNodeOrNull<ColorRect>(DragProxyRectPath);

        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _liveLogLabel = GetNodeOrNull<Label>(LiveLogLabelPath);

        _gameState = GetTree()?.Root?.GetNodeOrNull<GameState>("GameState");
        _apiClient = GetTree()?.Root?.GetNodeOrNull<ApiClient>("ApiClient");
        _worldSimTick = GetTree()?.Root?.GetNodeOrNull<WorldSimTick>("WorldSimTick");
    }

    /// <summary>
    /// Apply the project.godot rendering-trap memory : preflight
    /// SubViewport invariants explicitly. Round-2 lessons from the
    /// CompagniePanelProbe codified here : OwnWorld3D + World3D
    /// non-null assignment, PhysicsObjectPicking, RenderTargetUpdateMode,
    /// Container child-0 = SubViewport.
    /// </summary>
    private void RunPreflight()
    {
        var broken = new List<string>();
        if (_compagnieContainer is null) broken.Add("CompagnieContainer");
        if (_compagnieSubViewport is null) broken.Add("CompagnieSubViewport");
        if (_compagnieRoot3D is null) broken.Add("CompagnieRoot3D");
        if (_compagnieCamera3D is null) broken.Add("CompagnieCamera3D");
        if (_personasContainer is null) broken.Add("PersonasContainer");
        if (_compagnieHandle is null) broken.Add("CompagnieHandle");
        if (_missionHeader is null) broken.Add("MissionHeader");
        if (_dropSlot is null) broken.Add("DropSlot");
        if (_affectButton is null) broken.Add("AffectButton");
        if (_declineButton is null) broken.Add("DeclineButton");
        if (_dragProxyLayer is null) broken.Add("DragProxyLayer");
        if (_dragProxyRect is null) broken.Add("DragProxyRect");
        if (_gameState is null) broken.Add("GameState autoload");
        if (_apiClient is null) broken.Add("ApiClient autoload");
        if (_worldSimTick is null) broken.Add("WorldSimTick autoload");

        if (broken.Count > 0)
        {
            GD.PushError($"[M1 SLICE PREFLIGHT FAIL] {broken.Count} broken binding(s) : {string.Join(" ; ", broken)}");
        }
        else
        {
            GD.Print("[M1 SLICE PREFLIGHT OK] all 15 bindings resolved");
        }

        if (_compagnieSubViewport is not null)
        {
            if (!_compagnieSubViewport.PhysicsObjectPicking)
            {
                _compagnieSubViewport.PhysicsObjectPicking = true;
                GD.PushWarning("[M1 SLICE] forced SubViewport.PhysicsObjectPicking=true");
            }
            GD.Print($"[M1 SLICE SUBVIEWPORT] OwnWorld3D={_compagnieSubViewport.OwnWorld3D} World3D={(_compagnieSubViewport.World3D is null ? "null" : "non-null")} Size={_compagnieSubViewport.Size} UpdateMode={_compagnieSubViewport.RenderTargetUpdateMode}");
        }

        if (_compagnieCamera3D is not null)
        {
            _compagnieCamera3D.MakeCurrent();
            _compagnieCamera3D.LookAt(new Vector3(0, 0, 0), Vector3.Up);
            GD.Print($"[M1 SLICE CAMERA] current={_compagnieCamera3D.Current} pos={_compagnieCamera3D.GlobalPosition} size={_compagnieCamera3D.Size}");
        }

        if (_dropSlot is not null)
        {
            GD.Print($"[M1 SLICE DROPSLOT] globalRect={_dropSlot.GetGlobalRect()}");
        }
    }

    /// <summary>
    /// Godot 4.6 quirk : own_world_3d=true in the .tscn does NOT
    /// lazy-init the World3D. Same defensive assignment as in
    /// CompagniePanelProbe (commit 0c028d1, J5+).
    /// </summary>
    private void EnsureCompagnieWorld3D()
    {
        if (_compagnieSubViewport is not null
            && _compagnieSubViewport.OwnWorld3D
            && _compagnieSubViewport.World3D is null)
        {
            _compagnieSubViewport.World3D = new World3D();
            GD.Print("[M1 SLICE] World3D was null despite OwnWorld3D=true -- assigned a fresh World3D (Godot 4.6 quirk)");
        }
    }

    // ====================================================================
    // Compagnie SubViewport build-out (Pattern P1)
    // ====================================================================

    private void BuildCompagnieSubSpaceContents()
    {
        if (_compagnieRoot3D is null || _personasContainer is null) return;

        // Backdrop placeholder — flat warm-earth quad on the iso ground plane.
        var backdrop = _compagnieRoot3D.GetNodeOrNull<MeshInstance3D>("TaverneBackdrop");
        if (backdrop is null)
        {
            backdrop = new MeshInstance3D
            {
                Name = "TaverneBackdrop",
                Position = new Vector3(0, -1f, 0),
                Mesh = new BoxMesh { Size = new Vector3(400f, 0.5f, 200f) },
            };
            backdrop.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.42f, 0.30f, 0.20f, 1f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            _compagnieRoot3D.AddChild(backdrop);
        }

        // 3 personas (Varn-aligned : hodge / mirelle / wattan).
        for (var i = 0; i < DefaultPersonas.Count; i++)
        {
            var p = DefaultPersonas[i];
            var pos = new Vector3(
                x: -PersonaSpacingWorldUnits + i * PersonaSpacingWorldUnits,
                y: 0f, z: 0f);
            BuildPersonaVisual(p, pos);
        }
    }

    private void BuildPersonaVisual(PersonaPlaceholder persona, Vector3 pos)
    {
        if (_personasContainer is null) return;

        var existing = _personasContainer.GetNodeOrNull<Node3D>(persona.Id);
        if (existing is not null)
        {
            _personaVisuals.Remove(persona.Id);
            existing.QueueFree();
        }

        var personaRoot = new Node3D { Name = persona.Id, Position = pos };

        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new BoxMesh { Size = new Vector3(48f, 64f, 48f) },
            Position = new Vector3(0, 32f, 0),
        };
        body.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = TintToColor(persona.Tint),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        personaRoot.AddChild(body);

        var label = new Label3D
        {
            Name = "Label",
            Text = $"{persona.DisplayName} -- {persona.ShortStatus}",
            Position = new Vector3(0, 96f, 0),
            Modulate = new Color(0.95f, 0.92f, 0.80f, 1f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 24,
            PixelSize = Label3DPixelSize,
            OutlineSize = 6,
            OutlineModulate = new Color(0, 0, 0, 1),
        };
        personaRoot.AddChild(label);

        var area = new Area3D
        {
            Name = "Area",
            InputRayPickable = true,
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        var shape = new CollisionShape3D
        {
            Name = "Shape",
            Shape = new BoxShape3D { Size = new Vector3(48f, 64f, 48f) },
            Position = new Vector3(0, 32f, 0),
        };
        area.AddChild(shape);
        personaRoot.AddChild(area);

        var capturedPersona = persona;
        Area3D.InputEventEventHandler handler = (camera, ev, position, normal, shapeIdx) =>
            OnPersonaInputEvent(capturedPersona, ev);
        area.InputEvent += handler;

        _personasContainer.AddChild(personaRoot);
        _personaVisuals[persona.Id] = new PersonaVisual(personaRoot, body, label, area, handler);
    }

    private static Color TintToColor(PersonaTint tint) => tint switch
    {
        PersonaTint.Olive => new Color(0.55f, 0.62f, 0.38f, 1f),
        PersonaTint.Cream => new Color(0.92f, 0.85f, 0.66f, 1f),
        PersonaTint.Rust => new Color(0.78f, 0.42f, 0.30f, 1f),
        _ => new Color(1, 1, 1, 1),
    };

    // ====================================================================
    // Compagnie slide state machine (Pattern P2)
    // ====================================================================

    private void ApplyCompagnieContainerPosition()
    {
        if (_compagnieContainer is null) return;
        var x = _compagnieSlideState switch
        {
            SlideState.Open => CompagnieOpenXPx,
            SlideState.Closed => CompagnieClosedXPx,
            _ => CompagnieClosedXPx,
        };
        _compagnieContainer.Position = new Vector2(x, _compagnieContainer.Position.Y);
    }

    private void WireCompagnieHandle()
    {
        if (_compagnieHandle is not null)
            _compagnieHandle.GuiInput += OnHandleGuiInput;
    }

    private void OnHandleGuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            DispatchCompagnieIntent(SlideIntent.ClickHandle);
        }
    }

    private void DispatchCompagnieIntent(SlideIntent intent)
    {
        var d = DecideNextSlideState(_compagnieSlideState, intent, _compagnieInFlightTarget);
        Log($"compagnie slide intent={intent} : {_compagnieSlideState} -> {d.NextState} (target={d.SlideTarget?.ToString() ?? "n/a"})");
        _compagnieSlideState = d.NextState;

        if (d.NextState == SlideState.Sliding && d.SlideTarget is { } target)
        {
            if (_compagnieInFlightTarget != target)
            {
                _compagnieInFlightTarget = target;
                StartCompagnieSlideTween(target);
            }
        }
        else
        {
            _compagnieInFlightTarget = null;
        }

        UpdateStatusLabel();
    }

    private void StartCompagnieSlideTween(SlideState target)
    {
        if (_compagnieContainer is null) return;
        if (_compagnieSlideTween is not null && _compagnieSlideTween.IsValid())
            _compagnieSlideTween.Kill();

        var targetX = target == SlideState.Open ? CompagnieOpenXPx : CompagnieClosedXPx;
        _compagnieSlideTween = CreateTween();
        _compagnieSlideTween.TweenProperty(
                _compagnieContainer,
                "position",
                new Vector2(targetX, _compagnieContainer.Position.Y),
                CompagnieSlideDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _compagnieSlideTween.Finished += () => DispatchCompagnieIntent(SlideIntent.TweenFinished);
    }

    // ====================================================================
    // Cross-panel drag-drop (Pattern P5)
    // ====================================================================

    private void OnPersonaInputEvent(PersonaPlaceholder persona, InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb) return;

        if (mb.ButtonIndex != MouseButton.Left) return;

        if (mb.Pressed)
        {
            // Press : start a drag if persona is not already in mission.
            if (_personasInMission.Contains(persona.Id))
            {
                Log($"persona {persona.Id} already in mission — drag refused (v1, no recall)");
                return;
            }
            BeginDrag(persona);
        }
        // Release inside the SubViewport (i.e. NOT over the Mission slot)
        // is handled by _UnhandledInput's release branch — that single
        // path covers "release anywhere on screen". Defensive : no-op
        // here on release to avoid double-fire.
    }

    private void BeginDrag(PersonaPlaceholder persona)
    {
        if (_dragProxyRect is null) return;

        _dragPersonaId = persona.Id;
        _dragProxyRect.Visible = true;
        UpdateProxyToCursor();

        Log($"[DRAG START] persona={persona.Id} (Pattern P5 cross-panel drag)");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_dragPersonaId is null) return;

        if (@event is InputEventMouseMotion)
        {
            UpdateProxyToCursor();
        }
        else if (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            ResolveDrop();
        }
    }

    private void UpdateProxyToCursor()
    {
        if (_dragProxyRect is null) return;
        var cursor = GetViewport().GetMousePosition();
        // Centre the proxy rect on the cursor.
        var rectSize = _dragProxyRect.Size;
        _dragProxyRect.Position = new Vector2(cursor.X - rectSize.X * 0.5f, cursor.Y - rectSize.Y * 0.5f);
    }

    private void ResolveDrop()
    {
        if (_dragPersonaId is null) return;
        var personaId = _dragPersonaId;
        _dragPersonaId = null;
        if (_dragProxyRect is not null) _dragProxyRect.Visible = false;

        var cursor = GetViewport().GetMousePosition();
        var slotRect = _dropSlot is not null ? _dropSlot.GetGlobalRect() : new Rect2();

        var pkaCursor = new M1SliceLogic.Point(cursor.X, cursor.Y);
        var pkaSlot = new M1SliceLogic.Rect(slotRect.Position.X, slotRect.Position.Y, slotRect.Size.X, slotRect.Size.Y);
        var overSlot = IsCursorOverSlot(pkaCursor, pkaSlot);

        EmergentMissionDto? mission = null;
        if (_gameState is not null && _gameState.PendingMissions.Count > 0)
            mission = _gameState.PendingMissions[0];

        var alreadyAssigned = _personasInMission.Contains(personaId);
        var inEligible = mission is not null && IsPersonaInEligibleList(personaId, mission.EligiblePersonas);

        var decision = DecideDropOutcome(
            cursorOverMissionSlot: overSlot,
            missionPresent: mission is not null,
            personaAlreadyAssigned: alreadyAssigned,
            personaInEligibleList: inEligible);

        Log($"[DROP] persona={personaId} cursor={cursor} overSlot={overSlot} mission={(mission?.Id ?? "null")} -> {decision}");

        if (decision == DropDecision.Accepted && mission is not null)
        {
            // Emit the cross-panel signal and run the resolve pipeline.
            // Build the assignment event up-front for the contract-shape
            // pin (xUnit covers BuildAssignmentEvent's defensive throws).
            var ev = BuildAssignmentEvent(personaId, mission.Id);
            EmitSignal(SignalName.PersonaAssignedToMission, ev.PersonaId, ev.MissionId);
            Log($"[EVENT {PersonaAssignedToMissionEventName}] persona={ev.PersonaId} mission={ev.MissionId}");

            // Flip the persona's [mission] indicator (NPC autonomy : the
            // persona Node3D is NOT re-parented).
            FlipPersonaToInMission(personaId);

            // Fire the resolve pipeline — same path as Mission panel's
            // OnAffectPressed.
            _ = ResolveAcceptedAssignment(mission, personaId);
        }
        else if (decision != DropDecision.Accepted)
        {
            // Cancelled : a v2 nicety would tween the proxy back to the
            // persona's position. v1 just hides it instantly.
            Log($"[DROP CANCELLED] reason={decision}");
            if (_missionToast is not null)
                _missionToast.Text = $"[DROP CANCELLED] {decision}";
        }
    }

    private void FlipPersonaToInMission(string personaId)
    {
        if (!_personaVisuals.TryGetValue(personaId, out var pv)) return;
        _personasInMission.Add(personaId);

        var p = FindPersonaPlaceholder(personaId);
        var displayName = p?.DisplayName ?? personaId;
        var status = p?.ShortStatus ?? "";
        pv.Label.Text = $"{displayName} -- {status} [mission]";
        pv.Label.Modulate = new Color(1.0f, 0.85f, 0.30f, 1f);

        // NPC autonomy live canary.
        if (pv.Root.GetParent() != _personasContainer)
        {
            GD.PushError($"[CANARY FAIL] persona {personaId} got re-parented during cross-panel drag-drop -- NPC autonomy invariant violated");
        }
    }

    private static PersonaPlaceholder? FindPersonaPlaceholder(string personaId)
    {
        for (var i = 0; i < DefaultPersonas.Count; i++)
        {
            if (DefaultPersonas[i].Id == personaId) return DefaultPersonas[i];
        }
        return null;
    }

    /// <summary>
    /// Run the same Affect-equivalent resolve flow as
    /// <see cref="MissionPanelLogic.BuildAffectRequest"/> + ApiClient.ResolveMissionAsync.
    /// Async fire-and-forget at the caller site.
    /// </summary>
    private async Task ResolveAcceptedAssignment(EmergentMissionDto mission, string personaId)
    {
        if (_apiClient is null || _gameState is null) return;

        var request = MissionPanelLogic.BuildAffectRequest(mission, personaId, _currentTick);
        var result = await _apiClient.ResolveMissionAsync(request);

        switch (result)
        {
            case Result<MissionResolveResponseDto, ApiError>.Success ok:
                ApplyResolutionSuccess(ok.Value, mission, isAffect: true);
                break;
            case Result<MissionResolveResponseDto, ApiError>.Failure fail:
                ApplyResolutionFailure(fail.Error);
                break;
        }
    }

    // ====================================================================
    // Mission panel wiring (mirror of MissionPanelProbe trimmed)
    // ====================================================================

    private void WireMissionPanel()
    {
        if (_affectButton is not null) _affectButton.Pressed += OnAffectPressed;
        if (_declineButton is not null) _declineButton.Pressed += OnDeclinePressed;
    }

    private void WireWorldSimTick()
    {
        if (_worldSimTick is not null)
        {
            _worldSimTick.MissionEmerged += OnMissionEmerged;
            _worldSimTick.TickAdvanced += OnTickAdvanced;
        }
    }

    private void SeedCompany()
    {
        if (_gameState is null) return;
        if (_gameState.CompanyPersonas.Count > 0) return;

        _gameState.CompanyPersonas.Add(new CharacterStateDto
        {
            Name = "hodge",
            CharClass = "scout",
            Faction = "Wayfinders company",
            StrBucket = DescriptorBucketValues.Mid,
            DexBucket = DescriptorBucketValues.Mid,
            WisBucket = DescriptorBucketValues.Low,
            HpBucket = HpBucketValues.Unhurt,
            StressBucket = StressBucketValues.Composed,
        });
        _gameState.CompanyPersonas.Add(new CharacterStateDto
        {
            Name = "mirelle",
            CharClass = "diplomat",
            Faction = "Wayfinders company",
            StrBucket = DescriptorBucketValues.Low,
            DexBucket = DescriptorBucketValues.High,
            WisBucket = DescriptorBucketValues.High,
            HpBucket = HpBucketValues.Unhurt,
            StressBucket = StressBucketValues.Composed,
        });
        _gameState.CompanyPersonas.Add(new CharacterStateDto
        {
            Name = "wattan",
            CharClass = "warden",
            Faction = "Wayfinders company",
            StrBucket = DescriptorBucketValues.High,
            DexBucket = DescriptorBucketValues.VeryLow,
            WisBucket = DescriptorBucketValues.Mid,
            HpBucket = HpBucketValues.Scratched,
            StressBucket = StressBucketValues.Tense,
        });

        Log("seeded 3 personas : hodge / mirelle / wattan");
    }

    private void OnTickAdvanced(int tick)
    {
        _currentTick = tick;
        if (_tickLabel is not null) _tickLabel.Text = $"tick: {tick}";
    }

    private void OnMissionEmerged(string missionId)
    {
        Log($"[MISSION EMERGED] id={missionId}");
        // Pattern P6 lock : Mission new content does NOT trigger
        // Compagnie auto-rabat (xUnit-pinned).
        if (ShouldDispatchAutoRabatOnMissionEvent(MissionPanelEvent.NewMissionRendered))
        {
            // Defensive : if a future contract change flips P6 to true,
            // the runtime path is here for free. M1 always skips.
            DispatchCompagnieIntent(SlideIntent.AnotherLayerOpened);
        }
        RenderHeadMission();
    }

    private void RenderHeadMission()
    {
        if (_gameState is null) return;
        if (_gameState.PendingMissions.Count == 0)
        {
            RenderEmptyMissionPanel();
            return;
        }

        var mission = _gameState.PendingMissions[0];
        _selectedPersonaId = null;

        if (_missionHeader is not null) _missionHeader.Text = MissionPanelLogic.FormatMissionHeader(mission);
        if (_missionHook is not null) _missionHook.Text = mission.NarrativeHook;
        if (_missionEligible is not null) _missionEligible.Text = MissionPanelLogic.FormatEligiblePersonas(mission);

        UpdateMissionButtons(mission);
        UpdateQueueStatus();
    }

    private void RenderEmptyMissionPanel()
    {
        _selectedPersonaId = null;
        if (_missionHeader is not null) _missionHeader.Text = "[MISSION] (none — waiting for tick cadence)";
        if (_missionHook is not null) _missionHook.Text = "";
        if (_missionEligible is not null) _missionEligible.Text = "";
        UpdateMissionButtons(mission: null);
        UpdateQueueStatus();
    }

    private void UpdateMissionButtons(EmergentMissionDto? mission)
    {
        if (_affectButton is not null)
        {
            // Affect button is a 5c backup path — for 5ab we keep it
            // visible-but-disabled-without-selection (no persona button
            // list yet ; selection happens via drag-drop). It enables
            // when a persona has dropped onto the slot, which sets
            // _selectedPersonaId implicitly via the cross-panel signal.
            var affectState = MissionPanelLogic.DecideAffectButtonState(mission, _selectedPersonaId);
            _affectButton.Visible = affectState != MissionPanelLogic.AffectButtonState.Hidden;
            _affectButton.Disabled = affectState != MissionPanelLogic.AffectButtonState.Enabled;
        }
        if (_declineButton is not null)
        {
            var declineState = MissionPanelLogic.DecideDeclineButtonState(mission);
            _declineButton.Visible = declineState != MissionPanelLogic.DeclineButtonState.Hidden;
        }
    }

    private void UpdateQueueStatus()
    {
        if (_queueStatusLabel is null || _gameState is null) return;
        _queueStatusLabel.Text =
            $"queue: {_gameState.PendingMissions.Count} pending mission(s) | "
            + $"legacy tags: {TotalLegacyTagCount(_gameState)}";
    }

    private static int TotalLegacyTagCount(GameState gs)
    {
        var n = 0;
        foreach (var kv in gs.PersonaLegacy) n += kv.Value.Count;
        return n;
    }

    private async void OnAffectPressed()
    {
        if (_gameState is null || _apiClient is null) return;
        if (_gameState.PendingMissions.Count == 0) return;
        if (string.IsNullOrEmpty(_selectedPersonaId)) return;

        var mission = _gameState.PendingMissions[0];
        var request = MissionPanelLogic.BuildAffectRequest(mission, _selectedPersonaId, _currentTick);
        await ResolveAndApply(request, mission, isAffect: true);
    }

    private async void OnDeclinePressed()
    {
        if (_gameState is null || _apiClient is null) return;
        if (_gameState.PendingMissions.Count == 0) return;

        var mission = _gameState.PendingMissions[0];
        var request = MissionPanelLogic.BuildDeclineRequest(mission, _currentTick);
        await ResolveAndApply(request, mission, isAffect: false);
    }

    private async Task ResolveAndApply(
        MissionResolveRequestDto request,
        EmergentMissionDto mission,
        bool isAffect)
    {
        if (_apiClient is null || _gameState is null) return;

        Log($"[RESOLVE] mission={request.MissionId} outcome={request.Outcome} assigned=[{string.Join(",", request.AssignedPersonas)}]");
        var result = await _apiClient.ResolveMissionAsync(request);

        switch (result)
        {
            case Result<MissionResolveResponseDto, ApiError>.Success ok:
                ApplyResolutionSuccess(ok.Value, mission, isAffect);
                break;
            case Result<MissionResolveResponseDto, ApiError>.Failure fail:
                ApplyResolutionFailure(fail.Error);
                break;
        }
    }

    private void ApplyResolutionSuccess(
        MissionResolveResponseDto response,
        EmergentMissionDto mission,
        bool isAffect)
    {
        if (_gameState is null) return;

        for (var i = 0; i < response.TagsCreated.Count; i++)
        {
            var tag = response.TagsCreated[i];
            _gameState.AppendPersonaLegacyTag(tag);
            Log($"[LEGACY TAG] persona={tag.PersonaId} mission={tag.MissionId} type={tag.MissionType} outcome={tag.Outcome}");
        }

        _gameState.PendingMissions.RemoveAt(0);

        if (_missionToast is not null)
        {
            var verb = isAffect ? "AFFECTED" : "DECLINED";
            _missionToast.Text = $"[{verb}] {MissionPanelLogic.FormatLegacyTagToast(response.TagsCreated)}";
        }

        RenderHeadMission();
    }

    private void ApplyResolutionFailure(ApiError error)
    {
        var msg = error switch
        {
            ApiError.NotReachable nr => $"NOT REACHABLE: {nr.Reason}",
            ApiError.ServerError se => $"SERVER {se.StatusCode}: {se.Body}",
            ApiError.DeserializationError de => $"PARSE: {de.Reason}",
            ApiError.Cancelled => "CANCELLED",
            _ => "(unknown)"
        };
        GD.PushWarning($"[M1 SLICE RESOLVE FAILED] {msg}");
        if (_missionToast is not null) _missionToast.Text = $"[ERROR] {msg}";
    }

    // ====================================================================
    // Status overlay + log
    // ====================================================================

    private void UpdateStatusLabel()
    {
        if (_statusLabel is null) return;
        _statusLabel.Text =
            $"COMPAGNIE: {_compagnieSlideState}"
            + (_compagnieInFlightTarget is { } t ? $" (-> {t})" : "")
            + $"\nMISSION: fixed (Pattern P6)"
            + $"\nin mission: [{string.Join(", ", _personasInMission)}]";
    }

    private void Log(string message)
    {
        var line = $"[M1Slice] {message}";
        GD.Print(line);

        _liveLog.Enqueue(line);
        while (_liveLog.Count > LiveLogMaxLines) _liveLog.Dequeue();

        if (_liveLogLabel is not null)
            _liveLogLabel.Text = string.Join("\n", _liveLog);
    }

    private sealed record PersonaVisual(
        Node3D Root,
        MeshInstance3D Body,
        Label3D Label,
        Area3D Area3D,
        Area3D.InputEventEventHandler AreaInputHandler);
}
