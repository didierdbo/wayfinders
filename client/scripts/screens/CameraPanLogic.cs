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

    /// <summary>
    /// Clamp a desired Camera2D center so the viewport rectangle stays
    /// entirely inside an iso 2:1 painted diamond centered on the origin.
    /// Pure-C# counterpart to <see cref="ClampCameraCenter"/> for the iso
    /// case (E1 IsoMapE1Probe, future tactical scenes).
    ///
    /// <para>
    /// <b>Position-projection variant (legacy).</b> When the desired
    /// center is outside the allowed diamond, projects (cx, cy) back onto
    /// the boundary along the ray from origin. This preserves the camera's
    /// "intended direction of motion" from the origin's perspective, which
    /// sounds good but produces a parasitic glide along the boundary when
    /// the user pushes against an edge -- bug C 2026-05-13 evening. The
    /// projection direction depends on the camera's POSITION relative to
    /// origin, not on the user's input DELTA, so holding a key (or
    /// continuing a drag) past the boundary makes the camera slide in a
    /// direction the user did not request.
    /// </para>
    ///
    /// <para>
    /// <b>Why kept.</b> Pinned as the reference algorithm for the corner
    /// constraint (worst-case viewport corner inside the painted diamond)
    /// and as a fallback if a future scene wants the smooth-glide feel.
    /// The runtime now uses <see cref="ClampCameraCenterToIsoDiamondHardWall"/>
    /// which caps the DELTA instead of projecting the POSITION.
    /// </para>
    ///
    /// <para>
    /// <b>Why a diamond clamp, not the rectangular one.</b> A 64x64 iso
    /// 2:1 grid paints a DIAMOND, not a rectangle : tile centers occupy
    /// <c>(gx - gy) * IsoWStride</c> on X (range
    /// <c>[-(N-1)*IsoWStride, +(N-1)*IsoWStride]</c>) and
    /// <c>(gx + gy) * IsoHStride - centerOffset</c> on Y (range
    /// <c>[-(N-1)*IsoHStride, +(N-1)*IsoHStride]</c>). The painted region
    /// is the locus of points satisfying
    /// <c>|x|/halfDiamondW + |y|/halfDiamondH &lt;= 1</c>. Clamping the
    /// camera to the diamond's BOUNDING RECTANGLE (as
    /// <c>Camera2D.LimitLeft/Right/Top/Bottom</c> would do) reveals grey
    /// background bands in the four diamond CORNERS the moment the
    /// camera approaches the rect edge.
    /// </para>
    ///
    /// <para>
    /// <b>Algorithm.</b> Given the painted diamond with half-extents
    /// <c>(halfDiamondW, halfDiamondH)</c> centered on the origin, and a
    /// viewport of size <c>viewportSize</c>, the camera center
    /// <c>(cx, cy)</c> is allowed iff every corner of the viewport rect
    /// <c>(cx +/- vw/2, cy +/- vh/2)</c> satisfies
    /// <c>|x|/halfDiamondW + |y|/halfDiamondH &lt;= 1</c>. The
    /// worst-case corner is the one in the same quadrant as <c>(cx, cy)</c>,
    /// at <c>(|cx| + vw/2, |cy| + vh/2)</c>. If that point exceeds the
    /// diamond, we project the camera center back onto the largest
    /// inscribed diamond that still keeps every viewport corner inside.
    /// </para>
    ///
    /// <para>
    /// <b>Zoom-aware via viewportSize.</b> The caller passes the
    /// EFFECTIVE viewport in world pixels (raw viewport divided by zoom
    /// for <c>Camera2D.Zoom &lt; 1</c>, multiplied for zoom &gt; 1). At
    /// zoom 0.31x with a 1920x1080 raw viewport, pass
    /// <c>(6194, 3484)</c>. This contract matches
    /// <see cref="ClampCameraCenter"/>'s "viewportSize means
    /// world-space visible rect dimensions".
    /// </para>
    ///
    /// <para>
    /// <b>Edge case: viewport larger than diamond on both axes.</b> If
    /// <c>vw/2 &gt;= halfDiamondW</c> AND <c>vh/2 &gt;= halfDiamondH</c>,
    /// no camera position can fit the viewport inside the diamond. We
    /// snap to <c>(0, 0)</c> so the diamond is centered and grey bands
    /// appear symmetrically (least-bad failure mode, surfaces visually
    /// that the zoom-out is too aggressive). The runtime should raise
    /// <c>ZoomMin</c> if this triggers in production.
    /// </para>
    /// </summary>
    /// <param name="desiredCenter">Desired camera center, world coords, origin at diamond center.</param>
    /// <param name="halfDiamondW">Half-width of the painted diamond (e.g., 8192 for a 64x64 grid with IsoWStride=128 plus half-tile margin).</param>
    /// <param name="halfDiamondH">Half-height of the painted diamond (e.g., 4098 for the same grid with IsoHStride=64 plus half-tile margin).</param>
    /// <param name="viewportSize">Effective viewport in world pixels (raw viewport divided by zoom).</param>
    /// <returns>The clamped camera center, ready to assign to <c>Camera2D.Position</c>.</returns>
    public static PanVec2 ClampCameraCenterToIsoDiamond(
        PanVec2 desiredCenter,
        float halfDiamondW,
        float halfDiamondH,
        PanVec2 viewportSize)
    {
        var halfVw = viewportSize.X / 2f;
        var halfVh = viewportSize.Y / 2f;

        // Degenerate case : viewport bigger than diamond on both axes,
        // no valid camera position exists. Snap to center and let the
        // grey bands surface the over-zoom-out.
        if (halfVw >= halfDiamondW && halfVh >= halfDiamondH)
        {
            return PanVec2.Zero;
        }

        // The viewport's worst-case corner sits at distance
        // (|cx| + halfVw, |cy| + halfVh) from origin. To keep that
        // corner inside the painted diamond, we need :
        //   (|cx| + halfVw) / halfDiamondW + (|cy| + halfVh) / halfDiamondH <= 1
        //
        // Rewriting :
        //   |cx| / halfDiamondW + |cy| / halfDiamondH <= 1 - halfVw/halfDiamondW - halfVh/halfDiamondH
        //
        // Let k = 1 - halfVw/halfDiamondW - halfVh/halfDiamondH. If k <= 0
        // the viewport corners are too big to fit (degenerate, handled above
        // for the both-axes case ; single-axis is handled by snapping that
        // axis to 0 and letting the other axis use its full inner range).
        var k = 1f - halfVw / halfDiamondW - halfVh / halfDiamondH;
        if (k <= 0f)
        {
            // One axis is too big alone -- snap that axis to 0 and clamp
            // the other axis to its remaining range. Same "least-bad"
            // discipline as the both-axes case.
            var fallbackX = halfVw >= halfDiamondW
                ? 0f
                : Clamp(desiredCenter.X, -(halfDiamondW - halfVw), halfDiamondW - halfVw);
            var fallbackY = halfVh >= halfDiamondH
                ? 0f
                : Clamp(desiredCenter.Y, -(halfDiamondH - halfVh), halfDiamondH - halfVh);
            return new PanVec2(fallbackX, fallbackY);
        }

        // Compute the normalised "diamond distance" of the desired center.
        // If <= k, the corner constraint is satisfied as-is.
        var absX = desiredCenter.X < 0f ? -desiredCenter.X : desiredCenter.X;
        var absY = desiredCenter.Y < 0f ? -desiredCenter.Y : desiredCenter.Y;
        var d = absX / halfDiamondW + absY / halfDiamondH;

        if (d <= k)
        {
            return desiredCenter;
        }

        // Outside the allowed diamond : project onto the boundary by
        // scaling (cx, cy) by k/d. This is the projection onto the
        // L1-shaped "allowed center" diamond along the ray from origin,
        // which preserves the camera's intended direction of motion.
        var scale = k / d;
        return new PanVec2(desiredCenter.X * scale, desiredCenter.Y * scale);
    }

    /// <summary>
    /// Hard-wall variant of <see cref="ClampCameraCenterToIsoDiamond"/> :
    /// caps the MOVEMENT DELTA instead of projecting the desired POSITION
    /// onto the allowed-center diamond boundary. If the desired center is
    /// outside the allowed diamond, the camera stays put at
    /// <paramref name="currentCenter"/> -- "mur dur" feel. If the desired
    /// center is inside, it is returned as-is.
    ///
    /// <para>
    /// <b>Why this exists.</b> The position-projection variant (the legacy
    /// <see cref="ClampCameraCenterToIsoDiamond"/>) projects (cx, cy) along
    /// the ray from origin. When the user pushes against the boundary --
    /// holding a key or continuing a mouse drag past the edge -- the
    /// projection slides the camera along the boundary in a direction that
    /// depends on the camera's POSITION (its angle from origin), not on the
    /// user's input DELTA. The visible effect : "ca force / change de sens",
    /// a parasitic glide along the diamond edge with the input direction
    /// becoming irrelevant. Bug C 2026-05-13 evening, both ZQSD and
    /// mouse-drag paths.
    /// </para>
    ///
    /// <para>
    /// <b>Algorithm.</b> Same corner-constraint as the projection variant
    /// (compute <c>k</c>, then <c>d</c> for the desired center). If
    /// <c>d &lt;= k</c> the desired center is inside the allowed diamond
    /// and is returned. Otherwise we return <paramref name="currentCenter"/>
    /// unchanged -- the entire requested delta is dropped. This gives the
    /// "mur" feel : predictable, no surprise direction change, the user's
    /// intent is honored when it can be and refused cleanly when it cannot.
    /// </para>
    ///
    /// <para>
    /// <b>Trade-off accepted.</b> When the camera is already at the
    /// boundary and the user requests a DIAGONAL move with one component
    /// "along the wall" and one "into the wall", the entire move is
    /// dropped -- including the along-wall component. Felt as rude but
    /// is predictable. Owner preference 2026-05-13 evening : "sensation
    /// mur dur, moins glissant et plus previsible que la projection L1".
    /// If a future scene needs the smoother per-axis sliding feel
    /// (cancel only the perpendicular component, keep the parallel one),
    /// the algorithm to add would project the DELTA onto the diamond
    /// edge tangent at the current contact point ; that's a different
    /// function, not a parameter on this one.
    /// </para>
    ///
    /// <para>
    /// <b>Degenerate cases.</b> Same as the projection variant -- viewport
    /// bigger than diamond on both axes snaps to (0, 0) ; single-axis
    /// overflow snaps that axis to 0 and clamps the other to its remaining
    /// range. These cases reset the camera regardless of currentCenter
    /// because no valid pan exists ; the "hard wall" semantics only apply
    /// to the normal in-bounds geometry.
    /// </para>
    /// </summary>
    /// <param name="currentCenter">Camera center this frame. Returned unchanged when desiredCenter is outside the allowed diamond.</param>
    /// <param name="desiredCenter">Requested next center = currentCenter + frame delta. May be outside the diamond.</param>
    /// <param name="halfDiamondW">Half-width of the painted diamond.</param>
    /// <param name="halfDiamondH">Half-height of the painted diamond.</param>
    /// <param name="viewportSize">Effective viewport in world pixels (raw viewport divided by zoom).</param>
    /// <returns>Either <paramref name="desiredCenter"/> (if inside) or <paramref name="currentCenter"/> (if outside) ; degenerate cases snap to the same fallback as <see cref="ClampCameraCenterToIsoDiamond"/>.</returns>
    public static PanVec2 ClampCameraCenterToIsoDiamondHardWall(
        PanVec2 currentCenter,
        PanVec2 desiredCenter,
        float halfDiamondW,
        float halfDiamondH,
        PanVec2 viewportSize)
    {
        var halfVw = viewportSize.X / 2f;
        var halfVh = viewportSize.Y / 2f;

        // Degenerate case : viewport bigger than diamond on both axes.
        // No valid camera position exists -- the "hard wall" notion is
        // moot, fall back to centering. Mirrors the projection variant.
        if (halfVw >= halfDiamondW && halfVh >= halfDiamondH)
        {
            return PanVec2.Zero;
        }

        var k = 1f - halfVw / halfDiamondW - halfVh / halfDiamondH;
        if (k <= 0f)
        {
            // Single-axis overflow : snap that axis, clamp the other.
            // Apply to the DESIRED center (not currentCenter) so the
            // caller's other-axis input is still honored within range.
            var fallbackX = halfVw >= halfDiamondW
                ? 0f
                : Clamp(desiredCenter.X, -(halfDiamondW - halfVw), halfDiamondW - halfVw);
            var fallbackY = halfVh >= halfDiamondH
                ? 0f
                : Clamp(desiredCenter.Y, -(halfDiamondH - halfVh), halfDiamondH - halfVh);
            return new PanVec2(fallbackX, fallbackY);
        }

        // Normal case : check whether the desired center keeps the worst-
        // case viewport corner inside the painted diamond. Compute the
        // L1-style diamond distance, compare against k.
        var absX = desiredCenter.X < 0f ? -desiredCenter.X : desiredCenter.X;
        var absY = desiredCenter.Y < 0f ? -desiredCenter.Y : desiredCenter.Y;
        var d = absX / halfDiamondW + absY / halfDiamondH;

        // Inside the allowed diamond : honor the requested center.
        // Outside : hard wall, the camera does not move this frame.
        return d <= k ? desiredCenter : currentCenter;
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
