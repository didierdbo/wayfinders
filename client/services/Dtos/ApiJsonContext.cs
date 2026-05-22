using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// <c>System.Text.Json</c> source-generation context for the FastAPI
/// boundary. Every type that crosses the wire must be listed here via
/// <see cref="JsonSerializableAttribute"/> so the source generator can
/// emit reflection-free <c>JsonTypeInfo</c> at compile time.
///
/// <para>
/// <b>Why source-gen, not reflection:</b> Steam shipping uses .NET AOT
/// (ahead-of-time) compilation. AOT strips unreferenced metadata, which
/// breaks reflection-based JSON deserialization at runtime — a class
/// of bug that does not surface in editor play, only in the published
/// build. Source-gen runs at <i>compile</i> time and emits ordinary
/// strongly-typed code that survives trimming and AOT cleanly. Wiring
/// it from L2 onward avoids a painful retrofit when the first Steam
/// export build inevitably exposes the gap.
/// </para>
///
/// <para>
/// <b>Naming policy:</b> snake_case_lower applied uniformly. FastAPI's
/// default field convention is snake_case; C# convention is PascalCase.
/// Setting the policy once on the context is cleaner than peppering
/// every DTO property with <c>[JsonPropertyName]</c>: one rule, no drift,
/// new fields auto-map correctly. If a single field ever needs a
/// non-conforming wire name, a per-property attribute still wins —
/// attributes override the policy.
/// </para>
///
/// <para>
/// <b>Case sensitivity:</b> <c>PropertyNameCaseInsensitive = false</c>
/// is the default but spelled out here so that any future drift in the
/// wire (e.g. someone manually crafting a payload with mixed case)
/// fails loudly at integration time rather than silently parsing.
/// </para>
///
/// <para>
/// <b>How to add a new DTO:</b> create the record, then add a
/// <c>[JsonSerializable(typeof(NewDto))]</c> line below for the
/// single-instance shape, plus
/// <c>[JsonSerializable(typeof(IList&lt;NewDto&gt;))]</c> if it ever
/// returns as a list. Do not forget — missing entries fail at runtime
/// with a <c>NotSupportedException</c>, not at compile time.
/// </para>
///
/// <para>
/// <b>4b additions.</b> Mission-emergence resolution wire types
/// (<see cref="MissionResolveRequestDto"/>,
/// <see cref="MissionResolveResponseDto"/>,
/// <see cref="PersonaLegacyTagDto"/>) and the now-typed
/// <see cref="CharacterStateDto"/> +
/// <see cref="EpisodicEventDto"/> +
/// <see cref="BondDto"/> mirror used inside
/// <see cref="WorldTickRequestDto.CompanyPersonas"/>. The dictionaries
/// inside <see cref="CharacterStateDto"/> use
/// <c>IReadOnlyDictionary&lt;string, T&gt;</c> for the contract but
/// the source-gen needs the closed concrete instantiations registered
/// here — STJ source-gen does not auto-recurse into property types
/// at compile time when those types are open generics.
/// </para>
///
/// <para>
/// <b>M2-advance step 2 additions (2026-05-10).</b> Mission conclude
/// wire types pivot the resolve flow to a server-authority weighted
/// roll. Mirrors Pydantic models in
/// <c>wayfinders/api/mission_conclude.py</c> (Tess commit d533373) :
/// <see cref="MissionConcludeRequestDto"/>,
/// <see cref="MissionConcludeResponseDto"/>,
/// <see cref="RollBreakdownEntryDto"/>,
/// <see cref="CharacterStateWireDto"/>. The thin wire DTO carries
/// only <c>(persona_id, dex_bucket, wis_bucket)</c> — see
/// <c>MissionConcludeDtos.cs</c> for the rationale.
/// </para>
///
/// <para>
/// <b>J5 additions (2026-05-22).</b> The world referential wire type
/// <see cref="WorldReferentialResponse"/> (GET /api/world). Only the
/// envelope is registered — STJ source-gen recurses into the concrete
/// nested record types (<see cref="WorldDto"/>, <see cref="WorldCityDto"/>,
/// <see cref="WorldDistrictDto"/>, <see cref="WorldPositionDto"/>) and the
/// <c>IReadOnlyList&lt;T&gt;</c> property instantiations automatically.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = false
)]
[JsonSerializable(typeof(UnitDto))]
[JsonSerializable(typeof(IList<UnitDto>))]
// M1 mission-emergence wire types (POST /api/world/tick). Mirrors
// Pydantic models in wayfinders/api/world_tick_models.py — see
// WorldTickDtos.cs for the records and the Varn-locked closed lookups.
[JsonSerializable(typeof(WorldTickRequestDto))]
[JsonSerializable(typeof(WorldTickResponseDto))]
[JsonSerializable(typeof(EmergentMissionDto))]
// Section A (Varn-lock 2026-05-17): GET /api/missions/active returns
// list[EmergentMission] — register the IList<EmergentMissionDto> shape
// for source-gen deserialisation in ApiClient.GetActiveMissionsAsync.
[JsonSerializable(typeof(IList<EmergentMissionDto>))]
// 4b — typed CharacterState mirror for company_personas. Pydantic
// schema source : wayfinders/ml/schemas/character.py (Varn 2026-04-30).
// Wire format doc : Owner's Inbox/2026-05-10-Wayfinders-CharacterState-wire-format.md.
[JsonSerializable(typeof(CharacterStateDto))]
[JsonSerializable(typeof(IList<CharacterStateDto>))]
[JsonSerializable(typeof(EpisodicEventDto))]
[JsonSerializable(typeof(BondDto))]
// 4b — mission resolution wire types (POST /api/world/mission/resolve).
// Mirrors Pydantic MissionResolveRequest / Response /
// PersonaLegacyTagWire in wayfinders/api/mission_resolve.py.
[JsonSerializable(typeof(MissionResolveRequestDto))]
[JsonSerializable(typeof(MissionResolveResponseDto))]
[JsonSerializable(typeof(PersonaLegacyTagDto))]
// M2-advance step 2 — mission conclude wire types
// (POST /api/world/mission/conclude). Mirrors Pydantic
// MissionConcludeRequest / Response / RollBreakdownEntry /
// CharacterStateWireDto in wayfinders/api/mission_conclude.py
// (Tess commit d533373, 2026-05-10).
[JsonSerializable(typeof(MissionConcludeRequestDto))]
[JsonSerializable(typeof(MissionConcludeResponseDto))]
[JsonSerializable(typeof(RollBreakdownEntryDto))]
[JsonSerializable(typeof(CharacterStateWireDto))]
[JsonSerializable(typeof(IList<CharacterStateWireDto>))]
[JsonSerializable(typeof(IList<RollBreakdownEntryDto>))]
// §A.8.D5 mission action endpoints (Varn-lock 2026-05-17 + Tess
// commit 5131d5f). POST /api/missions/<id>/{accept,decline,conclude,resolve}
// — minimal request bodies + status-shaped responses. See
// MissionActionDtos.cs for the records and the closed-lookup
// constants.
[JsonSerializable(typeof(MissionActionEmptyRequestDto))]
[JsonSerializable(typeof(MissionAcceptResponseDto))]
[JsonSerializable(typeof(MissionDeclineResponseDto))]
[JsonSerializable(typeof(MissionConcludeActionRequestDto))]
[JsonSerializable(typeof(MissionConcludeActionResponseDto))]
[JsonSerializable(typeof(MissionResolveActionRequestDto))]
[JsonSerializable(typeof(MissionResolveActionResponseDto))]
// J5 — world referential wire type (GET /api/world). Mirrors Pydantic
// WorldReferentialResponse / WorldDto / CityDto / DistrictDto /
// PositionDto in wayfinders/api/world_referential_models.py (Tess
// commit 55487d7). Only the envelope is registered : STJ source-gen
// recurses into the nested concrete record types and the
// IReadOnlyList<T> property instantiations automatically.
[JsonSerializable(typeof(WorldReferentialResponse))]
internal sealed partial class ApiJsonContext : JsonSerializerContext
{
    // Body intentionally empty — the source generator emits the
    // implementation at compile time. Concretely, an
    // `ApiJsonContext.UnitDto` and `ApiJsonContext.IListUnitDto`
    // property appear via a generated partial-class file. Those are what
    // `GetFromJsonAsync` consumes via `ApiJsonContext.Default.IListUnitDto`.
}
