namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helper for the P8.2 World Map scroll/pan camera. Hosts the
/// invariants that determine where a Camera2D positioned over a finite
/// 2D image is allowed to sit, given a fixed viewport size. Godot-free
/// by design so xUnit pins the contract -- see
/// <c>Wayfinders.Client.Tests.csproj</c> for the
/// <c>Compile Include</c> line.
///
/// <para>
/// <b>Why a separate helper.</b> Same pattern as
/// <see cref="LadderResolutionLogic"/> at P8.1 and
/// <see cref="PoiDispatchLogic"/> at J3/J4: the runtime delegates the
/// actual clamp to <c>Godot.Camera2D.LimitLeft/Top/Right/Bottom</c>
/// (engine-natif, the right tool for the job at the engine seam), but
/// the contract -- "a camera centered at <c>cameraCenter</c> over an
/// image of <c>imageSize</c> with a viewport of <c>viewportSize</c> must
/// stay within these bounds" -- is small enough to encode here once and
/// be testable from xUnit without the engine. When the post-MVP zoom
/// shows up (Ctrl+wheel, currently rejected master pre-brief P8 §3.2 H-03),
/// the test suite will surface every consumer that depended on the
/// fixed-zoom assumption.
/// </para>
///
/// <para>
/// <b>Coordinate convention.</b> All coordinates are in world-space
/// pixels. The world's top-left corner is <c>(0, 0)</c>. The image
/// occupies the rectangle <c>[0, imageSize.X] x [0, imageSize.Y]</c>.
/// The camera position is the <i>center</i> of the visible viewport in
/// world coords (Godot Camera2D semantics, not the top-left of the
/// view rect).
/// </para>
///
/// <para>
/// <b>Why <see cref="PanVec2"/> instead of <c>Godot.Vector2</c>.</b>
/// <c>Godot.Vector2</c> lives in the engine assembly. Pulling it into
/// the test project would re-couple this helper to GodotSharp and
/// break the Godot-free invariant. <see cref="PanVec2"/> is a tiny
/// readonly record struct with the two fields we need ; the
/// <c>E2WorldMap</c> runtime translates between
/// <c>Godot.Vector2</c> and <see cref="PanVec2"/> at the seam (one-line
/// constructor calls). Same trade-off Coda took on the FastAPI
/// boundary -- never let an engine type leak into a logic seam.
/// </para>
///
/// <para>
/// <b>Edge case: image smaller than viewport.</b> If the source image
/// is smaller than the viewport on either axis, there is nothing to
/// pan along that axis -- the entire image fits. We snap that axis
/// to the image center (so the image looks centered in the viewport
/// with letterbox / pillarbox bands of background color around it).
/// The other axis is clamped normally. This case is not expected for
/// MVP (E2.1 master is 3840x2160, viewport 1920x1080 = always pan-able
/// on both axes), but the helper handles it cleanly so the future
/// E1 / E4 / loading-screens seams that might ship a smaller asset do
/// not surprise.
/// </para>
/// </summary>
public static class CameraPanLogic
{
    /// <summary>
    /// Clamp a desired Camera2D center position so the viewport rectangle
    /// stays entirely inside the image rectangle. Mirrors the runtime
    /// behaviour of <c>Camera2D.LimitLeft/Top/Right/Bottom</c> in Godot 4
    /// (which clamps the camera so the visible rect stays inside the
    /// limits, not so the camera center stays inside).
    /// </summary>
    /// <param name="desiredCenter">
    /// The position the caller wants to set on the camera, in world
    /// coords. Typically the result of "previous center + pan delta"
    /// after a drag or a ZQSD tick.
    /// </param>
    /// <param name="imageSize">World-space dimensions of the source image.</param>
    /// <param name="viewportSize">Viewport (camera visible-rect) dimensions.</param>
    /// <returns>The clamped camera center, ready to assign to <c>Camera2D.Position</c>.</returns>
    public static PanVec2 ClampCameraCenter(
        PanVec2 desiredCenter,
        PanVec2 imageSize,
        PanVec2 viewportSize)
    {
        var halfViewportX = viewportSize.X / 2f;
        var halfViewportY = viewportSize.Y / 2f;

        // Edge case: image narrower than viewport along an axis -- no
        // pan-room on that axis, so the camera snaps to the image center
        // along it. The other axis is clamped normally.
        var clampedX = imageSize.X < viewportSize.X
            ? imageSize.X / 2f
            : Clamp(desiredCenter.X, halfViewportX, imageSize.X - halfViewportX);

        var clampedY = imageSize.Y < viewportSize.Y
            ? imageSize.Y / 2f
            : Clamp(desiredCenter.Y, halfViewportY, imageSize.Y - halfViewportY);

        return new PanVec2(clampedX, clampedY);
    }

