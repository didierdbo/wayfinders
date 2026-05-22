using System;
using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="WorldMapCalage"/> — the J5 world-metre →
/// render-pixel transform used to pin the Halfgate city marker on the eM
/// world mesh.
///
/// <para>
/// <b>What this suite locks (schema v2, 2026-05-22).</b> The calage sidecars
/// now ship the <i>exact</i> iso world→pixel projection as a 2×3 affine
/// matrix — the v1 flat <c>render_scale</c> is gone (it was mathematically
/// wrong for an iso render and landed the Halfgate marker out in the sea).
/// Each sidecar carries a verified <c>control_point</c>: a known world point
/// and the pixel it projects to. This suite pins both factories against
/// their sidecar's <c>control_point</c> — if a sidecar is regenerated and the
/// factory is not updated in lockstep, the control-point assertion goes red
/// here, long before a city marker lands at the wrong coastline on screen.
/// It also pins the iso cross terms (a flat-scale regression has zero cross
/// terms), the frame corners, and the inverse round-trip.
/// </para>
/// </summary>
public sealed class WorldMapCalageTests
{
    // Tolerance for a projected pixel against the sidecar's verified value.
    // The sidecar quotes the matrix to 8 decimal places; ~1e-2 px absorbs
    // float accumulation while still catching any real drift.
    private const float PixelTol = 1e-2f;

    // ----- eM world mesh: control_point pin -----------------------------
    // wf_e2_world_map_mesh_iso_2048x1024.png.calage.json ->
    // iso_projection.world_to_pixel_projection.control_point
    private const float EmControlWorldX = 250_000f;
    private const float EmControlWorldY = 125_000f;
    private const float EmControlPixelX = 1024f;
    private const float EmControlPixelY = 512f;

    // ----- eT district common frame: control_point pin ------------------
    // wf_e3_district_*_mesh_iso_1600x800.png.calage.json ->
    // iso_projection.world_to_pixel_projection.control_point (identical in
    // BOTH district sidecars — the shared projection).
    private const float DistrictControlWorldX = 92_100f;
    private const float DistrictControlWorldY = 37_950f;
    private const float DistrictControlPixelX = 800f;
    private const float DistrictControlPixelY = 400f;

    [Fact]
    public void Em_control_point_projects_to_the_sidecar_pixel()
    {
        // The sidecar's verified pin: world (250000,125000) m — the frame
        // centre — projects to pixel (1024,512), the PNG centre. This is the
        // direct guard that ForEmWorldMesh() carries the v2 matrix exactly.
        var calage = WorldMapCalage.ForEmWorldMesh();

        var pixel = calage.WorldMetresToRenderPixel(
            new SysVec2(EmControlWorldX, EmControlWorldY));

        Assert.Equal(EmControlPixelX, pixel.X, PixelTol);
        Assert.Equal(EmControlPixelY, pixel.Y, PixelTol);
    }

    [Fact]
    public void Em_factory_carries_the_sidecar_render_size()
    {
        var calage = WorldMapCalage.ForEmWorldMesh();

        Assert.Equal(2048f, calage.RenderWidthPx);
        Assert.Equal(1024f, calage.RenderHeightPx);
    }

    [Fact]
    public void Em_sw_corner_maps_to_the_sidecar_origin_pixel()
    {
        // The world SW reference corner (0,0) projects to origin_px
        // (-2.73022584, 367.11298207). It is slightly negative on X because
        // CAM_ORTHO_SCALE keeps a 10% margin around the plane.
        var calage = WorldMapCalage.ForEmWorldMesh();

        var pixel = calage.WorldMetresToRenderPixel(new SysVec2(0f, 0f));

        Assert.Equal(-2.73022584f, pixel.X, PixelTol);
        Assert.Equal(367.11298207f, pixel.Y, PixelTol);
    }

    [Fact]
    public void Em_projection_has_real_iso_cross_terms()
    {
        // The iso guard. In a true iso projection pixel X depends on world Y
        // (and pixel Y on world X) — the cross terms m01 and m10 are
        // non-zero. A regression back to a flat separable px/m scale has zero
        // cross terms; this assertion catches exactly that.
        var calage = WorldMapCalage.ForEmWorldMesh();

        Assert.NotEqual(0.0, calage.M01);
        Assert.NotEqual(0.0, calage.M10);
    }

    [Fact]
    public void Em_world_north_maps_to_a_smaller_pixel_y()
    {
        // The Y-flip is baked into the matrix (m11 is negative). A point
        // further NORTH (larger world Y) must still map to a SMALLER pixel Y
        // (closer to the top of the PNG). A sign flip on m11 fails this.
        var calage = WorldMapCalage.ForEmWorldMesh();

        var south = calage.WorldMetresToRenderPixel(new SysVec2(0f, 50_000f));
        var north = calage.WorldMetresToRenderPixel(new SysVec2(0f, 200_000f));

        Assert.True(
            north.Y < south.Y,
            $"world-north should map to a smaller pixel Y; "
            + $"north.Y={north.Y} south.Y={south.Y}");
    }

