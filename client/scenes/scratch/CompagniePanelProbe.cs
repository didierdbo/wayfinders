using Godot;
using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;
using static Wayfinders.Client.Scripts.Screens.CompagniePanelLogic;

namespace Wayfinders.Client.Scenes.Scratch;

/// <summary>
/// Jalon 5+ probe -- proof-of-concept for the <b>3D-backing pattern
/// applied to UI panels</b> (Wayfinders 3D Backing Architecture, doc
/// extension 2026-05-09 soir &#167; "Extension UI panels = iso
/// sub-spaces"). The Compagnie panel is the first UI panel of Wayfinders
/// to use this architecture -- patterns discovered here will be reused
/// for Mission, craft, HQ, combat tactique futurs.
///
/// <para>
/// <b>What is new vs the J1-J4 cell probes.</b> Earlier probes
/// (<see cref="Tile3DBackingProbe"/>, <see cref="Tile3DBackingShadowSyncProbe"/>)
/// rendered their 3D content in the <i>root</i> viewport, mixing
/// Camera2D + Camera3D current together (Pattern B2 from J2). A UI panel
/// is different : it is a <b>sub-space</b> of the screen with its own
/// content, occluding part of the parent screen. The right Godot
/// container for that is a <see cref="SubViewport"/> wrapped in a
/// <see cref="SubViewportContainer"/> -- the panel renders into its own
/// off-screen viewport, the Container blits the result onto the parent
/// screen at its own <see cref="Control"/> rectangle. This isolates the
/// panel's Camera2D + Camera3D pair from the parent screen's cameras
/// (no current-camera conflict, no picking-ray cross-contamination).
/// </para>
///
/// <para>
/// <b>Pattern P1 (new J5+) -- "UI panel = SubViewport sub-space"</b>.
/// Every retractable UI panel of Wayfinders (Compagnie, Mission, craft,
/// HQ, ...) is hosted in a <c>SubViewportContainer + SubViewport</c>.
/// The SubViewport contains :
/// <list type="bullet">
///   <item>a <see cref="Node3D"/> root of the sub-iso scene (the
///         <i>spatial</i> truth -- personas, mission slots, props) ;</item>
///   <item>a <see cref="Camera3D"/> ortho follower current in the
///         <b>SubViewport</b> (not the root) -- this is critical, see
///         the canary checks at <see cref="AssertCanaryInvariants"/> ;</item>
///   <item>a <see cref="Camera2D"/> if the panel needs 2D-rendered
///         elements anchored to the same iso frame (here we keep the v1
///         simple : 3D only inside the SubViewport, the diegetic 2D
///         handle lives on the parent screen as a sibling
///         Control) ;</item>
///   <item>the iso-projected 2D background (taverne placeholder) as a
///         <see cref="Sprite3D"/> or <see cref="MeshInstance3D"/> on the
///         iso ground plane -- v1 uses a flat colored quad, the real
///         taverne sprite from Mira plugs in later.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Pattern P2 (new J5+) -- "Slide via Tween3D on the panel root
/// Node3D"</b>. The slide-in / slide-out animation is a
/// <see cref="Tween"/> on the <i>root <see cref="Node3D"/></i> of the
/// sub-space (translation on X). NOT a 2D Control tween, NOT a
/// SubViewportContainer position tween. The visual difference is
/// invisible at v1 (a flat quad slides), but Pattern P2 generalises to
/// future tactile board feel : when the taverne backdrop has thick
/// walls, lit fireplace, persona figurines, the Tween3D translates the
/// whole 3D world of the panel along its X axis -- the camera follower
/// re-projects each frame, the 2D rendering of the panel slides in
/// lock-step. Single source of truth for the position.
/// </para>
///
/// <para>
/// <b>Pattern P3 (new J5+) -- "Drag-drop persona = proxy Tween3D + state
/// event, NOT re-parenting"</b>. Memory
/// <c>project_wayfinders_npc_autonomy.md</c> locks the contract :
/// <i>"the perso stays in the Compagnie ; the drag-drop is a tactical
/// signal of the player ; the GameState authoritative state is updated
/// via an event ; the visual on the Compagnie side flips its indicator
/// to in-mission"</i>. The probe :
/// <list type="number">
///   <item>On click on a persona Area3D, spawns a <i>proxy</i>
///         <see cref="Node3D"/> at the persona's world position.</item>
///   <item>Runs a Tween3D on the <i>proxy's</i> position, sliding it
///         toward the mission slot witness Node3D.</item>
///   <item>At Tween finish, frees the proxy and emits the drag-drop
///         event (in v1 a <see cref="GD.Print"/> log ; the real impl
///         will route it through GameState).</item>
///   <item>The <i>original</i> persona Node3D stays put -- its scene
///         tree parent is not touched. A status indicator (here, the
///         Label3D suffix " [mission]") flips to mark the persona
///         as in-mission.</item>
/// </list>
/// Pin tested in <see cref="Wayfinders.Client.Tests.Phase9_3DBacking.CompagniePanelLogicTests"/>
/// at the contract level (<see cref="AssignToMissionEvent"/> shape) ;
/// runtime invariant pinned at the canary self-check (the persona
/// remains a child of the sub-space root after a drag-drop).
/// </para>
///
/// <para>
/// <b>Pattern P4 (new J5+) -- "Auto-rabat via global signal"</b>. The
/// Spec &#167; "auto-rabat actif" v1 says : when another panel layer
/// opens, the Compagnie panel slides closed automatically. The probe
/// implements this with a <i>local</i> signal (<see cref="OtherLayerOpened"/>)
/// fired by a probe-only debug button -- the production version will
/// promote this to a global autoload signal bus
/// (<c>PanelLayerSignalBus.LayerOpened</c>) that any panel can subscribe
/// to. The state machine in <see cref="CompagniePanelLogic"/> is already
/// shape-correct for the production path (<see cref="SlideIntent.AnotherLayerOpened"/>
/// is the same event whether it comes from a debug button or a global
/// bus).
/// </para>
///
/// <para>
/// <b>Strict isolation.</b> This scene touches no production node.
/// <c>metadata/skip_opening_bootstrap = true</c> on the root prevents
/// the <see cref="Wayfinders.Client.Services.OpeningBootstrap"/> autoload
/// from injecting the cartographer's office CanvasLayer over the probe
/// (lesson 1bis from <c>feedback_godot_rendering_input_traps.md</c>).
/// Reversibility : delete <c>scenes/scratch/CompagniePanel*</c> +
/// <c>scripts/screens/CompagniePanelLogic.cs</c> +
/// <c>tests/Phase9_3DBacking/CompagniePanelLogicTests.cs</c> +
/// revert the <c>project.godot main_scene</c> line and the <c>.csproj</c>
/// Compile Include line. No production touch.
/// </para>
///
/// <para>
/// <b>v1 placeholders explicit.</b> All visuals here are scratch-grade :
/// <list type="bullet">
///   <item>3 personas = 3 colored MeshInstance3D cubes (olive / cream /
///         rust). Real Mira sprites plug in later via the same Node3D
///         seam -- replace the cube mesh with a Sprite3D textured with
///         the per-persona painted asset.</item>
///   <item>Taverne backdrop = a flat brown quad on the iso ground
///         plane. Real Bourgeon-Bonner taverne painting plugs in later.</item>
///   <item>Mission slot = a flat dark cube at +X offset from the
///         personas. Real Mission panel will be its own
///         SubViewport + sub-iso scene at the next jalon.</item>
///   <item>Diegetic handle = a brown ColorRect on the parent screen.
///         Real "tranche du registre" by Mira (cuir umber + brass caps
///         + marque-pages parchemin + cachet de cire rouge) plugs in
///         later as a Sprite2D with a click-target Area2D.</item>
/// </list>
/// </para>
/// </summary>
public partial class CompagniePanelProbe : Node2D
{
    /// <summary>Slide animation duration in seconds. 0.4 s feels
    /// natural for a panel-width slide on most hardware ; the
    /// anti-stuck pin test pins the runtime budget at < 1 s, leaving
    /// margin for hardware-dependent jitter.</summary>
    [Export] public float SlideDurationSec { get; set; } = 0.4f;

