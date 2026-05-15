using System.Numerics;

namespace Wayfinders.Client.Scripts.Poi;

/// <summary>
/// PR5 — Pure-C# decision helpers for the POI "pion sur plateau" visual
/// ingredients. Godot-free by design : the four ingredients (shadow, lift,
/// rim, parallax) have one piece of math each that can be pinned without
/// spinning a Godot runtime — the rest (Sprite2D Shadow node, ShaderMaterial
/// setup, parent attach) lives in <see cref="Poi"/> / <see cref="PoiSpawner"/>.
///
/// <para>
/// <b>What is here vs what is in Poi.cs.</b> The lift clamp and parallax
/// offset are stateless pure functions of their inputs ; they belong here
/// so xUnit pins the bounds and the sign. The shader uniform set, the
/// Sprite2D Shadow attach, and the per-frame <c>_Process</c> camera read
/// stay in Poi.cs because they touch Godot types.
/// </para>
///
/// <para>
/// <b>Locked memo (project_wayfinders_pion_sur_plateau).</b>
/// <list type="bullet">
///   <item><see cref="MinLiftPx"/> = 0 (sticker — locked anti-pattern but
///         allowed for A/B at probe).</item>
///   <item>Locked target = 2 (posed). 3 = jouet (toy-feel slippage edge).</item>
///   <item><see cref="MaxLiftPx"/> = 3. Anything &gt; 3 is the "toy" anti-pattern
///         and the clamp defends.</item>
///   <item><see cref="MinParallaxStrength"/> = 0.0 (off). Allowed at probe.</item>
///   <item>Locked target = 0.04 (~4% — within 3-5% window).</item>
///   <item><see cref="MaxParallaxStrength"/> = 0.05 (5%, upper bound).</item>
/// </list>
/// </para>
/// </summary>
public static class PoiVisualLogic
{
    /// <summary>Locked minimum lift (sticker — A/B-only at probe).</summary>
    public const int MinLiftPx = 0;

    /// <summary>Locked maximum lift. 3 = toy-feel edge ; &gt; 3 forbidden.</summary>
    public const int MaxLiftPx = 3;

    /// <summary>Locked minimum parallax strength.</summary>
    public const float MinParallaxStrength = 0.0f;

    /// <summary>Locked maximum parallax strength (5% — upper window edge).</summary>
    public const float MaxParallaxStrength = 0.05f;

    /// <summary>
    /// Clamp the lift to the locked [<see cref="MinLiftPx"/>,
    /// <see cref="MaxLiftPx"/>] window. The Poi calls this at <c>_Ready</c> on
    /// the <c>LiftPx</c> export so a slipped inspector value (someone types 5
    /// in a probe to see what happens) cannot ship a "jouet" silhouette.
    /// </summary>
    public static int ClampLift(int requested)
    {
        if (requested < MinLiftPx) return MinLiftPx;
        if (requested > MaxLiftPx) return MaxLiftPx;
        return requested;
    }

    /// <summary>
    /// Clamp parallax strength to the locked [<see cref="MinParallaxStrength"/>,
    /// <see cref="MaxParallaxStrength"/>] window. Same defense logic as
    /// <see cref="ClampLift"/> for the 3-5% upper bound.
    /// </summary>
    public static float ClampParallaxStrength(float requested)
    {
        if (float.IsNaN(requested)) return MinParallaxStrength;
        if (requested < MinParallaxStrength) return MinParallaxStrength;
        if (requested > MaxParallaxStrength) return MaxParallaxStrength;
        return requested;
    }

    /// <summary>
    /// Compute the parallax visual offset for a POI given the camera and
    /// reference (spawn-time tile) positions.
    ///
    /// <para>
    /// <b>Formula.</b> <c>offset = (camera - reference) × strength</c>.
    /// The reference is captured ONCE at POI spawn (<c>Poi._Ready</c>) — it
    /// is the world position the POI would sit at if no parallax was
    /// applied. As the camera moves AWAY from reference, the POI drifts
    /// in the SAME direction as the camera (positive sign) — this simulates
    /// the POI being slightly "in front" of the ground plane (closer to
    /// the viewer's eye), the same trick a 3D parallax stack does
    /// implicitly. With strength = 0.04, panning 100 world-px shifts the
    /// POI by 4 world-px.
    /// </para>
    ///
    /// <para>
    /// <b>Sign sanity.</b> If the camera pans EAST (positive X), the POI
    /// drifts EAST too — visually it lags behind the cadastral grid by
    /// `strength × pan`. That is the "in front of the ground" feel we want
    /// (a real 3D parallax of a POI floating just above the ground would
    /// drift WITH the camera, not against it).
    /// </para>
    ///
    /// <para>
    /// <b>Not a real 3D parallax.</b> A real parallax-scroll engine would
    /// derive the offset from depth-relative-to-camera. We approximate with
    /// a fixed strength (one knob, locked window 3-5%). If E2 World Map at
    /// extreme zoom shows visible artefacts, switch to depth-aware later —
    /// not in PR5 scope.
    /// </para>
    /// </summary>
    /// <param name="cameraGlobal">Current camera global position (world coords).</param>
    /// <param name="referenceGlobal">POI tile center at spawn (world coords).</param>
    /// <param name="strength">Multiplier in [0, 0.05]. Locked default 0.04.</param>
    /// <returns>Visual offset to add to the POI's base position.</returns>
    public static Vector2 ComputeParallaxOffset(
        Vector2 cameraGlobal,
        Vector2 referenceGlobal,
        float strength)
    {
        var clamped = ClampParallaxStrength(strength);
        return (cameraGlobal - referenceGlobal) * clamped;
    }
}
