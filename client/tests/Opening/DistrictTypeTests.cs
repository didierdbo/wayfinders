using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the Varn-locked closed lookup A4.4 (district_type vocab). The
/// 6 values + their integer mapping + their asset-key suffixes are
/// load-bearing across the E2.1 grid rendering, the per-cell hover
/// tooltip composition, and the future NPC <c>district_origin</c>
/// schema lock (A4.3).
/// </summary>
public sealed class DistrictTypeTests
{
    [Fact]
    public void Closed_lookup_has_exactly_six_values()
    {
        // A4.4 lock : 6 values, no more, no less. Adding a 7th value
        // without a Varn pass is a Varn-locked-spec violation per
        // feedback_varn_locked_specs.
        var values = System.Enum.GetValues<DistrictType>();
        Assert.Equal(6, values.Length);
    }

    [Fact]
    public void Integer_values_are_pinned_for_persistence_compat()
    {
        // Persistence schema lock. Renumbering would silently corrupt
        // any save format that serialises district_origin / district_type.
        Assert.Equal(0, (int)DistrictType.Intramuros);
        Assert.Equal(1, (int)DistrictType.Wall);
        Assert.Equal(2, (int)DistrictType.Gateway);
        Assert.Equal(3, (int)DistrictType.Outskirts);
        Assert.Equal(4, (int)DistrictType.HinterlandAgri);
        Assert.Equal(5, (int)DistrictType.Littoral);
    }

    [Theory]
    [InlineData(DistrictType.Intramuros,     "Intramuros")]
    [InlineData(DistrictType.Wall,           "Wall")]
    [InlineData(DistrictType.Gateway,        "Gateway")]
    [InlineData(DistrictType.Outskirts,      "Outskirts")]
    [InlineData(DistrictType.HinterlandAgri, "Hinterland")]
    [InlineData(DistrictType.Littoral,       "Littoral")]
    public void DisplayName_returns_human_readable_label(DistrictType d, string expected)
    {
        Assert.Equal(expected, DistrictTypeHelpers.DisplayName(d));
    }

    [Theory]
    [InlineData(DistrictType.Intramuros,     "intramuros")]
    [InlineData(DistrictType.Wall,           "wall")]
    [InlineData(DistrictType.Gateway,        "gateway")]
    [InlineData(DistrictType.Outskirts,      "outskirts")]
    [InlineData(DistrictType.HinterlandAgri, "hinterland_agri")]
    [InlineData(DistrictType.Littoral,       "littoral")]
    public void AssetKeySuffix_locks_the_snake_case_naming_convention(DistrictType d, string expected)
    {
        // The suffix is part of the asset_keys.json contract :
        // "e2.area_grid.tile.<suffix>". Drifting the suffix here breaks
        // the texture lookup silently (placeholder fallback fires).
        Assert.Equal(expected, DistrictTypeHelpers.AssetKeySuffix(d));
    }
}
