using System;
using Godot;
using System.Text.Json;

namespace Wayfinders.Client.Data;

public static class PoiSidecarLoader
{
    public static PoiData Load(string pngResPath)
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

        return new PoiData
        {
            AnchorPixel = new Vector2I(dto.AnchorPixel[0], dto.AnchorPixel[1]),
            DisplayName = dto.DisplayName,
            TexturePath = pngResPath,
            Texture = texture
        };
    }
}
