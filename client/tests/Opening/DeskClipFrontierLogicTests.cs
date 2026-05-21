using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskClipFrontierLogic"/> — the J3c-1bis
/// desk triangular-clip geometry. Pins the contract that turns the
/// maquette/desk frontier (a diagonal in screen pixels) into the
/// <c>clip_line_point</c> / <c>clip_line_normal</c> UV-space uniforms the
/// <c>desk_triangle_clip.gdshader</c> consumes.
///
/// <para>
/// <b>What this suite owns.</b> The screen-to-UV mapping, the orientation
/// of the frontier normal toward the desk (bottom-left) keep side, and the
/// loud failure on a degenerate (zero-length edge / non-positive rect)
/// input. The Godot-bound wiring — feeding scene geometry in and writing
/// the shader uniforms out — lives in <c>GameScreen.ApplyDeskClipFrontier</c>
/// and is validated via the <c>GameScreen.tscn</c> F6 smoke.
/// </para>
///
/// <para>
/// <b>Full-screen desk rect (J3c-1bis F6 fix, 2026-05-21).</b> The desk
/// <c>TextureRect</c> is full-screen so the triangular clip runs corner to
/// corner (a small rect bounded the triangle with a hard horizontal / hard
/// vertical border — the "truncated triangle" F6 bug). The fixture rect
/// below therefore matches the full screen, <c>[0,0 1920x1080]</c>. The
/// helper itself is pure geometry and rect-size agnostic; the fixture is
/// kept in sync only so the tests document the real wired rect.
/// </para>
/// </summary>
public sealed class DeskClipFrontierLogicTests
{
    // A representative desk rect: the full 1920x1080 screen, matching
    // GameScreen.tscn's full-screen DeskTextureRect (anchors_preset 15).
    private static readonly SysVec2 DeskOrigin = new(0f, 0f);
    private static readonly SysVec2 DeskSize = new(1920f, 1080f);

    [Fact]
    public void ScreenToUv_maps_rect_origin_to_uv_zero()
    {
        var uv = DeskClipFrontierLogic.ScreenToUv(DeskOrigin, DeskOrigin, DeskSize);

        Assert.Equal(0f, uv.X, precision: 4);
        Assert.Equal(0f, uv.Y, precision: 4);
    }

    [Fact]
    public void ScreenToUv_maps_rect_far_corner_to_uv_one()
    {
        var farCorner = DeskOrigin + DeskSize;

        var uv = DeskClipFrontierLogic.ScreenToUv(farCorner, DeskOrigin, DeskSize);

        Assert.Equal(1f, uv.X, precision: 4);
        Assert.Equal(1f, uv.Y, precision: 4);
    }

    [Fact]
    public void ScreenToUv_maps_rect_centre_to_uv_half()
    {
        var centre = DeskOrigin + DeskSize * 0.5f;

        var uv = DeskClipFrontierLogic.ScreenToUv(centre, DeskOrigin, DeskSize);

        Assert.Equal(0.5f, uv.X, precision: 4);
        Assert.Equal(0.5f, uv.Y, precision: 4);
    }

    [Fact]
    public void Compute_point_is_the_edge_start_in_uv_space()
    {
        // The frontier is the rect's main diagonal — top-left corner to
        // bottom-right corner — which is the diagonal that separates the
        // bottom-LEFT desk triangle from the top-RIGHT maquette triangle.
        var edgeStart = DeskOrigin;                          // top-left (0,0)
        var edgeEnd = DeskOrigin + DeskSize;                 // bottom-right (1,1)

        var frontier = DeskClipFrontierLogic.Compute(
            edgeStart, edgeEnd, DeskOrigin, DeskSize);

        // The frontier point is the edge start mapped into UV: the
        // top-left corner -> (0, 0).
        Assert.Equal(0f, frontier.PointUv.X, precision: 4);
        Assert.Equal(0f, frontier.PointUv.Y, precision: 4);
    }

    [Fact]
    public void Compute_keeps_the_bottom_left_corner_and_drops_the_top_right()
    {
        // The desk is the BOTTOM-LEFT triangle. The frontier is the rect's
        // main diagonal (top-left to bottom-right): the bottom-left corner
        // must be on the keep side (dot >= 0) and the top-right corner on
        // the drop side (dot < 0).
        var edgeStart = DeskOrigin;            // top-left corner
        var edgeEnd = DeskOrigin + DeskSize;   // bottom-right corner

        var frontier = DeskClipFrontierLogic.Compute(
            edgeStart, edgeEnd, DeskOrigin, DeskSize);

        // Shader keep test: dot(uv - point, normal) >= 0.
        float bottomLeftSide = SysDot(
            new SysVec2(0f, 1f) - frontier.PointUv, frontier.NormalUv);
        float topRightSide = SysDot(
            new SysVec2(1f, 0f) - frontier.PointUv, frontier.NormalUv);

        Assert.True(bottomLeftSide >= 0f,
            $"bottom-left corner must be kept; got side={bottomLeftSide}");
        Assert.True(topRightSide < 0f,
            $"top-right corner must be dropped; got side={topRightSide}");
    }

    [Fact]
    public void Compute_keeps_the_bottom_left_even_when_edge_endpoints_are_swapped()
    {
        // The helper must not depend on which endpoint is "start": the
        // normal is oriented toward the desk corner after the fact, so
        // swapping start/end leaves the keep side unchanged.
        var topLeft = DeskOrigin;
        var bottomRight = DeskOrigin + DeskSize;

        var a = DeskClipFrontierLogic.Compute(
            topLeft, bottomRight, DeskOrigin, DeskSize);
        var b = DeskClipFrontierLogic.Compute(
            bottomRight, topLeft, DeskOrigin, DeskSize);

        // Bottom-left corner is kept under both orderings.
        Assert.True(SysDot(new SysVec2(0f, 1f) - a.PointUv, a.NormalUv) >= 0f);
        Assert.True(SysDot(new SysVec2(0f, 1f) - b.PointUv, b.NormalUv) >= 0f);
        // Top-right corner is dropped under both orderings.
        Assert.True(SysDot(new SysVec2(1f, 0f) - a.PointUv, a.NormalUv) < 0f);
        Assert.True(SysDot(new SysVec2(1f, 0f) - b.PointUv, b.NormalUv) < 0f);
    }

    [Fact]
    public void Compute_throws_on_a_zero_length_edge()
    {
        var p = DeskOrigin + new SysVec2(100f, 100f);

        // A zero-length maquette edge leaves the frontier undefined — a
        // caller bug, not a silent default.
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.Compute(p, p, DeskOrigin, DeskSize));
    }

    [Theory]
    [InlineData(0f, 1080f)]
    [InlineData(1920f, 0f)]
    [InlineData(-1f, 1080f)]
    public void Compute_throws_on_a_non_positive_desk_rect(float w, float h)
    {
        var edgeStart = new SysVec2(0f, 0f);
        var edgeEnd = new SysVec2(100f, 100f);

        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.Compute(
                edgeStart, edgeEnd, DeskOrigin, new SysVec2(w, h)));
    }

    private static float SysDot(SysVec2 a, SysVec2 b)
        => SysVec2.Dot(a, b);
}
