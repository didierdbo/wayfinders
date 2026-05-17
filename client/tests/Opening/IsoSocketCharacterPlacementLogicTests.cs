using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// xUnit pins for <see cref="IsoSocketCharacterPlacementLogic"/> -- the
/// helper that aligns the occupant character's parallelepiped BASE with
/// the socle rhombus' TOP apex (E2.2 étape 6 fix, 2026-05-17).
///
/// <para>
/// <b>Why pinned.</b> The first cut of <c>SetOccupiedNpc</c> silently
/// mixed the rhombus' vertical centre with the occupant's full Control
/// height, producing the "perso flotte au-dessus du socle" smoke bug.
/// These pins catch any future drift in the alignment math (padding
/// constants, label-band conventions, or the cmin sizing of the
/// IsoCharacterPlaceholder).
/// </para>
/// </summary>
public sealed class IsoSocketCharacterPlacementLogicTests
{
    private const float OccupantPadding = 12f;

    [Fact]
    public void Base_of_occupant_lands_on_rhombus_top_apex()
    {
        // Socket Size.X = 168, rhombus drawn with top apex at y = 4
        // (PaddingPx = 4 + (availH - h) / 2 with availH = h).
        const float socketX = 168f;
        const float rhombusTopY = 4f;
        // Compact occupant cmin = (120, 244), padding internal = 12.
        const float occX = 120f;
        const float occY = 244f;

        var (_, posY) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketX, rhombusTopY, occX, occY, OccupantPadding);

        // Base in occupant-local coords = occY - padding = 232.
        // Position.Y + 232 must equal rhombusTopY = 4.
        // => Position.Y = 4 - 232 = -228.
        Assert.Equal(-228f, posY);
        // Verify the projection lands exactly on the apex.
        var globalBaseY = posY + (occY - OccupantPadding);
        Assert.Equal(rhombusTopY, globalBaseY);
    }

    [Fact]
    public void Occupant_is_centred_horizontally_on_socket()
    {
        const float socketX = 200f;
        const float occX = 120f;

        var (posX, _) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketX, rhombusTopY: 10f, occX, occupantSizeY: 244f, OccupantPadding);

        // (200 - 120) / 2 = 40
        Assert.Equal(40f, posX);
    }

    [Fact]
    public void Same_socket_and_occupant_width_yields_zero_x()
    {
        var (posX, _) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketSizeX: 120f,
            rhombusTopY: 4f,
            occupantSizeX: 120f,
            occupantSizeY: 244f,
            occupantPaddingPx: OccupantPadding);
        Assert.Equal(0f, posX);
    }

    [Fact]
    public void Larger_occupant_yields_negative_x()
    {
        // Edge case : occupant wider than socket (shouldn't happen with
        // CompactMinSize=120 vs socle min=168, but the formula must stay
        // symmetric -- negative offset = occupant overhangs to the left).
        var (posX, _) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketSizeX: 100f,
            rhombusTopY: 0f,
            occupantSizeX: 150f,
            occupantSizeY: 244f,
            occupantPaddingPx: OccupantPadding);
        Assert.Equal(-25f, posX);
    }

    [Fact]
    public void Padding_is_subtracted_from_occupant_height_for_base_alignment()
    {
        // Lock the contract : the base is at (occupantSizeY - padding),
        // NOT at occupantSizeY. If the IsoCharacterPlaceholder ever drops
        // its bottom padding, this test breaks loud + the helper has to
        // be re-derived.
        const float rhombusTopY = 100f;
        const float occY = 244f;
        const float padding = 12f;

        var (_, posY) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketSizeX: 168f,
            rhombusTopY: rhombusTopY,
            occupantSizeX: 120f,
            occupantSizeY: occY,
            occupantPaddingPx: padding);

        // Expected: rhombusTopY - (occY - padding) = 100 - 232 = -132
        Assert.Equal(rhombusTopY - (occY - padding), posY);
    }

    [Fact]
    public void Zero_padding_aligns_base_at_full_height()
    {
        // If a future occupant subclass drops its padding entirely, the
        // base is the bottom of the Control rect. Formula must still
        // hold : Position.Y = rhombusTopY - occupantSizeY.
        var (_, posY) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketSizeX: 168f,
            rhombusTopY: 50f,
            occupantSizeX: 120f,
            occupantSizeY: 200f,
            occupantPaddingPx: 0f);
        Assert.Equal(50f - 200f, posY);
    }
}
