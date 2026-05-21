using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskClipFrontierLogic"/> — the J3c-1 desk
/// <b>twin-corner</b> triangular-clip geometry. Pins the contract that turns
/// the centred maquette diamond into the five UV-space uniforms the
/// <c>desk_triangle_clip.gdshader</c> consumes: a left hypotenuse half-plane,
/// a right hypotenuse half-plane, and a vertical split.
///
/// <para>
/// <b>What this suite owns.</b> The strict iso 2:1 slope of all four diamond
/// edges (the regression invariant — see below), the sideways-overflowing
/// diamond placement, the proportion knob that sizes it, the lower point
/// being placed flush on the bottom HUD band's top edge (J3c-1octies — no
/// desk strip below the diamond), the screen-to-UV mapping, the orientation
/// of each hypotenuse normal toward its own desk corner, the split being the
/// diamond lower point's UV x, and the loud failure on a degenerate input.
/// The decisive behaviour test: the two bottom-screen corners are kept (the
/// desk), the centre and the top-screen corners are dropped (the maquette
/// diamond). The Godot-bound wiring — feeding scene geometry in and writing
/// the shader uniforms out — lives in <c>GameScreen.ApplyDeskClipFrontier</c>
/// and is validated via the <c>GameScreen.tscn</c> F6 smoke.
/// </para>
///
/// <para>
/// <b>The regression this suite locks (J3c-1quinquies, Didier,
/// 2026-05-21).</b> Commit 5f9aeb5 grew the maquette by driving the
/// diamond's vertical extent from a free screen-height fraction. That made
/// a lower edge run <c>(0,324) -&gt; (960,1015.2)</c> — slope ≈ 0.72 — so
/// the diamond outline stopped being parallel to the iso grid drawn inside
/// it. The fix re-locks the diamond to a strict iso 2:1 rhombus (edge slope
/// exactly <see cref="DeskClipFrontierLogic.IsoEdgeSlope"/> = 0.5) and keeps
/// the maquette large by letting the diamond overflow the screen sideways
/// (side apexes off-screen). <see cref="All_four_diamond_edges_have_the_iso_2to1_slope"/>
/// is the dedicated invariant guard.
/// </para>
///
/// <para>
/// <b>J3c-1octies (Didier, 2026-05-21) — the lower point is derived from
/// the bottom HUD band.</b> The lower point used to sit at a magic
/// screen-height fraction (0.96), ~11 px above the bottom HUD band, leaving
/// a thin strip of desk floor visible below the diamond. It is now placed at
/// <c>screenHeight - bottomHudBandHeight</c> — flush on the band's top edge.
/// <see cref="MaquetteDiamond_lower_point_is_flush_on_the_bottom_hud_band"/>
/// is the dedicated guard.
/// </para>
/// </summary>
public sealed class DeskClipFrontierLogicTests
{
    // The full 1920x1080 screen — GameScreen.tscn's full-screen DeskTextureRect.
    private static readonly SysVec2 DeskOrigin = new(0f, 0f);
    private static readonly SysVec2 DeskSize = new(1920f, 1080f);
    private const float TopHudBand = 32f;
    private const float BottomHudBand = 32f;

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

    // --- THE ISO INVARIANT — the regression guard -------------------------

