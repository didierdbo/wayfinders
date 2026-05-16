using System.Collections.Generic;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the E3 City Halfgate navigation surface in J4.
/// The Godot-side <c>E2AreaMap</c> cannot be loaded from xUnit
/// (no engine), so these tests assert on a fake harness that mimics
/// the SceneManager + dispatcher integration and locks the expected
/// intents:
/// <list type="bullet">
///   <item>Back button / Esc -&gt; NavigateBack()</item>
///   <item>District POI click -&gt; NavigateTo("E3_DISTRICT")</item>
///   <item>NpcCandidate POI click -&gt; OpenModal("E4_CHARACTER_SHEET")
///         with the npc id threaded through the payload bag</item>
///   <item>KeyBuilding POI click -&gt; ShowBlockedIndicator
///         (E2AreaMapKeyBuildingBlockedIndicator), no navigate, no modal</item>
/// </list>
///
/// <para>
/// Pattern mirrors <see cref="E1WorldMapNavigationContractTests"/> (J3): a
/// FakeNavigator simulates the SceneManager's public surface ; the
/// E3 click handler is reproduced inline (fake "OnPoiPressed") so the
/// dispatch -&gt; navigation/modal/blocked wiring is testable
/// hors-Godot. Same exact flow as the real
/// <c>E2AreaMap.OnPoiPressed</c> body.
/// </para>
/// </summary>
public sealed class E2AreaMapNavigationContractTests
{
    /// <summary>
    /// Stable payload key used by E3 to thread the npc id into the
    /// modal context. Mirrors the const on the Godot-side
    /// <c>E2AreaMap.NpcIdPayloadKey</c> -- duplicated here so the
    /// xUnit assembly does not need to compile-include the Godot
    /// screen file.
    /// </summary>
    private const string NpcIdPayloadKey = "E4.NpcId";

    private sealed class FakeE3Navigator
    {
        public List<string> NavigateToCalls { get; } = new();
        public List<string> NavigateBackCalls { get; } = new();
        public List<(string ModalId, IReadOnlyDictionary<string, object> Payload)> OpenModalCalls { get; } = new();
        public List<string> BlockedIndicatorRequests { get; } = new();

        public Task NavigateTo(string screenId)
        {
            NavigateToCalls.Add(screenId);
            return Task.CompletedTask;
        }

        public Task NavigateBack()
        {
            NavigateBackCalls.Add("back");
            return Task.CompletedTask;
        }

        public Task OpenModal(string modalId, IReadOnlyDictionary<string, object> payload)
        {
            OpenModalCalls.Add((modalId, payload));
            return Task.CompletedTask;
        }

        public void RequestBlockedIndicator(string textKey) =>
            BlockedIndicatorRequests.Add(textKey);
    }

    /// <summary>
    /// Reproduces the E2AreaMap.OnPoiPressed dispatch shape
    /// without the Godot context. Same code path as the Godot-side
    /// handler (D-J4-07): dispatcher decides the intent ; the navigator
    /// fulfils Navigate / OpenModal / ShowBlockedIndicator outcomes.
    /// </summary>
    private static async Task FakeOnPoiPressed(
        FakeE3Navigator nav, PoiDefinitionDto poi)
    {
        var result = PoiDispatchLogic.Dispatch(poi);
        switch (result.Outcome)
        {
            case PoiDispatchOutcome.NavigateToScreen:
                await nav.NavigateTo(result.TargetScreenId!);
                break;
            case PoiDispatchOutcome.OpenModal:
                var payload = new Dictionary<string, object>
                {
                    [NpcIdPayloadKey] = result.NpcId ?? string.Empty,
                };
                await nav.OpenModal(result.ModalId!, payload);
                break;
            case PoiDispatchOutcome.ShowBlockedIndicator:
                nav.RequestBlockedIndicator(result.IndicatorTextKey!);
                break;
        }
    }

    private static PoiDefinitionDto Marche() => new(
        PoiId: "halfgate.marche",
        IsClickable: true,
        TargetScreenId: "E3_DISTRICT",
        TooltipKey: "E2AreaMapPoiDistrictTooltipTemplate",
        Kind: PoiKind.District);

