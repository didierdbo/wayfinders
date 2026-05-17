using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Wire-format DTOs for the <c>POST /api/world/tick</c> endpoint
/// (M1 mission-emergence boundary, server schema in
/// <c>wayfinders/api/world_tick_models.py</c>).
///
/// <para>
/// <b>What this file is.</b> Mirror of the Pydantic
/// models on the FastAPI side. Three records :
/// <see cref="WorldTickRequestDto"/> (what the client posts),
/// <see cref="WorldTickResponseDto"/> (what the server returns),
/// <see cref="EmergentMissionDto"/> (the optional mission carried inside
/// the response). The closed-lookup string sets are pinned as
/// <c>const string</c> here so a typo cannot drift past compile time.
/// </para>
///
/// <para>
/// <b>4b update — typed company personas.</b> The 4a stub posted
/// <c>company_personas: []</c> with the C# type
/// <c>IReadOnlyList&lt;object&gt;</c> as a placeholder. After Didier
/// ratified the full-mirror decision (memory
/// <c>project_wayfinders_mission_emergence.md</c>, 2026-05-10 post-4a),
/// this is now <see cref="IReadOnlyList{T}"/> of
/// <see cref="CharacterStateDto"/> — see <c>CharacterStateDto.cs</c>
/// for the field-by-field rationale and the
/// <c>2026-05-10-Wayfinders-CharacterState-wire-format.md</c> doc by
/// Tess for the canonical wire format.
/// </para>
///
/// <para>
/// <b>Closed lookups (Varn-locked 2026-05-10).</b> Mission types,
/// difficulty buckets, and resolution outcomes are pinned as
/// <c>const string</c> below to give callers a single source of truth
/// without dragging an enum across the wire (the wire is a string ; the
/// server's Pydantic <c>Literal</c> validates it, and an enum on this
/// side would force a custom <c>JsonStringEnumConverter</c> per type
/// for the snake-case-with-hyphens <c>very-low</c>/<c>very-high</c>
/// bucket spellings). Strings + constants is the simpler path.
/// </para>
///
/// <para>
/// <b>Naming policy.</b> Wire is snake_case ; C# is PascalCase ; the
/// uniform <c>SnakeCaseLower</c> policy on
/// <see cref="ApiJsonContext"/> handles the mapping. Note one subtle
/// corner : the difficulty bucket strings <c>very-low</c> and
/// <c>very-high</c> use a hyphen, not an underscore — that is Varn's
/// stat-lane vocabulary lock from <c>vocabularies.py:25</c>, not the
/// JSON naming policy. The hyphen is part of the <i>value</i>, not the
/// field name, so the policy does not touch it.
/// </para>
/// </summary>
public static class WorldTickWireFormat
{
    /// <summary>Mission type closed lookup. M1 strict mode (Varn §A.8.D3
    /// 2026-05-17) restricts emission to <see cref="Recruit"/> only ;
    /// <see cref="ScoutRoute"/> and <see cref="ParleyLocal"/> stay on
    /// the wire schema for M2 restore but are not emitted in M1.
    /// Server-side <c>MissionType = Literal["recruit", "scout_route",
    /// "parley_local"]</c>.</summary>
    public static class MissionType
    {
        /// <summary>M1 strict mode (Varn §A.8.D3) — only type emitted in
        /// M1. Mission anchored to one Halfgate district per recruit
        /// roster entry (kira/gateway, dorn/intramuros, vell/littoral).
        /// Each recruit mission carries a non-null
        /// <see cref="EmergentMissionDto.RecruitTargetNpcId"/>.</summary>
        public const string Recruit = "recruit";
        public const string ScoutRoute = "scout_route";
        public const string ParleyLocal = "parley_local";
    }

    /// <summary>Difficulty bucket closed lookup (Varn-locked 2026-05-10,
    /// reuses the stat-lane vocabulary from
    /// <c>vocabularies.py:25</c>). M1 forces <c>Mid</c>. Note the hyphens
    /// in <c>VeryLow</c>/<c>VeryHigh</c> are part of the wire value, not
    /// a naming-policy artefact.</summary>
    public static class DifficultyBucket
    {
        public const string VeryLow = "very-low";
        public const string Low = "low";
        public const string Mid = "mid";
        public const string High = "high";
        public const string VeryHigh = "very-high";
    }

