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
///         <c>DefaultFeatherWidth=0.30f</c>,
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
}
