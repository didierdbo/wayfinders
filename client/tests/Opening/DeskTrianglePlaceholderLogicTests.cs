using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskTrianglePlaceholderLogic"/> — the CPU
/// draw-time desk-floor carve, calibrated against the shipped Mira
/// back-decor alpha hole (Rune, 2026-05-23 rebuild).
///
/// <para>
/// <b>What this suite owns.</b> The two wedges <b>match decor A's
/// alpha-hole geometry</b> (V apex at the centre-bottom, V shoulders on the
/// side edges) — they tile the alpha hole edge-to-edge so the desk wood
/// fully covers what decor A leaves transparent, with no central gap and
/// no transparent sliver along the V edge. The V edge is pulled UP by
/// <see cref="DeskTrianglePlaceholderLogic.VEdgeOverlapPx"/> so the wedges
/// over-extend UNDER decor D's lip (D is drawn after C and covers the
/// overlap) — that is what closes the thin C↔D seam where the maquette
/// used to leak through. The screen centre (well above the V apex) is
/// still left UNpainted so the maquette diamond shows through. The
/// point-in-wedge convex-polygon test is winding-agnostic. The
/// Godot-bound wiring — feeding the wedges into <c>IsoBoard._Draw</c> so
/// the floor bitmap + grid are skipped outside them — is validated via
/// the F6 smoke.
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
    public void Both_wedges_anchor_at_their_screen_bottom_corner()
    {
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float left = OriginX;
        float right = OriginX + ViewportSize.X;
        float bottom = OriginY + ViewportSize.Y;

        Assert.Equal(new SysVec2(left, bottom), wedges.Left.BottomCorner);
        Assert.Equal(new SysVec2(right, bottom), wedges.Right.BottomCorner);
    }

    [Fact]
    public void The_two_wedges_meet_along_the_screen_centre_edge()
    {
        // The new alpha-aligned carve : the two wedges tile decor A's alpha
        // hole edge-to-edge along the screen centre, sharing both the
        // bottom-centre point and the inner-apex point. Without this, the
        // F6 desk floor had a central gap where the layer-B SubViewport
        // showed through (bug observed 2026-05-23, the cause of the
        // "maquette qui déborde" report).
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        Assert.Equal(wedges.Left.BottomCentre, wedges.Right.BottomCentre);
        Assert.Equal(wedges.Left.InnerApex, wedges.Right.InnerApex);
    }

    [Fact]
    public void The_two_wedges_are_mirror_symmetric_about_the_viewport_centre()
    {
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float centreX = OriginX + ViewportSize.X * 0.5f;

        // Each left-wedge vertex mirrored across centreX must equal the
        // corresponding right-wedge vertex (BottomCentre and InnerApex are
        // their own mirrors).
        Assert.Equal(
            2f * centreX - wedges.Left.BottomCorner.X,
            wedges.Right.BottomCorner.X, precision: 3);
        Assert.Equal(
            wedges.Left.BottomCorner.Y,
            wedges.Right.BottomCorner.Y, precision: 3);
        Assert.Equal(
            2f * centreX - wedges.Left.SideShoulder.X,
            wedges.Right.SideShoulder.X, precision: 3);
        Assert.Equal(
            wedges.Left.SideShoulder.Y,
            wedges.Right.SideShoulder.Y, precision: 3);
    }

    [Fact]
    public void The_inner_apex_height_is_decor_A_apex_pulled_up_by_the_V_overlap()
    {
        // Decor A's V apex sits InnerApexFractionFromBottom of the viewport
        // height above the bottom — pixel-exact against the measured alpha.
        // The wedge's InnerApex is then pulled UP by VEdgeOverlapPx so the
        // wedge over-extends UNDER decor D's lip and no C↔D sliver remains.
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float bottom = OriginY + ViewportSize.Y;
        float expectedApexY =
            bottom - ViewportSize.Y
              * DeskTrianglePlaceholderLogic.InnerApexFractionFromBottom
              - DeskTrianglePlaceholderLogic.VEdgeOverlapPx;

        Assert.Equal(expectedApexY, wedges.Left.InnerApex.Y, precision: 3);
        Assert.Equal(expectedApexY, wedges.Right.InnerApex.Y, precision: 3);
    }

    [Fact]
    public void The_side_shoulder_height_is_decor_A_shoulder_pulled_up_by_the_V_overlap()
    {
        // Decor A's V shoulder sits SideShoulderFractionFromBottom of the
        // viewport height above the bottom — pixel-exact against the
        // measured alpha (at x=0 the last opaque row of decor A is y=580 on
        // a 1080-tall image, hence 500/1080 ≈ 0.4630). The wedge's
        // SideShoulder is then pulled UP by VEdgeOverlapPx for the same
        // C↔D-seam reason as InnerApex.
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float bottom = OriginY + ViewportSize.Y;
        float expectedShoulderY =
            bottom - ViewportSize.Y
              * DeskTrianglePlaceholderLogic.SideShoulderFractionFromBottom
              - DeskTrianglePlaceholderLogic.VEdgeOverlapPx;

        Assert.Equal(expectedShoulderY, wedges.Left.SideShoulder.Y, precision: 3);
        Assert.Equal(expectedShoulderY, wedges.Right.SideShoulder.Y, precision: 3);
    }

    [Fact]
    public void The_V_edge_overlap_preserves_the_V_slope()
    {
        // The whole point of pulling InnerApex AND SideShoulder up by the
        // same VEdgeOverlapPx : the V edge stays parallel to decor A's V
        // edge, so the wedge's V boundary still runs edge-to-edge with A's
        // V (just shifted up). If a future refactor shifted one vertex but
        // not the other, the wedge's V slope would diverge from A's V slope
        // and a sliver would re-open lower down where they used to coincide.
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float bottom = OriginY + ViewportSize.Y;
        float apexDelta = bottom
            - ViewportSize.Y * DeskTrianglePlaceholderLogic.InnerApexFractionFromBottom
            - wedges.Left.InnerApex.Y;
        float shoulderDelta = bottom
            - ViewportSize.Y * DeskTrianglePlaceholderLogic.SideShoulderFractionFromBottom
            - wedges.Left.SideShoulder.Y;

        Assert.Equal(DeskTrianglePlaceholderLogic.VEdgeOverlapPx, apexDelta, precision: 3);
        Assert.Equal(DeskTrianglePlaceholderLogic.VEdgeOverlapPx, shoulderDelta, precision: 3);
        Assert.Equal(apexDelta, shoulderDelta, precision: 3);
    }

    [Fact]
    public void V_edge_overlap_is_positive_so_the_wedge_actually_over_extends()
    {
        // Pinning the contract : the overlap is strictly positive — a zero
        // or negative value would mean the wedge stops AT or BELOW decor
        // A's V boundary, re-opening the C↔D sliver this constant was
        // introduced to close.
        Assert.True(DeskTrianglePlaceholderLogic.VEdgeOverlapPx > 0f,
            "VEdgeOverlapPx must be strictly positive ; a non-positive " +
            "value re-opens the C↔D-lip sliver.");
    }

    [Fact]
    public void The_two_wedges_cover_the_whole_bottom_edge()
    {
        // The old placeholder used BottomEdgeFraction=0.38 — the two wedges
        // covered only 76% of the bottom edge, leaving a 24% central gap
        // where the layer-B SubViewport showed through. The alpha-aligned
        // wedges meet at the screen centre, so a point a hair above the
        // bottom edge at ANY x is inside one of the two wedges.
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float left = OriginX;
        float right = OriginX + ViewportSize.X;
        float bottom = OriginY + ViewportSize.Y;

        // Sample every 10% of the bottom edge.
        for (int i = 0; i <= 10; i++)
        {
            float t = i / 10f;
            float x = left + (right - left) * t;
            var probe = new SysVec2(x, bottom - 2f);
            Assert.True(
                DeskTrianglePlaceholderLogic.Contains(wedges, probe),
                $"a point near the bottom edge at fraction {t:0.0} " +
                $"must lie inside the carved wedges (the old central gap " +
                $"is what produced the 'maquette qui déborde' F6 bug).");
        }
    }

    [Fact]
    public void Neither_wedge_climbs_above_the_viewport_mid_line()
    {
        // The whole point of the carve : the desk wedges stay in the bottom
        // half so they never overpaint the maquette zone. The V-edge
        // overlap is small (a handful of pixels) so the shoulder vertex
        // (~500 px above the bottom on the reference 1080-tall viewport)
        // still sits comfortably below the mid-line (540 px above the
        // bottom) even after being pulled up by VEdgeOverlapPx.
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float midY = OriginY + ViewportSize.Y * 0.5f;

        foreach (var wedge in new[] { wedges.Left, wedges.Right })
        {
            foreach (var v in DeskTrianglePlaceholderLogic.Vertices(wedge))
            {
                Assert.True(v.Y >= midY,
                    $"wedge vertex {v} climbs above the mid-line {midY}");
            }
        }
    }

    [Fact]
    public void The_screen_centre_is_left_unpainted()
    {
        // The maquette diamond must show through the screen centre — the
        // centre point (well above the wedge inner-apex) must be in
        // NEITHER wedge.
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var centre = new SysVec2(
            OriginX + ViewportSize.X * 0.5f,
            OriginY + ViewportSize.Y * 0.5f);

        Assert.False(DeskTrianglePlaceholderLogic.Contains(wedges, centre),
            "the screen centre must be unpainted so the maquette shows through");
    }

    [Fact]
    public void A_point_deep_in_each_bottom_corner_is_inside_its_wedge()
    {
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        float left = OriginX;
        float right = OriginX + ViewportSize.X;
        float bottom = OriginY + ViewportSize.Y;

        // A point a small step in from each bottom corner.
        var nearBottomLeft = new SysVec2(left + 20f, bottom - 20f);
        var nearBottomRight = new SysVec2(right - 20f, bottom - 20f);

        Assert.True(DeskTrianglePlaceholderLogic.Contains(wedges, nearBottomLeft));
        Assert.True(DeskTrianglePlaceholderLogic.Contains(wedges, nearBottomRight));
    }

    [Fact]
    public void A_point_above_the_screen_top_is_outside_both_wedges()
    {
        var wedges = DeskTrianglePlaceholderLogic.Build(
            ViewportSize, OriginX, OriginY);

        var nearTop = new SysVec2(
            OriginX + ViewportSize.X * 0.5f, OriginY + 40f);

        Assert.False(DeskTrianglePlaceholderLogic.Contains(wedges, nearTop));
    }

    [Fact]
    public void PointInWedge_is_winding_agnostic()
    {
        // Same convex quad, vertices reversed — the inside test must agree.
        var cw = new DeskTrianglePlaceholderLogic.Wedge(
            BottomCorner: new SysVec2(0f, 10f),
            BottomCentre: new SysVec2(10f, 10f),
            InnerApex: new SysVec2(10f, 0f),
            SideShoulder: new SysVec2(0f, 0f));
        var ccw = new DeskTrianglePlaceholderLogic.Wedge(
            BottomCorner: new SysVec2(0f, 0f),
            BottomCentre: new SysVec2(10f, 0f),
            InnerApex: new SysVec2(10f, 10f),
            SideShoulder: new SysVec2(0f, 10f));

        var inside = new SysVec2(2f, 2f);
        var outside = new SysVec2(20f, 20f);

        Assert.True(DeskTrianglePlaceholderLogic.PointInWedge(cw, inside));
        Assert.True(DeskTrianglePlaceholderLogic.PointInWedge(ccw, inside));
        Assert.False(DeskTrianglePlaceholderLogic.PointInWedge(cw, outside));
        Assert.False(DeskTrianglePlaceholderLogic.PointInWedge(ccw, outside));
    }

    [Fact]
    public void A_point_on_an_edge_counts_as_inside()
    {
        var wedge = new DeskTrianglePlaceholderLogic.Wedge(
            BottomCorner: new SysVec2(0f, 10f),
            BottomCentre: new SysVec2(10f, 10f),
            InnerApex: new SysVec2(10f, 0f),
            SideShoulder: new SysVec2(0f, 0f));

        // Midpoint of the BottomCorner → BottomCentre edge.
        Assert.True(DeskTrianglePlaceholderLogic.PointInWedge(
            wedge, new SysVec2(5f, 10f)));
    }

    [Fact]
    public void Vertices_returns_four_wedge_corners_in_clockwise_order()
    {
        var wedge = new DeskTrianglePlaceholderLogic.Wedge(
            BottomCorner: new SysVec2(1f, 2f),
            BottomCentre: new SysVec2(3f, 4f),
            InnerApex: new SysVec2(5f, 6f),
            SideShoulder: new SysVec2(7f, 8f));

        var v = DeskTrianglePlaceholderLogic.Vertices(wedge);

        Assert.Equal(4, v.Length);
        Assert.Equal(wedge.BottomCorner, v[0]);
        Assert.Equal(wedge.BottomCentre, v[1]);
        Assert.Equal(wedge.InnerApex, v[2]);
        Assert.Equal(wedge.SideShoulder, v[3]);
    }

    [Fact]
    public void Calibration_constants_match_the_measured_decor_A_alpha()
    {
        // Pinning the fractions so a future refactor cannot silently drift
        // them away from the measured Mira back-pass alpha geometry. If
        // Mira ships a re-rendered decor with a different V apex / V
        // shoulder, update BOTH the alpha analysis comment and these
        // pinned values — they must move together.
        Assert.Equal(
            20f / 1080f,
            DeskTrianglePlaceholderLogic.InnerApexFractionFromBottom,
            precision: 6);
        Assert.Equal(
            500f / 1080f,
            DeskTrianglePlaceholderLogic.SideShoulderFractionFromBottom,
            precision: 6);
    }
}
