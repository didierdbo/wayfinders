using System;
using Godot;

namespace Wayfinders.Client.Services;

/// <summary>
/// Autoload singleton that maps asset keys (e.g. <c>e1.bureau</c>) to
/// runtime <see cref="Texture2D"/> instances, with a deterministic
/// colored-rectangle fallback when the file is missing or fails to load.
///
/// <para>
/// Hot-swap-friendly: the resolver does not cache textures across calls
/// in J1, so dropping a new PNG in <c>res://assets/wayfinders_visual_assets/</c>
/// and reloading the scene picks it up immediately. Caching, if needed
/// for perf, lands in J3+ when we know what gets rendered most.
/// </para>
///
/// <para>
/// <b>Pre-brief Risk #2 mitigation (file lock during hot-swap).</b>
/// <see cref="Resolve"/> wraps <c>GD.Load</c> in try/catch and falls back
/// to the placeholder rather than throwing if the file is mid-write.
/// Logged at warning level so dev sees what happened.
/// </para>
///
/// <para>
/// <b>D3 hybrid res:// + user:// override (validated).</b> The resolver
/// tries <see cref="UserAssetRoot"/> first, falls back to
/// <see cref="ResAssetRoot"/>. In dev, drop a file under user:// to
/// override without rebuild ; the shipped Steam binary embeds res://
/// proudly because user:// is empty. Both roots are configurable through
/// editor [Export] for future flexibility.
/// </para>
/// </summary>
public partial class AssetResolver : Node
{
    /// <summary>
    /// Canonical asset root, embedded in the binary. Used for the shipped
    /// Steam build and as the default in dev.
    /// </summary>
    [Export] public string ResAssetRoot { get; set; } = "res://assets/wayfinders_visual_assets/";

    /// <summary>
    /// Optional override root for live hot-swap. Tried first ; if a key's
    /// resolved path does not exist there, falls back to ResAssetRoot.
    /// </summary>
    [Export] public string UserAssetRoot { get; set; } = "user://wayfinders_visual_assets/";

    /// <summary>
    /// Path to the asset_keys.json mapping file, relative to res:// (the
    /// mapping ships with the binary — it is contract, not content).
    /// </summary>
    [Export] public string KeyMapPath { get; set; } = "res://data/asset_keys.json";

    private AssetKeyMap? _keyMap;

    public override void _Ready()
    {
        try
        {
            using var file = FileAccess.Open(KeyMapPath, FileAccess.ModeFlags.Read);
            if (file is not null)
            {
                var json = file.GetAsText();
                _keyMap = AssetKeyMap.FromJson(json);
                GD.Print($"[AssetResolver] ready, {_keyMap.Count} keys loaded from {KeyMapPath}");
            }
            else
            {
                GD.PushError($"[AssetResolver] could not open {KeyMapPath} — all Resolve() calls will hit fallback");
            }
        }
        catch (Exception ex)
        {
            // Bad JSON shape, etc. Loud error but no crash — fallback path
            // keeps the game runnable so dev can fix the JSON without a
            // restart-per-fix loop.
            GD.PushError($"[AssetResolver] failed to parse {KeyMapPath}: {ex.Message}");
            _keyMap = null;
        }
    }

    /// <summary>
    /// Resolve an asset key to a <see cref="Texture2D"/>. Order:
    /// <list type="number">
    ///   <item>Look up the key in the JSON map. If unknown, log + fallback.</item>
    ///   <item>Try user://[path]. If it exists and loads, return it.</item>
    ///   <item>Try res://[path]. If it exists and loads, return it.</item>
    ///   <item>Return a colored placeholder (deterministic per key).</item>
    /// </list>
    /// </summary>
    public Texture2D Resolve(string assetKey)
    {
        if (_keyMap is null)
            return GeneratePlaceholder(assetKey, "no key map");

        var relativePath = _keyMap.TryResolvePath(assetKey);
        if (relativePath is null)
        {
            GD.PushWarning($"[AssetResolver] unknown key '{assetKey}' — fallback");
            return GeneratePlaceholder(assetKey, "unknown key");
        }

        var userPath = UserAssetRoot.TrimEnd('/') + "/" + relativePath;
        if (TryLoadTexture(userPath, out var userTex) && userTex is not null)
            return userTex;

        var resPath = ResAssetRoot.TrimEnd('/') + "/" + relativePath;
        if (TryLoadTexture(resPath, out var resTex) && resTex is not null)
            return resTex;

        GD.PushWarning($"[AssetResolver] missing file for key '{assetKey}' (tried {userPath}, {resPath}) — fallback");
        return GeneratePlaceholder(assetKey, "missing file");
    }

    /// <summary>
    /// Try loading a Texture2D at the given absolute Godot path. Wraps the
    /// load in try/catch to neutralize transient Windows file-lock errors
    /// during hot-swap (Pre-brief Risk #2).
    /// </summary>
    private static bool TryLoadTexture(string godotPath, out Texture2D? texture)
    {
        texture = null;
        if (!FileAccess.FileExists(godotPath))
            return false;

        try
        {
            texture = GD.Load<Texture2D>(godotPath);
            return texture is not null;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[AssetResolver] load failed for {godotPath}: {ex.Message} — falling back");
            return false;
        }
    }

    /// <summary>
    /// Build a deterministic placeholder texture: a flat color rectangle
    /// keyed off the asset key's hash. Good enough for J1 to make the
    /// "asset missing" state obvious without crashing.
    /// </summary>
    private static Texture2D GeneratePlaceholder(string assetKey, string reason)
    {
        var color = ColorFromKey(assetKey);
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        image.Fill(color);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Deterministic color from key. Same key always gets same color, so
    /// dev recognizes "oh, that's the world map placeholder again" at a
    /// glance.
    /// </summary>
    private static Color ColorFromKey(string assetKey)
    {
        // FNV-1a 32-bit hash. Stable, no allocation, no crypto baggage.
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;
        var hash = fnvOffset;
        foreach (var c in assetKey)
        {
            hash ^= c;
            unchecked { hash *= fnvPrime; }
        }
        var r = ((hash >> 16) & 0xFF) / 255f;
        var g = ((hash >> 8) & 0xFF) / 255f;
        var b = (hash & 0xFF) / 255f;
        // Avoid pure black ; bias toward muted earth tones to match the
        // Wayfinders palette and stay readable as a placeholder.
        return new Color(0.3f + 0.5f * r, 0.3f + 0.5f * g, 0.3f + 0.5f * b);
    }
}
