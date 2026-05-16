namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for POI clicks across E2 (world) and E3
/// (cité). Maps a <see cref="PoiDefinitionDto"/> to the intent the
/// hosting screen should fulfil:
/// <list type="bullet">
///   <item><see cref="PoiDispatchOutcome.NavigateToScreen"/> — go to
///         <see cref="PoiDispatchResult.TargetScreenId"/>
///         (City -&gt; E3, District -&gt; E5).</item>
///   <item><see cref="PoiDispatchOutcome.OpenModal"/> — open the
///         <see cref="PoiDispatchResult.ModalId"/> overlay with the
///         <see cref="PoiDispatchResult.NpcId"/> threaded through
///         <c>ScreenContext</c> (NpcCompanion / NpcCandidate -&gt; E4
///         fiche cadastrale).</item>
///   <item><see cref="PoiDispatchOutcome.ShowBlockedIndicator"/> — flash
///         the bottom-center label with the
///         <see cref="PoiDispatchResult.IndicatorTextKey"/> key from
///         <see cref="Wayfinders.Client.Data.OpeningStrings"/>
///         (KeyBuilding stub J4, AmbientNpc/Interactable defense in
///         depth, anything malformed in the .tres).</item>
/// </list>
///
/// <para>
/// <b>Why a separate helper, not a method on the screen.</b> Same
/// pattern as <see cref="ContinueGatingLogic"/> at J2: the rule is one
/// switch, but the rule is also <i>contractual</i>. Encoding it once in
/// a Godot-free class lets xUnit pin the dispatch table — when the
/// 1-cité MVP gate (Varn §6.D6.10) eventually relaxes (J5+), the test
/// suite will surface every consumer that depended on the strict gate.
/// </para>
///
/// <para>
/// <b>J4 extension (D-J4-02).</b> Branches by <see cref="PoiKind"/>.
/// J3 verrouillé behaviour preserved: <see cref="PoiKind.City"/> entries
/// keep returning <see cref="PoiDispatchOutcome.NavigateToScreen"/> /
/// <see cref="PoiDispatchOutcome.ShowBlockedIndicator"/> exactly as
/// before — the new branches are additive, the J3 contract tests still
/// pass without modification.
/// </para>
///
/// <para>
/// <b>Defensive defaults.</b> A POI marked clickable with an empty
/// <see cref="PoiDefinitionDto.TargetScreenId"/> falls back to
/// <see cref="PoiDispatchOutcome.ShowBlockedIndicator"/>. A POI of
/// kind <see cref="PoiKind.AmbientNpc"/> or <see cref="PoiKind.Interactable"/>
/// is forced to ShowBlockedIndicator regardless of <c>IsClickable</c> —
/// these types are hover-only in J4 and the defense exists so a hot-edit
/// accident does not silently navigate or open a modal.
/// </para>
/// </summary>
public static class PoiDispatchLogic
{
    /// <summary>
    /// <see cref="OpeningStrings"/> key consumed by E2 to populate the
    /// blocked-flash bottom-center label for City-kind POIs (J3 verrouillé:
    /// "Cadastre suspendu — accès non encore patenté.").
    /// </summary>
    public const string BlockedIndicatorTextKey = "E1WorldMapPoiBlockedIndicator";

    /// <summary>
    /// <see cref="OpeningStrings"/> key consumed by E3 for the KeyBuilding
    /// stub J4 click flash ("Bâtiment non encore patenté.").
    /// </summary>
    public const string KeyBuildingBlockedIndicatorTextKey = "E2AreaMapKeyBuildingBlockedIndicator";

    /// <summary>
    /// <see cref="OpeningStrings"/> key consumed by E3/E5 for the
    /// hover-only-forced types (AmbientNpc, Interactable) when defense
    /// in depth fires. J4 stub reuses the City wording so dev sees the
    /// flash without a missing-key warning ; a dedicated key can be
    /// authored in J5+ when those POI types ship.
    /// </summary>
    public const string GenericBlockedIndicatorTextKey = "E1WorldMapPoiBlockedIndicator";

    /// <summary>
    /// <see cref="Wayfinders.Client.Services.SceneManager"/> modal id for
    /// E4. Stable so the E3 screen and the dispatcher don't both literal-duplicate it.
    /// </summary>
    public const string CharacterSheetModalId = "E4_CHARACTER_SHEET";

