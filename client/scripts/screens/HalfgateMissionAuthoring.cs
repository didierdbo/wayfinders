using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Varn-locked hardcoded authoring for the Halfgate E2.1 tutorial
/// mission, encoded against the A4.2 reveal grammar (canonical schema :
/// <c>pop_effect</c> / <c>success_effect</c> / <c>reveal_on_fail</c> --
/// the A3.2 fields <c>reveal_level</c> / <c>reveal_tile_count</c> are
/// SUPERSEDED and MUST NOT appear here).
///
/// <para>
/// <b>One mission at E2.1, no UI yet.</b> Per Varn jalons §E2.1, the
/// hardcoded tutorial mission lives in code (or a Godot Resource) but
/// has no Mission panel UI -- that lands at E2.3. The E2.1 visible
/// effect is purely the reveal substrate : at boot the mission's
/// <c>pop_effect</c> fires, transitioning the 4 central footprint cells
/// from <see cref="TileRevealState.Fog"/> to
/// <see cref="TileRevealState.Partial"/> (rendered as the parchment
/// overlay shader).
/// </para>
///
/// <para>
/// <b>Why a static class and not a Godot Resource.</b> Same concrete-first
/// rationale as <see cref="NpcCatalog"/> + <see cref="AreaGridLogic"/> :
/// the MVP has ONE tutorial mission. Promoting to a <c>.tres</c> Resource
/// invites typos and adds an editor-roundtrip cost (re-import) for every
/// narrative tweak. When E2.6 lands the ML mission generator, the mission
/// shape will live in a typed DTO (<see cref="HalfgateMission"/>) flowing
/// from Tess's API, and this static authoring becomes the offline /
/// fallback path (per Varn §E2.6 jalon "fall back to the hardcoded 2
/// missions").
/// </para>
///
/// <para>
/// <b>Naming neutrality.</b> The mission id is
/// <c>mission_tuto_halfgate</c> -- semantically neutral, no narrative
/// commitments. The narrative copy is Pratchett-tone but minimal at
/// E2.1 (one line as the rumor hook).
/// </para>
///
/// <para>
/// <b>A4.2 schema field map.</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="HalfgateMission.PopEffectPoiVisible"/> --
///         <c>pop_effect.poi_visible : bool</c></item>
///   <item><see cref="HalfgateMission.PopEffectTilesToPartial"/> --
///         <c>pop_effect.tiles_to_partial : list[CellCoord]</c></item>
///   <item><see cref="HalfgateMission.SuccessEffectRevealRule"/> --
///         <c>success_effect.reveal_rule : enum</c></item>
///   <item><see cref="HalfgateMission.RevealOnFailRule"/> --
///         <c>reveal_on_fail.rule : enum</c></item>
/// </list>
/// </summary>
public static class HalfgateMissionAuthoring
{
    /// <summary>
    /// Mission id for the Halfgate E2.1 tutorial mission. Stable string,
    /// matches any future Resource / DTO surface that consumes the same
    /// authoring.
    /// </summary>
    public const string TutorialMissionId = "mission_tuto_halfgate";

    /// <summary>
    /// Single source of truth for the tutorial mission entry. Resolved
    /// once at boot by <c>E2AreaMap.OnEnter</c>.
    /// </summary>
    public static HalfgateMission Tutorial { get; } = new(
        MissionId: TutorialMissionId,
        DisplayName: "The first footing",
        // Pratchett-tone : intimate, mock-serious, one-line rumor hook.
        // The Cadastre approves of footings, in writing, on a quarter-sheet
        // of parchment, twice notarised. Anything else is a footing too,
        // but unofficial.
        NarrativeHook: "Someone has been walking, deliberately, in the centre. The Cadastre would like a name.",
        PopEffectPoiVisible: true,
        // Locked from AreaGridLogic.CentralPoiFootprint() : the 4 intramuros
        // cells around the centre. These will transition fog -> partial
        // when the mission pops at E2.1 boot.
        PopEffectTilesToPartial: AreaGridLogic.CentralPoiFootprint(),
        // A4.2 enum : on success, the recruited NPC's known_tiles are
        // applied to the partial set. Not exercised at E2.1 (no Recruit
        // button until E2.2), but the schema field is filled per the
        // Varn-locked spec.
        SuccessEffectRevealRule: RevealRule.ByRecruitedNpc,
        // A4.2 MVE default : reveal_on_fail = keep_partial for all
        // mission types (EC4).
        RevealOnFailRule: RevealOnFail.KeepPartial);
}

