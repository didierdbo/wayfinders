using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Varn-locked hardcoded authoring for the Halfgate E2 mission set —
/// missions that target a SINGLE district at the E2 layer (poi_layer=e2),
/// rather than the city-as-a-whole tuto (which lives in
/// <see cref="HalfgateMissionAuthoring"/> and targets the E1 4×4
/// footprint).
///
/// <para>
/// <b>E2.1c scope (2026-05-16, Didier lock).</b> The E2.1c jalon brief is
/// strict : "afficher des POI markers au centre des quartiers pour
/// lesquels une mission est active". At E2.1c we mock 2-3 missions
/// hardcoded against a Varn-locked minimal A4/A4.7-shaped schema —
/// the render layer reads <see cref="All"/>, resolves each mission's
/// <see cref="HalfgateE2Mission.TargetDistrict"/> to a centroid via
/// <see cref="AreaGridLogic.DistrictCentroid"/>, and spawns one
/// terracotta POI marker per active mission. No hover, no click, no
/// tooltip — lecture only.
/// </para>
///
/// <para>
/// <b>Schema (minimal A4/A4.7 shape).</b> The brief lists 4 fields :
/// <list type="bullet">
///   <item><c>poi_layer = e2</c> — the layer at which the mission's POI
///         renders. Locked to <see cref="RevealLayer.E2"/> for every
///         entry in this authoring (the file is the E2 mission set).</item>
///   <item><c>poi_id</c> — stable string identifying the targeted POI.
///         Convention <c>halfgate.&lt;district_suffix&gt;</c> matching
///         <see cref="DistrictTypeHelpers.AssetKeySuffix"/>.</item>
///   <item><c>pop_effect.tiles_to_partial_layer</c> — at E2.1c we leave
///         this EMPTY. The full E2 8×8 grid is already at Partial via the
///         E1 tuto mission's R3 top-down projection ; re-applying a
///         per-district partial flip would be redundant and breaks the
///         "one mission owns the reveal" invariant we will need at E2.2
///         when recruit-merge starts adding cells under specific
///         missions.</item>
///   <item><c>success_effect.reveal_rule = none</c> — at E2.1c there is
///         no success flow yet (recruit lands at E2.2). The schema field
///         is set to <see cref="MissionRevealRule.None"/> so the slot
///         is filled per the Varn-locked closed-lookup discipline ;
///         re-routing to <see cref="MissionRevealRule.ByRecruitedNpc"/>
///         is a single-line edit when E2.2 wires the recruit panel.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a separate authoring file from <see cref="HalfgateMissionAuthoring"/>.</b>
/// The E1 tuto mission targets a 4×4 cell footprint via
/// <c>tiles_to_partial_layer[E1]</c> and is fundamentally a "city-as-a-
/// whole" boot pop. The E2 missions target a SINGLE district at the E2
/// layer via a <c>poi_id</c> + <see cref="HalfgateE2Mission.TargetDistrict"/>
/// pair. Sharing a record type would force every entry to fill fields
/// they do not own (HalfgateMission has no TargetDistrict ;
/// HalfgateE2Mission has no tiles_to_partial_layer[E1] cell list). Two
/// concrete records, two files. When E2.6 lands the ML mission
/// generator a single typed DTO shape covering both will emerge from
/// the API ; for the MVP single-area concrete-first wins.
/// </para>
///
/// <para>
/// <b>Why a static class and not a Godot Resource.</b> Same rationale as
/// <see cref="HalfgateMissionAuthoring"/> + <see cref="NpcCatalog"/> +
/// <see cref="AreaGridLogic"/> : the MVP has a fixed handful of mock
/// missions. Promoting to <c>.tres</c> Resources invites typos in the
/// district enum value and adds an editor-roundtrip cost for every
/// authoring tweak. When the ML generator emits these the DTO shape
/// takes over.
/// </para>
///
/// <para>
/// <b>Three missions covering three districts.</b> The mock set anchors
/// one mission per visually-distinct district zone in the 8×8 grid :
/// Intramuros (centre), Gateway (cardinal piercings N+S), Littoral
/// (E+W coastal edges). Wall is intentionally skipped — at E2.1c the
/// player should read "3 markers in different visual zones" as the
/// observable signal, not "every district has a marker". The mock set
/// stays at 3 entries until E2.2 when missions become emergent.
/// </para>
/// </summary>
public static class HalfgateE2MissionAuthoring
{
    /// <summary>
    /// Mission id for the Intramuros-targeting tuto mission. Stable
    /// string ; matches any future Resource / DTO surface that consumes
    /// the same authoring shape.
    /// </summary>
    public const string IntramurosMissionId = "mission_tuto_intramuros_e2";

    /// <summary>
    /// Mission id for the Gateway-targeting tuto mission.
    /// </summary>
    public const string GatewayMissionId = "mission_tuto_gateway_e2";

