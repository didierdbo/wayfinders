using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin AMENDMENT A4.7 (Varn-lock 2026-05-16) for the cross-layer reveal
/// projection cascade. Each test anchors one rule (R3, R5) or one
/// edge-case (EC10, EC11, EC12) from the locked spec.
///
/// <para>
/// <b>Why these tests are critical.</b> The projector is the seam that
/// turns "mission lists cells at ONE layer" into "every layer's render
/// state is consistent". A silent drift (off-by-one in the 2×2 block,
/// no-downgrade clamp lapses, etc.) would surface as visible reveal
/// glitches in E2.1b smoke or worse — silent data corruption on the
/// E1↔E2 round trip once both layers render production maps.
/// </para>
/// </summary>
public sealed class TileRevealProjectorTests
{
    // Helper : a fresh state map seeded with one (cell, state) entry
    // and a getter that returns Fog for cells absent from the map.
    // Mirrors GameState.GetTileRevealState's sparse-dict semantics.
    private static System.Func<GridCoord, TileRevealState> StateMap(
        Dictionary<GridCoord, TileRevealState>? seed = null)
    {
        var map = seed ?? new Dictionary<GridCoord, TileRevealState>();
        return c => map.TryGetValue(c, out var s) ? s : TileRevealState.Fog;
    }

    // -----------------------------------------------------------------
    // R3 top-down formula (A4.7.4 + A4.7.8 anchor)
    // -----------------------------------------------------------------

    [Fact]
    public void ProjectTopDown_E1_to_E2_maps_one_parent_to_four_children()
    {
        // A4.7.4 R3 : children(L, x, y) = {(2x + dx, 2y + dy) : dx,dy in {0,1}}
        // One E1 cell (1, 2) -> {(2,4), (3,4), (2,5), (3,5)}.
        var args = new ProjectTopDownArgs
        {
            SourceLayer = RevealLayer.E1,
            TargetLayer = RevealLayer.E2,
            SourceCells = new[] { new GridCoord(1, 2) },
            TargetState = TileRevealState.Partial,
            GetCurrentTargetState = StateMap(),
        };

        var result = TileRevealProjector.ProjectTopDown(args);
        var writes = result.CellsToWrite.Select(w => w.cell).ToHashSet();

        Assert.Equal(4, writes.Count);
        Assert.Contains(new GridCoord(2, 4), writes);
        Assert.Contains(new GridCoord(3, 4), writes);
        Assert.Contains(new GridCoord(2, 5), writes);
        Assert.Contains(new GridCoord(3, 5), writes);
    }

    [Fact]
    public void ProjectTopDown_E1_to_E2_block_origin_lands_at_zero_zero()
    {
        // R3 corner case : the parent (0, 0) maps to the 4 children
        // {(0,0), (1,0), (0,1), (1,1)}. This is the natural anchor
        // HalfgateMissionAuthoring.HalfgateE1Footprint uses so the
        // projection lands cleanly on the E2 grid's (0..7, 0..7).
        var args = new ProjectTopDownArgs
        {
            SourceLayer = RevealLayer.E1,
            TargetLayer = RevealLayer.E2,
            SourceCells = new[] { new GridCoord(0, 0) },
            TargetState = TileRevealState.Partial,
            GetCurrentTargetState = StateMap(),
        };

        var writes = TileRevealProjector.ProjectTopDown(args)
            .CellsToWrite.Select(w => w.cell).ToHashSet();
        Assert.Equal(
            new HashSet<GridCoord>
            {
                new(0, 0), new(1, 0), new(0, 1), new(1, 1),
            },
            writes);
    }

