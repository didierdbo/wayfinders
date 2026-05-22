using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskTrianglePlaceholderLogic"/> — the
/// <b>temporary</b> twin-corner desk-floor framing placeholder (Rune,
/// 2026-05-22 F6-readability fix).
///
/// <para>
/// <b>What this suite owns.</b> The two bottom-corner triangles are built
/// mirror-symmetric about the viewport centre X, they stay in the bottom
/// half of the screen (never climb into the maquette zone), the screen
/// centre is left UNpainted (so the maquette shows through), and the
/// point-in-triangle test is winding-agnostic. The Godot-bound wiring —
/// feeding the triangles into <c>IsoBoard._Draw</c> so the floor aplat +
/// grid are skipped outside them — is validated via the F6 smoke.
/// </para>
///
/// <para>
/// This whole helper is a placeholder; when Mira's Blender decor lands it is
/// deleted with its <c>IsoBoard</c> flag. The suite is here so the gap-state
/// F6 cannot silently regress to the full-rectangle desk floor that hid the
/// maquette.
/// </para>
/// </summary>
public sealed class DeskTrianglePlaceholderLogicTests
{
    // A representative desk setup: the full 1920x1080 screen viewport, the
    // desk board origin parked so the visible rect's top-left is at some
    // negative board-local offset (the real ConfigureDesk does exactly this).
    private static readonly SysVec2 ViewportSize = new(1920f, 1080f);
    private const float OriginX = -200f;
    private const float OriginY = -120f;

    [Fact]
    public void Build_throws_on_a_non_positive_viewport()
    {
        Assert.Throws<System.ArgumentException>(
            () => DeskTrianglePlaceholderLogic.Build(
                new SysVec2(0f, 1080f), OriginX, OriginY));
        Assert.Throws<System.ArgumentException>(
            () => DeskTrianglePlaceholderLogic.Build(
                new SysVec2(1920f, -1f), OriginX, OriginY));
    }

    [Fact]
    public void Both_triangles_have_a_vertex_in_their_bottom_corner()
    {
        var tris = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float left = OriginX;
        float right = OriginX + ViewportSize.X;
        float bottom = OriginY + ViewportSize.Y;

        Assert.Equal(new SysVec2(left, bottom), tris.Left.A);
        Assert.Equal(new SysVec2(right, bottom), tris.Right.A);
    }

    [Fact]
    public void The_two_triangles_are_mirror_symmetric_about_the_viewport_centre()
    {
        var tris = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float centreX = OriginX + ViewportSize.X * 0.5f;

        // Each left vertex mirrored across centreX must equal a right vertex.
        Assert.Equal(2f * centreX - tris.Left.A.X, tris.Right.A.X, precision: 3);
        Assert.Equal(tris.Left.A.Y, tris.Right.A.Y, precision: 3);
        Assert.Equal(2f * centreX - tris.Left.B.X, tris.Right.B.X, precision: 3);
        Assert.Equal(tris.Left.B.Y, tris.Right.B.Y, precision: 3);
        Assert.Equal(2f * centreX - tris.Left.C.X, tris.Right.C.X, precision: 3);
        Assert.Equal(tris.Left.C.Y, tris.Right.C.Y, precision: 3);
    }

    [Fact]
    public void Neither_triangle_climbs_above_the_viewport_mid_line()
    {
        // The whole point of the placeholder: the desk wedges stay in the
        // bottom half so they never overpaint the maquette zone.
        var tris = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float midY = OriginY + ViewportSize.Y * 0.5f;

        foreach (var tri in new[] { tris.Left, tris.Right })
        {
            foreach (var v in DeskTrianglePlaceholderLogic.Vertices(tri))
            {
                Assert.True(v.Y >= midY,
                    $"triangle vertex {v} climbs above the mid-line {midY}");
            }
        }
    }

    [Fact]
    public void The_screen_centre_is_left_unpainted()
    {
        // The maquette diamond must show through the centre — the centre
        // point must be in NEITHER triangle.
        var tris = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var centre = new SysVec2(
            OriginX + ViewportSize.X * 0.5f,
            OriginY + ViewportSize.Y * 0.5f);

        Assert.False(DeskTrianglePlaceholderLogic.Contains(tris, centre),
            "the screen centre must be unpainted so the maquette shows through");
    }

    [Fact]
    public void A_point_deep_in_each_bottom_corner_is_inside_its_triangle()
    {
        var tris = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float left = OriginX;
        float right = OriginX + ViewportSize.X;
        float bottom = OriginY + ViewportSize.Y;

        // A point a small step in from each bottom corner.
        var nearBottomLeft = new SysVec2(left + 20f, bottom - 20f);
        var nearBottomRight = new SysVec2(right - 20f, bottom - 20f);

        Assert.True(DeskTrianglePlaceholderLogic.Contains(tris, nearBottomLeft));
        Assert.True(DeskTrianglePlaceholderLogic.Contains(tris, nearBottomRight));
    }

    [Fact]
    public void A_point_above_the_screen_top_is_outside_both_triangles()
    {
        var tris = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var nearTop = new SysVec2(
            OriginX + ViewportSize.X * 0.5f, OriginY + 40f);

        Assert.False(DeskTrianglePlaceholderLogic.Contains(tris, nearTop));
    }

    [Fact]
    public void PointInTriangle_is_winding_agnostic()
    {
        // Same triangle, vertices reversed — the inside test must agree.
        var cw = new DeskTrianglePlaceholderLogic.Triangle(
            new SysVec2(0f, 0f), new SysVec2(10f, 0f), new SysVec2(0f, 10f));
        var ccw = new DeskTrianglePlaceholderLogic.Triangle(
            new SysVec2(0f, 0f), new SysVec2(0f, 10f), new SysVec2(10f, 0f));

        var inside = new SysVec2(2f, 2f);
        var outside = new SysVec2(8f, 8f);

        Assert.True(DeskTrianglePlaceholderLogic.PointInTriangle(cw, inside));
        Assert.True(DeskTrianglePlaceholderLogic.PointInTriangle(ccw, inside));
        Assert.False(DeskTrianglePlaceholderLogic.PointInTriangle(cw, outside));
        Assert.False(DeskTrianglePlaceholderLogic.PointInTriangle(ccw, outside));
    }

    [Fact]
    public void A_point_on_an_edge_counts_as_inside()
    {
        var tri = new DeskTrianglePlaceholderLogic.Triangle(
            new SysVec2(0f, 0f), new SysVec2(10f, 0f), new SysVec2(0f, 10f));

        // Midpoint of the A-B edge.
        Assert.True(DeskTrianglePlaceholderLogic.PointInTriangle(
            tri, new SysVec2(5f, 0f)));
    }

    [Fact]
    public void Vertices_returns_the_three_triangle_corners()
    {
        var tri = new DeskTrianglePlaceholderLogic.Triangle(
            new SysVec2(1f, 2f), new SysVec2(3f, 4f), new SysVec2(5f, 6f));

        var v = DeskTrianglePlaceholderLogic.Vertices(tri);

        Assert.Equal(3, v.Length);
        Assert.Equal(tri.A, v[0]);
        Assert.Equal(tri.B, v[1]);
        Assert.Equal(tri.C, v[2]);
    }
}
