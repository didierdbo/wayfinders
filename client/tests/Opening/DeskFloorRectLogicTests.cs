using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="DeskFloorRectLogic"/> — the J3c-1bis F6
/// fix #1 desk-floor fill rectangle. Pins the contract that the desk floor
/// covers the <i>whole</i> desk viewport (an axis-aligned rect under the
/// immobile desk camera) rather than the compact iso diamond bounding the
/// 8×8 grid — so the only edge the player sees on the desk floor is the
/// triangular clip shader's diagonal hypotenuse, never a parasitic diamond
/// edge sitting inside the screen.
///
/// <para>
/// <b>What this suite owns.</b> The viewport-covering rect maths
/// (<c>cameraCentre ± viewportSize/2</c>, grown by the safety slack), the
/// loud failure on a non-positive viewport, and the clockwise corner
/// order of the polygon fill. The Godot-bound wiring — feeding the desk
/// camera + viewport size in and handing the rect to
/// <c>IsoBoard.SetDeskFloorFillRect</c> — lives in
/// <c>GameScreen.ConfigureDesk</c> and is validated via the F6 smoke.
/// </para>
/// </summary>
public sealed class DeskFloorRectLogicTests
{
    // A representative desk setup: the full 1920x1080 screen viewport,
    // matching GameScreen.tscn's full-screen DeskViewport, and a camera
    // parked somewhere in the lower-left framing quadrant.
    private static readonly SysVec2 ViewportSize = new(1920f, 1080f);
    private static readonly SysVec2 CameraCentre = new(640f, 420f);

    [Fact]
    public void Compute_rect_is_centred_on_the_camera()
    {
        var rect = DeskFloorRectLogic.Compute(CameraCentre, ViewportSize);

        // The rect centre is topLeft + size/2 — it must equal the camera.
        var centre = rect.TopLeft + rect.Size * 0.5f;
        Assert.Equal(CameraCentre.X, centre.X, precision: 3);
        Assert.Equal(CameraCentre.Y, centre.Y, precision: 3);
    }

    [Fact]
    public void Compute_rect_covers_the_whole_viewport_plus_slack()
    {
        var rect = DeskFloorRectLogic.Compute(CameraCentre, ViewportSize);

        // The rect is the viewport grown by the slack on every side, so
        // size = viewport + 2*slack on each axis.
        float expectedW = ViewportSize.X + 2f * DeskFloorRectLogic.EdgeSlackPx;
        float expectedH = ViewportSize.Y + 2f * DeskFloorRectLogic.EdgeSlackPx;
        Assert.Equal(expectedW, rect.Size.X, precision: 3);
        Assert.Equal(expectedH, rect.Size.Y, precision: 3);
    }

    [Fact]
    public void Compute_rect_strictly_contains_the_visible_viewport()
    {
        var rect = DeskFloorRectLogic.Compute(CameraCentre, ViewportSize);

        // The visible viewport rect is cameraCentre ± viewportSize/2. The
        // floor rect must contain it with the slack to spare on every side
        // — the whole point of the slack: never expose an unpainted sliver.
        var visibleTopLeft = CameraCentre - ViewportSize * 0.5f;
        var visibleBottomRight = CameraCentre + ViewportSize * 0.5f;

        Assert.True(rect.TopLeft.X < visibleTopLeft.X,
            "floor rect left must be outside the visible viewport left");
        Assert.True(rect.TopLeft.Y < visibleTopLeft.Y,
            "floor rect top must be outside the visible viewport top");
        Assert.True(rect.TopLeft.X + rect.Size.X > visibleBottomRight.X,
            "floor rect right must be outside the visible viewport right");
        Assert.True(rect.TopLeft.Y + rect.Size.Y > visibleBottomRight.Y,
            "floor rect bottom must be outside the visible viewport bottom");
    }

    [Theory]
    [InlineData(0f, 1080f)]
    [InlineData(1920f, 0f)]
    [InlineData(-1f, 1080f)]
    [InlineData(1920f, -5f)]
    public void Compute_throws_on_a_non_positive_viewport(float w, float h)
    {
        // A non-positive viewport dimension is a caller bug — fail loud,
        // not a silent zero-sized floor that would draw nothing.
        Assert.Throws<System.ArgumentException>(
            () => DeskFloorRectLogic.Compute(CameraCentre, new SysVec2(w, h)));
    }

    [Fact]
    public void Corners_are_the_four_rect_corners_clockwise_from_top_left()
    {
        var rect = DeskFloorRectLogic.Compute(CameraCentre, ViewportSize);

        var corners = DeskFloorRectLogic.Corners(rect);

        Assert.Equal(4, corners.Length);
        // Clockwise from top-left: TL, TR, BR, BL.
        Assert.Equal(rect.TopLeft, corners[0]);
        Assert.Equal(rect.TopLeft + new SysVec2(rect.Size.X, 0f), corners[1]);
        Assert.Equal(rect.TopLeft + rect.Size, corners[2]);
        Assert.Equal(rect.TopLeft + new SysVec2(0f, rect.Size.Y), corners[3]);
    }

    [Fact]
    public void Corners_wind_clockwise_in_a_y_down_space()
    {
        // The desk board's local pixel space is +Y down. A clockwise
        // polygon in +Y-down has a positive signed (shoelace) area; pin it
        // so a future re-order of Corners cannot silently flip the winding
        // and make DrawColoredPolygon cull the fill.
        var rect = DeskFloorRectLogic.Compute(CameraCentre, ViewportSize);
        var c = DeskFloorRectLogic.Corners(rect);

        float signedArea = 0f;
        for (int i = 0; i < c.Length; i++)
        {
            var a = c[i];
            var b = c[(i + 1) % c.Length];
            signedArea += a.X * b.Y - b.X * a.Y;
        }
        Assert.True(signedArea > 0f,
            $"corners must wind clockwise in +Y-down space; got area={signedArea}");
    }
}