    [Fact]
    public void ProjectTopDown_halfgate_4x4_footprint_yields_full_8x8_e2_grid()
    {
        // The canonical Halfgate boot scenario (A4.7.6 worked example).
        // 16 E1 cells of the 4x4 footprint at (0..3, 0..3) project to
        // the entire 64-cell E2 grid (0..7, 0..7). This is exactly what
        // the E2.1b smoke test relies on : the player sees all 64
        // tiles flip fog -> partial at boot.
        var args = new ProjectTopDownArgs
        {
            SourceLayer = RevealLayer.E1,
            TargetLayer = RevealLayer.E2,
            SourceCells = HalfgateMissionAuthoring.HalfgateE1Footprint,
            TargetState = TileRevealState.Partial,
            GetCurrentTargetState = StateMap(),
        };

        var result = TileRevealProjector.ProjectTopDown(args);
        Assert.Equal(64, result.CellsToWrite.Count);

        // Every (col, row) in [0..7] x [0..7] is present exactly once.
        var distinct = result.CellsToWrite.Select(w => w.cell).ToHashSet();
        Assert.Equal(64, distinct.Count);
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                Assert.Contains(new GridCoord(col, row), distinct);
            }
        }

        // All transitions go to Partial (no surprise downgrades).
        Assert.All(result.CellsToWrite, w => Assert.Equal(TileRevealState.Partial, w.newState));
    }

    // -----------------------------------------------------------------
    // R5 + EC10 + EC11 : no-downgrade across layers
    // -----------------------------------------------------------------

    [Fact]
    public void ProjectTopDown_does_not_downgrade_cells_already_revealed()
    {
        // EC11 : if an E2 cell projected from an E1 mission is already
        // Revealed, the projection is a no-op on that cell. The cell
        // is omitted from the write list entirely (mirroring the
        // idempotent SetTileRevealState contract -- no signal for a
        // no-change write).
        var preExisting = new Dictionary<GridCoord, TileRevealState>
        {
            // Suppose E2 cell (0, 0) was already revealed by some prior
            // path (e.g. an NPC recruit, or a successful mission).
            [new GridCoord(0, 0)] = TileRevealState.Revealed,
        };
        var args = new ProjectTopDownArgs
        {
            SourceLayer = RevealLayer.E1,
            TargetLayer = RevealLayer.E2,
            SourceCells = new[] { new GridCoord(0, 0) }, // projects to (0,0) (1,0) (0,1) (1,1)
            TargetState = TileRevealState.Partial,
            GetCurrentTargetState = StateMap(preExisting),
        };

        var result = TileRevealProjector.ProjectTopDown(args);
        // 3 writes (the other 3 children), NOT 4 -- the revealed cell
        // is silently skipped.
        Assert.Equal(3, result.CellsToWrite.Count);
        Assert.DoesNotContain(
            result.CellsToWrite,
            w => w.cell == new GridCoord(0, 0));
    }

    [Fact]
    public void ProjectTopDown_does_not_downgrade_cells_already_partial()
    {
        // EC2 / EC10 corner : a cell already at Partial stays Partial
        // when the projection also wants Partial -- the no-downgrade
        // clamp produces no change. The write set drops the no-op.
        var preExisting = new Dictionary<GridCoord, TileRevealState>
        {
            [new GridCoord(2, 3)] = TileRevealState.Partial,
        };
        var args = new ProjectTopDownArgs
        {
            SourceLayer = RevealLayer.E1,
            TargetLayer = RevealLayer.E2,
            SourceCells = new[] { new GridCoord(1, 1) }, // -> (2,2)(3,2)(2,3)(3,3)
            TargetState = TileRevealState.Partial,
            GetCurrentTargetState = StateMap(preExisting),
        };

        var result = TileRevealProjector.ProjectTopDown(args);
        // (2, 3) drops out as a no-op. The other 3 children write.
        Assert.Equal(3, result.CellsToWrite.Count);
        Assert.DoesNotContain(
            result.CellsToWrite,
            w => w.cell == new GridCoord(2, 3));
    }

    [Fact]
    public void ProjectTopDown_upgrades_partial_to_revealed_when_target_is_revealed()
    {
        // The clamp is max(current, projected) per R3 / R5 -- not "if
        // current is fog, set to projected". A Partial cell projected
        // with target Revealed UPGRADES to Revealed.
        var preExisting = new Dictionary<GridCoord, TileRevealState>
        {
            [new GridCoord(0, 0)] = TileRevealState.Partial,
        };
        var args = new ProjectTopDownArgs
        {
            SourceLayer = RevealLayer.E1,
            TargetLayer = RevealLayer.E2,
            SourceCells = new[] { new GridCoord(0, 0) },
            TargetState = TileRevealState.Revealed,
            GetCurrentTargetState = StateMap(preExisting),
        };

        var result = TileRevealProjector.ProjectTopDown(args);
        Assert.Equal(4, result.CellsToWrite.Count);
        var upgrade = result.CellsToWrite
            .First(w => w.cell == new GridCoord(0, 0));
        Assert.Equal(TileRevealState.Revealed, upgrade.newState);
    }

    // -----------------------------------------------------------------
    // ClampNoDowngrade (R5 unit) + LayerRatioMatrix
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(TileRevealState.Fog,      TileRevealState.Fog,      TileRevealState.Fog)]
    [InlineData(TileRevealState.Fog,      TileRevealState.Partial,  TileRevealState.Partial)]
    [InlineData(TileRevealState.Fog,      TileRevealState.Revealed, TileRevealState.Revealed)]
    [InlineData(TileRevealState.Partial,  TileRevealState.Fog,      TileRevealState.Partial)]   // no downgrade
    [InlineData(TileRevealState.Partial,  TileRevealState.Partial,  TileRevealState.Partial)]
    [InlineData(TileRevealState.Partial,  TileRevealState.Revealed, TileRevealState.Revealed)]  // upgrade
    [InlineData(TileRevealState.Revealed, TileRevealState.Fog,      TileRevealState.Revealed)]  // no downgrade
    [InlineData(TileRevealState.Revealed, TileRevealState.Partial,  TileRevealState.Revealed)]  // no downgrade
    [InlineData(TileRevealState.Revealed, TileRevealState.Revealed, TileRevealState.Revealed)]
    public void ClampNoDowngrade_returns_max_of_current_and_projected(
        TileRevealState current, TileRevealState projected, TileRevealState expected)
    {
        Assert.Equal(expected, TileRevealProjector.ClampNoDowngrade(current, projected));
    }

    [Fact]
    public void LayerRatioMatrix_locked_values_e1_to_e2_equals_two()
    {
        // A4.7.8 anti-drift anchor : changing E1ToE2 from 2 to anything
        // else requires a Varn pass. The test value here is hardcoded
        // (not pulled from LayerRatioMatrix.E1ToE2) so a silent edit of
        // the constant surfaces.
        Assert.Equal(2, LayerRatioMatrix.E1ToE2);
        Assert.Equal(2, LayerRatioMatrix.ResolveRatio(RevealLayer.E1, RevealLayer.E2));
    }

    [Fact]
    public void LayerRatioMatrix_locked_values_e2_to_e3_equals_two()
    {
        Assert.Equal(2, LayerRatioMatrix.E2ToE3);
        Assert.Equal(2, LayerRatioMatrix.ResolveRatio(RevealLayer.E2, RevealLayer.E3));
    }

    [Fact]
    public void LayerRatioMatrix_ResolveRatio_throws_for_non_adjacent_steps()
    {
        // Only consecutive top-down steps are defined. E1 -> E3
        // directly is undefined ; the caller composes single-step
        // cascades via repeated calls.
        Assert.Throws<System.ArgumentException>(
            () => LayerRatioMatrix.ResolveRatio(RevealLayer.E1, RevealLayer.E3));
        // Same direction enforced : E2 -> E1 is parent direction, not
        // a top-down step.
        Assert.Throws<System.ArgumentException>(
            () => LayerRatioMatrix.ResolveRatio(RevealLayer.E2, RevealLayer.E1));
    }

    [Fact]
    public void ProjectTopDown_throws_for_non_adjacent_layers()
    {
        // Multi-step cascades (E1 -> E3) must be composed by the caller
        // (one E1 -> E2 call, then one E2 -> E3 call). A direct call
        // is a programming error.
        var args = new ProjectTopDownArgs
        {
            SourceLayer = RevealLayer.E1,
            TargetLayer = RevealLayer.E3,
            SourceCells = new[] { new GridCoord(0, 0) },
            TargetState = TileRevealState.Partial,
            GetCurrentTargetState = StateMap(),
        };
        Assert.Throws<System.ArgumentException>(
            () => TileRevealProjector.ProjectTopDown(args));
    }

    // -----------------------------------------------------------------
    // ResolveParentCoord (R4 inverse formula, reserved-but-pinned)
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, 0)] // E2 (0,0) -> E1 (0,0)
    [InlineData(1, 0, 0, 0)] // E2 (1,0) -> E1 (0,0)
    [InlineData(0, 1, 0, 0)] // E2 (0,1) -> E1 (0,0)
    [InlineData(1, 1, 0, 0)] // E2 (1,1) -> E1 (0,0)
    [InlineData(2, 0, 1, 0)] // E2 (2,0) -> E1 (1,0)
    [InlineData(7, 7, 3, 3)] // E2 (7,7) -> E1 (3,3) (south-east corner)
    public void ResolveParentCoord_e2_to_e1_is_floor_division(
        int childCol, int childRow, int expectedParentCol, int expectedParentRow)
    {
        // A4.7.4 R4 : parent(L, u, v) = (u // 2, v // 2). Pin this
        // formula now so the post-MVE bottom-up aggregation cascade
        // does not drift.
        var parent = TileRevealProjector.ResolveParentCoord(
            RevealLayer.E2, new GridCoord(childCol, childRow), RevealLayer.E1);
        Assert.Equal(new GridCoord(expectedParentCol, expectedParentRow), parent);
    }
}
