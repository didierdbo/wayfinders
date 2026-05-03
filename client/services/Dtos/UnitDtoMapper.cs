using Godot;
using Wayfinders.Client.Data.Units;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Anti-corruption layer between the wire format (<see cref="UnitDto"/>) and
/// the in-memory domain shape (<see cref="UnitData"/>). One file, one method,
/// one job: convert a deserialized DTO into a <see cref="Resource"/> the
/// component <c>Configure</c> APIs already accept.
///
/// <para>
/// <b>Why this seam exists.</b> The components (<c>HealthComponent</c>,
/// <c>MovementComponent</c>, <c>StressComponent</c>) were authored in Phase 2
/// against <see cref="UnitData"/>. Pointing them at <see cref="UnitDto"/>
/// instead would couple every component to the wire format — rename a JSON
/// field on the FastAPI side and the components compile-break. The mapper
/// puts a translation step at the network edge so the domain stays clean:
/// </para>
///
/// <code>
///   FastAPI JSON  -- HttpClient/source-gen --&gt;  UnitDto (record)
///                                                   |
///                                       UnitDtoMapper.ToUnitData(dto)
///                                                   v
///                                              UnitData (Resource)
///                                                   |
///                                          unit.Configure(unitData)
///                                                   v
///                                              Unit + components
/// </code>
///
/// <para>
/// <b>Java reflex callout.</b> A senior backend engineer's instinct here is
/// to reach for AutoMapper, MapStruct, or BeanUtils.copyProperties. Resist.
/// The wire format and the domain shape will drift independently as the
/// game grows; an explicit hand-written mapper makes that drift visible
/// every time and keeps the boundary auditable. When the mapping outgrows
/// a single file, you split the file - you do not introduce a framework.
/// </para>
///
/// <para>
/// <b>Portrait handling.</b> The DTO carries a <c>portrait</c> string
/// (a <c>res://</c>-relative path or a placeholder). Resolution to a
/// <see cref="Texture2D"/> via <c>ResourceLoader</c> is deferred until a
/// real portrait asset pipeline exists — Mira and Varn own that work. For
/// L5, the mapper records the path on a <see cref="UnitData"/> field and
/// the roster scene displays the unit name + stats only, no image.
/// </para>
/// </summary>
public static class UnitDtoMapper
{
    /// <summary>
    /// Convert a wire-format <see cref="UnitDto"/> into an in-memory
    /// <see cref="UnitData"/> the component <c>Configure</c> path accepts.
    /// Pure function, no I/O, no engine state mutation.
    /// </summary>
    public static UnitData ToUnitData(UnitDto dto)
    {
        // Portrait is left null for now: until a real portrait asset
        // pipeline exists, ResourceLoader.Load<Texture2D>(dto.Portrait)
        // would either fail or return a placeholder we have not authored.
        // The roster scene renders text-only labels for L5; portrait
        // resolution lights up when art lands.
        return new UnitData
        {
            DisplayName = dto.DisplayName,
            MaxHp = dto.MaxHp,
            BaseSpeed = dto.BaseSpeed,
            Portrait = null!,
        };
    }
}
