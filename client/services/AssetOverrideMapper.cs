using System;
using System.Collections.Generic;

namespace Wayfinders.Client.Services;

/// <summary>
/// Pure-C# helper that maps any <c>res://assets/...</c> Godot resource path
/// to the ordered list of <c>user://...</c> override candidates a loader
/// should try before falling back to <c>res://</c>. Godot-free by design so
/// xUnit pins the policy ; the runtime
/// <see cref="Wayfinders.Client.Utils.AssetLoader.LoadTextureWithUserOverride"/>
/// consumes the candidate list and owns the I/O.
///
/// <para>
/// <b>Why this exists.</b> Before 2026-05-18, the override system supported
/// two channels :
/// <list type="number">
///   <item><see cref="AssetResolver"/> autoload, keyed by
///         <c>asset_keys.json</c> entries (40 keys at boot). Anything not
///         in the registry was invisible to the override.</item>
///   <item><see cref="Wayfinders.Client.Utils.AssetLoader.LoadAssetOrPlaceholder"/>,
///         keyed by <c>(assetDir, assetName)</c> arguments. Required code
///         to call it explicitly ; many sites (POI sidecar loader, area
///         grid baked palette, etc.) bypass it.</item>
/// </list>
/// Didier's expectation (2026-05-18) : "I want to override <i>any</i>
/// loaded asset by dropping a PNG with the same filename under
/// <c>user://wayfinders_visual_assets/&lt;sub&gt;/</c>". This helper +
/// <see cref="Wayfinders.Client.Utils.AssetLoader.LoadTextureWithUserOverride"/>
/// implement that universal channel — call sites pass a <c>res://</c>
/// resource path, the helper returns the user:// candidates in priority
/// order, the loader tries each and falls back to <c>res://</c>.
/// </para>
///
/// <para>
/// <b>Candidate ordering.</b> For a res:// path <c>res://assets/&lt;sub&gt;/&lt;rest&gt;</c>
/// :
/// <list type="number">
///   <item><b>Symmetric strip</b> : <c>user://&lt;sub&gt;/&lt;rest&gt;</c>.
///         Mirrors <see cref="Wayfinders.Client.Utils.AssetLoader.LoadAssetOrPlaceholder"/>
///         and <see cref="AssetResolver"/>. Used when the dev wants the
///         override folder to mirror the source folder exactly.</item>
///   <item><b>Wayfinders visual assets convention</b> :
///         <c>user://wayfinders_visual_assets/&lt;parentDir&gt;/&lt;file&gt;</c>,
///         where <c>parentDir</c> is the immediate parent folder of the
///         file and <c>file</c> is the basename. Matches the way Didier
///         has been consolidating overrides under a single
///         <c>wayfinders_visual_assets/</c> root with per-screen subdirs
///         (<c>e1/</c>, <c>e2/</c>, etc.). The POI Halfgate case
///         (<c>res://assets/poi/e1/wf_e1_halfgate_poi.png</c>) drops at
///         <c>user://wayfinders_visual_assets/e1/wf_e1_halfgate_poi.png</c>
///         under this rule, even though the res:// source lives under
///         <c>poi/</c>, not <c>wayfinders_visual_assets/</c>.</item>
/// </list>
/// When candidate 1 and candidate 2 collapse to the same path (asset
/// already under <c>res://assets/wayfinders_visual_assets/&lt;sub&gt;/&lt;file&gt;</c>),
/// only one is emitted — the loader doesn't double-try a path.
/// </para>
///
/// <para>
/// <b>Returns empty list</b> for paths outside <c>res://assets/</c> (no
/// override channel for the .godot/imported pseudo-dir, no override for
/// raw user:// paths, no override for shaders/scenes/etc.). Caller should
/// then load straight from res://.
/// </para>
///
/// <para>
/// <b>Trap #13</b> (memo <c>feedback_godot_rendering_input_traps.md</c>) :
/// <see cref="Wayfinders.Client.Data.PoiSidecarLoader"/> was calling
/// <c>ResourceLoader.Load&lt;Texture2D&gt;</c> directly on a res:// path,
/// bypassing both the AssetResolver autoload AND
/// <c>AssetLoader.LoadAssetOrPlaceholder</c>. Didier's drop of
/// <c>wf_e1_halfgate_poi.png</c> under user:// looked correct (the
/// AssetResolver boot log even ran), but the POI Halfgate call site
/// never consulted the override system. Symptom : silent no-op of the
/// override, original res:// asset keeps rendering. Fix : route the
/// sidecar loader through <c>AssetLoader.LoadTextureWithUserOverride</c>.
/// </para>
/// </summary>
public static class AssetOverrideMapper
{
    private const string ResAssetPrefix = "res://assets/";
    private const string UserPrefix = "user://";
    private const string WayfindersVisualAssetsBucket = "wayfinders_visual_assets";

