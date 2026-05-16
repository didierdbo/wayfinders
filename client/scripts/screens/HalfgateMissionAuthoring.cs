using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Varn-locked hardcoded authoring for the Halfgate E2.1 tutorial
/// mission, encoded against the A4.2 reveal grammar AS AMENDED BY A4.7
/// (canonical schema : <c>pop_effect</c> / <c>success_effect</c> /
/// <c>reveal_on_fail</c> — the A3.2 fields <c>reveal_level</c> /
/// <c>reveal_tile_count</c> are SUPERSEDED and MUST NOT appear here).
///
/// <para>
/// <b>A4.7 schema migration (2026-05-16).</b> The
/// <c>pop_effect.tiles_to_partial</c> flat list is REPLACED by
/// <c>pop_effect.tiles_to_partial_layer</c> — a per-layer dictionary
/// (<see cref="RevealLayer"/> → <see cref="GridCoord"/> list). The
/// tuto mission now lists its 16 E1 cells (the 4×4 Halfgate footprint)
/// under the <see cref="RevealLayer.E1"/> key ; the auto-projection
/// cascade (<see cref="TileRevealProjector.ProjectTopDown"/>) produces
/// the 64 E2 cells at write time. Explicit cross-layer listing in a
/// single mission is forbidden per A4.7.1 Q1 + A4.7.8 anti-drift.
/// </para>
///
/// <para>
/// <b>One mission at E2.1, no UI yet.</b> Per Varn jalons §E2.1, the
/// hardcoded tutorial mission lives in code (or a Godot Resource) but
/// has no Mission panel UI — that lands at E2.3. The E2.1b visible
/// effect is the reveal substrate : at boot the mission's
/// <c>pop_effect</c> fires, transitioning all 64 E2 cells fog → partial
/// via the R3 top-down projection from the 16 E1 footprint cells
/// (rendered as the parchment overlay shader through
/// <see cref="TileRevealRenderController"/>).
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
/// <c>mission_tuto_halfgate</c> — semantically neutral, no narrative
/// commitments. The narrative copy is Pratchett-tone but minimal at
/// E2.1 (one line as the rumor hook).
/// </para>
///
/// <para>
/// <b>A4.2 + A4.7 schema field map.</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="HalfgateMission.PopEffectPoiVisible"/> —
///         <c>pop_effect.poi_visible : bool</c></item>
///   <item><see cref="HalfgateMission.PopEffectTilesToPartialLayer"/> —
///         <c>pop_effect.tiles_to_partial_layer : dict[layer, list[CellCoord]]</c>
///         (A4.7 REQUIRED field).</item>
///   <item><see cref="HalfgateMission.SuccessEffectRevealRule"/> —
///         <c>success_effect.reveal_rule : enum</c></item>
///   <item><see cref="HalfgateMission.RevealOnFailRule"/> —
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
    /// The 4×4 = 16 cells of the Halfgate footprint at the E1 layer,
    /// expressed in the E1 coordinate frame the projector consumes.
    /// Anchored at (0, 0) so the top-down projection lands on the
    /// E2 8×8 grid <see cref="AreaGridLogic.AllCells"/> = (0..7, 0..7).
    ///
    /// <para>
    /// <b>Coord anchor choice.</b> A4.7.6 worked example uses
    /// (10, 10)..(13, 13) as the canonical E1 Halfgate footprint
    /// position. For the MVE single-area we anchor at (0, 0) instead
    /// because the projector's formula is layer-local (R3 :
    /// <c>child(x, y) = (2x + dx, 2y + dy)</c>) and the E2 8×8 grid
    /// the renderer materialises uses (0..7, 0..7) too. Aligning the
    /// E1 footprint to (0..3, 0..3) makes the projection land on the
    /// rendered 8×8 cells directly — no offset translation needed at
    /// the boundary. When E1 enters production rendering and the
    /// Halfgate POI lives at its narratively-correct E1 cell, this
    /// list moves to that anchor and the projector outputs project
    /// against the corresponding E2 sub-block ; only the constants
    /// here and the corresponding test expectations need updating.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<GridCoord> HalfgateE1Footprint = new[]
    {
        new GridCoord(0, 0), new GridCoord(1, 0), new GridCoord(2, 0), new GridCoord(3, 0),
        new GridCoord(0, 1), new GridCoord(1, 1), new GridCoord(2, 1), new GridCoord(3, 1),
        new GridCoord(0, 2), new GridCoord(1, 2), new GridCoord(2, 2), new GridCoord(3, 2),
        new GridCoord(0, 3), new GridCoord(1, 3), new GridCoord(2, 3), new GridCoord(3, 3),
    };

    /// <summary>
    /// Single source of truth for the tutorial mission entry. Resolved
    /// once at boot by <c>E2AreaMap.OnEnter</c>.
    ///
    /// <para>
    /// <b>A4.7 cells.</b> The <c>tiles_to_partial_layer[E1]</c> list
    /// contains the 16 cells of the 4×4 E1 Halfgate footprint
    /// (<see cref="HalfgateE1Footprint"/>). At pop time the
    /// <see cref="TileRevealProjector"/> projects these into the 8×8
    /// E2 grid (64 cells) automatically — no E2 cells are listed here
    /// (forbidden per A4.7.8 anti-drift).
    /// </para>
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
        // A4.7 lock : tiles_to_partial_layer = {E1: <16 cells of the 4×4
        // Halfgate footprint>}. The projection cascade fills E2 (64
        // cells) automatically at write time. EXPLICIT E2 LISTING IS
        // FORBIDDEN per A4.7.8 anti-drift.
        PopEffectTilesToPartialLayer: new Dictionary<RevealLayer, IReadOnlyList<GridCoord>>
        {
            { RevealLayer.E1, HalfgateE1Footprint },
        },
        // A4.2 enum : on success, the recruited NPC's known_tiles are
        // applied to the partial set. Not exercised at E2.1 (no Recruit
        // button until E2.2), but the schema field is filled per the
        // Varn-locked spec.
        SuccessEffectRevealRule: RevealRule.ByRecruitedNpc,
        // A4.2 MVE default : reveal_on_fail = keep_partial for all
        // mission types (EC4). A4.7 EC10 extends this across layers :
        // the cross-layer-projected E2 cells also stay partial on fail.
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
    /// the rumor existed" — canonical MVE default per Varn EC4. A4.7
    /// EC10 extension : cross-layer-projected cells also stay partial.
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
/// A4.2 reveal grammar as amended by A4.7. Used by
/// <see cref="HalfgateMissionAuthoring"/> for the hardcoded tutorial
/// mission ; will be re-used as the DTO shape when E2.6 wires the ML
/// mission generator.
/// </summary>
/// <param name="MissionId">Stable mission identifier.</param>
/// <param name="DisplayName">Player-facing title (Pratchett-tone,
/// 1 line, ~30-50 char).</param>
/// <param name="NarrativeHook">Single-line rumor hook (Pratchett-tone,
/// ~60-140 char). Surfaced by the per-cell hover tooltip (EC6 path) for
/// any partial cell included in
/// <see cref="PopEffectTilesToPartialLayer"/> (after R3 projection).</param>
/// <param name="PopEffectPoiVisible">A4.2 :
/// <c>pop_effect.poi_visible</c>.</param>
/// <param name="PopEffectTilesToPartialLayer">A4.7 :
/// <c>pop_effect.tiles_to_partial_layer : dict[layer, list[CellCoord]]</c>.
/// REQUIRED under A4.7. Cells transition fog → partial when the mission
/// pops ; auto-projection cascades to other layers via
/// <see cref="TileRevealProjector"/>. Explicit cross-layer listing is
/// forbidden (Varn-locked-spec violation per A4.7.8).</param>
/// <param name="SuccessEffectRevealRule">A4.2 :
/// <c>success_effect.reveal_rule</c>.</param>
/// <param name="RevealOnFailRule">A4.2 : <c>reveal_on_fail.rule</c>.
/// MVE locked at <see cref="RevealOnFail.KeepPartial"/>.</param>
public sealed record HalfgateMission(
    string MissionId,
    string DisplayName,
    string NarrativeHook,
    bool PopEffectPoiVisible,
    IReadOnlyDictionary<RevealLayer, IReadOnlyList<GridCoord>> PopEffectTilesToPartialLayer,
    RevealRule SuccessEffectRevealRule,
    RevealOnFail RevealOnFailRule);
