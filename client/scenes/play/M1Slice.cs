using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Poi;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
using static Wayfinders.Client.Scripts.Screens.CompagniePanelLogic;
using static Wayfinders.Client.Scripts.Screens.M1SliceLogic;

namespace Wayfinders.Client.Scenes.Play;

/// <summary>
/// M1 slice 5ab+5c (2026-05-10) — first composite playable scene
/// where the Compagnie panel and the Mission panel coexist in
/// one runtime tree. This is the seed of the M1 ship-state UI :
/// a sliding Compagnie sub-iso panel on the LEFT, a fixed
/// text-only Mission panel on the RIGHT, with cross-panel
/// drag-drop wired through the new
/// <see cref="M1SliceLogic"/> contract, the resolve round-trip
/// to <c>POST /api/world/mission/resolve</c>, and the legacy-tag
/// storage pipeline back into <c>GameState.PersonaLegacy</c>.
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
///         is hovering it (5c addition).</item>
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
///         GetGlobalRect() is the hit-test target. <b>5c addition :</b>
///         when the cursor enters/exits the slot rect the
///         StyleBoxFlat border colour pulses brighter (hover
///         affordance), reset on drop or drag-cancel.</item>
///   <item>On LMB release, <see cref="M1SliceLogic.DecideDropOutcome"/>
///         classifies the drop. <b>Accepted</b> : emit
///         <see cref="PersonaAssignedToMissionEventHandler"/>
///         signal + flip the persona's [mission] indicator + the
///         resolve pipeline runs (POST /api/world/mission/resolve,
///         tags appended to <c>GameState.PersonaLegacy</c>, head
///         mission popped from <c>PendingMissions</c>,
///         <see cref="MissionResolvedEventHandler"/> emitted).
///         <b>Cancelled</b> : kick off a 300ms snap-back tween
///         that animates the proxy back to the persona's
///         screen-projected position before hiding it (5c
///         addition ; v1 used to hide instantly).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>5c (2026-05-10) — Affect button backup path.</b> Didier
/// validated 2026-05-10 that the click-to-dispatch button list
/// stays available alongside the drag-drop, for keyboard-first
/// ergonomics. <see cref="RenderHeadMission"/> dynamically builds
/// one Button per eligible persona (under
/// <see cref="MissionPanelLogic.FormatEligiblePersonas"/>). Click
/// selects, click again on a different persona switches, then
/// "Affecter" runs the same resolve pipeline as the drag-drop
/// release. Symmetric outcome : both paths POST the same
/// <see cref="MissionResolveRequestDto"/>, both store tags via
/// <see cref="M1SliceLogic.AppendTagToLegacyMap"/>, both pop the
/// head via <see cref="M1SliceLogic.PopHeadMission"/>.
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

    /// <summary>5c — duration of the snap-back tween that returns
    /// a cancelled-drop proxy to the persona's icon position before
    /// hiding. 300ms ease-out matches Didier's spec (2026-05-10).</summary>
    [Export] public float SnapBackDurationSec { get; set; } = 0.3f;

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
    /// <summary>5c — anchor for the dynamic eligible-persona button list
    /// (clavier-first backup path). The runtime adds children Buttons
    /// under this Container during RenderHeadMission and frees them
    /// in RenderEmptyMissionPanel. Defaults to a sibling VBox above
    /// the Affect button ; if missing in the .tscn, the runtime falls
    /// back to placing the buttons under the Mission Panel root with
    /// hard-coded Y offsets.</summary>
    [Export] public NodePath MissionPersonaButtonsContainerPath { get; set; } = "MissionRoot/Panel/PersonaButtons";

    // ---- Drag-drop proxy CanvasLayer.
    [Export] public NodePath DragProxyLayerPath { get; set; } = "DragProxyLayer";
    [Export] public NodePath DragProxyRectPath { get; set; } = "DragProxyLayer/ProxyRect";

    // ---- Status / log labels.
    [Export] public NodePath StatusLabelPath { get; set; } = "OverlayUI/StatusLabel";
    [Export] public NodePath LiveLogLabelPath { get; set; } = "OverlayUI/LiveLogLabel";

    // ---- PR6 — POI integration pattern wiring.
    /// <summary>
    /// PR6 — designer-authored list of POI to spawn into this scene
    /// at <c>_Ready</c>. Each entry pairs a <see cref="PoiData"/> (or a
    /// sidecar PNG path that <see cref="PoiSidecarLoader"/> resolves) with
    /// the iso tile coord. See <c>client/data/POI_INTEGRATION.md</c> for
    /// the "add a POI in 5 steps" walkthrough. The array is hot-editable
    /// from the Godot inspector — no code change to add Halfgate at a
    /// new tile or to plug a future POI when its asset lands.
    /// </summary>
    [Export] public Godot.Collections.Array<PoiSpawnEntry> PoisToSpawn { get; set; } = new();

    /// <summary>NodePath of the <see cref="PoiSpawner"/> child added
    /// to this scene's .tscn. Kept exported so a future probe / variant
    /// can swap to a stub spawner without recompiling.</summary>
    [Export] public NodePath PoiSpawnerPath { get; set; } = "PoiSpawner";

    /// <summary>
    /// PR6 — when the POI input router emits <c>PoiClicked</c> for one of
    /// the spawned POIs, navigate to this screen id via
    /// <see cref="SceneManager.NavigateTo"/>. Defaults to
    /// <c>E3_CITY_HALFGATE</c> (the existing E2WorldMap dispatch target).
    /// Empty string disables navigation — the click is only logged.
    /// MVP only : a future PR replaces this single-target field with a
    /// per-POI dispatch table keyed by <c>DisplayName</c>.
    /// </summary>
    [Export] public string PoiClickNavigateTo { get; set; } = "E3_CITY_HALFGATE";

    private const float PersonaSpacingWorldUnits = 96f;
    private const float Label3DPixelSize = 0.5f;
    private const int LiveLogMaxLines = 8;

    /// <summary>Cross-panel drag-drop signal — the Mission panel
    /// pipeline subscribes to this and routes through its existing
    /// resolve flow. Production-shaped : same pattern as
    /// CompagniePanelLogic.AssignToMissionEventName, scoped to
    /// the M1 slice.</summary>
    [Signal] public delegate void PersonaAssignedToMissionEventHandler(string personaId, string missionId);

    /// <summary>5c — emitted after a successful resolve round-trip
    /// has stored its tags into <c>GameState.PersonaLegacy</c> and
    /// the head mission has been popped from
    /// <c>GameState.PendingMissions</c>. Listeners (5d visualisation,
    /// future analytics, the Compagnie panel's veteran-flip glyph)
    /// subscribe by <see cref="M1SliceLogic.MissionResolvedEventName"/>
    /// to react. The <c>tagsCreatedCount</c> payload is enough to
    /// drive UI ; full tag content is read from
    /// <c>GameState.PersonaLegacy</c> by listeners that need it
    /// (avoids marshalling a List across the Godot/C# boundary in a
    /// signal — Godot signals don't support generic collections
    /// natively). Naming convention matches
    /// <see cref="PersonaAssignedToMissionEventHandler"/>.</summary>
    [Signal] public delegate void MissionResolvedEventHandler(string missionId, int tagsCreatedCount);

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
    private Container? _personaButtonsContainer;

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
    // 5c — drop slot hover state. True while the proxy is over the
    // slot's screen-rect during a drag. Drives the StyleBoxFlat
    // border-colour pulse on the drop slot Panel.
    private bool _dropSlotHovered;
    // 5c — the snap-back tween that returns a cancelled-drop proxy
    // to the persona's icon screen-projected position. Stored so a
    // new BeginDrag can Kill() any in-flight snap-back before
    // starting fresh.
    private Tween? _snapBackTween;
    // 5c — cached StyleBox references for the drop slot's normal /
    // hovered visuals. Lazily captured at WireMissionPanel time
    // because the StyleBoxFlat is allocated by the .tscn loader.
    private StyleBoxFlat? _dropSlotStyleNormal;
    private StyleBoxFlat? _dropSlotStyleHovered;

    // 5c — dynamic eligible-persona button list for the clavier-first
    // backup path. Rebuilt each RenderHeadMission ; freed each
    // RenderEmptyMissionPanel + _ExitTree. Each button's Pressed
    // handler is captured by-id (closure on the button's persona id)
    // and lives on the button itself ; freeing the button
    // disconnects automatically.
    private readonly List<Button> _personaButtons = new();

    // Live log buffer.
    private readonly Queue<string> _liveLog = new();

    // ---- PR6 — POI wiring runtime state.
    private PoiSpawner? _poiSpawner;
    private PoiInputRouter? _poiRouter;
    private readonly List<Poi> _spawnedPois = new();

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

        WirePoisAndRouter();

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
        if (_dropSlot is not null)
        {
            _dropSlot.MouseEntered -= OnDropSlotMouseEntered;
            _dropSlot.MouseExited -= OnDropSlotMouseExited;
        }
        // 5d — mirror the WireMissionPanel subscription. Disconnection
        // discipline matters here because M1Slice both emits and listens
        // on MissionResolved ; if a future scene reload didn't unwind
        // the subscription, the new instance's handler would still be
        // queued behind the freed instance's slot.
        MissionResolved -= OnMissionResolvedRefreshLabels;

        // 5c — kill any in-flight snap-back tween before the scene
        // tears down ; otherwise the tween's Finished callback can
        // fire after _ExitTree and touch a freed proxy node.
        if (_snapBackTween is not null && _snapBackTween.IsValid())
            _snapBackTween.Kill();

        ClearPersonaButtons();

        // PR6 — POI cleanup. Router subscription unwinds first (disconnection
        // discipline ; the router is an autoload that outlives the scene).
        // The spawned POI nodes are freed by the scene tree teardown, but we
        // clear the local list to drop our refs and let GC reclaim the data.
        if (_poiRouter is not null)
        {
            _poiRouter.PoiClicked -= OnPoiClicked;
        }
        _spawnedPois.Clear();

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
        _personaButtonsContainer = GetNodeOrNull<Container>(MissionPersonaButtonsContainerPath);

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
            // M2-advance step 2b — busy check reads canonical
            // GameState.ActiveMissions via MissionDurationLogic.IsPersonaBusy.
            // The transient _personasInMission set is kept for the
            // visual flip (FlipPersonaToInMission below) but the gate
            // is now the data-driven check : if the persona is bound
            // to any active mission, the drag is refused. Spec
            // 2026-05-10 : "pas d'erreur visuelle agressive, juste
            // pas de proxy spawn".
            var activeMissions = _gameState is not null
                ? (System.Collections.Generic.IReadOnlyList<ActiveMissionDto>)_gameState.ActiveMissions
                : System.Array.Empty<ActiveMissionDto>();
            if (MissionDurationLogic.IsPersonaBusy(persona.Id, activeMissions))
            {
                // Find the bound mission for the diagnostic log.
                string? boundMission = null;
                for (var i = 0; i < activeMissions.Count && boundMission is null; i++)
                {
                    var m = activeMissions[i];
                    for (var j = 0; j < m.AssignedPersonas.Count; j++)
                    {
                        if (m.AssignedPersonas[j] == persona.Id)
                        {
                            boundMission = m.Mission.Id;
                            break;
                        }
                    }
                }
                Log($"[DRAG BLOCKED] persona={persona.Id} is in mission={boundMission ?? "(unknown)"}");
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

        // 5c — if a snap-back tween from a previous cancelled drop is
        // still in flight, kill it so the proxy can be repositioned
        // immediately for this new drag. Same defensive Kill() pattern
        // as the slide tween.
        if (_snapBackTween is not null && _snapBackTween.IsValid())
            _snapBackTween.Kill();

        _dragPersonaId = persona.Id;
        _dragProxyRect.Visible = true;
        _dropSlotHovered = false;
        ApplyDropSlotVisualState();
        UpdateProxyToCursor();

        Log($"[DRAG START] persona={persona.Id} (Pattern P5 cross-panel drag)");
    }

    /// <summary>
    /// Drag tracking lives in <c>_Input</c>, NOT <c>_UnhandledInput</c>.
    /// Reason : Controls with <c>mouse_filter = Stop</c> (the
    /// CompagnieRoot SubViewportContainer at line 92 of the .tscn,
    /// the MissionRoot Panel at line 155, the Compagnie Handle at
    /// line 121, and every default-Stop Label in OverlayUI) consume
    /// <c>InputEventMouseMotion</c> before <c>_UnhandledInput</c>
    /// fires. With <c>_UnhandledInput</c>, the golden proxy froze
    /// the instant the cursor crossed into Mission Panel territory
    /// because every subsequent motion event was eaten by the panel
    /// frame's mouse filter. <c>_Input</c> runs <i>before</i> GUI
    /// hit-testing and is unaffected by Control mouse_filter — the
    /// canonical Godot pattern for "this scene owns the gesture
    /// during an active drag" (cf. trap memory §5 mutual
    /// ergonomic gestures).
    ///
    /// <para>
    /// Defensive : we deliberately do NOT call
    /// <c>SetInputAsHandled</c> on motion events — other systems
    /// (the OverlayUI live log, future hover affordances) may want
    /// to react to the same motion. We only own the
    /// <i>response</i>, not the <i>event</i>. The button-release
    /// branch could theoretically be marked handled, but we leave
    /// it loose for the same reason ; the <c>_dragPersonaId</c>
    /// gate already makes this method a no-op when no drag is in
    /// flight.
    /// </para>
    /// </summary>
    public override void _Input(InputEvent @event)
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
        var newPos = new Vector2(cursor.X - rectSize.X * 0.5f, cursor.Y - rectSize.Y * 0.5f);
        _dragProxyRect.Position = newPos;

        // 5c — recompute drop-slot hover state every motion event
        // (cheap rect math). The MouseEntered / MouseExited signals
        // on the DropSlot do NOT fire while a proxy is being dragged
        // because the proxy lives on layer=20 (above the Mission
        // panel) and the cursor "appears" to never leave it. We
        // therefore drive hover from the same _Input motion path
        // that drives the proxy.
        if (_dropSlot is not null)
        {
            var slotRect = _dropSlot.GetGlobalRect();
            var nowHovered = slotRect.HasPoint(cursor);
            if (nowHovered != _dropSlotHovered)
            {
                _dropSlotHovered = nowHovered;
                ApplyDropSlotVisualState();
            }
        }

        // [DRAG] canary dump : if the proxy ever freezes again,
        // watch this in the editor Output panel — a freeze means
        // motion events stopped arriving (likely a new
        // mouse_filter=Stop Control was added in the cursor's
        // path). GD.Print is rate-throttled by Godot itself, so
        // tracing every motion is fine for diagnostic builds.
        // (Trap memory §2 : preflight + live canary > post-mortem.)
        var layerVal = _dragProxyLayer is not null ? _dragProxyLayer.Layer : -999;
        var visible = _dragProxyRect.Visible;
        GD.Print($"[DRAG TRACK] persona={_dragPersonaId} cursor=({cursor.X:F0},{cursor.Y:F0}) proxyPos=({newPos.X:F0},{newPos.Y:F0}) layer={layerVal} visible={visible} hovered={_dropSlotHovered}");
    }

    private void ResolveDrop()
    {
        if (_dragPersonaId is null) return;
        var personaId = _dragPersonaId;
        _dragPersonaId = null;

        var cursor = GetViewport().GetMousePosition();
        var slotRect = _dropSlot is not null ? _dropSlot.GetGlobalRect() : new Rect2();

        var pkaCursor = new M1SliceLogic.Point(cursor.X, cursor.Y);
        var pkaSlot = new M1SliceLogic.Rect(slotRect.Position.X, slotRect.Position.Y, slotRect.Size.X, slotRect.Size.Y);
        var overSlot = IsCursorOverSlot(pkaCursor, pkaSlot);

        EmergentMissionDto? mission = null;
        if (_gameState is not null && _gameState.PendingMissions.Count > 0)
            mission = _gameState.PendingMissions[0];

        // M2-advance step 2b — alreadyAssigned reads from
        // GameState.ActiveMissions (canonical) rather than the
        // transient _personasInMission set. Keeps the drop decision
        // symmetric with the OnPersonaInputEvent busy gate above.
        var activeForCheck = _gameState is not null
            ? (System.Collections.Generic.IReadOnlyList<ActiveMissionDto>)_gameState.ActiveMissions
            : System.Array.Empty<ActiveMissionDto>();
        var alreadyAssigned = MissionDurationLogic.IsPersonaBusy(personaId, activeForCheck);
        var inEligible = mission is not null && IsPersonaInEligibleList(personaId, mission.EligiblePersonas);

        var decision = DecideDropOutcome(
            cursorOverMissionSlot: overSlot,
            missionPresent: mission is not null,
            personaAlreadyAssigned: alreadyAssigned,
            personaInEligibleList: inEligible);

        Log($"[DROP] persona={personaId} cursor={cursor} overSlot={overSlot} mission={(mission?.Id ?? "null")} -> {decision}");

        // Reset hover state regardless of outcome — the drag is over.
        _dropSlotHovered = false;
        ApplyDropSlotVisualState();

        if (decision == DropDecision.Accepted && mission is not null)
        {
            // Hide the proxy immediately on accept — the drop landed,
            // no need to animate back to origin.
            if (_dragProxyRect is not null) _dragProxyRect.Visible = false;

            // Emit the cross-panel signal and run the resolve pipeline.
            // Build the assignment event up-front for the contract-shape
            // pin (xUnit covers BuildAssignmentEvent's defensive throws).
            var ev = BuildAssignmentEvent(personaId, mission.Id);
            EmitSignal(SignalName.PersonaAssignedToMission, ev.PersonaId, ev.MissionId);
            Log($"[EVENT {PersonaAssignedToMissionEventName}] persona={ev.PersonaId} mission={ev.MissionId}");

            // Flip the persona's [mission] indicator (NPC autonomy : the
            // persona Node3D is NOT re-parented).
            FlipPersonaToInMission(personaId);

            // M2-advance step 2b pivot — drop-accept does NOT
            // fire conclude immediately. It transitions the mission
            // from PendingMissions to ActiveMissions and starts the
            // 5-tick countdown. The conclude pipeline fires from
            // OnTickAdvanced when MissionDurationLogic.IsMissionDue
            // returns true on this entry.
            StartActiveMissionFromAccept(mission, personaId, isAffect: true);
        }
        else
        {
            // 5c — animate the proxy back to the persona's icon
            // position over SnapBackDurationSec, then hide it.
            // Spec : 300ms ease-out (Didier 2026-05-10).
            Log($"[DROP CANCELLED] reason={decision}");
            if (_missionToast is not null)
                _missionToast.Text = $"[DROP CANCELLED] {decision}";
            StartSnapBackTween(personaId);
        }
    }

    /// <summary>
    /// 5c — kick off a 300ms ease-out tween that returns the
    /// cancelled-drop proxy to the persona's screen-projected
    /// icon position before hiding it. Defensive against missing
    /// nodes : if anything is null, hide the proxy immediately
    /// (graceful fallback to v1 behaviour).
    /// </summary>
    private void StartSnapBackTween(string personaId)
    {
        if (_dragProxyRect is null)
        {
            return;
        }

        var origin = ProjectPersonaToScreen(personaId);
        if (origin is null)
        {
            // Could not project — hide instantly as fallback.
            _dragProxyRect.Visible = false;
            return;
        }

        if (_snapBackTween is not null && _snapBackTween.IsValid())
            _snapBackTween.Kill();

        var rectSize = _dragProxyRect.Size;
        var targetPos = new Vector2(
            origin.Value.X - rectSize.X * 0.5f,
            origin.Value.Y - rectSize.Y * 0.5f);

        _snapBackTween = CreateTween();
        _snapBackTween.TweenProperty(
                _dragProxyRect,
                "position",
                targetPos,
                SnapBackDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _snapBackTween.Finished += OnSnapBackFinished;
    }

    private void OnSnapBackFinished()
    {
        if (_dragProxyRect is not null) _dragProxyRect.Visible = false;
    }

    /// <summary>
    /// 5c — project a persona's Node3D position to screen-space
    /// for the snap-back tween target. Returns null if the
    /// persona, the SubViewport or the camera is missing, or if
    /// the persona is behind the camera (UnprojectPosition
    /// undefined). The runtime offsets the projection by the
    /// SubViewportContainer's position so the screen-space
    /// coordinate aligns with where the persona "appears" on
    /// the host viewport (same maths as BeginDrag's intended
    /// initial placement).
    /// </summary>
    private Vector2? ProjectPersonaToScreen(string personaId)
    {
        if (!_personaVisuals.TryGetValue(personaId, out var pv)) return null;
        if (_compagnieCamera3D is null || _compagnieContainer is null) return null;

        if (_compagnieCamera3D.IsPositionBehind(pv.Root.GlobalPosition))
            return null;

        var inSub = _compagnieCamera3D.UnprojectPosition(pv.Root.GlobalPosition);
        return inSub + _compagnieContainer.GlobalPosition;
    }

    private void FlipPersonaToInMission(string personaId)
    {
        if (!_personaVisuals.TryGetValue(personaId, out var pv)) return;
        _personasInMission.Add(personaId);

        // 5d — delegate text assembly to BuildPersonaLabelText so the
        // legacy-tag suffix is included the instant the persona is
        // flipped. The runtime also calls this from
        // RefreshPersonaLegacyLabels on MissionResolved, so suffix
        // updates are symmetric across both paths.
        pv.Label.Text = BuildPersonaLabelText(personaId);
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
    /// M2-advance step 2b — transition a freshly-accepted mission
    /// from <see cref="GameState.PendingMissions"/> to
    /// <see cref="GameState.ActiveMissions"/> and start the
    /// <see cref="MissionDurationLogic.DurationTicks"/>-tick
    /// countdown. NO HTTP call here ; the conclude round-trip
    /// fires later at <c>tick_due</c> from
    /// <see cref="OnTickAdvanced"/>.
    ///
    /// <para>
    /// <b>Why a sync method, not async.</b> The accept path is
    /// pure state mutation : build an ActiveMissionDto, append it
    /// to GameState.ActiveMissions, pop the head of PendingMissions,
    /// flip the persona's visual indicator, refresh the head-mission
    /// render. None of that touches the network. Keeping it sync
    /// avoids an unnecessary continuation marshal back to the main
    /// thread.
    /// </para>
    ///
    /// <para>
    /// <b>Why we still flip <c>_personasInMission</c>.</b> The set
    /// is the runtime's in-mission visual marker (drives label
    /// modulate colour and the [mission] indicator on Label3D). The
    /// canonical source of truth is now
    /// <see cref="GameState.ActiveMissions"/>, but the visual flag
    /// is faster to query in the per-frame label refresh than
    /// scanning the active-missions list. Kept in sync via Add
    /// here and Remove in <see cref="OnConcludeSuccessApply"/>.
    /// </para>
    /// </summary>
    private void StartActiveMissionFromAccept(
        EmergentMissionDto mission,
        string personaId,
        bool isAffect)
    {
        if (_gameState is null) return;

        var active = MissionDurationLogic.BuildActiveMission(
            mission, personaId, tickStarted: _currentTick);
        _gameState.ActiveMissions.Add(active);

        // Pop from pending — same defensive helper as the M1 path.
        var poppedId = PopHeadMission(_gameState.PendingMissions);
        Log($"[ACTIVE START] mission={mission.Id} persona={personaId} tickStarted={active.TickStarted} tickDue={active.TickDue} (popped pending : {poppedId ?? "(queue was already empty)"})");

        // Flip the persona's visual indicator (NPC autonomy : the
        // persona Node3D is NOT re-parented).
        FlipPersonaToInMission(personaId);

        if (_missionToast is not null)
        {
            var verb = isAffect ? "AFFECTED" : "ACCEPTED";
            _missionToast.Text = $"[{verb}] mission={mission.Id} -> countdown 5/5 (concludes at tick {active.TickDue})";
        }

        // Refresh the head-mission render (queue may have a
        // follow-up mission, or we go to the empty state).
        RenderHeadMission();
        // Refresh persona labels so the [mission 5/5] suffix lands
        // immediately rather than waiting for the next tick.
        RefreshPersonaLegacyLabels();
    }

    // ====================================================================
    // Drop slot hover affordance (5c)
    // ====================================================================

    /// <summary>5c — capture the StyleBoxFlat the .tscn assigned
    /// to the drop slot, derive a "hovered" variant by brightening
    /// the border colour, and apply the appropriate one based on
    /// <see cref="_dropSlotHovered"/>. Cached so we are not
    /// reallocating StyleBoxFlats every motion event.</summary>
    private void EnsureDropSlotStyles()
    {
        if (_dropSlot is null) return;
        if (_dropSlotStyleNormal is not null && _dropSlotStyleHovered is not null) return;

        var existing = _dropSlot.GetThemeStylebox("panel") as StyleBoxFlat;
        if (existing is null) return;

        // Take a duplicate as the "normal" baseline so re-applying
        // does not mutate the .tscn-loaded StyleBox shared with
        // other Panels in the scene.
        _dropSlotStyleNormal = (StyleBoxFlat)existing.Duplicate();
        _dropSlotStyleHovered = (StyleBoxFlat)existing.Duplicate();

        // Brighten the border for hover : push toward white, also
        // slightly thicker on all sides for a tactile feel.
        var n = _dropSlotStyleNormal.BorderColor;
        var brightened = new Color(
            (n.R + 1f) * 0.5f,
            (n.G + 1f) * 0.5f,
            (n.B + 1f) * 0.5f,
            n.A);
        _dropSlotStyleHovered.BorderColor = brightened;
        _dropSlotStyleHovered.BorderWidthLeft = _dropSlotStyleNormal.BorderWidthLeft + 2;
        _dropSlotStyleHovered.BorderWidthTop = _dropSlotStyleNormal.BorderWidthTop + 2;
        _dropSlotStyleHovered.BorderWidthRight = _dropSlotStyleNormal.BorderWidthRight + 2;
        _dropSlotStyleHovered.BorderWidthBottom = _dropSlotStyleNormal.BorderWidthBottom + 2;
    }

    private void ApplyDropSlotVisualState()
    {
        if (_dropSlot is null) return;
        EnsureDropSlotStyles();
        var style = _dropSlotHovered ? _dropSlotStyleHovered : _dropSlotStyleNormal;
        if (style is null) return;
        _dropSlot.AddThemeStyleboxOverride("panel", style);
    }

    private void OnDropSlotMouseEntered()
    {
        // Idle-time hover : we don't bother changing the visual,
        // because the affordance is meant to fire only during a
        // drag. Leave the no-op here so the wiring in
        // WireMissionPanel stays symmetrical with MouseExited.
    }

    private void OnDropSlotMouseExited()
    {
        // Idem.
    }

    // ====================================================================
    // Mission panel wiring (mirror of MissionPanelProbe trimmed)
    // ====================================================================

    private void WireMissionPanel()
    {
        if (_affectButton is not null) _affectButton.Pressed += OnAffectPressed;
        if (_declineButton is not null) _declineButton.Pressed += OnDeclinePressed;
        if (_dropSlot is not null)
        {
            _dropSlot.MouseEntered += OnDropSlotMouseEntered;
            _dropSlot.MouseExited += OnDropSlotMouseExited;
            EnsureDropSlotStyles();
            ApplyDropSlotVisualState();
        }
        // 5d — self-subscribe to MissionResolved so the persona labels
        // refresh their legacy-tag suffix the instant a resolve round-trip
        // commits. We emit + listen on the same signal because (i) it
        // keeps the resolve path linear (mutate state -> emit -> render
        // off the emit), (ii) future listeners (analytics, save trigger)
        // can hang off the same signal without touching the resolve flow.
        MissionResolved += OnMissionResolvedRefreshLabels;
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

        // M2-advance step 2b — every tick :
        //   1. Refresh persona Label3D suffixes so the
        //      [mission X/5] countdown decrements visually
        //      (every persona in an active mission updates).
        //   2. Scan ActiveMissions for due entries and fire the
        //      conclude pipeline asynchronously (one task per due
        //      mission ; in M2 that is at most one).
        //
        // The two ops are deliberately ordered : refresh first
        // (cheap, sync) so the player sees the 0/5 frame even if
        // the conclude round-trip lands on the same tick before
        // the next render. Then we kick off the conclude tasks ;
        // their continuations marshal back via OnConcludeSuccessApply
        // / OnConcludeFailureApply and remove the mission from
        // ActiveMissions on success.
        RefreshPersonaLegacyLabels();
        ScanAndConcludeDueMissions();
    }

    /// <summary>
    /// M2-advance step 2b — iterate <see cref="GameState.ActiveMissions"/>
    /// and fire the conclude pipeline for every entry where
    /// <see cref="MissionDurationLogic.IsMissionDue"/> returns true.
    ///
    /// <para>
    /// <b>Why we copy the list before iterating.</b> The conclude
    /// pipeline may complete synchronously in degenerate cases
    /// (e.g. a unit-test ApiClient that returns a pre-populated
    /// Task) and would mutate the list while we iterate. The copy
    /// is cheap (≤5 entries in M2) and removes the foreach-mutation
    /// hazard.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotence.</b> An <see cref="ActiveMissionDto"/> is
    /// removed from the list at conclude-success time
    /// (see <see cref="OnConcludeSuccessApply"/>). If the same
    /// mission appears as due on two consecutive ticks (e.g. the
    /// first conclude failed transiently and the next tick scan
    /// retries), the server's deterministic SHA-256-derived roll
    /// guarantees the same outcome and same tags — the client is
    /// safe to retry. M1 simple impl : we just kick off another
    /// task ; M3 may add a per-mission "in flight" guard if the
    /// double-fire becomes a real problem.
    /// </para>
    /// </summary>
    private void ScanAndConcludeDueMissions()
    {
        if (_gameState is null || _apiClient is null) return;
        if (_gameState.ActiveMissions.Count == 0) return;

        // Defensive copy : the conclude pipeline may mutate the
        // list (sync test paths, future cancellation that pops
        // mid-iter, etc.).
        var snapshot = _gameState.ActiveMissions.ToArray();

        // Build the company snapshot once — every due mission this
        // tick reads from the same map.
        var companyById = BuildCompanyById();

        for (var i = 0; i < snapshot.Length; i++)
        {
            var active = snapshot[i];
            if (!MissionDurationLogic.IsMissionDue(active, _currentTick)) continue;

            Log($"[CONCLUDE FIRE] mission={active.Mission.Id} tickDue={active.TickDue} currentTick={_currentTick}");
            _ = ConcludeOneAsync(active, companyById);
        }
    }

    /// <summary>
    /// M2-advance step 2b — async pipeline for one due mission.
    /// Builds the request via
    /// <see cref="MissionDurationLogic.BuildConcludeRequest"/>,
    /// posts to <c>/api/world/mission/conclude</c>, marshals the
    /// response back to the main thread via <c>CallDeferred</c>.
    /// </summary>
    private async Task ConcludeOneAsync(
        ActiveMissionDto active,
        System.Collections.Generic.IReadOnlyDictionary<string, CharacterStateDto> companyById)
    {
        if (_apiClient is null) return;

        MissionConcludeRequestDto request;
        try
        {
            request = MissionDurationLogic.BuildConcludeRequest(active, companyById);
        }
        catch (System.Exception ex)
        {
            // Defensive : a contract violation here means the
            // company snapshot is missing a persona that is in the
            // active mission. Surface to the toast and remove the
            // active mission so we don't loop firing failed builds.
            GD.PushError($"[CONCLUDE BUILD FAILED] mission={active.Mission.Id} : {ex.Message}");
            CallDeferred(MethodName.OnConcludeBuildFailedApply,
                active.Mission.Id, ex.Message);
            return;
        }

        var result = await _apiClient.ConcludeMissionAsync(request).ConfigureAwait(false);

        switch (result)
        {
            case Result<MissionConcludeResponseDto, ApiError>.Success ok:
                // Marshal the wire DTO back via CallDeferred. Godot
                // signals don't support arbitrary C# records as
                // Variant args ; we package the relevant scalars +
                // a string payload (mission id) so the deferred
                // handler can re-resolve the active mission and
                // process the response.
                var response = ok.Value;
                CallDeferred(MethodName.OnConcludeSuccessApply,
                    active.Mission.Id,
                    response.Outcome,
                    BuildTagsPayloadString(response.TagsCreated),
                    response.TagsCreated.Count);
                break;
            case Result<MissionConcludeResponseDto, ApiError>.Failure fail:
                // Unpack the typed ApiError into a string for the
                // CallDeferred Variant payload (Godot signals do not
                // marshal arbitrary C# records). Same shape as
                // WorldSimTick.FailureMessageOf.
                var failureMsg = fail.Error switch
                {
                    ApiError.NotReachable nr => nr.Reason,
                    ApiError.ServerError se => $"{se.StatusCode} {se.Body}",
                    ApiError.DeserializationError de => de.Reason,
                    ApiError.Cancelled => "cancelled",
                    _ => "(unknown)"
                };
                CallDeferred(MethodName.OnConcludeFailureApply,
                    active.Mission.Id,
                    fail.Error.GetType().Name,
                    failureMsg);
                break;
        }
    }

    /// <summary>
    /// Marshalled-to-main-thread handler for a successful conclude
    /// response. Mutates <see cref="GameState"/> (appends tags,
    /// removes the active mission), refreshes labels, emits
    /// <c>MissionResolved</c>.
    ///
    /// <para>
    /// <b>Why we re-find the active mission by id.</b> We could not
    /// pass the <see cref="ActiveMissionDto"/> directly through
    /// <c>CallDeferred</c> (Godot Variant marshalling does not
    /// support arbitrary C# records). Instead we pass the mission
    /// id and look it up here. If the mission was already removed
    /// (e.g. a duplicate fire from the previous tick scan landed
    /// first) we simply log and bail — idempotence preserved.
    /// </para>
    /// </summary>
    private void OnConcludeSuccessApply(
        string missionId,
        string outcome,
        string tagsPayload,
        int tagsCreatedCount)
    {
        if (_gameState is null) return;

        // Find and remove the active mission. RemoveAll lets us
        // handle the absent case cleanly.
        var removed = 0;
        EmergentMissionDto? originalMission = null;
        var assignedPersonaIds = new System.Collections.Generic.List<string>();
        for (var i = _gameState.ActiveMissions.Count - 1; i >= 0; i--)
        {
            var am = _gameState.ActiveMissions[i];
            if (am.Mission.Id != missionId) continue;
            originalMission = am.Mission;
            for (var j = 0; j < am.AssignedPersonas.Count; j++)
                assignedPersonaIds.Add(am.AssignedPersonas[j]);
            _gameState.ActiveMissions.RemoveAt(i);
            removed++;
        }
        if (removed == 0)
        {
            Log($"[CONCLUDE LATE] mission={missionId} already removed (duplicate fire ?) — ignoring");
            return;
        }

        // Parse the payload string back into typed tags. Format :
        // "personaId|missionId|missionType|region|actorTarget|outcome|earnedAtTick"
        // joined by "::" between entries. See
        // BuildTagsPayloadString for the symmetric serialiser.
        var tags = ParseTagsPayloadString(tagsPayload);

        // Append every tag through the pure-C# helper so the same
        // create-list-if-missing semantics apply as the M1 resolve
        // path.
        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            AppendTagToLegacyMap(_gameState.PersonaLegacy, tag);
            Log($"[LEGACY TAG STORED] persona={tag.PersonaId} mission={tag.MissionId} outcome={tag.Outcome}");
        }

        // Free the persona's in-mission visual flag so the next
        // RefreshPersonaLegacyLabels strips the [mission X/5]
        // suffix and folds the new legacy tag into the
        // " • N missions : ..." suffix.
        for (var i = 0; i < assignedPersonaIds.Count; i++)
            _personasInMission.Remove(assignedPersonaIds[i]);

        // Reset the persona body Modulate to neutral (the warm-earth
        // tints) so the visual flip-back is symmetric with
        // FlipPersonaToInMission.
        for (var i = 0; i < assignedPersonaIds.Count; i++)
        {
            var pid = assignedPersonaIds[i];
            var placeholder = FindPersonaPlaceholder(pid);
            if (_personaVisuals.TryGetValue(pid, out var pv))
            {
                pv.Label.Modulate = new Color(0.95f, 0.92f, 0.80f, 1f);
            }
            // The placeholder is unused here ; kept defensively for
            // future per-tint flip-back symmetry.
            _ = placeholder;
        }

        if (_missionToast is not null)
        {
            _missionToast.Text = $"[CONCLUDE {outcome.ToUpperInvariant()}] mission={missionId} tags={tagsCreatedCount}";
        }

        // Refresh labels — the [mission X/5] suffix disappears, the
        // legacy " • N missions : ..." suffix appears.
        RefreshPersonaLegacyLabels();

        // Emit MissionResolved so 5d listeners (legacy label refresh,
        // future analytics) react. The signal payload is the same
        // shape as the M1 resolve path : (missionId, tagsCreatedCount).
        EmitSignal(SignalName.MissionResolved, missionId, tagsCreatedCount);
        Log($"[EVENT {MissionResolvedEventName}] mission={missionId} outcome={outcome} tags={tagsCreatedCount}");
    }

    private void OnConcludeFailureApply(string missionId, string errorVariant, string message)
    {
        // M2 simple impl — surface the failure on the toast, leave
        // the active mission in place (next tick scan will retry
        // because the deterministic server roll yields the same
        // outcome). M3 may add a bounded retry policy or a
        // user-facing "abandon mission" verb.
        GD.PushWarning($"[CONCLUDE FAILED] mission={missionId} : {errorVariant} {message}");
        if (_missionToast is not null)
            _missionToast.Text = $"[CONCLUDE ERROR {errorVariant}] mission={missionId}";
    }

    private void OnConcludeBuildFailedApply(string missionId, string message)
    {
        // The request build threw — usually because a persona is
        // missing from the company snapshot. Remove the active
        // mission to break the loop ; the user sees the toast and
        // the mission is lost (no tags created). Acceptable in M2 ;
        // M3+ may stash the unbuilt request for retry after a
        // company-snapshot resync.
        if (_gameState is null) return;
        for (var i = _gameState.ActiveMissions.Count - 1; i >= 0; i--)
        {
            if (_gameState.ActiveMissions[i].Mission.Id == missionId)
            {
                var dropped = _gameState.ActiveMissions[i];
                _gameState.ActiveMissions.RemoveAt(i);
                for (var j = 0; j < dropped.AssignedPersonas.Count; j++)
                    _personasInMission.Remove(dropped.AssignedPersonas[j]);
                break;
            }
        }
        if (_missionToast is not null)
            _missionToast.Text = $"[CONCLUDE BUILD FAILED] {missionId} dropped : {message}";
        RefreshPersonaLegacyLabels();
    }

    /// <summary>
    /// M2-advance step 2b — build the persona-id → CharacterStateDto
    /// lookup snapshot from <see cref="GameState.CompanyPersonas"/>.
    /// O(personas) ; called once per tick scan. The lookup is fed
    /// to <see cref="MissionDurationLogic.BuildConcludeRequest"/>
    /// for projection to <see cref="CharacterStateWireDto"/>.
    ///
    /// <para>
    /// <b>Defensive against duplicate Names.</b> The M1 seeder uses
    /// distinct lowercase names ; if a future flow accidentally
    /// adds two personas with the same Name, we keep the FIRST
    /// occurrence (stable behaviour) and log a warning. A throw
    /// would break the conclude path for unrelated missions.
    /// </para>
    /// </summary>
    private System.Collections.Generic.IReadOnlyDictionary<string, CharacterStateDto> BuildCompanyById()
    {
        var map = new System.Collections.Generic.Dictionary<string, CharacterStateDto>();
        if (_gameState is null) return map;
        for (var i = 0; i < _gameState.CompanyPersonas.Count; i++)
        {
            var p = _gameState.CompanyPersonas[i];
            if (string.IsNullOrEmpty(p.Name)) continue;
            if (!map.ContainsKey(p.Name)) map[p.Name] = p;
            else GD.PushWarning($"[BuildCompanyById] duplicate persona Name={p.Name} — keeping first");
        }
        return map;
    }

    /// <summary>
    /// Serialise a list of <see cref="PersonaLegacyTagDto"/> to a
    /// flat string suitable for <c>CallDeferred</c> Variant
    /// marshalling. Each tag is "personaId|missionId|missionType|
    /// region|actorTarget|outcome|earnedAtTick" ; entries are
    /// joined by "::". <see cref="ParseTagsPayloadString"/> is the
    /// symmetric deserialiser.
    ///
    /// <para>
    /// <b>Why a flat string and not Godot.Collections.Array.</b>
    /// PersonaLegacyTagDto is a C# record ; passing it through
    /// CallDeferred would require either (a) registering it with
    /// Godot's Variant marshalling (not feasible without a
    /// [GodotObject]-derived wrapper), or (b) packing each field
    /// into a Godot.Collections.Array for each tag, then a parent
    /// Array of Arrays. The flat string is the simplest seam :
    /// one Variant arg, parsed back into typed records on the
    /// main thread.
    /// </para>
    ///
    /// <para>
    /// <b>Why the pipe and double-colon separators.</b> Neither
    /// character appears in canonical persona ids, mission ids,
    /// mission types, region ids, or outcome strings (Varn-locked
    /// closed lookups). actor_target is free-form but M1 templates
    /// always render with null. If a future region ever contains
    /// either character, this seam needs revisiting (replace with
    /// Godot.Collections.Array of typed Arrays).
    /// </para>
    /// </summary>
    private static string BuildTagsPayloadString(
        System.Collections.Generic.IReadOnlyList<PersonaLegacyTagDto> tags)
    {
        if (tags is null || tags.Count == 0) return "";
        var parts = new System.Collections.Generic.List<string>(tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var t = tags[i];
            parts.Add(string.Join("|",
                t.PersonaId,
                t.MissionId,
                t.MissionType,
                t.Region,
                t.ActorTarget ?? "",
                t.Outcome,
                t.EarnedAtTick.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return string.Join("::", parts);
    }

    private static System.Collections.Generic.IReadOnlyList<PersonaLegacyTagDto> ParseTagsPayloadString(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            return System.Array.Empty<PersonaLegacyTagDto>();
        var entries = payload.Split("::");
        var list = new System.Collections.Generic.List<PersonaLegacyTagDto>(entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var fields = entries[i].Split('|');
            if (fields.Length != 7) continue;
            list.Add(new PersonaLegacyTagDto(
                PersonaId: fields[0],
                MissionId: fields[1],
                MissionType: fields[2],
                Region: fields[3],
                ActorTarget: string.IsNullOrEmpty(fields[4]) ? null : fields[4],
                Outcome: fields[5],
                EarnedAtTick: int.TryParse(fields[6], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0));
        }
        return list;
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

        BuildEligiblePersonaButtons(mission);
        UpdateMissionButtons(mission);
        UpdateQueueStatus();
    }

    private void RenderEmptyMissionPanel()
    {
        _selectedPersonaId = null;
        if (_missionHeader is not null) _missionHeader.Text = "[MISSION] (none — waiting for tick cadence)";
        if (_missionHook is not null) _missionHook.Text = "";
        if (_missionEligible is not null) _missionEligible.Text = "";
        ClearPersonaButtons();
        UpdateMissionButtons(mission: null);
        UpdateQueueStatus();
    }

    /// <summary>5c — build one Button per eligible persona under the
    /// dynamic container (or directly under the Mission panel as a
    /// fallback). Click selects the persona and refreshes the Affect
    /// button state. The personas already in mission are skipped (no
    /// recall in v1). Same eligibility filter as the drag-drop path
    /// (<see cref="M1SliceLogic.IsPersonaInEligibleList"/>) — by
    /// construction the buttons map 1-to-1 to mission.EligiblePersonas
    /// minus the already-assigned set.</summary>
    private void BuildEligiblePersonaButtons(EmergentMissionDto mission)
    {
        ClearPersonaButtons();

        var parent = _personaButtonsContainer as Node ?? _missionHeader?.GetParent();
        if (parent is null) return;

        for (var i = 0; i < mission.EligiblePersonas.Count; i++)
        {
            var personaId = mission.EligiblePersonas[i];
            if (_personasInMission.Contains(personaId)) continue;

            var btn = new Button
            {
                Text = $"-> Affecter : {personaId}",
                Name = $"PersonaBtn_{personaId}",
            };
            // Place the button just under the eligible label (Y=290 in
            // the .tscn) ; if the dynamic container is present we let
            // it manage layout, otherwise we offset manually.
            if (_personaButtonsContainer is null)
            {
                btn.Position = new Vector2(24, 290 + i * 36);
                btn.Size = new Vector2(556, 32);
            }

            var capturedId = personaId;
            btn.Pressed += () => OnPersonaButtonPressed(capturedId);

            parent.AddChild(btn);
            _personaButtons.Add(btn);
        }

        Log($"[5c] built {_personaButtons.Count} eligible-persona button(s) under {parent.Name}");
    }

    private void ClearPersonaButtons()
    {
        for (var i = 0; i < _personaButtons.Count; i++)
        {
            var btn = _personaButtons[i];
            if (IsInstanceValid(btn)) btn.QueueFree();
        }
        _personaButtons.Clear();
    }

    private void OnPersonaButtonPressed(string personaId)
    {
        _selectedPersonaId = personaId;
        Log($"[5c] selected persona via button : {personaId}");
        if (_gameState is not null && _gameState.PendingMissions.Count > 0)
        {
            UpdateMissionButtons(_gameState.PendingMissions[0]);
        }
    }

    private void UpdateMissionButtons(EmergentMissionDto? mission)
    {
        if (_affectButton is not null)
        {
            // Affect button is a 5c backup path — clavier-first
            // ergonomics. Enables when a persona has been clicked
            // in the eligible-persona button list (or implicitly
            // selected by a successful drag-drop, but the drag-drop
            // path runs the resolve directly without the Affect
            // button).
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

    private void OnAffectPressed()
    {
        // M2-advance step 2b — keyboard-first Affect path is now
        // symmetric with the drag-drop accept path : both call
        // StartActiveMissionFromAccept. NO HTTP at this stage ;
        // the conclude round-trip fires at tick_due from
        // OnTickAdvanced. Method body is sync now (was async to
        // await the resolve round-trip in M1).
        if (_gameState is null) return;
        if (_gameState.PendingMissions.Count == 0) return;
        if (string.IsNullOrEmpty(_selectedPersonaId)) return;

        var mission = _gameState.PendingMissions[0];
        StartActiveMissionFromAccept(mission, _selectedPersonaId, isAffect: true);
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

        // 5c — store each tag through the pure-C# helper so xUnit
        // pins the create-list-if-missing semantics. The runtime
        // GameState.AppendPersonaLegacyTag does the same thing but
        // is Godot-bound and not directly testable.
        for (var i = 0; i < response.TagsCreated.Count; i++)
        {
            var tag = response.TagsCreated[i];
            AppendTagToLegacyMap(_gameState.PersonaLegacy, tag);
            // Spec 5c §1 — log format pinned by Didier.
            Log($"[LEGACY TAG STORED] persona={tag.PersonaId} mission={tag.MissionId} outcome={tag.Outcome}");
        }

        // 5c — pop via the pure-C# helper so the empty-list defensive
        // branch is exercised here AND testable in xUnit. The runtime
        // had been calling RemoveAt(0) blindly, which would throw if
        // a concurrent tick resolution emptied the queue while the
        // resolve round-trip was in flight.
        var poppedId = PopHeadMission(_gameState.PendingMissions);
        Log($"[5c] popped head mission : {poppedId ?? "(queue was already empty)"}");

        // 5c — emit MissionResolved AFTER state mutation so listeners
        // see the post-resolve world (PendingMissions popped, tags
        // appended). tagsCreatedCount is the wire-format field the
        // signal carries ; full tags read from GameState.PersonaLegacy
        // by listeners that need the full content.
        EmitSignal(SignalName.MissionResolved, mission.Id, response.TagsCreated.Count);
        Log($"[EVENT {MissionResolvedEventName}] mission={mission.Id} tagsCreatedCount={response.TagsCreated.Count}");

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

    // ====================================================================
    // 5d — legacy-tag visualisation helpers
    // ====================================================================

    /// <summary>5d + M2-advance step 2b — assemble the
    /// canonical Label3D text for a persona. Priority order :
    /// <list type="number">
    ///   <item>If the persona is in an active mission (countdown
    ///         in progress), render
    ///         <c>"{Name} -- {Status} [mission X/5]"</c>. The
    ///         countdown suffix replaces the static <c>[mission]</c>
    ///         indicator from M1 — the count-down strictly
    ///         carries more information.</item>
    ///   <item>Otherwise, render the canonical
    ///         <c>"{Name} -- {Status}"</c> plus the legacy-tag
    ///         suffix from
    ///         <see cref="M1SliceLogic.ComputeLegacyLabelSuffix"/>.</item>
    /// </list>
    /// Single source of truth so the click-to-affect path, the
    /// drag-drop path, the snap-back path, the per-tick suffix
    /// refresh, and the MissionResolved refresh all stay
    /// byte-for-byte symmetric.</summary>
    private string BuildPersonaLabelText(string personaId)
    {
        var p = FindPersonaPlaceholder(personaId);
        var displayName = p?.DisplayName ?? personaId;
        var status = p?.ShortStatus ?? "";

        // M2-advance step 2b : countdown suffix has priority over
        // both the [mission] static indicator and the legacy-tag
        // suffix. Computed via the pure-C# helper for symmetry
        // with the MissionDurationLogicTests pin (no Godot deps).
        if (_gameState is not null)
        {
            var activeMissions = (System.Collections.Generic.IReadOnlyList<ActiveMissionDto>)_gameState.ActiveMissions;
            var countdown = MissionDurationLogic.ComputeMissionCountdownSuffix(
                personaId, activeMissions, _currentTick);
            if (countdown is not null)
            {
                return $"{displayName} -- {status}{countdown}";
            }
        }

        // Not in an active mission — fall back to the legacy
        // suffix (5d behaviour preserved). The transient
        // _personasInMission set is no longer used here ; it is
        // kept for the body Modulate flip-back in the runtime.
        IReadOnlyList<PersonaLegacyTagDto>? tags = null;
        if (_gameState is not null && _gameState.PersonaLegacy.TryGetValue(personaId, out var list))
        {
            tags = list;
        }
        var suffix = M1SliceLogic.ComputeLegacyLabelSuffix(tags);

        return $"{displayName} -- {status}{suffix}";
    }

    /// <summary>5d — re-render every persona Label3D in the
    /// Compagnie sub-space using the latest
    /// <c>GameState.PersonaLegacy</c> snapshot. Called on
    /// MissionResolved (self-signal) and once at boot for the case
    /// where a future save-load lands tags before the scene wires up.
    /// O(personas) per call ; the tag count per persona stays small
    /// (1-10 in the M1 horizon), so the cost is dominated by the
    /// Label3D.Text assignment cross-boundary call -- well under a
    /// frame budget for the 3-persona placeholder roster.</summary>
    private void RefreshPersonaLegacyLabels()
    {
        foreach (var kv in _personaVisuals)
        {
            kv.Value.Label.Text = BuildPersonaLabelText(kv.Key);
        }
    }

    /// <summary>5d — handler bound to the self-emitted
    /// MissionResolved signal. Refreshes every persona label so the
    /// suffix update is immediate (no waiting for the next tick or
    /// the next click). The signal payload is logged but not used
    /// for filtering : we refresh the whole roster, not just the
    /// assigned persona, because (i) the per-persona delta is cheap,
    /// (ii) a future cascade rule (e.g. "a successful Halfgate
    /// mission also flips the regional flavour suffix on Hodge")
    /// would still work without rewiring this handler.</summary>
    private void OnMissionResolvedRefreshLabels(string missionId, int tagsCreatedCount)
    {
        Log($"[5d] MissionResolved received -> refreshing legacy labels (mission={missionId}, tagsCreated={tagsCreatedCount})");
        RefreshPersonaLegacyLabels();
    }

    // ====================================================================
    // PR6 - POI integration pattern wiring.
    //
    // Architectural note. M1Slice is the composite Compagnie + Mission UI
    // scene ; it does NOT render a 2D iso terrain. The four "pion sur
    // plateau" ingredients (shadow, lift, rim, parallax) are validated
    // visually on PoiSpawnProbe against a bare grid ; M1Slice is the
    // wiring-pattern validation point. Limitations consequently
    // documented in client/data/POI_INTEGRATION.md :
    //  - No Camera2D in M1Slice (only the Compagnie SubViewport Camera3D
    //    for the persona sub-iso). Parallax is therefore null-camera =
    //    disabled - the POI sits in absolute Node2D world coords.
    //  - No terrain habille underneath. The POI sprite renders against
    //    the M1Slice OverlayUI panels ; visual A/B for terrain integration
    //    stays on the probe / future PR5.X+ that wires the iso world scene.
    //
    // PR6 still ships the full wiring : data load + spawn + router signal
    // + SceneManager dispatch + ExitTree disconnect, so adding a POI to a
    // *future* iso world scene is one Array entry, not one architecture
    // discussion.
    // ====================================================================

    /// <summary>
    /// PR6 - iterate <see cref="PoisToSpawn"/>, resolve each entry's
    /// <see cref="PoiData"/> (either from the entry's <c>Data</c> field or
    /// by loading the sidecar PNG at <c>SidecarPngPath</c>), spawn it via
    /// the <see cref="PoiSpawner"/> child, and register the click handler
    /// once on the <see cref="PoiInputRouter"/> autoload.
    ///
    /// <para>
    /// Disconnection is owned by <c>_ExitTree</c> ; subscription happens
    /// here so it is paired with the spawn call site rather than scattered
    /// across the boot sequence.
    /// </para>
    /// </summary>
    private void WirePoisAndRouter()
    {
        _poiSpawner = GetNodeOrNull<PoiSpawner>(PoiSpawnerPath);
        if (_poiSpawner is null)
        {
            GD.PushWarning(
                $"[M1 SLICE POI] no PoiSpawner at '{PoiSpawnerPath}' - POI wiring " +
                "skipped (the .tscn must include a 'PoiSpawner' child node before " +
                "any POI can spawn). PR6 anti-checklist : a missing spawner is " +
                "warned, never thrown - this scene's other systems must keep booting.");
            return;
        }

        _poiRouter = GetTree()?.Root?.GetNodeOrNull<PoiInputRouter>("PoiInputRouter");
        if (_poiRouter is null)
        {
            GD.PushWarning(
                "[M1 SLICE POI] PoiInputRouter autoload NOT found at /root - " +
                "POIs will spawn but hover/click events will fall through. Check " +
                "project.godot autoload registration.");
        }
        else
        {
            // Disconnection-discipline pairing : the unsubscribe lives in
            // _ExitTree (methodology trap #10).
            _poiRouter.PoiClicked += OnPoiClicked;
        }

        if (PoisToSpawn is null || PoisToSpawn.Count == 0)
        {
            GD.Print("[M1 SLICE POI] PoisToSpawn empty - no POI to spawn this run.");
            return;
        }

        // M1Slice has no 2D Camera2D (only the Compagnie SubViewport
        // Camera3D), so we pass null and the spawned POIs run with
        // parallax disabled regardless of their per-POI ParallaxStrength
        // export. Dette PR5.X+ : when an iso world scene with a Camera2D
        // is wired in, pass that camera through here.
        Camera2D? parallaxCamera = null;

        var spawnedCount = 0;
        foreach (var entry in PoisToSpawn)
        {
            if (entry is null)
            {
                GD.PushWarning("[M1 SLICE POI] PoisToSpawn contains a null entry - skipping.");
                continue;
            }

            var data = ResolvePoiData(entry);
            if (data is null)
            {
                GD.PushError(
                    $"[M1 SLICE POI] entry at tile=({entry.Tile.X},{entry.Tile.Y}) " +
                    "has neither Data nor a resolvable SidecarPngPath - skipping.");
                continue;
            }

            // Spawn under the M1Slice Node2D root. CanvasLayer children
            // would render in screen-space ; we want world-space so the
            // future Camera2D pan / zoom can move the POI naturally.
            var poi = _poiSpawner.SpawnAt(data, entry.Tile, this, parallaxCamera);
            _spawnedPois.Add(poi);
            spawnedCount++;

            GD.Print(
                $"[M1 SLICE POI] spawned '{data.DisplayName}' " +
                $"at tile=({entry.Tile.X},{entry.Tile.Y}) " +
                $"worldPos=({poi.Position.X:F1},{poi.Position.Y:F1})");
        }

        GD.Print($"[M1 SLICE POI] WirePoisAndRouter done : {spawnedCount}/{PoisToSpawn.Count} POI(s) spawned.");
    }

    /// <summary>
    /// PR6 - resolve a <see cref="PoiSpawnEntry"/> to a runtime
    /// <see cref="PoiData"/>. Priority : the entry's <c>Data</c> field, then
    /// the sidecar PNG path via <see cref="PoiSidecarLoader"/>. Applies the
    /// <c>DisplayNameOverride</c> when non-empty.
    /// </summary>
    private static PoiData? ResolvePoiData(PoiSpawnEntry entry)
    {
        PoiData? data = entry.Data;
        if (data is null && !string.IsNullOrEmpty(entry.SidecarPngPath))
        {
            try
            {
                data = PoiSidecarLoader.Load(entry.SidecarPngPath);
            }
            catch (System.Exception e)
            {
                GD.PushError(
                    $"[M1 SLICE POI] PoiSidecarLoader.Load({entry.SidecarPngPath}) " +
                    $"threw : {e.Message} - entry skipped.");
                return null;
            }
        }
        if (data is not null && !string.IsNullOrEmpty(entry.DisplayNameOverride))
        {
            // The override mutates the loaded copy. It is safe : either
            // (i) the data was just constructed by the loader and is unique
            // to this scene-spawn, or (ii) the designer pointed multiple
            // entries at the same .tres and is responsible for the
            // semantics (the override is the intended way to disambiguate).
            data.DisplayName = entry.DisplayNameOverride;
        }
        return data;
    }

    /// <summary>
    /// PR6 - router signal handler. MVP : log the click, then dispatch
    /// navigation via <see cref="SceneManager.NavigateTo"/> if
    /// <see cref="PoiClickNavigateTo"/> is non-empty. No modal, no fancy
    /// UI - the prebrief anti-checklist forbids both for PR6.
    /// </summary>
    private void OnPoiClicked(string displayName)
    {
        Log($"[M1Slice] POI clicked : {displayName}");

        if (string.IsNullOrEmpty(PoiClickNavigateTo))
        {
            GD.Print($"[M1 SLICE POI] click on '{displayName}' - PoiClickNavigateTo empty, log-only MVP path.");
            return;
        }

        var sceneManager = GetTree()?.Root?.GetNodeOrNull<SceneManager>("SceneManager");
        if (sceneManager is null)
        {
            GD.PushWarning("[M1 SLICE POI] SceneManager autoload not found - click logged but no navigation dispatched.");
            return;
        }

        GD.Print($"[M1 SLICE POI] dispatching SceneManager.NavigateTo('{PoiClickNavigateTo}')");
        // Fire-and-forget : navigation is async, but we are an event handler
        // (not async ourselves). The SceneManager owns cancellation and
        // re-entrancy via its per-navigation token (see SceneManager XML
        // doc, Risk #1 mitigation).
        _ = sceneManager.NavigateTo(PoiClickNavigateTo);
    }

    private sealed record PersonaVisual(
        Node3D Root,
        MeshInstance3D Body,
        Label3D Label,
        Area3D Area3D,
        Area3D.InputEventEventHandler AreaInputHandler);
}
