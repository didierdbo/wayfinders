// PR5 — pin tests for the POI "pion sur plateau" pure-C# helpers.
//
// What this file pins :
//   1. ClampLift defends the locked [0, 3] window (0 = sticker, 3 = toy edge,
//      > 3 = anti-pattern).
//   2. ClampParallaxStrength defends the locked [0, 0.05] window.
//   3. ClampParallaxStrength is NaN-safe (returns 0, no exception).
//   4. ComputeParallaxOffset is zero at the reference point.
//   5. ComputeParallaxOffset signs are correct (drift WITH camera pan).
//   6. ComputeParallaxOffset scales linearly with strength.
//   7. ComputeParallaxOffset is clamped if a slipped strength sneaks in.
//
// The locked window comes from project_wayfinders_pion_sur_plateau :
//   - Lift 2 = posed (target). 0 = sticker, 3 = toy. > 3 forbidden.
//   - Parallax 3-5% (0.03 to 0.05). Target 0.04. > 0.05 forbidden.

using System.Numerics;
using Wayfinders.Client.Scripts.Poi;

namespace Wayfinders.Client.Tests;

public sealed class PoiVisualLogicTests
{
    // ====================================================================
    // ClampLift — defend [0, 3] window
    // ====================================================================

    [Fact]
    public void ClampLift_target_2_passes_through()
    {
        Assert.Equal(2, PoiVisualLogic.ClampLift(2));
    }

    [Fact]
    public void ClampLift_zero_passes_through_for_sticker_AB()
    {
        Assert.Equal(0, PoiVisualLogic.ClampLift(0));
    }

    [Fact]
    public void ClampLift_three_passes_through_for_toy_edge_AB()
    {
        Assert.Equal(3, PoiVisualLogic.ClampLift(3));
    }

    [Fact]
    public void ClampLift_negative_snaps_to_min()
    {
        Assert.Equal(PoiVisualLogic.MinLiftPx, PoiVisualLogic.ClampLift(-5));
    }

    [Fact]
    public void ClampLift_four_snaps_to_max_locked_three()
    {
        // Defends the "toy" anti-pattern : a slipped 4 in the inspector
        // (or a probe typo) must not ship a floating jouet.
        Assert.Equal(3, PoiVisualLogic.ClampLift(4));
    }

    [Fact]
    public void ClampLift_extreme_value_snaps_to_max()
    {
        Assert.Equal(3, PoiVisualLogic.ClampLift(9999));
    }

    // ====================================================================
    // ClampParallaxStrength — defend [0, 0.05] window
    // ====================================================================

    [Fact]
    public void ClampParallax_locked_target_0_04_passes_through()
    {
        Assert.Equal(0.04f, PoiVisualLogic.ClampParallaxStrength(0.04f));
    }

    [Fact]
    public void ClampParallax_zero_passes_through_for_off_AB()
    {
        Assert.Equal(0.0f, PoiVisualLogic.ClampParallaxStrength(0.0f));
    }

    [Fact]
    public void ClampParallax_max_0_05_passes_through_for_upper_edge_AB()
    {
        Assert.Equal(0.05f, PoiVisualLogic.ClampParallaxStrength(0.05f));
    }

    [Fact]
    public void ClampParallax_negative_snaps_to_min()
    {
        Assert.Equal(PoiVisualLogic.MinParallaxStrength,
            PoiVisualLogic.ClampParallaxStrength(-0.1f));
    }

    [Fact]
    public void ClampParallax_above_max_snaps_to_max()
    {
        // Defends the "slip past 5%" anti-pattern : a slipped 0.1 would
        // make POIs feel floaty / 3D-jumpy.
        Assert.Equal(0.05f, PoiVisualLogic.ClampParallaxStrength(0.1f));
    }

    [Fact]
    public void ClampParallax_NaN_returns_min_not_throw()
    {
        Assert.Equal(PoiVisualLogic.MinParallaxStrength,
            PoiVisualLogic.ClampParallaxStrength(float.NaN));
    }

    // ====================================================================
    // ComputeParallaxOffset — formula, sign, linearity
    // ====================================================================

    [Fact]
    public void ParallaxOffset_at_reference_is_zero()
    {
        var reference = new Vector2(500, 300);
        var offset = PoiVisualLogic.ComputeParallaxOffset(reference, reference, 0.04f);
        Assert.Equal(Vector2.Zero, offset);
    }

    [Fact]
    public void ParallaxOffset_drifts_east_when_camera_pans_east()
    {
        // Camera moves +100 X away from reference. With strength 0.04,
        // POI drifts +4 X (same direction as camera — sign sanity).
        var reference = new Vector2(0, 0);
        var camera = new Vector2(100, 0);
        var offset = PoiVisualLogic.ComputeParallaxOffset(camera, reference, 0.04f);
        Assert.Equal(4.0f, offset.X, precision: 3);
        Assert.Equal(0.0f, offset.Y, precision: 3);
    }

    [Fact]
    public void ParallaxOffset_drifts_south_when_camera_pans_south()
    {
        var reference = new Vector2(0, 0);
        var camera = new Vector2(0, 200);
        var offset = PoiVisualLogic.ComputeParallaxOffset(camera, reference, 0.05f);
        Assert.Equal(0.0f, offset.X, precision: 3);
        Assert.Equal(10.0f, offset.Y, precision: 3);
    }

    [Fact]
    public void ParallaxOffset_scales_linearly_with_strength()
    {
        var reference = new Vector2(0, 0);
        var camera = new Vector2(100, 100);
        var weak = PoiVisualLogic.ComputeParallaxOffset(camera, reference, 0.03f);
        var strong = PoiVisualLogic.ComputeParallaxOffset(camera, reference, 0.05f);
        // 0.05 / 0.03 ≈ 1.6667
        Assert.Equal(5.0f / 3.0f, strong.X / weak.X, precision: 3);
    }

    [Fact]
    public void ParallaxOffset_zero_strength_disables()
    {
        var reference = new Vector2(0, 0);
        var camera = new Vector2(1000, -1000);
        var offset = PoiVisualLogic.ComputeParallaxOffset(camera, reference, 0.0f);
        Assert.Equal(Vector2.Zero, offset);
    }

    [Fact]
    public void ParallaxOffset_above_max_strength_is_clamped()
    {
        // Slipped 0.1 should behave as 0.05 (the locked upper bound).
        var reference = new Vector2(0, 0);
        var camera = new Vector2(100, 0);
        var offsetSlipped = PoiVisualLogic.ComputeParallaxOffset(camera, reference, 0.1f);
        var offsetClamped = PoiVisualLogic.ComputeParallaxOffset(camera, reference, 0.05f);
        Assert.Equal(offsetClamped.X, offsetSlipped.X, precision: 5);
    }

    [Fact]
    public void ParallaxOffset_negative_pan_negative_drift()
    {
        // Camera moves WEST (negative X) → POI drifts WEST too (same sign).
        var reference = new Vector2(0, 0);
        var camera = new Vector2(-200, 0);
        var offset = PoiVisualLogic.ComputeParallaxOffset(camera, reference, 0.04f);
        Assert.Equal(-8.0f, offset.X, precision: 3);
    }
}
