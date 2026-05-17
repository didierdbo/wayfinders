using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="IsoCharacterGeometryLogic"/> -- the three
/// visible faces of the 2.5D iso parallelepiped silhouette rendered by
/// <c>IsoCharacterPlaceholder</c>. Locks the projection convention
/// (true 2:1 iso, depth = half-width, Y down, origin at top apex) and
/// the shared-vertex invariant that makes the three faces read as one
/// solid block instead of three detached shapes.
///
/// <para>
/// Without these pins, the d5c3705 regression (cube-2D + floating
/// losange) is invisible to CI -- the visual bug only shows up at
/// smoke time. These tests catch any geometry drift that breaks the
/// silhouette as soon as a coordinate moves.
/// </para>
/// </summary>
public sealed class IsoCharacterGeometryLogicTests
{
    // Reference dimensions used across the pins. Match the default
    // IsoCharacterPlaceholder sizing (W=160, D=80, H=200 -> 160x280
    // bounding box, true 2:1 iso ratio so the three faces read as
    // visibly inclined instead of a near-frontal cube).
    private const float W = 160f;
    private const float D = 80f;
    private const float H = 200f;

    // --------------------------------------------------------------------
    // Top face -- iso losange, 4 vertices, 2:1 ratio.
    // --------------------------------------------------------------------

    [Fact]
    public void TopFace_has_four_vertices()
        => Assert.Equal(4, IsoCharacterGeometryLogic.TopFace(W, D, H).Length);

    [Fact]
    public void TopFace_top_apex_is_centered_at_origin_y()
    {
        var pts = IsoCharacterGeometryLogic.TopFace(W, D, H);
        Assert.Equal(W * 0.5f, pts[0].X);
        Assert.Equal(0f, pts[0].Y);
    }

    [Fact]
    public void TopFace_spans_full_width_at_mid_depth()
    {
        var pts = IsoCharacterGeometryLogic.TopFace(W, D, H);
        // right apex
        Assert.Equal(W, pts[1].X);
        Assert.Equal(D * 0.5f, pts[1].Y);
        // left apex
        Assert.Equal(0f, pts[3].X);
        Assert.Equal(D * 0.5f, pts[3].Y);
    }

    [Fact]
    public void TopFace_bottom_apex_is_shared_front_top_vertex()
    {
        var pts = IsoCharacterGeometryLogic.TopFace(W, D, H);
        Assert.Equal(W * 0.5f, pts[2].X);
        Assert.Equal(D, pts[2].Y);
    }

    [Fact]
    public void TopFace_is_independent_of_height()
    {
        var lo = IsoCharacterGeometryLogic.TopFace(W, D, 10f);
        var hi = IsoCharacterGeometryLogic.TopFace(W, D, 500f);
        Assert.Equal(lo, hi);
    }

    // --------------------------------------------------------------------
    // Front face -- parallelogram, 4 vertices, leans down-right.
    // --------------------------------------------------------------------

    [Fact]
    public void FrontFace_top_left_matches_top_losange_left_apex()
    {
        var top = IsoCharacterGeometryLogic.TopFace(W, D, H);
        var front = IsoCharacterGeometryLogic.FrontFace(W, D, H);
        Assert.Equal(top[3], front[0]);
    }

    [Fact]
    public void FrontFace_top_right_matches_shared_front_top_vertex()
    {
        var top = IsoCharacterGeometryLogic.TopFace(W, D, H);
        var front = IsoCharacterGeometryLogic.FrontFace(W, D, H);
        // top[2] is the bottom apex of the losange = shared vertex
        Assert.Equal(top[2], front[1]);
    }

    [Fact]
    public void FrontFace_bottom_right_sits_at_total_box_bottom()
    {
        var front = IsoCharacterGeometryLogic.FrontFace(W, D, H);
        // The front-bottom-right is the lowest visible point of the
        // box (y = D + H) ; the front-bottom-left is at the left-edge
        // depth-band offset (y = D/2 + H).
        Assert.Equal(D + H, front[2].Y);
        Assert.Equal(D * 0.5f + H, front[3].Y);
    }

