namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helper for the E2.2 étape 6 "drop hit-test by ghost base"
/// fix (Didier smoke 2026-05-17). The forward drag-drop seam between
/// <c>IsoCharacterPlaceholder</c> (source, recruit panel) and
/// <c>IsoSocketPlaceholder</c> (target, company socle) was previously
/// gated by the cursor position alone -- the player had to manually
/// drive the mouse down to the socle's rhombus before the drop would
/// accept. With sticky-to-click drag preview, the player naturally
/// holds the cursor near the click point (head, torso, foot) and the
/// GHOST'S BASE (the parallelepiped front-face bottom edge) is what
/// visually lands on the socle. The accept gate has to follow.
///
/// <para>
/// <b>Pickup-offset-Y contract.</b> When the drag starts,
/// <c>_GetDragData</c> records, in the drag payload, the vertical
/// distance (in preview-local pixels) between the click point and the
/// ghost's base. That offset travels through every <c>_CanDropData</c>
/// / <c>_DropData</c> call so the drop site can shift the cursor's Y
/// down to where the ghost's base is actually drawn on screen
/// (= <c>cursor.Y + pickup_offset_y</c>).
/// </para>
///
/// <para>
/// <b>Why a dedicated helper.</b> Three pieces of arithmetic that, if
/// inlined in two Control subclasses, would drift independently :
/// <list type="bullet">
///   <item><see cref="ComputePickupOffsetY"/> -- the distance from the
///         click point down to the base, in preview-local coords. Uses
///         the same <c>padding + isoBodyHeight</c> anchor as
///         <see cref="IsoSocketCharacterPlacementLogic"/> so a future
///         iso body height tweak fans out from a single constant.</item>
///   <item><see cref="ComputeEffectiveBaseY"/> -- the trivial
///         <c>mouseY + pickupOffsetY</c> projection. Pinned to make
///         the sign convention loud (positive offset means base sits
///         BELOW the cursor) and to forbid a future "Math.Abs"-shaped
///         regression.</item>
///   <item><see cref="IsBaseWithinSocketHeight"/> -- the inclusive
///         tolerance check used by the socle's <c>_CanDropData</c>.
///         Tolerance is exposed as a parameter so the test surface
///         pins both the default (8 px = roughly half the rhombus top
///         apex thickness) and the edge cases.</item>
/// </list>
/// </para>
/// </summary>
public static class IsoDropHitTestLogic
{
    /// <summary>Default tolerance (px) applied around the socle's
    /// vertical bounds for the "ghost base within socket" hit-test.
    /// 8 px = roughly half the rhombus' top-apex thickness at the
    /// E2.2 default rhombus size (h ≈ 88-136 px) ; tight enough to
    /// reject a clearly-overshot drop, loose enough to absorb sub-pixel
    /// cursor jitter at drop-release time.</summary>
    public const float DefaultBaseToleranceYpx = 8f;

    /// <summary>
    /// Compute the pickup-offset-Y to write into the drag payload at
    /// <c>_GetDragData</c> time. = vertical distance, in preview-local
    /// pixels, from the click point down to the ghost's base
    /// (parallelepiped front-face bottom edge).
    ///
    /// <para>
    /// <b>Sign.</b> Positive when the click is ABOVE the base (the
    /// usual case -- player grabs the head or torso, base hangs below
    /// the cursor). Negative if the click somehow lands below the
    /// base (degenerate -- the preview rect would have to be smaller
    /// than the source rect ; we do not clamp because the consumer
    /// just adds the value to <c>mouseY</c> and the maths is still
    /// correct).
    /// </para>
    /// </summary>
    /// <param name="previewAtPositionY">Click point's Y in
    /// preview-local coords (= source <c>atPosition.Y</c> scaled by
    /// <c>previewSize.Y / sourceSize.Y</c>). Must already be in the
    /// preview frame -- the source frame is the Control receiving the
    /// drag, the preview frame is the compact silhouette built by
    /// <c>BuildDragPreview</c>.</param>
    /// <param name="paddingPx">Top padding inside the preview Control's
    /// rect (= <see cref="IsoSocketCharacterPlacementLogic.OccupantPaddingPx"/>
    /// = 12).</param>
    /// <param name="isoBodyHeight">Vertical extent (D + H) of the
    /// preview's iso body (= <see cref="IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight"/>
    /// = 220, the preview always renders in compact mode).</param>
    public static float ComputePickupOffsetY(
        float previewAtPositionY,
        float paddingPx,
        float isoBodyHeight)
        => paddingPx + isoBodyHeight - previewAtPositionY;

    /// <summary>
    /// Compute the effective ghost-base Y, in the drop-target Control's
    /// local frame, given the cursor position the drop target received
    /// via <c>_CanDropData</c> / <c>_DropData</c> and the pickup offset
    /// extracted from the drag payload.
    ///
    /// <para>
    /// <b>Why pinned.</b> The formula is trivial (one add) but the
    /// sign convention is load-bearing : a "Math.Abs" or a subtract
    /// here would silently invert the hit-test and make ALL drops
    /// look 200 px too high. The pin forbids that drift.
    /// </para>
    /// </summary>
    public static float ComputeEffectiveBaseY(float mouseY, float pickupOffsetY)
        => mouseY + pickupOffsetY;

    /// <summary>
    /// Decide whether a computed effective base Y falls within the
    /// socle Control's vertical bounds, plus / minus a tolerance.
    /// Inclusive on both edges so a base landing exactly at <c>0</c>
    /// or exactly at <c>socketSizeY</c> still accepts.
    /// </summary>
    /// <param name="effectiveBaseY">Output of
    /// <see cref="ComputeEffectiveBaseY"/>.</param>
    /// <param name="socketSizeY">Socle Control's Size.Y (px).</param>
    /// <param name="tolerancePx">Slack added on both edges.
    /// Pass <see cref="DefaultBaseToleranceYpx"/> for the standard 8 px
    /// window ; tests pass explicit values to pin the edges.</param>
    public static bool IsBaseWithinSocketHeight(
        float effectiveBaseY,
        float socketSizeY,
        float tolerancePx)
        => effectiveBaseY >= -tolerancePx
           && effectiveBaseY <= socketSizeY + tolerancePx;
}
