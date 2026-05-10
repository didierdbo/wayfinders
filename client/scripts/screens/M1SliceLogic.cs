using System;
using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

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
/// <b>5c additions (2026-05-10).</b> Three pure-C# helpers added for the
/// resolve pipeline triggered at drop release :
/// <see cref="AppendTagToLegacyMap"/> (server returns tags, client
/// appends per persona id under its bag) ;
/// <see cref="PopHeadMission"/> (mission resolved, head leaves the
/// pending queue) ; <see cref="ShouldRenderEmptyMissionState"/>
/// (after pop, panel goes empty when no mission is left). Same
/// extraction rationale : the runtime mutates
/// <c>GameState.PersonaLegacy</c> + <c>GameState.PendingMissions</c>
/// (Godot-bound) ; xUnit pins the data-flow rules here against
/// plain dictionaries / lists.
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
    /// The signal name the runtime emits after a successful
    /// resolve round-trip stores the server's tags into
    /// <c>GameState.PersonaLegacy</c>. Stable string, pinned by
    /// xUnit. Listeners (5d visualisation, future analytics)
    /// subscribe by this exact name. Same naming convention as
    /// <see cref="PersonaAssignedToMissionEventName"/>.
    /// </summary>
    public const string MissionResolvedEventName = "mission_resolved";

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

    // ====================================================================
    // 5c (2026-05-10) -- resolve pipeline pure helpers
    // ====================================================================

    /// <summary>
    /// Append a single legacy tag to a per-persona dictionary,
    /// creating the per-persona list lazily on first append.
    /// Mirrors the runtime
    /// <c>GameState.AppendPersonaLegacyTag</c> (which mutates the
    /// Godot-bound autoload's Dictionary slot) so xUnit can pin
    /// the append-or-create-list semantics without spinning up
    /// the engine.
    ///
    /// <para>
    /// <b>Defensive contract.</b> Throws if <paramref name="tag"/>
    /// is null -- M1 server contract guarantees non-null tags in
    /// <c>MissionResolveResponse.tags_created</c>, but the
    /// transport (System.Text.Json) could in theory deserialise
    /// a list element to null. Surface that as a contract
    /// violation rather than poison the dictionary.
    /// </para>
    ///
    /// <para>
    /// <b>Dedup is NOT performed here.</b> The natural key
    /// <c>(persona_id, mission_id)</c> is enforced server-side
    /// inside a single resolve call. Cross-call dedup (e.g.
    /// retrying after a transient
    /// <see cref="Wayfinders.Client.Services.ApiError.NotReachable"/>)
    /// is a M2 concern -- M1 simple impl appends blindly. If a
    /// future contract change demands client-side dedup, add a
    /// second helper here ; do not silently change this one's
    /// semantics.
    /// </para>
    /// </summary>
    public static void AppendTagToLegacyMap(
        IDictionary<string, List<PersonaLegacyTagDto>> legacyMap,
        PersonaLegacyTagDto tag)
    {
        if (legacyMap is null) throw new ArgumentNullException(nameof(legacyMap));
        if (tag is null) throw new ArgumentNullException(nameof(tag));
        if (string.IsNullOrEmpty(tag.PersonaId))
            throw new ArgumentException("PersonaLegacyTagDto.PersonaId must be non-empty", nameof(tag));

        if (!legacyMap.TryGetValue(tag.PersonaId, out var list))
        {
            list = new List<PersonaLegacyTagDto>();
            legacyMap[tag.PersonaId] = list;
        }
        list.Add(tag);
    }

    /// <summary>
    /// Pop the head mission from the pending queue, returning
    /// the popped mission id (or <c>null</c> if the list was
    /// already empty). Mirrors the runtime
    /// <c>GameState.PendingMissions.RemoveAt(0)</c> after a
    /// successful resolve.
    ///
    /// <para>
    /// <b>Why a helper for one line.</b> Two reasons : (1) the
    /// runtime currently calls <c>RemoveAt(0)</c> blindly which
    /// would throw <see cref="ArgumentOutOfRangeException"/> if
    /// the list was concurrently emptied (e.g. a tick resolved a
    /// mission while the resolve round-trip was in flight) ;
    /// using this helper forces the defensive empty-check at
    /// the call site. (2) The xUnit pin makes the FIFO
    /// semantics ("head goes first") explicit -- a future
    /// "resolve any pending mission by id" feature would NOT
    /// land here, it would be a sibling helper.
    /// </para>
    /// </summary>
    public static string? PopHeadMission(IList<EmergentMissionDto> pendingMissions)
    {
        if (pendingMissions is null)
            throw new ArgumentNullException(nameof(pendingMissions));
        if (pendingMissions.Count == 0) return null;

        var head = pendingMissions[0];
        pendingMissions.RemoveAt(0);
        return head.Id;
    }

    /// <summary>
    /// Decide whether the Mission panel should render the
    /// "empty state" (no mission, waiting for tick cadence)
    /// after the pending queue mutates. True when the queue is
    /// empty ; false otherwise. Pinned numerically so the
    /// rendering branch in the runtime cannot drift to "show
    /// stale head" or "show queue length".
    /// </summary>
    public static bool ShouldRenderEmptyMissionState(int pendingMissionCount)
    {
        if (pendingMissionCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(pendingMissionCount),
                "pending mission count cannot be negative");
        return pendingMissionCount == 0;
    }
}
