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
internal sealed partial class ApiJsonContext : JsonSerializerContext
{
    // Body intentionally empty — the source generator emits the
    // implementation at compile time. Concretely, an
    // `ApiJsonContext.UnitDto` and `ApiJsonContext.IListUnitDto`
    // property appear via a generated partial-class file. Those are what
    // `GetFromJsonAsync` consumes via `ApiJsonContext.Default.IListUnitDto`.
}
