using System;

namespace Wayfinders.Client.Services;

/// <summary>
/// Pure-C# helper that maps an asset key + (userRoot, resRoot, keyMap)
/// triple to the two filesystem-shaped paths the loader can try (user://
/// override first, res:// fallback second). Lives separately from
/// <see cref="AssetResolver"/> so the path-construction policy is shared
/// by the runtime autoload AND by editor-only <c>[Tool]</c> scripts that
/// have no access to the autoload (they are instantiated in the editor's
/// own scene, not the running game's tree).
///
/// <para>
/// <b>Why extract this.</b> Slice 3.6 surfaced a silent mismatch: the
/// runtime <see cref="AssetResolver"/> followed user:// → res:// for
/// <c>e2.world_map</c>, but <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/>
/// hard-coded a <c>res://</c> path. When Didier baked from the placeholder
/// res:// PNG while the runtime loaded the user:// override, the bake
/// dimensions were correct for the placeholder but wrong for the live
/// asset, so <see cref="Wayfinders.Client.Services.BakedFogPaletteSource"/>
/// fell back to neutral-carton swatches. Extracting the resolution policy
/// here means the tool and the runtime cannot drift again.
/// </para>
///
/// <para>
/// <b>Pure C# by design.</b> No <c>Godot.*</c> imports — only string
/// concatenation. <see cref="AssetResolver"/> stays the Godot-binding
/// layer (FileAccess, Image.Load, ResourceLoader); this helper stays the
/// pure policy. xUnit pins the policy ; the autoload owns the I/O.
/// </para>
///
/// <para>
/// <b>Path shape contract.</b> Both <see cref="ResolvedPaths.UserPath"/>
/// and <see cref="ResolvedPaths.ResPath"/> are returned as Godot virtual
/// paths (<c>user://...</c> / <c>res://...</c>). The caller decides which
/// API to feed them to (FileAccess + Image.Load for user://, ResourceLoader
/// for res://) — same boundary as the existing
/// <see cref="AssetResolver.Resolve"/> body.
/// </para>
/// </summary>
public static class AssetKeyResolverLogic
{
    /// <summary>
    /// Resolve an asset key to the user:// + res:// path pair. Returns
    /// null when the key is not in the map (caller treats as "use
    /// placeholder / log warning"). The path roots are normalised by
    /// trimming a single trailing slash, mirroring
    /// <see cref="AssetResolver.Resolve"/>.
    /// </summary>
    /// <param name="assetKey">e.g. <c>"e2.world_map"</c>.</param>
    /// <param name="userAssetRoot">e.g. <c>"user://wayfinders_visual_assets/"</c>.</param>
    /// <param name="resAssetRoot">e.g. <c>"res://assets/wayfinders_visual_assets/"</c>.</param>
    /// <param name="keyMap">Loaded asset key map (typically from asset_keys.json).</param>
    /// <returns>The (user, res) path pair, or null if the key is unknown.</returns>
    public static ResolvedPaths? ResolvePathsForKey(
        string assetKey,
        string userAssetRoot,
        string resAssetRoot,
        AssetKeyMap keyMap)
    {
        if (assetKey is null) throw new ArgumentNullException(nameof(assetKey));
        if (userAssetRoot is null) throw new ArgumentNullException(nameof(userAssetRoot));
        if (resAssetRoot is null) throw new ArgumentNullException(nameof(resAssetRoot));
        if (keyMap is null) throw new ArgumentNullException(nameof(keyMap));

        var relativePath = keyMap.TryResolvePath(assetKey);
        if (relativePath is null)
            return null;

        var userPath = userAssetRoot.TrimEnd('/') + "/" + relativePath;
        var resPath = resAssetRoot.TrimEnd('/') + "/" + relativePath;
        return new ResolvedPaths(userPath, resPath, relativePath);
    }
}

/// <summary>
/// The two virtual paths a caller can attempt to load (user:// override
/// first, res:// fallback second), plus the relative path for diagnostics.
/// </summary>
public readonly record struct ResolvedPaths(string UserPath, string ResPath, string RelativePath);