    /// <summary>Drag-drop animation duration. Slightly faster than the
    /// panel slide so the player feels "dispatch is responsive" --
    /// 0.25 s is the sweet spot between "instant feels jittery" and
    /// "slow feels reluctant". v1 placeholder ; tune at integration.</summary>
    [Export] public float DragDropDurationSec { get; set; } = 0.25f;

    /// <summary>Open / closed X offsets in pixels (parent screen
    /// coords) for the SubViewportContainer. Closed = panel hides
    /// off-screen to the left, only the handle pokes out.</summary>
    [Export] public float ClosedXPx { get; set; } = -560f;
    [Export] public float OpenXPx { get; set; } = 0f;

    [Export] public NodePath SubViewportContainerPath { get; set; } = "PanelLayer/SubViewportContainer";
    [Export] public NodePath SubViewportPath { get; set; } = "PanelLayer/SubViewportContainer/SubViewport";
    [Export] public NodePath SubSpaceRoot3DPath { get; set; } = "PanelLayer/SubViewportContainer/SubViewport/SubSpaceRoot3D";
    [Export] public NodePath SubSpaceCamera3DPath { get; set; } = "PanelLayer/SubViewportContainer/SubViewport/SubSpaceRoot3D/SubSpaceCamera3D";
    [Export] public NodePath PersonasContainerPath { get; set; } = "PanelLayer/SubViewportContainer/SubViewport/SubSpaceRoot3D/Personas";
    [Export] public NodePath MissionSlotPath { get; set; } = "PanelLayer/SubViewportContainer/SubViewport/SubSpaceRoot3D/MissionSlot";
    [Export] public NodePath HandleAreaPath { get; set; } = "OverlayUI/Handle";
    [Export] public NodePath OtherLayerButtonPath { get; set; } = "OverlayUI/OtherLayerButton";
    [Export] public NodePath StatusLabelPath { get; set; } = "OverlayUI/StatusLabel";
    [Export] public NodePath LiveLogLabelPath { get; set; } = "OverlayUI/LiveLogLabel";

    /// <summary>Iso world-coord spacing between persona cubes. Cell size
    /// 64 in 3D world units -- chosen to fit 3 personas + 1 mission
    /// slot horizontally inside a 256 x 256 SubViewport at zoom 1
    /// without overlap.</summary>
    private const float PersonaSpacingWorldUnits = 96f;

    /// <summary>Local-bus signal fired by the probe-only debug
    /// "Open another layer" button. Drives Pattern P4 (auto-rabat).
    /// The production version promotes this to a global autoload signal
    /// bus.</summary>
    [Signal] public delegate void OtherLayerOpenedEventHandler();

    /// <summary>Production-shaped event the probe emits at drag-drop
    /// completion. v1 just logs ; the real Mission panel subscribes to
    /// this signal at integration.</summary>
    [Signal] public delegate void PersoAssignedToMissionEventHandler(string personaId, string missionSlotId);

    private SubViewportContainer? _subViewportContainer;
    private SubViewport? _subViewport;
    private Node3D? _subSpaceRoot3D;
    private Camera3D? _subSpaceCamera3D;
    private Node3D? _personasContainer;
    private Node3D? _missionSlot;
    private Control? _handle;
    private Button? _otherLayerButton;
    private Label? _statusLabel;
    private Label? _liveLogLabel;

    private SlideState _slideState = SlideState.Closed;
    private SlideState? _inFlightTarget;
    private Tween? _slideTween;

    /// <summary>Set of persona ids currently assigned to a mission. The
    /// runtime probe keeps a local copy purely to flip the Label3D
    /// suffix on the persona ; production routes this through GameState
    /// (single source of truth) and listens to the same signal.</summary>
    private readonly HashSet<string> _personasInMission = new();

    /// <summary>Per-persona Node3D index, used to mutate the visual
    /// indicator after a drag-drop without re-parenting (Pattern P3).</summary>
    private readonly Dictionary<string, PersonaVisual> _personaVisuals = new();

    private const int LiveLogMaxLines = 10;
    private readonly Queue<string> _liveLog = new();

    // Cached signal handlers for disconnection discipline.
    private System.Action? _handleClickHandler;
    private System.Action? _otherLayerClickHandler;

    /// <summary>
    /// _EnterTree fires BEFORE _Ready on the same node, and BEFORE
    /// CallDeferred-scheduled work inside autoloads. Printing a banner
    /// here gives Didier a copy-paste-survivable sentinel that any
    /// missing canary lines below can be diagnosed against.
    ///
    /// <para>
    /// 2026-05-10 (Rune) -- Didier reported the runtime click logs
    /// landing in Output but no <c>[CANARY OK]</c> nor
    /// <c>[LABEL3D PREFLIGHT]</c> lines visible. Root cause turned out
    /// to be the Output panel scroll buffer + a partial copy-paste --
    /// the canary HAD run, but the early lines had scrolled off and
    /// were not in the snippet sent for review. Defence : a banner
    /// that is impossible to miss (5 stars in a row, distinct ASCII
    /// signature) and a try/catch wrap around the canary so a future
    /// silent exception cannot mask the prints either.
    /// </para>
    /// </summary>
    public override void _EnterTree()
    {
        GD.Print("*****");
        GD.Print("***** [PROBE Compagnie] _EnterTree -- scene is loading the CompagniePanelProbe class");
        GD.Print("***** [PROBE Compagnie] if you see this, the C# binary is current ; if not, F5/F6 did not rebuild");
        GD.Print("*****");
    }

