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

    /// <summary>
    /// Pin the Varn-locked per-district tint palette (Rune lock
    /// 2026-05-16, Option C). The 6 sRGB triplets feed the
    /// <c>district_tint</c> uniform of
    /// <c>area_grid_tile_overlay.gdshader</c> ; drifting any value here
    /// is a silent visual regression (a cell would render in a different
    /// hue at runtime), so we pin all 24 floats to surface the change
    /// as a red xUnit step.
    /// </summary>
    [Theory]
    [InlineData(DistrictType.Intramuros,     0.945f, 0.910f, 0.808f)]
    [InlineData(DistrictType.Wall,           0.620f, 0.600f, 0.565f)]
    [InlineData(DistrictType.Gateway,        0.835f, 0.510f, 0.376f)]
    [InlineData(DistrictType.Outskirts,      0.815f, 0.733f, 0.580f)]
    [InlineData(DistrictType.HinterlandAgri, 0.580f, 0.620f, 0.396f)]
    [InlineData(DistrictType.Littoral,       0.560f, 0.640f, 0.682f)]
    public void TintRgba_locks_the_per_district_palette(
        DistrictType d, float expectedR, float expectedG, float expectedB)
    {
        var tint = DistrictTypeHelpers.TintRgba(d);
        Assert.Equal(expectedR, tint.R);
        Assert.Equal(expectedG, tint.G);
        Assert.Equal(expectedB, tint.B);
        Assert.Equal(1.0f, tint.A);
    }

    [Fact]
    public void TintRgba_returns_identity_white_for_unknown_value()
    {
        // Defensive : the helper stays total. An unknown enum value
        // (cast from a stale persisted int) renders as identity-white
        // = the bare bitmap, not a black hole.
        var tint = DistrictTypeHelpers.TintRgba((DistrictType)42);
        Assert.Equal(1.0f, tint.R);
        Assert.Equal(1.0f, tint.G);
        Assert.Equal(1.0f, tint.B);
        Assert.Equal(1.0f, tint.A);
    }
}