    /// <summary>
    /// Mission id for the Littoral-targeting tuto mission. Optional 3rd
    /// entry per the brief — included because it exercises a third
    /// visual zone (cool grey-blue) and surfaces the centroid resolver's
    /// behaviour on a district that lives in two disjoint coastal strips
    /// (E and W edges).
    /// </summary>
    public const string LittoralMissionId = "mission_tuto_littoral_e2";

    /// <summary>
    /// Shared empty dictionary used by every E2.1c mock mission for the
    /// <c>pop_effect.tiles_to_partial_layer</c> slot. A new
    /// <c>Dictionary&lt;,&gt;</c> per mission would allocate 3 empty
    /// dicts on first access — pointless. Locked
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/> exposes the shape
    /// without allowing mutation through the slot.
    ///
    /// <para>
    /// <b>Init order discipline.</b> Declared BEFORE the three Tutorial
    /// property initialisers so the C# static init walk (source order)
    /// fills this slot first. A trailing declaration would surface as a
    /// CS8604 nullable warning on every Tutorial entry (the compiler
    /// sees the field as default-null at the moment the Tutorial
    /// property runs its initialiser).
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<RevealLayer, IReadOnlyList<GridCoord>> EmptyTilesByLayer
        = new Dictionary<RevealLayer, IReadOnlyList<GridCoord>>();

    /// <summary>
    /// Intramuros-targeting tuto mission. The POI marker lands at the
    /// centroid of the 12 Intramuros cells (the inner 4-cell core plus
    /// the 8-cell ring around it).
    /// </summary>
    public static HalfgateE2Mission TutorialIntramuros { get; } = new(
        MissionId: IntramurosMissionId,
        DisplayName: "Intramuros : a name for a footing",
        // Pratchett-tone : the Cadastre frets, in writing, in triplicate.
        // 60-140 char budget, ends with a period, no parodic register.
        NarrativeHook: "The Cadastre suspects a name. The footings, this time, are notarised.",
        PoiLayer: RevealLayer.E2,
        // Convention : halfgate.<district_suffix>. The suffix matches
        // DistrictTypeHelpers.AssetKeySuffix so the marker's textual
        // identity stays consistent with any future per-district asset
        // lookup.
        PoiId: "halfgate.intramuros",
        TargetDistrict: DistrictType.Intramuros,
        // E2.1c lock : tiles_to_partial_layer is intentionally empty. The
        // E2 8×8 cells are already at Partial via the E1 tuto's R3
        // projection. Re-applying a per-district partial flip here would
        // re-write cells that another mission owns, breaking the
        // "one mission owns one reveal" invariant E2.2 will need.
        PopEffectTilesToPartialLayer: EmptyTilesByLayer,
        // E2.1c lock : no success flow yet (recruit lands at E2.2). The
        // schema slot is filled with the None value so the closed-lookup
        // discipline is preserved.
        SuccessEffectRevealRule: MissionRevealRule.None);

    /// <summary>
    /// Gateway-targeting tuto mission. Gateway cells live in two disjoint
    /// 2-cell strips (north {(3,0),(4,0)} and south {(3,7),(4,7)}) plus
    /// two single-cell side gates ({(1,3),(1,4)}, {(6,3),(6,4)}). The
    /// centroid resolver picks the centre of the closest-to-grid-centre
    /// zone for visual clarity (see <see cref="AreaGridLogic.DistrictCentroid"/>).
    /// </summary>
    public static HalfgateE2Mission TutorialGateway { get; } = new(
        MissionId: GatewayMissionId,
        DisplayName: "Gateway : the customs ledger",
        NarrativeHook: "Someone has been walking through, deliberately, without paying the toll.",
        PoiLayer: RevealLayer.E2,
        PoiId: "halfgate.gateway",
        TargetDistrict: DistrictType.Gateway,
        PopEffectTilesToPartialLayer: EmptyTilesByLayer,
        SuccessEffectRevealRule: MissionRevealRule.None);

    /// <summary>
    /// Littoral-targeting tuto mission. Littoral cells live in two
    /// disjoint coastal strips (west col=0 rows 2..5 and east col=7
    /// rows 2..5). The centroid resolver picks one zone.
    /// </summary>
    public static HalfgateE2Mission TutorialLittoral { get; } = new(
        MissionId: LittoralMissionId,
        DisplayName: "Littoral : the brackish ledger",
        NarrativeHook: "A boat. An hour. A name. The Capitainerie regrets it has no record.",
        PoiLayer: RevealLayer.E2,
        PoiId: "halfgate.littoral",
        TargetDistrict: DistrictType.Littoral,
        PopEffectTilesToPartialLayer: EmptyTilesByLayer,
        SuccessEffectRevealRule: MissionRevealRule.None);

