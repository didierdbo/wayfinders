namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for slice 3 zoom-driven navigation on L1 World
/// (M3 / Phase 9 / 2026-05-08). Same Godot-free seam pattern as
/// <see cref="CameraPanLogic"/>, <see cref="MapPanInputLogic"/>, and
/// <see cref="LadderResolutionLogic"/>: the runtime
/// <see cref="Wayfinders.Client.Components.MapPan2DComponent"/> translates
/// engine wheel events into <see cref="WheelGesture"/> + scalar inputs,
/// this helper resolves the action, the runtime applies it.
///
/// <para>
/// <b>Vocabulary lock (P8 final-lock 2026-05-07).</b> This file uses
/// <see cref="WheelGesture.Pull"/> and <see cref="WheelGesture.Push"/>
/// exclusively. The Godot raw event labels (<c>WheelUp</c> /
/// <c>WheelDown</c>) are deliberately absent : the pull/push axis is the
/// physical-gesture truth, the WheelUp/WheelDown labels are the hardware
/// wiring we already pinned. Translation lives at exactly one site --
/// <c>MapPan2DComponent._Input</c> -- per the meta-lesson from the
/// four-iteration swap loop : "ne jamais swapper sans re-validation
/// physique Didier". On Didier's Logitech hardware (locked target):
/// <list type="bullet">
///   <item><b>Pull</b> (geste vers le poignet) → <c>MouseButton.WheelDown</c>
///         event → ladder verb is <i>climb out</i>, slice 3 verb is
///         <i>zoom out</i> (vue d'ensemble).</item>
///   <item><b>Push</b> (geste vers les phalanges) → <c>MouseButton.WheelUp</c>
///         event → ladder verb is <i>drill in</i>, slice 3 verb is
///         <i>zoom in</i> (détail).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a separate helper.</b> The decision tree -- continuous zoom vs
/// drill candidate vs climb candidate vs debounce -- is small but
/// contractual. Encoding the threshold + hysteresis + debounce + tile-knowledge
/// gate once, here, lets xUnit pin the slice 3 behaviour without touching
/// the engine. When a future slice extends this (drill from L2 to L3,
/// per-cell zoom thresholds, accessibility keyboard fallback), the test
/// surface catches every consumer that depended on the slice 3 shape.
/// </para>
///
/// <para>
/// <b>Threshold semantics (brief slice 3).</b>
/// <list type="bullet">
///   <item><see cref="ZoomThresholds.ZoomInThreshold"/> = ZoomMax × 0.95
///         -- "almost at the ceiling", an additional Push fires the
///         drill candidate.</item>
///   <item><see cref="ZoomThresholds.ZoomOutThreshold"/> = ZoomMin × 1.20
///         -- 20 % asymmetric hysteresis (Varn) : the player must Pull
///         a little further to climb than Push to drill, mirroring how
///         "vue d'ensemble" is a more deliberate gesture than "loupe".</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Hard-cap behaviour at the limits (Rune call, justified).</b> When
/// the zoom is already at <c>ZoomMax</c> and the player Pushes again, we
/// do NOT zoom further (clamp at the limit) but we DO evaluate the drill
/// candidate. Symmetric at <c>ZoomMin</c> for climb. Rationale : a player
/// hammering the wheel at the ceiling expects either visible progress
/// or a clear "next step" (drill) ; silently consuming the input feels
/// broken. The explicit drill candidate at the cap converts the
/// frustration moment into the actual transition. Same shape as Google
/// Maps "scroll past max zoom = enter Street View" affordance.
/// </para>
///
/// <para>
/// <b>Debounce semantics.</b> After a drill OR climb, every wheel event
/// arriving within <see cref="ZoomThresholds.PostTransitionDebounceMs"/>
/// returns <see cref="ZoomNavAction.Debounced"/> -- runtime ignores it
/// entirely (no zoom, no drill, no climb). Mirrors the
/// <c>SceneManager.PostDragGraceMs</c> pattern : a transition is a
/// narrow window where the player's gesture is not yet "ready" for new
/// continuous zoom input. 250 ms is the same horizon as the post-drag
/// grace period.
/// </para>
///
/// <para>
/// <b>Tile-knowledge gate.</b> Drill-in only fires when the cell under
/// the cursor has knowledge state at <see cref="TileKnowledgeLadder.Esquissee"/>
/// or higher (Varn §1.4 lock D-TILE-03). Below that, the action is
/// <see cref="ZoomNavAction.DrillBlocked"/> -- the runtime plays the
/// "tuile veut être vue mais pas encore" feedback without transitioning.
/// The gate is only consulted on the cap-Push branch ; continuous
/// zoom-in below the cap never queries the knowledge store.
/// </para>
///
/// <para>
/// <b>Why time is passed in, not <see cref="System.Environment.TickCount"/>
/// internally.</b> Same testability rationale as
/// <see cref="MapPanInputLogic"/>'s state machine -- xUnit can step the
/// clock deterministically and assert debounce boundaries to the
/// millisecond.
/// </para>
/// </summary>
public static class ZoomNavLogic
{
    /// <summary>
    /// Evaluate a wheel input event and decide what the runtime should do.
    /// Pure function -- no side effects, no global state.
    /// </summary>
    /// <param name="currentZoom">Current Camera2D zoom scalar (uniform on
    ///   both axes, > 0). Smaller values = zoomed-in (Camera2D semantics
    ///   are inverted -- a zoom of 0.5 makes the world appear 2× larger).
    ///   We work in the "design" frame where larger = more detail :
    ///   <see cref="ZoomThresholds.ZoomMin"/> is the "vue d'ensemble"
    ///   value (small numerical zoom -- world looks small) and
    ///   <see cref="ZoomThresholds.ZoomMax"/> is the "détail" value
    ///   (large numerical zoom -- world looks large). The runtime
    ///   <c>MapPan2DComponent</c> writes the numerical value directly to
    ///   <c>Camera2D.Zoom</c> and Godot's inverse semantic kicks in there.</param>
    /// <param name="gesture">The physical gesture, post-translation from
    ///   the raw Godot event at the runtime seam.</param>
    /// <param name="tileKnowledgeAtCursor">Knowledge state of the cell
    ///   under the cursor at this exact frame. Used only to gate the
    ///   drill candidate ; ignored otherwise.</param>
    /// <param name="lastTransitionTimeMs">Timestamp of the most recent
    ///   drill or climb. Pass 0 if none has happened this session
    ///   (debounce never fires before the first transition).</param>
    /// <param name="currentTimeMs">Now, in milliseconds. The runtime
    ///   passes <c>Time.GetTicksMsec()</c> ; xUnit passes a fixed value.</param>
    /// <param name="thresholds">Configuration. The runtime constructs
    ///   one from the component's <c>[Export]</c> fields ; xUnit uses
    ///   <see cref="ZoomThresholds.Default"/>.</param>
    public static ZoomNavOutcome EvaluateWheelInput(
        float currentZoom,
        WheelGesture gesture,
        TileKnowledgeLadder tileKnowledgeAtCursor,
        ulong lastTransitionTimeMs,
        ulong currentTimeMs,
        ZoomThresholds thresholds)
    {
        // Debounce is the first gate : a drill or climb just fired, the
        // player's hand is mid-recoil, every wheel event is a no-op.
        // Note the long-running-session subtlety : if lastTransitionTimeMs
        // == 0 (never transitioned), the predicate is `currentTimeMs <
        // PostTransitionDebounceMs` for ~250 ms after game boot. Acceptable
        // floor case -- the player can't possibly be at zoom max in the
        // first 250 ms anyway.
        if (lastTransitionTimeMs > 0
            && currentTimeMs - lastTransitionTimeMs < thresholds.PostTransitionDebounceMs)
        {
            return ZoomNavOutcome.Debounced();
        }

        if (gesture == WheelGesture.Push)
        {
            // Push = zoom in / drill in. Three branches :
            //   1. Below ZoomInThreshold     -> continuous zoom in.
            //   2. Between threshold and Max -> still continuous zoom in
            //      (the threshold is a "drill candidacy" line, not a
            //      cap on continuous zoom).
            //   3. At or beyond ZoomMax      -> drill candidacy fires
            //      (cap-Push behaviour, see class XML doc).
            //
            // The drill candidacy condition that the brief specs is
            // "currentZoom >= ZoomInThreshold AND additional Push event".
            // That phrasing collapses cleanly into two cases : either
            // the next Push CAN still zoom (we are below ZoomMax), in
            // which case we do continuous zoom AND don't fire drill yet
            // (Varn §1.4 progressive disclosure -- the drill is the
            // explicit transition past the cap, not a side-effect of
            // pushing through the threshold) ; or we ARE at ZoomMax,
            // in which case continuous zoom can't progress and the
            // drill candidacy is the only meaningful action.
            //
            // Brief Q2 trade-off documented in class XML doc : "drill
            // happens at the cap, not at the threshold". One-sentence
            // justification : threshold-fire steals user agency
            // ("i wanted to keep zooming, not transition") ; cap-fire
            // gives the player an intentional ceiling event.
            if (currentZoom >= thresholds.ZoomMax - ZoomEpsilon)
            {
                // At or past max -- evaluate drill candidacy with the
                // tile-knowledge gate.
                if (tileKnowledgeAtCursor >= TileKnowledgeLadder.Esquissee)
                {
                    return ZoomNavOutcome.DrillCandidate();
                }
                return ZoomNavOutcome.DrillBlocked();
            }
            // Below cap -- continuous zoom in.
            return ZoomNavOutcome.ZoomContinuous();
        }

        // Pull = zoom out / climb out. Mirror logic, no tile gate
        // (climbing always succeeds when the ladder allows).
        if (currentZoom <= thresholds.ZoomMin + ZoomEpsilon)
        {
            return ZoomNavOutcome.ClimbCandidate();
        }
        return ZoomNavOutcome.ZoomContinuous();
    }