    [Fact]
    public void All_four_diamond_edges_have_the_iso_2to1_slope()
    {
        // THE invariant commit 5f9aeb5 broke: every edge of the maquette
        // diamond must have slope EXACTLY 0.5 (iso 2:1 — one vertical for
        // two horizontal), so the diamond outline stays parallel to the
        // diagonals of the iso grid drawn inside it. If a future change
        // re-introduces a free vertical stretch, this test goes red here,
        // not at an F6 capture.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

        // Top-left edge: top apex -> left apex.
        Assert.Equal(
            DeskClipFrontierLogic.IsoEdgeSlope,
            DeskClipFrontierLogic.EdgeSlope(d.TopApex, d.LeftApex),
            precision: 5);
        // Top-right edge: top apex -> right apex.
        Assert.Equal(
            DeskClipFrontierLogic.IsoEdgeSlope,
            DeskClipFrontierLogic.EdgeSlope(d.TopApex, d.RightApex),
            precision: 5);
        // Bottom-left edge: left apex -> lower point (the left desk
        // hypotenuse — this is the one the F6 [DESK-DIAG] log diagnosed at
        // slope 0.72).
        Assert.Equal(
            DeskClipFrontierLogic.IsoEdgeSlope,
            DeskClipFrontierLogic.EdgeSlope(d.LeftApex, d.LowerPoint),
            precision: 5);
        // Bottom-right edge: lower point -> right apex.
        Assert.Equal(
            DeskClipFrontierLogic.IsoEdgeSlope,
            DeskClipFrontierLogic.EdgeSlope(d.LowerPoint, d.RightApex),
            precision: 5);
    }

    [Fact]
    public void Iso_edge_slope_constant_is_exactly_one_half()
    {
        // The 2:1 iso ratio is a property of the projection, not a tunable
        // value. Pin the constant itself so it cannot drift.
        Assert.Equal(0.5f, DeskClipFrontierLogic.IsoEdgeSlope);
    }

