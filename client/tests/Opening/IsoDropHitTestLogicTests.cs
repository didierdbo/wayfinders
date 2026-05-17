using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="IsoDropHitTestLogic"/> -- the three
/// pure-C# helpers that drive the sticky-to-click drag preview's
/// ghost-base hit-test (E2.2 étape 6 UX bug 2 fix, 2026-05-17 PM).
///
/// <para>
/// <b>Why pinned.</b> The pickup-offset-Y arithmetic is one add and
/// one subtract, but the sign convention is load-bearing : a stray
/// Math.Abs or an inverted subtract would silently shift ALL drops
/// 200 px high (or low) and the player would not be able to drop at
/// all. The pin keeps the convention loud. The IsBaseWithinSocketHeight
/// edge-tolerance is similarly pinned so a future "0 means exclusive"
/// regression surfaces red, not as a flaky drop accept.
/// </para>
/// </summary>
public sealed class IsoDropHitTestLogicTests
{
    private const float CompactPadding = IsoSocketCharacterPlacementLogic.OccupantPaddingPx;
    private const float CompactIsoBody = IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight;

    // -----------------------------------------------------------------
    // ComputePickupOffsetY -- preview-local click-to-base distance.
    // -----------------------------------------------------------------

    [Fact]
    public void Click_at_top_of_silhouette_yields_full_body_offset()
    {
        // Click at preview-local Y = 0 (= above the top apex). Base
        // sits at padding + isoBodyHeight = 12 + 220 = 232. Offset =
        // 232 - 0 = 232.
        var offset = IsoDropHitTestLogic.ComputePickupOffsetY(
            previewAtPositionY: 0f,
            paddingPx: CompactPadding,
            isoBodyHeight: CompactIsoBody);
        Assert.Equal(232f, offset);
    }

    [Fact]
    public void Click_at_top_apex_yields_iso_body_height_offset()
    {
        // Click at preview-local Y = padding = 12 (= exactly on the
        // top apex). Offset = (12 + 220) - 12 = 220.
        var offset = IsoDropHitTestLogic.ComputePickupOffsetY(
            previewAtPositionY: CompactPadding,
            paddingPx: CompactPadding,
            isoBodyHeight: CompactIsoBody);
        Assert.Equal(220f, offset);
    }

    [Fact]
    public void Click_at_silhouette_base_yields_zero_offset()
    {
        // Click at preview-local Y = padding + isoBodyHeight = 232
        // (= exactly on the base). Offset = 232 - 232 = 0.
        var offset = IsoDropHitTestLogic.ComputePickupOffsetY(
            previewAtPositionY: CompactPadding + CompactIsoBody,
            paddingPx: CompactPadding,
            isoBodyHeight: CompactIsoBody);
        Assert.Equal(0f, offset);
    }

    [Fact]
    public void Click_below_base_yields_negative_offset()
    {
        // Degenerate but supported : if the click lands BELOW the iso
        // body's base (e.g. inside a future "shadow" pad), the offset
        // is negative -- the consumer just adds it to mouseY, and the
        // base ends up ABOVE the cursor. The maths stays consistent.
        var offset = IsoDropHitTestLogic.ComputePickupOffsetY(
            previewAtPositionY: CompactPadding + CompactIsoBody + 10f,
            paddingPx: CompactPadding,
            isoBodyHeight: CompactIsoBody);
        Assert.Equal(-10f, offset);
    }

    // -----------------------------------------------------------------
    // ComputeEffectiveBaseY -- cursor + offset projection.
    // -----------------------------------------------------------------

    [Fact]
    public void Effective_base_y_is_simple_sum()
    {
        // The pin's whole point : forbid a future "Math.Abs" or
        // subtract regression here. Sum, period.
        Assert.Equal(150f, IsoDropHitTestLogic.ComputeEffectiveBaseY(50f, 100f));
        Assert.Equal(-50f, IsoDropHitTestLogic.ComputeEffectiveBaseY(50f, -100f));
        Assert.Equal(0f, IsoDropHitTestLogic.ComputeEffectiveBaseY(0f, 0f));
    }

    // -----------------------------------------------------------------
    // IsBaseWithinSocketHeight -- inclusive tolerance window.
    // -----------------------------------------------------------------

    [Fact]
    public void Base_inside_bounds_accepts()
    {
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effectiveBaseY: 40f, socketSizeY: 100f, tolerancePx: 8f));
    }

    [Fact]
    public void Base_at_exact_top_edge_accepts()
    {
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effectiveBaseY: 0f, socketSizeY: 100f, tolerancePx: 0f));
    }

    [Fact]
    public void Base_at_exact_bottom_edge_accepts()
    {
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effectiveBaseY: 100f, socketSizeY: 100f, tolerancePx: 0f));
    }

    [Fact]
    public void Base_within_top_tolerance_accepts()
    {
        // -5 with tolerance 8 -> accepts (within -8 .. 108).
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effectiveBaseY: -5f, socketSizeY: 100f, tolerancePx: 8f));
    }

    [Fact]
    public void Base_within_bottom_tolerance_accepts()
    {
        // 105 with tolerance 8 -> accepts (within -8 .. 108).
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effectiveBaseY: 105f, socketSizeY: 100f, tolerancePx: 8f));
    }

    [Fact]
    public void Base_beyond_top_tolerance_rejects()
    {
        // -10 with tolerance 8 -> rejects (below -8).
        Assert.False(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effectiveBaseY: -10f, socketSizeY: 100f, tolerancePx: 8f));
    }

    [Fact]
    public void Base_beyond_bottom_tolerance_rejects()
    {
        // 120 with tolerance 8 -> rejects (above 108).
        Assert.False(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effectiveBaseY: 120f, socketSizeY: 100f, tolerancePx: 8f));
    }

    [Fact]
    public void Default_tolerance_is_eight_pixels()
    {
        // Belt-and-braces : the runtime sites pass DefaultBaseToleranceYpx
        // ; the constant has to stay at 8 (memo : roughly half the
        // rhombus' top-apex thickness at the E2.2 default size).
        Assert.Equal(8f, IsoDropHitTestLogic.DefaultBaseToleranceYpx);
    }

    // -----------------------------------------------------------------
    // End-to-end : full pickup -> drop arithmetic round-trip.
    // -----------------------------------------------------------------

    [Fact]
    public void Sticky_to_click_round_trip_lands_base_at_cursor_plus_offset()
    {
        // Player clicks at the silhouette's top apex (preview-local
        // Y = 12). Drags so the cursor sits at socket-local Y = 30
        // (somewhere above the rhombus). The ghost base on screen ends
        // up at 30 + 220 = 250 (= socle bottom-ish). If the socle Size
        // is 240, 250 is 10 px beyond the bottom edge -- within the
        // 8 px default tolerance ? No, 10 > 8 -> rejects. This is the
        // exact "drop too low" scenario the gate must guard against.
        var pickup = IsoDropHitTestLogic.ComputePickupOffsetY(
            previewAtPositionY: 12f,
            paddingPx: CompactPadding,
            isoBodyHeight: CompactIsoBody);
        var effective = IsoDropHitTestLogic.ComputeEffectiveBaseY(30f, pickup);
        Assert.Equal(250f, effective);
        Assert.False(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effective, socketSizeY: 240f, tolerancePx: 8f));

        // Same click, cursor at socket-local Y = 10 -> base at 230,
        // within the socle (240 + 8 tolerance) -> accepts.
        effective = IsoDropHitTestLogic.ComputeEffectiveBaseY(10f, pickup);
        Assert.Equal(230f, effective);
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effective, socketSizeY: 240f, tolerancePx: 8f));
    }
}
