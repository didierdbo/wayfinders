using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wayfinders.Client.Services;

/// <summary>
/// Pure-C# loader and resolver for the <c>asset_keys.json</c> mapping
/// (asset key -&gt; relative path under the asset root). Lives separately
/// from <see cref="AssetResolver"/> so the JSON-shape contract is testable
/// hors-Godot — see pre-brief §4.6.
///
/// <para>
/// The JSON file is the single source of truth shared between Mira (asset
/// generation), Varn (spec naming), and Rune (code lookup). When Mira
/// adds a new asset key, the only edit needed in code is appending a
/// line to this JSON file.
/// </para>
///
/// <para>
/// <b>AOT note (L5a audit aware).</b> Deserialized via the
/// source-generated <see cref="AssetKeyMapJsonContext"/> — no reflection
/// path. Aligns with the Phase 6 L5a precedent on
/// <c>ApiJsonContext</c>.
/// </para>
/// </summary>
public sealed class AssetKeyMap
{
    private readonly IReadOnlyDictionary<string, string> _keyToPath;

    /// <summary>Number of keys in the map. Useful for tests and diagnostics.</summary>
    public int Count => _keyToPath.Count;

    /// <summary>All known keys, alphabetically sorted, for diagnostic logging.</summary>
    public IReadOnlyList<string> AllKeys
    {
        get
        {
            var list = new List<string>(_keyToPath.Keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }

    public AssetKeyMap(IReadOnlyDictionary<string, string> keyToPath)
    {
        _keyToPath = keyToPath ?? throw new ArgumentNullException(nameof(keyToPath));
    }

    /// <summary>
    /// Resolve a key to its registered relative path, or null if the key
    /// is not in the map. The map intentionally does not check filesystem
    /// existence — that is <see cref="AssetResolver"/>'s job.
    /// </summary>
    public string? TryResolvePath(string assetKey) =>
        _keyToPath.TryGetValue(assetKey, out var path) ? path : null;

    /// <summary>
    /// Parse the JSON document into a map. Throws on malformed input —
    /// the asset_keys file is shipped, not user input, so a parse error
    /// is a build-time mistake we want loud.
    /// </summary>
    public static AssetKeyMap FromJson(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
            throw new ArgumentException("asset_keys JSON is empty", nameof(jsonText));

        var dto = JsonSerializer.Deserialize(jsonText, AssetKeyMapJsonContext.Default.AssetKeyMapDto)
                  ?? throw new InvalidOperationException("asset_keys JSON parsed to null");

        return new AssetKeyMap(dto.Keys);
    }
}

/// <summary>
/// Wire-shape of the asset_keys.json file. <c>keys</c> wraps the dictionary
/// so we can add metadata fields (version, last_updated) later without a
/// breaking format change.
/// </summary>
public sealed record AssetKeyMapDto(
    [property: JsonPropertyName("keys")] Dictionary<string, string> Keys);

/// <summary>
/// Source-generated JSON context for <see cref="AssetKeyMapDto"/>. Gives
/// the AOT-safe deserialization path matching the L5a audit precedent.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(AssetKeyMapDto))]
internal sealed partial class AssetKeyMapJsonContext : JsonSerializerContext
{
}
