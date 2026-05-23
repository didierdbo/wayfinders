using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helper that maps the pixel-space vertices of one desk-floor
/// wedge (<see cref="DeskTrianglePlaceholderLogic.Wedge"/>) to the
/// <b>normalized texture UVs</b> the engine seam hands to
/// <c>CanvasItem.DrawColoredPolygon</c> so Mira's
/// <c>wf_e1_desk_wood_floor.png</c> samples coherently inside the wedge.
///
/// <para>
/// <b>The bug this solves (2026-05-23 wedge-rendering round).</b> The
/// previous engine-seam implementation in
/// <c>IsoBoard.DrawDeskWoodWedge</c> set the UV of vertex <c>v</c> to
/// <c>v - rect.TopLeft</c> — in <i>pixel</i> coordinates — and passed
/// that to <c>DrawColoredPolygon(points, color, uvs, texture)</c>. Godot 4
/// expects UVs in the normalized <c>[0,1]</c> domain ; with values of
/// magnitude ~10² the texture sampler ran far outside that domain and
/// clamped to the edge texel (the default texture wrap mode is clamp). The
/// whole wedge then read as a flat colour — the "aplat marron" F6 bug —
/// and any non-opaque pixel at the texture edge leaked the maquette
/// (<c>SubViewport</c> behind layer C) through the floor. Same root
/// cause for two visible symptoms.
/// </para>
///
/// <para>
/// <b>The contract this helper pins.</b> Given a wedge in the board's
/// local pixel space and the floor rect Mira's wood bitmap is authored
/// against, <see cref="ComputeNormalizedUvs"/> returns one UV per wedge
/// vertex in the same clockwise order
/// (<c>BottomCorner → BottomCentre → InnerApex → SideShoulder</c>) with
/// each component in <c>[0,1]</c> when the wedge sits inside the rect.
/// The mapping is <c>uv = (vert - rect.TopLeft) / rect.Size</c> — a single
/// affine register of the wood bitmap onto the floor rect, with the rect
/// itself acting as the texture's <c>[0,1]² → screen</c> registration.
/// Continuity across the two wedges follows automatically: every vertex
/// shared by both wedges (<c>BottomCentre</c>, <c>InnerApex</c>) gets the
/// same UV from both calls, so the texture is seamless along the V.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Everything is
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> — so
/// this helper is cherry-pickable into the xUnit host with no GodotSharp.
/// <c>IsoBoard</c> converts at the engine seam. Same logic-vs-node split
/// as <see cref="DeskTrianglePlaceholderLogic"/> and
/// <see cref="DeskFloorRectLogic"/>.
/// </para>
/// </summary>
public static class DeskWoodWedgeUvLogic
{
    /// <summary>
    /// Compute the normalized [0,1] texture UVs for the four wedge
    /// vertices, in the same order <see cref="DeskTrianglePlaceholderLogic.Vertices"/>
    /// returns them.
    ///
    /// <para>
    /// The wood bitmap is authored at the floor rect's size and registered
    /// onto its top-left, so the affine map is just
    /// <c>uv = (vert - rect.TopLeft) / rect.Size</c>. A vertex sitting at
    /// the floor rect's top-left gets <c>(0,0)</c> ; a vertex at the
    /// bottom-right gets <c>(1,1)</c> ; values outside <c>[0,1]</c> are
    /// returned unclamped so a caller-side assertion or a deliberate
    /// out-of-bounds wedge stays observable. Edge clamping (<see cref="Godot"/>'s
    /// default texture wrap mode) handles the sub-pixel safety slack ; the
    /// helper does not need to clamp itself.
    /// </para>
    /// </summary>
    /// <param name="wedge">
    /// The four-vertex convex polygon to texture, in board-local pixels.
    /// </param>
    /// <param name="rect">
    /// The floor rect Mira's wood bitmap is registered onto. <c>Size</c>
    /// must be strictly positive on both axes ; an axis-aligned floor rect
    /// with a zero-area is a caller bug, not a silent default.
    /// </param>
    /// <returns>
    /// Four normalized UVs in the same clockwise order as
    /// <see cref="DeskTrianglePlaceholderLogic.Vertices"/> :
    /// <c>BottomCorner → BottomCentre → InnerApex → SideShoulder</c>.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// If <paramref name="rect"/>.Size is non-positive on either axis.
    /// </exception>
    public static SysVec2[] ComputeNormalizedUvs(
        DeskTrianglePlaceholderLogic.Wedge wedge,
        DeskFloorRectLogic.FloorRect rect)
    {
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Floor rect size must be strictly positive ; got {rect.Size}.",
                nameof(rect));
        }

        var verts = DeskTrianglePlaceholderLogic.Vertices(wedge);
        var uvs = new SysVec2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            uvs[i] = NormalizeVertex(verts[i], rect);
        }
        return uvs;
    }

    /// <summary>
    /// Map one pixel-space vertex to its normalized UV against
    /// <paramref name="rect"/>. Single-vertex variant of
    /// <see cref="ComputeNormalizedUvs"/> ; exposed so the test suite can
    /// pin the affine formula on representative corners without going
    /// through a whole wedge.
    /// </summary>
    public static SysVec2 NormalizeVertex(
        SysVec2 vert, DeskFloorRectLogic.FloorRect rect)
    {
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Floor rect size must be strictly positive ; got {rect.Size}.",
                nameof(rect));
        }

        return new SysVec2(
            (vert.X - rect.TopLeft.X) / rect.Size.X,
            (vert.Y - rect.TopLeft.Y) / rect.Size.Y);
    }
}
