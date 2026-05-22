using System.Numerics;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskInputRoutingLogic"/> — the J3c-2
/// screen → desk-world coordinate routing.
///
/// <para>
/// <b>What this suite owns.</b> The single affine map a desk click goes
/// through: <c>world = cameraCentre + (screenPos - screenSize/2) / zoom</c>.
/// It pins the centre identity (a click at the screen centre lands on the
/// camera position), the zoom scaling (a zoom &lt; 1 enlarges the
/// desk-world span a screen offset covers), the inverse round-trip
/// against the camera-park maths <c>GameScreen.ConfigureDesk</c> uses,
/// and the misconfigured-zoom guard. The Godot-bound routing
/// (<c>GameScreen._UnhandledInput</c> reading the real
/// <c>InputEventMouseButton.Position</c> and the live desk camera) is
/// validated via the <c>GameScreen.tscn</c> F6 smoke + the
/// <c>[GameScreen] preflight: J3c-2 routing</c> round-trip block.
/// </para>
/// </summary>
public sealed class DeskInputRoutingLogicTests
{
    [Fact]
    public void A_click_at_screen_centre_lands_on_the_camera_centre()
    {
        // The camera Position is, by definition, the world point shown at
        // the screen centre — so a click there routes to it whatever the
        // zoom.
        var screen = new Vector2(1920f, 1080f);
        var cameraCentre = new Vector2(400f, 250f);

        var world = DeskInputRoutingLogic.ScreenToDeskWorld(
            screenPos: screen * 0.5f,
            screenSize: screen,
            deskCameraCentre: cameraCentre,
            deskCameraZoom: new Vector2(0.85f, 0.85f));

        Assert.Equal(cameraCentre.X, world.X, 3);
        Assert.Equal(cameraCentre.Y, world.Y, 3);
    }

    [Fact]
    public void A_zoom_below_one_enlarges_the_world_span_of_a_screen_offset()
    {
        // Camera2D Zoom < 1 zooms OUT: a fixed screen offset from centre
        // covers a LARGER desk-world distance. At zoom 0.5 a 100 px screen
        // offset is a 200 px desk-world offset.
        var screen = new Vector2(1000f, 1000f);
        var cameraCentre = Vector2.Zero;
        var zoom = new Vector2(0.5f, 0.5f);

        var world = DeskInputRoutingLogic.ScreenToDeskWorld(
            screenPos: new Vector2(600f, 500f), // +100 px on X from centre
            screenSize: screen,
            deskCameraCentre: cameraCentre,
            deskCameraZoom: zoom);

        Assert.Equal(200f, world.X, 3); // 100 / 0.5
        Assert.Equal(0f, world.Y, 3);   // centred on Y
    }

    [Fact]
    public void A_zoom_above_one_shrinks_the_world_span_of_a_screen_offset()
    {
        var screen = new Vector2(1000f, 1000f);
        var zoom = new Vector2(2f, 2f);

        var world = DeskInputRoutingLogic.ScreenToDeskWorld(
            screenPos: new Vector2(500f, 700f), // +200 px on Y from centre
            screenSize: screen,
            deskCameraCentre: Vector2.Zero,
            deskCameraZoom: zoom);

        Assert.Equal(0f, world.X, 3);
        Assert.Equal(100f, world.Y, 3); // 200 / 2
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.20f, 0.85f)]
    [InlineData(0.55f, 0.10f)]
    [InlineData(1f, 1f)]
    public void Routing_is_the_exact_inverse_of_the_camera_park_maths(
        float screenFracX, float screenFracY)
    {
        // The camera-park maths in GameScreen.ConfigureDesk places a
        // desk-world point at screen fraction `frac`:
        //   screenPos = worldOffset * zoom + screenSize/2
        //   where worldOffset = worldPoint - cameraCentre.
        // The routing must invert that exactly — else a click selects the
        // wrong cell. Pin the round-trip.
        var screen = new Vector2(1920f, 1080f);
        var cameraCentre = new Vector2(733f, -128f);
        var zoom = new Vector2(0.85f, 0.85f);

        // A desk-world point chosen so it lands at `frac` of the screen.
        var screenPos = new Vector2(
            screen.X * screenFracX, screen.Y * screenFracY);

        var world = DeskInputRoutingLogic.ScreenToDeskWorld(
            screenPos, screen, cameraCentre, zoom);

        // Forward (the GameScreen camera-park direction) must return the
        // original screen pixel.
        var worldOffset = world - cameraCentre;
        var backToScreen = new Vector2(
            worldOffset.X * zoom.X + screen.X * 0.5f,
            worldOffset.Y * zoom.Y + screen.Y * 0.5f);

        Assert.Equal(screenPos.X, backToScreen.X, 2);
        Assert.Equal(screenPos.Y, backToScreen.Y, 2);
    }

    [Fact]
    public void Anisotropic_zoom_scales_each_axis_independently()
    {
        // The desk cameras use an isotropic zoom, but the formula must not
        // silently assume zoom.X == zoom.Y — pin per-axis scaling.
        var world = DeskInputRoutingLogic.ScreenToDeskWorld(
            screenPos: new Vector2(700f, 800f), // +200,+300 from centre
            screenSize: new Vector2(1000f, 1000f),
            deskCameraCentre: Vector2.Zero,
            deskCameraZoom: new Vector2(2f, 0.5f));

        Assert.Equal(100f, world.X, 3); // 200 / 2
        Assert.Equal(600f, world.Y, 3); // 300 / 0.5
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 1f)]
    [InlineData(0f, 0f)]
    public void A_non_positive_zoom_is_rejected(float zoomX, float zoomY)
    {
        // A zero or negative zoom is a misconfigured camera, not a
        // routable click — fail loud rather than divide by zero / flip the
        // axis silently.
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => DeskInputRoutingLogic.ScreenToDeskWorld(
                screenPos: new Vector2(100f, 100f),
                screenSize: new Vector2(1000f, 1000f),
                deskCameraCentre: Vector2.Zero,
                deskCameraZoom: new Vector2(zoomX, zoomY)));
    }
}
