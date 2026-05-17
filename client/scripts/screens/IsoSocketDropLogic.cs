using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# drop-validation helper for the E2.2 step 6 drag&amp;drop seam
/// between <c>IsoCharacterPlaceholder</c> (drag source, sits inside the
/// MissionRecruitPanel) and <c>IsoSocketPlaceholder</c> (drop target,
/// sits inside the CompanyPanel). Godot's <c>_CanDropData</c> /
/// <c>_DropData</c> overrides forward to this helper so the test surface
/// stays engine-free.
///
/// <para>
/// <b>What this helper pins.</b> The drag payload travels as a Godot
/// <c>Variant</c> -- on the C# side that surfaces as either a
/// <c>Godot.Collections.Dictionary</c> or an <c>object</c>. The helper
/// accepts both representations via a thin
/// <see cref="DragPayload"/> wrapper that the runtime constructs at the
/// boundary, so all the validation logic lives in plain-C# code that
/// xUnit can exercise without spinning up a Godot runtime.
/// </para>
///
/// <para>
/// <b>The shape contract.</b> A valid drop payload carries :
/// <list type="bullet">
///   <item><c>npc_id</c> -- non-empty string, must match one of the
///         M1 roster ids (kira / dorn / vell). Unknown ids reject so
///         the player cannot "drop" a corrupt placeholder into the
///         Compagnie.</item>
///   <item><c>mission_id</c> -- non-empty string, identifies which
///         mission's recruit panel originated the drag. Logged on
///         drop for traceability ; not used to gate the drop itself
///         at étape 6 (the recruit panel only ever has one mission
///         open at a time).</item>
///   <item><c>source</c> -- closed string lookup, either
///         <see cref="DragSourceRecruitPanel"/> or
///         <see cref="DragSourceCompanySocle"/>. The source string
///         disambiguates the "drag back to mission panel" reverse
///         path at étape 6 ; future extensions (drag from roster,
///         drag from tactical scene) add a new constant + one test
///         pin.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a closed roster check.</b> M1 has a 3-NPC roster locked by
/// Varn §A.8. A future M2 NPC arriving in the registry must opt-in to
/// being recruit-droppable -- silently accepting any string from
/// <see cref="NpcCatalog"/> would let a hypothetical "story-only NPC"
/// land on the socle by accident. The helper passes the roster set
/// in explicitly so the runtime can keep using <see cref="NpcCatalog"/>
/// as its source of truth while the test surface pins the exact set
/// at the test site.
/// </para>
/// </summary>
public static class IsoSocketDropLogic
{
    /// <summary>Closed string for the "drag started from the recruit
    /// panel's character placeholder" source. Pinned to <c>"recruit_panel"</c>
    /// so the wire-format snake_case naming is stable.</summary>
    public const string DragSourceRecruitPanel = "recruit_panel";

    /// <summary>Closed string for the "drag started from the company
    /// socle" reverse-drag source. Pinned to <c>"company_socle"</c>.</summary>
    public const string DragSourceCompanySocle = "company_socle";

    /// <summary>Payload-dictionary key for the NPC id field. Pinned
    /// as a constant so a typo at one end of the boundary surfaces
    /// as a red test, not as a silent reject.</summary>
    public const string PayloadKeyNpcId = "npc_id";

    /// <summary>Payload-dictionary key for the source mission id.</summary>
    public const string PayloadKeyMissionId = "mission_id";

    /// <summary>Payload-dictionary key for the drag source enum.</summary>
    public const string PayloadKeySource = "source";

