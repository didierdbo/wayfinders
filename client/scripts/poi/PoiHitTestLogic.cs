using System.Collections;

namespace Wayfinders.Client.Scripts.Poi;

/// <summary>
/// PR4 — Pure-C# bitmap hit-test for POI pixel-perfect input.
///
/// <para>
/// <b>Why pure.</b> The PoiInputRouter autoload owns the Godot input cascade
/// (mouse position, viewport, Y-sort iteration, SetInputAsHandled). The
/// pixel-level decision — "is this local pixel opaque enough to count as a
/// hit?" — has no Godot dependency once you have the <see cref="BitArray"/>
/// and its row stride. We isolate it here so xUnit pins the contract without
/// spinning a Godot runtime. Same separation pattern as
/// <c>PoiDispatchLogic</c> + <c>PoiDefinition</c>, <c>NpcCatalog</c> +
/// <c>NpcRegistry</c>, <c>MissionPanelLogic</c> + <c>MissionPanelProbe</c>.
/// </para>
///
/// <para>
/// <b>The stride trap.</b> <c>PoiFootprintBuilder</c> samples the source
/// image with <c>stride=2</c> for footprint discovery — only pixels at even
/// (x,y) coordinates ever set a bit in <see cref="PoiData.AlphaMask"/>. If
/// the cursor lands on an odd-parity pixel that is visually opaque, a naive
/// indexed read returns <c>false</c> and the POI registers a phantom miss.
/// Fix : snap the local pixel down to the previous even coordinate before
/// indexing. This is a deliberate MVP coupling to the builder's stride ; if
/// the stride changes, this helper must too — see
/// <see cref="SamplingStride"/>.
/// </para>
///
/// <para>
/// <b>Coordinate frame.</b> <c>localPixel</c> is texture-space — origin at
/// the texture's (0,0) pixel, X right, Y down. The router converts the
/// global cursor coordinate to local pixel before calling. See
/// <c>PoiInputRouter.HitTest</c> for the conversion (Centered=false,
/// Offset=-AnchorPixel).
/// </para>
/// </summary>
public static class PoiHitTestLogic
{
    /// <summary>
    /// Sampling stride used by <c>PoiFootprintBuilder.Build</c>. Mirror
    /// this value if the builder's stride is changed. Locked at 2 in PR2.
    /// </summary>
    public const int SamplingStride = 2;

    /// <summary>
    /// True if <paramref name="localPixel"/> falls on an opaque sample in
    /// <paramref name="alphaMask"/>. Returns false on out-of-bounds, null
    /// mask, or transparent sample.
    /// </summary>
    /// <param name="alphaMask">
    /// Row-major bit array of length <c>maskWidth * maskHeight</c>. A set
    /// bit at <c>(x, y)</c> means the source image was opaque enough (above
    /// the alpha threshold) at that pixel.
    /// </param>
    /// <param name="maskWidth">Width of the original texture in pixels.</param>
    /// <param name="maskHeight">Height of the original texture in pixels.</param>
    /// <param name="localPixel">
    /// Cursor position in texture-space coordinates (origin top-left of the
    /// texture). The router computes this from the global mouse position
    /// using the POI's <c>GlobalPosition</c>, <c>Offset</c>, and the texture
    /// anchor.
    /// </param>
    public static bool HitTestPoiPure(
        BitArray? alphaMask,
        int maskWidth,
        int maskHeight,
        (int X, int Y) localPixel)
    {
        if (alphaMask == null) return false;
        if (maskWidth <= 0 || maskHeight <= 0) return false;

        var (x, y) = localPixel;

        // Out of bounds — caller should normally have filtered via AABB, but
        // we defend in depth (the cursor sits on the AABB edge by one pixel,
        // float rounding, etc.).
        if (x < 0 || y < 0 || x >= maskWidth || y >= maskHeight) return false;

        // Snap to the sampling grid. The builder only wrote bits at
        // multiples of SamplingStride ; reading an odd-parity coordinate
        // would yield a false miss on a visually opaque pixel. We snap
        // *down* so a cursor at (3,3) reads the (2,2) sample — adequate
        // for MVP. A higher-fidelity variant would test the 2x2 cell
        // (4 reads) and OR them ; not worth the cycles for PR4.
        var xSnapped = (x / SamplingStride) * SamplingStride;
        var ySnapped = (y / SamplingStride) * SamplingStride;

        // Recheck bounds after snap (no-op for valid inputs, defensive
        // when maskWidth is not a multiple of SamplingStride).
        if (xSnapped < 0 || ySnapped < 0
            || xSnapped >= maskWidth || ySnapped >= maskHeight)
        {
            return false;
        }

        var index = ySnapped * maskWidth + xSnapped;
        if (index < 0 || index >= alphaMask.Length) return false;

        return alphaMask[index];
    }
}
