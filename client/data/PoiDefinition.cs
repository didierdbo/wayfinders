using Godot;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Data;

/// <summary>
/// Data container describing a single POI on a map screen (E2 World,
/// E3 Cité, E5 Quartier). Authored in <c>.tres</c> resources, loaded by
/// the hosting screen at <c>_Ready</c>, and projected to a Godot-free
/// <see cref="PoiDefinitionDto"/> when handed to
/// <see cref="PoiDispatchLogic"/>.
///
/// <para>
/// <b>Why a Resource (J3 D-J3-01 implementation).</b> The hot-swap need
/// is real: when Mira ships the actual map artwork, the POI screen
/// positions must align pixel-precisely with sprites Rune cannot
/// foresee. The <c>.tres</c> is hot-editable in the Godot inspector so
/// Didier adjusts <see cref="Position"/> values without a C# rebuild.
/// Hard-coding positions in C# would force a recompile per pixel nudge —
/// unacceptable iteration cost for an asset that lives outside Rune's
/// hands.
/// </para>
///
/// <para>
/// <b>Why <c>[GlobalClass]</c>.</b> The attribute registers the type
/// with Godot's resource system, exposing it as a creatable
/// <c>Resource</c> sub-class in the editor's "New Resource" dropdown.
/// Without it, the inspector would not let Didier author a
/// <c>.tres</c> file of this type.
/// </para>
///
/// <para>
/// <b>J4 extension (D-J4-02).</b> Added <see cref="Kind"/> with default
/// <see cref="PoiKind.City"/> so the J3 <c>world_map_pois.tres</c>
/// keeps loading without edit (the unset field projects to the default
/// enum value via Godot's resource loader). The dispatcher now branches
/// by <see cref="Kind"/> — see <see cref="PoiDispatchLogic"/> for the
/// switch and <see cref="PoiKind"/> for the type taxonomy.
/// </para>
/// </summary>
[GlobalClass]
public partial class PoiDefinition : Resource
{
    /// <summary>
    /// Stable identifier, lower-snake-case (e.g. <c>halfgate</c>,
    /// <c>halfgate.marche</c>, <c>kira</c>). Used in console logs, as the
    /// dispatcher key, and for E4 modal context (NpcId for PNJ POIs).
    /// </summary>
    [Export] public string PoiId { get; set; } = "";

    /// <summary>
    /// Human-readable name for diagnostic logging and the runtime
    /// tooltip interpolation (Varn E2.G / E3.J templates inject
    /// <c>[Nom]</c> / <c>[nom]</c>).
    /// </summary>
    [Export] public string DisplayName { get; set; } = "";

    /// <summary>
    /// Pixel position of the POI's top-left corner relative to the
    /// <c>PoiContainer</c> Control (a full-rect sibling of the
    /// background <c>TextureRect</c>). Authored visually in the
    /// <c>.tres</c> inspector ; Didier adjusts after Mira lands the
    /// real map artwork.
    /// </summary>
    [Export] public Vector2 Position { get; set; }

    /// <summary>
    /// Hit-box size in pixels. 96x96 is the J3 default for the
    /// 512x512 marker sprite scaled down — adjustable per POI when
    /// Mira ships marker variants.
    /// </summary>
    [Export] public Vector2 Size { get; set; } = new(96, 96);

    /// <summary>
    /// True when the POI navigates somewhere on click. False for the
    /// MVP-grisée cities that respect Varn §6.D6.10 lock 1-cité.
    /// For <see cref="PoiKind.AmbientNpc"/> / <see cref="PoiKind.Interactable"/>
    /// the dispatcher forces the blocked path regardless of this flag
    /// (defense in depth).
    /// </summary>
    [Export] public bool IsClickable { get; set; } = false;

    /// <summary>
    /// SceneManager screen id (e.g. <c>E2_AREA</c>,
    /// <c>E3_DISTRICT</c>) to navigate to when <see cref="IsClickable"/>
    /// is true and <see cref="Kind"/> is City or District. Empty string
    /// for grisée POIs and for kinds that don't navigate
    /// (NpcCandidate / NpcCompanion open a modal instead).
    /// </summary>
    [Export] public string TargetScreenId { get; set; } = "";

    /// <summary>
    /// <see cref="OpeningStrings"/> key for the tooltip text. May be
    /// empty when the tooltip uses runtime <c>[Nom]</c> interpolation
    /// against a template key (then the screen resolves the template +
    /// substitutes the <see cref="DisplayName"/>).
    /// </summary>
    [Export] public string TooltipKey { get; set; } = "";

    /// <summary>
    /// Asset key (resolved by <c>AssetResolver</c>) for the marker
    /// texture. J3 keys: <c>e1.poi_marker.active</c> (Halfgate) and
    /// <c>e1.poi_marker.suspended</c> (grisée cities). J4 keys:
    /// <c>e3.poi.district</c>, <c>e3.poi.key_building.hq</c>,
    /// <c>e3.poi.npc_candidate</c>.
    /// </summary>
    [Export] public string AssetKey { get; set; } = "";

    /// <summary>
    /// POI taxonomy used by <see cref="PoiDispatchLogic"/>. Default
    /// <see cref="PoiKind.City"/> preserves backward compat with the
    /// J3 <c>world_map_pois.tres</c> (which never authored this field).
    /// J4 cités author District / KeyBuilding / NpcCandidate / NpcCompanion.
    /// </summary>
    [Export] public PoiKind Kind { get; set; } = PoiKind.City;

    /// <summary>
    /// Project to the Godot-free DTO consumed by
    /// <see cref="PoiDispatchLogic"/>. Centralised here so callers
    /// don't re-hand-write the field copy.
    /// </summary>
    public PoiDefinitionDto ToDto() =>
        new(PoiId: PoiId,
            IsClickable: IsClickable,
            TargetScreenId: TargetScreenId,
            TooltipKey: TooltipKey,
            Kind: Kind);
}
