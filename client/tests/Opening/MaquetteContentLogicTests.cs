using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="MaquetteContentLogic"/> — the J3c-1sexies
/// maquette-centring geometry. Pins the contract that the
/// <c>MapCamera2D</c> parks on the maquette content's <b>geometric</b>
/// centre, not its bounding-box centre.
///
/// <para>
/// <b>The regression this suite locks (Didier, 2026-05-21 — "the maquette
/// diamond leans to the left").</b> <c>GameScreen._Ready</c> parked the pan
/// camera on <c>contentSize * 0.5</c> — the bounding-box centre. The
/// triangular clip diamond is centred on <c>screenWidth / 2</c>; for the iso
/// placeholder grid the content's bounding-box centre and the diamond's true
/// geometric centre do NOT coincide, so the maquette content slid left of
/// the clip diamond. <see cref="DiamondCentre"/> returns the geometric
/// centre; <see cref="Diamond_centre_differs_from_the_bounding_box_centre_for_a_non_square_offset"/>
/// is the dedicated guard that the two values are genuinely distinct (so a
/// future "just use boundingBox/2" shortcut goes red here, not at an F6
/// capture).
/// </para>
/// </summary>
public sealed class MaquetteContentLogicTests
{
    // The J3a maquette placeholder, as authored in GameScreen.tscn:
    // a 16x16 grid at tile width 128.
    private const int GridW = 16;
    private const int GridH = 16;
    private const int TileW = 128;

    // --- The centring contract --------------------------------------------

    [Fact]
    public void Diamond_centre_of_a_square_grid_is_the_anchor_x()
    {
        // For a SQUARE placeholder grid the diamond is a rhombus whose
        // geometric centre X is exactly the IsoBoard anchor X
        // ((h-1)*halfW), and whose centre Y is (h-1)*halfH. This is the
        // value the camera must park on — NOT the bounding-box centre.
        var centre = MaquetteContentLogic.DiamondCentre(GridW, GridH, TileW);

        float halfW = TileW * 0.5f;   // 64
        float halfH = TileW * 0.25f;  // 32
        float expectedX = (GridH - 1) * halfW; // 15 * 64 = 960
        float expectedY = (GridH - 1) * halfH; // 15 * 32 = 480

        Assert.Equal(expectedX, centre.X, precision: 3);
        Assert.Equal(expectedY, centre.Y, precision: 3);
    }

    [Fact]
    public void Diamond_centre_differs_from_the_bounding_box_centre_for_a_non_square_offset()
    {
        // THE bug guard. The placeholder IsoBoard anchors cell (0,0) so the
        // diamond has x >= 0; that anchoring shifts the bounding box but not
        // the diamond's geometric centre. The bounding box runs from -halfW
        // to (w+h-1)*halfW, so its centre is ((w+h)*halfW)/2 - 0 ... in
        // short: NOT the geometric centre. If a future change reverts to
        // `contentSize * 0.5` this test goes red.
        var centre = MaquetteContentLogic.DiamondCentre(GridW, GridH, TileW);

        float halfW = TileW * 0.5f;
        // The bounding box X span: leftmost cell (0,h-1) -> diamond left
        // apex at x = -halfW; rightmost cell (w-1,0) -> right apex at
        // x = (w+h-1)*halfW + halfW = (w+h)*halfW ... measured from the
        // anchored origin the box runs [-halfW, (w+h-1)*halfW].
        float boxWidth = (GridW + GridH) * halfW; // 32 * 64 = 2048
        float boundingBoxCentreX = boxWidth * 0.5f; // 1024 — the WRONG value

        // The geometric centre (960) and the bounding-box centre (1024)
        // genuinely differ — by exactly halfW (64 px), the left-lean offset.
        Assert.NotEqual(boundingBoxCentreX, centre.X);
        Assert.Equal(halfW, boundingBoxCentreX - centre.X, precision: 3);
    }

    [Fact]
    public void Diamond_centre_is_the_average_of_the_four_apexes()
    {
        // The geometric centre is, by construction, the average of the
        // diamond's four apexes. Re-derive the four apexes here from the
        // projection formula and assert the helper agrees — an independent
        // cross-check of the maths.
        var centre = MaquetteContentLogic.DiamondCentre(GridW, GridH, TileW);

        float halfW = TileW * 0.5f;
        float halfH = TileW * 0.25f;
        float anchorX = (GridH - 1) * halfW;

        // Corner-cell centres (the four cells IsoBoard.GridBoundingDiamond
        // uses): top (0,0), right (w-1,0), bottom (w-1,h-1), left (0,h-1).
        var top = new SysVec2((0 - 0) * halfW + anchorX, (0 + 0) * halfH);
        var right = new SysVec2((GridW - 1) * halfW + anchorX, (GridW - 1) * halfH);
        var bottom = new SysVec2(
            (GridW - 1 - (GridH - 1)) * halfW + anchorX,
            (GridW - 1 + GridH - 1) * halfH);
        var left = new SysVec2(
            (0 - (GridH - 1)) * halfW + anchorX, (GridH - 1) * halfH);

        var avg = (top + right + bottom + left) * 0.25f;
        Assert.Equal(avg.X, centre.X, precision: 3);
        Assert.Equal(avg.Y, centre.Y, precision: 3);
    }

    [Fact]
    public void Diamond_centre_x_is_symmetric_so_the_maquette_does_not_lean()
    {
        // The decisive behaviour test for the left-lean fix: the diamond
        // must extend equally far left and right of its geometric centre.
        // If centre.X is correct, the distance from centre to the left apex
        // equals the distance to the right apex — the diamond, parked under
        // the camera, is symmetric on screen, i.e. it does NOT lean.
        var centre = MaquetteContentLogic.DiamondCentre(GridW, GridH, TileW);

        float halfW = TileW * 0.5f;
        float anchorX = (GridH - 1) * halfW;
        // Diamond apex X: left apex of cell (0,h-1) pushed out by halfW;
        // right apex of cell (w-1,0) pushed out by halfW.
        float leftApexX = (0 - (GridH - 1)) * halfW + anchorX - halfW;
        float rightApexX = (GridW - 1 - 0) * halfW + anchorX + halfW;

        float distLeft = centre.X - leftApexX;
        float distRight = rightApexX - centre.X;
        Assert.Equal(distLeft, distRight, precision: 3);
    }

    // --- Argument validation ----------------------------------------------

    [Fact]
    public void DiamondCentre_throws_on_a_non_positive_grid()
    {
        Assert.Throws<System.ArgumentException>(
            () => MaquetteContentLogic.DiamondCentre(0, GridH, TileW));
        Assert.Throws<System.ArgumentException>(
            () => MaquetteContentLogic.DiamondCentre(GridW, -1, TileW));
    }

    [Fact]
    public void DiamondCentre_throws_on_a_tile_width_not_a_multiple_of_four()
    {
        // The IsoProjection.Iso2To1 contract: tile width must be a positive
        // multiple of 4 so the derived half-height stays even.
        Assert.Throws<System.ArgumentException>(
            () => MaquetteContentLogic.DiamondCentre(GridW, GridH, 0));
        Assert.Throws<System.ArgumentException>(
            () => MaquetteContentLogic.DiamondCentre(GridW, GridH, 130));
    }
}