    /// <summary>
    /// Build the ordered list of user:// override candidates for a given
    /// res:// resource path. Caller (typically
    /// <see cref="Wayfinders.Client.Utils.AssetLoader.LoadTextureWithUserOverride"/>)
    /// iterates the list, tries each candidate via <c>FileAccess.FileExists</c>
    /// + raw PNG decode, and falls back to ResourceLoader on res:// if none
    /// match.
    /// </summary>
    /// <param name="resPath">
    /// A Godot resource path, e.g. <c>res://assets/poi/e1/wf_e1_halfgate_poi.png</c>.
    /// Paths that do not start with <c>res://assets/</c> return an empty
    /// list — no override channel for engine-internal or scratch paths.
    /// </param>
    /// <returns>
    /// Ordered candidates ; empty list if no override channel applies.
    /// Always emits distinct paths (no duplicate try).
    /// </returns>
    public static IReadOnlyList<string> BuildUserCandidates(string resPath)
    {
        if (resPath is null) throw new ArgumentNullException(nameof(resPath));
        if (!resPath.StartsWith(ResAssetPrefix, StringComparison.Ordinal))
            return Array.Empty<string>();

        // Everything after "res://assets/" — e.g. "poi/e1/wf_e1_halfgate_poi.png"
        // or "wayfinders_visual_assets/e1/wf_e1_tile_neutral.png".
        var rest = resPath.Substring(ResAssetPrefix.Length);
        if (string.IsNullOrEmpty(rest))
            return Array.Empty<string>();

        var candidates = new List<string>(2);

        // Candidate 1 : symmetric strip (existing AssetLoader / AssetResolver
        // convention). For "poi/e1/wf_e1_halfgate_poi.png" → "user://poi/e1/wf_e1_halfgate_poi.png".
        var symmetric = UserPrefix + rest;
        candidates.Add(symmetric);

        // Candidate 2 : Wayfinders visual assets convention — parentDir + file
        // under user://wayfinders_visual_assets/. For "poi/e1/wf_e1_halfgate_poi.png"
        // → parentDir="e1", file="wf_e1_halfgate_poi.png"
        // → "user://wayfinders_visual_assets/e1/wf_e1_halfgate_poi.png".
        var lastSlash = rest.LastIndexOf('/');
        if (lastSlash > 0 && lastSlash < rest.Length - 1)
        {
            var head = rest.Substring(0, lastSlash); // "poi/e1" or "wayfinders_visual_assets/e1"
            var file = rest.Substring(lastSlash + 1); // "wf_e1_halfgate_poi.png"

            var parentLastSlash = head.LastIndexOf('/');
            var parentDir = parentLastSlash >= 0 ? head.Substring(parentLastSlash + 1) : head;

            if (!string.IsNullOrEmpty(parentDir) && !string.IsNullOrEmpty(file))
            {
                var visualAssets = $"{UserPrefix}{WayfindersVisualAssetsBucket}/{parentDir}/{file}";
                if (!string.Equals(visualAssets, symmetric, StringComparison.Ordinal))
                    candidates.Add(visualAssets);
            }
        }

        return candidates;
    }
}
