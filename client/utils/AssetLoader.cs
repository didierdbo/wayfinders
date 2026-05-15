using Godot;

namespace Wayfinders.Client.Utils;

/// <summary>
/// Asset loading utility implementing the Mira &lt;-&gt; Rune placeholder
/// framework v1.0 contract (locked 2026-05-13).
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
/// <b>Future use.</b> Any new Wayfinders asset loaded by client C# code
/// should go through <see cref="LoadAssetOrPlaceholder"/>. See spec
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