    public override void _Ready()
    {
        // Banner : 5 stars + repeated tag so even an aggressive copy-paste
        // tail of the Output panel includes at least one identifiable boot
        // marker. See _EnterTree docstring for the 2026-05-10 incident.
        GD.Print("*****");
        GD.Print("***** [PROBE Compagnie] _Ready ENTERED -- starting boot sequence");
        GD.Print("*****");

        _subViewportContainer = GetNode<SubViewportContainer>(SubViewportContainerPath);
        _subViewport = GetNode<SubViewport>(SubViewportPath);
        _subSpaceRoot3D = GetNode<Node3D>(SubSpaceRoot3DPath);
        _subSpaceCamera3D = GetNode<Camera3D>(SubSpaceCamera3DPath);
        _personasContainer = GetNode<Node3D>(PersonasContainerPath);
        _missionSlot = GetNode<Node3D>(MissionSlotPath);
        _handle = GetNodeOrNull<Control>(HandleAreaPath);
        _otherLayerButton = GetNodeOrNull<Button>(OtherLayerButtonPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _liveLogLabel = GetNodeOrNull<Label>(LiveLogLabelPath);

        GD.Print("[PROBE Compagnie] node references resolved -- building sub-space contents");

        // ----------------------------------------------------------------
        // World3D defensive init (Godot 4.6 quirk discovered 2026-05-10).
        //
        // Setting own_world_3d=true in the .tscn does NOT lazy-init a
        // World3D in Godot 4.6 -- the SubViewport.World3D getter returns
        // null until you assign one explicitly. Symptom : Camera3D current,
        // 3D nodes in the tree, OwnWorld3D=true, yet the SubViewport
        // renders an empty image because the camera is current in a NULL
        // world. Round-2 preflight on commit 571b615 surfaced this :
        // OwnWorld3D=True, World3D=null, sharedWithRoot=False.
        //
        // Fix : assign new World3D() once at _Ready BEFORE BuildSubSpaceContents.
        // The .tscn-declared Node3D children (SubSpaceRoot3D, Camera3D,
        // Personas, MissionSlot) are already in the tree at this point ;
        // assigning World3D causes the SubViewport to migrate its 3D
        // descendants into the new world (documented Godot behaviour).
        // BuildSubSpaceContents then adds its programmatic Node3D into the
        // same world.
        //
        // Note this is conditioned on OwnWorld3D=true -- if a future .tscn
        // edit flips that off, we do nothing here and Inv. 2b in the
        // canary will catch it. Idempotent : if World3D is already non-null
        // (e.g. Godot 4.7+ fixes the lazy-init), we skip.
        if (_subViewport is not null && _subViewport.OwnWorld3D && _subViewport.World3D is null)
        {
            _subViewport.World3D = new World3D();
            GD.Print("[PROBE Compagnie] World3D was null despite OwnWorld3D=true -- assigned a fresh World3D (Godot 4.6 quirk)");
        }

        BuildSubSpaceContents();
        ApplySubViewportContainerPosition();
        WireSignals();

        // Pattern J canary : 7 invariants adapted for a SubViewport
        // sub-space probe. The flag and the camera now live in the
        // SubViewport, NOT the root viewport -- that distinction is
        // the new invariant compared to J2.
        //
        // Wrapped in try/catch so a future regression that throws
        // partway through the canary still produces an observable
        // PushError instead of silently swallowing the rest of _Ready.
        // Defensive belt-and-braces for the 2026-05-10 visibility
        // incident.
        GD.Print("===== [PREFLIGHT START] CompagniePanelProbe canary invariants =====");
        try
        {
            AssertCanaryInvariants();
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[PREFLIGHT THREW] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        GD.Print("===== [PREFLIGHT END] CompagniePanelProbe canary invariants =====");

        UpdateStatusLabel();
        Log("PROBE Compagnie ready -- click HANDLE (left edge) to slide panel ; click PERSONA to drag-drop to mission ; click OTHER LAYER to test auto-rabat");
    }

    public override void _ExitTree()
    {
        // Disconnection discipline.
        if (_handle is not null && _handleClickHandler is not null)
            _handle.GuiInput -= OnHandleGuiInputBridge;
        if (_otherLayerButton is not null && _otherLayerClickHandler is not null)
            _otherLayerButton.Pressed -= _otherLayerClickHandler;

        foreach (var pv in _personaVisuals.Values)
        {
            if (pv.Area3D is not null && pv.AreaInputHandler is not null)
                pv.Area3D.InputEvent -= pv.AreaInputHandler;
        }

        _handleClickHandler = null;
        _otherLayerClickHandler = null;
    }

    // ====================================================================
    // Sub-space build-out
    // ====================================================================

    /// <summary>
    /// Build the contents of the SubViewport sub-iso scene : taverne
    /// backdrop placeholder, 3 persona cubes, mission slot witness,
    /// per-persona Label3D mini-frame. All 3D, all children of the
    /// SubViewport. Idempotent : safe to call multiple times (it
    /// re-purposes nodes from the .tscn rather than spawning new ones).
    /// </summary>
    private void BuildSubSpaceContents()
    {
        if (_subSpaceRoot3D is null || _personasContainer is null || _missionSlot is null) return;

        // ---- Taverne backdrop placeholder : flat warm-earth quad on
        // the iso ground plane (Y=0). The real Mira-painted taverne
        // plugs in here via a Sprite3D textured with the asset.
        var backdrop = _subSpaceRoot3D.GetNodeOrNull<MeshInstance3D>("TaverneBackdrop");
        if (backdrop is null)
        {
            backdrop = new MeshInstance3D
            {
                Name = "TaverneBackdrop",
                Position = new Vector3(0, -1f, 0), // slightly below the personas so it does not z-fight
                Mesh = new BoxMesh { Size = new Vector3(400f, 0.5f, 200f) },
            };
            backdrop.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.42f, 0.30f, 0.20f, 1f), // umber brown placeholder
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            _subSpaceRoot3D.AddChild(backdrop);
        }

        // ---- 3 personas, evenly spaced along Z (which after iso
        // projection becomes a screen-Y spread + screen-X spread).
        for (var i = 0; i < DefaultPersonas.Count; i++)
        {
            var p = DefaultPersonas[i];
            // Layout : centred around X=0, spread along Z.
            var pos = new Vector3(
                x: -PersonaSpacingWorldUnits + i * PersonaSpacingWorldUnits,
                y: 0f,
                z: 0f);
            BuildPersonaVisual(p, pos);
        }

        // ---- Mission slot witness : a flat dark cube at +X offset
        // beyond the personas, target of the drag-drop Tween3D proxy.
        var slotMesh = _missionSlot.GetNodeOrNull<MeshInstance3D>("SlotMesh");
        if (slotMesh is null)
        {
            slotMesh = new MeshInstance3D
            {
                Name = "SlotMesh",
                Mesh = new BoxMesh { Size = new Vector3(48f, 8f, 48f) },
            };
            slotMesh.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.55f, 0.45f, 0.30f, 1f), // terracotta clair -- visibility patch (Didier 2026-05-10)
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            _missionSlot.AddChild(slotMesh);
        }
        _missionSlot.Position = new Vector3(PersonaSpacingWorldUnits * 1.8f, 0f, 0f);

        var slotLabel = _missionSlot.GetNodeOrNull<Label3D>("SlotLabel");
        if (slotLabel is null)
        {
            slotLabel = new Label3D
            {
                Name = "SlotLabel",
                Text = "[mission slot]",
                Position = new Vector3(0, 40f, 0),
                Modulate = new Color(0.95f, 0.92f, 0.80f, 1f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FontSize = 28,
                OutlineSize = 6,
                OutlineModulate = new Color(0, 0, 0, 1),
            };
            _missionSlot.AddChild(slotLabel);
        }
    }

    /// <summary>
    /// Build a single persona's visual + interaction surface :
    /// <list type="bullet">
    ///   <item>a colored cube <see cref="MeshInstance3D"/> as the
    ///         persona body (placeholder for Mira sprite),</item>
    ///   <item>a <see cref="Label3D"/> mini-frame floating above
    ///         showing "Name -- status",</item>
    ///   <item>an <see cref="Area3D"/> wrapping a <see cref="BoxShape3D"/>
    ///         click volume,</item>
    ///   <item>an InputEvent handler that fires the drag-drop tween
    ///         on left-click.</item>
    /// </list>
    /// All children of the personas container in the SubViewport.
    /// </summary>
    private void BuildPersonaVisual(PersonaPlaceholder persona, Vector3 pos)
    {
        if (_personasContainer is null) return;

        // Reuse if already present (idempotent rebuild on hot reload).
        var existing = _personasContainer.GetNodeOrNull<Node3D>(persona.Id);
        if (existing is not null)
        {
            _personaVisuals.Remove(persona.Id);
            existing.QueueFree();
        }

        var personaRoot = new Node3D
        {
            Name = persona.Id,
            Position = pos,
        };

        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new BoxMesh { Size = new Vector3(48f, 64f, 48f) },
            Position = new Vector3(0, 32f, 0), // half-height up so the cube sits ON the ground plane
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

        // Capture the persona id for the closure -- we need it inside
        // the handler.
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
    // Slide state machine glue
    // ====================================================================

    /// <summary>
    /// Apply the SubViewportContainer X position to the current slide
    /// state. Used at <c>_Ready</c> to seed the panel at Closed without
    /// animating, and would also be the entry point if we ever need to
    /// snap to a state programmatically.
    /// </summary>
    private void ApplySubViewportContainerPosition()
    {
        if (_subViewportContainer is null) return;
        var x = _slideState switch
        {
            SlideState.Open => OpenXPx,
            SlideState.Closed => ClosedXPx,
            // Sliding is transient ; if we hit this branch at startup,
            // pick the closer endpoint defensively.
            _ => ClosedXPx,
        };
        _subViewportContainer.Position = new Vector2(x, _subViewportContainer.Position.Y);
    }

    /// <summary>
    /// Drive the state machine with an intent and (re)apply the
    /// resulting tween. Centralised so click handlers, the auto-rabat
    /// signal, and tween-finished callbacks all go through the same
    /// path -- single source of truth.
    /// </summary>
    private void DispatchIntent(SlideIntent intent)
    {
        var d = DecideNextSlideState(_slideState, intent, _inFlightTarget);
        Log($"slide intent={intent} : {_slideState} -> {d.NextState} (target={d.SlideTarget?.ToString() ?? "n/a"})");
        _slideState = d.NextState;

        if (d.NextState == SlideState.Sliding && d.SlideTarget is { } target)
        {
            // Only restart the tween if the target actually changed
            // (prevents the spam-click anti-stuck regression).
            if (_inFlightTarget != target)
            {
                _inFlightTarget = target;
                StartSlideTween(target);
            }
        }
        else
        {
            _inFlightTarget = null;
        }

        UpdateStatusLabel();
    }

    /// <summary>
    /// Start the Tween3D-on-Node3D Pattern P2 -- BUT for v1 we tween the
    /// SubViewportContainer.Position (a Control's 2D pixel position on
    /// the parent screen) because the user-visible motion is a panel
    /// sliding across the parent screen, not a 3D entity sliding in
    /// world coords. The 3D contents of the SubViewport (personas,
    /// taverne) stay at their fixed positions in the SubViewport's own
    /// world ; the SubViewport-as-a-window-into-that-world is what
    /// slides.
    ///
    /// <para>
    /// This is a refinement of Pattern P2 worth flagging for the
    /// architecture doc : for a panel that <i>occludes the parent
    /// screen</i>, the slide is on the SubViewportContainer (parent
    /// screen coords). For a panel that <i>moves an entity within
    /// itself</i> (drag-drop, persona walking), the Tween3D is on the
    /// entity's Node3D inside the SubViewport. Both patterns coexist
    /// in the same probe -- the slide tween is parent-screen-2D, the
    /// drag-drop tween is sub-space-3D.
    /// </para>
    /// </summary>
    private void StartSlideTween(SlideState target)
    {
        if (_subViewportContainer is null) return;

        if (_slideTween is not null && _slideTween.IsValid())
            _slideTween.Kill();

        var targetX = target == SlideState.Open ? OpenXPx : ClosedXPx;
        _slideTween = CreateTween();
        _slideTween.TweenProperty(
                _subViewportContainer,
                "position",
                new Vector2(targetX, _subViewportContainer.Position.Y),
                SlideDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _slideTween.Finished += () => DispatchIntent(SlideIntent.TweenFinished);
    }

    // ====================================================================
    // Click handlers
    // ====================================================================

    /// <summary>Wire the handle, the auto-rabat debug button, the global
    /// signal subscription. Disconnection at <c>_ExitTree</c>.</summary>
    private void WireSignals()
    {
        if (_handle is not null)
        {
            _handle.GuiInput += OnHandleGuiInputBridge;
            // We use the GuiInput path on the handle Control rather
            // than a Button.Pressed because the handle is a stylised
            // rectangle (placeholder for the registry spine), not a
            // semantic Button.
            _handleClickHandler = () => DispatchIntent(SlideIntent.ClickHandle);
        }

        if (_otherLayerButton is not null)
        {
            _otherLayerClickHandler = () =>
            {
                Log("DEBUG button : simulating another layer opening -- firing AnotherLayerOpened intent + emitting OtherLayerOpened signal");
                // Emit the [Signal] for the production-shaped subscriber
                // contract (other panels could subscribe at integration).
                EmitSignal(SignalName.OtherLayerOpened);
                // ... and drive the slide state machine directly. In v1
                // there is only one panel ; in production this would be
                // routed via a global PanelLayerSignalBus autoload that
                // consumes the [Signal] on a separate hop.
                DispatchIntent(SlideIntent.AnotherLayerOpened);
            };
            _otherLayerButton.Pressed += _otherLayerClickHandler;
        }
    }

    private void OnHandleGuiInputBridge(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            DispatchIntent(SlideIntent.ClickHandle);
        }
    }

    private void OnPersonaInputEvent(PersonaPlaceholder persona, InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
            return;

        // Anti-double-fire : if the persona is already in mission, the
        // click is a no-op (v1 has no "recall from mission" UI yet).
        if (_personasInMission.Contains(persona.Id))
        {
            Log($"persona {persona.Id} already in mission -- click ignored (v1, no recall)");
            return;
        }

        // Pattern P3 : drag-drop = proxy Tween3D + state event, NOT
        // re-parenting. The original persona Node3D stays where it is
        // in the scene tree.
        StartDragDropProxy(persona);
    }

    /// <summary>
    /// Pattern P3 implementation : spawn a proxy Node3D at the
    /// persona's world position, Tween3D it toward the mission slot,
    /// at finish free the proxy + emit the assign-to-mission signal +
    /// flip the persona's Label3D suffix.
    ///
    /// <para>
    /// Crucially, we do NOT re-parent the persona's Node3D. Memory
    /// <c>project_wayfinders_npc_autonomy.md</c> : "the perso stays in
    /// the Compagnie ; the drag-drop is a tactical signal of the
    /// player". The proxy is a throwaway visual ; the authoritative
    /// state lives in the GameState (here in v1, in the local
    /// <see cref="_personasInMission"/> set).
    /// </para>
    /// </summary>
    private void StartDragDropProxy(PersonaPlaceholder persona)
    {
        if (!_personaVisuals.TryGetValue(persona.Id, out var pv) || _missionSlot is null || _subSpaceRoot3D is null)
            return;

        // Spawn a proxy : a small bright cube at the persona's world
        // position. Distinct color so Didier sees it clearly.
        var proxy = new MeshInstance3D
        {
            Name = $"DragProxy_{persona.Id}",
            Mesh = new BoxMesh { Size = new Vector3(40f, 40f, 40f) },
            Position = pv.Root.Position + new Vector3(0, 64f, 0),
        };
        proxy.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.85f, 0.30f, 1f), // bright drag tint
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _subSpaceRoot3D.AddChild(proxy);

        var slotPos = _missionSlot.Position + new Vector3(0, 32f, 0);
        var dragTween = CreateTween();
        dragTween.TweenProperty(
                proxy,
                "position",
                slotPos,
                DragDropDurationSec)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.InOut);
        dragTween.Finished += () => OnDragDropFinished(persona, proxy);

        Log($"drag-drop proxy spawned for persona={persona.Id} -> mission slot");
    }

