namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# placement helper for "character standing on iso socle"
/// (E2.2 étape 6 fix, 2026-05-17). Given the parent socle's drawn
/// rhombus + the occupant <c>IsoCharacterPlaceholder</c>'s declared
/// size, returns the <c>(X, Y)</c> position to assign to the
/// occupant child Control so that the BASE of the parallelepiped
/// silhouette (the front-face bottom edge) lands exactly on the TOP
/// apex of the socle rhombus, and the silhouette is centred
/// horizontally over the socle.
///
/// <para>
/// <b>Why a dedicated helper.</b> The first cut of <c>SetOccupiedNpc</c>
/// computed the Y offset from <c>rhombusOffsetY + h/2 - sh</c>, which
/// silently mixed the rhombus' vertical centre with the occupant's
/// full Control height -- the visible result was a perso "floating" in
/// front of the rhombus instead of standing on it (Didier smoke 6 bug
/// 2026-05-17). Pinning the math in a pure-C# helper makes the
/// invariant explicit and catches drift if the IsoCharacterPlaceholder
/// padding / label band conventions ever change.
/// </para>
///
/// <para>
/// <b>Geometry recap (consumed conventions).</b> The
/// <c>IsoCharacterPlaceholder</c> draws its parallelepiped inside its
/// own rect with the apex of the top losange at
/// <c>(occupantSize.X / 2, labelBandPx + paddingPx)</c> in occupant-
/// local coords, and the BASE of the front face at
/// <c>y_local = labelBandPx + paddingPx + d + h</c>. In compact mode
/// the label band collapses to 0 ; the base sits at
/// <c>occupantSize.Y - paddingPx</c> by construction (the cmin size
/// is sized to fit padding + d + h exactly).
/// </para>
///
/// <para>
/// <b>Y formula.</b> To align the base with the rhombus top apex at
/// socket-local <c>y = rhombusTopY</c>, the occupant's Position.Y
/// must satisfy <c>Position.Y + baseYInOccupant = rhombusTopY</c>,
/// i.e. <c>Position.Y = rhombusTopY - (occupantSize.Y - paddingPx)</c>
/// in compact mode, or more generally
/// <c>Position.Y = rhombusTopY - (labelBandPx + paddingPx + d + h)</c>
/// where <c>d + h = availH</c> = <c>occupantSize.Y - labelBandPx - 2 * paddingPx</c>,
/// which simplifies back to <c>rhombusTopY - occupantSize.Y + paddingPx</c>
/// regardless of label-band presence.
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
    /// <summary>
    /// Compute the occupant Control's <c>Position</c> so its parallelepiped
    /// base sits on the socle rhombus' top apex.
    /// </summary>
    /// <param name="socketSizeX">Socle Control width (Size.X), px.</param>
    /// <param name="rhombusTopY">Y of the rhombus' top apex in the socle's
    /// local frame -- this is the <c>offY</c> the socle's <c>_Draw</c>
    /// computed.</param>
    /// <param name="occupantSizeX">Occupant Control width (Size.X), px.</param>
    /// <param name="occupantSizeY">Occupant Control height (Size.Y), px.</param>
    /// <param name="occupantPaddingPx">Padding constant the occupant
    /// applies inside its rect (defaults to the
    /// <c>IsoCharacterPlaceholder.PaddingPx</c> = 12).</param>
    /// <returns>The (X, Y) position to assign to the occupant.</returns>
    public static (float X, float Y) ComputeOccupantPosition(
        float socketSizeX,
        float rhombusTopY,
        float occupantSizeX,
        float occupantSizeY,
        float occupantPaddingPx)
    {
        var x = (socketSizeX - occupantSizeX) * 0.5f;
        // Base of the parallelepiped sits at (occupantSizeY - paddingPx)
        // in occupant-local Y, regardless of label-band presence (cmin
        // sizes are dimensioned to fit padding + d + h exactly).
        var baseYInOccupant = occupantSizeY - occupantPaddingPx;
        var y = rhombusTopY - baseYInOccupant;
        return (x, y);
    }
}
