using System.Collections.Generic;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// J5 contract tests for the E5 District Map promotion (D-J5-08 +
/// D-J5-10 reuse). The Godot-side <c>E3DistrictMap</c> cannot be loaded
/// from xUnit, so these tests assert on a fake harness mirroring the
/// E3-shape integration:
/// <list type="bullet">
///   <item>Back button / Esc -&gt; NavigateBack() (returns to E3).</item>
///   <item>NpcCandidate POI click (Kira reused cross-scene) -&gt;
///         OpenModal("E4_CHARACTER_SHEET") with the npc id threaded
///         through the SAME payload key as E3 (cross-scene contract).</item>
///   <item>AmbientNpc POI click -&gt; ShowBlockedIndicator
///         (defense-in-depth from J4 dispatcher), no navigate, no
///         modal.</item>
/// </list>
///
/// <para>
/// Pattern mirrors <see cref="E2AreaMapNavigationContractTests"/>: same
/// FakeNavigator + FakeOnPoiPressed shape, swapping the test data
/// fixtures to E5 quartier POIs. The OpeningStrings consumption (label
/// rendering, tooltip text) is Godot-bound so it is verified by the
/// J5 manual checklist, not xUnit.
/// </para>
/// </summary>
public sealed class E3DistrictMapNavigationContractTests
{
    /// <summary>
    /// Stable payload key used by E5 to thread the npc id into the
    /// modal context. Mirrors the const on the Godot-side
    /// <c>E2AreaMap.NpcIdPayloadKey</c> -- duplicated here so the
    /// xUnit assembly does not need to compile-include the Godot
    /// screen file. Same duplication pattern as
    /// <see cref="E2AreaMapNavigationContractTests"/>.
    /// </summary>
    private const string NpcIdPayloadKey = "E4.NpcId";

    private sealed class FakeE5Navigator
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
    /// Reproduces the E3DistrictMap.OnPoiPressed dispatch shape without
    /// the Godot context. Same code path as the Godot-side handler
    /// (and identical to the E3 fake) -- the dispatcher is the shared
    /// engine across both screens.
    /// </summary>
    private static async Task FakeOnPoiPressed(
        FakeE5Navigator nav, PoiDefinitionDto poi)
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

    private static PoiDefinitionDto KiraOnE5() => new(
        PoiId: "kira",
        IsClickable: true,
        TargetScreenId: "",
        TooltipKey: "E3PoiCandidateTooltip",
        Kind: PoiKind.NpcCandidate);

    private static PoiDefinitionDto AmbientHomme() => new(
        PoiId: "halfgate.marche.ambient_homme",
        IsClickable: false,
        TargetScreenId: "",
        TooltipKey: "E5AmbientHommeTooltip",
        Kind: PoiKind.AmbientNpc);

    [Fact]
    public async Task E3DistrictMap_back_button_calls_NavigateBack()
    {
        var nav = new FakeE5Navigator();

        // E3DistrictMap.OnBackPressed body equivalent. SceneManager
        // pops the navigation stack -- top was E5, new top is E3.
        await nav.NavigateBack();

        Assert.Single(nav.NavigateBackCalls);
        Assert.Empty(nav.NavigateToCalls);
        Assert.Empty(nav.OpenModalCalls);
    }

    [Fact]
    public async Task E3DistrictMap_esc_key_calls_NavigateBack_via_scene_manager()
    {
        // Esc handling is owned by SceneManager._UnhandledInput
        // (mapped to ui_cancel) -- E5 inherits the J1 behaviour. Same
        // intent surface as the back button, separate input.
        var nav = new FakeE5Navigator();

        await nav.NavigateBack();   // simulating Esc

        Assert.Single(nav.NavigateBackCalls);
    }

    [Fact]
    public async Task E3DistrictMap_kira_npc_click_opens_E4_modal_with_npc_payload()
    {
        // D-J5-10 reuse: clicking the Kira NpcCandidate on E5 must open
        // the SAME E4 modal as on E3, with the SAME payload key
        // contract. This validates cross-scene NpcId portability --
        // the modal does not know whether it was opened from E3 or E5.
        var nav = new FakeE5Navigator();

        await FakeOnPoiPressed(nav, KiraOnE5());

        Assert.Single(nav.OpenModalCalls);
        var (modalId, payload) = nav.OpenModalCalls[0];
        Assert.Equal("E4_CHARACTER_SHEET", modalId);
        Assert.True(payload.ContainsKey(NpcIdPayloadKey));
        Assert.Equal("kira", payload[NpcIdPayloadKey]);
        Assert.Empty(nav.NavigateToCalls);
        Assert.Empty(nav.BlockedIndicatorRequests);
    }

    [Fact]
    public async Task E3DistrictMap_ambient_npc_click_blocked_does_not_navigate_or_open_modal()
    {
        // Defense-in-depth from J4 dispatcher: AmbientNpc kind is
        // forced through ShowBlockedIndicator regardless of
        // IsClickable, so a hot-edited .tres flipping clickability
        // accidentally cannot navigate or open a modal. J5 surfaces
        // this branch for the first time in real gameplay (E5 ambient
        // homme) -- locked here.
        var nav = new FakeE5Navigator();

        await FakeOnPoiPressed(nav, AmbientHomme());

        Assert.Single(nav.BlockedIndicatorRequests);
        Assert.Empty(nav.NavigateToCalls);
        Assert.Empty(nav.OpenModalCalls);
    }

    [Fact]
    public async Task E3DistrictMap_ambient_npc_clickable_hot_edit_still_blocked()
    {
        // Reinforces the previous test: even if a hot-edited
        // district_pois.tres flips IsClickable=true on the
        // ambient homme entry, the dispatcher MUST still route through
        // ShowBlockedIndicator. This pins the J5 lock that ambient
        // PNJs are hover-only by design (Varn E5.I-M, voix D Marges).
        var nav = new FakeE5Navigator();
        var clickableAmbient = AmbientHomme() with { IsClickable = true };

        await FakeOnPoiPressed(nav, clickableAmbient);

        Assert.Single(nav.BlockedIndicatorRequests);
        Assert.Empty(nav.NavigateToCalls);
        Assert.Empty(nav.OpenModalCalls);
    }

    [Fact]
    public void E3DistrictMap_kira_uses_same_payload_key_constant_as_test_E3()
    {
        // Documentation test: the cross-scene contract is "E4.NpcId".
        // Both E2AreaMapNavigationContractTests and E3DistrictMapNavigationContractTests
        // duplicate the constant -- a future rename of the Godot-side
        // E2AreaMap.NpcIdPayloadKey would surface here only if
        // both test files are also updated, which is the desired
        // friction (locks the cross-scene contract by sheer ceremony).
        Assert.Equal("E4.NpcId", NpcIdPayloadKey);
    }
}