    private void OnDragDropFinished(PersonaPlaceholder persona, MeshInstance3D proxy)
    {
        // Free the proxy (Pattern : Node not GC-managed ; QueueFree
        // explicitly).
        proxy.QueueFree();

        // Authoritative state mutation : add to in-mission set.
        // Production routes this through GameState ; v1 keeps it
        // local + logs the production-shaped event.
        _personasInMission.Add(persona.Id);

        // Resolve the persona visual once -- need it for both the
        // label flip and the live re-parenting canary check below.
        if (!_personaVisuals.TryGetValue(persona.Id, out var pv) || pv is null)
        {
            GD.PushError($"[CANARY FAIL] drag-drop finished for unknown persona {persona.Id} -- visual registry drift");
            return;
        }

        // Visual indicator on the Compagnie side : flip the Label3D
        // suffix to mark the persona as in-mission. The persona
        // Node3D is NOT re-parented -- pin invariant of NPC autonomy.
        pv.Label.Text = $"{persona.DisplayName} -- {persona.ShortStatus} [mission]";
        pv.Label.Modulate = new Color(1.0f, 0.85f, 0.30f, 1f);

        // Emit the production-shaped signal + log the structured event.
        var ev = ProduceAssignToMissionEvent(persona.Id, "mission_slot_1");
        EmitSignal(SignalName.PersoAssignedToMission, ev.PersonaId, ev.MissionSlotId);
        Log($"[EVENT {AssignToMissionEventName}] persona={ev.PersonaId} slot={ev.MissionSlotId}");

        // Anti-regression check at runtime : assert the persona
        // Node3D still has the personas container as parent.
        // This is the live counterpart of the xUnit
        // "drag-drop does not re-parent" pin contract.
        if (pv.Root.GetParent() != _personasContainer)
        {
            GD.PushError(
                $"[CANARY FAIL] persona {persona.Id} got re-parented during drag-drop -- " +
                $"NPC autonomy invariant violated. The Node3D MUST stay a child of the " +
                $"PersonasContainer ; only the GameState authoritative event flips.");
        }
        else
        {
            Log($"[CANARY OK] persona {persona.Id} still in PersonasContainer after drag-drop -- NPC autonomy invariant held");
        }
    }

