namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# state machine for the E2.2 step 6 drag&amp;drop seam between
/// <c>MissionRecruitPanel</c> (drag source) and <c>CompanyPanel</c>
/// (drop target). The panel-scoped state captures "has the player
/// already moved the recruit silhouette over to the Compagnie socle ?",
/// which drives two pieces of visual feedback :
///
/// <list type="bullet">
///   <item>the <c>IsoCharacterPlaceholder</c> inside the recruit panel
///         flips between "normal" and "greyed-out" (the perso visually
///         left the panel),</item>
///   <item>the Accept button flips between "default" and "highlighted"
///         (the perso is on the socle, you can now validate).</item>
/// </list>
///
/// <para>
/// <b>Why panel-scoped and not a global store.</b> Two reasons :
/// (1) the state is purely a UX read of "is the panel mid-drop" -- it
/// does not need to survive the panel's open/close cycle, and certainly
/// does not need to span screens (E1 / E2 / E3) ; (2) keeping it
/// panel-scoped means closing the recruit panel resets the state, which
/// matches the natural UX contract "if you closed the panel without
/// accepting, the drop never happened". A global store would have to
/// remember to clear itself on close, which is one more invariant to
/// keep right.
/// </para>
///
/// <para>
/// <b>Why a 2-state enum and not a bool.</b> Future-proof seam : the
/// étape 6 spec ships two states, but the recruit panel will likely
/// grow a third "validating" tick-state at étape 7+ (e.g. the player
/// pressed Accept but the backend round-trip has not landed yet --
/// disable the buttons, dim the Accept). An enum keeps the addition
/// to "one switch arm + one test pin", a bool would force a partial
/// rewrite.
/// </para>
///
/// <para>
/// <b>What this helper does NOT do.</b> It does not own any Godot
/// node, signal, or visual state -- the runtime <c>MissionRecruitPanel</c>
/// wires the IsoCharacterPlaceholder greying + the Accept button
/// highlight. The helper just decides the next state given the current
/// state + the event. Same separation pattern as
/// <see cref="M1SliceLogic"/> + <see cref="MissionRecruitPanelLogic"/>.
/// </para>
/// </summary>
public static class MissionRecruitPanelStateLogic
{
    /// <summary>
    /// Compute the next state given the current state and the event.
    /// Pure function, no side effects. Pinned by xUnit so the four
    /// transitions cannot drift without a red test.
    ///
    /// <para>
    /// <b>Transition table</b> (pinned by
    /// <c>MissionRecruitPanelStateLogicTests</c>) :
    /// <code>
    ///   (Initial,                CharacterDroppedOnSocle)   -> DroppedReadyToValidate
    ///   (Initial,                CharacterReturnedFromSocle) -> Initial          (no-op)
    ///   (DroppedReadyToValidate, CharacterDroppedOnSocle)   -> DroppedReadyToValidate (idempotent)
    ///   (DroppedReadyToValidate, CharacterReturnedFromSocle) -> Initial
    ///   (any,                    PanelOpened)               -> Initial          (reset on re-open)
    ///   (any,                    PanelClosed)               -> Initial          (reset on close)
    /// </code>
    /// </para>
    /// </summary>
    public static MissionRecruitPanelState Next(
        MissionRecruitPanelState current,
        MissionRecruitPanelEvent ev)
    {
        return ev switch
        {
            MissionRecruitPanelEvent.PanelOpened => MissionRecruitPanelState.Initial,
            MissionRecruitPanelEvent.PanelClosed => MissionRecruitPanelState.Initial,
            MissionRecruitPanelEvent.CharacterDroppedOnSocle
                => MissionRecruitPanelState.DroppedReadyToValidate,
            MissionRecruitPanelEvent.CharacterReturnedFromSocle
                => MissionRecruitPanelState.Initial,
            _ => current,
        };
    }

    /// <summary>
    /// Should the recruit panel grey out its IsoCharacterPlaceholder ?
    /// True iff the perso has been dropped on the socle (state =
    /// DroppedReadyToValidate). Pinned numerically so the runtime
    /// rendering cannot drift to "always grey" or "never grey".
    /// </summary>
    public static bool ShouldGreyOutCharacterPlaceholder(MissionRecruitPanelState state)
        => state == MissionRecruitPanelState.DroppedReadyToValidate;

    /// <summary>
    /// Should the Accept button render in its highlighted variant ?
    /// True iff the perso has been dropped on the socle. The Accept
    /// button remains ENABLED in both states -- highlight is a visual
    /// affordance, not a gating mechanism. The player can validate
    /// the mission without dragging (mouse-only path) ; the highlight
    /// just nudges them when the drag-drop UX is mid-flight.
    /// </summary>
    public static bool ShouldHighlightAcceptButton(MissionRecruitPanelState state)
        => state == MissionRecruitPanelState.DroppedReadyToValidate;
}

/// <summary>
/// The two visual states the recruit panel cycles through during the
/// E2.2 step 6 drag&amp;drop seam.
/// </summary>
public enum MissionRecruitPanelState
{
    /// <summary>Panel just opened, no drop yet. IsoCharacterPlaceholder
    /// renders at full opacity, Accept button uses its default
    /// theming.</summary>
    Initial = 0,

    /// <summary>Player has dropped the perso on the Compagnie socle.
    /// IsoCharacterPlaceholder renders greyed-out (the perso visually
    /// left the panel) ; Accept button switches to its highlighted
    /// theming.</summary>
    DroppedReadyToValidate = 1,
}

/// <summary>
/// Events the runtime <c>MissionRecruitPanel</c> feeds into
/// <see cref="MissionRecruitPanelStateLogic.Next"/>. Closed lookup ;
/// extending it requires updating the helper's switch + at least one
/// xUnit pin.
/// </summary>
public enum MissionRecruitPanelEvent
{
    /// <summary>Panel just transitioned from closed to open -- always
    /// reset to <see cref="MissionRecruitPanelState.Initial"/>.</summary>
    PanelOpened = 0,

    /// <summary>Panel just transitioned from open to closed -- always
    /// reset to <see cref="MissionRecruitPanelState.Initial"/>.</summary>
    PanelClosed = 1,

    /// <summary>Player just dropped the IsoCharacterPlaceholder onto
    /// the Compagnie socle. Idempotent in the DroppedReady state.</summary>
    CharacterDroppedOnSocle = 2,

    /// <summary>Player just dragged the perso back from the socle to
    /// the recruit panel. Reset to Initial regardless of current
    /// state.</summary>
    CharacterReturnedFromSocle = 3,
}
