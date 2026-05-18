using Godot;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Utils;

/// <summary>
/// Asset loading utility implementing the Mira &lt;-&gt; Rune placeholder
/// framework v1.0 contract (locked 2026-05-13) plus the universal user://
/// override channel (locked 2026-05-18).
///
/// <para>
/// <b>Convention.</b> Code asks for an asset by its <i>final</i> name
/// (e.g. <c>wf_e1_halfgate_poi.png</c>). The loader resolves the path as
/// follows:
/// <list type="number">
///   <item>If a <c>user://</c> override exists at the symmetric path
///         → load it as a raw PNG (no import pipeline). This is the
///         hot-swap channel for asset iteration without rebuild.</item>
///   <item>Else if <c>&lt;assetDir&gt;/&lt;assetName&gt;</c> exists → load it
///         (MJ final asset present, normal production path).</item>
///   <item>Else if <c>&lt;assetDir&gt;/&lt;basename&gt;_PLACEHOLDER.&lt;ext&gt;</c>
///         exists → load it and log a console warning so the placeholder is
///         visible in demo audits.</item>
///   <item>Else log an error and return <c>null</c>. Caller decides whether
///         to crash, draw a magenta rectangle, or skip.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why this pattern.</b> Production code never hardcodes the placeholder
/// path. The day Mira delivers the final MJ asset (same name, no
/// <c>_PLACEHOLDER</c> suffix), <c>FileAccess.FileExists</c> flips and the
/// final asset is served — zero code change required. Mira keeps the
/// placeholder PNG in the repo as a rollback safety net.
/// </para>
///
/// <para>
/// <b>Path convention.</b> <c>assetDir</c> is expected as a Godot resource
/// path (<c>res://...</c>). The loader uses <see cref="FileAccess.FileExists"/>
/// (which understands <c>res://</c>) for existence checks and
/// <see cref="GD.Load{T}"/> for the actual load (so the Godot import pipeline
/// applies, .ctex cache used, etc.).
/// </para>
///
/// <para>
/// <b>user:// override (2026-05-15).</b> Symmetric with
/// <see cref="Wayfinders.Client.Services.AssetResolver"/>: an <c>assetDir</c>
/// starting with <c>res://assets/</c> is also tried under <c>user://</c>
/// (with the <c>assets/</c> segment stripped) before the canonical
/// <c>res://</c> resolution. user:// PNGs are raw — they have no
/// <c>.import</c> sidecar — so the loader decodes them via
/// <see cref="Image.Load(string)"/> + <see cref="ImageTexture.CreateFromImage"/>
/// (mirrors <c>AssetResolver.TryLoadFromUser</c>). Drop a PNG in
/// <c>%APPDATA%/Godot/app_userdata/Wayfinders/wayfinders_visual_assets/...</c>
/// and F5 picks it up without a rebuild.
/// </para>
///
/// <para>
/// <b>Universal override (2026-05-18 — Didier).</b>
/// <see cref="LoadTextureWithUserOverride"/> is the path-only entry point
/// for call sites that already have a <c>res://</c> resource path in hand
/// (POI sidecar loader, future direct ResourceLoader sites, etc.). It
/// delegates the candidate-list construction to
/// <see cref="AssetOverrideMapper.BuildUserCandidates"/>, tries each
/// user:// candidate in order, then falls back to
/// <see cref="ResourceLoader.Load{T}"/> on the original res:// path. This
/// closes the "I dropped a PNG in user:// and nothing happened" trap
/// for any call site that used to call <c>ResourceLoader.Load&lt;Texture2D&gt;</c>
/// directly. See trap #13 in
/// <c>feedback_godot_rendering_input_traps.md</c>.
/// </para>
///
/// <para>
/// <b>Future use.</b> Any new Wayfinders asset loaded by client C# code
/// should go through either <see cref="LoadAssetOrPlaceholder"/> (when
/// the call site authors a placeholder fallback) or
/// <see cref="LoadTextureWithUserOverride"/> (when the call site just has
/// a res:// path and wants override transparency). See spec
/// <c>Owner's Inbox/mira-placeholder-framework-spec-2026-05-13-FR.md §6</c>.
/// </para>
/// </summary>
public static class AssetLoader
{
    // Symmetric with AssetResolver.ResAssetRoot / UserAssetRoot:
    //   res://assets/wayfinders_visual_assets/...  <-> user://wayfinders_visual_assets/...
    // assetDir typically starts with ResAssetPrefix; the user:// equivalent
    // is the rest of the path mounted under UserAssetPrefix.
    private const string ResAssetPrefix = "res://assets/";
    private const string UserAssetPrefix = "user://";