    // ====================================================================
    // Pattern J canary -- 7 invariants adapted for SubViewport sub-space
    // ====================================================================

    /// <summary>
    /// Pattern J self-check, J5+-flavoured for a SubViewport sub-space
    /// probe. The 7 invariants are adapted because the picking
    /// here happens inside the <i>SubViewport</i>, not the root :
    /// <list type="number">
    ///   <item><c>SubViewport.PhysicsObjectPicking == true</c> (NOT
    ///         the root viewport -- that is the new bit. If we set
    ///         the root flag instead, persona picking inside the
    ///         SubViewport silently fails.)</item>
    ///   <item><c>SubSpaceCamera3D.Current == true</c> within the
    ///         SubViewport.</item>
    ///   <item>Each persona <c>Area3D.InputRayPickable == true</c>.</item>
    ///   <item>Each persona <c>Area3D.CollisionLayer != 0</c>.</item>
    ///   <item>Each persona <c>CollisionShape3D.Disabled == false</c>
    ///         and <c>.Shape != null</c>.</item>
    ///   <item>SubViewportContainer renders the SubViewport
    ///         (<c>SubViewportContainer.GetChild(0) is SubViewport</c>)
    ///         -- this is the easy-to-miss invariant : if the .tscn
    ///         drift puts the SubViewport elsewhere in the tree, the
    ///         Container blits a default bgcolor instead.</item>
    ///   <item>Personas Node3D parent is still the personas container
    ///         (orthogonal check : we read the parent live, not the
    ///         registry, to catch a theoretical re-parenting bug
    ///         introduced before any drag-drop).</item>
    /// </list>
    /// Invariant 7 here replaces the J2 "rhombus prism poles" check --
    /// that one is cell-grid-specific. The new orthogonality check
    /// directly defends the NPC autonomy contract from the very first
    /// frame.
    /// </summary>
    private void AssertCanaryInvariants()
    {
        // Inv. 1 : SubViewport's own picking flag (NOT root viewport).
        if (_subViewport is null)
        {
            GD.PushError("[CANARY FAIL] SubViewport not resolvable -- panel has no sub-space to render.");
            return;
        }
        if (!_subViewport.PhysicsObjectPicking)
        {
            // Auto-correct since this is a probe (cheaper than asking
            // Didier to flip a flag in the inspector).
            _subViewport.PhysicsObjectPicking = true;
            GD.PushWarning("[CANARY AUTO] SubViewport.PhysicsObjectPicking was false -- forced to true at _Ready.");
        }
        else
        {
            GD.Print("[CANARY OK] SubViewport.PhysicsObjectPicking = true");
        }

        // Inv. 2 : Camera3D current INSIDE the SubViewport, AND looking
        // at the scene origin. The .tscn ships an iso-flavoured
        // Transform3D basis ; we re-derive it defensively at _Ready via
        // LookAt so a malformed basis (sign flip on the Z column, drift
        // from a Godot .tscn rewrite, etc.) cannot silently leave the
        // camera pointing AWAY from the personas.
        //
        // Visibility regression 2026-05-10 : Didier could not see any
        // Label3D -- root-cause investigation traced this to a Z-column
        // sign flip in the .tscn that pointed the camera forward at
        // (+0.7071, +0.3536, -0.6124) ie. away from origin. LookAt is
        // idempotent for a correctly-authored .tscn and corrective for
        // a drifted one. Cheap belt-and-braces.
        //
        // 2026-05-10 (round 2) -- preflight confirmed forward points to
        // origin AND Label3D project inside the 560x560 SubViewport,
        // yet Didier still sees nothing. Extended dump below for the
        // 7 newly-suspect surfaces : SubViewport render flags, World3D
        // ownership, Container rect, PanelLayer ordering, all-Camera3D
        // enumeration.
        if (_subSpaceCamera3D is null)
        {
            GD.PushError("[CANARY FAIL] SubSpaceCamera3D not resolvable.");
        }
        else
        {
            _subSpaceCamera3D.MakeCurrent();
            // Force the camera to look at the scene centre (origin in
            // sub-space coords). Up vector = world Y (standard iso).
            _subSpaceCamera3D.LookAt(new Vector3(0, 0, 0), Vector3.Up);
            // Forward in Godot = -Z in camera basis. Compute it for the
            // log so we have eyes-on proof in the Output panel.
            var forward = -_subSpaceCamera3D.GlobalTransform.Basis.Z;
            GD.Print(
                $"[CANARY OK] SubSpaceCamera3D.Current=true " +
                $"globalPos={_subSpaceCamera3D.GlobalPosition} " +
                $"forward={forward} " +
                $"projection={_subSpaceCamera3D.Projection} " +
                $"size={_subSpaceCamera3D.Size} " +
                $"near={_subSpaceCamera3D.Near} far={_subSpaceCamera3D.Far} " +
                $"cullMask=0x{_subSpaceCamera3D.CullMask:X}");
        }

        // ----------------------------------------------------------------
        // Round-2 diagnostics (2026-05-10) -- 7 new dumps for the "panel
        // opens but nothing visible" investigation. Each one targets a
        // distinct "could be the bug" surface so the Output panel pins
        // down the cause unambiguously.
        // ----------------------------------------------------------------

        // Inv. 2a : SubViewport render flags (RenderTargetUpdateMode,
        // Disable3D, TransparentBg, Size). If UpdateMode is not Always,
        // the SubViewport silently stops re-rendering. If Disable3D is
        // true, no 3D pass at all. TransparentBg combined with the
        // Environment can produce a fully transparent blit.
        GD.Print(
            $"[VIEWPORT FLAGS] SubViewport " +
            $"RenderTargetUpdateMode={_subViewport.RenderTargetUpdateMode} " +
            $"(Always=4, WhenVisible=2, Once=1, Disabled=0) " +
            $"Disable3D={_subViewport.Disable3D} " +
            $"TransparentBg={_subViewport.TransparentBg} " +
            $"Size={_subViewport.Size} " +
            $"HandleInputLocally={_subViewport.HandleInputLocally} " +
            $"Use3DAudio={_subViewport.AudioListenerEnable3D}");

        // Inv. 2b : SubViewport.OwnWorld3D + World3D ref. The single
        // most likely cause for "Camera current, Label3D in tree, but
        // empty render" : the SubViewport shares its parent's World3D
        // (own_world_3d=false default) so the 3D nodes are siblings in
        // the parent World3D, but the SubViewport's camera tries to
        // render its OWN empty world. Result : empty SubViewport.
        // The fix is own_world_3d=true on the SubViewport .tscn.
        var hasOwnWorld = _subViewport.OwnWorld3D;
        var world3D = _subViewport.World3D;
        var parentViewportWorld = _subViewport.GetTree()?.Root?.World3D;
        GD.Print(
            $"[VIEWPORT WORLD] SubViewport " +
            $"OwnWorld3D={hasOwnWorld} " +
            $"World3D={(world3D is null ? "null" : world3D.GetInstanceId().ToString())} " +
            $"parentRootWorld3D={(parentViewportWorld is null ? "null" : parentViewportWorld.GetInstanceId().ToString())} " +
            $"sharedWithRoot={(world3D is not null && parentViewportWorld is not null && world3D.GetInstanceId() == parentViewportWorld.GetInstanceId())}");
        if (!hasOwnWorld)
        {
            GD.PushError(
                "[CANARY FAIL] SubViewport.OwnWorld3D=false -- the 3D children of the " +
                "SubViewport live in the PARENT viewport's World3D, NOT the SubViewport's. " +
                "The Camera3D inside is current but renders an empty world. " +
                "Fix : set own_world_3d=true on the SubViewport node in the .tscn.");
        }
        // Defense in depth (2026-05-10) : even with OwnWorld3D=true, Godot 4.6
        // does NOT lazy-init the World3D -- the getter returns null. _Ready
        // assigns a fresh World3D() to compensate ; the canary asserts the
        // assignment took effect, so a future regression (e.g. someone moves
        // the assignment after BuildSubSpaceContents, or removes it on the
        // assumption that Godot 4.7 fixes the quirk without verifying) gets
        // caught at the very next probe boot.
        else if (world3D is null)
        {
            GD.PushError(
                "[CANARY FAIL] SubViewport.OwnWorld3D=true BUT World3D=null -- the _Ready " +
                "defensive assignment did not run, or ran too late, or was removed. The " +
                "Camera3D is current in a null world ; SubViewport will blit empty. Fix : " +
                "verify the _subViewport.World3D = new World3D() call near the top of " +
                "_Ready, before BuildSubSpaceContents.");
        }
        else
        {
            GD.Print($"[CANARY OK] SubViewport.World3D={world3D.GetInstanceId()} (non-null after _Ready defensive init)");
        }

        // Inv. 2c : enumerate every Camera3D in the whole scene and log
        // which one(s) report Current=true. MakeCurrent is not
        // idempotent across viewports -- another Camera3D somewhere in
        // the parent tree could have stolen the "current" slot for the
        // World3D the SubViewport is sharing (see Inv. 2b).
        var allCameras = new List<Camera3D>();
        var sceneRoot = GetTree()?.Root;
        if (sceneRoot is not null)
            CollectCamera3D(sceneRoot, allCameras);
        GD.Print($"[CAMERA ENUM] found {allCameras.Count} Camera3D in the whole tree");
        foreach (var cam in allCameras)
        {
            var camViewport = cam.GetViewport();
            GD.Print(
                $"[CAMERA] path={cam.GetPath()} " +
                $"Current={cam.Current} " +
                $"viewport={(camViewport is null ? "null" : camViewport.GetType().Name + ":" + camViewport.GetInstanceId())} " +
                $"globalPos={cam.GlobalPosition}");
        }

        // Inv. 3-5 : per-persona Area3D + Shape3D health.
        var personasOk = 0;
        foreach (var (id, pv) in _personaVisuals)
        {
            var areaOk = pv.Area3D is not null
                         && pv.Area3D.InputRayPickable
                         && pv.Area3D.CollisionLayer != 0u;
            var shapeOk = pv.Area3D is not null
                          && pv.Area3D.GetNodeOrNull<CollisionShape3D>("Shape") is { Disabled: false, Shape: not null };
            if (!areaOk)
                GD.PushError($"[CANARY FAIL] persona {id} Area3D mis-configured (pickable / layer)");
            else if (!shapeOk)
                GD.PushError($"[CANARY FAIL] persona {id} CollisionShape3D mis-configured (disabled / null shape)");
            else
                personasOk++;
        }
        GD.Print($"[CANARY OK] {personasOk}/{_personaVisuals.Count} personas Area3D + Shape3D ready");

        // Inv. 6 : SubViewportContainer wraps the SubViewport (NOT
        // an empty container that would blit bgcolor).
        if (_subViewportContainer is null)
        {
            GD.PushError("[CANARY FAIL] SubViewportContainer not resolvable.");
        }
        else if (_subViewportContainer.GetChildCount() == 0
                 || _subViewportContainer.GetChild(0) is not SubViewport)
        {
            GD.PushError(
                "[CANARY FAIL] SubViewportContainer first child is NOT a SubViewport -- " +
                "the panel will blit a default bgcolor. Re-check the .tscn wiring.");
        }
        else
        {
            GD.Print("[CANARY OK] SubViewportContainer wraps SubViewport at child[0]");
        }

        // Inv. 6a (round 2 2026-05-10) : Container actual rect on the
        // parent screen. If the Container's Position is offscreen at
        // _Ready (Closed state seeds it at -560x), or if Stretch is
        // false and the Container size is 0x0 (silently swallowing the
        // SubViewport content), the panel renders correctly into the
        // SubViewport but blits to nothing. Also dump GlobalRect in case
        // a parent CanvasLayer or transform offsets the visible area.
        if (_subViewportContainer is not null)
        {
            GD.Print(
                $"[CONTAINER RECT] SubViewportContainer " +
                $"Position={_subViewportContainer.Position} " +
                $"Size={_subViewportContainer.Size} " +
                $"GlobalPosition={_subViewportContainer.GlobalPosition} " +
                $"GlobalRect={_subViewportContainer.GetGlobalRect()} " +
                $"Stretch={_subViewportContainer.Stretch} " +
                $"Visible={_subViewportContainer.Visible} " +
                $"VisibleInTree={_subViewportContainer.IsVisibleInTree()} " +
                $"Modulate={_subViewportContainer.Modulate} " +
                $"SelfModulate={_subViewportContainer.SelfModulate}");

            // Sanity : at boot in Closed state we EXPECT Position.X=ClosedXPx
            // (-560), so the Container is fully offscreen and Didier
            // should see NOTHING from the panel until he clicks the
            // handle. This is by design ; the print above just makes
            // the design observable rather than implicit.
            if (_slideState == SlideState.Closed
                && _subViewportContainer.Position.X > -100f)
            {
                GD.PushWarning(
                    "[CONTAINER POS DRIFT] state=Closed but ContainerX > -100. " +
                    "Did the .tscn change the seed position ? Expected ~-560.");
            }
        }

        // Inv. 6b : enumerate every CanvasLayer and log layer index +
        // visibility. If two CanvasLayers stack at the same index, draw
        // order is undefined ; if a higher-layer opaque CanvasLayer
        // covers the SubViewportContainer, the panel renders correctly
        // but is hidden behind another layer's BgRect. Dual of the
        // existing "CanvasLayer hides Camera2D" trap from
        // feedback_godot_rendering_input_traps.md.
        var allLayers = new List<CanvasLayer>();
        if (sceneRoot is not null)
            CollectCanvasLayer(sceneRoot, allLayers);
        GD.Print($"[CANVAS LAYERS] found {allLayers.Count} CanvasLayer in the whole tree");
        foreach (var layer in allLayers)
        {
            GD.Print(
                $"[CANVAS LAYER] path={layer.GetPath()} " +
                $"Layer={layer.Layer} " +
                $"Visible={layer.Visible}");
        }

        // Inv. 7 : persona NPC autonomy parent invariant -- live read
        // of the actual scene tree, NOT the registry. This catches a
        // theoretical re-parenting bug introduced before any
        // drag-drop has fired.
        var autonomyOk = true;
        foreach (var (id, pv) in _personaVisuals)
        {
            if (pv.Root.GetParent() != _personasContainer)
            {
                GD.PushError(
                    $"[CANARY FAIL] persona {id} parent is NOT PersonasContainer at _Ready " +
                    $"(actual: {pv.Root.GetParent()?.Name ?? "null"}). NPC autonomy invariant " +
                    $"would be violated before any drag-drop has run.");
                autonomyOk = false;
            }
        }
        if (autonomyOk)
            GD.Print($"[CANARY OK] all {_personaVisuals.Count} personas live under PersonasContainer (NPC autonomy invariant held at boot)");

        // Inv. 8 (NEW 2026-05-10) : Label3D rendering preflight. Memory
        // feedback_godot_rendering_input_traps.md : "preflight GD.Print
        // mandatory" before claiming a rendering bug is solved. Dump
        // the global position, visibility, render layer, and modulate
        // alpha of every Label3D so a follow-up visual regression
        // (Didier reports "I cannot see Label X") has observable proof
        // in the Output panel of where the label thinks it is and what
        // its visibility flags are.
        DumpLabel3DRenderingPreflight();
    }

