using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin tests for <see cref="MaquetteModeLogic"/> — the eM↔eT state
/// machine + transition planner (Étape B, Rune, 2026-05-23). The planner
/// is what stops a second click on the same mission's district from
/// re-loading the texture and re-parking the camera (the "no-op
/// idempotent" branch of the F6 smoke).
/// </summary>
public sealed class MaquetteModeLogicTests
{
    // --- Texture path mapping -------------------------------------------

    [Theory]
    [InlineData(
        "lower_quays",
        "res://assets/wayfinders_visual_assets/e3/wf_e3_district_lower_quays_mesh_iso_1600x800.png")]
    [InlineData(
        "high_wall",
        "res://assets/wayfinders_visual_assets/e3/wf_e3_district_high_wall_mesh_iso_1600x800.png")]
    public void DistrictMapTexturePath_builds_canonical_eT_mesh_path(
        string districtSlug, string expectedPath)
    {
        Assert.Equal(expectedPath, MaquetteModeLogic.DistrictMapTexturePath(districtSlug));
    }

    [Fact]
    public void DistrictMapTexturePath_rejects_empty_slug()
    {
        Assert.Throws<System.ArgumentException>(
            () => MaquetteModeLogic.DistrictMapTexturePath(""));
    }

    [Fact]
    public void WorldMapTexturePath_constant_points_at_J6_world_mesh()
    {
        // Locked path — the value the J6 maquette is authored against
        // in GameScreen.tscn. If Mira ever regenerates the eM mesh
        // under a new path, this constant moves in lockstep with the
        // .tscn.
        Assert.Equal(
            "res://assets/wayfinders_visual_assets/e2/wf_e2_world_map_mesh_iso_2048x1024.png",
            MaquetteModeLogic.WorldMapTexturePath);
    }

    // --- PlanEnterDistrict ----------------------------------------------

    [Fact]
    public void PlanEnterDistrict_from_eM_yields_apply_with_district_path()
    {
        var plan = MaquetteModeLogic.PlanEnterDistrict(
            MaquetteMode.WorldMap, currentDistrictId: null, "lower_quays");

        Assert.True(plan.ShouldApply);
        Assert.Equal(MaquetteMode.DistrictMap, plan.TargetMode);
        Assert.Equal("lower_quays", plan.TargetDistrictId);
        Assert.Equal(
            MaquetteModeLogic.DistrictMapTexturePath("lower_quays"),
            plan.TargetTexturePath);
    }

    [Fact]
    public void PlanEnterDistrict_same_district_yields_no_op()
    {
        var plan = MaquetteModeLogic.PlanEnterDistrict(
            MaquetteMode.DistrictMap, currentDistrictId: "lower_quays", "lower_quays");

        Assert.False(plan.ShouldApply);
        Assert.Equal(string.Empty, plan.TargetTexturePath);
    }

    [Fact]
    public void PlanEnterDistrict_different_district_yields_apply()
    {
        // eT(lower_quays) → eT(high_wall) — different mission target,
        // both districts on eT. The planner applies.
        var plan = MaquetteModeLogic.PlanEnterDistrict(
            MaquetteMode.DistrictMap, currentDistrictId: "lower_quays", "high_wall");

        Assert.True(plan.ShouldApply);
        Assert.Equal(MaquetteMode.DistrictMap, plan.TargetMode);
        Assert.Equal("high_wall", plan.TargetDistrictId);
        Assert.Equal(
            MaquetteModeLogic.DistrictMapTexturePath("high_wall"),
            plan.TargetTexturePath);
    }

    [Fact]
    public void PlanEnterDistrict_rejects_empty_district_id()
    {
        Assert.Throws<System.ArgumentException>(() =>
            MaquetteModeLogic.PlanEnterDistrict(
                MaquetteMode.WorldMap, currentDistrictId: null, ""));
    }

    // --- PlanReturnToWorldMap -------------------------------------------

    [Fact]
    public void PlanReturnToWorldMap_from_eT_yields_apply_with_world_path()
    {
        var plan = MaquetteModeLogic.PlanReturnToWorldMap(
            MaquetteMode.DistrictMap, currentDistrictId: "lower_quays");

        Assert.True(plan.ShouldApply);
        Assert.Equal(MaquetteMode.WorldMap, plan.TargetMode);
        Assert.Null(plan.TargetDistrictId);
        Assert.Equal(MaquetteModeLogic.WorldMapTexturePath, plan.TargetTexturePath);
    }

    [Fact]
    public void PlanReturnToWorldMap_when_already_eM_is_no_op()
    {
        var plan = MaquetteModeLogic.PlanReturnToWorldMap(
            MaquetteMode.WorldMap, currentDistrictId: null);

        Assert.False(plan.ShouldApply);
        Assert.Equal(string.Empty, plan.TargetTexturePath);
    }

    // --- Round-trip integration -----------------------------------------

    [Fact]
    public void Round_trip_eM_to_eT_and_back_planner_outcomes()
    {
        // Smoke the full F6 round-trip path the controller will run.
        var mode = MaquetteMode.WorldMap;
        string? district = null;

        // Click 1 — enter lower_quays.
        var p1 = MaquetteModeLogic.PlanEnterDistrict(mode, district, "lower_quays");
        Assert.True(p1.ShouldApply);
        mode = p1.TargetMode;
        district = p1.TargetDistrictId;
        Assert.Equal(MaquetteMode.DistrictMap, mode);
        Assert.Equal("lower_quays", district);

        // Click 2 — Escape, return to eM.
        var p2 = MaquetteModeLogic.PlanReturnToWorldMap(mode, district);
        Assert.True(p2.ShouldApply);
        mode = p2.TargetMode;
        district = p2.TargetDistrictId;
        Assert.Equal(MaquetteMode.WorldMap, mode);
        Assert.Null(district);

        // Click 3 — Escape again, must be a no-op.
        var p3 = MaquetteModeLogic.PlanReturnToWorldMap(mode, district);
        Assert.False(p3.ShouldApply);
    }
}