    /// <summary>
    /// Numerical fudge for the zoom limit comparison. Without this,
    /// floating-point accumulation across tween steps could leave the
    /// camera at <c>ZoomMax - 1e-7</c>, missing the cap-Push branch
    /// silently. 1e-3 is large enough to absorb every reasonable
    /// accumulation, small enough to not bite the threshold semantics
    /// at typical [0.5, 2.0] zoom ranges.
    /// </summary>
    public const float ZoomEpsilon = 1e-3f;
}

/// <summary>
/// Physical wheel gesture, post-translation from Godot's
/// <c>InputEventMouseButton.WheelUp/WheelDown</c> at the runtime seam.
/// The pull/push vocabulary is the only one allowed here per the P8
/// final-lock meta-lesson -- WheelUp/WheelDown labels are forbidden in
/// this seam and in <see cref="ZoomNavLogic"/>.
/// </summary>
public enum WheelGesture
{
    /// <summary>
    /// Geste vers le poignet (Logitech hardware Didier). Fires
    /// <c>MouseButton.WheelDown</c> at the engine layer. Slice 3 verb :
    /// zoom out (vue d'ensemble) and -- at <see cref="ZoomThresholds.ZoomMin"/>
    /// -- climb-out candidate.
    /// </summary>
    Pull,

