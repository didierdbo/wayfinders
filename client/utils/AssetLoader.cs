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
///   <item>If <c>&lt;assetDir&gt;/&lt;assetName&gt;</c> exists → load it
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
/// <b>Future use.</b> Any new Wayfinders asset loaded by client C# code
/// should go through <see cref="LoadAssetOrPlaceholder"/>. See spec
/// <c>Owner's Inbox/mira-placeholder-framework-spec-2026-05-13-FR.md §6</c>.
/// </para>
/// </summary>
public static class AssetLoader
{
    /// <summary>
    /// Loads <c>&lt;assetDir&gt;/&lt;assetName&gt;</c> if present; otherwise
    /// falls back to <c>&lt;basename&gt;_PLACEHOLDER.&lt;ext&gt;</c>.
    /// Returns <c>null</c> if neither file exists (caller decides recovery).
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
        string finalPath = $"{assetDir}/{assetName}";

        if (FileAccess.FileExists(finalPath))
        {
            GD.Print($"[PLACEHOLDER LOAD] {assetName} -> final ({finalPath})");
            return GD.Load<Texture2D>(finalPath);
        }

        // Compose placeholder path: <basename>_PLACEHOLDER.<ext>.
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
}