    [Fact]
    public void Em_halfgate_now_projects_onto_land_near_the_render_centre()
    {
        // The fix this whole change exists for. Halfgate's placeholder world
        // position (92000, 38000) m (Varn spec §7, tunable). Under the v1
        // flat scale it projected to (~377, ~868) — low on the PNG, out in
        // the sea. Under the v2 iso matrix it projects to roughly
        // (351.5, 432.2): well inside the 2048×1024 render, in the upper-mid
        // band where the land sits — on the city, not the water.
        var calage = WorldMapCalage.ForEmWorldMesh();

        var pixel = calage.WorldMetresToRenderPixel(
            new SysVec2(92_000f, 38_000f));

        Assert.Equal(351.5239f, pixel.X, 1e-2f);
        Assert.Equal(432.2224f, pixel.Y, 1e-2f);

        // Inside the render bounds — and clearly NOT in the bottom-third
        // sea band the v1 bug placed it in.
        Assert.InRange(pixel.X, 0f, 2048f);
        Assert.InRange(pixel.Y, 0f, 1024f);
        Assert.True(
            pixel.Y < 512f,
            $"Halfgate must land in the upper half (land), not the lower "
            + $"half (sea); pixel.Y={pixel.Y}.");
    }

    [Fact]
    public void Em_round_trip_returns_the_original_world_point()
    {
        // world -> pixel -> world must return the input within tolerance.
        // Guards the inverse matrix maths used by any future eM hit-test.
        var calage = WorldMapCalage.ForEmWorldMesh();
        var world = new SysVec2(92_000f, 38_000f);

        var pixel = calage.WorldMetresToRenderPixel(world);
        var back = calage.RenderPixelToWorldMetres(pixel);

        // World coords here are ~1e5 m; the matrix is tiny (~1e-3 px/m), so
        // a round-trip in float accumulates a few metres. 1 m is well under
        // a render pixel (~380 m/px) and proves the inverse is correct.
        Assert.Equal(world.X, back.X, 1.0f);
        Assert.Equal(world.Y, back.Y, 1.0f);
    }

    [Fact]
    public void District_control_point_projects_to_the_sidecar_pixel()
    {
        // The eT shared pin: world (92100,37950) m — the common frame centre
        // — projects to pixel (800,400), the PNG centre. Both district
        // sidecars carry this identical control point.
        var calage = WorldMapCalage.ForDistrictCommonFrame();

        var pixel = calage.WorldMetresToRenderPixel(
            new SysVec2(DistrictControlWorldX, DistrictControlWorldY));

        Assert.Equal(DistrictControlPixelX, pixel.X, PixelTol);
        Assert.Equal(DistrictControlPixelY, pixel.Y, PixelTol);
    }

    [Fact]
    public void District_common_frame_factory_carries_the_shared_render_size()
    {
        // The puzzle-interlock invariant: BOTH eT district sidecars carry the
        // SAME iso projection and render size (1600×800). The single factory
        // enforces "one calage, used twice" so both districts pose at the
        // same top-left with the same projection.
        var calage = WorldMapCalage.ForDistrictCommonFrame();

        Assert.Equal(1600f, calage.RenderWidthPx);
        Assert.Equal(800f, calage.RenderHeightPx);
    }

    [Fact]
    public void District_projection_has_real_iso_cross_terms()
    {
        // The eT iso guard — same reasoning as the eM cross-term test.
        var calage = WorldMapCalage.ForDistrictCommonFrame();

        Assert.NotEqual(0.0, calage.M01);
        Assert.NotEqual(0.0, calage.M10);
    }

    [Fact]
    public void District_round_trip_returns_the_original_world_point()
    {
        var calage = WorldMapCalage.ForDistrictCommonFrame();
        var world = new SysVec2(DistrictControlWorldX, DistrictControlWorldY);

        var pixel = calage.WorldMetresToRenderPixel(world);
        var back = calage.RenderPixelToWorldMetres(pixel);

        Assert.Equal(world.X, back.X, 1e-2f);
        Assert.Equal(world.Y, back.Y, 1e-2f);
    }

    [Fact]
    public void Constructor_rejects_non_positive_render_size()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorldMapCalage(
                0.0026, 0.0029, -2.73, 0.0013, -0.0014, 367.0,
                renderWidthPx: 2048f, renderHeightPx: -1f));
    }

    [Fact]
    public void Constructor_rejects_a_singular_projection_matrix()
    {
        // A singular 2×2 linear part (det == 0) is not a valid projection —
        // it cannot be inverted and means a corrupt sidecar. Here both rows
        // are colinear (m10,m11 == 2× m00,m01), so det == 0.
        Assert.Throws<ArgumentException>(() =>
            new WorldMapCalage(
                0.0026, 0.0029, -2.73, 0.0052, 0.0058, 367.0,
                renderWidthPx: 2048f, renderHeightPx: 1024f));
    }
}
