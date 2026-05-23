using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskWoodWedgeUvLogic"/> — the affine
/// pixel→normalized-UV mapping the engine seam (<c>IsoBoard.DrawDeskWoodWedge</c>)
/// hands to <c>CanvasItem.DrawColoredPolygon</c>.
///
/// <para>
/// <b>What this suite owns.</b> The single load-bearing contract :
/// <c>DrawColoredPolygon</c> samples the texture in normalized <c>[0,1]</c>
/// UVs, so the helper must return UVs in that space — NOT in pixels. The
/// previous engine-seam implementation passed pixel-space UVs, the sampler
/// clamped to the edge texel, and the whole wedge read as a flat colour
/// ("aplat marron" + transparency-bleed F6 bugs). Pinning the contract
/// here closes both failure modes at the seam.
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

    private static DeskFloorRectLogic.FloorRect SampleFloorRect()
    {
        // The desk camera centres on the viewport's middle.
        var cameraCentre = new SysVec2(
            OriginX + ViewportSize.X * 0.5f,
            OriginY + ViewportSize.Y * 0.5f);
        return DeskFloorRectLogic.Compute(cameraCentre, ViewportSize);
    }

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
        // The whole point : Godot's DrawColoredPolygon samples textures in
        // [0,1] ; a UV of magnitude ~10² (the old pixel-space bug) clamps
        // to the edge texel and reads as a flat colour. This test pins the
        // contract that the helper returns UVs strictly inside [0,1] for
        // the realistic desk geometry.
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
}
