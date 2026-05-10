using System;
using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for the M1 composite slice scene
/// (<c>scenes/play/M1Slice.tscn</c>) -- the first time the
/// Compagnie panel and the Mission panel coexist in one runtime
/// scene. Owns the cross-panel drag-drop contract (Pattern P5),
/// the "Mission stays fixed, never emits auto-rabat" invariant
/// (Pattern P6), and the assignment-eligibility gate that the
/// runtime probe binds to its drop event handler.
///
/// <para>
/// <b>Why a logic seam, not a behemoth M1Slice.cs.</b> Same
/// rationale as <see cref="CompagniePanelLogic"/> and
/// <see cref="MissionPanelLogic"/> : the runtime scene wires
/// SubViewportContainers, CanvasLayers, the golden Sprite2D
/// proxy, signal connections to GameState / WorldSimTick / the
/// two child panels. None of that is xUnit-testable without a
/// Godot runtime. The <i>contracts</i> -- which rect counts as
/// "over the Mission slot", whether a persona is already
/// assigned, what payload travels with the cross-panel
/// signal -- are pure data-flow questions and live here.
/// </para>
///
/// <para>
/// <b>Pattern P5 (new M1 slice / 2026-05-10) -- "Cross-panel
/// drag-drop = 2D screen-space golden proxy + tactical signal,
/// NOT 3D Tween + re-parenting".</b>
/// The persona body lives inside the Compagnie SubViewport's
/// World3D ; the Mission slot lives in a sibling CanvasLayer
/// Control rect. Tweening a 3D node across that boundary would
/// mean re-parenting it out of the SubViewport (NPC-autonomy
/// invariant violation) AND re-projecting it through a 2D
/// CanvasLayer transform. We avoid both : the proxy is a 2D
/// Sprite2D / ColorRect drawn on a TOP CanvasLayer (above both
/// panels), seeded at the screen-projected position of the
/// clicked persona Node3D and tweened to the Mission slot's
/// global screen rect centre. At drop, the proxy is freed, the
/// <c>PersonaAssignedToMission</c> signal fires with the
/// (persona_id, mission_id) payload, and the Mission panel's
/// existing OnAffectPressed pipeline takes over.
/// </para>
///
/// <para>
/// <b>Pattern P6 (new M1 slice / 2026-05-10) -- "Mission panel
/// is fixed, NEVER emits AnotherLayerOpened".</b> Decision pinned
/// by Didier 2026-05-10 : Compagnie slides (Pattern P2-P4) ;
/// Mission stays anchored to the right side of the screen
/// always-visible. Compagnie's auto-rabat fires on any other
/// "panel layer opening" intent -- but Mission, by Didier's
/// design lock, does not <i>open</i> in the slide-tween sense :
/// it is just there. Concretely, the M1Slice runtime does NOT
/// connect Mission's "I just got new content" event to
/// Compagnie's <see cref="CompagniePanelLogic.SlideIntent.AnotherLayerOpened"/>
/// dispatch. <see cref="ShouldDispatchAutoRabatOnMissionEvent"/>
/// pins this contract numerically.
/// </para>
///
/// <para>
/// <b>What this helper does NOT do.</b>
/// <list type="bullet">
///   <item>It does not own any Node, Sprite2D, signal, or
///         GameState mutation -- the runtime <c>M1Slice</c> owns
///         all of that.</item>
///   <item>It does not animate -- the proxy tween lives in the
///         runtime ; here we just decide "should drop fire"
///         given the current state (cursor over slot rect,
///         persona not yet assigned, mission still pending).</item>
///   <item>It does not project 3D-to-screen -- that math is
///         engine-bound (Camera3D.UnprojectPosition). The
///         runtime calls Unproject and feeds the result into
///         <see cref="DecideDropOutcome"/>.</item>
/// </list>
/// </para>
/// </summary>
public static class M1SliceLogic
{
    /// <summary>
    /// The signal name the runtime emits at a successful
    /// cross-panel drop. Stable string, pinned by xUnit so a
    /// rename in <c>M1Slice.cs</c> alone breaks the test, not
    /// silently drops listeners. Production-shaped : same naming
    /// convention as
    /// <see cref="CompagniePanelLogic.AssignToMissionEventName"/>
    /// (snake_case, scoped, verb-y).
    /// </summary>
    public const string PersonaAssignedToMissionEventName = "persona_assigned_to_mission";

