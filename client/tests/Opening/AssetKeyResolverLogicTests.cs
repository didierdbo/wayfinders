using System;
using System.Collections.Generic;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="AssetKeyResolverLogic"/> — the path
/// construction policy shared by the runtime
/// <see cref="AssetResolver"/> autoload AND the editor-only
/// <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/>. The
/// extraction landed in the Phase 9 slice ménage (2026-05-08) after the
/// slice 3.6 silent mismatch (tool resolved res:// while runtime resolved
/// user://). This suite pins the policy so the two consumers cannot drift
/// again.
///
/// <para>
/// <b>Out of scope here.</b> Filesystem existence checks, Image.Load
/// branches, ResourceLoader fallbacks — all I/O lives in the Godot-bound
/// callers. xUnit pins the path-string policy ; the autoload's filesystem
/// branch is exercised through the J1 manual integration checklist
/// (drop a PNG in user://, observe the override log, delete it, observe
/// the res:// fallback).
/// </para>
/// </summary>
public sealed class AssetKeyResolverLogicTests
{
    private static AssetKeyMap MakeMap(params (string key, string path)[] entries)
    {
        var dict = new Dictionary<string, string>(entries.Length, StringComparer.Ordinal);
        foreach (var (k, p) in entries) dict[k] = p;
        return new AssetKeyMap(dict);
    }

    [Fact]
    public void ResolvePathsForKey_known_key_returns_user_then_res_pair()
    {
        var map = MakeMap(("e1.world_map", "e2/wf_e2_carte_monde_base_3840x2160.png"));

        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            "e1.world_map",
            "user://wayfinders_visual_assets/",
            "res://assets/wayfinders_visual_assets/",
            map);

        Assert.NotNull(resolved);
        Assert.Equal(
            "user://wayfinders_visual_assets/e2/wf_e2_carte_monde_base_3840x2160.png",
            resolved!.Value.UserPath);
        Assert.Equal(
            "res://assets/wayfinders_visual_assets/e2/wf_e2_carte_monde_base_3840x2160.png",
            resolved.Value.ResPath);
        Assert.Equal(
            "e2/wf_e2_carte_monde_base_3840x2160.png",
            resolved.Value.RelativePath);
    }

    [Fact]
    public void ResolvePathsForKey_unknown_key_returns_null()
    {
        var map = MakeMap(("e1.bureau", "e1/bg.png"));

        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            "e9.does_not_exist",
            "user://x/",
            "res://y/",
            map);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolvePathsForKey_normalises_trailing_slash_on_both_roots()
    {
        // Critical: runtime AssetResolver.Resolve trims a single trailing
        // slash before concat. Tool must do the exact same thing.
        var map = MakeMap(("e1.world_map", "e2/world.png"));

        var withSlash = AssetKeyResolverLogic.ResolvePathsForKey(
            "e1.world_map", "user://root/", "res://root/", map);
        var noSlash = AssetKeyResolverLogic.ResolvePathsForKey(
            "e1.world_map", "user://root", "res://root", map);

        Assert.NotNull(withSlash);
        Assert.NotNull(noSlash);
        Assert.Equal(withSlash!.Value.UserPath, noSlash!.Value.UserPath);
        Assert.Equal(withSlash.Value.ResPath, noSlash.Value.ResPath);
        Assert.Equal("user://root/e2/world.png", withSlash.Value.UserPath);
        Assert.Equal("res://root/e2/world.png", withSlash.Value.ResPath);
    }

    [Fact]
    public void ResolvePathsForKey_does_not_double_slash_when_both_root_and_relative_have_slashes()
    {
        var map = MakeMap(("e1.world_map", "e2/world.png"));

        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            "e1.world_map", "user://wayfinders_visual_assets/", "res://assets/wayfinders_visual_assets/", map);

        Assert.NotNull(resolved);
        Assert.DoesNotContain("//e2/", resolved!.Value.UserPath.AsSpan(8).ToString());
        Assert.DoesNotContain("//e2/", resolved.Value.ResPath.AsSpan(7).ToString());
    }

    [Fact]
    public void ResolvePathsForKey_preserves_godot_virtual_path_prefix_double_slash()
    {
        // The "//" in user:// and res:// is part of the Godot virtual path
        // scheme, NOT a duplicate separator. Trimming must not eat it.
        var map = MakeMap(("k", "rel.png"));

        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            "k", "user://r/", "res://r/", map);

        Assert.NotNull(resolved);
        Assert.StartsWith("user://", resolved!.Value.UserPath);
        Assert.StartsWith("res://", resolved.Value.ResPath);
    }

    [Fact]
    public void ResolvePathsForKey_handles_relative_path_with_subdirs()
    {
        var map = MakeMap(("e4.portrait.kira.calm", "e4/wf_e4_portrait_kira_calm_1024x1536.png"));

        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            "e4.portrait.kira.calm",
            "user://wayfinders_visual_assets/",
            "res://assets/wayfinders_visual_assets/",
            map);

        Assert.NotNull(resolved);
        Assert.Equal(
            "user://wayfinders_visual_assets/e4/wf_e4_portrait_kira_calm_1024x1536.png",
            resolved!.Value.UserPath);
    }

    [Fact]
    public void ResolvePathsForKey_throws_on_null_key()
    {
        var map = MakeMap();
        Assert.Throws<ArgumentNullException>(() =>
            AssetKeyResolverLogic.ResolvePathsForKey(null!, "user://", "res://", map));
    }

    [Fact]
    public void ResolvePathsForKey_throws_on_null_user_root()
    {
        var map = MakeMap();
        Assert.Throws<ArgumentNullException>(() =>
            AssetKeyResolverLogic.ResolvePathsForKey("k", null!, "res://", map));
    }

    [Fact]
    public void ResolvePathsForKey_throws_on_null_res_root()
    {
        var map = MakeMap();
        Assert.Throws<ArgumentNullException>(() =>
            AssetKeyResolverLogic.ResolvePathsForKey("k", "user://", null!, map));
    }

    [Fact]
    public void ResolvePathsForKey_throws_on_null_key_map()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AssetKeyResolverLogic.ResolvePathsForKey("k", "user://", "res://", null!));
    }

    [Fact]
    public void ResolvePathsForKey_relative_path_field_matches_keymap_value()
    {
        // Diagnostic helper field — useful when the tool / autoload logs
        // the resolution. Pin the value matches what TryResolvePath returned.
        var map = MakeMap(("e1.world_map", "e2/wf_e2_carte_monde_base_3840x2160.png"));

        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            "e1.world_map", "user://x/", "res://y/", map);

        Assert.NotNull(resolved);
        Assert.Equal("e2/wf_e2_carte_monde_base_3840x2160.png", resolved!.Value.RelativePath);
        Assert.Equal(map.TryResolvePath("e1.world_map"), resolved.Value.RelativePath);
    }

    [Fact]
    public void ResolvePathsForKey_user_and_res_paths_diverge_only_in_the_root()
    {
        // The whole point of the helper : same relative tail under both
        // roots, so user:// is a true override of res://.
        var map = MakeMap(("k", "sub/file.png"));

        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            "k", "user://A/", "res://B/", map);

        Assert.NotNull(resolved);
        Assert.EndsWith("/sub/file.png", resolved!.Value.UserPath);
        Assert.EndsWith("/sub/file.png", resolved.Value.ResPath);
    }

    [Fact]
    public void ResolvePathsForKey_empty_key_map_yields_null_for_any_key()
    {
        var map = MakeMap();
        var resolved = AssetKeyResolverLogic.ResolvePathsForKey(
            "anything", "user://", "res://", map);
        Assert.Null(resolved);
    }
}
