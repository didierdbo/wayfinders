using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Ui;

/// <summary>
/// Étape C (Rune, 2026-05-23, roadmap J8) — view-side controller that
/// instances one <see cref="DeskCompanyPawn"/> per recruited Company
/// member. Lives as a child of the desk entities <see cref="IsoBoard"/>
/// so the spawned pawns are siblings of the iso occupants the board
/// y-sorts.
///
/// <para>
/// <b>What this node owns — and what it does NOT own.</b> It owns the
/// "NpcId → spawned pawn" map and the lifetime of those pawns. It does
/// NOT own the recruitment decision (<see cref="GameState"/> + the
/// <c>RecruitFlowController</c> in <c>GameScreen.tscn</c> do that) and
/// it does NOT own the desk grid or the slot layout (the
/// <see cref="DeskSlotLayoutLogic"/> + <c>GameState.AdoptDeskGrid</c>
/// path remains authoritative). It is a pure reflector : it subscribes
/// to <see cref="GameState.CompanyMemberRecruited"/>, instances the
/// matching pawn when the signal fires, frees the pawn when the signal
/// fires in reverse (M2 — de-recruit / departure ; M1 only emits
/// recruits forward).
/// </para>
///
/// <para>
/// <b>Authority discipline (NPC-autonomy lock 2026-05-09).</b> Same
/// pattern as the J3c-2 <see cref="DeskCompanyPawn"/> selection feedback :
/// the pawn is a view of the authority's state, the authority is
/// <see cref="GameState"/>. This roster controller is one level above
/// the pawn — it owns whether a pawn EXISTS for an NpcId, the pawn
/// owns the per-pawn selection / placement reactions. Each layer is a
/// view of the layer beneath it ; nothing decides anything that isn't
/// in its own scope.
/// </para>
///
/// <para>
/// <b>Signal-disconnection discipline (trap #10).</b> The roster
/// subscribes to <see cref="GameState"/> in <see cref="_Ready"/> and
/// disconnects in <see cref="_ExitTree"/> — the autoload outlives this
/// view, leaving the delegate over a freed node would crash on the next
/// emit. Same discipline as <see cref="DeskCompanyPawn"/>.
/// </para>
///
/// <para>
/// <b>Why a separate Node and not extra code on
/// <see cref="Wayfinders.Client.Scenes.Screens.GameScreen"/>.</b>
/// <c>GameScreen</c> already owns the four-layer shell + the desk
/// configuration ; adding the recruit reflector there would mix two
/// concerns at different change rates (the shell is locked, the
/// roster's view shape will grow with M2 — naming, portrait, mood UI).
/// One node per concern, each pinable independently.
/// </para>
/// </summary>
public partial class CompanyDeskRoster : Node
{
    /// <summary>Console prefix for every diagnostic line this node emits.</summary>
    private const string Tag = "[COMPANY-ROSTER]";

    /// <summary>
    /// The desk entities board the spawned pawns live on. The caller
    /// (<see cref="Wayfinders.Client.Scenes.Screens.GameScreen.ConfigureDesk"/>)
    /// sets this in code before <c>AddChild</c> — no
    /// <c>[Export] NodePath</c> ceremony for an internal helper node.
    /// </summary>
    public IsoBoard? DeskBoard { get; set; }

    /// <summary>
    /// Map of NpcId → spawned pawn. Used to (a) skip duplicate spawns if
    /// the recruit signal ever fired twice for the same NpcId (idempotence
    /// guard ; <see cref="GameState.AddRecruitedCompanyMember"/> already
    /// blocks duplicates at the authority, but a view that trusts the
    /// signal blindly would be more fragile), and (b) look up the right
    /// pawn for an eventual de-recruit signal (M2 scope).
    /// </summary>
    private readonly Dictionary<string, DeskCompanyPawn> _pawnsByNpcId = new();

    /// <summary>
    /// Cached <see cref="GameState"/> autoload reference, captured at
    /// <c>_Ready</c> so <see cref="_ExitTree"/> disconnects against the
    /// same reference.
    /// </summary>
    private GameState? _gameState;

    public override void _Ready()
    {
        _gameState = GetNodeOrNull<GameState>("/root/GameState");
        if (_gameState is null)
        {
            GD.PushWarning($"{Tag} GameState autoload missing — recruit-driven " +
                           $"pawn spawn disabled.");
            return;
        }

        _gameState.CompanyMemberRecruited += OnCompanyMemberRecruited;
        GD.Print($"{Tag} _Ready: subscribed to GameState.CompanyMemberRecruited " +
                 $"(desk board='{DeskBoard?.Name ?? "(null)"}').");
    }

    public override void _ExitTree()
    {
        if (_gameState is not null && GodotObject.IsInstanceValid(_gameState))
        {
            _gameState.CompanyMemberRecruited -= OnCompanyMemberRecruited;
        }
    }

    /// <summary>
    /// Handler for <see cref="GameState.CompanyMemberRecruited"/>.
    /// Instances one <see cref="DeskCompanyPawn"/> at the slot cell the
    /// authority picked, configures it with the recruit's NpcId, and
    /// stores the reference for the idempotence guard.
    /// </summary>
    private void OnCompanyMemberRecruited(
        string npcId, int slotIndex, int slotCol, int slotRow)
    {
        if (DeskBoard is null)
        {
            GD.PushError($"{Tag} CompanyMemberRecruited({npcId}, slot={slotIndex}) " +
                         $"but DeskBoard reference is null — pawn NOT spawned.");
            return;
        }

        if (_pawnsByNpcId.ContainsKey(npcId))
        {
            // Belt-and-braces idempotence : GameState.AddRecruitedCompanyMember
            // already blocks duplicate adds, but a view that trusted the
            // signal alone would corrupt its own dict.
            GD.PushWarning($"{Tag} CompanyMemberRecruited({npcId}, slot={slotIndex}) " +
                           $"but a pawn already exists for that NpcId — " +
                           $"duplicate signal, no-op.");
            return;
        }

        var cell = new Vector2I(slotCol, slotRow);
        var pixel = DeskBoard.CellToPixel(cell);

        var pawn = new DeskCompanyPawn
        {
            Name = $"DeskPawn_{npcId}",
            Position = pixel,
        };
        // The DeskCompanyPawn's memberId is the authoritative key for the
        // J3c-2 selection / placement signals — we pass the NpcId verbatim
        // so a click on this pawn selects "kira", "dorn", "vell" directly
        // (no synthetic company-slot-N indirection).
        pawn.Configure(
            slotIndex: slotIndex,
            isLeader: DeskSlotLayoutLogic.IsLeaderSlot(slotIndex),
            memberId: npcId,
            board: DeskBoard);

        DeskBoard.AddOccupant(pawn);
        _pawnsByNpcId[npcId] = pawn;
        GD.Print($"{Tag} spawned pawn for NPC='{npcId}' on slot={slotIndex} " +
                 $"cell=({slotCol},{slotRow}) pixel=({pixel.X:0},{pixel.Y:0}) " +
                 $"isLeader={DeskSlotLayoutLogic.IsLeaderSlot(slotIndex)} " +
                 $"(roster now holds {_pawnsByNpcId.Count} pawn(s)).");
    }
}