    /// <summary>
    /// Geste vers les phalanges (Logitech hardware Didier). Fires
    /// <c>MouseButton.WheelUp</c> at the engine layer. Slice 3 verb :
    /// zoom in (détail) and -- at <see cref="ZoomThresholds.ZoomMax"/>
    /// -- drill-in candidate (gated by tile knowledge).
    /// </summary>
    Push,
}

/// <summary>
/// Action label returned by <see cref="ZoomNavLogic.EvaluateWheelInput"/>.
/// The runtime branches on this enum to either tween the camera, fire a
/// drill, fire a blocked-feedback, or fire a climb.
/// </summary>
public enum ZoomNavAction
{
    /// <summary>Below threshold or above floor : tween Camera2D.Zoom one step.</summary>
    ZoomContinuous,

    /// <summary>At ZoomMax + Push + cell knowledge >= Esquissée : drill into L2.</summary>
    DrillCandidate,

    /// <summary>At ZoomMax + Push + cell knowledge below Esquissée : play visual feedback, do not transition.</summary>
    DrillBlocked,

    /// <summary>At ZoomMin + Pull : pop back to the previous L0/L1 ladder rung.</summary>
    ClimbCandidate,

    /// <summary>Within the post-transition debounce window : ignore.</summary>
    Debounced,

