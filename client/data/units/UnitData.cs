using Godot;

namespace Wayfinders.Client.Data.Units;

/// <summary>
/// In-memory domain shape for a tactical unit. A <see cref="Resource"/> subclass
/// so it can be carried in <c>[Export]</c> slots, passed by reference into the
/// component <c>Configure(UnitData)</c> APIs banked from Phase 2, and (later)
/// authored locally for tests, modding, or save-file synthesis.
///
/// <para>
/// <b>Wire shape vs domain shape (Phase 3 L5 lock).</b> <see cref="UnitData"/>
/// is NOT the wire shape. The wire shape is
/// <see cref="Wayfinders.Client.Services.Dtos.UnitDto"/> — a JSON-serialisable
/// record with snake_case mapping, value equality, and no engine dependency.
/// The two are separated on purpose: the components consume <c>UnitData</c>
/// (engine-blessed <c>Resource</c>, designer-friendly <c>[Export]</c>s); the
/// HTTP layer produces <c>UnitDto</c> (anonymous structural data over the
/// wire). Conversion is a single explicit step at the boundary — see
/// <see cref="Wayfinders.Client.Services.Dtos.UnitDtoMapper"/>.
/// </para>
///
/// <para>
/// This split is the anti-corruption layer pattern: the wire format does
/// not get to dictate the shape of the domain, and the domain does not
/// leak engine semantics back across the wire. If FastAPI renames a
/// field, only the DTO and the mapper change — the components, the
/// scene, and any future <c>Resource</c> consumers are untouched.
/// </para>
///
/// <para>
/// <b>No <c>.tres</c> files in this project.</b> Phase 2's
/// <c>scout.tres</c> / <c>warden.tres</c> / <c>archer.tres</c> have
/// retired with the sandbox. The server is the single source of truth
/// for unit data now; on-disk <c>.tres</c> would be a second source and
/// drift would follow. The class is kept for in-memory use only, instantiated
/// by the mapper.
/// </para>
/// </summary>
[GlobalClass]
public partial class UnitData : Resource
{
    [Export] public string DisplayName { get; set; } = "";
    [Export] public int MaxHp { get; set; } = 10;
    [Export] public int BaseSpeed { get; set; } = 4;
    [Export] public Texture2D Portrait { get; set; } = null!;
}
