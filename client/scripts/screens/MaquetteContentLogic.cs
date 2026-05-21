using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# geometry for where the maquette <c>MapCamera2D</c> must park so
/// the pannable district content is <b>centred on screen</b> at start.
///
/// <para>
/// <b>The bug this solves (J3c-1sexies, Didier, 2026-05-21 — "the maquette
/// diamond leans to the left").</b> The triangular clip shader cuts the
/// maquette diamond centred on <c>screenWidth / 2</c>. But the <i>content</i>
/// of the maquette is positioned by its own pannable <c>MapCamera2D</c>, and
/// <c>GameScreen._Ready</c> parked that camera on
/// <c>contentSize * 0.5</c> — the centre of the content's <b>bounding box</b>.
/// For the placeholder iso grid that bounding box and the diamond's real
/// geometric centre do <b>not</b> coincide.
/// </para>
///
/// <para>
/// <b>Why the bounding-box centre is wrong.</b> The placeholder
/// <c>IsoBoard</c> anchors cell (0,0) at <c>AnchorPixel.X = (h-1)*halfW</c>
/// so the whole diamond has <c>x &gt;= 0</c>. For a 16×16 grid at tile
/// width 128 (<c>halfW = 64</c>) the diamond's local X runs from
/// <c>-halfW</c> to <c>(w+h-1)*halfW</c> — i.e. <c>-64</c> to <c>1984</c>,
/// a 2048-wide bounding box whose centre is <c>1024</c>. But the diamond
/// itself — a rhombus for a square grid — is geometrically centred on
/// <c>AnchorPixel.X = 960</c>, the left/right apex X. Parking the camera
/// on <c>1024</c> instead of <c>960</c> pushes the whole maquette 64 px to
/// the LEFT of the screen centre (the camera looks 64 px too far right, so
/// the content slides left). That is the visible lean.
/// </para>
///
/// <para>
/// <b>What this helper returns.</b> The world point — in the maquette
/// board's local pixel space — that the <c>MapCamera2D</c> must be parked
/// on so cell (0,0)'s diamond, and therefore the whole symmetric
/// placeholder rhombus, sits dead-centre of the screen. It is the
/// geometric centre of the iso diamond, derived from the four corner-cell
/// projections — never the bounding box, never a hard-coded number.
/// </para>
///
/// <para>
/// <b>Forward-compatible.</b> When Mira's real district floor bitmap lands,
/// the maquette content extent is the bitmap rect and its geometric centre
/// IS <c>bitmapSize * 0.5</c> — so <see cref="DiamondCentre"/> is only used
/// on the placeholder path; <c>GameScreen</c> keeps using the bitmap-centre
/// shortcut once a texture exists. The helper exists purely to make the
/// placeholder path centre correctly, and to be xUnit-pinned so the lean
/// cannot regress.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Everything is
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2</c> — so
/// this helper is cherry-pickable into the xUnit host with no GodotSharp.
/// <c>GameScreen</c> converts at the engine seam. Same logic-vs-node split
/// as <see cref="DeskClipFrontierLogic"/> and <see cref="DeskFloorRectLogic"/>.
/// </para>
/// </summary>
public static class MaquetteContentLogic
{
    /// <summary>
    /// The geometric centre of the placeholder iso diamond, in the maquette
    /// board's local pixel space — the point the <c>MapCamera2D</c> must be
    /// parked on so the diamond is centred on the screen.
    ///
    /// <para>
    /// The placeholder grid is the rectangle <c>(0,0)..(w-1,h-1)</c>; its
    /// iso 2:1 projection is a rhombus. The four apexes of that rhombus are
    /// the four corner-cell centres pushed out by a half-diamond. The
    /// geometric centre is the average of the top and bottom apexes (X) and
    /// of the left and right apexes (Y) — equivalently the average of all
    /// four apexes. This is the SAME centre the clip shader's diamond is
    /// built around, so parking the camera here aligns the content with the
    /// clip.
    /// </para>
    /// </summary>
    /// <param name="gridWidth">Placeholder grid width in cells. Positive.</param>
    /// <param name="gridHeight">Placeholder grid height in cells. Positive.</param>
    /// <param name="tileWidthPx">
    /// The iso cell diamond width in pixels (a positive multiple of 4 — the
    /// <c>IsoProjection.Iso2To1</c> contract). Height is <c>width / 2</c>.
    /// </param>
    /// <returns>
    /// The diamond's geometric centre in the board's local pixel space.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// If a grid dimension is non-positive or the tile width is not a
    /// positive multiple of 4.
    /// </exception>
    public static SysVec2 DiamondCentre(int gridWidth, int gridHeight, int tileWidthPx)
    {
        if (gridWidth <= 0 || gridHeight <= 0)
        {
            throw new System.ArgumentException(
                $"Grid dimensions must be positive; got " +
                $"{gridWidth}x{gridHeight}.", nameof(gridWidth));
        }
        if (tileWidthPx <= 0 || tileWidthPx % 4 != 0)
        {
            throw new System.ArgumentException(
                $"Tile width must be a positive multiple of 4; got " +
                $"{tileWidthPx}.", nameof(tileWidthPx));
        }

        float halfW = tileWidthPx * 0.5f;
        float halfH = tileWidthPx * 0.25f; // 2:1 ratio -> height = width / 2

        // The placeholder IsoBoard anchors cell (0,0) at
        // AnchorPixel.X = (h-1)*halfW, AnchorPixel.Y = 0, and projects
        // cell (col,row) to ( (col-row)*halfW + anchorX, (col+row)*halfH ).
        float anchorX = (gridHeight - 1) * halfW;

        // Corner-cell CENTRES (the same four cells IsoBoard.GridBoundingDiamond
        // uses): (0,0) top, (w-1,0) right, (w-1,h-1) bottom, (0,h-1) left.
        // The diamond apexes sit a half-diamond beyond these centres, but a
        // symmetric push cancels in the average — so the geometric centre is
        // simply the average of the four corner-cell centres.
        float topX = (0 - 0) * halfW + anchorX;
        float topY = (0 + 0) * halfH;
        float rightX = (gridWidth - 1 - 0) * halfW + anchorX;
        float rightY = (gridWidth - 1 + 0) * halfH;
        float bottomX = (gridWidth - 1 - (gridHeight - 1)) * halfW + anchorX;
        float bottomY = (gridWidth - 1 + gridHeight - 1) * halfH;
        float leftX = (0 - (gridHeight - 1)) * halfW + anchorX;
        float leftY = (0 + gridHeight - 1) * halfH;

        // Geometric centre: average of the four apex/corner-centre points.
        // For a square grid this collapses to (anchorX, (h-1)*halfH) — the
        // diamond's true centre, NOT the bounding-box centre.
        float centreX = (topX + rightX + bottomX + leftX) * 0.25f;
        float centreY = (topY + rightY + bottomY + leftY) * 0.25f;
        return new SysVec2(centreX, centreY);
    }
}
