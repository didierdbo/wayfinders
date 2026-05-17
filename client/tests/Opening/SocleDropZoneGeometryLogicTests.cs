using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="SocleDropZoneGeometryLogic"/> -- the
/// pure-C# geometry helper that sizes the SocleDropZone above the
/// IsoSocketPlaceholder so the GHOST-BASE hit-test catches every cursor
/// position the player COULD ghost-base-onto the socle (Didier UX bug 2
/// résiduel fix, 2026-05-17 soir, option 1 of 4).
///
/// <para>
/// <b>Why pinned.</b> The above-socle band must be at least
/// <c>padding + isoBodyHeight</c> tall : if it shrinks, the bug 2
/// residual symptom (drop refused when clicking on the perso's head)
/// returns silently -- the player gets no error, the cursor just feels
/// dead. The pin keeps the band height load-bearing. The Y-mapping
/// arithmetic is a one-line subtract but the sign convention drives
/// the existing IsoDropHitTestLogic's accept gate ; an inverted sign
/// would silently shift the accept window 488 px off.
/// </para>
/// </summary>
public sealed class SocleDropZoneGeometryLogicTests
{
    private const float Padding = IsoSocketCharacterPlacementLogic.OccupantPaddingPx;
    private const float IsoBody = IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight;

    [Fact]
    public void Above_band_covers_full_silhouette_plus_margin()
    {
        // Worst-case click at preview-local Y = 0 (above top apex) puts
        // the ghost base 232 px below the cursor. Plus the 16 px slack
        // for slight upward drift = 248 px.
        var above = SocleDropZoneGeometryLogic.ComputeAboveSocleBandPx(
            paddingPx: Padding,
            isoBodyHeight: IsoBody,
            extraMarginPx: SocleDropZoneGeometryLogic.DefaultExtraMarginYpx);
        Assert.Equal(248f, above);
    }

    [Fact]
    public void Above_band_with_zero_margin_collapses_to_strict_worst_case()
    {
        // A future "tighten the band" tweak would dial extraMarginPx down
        // to 0. The band collapses to exactly padding + isoBodyHeight =
        // 232. Any value below that re-introduces bug 2 silently.
        var above = SocleDropZoneGeometryLogic.ComputeAboveSocleBandPx(
            paddingPx: Padding,
            isoBodyHeight: IsoBody,
            extraMarginPx: 0f);
        Assert.Equal(232f, above);
    }

    [Fact]
    public void Default_extra_y_margin_is_sixteen_pixels()
    {
        // Belt-and-braces : the runtime sites pass DefaultExtraMarginYpx ;
        // the constant has to stay at 16 px (memo : roughly one mouse-
        // move tick at 30 fps drag tempo).
        Assert.Equal(16f, SocleDropZoneGeometryLogic.DefaultExtraMarginYpx);
    }

    [Fact]
    public void Default_extra_x_margin_is_twelve_pixels()
    {
        Assert.Equal(12f, SocleDropZoneGeometryLogic.DefaultExtraMarginXpx);
    }

    [Fact]
    public void Drop_zone_size_includes_socle_plus_above_band_and_horizontal_margins()
    {
        // Socle 280 x 240, above band 248, horizontal margin 12 each
        // side. Expected zone = (280 + 24) x (240 + 248) = 304 x 488.
        var above = SocleDropZoneGeometryLogic.ComputeAboveSocleBandPx(
            Padding, IsoBody, SocleDropZoneGeometryLogic.DefaultExtraMarginYpx);
        var (w, h) = SocleDropZoneGeometryLogic.ComputeDropZoneSize(
            socleWidth: 280f,
            socleHeight: 240f,
            aboveSocleBandPx: above,
            extraMarginXpx: SocleDropZoneGeometryLogic.DefaultExtraMarginXpx);
        Assert.Equal(304f, w);
        Assert.Equal(488f, h);
    }

    [Fact]
    public void Drop_zone_geometry_covers_above_socle_zone_for_top_click_pickup()
    {
        // Pin the cascade : a click at preview-local Y = 0 (very top of
        // silhouette) yields pickup_offset_y = 232. The cursor sits at
        // drop-zone-local Y = 0 (top edge of drop zone). After Y mapping
        // to socket-local + ComputeEffectiveBaseY + IsBaseWithinSocketHeight,
        // the drop must ACCEPT (this is the exact case bug 2 used to
        // silently refuse).
        var above = SocleDropZoneGeometryLogic.ComputeAboveSocleBandPx(
            Padding, IsoBody, SocleDropZoneGeometryLogic.DefaultExtraMarginYpx);
        // 248 px above-band, socle 240 tall, cursor at zone Y = 0 (very top).
        var socketLocalY = SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(
            dropZoneAtPositionY: 0f,
            socleOffsetYInDropZone: above);
        Assert.Equal(-248f, socketLocalY);

        var pickup = IsoDropHitTestLogic.ComputePickupOffsetY(
            previewAtPositionY: 0f, paddingPx: Padding, isoBodyHeight: IsoBody);
        Assert.Equal(232f, pickup);

        var effective = IsoDropHitTestLogic.ComputeEffectiveBaseY(socketLocalY, pickup);
        // -248 + 232 = -16. That's 16 px above the socle top edge (= the
        // safety slack we added). The IsBaseWithinSocketHeight tolerance
        // is 8 px ; -16 is OUTSIDE that, so the drop REJECTS in this
        // edge case (cursor at the very top of the slack band, not
        // inside the worst-case strict band).
        Assert.Equal(-16f, effective);
        Assert.False(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effective, socketSizeY: 240f, tolerancePx: 8f));

