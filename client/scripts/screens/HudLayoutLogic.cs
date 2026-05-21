namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# layout maths for the J3b Game Screen Shell HUD frame: given the
/// full screen size and the two fixed HUD band heights (top + bottom),
/// computes the <b>residual central rectangle</b> the map maquette
/// viewport must occupy so it is clipped strictly <i>behind</i> the bands.
///
/// <para>
/// <b>Why this is its own helper.</b> J3b adds two pieces to
/// <see cref="Wayfinders.Client.Scenes.Screens.GameScreen"/>: a
/// <c>CanvasLayer</c> overlay (pure scene-tree wiring, no maths) and the
/// re-sizing of the <c>MapViewportContainer</c> so the maquette no longer
/// bleeds under the HUD. That second piece <i>is</i> arithmetic — top-left
/// drops by the top band, height shrinks by both bands — and arithmetic
/// off in a <c>.tscn</c> by a few pixels is invisible until a playtest.
/// Pinning it in xUnit makes a wrong offset surface red, the same
/// logic-vs-node split as <see cref="MapViewportPanLogic"/> (J3a) and
/// <c>IsoProjection</c> (J3-iso).
/// </para>
///
/// <para>
/// <b>What the HUD bands themselves do NOT need.</b> The two
/// <c>Panel</c>s sit on a <c>CanvasLayer</c>; a <c>CanvasLayer</c> has its
/// own canvas transform that ignores the SubViewport's <c>Camera2D</c>, so
/// the HUD is fixed for free — no maths, no per-frame work. This helper is
/// only about the <i>map viewport</i> footprint, which is a plain
/// <c>Control</c> child and must be told the residual rect explicitly.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Inputs and outputs are primitive
/// <see cref="float"/>s wrapped in <see cref="HudRect"/>; the runtime
/// <c>GameScreen</c> converts to <c>Godot.Vector2</c> at the seam. xUnit
/// pins the residual-rect contract with no engine — see
/// <c>Wayfinders.Client.Tests.csproj</c> for the <c>Compile Include</c>.
/// </para>
/// </summary>
public static class HudLayoutLogic
{
    /// <summary>
    /// Compute the residual central rectangle for the map maquette
    /// viewport: the full screen minus a fixed band off the top and a
    /// fixed band off the bottom. The left and right edges are unchanged
    /// (the HUD bands span the full width).
    ///
    /// <para>
    /// Degenerate inputs are clamped, not rejected: negative band heights
    /// are treated as zero, and if the two bands together exceed the
    /// screen height the residual collapses to a zero-height strip at the
    /// top-band baseline rather than producing a negative height. A
    /// zero/negative residual is a design error the preflight log will
    /// surface — the helper stays total so a bad inspector value cannot
    /// throw at <c>_Ready</c>.
    /// </para>
    /// </summary>
    /// <param name="screenWidth">Full Game Screen width, pixels.</param>
    /// <param name="screenHeight">Full Game Screen height, pixels.</param>
    /// <param name="topBandHeight">Fixed height of the top HUD band, pixels.</param>
    /// <param name="bottomBandHeight">Fixed height of the bottom HUD band, pixels.</param>
    /// <returns>
    /// The residual rectangle the <c>MapViewportContainer</c> should be
    /// sized and positioned to, so the maquette clips exactly between the
    /// two bands and never bleeds under the HUD.
    /// </returns>
    public static HudRect ResidualMapRect(
        float screenWidth,
        float screenHeight,
        float topBandHeight,
        float bottomBandHeight)
    {
        var top = Max0(topBandHeight);
        var bottom = Max0(bottomBandHeight);
        var width = Max0(screenWidth);
        var height = Max0(screenHeight);

        // The residual height is the screen minus both bands. If the bands
        // overlap (sum exceeds the screen) clamp to zero rather than going
        // negative — a Control with a negative size is undefined.
        var residualHeight = Max0(height - top - bottom);

        return new HudRect(
            X: 0f,
            Y: top,
            Width: width,
            Height: residualHeight);
    }

    /// <summary>
    /// True when the two HUD bands leave a usable (strictly positive)
    /// central rectangle. The runtime preflight asserts this and logs a
    /// loud warning otherwise — a collapsed residual means the maquette
    /// would be invisible, a design error worth catching at <c>_Ready</c>
    /// rather than at a playtest.
    /// </summary>
    public static bool HasUsableResidual(
        float screenWidth,
        float screenHeight,
        float topBandHeight,
        float bottomBandHeight)
    {
        var rect = ResidualMapRect(screenWidth, screenHeight, topBandHeight, bottomBandHeight);
        return rect.Width > 0f && rect.Height > 0f;
    }

    private static float Max0(float v) => v > 0f ? v : 0f;
}

/// <summary>
/// A plain axis-aligned rectangle in screen pixels, Godot-free. The
/// runtime translates this into a <c>Control</c>'s offsets / size at the
/// <see cref="Wayfinders.Client.Scenes.Screens.GameScreen"/> seam.
/// </summary>
/// <param name="X">Left edge, pixels from screen origin.</param>
/// <param name="Y">Top edge, pixels from screen origin.</param>
/// <param name="Width">Rectangle width, pixels.</param>
/// <param name="Height">Rectangle height, pixels.</param>
public readonly record struct HudRect(float X, float Y, float Width, float Height);
