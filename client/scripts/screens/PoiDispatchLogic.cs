namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for E2 World Map POI clicks. Maps a
/// <see cref="PoiDefinitionDto"/> to the intent
/// <see cref="E2WorldMap"/> should fulfil:
/// <list type="bullet">
///   <item><see cref="PoiDispatchOutcome.NavigateToScreen"/> — go to
///         <see cref="PoiDispatchResult.TargetScreenId"/>.</item>
///   <item><see cref="PoiDispatchOutcome.ShowBlockedIndicator"/> — flash
///         the bottom-center label with the
///         <see cref="PoiDispatchResult.IndicatorTextKey"/> key from
///         <see cref="Wayfinders.Client.Data.OpeningStrings"/>.</item>
/// </list>
///
/// <para>
/// <b>Why a separate helper, not a method on E2WorldMap.</b> Same
/// pattern as <see cref="ContinueGatingLogic"/> at J2: the rule is one
/// branch, but the rule is also <i>contractual</i>. Encoding it once in
/// a Godot-free class lets xUnit pin the dispatch table — when the
/// 1-cité MVP gate (Varn §6.D6.10) eventually relaxes (J5+), the test
/// suite will surface every consumer that depended on the strict gate.
/// </para>
///
/// <para>
/// <b>Defensive defaults.</b> A POI marked clickable with an empty
/// <see cref="PoiDefinitionDto.TargetScreenId"/> falls back to
/// <see cref="PoiDispatchOutcome.ShowBlockedIndicator"/>. In practice
/// the world_map_pois.tres should never ship that combination ; the
/// fallback exists so a bad <c>.tres</c> hot-edit doesn't silently
/// navigate to a registered-but-empty screen id.
/// </para>
/// </summary>
public static class PoiDispatchLogic
{
    /// <summary>
    /// <see cref="OpeningStrings"/> key consumed by E2 to populate the
    /// blocked-flash bottom-center label. Stable so callers don't
    /// duplicate the literal.
    /// </summary>
    public const string BlockedIndicatorTextKey = "E2PoiBlockedIndicator";

    public static PoiDispatchResult Dispatch(PoiDefinitionDto poi)
    {
        if (poi.IsClickable && !string.IsNullOrEmpty(poi.TargetScreenId))
        {
            return new PoiDispatchResult(
                Outcome: PoiDispatchOutcome.NavigateToScreen,
                TargetScreenId: poi.TargetScreenId,
                IndicatorTextKey: null);
        }

        return new PoiDispatchResult(
            Outcome: PoiDispatchOutcome.ShowBlockedIndicator,
            TargetScreenId: null,
            IndicatorTextKey: BlockedIndicatorTextKey);
    }
}

/// <summary>
/// Two-state outcome for <see cref="PoiDispatchLogic.Dispatch"/>.
/// </summary>
public enum PoiDispatchOutcome
{
    /// <summary>Caller should ask SceneManager to NavigateTo the target screen.</summary>
    NavigateToScreen,

    /// <summary>Caller should flash the blocked-indicator label using the indicator key.</summary>
    ShowBlockedIndicator,
}

/// <summary>
/// Output of <see cref="PoiDispatchLogic.Dispatch"/>. Either the target
/// screen id is set (navigate path) or the indicator text key is set
/// (blocked-flash path) — never both, never neither.
/// </summary>
public readonly record struct PoiDispatchResult(
    PoiDispatchOutcome Outcome,
    string? TargetScreenId,
    string? IndicatorTextKey);
