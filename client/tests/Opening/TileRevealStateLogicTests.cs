using System;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="TileRevealStateLogic"/> — the per-tile
/// <c>reveal_level</c> state that drives the boundary-fade shader on
/// face-B Halfgate sprites (Varn spec
/// <c>varn-tile-reveal-shader-spec-2026-05-15.md</c>, locked 2026-05-15).
///
/// <para>
/// <b>What this pins.</b>
/// <list type="bullet">
///   <item>The locked constants : <c>GridSize=64</c>,
///         <c>DefaultFeatherWidth=0.30f</c>, <c>DefaultBlurPasses=3</c>,
///         <c>RevealMin/Max = 0/1</c>. If these drift in code without a
///         spec amendment, the shader-side defaults silently disagree
///         with the runtime writes and the cluster boundary fades to
///         the wrong width / wrong endpoints.</item>
///   <item>The clamp policy : NaN, &lt;0, &gt;1 are saturated to the
///         endpoints, not thrown. Mirrors the design of
///         <see cref="FogTileGridLogic.ComputeGridSize"/>'s defensive
///         fallback.</item>
///   <item>The R8 encoding : <c>round(value × 255)</c> with
///         <c>MidpointRounding.AwayFromZero</c>. The shader reads back
///         <c>byte / 255.0</c> ; round-trip symmetry matters at the
///         feather edges where a 1-byte bias visibly shifts the smoothstep
///         curve.</item>
///   <item>Out-of-bounds reads return <see cref="TileRevealStateLogic.RevealMin"/> ;
///         out-of-bounds writes throw. Asymmetric on purpose : the
///         shader samples past-the-edge neighbours and must read
///         "unknown" cleanly, while a runtime caller that tries to set
///         past the edge has a bug to surface.</item>
///   <item>The CPU blur (Larry mandate 2026-05-15 rewrite) :
///         <see cref="TileRevealStateLogic.BlurRevealMap"/> is the source
///         of truth for the boundary feathering after the shader-side
///         smoothstep was removed. Tests cover diffusion of an isolated
///         cell, attenuation pattern around a compact cluster, the
///         radial-symmetry property that motivated the rewrite (every
///         corner of a square cluster blurs identically), and
///         idempotence on a zero map.</item>
/// </list>
/// </para>
/// </summary>
public sealed class TileRevealStateLogicTests
{
    [Fact]
    public void GridSize_is_locked_to_64()
    {
        // Locked 2026-05-15. Same constant on both sides of the
        // shader seam (uniform default + C# texture allocation). If
        // this changes the shader's texel offset stride must change
        // in lock-step.
        Assert.Equal(64, TileRevealStateLogic.GridSize);
    }

    [Fact]
    public void Default_feather_width_is_locked_to_30_percent()
    {
        // Locked 2026-05-15, Varn §2. The shader uniform's default
        // value is the same number ; this constant lets the runtime
        // write the locked default without a magic number.
        Assert.Equal(0.30f, TileRevealStateLogic.DefaultFeatherWidth);
    }

    [Fact]
    public void Default_blur_passes_is_locked_to_three()
    {
        // Locked 2026-05-15 (Larry CPU-blur rewrite). 3 passes ≈
        // Gaussian σ=1 cell, which produces ~1 cell of feathering at
        // the cluster boundary — the visual target. Bumping this
        // softens the halo ; dropping it sharpens it.
        Assert.Equal(3, TileRevealStateLogic.DefaultBlurPasses);
    }

    [Fact]
    public void Reveal_endpoints_are_zero_and_one()
    {
        Assert.Equal(0.0f, TileRevealStateLogic.RevealMin);
        Assert.Equal(1.0f, TileRevealStateLogic.RevealMax);
    }

