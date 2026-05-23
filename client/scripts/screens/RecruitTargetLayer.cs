using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scenes.Ui;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Étape C (Rune, 2026-05-23, roadmap J8) — places the recruit-target
/// NPC silhouette on the eT district mesh and surfaces a click signal
/// when the player clicks it. Sibling of <see cref="MissionPoiLayer"/>
/// under the maquette <see cref="IsoBoard"/> in <c>GameScreen.tscn</c>.
///
/// <para>
/// <b>What this node owns.</b> One <see cref="IsoCharacterPlaceholder"/>
/// child (the procedural silhouette, tinted by NPC palette via
/// <see cref="IsoCharacterPlaceholder.SetNpc"/>) and the visibility
/// toggle driven by <see cref="MaquetteModeController.MaquetteModeChanged"/>.
/// It does NOT own the click cascade (<c>GameScreen._UnhandledInput</c>
/// owns the shell's input routing and calls <see cref="OnClickAttempt"/>
/// on this node when the click resolved inside the eT viewport) and it
/// does NOT own the recruitment decision (the
/// <see cref="RecruitFlowController"/> sibling consumes the click
/// signal and runs the authority writes).
/// </para>
///
/// <para>
/// <b>Visibility lifecycle.</b> The layer is invisible by default
/// (constructed visible=true to render once configured, then immediately
/// toggled off in <see cref="_Ready"/>) ; becomes visible only while
/// <see cref="MaquetteModeController.CurrentMode"/> is
/// <see cref="MaquetteMode.DistrictMap"/> AND the
/// <see cref="MissionStore"/> still holds the originating mission. On
/// every <see cref="MaquetteModeController.MaquetteModeChanged"/> we
/// pick the first remaining mission with a non-null
/// <see cref="EmergentMissionDto.RecruitTargetNpcId"/> and configure the
/// silhouette for that NPC. M1 ships one active recruit mission at a
/// time (3 NPCs, but the cadence stays low) so the "first match" rule
/// is the right shape.
/// </para>
///
/// <para>
/// <b>Why not a draw-time pin like
/// <see cref="MissionPoiLayer.MissionPin"/>.</b> The silhouette is a
/// full procedural body, not a flat diamond. The
/// <see cref="IsoCharacterPlaceholder"/> already implements the
/// 2.5D iso parallelepiped with NPC tinting + name label + size
/// discipline (memo feedback_godot_rendering_input_traps) — re-implementing
/// that here would duplicate code and lose the existing xUnit pin
/// surface on the geometry / palette helpers. We re-use it in the
/// "Decorative" source role : no drag-drop wiring (Étape C uses a
/// click, not a drag), MouseFilter = Ignore so the host control stays
/// passive and the parent IsoBoard's regular click cascade reaches the
/// hit-test on this layer.
/// </para>
///
/// <para>
/// <b>Click signal.</b> The node emits
/// <see cref="RecruitTargetClicked"/> with the resolved NpcId + mission
/// id. The <see cref="RecruitFlowController"/> subscribes and runs the
/// authority writes (NpcRegistry.MutateState, MissionStore.RemoveById,
/// GameState.AddRecruitedCompanyMember, then the return-to-eM hop). Same
/// signal seam as <see cref="MissionPoiLayer.MissionPoiClicked"/>.
/// </para>
///
/// <para>
/// <b>Signal-disconnect discipline (trap #10).</b> We subscribe to
/// three long-lived sources : <see cref="MaquetteModeController"/>
/// (sibling under <c>GameScreen</c>), <see cref="MissionStore"/>
/// (autoload). All disconnections happen in <see cref="_ExitTree"/>
/// against the cached references, matching the pattern of
/// <see cref="MissionPoiLayer"/>.
/// </para>
/// </summary>
public partial class RecruitTargetLayer : Node2D
{
    /// <summary>Console prefix for every diagnostic line this node emits.</summary>
    private const string Tag = "[RECRUIT-TGT]";

    /// <summary>Pick radius in board-local pixels, propagated to
    /// <see cref="RecruitTargetLayerLogic.IsClickOnTarget"/>. Exported so
    /// the F6 smoke can tune.</summary>
    [Export]
    public float PickRadiusPx { get; set; } = RecruitTargetLayerLogic.DefaultPickRadiusPx;

    /// <summary>
    /// Fired when the player left-clicks the recruit-target perso on eT.
    /// Carries the NpcId the player is recruiting + the mission id that
    /// originated the bascule (so the consumer can call
    /// <see cref="MissionStore.RemoveById"/> on it).
    /// </summary>
    [Signal]
    public delegate void RecruitTargetClickedEventHandler(string npcId, string missionId);

    /// <summary>The procedural silhouette child. Configured on every
    /// mode-change event ; null until <see cref="_Ready"/> has run.</summary>
    private IsoCharacterPlaceholder? _silhouette;

