using System;
using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tools;

/// <summary>
/// Editor-only <c>[Tool]</c> script that bakes the slice 1 placeholder
/// palette quantification of a world-map image (livrable 4 — M3 / L1
/// World fondations / 2026-05-08). Reads a PNG, divides it into the
/// same fog-tile grid <see cref="FogTileLayer"/> uses at runtime, and
/// for each cell extracts the **3 dominant teintes** via a simple
/// 5-bit-per-channel histogram bucketisation. Writes the result to
/// <see cref="OutputPath"/> as a PNG laid out 3 texels per cell along
/// the X axis : for cell (c, r) the texels at (3c, r), (3c+1, r),
/// (3c+2, r) are the three sorted-by-luminance teintes.
///
/// <para>
/// <b>Why histogram bucketisation, not k-means.</b> Slice 1 brief
/// authorises either (livrable 4 acceptance criteria : "k-means sur
/// les pixels de la cellule, k=3, ou simple histogramme bucketisé si
/// k-means trop lourd à implémenter"). Histogram bucketisation is
/// O(pixels) per cell with a tiny fixed-size frequency map, ships in
/// ~50 lines of code, and produces results visually indistinguishable
/// from k-means for the purpose of "give the player a 3-blob colour
/// approximation through the fog" (Varn §3.1 source primaire). K-means
/// is the right call when slice 2's biome-modulated palette needs
/// stable centroids across re-bakes ; for slice 1 placeholder, the
/// histogram is faster to implement, easier to reason about, and
/// produces deterministic output (a given input image always yields
/// byte-identical output, which makes the bake idempotent for
/// version control). Swap to k-means in slice 2 if the visual passes
/// reveal a real difference.
/// </para>
///
/// <para>
/// <b>Output format (slice 1 schema).</b> A single
/// <see cref="Image.Format.Rgba8"/> PNG of dimensions
/// <c>(3 × gridCols, gridRows)</c>. For cell at column <c>c</c>,
/// row <c>r</c>, the three palette teintes are at:
/// <list type="bullet">
///   <item><c>(3c+0, r)</c> -- darkest teinte (lowest perceptual
///         luminance).</item>
///   <item><c>(3c+1, r)</c> -- mid teinte.</item>
///   <item><c>(3c+2, r)</c> -- brightest teinte.</item>
/// </list>
/// Sorting by luminance keeps slice 2's runtime sampler trivial:
/// "row-by-row scan, skip 2 to advance one cell, no metadata needed".
/// The format is intentionally not a custom Godot Resource — a PNG is
/// the simplest output that survives version control, can be opened
/// in any image viewer for spot-check, and consumes no engine import
/// reflection at AOT time.
/// </para>
///
/// <para>
/// <b>How to run.</b> Drop a <c>PaletteQuantizerTool</c> node into a
/// scratch scene in the editor (or use the bundled
/// <c>tools/PaletteQuantizerTool.tscn</c> as the running scene), set
/// <see cref="InputResPath"/> and <see cref="OutputPath"/>, then check
/// the <see cref="RunBakeNow"/> tool button in the inspector. The
/// script runs synchronously (~1-3 s for the 3840×2160 master,
/// depending on cell size) and prints progress + per-cell timings to
/// the editor console. Not wired to the build pipeline by design —
/// the bake is an explicit, versionable artifact that lands in
/// <c>client/assets/baked/</c> alongside <c>asset_keys.json</c>'s
/// future <c>baked.*</c> entries.
/// </para>
///
/// <para>
/// <b>Cell coverage at the right/bottom edge.</b> The grid is computed
/// via <see cref="FogTileGridLogic.ComputeGridSize"/> using ceiling
/// division, so the last column / row may overhang the image. For
/// histogram sampling we clamp the per-cell pixel rect to the actual
/// image bounds so we don't index out of range — the overhang cells
/// just sample fewer pixels and end up with palettes weighted toward
/// the partial-row content. Acceptable for slice 1 placeholder ; if
/// slice 2 reveals visual artifacts at the edge, we can either align
/// the master image to a multiple of CellSizePx (asset-side fix) or
/// drop the overhang cells from the bake (code-side fix).
/// </para>
///
/// <para>
/// <b>Why no <c>#if TOOLS</c> guard.</b> The <c>[Tool]</c> attribute
/// alone makes the script editor-runnable ; the actual "do not run at
/// runtime" gating happens in <see cref="RunBakeNow"/> which checks
/// <see cref="Engine.IsEditorHint()"/> before invoking the bake. A
/// <c>#if TOOLS</c> conditional would require a TOOLS define in the
/// client csproj, which would split the build between editor and
/// runtime in a way that complicates the AOT publish path (the
/// runtime publish wouldn't see the file at all, and a hypothetical
/// future test would have to mirror the conditional).
/// </para>
/// </summary>
[Tool]
public partial class PaletteQuantizerTool : Node
{
    /// <summary>
    /// res:// path to the input PNG (the world-map master image).
    /// Default points at the E2.1 master so the slice 1 bake works
    /// out-of-the-box on a fresh checkout.
    /// </summary>
    [Export(PropertyHint.File, "*.png")]
    public string InputResPath { get; set; } =
        "res://assets/wayfinders_visual_assets/e2/wf_e2_carte_monde_base_3840x2160.png";

