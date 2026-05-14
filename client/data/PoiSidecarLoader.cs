using System;
using Godot;
using System.Text.Json;

namespace Wayfinders.Client.Data;

public static class PoiSidecarLoader
{
    public static PoiData Load(string pngResPath, int cellSizePx = 128)
    {
        string jsonResPath = pngResPath.Replace(".png", ".meta.json");
        string json = FileAccess.Open(jsonResPath, FileAccess.ModeFlags.Read).GetAsText();

        PoiSidecarDto?  dto = JsonSerializer.Deserialize<PoiSidecarDto>(json, PoiSidecarJsonContext.Default.PoiSidecarDto);
        if (dto == null)
        {
            throw new InvalidOperationException(
                $"PoiSidecarLoader: failed to deserialize sidecar at {jsonResPath}");
        }

        Texture2D texture = ResourceLoader.Load<Texture2D>(pngResPath);
        var anchorPixel = new Vector2I(dto.AnchorPixel[0], dto.AnchorPixel[1]);
        var (alphaMask, footprint, w) = PoiFootprintBuilder.Build(texture, anchorPixel, cellSizePx);


        return new PoiData
        {
            AnchorPixel = anchorPixel,
            DisplayName = dto.DisplayName,
            TexturePath = pngResPath,
            Texture = texture,
            AlphaMask=alphaMask,
            AlphaMaskWidth =  w,
            FootprintTiles=footprint
        };
    }
}