    /// <summary>The MissionId driving the currently-visible target —
    /// empty when no target is visible. Used by the click handler to
    /// emit the right correlation id.</summary>
    private string _currentMissionId = string.Empty;

    /// <summary>The NpcId of the currently-visible target — empty when
    /// nothing is shown.</summary>
    private string _currentNpcId = string.Empty;

    /// <summary>Cached <see cref="MaquetteModeController"/> sibling
    /// reference, captured at <c>_Ready</c> for the
    /// <c>_ExitTree</c> disconnect.</summary>
    private MaquetteModeController? _modeController;

    /// <summary>Cached <see cref="MissionStore"/> autoload reference.</summary>
    private MissionStore? _missionStore;

    public override void _Ready()
    {
        GD.Print($"{Tag} _Ready: RecruitTargetLayer entering tree, " +
                 $"pickRadiusPx={PickRadiusPx}.");

        // The silhouette is a child Control wrapped in a Node2D anchor.
        // IsoCharacterPlaceholder is a Control — to use it inside a
        // Node2D parent we host it under a CanvasLayer-less wrapper. The
        // simplest path : add it as a direct child of this Node2D ; the
        // Control's Position is in board-local pixels because the parent
        // Node2D contributes its transform.
        _silhouette = new IsoCharacterPlaceholder
        {
            Name = "RecruitSilhouette",
            // SourceRole.Decorative keeps MouseFilter = Ignore so the
            // parent IsoBoard's click cascade reaches this layer's
            // hit-test (Étape C uses a click, not a drag).
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_silhouette);

        // Start hidden — visibility flips on the first mode-change
        // event into DistrictMap.
        Visible = false;

        // Subscribe to the mode controller (sibling under GameScreen) so
        // we know when eT is active and which mission resolved the bascule.
        // GameScreen scene tree :
        //   GameScreen (= Owner of this node loaded inline)
        //     MapViewportContainer
        //       MapViewport
        //         Maquette
        //           RecruitTargetLayer  <- this node
        //     MaquetteModeController
        // The cleanest sibling-of-screen lookup is via Owner ; we fall
        // back to a tree-search if Owner is null (scene loaded
        // standalone, edge case).
        _modeController = Owner?
            .GetNodeOrNull<MaquetteModeController>("MaquetteModeController");
        if (_modeController is null)
        {
            _modeController = GetTree()?.Root?
                .FindChild("MaquetteModeController", recursive: true, owned: false)
                as MaquetteModeController;
        }
        if (_modeController is null)
        {
            GD.PushWarning($"{Tag} MaquetteModeController sibling not found — " +
                           $"recruit target will not toggle visibility.");
        }
        else
        {
            _modeController.MaquetteModeChanged += OnMaquetteModeChanged;
            GD.Print($"{Tag} subscribed to MaquetteModeController.MaquetteModeChanged.");
        }

        _missionStore = GetNodeOrNull<MissionStore>("/root/MissionStore");
        if (_missionStore is null)
        {
            GD.PushWarning($"{Tag} MissionStore autoload missing — recruit " +
                           $"target will not resolve a mission.");
        }
    }

    public override void _ExitTree()
    {
        if (_modeController is not null && GodotObject.IsInstanceValid(_modeController))
        {
            _modeController.MaquetteModeChanged -= OnMaquetteModeChanged;
        }
    }

    /// <summary>
    /// Reconcile the visibility + target NPC on every mode change. eT
    /// active = pick the first remaining recruit mission, configure the
    /// silhouette for its NpcId, position it, show ; eM active = hide.
    /// </summary>
    private void OnMaquetteModeChanged(int newMode, string districtIdOrEmpty)
    {
        var mode = (MaquetteMode)newMode;
        GD.Print($"{Tag} MaquetteModeChanged: mode={mode} district='{districtIdOrEmpty}'.");

        if (mode != MaquetteMode.DistrictMap)
        {
            // Returning to eM — hide.
            ClearTarget("returned to eM");
            return;
        }

        // eT active — pick the first active mission with a recruit target.
        if (_missionStore is null)
        {
            GD.PushWarning($"{Tag} cannot resolve a recruit target — " +
                           $"MissionStore unavailable.");
            ClearTarget("MissionStore unavailable");
            return;
        }

        EmergentMissionDto? candidate = null;
        foreach (var m in _missionStore.AllActive)
        {
            var npcId = RecruitFlowLogic.ResolveRecruitTarget(m);
            if (!string.IsNullOrEmpty(npcId))
            {
                candidate = m;
                break;
            }
        }

        if (candidate is null)
        {
            GD.PushWarning($"{Tag} eT active but no active mission carries a " +
                           $"recruit target — nothing to show.");
            ClearTarget("no recruit-mission candidate");
            return;
        }

        ConfigureTarget(candidate);
    }