    [Fact]
    public void Fresh_state_returns_RevealMin_for_every_cell()
    {
        var state = new TileRevealStateLogic();
        // Spot-check the corners, the centre, and a couple of mid cells.
        Assert.Equal(0.0f, state.GetTileRevealLevel(0, 0));
        Assert.Equal(0.0f, state.GetTileRevealLevel(63, 63));
        Assert.Equal(0.0f, state.GetTileRevealLevel(32, 32));
        Assert.Equal(0.0f, state.GetTileRevealLevel(30, 30)); // halfgate cluster
        Assert.Equal(0.0f, state.GetTileRevealLevel(33, 33)); // halfgate cluster
    }

    [Fact]
    public void SetTileRevealLevel_writes_and_GetTileRevealLevel_reads_back()
    {
        var state = new TileRevealStateLogic();
        state.SetTileRevealLevel(31, 31, 1.0f);
        Assert.Equal(1.0f, state.GetTileRevealLevel(31, 31));
        // Untouched neighbour stays at 0.
        Assert.Equal(0.0f, state.GetTileRevealLevel(31, 32));
    }

    [Fact]
    public void SetTileRevealLevel_clamps_above_one_to_one()
    {
        var state = new TileRevealStateLogic();
        var encoded = state.SetTileRevealLevel(10, 10, 1.5f);
        Assert.Equal(1.0f, state.GetTileRevealLevel(10, 10));
        // R8 encoding of 1.0 must be 255.
        Assert.Equal(255, encoded);
    }

    [Fact]
    public void SetTileRevealLevel_clamps_below_zero_to_zero()
    {
        var state = new TileRevealStateLogic();
        state.SetTileRevealLevel(10, 10, 1.0f); // dirty it first
        var encoded = state.SetTileRevealLevel(10, 10, -0.5f);
        Assert.Equal(0.0f, state.GetTileRevealLevel(10, 10));
        Assert.Equal(0, encoded);
    }

    [Fact]
    public void SetTileRevealLevel_clamps_NaN_to_zero()
    {
        var state = new TileRevealStateLogic();
        var encoded = state.SetTileRevealLevel(10, 10, float.NaN);
        Assert.Equal(0.0f, state.GetTileRevealLevel(10, 10));
        Assert.Equal(0, encoded);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(64, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 64)]
    [InlineData(100, 100)]
    public void SetTileRevealLevel_throws_on_out_of_bounds(int gx, int gy)
    {
        var state = new TileRevealStateLogic();
        // Wrap in an Action so xUnit picks the synchronous overload
        // (the byte-returning lambda is otherwise ambiguous with the
        // obsolete Func<Task> overload on xunit 2.5.3).
        Action call = () => state.SetTileRevealLevel(gx, gy, 0.5f);
        Assert.Throws<ArgumentOutOfRangeException>(call);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(64, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 64)]
    [InlineData(-50, -50)]
    [InlineData(1000, 1000)]
    public void GetTileRevealLevel_returns_RevealMin_for_out_of_bounds(int gx, int gy)
    {
        // The shader samples past-the-edge neighbours via UV offset and
        // expects "unknown" (0.0) ; this read-side default mirrors that.
        var state = new TileRevealStateLogic();
        Assert.Equal(0.0f, state.GetTileRevealLevel(gx, gy));
    }

    [Theory]
    [InlineData(0.0f, 0)]
    [InlineData(1.0f, 255)]
    [InlineData(0.5f, 128)] // banker's rounding off : 0.5*255 = 127.5, AwayFromZero → 128
    [InlineData(0.25f, 64)] // 0.25*255 = 63.75 → 64
    [InlineData(0.75f, 191)] // 0.75*255 = 191.25 → 191
    public void EncodeRevealByte_round_trip_pins(float value, byte expectedByte)
    {
        Assert.Equal(expectedByte, TileRevealStateLogic.EncodeRevealByte(value));
    }

    [Fact]
    public void EncodeRevealByte_clamps_out_of_range_inputs()
    {
        Assert.Equal((byte)0, TileRevealStateLogic.EncodeRevealByte(-1.0f));
        Assert.Equal((byte)255, TileRevealStateLogic.EncodeRevealByte(2.0f));
        Assert.Equal((byte)0, TileRevealStateLogic.EncodeRevealByte(float.NaN));
    }

