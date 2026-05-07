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
/// clickable -&gt; show blocked indicator ; NPC -&gt; open modal) does
/// not depend on geometry. Crossing the marshalling boundary on every
/// call site to read string fields would also be a wasteful pattern
/// (Godot.Collections marshalling trap, Rune coaching brief §13). The
/// DTO is the engine-independent projection that the dispatcher consumes.
/// </para>
///
/// <para>
/// <b>J4 extension (D-J4-02).</b> Added <see cref="Kind"/> so the
/// dispatcher can branch by POI type (City / District / NpcCandidate /
/// NpcCompanion / KeyBuilding / AmbientNpc / Interactable) instead of
/// inferring intent from clickability + target. Backward compat with J3
/// is preserved: the J3 <c>world_map_pois.tres</c> entries default to
/// <see cref="PoiKind.City"/> via the <c>[Export]</c> default on the
/// Godot-side resource — no <c>.tres</c> edit required.
/// </para>
/// </summary>
/// <param name="PoiId">Stable identifier, e.g. <c>halfgate</c>.</param>
/// <param name="IsClickable">
/// True when the POI navigates somewhere on click ; false when the POI
/// is grisée (Cadastre suspendu, Varn §6.D6.10 lock 1-cité MVP) or
/// hover-only (KeyBuilding stub J4, AmbientNpc/Interactable J6+).
/// </param>
/// <param name="TargetScreenId">
/// SceneManager screen id to navigate to when the POI is clickable, or
/// the empty string when the POI is grisée or opens a modal instead.
/// </param>
/// <param name="TooltipKey">
/// <see cref="Wayfinders.Client.Data.OpeningStrings"/> key for the
/// tooltip text. May be empty when the POI uses a runtime-interpolated
/// label (e.g. grisée cities that inject <c>[Nom]</c>).
/// </param>
/// <param name="Kind">
/// POI type used by the dispatcher to decide between NavigateToScreen,
/// OpenModal and ShowBlockedIndicator outcomes. Defaults to
/// <see cref="PoiKind.City"/> for backward compat with the J3
/// <c>world_map_pois.tres</c>.
/// </param>
public readonly record struct PoiDefinitionDto(
    string PoiId,
    bool IsClickable,
    string TargetScreenId,
    string TooltipKey,
    PoiKind Kind = PoiKind.City);

/// <summary>
/// Type taxonomy for POIs across E2 (world) and E3/E5 (cité, quartier).
/// Kept as a flat enum rather than a class hierarchy because the
/// behaviour difference lives entirely in <see cref="PoiDispatchLogic"/>
/// (a switch over the enum) — the data shared by every POI is the same
/// (PoiId / Position / Size / IsClickable / TargetScreenId / TooltipKey
/// / AssetKey). See Rune J4 pre-brief §11.1 for the design rationale.
///
/// <para>
/// Order is stable because the values are persisted in <c>.tres</c>
/// resource files via Godot's <c>[Export]</c> attribute. Inserting a
/// new value in the middle would shift integer codes and break
/// authored data — append-only.
/// </para>
/// </summary>
public enum PoiKind
{
    /// <summary>
    /// World-map cité (Varn type "cité"). Click navigates to the cité
    /// screen if clickable, else flashes the "Cadastre suspendu"
    /// indicator. J3 default — preserved verbatim by the new dispatch
    /// switch so <c>world_map_pois.tres</c> needs no migration.
    /// </summary>
    City = 0,

    /// <summary>
    /// E3 quartier POI. Click navigates to the corresponding E5
    /// District Map screen via <see cref="PoiDispatchOutcome.NavigateToScreen"/>.
    /// </summary>
    District = 1,

    /// <summary>
    /// E3/E5 PNJ compagnon (party member). Click opens the E4
    /// Character Sheet modal pre-filled with the compagnon's id.
    /// J4 ships the dispatcher branch ; sprite count is 0 in J4 (party
    /// vide post-E1 nouvelle partie, conforme Varn §3.1.6). Branch is
    /// reachable via tests, surfaces in gameplay at J5+.
    /// </summary>
    NpcCompanion = 2,

    /// <summary>
    /// E3/E5 PNJ candidat-au-recrutement. Click opens the E4 Character
    /// Sheet modal pre-filled with the candidat's id. J4 ships at least
    /// one such POI on E3 to validate the E3-&gt;E4 modal path
    /// end-to-end (Rune J4 pre-brief D-J4-07 option α, D-J4-10 = 2).
    /// </summary>
    NpcCandidate = 3,

    /// <summary>
    /// E3/E5 bâtiment-clé (taverne, temple, échoppe, QG, ...). J4
    /// hover-only with a <see cref="PoiDispatchOutcome.ShowBlockedIndicator"/>
    /// click outcome ("Bâtiment non encore patenté."). Real interior
    /// drill-down lands post-MVP via the P2 layer "bâtiment".
    /// </summary>
    KeyBuilding = 4,

    /// <summary>
    /// E5 PNJ ambient (set Varn §6 E5.I-M, voix D Marges). J4-J5
    /// hover-only (no click handler) — defense in depth in the
    /// dispatcher forces the blocked-indicator path even if a hot-edited
    /// <c>.tres</c> flips <c>IsClickable</c> by accident.
    /// </summary>
    AmbientNpc = 5,

    /// <summary>
    /// E5 interactable (panneau d'affichage, fontaine, autel, ...).
    /// Same hover-only treatment as <see cref="AmbientNpc"/> in J4-J5.
    /// </summary>
    Interactable = 6,
}
