using Godot;

namespace Wayfinders.Client.Data;

/// <summary>
/// Top-level Resource holding the list of <see cref="PoiDefinition"/>
/// entries for the E3 City Halfgate scene. Authored as
/// <c>res://data/city_halfgate_pois.tres</c>, loaded once at
/// <see cref="Wayfinders.Client.Scenes.Screens.E3CityHalfgate"/>'s
/// <c>_Ready</c>.
///
/// <para>
/// <b>Why a per-cité file (J4 D-J4-02).</b> The MVP gate is 1 cité
/// (Halfgate). Putting all cities into a single <c>city_pois.tres</c>
/// would create either a giant inspector-unfriendly resource or a
/// faux-split via naming convention. Establishing the
/// <c>city_&lt;name&gt;_pois.tres</c> pattern now means
/// <c>city_veylant_pois.tres</c> at post-MVP follows naturally.
/// See Rune J4 pre-brief §11.4 for the design rationale.
/// </para>
///
/// <para>
/// <b>Why <see cref="Godot.Collections.Array{T}"/> not
/// <c>System.Collections.Generic.List&lt;T&gt;</c>.</b>
/// Backend-brain trap (Rune coaching brief §14): only the Godot
/// collection types marshal across the C++/C# boundary in a way the
/// editor inspector understands. Same constraint as
/// <see cref="WorldMapPois"/> ; the marshalling tax is paid once at
/// load time when the screen projects each entry to a pure-C# DTO
/// before dispatch logic runs.
/// </para>
///
/// <para>
/// <b>J4 contents (D-J4-08, D-J4-09, D-J4-10).</b>
/// <list type="bullet">
///   <item>2 District POIs (<c>halfgate.marche</c>,
///         <c>halfgate.port</c>) -&gt; navigate to E5_DISTRICT.</item>
///   <item>1 KeyBuilding POI (<c>halfgate.qg</c>) -&gt; flash blocked
///         indicator (J4 stub, real opening lands post-MVP).</item>
///   <item>2 NpcCandidate POIs -&gt; open E4 modal stub.</item>
/// </list>
/// Mira's actual Halfgate base artwork will land later ; Didier will
/// adjust the <see cref="PoiDefinition.Position"/> values then.
/// </para>
/// </summary>
[GlobalClass]
public partial class CityHalfgatePois : Resource
{
    /// <summary>
    /// Ordered list of POIs to spawn on the City Halfgate map. Order
    /// does not matter for dispatch but matters for z-order when two
    /// markers overlap (later entries draw on top).
    /// </summary>
    [Export] public Godot.Collections.Array<PoiDefinition> Pois { get; set; } = new();
}