    /// <summary>Resolution outcome closed lookup (Varn-locked
    /// 2026-05-10). Server-side <c>ResolutionOutcomeType =
    /// Literal["success", "partial", "failure"]</c>. Set on
    /// <see cref="EmergentMissionDto.Outcome"/> at resolution time, not
    /// at emergence.</summary>
    public static class ResolutionOutcome
    {
        public const string Success = "success";
        public const string Partial = "partial";
        public const string Failure = "failure";
    }
}

/// <summary>
/// Input for <c>POST /api/world/tick</c>. Mirrors Pydantic
/// <c>WorldTickRequest</c> in <c>wayfinders/api/world_tick_models.py</c>.
///
/// <para>
/// <b>Field semantics.</b>
/// <list type="bullet">
///   <item><see cref="Tick"/> is the current tick index ; non-negative,
///         monotonically increasing across a session. Server uses it
///         for cadence gating and echoes it back on
///         <see cref="WorldTickResponseDto.Tick"/> so the client can
///         correlate async responses (relevant once the world tick is
///         no longer in lockstep with the request).</item>
///   <item><see cref="Seed"/> is caller-owned : derive from
///         <c>(world_seed, tick)</c> so emergence is reproducible. The
///         server holds no global RNG.</item>
///   <item><see cref="ContextProse"/> is the EN-rendered world prose
///         from the renderer — Varn-locked schema, identical slot to
///         UC1 resolution. Must be non-empty.</item>
///   <item><see cref="CompanyPersonas"/> are the typed
///         <see cref="CharacterStateDto"/> snapshots — see
///         <c>CharacterStateDto.cs</c>. Empty list when the company is
///         empty (Varn option (c) — mission still emerges, with empty
///         <see cref="EmergentMissionDto.EligiblePersonas"/>).</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Tick">Current tick index (non-negative).</param>
/// <param name="Seed">RNG seed for deterministic sampling.</param>
/// <param name="ContextProse">EN-rendered world context, non-empty.</param>
/// <param name="CompanyPersonas">
/// Typed <see cref="CharacterStateDto"/> snapshots of the company.
/// Source-gen JSON serialises each entry with the snake_case naming
/// policy (so PascalCase C# property names map to snake_case wire
/// fields). Empty list when the company is empty.
/// </param>
public sealed record WorldTickRequestDto(
    int Tick,
    long Seed,
    string ContextProse,
    IReadOnlyList<CharacterStateDto> CompanyPersonas
);

