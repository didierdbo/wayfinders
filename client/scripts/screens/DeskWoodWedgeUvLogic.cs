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
/// <b>The first bug this solved (2026-05-23 wedge-rendering round, first
/// pass).</b> The previous engine-seam implementation in
/// <c>IsoBoard.DrawDeskWoodWedge</c> set the UV of vertex <c>v</c> to
/// <c>v - rect.TopLeft</c> — in <i>pixel</i> coordinates — and passed
/// that to <c>DrawColoredPolygon(points, color, uvs, texture)</c>. Godot 4
/// expects UVs in the normalized <c>[0,1]</c> domain ; with values of
/// magnitude ~10² the texture sampler ran far outside that domain and
/// clamped to the edge texel. The whole wedge then read as a flat colour.
/// <see cref="ComputeNormalizedUvs"/> closed that bug with a screen-axis
/// affine map <c>uv = (vert - rect.TopLeft) / rect.Size</c>.
/// </para>
///
/// <para>
/// <b>The follow-up bug (same day, second pass — the "wood reads as a
/// screen-vertical quadrilage" report).</b> The screen-axis affine map
/// paints Mira's wood-grain bitmap as a flat top-down sample : the planks
/// align vertically on screen, while the rest of the scene runs on an iso
/// 2:1 basis. Visually the wedges read as a flat top-down floor pasted
/// behind a 3/4-view stage. The fix is to project each wedge vertex into
/// the iso (col,row) basis BEFORE normalizing — the wood texture is then
/// sampled in iso-space, the grain follows the iso axes, and the wood
/// shears onto the diagonals of the iso grid (slope ±0.5 on screen)
/// instead of staying screen-axis-aligned. The new entry point is
/// <see cref="ComputeIsoNormalizedUvs"/>. The legacy
/// <see cref="ComputeNormalizedUvs"/> stays so its pinning tests keep
/// their contract (screen-axis affine is still a legitimate fallback for
/// a flat top-down board).
/// </para>
///
/// <para>
/// <b>The iso basis we project through.</b> Standard locked 2:1 dimetric
/// (<see cref="Services.Iso.IsoProjection"/>) : the screen-pixel offset
/// <c>(px, py)</c> from a reference point maps to iso (col,row) coords
/// <c>col = px/halfW + py/halfH</c> divided by 2 ; <c>row = py/halfH -
/// px/halfW</c> divided by 2 (the inverse of <c>CellToWorld</c>). The
/// helper does NOT round — UVs need the continuous result. The four
/// corners of the floor rect project to a tilted (rotated 45° + scaled)
/// quad in iso space ; we take the axis-aligned bounding box of that
/// quad and divide by it to get each vertex's normalized UV. The wood
/// bitmap is then sampled with its X axis along the iso col-axis and its
/// Y axis along the iso row-axis — exactly what "wood that follows the
/// iso grid" means.
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
    /// vertices using a screen-axis affine map. Kept for the no-iso
    /// fallback case (a flat top-down board) and to preserve the
    /// pinned 2026-05-23 first-pass tests verbatim. <b>The desk uses
    /// <see cref="ComputeIsoNormalizedUvs"/> instead</b> ; see the
    /// type-level summary.
    ///
    /// <para>
    /// The wood bitmap is authored at the floor rect's size and registered
    /// onto its top-left, so the affine map is just
    /// <c>uv = (vert - rect.TopLeft) / rect.Size</c>. A vertex sitting at
    /// the floor rect's top-left gets <c>(0,0)</c> ; a vertex at the
    /// bottom-right gets <c>(1,1)</c> ; values outside <c>[0,1]</c> are
    /// returned unclamped so a caller-side assertion or a deliberate
    /// out-of-bounds wedge stays observable.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Compute the iso-basis normalized [0,1] texture UVs for the four
    /// wedge vertices — the production path on the desk board so the wood
    /// grain visually follows the iso grid axes instead of the screen
    /// axes.
    ///
    /// <para>
    /// <b>The transform.</b> For each wedge vertex <c>v</c> in board-local
    /// pixels :
    /// <list type="number">
    ///   <item>compute the screen-pixel offset from the floor rect's
    ///     top-left : <c>p = v - rect.TopLeft</c>.</item>
    ///   <item>project that offset into iso (col,row) space using the same
    ///     2:1 dimetric basis the rest of the engine uses :
    ///     <c>iso = (p.X/halfW + p.Y/halfH, p.Y/halfH - p.X/halfW) / 2</c>
    ///     where <c>halfW = tileWidthPx / 2</c>,
    ///     <c>halfH = tileWidthPx / 4</c> (locked 2:1 ratio).</item>
    ///   <item>normalize each vertex's iso coord by the iso-bounding-box
    ///     of the floor rect's four corners — so the four screen corners
    ///     map to a tilted iso quad and the helper produces UVs in
    ///     <c>[0,1]</c> for any vertex inside that quad.</item>
    /// </list>
    /// The wood bitmap is then sampled in iso space : its X axis runs
    /// along the iso col-axis (a board-local diagonal of slope +0.5 on
    /// screen) and its Y axis runs along the iso row-axis (slope −0.5 on
    /// screen). The grain shears onto the iso grid.
    /// </para>
    ///
    /// <para>
    /// <b>Why an explicit <paramref name="tileWidthPx"/>.</b> The helper
    /// stays Godot-free and projection-free — it does not depend on a
    /// constructed <c>IsoProjection</c> instance, only on the locked
    /// 2:1 aspect ratio and the desk board's tile width. The desk's
    /// <c>TileWidthPx</c> is the same one passed to
    /// <c>IsoProjection.Iso2To1</c>, so col/row in this helper line up
    /// with cells one-to-one. A board-pixel vector of length one cell
    /// along the iso col-axis maps to UV delta (1/colSpan, 0).
    /// </para>
    ///
    /// <para>
    /// Continuity across the V : the two wedges share BottomCentre and
    /// InnerApex ; the iso transform is a pure affine function of the
    /// vertex, so both wedges produce identical UVs at the shared points
    /// and the wood texture is seamless across the V edge (no visible seam
    /// at screen centre).
    /// </para>
    /// </summary>
    /// <param name="wedge">
    /// The four-vertex convex polygon to texture, in board-local pixels.
    /// </param>
    /// <param name="rect">
    /// The floor rect that defines the iso-bounding-box reference. The
    /// helper projects this rect's four corners into iso space and uses
    /// the resulting bounding box to normalize each wedge vertex.
    /// </param>
    /// <param name="tileWidthPx">
    /// The desk board's iso tile width in pixels — the same value passed
    /// to <see cref="Services.Iso.IsoProjection.Iso2To1"/>. Must be
    /// strictly positive (caller bug otherwise).
    /// </param>
    /// <returns>
    /// Four normalized UVs in the same clockwise order as
    /// <see cref="DeskTrianglePlaceholderLogic.Vertices"/> :
    /// <c>BottomCorner → BottomCentre → InnerApex → SideShoulder</c>.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// If <paramref name="rect"/>.Size is non-positive on either axis,
    /// or <paramref name="tileWidthPx"/> is non-positive.
    /// </exception>
    public static SysVec2[] ComputeIsoNormalizedUvs(
        DeskTrianglePlaceholderLogic.Wedge wedge,
        DeskFloorRectLogic.FloorRect rect,
        float tileWidthPx)
    {
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
        {
            throw new System.ArgumentException(
                $"Floor rect size must be strictly positive ; got {rect.Size}.",
                nameof(rect));
        }
        if (tileWidthPx <= 0f)
        {
            throw new System.ArgumentException(
                $"Tile width must be strictly positive ; got {tileWidthPx}.",
                nameof(tileWidthPx));
        }

        // Project the floor rect's four corners into iso (col,row) space
        // to find the iso-bounding-box we normalize against. The rect is
        // axis-aligned in screen space ; in iso space it becomes a tilted
        // rhombus, so a screen-axis bounding box is NOT the right
        // normalizer — we need the iso-axis bounding box.
        var c0 = ProjectToIso(rect.TopLeft, rect.TopLeft, tileWidthPx);
        var c1 = ProjectToIso(
            rect.TopLeft + new SysVec2(rect.Size.X, 0f),
            rect.TopLeft, tileWidthPx);
        var c2 = ProjectToIso(
            rect.TopLeft + rect.Size,
            rect.TopLeft, tileWidthPx);
        var c3 = ProjectToIso(
            rect.TopLeft + new SysVec2(0f, rect.Size.Y),
            rect.TopLeft, tileWidthPx);

        float isoMinX = System.MathF.Min(
            System.MathF.Min(c0.X, c1.X), System.MathF.Min(c2.X, c3.X));
        float isoMaxX = System.MathF.Max(
            System.MathF.Max(c0.X, c1.X), System.MathF.Max(c2.X, c3.X));
        float isoMinY = System.MathF.Min(
            System.MathF.Min(c0.Y, c1.Y), System.MathF.Min(c2.Y, c3.Y));
        float isoMaxY = System.MathF.Max(
            System.MathF.Max(c0.Y, c1.Y), System.MathF.Max(c2.Y, c3.Y));

        float isoSpanX = isoMaxX - isoMinX;
        float isoSpanY = isoMaxY - isoMinY;
        // Defence in depth : the rect was already validated non-degenerate,
        // and the iso projection of a non-degenerate rect is itself non-
        // degenerate, so isoSpan is always strictly positive. Guard anyway.
        if (isoSpanX <= 0f || isoSpanY <= 0f)
        {
            throw new System.InvalidOperationException(
                $"Iso projection collapsed the floor rect ; isoSpan=" +
                $"({isoSpanX}, {isoSpanY}). This should be unreachable.");
        }

        var verts = DeskTrianglePlaceholderLogic.Vertices(wedge);
        var uvs = new SysVec2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var iso = ProjectToIso(verts[i], rect.TopLeft, tileWidthPx);
            uvs[i] = new SysVec2(
                (iso.X - isoMinX) / isoSpanX,
                (iso.Y - isoMinY) / isoSpanY);
        }
        return uvs;
    }

    /// <summary>
    /// Project a board-pixel vertex into iso (col,row)-space relative to
    /// <paramref name="origin"/>, using the locked 2:1 dimetric basis.
    /// Exposed so the test suite can pin the basis on representative
    /// vectors. The result is unitless (cell counts), not pixels.
    /// </summary>
    public static SysVec2 ProjectToIso(
        SysVec2 vert, SysVec2 origin, float tileWidthPx)
    {
        if (tileWidthPx <= 0f)
        {
            throw new System.ArgumentException(
                $"Tile width must be strictly positive ; got {tileWidthPx}.",
                nameof(tileWidthPx));
        }

        float halfW = tileWidthPx * 0.5f;
        float halfH = tileWidthPx * 0.25f; // 2:1 ratio : height = width / 2.

        float px = vert.X - origin.X;
        float py = vert.Y - origin.Y;
        // Inverse of the CellToWorld basis :
        //   px = (col - row) * halfW
        //   py = (col + row) * halfH
        // => col = (px/halfW + py/halfH) / 2
        //    row = (py/halfH - px/halfW) / 2
        float a = px / halfW;
        float b = py / halfH;
        return new SysVec2((a + b) * 0.5f, (b - a) * 0.5f);
    }
}
