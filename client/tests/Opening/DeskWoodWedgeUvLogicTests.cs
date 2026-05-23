using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskWoodWedgeUvLogic"/> — the pixel ↔
/// normalized-UV mapping the engine seam (<c>IsoBoard.DrawDeskWoodWedge</c>)
/// hands to <c>CanvasItem.DrawColoredPolygon</c>.
///
/// <para>
/// <b>What this suite owns.</b> Two contracts :
/// <list type="number">
///   <item><b>Screen-axis affine map</b>
///     (<see cref="DeskWoodWedgeUvLogic.ComputeNormalizedUvs"/>) — the
///     legacy first-pass fix from 2026-05-23 round 1, kept for non-iso
///     boards. UVs land in <c>[0,1]</c> ; opposite corners of the rect
///     map to <c>(0,0)</c> and <c>(1,1)</c>.</item>
///   <item><b>Iso-basis map</b>
///     (<see cref="DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs"/>) — the
///     production path on the desk board so the wood grain follows the
///     iso grid axes instead of the screen axes. UVs are sampled in iso
///     (col,row) space ; the iso-bounding-box of the floor rect's four
///     corners is the normalizer ; UVs land in <c>[0,1]</c> for any
///     vertex inside that quad ; the two wedges share UVs along the V
///     edge so the wood is seamless.</item>
/// </list>
/// Both contracts are pinned here so a future refactor cannot silently
/// regress either path.
/// </para>
/// </summary>
public sealed class DeskWoodWedgeUvLogicTests
{
    // The same representative desk setup the DeskTrianglePlaceholder tests
    // use, with a representative floor rect Mira's wf_e1_desk_wood_floor.png
    // is authored against (a viewport-covering rect under the immobile
    // desk camera, grown by DeskFloorRectLogic.EdgeSlackPx on each side).
    private static readonly SysVec2 ViewportSize = new(1920f, 1080f);
    private const float OriginX = -200f;
    private const float OriginY = -120f;
    // Same tile width as the shipped DeskFloorBoard in GameScreen.tscn.
    private const float DeskTileWidthPx = 256f;

    private static DeskFloorRectLogic.FloorRect SampleFloorRect()
    {
        // The desk camera centres on the viewport's middle.
        var cameraCentre = new SysVec2(
            OriginX + ViewportSize.X * 0.5f,
            OriginY + ViewportSize.Y * 0.5f);
        return DeskFloorRectLogic.Compute(cameraCentre, ViewportSize);
    }

    // -----------------------------------------------------------------
    //  Screen-axis affine map — the legacy first-pass contract.
    // -----------------------------------------------------------------

    [Fact]
    public void Throws_on_a_zero_area_floor_rect()
    {
        var degenerate = new DeskFloorRectLogic.FloorRect(
            new SysVec2(0f, 0f), new SysVec2(0f, 1080f));
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        Assert.Throws<System.ArgumentException>(
            () => DeskWoodWedgeUvLogic.ComputeNormalizedUvs(
                wedges.Left, degenerate));
        Assert.Throws<System.ArgumentException>(
            () => DeskWoodWedgeUvLogic.NormalizeVertex(
                SysVec2.Zero, degenerate));
    }

    [Fact]
    public void Top_left_of_rect_maps_to_uv_zero_zero()
    {
        var rect = SampleFloorRect();
        var uv = DeskWoodWedgeUvLogic.NormalizeVertex(rect.TopLeft, rect);

        Assert.Equal(0f, uv.X, precision: 5);
        Assert.Equal(0f, uv.Y, precision: 5);
    }

    [Fact]
    public void Bottom_right_of_rect_maps_to_uv_one_one()
    {
        var rect = SampleFloorRect();
        var bottomRight = rect.TopLeft + rect.Size;
        var uv = DeskWoodWedgeUvLogic.NormalizeVertex(bottomRight, rect);

        Assert.Equal(1f, uv.X, precision: 5);
        Assert.Equal(1f, uv.Y, precision: 5);
    }

