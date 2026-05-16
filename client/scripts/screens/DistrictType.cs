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

    /// <summary>
    /// Resolve the per-district sRGB tint used by
    /// <c>area_grid_tile_overlay.gdshader</c>'s <c>district_tint</c>
    /// uniform (Rune-locked 2026-05-16, Option C).
    ///
    /// <para>
    /// <b>Why a tuple and not a Godot.Color.</b> This helper lives in the
    /// pure-C# layer beside <see cref="TileRevealStateHelpers"/> -- the
    /// Godot-free, xUnit-pinned seam. Returning a <c>Godot.Color</c>
    /// would pull <c>Godot</c> into a unit-test surface that intentionally
    /// stays engine-free. The consumer scene
    /// (<c>E2AreaMap.AreaGrid.cs</c>) wraps the tuple in
    /// <c>new Color(r,g,b,a)</c> at the spawn site -- one line, zero
    /// coupling cost.
    /// </para>
    ///
    /// <para>
    /// <b>Palette anchors (Wayfinders visual DNA, warm-earth axis,
    /// Bourgeon + Bonner reference).</b> The 6 tints sit on the locked
    /// terracotta-ochre + parchment-cream + umber axis. Saturation is
    /// intentionally low : the shader multiplies these against the
    /// painted base bitmap (E1 neutral today, per-district Mira pass
    /// later), so a saturated tint would clobber the brush-stroke
    /// readability. Each value was picked to read as a recognisable
    /// district hue WITHOUT screaming "pop colour".
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item><b>Intramuros</b> -- parchment cream (945, 910, 808) : the
    ///         centre reads "civic / authored / canonical map colour".</item>
    ///   <item><b>Wall</b> -- stone grey (620, 600, 565) : neutral
    ///         desaturated, reads "defensive masonry".</item>
    ///   <item><b>Gateway</b> -- terracotta (835, 510, 376) : warm chaud,
    ///         signals "main road / customs / traffic".</item>
    ///   <item><b>Outskirts</b> -- sand beige (815, 733, 580) : the
    ///         pierced earth between wall and fields, faubourg dust.</item>
    ///   <item><b>HinterlandAgri</b> -- olive (580, 620, 396) : muted
    ///         green-yellow, reads "champs / sheepfolds" without going
    ///         saturated-grass.</item>
    ///   <item><b>Littoral</b> -- grey-blue (560, 640, 682) : the only
    ///         cool tint, signals "brackish / quay / damp".</item>
    /// </list>
    ///
    /// <para>
    /// <b>Lock discipline.</b> Mira may dial these values during the
    /// per-district bitmap pass post-E2.1c, but the seam (the 6-entry
    /// closed lookup, the (R, G, B, A) tuple shape, the source_color
    /// expectation) stays put. Tests pin the values so a silent edit
    /// surfaces as a red xUnit step, not as a drift at runtime.
    /// </para>
    /// </summary>
    public static (float R, float G, float B, float A) TintRgba(DistrictType district) => district switch
    {
        DistrictType.Intramuros     => (0.945f, 0.910f, 0.808f, 1.0f),
        DistrictType.Wall           => (0.620f, 0.600f, 0.565f, 1.0f),
        DistrictType.Gateway        => (0.835f, 0.510f, 0.376f, 1.0f),
        DistrictType.Outskirts      => (0.815f, 0.733f, 0.580f, 1.0f),
        DistrictType.HinterlandAgri => (0.580f, 0.620f, 0.396f, 1.0f),
        DistrictType.Littoral       => (0.560f, 0.640f, 0.682f, 1.0f),
        _                            => (1.000f, 1.000f, 1.000f, 1.0f),
    };
}
