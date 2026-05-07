using System;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="AssetKeyMap"/> — JSON parsing and key
/// resolution. The Godot-side <see cref="AssetResolver"/> filesystem
/// fallback is validated via the J1 manual integration checklist (Risk #2
/// Windows file lock cannot be reproduced in xUnit).
/// </summary>
public sealed class AssetKeyMapTests
{
    [Fact]
    public void FromJson_parses_well_formed_mapping()
    {
        const string json = """
            {
              "keys": {
                "e1.bureau": "e1/bg.png",
                "e2.world_map": "e2/world.png"
              }
            }
            """;

        var map = AssetKeyMap.FromJson(json);

        Assert.Equal(2, map.Count);
        Assert.Equal("e1/bg.png", map.TryResolvePath("e1.bureau"));
        Assert.Equal("e2/world.png", map.TryResolvePath("e2.world_map"));
    }

    [Fact]
    public void TryResolvePath_unknown_key_returns_null()
    {
        const string json = """
            { "keys": { "e1.bureau": "e1/bg.png" } }
            """;

        var map = AssetKeyMap.FromJson(json);

        Assert.Null(map.TryResolvePath("e9.does_not_exist"));
    }

    [Fact]
    public void FromJson_throws_on_empty_string()
    {
        Assert.Throws<ArgumentException>(() => AssetKeyMap.FromJson(""));
        Assert.Throws<ArgumentException>(() => AssetKeyMap.FromJson("   "));
    }

    [Fact]
    public void FromJson_throws_on_malformed_json()
    {
        Assert.ThrowsAny<Exception>(() => AssetKeyMap.FromJson("{ this is not json"));
    }

    [Fact]
    public void FromJson_tolerates_extra_doc_field()
    {
        // Mirrors the actual asset_keys.json shape with a leading _doc.
        const string json = """
            {
              "_doc": "human-readable explanation",
              "keys": { "e1.bureau": "e1/bg.png" }
            }
            """;

        var map = AssetKeyMap.FromJson(json);

        Assert.Equal(1, map.Count);
        Assert.Equal("e1/bg.png", map.TryResolvePath("e1.bureau"));
    }

    [Fact]
    public void AllKeys_returns_alphabetical_snapshot()
    {
        const string json = """
            {
              "keys": {
                "z.last": "z.png",
                "a.first": "a.png",
                "m.middle": "m.png"
              }
            }
            """;

        var map = AssetKeyMap.FromJson(json);
        var keys = map.AllKeys;

        Assert.Equal(3, keys.Count);
        Assert.Equal("a.first", keys[0]);
        Assert.Equal("m.middle", keys[1]);
        Assert.Equal("z.last", keys[2]);
    }

    [Fact]
    public void Empty_keys_section_yields_zero_count()
    {
        const string json = """
            { "keys": {} }
            """;

        var map = AssetKeyMap.FromJson(json);
        Assert.Equal(0, map.Count);
        Assert.Null(map.TryResolvePath("anything"));
    }
}
