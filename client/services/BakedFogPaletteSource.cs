using System;
using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Services;

/// <summary>
/// Runtime consumer of the editor-baked palette PNG produced by
/// <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/> (slice 2 /
/// livrable 1 — M3 / L1 World fondations / 2026-05-08). Reads the PNG
/// once at construction, decodes it into a
/// <c>Dictionary&lt;GridCoord, PaletteRgb[]&gt;</c>, and serves
/// <see cref="IFogPaletteSource.GetPaletteForCell"/> queries from memory.
/// No <c>Image.GetPixel</c> at render time — the per-pixel sweep is paid
/// once at scene entry, not per frame, not per pan.
///
/// <para>
/// <b>Format contract (must match <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/>).</b>
/// The baked PNG has dimensions <c>(3 × gridCols, gridRows)</c>. For
/// cell <c>(c, r)</c>, the three teintes are at pixels
/// <c>(3c+0, r)</c>, <c>(3c+1, r)</c>, <c>(3c+2, r)</c>, sorted darkest
/// → brightest by Rec.709 luminance. The bake is deterministic so a
/// given input image always produces byte-identical output, which
/// matters for version control hygiene (commit the PNG, no re-bake
/// drift in diffs).
/// </para>
///
/// <para>
/// <b>Fallback policy (livrable 1 acceptance).</b> If the PNG path does
/// not resolve, fails to load, or has unexpected dimensions, the source
/// constructs in "fallback mode" : every
/// <see cref="GetPaletteForCell"/> call returns
/// <see cref="PaletteRgb.FallbackPalette"/>. The renderer then paints
/// neutral-carton swatches and the slice 2 scaffold remains observable
/// while waiting on the bake (which the editor only produces after
/// Didier flips <c>RunBakeNow</c>). Failure-mode console messages are
/// <see cref="GD.PushWarning"/>, not <see cref="GD.PushError"/>, because
/// pre-bake is the expected first-run state on a fresh checkout —
/// errors would suggest a regression.
/// </para>
///
/// <para>
/// <b>Why a plain class (not a Node/autoload).</b> Slice 2 scope is
/// E2 World only ; the source is owned by
/// <see cref="Wayfinders.Client.Components.FogTileLayer"/> which lives
/// inside the screen. Premature autoloading would tie the lifecycle to
/// /root and force a reset path on screen exit. When slice 3+ widens
/// scope to L2/L3 with their own bakes, each layer can construct its
/// own source pointing at its own PNG — keeps multiple bakes coexisting
/// trivially.
/// </para>
/// </summary>
public sealed class BakedFogPaletteSource : IFogPaletteSource
{
    private readonly Dictionary<GridCoord, PaletteRgb[]> _palettes;
    private readonly bool _fallbackMode;

    /// <summary>
    /// True iff the source is operating in fallback mode (no bake found).
    /// Diagnostic surface for the renderer + tests.
    /// </summary>
    public bool IsFallback => _fallbackMode;

    /// <summary>
    /// Number of cells with explicitly-loaded palettes. Zero in fallback
    /// mode, equal to <c>columns × rows</c> in nominal mode.
    /// </summary>
    public int LoadedCellCount => _palettes.Count;

    /// <summary>
    /// Construct from a res:// path to a baked palette PNG. Does the
    /// load + decode synchronously ; safe to call from
    /// <see cref="Wayfinders.Client.Components.FogTileLayer.Configure"/>
    /// which already runs at scene <c>_Ready</c>.
    /// </summary>
    /// <param name="bakedPaletteResPath">res:// path of the PNG (must
    /// match the <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/>
    /// output for the same world image).</param>
    /// <param name="expectedDimensions">Grid dimensions the renderer will
    /// query against. The decoder validates the PNG is exactly
    /// <c>(3 × cols, rows)</c> ; mismatched dimensions trigger fallback
    /// mode (defensive — slice 2 doesn't tolerate silent off-by-one
    /// from a stale bake).</param>
    public BakedFogPaletteSource(string bakedPaletteResPath, GridDimensions expectedDimensions)
    {
        _palettes = new Dictionary<GridCoord, PaletteRgb[]>();

        var loaded = TryLoadPalettes(bakedPaletteResPath, expectedDimensions, _palettes);
        _fallbackMode = !loaded;

        if (_fallbackMode)
        {
            GD.PushWarning(
                $"[BakedFogPaletteSource] no bake at {bakedPaletteResPath} (or dimensions " +
                $"mismatched ; expected {expectedDimensions.Columns * 3}×{expectedDimensions.Rows}). " +
                "Fallback to neutral-carton palette. Run PaletteQuantizerTool to produce the bake.");
        }
        else
        {
            GD.Print(
                $"[BakedFogPaletteSource] loaded {_palettes.Count} cell palettes from {bakedPaletteResPath} " +
                $"(expected grid {expectedDimensions.Columns}×{expectedDimensions.Rows}).");
        }
    }

    /// <summary>
    /// Fallback-only constructor for tests + transient slice 2 wiring
    /// where there is no PNG at all. All cells return the neutral
    /// carton triple.
    /// </summary>
    public static BakedFogPaletteSource ForFallback() => new();

    private BakedFogPaletteSource()
    {
        _palettes = new Dictionary<GridCoord, PaletteRgb[]>();
        _fallbackMode = true;
    }

    /// <inheritdoc />
    public PaletteRgb[] GetPaletteForCell(GridCoord coord)
    {
        if (_palettes.TryGetValue(coord, out var palette))
        {
            return palette;
        }
        // Off-grid OR fallback mode : neutral carton triple. Allocates
        // a fresh array each call (cheap, called only at fog spawn) so
        // a downstream mutation can't poison subsequent lookups.
        return PaletteRgb.FallbackPalette();
    }

    private static bool TryLoadPalettes(
        string resPath,
        GridDimensions expected,
        Dictionary<GridCoord, PaletteRgb[]> sink)
    {
        if (string.IsNullOrEmpty(resPath)) return false;

        if (!ResourceLoader.Exists(resPath))
        {
            return false;
        }

        Texture2D? texture;
        try
        {
            texture = ResourceLoader.Load<Texture2D>(resPath);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[BakedFogPaletteSource] failed to load {resPath}: {ex.Message}");
            return false;
        }

        if (texture is null) return false;

        var image = texture.GetImage();
        if (image is null) return false;
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            // Defensive convert ; the bake produces Rgba8 but a re-import
            // could have changed compression. Cheap to handle here.
            image.Convert(Image.Format.Rgba8);
        }

        var width = image.GetWidth();
        var height = image.GetHeight();
        var expectedWidth = expected.Columns * 3;
        var expectedHeight = expected.Rows;

        if (width != expectedWidth || height != expectedHeight)
        {
            GD.PushWarning(
                $"[BakedFogPaletteSource] dimensions mismatch at {resPath}: " +
                $"got {width}×{height}, expected {expectedWidth}×{expectedHeight}.");
            return false;
        }

        for (int row = 0; row < expected.Rows; row++)
        {
            for (int col = 0; col < expected.Columns; col++)
            {
                var c0 = image.GetPixel(col * 3 + 0, row);
                var c1 = image.GetPixel(col * 3 + 1, row);
                var c2 = image.GetPixel(col * 3 + 2, row);
                sink[new GridCoord(col, row)] = new[]
                {
                    new PaletteRgb(c0.R, c0.G, c0.B),
                    new PaletteRgb(c1.R, c1.G, c1.B),
                    new PaletteRgb(c2.R, c2.G, c2.B),
                };
            }
        }

        return true;
    }
}
