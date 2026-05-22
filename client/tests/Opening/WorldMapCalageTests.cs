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
/// <b>What this suite locks.</b> Three contracts the F6 smoke depends on :
/// the derived px/m scale matches Mira's calage sidecar
/// (<see cref="Em_render_scale_matches_the_calage_sidecar"/>) ; the Y axis
/// is flipped (world-north → pixel-top —
/// <see cref="World_north_maps_to_pixel_top"/>) ; and the two eM/eT factory
/// frames carry exactly the sidecar numbers. If a sidecar is regenerated at
/// a different size and the factory is not updated in lockstep, the scale
/// assertion goes red here — long before a city marker lands at the wrong
/// coastline on screen.
/// </para>
/// </summary>
public sealed class WorldMapCalageTests
{
    // The eM world mesh sidecar's render_scale (px per world metre).
    // wf_e2_world_map_mesh_iso_2048x1024.png.calage.json -> render_scale.
    private const float EmRenderPxPerMetre = 0.004096f;

    [Fact]
    public void Em_render_scale_matches_the_calage_sidecar()
    {
        var calage = WorldMapCalage.ForEmWorldMesh();

        // 2048 px / 500_000 m = 0.004096 ; 1024 / 250_000 = 0.004096.
        Assert.Equal(EmRenderPxPerMetre, calage.RenderPxPerMetreX, precision: 6);
        Assert.Equal(EmRenderPxPerMetre, calage.RenderPxPerMetreY, precision: 6);
    }

    [Fact]
    public void Em_factory_carries_the_sidecar_world_frame_and_render_size()
    {
        var calage = WorldMapCalage.ForEmWorldMesh();

        Assert.Equal(500_000f, calage.FrameSizeXMetres);
        Assert.Equal(250_000f, calage.FrameSizeYMetres);
        Assert.Equal(0f, calage.FrameSwAnchorXMetres);
        Assert.Equal(0f, calage.FrameSwAnchorYMetres);
        Assert.Equal(2048f, calage.RenderWidthPx);
        Assert.Equal(1024f, calage.RenderHeightPx);
    }

    [Fact]
    public void World_origin_maps_to_the_bottom_left_pixel()
    {
        var calage = WorldMapCalage.ForEmWorldMesh();

        // World SW origin (0,0) -> pixel (0, renderHeight): pixel X=0, and
        // pixel Y is the BOTTOM of the render because world-north grows up
        // while pixel-Y grows down.
        var pixel = calage.WorldMetresToRenderPixel(new SysVec2(0f, 0f));

        Assert.Equal(0f, pixel.X, precision: 3);
        Assert.Equal(1024f, pixel.Y, precision: 3);
    }

    [Fact]
    public void World_north_east_corner_maps_to_the_top_right_pixel()
    {
        var calage = WorldMapCalage.ForEmWorldMesh();

        // The NE world corner (max_x, max_y) -> top-right pixel
        // (renderWidth, 0).
        var pixel = calage.WorldMetresToRenderPixel(
            new SysVec2(500_000f, 250_000f));

        Assert.Equal(2048f, pixel.X, precision: 3);
        Assert.Equal(0f, pixel.Y, precision: 3);
    }

    [Fact]
    public void World_north_maps_to_pixel_top()
    {
        var calage = WorldMapCalage.ForEmWorldMesh();

        // The Y-flip guard: a point further NORTH (larger world Y) must map
        // to a SMALLER pixel Y (closer to the top of the PNG). A "forgot the
        // flip" regression makes this assertion fail.
        var south = calage.WorldMetresToRenderPixel(new SysVec2(0f, 50_000f));
        var north = calage.WorldMetresToRenderPixel(new SysVec2(0f, 200_000f));

        Assert.True(
            north.Y < south.Y,
            $"world-north should map to a smaller pixel Y; "
            + $"north.Y={north.Y} south.Y={south.Y}");
    }

    [Fact]
    public void Halfgate_placeholder_position_maps_to_a_pixel_inside_the_em_render()
    {
        var calage = WorldMapCalage.ForEmWorldMesh();

        // Halfgate's placeholder world position from world.yaml /
        // /api/world (Varn spec §7 — tunable until the coastline render is
        // signed off): (92000, 38000) m.
        var pixel = calage.WorldMetresToRenderPixel(new SysVec2(92_000f, 38_000f));

        // px = 92000 * 0.004096 = 376.832 ; py = 1024 - 38000 * 0.004096
        //    = 1024 - 155.648 = 868.352.
        Assert.Equal(376.832f, pixel.X, precision: 2);
        Assert.Equal(868.352f, pixel.Y, precision: 2);

        // And it lands inside the 2048x1024 render.
        Assert.InRange(pixel.X, 0f, 2048f);
        Assert.InRange(pixel.Y, 0f, 1024f);
    }

    [Fact]
    public void District_common_frame_factory_carries_the_shared_sidecar_frame()
    {
        // The puzzle-interlock invariant: BOTH eT district sidecars carry the
        // SAME world frame (400x500 m at SW 91900,37700) and render size
        // (1600x800). The single factory enforces "one calage, used twice"
        // so both districts pose at the same top-left.
        var calage = WorldMapCalage.ForDistrictCommonFrame();

        Assert.Equal(400f, calage.FrameSizeXMetres);
        Assert.Equal(500f, calage.FrameSizeYMetres);
        Assert.Equal(91_900f, calage.FrameSwAnchorXMetres);
        Assert.Equal(37_700f, calage.FrameSwAnchorYMetres);
        Assert.Equal(1600f, calage.RenderWidthPx);
        Assert.Equal(800f, calage.RenderHeightPx);
    }

    [Fact]
    public void District_common_frame_render_scale_is_anisotropic_per_the_sidecar()
    {
        // The eT sidecars carry 4.0 px/m on X and 1.6 px/m on Y — the iso
        // 2:1 squash. The transform must keep the two scales separate.
        var calage = WorldMapCalage.ForDistrictCommonFrame();

        Assert.Equal(4.0f, calage.RenderPxPerMetreX, precision: 4);
        Assert.Equal(1.6f, calage.RenderPxPerMetreY, precision: 4);
        Assert.NotEqual(calage.RenderPxPerMetreX, calage.RenderPxPerMetreY);
    }

    [Fact]
    public void Constructor_rejects_non_positive_frame_size()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorldMapCalage(0f, 250_000f, 0f, 0f, 2048f, 1024f));
    }

    [Fact]
    public void Constructor_rejects_non_positive_render_size()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorldMapCalage(500_000f, 250_000f, 0f, 0f, 2048f, -1f));
    }
}
