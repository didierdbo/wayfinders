using System;
using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tools;

/// <summary>
/// Editor-only <c>[Tool]</c> script that bakes the placeholder palette
/// quantification of a world-map image (slice 1 livrable 4 → slice 3.5
/// iso refactor — M3 / L1 World fondations / 2026-05-08). Reads a PNG,
/// divides it into the same fog-tile grid <see cref="Wayfinders.Client.Components.FogTileLayer"/>
/// uses at runtime (with the same <see cref="GridProjection"/> setting),
/// and for each cell extracts the **3 dominant teintes** via a 5-bit-per-channel
/// histogram bucketisation. Writes the result to <see cref="OutputPath"/>
/// as a PNG laid out 3 texels per cell along the X axis.
///
/// <para>
/// <b>Slice 3.5 iso refactor.</b> The bake now respects the configured
/// <see cref="Projection"/> mode :
/// <list type="bullet">
///   <item><b>Rect</b> — slice 1 behaviour preserved : sample every pixel
///         in the cell's bounding box.</item>
///   <item><b>IsoDiamondDown</b> — slice 3.5 default for L1 World : sample
///         only the pixels that fall <b>inside the iso diamond</b> of the
///         cell, using <see cref="FogTileGridLogic.IsoCellContainsWorldPoint"/>.
///         Pixels in the empty triangles between adjacent diamonds are
///         not double-counted.</item>
/// </list>
/// The output PNG format (3 × cols texels along X) is identical in both
/// modes — <see cref="Wayfinders.Client.Services.BakedFogPaletteSource"/>
/// does not need to know which projection produced the bake. The runtime
/// queries by (col, row) and the bake stores by (col, row) ; the
/// projection only affects the per-cell pixel sampling, not the output
/// schema.
/// </para>
///
/// <para>
/// <b>Re-bake required after slice 3.5.</b> The cell layout changed
/// (rect 30×17 → iso ~120×34) so any pre-slice-3.5 bake PNG is dimensionally
/// invalid for the new <see cref="Wayfinders.Client.Services.BakedFogPaletteSource"/>
/// expected dimensions. The fallback codepath in the source kicks in
/// (neutral-carton swatches) until Didier re-runs this tool. Mentioned
/// in the slice 3.5 closeout.
/// </para>
///
/// <para>
/// <b>Why histogram bucketisation, not k-means.</b> Same rationale as
/// slice 1 : O(pixels-in-cell), deterministic, indistinguishable from
/// k-means at the placeholder use case. Iso refactor doesn't change
/// this trade-off.
/// </para>
///
/// <para>
/// <b>Output format (slice 1 schema, preserved at slice 3.5).</b> A single
/// <see cref="Image.Format.Rgba8"/> PNG of dimensions
/// <c>(3 × gridCols, gridRows)</c>. For cell at column <c>c</c>,
/// row <c>r</c>, the three palette teintes are at (3c+0, r), (3c+1, r),
/// (3c+2, r), sorted darkest → brightest by Rec.709 luminance.
/// </para>
///
/// <para>
/// <b>How to run.</b> Drop a <c>PaletteQuantizerTool</c> node into a
/// scratch scene in the editor (or use the bundled
/// <c>tools/PaletteQuantizerTool.tscn</c>), set <see cref="InputResPath"/>
/// and <see cref="OutputPath"/> and <see cref="Projection"/>, then check
/// the <see cref="RunBakeNow"/> tool button.
/// </para>
/// </summary>
[Tool]
public partial class PaletteQuantizerTool : Node
{
    /// <summary>res:// path to the input PNG (the world-map master image).</summary>
    [Export(PropertyHint.File, "*.png")]
    public string InputResPath { get; set; } =
        "res://assets/wayfinders_visual_assets/e2/wf_e2_carte_monde_base_3840x2160.png";

    /// <summary>res:// path of the baked palette output.</summary>
    [Export(PropertyHint.File, "*.png")]
    public string OutputPath { get; set; } =
        "res://assets/baked/e2_world_palette_3teintes_128px.png";

    /// <summary>
    /// Cell side length in source pixels. Must match
    /// <see cref="Wayfinders.Client.Components.FogTileLayer.CellSizePx"/>
    /// for the runtime consumer of the baked output to align cell-by-cell.
    /// </summary>
    [Export]
    public int CellSizePx { get; set; } = 128;