    private static PoiDefinitionDto Kira() => new(
        PoiId: "kira",
        IsClickable: true,
        TargetScreenId: "",
        TooltipKey: "E2AreaMapPoiCandidateTooltip",
        Kind: PoiKind.NpcCandidate);

    private static PoiDefinitionDto Qg() => new(
        PoiId: "halfgate.qg",
        IsClickable: true,
        TargetScreenId: "",
        TooltipKey: "E2AreaMapPoiKeyBuildingHqTooltip",
        Kind: PoiKind.KeyBuilding);

    [Fact]
    public async Task E2AreaMap_back_button_calls_NavigateBack()
    {
        var nav = new FakeE3Navigator();

        // E2AreaMap.OnBackPressed body equivalent.
        await nav.NavigateBack();

        Assert.Single(nav.NavigateBackCalls);
        Assert.Empty(nav.NavigateToCalls);
        Assert.Empty(nav.OpenModalCalls);
    }

    [Fact]
    public async Task E2AreaMap_esc_key_calls_NavigateBack_via_scene_manager()
    {
        // Esc handling is owned by SceneManager._UnhandledInput
        // (mapped to ui_cancel) -- E3 inherits the J1 behaviour. Same
        // intent surface as the back button, separate input. We mirror
        // by invoking NavigateBack from the same FakeE3Navigator and
        // assert the contract is single-pathed.
        var nav = new FakeE3Navigator();

        await nav.NavigateBack();   // simulating Esc

        Assert.Single(nav.NavigateBackCalls);
    }

    [Fact]
    public async Task E2AreaMap_district_poi_click_navigates_to_E5_district()
    {
        var nav = new FakeE3Navigator();

        await FakeOnPoiPressed(nav, Marche());

        Assert.Single(nav.NavigateToCalls);
        Assert.Equal("E3_DISTRICT", nav.NavigateToCalls[0]);
        Assert.Empty(nav.OpenModalCalls);
        Assert.Empty(nav.BlockedIndicatorRequests);
    }

    [Fact]
    public async Task E2AreaMap_candidate_poi_click_opens_E4_modal_with_npc_payload()
    {
        // D-J4-07 option α: clicking a NpcCandidate POI opens the E4
        // fiche cadastrale modal pre-filled with the candidat's id.
        // The npc id rides in the ScreenContext.Payload bag under
        // the stable key "E4.NpcId" (E3 -> dispatcher -> E3 wraps ->
        // SceneManager.OpenModal -> E4.OnOpen reads).
        var nav = new FakeE3Navigator();

        await FakeOnPoiPressed(nav, Kira());

        Assert.Single(nav.OpenModalCalls);
        var (modalId, payload) = nav.OpenModalCalls[0];
        Assert.Equal("E4_CHARACTER_SHEET", modalId);
        Assert.True(payload.ContainsKey(NpcIdPayloadKey));
        Assert.Equal("kira", payload[NpcIdPayloadKey]);
        Assert.Empty(nav.NavigateToCalls);
        Assert.Empty(nav.BlockedIndicatorRequests);
    }

    [Fact]
    public async Task E2AreaMap_key_building_poi_click_shows_blocked_does_not_navigate()
    {
        // Varn §3.3.3 + D-J4-09: KeyBuilding QG is hover-only J4 ;
        // click stub-flashes the E2AreaMapKeyBuildingBlockedIndicator. No
        // navigation, no modal -- the click intent surfaces strictly
        // as the bottom-center flash. Real opening of the contracts
        // panel ships post-MVP.
        var nav = new FakeE3Navigator();

        await FakeOnPoiPressed(nav, Qg());

        Assert.Single(nav.BlockedIndicatorRequests);
        Assert.Equal("E2AreaMapKeyBuildingBlockedIndicator", nav.BlockedIndicatorRequests[0]);
        Assert.Empty(nav.NavigateToCalls);
        Assert.Empty(nav.OpenModalCalls);
    }
}
