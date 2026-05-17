using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="IsoSocketGeometryLogic"/> -- the flat iso
/// rhombus rendered by <c>IsoSocketPlaceholder</c> as the centre socle
/// inside the Compagnie panel (E2.2 step 5, Didier-lock 2026-05-17).
/// Mirrors the discipline of <see cref="IsoCharacterGeometryLogicTests"/>
/// : if the rhombus geometry drifts, the socle stops reading as an iso
/// surface and the étape 6 drop target lands in the wrong place.
/// </summary>
public sealed class IsoSocketGeometryLogicTests
{
    private const float W = 160f;

    [Fact]
    public void Rhombus_has_four_vertices()
        => Assert.Equal(4, IsoSocketGeometryLogic.Rhombus(W).Length);

    [Fact]
    public void Rhombus_top_apex_centered_at_origin_y()
    {
        var pts = IsoSocketGeometryLogic.Rhombus(W);
        Assert.Equal(W * 0.5f, pts[0].X);
        Assert.Equal(0f, pts[0].Y);
    }

    [Fact]
    public void Rhombus_right_apex_at_full_width_mid_height()
    {
        var pts = IsoSocketGeometryLogic.Rhombus(W);
        Assert.Equal(W, pts[1].X);
        Assert.Equal(W * 0.25f, pts[1].Y);   // H/2 = (W/2)/2 = W/4
    }

    [Fact]
    public void Rhombus_bottom_apex_at_full_height()
    {
        var pts = IsoSocketGeometryLogic.Rhombus(W);
        Assert.Equal(W * 0.5f, pts[2].X);
        Assert.Equal(W * 0.5f, pts[2].Y);    // H = W/2
    }

    [Fact]
    public void Rhombus_left_apex_at_origin_mid_height()
    {
        var pts = IsoSocketGeometryLogic.Rhombus(W);
        Assert.Equal(0f, pts[3].X);
        Assert.Equal(W * 0.25f, pts[3].Y);
    }

    [Fact]
    public void Locked_aspect_ratio_is_two_to_one()
    {
        // Pins the projection invariant : the rhombus is always twice
        // as wide on-screen as it is tall. Drifting this away from 2:1
        // breaks the iso reading -- it would look like a generic
        // diamond, not an iso tile.
        Assert.Equal(2f, IsoSocketGeometryLogic.WidthOverHeight);
        Assert.Equal(W / 2f, IsoSocketGeometryLogic.HeightFor(W));
    }

    [Fact]
    public void Bounding_box_matches_W_and_H()
    {
        var pts = IsoSocketGeometryLogic.Rhombus(W);
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        for (int i = 0; i < pts.Length; i++)
        {
            var x = pts[i].X;
            var y = pts[i].Y;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        Assert.Equal(0f, minX);
        Assert.Equal(W, maxX);
        Assert.Equal(0f, minY);
        Assert.Equal(W / 2f, maxY);
    }

    [Fact]
    public void Height_for_is_a_pure_division()
    {
        Assert.Equal(50f, IsoSocketGeometryLogic.HeightFor(100f));
        Assert.Equal(80f, IsoSocketGeometryLogic.HeightFor(160f));
        Assert.Equal(240f, IsoSocketGeometryLogic.HeightFor(480f));
    }
}
