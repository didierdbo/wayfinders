using System.Numerics;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Services.Iso;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="IsoProjection"/> — the single source of
/// truth for cell ↔ pixel conversion in the J3-iso hybrid socle. This is
/// the testable core of the socle: the design brief (Didier 2026-05-21)
/// is explicit that J3-iso writes "only testable coordinate conversions".
///
/// <para>
/// The load-bearing tests are the <b>round-trip</b> ones — for any cell
/// <c>c</c>, <c>WorldToCell(CellToWorld(c)) == c</c>. If they drift, every
/// entity placement and every analytic pick lands in the wrong cell.
/// </para>
/// </summary>
public sealed class IsoProjectionTests
{
    // A standard 2:1 board: 128-wide diamonds (64 tall), anchored at the
    // origin so the arithmetic in the pins reads cleanly.
    private static IsoProjection Standard()
        => IsoProjection.Iso2To1(128, Vector2.Zero);

    private static TileCoordinate Cell(int col, int row) => new(col, row);

    [Fact]
    public void Iso2To1_derives_half_height()
    {
        var p = IsoProjection.Iso2To1(128, Vector2.Zero);
        Assert.Equal(128, p.TileWidth);
        Assert.Equal(64, p.TileHeight);
        Assert.Equal(2f, IsoProjection.WidthOverHeight);
    }

    [Fact]
    public void CellToWorld_origin_lands_on_anchor()
    {
        var anchor = new Vector2(500f, 120f);
        var p = IsoProjection.Iso2To1(128, anchor);
        Assert.Equal(anchor, p.CellToWorld(Cell(0, 0)));
    }

    [Fact]
    public void CellToWorld_plus_x_moves_down_right()
    {
        // Increasing cx by 1: +halfW on X, +halfH on Y (screen down-right).
        var p = Standard();
        Assert.Equal(new Vector2(64f, 32f), p.CellToWorld(Cell(1, 0)));
    }

    [Fact]
    public void CellToWorld_plus_y_moves_down_left()
    {
        // Increasing cy by 1: -halfW on X, +halfH on Y (screen down-left).
        var p = Standard();
        Assert.Equal(new Vector2(-64f, 32f), p.CellToWorld(Cell(0, 1)));
    }

    [Fact]
    public void CellToWorld_diagonal_sums_on_y_only()
    {
        // (1,1): X cancels (cx - cy = 0), Y is (cx + cy) * halfH = 2*32.
        var p = Standard();
        Assert.Equal(new Vector2(0f, 64f), p.CellToWorld(Cell(1, 1)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(7, 7)]
    [InlineData(23, 17)]
    [InlineData(-4, -9)]   // negative coords must round-trip too
    [InlineData(40, -3)]
    public void RoundTrip_cell_to_world_to_cell_is_identity(int cx, int cy)
    {
        // The load-bearing invariant: WorldToCell o CellToWorld == id.
        var p = Standard();
        var cell = Cell(cx, cy);
        Assert.Equal(cell, p.WorldToCell(p.CellToWorld(cell)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    [InlineData(12, 5)]
    public void RoundTrip_holds_with_a_non_zero_anchor(int cx, int cy)
    {
        // The anchor offset must not break the inverse.
        var p = IsoProjection.Iso2To1(256, new Vector2(1024f, 333f));
        var cell = Cell(cx, cy);
        Assert.Equal(cell, p.WorldToCell(p.CellToWorld(cell)));
    }

    [Fact]
    public void WorldToCell_snaps_a_point_inside_a_diamond_to_that_cell()
    {
        // A point a few pixels off the centre of cell (3,2) still resolves
        // to (3,2) — picking tolerates being inside the diamond, not just
        // dead-centre.
        var p = Standard();
        var centre = p.CellToWorld(Cell(3, 2));
        Assert.Equal(Cell(3, 2), p.WorldToCell(centre + new Vector2(10f, 5f)));
        Assert.Equal(Cell(3, 2), p.WorldToCell(centre - new Vector2(10f, 5f)));
    }

    [Fact]
    public void WorldToCell_is_deterministic_on_a_diamond_edge()
    {
        // A point exactly between two cell centres must resolve the same
        // way every call (the rounding rule is fixed, not a coin flip).
        var p = Standard();
        var midpoint = (p.CellToWorld(Cell(0, 0)) + p.CellToWorld(Cell(1, 0))) * 0.5f;
        var first = p.WorldToCell(midpoint);
        Assert.Equal(first, p.WorldToCell(midpoint));
    }

    [Fact]
    public void No_y_inversion_plus_y_increases_screen_y()
    {
        // Trap guard: the FogYOffset bug came from inverting Y. A higher
        // cell sum must mean a larger (further down) screen Y, never
        // smaller.
        var p = Standard();
        float y0 = p.CellToWorld(Cell(0, 0)).Y;
        float y1 = p.CellToWorld(Cell(2, 3)).Y;
        Assert.True(y1 > y0, $"expected y1 ({y1}) > y0 ({y0})");
    }

    [Fact]
    public void Constructor_rejects_odd_tile_width()
        => Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new IsoProjection(127, 64, Vector2.Zero));

    [Fact]
    public void Constructor_rejects_non_positive_tile_height()
        => Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new IsoProjection(128, 0, Vector2.Zero));

    [Fact]
    public void Iso2To1_rejects_width_not_multiple_of_four()
        // 130 is even but 130/2 = 65 is odd — the derived height would be
        // odd and the round-trip would drift. The factory must reject it.
        => Assert.Throws<System.ArgumentOutOfRangeException>(
            () => IsoProjection.Iso2To1(130, Vector2.Zero));
}