    /// <summary>
    /// Walk every Label3D under the SubSpaceRoot3D and log its
    /// rendering-relevant state. Read-only diagnostic ; never mutates.
    /// Triggered from <see cref="AssertCanaryInvariants"/>.
    /// </summary>
    private void DumpLabel3DRenderingPreflight()
    {
        if (_subSpaceRoot3D is null)
        {
            GD.PushError("[CANARY FAIL] SubSpaceRoot3D not resolvable -- cannot run Label3D preflight.");
            return;
        }

        var labels = new List<Label3D>();
        CollectLabel3D(_subSpaceRoot3D, labels);

        GD.Print($"[LABEL3D PREFLIGHT] found {labels.Count} Label3D under SubSpaceRoot3D");

        foreach (var lbl in labels)
        {
            // IsVisibleInTree = AND of every ancestor's Visible flag,
            // which is the only flag Godot actually consults at render
            // time. Visible alone can mislead if a parent Node3D was
            // hidden.
            var visibleInTree = lbl.IsVisibleInTree();
            var globalPos = lbl.GlobalPosition;
            var modulate = lbl.Modulate;
            var renderLayer = lbl.Layers;
            var path = lbl.GetPath();

            // Camera frustum sanity : project the label world-pos to
            // the SubViewport NDC coords. Outside [-1, +1] on either
            // axis means the label is off-screen ; behind the camera
            // means the iso math is wrong.
            string ndcStr = "n/a";
            if (_subSpaceCamera3D is not null)
            {
                var ndc = _subSpaceCamera3D.UnprojectPosition(globalPos);
                ndcStr = $"screenPx={ndc} (SubViewport size={_subViewport?.Size})";
            }

            GD.Print(
                $"[LABEL3D] path={path} " +
                $"text=\"{lbl.Text}\" " +
                $"globalPos={globalPos} " +
                $"visibleInTree={visibleInTree} " +
                $"modulate={modulate} " +
                $"renderLayers=0x{renderLayer:X} " +
                $"noDepthTest={lbl.NoDepthTest} " +
                $"billboard={lbl.Billboard} " +
                $"fontSize={lbl.FontSize} " +
                $"projected: {ndcStr}");
        }

        // Also dump the personas container itself in case its Visible
        // flag was flipped off (would cascade to children).
        if (_personasContainer is not null)
            GD.Print(
                $"[LABEL3D PREFLIGHT] PersonasContainer.Visible={_personasContainer.Visible} " +
                $"VisibleInTree={_personasContainer.IsVisibleInTree()} " +
                $"GlobalPos={_personasContainer.GlobalPosition}");
        if (_missionSlot is not null)
            GD.Print(
                $"[LABEL3D PREFLIGHT] MissionSlot.Visible={_missionSlot.Visible} " +
                $"VisibleInTree={_missionSlot.IsVisibleInTree()} " +
                $"GlobalPos={_missionSlot.GlobalPosition}");
    }

