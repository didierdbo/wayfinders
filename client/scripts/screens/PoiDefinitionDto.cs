namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# mirror of <see cref="Wayfinders.Client.Data.PoiDefinition"/>
/// for the dispatch logic, stripped of Godot types so the helper sits
/// in a Godot-free assembly and is testable from xUnit without an
/// engine.
///
/// <para>
/// <b>Why a separate DTO record.</b> The Godot <c>PoiDefinition</c> is a
/// <see cref="Godot.Resource"/> sub-class with <c>Vector2</c> fields for
/// position/size. The dispatch decision (clickable -&gt; navigate ; not
/// clickable -&gt; show blocked indicator) does not depend on geometry.
/// Crossing the marshalling boundary on every call site to read string
/// fields would also be a wasteful pattern (Godot.Collections marshalling
/// trap, Rune coaching brief §13). The DTO is the engine-independent
/// projection that the dispatcher consumes.
/// </para>
/// </summary>
/// <param name="PoiId">Stable identifier, e.g. <c>halfgate</c>.</param>
/// <param name="IsClickable">
/// True when the POI navigates somewhere on click ; false when the POI
/// is grisée (Cadastre suspendu, Varn §6.D6.10 lock 1-cité MVP).
/// </param>
/// <param name="TargetScreenId">
/// SceneManager screen id to navigate to when the POI is clickable, or
/// the empty string when the POI is grisée.
/// </param>
/// <param name="TooltipKey">
/// <see cref="Wayfinders.Client.Data.OpeningStrings"/> key for the
/// tooltip text. May be empty when the POI uses a runtime-interpolated
/// label (e.g. grisée cities that inject <c>[Nom]</c>).
/// </param>
public readonly record struct PoiDefinitionDto(
    string PoiId,
    bool IsClickable,
    string TargetScreenId,
    string TooltipKey);
