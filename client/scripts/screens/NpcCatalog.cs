using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# NPC registry data + lookup helpers. The Godot autoload
/// <c>NpcRegistry</c> wraps this static catalog so xUnit can pin the
/// registry contract without spinning a Godot runtime.
///
/// <para>
/// <b>Same separation pattern as <see cref="PoiDispatchLogic"/> +
/// <see cref="PoiDefinitionDto"/>.</b> The Godot-side <c>NpcRegistry : Node</c>
/// holds nothing but a wiring layer that exposes
/// <see cref="LookupDisplayName"/> and <see cref="LookupPortraitKey"/>
/// to the engine. Lifetimes (Node lifecycle, autoload init) live on
/// the Godot side ; the data table + lookup logic live here. This keeps
/// the test surface Godot-free and the production runtime un-coupled
/// from xUnit's view of the same data.
/// </para>
///
/// <para>
/// <b>Why a static class with internal Dictionary, not a Resource.</b>
/// Concrete-first per Rune coaching brief §6: the catalog is engineering
/// data, not narrative copy. Varn does not hand-edit NPC ids -- those
/// are wired by the dispatcher and by Mira's portrait file naming. A
/// <c>.tres</c> here would invite typos that break hot-swap silently.
/// When the registry crosses ~5-7 entries OR Varn needs to author NPC
/// profiles by hand, we extract to a <c>NpcCatalog.tres</c> -- not before.
/// </para>
///
/// <para>
/// <b>J5 contents.</b> Two entries: <c>kira</c> (stateful 3-portrait,
/// Mira §6 E4.2) + <c>dorn</c> (stateless single portrait). Plus the
/// unknown-id fallback, returned as <see cref="NpcCatalogEntry.Default"/>.
/// </para>
/// </summary>
public static class NpcCatalog
{
    /// <summary>
    /// Asset key returned for unknown / null / empty NPC ids. Mapped
    /// to a neutral parchment placeholder via the AssetResolver +
    /// <c>asset_keys.json</c>.
    /// </summary>
    public const string DefaultPortraitKey = "e4.portrait.default";

    /// <summary>
    /// Display name returned for unknown / null / empty NPC ids. Voix
    /// A neutre cohérent J3/J4 blocked wording.
    /// </summary>
    public const string DefaultDisplayName = "Inconnu";

    private static readonly Dictionary<string, NpcCatalogEntry> Entries = new()
    {
        ["kira"] = new NpcCatalogEntry(
            DisplayName: "Kira",
            PortraitKeyBase: "e4.portrait.kira",
            HasPerStatePortraits: true),
        ["dorn"] = new NpcCatalogEntry(
            DisplayName: "Dorn",
            PortraitKeyBase: "e4.portrait.dorn",
            HasPerStatePortraits: false),
    };

    /// <summary>
    /// J5 known NPC -> fiche cadastrale number map. Used by the
    /// E4 modal title interpolation. Unknown NPCs fall back to "000"
    /// so missing registry entries are visible at a glance.
    /// </summary>
    private static readonly Dictionary<string, string> FicheNumbers = new()
    {
        ["kira"] = "001",
        ["dorn"] = "002",
    };

    /// <summary>Number of registered NPCs (excludes the fallback).</summary>
    public static int Count => Entries.Count;

    /// <summary>
    /// Resolve an NPC id to its catalog entry, or the fallback
    /// <see cref="NpcCatalogEntry.Default"/> when the id is unknown,
    /// null, or empty.
    /// </summary>
    public static NpcCatalogEntry Resolve(string? npcId)
    {
        if (string.IsNullOrEmpty(npcId))
            return NpcCatalogEntry.Default;
        return Entries.TryGetValue(npcId, out var entry)
            ? entry
            : NpcCatalogEntry.Default;
    }

    /// <summary>Display name for the NPC id, or the fallback default.</summary>
    public static string LookupDisplayName(string? npcId) => Resolve(npcId).DisplayName;

