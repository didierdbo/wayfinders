using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry for the CPU draw-time desk-floor carve (Rune,
/// 2026-05-23 alpha-hole-aligned rebuild). The name keeps the historical
/// <c>...PlaceholderLogic</c> suffix for diff-traceability — it is no
/// longer a "placeholder" in the original sense ; it is the production
/// carve, calibrated against the shipped Mira back-decor alpha geometry.
///
/// <para>
/// <b>READ THIS — what this is, and what it is NOT.</b> The helper answers
/// one question : "is this point inside the desk-floor mask region ?" — and
/// returns the polygon vertices the engine seam paints to fill that region.
/// <see cref="IsoBoard._Draw"/> uses both to decide which fragments of the
/// desk wood and the iso grid to paint. It is a CPU draw-time framing — no
/// shader, no material, no UV uniforms. It is NOT the removed
/// <c>desk_triangle_clip.gdshader</c> returning.
/// </para>
///
/// <para>
/// <b>Why a CPU carve still exists, after Mira's decor landed (the
/// stacking-order verdict, 2026-05-22).</b> Mira's back decor (layer A) is
/// the BACK layer in the locked A → B → C → D draw order. A's alpha holes
/// therefore CANNOT mask the desk floor (layer C, drawn AFTER A). The desk
/// wood bitmap is authored as a full viewport-covering rectangle ; if it
/// rendered unclipped it would cover the whole screen and bury the layer-B
/// maquette. This CPU carve confines the live desk wood to the alpha-hole
/// region. A fills the furniture around the hole ; the carve confines the
/// desk wood INTO the hole. Complementary, not redundant.
/// </para>
///
/// <para>
/// <b>The alpha-hole geometry (measured on
/// <c>wf_e1_gamescreen_decor_back.png</c>, 1920×1080 RGBA).</b> The decor's
/// opaque wood forms a chevron filling the upper half of the screen ; its
/// bottom edge is an inverted-V with apex at <c>(960, 1060)</c> and
/// shoulders at <c>(0, 580)</c> and <c>(1919, 580)</c>. The entire region
/// below that V — down to the bottom edge of the screen — is transparent
/// (alpha = 0), all the way across. So the alpha hole is ONE continuous
/// V-shaped polygon, not two isolated corner triangles ; <see cref="Build"/>
/// splits it at the screen-centre vertical axis into a LEFT and a RIGHT
/// quadrilateral so the iso grid stripe pass stays trivially indexable by
/// the screen-centre symmetry. The two quads share the screen-centre edge
/// from <c>(960, 1060)</c> down to <c>(960, 1080)</c>, so they tile the
/// alpha hole with no seam.
/// </para>
///
/// <para>
/// <b>The V-edge overlap (Rune 2026-05-23 second wedge-rendering round —
/// the C↔D lip gap fix).</b> The wedges' V edge (InnerApex →
/// SideShoulder) used to land EXACTLY on decor A's V boundary, with the
/// idea that decor D's lip would meet it pixel-exact above. In practice a
/// thin transparent sliver appeared at the boundary : decor D's lip
/// alpha-tapers across a few sub-pixel rows, so even a pixel-perfect line
/// of wedges left ~1-2 px of layer-B maquette visible through the seam.
/// The fix is to <i>over-extend</i> the wedges UP past A's V by
/// <see cref="VEdgeOverlapPx"/> pixels (default 4). Decor D paints over
/// the overlap (D is drawn AFTER C in the locked A→B→C→D order), so the
/// overlap is invisible ; the gap disappears because there is no row of
/// pixels where neither C nor D paints. The overlap is safe : pulling
/// <c>InnerApex</c> and <c>SideShoulder</c> a few pixels up only adds a
/// few rows of wood under decor D's opaque lip ; the wood there is hidden
/// behind D, the screen centre well above A's V apex stays untouched.
/// </para>
///
/// <para>
/// <b>The vertex order — and why the carve goes a few pixels BELOW A's V
/// edge near the corners.</b> Each wedge is a 4-vertex convex polygon with
/// these vertices, in clockwise order on the LEFT wedge :
/// <list type="number">
///   <item><c>BottomCorner</c> — the screen bottom-corner pixel
///     (<c>(left, bottom)</c>). The desk-floor bitmap must cover the very
///     bottom row of pixels (decor A is transparent there).</item>
///   <item><c>BottomCentre</c> — the screen bottom-centre pixel
///     (<c>(centreX, bottom)</c>). The two wedges meet here, so the desk
///     wood is continuous along the bottom edge of the screen.</item>
///   <item><c>InnerApex</c> — the V apex of decor A pulled UP by
///     <see cref="VEdgeOverlapPx"/>
///     (<c>(centreX, bottom - InnerApexOffsetPx - VEdgeOverlapPx)</c>),
///     so the wedge over-extends past A's V apex and meets decor D's lip
///     under the lip's alpha taper rather than beside it.</item>
///   <item><c>SideShoulder</c> — the V shoulder of decor A on the left
///     side, similarly pulled UP by <see cref="VEdgeOverlapPx"/>.</item>
/// </list>
/// The hypotenuse <c>SideShoulder → InnerApex</c> has slope
/// <c>(SideShoulderOffsetPx - InnerApexOffsetPx) / halfWidth</c>. Even
/// with the overlap the slope is unchanged (both vertices shift by the
/// same <c>VEdgeOverlapPx</c>), so the V edge stays parallel to decor A's
/// V edge — the desk-wood polygon and A's opaque chevron still meet edge-
/// to-edge along the V, with no seam and no transparent sliver. The
/// remaining edges (<c>BottomCorner → SideShoulder</c> along the side and
/// <c>BottomCorner → BottomCentre</c> along the bottom) hug the screen
/// border where A is fully transparent — so the desk floor solidly covers
/// the alpha hole.
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
    /// One desk-floor wedge — a 4-vertex convex polygon in this board's
    /// local pixel space. Vertex ordering documented on the type fields.
    /// </summary>
    /// <param name="BottomCorner">
    /// The screen bottom-corner vertex (bottom-left on the left wedge,
    /// bottom-right on the right wedge).
    /// </param>
    /// <param name="BottomCentre">
    /// The screen bottom-centre vertex (shared by both wedges along the
    /// bottom edge).
    /// </param>
    /// <param name="InnerApex">
    /// Decor A's V apex pulled UP by <see cref="VEdgeOverlapPx"/> so the
    /// wedge over-extends under decor D's lip. Shared by both wedges
    /// along the screen-centre vertical edge.
    /// </param>
    /// <param name="SideShoulder">
    /// Decor A's V shoulder on the side edge of the screen, pulled UP by
    /// <see cref="VEdgeOverlapPx"/> for the same reason.
    /// </param>
    public readonly record struct Wedge(
        SysVec2 BottomCorner,
        SysVec2 BottomCentre,
        SysVec2 InnerApex,
        SysVec2 SideShoulder);

    /// <summary>
    /// Back-compat alias for the historical name. Tests and one or two
    /// outside callers still spell it <c>Triangle</c> ; the type is the
    /// same 4-vertex convex polygon — only the name differs. Marked
    /// obsolete to nudge callers to <see cref="Wedge"/>.
    /// </summary>
    [System.Obsolete("Use Wedge — this is a 4-vertex convex polygon, " +
                     "not a triangle.")]
    public readonly record struct Triangle(SysVec2 A, SysVec2 B, SysVec2 C);

    /// <summary>
    /// The two desk-floor wedges that tile decor A's alpha hole.
    /// </summary>
    /// <param name="Left">The left wedge.</param>
    /// <param name="Right">The right wedge (mirror about the screen-X centre).</param>
    public readonly record struct DeskTriangles(Wedge Left, Wedge Right);

    /// <summary>
    /// How far ABOVE the screen bottom decor A's V <b>apex</b> sits, in
    /// pixels at the 1080-tall reference resolution. The two wedges meet at
    /// this height on the screen-centre vertical axis (before
    /// <see cref="VEdgeOverlapPx"/> is added). Measured on
    /// <c>wf_e1_gamescreen_decor_back.png</c> : the last opaque row of A is
    /// y=1060, so the apex offset above the bottom (y=1080) is <c>20</c>
    /// pixels. Stored as a <i>fraction</i> of the viewport height so the
    /// carve scales correctly under a future resolution change.
    /// </summary>
    public const float InnerApexFractionFromBottom = 20f / 1080f;

    /// <summary>
    /// How far ABOVE the screen bottom decor A's V <b>shoulder</b> sits, in
    /// pixels at the 1080-tall reference resolution. The wedge's
    /// <c>SideShoulder</c> vertex lands on the side edge of the screen at
    /// this height (before <see cref="VEdgeOverlapPx"/> is added).
    /// Measured on <c>wf_e1_gamescreen_decor_back.png</c> : at x=0 the
    /// last opaque row is y=580, so the shoulder offset above the bottom
    /// is <c>500</c> pixels. Stored as a fraction of viewport height so
    /// the carve scales under a future resolution change.
    /// </summary>
    public const float SideShoulderFractionFromBottom = 500f / 1080f;

    /// <summary>
    /// <b>The V-edge overlap, in pixels.</b> The wedge's V edge
    /// (InnerApex → SideShoulder) is shifted UP by this many pixels past
    /// decor A's measured V boundary so the wedge over-extends UNDER
    /// decor D's lip. Decor D paints over the overlap (D is drawn after
    /// C in the locked A→B→C→D order), so the overlap is invisible — but
    /// it eliminates the thin transparent sliver that appeared between
    /// the wedges and decor D when the V edge landed pixel-exact on A's
    /// V (decor D's lip alpha-tapers across a few rows and did not cover
    /// the boundary completely). 4 px swallows the taper without
    /// noticeably encroaching on the screen centre (the centre stays
    /// well above the V apex). Tunable if Mira re-renders decor D with a
    /// different taper width.
    /// </summary>
    public const float VEdgeOverlapPx = 4f;

    /// <summary>
    /// <b>Deprecated name.</b> Historical placeholder constant — kept as a
    /// thin alias so a stale grep still finds the symbol. Reads the new
    /// alpha-aligned <see cref="SideShoulderFractionFromBottom"/>.
    /// </summary>
    [System.Obsolete("Use SideShoulderFractionFromBottom.")]
    public const float SideEdgeFraction = SideShoulderFractionFromBottom;

    /// <summary>
    /// <b>Deprecated name.</b> Historical placeholder constant — the
    /// alpha-aligned wedges meet at the screen centre by construction, so
    /// this is now always <c>0.5</c>. Kept as a thin alias so a stale grep
    /// still finds the symbol.
    /// </summary>
    [System.Obsolete("The wedges always meet at the screen centre now.")]
    public const float BottomEdgeFraction = 0.5f;

    /// <summary>
    /// Build the two desk-floor wedges for a viewport of the given size, in
    /// that viewport's local pixel space (origin top-left, +Y down).
    /// </summary>
    /// <param name="viewportSize">
    /// The desk viewport size in local pixels. Both components must be
    /// positive. The fractions <see cref="InnerApexFractionFromBottom"/>
    /// and <see cref="SideShoulderFractionFromBottom"/> are applied to
    /// <paramref name="viewportSize"/>.Y to recover decor A's alpha-hole
    /// geometry under the current camera zoom ; <see cref="VEdgeOverlapPx"/>
    /// is then subtracted from the V vertices' Y so the wedge over-extends
    /// under decor D's lip.
    /// </param>
    /// <param name="originX">
    /// The desk board's local-X of the viewport's left edge. The desk
    /// board's <c>_Draw</c> runs in board-local pixels ; the visible
    /// viewport rect's top-left in that space is
    /// <c>cameraCentre - viewportSize/2</c>, so the wedges must be built
    /// against that, not against board origin (0,0).
    /// </param>
    /// <param name="originY">
    /// The desk board's local-Y of the viewport's top edge — see
    /// <paramref name="originX"/>.
    /// </param>
    /// <returns>The left and right desk-floor wedges.</returns>
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
        float centreX = originX + viewportSize.X * 0.5f;

        float innerApexY =
            bottom - viewportSize.Y * InnerApexFractionFromBottom
                   - VEdgeOverlapPx;
        float sideShoulderY =
            bottom - viewportSize.Y * SideShoulderFractionFromBottom
                   - VEdgeOverlapPx;

        // Left wedge — clockwise : bottom-left corner -> bottom-centre ->
        // V apex -> left V shoulder. This tiles the LEFT half of decor A's
        // alpha hole exactly, with the V edge pulled up by VEdgeOverlapPx
        // so the wedge over-extends UNDER decor D's lip and no transparent
        // sliver remains between C and D.
        var leftWedge = new Wedge(
            BottomCorner: new SysVec2(left, bottom),
            BottomCentre: new SysVec2(centreX, bottom),
            InnerApex: new SysVec2(centreX, innerApexY),
            SideShoulder: new SysVec2(left, sideShoulderY));

        // Right wedge — mirror about centreX.
        var rightWedge = new Wedge(
            BottomCorner: new SysVec2(right, bottom),
            BottomCentre: new SysVec2(centreX, bottom),
            InnerApex: new SysVec2(centreX, innerApexY),
            SideShoulder: new SysVec2(right, sideShoulderY));

        return new DeskTriangles(leftWedge, rightWedge);
    }

    /// <summary>
    /// True when <paramref name="point"/> lies inside either desk wedge
    /// (inclusive of the edges). Standard convex-polygon test : the point
    /// is inside when it sits on the same side of every edge.
    /// </summary>
    public static bool Contains(DeskTriangles wedges, SysVec2 point)
        => PointInWedge(wedges.Left, point)
        || PointInWedge(wedges.Right, point);

    /// <summary>
    /// True when <paramref name="point"/> lies inside the convex polygon
    /// <paramref name="wedge"/> (edges inclusive). Standard sign-of-edge
    /// cross-product test, repeated for the four edges. Winding-agnostic :
    /// the point is inside iff all four signs match (or are zero — on an
    /// edge).
    /// </summary>
    public static bool PointInWedge(Wedge wedge, SysVec2 point)
    {
        float d1 = EdgeSign(point, wedge.BottomCorner, wedge.BottomCentre);
        float d2 = EdgeSign(point, wedge.BottomCentre, wedge.InnerApex);
        float d3 = EdgeSign(point, wedge.InnerApex, wedge.SideShoulder);
        float d4 = EdgeSign(point, wedge.SideShoulder, wedge.BottomCorner);

        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f || d4 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f || d4 > 0f;

        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// Back-compat alias for the historical name. Some xUnit tests still
    /// spell it <c>PointInTriangle</c> ; routes through the 3-vertex degenerate
    /// case of the convex polygon test so its semantics are preserved.
    /// </summary>
    [System.Obsolete("Use PointInWedge — the carve polygon is a quadrilateral.")]
    public static bool PointInTriangle(Triangle tri, SysVec2 point)
    {
        // Three edge cross-products ; same convex-polygon test, restricted
        // to three edges.
        float d1 = EdgeSign(point, tri.A, tri.B);
        float d2 = EdgeSign(point, tri.B, tri.C);
        float d3 = EdgeSign(point, tri.C, tri.A);

        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;

        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// The four vertices of one wedge, ready for an engine-side polygon
    /// fill, in clockwise order :
    /// <c>BottomCorner → BottomCentre → InnerApex → SideShoulder</c>.
    /// Exposed so the engine seam converts one short array rather than
    /// re-deriving the vertices.
    /// </summary>
    public static SysVec2[] Vertices(Wedge wedge)
        => new[] { wedge.BottomCorner, wedge.BottomCentre, wedge.InnerApex, wedge.SideShoulder };

    /// <summary>
    /// Back-compat alias for the historical 3-vertex name. Returns three
    /// vertices in the historical order ; new callers go through the
    /// 4-vertex <see cref="Vertices(Wedge)"/> overload above.
    /// </summary>
    [System.Obsolete("Use the Wedge overload — returns 4 vertices.")]
    public static SysVec2[] Vertices(Triangle tri)
        => new[] { tri.A, tri.B, tri.C };

    private static float EdgeSign(SysVec2 p, SysVec2 a, SysVec2 b)
        => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
}
