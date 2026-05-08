namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Per-cell quantified palette consumer contract (slice 2 / livrable 1 —
/// M3 / L1 World fondations / 2026-05-08). A source supplies, for each
/// grid cell, an array of <b>three</b> teintes ordered darkest → brightest
/// by Rec.709 luminance (matching the order
/// <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/> writes to
/// the baked PNG). The fog renderer reads from a source once per cell at
/// spawn time, paints the swatch behind the carton, and never queries
/// again at runtime — palette is content-static for slice 2.
///
/// <para>
/// <b>Why an interface, not a concrete type.</b> Two implementations
/// already need to coexist :
/// <list type="bullet">
///   <item><see cref="Wayfinders.Client.Services.BakedFogPaletteSource"/> reads
///         the PNG produced by the editor tool. The runtime
///         <see cref="Wayfinders.Client.Components.FogTileLayer"/> uses
///         this in production.</item>
///   <item>A fallback "neutral carton" source, instantiated inline by
///         <see cref="Wayfinders.Client.Services.BakedFogPaletteSource"/>
///         when the baked PNG is missing or unreadable, so the slice 2
///         scaffold doesn't crash on a fresh checkout pre-bake. See the
///         <c>FallbackPalette</c> field on the implementation.</item>
/// </list>
/// xUnit pins the contract on the lookup signature and the fallback
/// shape ; the runtime PNG loader is exercised through manual integration
/// (livrable 1 acceptance criterion : "Fallback safe si le PNG n'existe
/// pas").
/// </para>
///
/// <para>
/// <b>Float-RGB struct, not <c>Godot.Color</c>.</b> The pure-C# seam stays
/// engine-free so xUnit pins the math. The Godot-bound implementation
/// (<see cref="Wayfinders.Client.Services.BakedFogPaletteSource"/>)
/// converts <c>Godot.Color</c> ↔ <see cref="PaletteRgb"/> at the
/// boundary — same pattern as <c>PanVec2</c> / <c>Vector2</c>.
/// </para>
/// </summary>
public interface IFogPaletteSource
{
    /// <summary>
    /// Look up the 3 quantified teintes for a given cell. Always
    /// returns an array of length 3 ; cells outside the grid bounds
    /// return the neutral carton fallback rather than null, so the
    /// caller never has to branch on a missing-cell case (the bake
    /// covers every cell <see cref="FogTileGridLogic.EnumerateCells"/>
    /// produces).
    /// </summary>
    /// <param name="coord">Cell coordinate (zero-indexed).</param>
    /// <returns>Three teintes, darkest → brightest by Rec.709 luminance.</returns>
    PaletteRgb[] GetPaletteForCell(GridCoord coord);
}

/// <summary>
/// Pure-C# float-RGB triple for the palette seam. Channel range
/// <c>[0, 1]</c> like <c>Godot.Color</c>. Equality semantics make the
/// type easy to assert against in xUnit ; the runtime converts to
/// <c>Godot.Color</c> at the renderer edge.
/// </summary>
/// <param name="R">Red channel, [0, 1].</param>
/// <param name="G">Green channel, [0, 1].</param>
/// <param name="B">Blue channel, [0, 1].</param>
public readonly record struct PaletteRgb(float R, float G, float B)
{
    /// <summary>
    /// Neutral-carton fallback teinte, used when the baked PNG is
    /// missing or fails to load. Matches the slice 1 carton beige-écru
    /// modulate (FogTileLayer.cs : <c>Color(0.91f, 0.85f, 0.72f)</c>).
    /// Three identical instances populate the fallback palette so the
    /// renderer's "darkest → brightest" assumption holds vacuously.
    /// </summary>
    public static readonly PaletteRgb NeutralCarton = new(0.91f, 0.85f, 0.72f);

    /// <summary>
    /// Three-element fallback palette returned when no bake is
    /// available. All three teintes are identical neutral-carton —
    /// visually the swatch reads as a flat colored band, the carton
    /// itself dominates, and the slice 2 scaffold remains observable
    /// while waiting on the bake.
    /// </summary>
    public static PaletteRgb[] FallbackPalette() => new[]
    {
        NeutralCarton, NeutralCarton, NeutralCarton,
    };
}
