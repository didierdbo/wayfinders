using System;
using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin tests for <see cref="MapViewportInputRoutingLogic"/> — the
/// screen → maquette-world router that backs the Étape A mission POI
/// click pipeline (Rune, 2026-05-23).
///
/// <para>
/// What this file pins :
/// <list type="number">
///   <item>The affine round-trip — a click at the container centre maps
///         exactly to the camera centre ; the same click recovered through
///         the inverse formula gives back the original screen pixel.</item>
///   <item>The container-offset strip — a click at the container's
///         top-left maps to <c>cameraCentre - containerSize/2 / zoom</c>
///         (the HUD-bands case).</item>
///   <item>The pannable-camera path — the same screen pixel maps to two
///         different world points when the <c>MapCamera2D</c> has moved
///         (verifies the camera centre is the live frame at click
///         time).</item>
///   <item>The zoom path — a non-1 zoom scales the world offset
///         correctly (the maquette doesn't ship zoom in J3a but the
///         contract has to survive a future zoom).</item>
///   <item>The IsInsideContainer guard — inclusive top-left, exclusive
///         bottom-right (Godot Rect2.HasPoint convention).</item>
///   <item>The validation surface — non-positive zoom or container size
///         throws <see cref="ArgumentOutOfRangeException"/>, not a
///         silent NaN.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MapViewportInputRoutingLogicTests
{
    // The Game Screen Shell shape J3a/J3b lock : 1920x1080 screen, HUD top
    // band = 32 px, MapRecessDepthPx = 8 px (so container.top = 40), HUD
    // bottom band = 32 px, container.bottom = 1048.
    private static readonly SysVec2 ContainerOffset = new(0f, 40f);
    private static readonly SysVec2 ContainerSize = new(1920f, 1008f);
    private static readonly SysVec2 ContainerCentreScreen =
        ContainerOffset + ContainerSize * 0.5f;
    private static readonly SysVec2 UnitZoom = new(1f, 1f);

    [Fact]
    public void ScreenToMaquetteWorld_centre_click_maps_to_camera_centre()
    {
        var cameraCentre = new SysVec2(1024f, 512f); // eM mesh centre, J5 lock
        var worldPos = MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
            ContainerCentreScreen, ContainerOffset, ContainerSize, cameraCentre, UnitZoom);
        Assert.Equal(cameraCentre.X, worldPos.X, 4);
        Assert.Equal(cameraCentre.Y, worldPos.Y, 4);
    }

    [Fact]
    public void ScreenToMaquetteWorld_container_topleft_maps_below_camera_by_half_container()
    {
        // The container's top-left in screen pixels.
        var cameraCentre = new SysVec2(1024f, 512f);
        var worldPos = MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
            ContainerOffset, ContainerOffset, ContainerSize, cameraCentre, UnitZoom);
        // top-left of the container <=> -containerSize/2 from the centre.
        var expected = cameraCentre - ContainerSize * 0.5f;
        Assert.Equal(expected.X, worldPos.X, 4);
        Assert.Equal(expected.Y, worldPos.Y, 4);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(960f, 540f)]
    [InlineData(1920f, 1080f)]
    public void ScreenToMaquetteWorld_round_trips_through_inverse(float sx, float sy)
    {
        var screenPos = new SysVec2(sx, sy);
        var cameraCentre = new SysVec2(800f, 400f);
        var world = MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
            screenPos, ContainerOffset, ContainerSize, cameraCentre, UnitZoom);

        // The inverse :
        //   screenPos = containerOffset + containerSize/2 +
        //               (world - cameraCentre) * zoom
        var roundTrip = ContainerOffset + ContainerSize * 0.5f
                      + (world - cameraCentre);
        Assert.Equal(screenPos.X, roundTrip.X, 4);
        Assert.Equal(screenPos.Y, roundTrip.Y, 4);
    }

    [Fact]
    public void ScreenToMaquetteWorld_follows_the_camera_when_panned()
    {
        // Same screen pixel, two different camera positions => two
        // different world points — the camera-pan invariant.
        var screenPos = ContainerCentreScreen;
        var origin = MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
            screenPos, ContainerOffset, ContainerSize,
            new SysVec2(1024f, 512f), UnitZoom);
        var panned = MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
            screenPos, ContainerOffset, ContainerSize,
            new SysVec2(1200f, 700f), UnitZoom);
        Assert.NotEqual(origin.X, panned.X);
        Assert.NotEqual(origin.Y, panned.Y);
        Assert.Equal(1024f, origin.X);
        Assert.Equal(1200f, panned.X);
    }

    [Fact]
    public void ScreenToMaquetteWorld_scales_offset_by_zoom()
    {
        // With zoom > 1 the same screen offset maps to a smaller world
        // offset — the standard Camera2D semantics.
        var cameraCentre = new SysVec2(1024f, 512f);
        var screenPos = ContainerCentreScreen + new SysVec2(100f, 0f);
        var w1 = MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
            screenPos, ContainerOffset, ContainerSize, cameraCentre, new SysVec2(1f, 1f));
        var w2 = MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
            screenPos, ContainerOffset, ContainerSize, cameraCentre, new SysVec2(2f, 2f));
        Assert.Equal(1124f, w1.X, 4);
        Assert.Equal(1074f, w2.X, 4); // 100 / 2 = 50 px world offset
    }

    // --- IsInsideContainer --------------------------------------------

    [Theory]
    [InlineData(0f, 40f, true)]         // inclusive top-left
    [InlineData(1919.999f, 1047f, true)] // just inside bottom-right
    [InlineData(1920f, 40f, false)]      // exclusive right
    [InlineData(0f, 1048f, false)]       // exclusive bottom
    [InlineData(-1f, 540f, false)]       // outside left
    [InlineData(960f, 20f, false)]       // above (HUD top band)
    [InlineData(960f, 1060f, false)]     // below (HUD bottom band)
    [InlineData(960f, 540f, true)]       // dead centre
    public void IsInsideContainer_inclusive_top_left_exclusive_bottom_right(
        float sx, float sy, bool expected)
    {
        Assert.Equal(expected, MapViewportInputRoutingLogic.IsInsideContainer(
            new SysVec2(sx, sy), ContainerOffset, ContainerSize));
    }

    // --- Validation -----------------------------------------------------

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 1f)]
    [InlineData(1f, -1f)]
    public void ScreenToMaquetteWorld_throws_on_non_positive_zoom(float zx, float zy)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
                new SysVec2(0, 0), ContainerOffset, ContainerSize,
                new SysVec2(0, 0), new SysVec2(zx, zy)));
    }

    [Theory]
    [InlineData(0f, 100f)]
    [InlineData(100f, 0f)]
    [InlineData(-1f, 100f)]
    public void ScreenToMaquetteWorld_throws_on_non_positive_container_size(float cx, float cy)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MapViewportInputRoutingLogic.ScreenToMaquetteWorld(
                new SysVec2(0, 0), ContainerOffset, new SysVec2(cx, cy),
                new SysVec2(0, 0), UnitZoom));
    }
}
