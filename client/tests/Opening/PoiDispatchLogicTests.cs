using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="PoiDispatchLogic"/>. Locks the J3
/// behaviour of the E2 World Map POI dispatcher: clickable POI -&gt;
/// NavigateToScreen ; non-clickable POI -&gt; ShowBlockedIndicator.
/// Defensive fallback when a clickable POI has an empty target id.
///
/// <para>
/// Why these tests matter: the Halfgate-only gate is the v1 MVP
/// constraint (Varn §6.D6.10 lock 1-cité). When that gate eventually
/// relaxes (J5+ when more cities ship), every consumer that depended on
/// the strict gate will surface here as a test diff. Pinning the dispatch
/// table now is cheap insurance.
/// </para>
/// </summary>
public sealed class PoiDispatchLogicTests
{
    [Fact]
    public void Dispatch_clickable_poi_returns_navigate_outcome()
    {
        var poi = new PoiDefinitionDto(
            PoiId: "halfgate",
            IsClickable: true,
            TargetScreenId: "E3_CITY_HALFGATE",
            TooltipKey: "E2PoiHalfgateTooltip");

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.NavigateToScreen, result.Outcome);
    }

    [Fact]
    public void Dispatch_clickable_poi_returns_correct_target_screen_id()
    {
        var poi = new PoiDefinitionDto(
            PoiId: "halfgate",
            IsClickable: true,
            TargetScreenId: "E3_CITY_HALFGATE",
            TooltipKey: "E2PoiHalfgateTooltip");

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal("E3_CITY_HALFGATE", result.TargetScreenId);
        Assert.Null(result.IndicatorTextKey);
    }

    [Fact]
    public void Dispatch_blocked_poi_returns_show_indicator_outcome()
    {
        var poi = new PoiDefinitionDto(
            PoiId: "veylant",
            IsClickable: false,
            TargetScreenId: "",
            TooltipKey: "");

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.ShowBlockedIndicator, result.Outcome);
    }

    [Fact]
    public void Dispatch_blocked_poi_returns_correct_indicator_key()
    {
        var poi = new PoiDefinitionDto(
            PoiId: "caer-mithren",
            IsClickable: false,
            TargetScreenId: "",
            TooltipKey: "");

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchLogic.BlockedIndicatorTextKey, result.IndicatorTextKey);
        Assert.Equal("E2PoiBlockedIndicator", result.IndicatorTextKey);
        Assert.Null(result.TargetScreenId);
    }

    [Fact]
    public void Dispatch_clickable_poi_with_empty_target_falls_back_to_blocked()
    {
        // Defensive: a hot-edited .tres that flagged a POI clickable but
        // forgot to set the target screen id should NOT silently navigate
        // to the empty string. Falls back to the blocked-indicator path.
        var poi = new PoiDefinitionDto(
            PoiId: "broken",
            IsClickable: true,
            TargetScreenId: "",
            TooltipKey: "");

        var result = PoiDispatchLogic.Dispatch(poi);

        Assert.Equal(PoiDispatchOutcome.ShowBlockedIndicator, result.Outcome);
        Assert.Null(result.TargetScreenId);
        Assert.Equal(PoiDispatchLogic.BlockedIndicatorTextKey, result.IndicatorTextKey);
    }

    [Fact]
    public void Dispatch_is_pure_no_state_carry_over_between_calls()
    {
        // Sanity: the dispatcher is a static helper with no fields ;
        // calling it 100 times in sequence with alternating inputs must
        // always return the per-input result, never leak state.
        var clickable = new PoiDefinitionDto(
            PoiId: "halfgate", IsClickable: true,
            TargetScreenId: "E3_CITY_HALFGATE", TooltipKey: "");
        var blocked = new PoiDefinitionDto(
            PoiId: "veylant", IsClickable: false,
            TargetScreenId: "", TooltipKey: "");

        for (int i = 0; i < 50; i++)
        {
            var c = PoiDispatchLogic.Dispatch(clickable);
            var b = PoiDispatchLogic.Dispatch(blocked);
            Assert.Equal(PoiDispatchOutcome.NavigateToScreen, c.Outcome);
            Assert.Equal(PoiDispatchOutcome.ShowBlockedIndicator, b.Outcome);
        }
    }
}