    /// <summary>
    /// Decide whether a drop should be accepted on the company socle.
    /// Pure function, no side effects. The runtime
    /// <c>IsoSocketPlaceholder._CanDropData</c> forwards here ; the
    /// test surface pins the truth table.
    ///
    /// <para>
    /// <b>Decision table</b> :
    /// <code>
    ///   payload null / not a dict                          -> Reject
    ///   missing or empty npc_id                            -> Reject
    ///   missing or empty source                            -> Reject
    ///   source not in {recruit_panel, company_socle}       -> Reject
    ///   source = company_socle                             -> Reject (cannot drop on self)
    ///   npc_id not in known roster                         -> Reject
    ///   all gates pass                                     -> Accept
    /// </code>
    /// </para>
    ///
    /// <para>
    /// <b>Why "source = company_socle" rejects.</b> Reverse drag from
    /// the socle is intercepted by the MissionRecruitPanel's
    /// IsoCharacterPlaceholder as the drop target -- the socle never
    /// accepts a drop from itself. Gating it here prevents the
    /// degenerate "drag the socle's own perso onto itself" no-op
    /// from registering as a drop event.
    /// </para>
    /// </summary>
    public static DropDecision CanAccept(
        DragPayload? payload,
        IReadOnlySet<string> knownNpcIds)
    {
        if (payload is null)
            return DropDecision.RejectMalformedPayload;
        if (string.IsNullOrEmpty(payload.NpcId))
            return DropDecision.RejectMissingNpcId;
        if (string.IsNullOrEmpty(payload.Source))
            return DropDecision.RejectMissingSource;
        if (payload.Source != DragSourceRecruitPanel
            && payload.Source != DragSourceCompanySocle)
            return DropDecision.RejectUnknownSource;
        if (payload.Source == DragSourceCompanySocle)
            return DropDecision.RejectSelfDrop;
        if (!knownNpcIds.Contains(payload.NpcId))
            return DropDecision.RejectUnknownNpc;
        return DropDecision.Accept;
    }

    /// <summary>
    /// Decide whether a drop should be accepted on the recruit panel's
    /// IsoCharacterPlaceholder (the reverse path -- drag from socle back
    /// to the mission panel). Symmetric to <see cref="CanAccept"/> but
    /// inverts the source gate : only <see cref="DragSourceCompanySocle"/>
    /// is accepted, because dropping the recruit panel's own placeholder
    /// onto itself is a no-op.
    /// </summary>
    public static DropDecision CanAcceptReverse(
        DragPayload? payload,
        IReadOnlySet<string> knownNpcIds)
    {
        if (payload is null)
            return DropDecision.RejectMalformedPayload;
        if (string.IsNullOrEmpty(payload.NpcId))
            return DropDecision.RejectMissingNpcId;
        if (string.IsNullOrEmpty(payload.Source))
            return DropDecision.RejectMissingSource;
        if (payload.Source != DragSourceRecruitPanel
            && payload.Source != DragSourceCompanySocle)
            return DropDecision.RejectUnknownSource;
        if (payload.Source == DragSourceRecruitPanel)
            return DropDecision.RejectSelfDrop;
        if (!knownNpcIds.Contains(payload.NpcId))
            return DropDecision.RejectUnknownNpc;
        return DropDecision.Accept;
    }

    /// <summary>The eight possible outcomes of <see cref="CanAccept"/>.
    /// The runtime cares only about Accept vs anything-else (the engine
    /// surface is a single bool out of <c>_CanDropData</c>), but the
    /// finer breakdown lets xUnit + diagnostic logging pinpoint why a
    /// drop was refused.</summary>
    public enum DropDecision
    {
        Accept = 0,
        RejectMalformedPayload = 1,
        RejectMissingNpcId = 2,
        RejectMissingSource = 3,
        RejectUnknownSource = 4,
        RejectSelfDrop = 5,
        RejectUnknownNpc = 6,
    }
}

/// <summary>
/// Engine-free mirror of the Godot drag-data dictionary. The runtime
/// builds one of these from the <c>Godot.Collections.Dictionary</c>
/// received by <c>_CanDropData</c> / <c>_DropData</c>, then forwards
/// to <see cref="IsoSocketDropLogic.CanAccept"/>. Keeps the test
/// surface engine-free.
/// </summary>
public sealed record DragPayload(
    string NpcId,
    string MissionId,
    string Source);