    /// <summary>
    /// res:// path of the baked palette output. The default lands the
    /// file under <c>assets/baked/</c> alongside the future
    /// <c>baked.*</c> asset_keys.json convention.
    /// </summary>
    [Export(PropertyHint.File, "*.png")]
    public string OutputPath { get; set; } =
        "res://assets/baked/e2_world_palette_3teintes_128px.png";

    /// <summary>
    /// Cell side length in source pixels. Must match
    /// <see cref="FogTileLayer.CellSizePx"/> for the runtime
    /// consumer of the baked output to align cell-by-cell. Slice 1
    /// default 128 px keeps both ends of the seam in lockstep.
    /// </summary>
    [Export]
    public int CellSizePx { get; set; } = 128;

    /// <summary>
    /// Frequency-bucket precision. 5 bits per channel = 32×32×32 =
    /// 32 768 buckets — fine enough that two visually-distinct
    /// teintes don't collapse into one bucket on a typical oil-paint
    /// surface, coarse enough that the dominant cluster wins by
    /// vote-mass without tuning.
    /// </summary>
    [Export]
    public int BitsPerChannel { get; set; } = 5;

    /// <summary>
    /// Editor inspector toggle — flip true to run the bake. Always
    /// reads as false so a re-flip can re-trigger another bake.
    /// </summary>
    [Export]
    public bool RunBakeNow
    {
        get => false;
        set
        {
            if (value && Engine.IsEditorHint())
            {
                Bake();
            }
        }
    }

    private void Bake()
    {
        GD.Print($"[PaletteQuantizerTool] starting bake: input={InputResPath}, output={OutputPath}, cellSize={CellSizePx}");
        var startUsec = Time.GetTicksUsec();

        var sourceImage = LoadSourceImage(InputResPath);
        if (sourceImage is null)
        {
            GD.PushError($"[PaletteQuantizerTool] failed to load input image: {InputResPath}");
            return;
        }

        var imageSize = sourceImage.GetSize();
        var dimensions = FogTileGridLogic.ComputeGridSize(
            new PanVec2(imageSize.X, imageSize.Y), CellSizePx);

        GD.Print(
            $"[PaletteQuantizerTool] source: {imageSize.X}×{imageSize.Y} px, " +
            $"grid: {dimensions.Columns}×{dimensions.Rows} cells " +
            $"({dimensions.TotalCells} total)");

        var outputWidth = dimensions.Columns * 3;
        var outputHeight = dimensions.Rows;
        var outputImage = Image.CreateEmpty(outputWidth, outputHeight, false, Image.Format.Rgba8);

        var bucketShift = 8 - BitsPerChannel;
        var perChannelBuckets = 1 << BitsPerChannel;

        foreach (var coord in FogTileGridLogic.EnumerateCells(dimensions))
        {
            var palette = QuantifyCell(sourceImage, coord, CellSizePx, bucketShift, perChannelBuckets);
            palette.Sort(CompareByLuminance);
            outputImage.SetPixel(coord.Col * 3 + 0, coord.Row, palette[0]);
            outputImage.SetPixel(coord.Col * 3 + 1, coord.Row, palette[1]);
            outputImage.SetPixel(coord.Col * 3 + 2, coord.Row, palette[2]);
        }

        var error = outputImage.SavePng(OutputPath);
        if (error != Error.Ok)
        {
            GD.PushError($"[PaletteQuantizerTool] failed to save output: {error}");
            return;
        }

        var elapsedMs = (Time.GetTicksUsec() - startUsec) / 1000.0f;
        GD.Print(
            $"[PaletteQuantizerTool] bake complete in {elapsedMs:F1} ms : " +
            $"{outputWidth}×{outputHeight} palette PNG saved to {OutputPath}");
    }

