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
/// The 7956c94 follow-up used <c>occupantSize.Y - paddingPx</c> as the
/// base offset, which still included label-band + bottom-padding slack
/// when the Control's actual Size diverged from its cmin -- the perso
/// kept floating. The current contract reads the visible iso body
/// height (D + H = <see cref="IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight"/>
/// = 220 in compact, <see cref="IsoSocketCharacterPlacementLogic.DefaultIsoBodyHeight"/>
/// = 280 in default) directly, decoupling the placement from any
/// Size / padding drift. These pins catch future drift in either the
/// helper math or the IsoBodyHeight constants.
/// </para>
/// </summary>
public sealed class IsoSocketCharacterPlacementLogicTests
{
    private const float OccupantPadding = IsoSocketCharacterPlacementLogic.OccupantPaddingPx;
    private const float CompactIsoBody = IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight;

    [Fact]
    public void Base_of_occupant_lands_on_rhombus_top_apex()
    {
        // Socket Size.X = 168, rhombus drawn with top apex at y = 4
        // (PaddingPx = 4 + (availH - h) / 2 with availH = h).
        const float socketX = 168f;
        const float rhombusTopY = 4f;
        // Compact occupant cmin width = 120, padding = 12, iso body = 220.
        const float occX = 120f;

        var (_, posY) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketX, rhombusTopY, occX, CompactIsoBody, OccupantPadding);

        // Base in occupant-local coords = padding + isoBodyHeight = 232.
        // Position.Y + 232 must equal rhombusTopY = 4.
        // => Position.Y = 4 - 232 = -228.
        Assert.Equal(-228f, posY);
        // Verify the projection lands exactly on the apex.
        var globalBaseY = posY + (OccupantPadding + CompactIsoBody);
        Assert.Equal(rhombusTopY, globalBaseY);
    }

    [Fact]
    public void Occupant_is_centred_horizontally_on_socket()
    {
        const float socketX = 200f;
        const float occX = 120f;

        var (posX, _) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketX, rhombusTopY: 10f, occX, isoBodyHeight: CompactIsoBody, OccupantPadding);

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
            isoBodyHeight: CompactIsoBody,
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
            isoBodyHeight: CompactIsoBody,
            occupantPaddingPx: OccupantPadding);
        Assert.Equal(-25f, posX);
    }

    [Fact]
    public void Iso_body_height_drives_base_offset_not_total_control_height()
    {
        // Lock the contract : the base offset in occupant-local coords
        // is (paddingPx + isoBodyHeight), NOT (occupantSize.Y - paddingPx).
        // This is THE fix for the 2nd "perso flotte" bug : even when the
        // Control's actual Size.Y is larger than cmin (parent layout
        // stretching, padding drift, future label band addition), the
        // placement stays anchored on the iso body itself.
        const float rhombusTopY = 100f;
        const float isoBody = 220f;
        const float padding = 12f;

        var (_, posY) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketSizeX: 168f,
            rhombusTopY: rhombusTopY,
            occupantSizeX: 120f,
            isoBodyHeight: isoBody,
            occupantPaddingPx: padding);

        // Expected: rhombusTopY - (padding + isoBody) = 100 - 232 = -132
        Assert.Equal(rhombusTopY - (padding + isoBody), posY);
    }

    [Fact]
    public void Zero_padding_collapses_base_to_iso_body_height()
    {
        // If a future occupant subclass drops its top padding entirely,
        // the body's top apex sits at y = 0 in occupant-local and the
        // base sits at y = isoBodyHeight. Formula must still hold :
        // Position.Y = rhombusTopY - isoBodyHeight.
        var (_, posY) = IsoSocketCharacterPlacementLogic.ComputeOccupantPosition(
            socketSizeX: 168f,
            rhombusTopY: 50f,
            occupantSizeX: 120f,
            isoBodyHeight: 200f,
            occupantPaddingPx: 0f);
        Assert.Equal(50f - 200f, posY);
    }

    // -------------------------------------------------------------------
    // IsoBodyHeight constant pins -- guard the "D + H" source of truth.
    // -------------------------------------------------------------------

    [Fact]
    public void Compact_iso_body_height_matches_compact_cmin_minus_padding()
    {
        // CompactMinSize.Y = 244, PaddingPx = 12, no label band.
        // D + H = availH = 244 - 0 - 2*12 = 220. If the cmin shifts,
        // the iso body height constant has to shift in lock-step or the
        // placement helper drifts.
        const float compactCminY = 244f;
        const float padding = 12f;
        const float labelBand = 0f;
        var derived = compactCminY - labelBand - 2f * padding;
        Assert.Equal(IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight, derived);
        Assert.Equal(IsoSocketCharacterPlacementLogic.CompactIsoBodyHeight, 220f);
    }

    [Fact]
    public void Default_iso_body_height_matches_default_cmin_minus_label_and_padding()
    {
        // DefaultMinSize.Y = 336, PaddingPx = 12, LabelBandPx = 32.
        // D + H = availH = 336 - 32 - 2*12 = 280.
        const float defaultCminY = 336f;
        const float padding = 12f;
        const float labelBand = 32f;
        var derived = defaultCminY - labelBand - 2f * padding;
        Assert.Equal(IsoSocketCharacterPlacementLogic.DefaultIsoBodyHeight, derived);
        Assert.Equal(IsoSocketCharacterPlacementLogic.DefaultIsoBodyHeight, 280f);
    }
}