    /// <summary>
    /// Decide whether a cross-panel drag-drop completes
    /// successfully given the cursor screen position at release,
    /// the Mission slot's screen-space rect, and the assignment
    /// state.
    ///
    /// <para>
    /// <b>The full decision table</b> -- pinned by
    /// <c>M1SliceLogicTests</c> :
    /// <code>
    ///   (over_slot=false, _, _)                                 -> CancelledMissedSlot
    ///   (over_slot=true, no_pending_mission, _)                 -> CancelledNoMission
    ///   (over_slot=true, mission_present, persona_already_in)   -> CancelledAlreadyAssigned
    ///   (over_slot=true, mission_present, persona_not_eligible) -> CancelledNotEligible
    ///   (over_slot=true, mission_present, persona_eligible)     -> Accepted
    /// </code>
    /// </para>
    ///
    /// <para>
    /// <b>Why not_eligible is "cancel" and not "still drop".</b>
    /// The Mission panel's
    /// <see cref="MissionPanelLogic.DecideAffectButtonState"/>
    /// already disables Affect when the selected persona is not
    /// in <see cref="Wayfinders.Client.Services.Dtos.EmergentMissionDto.EligiblePersonas"/>.
    /// Cross-panel drag-drop is a tactile path to the same
    /// outcome -- if the click-Affect path would refuse, the
    /// drag-drop must too. Defensive symmetry : the player can't
    /// sneak past the eligibility filter with a different input
    /// modality.
    /// </para>
    /// </summary>
    public static DropDecision DecideDropOutcome(
        bool cursorOverMissionSlot,
        bool missionPresent,
        bool personaAlreadyAssigned,
        bool personaInEligibleList)
    {
        if (!cursorOverMissionSlot)
            return DropDecision.CancelledMissedSlot;
        if (!missionPresent)
            return DropDecision.CancelledNoMission;
        if (personaAlreadyAssigned)
            return DropDecision.CancelledAlreadyAssigned;
        if (!personaInEligibleList)
            return DropDecision.CancelledNotEligible;
        return DropDecision.Accepted;
    }

    /// <summary>
    /// Build the cross-panel signal payload for an Accepted drop.
    /// Pre-condition : caller has verified
    /// <see cref="DecideDropOutcome"/> returned
    /// <see cref="DropDecision.Accepted"/>. Throws on
    /// empty / null inputs -- contract violation that should
    /// never reach this layer ; better to throw than to leak a
    /// malformed payload onto the bus
    /// (cf. <see cref="CompagniePanelLogic.ProduceAssignToMissionEvent"/>
    /// where the same defensive throw lives).
    /// </summary>
    public static AssignmentEvent BuildAssignmentEvent(
        string personaId,
        string missionId)
    {
        if (string.IsNullOrEmpty(personaId))
            throw new ArgumentException("personaId required", nameof(personaId));
        if (string.IsNullOrEmpty(missionId))
            throw new ArgumentException("missionId required", nameof(missionId));
        return new AssignmentEvent(personaId, missionId);
    }

    /// <summary>
    /// Pin the Pattern P6 contract : when the Mission panel
    /// receives new content (a new mission emerges into its
    /// Pending list, or it gets re-rendered after a resolve),
    /// should the M1Slice scene fire
    /// <see cref="CompagniePanelLogic.SlideIntent.AnotherLayerOpened"/>
    /// at the Compagnie panel ? <b>Always false in M1.</b>
    /// Mission is fixed, never opens-as-an-event.
    ///
    /// <para>
    /// Pinned numerically rather than in prose so a future
    /// well-meaning refactor that "wires up auto-rabat
    /// everywhere" cannot silently revert this design lock.
    /// </para>
    /// </summary>
    public static bool ShouldDispatchAutoRabatOnMissionEvent(MissionPanelEvent ev) => false;

