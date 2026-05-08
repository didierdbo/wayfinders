using System.Collections.Generic;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for the slice 2 palette source contract
/// <see cref="IFogPaletteSource"/> + <see cref="PaletteRgb"/> (livrable 1
/// — M3 / L1 World fondations / 2026-05-08). The Godot-bound
/// <c>BakedFogPaletteSource</c> reader stays in the client csproj only
/// (it depends on <c>ResourceLoader</c> + <c>Image.GetPixel</c>) ;
/// this suite covers the engine-free seam every consumer agrees on.
///
/// <para>
/// <b>What we test here.</b>
/// <list type="bullet">
///   <item><see cref="PaletteRgb.NeutralCarton"/> matches the
///         <c>FogTileLayer.CartonBaseTint</c> RGB so the fallback
///         swatch reads as carton-on-carton (visual continuity).</item>
///   <item><see cref="PaletteRgb.FallbackPalette"/> always returns
///         exactly 3 elements, matching the
///         <see cref="IFogPaletteSource.GetPaletteForCell"/> contract.</item>
///   <item>The interface accepts a fake implementation (smoke test
///         that the contract is reachable without Godot ; the
///         renderer can be mocked at the seam if a future test
///         needs it).</item>
/// </list>
/// </para>
/// </summary>
public sealed class FogPaletteSourceTests
{
    [Fact]
    public void Neutral_carton_matches_fog_tile_layer_base_tint_rgb()
    {
        // Slice 2 visual continuity : the fallback swatch (when the
        // bake doesn't exist) should read as carton-on-carton, not
        // as a clashing default. The values mirror
        // FogTileLayer.CartonBaseTint = Color(0.91f, 0.85f, 0.72f) —
        // if either side drifts, the fallback fog tiles look broken
        // even though both endpoints are individually consistent.
        Assert.Equal(0.91f, PaletteRgb.NeutralCarton.R, precision: 3);
        Assert.Equal(0.85f, PaletteRgb.NeutralCarton.G, precision: 3);
        Assert.Equal(0.72f, PaletteRgb.NeutralCarton.B, precision: 3);
    }

    [Fact]
    public void Fallback_palette_returns_exactly_three_elements()
    {
        // Contract : GetPaletteForCell always returns an array of
        // length 3 (darkest → brightest, even when "darkest" and
        // "brightest" are identical fallbacks). The renderer's
        // 3-band swatch loop assumes this.
        var palette = PaletteRgb.FallbackPalette();
        Assert.Equal(3, palette.Length);
    }

    [Fact]
    public void Fallback_palette_all_three_entries_are_neutral_carton()
    {
        // The fallback strategy is "uniform neutral carton, three
        // identical bands" so the swatch reads as a flat colored
        // band — visually quiet, lets the carton itself dominate.
        var palette = PaletteRgb.FallbackPalette();
        foreach (var teinte in palette)
        {
            Assert.Equal(PaletteRgb.NeutralCarton, teinte);
        }
    }

    [Fact]
    public void Fallback_palette_returns_independent_arrays_each_call()
    {
        // Each call allocates a fresh array — a downstream mutation
        // (e.g. the renderer modulating one band) cannot poison
        // subsequent lookups. Slice 2 doesn't need the immutability
        // strictly speaking, but the contract is cheap to honour
        // and prevents a class of action-at-a-distance bugs.
        var a = PaletteRgb.FallbackPalette();
        var b = PaletteRgb.FallbackPalette();
        Assert.NotSame(a, b);
    }

    [Fact]
    public void Interface_accepts_inline_fake_implementation()
    {
        // Smoke test : the IFogPaletteSource contract is reachable
        // from xUnit without Godot. If a future test needs to mock
        // the source (e.g. assert the renderer reads correct cells)
        // this fake is the pattern.
        var fake = new FakePaletteSource();
        var palette = fake.GetPaletteForCell(new GridCoord(2, 3));

        Assert.Equal(3, palette.Length);
        Assert.Equal(0.1f, palette[0].R, precision: 3);
        Assert.Equal(0.5f, palette[1].R, precision: 3);
        Assert.Equal(0.9f, palette[2].R, precision: 3);
    }

    private sealed class FakePaletteSource : IFogPaletteSource
    {
        public PaletteRgb[] GetPaletteForCell(GridCoord coord) => new[]
        {
            new PaletteRgb(0.1f, 0.1f, 0.1f),
            new PaletteRgb(0.5f, 0.5f, 0.5f),
            new PaletteRgb(0.9f, 0.9f, 0.9f),
        };
    }
}
