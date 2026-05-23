using Godot;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Étape C (Rune, 2026-05-23, roadmap J8) — glues the recruit click on
/// eT to the authority writes (NpcRegistry, MissionStore, GameState) and
/// then asks the <see cref="MaquetteModeController"/> to return to eM.
/// Sibling of <see cref="MaquetteModeController"/> in
/// <c>GameScreen.tscn</c>.
///
/// <para>
/// <b>The recruit flow (single click → 4 state writes).</b>
/// <list type="number">
///   <item><see cref="RecruitTargetLayer"/> emits
///         <c>RecruitTargetClicked(npcId, missionId)</c>.</item>
///   <item>This controller calls
///         <c>NpcRegistry.MutateState(npcId, s =&gt; s with { IsRecruited
///         = true, RecruitedAtTick = currentTick })</c> — the authority
///         flip of the NPC's recruited state (same writer pattern as
///         the E2.2 MissionRecruitPanel.OnAcceptSucceededDeferred).</item>
///   <item>Calls <c>GameState.AddRecruitedCompanyMember(npcId)</c> — the
///         authority that seeds the new pawn slot on the desk grid +
///         emits <c>CompanyMemberRecruited</c>. The
///         <see cref="Scenes.Ui.CompanyDeskRoster"/> view spawns the
///         pawn in reaction.</item>
///   <item>Calls <c>MissionStore.RemoveById(missionId)</c> — the
///         mission disappears from active set (its eM POI losange
///         vanishes when we return to eM).</item>
///   <item>Calls <c>MaquetteModeController.RequestReturnToWorldMap()</c>
///         — automatic return to eM, the most natural PoC shape
///         (XCOM-style "act, return to strategic view").</item>
///   <item>Logs the canonical
///         <c>[RECRUITMENT] NPC=&lt;id&gt; recruited from mission=
///         &lt;id&gt; -&gt; Company slot=&lt;n&gt;</c> line the F6 smoke
///         watches for.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Authority discipline (NPC-autonomy lock 2026-05-09).</b> This
/// controller is a thin sequencer — it does NOT hold any state of its
/// own. Every state change is delegated to an autoload that owns its
/// slot. The autoload writes their own signals, the views (recruit
/// panel, company desk roster, mission POI layer) react. Same shape as
/// the J3c-2 placement intent flow : the click is an INTENT, the
/// authority decides, the view animates.
/// </para>
///
/// <para>
/// <b>Idempotence + degraded paths.</b> Every authority call is
/// idempotent on its own surface :
/// <see cref="NpcRegistry.MutateState"/> no-ops when the post-state
/// equals the pre-state (record equality on
/// <see cref="Wayfinders.Client.Services.Dtos.NpcRuntimeState"/>) ;
/// <see cref="GameState.AddRecruitedCompanyMember"/> blocks duplicate
/// NpcIds ; <see cref="MissionStore.RemoveById"/> no-ops on a missing
/// id. So a re-fired click signal (rare but possible if the eT layer's
/// signal landed twice) cannot corrupt the state. If any authority is
/// unavailable, we log + skip — graceful degrade, the player stays on
/// eT and can click Escape to return.
/// </para>
///
/// <para>
/// <b>Signal-disconnect discipline (trap #10).</b> We connect to
/// <see cref="RecruitTargetLayer.RecruitTargetClicked"/> at
/// <see cref="_Ready"/> against the cached reference and disconnect in
/// <see cref="_ExitTree"/>. Same pattern as
/// <see cref="MaquetteModeController"/> against
/// <see cref="MissionPoiLayer"/>.
/// </para>
/// </summary>
public partial class RecruitFlowController : Node
{
    /// <summary>Console prefix for every diagnostic line this node emits.</summary>
    private const string Tag = "[RECRUITMENT]";

    /// <summary>
    /// The <see cref="RecruitTargetLayer"/> this controller subscribes
    /// to. Sibling under the maquette in the shell scene tree.
    /// </summary>
    [Export]
    public NodePath RecruitTargetLayerPath { get; set; } = new();

    /// <summary>
    /// The <see cref="MaquetteModeController"/> we ask to return to eM
    /// after a successful recruit. Sibling under <c>GameScreen</c>.
    /// </summary>
    [Export]
    public NodePath MaquetteModeControllerPath { get; set; } = new();

    private RecruitTargetLayer? _recruitTargetLayer;
    private MaquetteModeController? _modeController;
    private NpcRegistry? _npcRegistry;
    private MissionStore? _missionStore;
    private GameState? _gameState;
    private WorldSimTick? _worldSimTick;