    /// <summary>
    /// All E2-layer mock missions for the Halfgate area, in their
    /// canonical render order (Intramuros centre → Gateway N+S →
    /// Littoral E+W). Consumer is
    /// <see cref="Wayfinders.Client.Scenes.Screens.E2AreaMap"/> which
    /// iterates this list at E2.1c+ and spawns one POI marker per entry.
    ///
    /// <para>
    /// <b>Order matters for Y-sort.</b> The list order is preserved when
    /// markers are added to the AreaGridLayer so the spawn iteration
    /// order is deterministic. Y-sort then arbitrates the final paint
    /// order from each marker's iso-screen Position.Y. A typical run
    /// renders Intramuros marker on top of Gateway (which sits N+S of
    /// it) — exactly the "centre reads as focused" pattern.
    /// </para>
    /// </summary>
    public static IReadOnlyList<HalfgateE2Mission> All { get; } = new[]
    {
        TutorialIntramuros,
        TutorialGateway,
        TutorialLittoral,
    };
}

/// <summary>
/// Varn-locked closed lookup of reveal-rule values on mission success
/// for the E2-layer mission set (A4.2-shaped, minimal subset at E2.1c).
///
/// <para>
/// <b>Why a new enum and not a reuse of <see cref="RevealRule"/>.</b>
/// <see cref="RevealRule"/> models the E1 tuto mission's success path
/// (ByRecruitedNpc / PoiFootprintToRevealed / CustomCellSet). The E2.1c
/// brief explicitly locks success_effect.reveal_rule = none for the
/// mock E2 missions ; the existing enum has no None value, and adding
/// one to <see cref="RevealRule"/> would change a Varn-locked surface
/// HalfgateMission.SuccessEffectRevealRule already pins. Two enums,
/// two locks. When E2.2 unifies the schema under a single DTO
/// (post-ML-generator), the merge is intentional and Varn-reviewed.
/// </para>
/// </summary>
public enum MissionRevealRule
{
    /// <summary>
    /// No success effect. The mission is decorative / lecture-only at
    /// E2.1c. Re-routing to <see cref="ByRecruitedNpc"/> is the E2.2
    /// follow-up when the recruit panel lands.
    /// </summary>
    None = 0,

    /// <summary>
    /// On success, the recruited NPC's known_tiles transition to Partial.
    /// Schema-compatible with <see cref="RevealRule.ByRecruitedNpc"/> ;
    /// E2.2-deferred.
    /// </summary>
    ByRecruitedNpc = 1,
}

/// <summary>
/// Pure-data record carrying one E2-layer mission entry (minimal A4/A4.7
/// shape per the E2.1c brief). Mirrors <see cref="HalfgateMission"/> but
/// targets a single district (via <see cref="TargetDistrict"/>) and
/// renders a POI marker at the district centroid rather than at a 4×4
/// cell footprint.
/// </summary>
/// <param name="MissionId">Stable mission identifier
/// (<c>mission_tuto_&lt;district&gt;_e2</c>).</param>
/// <param name="DisplayName">Player-facing title (Pratchett-tone,
/// 1 line, ~30-50 char). Not surfaced at E2.1c (no UI yet) ; reserved
/// for the E2.2 mission tooltip pass.</param>
/// <param name="NarrativeHook">Single-line rumor hook
/// (Pratchett-tone, ~40-100 char). Reserved for E2.1c+ tooltip
/// surfacing.</param>
/// <param name="PoiLayer">A4.7 : the layer at which the POI renders.
/// Locked to <see cref="RevealLayer.E2"/> for every entry of this
/// authoring file.</param>
/// <param name="PoiId">A4.7 : stable string identifying the POI.</param>
/// <param name="TargetDistrict">Closed-lookup district the mission's
/// POI marker is centred on. Consumed by
/// <see cref="AreaGridLogic.DistrictCentroid"/> at render time.</param>
/// <param name="PopEffectTilesToPartialLayer">A4.7 :
/// <c>pop_effect.tiles_to_partial_layer</c>. Empty at E2.1c per the
/// brief (the E2 cells are already at Partial via the E1 tuto's R3
/// projection).</param>
/// <param name="SuccessEffectRevealRule">A4.2 :
/// <c>success_effect.reveal_rule</c>. Locked at
/// <see cref="MissionRevealRule.None"/> for the E2.1c mock set.</param>
public sealed record HalfgateE2Mission(
    string MissionId,
    string DisplayName,
    string NarrativeHook,
    RevealLayer PoiLayer,
    string PoiId,
    DistrictType TargetDistrict,
    IReadOnlyDictionary<RevealLayer, IReadOnlyList<GridCoord>> PopEffectTilesToPartialLayer,
    MissionRevealRule SuccessEffectRevealRule);