    /// <summary>
    /// Loads <c>&lt;assetDir&gt;/&lt;assetName&gt;</c> if present; otherwise
    /// falls back to <c>&lt;basename&gt;_PLACEHOLDER.&lt;ext&gt;</c>.
    /// A <c>user://</c> override at the symmetric path takes precedence
    /// over both. Returns <c>null</c> if nothing resolves.
    /// </summary>
    /// <param name="assetDir">
    /// Godot resource dir, e.g. <c>res://assets/wayfinders_visual_assets/e1</c>.
    /// No trailing slash.
    /// </param>
    /// <param name="assetName">
    /// Final asset filename with extension, e.g. <c>wf_e1_halfgate_poi.png</c>.
    /// </param>
    /// <returns>
    /// The loaded <see cref="Texture2D"/>, or <c>null</c> if no candidate file
    /// was found (an error is pushed to the Godot console in that case).
    /// </returns>
    public static Texture2D? LoadAssetOrPlaceholder(string assetDir, string assetName)
    {
        // 1. user:// override (symmetric path, raw PNG decode).
        var userPath = TryMapToUserPath(assetDir, assetName);
        if (userPath is not null && FileAccess.FileExists(userPath))
        {
            var userTex = TryLoadRawFromUser(userPath);
            if (userTex is not null)
            {
                GD.Print($"[AssetLoader] loaded {assetName} from user:// override ({userPath})");
                return userTex;
            }
            // Fall through to res:// when user:// decode failed — TryLoadRawFromUser
            // already logged a PushWarning with the underlying error.
        }

        // 2. res:// final.
        string finalPath = $"{assetDir}/{assetName}";
        if (FileAccess.FileExists(finalPath))
        {
            GD.Print($"[PLACEHOLDER LOAD] {assetName} -> final ({finalPath})");
            return GD.Load<Texture2D>(finalPath);
        }

        // 3. res:// placeholder (basename + _PLACEHOLDER suffix, same ext).
        string basename = assetName.GetBaseName();   // e.g. "wf_e1_halfgate_poi"
        string ext = assetName.GetExtension();       // e.g. "png"
        string placeholderPath = $"{assetDir}/{basename}_PLACEHOLDER.{ext}";

        if (FileAccess.FileExists(placeholderPath))
        {
            // Warning visible in console + audit log for demo runs.
            GD.PushWarning($"[PLACEHOLDER] using {placeholderPath} (expected {assetName})");
            GD.Print($"[PLACEHOLDER LOAD] {assetName} -> placeholder ({placeholderPath})");
            return GD.Load<Texture2D>(placeholderPath);
        }

        GD.PushError($"[ASSET MISSING] no final, no placeholder for {assetName} in {assetDir}");
        GD.Print($"[PLACEHOLDER LOAD] {assetName} -> MISSING (no final, no placeholder in {assetDir})");
        return null;
    }