    public override void _Ready()
    {
        GD.Print($"{Tag} _Ready: RecruitFlowController entering tree.");

        _recruitTargetLayer = GetNodeOrNull<RecruitTargetLayer>(RecruitTargetLayerPath);
        if (_recruitTargetLayer is null)
        {
            GD.PushError($"{Tag} RecruitTargetLayerPath='{RecruitTargetLayerPath}' did " +
                         $"not resolve — recruit clicks will not be routed.");
        }
        else
        {
            _recruitTargetLayer.RecruitTargetClicked += OnRecruitTargetClicked;
            GD.Print($"{Tag} subscribed to RecruitTargetLayer.RecruitTargetClicked.");
        }

        _modeController = GetNodeOrNull<MaquetteModeController>(MaquetteModeControllerPath);
        if (_modeController is null)
        {
            GD.PushWarning($"{Tag} MaquetteModeControllerPath did not resolve — " +
                           $"return-to-eM on recruit will be skipped.");
        }

        _npcRegistry = GetNodeOrNull<NpcRegistry>("/root/NpcRegistry");
        if (_npcRegistry is null)
        {
            GD.PushWarning($"{Tag} NpcRegistry autoload missing — IsRecruited write " +
                           $"disabled.");
        }

        _missionStore = GetNodeOrNull<MissionStore>("/root/MissionStore");
        if (_missionStore is null)
        {
            GD.PushWarning($"{Tag} MissionStore autoload missing — mission removal " +
                           $"disabled.");
        }

        _gameState = GetNodeOrNull<GameState>("/root/GameState");
        if (_gameState is null)
        {
            GD.PushError($"{Tag} GameState autoload missing — desk pawn spawn " +
                         $"disabled, recruit flow is no-op.");
        }

        _worldSimTick = GetNodeOrNull<WorldSimTick>("/root/WorldSimTick");
        // WorldSimTick is the source of currentTick for RecruitedAtTick.
        // If absent, RecruitedAtTick falls back to 0 — not a hard error
        // (M1 does not key any logic off this field yet).
    }

    public override void _ExitTree()
    {
        if (_recruitTargetLayer is not null && GodotObject.IsInstanceValid(_recruitTargetLayer))
        {
            _recruitTargetLayer.RecruitTargetClicked -= OnRecruitTargetClicked;
        }
    }

    /// <summary>
    /// Handler for <see cref="RecruitTargetLayer.RecruitTargetClicked"/>.
    /// Runs the 5-step recruit flow described in the class doc.
    /// </summary>
    private void OnRecruitTargetClicked(string npcId, string missionId)
    {
        if (string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(missionId))
        {
            GD.PushWarning($"{Tag} OnRecruitTargetClicked called with empty arg " +
                           $"(npc='{npcId}' mission='{missionId}') — ignored.");
            return;
        }

        // Step 1 — NpcRegistry : flip IsRecruited.
        if (_npcRegistry is not null && GodotObject.IsInstanceValid(_npcRegistry))
        {
            var tick = _worldSimTick?.CurrentTick ?? 0;
            _npcRegistry.MutateState(npcId, s => s with
            {
                IsRecruited = true,
                RecruitedAtTick = tick,
            });
            GD.Print($"{Tag} NpcRegistry.IsRecruited write npc='{npcId}' tick={tick}.");
        }

        // Step 2 — GameState : add to the Compagnie. This is where the
        // slot index is decided ; we log the canonical line with the
        // resolved slot for the F6 smoke.
        int slot = -1;
        if (_gameState is not null && GodotObject.IsInstanceValid(_gameState))
        {
            var assignment = _gameState.AddRecruitedCompanyMember(npcId);
            slot = assignment?.SlotIndex ?? -1;
        }

        // Step 3 — MissionStore : drop the mission. Its eM POI losange
        // disappears when we return to eM (the MissionPoiLayer reconciles
        // on the PendingMissionsChanged signal).
        if (_missionStore is not null && GodotObject.IsInstanceValid(_missionStore))
        {
            _missionStore.RemoveById(missionId);
        }

        // Step 4 — return to eM. Automatic ; the most natural PoC shape.
        if (_modeController is not null && GodotObject.IsInstanceValid(_modeController))
        {
            _modeController.RequestReturnToWorldMap();
        }

        // Step 5 — canonical log line the F6 smoke greps for. After all
        // authority writes ; the slot index reflects the actual slot the
        // authority picked.
        GD.Print($"{Tag} NPC={npcId} recruited from mission={missionId} -> " +
                 $"Company slot={slot}");
    }
}
