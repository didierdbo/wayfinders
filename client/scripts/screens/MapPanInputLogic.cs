namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# state machine that decides drag-start / drag-update / drag-end
/// for the E2 World Map pan gesture (P8.3, M3 / Arc 3 / Phase 8.3).
/// Same Godot-free pattern as <see cref="LadderResolutionLogic"/> at P8.1
/// and <see cref="CameraPanLogic"/> at P8.2: the runtime
/// <see cref="Wayfinders.Client.Scenes.Screens.E2WorldMap"/> translates
/// <see cref="Godot.InputEventMouseButton"/> /
/// <see cref="Godot.InputEventMouseMotion"/> events into method calls on
/// this helper, the helper returns intent records, and the runtime
/// applies the intent to the Camera2D + SceneManager seam.
///
/// <para>
/// <b>State shape (P8.3 §3.5).</b>
/// <list type="bullet">
///   <item><see cref="MapPanState.Idle"/> -- no active button, no
///         pending drag.</item>
///   <item><see cref="MapPanState.Tracking"/> -- the active button is
///         held but the cursor has not yet moved beyond the drag
///         threshold. Reserved for <see cref="MapPanButton.Right"/> :
///         the 6 px gate exists so a click-without-motion can later
///         open a context menu without falsely committing to pan.
///         <see cref="MapPanButton.Middle"/> never enters this state ;
///         it skips directly to <see cref="MapPanState.Dragging"/>
///         on press (no threshold, single-purpose button).</item>
///   <item><see cref="MapPanState.Dragging"/> -- the camera is being
///         actively translated. The runtime reads
///         <see cref="MotionOutcome.PressPosition"/> /
///         <see cref="MotionOutcome.CameraStart"/> to compute the pan
///         delta as <c>mousePosition - pressPosition</c>, applied as an
///         inverse offset to <c>cameraStart</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Threshold semantics (P8.3 §3.5 + Pax 6 px lock).</b>
/// The threshold is the squared euclidean distance from the press
/// position. A motion event with <c>dx*dx + dy*dy &gt; 6*6</c> promotes
/// <see cref="MapPanState.Tracking"/> -&gt; <see cref="MapPanState.Dragging"/>.
/// Strictly <c>&gt;</c> not <c>&gt;=</c> so a cursor that grazes exactly
/// the boundary does not commit -- gives the player one frame of grace
/// at the edge.
/// </para>
///
/// <para>
/// <b>Delta-from-press, not delta-from-cross (P8.3 §3.5 sweet spot).</b>
/// On promotion, <see cref="MotionOutcome.PressPosition"/> still carries
/// the original press point, not the cross point. The runtime applies
/// the full delta from press to keep the camera following the cursor 1:1
/// without a 6 px visual jump at the threshold crossing. Same pattern as
/// Google Maps, Unity Editor, Figma -- pinned by
/// <c>Promotion_outcome_carries_press_position_for_full_delta_application</c>.
/// </para>
///
/// <para>
/// <b>Why this is Godot-free.</b> Every input enters as a primitive
/// (<see cref="MapPanButton"/> enum, <see cref="PanVec2"/> for positions),
/// every output exits as a record struct. The runtime
/// <c>E2WorldMap._Input</c> handles the
/// <see cref="Godot.MouseButton"/> -&gt; <see cref="MapPanButton"/>
/// translation and the <see cref="Godot.Vector2"/> -&gt;
/// <see cref="PanVec2"/> conversion at the seam. xUnit pins this contract
/// without an engine -- see
/// <c>Wayfinders.Client.Tests.csproj</c> for the <c>Compile Include</c>.
/// </para>
///
/// <para>
/// <b>Reset discipline (P8.3 §3.6 D-P8.3-11).</b>
/// <see cref="Reset"/> snaps the state machine back to
/// <see cref="MapPanState.Idle"/> regardless of where it was. Two
/// callers wire it: (1) modal-open in <c>E2WorldMap._Input</c> -- mirror
/// of P8.1's modal-owns-input rule ; (2) <c>OnSettingsChanged</c> --
/// if the player toggles MMB-vs-RMB while a drag is live, we cut the
/// gesture cleanly instead of carrying ambiguous state across the
/// preference flip. The second hook is the defense-in-depth complement
/// to the first ; the modal-open path already covers most of the
/// observed cases but not the hypothetical "Options modal closes and
/// the next event arrives mid-toggle" frame race.
/// </para>
/// </summary>
public sealed class MapPanInputLogic
{
    /// <summary>
    /// Drag threshold in screen pixels. Locked by Pax 2026-05-07 research
    /// at the sweet spot of Windows SM_CXDRAG (4 px), browsers (~5 px),
    /// Unity / Unreal (5-10 px). 6 px = enough to filter wrist tremor on
    /// a click-without-motion intent, low enough that a deliberate drag
    /// does not feel laggy at the edge. Not user-configurable -- this is
    /// UX tuning, not a gameplay preference. If a future hardware report
    /// surfaces a mismatch, swap the constant and re-run
    /// <c>MapPanInputLogicTests</c> ; nothing else depends on the value.
    /// </summary>
    public const float DragThresholdPx = 6f;

