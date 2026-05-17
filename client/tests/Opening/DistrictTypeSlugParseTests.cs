using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Section A (Varn-lock 2026-05-17) — pin the inverse of
/// <see cref="DistrictTypeHelpers.AssetKeySuffix"/>. Section A's marker
/// spawner parses the slug-2 segment of an active mission's
/// <c>target_poi</c> ("e2.halfgate.<b>gateway</b>") and feeds it to
/// <see cref="DistrictTypeHelpers.TryParseSlug"/> to resolve the
/// centroid. The closed lookup MUST stay symmetric with
/// <see cref="DistrictTypeHelpers.AssetKeySuffix"/> ; an unmapped slug
/// MUST return false so the E2 spawner can log + skip per Decision 5.
/// </summary>
public sealed class DistrictTypeSlugParseTests
{
    [Theory]
    [InlineData("intramuros",      DistrictType.Intramuros)]
    [InlineData("wall",            DistrictType.Wall)]
    [InlineData("gateway",         DistrictType.Gateway)]
    [InlineData("outskirts",       DistrictType.Outskirts)]
    [InlineData("hinterland_agri", DistrictType.HinterlandAgri)]
    [InlineData("littoral",        DistrictType.Littoral)]
    public void TryParseSlug_returns_true_for_every_known_slug(
        string slug,
        DistrictType expected)
    {
        var ok = DistrictTypeHelpers.TryParseSlug(slug, out var district);
        Assert.True(ok);
        Assert.Equal(expected, district);
    }

    [Theory]
    [InlineData("")]
    [InlineData("INTRAMUROS")]  // case-sensitive
    [InlineData("Intramuros")]  // case-sensitive
    [InlineData("unknown_slug")]
    [InlineData("gateway_extra")]
    public void TryParseSlug_returns_false_for_unknown_or_drifted_slugs(string slug)
    {
        // Unknown / case-drifted slugs MUST fail. The E2 spawner logs
        // a warning and skips the marker (Decision 5). Lenient parsing
        // here would let a backend drift land a marker at the wrong
        // district silently.
        var ok = DistrictTypeHelpers.TryParseSlug(slug, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryParseSlug_is_inverse_of_AssetKeySuffix_for_every_district()
    {
        // Symmetry pin : every district's AssetKeySuffix must parse
        // back to the same enum value. A silent rename of either
        // surface breaks this.
        foreach (DistrictType district in System.Enum.GetValues(typeof(DistrictType)))
        {
            var slug = DistrictTypeHelpers.AssetKeySuffix(district);
            var ok = DistrictTypeHelpers.TryParseSlug(slug, out var parsed);
            Assert.True(ok, $"AssetKeySuffix({district}) = '{slug}' failed to parse back");
            Assert.Equal(district, parsed);
        }
    }
}