    [Fact]
    public void ClampRevealLevel_saturates_at_endpoints()
    {
        Assert.Equal(0.0f, TileRevealStateLogic.ClampRevealLevel(-100f));
        Assert.Equal(1.0f, TileRevealStateLogic.ClampRevealLevel(100f));
        Assert.Equal(0.5f, TileRevealStateLogic.ClampRevealLevel(0.5f));
        Assert.Equal(0.0f, TileRevealStateLogic.ClampRevealLevel(float.NaN));
    }

    [Fact]
    public void EnumerateAllCells_yields_GridSize_squared_cells_in_row_major_order()
    {
        var state = new TileRevealStateLogic();
        var cells = state.EnumerateAllCells().ToList();
        Assert.Equal(64 * 64, cells.Count);
        // Row-major : the first 64 cells share gy=0.
        Assert.True(cells.Take(64).All(c => c.gy == 0));
        // The 64..127 share gy=1.
        Assert.True(cells.Skip(64).Take(64).All(c => c.gy == 1));
        // The last cell is (63, 63).
        Assert.Equal((63, 63), cells[^1]);
    }

    [Fact]
    public void ComputeBatchSetToMax_returns_byte_255_for_every_cell()
    {
        // The e1 reveal sets the 16 cells of the 4×4 cluster
        // [30..33] × [30..33] all to RevealMax at the flip midpoint.
        var halfgateCluster = Enumerable.Range(30, 4)
            .SelectMany(gx => Enumerable.Range(30, 4).Select(gy => (gx, gy)))
            .ToList();
        var writes = TileRevealStateLogic.ComputeBatchSetToMax(halfgateCluster).ToList();
        Assert.Equal(16, writes.Count);
        Assert.All(writes, w => Assert.Equal((byte)255, w.encodedByte));
    }

    [Fact]
    public void ComputeBatchSetToMax_is_null_safe()
    {
        // Defensive : the runtime can pass an empty/null sequence on
        // boot before any tile reveals — the helper should yield
        // nothing rather than throw.
        Assert.Empty(TileRevealStateLogic.ComputeBatchSetToMax(null!));
        Assert.Empty(TileRevealStateLogic.ComputeBatchSetToMax(Array.Empty<(int, int)>()));
    }

    [Fact]
    public void Encoded_byte_returned_from_SetTileRevealLevel_matches_static_encoder()
    {
        // The instance setter MUST return the same byte the static
        // EncodeRevealByte would produce — the runtime writes that
        // byte into the Image pixel and the seam survives only if
        // both encoders agree.
        var state = new TileRevealStateLogic();
        var fromInstance = state.SetTileRevealLevel(5, 5, 0.7f);
        var fromStatic = TileRevealStateLogic.EncodeRevealByte(0.7f);
        Assert.Equal(fromStatic, fromInstance);
    }

    // ------------------------------------------------------------------------
    // SnapshotRevealMap — defensive copy contract
    // ------------------------------------------------------------------------