    private MapPanState _state = MapPanState.Idle;
    private PanVec2 _pressPosition;
    private PanVec2 _cameraStart;

    /// <summary>Current state. Exposed for diagnostics + tests, not for runtime branching.</summary>
    public MapPanState State => _state;

    /// <summary>
    /// Called on the press edge of the active pan button. Records the
    /// press position and the camera position at press time so a future
    /// drag can compute its delta from these anchors. Returns whether
    /// the press promoted directly to <see cref="MapPanState.Dragging"/>
    /// (true for <see cref="MapPanButton.Middle"/>, false for
    /// <see cref="MapPanButton.Right"/> which enters
    /// <see cref="MapPanState.Tracking"/> first).
    ///
    /// <para>
    /// <b>State preconditions.</b> The runtime is expected to call
    /// <see cref="OnPress"/> only when the active button is the one
    /// currently mapped to the pan gesture (filter at the
    /// <c>InputEventMouseButton.ButtonIndex</c> check). A press of an
    /// inactive button never reaches this method.
    /// </para>
    /// </summary>
    /// <param name="activeButton">The currently configured pan button.</param>
    /// <param name="mousePosition">Cursor position in viewport coords at press time.</param>
    /// <param name="cameraPosition">Camera2D center in world coords at press time.</param>
    /// <returns>A press outcome carrying the entered-drag flag for runtime logging.</returns>
    public PressOutcome OnPress(MapPanButton activeButton, PanVec2 mousePosition, PanVec2 cameraPosition)
    {
        _pressPosition = mousePosition;
        _cameraStart = cameraPosition;
        _state = activeButton == MapPanButton.Middle
            ? MapPanState.Dragging
            : MapPanState.Tracking;
        return new PressOutcome(EnteredDrag: _state == MapPanState.Dragging);
    }

    /// <summary>
    /// Called on every mouse motion event while the active button is
    /// held. Returns a <see cref="MotionOutcome"/> describing what the
    /// runtime should do: nothing (still below threshold or idle), pan
    /// continuation (already dragging), or just-promoted-to-dragging
    /// (cross threshold this frame). The promotion case carries the
    /// original press position so the runtime computes the full delta
    /// from press, not from cross point -- avoids the 6 px visual jump.
    /// </summary>
    /// <param name="mousePosition">Current cursor position in viewport coords.</param>
    /// <returns>Motion outcome ready for the runtime to apply or skip.</returns>
    public MotionOutcome OnMotion(PanVec2 mousePosition)
    {
        if (_state == MapPanState.Idle) return MotionOutcome.NoOp;

        if (_state == MapPanState.Tracking)
        {
            var dx = mousePosition.X - _pressPosition.X;
            var dy = mousePosition.Y - _pressPosition.Y;
            if ((dx * dx + dy * dy) > DragThresholdPx * DragThresholdPx)
            {
                _state = MapPanState.Dragging;
                return MotionOutcome.PromotedToDrag(_pressPosition, _cameraStart);
            }
            return MotionOutcome.NoOp;
        }

        // Already Dragging. The runtime computes the pan delta from the
        // current mousePosition minus _pressPosition (full-delta-from-press
        // semantic), so we surface the anchor pair on every motion event.
        return MotionOutcome.PanContinue(_pressPosition, _cameraStart);
    }

    /// <summary>
    /// Called on the release edge of the active pan button. Returns
    /// whether the release ended an actual drag (Dragging -&gt; Idle)
    /// or merely an unproductive Tracking session (Tracking -&gt; Idle).
    /// The runtime fires <c>SceneManager.NotifyDragReleased</c> only on
    /// the former -- the latter is a no-op silent release (slot for the
    /// post-MVP context menu, kept silent in MVP per Varn §3 garde-fou
    /// to avoid the UI fantôme effect).
    /// </summary>
    /// <returns>Release outcome with the was-dragging flag.</returns>
    public ReleaseOutcome OnRelease()
    {
        var wasDragging = _state == MapPanState.Dragging;
        _state = MapPanState.Idle;
        return new ReleaseOutcome(WasDragging: wasDragging);
    }

