namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry helper for the E2.2 étape 6 "drop zone englobant le
/// socle + zone above-socle" fix (Didier UX bug 2 résiduel, 2026-05-17
/// soir). The bug : Godot only routes <c>_CanDropData</c> /
/// <c>_DropData</c> to the Control whose physical rect contains the
/// cursor, but the GHOST-BASE hit-test ("drop where the silhouette's
/// pieds land") needs the accept to fire even when the cursor sits
/// 200 px ABOVE the socle rhombus (= the case where the player clicked
/// the perso's head ; the click point stays glued to the head per the
/// sticky-to-click rule, so cursor never reaches the socle). Pre-fix,
/// the drop was silently refused because <c>_CanDropData</c> was never
/// called -- the cursor was outside the socle's rect.
///
/// <para>
/// <b>The fix.</b> Wrap the socle in a transparent <c>SocleDropZone</c>
/// Control whose rect extends far enough ABOVE the socle to catch every
/// cursor position that COULD ghost-base-onto the socle. The drop zone
/// owns the <c>_CanDropData</c> / <c>_DropData</c> hooks ; the socle
/// reverts to a pure visual + reverse-drag-source. The existing
/// <see cref="IsoDropHitTestLogic"/> stays unchanged -- the drop zone
/// just translates its local <c>atPosition</c> into socket-local before
/// forwarding.
/// </para>
///
/// <para>
/// <b>Above-socle band height.</b> The worst case for "click at top of
/// silhouette" is the player clicks at preview-local Y = 0 (above the
/// top apex). The ghost base then sits at preview-local Y =
/// <c>padding + isoBodyHeight</c> = 232 px BELOW the cursor. For the
/// drop to accept when the cursor is at the very top of the socle rect,
/// the drop zone must extend at least <c>padding + isoBodyHeight</c>
/// above the socle. We add an <see cref="DefaultExtraMarginYpx"/> on
/// top so a click slightly above the silhouette (= the player dragging
/// upward then releasing slightly higher than the click point) still
/// catches the drop zone.
/// </para>
///
/// <para>
/// <b>Width.</b> Symmetric horizontal margin around the socle's cmin
/// width so off-centre clicks (the player grabbed the silhouette's
/// shoulder, not its centre) still land in the drop zone. Capped so
/// the zone does not eat into the panel's header X button or chrome
/// margins (the CompanyPanel is 360 px wide ; content margin 16 each
/// side leaves ~328 px usable ; the drop zone width must stay below).
/// </para>
///
/// <para>
/// <b>Y mapping.</b> The drop zone receives <c>atPosition</c> in its
/// OWN local frame. The existing <see cref="IsoDropHitTestLogic"/>
/// expects socket-local Y. Map via subtract of the socle's
/// <c>Position.Y</c> inside the drop zone (= <c>AboveSocleBandPx</c>).
/// Pinned in <c>SocleDropZoneGeometryLogicTests</c>.
/// </para>
/// </summary>
public static class SocleDropZoneGeometryLogic
{
    /// <summary>Extra Y margin (px) added above the strict
    /// <c>padding + isoBodyHeight</c> "click-at-top" worst-case so a
    /// slightly-higher click still catches the drop zone. 16 px =
    /// roughly one mouse-move tick at 30 fps drag tempo ; loose enough
    /// to absorb upward drift, tight enough that a click 60 px above
    /// the silhouette top (= clearly off-target) still misses.</summary>
    public const float DefaultExtraMarginYpx = 16f;

    /// <summary>Extra X margin (px) on each horizontal side around the
    /// socle's cmin width. 12 px = roughly half a finger-tip's worth of
    /// horizontal jitter at trackpad tempo.</summary>
    public const float DefaultExtraMarginXpx = 12f;

    /// <summary>
    /// Compute the height of the "above-socle" band the drop zone must
    /// extend up above the socle rect so a click at the very top of a
    /// dragged silhouette still routes the drop through this zone.
    /// = <c>padding + isoBodyHeight + extraMargin</c>.
    ///
    /// <para>
    /// Pinned by
    /// <c>SocleDropZoneGeometryLogicTests.Above_band_covers_full_silhouette_plus_margin</c>.
    /// </para>
    /// </summary>
    /// <param name="paddingPx">Top padding the iso silhouette applies
    /// inside its rect (= <see cref="IsoSocketCharacterPlacementLogic.OccupantPaddingPx"/>
    /// = 12 px).</param>
    /// <param name="isoBodyHeight">Vertical extent (D + H) of the
    /// silhouette's iso body in compact mode (= <see cref="IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight"/>
    /// = 220 px).</param>
    /// <param name="extraMarginPx">Slack above the worst-case click.
    /// Pass <see cref="DefaultExtraMarginYpx"/> for the standard 16 px.</param>
    public static float ComputeAboveSocleBandPx(
        float paddingPx,
        float isoBodyHeight,
        float extraMarginPx)
        => paddingPx + isoBodyHeight + extraMarginPx;

    /// <summary>
    /// Compute the full drop-zone size given the socle rect + the click-
    /// at-top above-band. Width = <c>socleWidth + 2 * extraMarginX</c> ;
    /// Height = <c>socleHeight + aboveBandPx</c>.
    ///
    /// <para>
    /// <b>Why height does NOT add extraMargin BELOW the socle.</b> A
    /// click BELOW the silhouette is degenerate (the click would have to
    /// land below the perso's pieds, which is outside the silhouette's
    /// drawn rect anyway). The above-band is the only asymmetric extension
    /// the ghost-base-hit-test needs.
    /// </para>
    /// </summary>
    public static (float W, float H) ComputeDropZoneSize(
        float socleWidth,
        float socleHeight,
        float aboveSocleBandPx,
        float extraMarginXpx)
    {
        var w = socleWidth + 2f * extraMarginXpx;
        var h = socleHeight + aboveSocleBandPx;
        return (w, h);
    }

    /// <summary>
    /// Map a drop-zone-local Y to the equivalent socket-local Y. = simple
    /// subtract of <paramref name="socleOffsetYInDropZone"/>. Pinned to
    /// keep the sign convention loud : the socle sits BELOW the above-
    /// band, so the offset is positive ; a click at drop-zone Y = 0
    /// (top edge) maps to socket Y = <c>-aboveBandPx</c> (= far above
    /// the socle's top edge ; the
    /// <see cref="IsoDropHitTestLogic.ComputeEffectiveBaseY"/> then adds
    /// the pickup offset to land the ghost base inside the socle).
    /// </summary>
    public static float MapDropZoneYToSocketY(
        float dropZoneAtPositionY,
        float socleOffsetYInDropZone)
        => dropZoneAtPositionY - socleOffsetYInDropZone;
}
