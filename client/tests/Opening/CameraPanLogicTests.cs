using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="CameraPanLogic"/>. Pins the P8.2
/// scroll/pan camera contract on the E2 World Map -- clamp at image
/// edges, diagonal normalisation, image-smaller-than-viewport edge
/// case. Godot-free by design, see <c>Wayfinders.Client.Tests.csproj</c>
/// for the <c>Compile Include</c> line.
///
/// <para>
/// <b>Coverage strategy.</b>
/// <list type="bullet">
///   <item>Clamp invariants on each of the four edges, the four
///         corners, and the no-clamp center case.</item>
///   <item>Image-smaller-than-viewport degenerate case (axis snaps to
///         image center).</item>
///   <item>Diagonal-normalisation invariant (no sqrt(2) speedup on
///         simultaneous keys).</item>
///   <item>Defense-doc: pan with zero direction returns the input
///         center unchanged (no spurious drift on idle frames).</item>
///   <item>Defense-doc: pan with a clamped target snaps to the limit
///         even when the desired target is beyond.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Coordinate convention reminder.</b> Camera center, world coords,
/// origin top-left of the image. Image E2.1 master = 3840x2160,
/// viewport = 1920x1080 ; the valid camera-center rectangle is
/// [960, 2880] x [540, 1620]. Numbers below derive from those.
/// </para>
/// </summary>
public sealed class CameraPanLogicTests
{
    // E2 World Map MVP dimensions (P8.2 D-P8.2-15: viewport figé MVP).
    private static readonly PanVec2 E2ImageSize    = new(3840f, 2160f);
    private static readonly PanVec2 E2ViewportSize = new(1920f, 1080f);

    [Fact]
    public void Clamp_center_of_image_is_unchanged()
    {
        // Sanity: sitting at the geometric center of the image is the
        // canonical "no clamp needed" state. Any movement worth shipping
        // starts from a no-op here.
        var input = new PanVec2(1920f, 1080f);
        var clamped = CameraPanLogic.ClampCameraCenter(input, E2ImageSize, E2ViewportSize);

        Assert.Equal(1920f, clamped.X);
        Assert.Equal(1080f, clamped.Y);
    }

    [Fact]
    public void Clamp_left_of_min_snaps_to_left_limit()
    {
        // Camera-center less than half-viewport.X means the visible rect
        // would peek out of the image on the left -- bord noir. Snap.
        var input = new PanVec2(0f, 1080f);
        var clamped = CameraPanLogic.ClampCameraCenter(input, E2ImageSize, E2ViewportSize);

        Assert.Equal(960f, clamped.X);   // halfViewportX
        Assert.Equal(1080f, clamped.Y);  // y unchanged (already valid)
    }

    [Fact]
    public void Clamp_right_of_max_snaps_to_right_limit()
    {
        var input = new PanVec2(99999f, 1080f);
        var clamped = CameraPanLogic.ClampCameraCenter(input, E2ImageSize, E2ViewportSize);

        Assert.Equal(2880f, clamped.X);  // imageSize.X - halfViewportX = 3840 - 960
        Assert.Equal(1080f, clamped.Y);
    }

    [Fact]
    public void Clamp_above_min_snaps_to_top_limit()
    {
        var input = new PanVec2(1920f, -500f);
        var clamped = CameraPanLogic.ClampCameraCenter(input, E2ImageSize, E2ViewportSize);

        Assert.Equal(1920f, clamped.X);
        Assert.Equal(540f, clamped.Y);   // halfViewportY
    }

    [Fact]
    public void Clamp_below_max_snaps_to_bottom_limit()
    {
        var input = new PanVec2(1920f, 99999f);
        var clamped = CameraPanLogic.ClampCameraCenter(input, E2ImageSize, E2ViewportSize);

        Assert.Equal(1920f, clamped.X);
        Assert.Equal(1620f, clamped.Y);  // imageSize.Y - halfViewportY = 2160 - 540
    }

    [Theory]
    [InlineData(-1000f, -1000f, 960f, 540f)]      // top-left corner
    [InlineData(99999f, -1000f, 2880f, 540f)]     // top-right corner
    [InlineData(-1000f, 99999f, 960f, 1620f)]     // bottom-left corner
    [InlineData(99999f, 99999f, 2880f, 1620f)]    // bottom-right corner
    public void Clamp_corners_clamp_both_axes(
        float inX, float inY, float expectedX, float expectedY)
    {
        // Defense-doc: the four corners must clamp on both axes
        // simultaneously. A bug where only one axis clamps would let the
        // camera pan diagonally beyond the image and surface as a black
        // wedge in the corner.
        var input = new PanVec2(inX, inY);
        var clamped = CameraPanLogic.ClampCameraCenter(input, E2ImageSize, E2ViewportSize);

        Assert.Equal(expectedX, clamped.X);
        Assert.Equal(expectedY, clamped.Y);
    }

    [Fact]
    public void Clamp_image_narrower_than_viewport_snaps_x_to_image_center()
    {
        // Edge case (not expected in MVP but specified -- post-MVP a
        // smaller asset should not corrupt the camera). Image 1000 wide,
        // viewport 1920 wide => no horizontal pan room, snap X to 500.
        var smallImage = new PanVec2(1000f, 4000f);
        var input = new PanVec2(800f, 2000f);
        var clamped = CameraPanLogic.ClampCameraCenter(input, smallImage, E2ViewportSize);

        Assert.Equal(500f, clamped.X);                  // image center X
        Assert.Equal(2000f, clamped.Y);                 // y still pan-able and valid
    }

