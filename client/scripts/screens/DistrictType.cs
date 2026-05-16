namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Varn-locked closed lookup of district types covering the E2AreaMap 8×8
/// intra-area grid (A4.4, ratified 2026-05-16). Referenced by
/// <see cref="HalfgateNpcAuthoring"/>'s <c>district_origin</c> field and by
/// <see cref="AreaGridLogic"/>'s coord-to-district mapping.
///
/// <para>
/// <b>Lock contract.</b> This is a Varn-locked closed lookup
/// (memory <c>feedback_varn_locked_specs</c>) -- no silent additions,
/// removals, or value renumbering. Any change goes through Varn. The
/// integer values are part of the persistence contract.
/// </para>
///
/// <para>
/// <b>Six values, in the locked order.</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="Intramuros"/> -- the walled city centre (Cadastre +
///         Maison de l'Arpenteur core blocks).</item>
///   <item><see cref="Wall"/> -- the curtain wall ring (defensive
///         circumference, guard posts).</item>
///   <item><see cref="Gateway"/> -- the gates piercing the wall (main road
///         entry, customs, traffic).</item>
///   <item><see cref="Outskirts"/> -- the immediate periphery outside the
///         wall (faubourgs, workshops, road approaches).</item>
///   <item><see cref="HinterlandAgri"/> -- the agricultural belt beyond
///         the outskirts (fields, orchards, sheepfolds).</item>
///   <item><see cref="Littoral"/> -- the coastal / river edge (docks,
///         fishing huts, brackish marsh).</item>
/// </list>
///
/// <para>
/// The mapping coord → district_type for Halfgate's 8×8 grid is owned by
/// <see cref="AreaGridLogic.ResolveDistrictType"/> ; that is Varn's
/// authoring surface (the grid is laid out as a city-with-wall pattern, not
/// a uniform tessellation).
/// </para>
/// </summary>
public enum DistrictType
{
    /// <summary>Walled centre. Locked integer 0.</summary>
    Intramuros = 0,

    /// <summary>Curtain wall ring. Locked integer 1.</summary>
    Wall = 1,

    /// <summary>Gates piercing the wall. Locked integer 2.</summary>
    Gateway = 2,

    /// <summary>Immediate periphery outside the wall. Locked integer 3.</summary>
    Outskirts = 3,

    /// <summary>Agricultural belt beyond the outskirts. Locked integer 4.</summary>
    HinterlandAgri = 4,

    /// <summary>Coastal / river edge. Locked integer 5.</summary>
    Littoral = 5,
}

/// <summary>
/// Pure-C# helpers around <see cref="DistrictType"/>. Mirrors the
/// <see cref="TileRevealStateHelpers"/> seam : Godot-free, xUnit-pinned,
/// so the closed-lookup contract survives engine refactors.
/// </summary>
public static class DistrictTypeHelpers
{
    /// <summary>
    /// Resolve a <see cref="DistrictType"/> to its parchment-flavoured
    /// human-readable label. Used by tooltip text composition (A4.5 EC6
    /// per-cell hover : "district_type + rumor hook"). Lines are in
    /// English at the canonical layer ; localisation lands later via the
    /// <c>OpeningStrings</c> resource pattern.
    /// </summary>
    public static string DisplayName(DistrictType district) => district switch
    {
        DistrictType.Intramuros     => "Intramuros",
        DistrictType.Wall           => "Wall",
        DistrictType.Gateway        => "Gateway",
        DistrictType.Outskirts      => "Outskirts",
        DistrictType.HinterlandAgri => "Hinterland",
        DistrictType.Littoral       => "Littoral",
        _ => "Unknown",
    };

    /// <summary>
    /// Asset-key suffix used by <c>AssetResolver</c> to fetch the district
    /// tile texture. Locked snake_case so the file naming convention stays
    /// stable (matches <c>asset_keys.json</c> entries
    /// <c>e2.area_grid.tile.&lt;suffix&gt;</c>).
    /// </summary>
    public static string AssetKeySuffix(DistrictType district) => district switch
    {
        DistrictType.Intramuros     => "intramuros",
        DistrictType.Wall           => "wall",
        DistrictType.Gateway        => "gateway",
        DistrictType.Outskirts      => "outskirts",
        DistrictType.HinterlandAgri => "hinterland_agri",
        DistrictType.Littoral       => "littoral",
        _ => "outskirts",
    };
}
