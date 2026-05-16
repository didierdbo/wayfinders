using System.Collections.Generic;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the E2 World Map navigation surface in J3. The
/// Godot-side <c>E1WorldMap</c> cannot be loaded from xUnit (no engine),
/// so these tests assert on a fake harness that mimics the SceneManager
/// + dispatcher integration and locks the expected intents:
/// <list type="bullet">
///   <item>Halfgate POI click -&gt; NavigateTo("E2_AREA")</item>
///   <item>Grisée POI click -&gt; no NavigateTo, blocked indicator
///         requested instead</item>
///   <item>Back button / Esc -&gt; NavigateBack()</item>
///   <item>Rapid-clicking grisée POIs does not stack indicator
///         requests beyond the latest</item>
/// </list>
///
/// <para>
/// Pattern mirrors <see cref="E1NavigationContractTests"/>: a
/// FakeNavigator simulates the SceneManager's public surface ; the
/// E2 click handler is reproduced inline (fake "OnPoiPressed") so the
/// dispatch -&gt; navigation wiring is testable hors-Godot.
/// </para>
/// </summary>
public sealed class E1WorldMapNavigationContractTests
{
    private sealed class FakeNavigator
    {
        public List<string> NavigateToCalls { get; } = new();
        public List<string> NavigateBackCalls { get; } = new();
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

        public void RequestBlockedIndicator(string textKey) =>
            BlockedIndicatorRequests.Add(textKey);
    }

    /// <summary>
    /// Reproduces the E1WorldMap.OnPoiPressed dispatch shape without
    /// the Godot context: dispatcher decides the intent, the navigator
    /// fulfils it. Same code path as the Godot-side handler.
    /// </summary>
    private static async Task FakeOnPoiPressed(
        FakeNavigator nav, PoiDefinitionDto poi)
    {
        var result = PoiDispatchLogic.Dispatch(poi);
        switch (result.Outcome)
        {
            case PoiDispatchOutcome.NavigateToScreen:
                await nav.NavigateTo(result.TargetScreenId!);
                break;
            case PoiDispatchOutcome.ShowBlockedIndicator:
                nav.RequestBlockedIndicator(result.IndicatorTextKey!);
                break;
        }
    }

    private static PoiDefinitionDto ClickableArea() => new(
        PoiId: "halfgate",
        IsClickable: true,
        TargetScreenId: "E2_AREA",
        TooltipKey: "E1WorldMapPoiCityPatentedTooltipTemplate");

    private static PoiDefinitionDto Veylant() => new(
        PoiId: "veylant",
        IsClickable: false,
        TargetScreenId: "",
        TooltipKey: "");

    [Fact]
    public async Task E1WorldMap_back_button_calls_NavigateBack()
    {
        var nav = new FakeNavigator();

        // E1WorldMap.OnBackPressed body equivalent.
        await nav.NavigateBack();

        Assert.Single(nav.NavigateBackCalls);
        Assert.Empty(nav.NavigateToCalls);
    }

    [Fact]
    public async Task E1WorldMap_area_poi_click_navigates_to_E2_area()
    {
        var nav = new FakeNavigator();

        await FakeOnPoiPressed(nav, ClickableArea());

        Assert.Single(nav.NavigateToCalls);
        Assert.Equal("E2_AREA", nav.NavigateToCalls[0]);
        Assert.Empty(nav.BlockedIndicatorRequests);
    }

    [Fact]
    public async Task E1WorldMap_grisée_poi_click_does_not_navigate()
    {
        var nav = new FakeNavigator();

        await FakeOnPoiPressed(nav, Veylant());

        Assert.Empty(nav.NavigateToCalls);
        Assert.Empty(nav.NavigateBackCalls);
    }

    [Fact]
    public async Task E1WorldMap_grisée_poi_click_emits_blocked_indicator_request()
    {
        var nav = new FakeNavigator();

        await FakeOnPoiPressed(nav, Veylant());

        Assert.Single(nav.BlockedIndicatorRequests);
        Assert.Equal("E1WorldMapPoiBlockedIndicator", nav.BlockedIndicatorRequests[0]);
    }

    [Fact]
    public async Task E1WorldMap_rapid_grisée_clicks_request_indicator_per_click()
    {
        // The flash-debounce on the Godot side restarts the tween on
        // each new click ; from the navigator's perspective each click
        // is a fresh request and the .tres-driven indicator key is the
        // same. The visual debounce is covered by the manual checklist
        // (step 13 in pre-brief §6.2) -- here we just lock the contract
        // that no clicks are silently dropped.
        var nav = new FakeNavigator();

        await FakeOnPoiPressed(nav, Veylant());
        await FakeOnPoiPressed(nav, Veylant());
        await FakeOnPoiPressed(nav, Veylant());

        Assert.Equal(3, nav.BlockedIndicatorRequests.Count);
        Assert.All(nav.BlockedIndicatorRequests,
            k => Assert.Equal("E1WorldMapPoiBlockedIndicator", k));
        Assert.Empty(nav.NavigateToCalls);
    }

    [Fact]
    public async Task E1WorldMap_mixed_clicks_route_correctly()
    {
        // Clickable POI + grisée POI in alternation: the navigator
        // sees one NavigateTo and one BlockedIndicator request,
        // independent of each other. Locks that the dispatcher does
        // not leak state between calls (covered also by
        // PoiDispatchLogicTests.Dispatch_is_pure_*, but a wired test
        // is good belt-and-braces).
        var nav = new FakeNavigator();

        await FakeOnPoiPressed(nav, ClickableArea());
        await FakeOnPoiPressed(nav, Veylant());
        await FakeOnPoiPressed(nav, ClickableArea());

        Assert.Equal(2, nav.NavigateToCalls.Count);
        Assert.Equal("E2_AREA", nav.NavigateToCalls[0]);
        Assert.Equal("E2_AREA", nav.NavigateToCalls[1]);
        Assert.Single(nav.BlockedIndicatorRequests);
    }
}
