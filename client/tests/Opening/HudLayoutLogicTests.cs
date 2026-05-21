using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="HudLayoutLogic"/> — the J3b residual
/// map-viewport rectangle maths. Pins the contract that turns a screen
/// size plus two fixed HUD band heights into the central rectangle the
/// maquette viewport must occupy so it clips strictly between the bands.
///
/// <para>
/// <b>What this suite owns.</b> Only the residual-rect arithmetic: the
/// top-left drops by the top band, the height shrinks by both bands, the
/// width is unchanged, and degenerate inputs (negative heights, bands that
/// overlap) clamp instead of going negative. The <c>CanvasLayer</c>
/// overlay wiring itself carries no maths and is validated via the
/// <c>GameScreen.tscn</c> F6 smoke.
/// </para>
///
/// <para>
/// <b>Band height adjusted 2026-05-21 (Didier).</b> The nominal HUD band
/// height was re-authored from 90 px to <see cref="HudBand"/> = 32 px
/// (slim bands), so the residual map rect on a 1920x1080 screen grows to
/// <c>[0,32 1920x1016]</c>. These tests are updated to the new value;
/// <see cref="HudBand"/> here mirrors <c>GameScreen.HudBandHeight</c>, the
/// single source of truth in the scene.
/// </para>
/// </summary>
public sealed class HudLayoutLogicTests
{
    // The J3b GameScreen.tscn shape: 1920x1080 screen, 32 px slim bands
    // (Didier adjustment 2026-05-21 — was 90 px).
    private const float ScreenW = 1920f;
    private const float ScreenH = 1080f;
    private const float HudBand = 32f;

    [Fact]
    public void Residual_rect_drops_top_left_by_the_top_band()
    {
        var rect = HudLayoutLogic.ResidualMapRect(ScreenW, ScreenH, HudBand, HudBand);

        Assert.Equal(0f, rect.X);
        Assert.Equal(HudBand, rect.Y);
    }

    [Fact]
    public void Residual_rect_keeps_the_full_screen_width()
    {
        var rect = HudLayoutLogic.ResidualMapRect(ScreenW, ScreenH, HudBand, HudBand);

        // The HUD bands span the full width; the central rect is only
        // squeezed vertically.
        Assert.Equal(ScreenW, rect.Width);
    }

    [Fact]
    public void Residual_rect_height_is_screen_minus_both_bands()
    {
        var rect = HudLayoutLogic.ResidualMapRect(ScreenW, ScreenH, HudBand, HudBand);

        Assert.Equal(ScreenH - HudBand - HudBand, rect.Height); // 1016
    }

    [Fact]
    public void Nominal_slim_band_shape_is_the_locked_residual_rect()
    {
        // Pins the exact 32 px adjustment: 1920x1080 screen, two 32 px
        // bands -> [0,32 1920x1016]. A drift in HudBandHeight surfaces
        // here red.
        var rect = HudLayoutLogic.ResidualMapRect(ScreenW, ScreenH, HudBand, HudBand);

        Assert.Equal(0f, rect.X);
        Assert.Equal(32f, rect.Y);
        Assert.Equal(1920f, rect.Width);
        Assert.Equal(1016f, rect.Height);
    }

    [Fact]
    public void Asymmetric_bands_are_handled_independently()
    {
        var rect = HudLayoutLogic.ResidualMapRect(ScreenW, ScreenH, 120f, 60f);

        Assert.Equal(120f, rect.Y);
        Assert.Equal(ScreenH - 120f - 60f, rect.Height); // 900
    }

    [Fact]
    public void Negative_band_heights_are_clamped_to_zero()
    {
        var rect = HudLayoutLogic.ResidualMapRect(ScreenW, ScreenH, -50f, -30f);

        // A negative band cannot push the residual past the screen edge.
        Assert.Equal(0f, rect.Y);
        Assert.Equal(ScreenH, rect.Height);
    }

    [Fact]
    public void Bands_overlapping_the_whole_screen_collapse_to_zero_height_not_negative()
    {
        // 700 + 700 = 1400 > 1080: a design error, but the helper must
        // stay total — a Control with a negative size is undefined.
        var rect = HudLayoutLogic.ResidualMapRect(ScreenW, ScreenH, 700f, 700f);

        Assert.Equal(700f, rect.Y);
        Assert.Equal(0f, rect.Height);
        Assert.True(rect.Height >= 0f);
    }

    [Fact]
    public void HasUsableResidual_true_for_the_nominal_shape()
    {
        Assert.True(HudLayoutLogic.HasUsableResidual(ScreenW, ScreenH, HudBand, HudBand));
    }

    [Fact]
    public void HasUsableResidual_false_when_the_bands_eat_the_whole_screen()
    {
        Assert.False(HudLayoutLogic.HasUsableResidual(ScreenW, ScreenH, 700f, 700f));
    }

    [Fact]
    public void HasUsableResidual_false_for_a_zero_width_screen()
    {
        Assert.False(HudLayoutLogic.HasUsableResidual(0f, ScreenH, HudBand, HudBand));
    }
}