    [Fact]
    public void EdgeSlope_throws_on_a_vertical_edge()
    {
        // An iso diamond has no vertical edge; a Δx == 0 input is degenerate.
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.EdgeSlope(
                new SysVec2(100f, 0f), new SysVec2(100f, 500f)));
    }

    [Fact]
    public void Iso_slope_holds_across_screen_sizes()
    {
        // The slope is derived from IsoEdgeSlope, not from the screen — it
        // must stay 0.5 on any screen aspect ratio.
        foreach (var screen in new[]
                 {
                     new SysVec2(1280f, 720f),
                     new SysVec2(2560f, 1080f),
                     new SysVec2(1080f, 1920f), // portrait
                 })
        {
            var d = DeskClipFrontierLogic.MaquetteDiamond(
                screen, TopHudBand, BottomHudBand);
            Assert.Equal(
                DeskClipFrontierLogic.IsoEdgeSlope,
                DeskClipFrontierLogic.EdgeSlope(d.LeftApex, d.LowerPoint),
                precision: 5);
            Assert.Equal(
                DeskClipFrontierLogic.IsoEdgeSlope,
                DeskClipFrontierLogic.EdgeSlope(d.LowerPoint, d.RightApex),
                precision: 5);
        }
    }

    // --- The proportion knob ----------------------------------------------

    [Fact]
    public void Half_width_knob_overflows_the_screen_so_the_maquette_owns_it()
    {
        // The J3c-1quinquies intent: the maquette is kept large by making
        // the diamond WIDER than the screen (side apexes off-screen), NOT
        // by stretching it vertically. A half-width fraction > 0.5 is what
        // produces that overflow.
        Assert.True(
            DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX > 0.5f,
            "the half-width fraction must exceed 0.5 so the diamond " +
            "overflows the screen sideways and the maquette stays large");
    }

    [Fact]
    public void Lower_point_fraction_fallback_puts_the_diamond_low()
    {
        // The diamond points down; the fallback lower-point fraction (used
        // only when no bottom HUD band is supplied) must sit below the
        // vertical middle so the desk does not eat the bottom half.
        Assert.True(
            DeskClipFrontierLogic.MaquetteLowerPointScreenFracY > 0.5f,
            "the fallback lower-point fraction must sit below the screen " +
            "vertical middle so the desk does not eat the bottom half");
    }

    // --- MaquetteDiamond --------------------------------------------------

    [Fact]
    public void MaquetteDiamond_side_apexes_overflow_off_screen_left_and_right()
    {
        // With the default half-width fraction (0.95 > 0.5) the diamond is
        // wider than the screen: the left apex has a NEGATIVE x and the
        // right apex an x past the screen width. This is the deliberate
        // sideways overflow.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

        Assert.True(d.LeftApex.X < 0f,
            $"left apex must overflow off-screen left; got x={d.LeftApex.X}");
        Assert.True(d.RightApex.X > DeskSize.X,
            $"right apex must overflow off-screen right; got x={d.RightApex.X}");
        // The diamond stays centred and symmetric: the two apexes are
        // mirror images about the screen's horizontal middle.
        float centreX = DeskSize.X * 0.5f;
        Assert.Equal(centreX - d.LeftApex.X, d.RightApex.X - centreX, precision: 3);
        // Side apexes share the same Y — the diamond is symmetric.
        Assert.Equal(d.LeftApex.Y, d.RightApex.Y, precision: 3);
    }

    [Fact]
    public void MaquetteDiamond_lower_point_is_on_the_screen_horizontal_middle()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

        // The lower point's X is the screen horizontal middle — the diamond
        // is centred, which is what makes the two desk corners symmetric.
        Assert.Equal(DeskSize.X * 0.5f, d.LowerPoint.X, precision: 3);
    }

    [Fact]
    public void MaquetteDiamond_lower_point_is_flush_on_the_bottom_hud_band()
    {
        // J3c-1octies (Didier, 2026-05-21): the diamond's lower point must
        // land EXACTLY on the bottom HUD band's top edge — screenHeight
        // minus the bottom band — so no strip of desk floor shows between
        // the diamond's lower point and the HUD. Before this fix the lower
        // point sat at a magic 0.96 fraction (y ≈ 1037 on a 1080 screen),
        // ~11 px above a 32 px bottom band [1048,1080] — the visible brown
        // band Didier flagged.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

        Assert.Equal(
            DeskSize.Y - BottomHudBand, d.LowerPoint.Y, precision: 3);

        // And it tracks a different band height — the placement is derived,
        // not a magic constant: a 64 px band moves the lower point up by 32.
        var taller = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, 64f);
        Assert.Equal(DeskSize.Y - 64f, taller.LowerPoint.Y, precision: 3);
    }

    [Fact]
    public void MaquetteDiamond_lower_point_falls_back_to_the_fraction_with_no_bottom_band()
    {
        // With a zero bottom band the lower point falls back to the
        // documented MaquetteLowerPointScreenFracY fraction — the historical
        // pre-octies behaviour, kept for callers that pass no band.
        var d = DeskClipFrontierLogic.MaquetteDiamond(DeskSize, TopHudBand, 0f);

        Assert.Equal(
            DeskSize.Y * DeskClipFrontierLogic.MaquetteLowerPointScreenFracY,
            d.LowerPoint.Y, precision: 3);
    }

    [Fact]
    public void MaquetteDiamond_half_height_is_derived_from_the_locked_iso_slope()
    {
        // The half-height is NOT a free parameter: it is the half-width
        // times the locked iso slope. This is what guarantees the 0.5 edge
        // slope. Pin the derivation explicitly.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

        float halfW = DeskSize.X * DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX;
        float expectedHalfH = halfW * DeskClipFrontierLogic.IsoEdgeSlope;
        float actualHalfH = d.LowerPoint.Y - d.LeftApex.Y;
        Assert.Equal(expectedHalfH, actualHalfH, precision: 2);
    }

    [Fact]
    public void MaquetteDiamond_lower_point_is_below_the_side_apexes()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

        // +Y is down: the lower point has the largest Y, the top apex the
        // smallest, the side apexes between.
        Assert.True(d.LowerPoint.Y > d.LeftApex.Y,
            "the lower point must be below the side apexes");
        Assert.True(d.LeftApex.Y > d.TopApex.Y,
            "the side apexes must be below the top apex");
        // The top apex of this wide overflowing diamond is cropped
        // above-screen.
        Assert.True(d.TopApex.Y < 0f,
            "the top apex is cropped above the screen / behind the HUD");
    }

    [Fact]
    public void MaquetteDiamond_is_symmetric_about_the_side_apex_line()
    {
        // The diamond is a true rhombus: the top apex mirrors the lower
        // point across the side-apex line, so the top and bottom triangles
        // of the diamond are equal height.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

        float upperHalf = d.LeftApex.Y - d.TopApex.Y;
        float lowerHalf = d.LowerPoint.Y - d.LeftApex.Y;
        Assert.Equal(lowerHalf, upperHalf, precision: 3);
    }

    [Fact]
    public void MaquetteDiamond_is_wider_than_the_screen()
    {
        // The whole point of J3c-1quinquies: the diamond overflows
        // sideways so the maquette stays large WITHOUT a vertical stretch.
        // The full diamond width must exceed the screen width.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

        float fullWidth = d.RightApex.X - d.LeftApex.X;
        Assert.True(fullWidth > DeskSize.X,
            $"the maquette diamond must be wider than the screen so it " +
            $"owns the bulk of it without a vertical stretch; got " +
            $"width={fullWidth}, screen={DeskSize.X}");
    }

    [Fact]
    public void MaquetteDiamond_throws_on_a_non_positive_screen()
    {
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.MaquetteDiamond(
                new SysVec2(0f, 1080f), TopHudBand, BottomHudBand));
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.MaquetteDiamond(
                new SysVec2(1920f, -1f), TopHudBand, BottomHudBand));
    }

    [Fact]
    public void MaquetteDiamond_throws_on_a_negative_hud_band()
    {
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.MaquetteDiamond(
                DeskSize, -1f, BottomHudBand));
        Assert.Throws<System.ArgumentException>(
            () => DeskClipFrontierLogic.MaquetteDiamond(
                DeskSize, TopHudBand, -1f));
    }

    // --- Compute: the twin-corner clip ------------------------------------

    [Fact]
    public void Compute_split_is_the_diamond_lower_point_uv_x()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

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
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);
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
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);
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
    public void Compute_desk_corners_are_thin_so_the_maquette_owns_the_screen()
    {
        // J3c-1quinquies intent check, in UV space: a point at the
        // bottom-CENTRE, just inside the diamond's lower wedge, must be
        // DROPPED. The diamond's lower point reaches the bottom centre, so
        // the desk no longer covers the whole bottom strip; it is confined
        // to the two corner wedges.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);
        var f = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        // Derive the test point from the real geometry: take a UV y a touch
        // above the lower point and an x a touch right of the left desk
        // hypotenuse at that height — i.e. just INSIDE the diamond's lower
        // wedge, not in the thin left desk corner. The diamond is wide
        // (overflowing), so at this height the left edge is very close to
        // the split; the point must still be read as maquette, not desk.
        var leftApexUv = DeskClipFrontierLogic.ScreenToUv(
            d.LeftApex, DeskOrigin, DeskSize);
        var lowerUv = DeskClipFrontierLogic.ScreenToUv(
            d.LowerPoint, DeskOrigin, DeskSize);
        float testY = lowerUv.Y - 0.04f;
        // x of the left hypotenuse at testY (linear interpolation).
        float t = (testY - leftApexUv.Y) / (lowerUv.Y - leftApexUv.Y);
        float edgeX = leftApexUv.X + t * (lowerUv.X - leftApexUv.X);
        // A hair to the diamond side of that edge — inside the lower wedge.
        var bottomCentre = new SysVec2(edgeX + 0.01f, testY);
        Assert.False(KeptByShader(f, bottomCentre),
            "the bottom-centre belongs to the maquette diamond, not the desk");
    }

    [Fact]
    public void Compute_drops_just_above_and_keeps_just_below_the_lower_point()
    {
        // Just ABOVE the diamond's lower point (on the split line) is still
        // inside the diamond -> dropped; just BELOW it, slightly left of
        // the split, is in the left desk corner -> kept.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);
        var f = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        var lowerPointUv = DeskClipFrontierLogic.ScreenToUv(
            d.LowerPoint, DeskOrigin, DeskSize);

        var insideDiamond = new SysVec2(
            lowerPointUv.X, lowerPointUv.Y - 0.03f);
        Assert.False(KeptByShader(f, insideDiamond),
            "just above the diamond lower point is inside the diamond");

        var belowLowerPoint = new SysVec2(
            lowerPointUv.X - 0.02f, lowerPointUv.Y + 0.01f);
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
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);
        var f = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        // x = 0.75 (right of the split) and y = 0.15 (well above the bottom,
        // inside the diamond and NOT in the right corner). If the left
        // half-plane could bleed across the split it would be wrongly kept.
        var rightOfSplitInsideDiamond = new SysVec2(0.75f, 0.15f);
        Assert.False(KeptByShader(f, rightOfSplitInsideDiamond),
            "a point right of the split inside the diamond must be dropped — " +
            "the left half-plane must not bleed across the split");
    }

    [Fact]
    public void Compute_throws_on_a_non_positive_desk_rect()
    {
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);

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

    // --- THE PAWN-OVERFLOW INVARIANT — locks the three-viewport split ----

    [Fact]
    public void A_pawn_standing_at_the_floor_edge_has_its_body_dropped_by_the_floor_clip()
    {
        // THE invariant behind the J3c-1septies three-viewport architecture.
        //
        // A DeskCompanyPawn is ~96-120 px tall: its foot sits on a desk
        // cell, its body and head extend UP from that foot. A pawn whose
        // foot is near the floor edge therefore has its upper body
        // OVERFLOWING above the desk floor triangle, into the maquette
        // diamond zone.
        //
        // This test proves the floor clip shader WOULD discard that upper
        // body — i.e. if the pawns rendered through the clipped FLOOR
        // viewport they would be sliced. That is exactly why the pawns must
        // ride a SEPARATE, UNCLIPPED entities viewport. If a future change
        // ever merges the floor and the pawns back into one clipped
        // viewport, the pawn-clipping bug returns; this test documents and
        // pins the reason the split exists.
        var d = DeskClipFrontierLogic.MaquetteDiamond(
            DeskSize, TopHudBand, BottomHudBand);
        var f = DeskClipFrontierLogic.Compute(d, DeskOrigin, DeskSize);

        // Pick a point ON the left desk-floor hypotenuse — a pawn foot
        // placed right at the floor edge. Derive it from the real geometry:
        // a UV y partway down the left hypotenuse, x exactly on the edge.
        var leftApexUv = DeskClipFrontierLogic.ScreenToUv(
            d.LeftApex, DeskOrigin, DeskSize);
        var lowerUv = DeskClipFrontierLogic.ScreenToUv(
            d.LowerPoint, DeskOrigin, DeskSize);
        float footY = leftApexUv.Y + 0.6f * (lowerUv.Y - leftApexUv.Y);
        float t = (footY - leftApexUv.Y) / (lowerUv.Y - leftApexUv.Y);
        float footEdgeX = leftApexUv.X + t * (lowerUv.X - leftApexUv.X);

        // The foot, a hair INSIDE the desk floor triangle — kept (the floor
        // covers the foot, the pawn does stand on the desk).
        var foot = new SysVec2(footEdgeX - 0.01f, footY);
        Assert.True(KeptByShader(f, foot),
            "the pawn foot stands on the desk floor triangle");

        // The pawn body/head: the SAME screen x, but well ABOVE the foot
        // (smaller UV y — the body extends up). For a ~100 px tall pawn on a
        // 1080 screen that is roughly 0.10 of the screen height up. This
        // upper body is in the maquette diamond zone, NOT on the floor
        // triangle: the floor clip shader DROPS it.
        var bodyTop = new SysVec2(footEdgeX - 0.01f, footY - 0.10f);
        Assert.False(KeptByShader(f, bodyTop),
            "the pawn body overflows above the floor triangle and would be " +
            "clipped by the FLOOR shader — hence the pawns ride a separate " +
            "UNCLIPPED entities viewport (the three-viewport architecture)");
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