    private static void CollectLabel3D(Node node, List<Label3D> sink)
    {
        if (node is Label3D lbl)
            sink.Add(lbl);
        foreach (var child in node.GetChildren())
            CollectLabel3D(child, sink);
    }

    /// <summary>Recursively collect every Camera3D in the tree.
    /// Used by Inv. 2c to enumerate "current camera" candidates and
    /// catch a Camera3D outside the SubViewport stealing the slot.</summary>
    private static void CollectCamera3D(Node node, List<Camera3D> sink)
    {
        if (node is Camera3D cam)
            sink.Add(cam);
        foreach (var child in node.GetChildren())
            CollectCamera3D(child, sink);
    }

    /// <summary>Recursively collect every CanvasLayer in the tree.
    /// Used by Inv. 6b to enumerate layer indices and catch an opaque
    /// higher-layer CanvasLayer covering the SubViewportContainer
    /// (the dual of the J2 trap "CanvasLayer hides Camera2D").</summary>
    private static void CollectCanvasLayer(Node node, List<CanvasLayer> sink)
    {
        if (node is CanvasLayer layer)
            sink.Add(layer);
        foreach (var child in node.GetChildren())
            CollectCanvasLayer(child, sink);
    }

    // ====================================================================
    // Logging + status overlay
    // ====================================================================

    private void UpdateStatusLabel()
    {
        if (_statusLabel is null) return;
        _statusLabel.Text =
            $"STATE: {_slideState}\n" +
            (_inFlightTarget is { } t ? $"target -> {t}\n" : "") +
            $"in mission: [{string.Join(", ", _personasInMission)}]";
    }

    private void Log(string message)
    {
        var line = $"[Compagnie] {message}";
        GD.Print(line);

        _liveLog.Enqueue(line);
        while (_liveLog.Count > LiveLogMaxLines)
            _liveLog.Dequeue();

        if (_liveLogLabel is not null)
            _liveLogLabel.Text = string.Join("\n", _liveLog);
    }

    /// <summary>
    /// Per-persona visual + interaction registry entry. Captured at
    /// build-time so we can flip the label and read the live parent
    /// without re-walking the scene tree on every event.
    /// </summary>
    private sealed record PersonaVisual(
        Node3D Root,
        MeshInstance3D Body,
        Label3D Label,
        Area3D Area3D,
        Area3D.InputEventEventHandler AreaInputHandler);
}