    /// <summary>
    /// Snap to <see cref="MapPanState.Idle"/> regardless of where the
    /// state machine currently sits. Wired by two callers: modal-open
    /// in <c>E2WorldMap._Input</c> (mirrors P8.1 modal-owns-input) and
    /// the <c>SettingsChanged</c> handler (so a mid-drag MMB-to-RMB
    /// flip does not leak ambiguous state across the preference change).
    /// </summary>
    public void Reset() => _state = MapPanState.Idle;

    /// <summary>
    /// Press-edge outcome. <see cref="EnteredDrag"/> is true iff the
    /// press immediately committed to a drag (MMB path). False means
    /// the state machine is now in <see cref="MapPanState.Tracking"/>
    /// awaiting motion (RMB path). The runtime uses the flag to decide
    /// whether to log a "drag begin" line or stay silent until motion
    /// crosses the threshold.
    /// </summary>
    /// <param name="EnteredDrag">True iff the state is now <see cref="MapPanState.Dragging"/>.</param>
    public readonly record struct PressOutcome(bool EnteredDrag);

    /// <summary>
    /// Motion-frame outcome. Three logical shapes:
    /// <list type="bullet">
    ///   <item><see cref="NoOp"/> -- caller skips the camera update.
    ///         <see cref="ShouldPan"/> false, anchor fields zeroed.</item>
    ///   <item><see cref="PromotedToDrag"/> -- this frame crossed the
    ///         threshold. <see cref="ShouldPan"/> true,
    ///         <see cref="JustPromoted"/> true, anchor pair carries the
    ///         original press anchors so the runtime applies the full
    ///         delta-from-press.</item>
    ///   <item><see cref="PanContinue"/> -- already dragging.
    ///         <see cref="ShouldPan"/> true, <see cref="JustPromoted"/>
    ///         false, anchor pair carries the same press anchors as on
    ///         every motion event during the drag.</item>
    /// </list>
    /// </summary>
    /// <param name="ShouldPan">True iff the runtime should compute a pan delta this frame.</param>
    /// <param name="JustPromoted">True iff this is the threshold-cross frame (RMB path).</param>
    /// <param name="PressPosition">Press anchor in viewport coords (zero on NoOp).</param>
    /// <param name="CameraStart">Camera anchor in world coords (zero on NoOp).</param>
    public readonly record struct MotionOutcome(
        bool ShouldPan,
        bool JustPromoted,
        PanVec2 PressPosition,
        PanVec2 CameraStart)
    {
        /// <summary>The runtime should skip this motion event (idle or below threshold).</summary>
        public static MotionOutcome NoOp => new(false, false, default, default);

        /// <summary>This motion event crossed the threshold ; runtime should log + apply.</summary>
        public static MotionOutcome PromotedToDrag(PanVec2 pressPosition, PanVec2 cameraStart) =>
            new(true, true, pressPosition, cameraStart);

        /// <summary>Continuing an active drag ; runtime applies pan but does not log.</summary>
        public static MotionOutcome PanContinue(PanVec2 pressPosition, PanVec2 cameraStart) =>
            new(true, false, pressPosition, cameraStart);
    }

    /// <summary>
    /// Release-edge outcome. <see cref="WasDragging"/> is true iff the
    /// release ended an actual drag (state was
    /// <see cref="MapPanState.Dragging"/> at the release edge). The
    /// runtime fires <c>SceneManager.NotifyDragReleased</c> only on
    /// true -- a Tracking-only release is a silent no-op slot for the
    /// post-MVP context menu.
    /// </summary>
    /// <param name="WasDragging">True iff the release ended an actual drag.</param>
    public readonly record struct ReleaseOutcome(bool WasDragging);
}

/// <summary>
/// Three-state machine for the world-map pan gesture. See
/// <see cref="MapPanInputLogic"/> class doc for transitions and
/// rationale. Exposed as a public enum so xUnit can assert state
/// directly via <see cref="MapPanInputLogic.State"/>.
/// </summary>
public enum MapPanState
{
    /// <summary>No active button, no pending drag.</summary>
    Idle,

    /// <summary>RMB held but cursor has not yet crossed the threshold.</summary>
    Tracking,

    /// <summary>Camera is being actively translated by mouse motion.</summary>
    Dragging,
}