/// <summary>
/// A4.2-shaped reveal rule on mission success. Closed lookup, Varn-locked.
/// MVP wires only <see cref="ByRecruitedNpc"/> ; the others are
/// schema-compatible for E2.2+ and the future ML-mission emergence path
/// (E2.6).
/// </summary>
public enum RevealRule
{
    /// <summary>
    /// The recruited NPC's <c>known_tiles</c> (A4.3) are merged into the
    /// Company's partial set. Fog cells transition to Partial ; cells
    /// already Revealed stay Revealed (no downgrade per EC3).
    /// </summary>
    ByRecruitedNpc = 0,

    /// <summary>
    /// The POI footprint cells transition Partial -> Revealed
    /// (deterministic, mission-internal). Schema-compatible, MVP-unused.
    /// </summary>
    PoiFootprintToRevealed = 1,

    /// <summary>
    /// A custom payload-driven cell set transitions to Revealed.
    /// Schema-compatible, MVP-unused. Used by future ML-generated
    /// missions that ship explicit cell lists.
    /// </summary>
    CustomCellSet = 2,
}

/// <summary>
/// A4.2-shaped reveal rule on mission failure. Closed lookup, Varn-locked.
/// MVE locks <see cref="KeepPartial"/> for ALL mission types (EC4) ; the
/// other values are schema-compatible, MVE-deferred (A4.6).
/// </summary>
public enum RevealOnFail
{
    /// <summary>
    /// Failure preserves the partial knowledge. The Company "still knows
    /// the rumor existed" -- canonical MVE default per Varn EC4.
    /// </summary>
    KeepPartial = 0,

    /// <summary>
    /// Failure rolls the cells back to Fog. Schema-compatible,
    /// MVE-deferred.
    /// </summary>
    RevertToFog = 1,

    /// <summary>
    /// Failure escalates to Revealed (the player learns the area
    /// "the hard way"). Schema-compatible, MVE-deferred.
    /// </summary>
    EscalateToRevealed = 2,
}

/// <summary>
/// Pure-data record carrying one mission entry. Encoded against the
/// A4.2 reveal grammar. Used by <see cref="HalfgateMissionAuthoring"/>
/// for the hardcoded tutorial mission ; will be re-used as the DTO shape
/// when E2.6 wires the ML mission generator.
/// </summary>
/// <param name="MissionId">Stable mission identifier.</param>
/// <param name="DisplayName">Player-facing title (Pratchett-tone,
/// 1 line, ~30-50 char).</param>
/// <param name="NarrativeHook">Single-line rumor hook (Pratchett-tone,
/// ~60-140 char). Surfaced by the per-cell hover tooltip (EC6 path) for
/// any partial cell included in <see cref="PopEffectTilesToPartial"/>.</param>
/// <param name="PopEffectPoiVisible">A4.2 :
/// <c>pop_effect.poi_visible</c>.</param>
/// <param name="PopEffectTilesToPartial">A4.2 :
/// <c>pop_effect.tiles_to_partial</c>. Cells that transition fog ->
/// partial when the mission pops.</param>
/// <param name="SuccessEffectRevealRule">A4.2 :
/// <c>success_effect.reveal_rule</c>.</param>
/// <param name="RevealOnFailRule">A4.2 : <c>reveal_on_fail.rule</c>.
/// MVE locked at <see cref="RevealOnFail.KeepPartial"/>.</param>
public sealed record HalfgateMission(
    string MissionId,
    string DisplayName,
    string NarrativeHook,
    bool PopEffectPoiVisible,
    IReadOnlyList<GridCoord> PopEffectTilesToPartial,
    RevealRule SuccessEffectRevealRule,
    RevealOnFail RevealOnFailRule);
