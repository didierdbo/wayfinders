namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# placement helper for "character standing on iso socle"
/// (E2.2 étape 6 fix, 2026-05-17). Given the parent socle's drawn
/// rhombus + the occupant <c>IsoCharacterPlaceholder</c>'s declared
/// iso body height, returns the <c>(X, Y)</c> position to assign to
/// the occupant child Control so that the BASE of the parallelepiped
/// silhouette (the front-face bottom edge) lands at the CENTER of the
/// socle rhombus (the visual "pied" sinks into the middle of the
/// losange, so the rhombus visibly serves as a base that the perso
/// stands ON, not floats above), and the silhouette is centred
/// horizontally over the socle.
///
/// <para>
/// <b>Why a dedicated helper.</b> The first cut of <c>SetOccupiedNpc</c>
/// computed the Y offset from <c>rhombusOffsetY + h/2 - sh</c>, which
/// silently mixed the rhombus' vertical centre with the occupant's
/// full Control height -- the visible result was a perso "floating" in
/// front of the rhombus instead of standing on it (Didier smoke 6 bug
/// 2026-05-17). The 7956c94 fix introduced this helper but used
/// <c>occupantSize.Y - paddingPx</c> as the base offset, which still
/// includes the label band + bottom padding slack ; the perso kept
/// floating above the socle. The 3610b64 cut took the visible iso
/// body height (D + H) directly via
/// <see cref="CompactIsoBodyHeight"/> / <see cref="DefaultIsoBodyHeight"/>,
/// decoupling the placement from any label / padding drift -- but
/// landed the base on the rhombus' TOP apex, which still reads as the
/// perso standing in FRONT of the socle, not ON it. The current cut
/// (UX bug 3, 2026-05-17 PM) targets the rhombus CENTER (= top apex
/// + h/2) so the base visibly sinks into the middle of the losange,
/// matching the locked POI "pion sur plateau" convention (memo
/// project_wayfinders_pion_sur_plateau).
/// </para>
///
/// <para>
/// <b>Geometry recap (consumed conventions).</b> The
/// <c>IsoCharacterPlaceholder</c> draws its parallelepiped inside its
/// own rect with the apex of the top losange at
/// <c>(occupantSize.X / 2, labelBandPx + paddingPx)</c> in occupant-
/// local coords, and the BASE of the front face at
/// <c>y_local = labelBandPx + paddingPx + (D + H)</c>. The
/// <c>(D + H)</c> sum is the source of truth exposed as a constant
/// here (220 px compact, 280 px default).
/// </para>
///
/// <para>
/// <b>Y formula.</b> To land the base on the rhombus CENTER at
/// socket-local <c>y = rhombusTopY + rhombusH / 2</c>, the occupant's
/// <c>Position.Y</c> must satisfy
/// <c>Position.Y + (paddingPx + isoBodyHeight) = rhombusTopY + rhombusH / 2</c>
/// in compact mode (no label band), or with <c>labelBandPx</c> added
/// to the padding term in default mode. The helper is currently only
/// used for the company socle's compact-mode occupant, so callers pass
/// <see cref="CompactIsoBodyHeight"/> and <see cref="OccupantPaddingPx"/>
/// directly ; a future default-mode site would add <c>labelBandPx</c>
/// to the padding argument.
/// </para>
///
/// <para>
/// <b>X formula.</b> Centre the occupant horizontally on the socle :
/// <c>Position.X = (socketSize.X - occupantSize.X) / 2</c>. Same
/// formula whether or not the rhombus is centred inside the socket
/// rect, because the rhombus IS centred (the <c>IsoSocketPlaceholder</c>
/// <c>_Draw</c> uses <c>offX = paddingPx + (availW - w) * 0.5</c>).
/// </para>
/// </summary>
public static class IsoSocketCharacterPlacementLogic
{
    /// <summary>Vertical extent (px) of the visible iso body alone (D + H)
    /// in compact mode, the mode used by the company socle's occupied-
    /// state mini silhouette. Derived from
    /// <c>IsoCharacterPlaceholder.CompactMinSize.Y</c> (244) minus the
    /// compact-mode label band (0) minus 2 x <see cref="OccupantPaddingPx"/>
    /// = 220. Source of truth -- the IsoCharacterPlaceholder Control
    /// reads back from here via <c>ActiveIsoBodyHeight</c> so a future
    /// cmin / padding tweak fans out from a single constant.</summary>
    public const float CompactIsoBodyHeight = 220f;

