using System;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# state machine + transition planner for the eM ↔ eT bascule
/// (Étape B — bascule eM → eT au clic mission, Rune, 2026-05-23). The
/// Godot-bound <see cref="MaquetteModeController"/> owns the engine wiring
/// (texture swap, camera reparking, overlay visibility) ; this helper
/// owns the decisions — which texture to load next, whether the
/// transition is a no-op, whether a return-to-eM is legal right now.
///
/// <para>
/// <b>What "modes" the maquette has.</b> The roadmap (J6/J7 + Étape B)
/// names exactly two : <see cref="MaquetteMode.WorldMap"/> ("eM", the
/// 2048×1024 world mesh) and <see cref="MaquetteMode.DistrictMap"/>
/// ("eT", a 1600×800 district mesh). Étape B wires the swap between
/// them ; future scopes (J8 recruit on eT, J9 multi-district) reuse the
/// same surface. We resist the urge to model
/// <c>Transitioning</c> / <c>Animating</c> as separate states : the swap
/// is currently instant (no tween), and adding the state up-front would
/// be premature.
/// </para>
///
/// <para>
/// <b>Texture path policy.</b> District mesh path :
/// <c>res://assets/wayfinders_visual_assets/e3/wf_e3_district_{slug}_mesh_iso_1600x800.png</c>.
/// World mesh path :
/// <c>res://assets/wayfinders_visual_assets/e2/wf_e2_world_map_mesh_iso_2048x1024.png</c>.
/// Both paths are <see cref="WorldMapTexturePath"/> /
/// <see cref="DistrictMapTexturePath"/> constants here ; the controller
/// loads them via the universal user-override mapper
/// (<c>AssetLoader.LoadTextureWithUserOverride</c>) so a user-side hot
/// swap works without a rebuild (trap #13).
/// </para>
///
/// <para>
/// <b>Idempotence.</b> <see cref="PlanEnterDistrict"/> against the same
/// district id as the current state is a no-op — the F6 smoke would log
/// "already in eT(lower_quays), no transition". Same for
/// <see cref="PlanReturnToWorldMap"/> when already on eM. The Godot
/// controller asks the planner first, only swaps the texture if the
/// outcome's <see cref="TransitionOutcome.ShouldApply"/> is true. Cheap,
/// pinnable, and avoids the camera-reparking flicker that re-running the
/// load would cause.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Plain enums, records, string paths. No
/// engine types. Same seam discipline as
/// <see cref="MaquetteContentLogic"/> and the rest of the J3 logic layer.
/// </para>
/// </summary>
public static class MaquetteModeLogic
{
    /// <summary>
    /// res:// path to the eM world map mesh. Same value the J6 maquette
    /// <c>BackgroundTexturePath</c> is authored with in
    /// <c>GameScreen.tscn</c> ; documented here so the eM-restore path
    /// has a single named source instead of a magic string in the
    /// controller.
    /// </summary>
    public const string WorldMapTexturePath =
        "res://assets/wayfinders_visual_assets/e2/wf_e2_world_map_mesh_iso_2048x1024.png";

    /// <summary>
    /// res:// path template for an eT district mesh. The
    /// <c>{slug}</c> placeholder is the world.yaml district id —
    /// <c>lower_quays</c> or <c>high_wall</c> in M1. Use
    /// <see cref="DistrictMapTexturePath"/> rather than interpolating
    /// the string at every call site so a future asset relocation has
    /// one edit point.
    /// </summary>
    public const string DistrictMapTexturePathTemplate =
        "res://assets/wayfinders_visual_assets/e3/wf_e3_district_{slug}_mesh_iso_1600x800.png";

    /// <summary>
    /// Build the eT mesh path for one district slug.
    /// </summary>
    /// <param name="districtSlug">The world.yaml district id
    /// (e.g. <c>"lower_quays"</c>). Must be non-empty.</param>
    /// <returns>The res:// path of that district's eT mesh.</returns>
    /// <exception cref="ArgumentException">If <paramref name="districtSlug"/>
    /// is null or empty.</exception>
    public static string DistrictMapTexturePath(string districtSlug)
    {
        if (string.IsNullOrEmpty(districtSlug))
        {
            throw new ArgumentException(
                "District slug must be non-empty to build an eT mesh path.",
                nameof(districtSlug));
        }
        return DistrictMapTexturePathTemplate.Replace("{slug}", districtSlug);
    }

