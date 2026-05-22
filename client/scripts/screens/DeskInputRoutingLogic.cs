using System.Numerics;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# screen → desk-world coordinate routing for the J3c-2
/// interactive desk (roadmap milestone J3c-2, Rune, 2026-05-22). Turns a
/// raw screen-pixel mouse position into a position in the desk's iso
/// <i>world</i> space — the space the desk boards' <c>IsoProjection</c>
/// then converts into a cell.
///
/// <para>
/// <b>The point that looked heavy and is not — routing input through
/// three SubViewports.</b> The desk is rendered through THREE
/// co-located SubViewports (the boxed architecture comment in
/// <c>GameScreen.cs</c>): a clipped FLOOR viewport, an unclipped
/// ENTITIES viewport, and — at a different iso scale — the maquette
/// viewport. A naïve reading is "a click must be routed into the right
/// SubViewport and converted there". That reading is wrong for the
/// desk, and the reason it is wrong is the locked architecture itself:
/// <list type="bullet">
///   <item>The desk's FLOOR camera and ENTITIES camera are parked at an
///     <i>identical</i> Position and Zoom — by construction, computed
///     once in pure-C# and applied to both (see <c>GameScreen</c>'s
///     <c>DeskCameraZoom</c> / <c>deskCameraPos</c>). So a screen pixel
///     maps to the <i>same</i> desk-world point in both viewports.
///     There is one desk camera frame, not two — therefore one
///     screen → desk-world conversion, not two.</item>
///   <item>The desk SubViewports are full-screen and their TextureRects
///     are full-screen at the screen origin. A screen pixel and a desk
///     viewport pixel are the same number; no per-viewport offset.</item>
///   <item>The pawns are layer-3 occupants placed at
///     <c>CellToPixel(cell)</c> in the entities board's local space,
///     and the floor grid lives in the floor board's local space — the
///     two boards are the same scene at the same tile scale with the
///     same (zero) board nudge, so a cell projects to the same pixel in
///     both. Hit-testing a click is therefore a single
///     <c>WorldToCell</c> against one shared projection.</item>
/// </list>
/// So the multi-SubViewport routing collapses to one affine map. This
/// helper is that map; it does NOT need to know there are three
/// viewports. The maquette viewport is a separate iso scale and a
/// separate (pannable) camera — a desk click is never a maquette click,
/// the desk TextureRects sit on top, so the desk simply consumes the
/// event first. Routing <i>between</i> the desk and the maquette is the
/// J6 concern; J3c-2 owns only the desk side.
/// </para>
///
/// <para>
/// <b>The conversion.</b> A Godot <see cref="Camera2D"/> shows its
/// <c>Position</c> at the centre of its viewport; a screen offset
/// <c>d</c> from the viewport centre maps to a world offset
/// <c>d / zoom</c>. So
/// <c>world = cameraCentre + (screenPos - screenSize/2) / zoom</c>.
/// This is the exact inverse of the camera-park maths
/// <c>GameScreen.ConfigureDesk</c> uses to place the formation on
/// screen — pinning it here as a pure function makes a sign flip or a
/// dropped <c>/zoom</c> term surface red in xUnit rather than as a
/// click that selects the wrong pawn.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Speaks only
/// <see cref="System.Numerics.Vector2"/> so it is cherry-pickable into
/// the xUnit host with no GodotSharp — same seam discipline as
/// <see cref="MapViewportPanLogic"/>, <see cref="DeskFloorRectLogic"/>,
/// and the rest of the J3 logic layer. The Godot-bound
/// <c>GameScreen</c> converts <c>Godot.Vector2</c> ↔
/// <see cref="System.Numerics.Vector2"/> at the engine edge and then
/// hands the desk-world point to the desk board's <c>IsoProjection</c>.
/// </para>
/// </summary>
public static class DeskInputRoutingLogic
{
    /// <summary>
    /// Map a screen-pixel mouse position to a position in the desk's iso
    /// world space, given the desk camera frame (the one frame both desk
    /// cameras share).
    /// </summary>
    /// <param name="screenPos">
    /// Mouse position in screen pixels — <c>InputEventMouse.Position</c>
    /// at the engine edge. The desk TextureRects are full-screen at the
    /// origin, so this is also the desk viewport pixel.
    /// </param>
    /// <param name="screenSize">
    /// Screen size in pixels (<c>GetViewportRect().Size</c>). The desk
    /// SubViewports are full-screen, so this is also the viewport render
    /// size.
    /// </param>
    /// <param name="deskCameraCentre">
    /// The desk camera <c>Position</c> — the desk-world point shown at
    /// the screen centre. Identical on the floor and entities cameras
    /// (the locked invariant); pass either.
    /// </param>
    /// <param name="deskCameraZoom">
    /// The desk camera <c>Zoom</c>. Identical on both desk cameras.
    /// Must be positive on both axes.
    /// </param>
    /// <returns>The position in desk-world pixels.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// If either zoom component is non-positive — a zero or negative
    /// zoom is a misconfigured camera, not a routable click.
    /// </exception>
    public static Vector2 ScreenToDeskWorld(
        Vector2 screenPos,
        Vector2 screenSize,
        Vector2 deskCameraCentre,
        Vector2 deskCameraZoom)
    {
        if (deskCameraZoom.X <= 0f || deskCameraZoom.Y <= 0f)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(deskCameraZoom),
                $"Desk camera zoom must be positive on both axes; got " +
                $"{deskCameraZoom}.");
        }

        var screenCentre = screenSize * 0.5f;
        var offsetFromCentre = screenPos - screenCentre;
        var worldOffset = new Vector2(
            offsetFromCentre.X / deskCameraZoom.X,
            offsetFromCentre.Y / deskCameraZoom.Y);
        return deskCameraCentre + worldOffset;
    }
}
