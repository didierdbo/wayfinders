using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry for the J3c-1bis desk <i>entity layer</i> — the
/// screen-space node that hosts the desk pawns (and, later, any vertical
/// element posed on the desk floor) <b>without</b> the triangular clip
/// shader cutting them.
///
/// <para>
/// <b>The bug this solves (F6, 2026-05-21).</b> The desk floor is clipped
/// to a bottom-left triangle by <c>desk_triangle_clip.gdshader</c>, which
/// runs on the <c>DeskTextureRect</c> showing the whole <c>DeskViewport</c>
/// render. But that render contains the floor <i>and</i> the pawns: the
/// shader cut every fragment past the diagonal, so a pawn standing near
/// the floor edge had its body and head sliced off — only its feet
/// survived. The clip must cut <b>only the floor</b> (the brown iso
/// plancher): pawns posed on the floor are vertical and legitimately rise
/// above the floor's diagonal edge — a head poking past the floor edge is
/// correct, not a bug.
/// </para>
///
/// <para>
/// <b>The fix — entities live outside the clipped SubViewport.</b> The
/// <c>DeskViewport</c> keeps <i>only</i> the floor + grid; the clip shader
/// keeps cutting that. The pawns move to a separate Y-sorted
/// <see cref="System.Object">Node2D</see> ("DeskEntities") parented under
/// the screen-space <c>Control</c> root, carrying <b>no</b> shader. The
/// two locked SubViewports (maquette + desk) are untouched — the entity
/// layer is a plain node, not a third render world.
/// </para>
///
/// <para>
/// <b>What this helper returns.</b> The <see cref="System.Object">Node2D</see>
/// position the entity layer must sit at so a pawn placed at a desk-local
/// pixel (the same <c>IsoBoard.CellToPixel</c> coordinate the floor's iso
/// grid uses) lands on screen at exactly the same place the clipped floor
/// draws it. The desk camera shows world point <c>cameraCentre</c> at the
/// viewport centre, and the <c>DeskViewport</c> + <c>DeskTextureRect</c>
/// are both full-screen at the screen origin — so a desk-local point
/// <c>P</c> appears on screen at <c>P - cameraCentre + screenSize/2</c>.
/// A child placed at desk-local <c>P</c> under a parent at
/// <c>screenSize/2 - cameraCentre</c> therefore lands on that exact
/// screen pixel: the entity layer and the clipped floor share one
/// coordinate frame, the pawns sit on their cells, only the clip differs.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Everything is
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> — so
/// this helper is cherry-pickable into the xUnit host with no GodotSharp.
/// <c>GameScreen</c> converts at the engine seam. Same logic-vs-node split
/// as <see cref="DeskClipFrontierLogic"/>, <see cref="DeskFloorRectLogic"/>,
/// and <see cref="DeskSlotLayoutLogic"/>.
/// </para>
/// </summary>
public static class DeskEntityLayerLogic
{
    /// <summary>
    /// The screen-space position the desk entity-layer <c>Node2D</c> must
    /// be placed at so a child posed at a desk-local pixel lands on screen
    /// exactly where the clipped desk floor draws that same pixel.
    ///
    /// <para>
    /// Derivation: a desk-local point <c>P</c> renders on screen at
    /// <c>P - cameraCentre + screenSize/2</c> (the immobile desk camera
    /// shows <c>cameraCentre</c> at the centre of the full-screen viewport).
    /// A Godot child at local position <c>P</c> under a parent at position
    /// <c>L</c> renders at <c>L + P</c>. Equate: <c>L = screenSize/2 -
    /// cameraCentre</c>.
    /// </para>
    /// </summary>
    /// <param name="deskCameraCentre">
    /// The desk camera's <c>Position</c> — the desk-local world point shown
    /// at the centre of the desk viewport.
    /// </param>
    /// <param name="screenSize">
    /// The full screen size in pixels — equal to the desk viewport and the
    /// desk <c>TextureRect</c> size (both full-screen by the J3c-1bis F6
    /// fix). Both components must be positive.
    /// </param>
    /// <returns>
    /// The position to assign to the desk entity-layer <c>Node2D</c>.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// If either screen dimension is non-positive — a caller bug, not a
    /// silent default.
    /// </exception>
    public static SysVec2 LayerOrigin(SysVec2 deskCameraCentre, SysVec2 screenSize)
    {
        if (screenSize.X <= 0f || screenSize.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Screen size must be positive; got {screenSize}.",
                nameof(screenSize));
        }

        return screenSize * 0.5f - deskCameraCentre;
    }

    /// <summary>
    /// Where a desk-local pixel lands on screen, given the entity-layer
    /// origin from <see cref="LayerOrigin"/>. Exposed so a test (and the
    /// <c>GameScreen</c> preflight) can assert a pawn at a desk cell ends
    /// up on the same screen pixel the clipped floor would draw it at —
    /// the whole point of the shared coordinate frame.
    /// </summary>
    /// <param name="deskLocalPixel">
    /// A point in the desk board's local pixel space — typically an
    /// <c>IsoBoard.CellToPixel</c> result for a slot cell.
    /// </param>
    /// <param name="layerOrigin">
    /// The entity-layer node position, from <see cref="LayerOrigin"/>.
    /// </param>
    /// <returns>The screen pixel the desk-local point renders at.</returns>
    public static SysVec2 DeskLocalToScreen(SysVec2 deskLocalPixel, SysVec2 layerOrigin)
        => layerOrigin + deskLocalPixel;
}