    /// <summary>Default-mode counterpart of
    /// <see cref="CompactIsoBodyHeight"/>. Derived from
    /// <c>IsoCharacterPlaceholder.DefaultMinSize.Y</c> (336) minus the
    /// default-mode label band (32) minus 2 x <see cref="OccupantPaddingPx"/>
    /// = 280. Not currently consumed by the helper (the company socle
    /// always hosts compact-mode occupants) ; exposed so a future
    /// default-mode placement site has a single anchor.</summary>
    public const float DefaultIsoBodyHeight = 280f;

    /// <summary>Top padding (px) the IsoCharacterPlaceholder applies
    /// inside its rect, above the iso body's top apex. Same value in
    /// both default and compact modes. Source of truth -- the
    /// IsoCharacterPlaceholder Control reads back from here via
    /// <c>PaddingPx</c> so the placement helper and the Control's
    /// _Draw share a single anchor.</summary>
    public const float OccupantPaddingPx = 12f;

    /// <summary>
    /// Compute the occupant Control's <c>Position</c> so its parallelepiped
    /// base lands at the rhombus' vertical CENTER (= top apex + h/2).
    /// </summary>
    /// <param name="socketSizeX">Socle Control width (Size.X), px.</param>
    /// <param name="rhombusTopY">Y of the rhombus' top apex in the socle's
    /// local frame -- this is the <c>offY</c> the socle's <c>_Draw</c>
    /// computed.</param>
    /// <param name="rhombusHeight">Rhombus' vertical extent (h, px) in
    /// the socle's local frame -- this is the <c>h</c> the socle's
    /// <c>_Draw</c> derived via <c>IsoSocketGeometryLogic.HeightFor(w)</c>.
    /// The base lands at <c>rhombusTopY + rhombusHeight / 2</c> so the
    /// perso visibly sinks into the middle of the losange (locked
    /// 2026-05-17 PM, Didier UX bug 3).</param>
    /// <param name="occupantSizeX">Occupant Control width (Size.X), px.</param>
    /// <param name="isoBodyHeight">Vertical extent of the visible iso
    /// body (D + H) in the occupant's active rendering mode. Pass
    /// <see cref="CompactIsoBodyHeight"/> for compact-mode occupants.
    /// Decouples the placement from any label-band / padding slack in
    /// the Control's total Size.Y.</param>
    /// <param name="occupantPaddingPx">Top padding the occupant applies
    /// inside its rect, above the iso body's top apex. Defaults to
    /// <see cref="OccupantPaddingPx"/> = 12 px.</param>
    /// <returns>The (X, Y) position to assign to the occupant.</returns>
    public static (float X, float Y) ComputeOccupantPosition(
        float socketSizeX,
        float rhombusTopY,
        float rhombusHeight,
        float occupantSizeX,
        float isoBodyHeight,
        float occupantPaddingPx)
    {
        var x = (socketSizeX - occupantSizeX) * 0.5f;
        // Base of the iso body sits at (paddingPx + isoBodyHeight) in
        // occupant-local Y, where paddingPx is the top padding band
        // above the top losange apex. Compact mode = label band 0, so
        // no extra term ; default mode would add the label band on top
        // of paddingPx but this helper is only used by the compact
        // socle occupant path today.
        var baseYInOccupant = occupantPaddingPx + isoBodyHeight;
        // Target the rhombus' vertical CENTER (top apex + h/2) so the
        // base sinks into the middle of the losange : the perso
        // visibly stands ON the socle, not in front of it. Pinned by
        // IsoSocketCharacterPlacementLogicTests.Base_lands_on_rhombus_center.
        var targetBaseY = rhombusTopY + rhombusHeight * 0.5f;
        var y = targetBaseY - baseYInOccupant;
        return (x, y);
    }
}
