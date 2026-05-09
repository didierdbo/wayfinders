using System;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Phase9_3DBacking;

/// <summary>
/// Jalon 4 contract tests -- the J3-pinned forward iso projection
/// (<see cref="J3CellAuthorityTests.Witness_2D_position_derived_from_3D_authority_matches_canonical_pre_J3_position"/>)
/// is verified at 4 hand-picked points. J4 generalises to <i>every cell</i>
/// of the grid, so we expand the pin to every coord of a representative
/// E2-sized grid -- if the derivation diverges anywhere on the grid, this
/// test catches it.
///
/// <para>
/// Why we do this in a separate fixture rather than expanding J3's
/// theory : J3 is the "single witness" contract (one cell, the migration
/// foothold), J4 is the "all cells" contract (the generalised path). The
/// J3 fixture stays as-is so the J3 contract is preserved as historical
/// pin -- a future migration that breaks J4 should not silently break J3
/// too.
/// </para>
/// </summary>
public sealed class J4GeneralisationContractTests
{
    private const int CellSizePx = 128;

    /// <summary>
    /// Pin the J4 derivation for every cell of an E2-sized grid
    /// (~30 x 17). The forward iso projection of <c>(col*cs, 0, row*cs)</c>
    /// + iso shift + halfH + fogYOffset must match the canonical
    /// <c>ComputeCellCenter + ComputeIsoOriginShift + (0, FogYOffset)</c>
    /// path to within 1 pixel. If a future refactor "optimises" one of
    /// the two paths and they drift apart, the visible production grid
    /// would shift relative to the 3D-authoritative grid -- catastrophic
    /// silently. The test pins both paths against each other on every
    /// cell so the regression is impossible.
    /// </summary>
    [Fact]
    public void Forward_iso_projection_matches_canonical_2D_path_on_every_cell_of_E2_grid()
    {
        const float fogYOffset = -8f;
        var dims = new GridDimensions(Columns: 30, Rows: 17);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, CellSizePx, GridProjection.IsoDiamondDown);
        var halfH = CellSizePx / 4f;

        var cellsChecked = 0;
        var maxResidual = 0f;
        foreach (var coord in FogTileGridLogic.EnumerateCells(dims))
        {
            // Canonical 2D path -- pre-J3 behaviour.
            var center2D = FogTileGridLogic.ComputeCellCenter(
                coord, CellSizePx, GridProjection.IsoDiamondDown);
            var canonicalX = center2D.X + shift.X;
            var canonicalY = center2D.Y + shift.Y + fogYOffset;

            // J4 path -- forward iso projection from the 3D witness.
            var node3DX = coord.Col * CellSizePx;
            var node3DZ = coord.Row * CellSizePx;
            var screenX = (node3DX - node3DZ) / 2f;
            var screenY = (node3DX + node3DZ) / 4f;
            var derivedX = screenX + shift.X;
            var derivedY = screenY + shift.Y + halfH + fogYOffset;

            var rx = MathF.Abs(canonicalX - derivedX);
            var ry = MathF.Abs(canonicalY - derivedY);
            maxResidual = MathF.Max(maxResidual, MathF.Max(rx, ry));

            Assert.True(
                rx <= 1.0f && ry <= 1.0f,
                $"Cell ({coord.Col}, {coord.Row}) derivation drift : " +
                $"canonical=({canonicalX}, {canonicalY}) derived=({derivedX}, {derivedY}) " +
                $"residual=({rx:F4}, {ry:F4}). " +
                $"J4 generalisation broke -- the 3D-authoritative path no longer matches the canonical 2D path on this cell.");
            cellsChecked++;
        }

        // Sanity : we iterated the whole grid.
        Assert.Equal(dims.TotalCells, cellsChecked);
        // Documentation-by-assertion : the residual stays < 1 px across
        // the whole grid, so the J4 generalisation is pixel-stable.
        Assert.True(maxResidual <= 1.0f,
            $"Max residual across {cellsChecked} cells = {maxResidual:F4} px (budget 1 px).");
    }

    /// <summary>
    /// Pin the J4 invariant : iterating <see cref="FogTileGridLogic.EnumerateCells"/>
    /// returns exactly <c>Columns * Rows</c> coords for an iso grid (the
    /// J4 spawn loop relies on this -- if EnumerateCells starts emitting
    /// duplicates or skipping, the follower count diverges from
    /// <c>TotalCells</c> and the J4 canary log emits the wrong number).
    /// Documents the contract J4 spawn relies on.
    /// </summary>
    [Fact]
    public void EnumerateCells_emits_exactly_TotalCells_distinct_coords()
    {
        var dims = new GridDimensions(Columns: 30, Rows: 17);
        var seen = new System.Collections.Generic.HashSet<GridCoord>();
        var emitted = 0;
        foreach (var coord in FogTileGridLogic.EnumerateCells(dims))
        {
            Assert.True(seen.Add(coord),
                $"Duplicate coord ({coord.Col}, {coord.Row}) emitted by EnumerateCells -- " +
                $"J4 spawn would create two followers on the same cell.");
            emitted++;
        }
        Assert.Equal(dims.TotalCells, emitted);
        Assert.Equal(dims.TotalCells, seen.Count);
    }
}
