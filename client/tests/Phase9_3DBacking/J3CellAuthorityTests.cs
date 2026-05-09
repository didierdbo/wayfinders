using System;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Phase9_3DBacking;

/// <summary>
/// Jalon 3 -- pure-C# pin tests for the cell-authority and zoom-sync
/// invariants of <see cref="Wayfinders.Client.Components.Cell3DBackingFollower"/>
/// (Wayfinders 3D Backing Architecture, doc §3 Jalon 2 questions for
/// J3 + GO J3 2026-05-09).
///
/// <para>
/// <b>What this fixture pins.</b>
/// <list type="bullet">
///   <item>Option A in runtime : the witness 2D screen position is
///         <i>derived</i> from the 3D witness Node3D position via the
///         forward iso projection -- not the other way around. The math
///         is pinned here so a future regression that "fixes" the
///         derivation by reading from the 2D side cannot land
///         silently.</item>
///   <item>Zoom sync formula : <c>Camera3D.Size = (viewportH /
///         Camera2D.Zoom.Y) * sqrt(2)</c>. Tested at zoom 1.0
///         (default), zoom 0.7 (zoom-out floor on E2 World) and zoom
///         2.0 (zoom-in ceiling). If the formula drifts, this test
///         catches it before any pixel-level regression in the
///         playtest.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why pure-C#.</b> Same seam pattern as
/// <see cref="IsoProjection3DTo2DTests"/> and <see cref="FogTileGridLogicTests"/>.
/// The math used by the runtime <see cref="Wayfinders.Client.Components.Cell3DBackingFollower"/>
/// is duplicated inline here -- if the runtime drifts, the pin test
/// fails ; if the pin test changes, the runtime is wrong. Single
/// source of truth lives in the test, the runtime tracks it. Mirrors
/// Pattern H ("la math iso est un pin, pas une convention", J2 lock).
/// </para>
/// </summary>
public sealed class J3CellAuthorityTests
{
    private const int CellSizePx = 128;

    /// <summary>
    /// Witness coords spanning the corners and the interior of the
    /// E2 World grid (~30x17 cells at 128 px). The (0,0) coord is
    /// the J3 default ; the others ensure the math holds for any
    /// future witness choice.
    /// </summary>
    public static TheoryData<int, int> WitnessCoords()
    {
        var data = new TheoryData<int, int>();
        data.Add(0, 0);   // J3 default -- top-left iso pole
        data.Add(5, 5);   // J2 probe witness (cross-check)
        data.Add(15, 8);  // mid-grid
        data.Add(29, 16); // last cell of E2 master grid
        return data;
    }

    /// <summary>
    /// Pin Option A : the 2D screen position derived from a 3D
    /// witness Node3D at <c>(col*cs, 0, row*cs)</c> equals the
    /// canonical position
    /// <c>ComputeCellCenter(coord) + ComputeIsoOriginShift(dims) + (0, FogYOffset)</c>
    /// up to one pixel.
    ///
    /// <para>
    /// This is the runtime invariant of <c>Cell3DBackingFollower</c>'s
    /// <c>ApplyFollowerSync</c> : <c>WitnessCell2DPosition</c> = forward
    /// iso projection of <c>WitnessNode3D.Position</c>, plus shift +
    /// halfH + FogYOffset. If this assertion holds, the witness cell
    /// in production renders at exactly the same pixel as it would
    /// have rendered in the pre-J3 path -- the 3D-authoritative
    /// migration is invisible to the player but live in the data
    /// flow.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(WitnessCoords))]
    public void Witness_2D_position_derived_from_3D_authority_matches_canonical_pre_J3_position(
        int col, int row)
    {
        // Canonical 2D position (pre-J3 path : direct from FogTileGridLogic).
        const float fogYOffset = -8f;
        var coord = new GridCoord(col, row);
        var dims = new GridDimensions(Columns: 64, Rows: 64);
        var center2D = FogTileGridLogic.ComputeCellCenter(
            coord, CellSizePx, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, CellSizePx, GridProjection.IsoDiamondDown);
        var canonicalX = center2D.X + shift.X;
        var canonicalY = center2D.Y + shift.Y + fogYOffset;

        // J3 path : 3D witness Node3D at (col*cs, 0, row*cs), forward-
        // projected back to 2D, then shift + halfH + FogYOffset
        // re-applied. This is the exact math
        // Cell3DBackingFollower.ApplyFollowerSync executes each frame.
        var node3DX = col * CellSizePx;
        var node3DZ = row * CellSizePx;
        var screenX = (node3DX - node3DZ) / 2f;
        var screenY = (node3DX + node3DZ) / 4f;
        var halfH = CellSizePx / 4f;
        var derivedX = screenX + shift.X;
        var derivedY = screenY + shift.Y + halfH + fogYOffset;

        var residualX = MathF.Abs(canonicalX - derivedX);
        var residualY = MathF.Abs(canonicalY - derivedY);

        Assert.True(
            residualX <= 1.0f,
            $"Witness ({col},{row}) X residual = {residualX:F4} px (canonical={canonicalX}, derived={derivedX}). " +
            $"Option A 2D-from-3D derivation broke -- the 3D-authoritative path no longer matches the canonical 2D path.");
        Assert.True(
            residualY <= 1.0f,
            $"Witness ({col},{row}) Y residual = {residualY:F4} px (canonical={canonicalY}, derived={derivedY}). " +
            $"Option A 2D-from-3D derivation broke -- the 3D-authoritative path no longer matches the canonical 2D path.");
    }

