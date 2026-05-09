using System;
using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for the Compagnie panel probe (Wayfinders 3D
/// Backing Architecture, doc &#167;3 Jalon 5+ -- "UI panels = iso
/// sub-spaces", locked 2026-05-09 soir, see memory
/// <c>project_wayfinders_3d_backing.md</c> &#167; "Extension UI panels").
///
/// <para>
/// <b>Why a pure-C# seam.</b> The runtime <c>CompagniePanelProbe</c> wires
/// SubViewport + Camera2D/Camera3D + Tween3D + drag-drop signals -- all
/// engine-bound, none xUnit-testable without a Godot runtime. The
/// <i>contracts</i> that govern the panel (slide states, drag-drop
/// semantics, auto-rabat dispatch, NPC autonomy invariant) are pure
/// data-flow questions that we pin here so a future regression cannot
/// land silently. Same seam pattern as <see cref="MapPanInputLogic"/>,
/// <see cref="ZoomNavLogic"/>, <see cref="PanWatchdogLogic"/>.
/// </para>
///
/// <para>
/// <b>What this helper does NOT do.</b>
/// <list type="bullet">
///   <item>It does not own any Node3D or signals -- the runtime probe owns
///         those.</item>
///   <item>It does not animate -- <see cref="DecideNextSlideState"/> is a
///         state machine that says "where should we be next" ; the Tween3D
///         in the probe drives the actual interpolation.</item>
///   <item>It does not know about the GameState authority -- it just
///         names the events the probe should emit when the user's signal
///         is captured (<see cref="AssignToMissionEventName"/>).</item>
/// </list>
/// </para>
/// </summary>
public static class CompagniePanelLogic
{
    /// <summary>
    /// Slide states of the panel. Three discrete positions ; the Tween3D
    /// interpolates between them.
    /// </summary>
    public enum SlideState
    {
        /// <summary>Panel hidden, only the diegetic handle (registry
        /// spine) is visible at the screen edge. Default state at
        /// boot.</summary>
        Closed,

        /// <summary>Tween in progress, panel is animating between Closed
        /// and Open (or vice versa). Used to gate user input during the
        /// motion -- a click on the handle while Sliding does not retrig
        /// a new tween (anti-stuck-transition pin test).</summary>
        Sliding,

        /// <summary>Panel fully extended, sub-iso scene visible, drag-drop
        /// available on the personas.</summary>
        Open,
    }

    /// <summary>
    /// User intents that drive the slide state machine.
    /// </summary>
    public enum SlideIntent
    {
        /// <summary>The user clicked the handle. Toggles the panel.</summary>
        ClickHandle,

        /// <summary>Another panel layer opened ; auto-rabat fires
        /// (Spec &#167; "auto-rabat actif" v1).</summary>
        AnotherLayerOpened,

        /// <summary>The Tween3D finished its interpolation and reached
        /// the target endpoint.</summary>
        TweenFinished,
    }

    /// <summary>
    /// Decide the next slide state given the current state, an intent,
    /// and (when intent = TweenFinished) the in-flight target.
    ///
    /// <para>
    /// <b>The full transition table</b> -- pinned numerically by
    /// <c>CompagniePanelLogicTests</c> :
    /// <code>
    ///   (Closed,  ClickHandle, _)            -> Sliding(target=Open)
    ///   (Open,    ClickHandle, _)            -> Sliding(target=Closed)
    ///   (Sliding, ClickHandle, _)            -> Sliding(unchanged target)  // anti-stuck
    ///   (Open,    AnotherLayerOpened, _)     -> Sliding(target=Closed)     // auto-rabat
    ///   (Closed,  AnotherLayerOpened, _)     -> Closed                     // already closed
    ///   (Sliding, AnotherLayerOpened, _)     -> Sliding(target=Closed)     // override
    ///   (Sliding, TweenFinished, target)     -> target                     // settle
    ///   (Closed,  TweenFinished, _)          -> Closed                     // ghost ; should not happen
    ///   (Open,    TweenFinished, _)          -> Open                       // ghost ; should not happen
    /// </code>
    /// </para>
    ///
    /// <para>
    /// <b>Why <c>Sliding(unchanged)</c> on click during slide.</b> The user
    /// can spam-click the handle ; the panel must not "retrig" a new tween
    /// from scratch every click. The probe's Tween3D continues to its
    /// current target ; a new click is ignored. Validated by the
    /// anti-stuck pin test "click during slide is a no-op".
    /// </para>
    ///
    /// <para>
    /// <b>Why <c>Sliding(target=Closed)</c> on
    /// <c>AnotherLayerOpened</c> while already <c>Sliding</c>.</b> If the
    /// user opens another panel mid-slide-out, we want the slide to
    /// continue toward Closed. If they opened another panel mid-slide-in
    /// (target Open), we override : auto-rabat wins, target flips to
    /// Closed. The Tween3D at runtime kills the in-flight tween and starts
    /// a new one toward Closed -- same kill+start pattern as
    /// <c>MapPan2DComponent.ApplyContinuousZoomToTarget</c>.
    /// </para>
    /// </summary>
    public static SlideDecision DecideNextSlideState(
        SlideState current,
        SlideIntent intent,
        SlideState? inFlightTarget = null)
    {
        switch (intent)
        {
            case SlideIntent.ClickHandle:
                return current switch
                {
                    SlideState.Closed => new SlideDecision(SlideState.Sliding, SlideState.Open),
                    SlideState.Open => new SlideDecision(SlideState.Sliding, SlideState.Closed),
                    SlideState.Sliding => new SlideDecision(SlideState.Sliding, inFlightTarget ?? SlideState.Closed),
                    _ => throw new ArgumentOutOfRangeException(nameof(current)),
                };

            case SlideIntent.AnotherLayerOpened:
                return current switch
                {
                    // Already closed : nothing to do, no tween to fire.
                    SlideState.Closed => new SlideDecision(SlideState.Closed, null),
                    // Open or already sliding (in either direction) :
                    // commit to a slide-out toward Closed. If the panel
                    // was sliding toward Open, the runtime kills that
                    // tween and starts a new one toward Closed.
                    SlideState.Open => new SlideDecision(SlideState.Sliding, SlideState.Closed),
                    SlideState.Sliding => new SlideDecision(SlideState.Sliding, SlideState.Closed),
                    _ => throw new ArgumentOutOfRangeException(nameof(current)),
                };

            case SlideIntent.TweenFinished:
                // Only valid from Sliding ; from the other two states we
                // just hold (no-op). Defensive : if a stray Finished fires
                // because we killed the tween mid-flight, we don't want
                // to clobber Closed/Open back into Sliding.
                return current switch
                {
                    SlideState.Sliding => new SlideDecision(inFlightTarget ?? SlideState.Closed, null),
                    SlideState.Closed => new SlideDecision(SlideState.Closed, null),
                    SlideState.Open => new SlideDecision(SlideState.Open, null),
                    _ => throw new ArgumentOutOfRangeException(nameof(current)),
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(intent));
        }
    }

    /// <summary>
    /// Result of <see cref="DecideNextSlideState"/> : the next state and,
    /// when relevant, the slide target the runtime should aim its Tween3D
    /// at. Target is non-null iff <see cref="NextState"/> is
    /// <see cref="SlideState.Sliding"/>.
    /// </summary>
    public readonly record struct SlideDecision(SlideState NextState, SlideState? SlideTarget);

    // ====================================================================
    // Drag-drop semantics (NPC autonomy invariant)
    // ====================================================================

    /// <summary>
    /// Pin the NPC autonomy contract from memory
    /// <c>project_wayfinders_npc_autonomy.md</c> : a drag-drop of a perso
    /// from the Compagnie panel to a mission slot is a <i>tactical
    /// signal</i>, not a re-parenting of the persona Node3D. The persona
    /// stays in the Compagnie sub-space ; the GameState authoritative
    /// state is updated via the
    /// <see cref="ProduceAssignToMissionEvent"/> event ; the visual on the
    /// Compagnie side flips its indicator to "in mission" via the same
    /// event consumer.
    ///
    /// <para>
    /// The runtime probe verifies this contract by asserting that, after
    /// a drag-drop, the original persona Node3D is still a child of the
    /// Compagnie sub-space (anti-regression to "drag-drop = re-parenting"
    /// reflex).
    /// </para>
    /// </summary>
    public const string AssignToMissionEventName = "perso_assigned_to_mission";

    /// <summary>
    /// Pure-C# producer for the drag-drop event payload. Names the persona
    /// id and the mission slot id ; the runtime probe captures the call
    /// and emits a Godot signal (or a <c>GD.Print</c> stub for v1).
    /// </summary>
    public static AssignToMissionEvent ProduceAssignToMissionEvent(
        string personaId,
        string missionSlotId)
    {
        if (string.IsNullOrEmpty(personaId))
            throw new ArgumentException("personaId required", nameof(personaId));
        if (string.IsNullOrEmpty(missionSlotId))
            throw new ArgumentException("missionSlotId required", nameof(missionSlotId));
        return new AssignToMissionEvent(personaId, missionSlotId);
    }

    /// <summary>The drag-drop event payload. The runtime probe maps this
    /// to a Godot signal ; xUnit pins the field shape so the contract
    /// shape cannot drift.</summary>
    public readonly record struct AssignToMissionEvent(string PersonaId, string MissionSlotId);

    // ====================================================================
    // Persona placeholders (Varn-aligned, NOT Mira's MJ tags)
    // ====================================================================

    /// <summary>
    /// v1 placeholder personas. Varn-aligned names (NOT Kira/Lyra/Borr from
    /// Mira's mockup -- those are MJ-prompt placeholders subordinate to
    /// the visual brief, not canonical Wayfinders persona ids). The mission
    /// slot stays empty until a drag-drop fires
    /// <see cref="AssignToMissionEvent"/>.
    /// </summary>
    public static readonly IReadOnlyList<PersonaPlaceholder> DefaultPersonas = new[]
    {
        new PersonaPlaceholder("hodge", "Hodge", "fatigue", PersonaTint.Olive),
        new PersonaPlaceholder("mirelle", "Mirelle", "calme", PersonaTint.Cream),
        new PersonaPlaceholder("wattan", "Wattan", "blesse", PersonaTint.Rust),
    };

    /// <summary>Placeholder persona descriptor for the v1 probe. Replaced
    /// by Varn-canonical persona resources once defined.</summary>
    public readonly record struct PersonaPlaceholder(
        string Id,
        string DisplayName,
        string ShortStatus,
        PersonaTint Tint);

    /// <summary>Distinct flat tints for the 3 v1 placeholder cubes -- so
    /// Didier visually distinguishes Hodge / Mirelle / Wattan in the
    /// probe without the real Mira sprites. Warm earth aligned with the
    /// visual DNA lock 2026-05-07 (terracotta / parchment / umber) but
    /// chosen for diff visibility, not for ship.</summary>
    public enum PersonaTint
    {
        Olive,
        Cream,
        Rust,
    }
}