    public static PoiDispatchResult Dispatch(PoiDefinitionDto poi)
    {
        // Defense in depth: types that are hover-only in J4 are forced
        // to the blocked path even if the .tres flagged them clickable.
        if (poi.Kind is PoiKind.AmbientNpc or PoiKind.Interactable)
        {
            return new PoiDispatchResult(
                Outcome: PoiDispatchOutcome.ShowBlockedIndicator,
                TargetScreenId: null,
                IndicatorTextKey: GenericBlockedIndicatorTextKey,
                ModalId: null,
                NpcId: null);
        }

        // KeyBuilding click fires the J4 stub flash regardless of the
        // .tres TargetScreenId — Varn §3.3.3 says hover-only J4 with the
        // QG opening the contracts panel post-MVP. We surface the click
        // intent now via the indicator so dev sees the "blocked" affordance.
        if (poi.Kind is PoiKind.KeyBuilding)
        {
            // Non-clickable KeyBuilding = silent hover-only ; clickable
            // KeyBuilding = stub flash. Both routed through the same
            // outcome so the screen handles a single case.
            return new PoiDispatchResult(
                Outcome: PoiDispatchOutcome.ShowBlockedIndicator,
                TargetScreenId: null,
                IndicatorTextKey: KeyBuildingBlockedIndicatorTextKey,
                ModalId: null,
                NpcId: null);
        }

        // PNJ types open the E4 modal with the npc id threaded through
        // ScreenContext.Payload. Non-clickable PNJ falls through to the
        // generic blocked path below (defense in depth).
        if (poi.IsClickable && poi.Kind is PoiKind.NpcCandidate or PoiKind.NpcCompanion)
        {
            return new PoiDispatchResult(
                Outcome: PoiDispatchOutcome.OpenModal,
                TargetScreenId: null,
                IndicatorTextKey: null,
                ModalId: CharacterSheetModalId,
                NpcId: poi.PoiId);
        }

        // City + District clickable -> navigate. Empty target id falls
        // through to the blocked path below (J3 defensive default
        // preserved verbatim).
        if (poi.IsClickable
            && poi.Kind is PoiKind.City or PoiKind.District
            && !string.IsNullOrEmpty(poi.TargetScreenId))
        {
            return new PoiDispatchResult(
                Outcome: PoiDispatchOutcome.NavigateToScreen,
                TargetScreenId: poi.TargetScreenId,
                IndicatorTextKey: null,
                ModalId: null,
                NpcId: null);
        }

        // Default blocked path: City uses the J3 wording, District uses
        // the same key for now (dedicated district-blocked key can be
        // added when post-MVP gating ships). PNJ non-clickable also
        // lands here.
        return new PoiDispatchResult(
            Outcome: PoiDispatchOutcome.ShowBlockedIndicator,
            TargetScreenId: null,
            IndicatorTextKey: BlockedIndicatorTextKey,
            ModalId: null,
            NpcId: null);
    }
}

/// <summary>
/// Three-state outcome for <see cref="PoiDispatchLogic.Dispatch"/>.
/// </summary>
public enum PoiDispatchOutcome
{
    /// <summary>Caller should ask SceneManager to NavigateTo the target screen.</summary>
    NavigateToScreen,

    /// <summary>Caller should flash the blocked-indicator label using the indicator key.</summary>
    ShowBlockedIndicator,

    /// <summary>
    /// Caller should ask SceneManager to OpenModal with the modal id and
    /// thread the npc id through <c>ScreenContext.Payload["E4.NpcId"]</c>.
    /// J4 addition for PNJ POI clicks.
    /// </summary>
    OpenModal,
}

/// <summary>
/// Output of <see cref="PoiDispatchLogic.Dispatch"/>. Exactly one of
/// <see cref="TargetScreenId"/> (navigate path) /
/// <see cref="IndicatorTextKey"/> (blocked path) /
/// <see cref="ModalId"/> + <see cref="NpcId"/> (modal path) is set
/// depending on <see cref="Outcome"/>.
/// </summary>
public readonly record struct PoiDispatchResult(
    PoiDispatchOutcome Outcome,
    string? TargetScreenId,
    string? IndicatorTextKey,
    string? ModalId = null,
    string? NpcId = null);
