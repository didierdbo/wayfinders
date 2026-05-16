using Godot;

namespace Wayfinders.Client.Data;

/// <summary>
/// Top-level Resource holding the list of <see cref="PoiDefinition"/>
/// entries for the E5 District Map (Halfgate, quartier du marché).
/// Authored as <c>res://data/halfgate_marche_pois.tres</c>, loaded once
/// at <see cref="Wayfinders.Client.Scenes.Screens.E3DistrictMap"/>'s
/// <c>_Ready</c>.
///
/// <para>
/// <b>Why a per-quartier file (J5 D-J5-09 + Mira §7 E5.1 naming).</b>
/// Mira's filename convention is
/// <c>wf_e5_[cite]_[quartier]_base_3840x2160.png</c> ; mirroring it on
/// the POI side as <c>halfgate_[quartier]_pois.tres</c> means a future
/// quartier (port, halls...) just adds one file without touching the
/// E5 screen. Reuses the J4 <c>city_halfgate_pois.tres</c> shape
/// verbatim.
/// </para>
///
/// <para>
/// <b>Why <see cref="Godot.Collections.Array{T}"/> not
/// <c>System.Collections.Generic.List&lt;T&gt;</c>.</b>
/// Same backend-trap reason as <see cref="CityHalfgatePois"/> and
/// <see cref="WorldMapPois"/>: only the Godot collection types
/// marshal into the editor inspector. The marshalling tax is paid
/// once at load time when the screen projects each entry to a
/// pure-C# DTO before dispatch logic runs.
/// </para>
///
/// <para>
/// <b>J5 contents (D-J5-10 reuse Kira cross-scene + 1 AmbientNpc).</b>
/// <list type="bullet">
///   <item>1 AmbientNpc (<c>halfgate.marche.ambient_homme</c>) -&gt;
///         hover-only with the verbatim Varn E5.I tooltip ; click
///         flashes the generic blocked indicator (defense-in-depth
///         path, unchanged from J4 dispatcher).</item>
///   <item>1 NpcCandidate (<c>kira</c>) -&gt; opens the E4 modal
///         exactly as from E3 ; the npc id propagates through the
///         payload bag verbatim. This validates the modal cross-scene
///         seam.</item>
/// </list>
/// </para>
/// </summary>
[GlobalClass]
public partial class HalfgateMarchePois : Resource
{
    /// <summary>
    /// Ordered list of POIs to spawn on the E5 quartier map. Order
    /// matters for z-order on overlapping markers (later entries draw
    /// on top). J5: 2 entries.
    /// </summary>
    [Export] public Godot.Collections.Array<PoiDefinition> Pois { get; set; } = new();
}