    [Fact]
    public void Clamp_image_shorter_than_viewport_snaps_y_to_image_center()
    {
        var smallImage = new PanVec2(8000f, 600f);
        var input = new PanVec2(4000f, 999f);
        var clamped = CameraPanLogic.ClampCameraCenter(input, smallImage, E2ViewportSize);

        Assert.Equal(4000f, clamped.X);
        Assert.Equal(300f, clamped.Y);                  // image center Y
    }

    [Fact]
    public void ResolvePanDirection_no_keys_pressed_returns_zero()
    {
        // Idle-frame contract: no input = zero vector = caller can skip
        // the camera write entirely. A spurious non-zero would drift the
        // camera one pixel per frame on an inactive ZQSD.
        var dir = CameraPanLogic.ResolvePanDirection(
            left: false, right: false, up: false, down: false);

        Assert.Equal(PanVec2.Zero, dir);
    }

    [Theory]
    [InlineData(true,  false, false, false, -1f, 0f)]   // left
    [InlineData(false, true,  false, false,  1f, 0f)]   // right
    [InlineData(false, false, true,  false,  0f, -1f)]  // up
    [InlineData(false, false, false, true,   0f,  1f)]  // down
    public void ResolvePanDirection_cardinal_keys_return_unit_axis(
        bool left, bool right, bool up, bool down,
        float expectedX, float expectedY)
    {
        var dir = CameraPanLogic.ResolvePanDirection(left, right, up, down);

        Assert.Equal(expectedX, dir.X);
        Assert.Equal(expectedY, dir.Y);
    }

    [Fact]
    public void ResolvePanDirection_diagonal_normalises_to_unit_length()
    {
        // sqrt(2) trap: pressing right + down naively yields (1, 1) which
        // is sqrt(2) longer than a cardinal press. Normalising to (~0.707,
        // ~0.707) keeps diagonal speed equal to cardinal speed -- the
        // industry-standard scroll-cam feel.
        var dir = CameraPanLogic.ResolvePanDirection(
            left: false, right: true, up: false, down: true);

        var length = System.MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        Assert.Equal(1f, length, precision: 5);
        Assert.Equal(0.7071f, dir.X, precision: 3);
        Assert.Equal(0.7071f, dir.Y, precision: 3);
    }

    [Fact]
    public void ResolvePanDirection_opposite_keys_cancel_to_zero()
    {
        // Defense-doc: holding both left+right with no other key yields
        // zero, not (0,0) by accident -- this is what the early-return on
        // (x==0 && y==0) guarantees so we don't divide-by-zero in the
        // normalise.
        var dir = CameraPanLogic.ResolvePanDirection(
            left: true, right: true, up: false, down: false);

        Assert.Equal(PanVec2.Zero, dir);
    }

    [Fact]
    public void AdvanceCameraCenter_zero_direction_returns_current_center()
    {
        // Idle frame: no key, no motion. Output equals input (after a
        // no-op clamp at a valid position).
        var current = new PanVec2(1820f, 980f);  // Halfgate POI center
        var advanced = CameraPanLogic.AdvanceCameraCenter(
            current, PanVec2.Zero, speedPxPerSec: 800f, deltaSeconds: 1f / 60f,
            E2ImageSize, E2ViewportSize);

        Assert.Equal(current, advanced);
    }

    [Fact]
    public void AdvanceCameraCenter_right_at_default_speed_advances_correctly()
    {
        // 800 px/s * 1/60s = 13.333 px per frame. From Halfgate (1820,980)
        // moving right => (1833.333, 980). Well within bounds, no clamp.
        var current = new PanVec2(1820f, 980f);
        var advanced = CameraPanLogic.AdvanceCameraCenter(
            current, new PanVec2(1f, 0f), speedPxPerSec: 800f, deltaSeconds: 1f / 60f,
            E2ImageSize, E2ViewportSize);

        Assert.Equal(1820f + 800f / 60f, advanced.X, precision: 3);
        Assert.Equal(980f, advanced.Y);
    }

    [Fact]
    public void AdvanceCameraCenter_clamps_at_right_edge()
    {
        // Defense-doc: even with a huge per-frame delta (slow framerate
        // simulation), the clamp must hold. 800 px/s * 1s from x=2500
        // would land at 3300, which is past the right limit 2880 -- snap.
        var current = new PanVec2(2500f, 1080f);
        var advanced = CameraPanLogic.AdvanceCameraCenter(
            current, new PanVec2(1f, 0f), speedPxPerSec: 800f, deltaSeconds: 1f,
            E2ImageSize, E2ViewportSize);

        Assert.Equal(2880f, advanced.X);    // clamped
        Assert.Equal(1080f, advanced.Y);
    }

    [Fact]
    public void HalfgatePoiCenter_is_within_valid_camera_range()
    {
        // Defense-doc P8.2 D-P8.2-08: position initiale = Halfgate POI
        // center (1820, 980 in world_map_pois.tres). It must already be
        // a legal camera position so MakeCurrent() at _Ready does not
        // trigger a visible snap. Halfgate is at x=1820 (vs valid range
        // [960, 2880]) and y=980 (vs [540, 1620]) -- inside.
        var halfgate = new PanVec2(1820f, 980f);
        var clamped = CameraPanLogic.ClampCameraCenter(halfgate, E2ImageSize, E2ViewportSize);

        Assert.Equal(halfgate, clamped);
    }
}