    /// <summary>
    /// Slice 3.5 -- grid projection. Must match
    /// <see cref="Wayfinders.Client.Components.FogTileLayer.Projection"/>
    /// or the bake samples pixels from regions the runtime won't display.
    /// Default <see cref="GridProjection.IsoDiamondDown"/> for L1 World.
    /// </summary>
    [Export]
    public GridProjection Projection { get; set; } = GridProjection.IsoDiamondDown;

    /// <summary>Frequency-bucket precision : 5 bits per channel = 32×32×32 buckets.</summary>
    [Export]
    public int BitsPerChannel { get; set; } = 5;

    /// <summary>Editor inspector toggle — flip true to run the bake.</summary>
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
        GD.Print(
            $"[PaletteQuantizerTool] starting bake: input={InputResPath}, output={OutputPath}, " +
            $"cellSize={CellSizePx}, projection={Projection}");
        var startUsec = Time.GetTicksUsec();

        var sourceImage = LoadSourceImage(InputResPath);
        if (sourceImage is null)
        {
            GD.PushError($"[PaletteQuantizerTool] failed to load input image: {InputResPath}");
            return;
        }

        var imageSize = sourceImage.GetSize();
        var dimensions = FogTileGridLogic.ComputeGridSize(
            new PanVec2(imageSize.X, imageSize.Y), CellSizePx, Projection);

        GD.Print(
            $"[PaletteQuantizerTool] source: {imageSize.X}×{imageSize.Y} px, " +
            $"grid: {dimensions.Columns}×{dimensions.Rows} cells " +
            $"({dimensions.TotalCells} total), projection={Projection}");

        var outputWidth = dimensions.Columns * 3;
        var outputHeight = dimensions.Rows;
        var outputImage = Image.CreateEmpty(outputWidth, outputHeight, false, Image.Format.Rgba8);

        var bucketShift = 8 - BitsPerChannel;
        var perChannelBuckets = 1 << BitsPerChannel;

        foreach (var coord in FogTileGridLogic.EnumerateCells(dimensions))
        {
            var palette = QuantifyCell(
                sourceImage, coord, CellSizePx, dimensions, Projection,
                bucketShift, perChannelBuckets);
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
    /// Per-cell palette extraction. In rect mode, sweeps the cell's bbox.
    /// In iso mode, sweeps the bbox and rejects pixels that fall outside
    /// the iso diamond via <see cref="FogTileGridLogic.IsoCellContainsWorldPoint"/>.
    /// </summary>
    private static List<Color> QuantifyCell(
        Image sourceImage,
        GridCoord coord,
        int cellSizePx,
        GridDimensions dimensions,
        GridProjection projection,
        int bucketShift,
        int perChannelBuckets)
    {
        var imgWidth = sourceImage.GetWidth();
        var imgHeight = sourceImage.GetHeight();

        // Compute the cell's bounding box in world coords. For iso, this
        // is the diamond's bounding rectangle ; for rect it's the cell
        // rectangle itself.
        int x0, y0, x1, y1;
        if (projection == GridProjection.IsoDiamondDown)
        {
            var center = FogTileGridLogic.ComputeCellCenter(coord, cellSizePx, projection);
            var shift = FogTileGridLogic.ComputeIsoOriginShift(dimensions, cellSizePx, projection);
            var halfW = cellSizePx / 2f;
            var halfH = cellSizePx / 4f;
            var cx = center.X + shift.X;
            var cy = center.Y + shift.Y;
            x0 = (int)System.MathF.Floor(cx - halfW);
            y0 = (int)System.MathF.Floor(cy - halfH);
            x1 = (int)System.MathF.Ceiling(cx + halfW);
            y1 = (int)System.MathF.Ceiling(cy + halfH);
        }
        else
        {
            x0 = coord.Col * cellSizePx;
            y0 = coord.Row * cellSizePx;
            x1 = x0 + cellSizePx;
            y1 = y0 + cellSizePx;
        }

        // Clamp to image bounds.
        x0 = Math.Max(x0, 0);
        y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, imgWidth);
        y1 = Math.Min(y1, imgHeight);

        var freq = new Dictionary<int, int>(64);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (projection == GridProjection.IsoDiamondDown)
                {
                    // Iso : reject pixels outside the diamond. The pixel
                    // center is at (x+0.5, y+0.5).
                    if (!FogTileGridLogic.IsoCellContainsWorldPoint(
                            coord,
                            new PanVec2(x + 0.5f, y + 0.5f),
                            cellSizePx,
                            dimensions))
                    {
                        continue;
                    }
                }

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