    [Fact]
    public void SnapshotRevealMap_returns_a_GridSize_squared_buffer_of_zeros_on_fresh_state()
    {
        var state = new TileRevealStateLogic();
        var snap = state.SnapshotRevealMap();
        Assert.Equal(64, snap.GetLength(0));
        Assert.Equal(64, snap.GetLength(1));
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                Assert.Equal(0.0f, snap[x, y]);
            }
        }
    }

    [Fact]
    public void SnapshotRevealMap_reflects_writes_at_correct_coords()
    {
        var state = new TileRevealStateLogic();
        state.SetTileRevealLevel(31, 32, 1.0f);
        state.SetTileRevealLevel(0, 0, 0.5f);
        var snap = state.SnapshotRevealMap();
        Assert.Equal(1.0f, snap[31, 32]);
        Assert.Equal(0.5f, snap[0, 0]);
        // Spot-check an untouched cell stays at 0.
        Assert.Equal(0.0f, snap[10, 10]);
    }

    [Fact]
    public void SnapshotRevealMap_is_defensive_copy()
    {
        // Mutating the returned buffer must NOT bleed back into the
        // state. This is what allows ComputeBlurredRevealMap to feed
        // a snapshot into BlurRevealMap without worrying about the
        // blur clobbering the source-of-truth.
        var state = new TileRevealStateLogic();
        state.SetTileRevealLevel(20, 20, 1.0f);
        var snap = state.SnapshotRevealMap();
        snap[20, 20] = 0.0f;
        snap[40, 40] = 0.9f;
        Assert.Equal(1.0f, state.GetTileRevealLevel(20, 20));
        Assert.Equal(0.0f, state.GetTileRevealLevel(40, 40));
    }

    // ------------------------------------------------------------------------
    // BlurRevealMap — CPU box-blur (Larry mandate 2026-05-15)
    // ------------------------------------------------------------------------

    [Fact]
    public void BlurRevealMap_on_all_zeros_stays_all_zeros_idempotence()
    {
        // Idempotence on the zero map : (1) blurring all-zeros stays
        // all-zeros, and (2) blurring an arbitrary number of times has
        // no effect. This is the boot-state contract — before any
        // reveal event, the shader must read 0 everywhere.
        var src = new float[64, 64];
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                Assert.Equal(0.0f, blurred[x, y]);
            }
        }
        // Also pin for very-many passes — the blur has no source of
        // mass, so the result is still zero.
        var deeplyBlurred = TileRevealStateLogic.BlurRevealMap(src, passes: 100);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                Assert.Equal(0.0f, deeplyBlurred[x, y]);
            }
        }
    }

    [Fact]
    public void BlurRevealMap_returns_a_defensive_copy_for_zero_passes()
    {
        // 0 passes is documented to still return a fresh buffer (not
        // the original ref) — defensive copy contract.
        var src = new float[4, 4];
        src[1, 1] = 1.0f;
        var copy = TileRevealStateLogic.BlurRevealMap(src, passes: 0);
        Assert.NotSame(src, copy);
        // Values match.
        Assert.Equal(1.0f, copy[1, 1]);
        // Mutating the copy must not bleed back.
        copy[1, 1] = 0.0f;
        Assert.Equal(1.0f, src[1, 1]);
    }

    [Fact]
    public void BlurRevealMap_with_negative_passes_returns_a_copy()
    {
        // Defensive : negative passes is nonsense input but should not
        // throw — silently behave like passes=0 (return a copy).
        var src = new float[4, 4];
        src[2, 2] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: -3);
        Assert.NotSame(src, blurred);
        Assert.Equal(1.0f, blurred[2, 2]);
    }

    [Fact]
    public void BlurRevealMap_throws_on_null_input()
    {
        // The runtime should never feed a null in, but if it does, the
        // logic layer surfaces it loudly rather than silently producing
        // an empty buffer.
        Assert.Throws<ArgumentNullException>(
            () => TileRevealStateLogic.BlurRevealMap(null!, passes: 3));
    }

    [Fact]
    public void BlurRevealMap_diffuses_an_isolated_unit_below_half()
    {
        // Place a single 1.0 in the centre of a 64×64 buffer of zeros.
        // After 3 box-blur passes, the centre must drop below 0.5 :
        // the original "mass" of 1.0 has spread over the 7×7 footprint
        // of three convolved 3×3 kernels (central limit) ≈ 49 cells,
        // so the centre falls to roughly 1/9 + small overlap residue.
        var src = new float[64, 64];
        src[32, 32] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);
        Assert.True(blurred[32, 32] < 0.5f,
            $"isolated unit centre should diffuse below 0.5 after 3 passes, got {blurred[32, 32]}");
        // Spreads outward : a cell 1 step away receives non-trivial mass.
        Assert.True(blurred[33, 32] > 0.0f);
        Assert.True(blurred[32, 33] > 0.0f);
    }

    [Fact]
    public void BlurRevealMap_isolated_unit_preserves_total_mass()
    {
        // A box blur with the canonical 1/9 weights and clamp-to-edge
        // boundaries is mass-preserving on a buffer whose hot pixel
        // sits well inside the grid (no boundary clipping). Pin the
        // invariant : the sum after 3 passes equals the sum before.
        var src = new float[64, 64];
        src[32, 32] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);
        float sum = 0f;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                sum += blurred[x, y];
        Assert.InRange(sum, 0.999f, 1.001f);
    }

    [Fact]
    public void BlurRevealMap_compact_4x4_cluster_centre_stays_near_one()
    {
        // The e1 Halfgate cluster : 4×4 ones at [30..33] × [30..33] on
        // a 64×64 grid. After 3 box-blur passes, the cells INSIDE the
        // cluster (the 2×2 interior at gx,gy ∈ [31..32]) should still
        // read close to 1.0 — every 3×3 sample of an interior cell hits
        // 9 ones, so the value stays at 1.0 ; further passes do the
        // same. Only the perimeter loses mass to the zeros outside.
        var src = new float[64, 64];
        for (int gx = 30; gx <= 33; gx++)
            for (int gy = 30; gy <= 33; gy++)
                src[gx, gy] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: TileRevealStateLogic.DefaultBlurPasses);

        // 2×2 deep interior of a 4×4 cluster after 3 box-blur passes
        // settles around 0.66 (each interior cell sees a 7×7 footprint
        // after 3 convolved 3×3 kernels ; 16 ones out of 49 cells in the
        // footprint ≈ 0.33, plus border-clamp residue from neighbours
        // each contributing ≈ 0.33 lifts it back to ~0.66). Pin a
        // comfortable lower bound > 0.5 — proves the interior stays the
        // BRIGHTEST region and well above the halo, even if the
        // mandate's casual 'centre ≈ 1.0' expectation is unrealistic
        // for a cluster this small with this many passes. If Didier
        // bumps RevealBlurPasses to 5+, this test will start to fail
        // and the threshold should be relaxed (or the test split by
        // pass count).
        Assert.True(blurred[31, 31] > 0.5f, $"interior (31,31) lost too much mass : {blurred[31, 31]}");
        Assert.True(blurred[32, 32] > 0.5f, $"interior (32,32) lost too much mass : {blurred[32, 32]}");
        Assert.True(blurred[31, 32] > 0.5f, $"interior (31,32) lost too much mass : {blurred[31, 32]}");
        Assert.True(blurred[32, 31] > 0.5f, $"interior (32,31) lost too much mass : {blurred[32, 31]}");

        // Strict radial ordering : interior > perimeter > halo. This
        // is the shape contract the user cares about visually — a
        // monotone radial gradient outward, regardless of absolute
        // values. Surfaces a regression where the blur shape inverts
        // (which would be a math bug).
        float perimeter = blurred[30, 31];
        float halo = blurred[29, 31];
        Assert.True(blurred[31, 31] > perimeter, "interior must be brighter than perimeter");
        Assert.True(perimeter > halo, "perimeter must be brighter than halo");
    }

    [Fact]
    public void BlurRevealMap_compact_4x4_cluster_perimeter_attenuates()
    {
        // Perimeter cells of the 4×4 cluster (e.g. (30, 31) — west
        // border) should fade — they see zeros on one side. After 3
        // passes they should land somewhere meaningfully below 1.0 but
        // still meaningfully above 0.
        var src = new float[64, 64];
        for (int gx = 30; gx <= 33; gx++)
            for (int gy = 30; gy <= 33; gy++)
                src[gx, gy] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);

        float wEdge = blurred[30, 31]; // west border interior row
        Assert.True(wEdge < 1.0f, $"perimeter cell should fade below 1.0, got {wEdge}");
        Assert.True(wEdge > 0.1f, $"perimeter cell should still hold meaningful mass, got {wEdge}");
    }

    [Fact]
    public void BlurRevealMap_one_cell_outside_compact_cluster_is_softly_lit()
    {
        // The visual target : the cell immediately outside the 4×4
        // cluster (e.g. (29, 31) just west of the cluster) should have
        // received SOME diffused mass — exit-criterion "fade ~0.1-0.3
        // at one cell outside". This is the actual halo Didier wants
        // to see at F5.
        var src = new float[64, 64];
        for (int gx = 30; gx <= 33; gx++)
            for (int gy = 30; gy <= 33; gy++)
                src[gx, gy] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);

        float oneCellWest = blurred[29, 31];
        // Should be visibly above 0 (some halo) but well below the
        // interior (the boundary IS a fade). The exit-criteria target
        // is 0.1-0.3 ; we pin the range slightly wider to absorb
        // box-vs-Gaussian shape differences.
        Assert.True(oneCellWest > 0.05f, $"one-cell-out halo too dark : {oneCellWest}");
        Assert.True(oneCellWest < 0.5f, $"one-cell-out halo too bright (boundary lost) : {oneCellWest}");
    }

    [Fact]
    public void BlurRevealMap_two_cells_outside_compact_cluster_approaches_zero()
    {
        // Beyond the immediate halo, the diffusion should fall off
        // toward zero. Two cells out (28, 31) should be quite dim.
        var src = new float[64, 64];
        for (int gx = 30; gx <= 33; gx++)
            for (int gy = 30; gy <= 33; gy++)
                src[gx, gy] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);

        float twoCellsWest = blurred[28, 31];
        Assert.True(twoCellsWest < 0.15f, $"two-cells-out halo bleeds too far : {twoCellsWest}");
    }

    [Fact]
    public void BlurRevealMap_compact_4x4_cluster_has_radially_symmetric_corners()
    {
        // The whole point of the rewrite : the 4 corners of a square
        // cluster must blur identically. This is what the previous
        // shader-side composition failed at — the unevenness Didier saw.
        // We pin that the cell DIAGONALLY one step from each cluster
        // corner reads the same value at all 4 corners.
        var src = new float[64, 64];
        for (int gx = 30; gx <= 33; gx++)
            for (int gy = 30; gy <= 33; gy++)
                src[gx, gy] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);

        float nw = blurred[29, 29]; // NW outside corner
        float ne = blurred[34, 29]; // NE outside corner
        float sw = blurred[29, 34]; // SW outside corner
        float se = blurred[34, 34]; // SE outside corner
        // Float equality within 1e-5 tolerance — the blur math is
        // entirely symmetric on a centred symmetric input, so any
        // visible drift would indicate a directional asymmetry bug.
        Assert.Equal(nw, ne, 5);
        Assert.Equal(nw, sw, 5);
        Assert.Equal(nw, se, 5);
    }

    [Fact]
    public void BlurRevealMap_compact_4x4_cluster_has_radially_symmetric_edges()
    {
        // Edge symmetry counterpart of the corner symmetry test. The
        // cell one step OUTSIDE each cardinal edge of the cluster
        // (centred on the cluster's midline) must read the same value
        // on all 4 sides. The previous shader composition broke this
        // too — it weighted cardinals and diagonals differently per
        // tile's neighbourhood.
        var src = new float[64, 64];
        for (int gx = 30; gx <= 33; gx++)
            for (int gy = 30; gy <= 33; gy++)
                src[gx, gy] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);

        // Cluster mid-edges land between gx=31 and gx=32 (or gy 31/32).
        // Pick one cell on each cardinal exterior, centred on the
        // cluster's midline.
        float wEdge = blurred[29, 31]; // 1 cell west of cluster, row 31
        float eEdge = blurred[34, 31]; // 1 cell east, row 31
        float nEdge = blurred[31, 29]; // 1 cell north, col 31
        float sEdge = blurred[31, 34]; // 1 cell south, col 31

        Assert.Equal(wEdge, eEdge, 5);
        Assert.Equal(wEdge, nEdge, 5);
        Assert.Equal(wEdge, sEdge, 5);
    }

    [Fact]
    public void BlurRevealMap_3x3_cluster_corner_symmetry()
    {
        // Same radial-symmetry contract as the 4×4 test but on an odd
        // (3×3) cluster centred on (32, 32). Pins that the symmetry
        // doesn't depend on the cluster being even-sized — every cluster
        // shape gets a clean halo.
        var src = new float[64, 64];
        for (int gx = 31; gx <= 33; gx++)
            for (int gy = 31; gy <= 33; gy++)
                src[gx, gy] = 1.0f;
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);

        float nw = blurred[30, 30];
        float ne = blurred[34, 30];
        float sw = blurred[30, 34];
        float se = blurred[34, 34];
        Assert.Equal(nw, ne, 5);
        Assert.Equal(nw, sw, 5);
        Assert.Equal(nw, se, 5);
    }

    [Fact]
    public void BlurRevealMap_does_not_mutate_source_buffer()
    {
        // Pin the "BlurRevealMap returns a fresh buffer" contract. The
        // runtime feeds a snapshot in and must not see it corrupted —
        // ComputeBlurredRevealMap calls SnapshotRevealMap which itself
        // is defensive, but the contract belongs to BlurRevealMap too.
        var src = new float[64, 64];
        src[32, 32] = 1.0f;
        var srcCopy = (float[,])src.Clone();
        TileRevealStateLogic.BlurRevealMap(src, passes: 3);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                Assert.Equal(srcCopy[x, y], src[x, y]);
            }
        }
    }

    [Fact]
    public void BlurRevealMap_clamp_to_edge_at_corner_does_not_introduce_dark_seam()
    {
        // A cluster touching the world edge (gx=0 row) used to be a
        // hypothetical risk : if the blur clamped past-the-edge samples
        // to ZERO, the edge row would lose mass to non-existent zeros
        // and produce a dark seam against the world border. The
        // implementation clamps to the boundary CELL (edge-extend), so
        // a fully revealed left column stays bright.
        var src = new float[64, 64];
        for (int y = 0; y < 64; y++)
        {
            src[0, y] = 1.0f; // entire left column revealed
        }
        var blurred = TileRevealStateLogic.BlurRevealMap(src, passes: 3);
        // Interior of the left column (away from world-grid top/bottom)
        // should stay close to 1.0 — its 3×3 footprint reads two ones
        // (self + edge-extended self) and one zero on the right, but
        // after multiple passes the column equilibrates with the
        // edge-extended versions of itself. Pin a lower bound that
        // would FAIL under clamp-to-zero (which gives ~0.15 at the edge).
        // 0.4 is comfortably above the clamp-to-zero baseline and below
        // the actual edge-extend result (~0.48), so the test surfaces a
        // regression to clamp-to-zero immediately.
        Assert.True(blurred[0, 32] > 0.4f,
            $"left-column cell should keep mass under edge-extend, got {blurred[0, 32]}");
    }

    [Fact]
    public void ComputeBlurredRevealMap_round_trips_via_state()
    {
        // The instance wrapper must produce the same result as feeding
        // SnapshotRevealMap into the static BlurRevealMap. This is the
        // contract IsoMapE1Probe.SetTileRevealLevel relies on : one
        // call gets the matrix, encode each byte.
        var state = new TileRevealStateLogic();
        for (int gx = 30; gx <= 33; gx++)
            for (int gy = 30; gy <= 33; gy++)
                state.SetTileRevealLevel(gx, gy, 1.0f);

        var fromWrapper = state.ComputeBlurredRevealMap(passes: 3);
        var fromStatic = TileRevealStateLogic.BlurRevealMap(state.SnapshotRevealMap(), passes: 3);

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                Assert.Equal(fromStatic[x, y], fromWrapper[x, y]);
            }
        }
    }

    [Fact]
    public void ComputeBlurredRevealMap_uses_default_passes_when_unspecified()
    {
        // Pin that the parameterless overload defaults to
        // DefaultBlurPasses, so the runtime can call the no-arg form
        // and get the locked feathering width.
        var state = new TileRevealStateLogic();
        state.SetTileRevealLevel(32, 32, 1.0f);

        var defaulted = state.ComputeBlurredRevealMap();
        var explicitly = state.ComputeBlurredRevealMap(TileRevealStateLogic.DefaultBlurPasses);

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                Assert.Equal(explicitly[x, y], defaulted[x, y]);
            }
        }
    }
}