/// <summary>
/// A mission that emerged at the current tick. Mirrors Pydantic
/// <c>EmergentMission</c>. Immutable wire shape.
///
/// <para>
/// <b>M1 invariants pinned by the server schema.</b>
/// <list type="bullet">
///   <item><see cref="Difficulty"/> is always
///         <see cref="WorldTickWireFormat.DifficultyBucket.Mid"/> in M1
///         (sampler disabled).</item>
///   <item><see cref="DeadlineTicks"/> is always <c>null</c> in M1
///         (no timer).</item>
///   <item><see cref="Outcome"/> is always <c>null</c> at emergence ;
///         the resolution endpoint sets it later.</item>
///   <item><see cref="EligiblePersonas"/> is computed server-side by
///         <c>EmergenceEngine.filter_eligible_personas()</c> using the
///         ±1-bucket window rule (Varn-lock 2026-05-10). Empty when
///         the company is empty or no persona is in range — mission
///         still emerges (option (c)).</item>
///   <item><b>Varn §A.8.D3 strict M1 (2026-05-17)</b> — emitted
///         <see cref="Type"/> is exclusively
///         <see cref="WorldTickWireFormat.MissionType.Recruit"/> and
///         <see cref="RecruitTargetNpcId"/> is non-null. For M2 types
///         it may be null.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Mission↔POI hierarchy (Varn-lock 2026-05-15 v2 §1).</b> The
/// authoritative POI binding is <see cref="TargetPoi"/> — a canonical
/// hierarchical id of the form <c>e{layer}.{ancestor}.{...}.{local_id}</c>
/// (regex <c>^e[1-3]\.[a-z0-9\-]+(\.[a-z0-9\-]+){0,2}$</c>). M1 caps
/// emergence at layer e1, so M1 missions carry e.g.
/// <c>"e1.halfgate"</c>. M2+ may carry deeper ids
/// (<c>e2.halfgate.docks</c>, <c>e3.halfgate.docks.taverne-du-cormoran</c>).
/// <see cref="Region"/> is kept on the wire for backward compat (and
/// used by the M1 narrative_hook template
/// <c>f"Une affaire de {type} dans {region}."</c>) but is NOT
/// authoritative for the tooltip-aggregation rule — the aggregator
/// filters by prefix match on <see cref="TargetPoi"/> against the
/// hovered POI's <c>PoiData.PoiId</c>, walking the tree via
/// <see cref="Wayfinders.Client.Services.PoiTreeService"/>. See
/// Tess's backend commits 6d2d2e4 (DTO migration) and 6c57df5
/// (<c>/api/world/poi_tree</c> endpoint).
/// </para>
/// </summary>
/// <param name="Id">Stable UUID4 generated at emergence.</param>
/// <param name="Type">
/// Closed lookup, see <see cref="WorldTickWireFormat.MissionType"/>.
/// </param>
/// <param name="NarrativeHook">1-2 line server-rendered hook.</param>
/// <param name="EligiblePersonas">
/// Persona ids eligible for assignment (deterministic asc order).
/// </param>
/// <param name="Difficulty">
/// Closed lookup, see
/// <see cref="WorldTickWireFormat.DifficultyBucket"/>.
/// </param>
/// <param name="Region">Free-form region id (M1, backward-compat
/// retained ; the authoritative POI binding is
/// <see cref="TargetPoi"/>).</param>
/// <param name="DeadlineTicks">
/// Ticks until expiry. <c>null</c> in M1.
/// </param>
/// <param name="Outcome">
/// Closed lookup, see
/// <see cref="WorldTickWireFormat.ResolutionOutcome"/>. <c>null</c> at
/// emergence.
/// </param>
/// <param name="Seed">Spawn seed audit trail.</param>
/// <param name="TargetPoi">Canonical hierarchical POI id (Varn-lock
/// 2026-05-15 v2 §1, regex
/// <c>^e[1-3]\.[a-z0-9\-]+(\.[a-z0-9\-]+){0,2}$</c>). Defaults to
/// empty string so legacy call sites that haven't been updated still
/// compile ; the live server (Tess commit 6d2d2e4) always returns a
/// non-empty value.</param>
/// <param name="RecruitTargetNpcId">M1 strict mode (Varn §A.8.D3,
/// 2026-05-17) — when <see cref="Type"/> equals
/// <see cref="WorldTickWireFormat.MissionType.Recruit"/> this is the
/// NpcId (kira / dorn / vell) the player can recruit by resolving the
/// mission. Null for any non-recruit type (M2+ : the schema retains
/// the slot ; for M1 the field is always non-null because recruit is
/// the only emitted type). Source-gen mapping : C# PascalCase →
/// snake_case <c>recruit_target_npc_id</c> via the uniform
/// <see cref="ApiJsonContext"/> naming policy.</param>
public sealed record EmergentMissionDto(
    string Id,
    string Type,
    string NarrativeHook,
    IReadOnlyList<string> EligiblePersonas,
    string Difficulty,
    string Region,
    int? DeadlineTicks,
    string? Outcome,
    long Seed,
    [property: JsonPropertyName("target_poi")] string TargetPoi = "",
    string? RecruitTargetNpcId = null
);

/// <summary>
/// Response from <c>POST /api/world/tick</c>. Mirrors Pydantic
/// <c>WorldTickResponse</c>.
///
/// <para>
/// <see cref="Mission"/> is <c>null</c> when no mission emerges this
/// tick (cadence-gated). With the M1 sampler config (no_emergence
/// disabled, cadence forced to every 5-10 ticks) the server returns
/// <c>null</c> only between cadence windows ; once inside the window
/// a mission is guaranteed.
/// </para>
/// </summary>
/// <param name="Mission">The emerged mission, or <c>null</c>.</param>
/// <param name="Tick">Echo of the request tick.</param>
public sealed record WorldTickResponseDto(
    EmergentMissionDto? Mission,
    int Tick
);