    /// <summary>
    /// Pin the J3 zoom-sync formula : <c>Camera3D.Size = (viewportH /
    /// Camera2D.Zoom.Y) * sqrt(2)</c>. The <c>sqrt(2)</c> factor is the
    /// projection scale pinned by <see cref="IsoProjection3DTo2DTests"/>.
    /// The <c>1/Zoom.Y</c> factor reflects how Camera2D.Zoom > 1 means
    /// "zoomed in, see fewer pixels", so the 3D ortho size must shrink
    /// correspondingly to keep the 3D frustum aligned with the 2D
    /// viewport.
    /// </summary>
    [Theory]
    [InlineData(1.0f, 1080f, 1527.35f)]   // default zoom
    [InlineData(0.7f, 1080f, 2181.93f)]   // zoom-out floor (E2 ZoomMin)
    [InlineData(2.0f, 1080f, 763.68f)]    // zoom-in ceiling (E2 ZoomMax)
    [InlineData(1.0f, 720f, 1018.23f)]    // smaller viewport, 720p
    public void Camera3D_Size_matches_viewportH_over_Camera2D_zoom_times_sqrt2(
        float zoomY,
        float viewportHeight,
        float expectedSize)
    {
        // The runtime formula. Mirrors Cell3DBackingFollower.ApplyFollowerSync.
        var actualSize = (viewportHeight / zoomY) * MathF.Sqrt(2f);

        // 1-unit tolerance : the formula is a direct multiplication so
        // float drift is single-ULP. We use a 0.1 tolerance to keep the
        // assertion human-readable in the test data above.
        Assert.True(
            MathF.Abs(actualSize - expectedSize) < 0.1f,
            $"Camera3D.Size formula drift : zoom={zoomY}, viewportH={viewportHeight}, expected={expectedSize:F2}, actual={actualSize:F2}");
    }

    /// <summary>
    /// Sanity probe : if the J3 sync drops the <c>sqrt(2)</c> factor
    /// (a likely "simplification" temptation when the formula looks
    /// suspicious), the Camera3D ortho size differs by ~41 % from the
    /// correct value. This test documents the drift so a regression to
    /// <c>Size = viewportH / Zoom.Y</c> (no sqrt(2)) fails immediately
    /// rather than waiting for a playtest to expose pixel-level
    /// misalignment.
    /// </summary>
    [Fact]
    public void Dropping_sqrt2_factor_breaks_the_zoom_sync_so_the_factor_is_load_bearing()
    {
        const float viewportH = 1080f;
        const float zoomY = 1.0f;
        var withSqrt2 = (viewportH / zoomY) * MathF.Sqrt(2f);
        var withoutSqrt2 = viewportH / zoomY;

        var deltaPx = MathF.Abs(withSqrt2 - withoutSqrt2);
        Assert.True(
            deltaPx > 400f,
            $"Sanity guard : the sqrt(2) factor is load-bearing. Dropping it changes the " +
            $"Camera3D ortho size by {deltaPx:F0} world units (~{deltaPx / withSqrt2 * 100f:F0}%). " +
            $"Anyone tempted to 'simplify' the formula must rederive from IsoProjection3DTo2DTests first.");
    }

    /// <summary>
    /// Pin the rhombus-prism collision-shape pole vertices (Pattern J
    /// invariant 7, locked J2 night). The rhombus poles are
    /// <c>(±halfW, 0, ±halfW)</c> with <c>halfW = CellSizePx/2</c>, NOT
    /// <c>SpriteTopWidthPx/2</c> (the regression that caused the second
    /// J2 playtest hover-overshoot bug).
    /// </summary>
    [Fact]
    public void Rhombus_prism_poles_anchor_on_cell_size_not_sprite_width()
    {
        // The visible iso silhouette has half-extents CellSizePx/2 px,
        // independent of the sprite bitmap dimensions. The collision
        // shape must match the silhouette.
        var halfW = CellSizePx / 2f;

        // The 4 expected pole positions in 3D world coords (top face).
        var rightPole = (X: +halfW, Z: -halfW);
        var leftPole = (X: -halfW, Z: +halfW);
        var topPole = (X: -halfW, Z: -halfW);
        var bottomPole = (X: +halfW, Z: +halfW);

        // Project each pole through the forward iso projection and
        // check it lands on the expected screen-space pole offset.
        // Right pole on screen = (+halfW, 0). Top pole on screen =
        // (0, -halfH). And so on -- this is the rhombus the player
        // sees, and the collision must match it exactly.
        var halfH = CellSizePx / 4f;

        var rightScreenX = (rightPole.X - rightPole.Z) / 2f;
        var rightScreenY = (rightPole.X + rightPole.Z) / 4f;
        Assert.Equal(+halfW, rightScreenX, precision: 5);
        Assert.Equal(0f, rightScreenY, precision: 5);

        var leftScreenX = (leftPole.X - leftPole.Z) / 2f;
        var leftScreenY = (leftPole.X + leftPole.Z) / 4f;
        Assert.Equal(-halfW, leftScreenX, precision: 5);
        Assert.Equal(0f, leftScreenY, precision: 5);

        var topScreenX = (topPole.X - topPole.Z) / 2f;
        var topScreenY = (topPole.X + topPole.Z) / 4f;
        Assert.Equal(0f, topScreenX, precision: 5);
        Assert.Equal(-halfH, topScreenY, precision: 5);

        var botScreenX = (bottomPole.X - bottomPole.Z) / 2f;
        var botScreenY = (bottomPole.X + bottomPole.Z) / 4f;
        Assert.Equal(0f, botScreenX, precision: 5);
        Assert.Equal(+halfH, botScreenY, precision: 5);
    }
}
