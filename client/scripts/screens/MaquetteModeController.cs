using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Godot-bound controller for the eM ↔ eT bascule (Étape B —
/// bascule eM → eT au clic mission, Rune, 2026-05-23). Sibling of the
/// other shell controllers under <c>GameScreen</c>, lives on a plain
/// <see cref="Node"/> with no children of its own.
///
/// <para>
/// <b>What it owns — and what it does not.</b> It owns the eM/eT mode
/// state (<see cref="CurrentMode"/>, <see cref="CurrentDistrictId"/>),
/// the texture swap on the maquette <see cref="IsoBoard"/>, the camera
/// reparking on each transition, and the visibility toggle of the eM
/// overlay layers (<see cref="HalfgateMarkerLayer"/> +
/// <see cref="MissionPoiLayer"/>). It does NOT own the click that
/// triggers the bascule (<see cref="MissionPoiLayer"/> emits
/// <c>MissionPoiClicked</c>) nor the Escape key that returns to eM
/// (<c>GameScreen._UnhandledInput</c> routes the key here). It does
/// NOT touch the desk SubViewports, the HUD, or the front/back decor —
/// the four-layer shell is preserved verbatim.
/// </para>
///
/// <para>
/// <b>State authority.</b> The controller holds the maquette's display
/// mode ; it also writes the resolved district id to
/// <see cref="GameState.SetCurrentDistrictId"/> on every transition,
/// because <c>GameState</c> is the cross-scene state authority
/// (NPC-autonomy lock 2026-05-09). A future system that wants to know
/// "is the player looking at lower_quays right now" reads
/// <c>GameState.LocationContext.CurrentDistrictId</c>, not this node.
/// The HUD bottom label that J7's spec mentions will subscribe to
/// <c>GameState.LocationContextChanged</c> and stay decoupled from this
/// controller.
/// </para>
///
/// <para>
/// <b>The transition shape.</b>
/// <list type="number">
///   <item><c>MissionPoiClicked(missionId, _)</c> arrives from the
///         <see cref="MissionPoiLayer"/>.</item>
///   <item>Resolve <c>mission → district</c> via
///         <see cref="MissionDistrictResolverLogic"/> against the
///         <c>WorldMapService</c> cache. Three-branch cascade : exact
///         match, first-of-city fallback (the M1 norm), failure.</item>
///   <item>Plan the swap via <see cref="MaquetteModeLogic.PlanEnterDistrict"/>.
///         A second click on the same mission's district is idempotent
///         — the planner returns a no-op outcome and we log it.</item>
///   <item>Apply : <see cref="IsoBoard.SetBackgroundTexturePath"/>,
///         re-park the maquette camera on the new content centre +
///         re-clamp, toggle the overlay layers off, write the district
///         id into <c>GameState</c>.</item>
/// </list>
/// The Escape path runs the inverse :
/// <see cref="MaquetteModeLogic.PlanReturnToWorldMap"/> → restore the
/// eM texture → re-park camera on eM content centre → toggle overlays
/// back on → clear the district id in <c>GameState</c>.
/// </para>
///
/// <para>
/// <b>Signal-disconnect discipline (trap #10).</b> We connect to
/// <see cref="MissionPoiLayer.MissionPoiClicked"/> at <c>_Ready</c>
/// against the cached <see cref="_missionPoiLayer"/> reference and
/// explicitly disconnect on <c>_ExitTree</c>. Same pattern as
/// <see cref="HalfgateMarkerLayer"/> /
/// <see cref="MissionPoiLayer"/> against the long-lived autoloads.
/// </para>
///
/// <para>
/// <b>Why a separate Node and not extra logic on <c>GameScreen</c>.</b>
/// <c>GameScreen.cs</c> is already 1100 lines of shell wiring. Putting
/// the bascule there would mix two concerns : the shell's geometry +
/// click routing + decor calibration with the maquette content's
/// lifecycle. They have different change rates (the shell is locked
/// 4-layer ; the maquette content will grow with J7/J8/J9 — multi-
/// district eT, recruit overlays, etc.). One controller per concern,
/// each pinable independently.
/// </para>
/// </summary>
public partial class MaquetteModeController : Node
{
    /// <summary>Console prefix for every diagnostic line this node emits.</summary>
    private const string Tag = "[GAME-MODE]";

    /// <summary>
    /// The maquette board this controller swaps the background of —
    /// resolved at <c>_Ready</c> from the scene-tree path exported in
    /// the inspector. Required ; a null reference is a scene-wiring
    /// bug, logged loudly so the F6 smoke fails visibly rather than
    /// silently no-oping the bascule.
    /// </summary>
    [Export]
    public NodePath MaquetteBoardPath { get; set; } = new();

    /// <summary>
    /// The <see cref="MissionPoiLayer"/> we subscribe to. Resolved at
    /// <c>_Ready</c> from this path. Sibling under the maquette in the
    /// shell scene tree.
    /// </summary>
    [Export]
    public NodePath MissionPoiLayerPath { get; set; } = new();

    /// <summary>
    /// The <see cref="HalfgateMarkerLayer"/> we toggle visibility on.
    /// Optional — if null we just skip the toggle (placement is a
    /// no-op layer on eT either way).
    /// </summary>
    [Export]
    public NodePath HalfgateMarkerLayerPath { get; set; } = new();

    /// <summary>
    /// The <see cref="Camera2D"/> the maquette pans through — needed to
    /// re-park on the new content centre on every transition. Same
    /// node <c>GameScreen.MapCamera2D</c> consumes for pan.
    /// </summary>
    [Export]
    public NodePath MapCameraPath { get; set; } = new();

    /// <summary>
    /// The current maquette mode. Read-only outside this controller ;
    /// mutated only inside an applied transition.
    /// </summary>
    public MaquetteMode CurrentMode { get; private set; } = MaquetteMode.WorldMap;

    /// <summary>
    /// The current district id when <see cref="CurrentMode"/> is
    /// <see cref="MaquetteMode.DistrictMap"/>, null on eM.
    /// </summary>
    public string? CurrentDistrictId { get; private set; }

    /// <summary>
    /// Fired after a transition has been applied (or after a planned
    /// no-op has been logged). Consumers that want to react to the
    /// mode change subscribe here. Carries the new mode and the
    /// district id (empty string on eM, the slug on eT).
    /// </summary>
    [Signal]
    public delegate void MaquetteModeChangedEventHandler(int newMode, string districtIdOrEmpty);

    private IsoBoard? _maquetteBoard;
    private MissionPoiLayer? _missionPoiLayer;
    private HalfgateMarkerLayer? _halfgateMarkerLayer;
    private Camera2D? _mapCamera;
    private WorldMapService? _worldMapService;
    private GameState? _gameState;

    public override void _Ready()
    {
        GD.Print($"{Tag} _Ready: MaquetteModeController entering tree, " +
                 $"initialMode={CurrentMode}, initialDistrictId='(none)'.");

        _maquetteBoard = ResolveOrLog<IsoBoard>(MaquetteBoardPath, nameof(MaquetteBoardPath));
        _missionPoiLayer = ResolveOrLog<MissionPoiLayer>(MissionPoiLayerPath, nameof(MissionPoiLayerPath));
        _halfgateMarkerLayer = GetNodeOrNull<HalfgateMarkerLayer>(HalfgateMarkerLayerPath);
        _mapCamera = ResolveOrLog<Camera2D>(MapCameraPath, nameof(MapCameraPath));

        _worldMapService = GetNodeOrNull<WorldMapService>("/root/WorldMapService");
        if (_worldMapService is null)
        {
            GD.PushWarning($"{Tag} WorldMapService autoload missing — eM↔eT " +
                           $"bascule will refuse every click (graceful degrade).");
        }

        _gameState = GetNodeOrNull<GameState>("/root/GameState");
        if (_gameState is null)
        {
            GD.PushWarning($"{Tag} GameState autoload missing — district id " +
                           $"will not be written into LocationContext.");
        }

        if (_missionPoiLayer is not null)
        {
            _missionPoiLayer.MissionPoiClicked += OnMissionPoiClicked;
            GD.Print($"{Tag} subscribed to MissionPoiLayer.MissionPoiClicked.");
        }
    }

    public override void _ExitTree()
    {
        if (_missionPoiLayer is not null && GodotObject.IsInstanceValid(_missionPoiLayer))
        {
            _missionPoiLayer.MissionPoiClicked -= OnMissionPoiClicked;
        }
    }

    /// <summary>
    /// Handler for <see cref="MissionPoiLayer.MissionPoiClicked"/>. Runs
    /// the resolver cascade, the transition planner, then applies the
    /// swap if the planner says so. Failures (no referential, mission
    /// unresolved, district unknown) log + stay on eM — graceful
    /// degrade, exactly the discipline the rest of the shell uses.
    /// </summary>
    private void OnMissionPoiClicked(string missionId, Vector2 worldPosition)
    {
        GD.Print($"{Tag} OnMissionPoiClicked: missionId='{missionId}' " +
                 $"worldPos=({worldPosition.X:0.0},{worldPosition.Y:0.0}) " +
                 $"currentMode={CurrentMode} currentDistrictId='{CurrentDistrictId ?? "(none)"}'.");

        if (_worldMapService?.World is null)
        {
            GD.PushWarning($"{Tag} cannot resolve mission '{missionId}' — world " +
                           $"referential not loaded. Staying on eM.");
            return;
        }

        // 1. Look up the mission DTO. MissionStore is the authority ;
        //    a click on a stale pin (mission already removed) is rare
        //    but possible during a tick poll race, so we tolerate a
        //    miss by logging.
        var missionStore = GetNodeOrNull<MissionStore>("/root/MissionStore");
        EmergentMissionDto? mission = null;
        if (missionStore is not null)
        {
            foreach (var m in missionStore.AllActive)
            {
                if (m?.Id == missionId) { mission = m; break; }
            }
        }
        if (mission is null)
        {
            GD.PushWarning($"{Tag} mission '{missionId}' not in MissionStore.AllActive — " +
                           $"clicked pin was stale. Staying on eM.");
            return;
        }

        // 2. Resolve mission → district via the cascade.
        var resolution = MissionDistrictResolverLogic.ResolveDistrict(
            mission, _worldMapService.World.Cities, _worldMapService.World.Districts);

        GD.Print($"{Tag} mission '{missionId}' target_poi='{mission.TargetPoi}' " +
                 $"resolved via {resolution.Method} -> district='{resolution.DistrictId ?? "(null)"}' " +
                 $"diagnostic='{resolution.Diagnostic}'.");

        if (resolution.Method == MissionDistrictResolverLogic.ResolutionMethod.Failure
            || resolution.DistrictId is null)
        {
            GD.PushWarning($"{Tag} could not resolve a district for mission " +
                           $"'{missionId}' — {resolution.Diagnostic}. Staying on eM.");
            return;
        }

        // 3. Plan the transition.
        var plan = MaquetteModeLogic.PlanEnterDistrict(
            CurrentMode, CurrentDistrictId, resolution.DistrictId);

        if (!plan.ShouldApply)
        {
            GD.Print($"{Tag} planner: {plan.Diagnostic} — no-op.");
            return;
        }

        // 4. Apply.
        ApplyTransition(plan, missionId);
    }

    /// <summary>
    /// Public API the <c>GameScreen</c> Escape handler calls to ask for
    /// a return to eM. A no-op when already on eM ; otherwise runs the
    /// inverse transition.
    /// </summary>
    public void RequestReturnToWorldMap()
    {
        GD.Print($"{Tag} RequestReturnToWorldMap: currentMode={CurrentMode} " +
                 $"currentDistrictId='{CurrentDistrictId ?? "(none)"}'.");

        var plan = MaquetteModeLogic.PlanReturnToWorldMap(CurrentMode, CurrentDistrictId);
        if (!plan.ShouldApply)
        {
            GD.Print($"{Tag} planner: {plan.Diagnostic} — no-op.");
            return;
        }

        ApplyTransition(plan, missionIdOrEmpty: string.Empty);
    }

    /// <summary>
    /// Apply one transition outcome : swap the texture, re-park the
    /// camera on the new content centre, toggle the overlay layers,
    /// update the cross-scene state. Logs the canonical
    /// <c>switched eM → eT</c> / <c>switched eT → eM</c> line the F6
    /// smoke watches for.
    /// </summary>
    private void ApplyTransition(MaquetteModeLogic.TransitionOutcome plan, string missionIdOrEmpty)
    {
        if (_maquetteBoard is null)
        {
            GD.PushError($"{Tag} cannot apply transition '{plan.Diagnostic}' — " +
                         $"maquette board reference is null. Scene wiring bug.");
            return;
        }

        // Swap the texture first ; if it fails we abort, leaving state
        // intact (no half-transition).
        if (!_maquetteBoard.SetBackgroundTexturePath(plan.TargetTexturePath))
        {
            GD.PushError($"{Tag} texture swap to '{plan.TargetTexturePath}' " +
                         $"failed — aborting transition '{plan.Diagnostic}'.");
            return;
        }

        // Re-park the camera on the new content geometric centre + clamp.
        ReparkMapCamera();

        // Toggle the eM overlay layers off in eT, on in eM. We toggle
        // visibility rather than freeing the nodes so the marker /
        // mission-poi state survives the round-trip — when we return
        // to eM the same node carries the same subscriptions and the
        // pins are still there.
        bool isWorldMap = plan.TargetMode == MaquetteMode.WorldMap;
        if (_missionPoiLayer is not null)
        {
            _missionPoiLayer.Visible = isWorldMap;
        }
        if (_halfgateMarkerLayer is not null)
        {
            _halfgateMarkerLayer.Visible = isWorldMap;
        }

        // Update state (this controller's + GameState's).
        var oldMode = CurrentMode;
        var oldDistrictId = CurrentDistrictId;
        CurrentMode = plan.TargetMode;
        CurrentDistrictId = plan.TargetDistrictId;

        _gameState?.SetCurrentDistrictId(plan.TargetDistrictId);

        // Canonical log lines the roadmap exit criteria watch for.
        if (plan.TargetMode == MaquetteMode.DistrictMap)
        {
            GD.Print($"{Tag} switched eM → eT (district='{plan.TargetDistrictId}', " +
                     $"missionId='{missionIdOrEmpty}').");
        }
        else
        {
            GD.Print($"{Tag} switched eT → eM (was district='{oldDistrictId}').");
        }

        EmitSignal(SignalName.MaquetteModeChanged,
            (int)CurrentMode,
            CurrentDistrictId ?? string.Empty);
    }

    /// <summary>
    /// Re-park the maquette <see cref="Camera2D"/> on the new content's
    /// geometric centre and let the next pan event re-clamp. Mirrors
    /// the parking discipline at <c>GameScreen._Ready</c>.
    /// </summary>
    private void ReparkMapCamera()
    {
        if (_mapCamera is null || _maquetteBoard is null) return;

        var contentSize = _maquetteBoard.GetBackgroundTextureSizeOrZero();
        if (contentSize == Vector2.Zero)
        {
            // Texture loaded but reported zero size — shouldn't happen on
            // a successful swap. Park at origin and log.
            GD.PushWarning($"{Tag} new background reports zero size — " +
                           $"camera parked at origin.");
            _mapCamera.Position = Vector2.Zero;
            return;
        }

        // The maquette's Sprite2D is Centered=false at the board origin,
        // so the bitmap's top-left is at (0,0) in board-local space and
        // its geometric centre is at size/2.
        _mapCamera.Position = contentSize * 0.5f;
        GD.Print($"{Tag} maquette camera re-parked on content centre " +
                 $"=({_mapCamera.Position.X:0},{_mapCamera.Position.Y:0}) " +
                 $"(contentSize={contentSize}).");
    }

    /// <summary>
    /// Resolve a node by path with a loud error if it does not exist.
    /// Same discipline as the rest of the shell's
    /// <c>GetNode&lt;T&gt;()</c> calls — a missing wire is a scene bug,
    /// not a graceful-degrade case.
    /// </summary>
    private T? ResolveOrLog<T>(NodePath path, string pathField) where T : Node
    {
        var node = GetNodeOrNull<T>(path);
        if (node is null)
        {
            GD.PushError($"{Tag} {pathField}='{path}' did not resolve to a " +
                         $"{typeof(T).Name} — eM↔eT bascule will not work.");
        }
        return node;
    }
}