    /// <summary>
    /// Configure the silhouette for a mission's recruit target and place
    /// it on the eT mesh. Called from the mode-change handler.
    /// </summary>
    private void ConfigureTarget(EmergentMissionDto mission)
    {
        if (_silhouette is null) return;

        var npcId = RecruitFlowLogic.ResolveRecruitTarget(mission);
        if (string.IsNullOrEmpty(npcId)) return;

        _currentMissionId = mission.Id;
        _currentNpcId = npcId;
        _silhouette.SetNpc(npcId);

        // Position the silhouette so its foot anchor lands at the
        // logic-decided centre on the district mesh. The Control's
        // Position is its top-left — we offset by the silhouette's own
        // size to anchor on its centre-bottom (the foot).
        var board = GetParentOrNull<IsoBoard>();
        var meshSize = board?.GetBackgroundTextureSizeOrZero() ?? Vector2.Zero;
        var footPx = RecruitTargetLayerLogic.ComputePersoCentrePx(
            new SysVec2(meshSize.X, meshSize.Y));

        // The IsoCharacterPlaceholder's CustomMinimumSize is 160x336 in
        // default (non-compact) mode ; we want the foot (bottom centre)
        // to land at footPx, so the Control's top-left is footPx minus
        // (half-width, full-height-minus-padding-bottom).
        var ctrl = _silhouette;
        var size = ctrl.CustomMinimumSize;
        // The procedural body inside the Control is offset by PaddingPx
        // from each edge — the visual foot sits roughly at the
        // Control's bottom-centre, so the simple top-left = footPx -
        // (size.X/2, size.Y) anchor is correct within ~PaddingPx (24
        // px). Good enough for the MVP click hit-test (90 px radius).
        ctrl.Position = new Vector2(
            footPx.X - size.X * 0.5f,
            footPx.Y - size.Y);
        ctrl.Size = size;

        Visible = true;
        GD.Print($"{Tag} configured target: npc='{npcId}' mission='{mission.Id}' " +
                 $"mesh=({meshSize.X:0}x{meshSize.Y:0}) foot=({footPx.X:0},{footPx.Y:0}) " +
                 $"ctrlTopLeft=({ctrl.Position.X:0},{ctrl.Position.Y:0}) " +
                 $"size=({size.X:0}x{size.Y:0}) visible=true.");
    }

    /// <summary>
    /// Hide the silhouette and clear the current target tracking.
    /// </summary>
    private void ClearTarget(string reason)
    {
        if (_currentMissionId.Length == 0 && _currentNpcId.Length == 0 && !Visible)
        {
            return; // Idempotent — no allocation, no log spam.
        }
        _currentMissionId = string.Empty;
        _currentNpcId = string.Empty;
        Visible = false;
        GD.Print($"{Tag} cleared target ({reason}).");
    }

    /// <summary>
    /// Hit-test entry point called by <c>GameScreen._UnhandledInput</c>
    /// when the eT bascule is active and a left-click landed in the
    /// maquette viewport. Returns true if the click hit the recruit
    /// target (and we emitted <see cref="RecruitTargetClicked"/>) ;
    /// false otherwise so the caller can fall through to other cascades.
    /// </summary>
    /// <param name="boardLocalClickPx">The click in maquette-board local
    /// pixels (post-routing).</param>
    public bool OnClickAttempt(Vector2 boardLocalClickPx)
    {
        if (!Visible) return false;
        if (string.IsNullOrEmpty(_currentNpcId)) return false;
        if (string.IsNullOrEmpty(_currentMissionId)) return false;

        var board = GetParentOrNull<IsoBoard>();
        var meshSize = board?.GetBackgroundTextureSizeOrZero() ?? Vector2.Zero;
        var footPx = RecruitTargetLayerLogic.ComputePersoCentrePx(
            new SysVec2(meshSize.X, meshSize.Y));

        var hit = RecruitTargetLayerLogic.IsClickOnTarget(
            new SysVec2(boardLocalClickPx.X, boardLocalClickPx.Y),
            footPx,
            PickRadiusPx);

        if (!hit) return false;

        GD.Print($"{Tag} CLICK HIT npc='{_currentNpcId}' mission='{_currentMissionId}' " +
                 $"clickBoardLocal=({boardLocalClickPx.X:0},{boardLocalClickPx.Y:0}) " +
                 $"footPx=({footPx.X:0},{footPx.Y:0}) " +
                 $"pickRadiusPx={PickRadiusPx} — emitting RecruitTargetClicked.");
        EmitSignal(SignalName.RecruitTargetClicked, _currentNpcId, _currentMissionId);
        return true;
    }
}
