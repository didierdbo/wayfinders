namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# palette lookup for the iso character placeholder rendered in
/// the E2.2 mission recruit panel (step 3+4, Varn §A.8 roster
/// kira/dorn/vell). Same separation pattern as
/// <see cref="NpcCatalog"/> + <c>NpcRegistry</c> :
/// the Godot-bound <c>IsoCharacterPlaceholder</c> Control reads its
/// colour via this helper and constructs a <c>Godot.Color</c> at the
/// draw site, so the test surface stays engine-free.
///
/// <para>
/// <b>Why a placeholder instead of a Sprite2D right now.</b> Mira has
/// not shipped iso character bitmaps for kira / dorn / vell at recruit
/// landing time. The placeholder is a procedural parallelepiped iso
/// silhouette with a per-NPC unit tint so the panel reads as
/// "character A vs character B" while we wait for the bitmap pass. The
/// swap will be a 1-line change in
/// <see cref="Wayfinders.Client.Scenes.Ui.IsoCharacterPlaceholder"/> :
/// the <c>_Draw</c> override gets replaced by a child
/// <see cref="Godot.Sprite2D"/> at the same logical anchor.
/// </para>
///
/// <para>
/// <b>Palette anchors (Wayfinders visual DNA, warm-earth axis,
/// terracotta / umber / parchment lock 2026-05-07).</b> Three unit
/// tints, one per M1 roster entry :
/// <list type="bullet">
///   <item><c>kira</c> -- sable / terracotta clair
///         <c>#C8946E</c> (200, 148, 110). Reads "Gateway / road
///         dust / customs", her district affinity.</item>
///   <item><c>dorn</c> -- charcoal / umber foncé <c>#5A4A40</c>
///         (90, 74, 64). Reads "Intramuros / civic stone / heavy
///         leather", his district affinity.</item>
///   <item><c>vell</c> -- bronze / cuivre patiné <c>#A87856</c>
///         (168, 120, 86). Reads "Littoral / sea-weathered metal",
///         her district affinity.</item>
/// </list>
/// Each value sits inside the locked terracotta-ochre + umber + parchment
/// palette window. The fallback (unknown NPC id) returns parchment cream
/// <c>#E8DDC4</c> (232, 221, 196) -- intentionally neutral so a missing
/// roster entry shows visibly as "blank silhouette" rather than as a
/// random colour.
/// </para>
///
/// <para>
/// <b>Lock discipline.</b> Mira may dial these per-NPC tints during the
/// real bitmap pass (post-E2.2), but the seam (the 3-entry closed
/// lookup, the <c>(R,G,B)</c> tuple shape, the parchment fallback) stays
/// put. Tests pin the values so a silent edit surfaces as a red xUnit
/// step.
/// </para>
/// </summary>
public static class IsoCharacterPaletteLogic
{
    /// <summary>
    /// Parchment cream fallback (R,G,B) when the NPC id is unknown,
    /// null, or empty. Intentionally neutral so a missing roster entry
    /// reads visibly as "blank silhouette" instead of as a real
    /// character.
    /// </summary>
    public static readonly (float R, float G, float B) DefaultTintRgb =
        (232f / 255f, 221f / 255f, 196f / 255f); // #E8DDC4

    /// <summary>
    /// Resolve a M1 roster NPC id (<c>kira</c> / <c>dorn</c> /
    /// <c>vell</c>) to its placeholder unit tint. Unknown / null /
    /// empty ids return <see cref="DefaultTintRgb"/>.
    ///
    /// <para>
    /// <b>Why a tuple and not a Godot.Color.</b> Same rationale as
    /// <see cref="DistrictTypeHelpers.TintRgba"/> : this lives in the
    /// pure-C# layer beside the rest of the test-pinned palette helpers,
    /// and returning a <c>Godot.Color</c> would pull <c>Godot</c> into a
    /// unit-test surface that stays engine-free. The
    /// <c>IsoCharacterPlaceholder</c> wraps the tuple in
    /// <c>new Color(r,g,b,1f)</c> at the draw site -- one line, zero
    /// coupling cost.
    /// </para>
    /// </summary>
    public static (float R, float G, float B) ResolveTintRgb(string? npcId)
    {
        return npcId switch
        {
            "kira" => (200f / 255f, 148f / 255f, 110f / 255f), // #C8946E
            "dorn" => (90f / 255f, 74f / 255f, 64f / 255f),    // #5A4A40
            "vell" => (168f / 255f, 120f / 255f, 86f / 255f),  // #A87856
            _ => DefaultTintRgb,
        };
    }

    /// <summary>
    /// Convenience helper -- compose the hex string of the tint
    /// resolved by <see cref="ResolveTintRgb"/>. Mirrors the
    /// "#RRGGBB" form used in the brief so test pins read naturally
    /// against the spec.
    /// </summary>
    public static string ResolveTintHex(string? npcId)
    {
        var (r, g, b) = ResolveTintRgb(npcId);
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "#{0:X2}{1:X2}{2:X2}",
            (int)System.Math.Round(r * 255f),
            (int)System.Math.Round(g * 255f),
            (int)System.Math.Round(b * 255f));
    }
}
