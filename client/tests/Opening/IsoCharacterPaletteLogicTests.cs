using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="IsoCharacterPaletteLogic"/> -- the per-NPC
/// placeholder unit tint table consumed by
/// <c>IsoCharacterPlaceholder</c>'s procedural <c>_Draw</c>. Locks the
/// 3 M1 roster entries (kira / dorn / vell) + the parchment cream
/// fallback so a silent palette drift surfaces as a red test, not as a
/// visual bug at smoke time.
///
/// <para>
/// Same shape as <c>DistrictTypeTints</c> pins : closed-lookup table,
/// exact RGB values, fallback hex pinned.
/// </para>
/// </summary>
public sealed class IsoCharacterPaletteLogicTests
{
    // --------------------------------------------------------------------
    // Hex form -- the most readable pin against the brief.
    // --------------------------------------------------------------------

    [Fact]
    public void Kira_resolves_to_sand_terracotta()
        => Assert.Equal("#C8946E", IsoCharacterPaletteLogic.ResolveTintHex("kira"));

    [Fact]
    public void Dorn_resolves_to_charcoal_umber()
        => Assert.Equal("#5A4A40", IsoCharacterPaletteLogic.ResolveTintHex("dorn"));

    [Fact]
    public void Vell_resolves_to_bronze_copper()
        => Assert.Equal("#A87856", IsoCharacterPaletteLogic.ResolveTintHex("vell"));

    // --------------------------------------------------------------------
    // Fallback discipline -- unknown / null / empty -> parchment cream.
    // --------------------------------------------------------------------

    [Fact]
    public void Unknown_npc_returns_parchment_fallback()
        => Assert.Equal("#E8DDC4", IsoCharacterPaletteLogic.ResolveTintHex("hodge"));

    [Fact]
    public void Null_npc_returns_parchment_fallback()
        => Assert.Equal("#E8DDC4", IsoCharacterPaletteLogic.ResolveTintHex(null));

    [Fact]
    public void Empty_npc_returns_parchment_fallback()
        => Assert.Equal("#E8DDC4", IsoCharacterPaletteLogic.ResolveTintHex(""));

    // --------------------------------------------------------------------
    // RGB float form -- pins the tuple shape so the Godot.Color wrap at
    // the draw site stays a one-liner.
    // --------------------------------------------------------------------

    [Fact]
    public void Rgb_tuple_matches_byte_division_for_kira()
    {
        var (r, g, b) = IsoCharacterPaletteLogic.ResolveTintRgb("kira");
        Assert.Equal(200f / 255f, r, precision: 5);
        Assert.Equal(148f / 255f, g, precision: 5);
        Assert.Equal(110f / 255f, b, precision: 5);
    }

    [Fact]
    public void Rgb_tuple_default_matches_documented_constant()
    {
        var (r, g, b) = IsoCharacterPaletteLogic.DefaultTintRgb;
        Assert.Equal(232f / 255f, r, precision: 5);
        Assert.Equal(221f / 255f, g, precision: 5);
        Assert.Equal(196f / 255f, b, precision: 5);
    }

    // --------------------------------------------------------------------
    // Distinctness -- the 3 roster tints must be visibly distinct from
    // each other AND from the fallback. Cheap insurance against a
    // copy-paste typo that ships kira's hex on dorn.
    // --------------------------------------------------------------------

    [Fact]
    public void Three_roster_tints_are_all_distinct()
    {
        var kira = IsoCharacterPaletteLogic.ResolveTintHex("kira");
        var dorn = IsoCharacterPaletteLogic.ResolveTintHex("dorn");
        var vell = IsoCharacterPaletteLogic.ResolveTintHex("vell");
        var fallback = IsoCharacterPaletteLogic.ResolveTintHex(null);

        Assert.NotEqual(kira, dorn);
        Assert.NotEqual(kira, vell);
        Assert.NotEqual(dorn, vell);
        Assert.NotEqual(kira, fallback);
        Assert.NotEqual(dorn, fallback);
        Assert.NotEqual(vell, fallback);
    }
}
