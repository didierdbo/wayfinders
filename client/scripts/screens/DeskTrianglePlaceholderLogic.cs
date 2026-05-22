using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry for the <b>temporary</b> twin-corner desk-floor framing
/// placeholder (Rune, 2026-05-22 — F6-readability fix, supersedes nothing,
/// scaffolds the gap).
///
/// <para>
/// <b>READ THIS — what this is, and what it is NOT.</b> This helper is a
/// PLACEHOLDER. It exists ONLY to keep the F6 smoke interpretable in the gap
/// between the removal of <c>desk_triangle_clip.gdshader</c> (the 4-layer
/// rework, 2026-05-22) and the arrival of Mira's Blender decor — the decor
/// whose two alpha holes will carry the desk-floor shape for real.
/// </para>
///
/// <para>
/// <b>It is NOT a return of the removed clip shader.</b> The removed scaffold
/// was a GPU UV clip: a <c>.gdshader</c> on the floor <c>TextureRect</c> fed
/// five UV uniforms by <c>DeskClipFrontierLogic</c>, carving a rectangular
/// SubViewport <i>render target</i>. This helper instead just answers one
/// question — "is this point inside one of the two bottom-corner triangles?"
/// — and the <c>IsoBoard._Draw</c> pass uses that answer to <i>skip drawing</i>
/// the floor aplat and the grid diamonds outside the triangles. It is a
/// CPU draw-time framing, no shader, no material, no UV uniforms, no render
/// target surgery. When Mira's decor lands, delete this file and the
/// <c>IsoBoard.DeskTrianglePlaceholderClip</c> flag that consumes it; nothing
/// else changes.
/// </para>
///
/// <para>
/// <b>The two triangles.</b> The desk reads as a cartographer's table seen
/// from above: the map maquette occupies the centre, the desk wood shows
/// only in the two BOTTOM corners of the screen — a bottom-left triangle and
/// a bottom-right triangle, mirror-symmetric about the screen centre X. Each
/// triangle's hypotenuse runs from a bottom corner up-and-inward to a point
/// on the bottom edge near the centre and a point on the side edge. The exact
/// painted decor will refine these edges; the placeholder just needs the two
/// wedges to read as "the desk is down here in the corners".
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Everything is
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> — so
/// this helper is cherry-pickable into the xUnit host with no GodotSharp.
/// <c>IsoBoard</c> converts at the engine seam. Same logic-vs-node split as
/// <see cref="DeskFloorRectLogic"/>.
/// </para>
/// </summary>
public static class DeskTrianglePlaceholderLogic
{
    /// <summary>
    /// One bottom-corner triangle, three vertices in this board's local
    /// pixel space. Returned as a value type so the helper stays a pure
    /// function.
    /// </summary>
    /// <param name="A">First vertex — the screen bottom corner.</param>
    /// <param name="B">Second vertex — on the screen bottom edge.</param>
    /// <param name="C">Third vertex — on the screen side edge.</param>
    public readonly record struct Triangle(SysVec2 A, SysVec2 B, SysVec2 C);

    /// <summary>
    /// The two bottom-corner desk triangles for the placeholder framing.
    /// </summary>
    /// <param name="Left">The bottom-left wedge.</param>
    /// <param name="Right">The bottom-right wedge.</param>
    public readonly record struct DeskTriangles(Triangle Left, Triangle Right);

    /// <summary>
    /// How far along the screen bottom edge, from each bottom corner toward
    /// the centre, the triangle's inner foot reaches — as a fraction of the
    /// screen width. 0.38 leaves a small unpainted gap straddling the centre
    /// (the maquette diamond shows through there) and gives each wedge a
    /// believable table-corner footprint.
    /// </summary>
    public const float BottomEdgeFraction = 0.38f;

    /// <summary>
    /// How far up the screen side edge, from each bottom corner, the
    /// triangle's outer top vertex reaches — as a fraction of the screen
    /// height. 0.46 keeps the wedge below the screen mid-line so it never
    /// climbs into the maquette zone.
    /// </summary>
    public const float SideEdgeFraction = 0.46f;

    /// <summary>
    /// Build the two bottom-corner desk triangles for a viewport of the
    /// given size, in that viewport's local pixel space (origin top-left,
    /// +Y down).
    /// </summary>
    /// <param name="viewportSize">
    /// The desk viewport size in pixels. Both components must be positive.
    /// </param>
    /// <param name="originX">
    /// The desk board's local-X of the viewport's left edge. The desk
    /// board's <c>_Draw</c> runs in board-local pixels; the visible viewport
    /// rect's top-left in that space is
    /// <c>cameraCentre - viewportSize/2</c>, so the triangles must be built
    /// against that, not against board origin (0,0).
    /// </param>
    /// <param name="originY">
    /// The desk board's local-Y of the viewport's top edge — see
    /// <paramref name="originX"/>.
    /// </param>
    /// <returns>The bottom-left and bottom-right desk wedge triangles.</returns>
    /// <exception cref="System.ArgumentException">
    /// If either viewport dimension is non-positive — a caller bug, not a
    /// silent default.
    /// </exception>
    public static DeskTriangles Build(
        SysVec2 viewportSize, float originX, float originY)
    {
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Desk viewport size must be positive; got {viewportSize}.",
                nameof(viewportSize));
        }

        float left = originX;
        float right = originX + viewportSize.X;
        float bottom = originY + viewportSize.Y;

        float bottomReach = viewportSize.X * BottomEdgeFraction;
        float sideReach = viewportSize.Y * SideEdgeFraction;

        // Bottom-left wedge: the bottom-left corner, a point along the bottom
        // edge toward centre, and a point up the left edge.
        var leftTri = new Triangle(
            A: new SysVec2(left, bottom),
            B: new SysVec2(left + bottomReach, bottom),
            C: new SysVec2(left, bottom - sideReach));

        // Bottom-right wedge: the mirror about the viewport centre X.
        var rightTri = new Triangle(
            A: new SysVec2(right, bottom),
            B: new SysVec2(right - bottomReach, bottom),
            C: new SysVec2(right, bottom - sideReach));

        return new DeskTriangles(leftTri, rightTri);
    }

    /// <summary>
    /// True when <paramref name="point"/> lies inside either desk triangle
    /// (inclusive of the edges). Uses the standard barycentric / sign-of-area
    /// test — robust to either winding.
    /// </summary>
    public static bool Contains(DeskTriangles triangles, SysVec2 point)
        => PointInTriangle(triangles.Left, point)
        || PointInTriangle(triangles.Right, point);

    /// <summary>
    /// True when <paramref name="point"/> lies inside the single triangle
    /// <paramref name="tri"/> (edges inclusive).
    /// </summary>
    public static bool PointInTriangle(Triangle tri, SysVec2 point)
    {
        // Three edge cross-products; the point is inside when all three have
        // the same sign (or are zero — on an edge). Winding-agnostic.
        float d1 = EdgeSign(point, tri.A, tri.B);
        float d2 = EdgeSign(point, tri.B, tri.C);
        float d3 = EdgeSign(point, tri.C, tri.A);

        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;

        // Inside (or on an edge) iff the signs are not mixed.
        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// The four vertices of one triangle, ready for an engine-side polygon
    /// fill. Exposed so the engine seam converts one short array rather than
    /// re-deriving the vertices.
    /// </summary>
    public static SysVec2[] Vertices(Triangle tri)
        => new[] { tri.A, tri.B, tri.C };

    private static float EdgeSign(SysVec2 p, SysVec2 a, SysVec2 b)
        => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
}
