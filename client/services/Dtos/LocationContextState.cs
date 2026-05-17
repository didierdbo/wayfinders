namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Typed snapshot of cross-screen "where am I / who's selected" state.
/// Held on <see cref="Wayfinders.Client.Services.GameState"/> ; mutated
/// only via GameState setter methods (no raw property setters, so each
/// write fires <c>LocationContextChanged</c> exactly once).
///
/// <para>
/// <b>Varn-lock 2026-05-17 Section B.</b> Replaces the free-form
/// <c>Dictionary&lt;string, object&gt; LocationContext</c> on GameState
/// (zero readers / zero writers in the codebase before this promotion ;
/// the dict was J3+ scaffolding waiting to multiply key-string typos).
/// All four fields nullable + default null. Readers MUST tolerate null
/// (a fresh boot, or a player on E1 with no city focus yet, returns
/// <see cref="Empty"/>).
/// </para>
///
/// <para>
/// <b>Field authority (Varn-lock §B.3).</b> Each field has exactly one
/// writer screen per state transition :
/// <list type="bullet">
///   <item><see cref="CurrentCityId"/> — written by E2AreaMap.OnEnter,
///         cleared by E1Title.OnEnter / cinematic OnLeave.</item>
///   <item><see cref="CurrentDistrictId"/> — written by
///         E3DistrictMap.OnEnter, cleared by E2AreaMap.OnEnter.</item>
///   <item><see cref="SelectedNpcId"/> — written by E4 modal OnOpen
///         and by the E2.3 recruit panel click handler.</item>
///   <item><see cref="SelectedPoiId"/> — written by E2 marker click,
///         cleared by E2 OnLeave.</item>
/// </list>
/// If a future feature needs new state, add a new field ; do not
/// multiplex an existing one.
/// </para>
///
/// <para>
/// <b>Why a record (immutable), not a mutable struct/class.</b> The
/// setters on <see cref="Wayfinders.Client.Services.GameState"/> replace
/// the whole snapshot via <c>with</c>-expressions ; that gives free
/// value equality for the "did this setter actually change anything"
/// gate that suppresses no-op signal emissions. A mutable shape would
/// need explicit per-field old/new comparisons in the setter.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> No <c>using Godot;</c> so xUnit can
/// pin the record contract (default-null fields, <c>with</c>-expression
/// equality, all-null <see cref="Empty"/> identity) via the cherry-pick
/// path in <c>Wayfinders.Client.Tests.csproj</c>. The Node-bound signal
/// surface on GameState is validated via integration smoke.
/// </para>
/// </summary>
public sealed record LocationContextState
{
    /// <summary>Canonical RegionId of the city the player is viewing
    /// (e.g. <c>"halfgate"</c>). Null on E1 world map (no city focus).</summary>
    public string? CurrentCityId { get; init; }

    /// <summary>Canonical district slug inside the current city
    /// (e.g. <c>"gateway"</c>, <c>"intramuros"</c>, <c>"littoral"</c>).
    /// Null when the player is at E2 layer without having drilled into
    /// a specific district.</summary>
    public string? CurrentDistrictId { get; init; }

    /// <summary>NpcId of the currently-selected NPC (the one the
    /// player is about to inspect via E4, or recruit via the E2.3
    /// recruit panel). Null when no NPC is selected.</summary>
    public string? SelectedNpcId { get; init; }

    /// <summary>PoiId the player last clicked / focused (e.g.
    /// <c>"e2.halfgate.gateway"</c>). Disambiguates "which marker did
    /// I just open" when multiple tooltips/modals could chain.</summary>
    public string? SelectedPoiId { get; init; }

    /// <summary>
    /// Canonical empty snapshot — all four fields null. Returned by
    /// <see cref="Wayfinders.Client.Services.GameState.LocationContext"/>
    /// on boot and after <c>ClearLocationContext()</c>.
    /// </summary>
    public static LocationContextState Empty { get; } = new();
}
