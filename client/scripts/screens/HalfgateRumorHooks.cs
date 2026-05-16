using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Varn-locked one-line rumor hooks used by the E2.1 per-cell hover
/// tooltip (A4.5 EC6).
///
/// <para>
/// <b>Tooltip composition rule (A4.5 EC6).</b> When the player hovers a
/// <see cref="TileRevealState.Partial"/> or
/// <see cref="TileRevealState.Revealed"/> cell, the tooltip displays :
/// </para>
/// <list type="number">
///   <item>The cell's <see cref="DistrictType"/> human-readable label
///         (from <see cref="DistrictTypeHelpers.DisplayName"/>).</item>
///   <item>One rumor hook line. Preferred source : if a mission whose
///         <c>tiles_to_partial</c> footprint included this cell exists,
///         take that mission's <c>narrative_hook</c>. Otherwise fall
///         back to the generic per-district line authored here.</item>
/// </list>
///
/// <para>
/// <b>Why the generic fallback exists.</b> E2.1 has one tutorial mission
/// whose footprint covers 4 intramuros cells only. The other 60 cells
/// in the 8×8 grid will be hover-able the moment the player recruits an
/// NPC (E2.2) -- their known_tiles span every district, so EC6 needs a
/// fallback line per district_type to avoid an empty tooltip.
/// </para>
///
/// <para>
/// <b>Pratchett-tone constraint.</b> Per the Wayfinders tonal DNA
/// (memory <c>project_wayfinders_tonal_dna</c>) : intimate, mock-serious,
/// no parodic register. Each line is 60-140 characters, single line, no
/// line breaks, ends with a period. The voice is the Cadastre itself --
/// dry, weary, faintly contemptuous of irregularity.
/// </para>
///
/// <para>
/// <b>Flagged for Varn.</b> The 6 lines below are Rune-authored
/// placeholders pending Varn audit. They cover the EC6 contract
/// (one line per district_type) and respect the tonal DNA at the
/// drafting level, but a Varn pass should refine the prose before
/// E2.6's ML mission generator inherits this fallback path. The
/// flag is also logged in the E2.1 final report.
/// </para>
/// </summary>
public static class HalfgateRumorHooks
{
    /// <summary>
    /// Resolve the fallback rumor hook for a district_type. Used by the
    /// hover-tooltip path when no mission narrative line applies for the
    /// cell.
    /// </summary>
    public static string ResolveFallback(DistrictType district) => district switch
    {
        DistrictType.Intramuros     => _fallbacks[DistrictType.Intramuros],
        DistrictType.Wall           => _fallbacks[DistrictType.Wall],
        DistrictType.Gateway        => _fallbacks[DistrictType.Gateway],
        DistrictType.Outskirts      => _fallbacks[DistrictType.Outskirts],
        DistrictType.HinterlandAgri => _fallbacks[DistrictType.HinterlandAgri],
        DistrictType.Littoral       => _fallbacks[DistrictType.Littoral],
        _ => _fallbacks[DistrictType.Outskirts],
    };

    /// <summary>
    /// Compose the full EC6 tooltip text for a cell. Either uses the
    /// supplied mission narrative line (if a mission applies to the
    /// cell), or falls back to <see cref="ResolveFallback"/>.
    /// </summary>
    public static string ComposeCellTooltip(
        DistrictType district,
        string? missionNarrativeHook)
    {
        var districtLabel = DistrictTypeHelpers.DisplayName(district);
        var rumor = string.IsNullOrEmpty(missionNarrativeHook)
            ? ResolveFallback(district)
            : missionNarrativeHook;
        return $"{districtLabel}\n{rumor}";
    }

    private static readonly Dictionary<DistrictType, string> _fallbacks = new()
    {
        [DistrictType.Intramuros] =
            "The walled centre. Filed in triplicate, watched in person, and rarely surprising.",
        [DistrictType.Wall] =
            "Cut stone, set in time of plenty, repointed in time of necessity, and never quite tall enough.",
        [DistrictType.Gateway] =
            "Where the road becomes the city. The Cadastre would rather it happened slower.",
        [DistrictType.Outskirts] =
            "Faubourgs : workshops, smoke, and people who know the wall better than the wall knows itself.",
        [DistrictType.HinterlandAgri] =
            "Fields and orchards. Half of Halfgate's bread, all of its gossip, and a surprising amount of sheep.",
        [DistrictType.Littoral] =
            "The edge where the city ends and the water begins. Nobody quite owns it. The Cadastre is trying.",
    };
}
