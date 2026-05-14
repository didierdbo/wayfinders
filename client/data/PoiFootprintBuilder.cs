using System.Collections.Generic;
using System.Collections;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Vector2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Data;

public static class PoiFootprintBuilder
{
    public static (BitArray alphaMask, HashSet<Vector2I> footprint, int maskWidth) Build(
        Texture2D texture,
        Vector2I anchorPixel,
        int cellSizePx,
        int stride = 2,
        float alphaThreshold = 0.1f)
    {
        var image = texture.GetImage();
        var w = image.GetWidth();
        var h = image.GetHeight();
        var alphaMask = new BitArray(w * h);
        var footprint = new HashSet<Vector2I>();

        for (var x = 0; x < w; x += stride)
        {
            for (var y = 0; y < h; y += stride)
            {
                if ( image.GetPixel(x, y).A > alphaThreshold)
                {
                    alphaMask[y * w + x] = true;
                    var offset = new Vector2(x - anchorPixel.X, y - anchorPixel.Y);
                    var gridCoord = FogTileGridLogic.PixelOffsetToTile(offset, cellSizePx, GridProjection.IsoDiamondDown);
                    footprint.Add(new Vector2I(gridCoord.Col, gridCoord.Row));
                }
            }
        }

        return (alphaMask, footprint, w);
    }
}
