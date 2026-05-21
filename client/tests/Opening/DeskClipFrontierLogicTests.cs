using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskClipFrontierLogic"/> — the J3c-1ter
/// desk <b>twin-corner</b> triangular-clip geometry. Pins the contract that
/// turns the centred maquette diamond into the five UV-space uniforms the
/// <c>desk_triangle_clip.gdshader</c> consumes: a left hypotenuse half-plane,
/// a right hypotenuse half-plane, and a vertical split.
///
/// <para>
/// <b>What this suite owns.</b> The centred-diamond placement (side apexes
/// on the screen edges, lower point on the horizontal middle), the
/// screen-to-UV mapping, the orientation of each hypotenuse normal toward
/// its own desk corner, the split being the diamond lower point's UV x,
/// and the loud failure on a degenerate input. The decisive behaviour
/// test: the two bottom-screen corners are kept (the desk), the centre and
/// the top-screen corners are dropped (the maquette diamond). The
/// Godot-bound wiring — feeding scene geometry in and writing the shader
/// uniforms out — lives in <c>GameScreen.ApplyDeskClipFrontier</c> and is
/// validated via the <c>GameScreen.tscn</c> F6 smoke.
/// </para>
///
/// <para>
/// <b>J3c-1ter shape (Didier, 2026-05-21, from the in-game capture).</b>
/// Earlier J3c-1bis cut a single bottom-left half-screen triangle. The
/// refined spec: the desk is two mirrored brown triangles in the two
/// bottom screen corners, the maquette is a centred iso diamond whose
/// lower point sits on the screen's horizontal middle. The fixture rect
/// below is the full 1920x1080 screen, matching <c>GameScreen.tscn</c>'s
/// full-screen <c>DeskTextureRect</c> (anchors_preset 15).
/// </para>
/// </summary>
public sealed class DeskClipFrontierLogicTests
{
    // The full 1920x1080 screen — GameScreen.tscn's full-screen DeskTextureRect.
    private static readonly SysVec2 DeskOrigin = new(0f, 0f);
    private static readonly SysVec2 DeskSize = new(1920f, 1080f);
    private const float TopHudBand = 32f;

    // --- ScreenToUv -------------------------------------------------------

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

    // --- MaquetteDiamond --------------------------------------------------