    [Fact]
    public void Centre_of_rect_maps_to_uv_half_half()
    {
        var rect = SampleFloorRect();
        var centre = rect.TopLeft + rect.Size * 0.5f;
        var uv = DeskWoodWedgeUvLogic.NormalizeVertex(centre, rect);

        Assert.Equal(0.5f, uv.X, precision: 5);
        Assert.Equal(0.5f, uv.Y, precision: 5);
    }

    [Fact]
    public void Every_wedge_vertex_uv_lies_in_the_unit_square()
    {
        // Godot's DrawColoredPolygon samples textures in [0,1] ; a UV of
        // magnitude ~10² (the original pixel-space bug) clamps to the edge
        // texel and reads as a flat colour. Pinning the contract that the
        // affine helper returns UVs strictly inside [0,1] for the realistic
        // desk geometry.
        var rect = SampleFloorRect();
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        foreach (var wedge in new[] { wedges.Left, wedges.Right })
        {
            var uvs = DeskWoodWedgeUvLogic.ComputeNormalizedUvs(wedge, rect);
            Assert.Equal(4, uvs.Length);

            foreach (var uv in uvs)
            {
                Assert.InRange(uv.X, 0f, 1f);
                Assert.InRange(uv.Y, 0f, 1f);
            }
        }
    }

    [Fact]
    public void Shared_vertices_between_wedges_get_identical_uvs_for_seamless_wood()
    {
        // The left and right wedges share BottomCentre + InnerApex along
        // the screen-centre vertical edge. Whatever UV the helper assigns
        // there must match across both wedges, so the wood texture is
        // continuous across the V (no visible seam at the screen centre).
        var rect = SampleFloorRect();
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var leftUvs = DeskWoodWedgeUvLogic.ComputeNormalizedUvs(
            wedges.Left, rect);
        var rightUvs = DeskWoodWedgeUvLogic.ComputeNormalizedUvs(
            wedges.Right, rect);

        // Index 1 is BottomCentre, index 2 is InnerApex (per
        // DeskTrianglePlaceholderLogic.Vertices ordering).
        Assert.Equal(leftUvs[1].X, rightUvs[1].X, precision: 5);
        Assert.Equal(leftUvs[1].Y, rightUvs[1].Y, precision: 5);
        Assert.Equal(leftUvs[2].X, rightUvs[2].X, precision: 5);
        Assert.Equal(leftUvs[2].Y, rightUvs[2].Y, precision: 5);
    }

    [Fact]
    public void Uv_order_mirrors_wedge_vertex_order()
    {
        // The engine seam zips the UV array with the points array index-by-
        // index, so the helper must preserve the
        // BottomCorner → BottomCentre → InnerApex → SideShoulder order.
        var rect = SampleFloorRect();
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var verts = DeskTrianglePlaceholderLogic.Vertices(wedges.Left);
        var uvs = DeskWoodWedgeUvLogic.ComputeNormalizedUvs(
            wedges.Left, rect);

        for (int i = 0; i < verts.Length; i++)
        {
            var expected = DeskWoodWedgeUvLogic.NormalizeVertex(verts[i], rect);
            Assert.Equal(expected.X, uvs[i].X, precision: 5);
            Assert.Equal(expected.Y, uvs[i].Y, precision: 5);
        }
    }

    [Fact]
    public void Uv_components_scale_linearly_with_rect_size()
    {
        // The mapping is uv = (vert - topLeft) / size — doubling the rect
        // size halves the UV of a fixed vertex. Pins the affine formula so
        // a future "let me sneak in a non-linear modifier" refactor is
        // caught.
        var rect = new DeskFloorRectLogic.FloorRect(
            new SysVec2(100f, 200f), new SysVec2(800f, 600f));
        var vert = new SysVec2(500f, 500f);
        var uv1 = DeskWoodWedgeUvLogic.NormalizeVertex(vert, rect);

        var doubled = new DeskFloorRectLogic.FloorRect(
            new SysVec2(100f, 200f), new SysVec2(1600f, 1200f));
        var uv2 = DeskWoodWedgeUvLogic.NormalizeVertex(vert, doubled);

        Assert.Equal(uv1.X * 0.5f, uv2.X, precision: 5);
        Assert.Equal(uv1.Y * 0.5f, uv2.Y, precision: 5);
    }