    /// <summary>
    /// Result of <see cref="DecideDropOutcome"/>. The runtime
    /// uses the variant to decide whether to emit the signal and
    /// flip the persona's [mission] indicator
    /// (<see cref="DropDecision.Accepted"/>) or to play the
    /// "snap-back" animation for the proxy
    /// (any Cancelled variant).
    /// </summary>
    public enum DropDecision
    {
        /// <summary>Player released the proxy outside the
        /// Mission slot's hit rect. No state change ; runtime
        /// snaps the proxy back and frees it.</summary>
        CancelledMissedSlot,
        /// <summary>Cursor was over the slot but no mission is
        /// pending right now. Should not happen in a well-formed
        /// UI (the Mission panel hides the slot when no mission
        /// is active) ; defensive branch caught here.</summary>
        CancelledNoMission,
        /// <summary>The persona is already assigned to a (the)
        /// pending mission. v1 has no recall-from-mission UI ;
        /// dragging an already-assigned persona is a no-op.</summary>
        CancelledAlreadyAssigned,
        /// <summary>The persona is not in the server's
        /// EligiblePersonas list for the current mission.
        /// Symmetric with
        /// <see cref="MissionPanelLogic.AffectButtonState.VisibleDisabled"/>.</summary>
        CancelledNotEligible,
        /// <summary>All preconditions met ; runtime emits
        /// <see cref="PersonaAssignedToMissionEventName"/>.</summary>
        Accepted,
    }

    /// <summary>The cross-panel drag-drop event payload.
    /// Captured by xUnit so the field shape cannot drift.
    /// PersonaId is the id from
    /// <see cref="CompagniePanelLogic.PersonaPlaceholder"/> ;
    /// MissionId is the id from
    /// <see cref="Wayfinders.Client.Services.Dtos.EmergentMissionDto"/>.</summary>
    public readonly record struct AssignmentEvent(string PersonaId, string MissionId);

    /// <summary>
    /// Discriminator for "the Mission panel just got something
    /// new". Used purely as input to
    /// <see cref="ShouldDispatchAutoRabatOnMissionEvent"/> --
    /// keeps the contract pinnable per-event-type. M1 has only
    /// <c>NewMissionRendered</c> ; future events (e.g. tutorial
    /// pop-in, special mission alert) extend this enum and the
    /// xUnit pin per-variant.
    /// </summary>
    public enum MissionPanelEvent
    {
        NewMissionRendered,
    }

    // ====================================================================
    // Hit-testing : pure 2D rect math
    // ====================================================================

    /// <summary>
    /// Pure-C# hit test : is the cursor screen position inside
    /// the slot's screen-space rect ? Pinned in xUnit because
    /// the runtime calls
    /// <see cref="Godot.Control.GetGlobalRect"/> for the slot
    /// rect and pipes the result through here -- a regression
    /// like "use local rect" or "off-by-one on the right edge"
    /// breaks the drag-drop only at runtime, hard to debug. The
    /// helper takes plain <see cref="Rect"/> + <see cref="Point"/>
    /// so we don't drag <c>using Godot;</c> into the test
    /// assembly.
    ///
    /// <para>
    /// <b>Inclusive on left/top, exclusive on right/bottom</b> --
    /// matches Godot's convention for
    /// <see cref="Godot.Rect2.HasPoint"/>. A pixel exactly at
    /// (rect.right, rect.top) is NOT a hit. Pinned numerically
    /// so a future "off-by-one fix" can't drift the contract.
    /// </para>
    /// </summary>
    public static bool IsCursorOverSlot(Point cursor, Rect slot)
    {
        return cursor.X >= slot.Left
            && cursor.X < slot.Left + slot.Width
            && cursor.Y >= slot.Top
            && cursor.Y < slot.Top + slot.Height;
    }

    /// <summary>2D point in screen pixels. Plain doubles so the
    /// helper has no Godot dependency.</summary>
    public readonly record struct Point(double X, double Y);

    /// <summary>2D rect in screen pixels (left/top + width/height).
    /// Plain doubles so the helper has no Godot dependency.</summary>
    public readonly record struct Rect(double Left, double Top, double Width, double Height);

    // ====================================================================
    // Eligibility lookup
    // ====================================================================

    /// <summary>
    /// Is <paramref name="personaId"/> in the eligible list ?
    /// Tiny helper but extracted so the runtime
    /// <see cref="DecideDropOutcome"/> caller stays focused and
    /// xUnit pins the case-sensitive string match (a future
    /// regression that lower-cases one side of the comparison
    /// silently breaks every drag-drop).
    /// </summary>
    public static bool IsPersonaInEligibleList(string personaId, IReadOnlyList<string> eligible)
    {
        if (string.IsNullOrEmpty(personaId)) return false;
        for (var i = 0; i < eligible.Count; i++)
        {
            if (eligible[i] == personaId) return true;
        }
        return false;
    }
}