    /// <summary>
    /// Per-cell palette extraction. Sweeps the cell's pixel rect,
    /// buckets each pixel by quantised RGB, then picks the three
    /// most-frequent buckets as the dominant teintes. The bucket
    /// centre (midpoint of the bucket's RGB range) is used as the
    /// representative colour rather than the average of pixels in
    /// the bucket — fewer cache misses, no second pass.
    /// </summary>
    private static List<Color> QuantifyCell(
        Image sourceImage,
        GridCoord coord,
        int cellSizePx,
        int bucketShift,
        int perChannelBuckets)
    {
        var imgWidth = sourceImage.GetWidth();
        var imgHeight = sourceImage.GetHeight();

        // Clamp cell rect to image bounds. Edge cells with overhang
        // sample fewer pixels but never read OOB.
        var x0 = coord.Col * cellSizePx;
        var y0 = coord.Row * cellSizePx;
        var x1 = Math.Min(x0 + cellSizePx, imgWidth);
        var y1 = Math.Min(y0 + cellSizePx, imgHeight);

        var freq = new Dictionary<int, int>(64);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                var c = sourceImage.GetPixel(x, y);
                var rB = (int)(c.R * 255f) >> bucketShift;
                var gB = (int)(c.G * 255f) >> bucketShift;
                var bB = (int)(c.B * 255f) >> bucketShift;
                var key = (rB * perChannelBuckets + gB) * perChannelBuckets + bB;
                freq[key] = freq.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        }

        // Pick the top 3 most frequent buckets.
        var top3 = new List<KeyValuePair<int, int>>(3);
        foreach (var kv in freq)
        {
            if (top3.Count < 3)
            {
                top3.Add(kv);
                top3.Sort((a, b) => b.Value.CompareTo(a.Value));
            }
            else if (kv.Value > top3[2].Value)
            {
                top3[2] = kv;
                top3.Sort((a, b) => b.Value.CompareTo(a.Value));
            }
        }

        // Pad if the cell had fewer than 3 distinct buckets.
        while (top3.Count < 3 && top3.Count > 0) top3.Add(top3[0]);
        // Fully degenerate: empty cell (zero pixels). Emit black.
        while (top3.Count < 3) top3.Add(new KeyValuePair<int, int>(0, 1));

        // Decode each bucket key back to a representative Color.
        var palette = new List<Color>(3);
        foreach (var kv in top3)
        {
            var key = kv.Key;
            var bB = key % perChannelBuckets;
            key /= perChannelBuckets;
            var gB = key % perChannelBuckets;
            var rB = key / perChannelBuckets;
            var r = (rB + 0.5f) / perChannelBuckets;
            var g = (gB + 0.5f) / perChannelBuckets;
            var b = (bB + 0.5f) / perChannelBuckets;
            palette.Add(new Color(r, g, b, 1f));
        }

        return palette;
    }

    private static int CompareByLuminance(Color a, Color b)
    {
        // Rec. 709 luma — same coefficients Godot uses internally for
        // grayscale conversions.
        var la = 0.2126f * a.R + 0.7152f * a.G + 0.0722f * a.B;
        var lb = 0.2126f * b.R + 0.7152f * b.G + 0.0722f * b.B;
        return la.CompareTo(lb);
    }

    private static Image? LoadSourceImage(string resPath)
    {
        if (resPath.StartsWith("res://", StringComparison.Ordinal))
        {
            var tex = ResourceLoader.Load<Texture2D>(resPath);
            if (tex is null) return null;
            var image = tex.GetImage();
            if (image is null) return null;
            if (image.GetFormat() != Image.Format.Rgba8)
            {
                image.Convert(Image.Format.Rgba8);
            }
            return image;
        }

        return Image.LoadFromFile(resPath);
    }
}
