using System;
using Godot;
using System.Text.Json;
using Wayfinders.Client.Utils;

namespace Wayfinders.Client.Data;

/// <summary>
/// Loads a POI's sidecar (PNG + <c>.meta.json</c>) into a runtime
/// <see cref="PoiData"/> bundle. The PNG load is routed through
/// <see cref="AssetLoader.LoadTextureWithUserOverride"/> (2026-05-18,
/// trap #13 fix) so a user:// override drops in transparently — same
/// channel as the rest of the Wayfinders asset pipeline.
///
/// <para>
/// <b>Before 2026-05-18 :</b> this loader called
/// <c>ResourceLoader.Load&lt;Texture2D&gt;(pngResPath)</c> directly,
/// bypassing both the <see cref="Wayfinders.Client.Services.AssetResolver"/>
/// autoload and <see cref="AssetLoader.LoadAssetOrPlaceholder"/>. Result :
/// dropping <c>wf_e1_halfgate_poi.png</c> in
/// <c>user://wayfinders_visual_assets/e1/</c> had no effect (silent
/// no-op of the override), the res:// asset kept rendering. Diagnosis
/// + fix logged as trap #13 in
/// <c>feedback_godot_rendering_input_traps.md</c>.
/// </para>
///
/// <para>
/// <b>Note on the .meta.json sidecar.</b> Only the PNG goes through the
/// override channel. The sidecar JSON (anchor pixel + display name) stays
/// res:// — it is contract, not content. If a user wants to override the
/// anchor pixel, they edit the source res:// JSON ; if they want to swap
/// the bitmap, they drop a PNG. Different lifecycles, different channels.
/// </para>
/// </summary>
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

        // 2026-05-18 trap #13 fix : route the PNG through AssetLoader so the
        // universal user:// override applies. If the override fails (decode
        // error, file missing), LoadTextureWithUserOverride falls back to
        // ResourceLoader on res:// — same behaviour as before for the
        // happy-path res://-only case.
        Texture2D? texture = AssetLoader.LoadTextureWithUserOverride(pngResPath);
        if (texture is null)
        {
            throw new InvalidOperationException(
                $"PoiSidecarLoader: failed to load texture at {pngResPath} " +
                "(neither user:// override nor res:// resolved)");
        }
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
