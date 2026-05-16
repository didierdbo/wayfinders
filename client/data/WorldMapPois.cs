using Godot;

namespace Wayfinders.Client.Data;

/// <summary>
/// Top-level Resource holding the list of <see cref="PoiDefinition"/>
/// entries for the E2 World Map. Authored as
/// <c>res://data/world_map_pois.tres</c>, loaded once at
/// <see cref="Wayfinders.Client.Scenes.Screens.E1WorldMap"/>'s
/// <c>_Ready</c>.
///
/// <para>
/// <b>Why <see cref="Godot.Collections.Array{T}"/> not
/// <c>System.Collections.Generic.List&lt;T&gt;</c>.</b>
/// Backend-brain trap (Rune coaching brief §14): only the Godot
/// collection types marshal across the C++/C# boundary in a way the
/// editor inspector understands. A <c>List&lt;PoiDefinition&gt;</c>
/// would compile, but the <c>.tres</c> editor would refuse to expose
/// the array for visual authoring. We pay the marshalling tax once at
/// load time (<see cref="Wayfinders.Client.Scenes.Screens.E1WorldMap"/>
/// projects each entry to a pure-C# DTO before dispatch logic runs).
/// </para>
///
/// <para>
/// <b>J3 contents.</b> 1 Halfgate (clickable) + 3 grisée cities
/// (Veylant, Caer-Mithren, Pelt — placeholder names per pre-brief
/// D-J3-09, swappable via hot-edit if Varn objects). Mira's actual
/// map artwork will land later ; Didier will adjust the
/// <see cref="PoiDefinition.Position"/> values then.
/// </para>
/// </summary>
[GlobalClass]
public partial class WorldMapPois : Resource
{
    /// <summary>
    /// Ordered list of POIs to spawn on the World Map. Order does not
    /// matter for dispatch but matters for z-order when two markers
    /// overlap (later entries draw on top).
    /// </summary>
    [Export] public Godot.Collections.Array<PoiDefinition> Pois { get; set; } = new();
}
