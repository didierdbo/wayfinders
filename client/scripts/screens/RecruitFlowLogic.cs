using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helpers for Étape C — the recruit-on-eT PoC chain
/// (Rune, 2026-05-23, roadmap J8). Lives next to the other Godot-free
/// helpers under <c>scripts/screens/</c>. The Godot-bound wiring lives
/// on <c>RecruitFlowController</c> (autoload-sibling Node in
/// <c>GameScreen.tscn</c>) and on <c>RecruitTargetLayer</c> (the visible
/// silhouette node under the maquette).
///
/// <para>
/// <b>What this helper decides.</b>
/// <list type="bullet">
///   <item><see cref="ResolveRecruitTarget"/> — given an
///         <see cref="EmergentMissionDto"/>, return the NpcId the player
///         can recruit by clicking the perso on eT. M1 strict mode
///         (Varn §A.8.D3) guarantees that recruit-type missions carry a
///         non-null <see cref="EmergentMissionDto.RecruitTargetNpcId"/>,
///         so the function is mostly a typed unwrap with one explicit
///         null branch for non-recruit / corrupt missions (we degrade
///         gracefully by returning null — the caller logs and the
///         clickable perso simply does not spawn).</item>
///   <item><see cref="ResolveNextCompanySlot"/> — given the locked
///         <c>DeskSlotLayoutLogic.CompanySlotCells()</c> formation and
///         the current desk occupancy, return the next free slot the
///         freshly-recruited NPC should land on (scan in slot order :
///         leader first, then formation rows). Returns null when the
///         formation is full — M1 caps at 3 recruits and the formation
///         has 7 cells, so the null branch is defensive, not exercised
///         in M1.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why this is its own helper.</b> Both decisions are arithmetic over
/// data the rest of the code already owns
/// (<see cref="EmergentMissionDto"/>,
/// <see cref="DeskSlotLayoutLogic.CompanySlotCells"/>,
/// <see cref="TileCoordinate"/>). Pulling them out keeps the Godot-bound
/// controller a thin wiring shell : the controller listens to signals
/// and forwards to authorities, the logic is here and xUnit-pinned. Same
/// separation pattern as <see cref="MaquetteModeLogic"/> +
/// <see cref="MaquetteModeController"/> and
/// <see cref="MissionDistrictResolverLogic"/> + its caller.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Speaks only <see cref="EmergentMissionDto"/>,
/// <see cref="TileCoordinate"/> (pure-C# integer pair), and
/// <see cref="IReadOnlyDictionary{TKey, TValue}"/> over standard types —
/// no <c>Vector2I</c>, no <c>Node</c>, no <c>GodotObject</c>. Cherry-picks
/// cleanly into the xUnit host alongside its siblings (memo
/// <c>feedback_godot_rendering_input_traps</c> Phase C-bis pin :
/// Godot-free seam keeps the test csproj independent of GodotSharp).
/// </para>
/// </summary>
public static class RecruitFlowLogic
{
    /// <summary>
    /// Extract the recruit target NpcId from a mission DTO. Returns the
    /// id verbatim when <see cref="EmergentMissionDto.Type"/> equals
    /// <see cref="WorldTickWireFormat.MissionType.Recruit"/> AND
    /// <see cref="EmergentMissionDto.RecruitTargetNpcId"/> is non-null /
    /// non-empty ; returns null otherwise.
    ///
    /// <para>
    /// <b>Defensive null branches.</b> Three explicit null returns :
    /// <list type="bullet">
    ///   <item>The mission DTO itself is null (caller bug, but we degrade
    ///         gracefully — the clickable perso simply does not spawn).</item>
    ///   <item>The mission type is not <c>recruit</c> (M2+ scout_route /
    ///         parley_local missions reach this code path once they emerge
    ///         again).</item>
    ///   <item>The <c>recruit_target_npc_id</c> field is null or empty —
    ///         a server bug or a pre-§A.8 fixture, again degrade.</item>
    /// </list>
    /// In every null case the Godot-bound caller logs and stays on eT
    /// without spawning the perso — same graceful-degrade discipline as
    /// the rest of the bascule chain.
    /// </para>
    /// </summary>
    /// <param name="mission">The mission the player clicked the eM POI
    /// for. May be null (caller bug guard).</param>
    /// <returns>The NpcId to recruit, or null when the mission cannot
    /// produce a recruit target.</returns>
    public static string? ResolveRecruitTarget(EmergentMissionDto? mission)
    {
        if (mission is null) return null;
        if (mission.Type != WorldTickWireFormat.MissionType.Recruit) return null;
        if (string.IsNullOrEmpty(mission.RecruitTargetNpcId)) return null;
        return mission.RecruitTargetNpcId;
    }