    /// <summary>
    /// Resolve a unit-direction pan input vector from the four
    /// keyboard pan flags. Used by the runtime <c>_Process(delta)</c>
    /// loop to compute the per-frame movement delta. Returns the zero
    /// vector when no key is pressed -- the caller decides whether to
    /// skip the camera update entirely or apply a zero delta.
    ///
    /// <para>
    /// Diagonal motion is normalised so the player does not scroll
    /// faster on diagonals (sqrt(2) bug). Same pattern XCOM, Battle
    /// Brothers, every isometric scroll-cam ships -- diagonal at the
    /// same speed as cardinal feels right, raw 2x speed feels broken.
    /// </para>
    /// </summary>
    /// <param name="left">True while <c>ui_pan_left</c> is held.</param>
    /// <param name="right">True while <c>ui_pan_right</c> is held.</param>
    /// <param name="up">True while <c>ui_pan_up</c> is held.</param>
    /// <param name="down">True while <c>ui_pan_down</c> is held.</param>
    /// <returns>A unit-length vector along the requested direction, or zero.</returns>
    public static PanVec2 ResolvePanDirection(bool left, bool right, bool up, bool down)
    {
        var x = 0f;
        var y = 0f;
        if (left)  x -= 1f;
        if (right) x += 1f;
        if (up)    y -= 1f;
        if (down)  y += 1f;

        if (x == 0f && y == 0f) return PanVec2.Zero;

        // Normalise so diagonals don't scroll sqrt(2) faster.
        var length = MathFSqrt(x * x + y * y);
        return new PanVec2(x / length, y / length);
    }

    /// <summary>
    /// Compute the camera center for the next frame given the current
    /// center, the unit-direction pan input, the speed in world pixels
    /// per second, and the frame delta. Output is clamped via
    /// <see cref="ClampCameraCenter"/>.
    /// </summary>
    /// <param name="currentCenter">Camera center this frame, world coords.</param>
    /// <param name="direction">Unit-direction from <see cref="ResolvePanDirection"/>.</param>
    /// <param name="speedPxPerSec">Pan speed in world pixels per second (P8.2 default 800).</param>
    /// <param name="deltaSeconds">Frame delta as seconds (Godot's <c>_Process</c> argument).</param>
    /// <param name="imageSize">World-space dimensions of the image.</param>
    /// <param name="viewportSize">Viewport dimensions.</param>
    /// <returns>The clamped next-frame camera center.</returns>
    public static PanVec2 AdvanceCameraCenter(
        PanVec2 currentCenter,
        PanVec2 direction,
        float speedPxPerSec,
        float deltaSeconds,
        PanVec2 imageSize,
        PanVec2 viewportSize)
    {
        var moveX = direction.X * speedPxPerSec * deltaSeconds;
        var moveY = direction.Y * speedPxPerSec * deltaSeconds;
        var desired = new PanVec2(currentCenter.X + moveX, currentCenter.Y + moveY);
        return ClampCameraCenter(desired, imageSize, viewportSize);
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    // Local sqrt impl so the pure-logic helper does not pull in the
    // System.Math dependency at the seam. Swapped to MathF.Sqrt at the
    // call site below; abstracted for readability.
    private static float MathFSqrt(float v) => System.MathF.Sqrt(v);
}

/// <summary>
/// Tiny 2D float vector. Godot-free stand-in for <c>Godot.Vector2</c>
/// at the pure-logic seam. The runtime <c>E2WorldMap.cs</c> converts
/// between <c>Godot.Vector2</c> and this type at the boundary.
///
/// <para>
/// <b>Why not <c>System.Numerics.Vector2</c>.</b> Pulling
/// <c>System.Numerics</c> in adds a System.Numerics.Vectors reference
/// that the rest of the pure-logic seam doesn't use, and the SIMD
/// machinery is overkill for a 2-float record we manipulate at most
/// 60 times per second. Local record struct keeps the seam minimal.
/// </para>
/// </summary>
/// <param name="X">Horizontal component, world pixels.</param>
/// <param name="Y">Vertical component, world pixels.</param>
public readonly record struct PanVec2(float X, float Y)
{
    /// <summary>The zero vector. Returned by <see cref="CameraPanLogic.ResolvePanDirection"/> when no key is held.</summary>
    public static PanVec2 Zero => new(0f, 0f);
}