        // Same click, cursor at drop-zone Y = 16 (= bottom of the safety
        // slack, top of the strict band). Socket-local = -232. Effective
        // = -232 + 232 = 0 (= EXACT top edge of socle). Within tolerance ?
        // 0 is inside [-8, 248] -> accepts.
        var socketLocalY2 = SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(
            dropZoneAtPositionY: 16f, socleOffsetYInDropZone: above);
        var effective2 = IsoDropHitTestLogic.ComputeEffectiveBaseY(socketLocalY2, pickup);
        Assert.Equal(0f, effective2);
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effective2, socketSizeY: 240f, tolerancePx: 8f));
    }

    [Fact]
    public void Drop_at_top_click_now_accepts_via_drop_zone_route()
    {
        // The exact Didier-reported scenario : player clicks halfway up
        // the silhouette (preview-local Y = 120, roughly torso), drags
        // up so the cursor sits well above the socle. Pre-fix, the
        // socle's rect did not contain the cursor -> _CanDropData never
        // fired -> drop silently refused. Post-fix, the drop zone's
        // rect catches the cursor + maps to socket-local + the existing
        // ghost-base helper accepts.
        const float clickY = 120f;
        var pickup = IsoDropHitTestLogic.ComputePickupOffsetY(
            clickY, Padding, IsoBody);
        // pickup = 12 + 220 - 120 = 112 (base sits 112 px below cursor).
        Assert.Equal(112f, pickup);

        var above = SocleDropZoneGeometryLogic.ComputeAboveSocleBandPx(
            Padding, IsoBody, SocleDropZoneGeometryLogic.DefaultExtraMarginYpx);

        // Player drags so the cursor sits at drop-zone Y = 140 (= 108 px
        // above the socle's top edge inside the drop zone : above is 248,
        // 248 - 140 = 108 above socle top).
        var socketLocalY = SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(
            dropZoneAtPositionY: 140f, socleOffsetYInDropZone: above);
        Assert.Equal(-108f, socketLocalY);
        var effective = IsoDropHitTestLogic.ComputeEffectiveBaseY(socketLocalY, pickup);
        // -108 + 112 = 4 (4 px below socle top edge) -> inside [-8, 248].
        Assert.Equal(4f, effective);
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effective, socketSizeY: 240f, tolerancePx: 8f));
    }

    [Fact]
    public void Map_drop_zone_y_to_socket_y_is_simple_subtract()
    {
        // Pin the sign convention loudly : socle sits BELOW the above-
        // band so the offset is positive ; click at drop-zone Y = 250
        // with offset 248 maps to socket-local Y = 2 (just inside socle).
        Assert.Equal(2f, SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(250f, 248f));
        Assert.Equal(-248f, SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(0f, 248f));
        Assert.Equal(0f, SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(248f, 248f));
        // Zero offset = drop zone same frame as socle = identity.
        Assert.Equal(100f, SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(100f, 0f));
    }

    [Fact]
    public void Bottom_click_on_socle_still_accepts_via_drop_zone()
    {
        // Sanity : the fix must not regress the working "cursor lands
        // on socle" path. Player clicks at the silhouette's base
        // (preview-local Y = 232, pickup_offset_y = 0). Drags so the
        // cursor sits squarely on the socle (drop-zone Y = above + 100
        // = inside the socle rhombus area).
        var pickup = IsoDropHitTestLogic.ComputePickupOffsetY(
            previewAtPositionY: Padding + IsoBody,
            paddingPx: Padding,
            isoBodyHeight: IsoBody);
        Assert.Equal(0f, pickup);

        var above = SocleDropZoneGeometryLogic.ComputeAboveSocleBandPx(
            Padding, IsoBody, SocleDropZoneGeometryLogic.DefaultExtraMarginYpx);
        var socketLocalY = SocleDropZoneGeometryLogic.MapDropZoneYToSocketY(
            dropZoneAtPositionY: above + 100f, socleOffsetYInDropZone: above);
        Assert.Equal(100f, socketLocalY);
        var effective = IsoDropHitTestLogic.ComputeEffectiveBaseY(socketLocalY, pickup);
        Assert.Equal(100f, effective);
        Assert.True(IsoDropHitTestLogic.IsBaseWithinSocketHeight(
            effective, socketSizeY: 240f, tolerancePx: 8f));
    }
}