    /// <summary>
    /// Outcome of a <see cref="ResolveNextCompanySlot"/> call : the chosen
    /// slot's index in the canonical
    /// <see cref="DeskSlotLayoutLogic.CompanySlotCells"/> ordering AND
    /// the chosen slot's cell. Returned together so the caller does not
    /// have to second-guess which slot got picked when it logs the
    /// canonical <c>[RECRUITMENT] ... -> Company slot=N</c> line.
    /// </summary>
    /// <param name="SlotIndex">0 for leader, 1-6 for formation rows
    /// (Varn-locked formation, see
    /// <see cref="DeskSlotLayoutLogic.CompanySlotCells"/>).</param>
    /// <param name="Cell">The slot's iso-grid cell (TileCoordinate, not
    /// Vector2I — Godot-free seam).</param>
    public sealed record CompanySlotAssignment(int SlotIndex, TileCoordinate Cell);

    /// <summary>
    /// Scan the canonical Company formation in slot order (leader first,
    /// then formation rows) and return the first cell that is NOT in the
    /// current desk occupancy. M1 starts empty so the first recruit
    /// always lands on slot 0 (leader, <c>(3,3)</c>) ; subsequent recruits
    /// fill slots 1, 2, 3, ... in declaration order.
    ///
    /// <para>
    /// <b>Why a stable scan order rather than a per-NPC affinity.</b> M1
    /// has 3 recruitable NPCs and 7 slots ; assigning slots by recruit
    /// order is deterministic, simple, and demonstrably visible at the
    /// F6 smoke (first recruit = leader = front-centre big pawn ; second
    /// = middle row ; third = middle row flank). A per-NPC affinity
    /// (kira always to slot N) is M2+ scope when the recruit pool grows
    /// and a "fixed leader" decision is warranted.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotence at the caller's edge.</b> This function is a
    /// scanner — it does NOT consult whether <paramref name="occupants"/>
    /// already contains an id matching the freshly-recruited NPC. The
    /// caller (<c>GameState.AddRecruitedCompanyMember</c>) guards against
    /// double-recruitment of the same NpcId before invoking this scanner ;
    /// at this layer we trust the occupancy snapshot we're given.
    /// </para>
    /// </summary>
    /// <param name="slotCells">The ordered Company formation cells —
    /// typically <see cref="DeskSlotLayoutLogic.CompanySlotCells"/>.
    /// Passed as a parameter for testability.</param>
    /// <param name="occupants">The current desk cell → member-id index.
    /// Cells with an entry are considered occupied ; cells absent from
    /// the dictionary are considered free.</param>
    /// <returns>The slot assignment for the next free cell, or null when
    /// every formation slot is occupied.</returns>
    public static CompanySlotAssignment? ResolveNextCompanySlot(
        IReadOnlyList<TileCoordinate> slotCells,
        IReadOnlyDictionary<TileCoordinate, string> occupants)
    {
        System.ArgumentNullException.ThrowIfNull(slotCells);
        System.ArgumentNullException.ThrowIfNull(occupants);

        for (int i = 0; i < slotCells.Count; i++)
        {
            var cell = slotCells[i];
            if (!occupants.ContainsKey(cell))
            {
                return new CompanySlotAssignment(i, cell);
            }
        }
        return null;
    }
}