    /// <summary>
    /// Outcome of a transition planner call : whether to apply the
    /// transition, and if so, the target mode + texture path + a
    /// human-readable diagnostic the controller can log. A no-op outcome
    /// keeps <see cref="TargetMode"/> / <see cref="TargetTexturePath"/>
    /// at their "no change" values (the current mode, an empty path).
    /// </summary>
    /// <param name="ShouldApply">True iff the caller should swap.</param>
    /// <param name="TargetMode">The mode after the swap (same as current
    /// when <see cref="ShouldApply"/> is false).</param>
    /// <param name="TargetDistrictId">The district slug after the swap,
    /// or null when targeting <see cref="MaquetteMode.WorldMap"/>.</param>
    /// <param name="TargetTexturePath">The res:// texture path to load,
    /// or empty string on a no-op.</param>
    /// <param name="Diagnostic">A short human-readable hint for the F6
    /// log.</param>
    public readonly record struct TransitionOutcome(
        bool ShouldApply,
        MaquetteMode TargetMode,
        string? TargetDistrictId,
        string TargetTexturePath,
        string Diagnostic);

    /// <summary>
    /// Plan an "enter eT" transition from the current mode onto
    /// <paramref name="districtId"/>. Idempotent : asking to enter the
    /// district we are already on returns a no-op outcome.
    /// </summary>
    /// <param name="currentMode">The current maquette mode.</param>
    /// <param name="currentDistrictId">The current district id (null
    /// when <paramref name="currentMode"/> is
    /// <see cref="MaquetteMode.WorldMap"/>).</param>
    /// <param name="districtId">The district to enter. Must be non-empty.</param>
    /// <returns>A <see cref="TransitionOutcome"/>.</returns>
    /// <exception cref="ArgumentException">If <paramref name="districtId"/>
    /// is null or empty.</exception>
    public static TransitionOutcome PlanEnterDistrict(
        MaquetteMode currentMode,
        string? currentDistrictId,
        string districtId)
    {
        if (string.IsNullOrEmpty(districtId))
        {
            throw new ArgumentException(
                "Cannot enter a district with a null/empty id.",
                nameof(districtId));
        }

        if (currentMode == MaquetteMode.DistrictMap
            && string.Equals(currentDistrictId, districtId, StringComparison.Ordinal))
        {
            return new TransitionOutcome(
                ShouldApply: false,
                TargetMode: MaquetteMode.DistrictMap,
                TargetDistrictId: districtId,
                TargetTexturePath: string.Empty,
                Diagnostic: $"already in eT(district='{districtId}') — no swap");
        }

        return new TransitionOutcome(
            ShouldApply: true,
            TargetMode: MaquetteMode.DistrictMap,
            TargetDistrictId: districtId,
            TargetTexturePath: DistrictMapTexturePath(districtId),
            Diagnostic: currentMode == MaquetteMode.WorldMap
                ? $"eM → eT(district='{districtId}')"
                : $"eT(district='{currentDistrictId}') → eT(district='{districtId}')");
    }

    /// <summary>
    /// Plan a "return to eM" transition from the current mode.
    /// Idempotent : asking to return when already on eM returns a no-op.
    /// </summary>
    /// <param name="currentMode">The current maquette mode.</param>
    /// <param name="currentDistrictId">The current district id, null on eM.</param>
    /// <returns>A <see cref="TransitionOutcome"/>.</returns>
    public static TransitionOutcome PlanReturnToWorldMap(
        MaquetteMode currentMode,
        string? currentDistrictId)
    {
        if (currentMode == MaquetteMode.WorldMap)
        {
            return new TransitionOutcome(
                ShouldApply: false,
                TargetMode: MaquetteMode.WorldMap,
                TargetDistrictId: null,
                TargetTexturePath: string.Empty,
                Diagnostic: "already on eM — no swap");
        }

        return new TransitionOutcome(
            ShouldApply: true,
            TargetMode: MaquetteMode.WorldMap,
            TargetDistrictId: null,
            TargetTexturePath: WorldMapTexturePath,
            Diagnostic: $"eT(district='{currentDistrictId}') → eM");
    }
}

/// <summary>
/// The two modes the maquette (layer B of the Game Screen Shell) can
/// display. Étape B (2026-05-23) wires the swap between them.
/// </summary>
public enum MaquetteMode
{
    /// <summary>The eM world map — the 2048×1024 iso mesh of the whole
    /// world, with the Halfgate city marker and the active mission POI
    /// pins.</summary>
    WorldMap,

    /// <summary>An eT district map — the 1600×800 iso mesh of one
    /// district (lower_quays or high_wall in M1).</summary>
    DistrictMap,
}