    [Fact]
    public void MaquetteDiamond_side_apexes_touch_the_screen_left_and_right_edges()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);

        Assert.Equal(0f, d.LeftApex.X, precision: 3);
        Assert.Equal(DeskSize.X, d.RightApex.X, precision: 3);
        // Side apexes share the same Y — the diamond is symmetric.
        Assert.Equal(d.LeftApex.Y, d.RightApex.Y, precision: 3);
    }

    [Fact]
    public void MaquetteDiamond_lower_point_is_on_the_screen_horizontal_middle()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);

        // The lower point's X is the screen horizontal middle — the
        // refined spec's "the lower point arrives at the middle".
        Assert.Equal(DeskSize.X * 0.5f, d.LowerPoint.X, precision: 3);
        // And it sits at the screen vertical middle (LowerPointScreenFracY).
        Assert.Equal(
            DeskSize.Y * DeskClipFrontierLogic.LowerPointScreenFracY,
            d.LowerPoint.Y, precision: 3);
    }

    [Fact]
    public void MaquetteDiamond_lower_point_is_below_the_side_apexes()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);

        // +Y is down: the lower point has the largest Y, the top apex the
        // smallest, the side apexes between.
        Assert.True(d.LowerPoint.Y > d.LeftApex.Y,
            "the lower point must be below the side apexes");
        Assert.True(d.LeftApex.Y > d.TopApex.Y,
            "the side apexes must be below the top apex");
        // The top apex of a full-width iso diamond is cropped above-screen.
        Assert.True(d.TopApex.Y < 0f,
            "the top apex is cropped above the screen / behind the HUD");
    }

    [Fact]
    public void MaquetteDiamond_keeps_the_iso_2_to_1_ratio()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);

        // A 2:1 iso diamond: full width = 2 * full height. Width is the
        // screen width; height is lowerPoint.Y - topApex.Y.
        float fullWidth = d.RightApex.X - d.LeftApex.X;
        float fullHeight = d.LowerPoint.Y - d.TopApex.Y;
        Assert.Equal(2f, fullWidth / fullHeight, precision: 3);
    }

    [Fact]
    public void MaquetteDiamond_throws_on_a_non_positive_screen()
    {
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.MaquetteDiamond(
                new SysVec2(0f, 1080f), TopHudBand));
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.MaquetteDiamond(
                new SysVec2(1920f, -1f), TopHudBand));
    }

    [Fact]
    public void MaquetteDiamond_throws_on_a_negative_hud_band()
    {
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.MaquetteDiamond(DeskSize, -1f));
    }

    // --- Compute: the twin-corner clip ------------------------------------

    [Fact]
    public void Compute_split_is_the_diamond_lower_point_uv_x()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);

        var frontier = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        // The lower point is on the horizontal middle, so the split is 0.5.
        Assert.Equal(0.5f, frontier.SplitU, precision: 4);
    }

    [Fact]
    public void Compute_keeps_both_bottom_screen_corners_for_the_desk()
    {
        // The desk is the two BOTTOM screen corners. Both must be inside
        // their own kept triangle: bottom-left under the left half-plane,
        // bottom-right under the right half-plane.
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);
        var f = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        Assert.True(KeptByShader(f, new SysVec2(0f, 1f)),
            "the bottom-left screen corner must be kept (desk)");
        Assert.True(KeptByShader(f, new SysVec2(1f, 1f)),
            "the bottom-right screen corner must be kept (desk)");
    }

    [Fact]
    public void Compute_drops_the_top_screen_corners_and_the_diamond_interior()
    {
        // The top screen corners and a point well inside the maquette
        // diamond belong to the maquette — the shader must discard them.
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);
        var f = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        Assert.False(KeptByShader(f, new SysVec2(0f, 0f)),
            "the top-left screen corner must be dropped (maquette)");
        Assert.False(KeptByShader(f, new SysVec2(1f, 0f)),
            "the top-right screen corner must be dropped (maquette)");

        // The diamond centre — on the vertical middle, halfway between the
        // top apex and the lower point. Strictly inside the diamond, so
        // dropped. (Not the screen centre: the screen centre is exactly the
        // diamond's lower point, an on-the-frontier edge case.)
        var topApexUv = DeskClipFrontierLogic.ScreenToUv(
            d.TopApex, DeskOrigin, DeskSize);
        var lowerUv = DeskClipFrontierLogic.ScreenToUv(
            d.LowerPoint, DeskOrigin, DeskSize);
        var diamondCentre = new SysVec2(
            0.5f, (topApexUv.Y + lowerUv.Y) * 0.5f);
        Assert.False(KeptByShader(f, diamondCentre),
            "the diamond centre must be dropped (inside the maquette diamond)");
    }

    [Fact]
    public void Compute_drops_just_above_and_keeps_just_below_the_lower_point()
    {
        // Just ABOVE the diamond's lower point (on the split line) is still
        // inside the diamond -> dropped; just BELOW it, slightly left of
        // the split, is in the left desk corner -> kept.
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);
        var f = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        var lowerPointUv = DeskClipFrontierLogic.ScreenToUv(
            d.LowerPoint, DeskOrigin, DeskSize);

        var insideDiamond = new SysVec2(
            lowerPointUv.X, lowerPointUv.Y - 0.05f);
        Assert.False(KeptByShader(f, insideDiamond),
            "just above the diamond lower point is inside the diamond");

        var belowLowerPoint = new SysVec2(
            lowerPointUv.X - 0.02f, lowerPointUv.Y + 0.05f);
        Assert.True(KeptByShader(f, belowLowerPoint),
            "just below the diamond lower point is in the left desk corner");
    }

    [Fact]
    public void Compute_left_halfplane_cannot_bleed_into_the_right_corner()
    {
        // The split is what confines each half-plane to its own corner. The
        // left hypotenuse's half-plane is unbounded, so a point right of the
        // split could satisfy its dot test; the split (uv.x <= splitU) must
        // keep it out of the left triangle, so a point right of the split
        // that is inside the diamond stays dropped.
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);
        var f = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        // x = 0.75 (right of the split) and y = 0.15 (above the right
        // hypotenuse, so inside the diamond and NOT in the right corner).
        // If the left half-plane could bleed across the split it would be
        // wrongly kept.
        var rightOfSplitInsideDiamond = new SysVec2(0.75f, 0.15f);
        Assert.False(KeptByShader(f, rightOfSplitInsideDiamond),
            "a point right of the split inside the diamond must be dropped — " +
            "the left half-plane must not bleed across the split");
    }

    [Fact]
    public void Compute_throws_on_a_non_positive_desk_rect()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand);

        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.Compute(
                d, DeskOrigin, new SysVec2(0f, 1080f)));
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.Compute(
                d, DeskOrigin, new SysVec2(1920f, -1f)));
    }

    [Fact]
    public void Compute_throws_on_a_degenerate_zero_length_hypotenuse()
    {
        // A diamond whose lower point coincides with the left apex gives a
        // zero-length left hypotenuse — the frontier is undefined.
        var apex = new SysVec2(100f, 100f);
        var degenerate = new DeskClipFrontierLogic.DiamondApexes(
            TopApex: new SysVec2(960f, -200f),
            LeftApex: apex,
            RightApex: new SysVec2(1920f, 60f),
            LowerPoint: apex);

        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.Compute(degenerate, DeskOrigin, DeskSize));
    }

    /// <summary>
    /// Mirror of the shader's keep test: a fragment at UV <paramref name="uv"/>
    /// is kept when it is inside the bottom-left corner triangle
    /// (<c>uv.x &lt;= split</c> AND left dot &gt;= 0) OR inside the
    /// bottom-right corner triangle (<c>uv.x &gt;= split</c> AND right dot
    /// &gt;= 0).
    /// </summary>
    private static bool KeptByShader(
        DeskClipFrontierLogic.TwinClipFrontier f, SysVec2 uv)
    {
        bool inLeft =
            uv.X <= f.SplitU
            && SysVec2.Dot(uv - f.LeftPointUv, f.LeftNormalUv) >= 0f;
        bool inRight =
            uv.X >= f.SplitU
            && SysVec2.Dot(uv - f.RightPointUv, f.RightNormalUv) >= 0f;
        return inLeft || inRight;
    }
}
