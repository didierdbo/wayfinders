namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# orchestration of the J3a map-viewport pan: turns a stream of
/// mouse press / motion / release events into a clamped
/// <c>MapCamera2D</c> centre position. The Godot-bound
/// <see cref="Wayfinders.Client.Scenes.Screens.GameScreen"/> translates
/// engine <c>InputEvent</c>s into calls on this helper and assigns the
/// returned centre to its internal <c>Camera2D.Position</c> — nothing
/// else.
///
/// <para>
/// <b>Why a third helper, and why it does not duplicate the other two.</b>
/// J3a's pan is exactly two already-shipped, already-tested pieces wired
/// together:
/// <list type="number">
///   <item><see cref="MapPanInputLogic"/> — the P8.3 three-state machine
///     (Idle / Tracking / Dragging) that decides <i>when</i> a drag is
///     live and surfaces the press anchors.</item>
///   <item><see cref="CameraPanLogic.ClampCameraCenter"/> — the P8.2
///     rectangular clamp that decides <i>where</i> a camera centred over
///     a finite image is allowed to sit.</item>
/// </list>
/// This class owns neither algorithm. It owns the <i>composition</i>: on
/// every motion event during a drag it computes the delta-from-press,
/// applies it inverse to the camera-start anchor, and runs the result
/// through the clamp. Re-deriving either the state machine or the clamp
/// here would be the trap #6 orthogonality violation — two clamps
/// scattered across two files diverge silently. There is one clamp
/// (<see cref="CameraPanLogic"/>) and one drag state machine
/// (<see cref="MapPanInputLogic"/>); this helper just calls them.
/// </para>
///
/// <para>
/// <b>Pan binding — middle mouse button.</b> Locked to
/// <see cref="MapPanButton.Middle"/>, the same default the P8.3 World Map
/// pan already shipped (D-P8.3-05, Pax research: 12/17 modern
/// iso/strategy titles ship MMB-pan). Two reasons it is the right call
/// for the maquette specifically: (a) consistency — the player pans the
/// E2 world map and the J3a maquette with the identical gesture; (b) it
/// keeps the left mouse button entirely free for entity selection, which
/// is the very next milestone on this surface (J3c-2 desk pawns, J6 POI
/// markers, J8 recruit click). Binding pan to left-drag would force a
/// click-vs-drag disambiguation against selection from day one — the
/// "shipped Steam title" choice is to not create that conflict. MMB also
/// skips the 6 px drag threshold (single-purpose button), so the maquette
/// pans 1:1 from the first pixel of motion.
/// </para>
///
/// <para>
/// <b>Delta-from-press, inverse-applied.</b> While dragging, the camera
/// centre is <c>cameraStart - (mouseNow - pressPosition)</c>. The minus
/// is the "grab the world and drag it" feel: moving the cursor right
/// pulls the camera left so the map content tracks the cursor. The full
/// delta is measured from the original press point (not incrementally
/// frame-to-frame) so a dropped or coalesced motion event cannot make
/// the pan drift — same anti-drift discipline as
/// <see cref="MapPanInputLogic"/>'s delta-from-press contract.
/// </para>
///
/// <para>
/// <b>Clamp bounds — the maquette content extent.</b> The clamp keeps the
/// <c>SubViewport</c>'s visible rectangle inside the maquette content. The
/// caller passes the content size (the <c>IsoBoard</c> background bitmap
/// size, or the placeholder extent until Mira's asset lands) and the
/// SubViewport size. <see cref="CameraPanLogic.ClampCameraCenter"/> then
/// forbids panning into the void past the map edge. If the content is
/// smaller than the viewport on an axis, that axis snaps to the content
/// centre (letterbox) — the P8.2 helper already handles this.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Every input is a primitive
/// (<see cref="PanVec2"/>); every output is a <see cref="PanVec2"/> or a
/// state enum. The runtime <c>GameScreen._Input</c> does the
/// <c>Godot.Vector2</c> ↔ <see cref="PanVec2"/> conversion at the seam.
/// xUnit pins the composition contract without an engine — see
/// <c>Wayfinders.Client.Tests.csproj</c> for the <c>Compile Include</c>.
/// </para>
/// </summary>
public sealed class MapViewportPanLogic
{
    private readonly MapPanInputLogic _drag = new();

    /// <summary>
    /// The pan button. Locked to <see cref="MapPanButton.Middle"/> — see
    /// the class doc for the rationale. Exposed so the runtime filters
    /// <c>InputEventMouseButton.ButtonIndex</c> against the same value
    /// the helper expects, with no magic constant on the engine side.
    /// </summary>
    public static MapPanButton PanButton => MapPanButton.Middle;

    /// <summary>
    /// Current drag state, for diagnostics and the preflight print. Not
    /// for runtime branching — the runtime branches on the returned
    /// <see cref="PanOutcome.CameraMoved"/> flag instead.
    /// </summary>
    public MapPanState State => _drag.State;

