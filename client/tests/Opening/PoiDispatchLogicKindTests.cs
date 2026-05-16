using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// J4 extension of <see cref="PoiDispatchLogicTests"/>: pins the
/// dispatch table for the new <see cref="PoiKind"/> branches added in
/// J4 (D-J4-02). City branch behaviour is locked by the J3 tests
/// already ; here we cover District, NpcCandidate, NpcCompanion,
/// KeyBuilding, AmbientNpc, Interactable plus the defensive fallback
/// when a clickable District has an empty target id.
///
/// <para>
/// Why it matters: the cité (E3) gets four POI types cohabiting on the
/// same layer (Varn §4.1) ; the dispatcher decides per click whether
/// to navigate, open a modal, or flash a blocked indicator. Pinning
/// the table here means "Halfgate POI behaves like Halfgate" survives
/// every future refactor of the helper.
/// </para>
///
/// <para>
/// Backward-compat lock: the J3 <c>world_map_pois.tres</c> stores POI
/// without a Kind field ; Godot defaults to <see cref="PoiKind.City"/>
/// via the <c>[Export]</c> on <c>PoiDefinition</c>, and the DTO
/// constructor defaults to the same value. The J3 tests
/// (<see cref="PoiDispatchLogicTests"/>) keep passing because the
/// City branch is preserved verbatim. The
/// <c>City_default_kind_navigates_when_clickable</c> test below
/// re-asserts that lock from the J4 surface for belt-and-braces.
/// </para>
/// </summary>
public sealed class PoiDispatchLogicKindTests
{
    [Fact]
    public void City_default_kind_navigates_when_clickable()
    {
        // Backward compat: a DTO built without specifying Kind must
        // default to PoiKind.City and route exactly like the J3 path.
        // This is the regression guard for the world_map_pois.tres
        // J3 entries that never authored Kind.
        var poi = new PoiDefinitionDto(
            PoiId: "halfgate",
            IsClickable: true,
            TargetScreenId: "E2_AREA",
            TooltipKey: "E2PoiHalfgateTooltip");

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.NavigateToScreen, result.Outcome);
        Assert.Equal("E2_AREA", result.TargetScreenId);
        Assert.Null(result.ModalId);
        Assert.Null(result.NpcId);
    }

    [Fact]
    public void District_clickable_navigates_to_target_screen()
    {
        var poi = new PoiDefinitionDto(
            PoiId: "halfgate.marche",
            IsClickable: true,
            TargetScreenId: "E3_DISTRICT",
            TooltipKey: "E3PoiDistrictTooltipTemplate",
            Kind: PoiKind.District);

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.NavigateToScreen, result.Outcome);
        Assert.Equal("E3_DISTRICT", result.TargetScreenId);
        Assert.Null(result.IndicatorTextKey);
        Assert.Null(result.ModalId);
    }

    [Fact]
    public void District_clickable_with_empty_target_falls_back_to_blocked()
    {
        // Defensive: same J3 fallback shape, but for the District kind.
        // A hot-edited area_pois.tres with empty TargetScreenId
        // must NOT silently navigate to "" -- flash the blocked indicator.
        var poi = new PoiDefinitionDto(
            PoiId: "halfgate.broken",
            IsClickable: true,
            TargetScreenId: "",
            TooltipKey: "",
            Kind: PoiKind.District);

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.ShowBlockedIndicator, result.Outcome);
        Assert.Null(result.TargetScreenId);
        Assert.Equal(PoiDispatchLogic.BlockedIndicatorTextKey, result.IndicatorTextKey);
    }

    [Fact]
    public void NpcCandidate_clickable_opens_modal_with_npc_id()
    {
        var poi = new PoiDefinitionDto(
            PoiId: "kira",
            IsClickable: true,
            TargetScreenId: "",
            TooltipKey: "E3PoiCandidateTooltip",
            Kind: PoiKind.NpcCandidate);

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.OpenModal, result.Outcome);
        Assert.Equal(PoiDispatchLogic.CharacterSheetModalId, result.ModalId);
        Assert.Equal("E4_CHARACTER_SHEET", result.ModalId);
        Assert.Equal("kira", result.NpcId);
        Assert.Null(result.TargetScreenId);
        Assert.Null(result.IndicatorTextKey);
    }

    [Fact]
    public void NpcCompanion_clickable_opens_modal_with_npc_id()
    {
        // J4 ships zero NpcCompanion sprites in the .tres (party vide
        // post-E1), but the dispatcher branch is reachable so J5+ can
        // wire compagnons without re-touching the helper. Locks the
        // contract now.
        var poi = new PoiDefinitionDto(
            PoiId: "thane",
            IsClickable: true,
            TargetScreenId: "",
            TooltipKey: "E3PoiCompanionTooltip",
            Kind: PoiKind.NpcCompanion);

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.OpenModal, result.Outcome);
        Assert.Equal("E4_CHARACTER_SHEET", result.ModalId);
        Assert.Equal("thane", result.NpcId);
    }

    [Fact]
    public void KeyBuilding_clickable_shows_blocked_with_keybuilding_key()
    {
        // Varn §3.3.3 + Rune J4 pre-brief D-J4-09: the QG (and any
        // other KeyBuilding) is hover-only in J4 ; click flashes the
        // E3KeyBuildingBlockedIndicator stub. Real opening of the
        // contracts panel for QG, real interior for other KeyBuildings,
        // both land post-MVP via the P2 layer "bâtiment".
        var poi = new PoiDefinitionDto(
            PoiId: "halfgate.qg",
            IsClickable: true,
            TargetScreenId: "",
            TooltipKey: "E3PoiKeyBuildingHqTooltip",
            Kind: PoiKind.KeyBuilding);

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.ShowBlockedIndicator, result.Outcome);
        Assert.Equal(PoiDispatchLogic.KeyBuildingBlockedIndicatorTextKey, result.IndicatorTextKey);
        Assert.Equal("E3KeyBuildingBlockedIndicator", result.IndicatorTextKey);
        Assert.Null(result.TargetScreenId);
        Assert.Null(result.ModalId);
    }

    [Fact]
    public void AmbientNpc_clickable_forced_to_blocked_defense_in_depth()
    {
        // Defense in depth: even if a hot-edited .tres flips
        // IsClickable on an ambient PNJ, the dispatcher refuses to
        // navigate or open a modal -- ambient PNJs are hover-only by
        // design (Varn E5.I-M, voix D Marges). J4-J5 forced blocked.
        var poi = new PoiDefinitionDto(
            PoiId: "ambient_001",
            IsClickable: true,                 // intentionally true
            TargetScreenId: "E4_CHARACTER_SHEET",
            TooltipKey: "E5.I",
            Kind: PoiKind.AmbientNpc);

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.ShowBlockedIndicator, result.Outcome);
        Assert.Null(result.TargetScreenId);
        Assert.Null(result.ModalId);
    }

    [Fact]
    public void Interactable_clickable_forced_to_blocked_defense_in_depth()
    {
        // Mirror of the AmbientNpc defense: interactables (panneau
        // d'affichage, fontaine, autel) are hover-only in J4-J5.
        var poi = new PoiDefinitionDto(
            PoiId: "halfgate.fontaine",
            IsClickable: true,
            TargetScreenId: "anywhere",
            TooltipKey: "E5.O",
            Kind: PoiKind.Interactable);

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.ShowBlockedIndicator, result.Outcome);
        Assert.Null(result.TargetScreenId);
        Assert.Null(result.ModalId);
    }
}
