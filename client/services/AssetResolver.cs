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
/// <see cref="Resolve"/> wraps loads in try/catch and falls back
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
///
/// <para>
/// <b>Loading paths differ by root.</b> <c>res://</c> assets are imported
/// by the editor (have a <c>.import</c> sidecar) and load via
/// <see cref="GD.Load{T}"/>. <c>user://</c> assets are raw PNGs dropped
/// at runtime — the editor never imports them, so <see cref="GD.Load{T}"/>
/// returns null. We load those with <see cref="Image.LoadFromFile"/> +
/// <see cref="ImageTexture.CreateFromImage"/>, which decodes the file
/// directly with no import pipeline.
/// </para>
///
/// <para>
/// <b>Phase 9 slice ménage refactor (2026-05-08).</b> Path construction
/// is delegated to <see cref="AssetKeyResolverLogic.ResolvePathsForKey"/>
/// — the same pure-C# helper consumed by
/// <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/> in the
/// editor. The autoload owns I/O ; the helper owns policy. Surfaced from
/// the slice 3.6 silent mismatch (tool resolved res:// while runtime
/// resolved user://, neutral-carton fallback ensued). xUnit pins the
/// policy ; the autoload's filesystem branch is exercised through manual
/// integration.
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
    ///   <item>Try user://[path] (raw PNG decode). If it exists and loads, return it.</item>
    ///   <item>Try res://[path] (imported resource). If it exists and loads, return it.</item>
    ///   <item>Return a colored placeholder (deterministic per key).</item>
    /// </list>
    /// </summary>
    public Texture2D Resolve(string assetKey)
    {
        if (_keyMap is null)
            return GeneratePlaceholder(assetKey, "no key map");

        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            assetKey, UserAssetRoot, ResAssetRoot, _keyMap);
        if (resolved is null)
        {
            GD.PushWarning($"[AssetResolver] unknown key '{assetKey}' — fallback");
            return GeneratePlaceholder(assetKey, "unknown key");
        }

        var paths = resolved.Value;
        if (TryLoadFromUser(paths.UserPath, out var userTex) && userTex is not null)
        {
            GD.Print($"[AssetResolver] resolved '{assetKey}' from user:// override");
            return userTex;
        }

        if (TryLoadFromRes(paths.ResPath, out var resTex) && resTex is not null)
            return resTex;

        GD.PushWarning($"[AssetResolver] missing file for key '{assetKey}' (tried {paths.UserPath}, {paths.ResPath}) — fallback");
        return GeneratePlaceholder(assetKey, "missing file");
    }

    /// <summary>
    /// Load a raw PNG/JPG sitting in user://. Bypasses the import pipeline
    /// (which only runs on res://). Decodes the file directly.
    /// </summary>
    private static bool TryLoadFromUser(string godotPath, out Texture2D? texture)
    {
        texture = null;
        if (!FileAccess.FileExists(godotPath))
            return false;

        try
        {
            var image = new Image();
            var err = image.Load(godotPath);
            if (err != Error.Ok)
            {
                GD.PushWarning($"[AssetResolver] Image.Load failed for {godotPath}: {err} — falling back");
                return false;
            }
            texture = ImageTexture.CreateFromImage(image);
            return texture is not null;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[AssetResolver] user load failed for {godotPath}: {ex.Message} — falling back");
            return false;
        }
    }

    /// <summary>
    /// Load an imported texture from res://. Uses ResourceLoader (GD.Load)
    /// which expects a .import sidecar produced by the editor.
    /// </summary>
    private static bool TryLoadFromRes(string godotPath, out Texture2D? texture)
    {
        texture = null;
        if (!ResourceLoader.Exists(godotPath))
            return false;

        try
        {
            texture = GD.Load<Texture2D>(godotPath);
            return texture is not null;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[AssetResolver] res load failed for {godotPath}: {ex.Message} — falling back");
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