    /// <summary>
    /// Handle the press edge of the pan button. Records the press anchor
    /// and the camera centre at press time inside the wrapped
    /// <see cref="MapPanInputLogic"/>. Returns a no-move outcome: a press
    /// alone never moves the camera, only subsequent motion does.
    /// </summary>
    /// <param name="mousePosition">
    /// Cursor position, in the <c>SubViewport</c>'s coordinate space, at
    /// press time. The runtime converts the engine event position into
    /// SubViewport space before calling — the SubViewport already
    /// delivers events in its own space, so this is usually the raw
    /// event position.
    /// </param>
    /// <param name="cameraCentre">
    /// The <c>MapCamera2D</c> centre at press time, world coords.
    /// </param>
    /// <returns>A no-move outcome carrying the unchanged camera centre.</returns>
    public PanOutcome OnPress(PanVec2 mousePosition, PanVec2 cameraCentre)
    {
        // MMB enters Dragging directly (no threshold) — see MapPanButton
        // doc. The press itself moves nothing.
        _drag.OnPress(PanButton, mousePosition, cameraCentre);
        return PanOutcome.NoMove(cameraCentre);
    }

    /// <summary>
    /// Handle a mouse-motion event. When a drag is live, computes the
    /// new clamped camera centre as
    /// <c>cameraStart - (mousePosition - pressPosition)</c> run through
    /// <see cref="CameraPanLogic.ClampCameraCenter"/>. When no drag is
    /// live, returns a no-move outcome.
    /// </summary>
    /// <param name="mousePosition">
    /// Current cursor position in <c>SubViewport</c> coordinate space.
    /// </param>
    /// <param name="contentSize">
    /// Size of the pannable maquette content (the <c>IsoBoard</c> floor
    /// bitmap, or the placeholder extent), world pixels.
    /// </param>
    /// <param name="viewportSize">
    /// Size of the <c>SubViewport</c> visible rectangle, world pixels.
    /// </param>
    /// <returns>
    /// A <see cref="PanOutcome"/>: <see cref="PanOutcome.CameraMoved"/>
    /// false when idle, true when the drag produced a (possibly clamped)
    /// new centre.
    /// </returns>
    public PanOutcome OnMotion(PanVec2 mousePosition, PanVec2 contentSize, PanVec2 viewportSize)
    {
        var motion = _drag.OnMotion(mousePosition);
        if (!motion.ShouldPan)
        {
            return PanOutcome.NoMove(default);
        }

        // delta = how far the cursor has travelled since press.
        var deltaX = mousePosition.X - motion.PressPosition.X;
        var deltaY = mousePosition.Y - motion.PressPosition.Y;

        // Inverse-apply: dragging the cursor right pulls the camera left
        // so the map content follows the cursor ("grab the world").
        var desired = new PanVec2(
            motion.CameraStart.X - deltaX,
            motion.CameraStart.Y - deltaY);

        var clamped = CameraPanLogic.ClampCameraCenter(desired, contentSize, viewportSize);
        return PanOutcome.Move(clamped);
    }

    /// <summary>
    /// Handle the release edge of the pan button. Ends the drag inside
    /// the wrapped state machine. Returns a no-move outcome carrying the
    /// supplied current centre — release does not itself move the camera.
    /// </summary>
    /// <param name="cameraCentre">The current camera centre, world coords.</param>
    /// <returns>A no-move outcome carrying the unchanged camera centre.</returns>
    public PanOutcome OnRelease(PanVec2 cameraCentre)
    {
        _drag.OnRelease();
        return PanOutcome.NoMove(cameraCentre);
    }

    /// <summary>
    /// Snap the drag state machine back to <see cref="MapPanState.Idle"/>.
    /// Wired by the runtime on <c>_ExitTree</c> and any future modal-open
    /// — same reset discipline as <see cref="MapPanInputLogic.Reset"/>.
    /// </summary>
    public void Reset() => _drag.Reset();

    /// <summary>
    /// Re-clamp a camera centre against (possibly new) content and
    /// viewport sizes, without any drag in progress. The runtime calls
    /// this once at <c>_Ready</c> — after the camera is parked at its
    /// start position but before the first frame — so a start position
    /// authored slightly out of bounds is corrected immediately rather
    /// than on the first drag. Also the seam a future window-resize
    /// handler would call.
    /// </summary>
    /// <param name="cameraCentre">The camera centre to validate.</param>
    /// <param name="contentSize">Maquette content size, world pixels.</param>
    /// <param name="viewportSize">SubViewport size, world pixels.</param>
    /// <returns>The clamped camera centre.</returns>
    public static PanVec2 ClampToContent(
        PanVec2 cameraCentre,
        PanVec2 contentSize,
        PanVec2 viewportSize)
        => CameraPanLogic.ClampCameraCenter(cameraCentre, contentSize, viewportSize);
}

/// <summary>
/// Outcome of one <see cref="MapViewportPanLogic"/> event. Carries
/// whether the camera should move this event and, if so, the clamped
/// target centre. A no-move outcome leaves <see cref="Centre"/> at
/// whatever the caller passed (press / release) or <c>default</c>
/// (an idle motion event the runtime simply skips).
/// </summary>
/// <param name="CameraMoved">
/// True iff the runtime should assign <see cref="Centre"/> to
/// <c>Camera2D.Position</c> this event.
/// </param>
/// <param name="Centre">
/// The clamped camera centre, world coords. Meaningful only when
/// <see cref="CameraMoved"/> is true (move outcomes) or when the caller
/// passed its own current centre (press / release outcomes).
/// </param>
public readonly record struct PanOutcome(bool CameraMoved, PanVec2 Centre)
{
    /// <summary>The camera should not move; <paramref name="centre"/> is the unchanged current centre.</summary>
    public static PanOutcome NoMove(PanVec2 centre) => new(false, centre);

    /// <summary>The camera should move to the clamped <paramref name="centre"/>.</summary>
    public static PanOutcome Move(PanVec2 centre) => new(true, centre);
}
