using Godot;
using Wayfinders.Client.Utils;

namespace Wayfinders.Client.Scenes.Iso;

/// <summary>
/// Étape B (2026-05-23, Rune) — runtime background-texture swap for the
/// maquette IsoBoard. Lives in this partial-class file rather than
/// <c>IsoBoard.cs</c> so the eM↔eT bascule wire-in does not bloat the
/// already-large core file, and so the swap surface is grouped in one
/// obvious place for future eT-multi-district work.
///
/// <para>
/// <b>What this does — and what it does NOT do.</b> It swaps the
/// <see cref="Sprite2D"/> texture of the layer-1 floor only, on a board
/// whose <c>DeskTrianglePlaceholderClip</c> is OFF (the maquette). The
/// desk board is excluded by an explicit guard : its texture rides the
/// twin-corner CPU carve, not the full-rect sprite, and a runtime swap
/// there would silently no-op. The placeholder grid + cell projection
/// are <i>not</i> rebuilt — eM and eT both render against the maquette
/// board's existing 16×16 placeholder grid (the iso grid is a developer
/// overlay turned off on the maquette in <c>GameScreen.tscn</c>, so this
/// is a moot point in practice). The content size <i>does</i> change
/// (2048×1024 ↔ 1600×800) — <see cref="GetBackgroundTextureSizeOrZero"/>
/// reflects the new size immediately, which is what the
/// <c>GameScreen</c> pan-clamp consumer wants.
/// </para>
///
/// <para>
/// <b>Why a runtime swap and not two IsoBoard instances.</b> Two
/// instances would double the maquette subtree, free-and-recreate the
/// occupants on each transition (the J6
/// <see cref="Wayfinders.Client.Scripts.Screens.HalfgateMarkerLayer"/>
/// and <see cref="Wayfinders.Client.Scripts.Screens.MissionPoiLayer"/>
/// are children of this <c>Maquette</c> board), and force the camera to
/// re-park twice per transition (board destroy → board recreate). A
/// single board with a texture swap keeps the marker/POI layers alive
/// (toggled via <c>Visible</c> by the
/// <see cref="Wayfinders.Client.Scripts.Screens.MaquetteModeController"/>)
/// and lets the camera reposition in one move. Matches the roadmap
/// J7 intent : "le bureau et le HUD ne clignotent pas".
/// </para>
///
/// <para>
/// <b>Trap #13 — user-override discipline.</b> The texture is loaded
/// through <see cref="AssetLoader.LoadTextureWithUserOverride"/> exactly
/// like the initial <c>LoadBackground</c> path. A
/// <c>user://wayfinders_visual_assets/e3/wf_e3_district_lower_quays_mesh_iso_1600x800.png</c>
/// drop-in hot-swaps without a rebuild.
/// </para>
/// </summary>
public partial class IsoBoard
{
    /// <summary>
    /// Swap the maquette's layer-1 background texture at runtime
    /// (Étape B eM↔eT bascule). The board must NOT be the desk board
    /// (its wood ride the twin-corner CPU carve, not the sprite).
    ///
    /// <para>
    /// The path is loaded via the universal user-override mapper
    /// (trap #13). A successful load reassigns the <see cref="Sprite2D"/>
    /// texture, flips <c>_hasFloorBitmap</c> true, and queues a redraw
    /// so any placeholder grid overlay refreshes. A failure logs an
    /// error and leaves the existing texture in place — the caller
    /// (<see cref="Wayfinders.Client.Scripts.Screens.MaquetteModeController"/>)
    /// must decide whether to abort the mode transition.
    /// </para>
    /// </summary>
    /// <param name="texturePath">
    /// res:// path of the new layer-1 background bitmap. Must be
    /// non-empty.
    /// </param>
    /// <returns>True on a successful swap, false on any failure
    /// (empty path, load failure, desk-board guard refused).</returns>
    public bool SetBackgroundTexturePath(string texturePath)
    {
        if (string.IsNullOrEmpty(texturePath))
        {
            GD.PushWarning("[IsoBoard] SetBackgroundTexturePath called with " +
                           "an empty path — no swap.");
            return false;
        }

        if (DeskTrianglePlaceholderClip)
        {
            // The desk-board path keeps the wood texture in
            // _deskWoodTexture (carved by _Draw into two wedges). A
            // runtime swap there would NOT reach the sprite ; we refuse
            // it loudly so a future caller does not silently no-op.
            GD.PushError("[IsoBoard] SetBackgroundTexturePath is for the " +
                         "maquette board only ; this board has the " +
                         "twin-corner desk clip enabled. No swap.");
            return false;
        }

        var texture = AssetLoader.LoadTextureWithUserOverride(texturePath);
        if (texture is null)
        {
            GD.PushError($"[IsoBoard] SetBackgroundTexturePath failed to load " +
                         $"'{texturePath}' — board unchanged.");
            return false;
        }

        _background.Texture = texture;
        _hasFloorBitmap = true;
        BackgroundTexturePath = texturePath;
        GD.Print($"[IsoBoard] background texture swapped to '{texturePath}' " +
                 $"size={texture.GetSize()} (eM↔eT bascule).");

        // The placeholder grid or any draw-time overlay must refresh
        // against the new content size — cheap, and only matters when
        // those overlays are on (off on the maquette in shipping
        // GameScreen.tscn ; on for the desk board, which is excluded
        // above).
        if (IsInsideTree())
        {
            QueueRedraw();
        }
        return true;
    }
}