    /// <summary>
    /// Universal path-only override-aware load (locked 2026-05-18). Takes
    /// a Godot resource path (<c>res://assets/...</c>) and returns a
    /// <see cref="Texture2D"/> from the highest-priority user:// candidate
    /// that exists on disk, falling back to <see cref="ResourceLoader.Load{T}"/>
    /// on the original res:// path. The candidate list is built by
    /// <see cref="AssetOverrideMapper.BuildUserCandidates"/> (symmetric
    /// strip first, then <c>wayfinders_visual_assets/&lt;parentDir&gt;/&lt;file&gt;</c>
    /// convention).
    ///
    /// <para>
    /// <b>When to call this.</b> Any call site that today writes
    /// <c>ResourceLoader.Load&lt;Texture2D&gt;(resPath)</c> on an asset that
    /// should be hot-swappable from user://. The <see cref="LoadAssetOrPlaceholder"/>
    /// entry point is for sites that also need a <c>_PLACEHOLDER</c>
    /// fallback (Mira framework). This entry point is for sites that just
    /// have a resolved res:// path and want override transparency.
    /// </para>
    ///
    /// <para>
    /// <b>Returns null</b> only if both all user:// candidates fail AND the
    /// res:// load fails (missing file, no .import sidecar, or
    /// ResourceLoader exception). Logs to GD on every fallback step.
    /// </para>
    /// </summary>
    public static Texture2D? LoadTextureWithUserOverride(string resPath)
    {
        if (resPath is null)
        {
            GD.PushError("[AssetLoader] LoadTextureWithUserOverride called with null resPath");
            return null;
        }

        var candidates = AssetOverrideMapper.BuildUserCandidates(resPath);
        foreach (var userPath in candidates)
        {
            if (!FileAccess.FileExists(userPath))
                continue;

            var tex = TryLoadRawFromUser(userPath);
            if (tex is not null)
            {
                GD.Print($"[AssetLoader] override applied for {resPath} -> {userPath}");
                return tex;
            }
            // Decode failed (file exists but Image.Load raised). TryLoadRawFromUser
            // already pushed a warning — try the next candidate, then res://.
        }

        // No user override took. Standard res:// load.
        try
        {
            var tex = ResourceLoader.Load<Texture2D>(resPath);
            if (tex is not null)
                return tex;

            GD.PushError($"[AssetLoader] ResourceLoader.Load<Texture2D>({resPath}) returned null");
            return null;
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[AssetLoader] ResourceLoader.Load<Texture2D>({resPath}) threw: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Map a <c>res://assets/&lt;rest&gt;</c> dir + asset name to its
    /// <c>user://&lt;rest&gt;/&lt;asset&gt;</c> equivalent. Returns null
    /// if <c>assetDir</c> does not live under the canonical
    /// <c>res://assets/</c> root (in which case there is no user://
    /// override channel for it — same convention as AssetResolver).
    /// </summary>
    private static string? TryMapToUserPath(string assetDir, string assetName)
    {
        if (assetDir is null || !assetDir.StartsWith(ResAssetPrefix))
            return null;
        var rest = assetDir.Substring(ResAssetPrefix.Length);
        return $"{UserAssetPrefix}{rest}/{assetName}";
    }

    /// <summary>
    /// Decode a raw PNG/JPG sitting under <c>user://</c>. The editor never
    /// imports user:// files (no <c>.import</c> sidecar), so
    /// <see cref="GD.Load{T}"/> would return null — we go through
    /// <see cref="Image.Load(string)"/> + <see cref="ImageTexture.CreateFromImage"/>.
    /// Mirrors <c>AssetResolver.TryLoadFromUser</c>.
    /// </summary>
    private static Texture2D? TryLoadRawFromUser(string userPath)
    {
        try
        {
            var image = new Image();
            var err = image.Load(userPath);
            if (err != Error.Ok)
            {
                GD.PushWarning($"[AssetLoader] Image.Load failed for {userPath}: {err} — falling back to res://");
                return null;
            }
            return ImageTexture.CreateFromImage(image);
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"[AssetLoader] user load failed for {userPath}: {ex.Message} — falling back to res://");
            return null;
        }
    }
}