    /// <summary>No-op (placeholder for future "wheel during transition" cases ; not currently emitted).</summary>
    None,
}

/// <summary>
/// Outcome record returned by <see cref="ZoomNavLogic.EvaluateWheelInput"/>.
/// The runtime branches on <see cref="Action"/> ; today the record carries
/// nothing else, but a record (not a bare enum) is the right shape because
/// slice 4 will extend the climb candidacy with a "snap to origin coord"
/// payload and the type stays binary-compat.
/// </summary>
/// <param name="Action">The decided action.</param>
public readonly record struct ZoomNavOutcome(ZoomNavAction Action)
{
    public static ZoomNavOutcome ZoomContinuous() => new(ZoomNavAction.ZoomContinuous);
    public static ZoomNavOutcome DrillCandidate() => new(ZoomNavAction.DrillCandidate);
    public static ZoomNavOutcome DrillBlocked() => new(ZoomNavAction.DrillBlocked);
    public static ZoomNavOutcome ClimbCandidate() => new(ZoomNavAction.ClimbCandidate);
    public static ZoomNavOutcome Debounced() => new(ZoomNavAction.Debounced);
    public static ZoomNavOutcome None() => new(ZoomNavAction.None);
}

/// <summary>
/// Configuration record for <see cref="ZoomNavLogic.EvaluateWheelInput"/>.
/// Default values are the brief slice 3 lock -- 0.5 / 1.0 / 2.0 with
/// 95 % drill-in line and 120 % climb-out line, 250 ms debounce. The
/// runtime <c>MapPan2DComponent</c> exposes each value as <c>[Export]</c>
/// so per-instance tuning (Varn §5.2 "smaller L3 quartier wants tighter
/// thresholds") doesn't require touching this file.
/// </summary>
/// <param name="ZoomMin">Minimum zoom value -- vue d'ensemble. Default 0.5.</param>
/// <param name="ZoomDefault">Initial zoom on scene entry -- medium. Default 1.0.</param>
/// <param name="ZoomMax">Maximum zoom value -- détail. Default 2.0.</param>
/// <param name="ZoomInThresholdRatio">Fraction of ZoomMax above which a Push at the cap fires drill. Default 0.95 (-> 1.90 with default Max).</param>
/// <param name="ZoomOutThresholdRatio">Multiplier of ZoomMin below which a Pull at the floor fires climb. Default 1.20 (-> 0.60 with default Min). Asymmetric vs ZoomInThresholdRatio per Varn.</param>
/// <param name="PostTransitionDebounceMs">Window after a drill/climb during which all wheel inputs are ignored. Default 250.</param>
public readonly record struct ZoomThresholds(
    float ZoomMin,
    float ZoomDefault,
    float ZoomMax,
    float ZoomInThresholdRatio,
    float ZoomOutThresholdRatio,
    ulong PostTransitionDebounceMs)
{
    /// <summary>Pre-computed drill-in line, used by diagnostics + xUnit.</summary>
    public float ZoomInThreshold => ZoomMax * ZoomInThresholdRatio;

    /// <summary>Pre-computed climb-out line, used by diagnostics + xUnit.</summary>
    public float ZoomOutThreshold => ZoomMin * ZoomOutThresholdRatio;

    /// <summary>Brief slice 3 default values. xUnit reuses ; runtime overrides via [Export].</summary>
    public static ZoomThresholds Default => new(
        ZoomMin: 0.5f,
        ZoomDefault: 1.0f,
        ZoomMax: 2.0f,
        ZoomInThresholdRatio: 0.95f,
        ZoomOutThresholdRatio: 1.20f,
        PostTransitionDebounceMs: 250);
}