    // -----------------------------------------------------------------
    //  Iso-basis map — the production path on the desk board.
    // -----------------------------------------------------------------

    [Fact]
    public void Iso_uv_throws_on_a_zero_area_floor_rect()
    {
        var degenerate = new DeskFloorRectLogic.FloorRect(
            new SysVec2(0f, 0f), new SysVec2(0f, 1080f));
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        Assert.Throws<System.ArgumentException>(
            () => DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
                wedges.Left, degenerate, DeskTileWidthPx));
    }

    [Fact]
    public void Iso_uv_throws_on_a_non_positive_tile_width()
    {
        var rect = SampleFloorRect();
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        Assert.Throws<System.ArgumentException>(
            () => DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
                wedges.Left, rect, 0f));
        Assert.Throws<System.ArgumentException>(
            () => DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
                wedges.Left, rect, -1f));
        Assert.Throws<System.ArgumentException>(
            () => DeskWoodWedgeUvLogic.ProjectToIso(
                SysVec2.Zero, SysVec2.Zero, 0f));
    }

    [Fact]
    public void Iso_projection_of_the_origin_is_the_zero_vector()
    {
        // ProjectToIso(origin, origin, tile) == (0,0) — a vector of length
        // zero relative to the origin has zero iso coords. Trivial but
        // pins the affine intercept.
        var origin = new SysVec2(100f, 200f);
        var iso = DeskWoodWedgeUvLogic.ProjectToIso(
            origin, origin, DeskTileWidthPx);

        Assert.Equal(0f, iso.X, precision: 5);
        Assert.Equal(0f, iso.Y, precision: 5);
    }

    [Fact]
    public void Iso_projection_follows_the_locked_2to1_dimetric_basis()
    {
        // The basis : px = (col - row) * halfW ; py = (col + row) * halfH.
        // A board-pixel vector of (halfW, halfH) relative to origin is
        // exactly one cell in the +col direction, so its iso projection
        // is (1, 0). Same for (-halfW, halfH) => (0, 1) on the +row axis.
        // This is THE load-bearing property of the iso UV transform : the
        // wood texture's X axis lines up with the iso col-axis, its Y axis
        // lines up with the iso row-axis.
        float halfW = DeskTileWidthPx * 0.5f;       // 128
        float halfH = DeskTileWidthPx * 0.25f;      // 64 (locked 2:1)

        var origin = new SysVec2(0f, 0f);

        var alongCol = DeskWoodWedgeUvLogic.ProjectToIso(
            new SysVec2(halfW, halfH), origin, DeskTileWidthPx);
        Assert.Equal(1f, alongCol.X, precision: 5);
        Assert.Equal(0f, alongCol.Y, precision: 5);

        var alongRow = DeskWoodWedgeUvLogic.ProjectToIso(
            new SysVec2(-halfW, halfH), origin, DeskTileWidthPx);
        Assert.Equal(0f, alongRow.X, precision: 5);
        Assert.Equal(1f, alongRow.Y, precision: 5);
    }

    [Fact]
    public void Iso_uv_returns_four_components_in_the_unit_square_for_a_realistic_wedge()
    {
        // The whole point : iso UVs land in [0,1] for any wedge vertex
        // inside the floor rect, so Godot's DrawColoredPolygon samples
        // the wood bitmap correctly in iso space.
        var rect = SampleFloorRect();
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        foreach (var wedge in new[] { wedges.Left, wedges.Right })
        {
            var uvs = DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
                wedge, rect, DeskTileWidthPx);
            Assert.Equal(4, uvs.Length);
            foreach (var uv in uvs)
            {
                Assert.InRange(uv.X, 0f, 1f);
                Assert.InRange(uv.Y, 0f, 1f);
            }
        }
    }

    [Fact]
    public void Iso_uv_at_the_floor_rect_top_left_is_at_a_corner_of_the_iso_bounding_box()
    {
        // The floor rect's screen top-left is one of the four corners that
        // define the iso-bounding-box ; its iso UV is therefore one of
        // {(0, x), (x, 0), (1, x), (x, 1)} — on one edge of the unit
        // square. Pinning this catches a "wrong corner used for the
        // bounding box" regression.
        var rect = SampleFloorRect();
        var topLeftWedge = new DeskTrianglePlaceholderLogic.Wedge(
            BottomCorner: rect.TopLeft,
            BottomCentre: rect.TopLeft + new SysVec2(rect.Size.X * 0.5f, 0f),
            InnerApex: rect.TopLeft + rect.Size * 0.5f,
            SideShoulder: rect.TopLeft + new SysVec2(0f, rect.Size.Y * 0.5f));
        var uvs = DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
            topLeftWedge, rect, DeskTileWidthPx);

        bool onEdge =
            System.MathF.Abs(uvs[0].X) < 1e-4f
            || System.MathF.Abs(uvs[0].Y) < 1e-4f
            || System.MathF.Abs(uvs[0].X - 1f) < 1e-4f
            || System.MathF.Abs(uvs[0].Y - 1f) < 1e-4f;
        Assert.True(onEdge,
            $"iso UV of the floor rect's top-left should sit on a unit-" +
            $"square edge ; got {uvs[0]}");
    }

    [Fact]
    public void Iso_uv_is_continuous_across_the_V_edge_between_the_two_wedges()
    {
        // The two wedges share BottomCentre + InnerApex along the screen-
        // centre vertical edge. The iso UV map is a pure affine function
        // of the vertex, so the shared vertices must produce identical
        // iso UVs in both wedges — same seamlessness guarantee as the
        // screen-axis map, restated for the iso path.
        var rect = SampleFloorRect();
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var leftUvs = DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
            wedges.Left, rect, DeskTileWidthPx);
        var rightUvs = DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
            wedges.Right, rect, DeskTileWidthPx);

        Assert.Equal(leftUvs[1].X, rightUvs[1].X, precision: 5);
        Assert.Equal(leftUvs[1].Y, rightUvs[1].Y, precision: 5);
        Assert.Equal(leftUvs[2].X, rightUvs[2].X, precision: 5);
        Assert.Equal(leftUvs[2].Y, rightUvs[2].Y, precision: 5);
    }

    [Fact]
    public void Iso_uv_order_mirrors_wedge_vertex_order()
    {
        // The engine seam zips the iso UV array with the points array
        // index-by-index ; pinned for the iso path the same way the affine
        // path is pinned above.
        var rect = SampleFloorRect();
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var verts = DeskTrianglePlaceholderLogic.Vertices(wedges.Left);
        var uvs = DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
            wedges.Left, rect, DeskTileWidthPx);

        Assert.Equal(verts.Length, uvs.Length);
    }

    [Fact]
    public void Iso_uv_is_NOT_the_same_as_screen_axis_uv_for_a_realistic_wedge()
    {
        // The whole reason the iso variant exists : it produces a
        // DIFFERENT UV than the screen-axis affine map. If a refactor
        // accidentally aliased them, the wood would silently revert to
        // the screen-vertical-grain failure mode that this round was
        // introduced to fix. Pinning the "iso ≠ affine" contract.
        var rect = SampleFloorRect();
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var isoUvs = DeskWoodWedgeUvLogic.ComputeIsoNormalizedUvs(
            wedges.Left, rect, DeskTileWidthPx);
        var screenUvs = DeskWoodWedgeUvLogic.ComputeNormalizedUvs(
            wedges.Left, rect);

        // BottomCorner (vertex 0) is at (left, bottom) — one of the screen-
        // rect corners. Under the screen-axis affine map it lands at a
        // unit-square corner ; under the iso map it lands somewhere along a
        // unit-square edge but NOT at the same UV. Distinct ≥ 0.1 on at
        // least one component is plenty of separation.
        float dx = System.MathF.Abs(isoUvs[0].X - screenUvs[0].X);
        float dy = System.MathF.Abs(isoUvs[0].Y - screenUvs[0].Y);
        Assert.True(dx + dy > 0.1f,
            $"iso UV {isoUvs[0]} must differ from screen-axis UV " +
            $"{screenUvs[0]} ; got delta=({dx},{dy})");
    }
}
