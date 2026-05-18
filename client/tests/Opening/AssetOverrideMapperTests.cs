using Wayfinders.Client.Services;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="AssetOverrideMapper"/> — the universal
/// user:// override candidate policy locked 2026-05-18 after the POI
/// Halfgate override bypass diagnostic (trap #13, memo
/// <c>feedback_godot_rendering_input_traps.md</c>).
///
/// <para>
/// <b>Out of scope here.</b> Filesystem existence checks, raw PNG decode,
/// ResourceLoader fallback — all I/O lives in
/// <see cref="Wayfinders.Client.Utils.AssetLoader.LoadTextureWithUserOverride"/>.
/// xUnit pins the path-string policy ; the Godot-bound loader's filesystem
/// branch is exercised through the smoke checklist (drop a PNG in user://,
/// observe override log, delete it, observe res:// fallback).
/// </para>
/// </summary>
public sealed class AssetOverrideMapperTests
{
    [Fact]
    public void BuildUserCandidates_poi_halfgate_emits_symmetric_then_visual_assets_bucket()
    {
        // The Didier 2026-05-18 case. POI Halfgate lives under res://assets/poi/e1/
        // but Didier dropped his override at user://wayfinders_visual_assets/e1/.
        // Candidate 2 must pick up the visual-assets bucket so the override fires.
        var candidates = AssetOverrideMapper.BuildUserCandidates(
            "res://assets/poi/e1/wf_e1_halfgate_poi.png");

        Assert.Equal(2, candidates.Count);
        Assert.Equal("user://poi/e1/wf_e1_halfgate_poi.png", candidates[0]);
        Assert.Equal(
            "user://wayfinders_visual_assets/e1/wf_e1_halfgate_poi.png",
            candidates[1]);
    }

    [Fact]
    public void BuildUserCandidates_visual_assets_source_collapses_to_single_candidate()
    {
        // For res://assets/wayfinders_visual_assets/e1/wf_e1_tile_neutral.png,
        // candidate 1 (symmetric strip) and candidate 2 (visual-assets bucket)
        // resolve to the same user:// path. The mapper must not emit duplicates
        // (the loader would do redundant FileExists I/O).
        var candidates = AssetOverrideMapper.BuildUserCandidates(
            "res://assets/wayfinders_visual_assets/e1/wf_e1_tile_neutral.png");

        Assert.Single(candidates);
        Assert.Equal(
            "user://wayfinders_visual_assets/e1/wf_e1_tile_neutral.png",
            candidates[0]);
    }

    [Fact]
    public void BuildUserCandidates_non_res_assets_path_returns_empty()
    {
        // No override channel for engine-internal pseudo-paths (.godot/imported,
        // scratch scenes, shaders, etc.). The loader will fall straight through
        // to ResourceLoader.
        Assert.Empty(AssetOverrideMapper.BuildUserCandidates("res://shaders/poi_shadow.gdshader"));
        Assert.Empty(AssetOverrideMapper.BuildUserCandidates("res://scenes/play/M1Slice.tscn"));
        Assert.Empty(AssetOverrideMapper.BuildUserCandidates("res://data/asset_keys.json"));
        Assert.Empty(AssetOverrideMapper.BuildUserCandidates("user://wayfinders_visual_assets/e1/already_user.png"));
    }

    [Fact]
    public void BuildUserCandidates_throws_on_null()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            AssetOverrideMapper.BuildUserCandidates(null!));
    }

    [Fact]
    public void BuildUserCandidates_empty_after_prefix_returns_empty()
    {
        // res://assets/ on its own has no relative tail.
        Assert.Empty(AssetOverrideMapper.BuildUserCandidates("res://assets/"));
    }

    [Fact]
    public void BuildUserCandidates_file_directly_under_assets_emits_symmetric_only()
    {
        // res://assets/loose.png has no parent subdir → candidate 2 cannot be
        // built (would need at least one slash to derive parentDir). Only the
        // symmetric strip emits.
        var candidates = AssetOverrideMapper.BuildUserCandidates(
            "res://assets/loose.png");

        Assert.Single(candidates);
        Assert.Equal("user://loose.png", candidates[0]);
    }

    [Fact]
    public void BuildUserCandidates_deep_nested_uses_immediate_parent_for_visual_bucket()
    {
        // res://assets/poi/e1/halfgate_face_b/halfgate_face_b_00.png
        // → candidate 1 symmetric: user://poi/e1/halfgate_face_b/halfgate_face_b_00.png
        // → candidate 2 visual bucket: user://wayfinders_visual_assets/halfgate_face_b/halfgate_face_b_00.png
        // (parentDir is the IMMEDIATE parent — last segment of the head before the file).
        var candidates = AssetOverrideMapper.BuildUserCandidates(
            "res://assets/poi/e1/halfgate_face_b/halfgate_face_b_00.png");

        Assert.Equal(2, candidates.Count);
        Assert.Equal(
            "user://poi/e1/halfgate_face_b/halfgate_face_b_00.png",
            candidates[0]);
        Assert.Equal(
            "user://wayfinders_visual_assets/halfgate_face_b/halfgate_face_b_00.png",
            candidates[1]);
    }

    [Fact]
    public void BuildUserCandidates_e4_portrait_double_match()
    {
        // res://assets/wayfinders_visual_assets/e4/wf_e4_portrait_kira_calm_1024x1536.png
        // collapses to single candidate (candidate 1 == candidate 2). Smoke test
        // for the existing AssetResolver-managed assets.
        var candidates = AssetOverrideMapper.BuildUserCandidates(
            "res://assets/wayfinders_visual_assets/e4/wf_e4_portrait_kira_calm_1024x1536.png");

        Assert.Single(candidates);
        Assert.Equal(
            "user://wayfinders_visual_assets/e4/wf_e4_portrait_kira_calm_1024x1536.png",
            candidates[0]);
    }

    [Fact]
    public void BuildUserCandidates_path_outside_res_assets_returns_empty()
    {
        // Defensive : we never override engine-internal paths or non-asset
        // res:// paths (shaders, scenes, .tres, etc. all live under res://
        // but outside res://assets/).
        Assert.Empty(AssetOverrideMapper.BuildUserCandidates("res://scripts/somefile.cs"));
        Assert.Empty(AssetOverrideMapper.BuildUserCandidates("res://"));
        Assert.Empty(AssetOverrideMapper.BuildUserCandidates(""));
    }

    [Fact]
    public void BuildUserCandidates_does_not_emit_duplicate_when_paths_equal()
    {
        // Pin the de-dup invariant explicitly. If we ever lose it, the loader
        // would FileExists-check the same path twice — wasted I/O but no
        // correctness bug. Still : the contract says distinct paths.
        var candidates = AssetOverrideMapper.BuildUserCandidates(
            "res://assets/wayfinders_visual_assets/e2_area_grid/wf_e2_grid_fog_256x256.png");

        Assert.Single(candidates);
    }
}