    // --------------------------------------------------------------------
    // Right face -- parallelogram, 4 vertices, leans down-left.
    // --------------------------------------------------------------------

    [Fact]
    public void RightFace_top_left_matches_shared_front_top_vertex()
    {
        var top = IsoCharacterGeometryLogic.TopFace(W, D, H);
        var right = IsoCharacterGeometryLogic.RightFace(W, D, H);
        Assert.Equal(top[2], right[0]);
    }

    [Fact]
    public void RightFace_top_right_matches_top_losange_right_apex()
    {
        var top = IsoCharacterGeometryLogic.TopFace(W, D, H);
        var right = IsoCharacterGeometryLogic.RightFace(W, D, H);
        Assert.Equal(top[1], right[1]);
    }

    [Fact]
    public void RightFace_bottom_left_sits_at_total_box_bottom()
    {
        var right = IsoCharacterGeometryLogic.RightFace(W, D, H);
        // right[2] = right-bottom-right, sits at D/2 + H (right edge,
        // raised by the depth band).
        // right[3] = right-bottom-left, sits at D + H (front-bottom
        // apex of the box, shared with front[2]).
        Assert.Equal(D * 0.5f + H, right[2].Y);
        Assert.Equal(D + H, right[3].Y);
    }

    // --------------------------------------------------------------------
    // The geometric invariant : front and right faces share their
    // top-right / top-left vertex with each other AND with the bottom
    // apex of the top losange. This is the trio of points whose
    // misalignment caused the cube-2D-with-floating-losange symptom.
    // --------------------------------------------------------------------

    [Fact]
    public void Three_faces_meet_at_common_front_top_vertex()
    {
        var top = IsoCharacterGeometryLogic.TopFace(W, D, H);
        var front = IsoCharacterGeometryLogic.FrontFace(W, D, H);
        var right = IsoCharacterGeometryLogic.RightFace(W, D, H);

        // top[2] (losange bottom) == front[1] (front top-right)
        //                          == right[0] (right top-left)
        Assert.Equal(top[2], front[1]);
        Assert.Equal(top[2], right[0]);
        Assert.Equal(front[1], right[0]);
    }

    [Fact]
    public void Front_and_right_faces_share_bottom_apex_edge()
    {
        var front = IsoCharacterGeometryLogic.FrontFace(W, D, H);
        var right = IsoCharacterGeometryLogic.RightFace(W, D, H);
        // front[2] (front bottom-right) == right[3] (right bottom-left)
        Assert.Equal(front[2], right[3]);
    }

    // --------------------------------------------------------------------
    // Bounding box -- total silhouette must be exactly W wide and
    // (D + H) tall. If this drifts, the Control padding logic ships
    // an off-by-one and the silhouette either crops or floats inside
    // the panel.
    // --------------------------------------------------------------------

    [Fact]
    public void Total_bounding_box_is_W_by_DplusH()
    {
        var top = IsoCharacterGeometryLogic.TopFace(W, D, H);
        var front = IsoCharacterGeometryLogic.FrontFace(W, D, H);
        var right = IsoCharacterGeometryLogic.RightFace(W, D, H);

        float minX = System.Math.Min(top[3].X, System.Math.Min(front[0].X, right[0].X));
        float maxX = System.Math.Max(top[1].X, System.Math.Max(front[1].X, right[1].X));
        float minY = top[0].Y;
        // Lowest visible Y on the box is the shared front-bottom apex
        // : front[2] = right[3] = (W/2, D + H). The side bottom-outer
        // corners (front[3], right[2]) sit higher, at D/2 + H.
        float maxY = System.Math.Max(front[2].Y, right[3].Y);

        Assert.Equal(0f, minX);
        Assert.Equal(W, maxX);
        Assert.Equal(0f, minY);
        Assert.Equal(D + H, maxY);
    }
}