    /// <summary>
    /// Compose the AssetResolver key for the NPC's portrait in the
    /// requested state. Stateless NPCs (Dorn, default) ignore the
    /// state argument and return the bare key base. Stateful NPCs
    /// (Kira) suffix the state lowercased.
    /// </summary>
    public static string LookupPortraitKey(string? npcId, NpcPortraitState state)
    {
        var entry = Resolve(npcId);
        if (!entry.HasPerStatePortraits)
            return entry.PortraitKeyBase;
        return $"{entry.PortraitKeyBase}.{state.ToKeySuffix()}";
    }

    /// <summary>
    /// Map an NPC id to its cadastre fiche number (the "[xxx]" slot in
    /// the Varn E4.A title template). Unknown NPCs fall back to the
    /// generic <c>000</c> stamp -- visibly different from real fiches
    /// so devs spot a missing registry entry at a glance.
    /// </summary>
    public static string LookupFicheNumber(string? npcId)
    {
        if (string.IsNullOrEmpty(npcId))
            return "000";
        return FicheNumbers.TryGetValue(npcId, out var n) ? n : "000";
    }
}

/// <summary>
/// Three-state portrait taxonomy for NPCs that ship multiple emotional
/// portraits (Mira spec §6 E4.2). J5 surfaces only
/// <see cref="Calm"/> (D-J5-02 + pre-brief Risk #2) ; the rest of the
/// enum is reachable via <see cref="NpcCatalog.LookupPortraitKey"/> so
/// the state-switch slot is already wired when the state-driven
/// mechanic ships post-MVP.
/// </summary>
public enum NpcPortraitState
{
    /// <summary>Default observed state -- the J5 always-on value.</summary>
    Calm = 0,

    /// <summary>NPC alert / strained -- variant Mira §6 E4.2.</summary>
    Alert = 1,

    /// <summary>NPC blessé / hors-combat -- variant Mira §6 E4.2.</summary>
    Wounded = 2,
}

/// <summary>
/// Helpers for <see cref="NpcPortraitState"/>.
/// </summary>
public static class NpcPortraitStateExtensions
{
    /// <summary>
    /// Lower-case suffix used in the AssetResolver key composition.
    /// Locked at <c>calm</c> / <c>alert</c> / <c>wounded</c> to match
    /// Mira's filename naming convention.
    /// </summary>
    public static string ToKeySuffix(this NpcPortraitState state) => state switch
    {
        NpcPortraitState.Calm => "calm",
        NpcPortraitState.Alert => "alert",
        NpcPortraitState.Wounded => "wounded",
        _ => "calm",
    };

    /// <summary>
    /// Lowercase French rendering for the Varn E4.N template
    /// "Sujet observé : {0}." (J5 always feeds <c>calme</c>).
    /// </summary>
    public static string ToFrenchLowercase(this NpcPortraitState state) => state switch
    {
        NpcPortraitState.Calm => "calme",
        NpcPortraitState.Alert => "tendu",
        NpcPortraitState.Wounded => "blessé",
        _ => "calme",
    };
}

/// <summary>
/// Single registry row. <see cref="HasPerStatePortraits"/> drives
/// whether <see cref="NpcCatalog.LookupPortraitKey"/> appends a state
/// suffix or returns the bare base.
/// </summary>
public sealed record NpcCatalogEntry(
    string DisplayName,
    string PortraitKeyBase,
    bool HasPerStatePortraits)
{
    /// <summary>
    /// Fallback row used when an NPC id is unknown. Stateless by
    /// design (no per-state default placeholders shipped J5).
    /// </summary>
    public static readonly NpcCatalogEntry Default = new(
        DisplayName: NpcCatalog.DefaultDisplayName,
        PortraitKeyBase: NpcCatalog.DefaultPortraitKey,
        HasPerStatePortraits: false);
}
