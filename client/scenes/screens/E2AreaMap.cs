using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Components;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E3 City Halfgate -- the Cadastre's Feuillet II for the unique MVP
/// cité (Varn §6.D6.10 lock 1-cité). J4 promoted this from the J1
/// three-button stub to a proper screen ; <b>Phase 9 slice 4</b>
/// (2026-05-08) migrated the screen-space TextureRect + CanvasLayer
/// pattern (P7 J3) to a world-space <see cref="MapPan2DComponent"/>
/// instance, mirroring E2's shape. <b>Phase 9 slice 5</b> (same day)
/// adds the L2→L3 drill wiring : a Push at <see cref="MapPan2DComponent.ZoomMax"/>
/// over any cursor position fires <see cref="MapPan2DComponent.DrillRequested"/>,
/// which routes to a <see cref="SceneManager.NavigateTo"/> push of the
/// E5 District Map.
///
/// <para>
/// <b>Slice 4 changes (2026-05-08).</b>
/// <list type="bullet">
///   <item>The Halfgate background bitmap is now the texture of
///         <c>MapPan2DComponent/WorldRoot/WorldMapSprite</c> (instead
///         of a screen-anchored <c>TextureRect</c>). Pan + continuous
///         zoom are inherited from the component verbatim, no
///         per-instance config beyond <see cref="MapPan2DComponent.Configure"/>.</item>
///   <item>POI hotspots spawn under <c>MapPan2DComponent.PoiContainer</c>
///         as <see cref="Node2D"/> roots holding a <see cref="Sprite2D"/>
///         and an <see cref="Area2D"/>+<see cref="CollisionShape2D"/>
///         hitbox -- same shape as E2 post-slice 1. POI source coords
///         (<c>area_pois.tres</c>) live in the source-image
///         pixel frame, which is the world frame for the WorldRoot
///         tree -- positions transfer 1-1 from the J3+J4 era.</item>
///   <item>Climb wiring : the pan component's <see cref="MapPan2DComponent.ClimbRequested"/>
///         signal is connected to <see cref="OnClimbRequested"/>, which
///         calls <see cref="SceneManager.NavigateBack"/>. Hide-not-Free
///         model on the autoload preserves E2's camera state across the
///         drill-then-climb round trip -- no explicit snapshot needed
///         (slice 4 livrable 3).</item>
///   <item>The chrome layers (<c>DecorationLayer</c>, <c>ChromeLayer</c>)
///         remain on <see cref="CanvasLayer"/>s above the world tree --
///         they are deliberately exempt from pan and zoom (banner + back
///         button + journal panel always visible at fixed screen
///         positions). The "CanvasLayer hides Camera2D" trap from
///         feedback_godot_rendering_input_traps does NOT apply here
///         because the world content lives directly under the Control
///         root via <c>MapPan2DComponent</c>, not under a CanvasLayer.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Slice 5 changes (2026-05-08).</b>
/// <list type="bullet">
///   <item><b>Drill L2→L3 wiring.</b> The pan component's
///         <see cref="MapPan2DComponent.DrillRequested"/> signal is now
///         connected to <see cref="OnDrillRequested"/>, which calls
///         <see cref="SceneManager.NavigateTo"/> with target id
///         <c>E3_DISTRICT</c> and a payload carrying the cursor's
///         world-space position under <see cref="OriginCoordPayloadKey"/>.</item>
///   <item><b>Always-true drill resolver (Didier lock α).</b> The slice
///         5 brief explicitly trades fog tiles for "drill libre" : at L2
///         we install a resolver that returns a constant
///         (<see cref="GridCoord"/>, <see cref="TileKnowledgeLadder.Levee"/>)
///         pair so <see cref="ZoomNavLogic"/> always classifies the
///         cap-Push as <see cref="ZoomNavAction.DrillCandidate"/>. The
///         coord itself is a sentinel ((0,0)) and is NOT used by the
///         drill payload -- E3 captures the actual cursor world-space
///         position at the handler entry instead, so the payload
///         carries useful drill-origin info even though the resolver
///         coord is meaningless. Decision α locked, no fog gate at L2.</item>
///   <item><b>Re-entry resilience (slice 5 livrable 4).</b> Because E3
///         lives across the drill (Hide-not-Free), the <c>_Ready</c>
///         path runs ONCE per E3 lifetime, and the resolver + signal
///         subscription persist across L2↔L3 round trips. The transition
///         lock is released by <see cref="OnDrillRequested"/>'s finally
///         block once <c>NavigateTo</c> returns, so a subsequent climb
///         from E5 lands on an E3 ready to accept input.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Out of scope, slice 5 (Didier locks).</b>
/// <list type="bullet">
///   <item><b>(a)</b> No <see cref="FogTileLayer"/> at L2 nor L3 -- the
///         cité is fully visible from arrival. If Varn wants fog later,
///         slice 5.5 dédiée. The slice 5 brief explicitly forbids tile
///         systems this slice.</item>
///   <item><b>(α)</b> No anchored drill on POIs -- any cursor position
///         at zoom max + Push triggers the drill, not just over a
///         specific district POI. POIs at L2 keep their existing click
///         dispatch (NpcCandidate -&gt; E4 modal, KeyBuilding -&gt;
///         blocked indicator) ; drill is a parallel input channel
///         dispatched by zoom + wheel.</item>
///   <item>No L2↔L3 fade transition -- flagged as ongoing dette,
///         identical to L1↔L2 dette from slice 4. SceneManager has no
///         fade hook in <see cref="SceneManager.NavigateTo"/> and
///         adding one would touch every IScreen consumer. Out of slice
///         risk budget.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>OriginCoord payload contract (slice 4 + slice 5).</b>
/// <list type="bullet">
///   <item><b>E2 -&gt; E3</b> (slice 4) : payload key
///         <see cref="OriginCoordPayloadKey"/> = <c>"E1.OriginCoord"</c>,
///         value <see cref="Vector2I"/> = the L1 grid cell coord the
///         drill fired on. Logged in <see cref="OnEnter"/> for
///         traceability ; not consumed visually.</item>
///   <item><b>E3 -&gt; E5</b> (slice 5) : payload key
///         <c>"E2.OriginCoord"</c>
///         (<see cref="E3DistrictMap.OriginCoordPayloadKey"/>), value
///         <see cref="Vector2"/> = the L2 image-space cursor position
///         at drill time. Logged in <see cref="E3DistrictMap.OnEnter"/>
///         for traceability ; not consumed visually for the same
///         reason as the L1→L2 hop : the L2-image-position-to-L3-image-position
///         mapping is undefined for the MVP single-quartier.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Re-entry semantics (slice 4 livrable 3 + slice 5 livrable 4).</b>
/// The SceneManager Hide-not-Free model preserves E2's
/// <c>MapPan2DComponent</c> state across L1↔L2 drill+climb round trips
/// AND now preserves E3's state across L2↔L3 drill+climb round trips
/// (smoke #6/#7 of slice 5). When the player climbs out of E3 (slice 4
/// path), E2's camera is exactly where it was at the moment of the
/// drill ; when the player climbs out of E5 (slice 5 path), E3's
/// camera is exactly where it was at the moment of the L3 drill --
/// zoom=ZoomMax, position=cursor anchor at drill time. E3 / E5 are
/// freed on Pop, so a re-drill instantiates a fresh component at
/// <c>ZoomDefault</c> centered on the image (no per-session state).
/// Acceptable MVP shape ; per-cité / per-quartier camera memory is a
/// post-MVP extension via <c>ScreenContext.Payload</c>.
/// </para>
///
/// <para>
/// <b>POI hotspot pattern (J3 D-J3-01 reused, slice 4 ported to
/// world-space, slice 5 unchanged).</b> POIs are <see cref="Node2D"/>
/// hotspots spawned from <c>res://data/area_pois.tres</c> at
/// <c>_Ready</c>, parented to <c>MapPan2DComponent.PoiContainer</c>.
/// Position + hit-box come from each <see cref="PoiDefinition"/>. The
/// Risk #1 lambda-capture dictionary (<c>_poiHandlers</c>) keeps the
/// per-POI lambdas around so <c>_ExitTree</c> can disconnect them with
/// the exact references used at wire time -- captured-reference
/// signal-leak discipline, same shape as E2.
/// </para>
///
/// <para>
/// <b>Multi-Kind dispatch via PoiDispatchLogic (J4 D-J4-07 unchanged).</b>
/// Click goes through the pure-C# helper :
/// <list type="bullet">
///   <item>City clickable -&gt; <see cref="PoiDispatchOutcome.NavigateToScreen"/></item>
///   <item>District clickable -&gt; <see cref="PoiDispatchOutcome.NavigateToScreen"/></item>
///   <item>NpcCandidate / NpcCompanion clickable -&gt;
///         <see cref="PoiDispatchOutcome.OpenModal"/> with the npc id
///         threaded through <c>ScreenContext.Payload["E4.NpcId"]</c></item>
///   <item>KeyBuilding -&gt; <see cref="PoiDispatchOutcome.ShowBlockedIndicator"/>
///         with the J4 stub key <c>E2AreaMapKeyBuildingBlockedIndicator</c></item>
///   <item>AmbientNpc / Interactable -&gt; defense-in-depth blocked</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Modal invariant (J3-fix locked, unchanged by slice 4 / 5).</b>
/// Opening E4 from a NpcCandidate click leaves E3 visible underneath --
/// the modal sits on <see cref="SceneManager"/>'s <c>_modalLayer</c>
/// (CanvasLayer Layer=10). The <c>OnSuspend</c> implementation here
/// only disables input (and dims the screen) -- it does NOT toggle
/// visibility. Test
/// <see cref="E4ModalContractTests.OpenModal_E4_does_not_hide_E3_host"/>
/// pins this contract for the (E3, E4) couple specifically.
/// </para>
///
/// <para>
/// <b>Esc handling (D-J3-11 reused).</b> Not bound here -- the
/// SceneManager autoload already maps <c>ui_cancel</c> to
/// <see cref="SceneManager.NavigateBack"/>. The "Quitter la cité"
/// button calls <see cref="SceneManager.NavigateBack"/> too. Climb-via-PULL
/// at zoom min is a third equivalent path (slice 4).
/// </para>
///
/// <para>
/// <b>Risk #3 mitigation (tooltip orphelin) reused J3.</b>
/// <see cref="HoverTooltipController.CancelTooltip"/> is called on
/// <c>_ExitTree</c> so a 600ms-pending tooltip cannot fire on the
/// next screen.
/// </para>
/// </summary>
public partial class E2AreaMap : Control, IScreen
{
    public string ScreenId => "E2_AREA";

    /// <summary>
    /// <see cref="ScreenContext.Payload"/> key for the npc id surfaced
    /// when E3 opens E4 from a PNJ POI click. Stable so E4 reads the
    /// same key the dispatcher and E3 wrote. See Rune J4 pre-brief
    /// §3.7 (ScreenContext extension via Payload bag, not typed
    /// property -- concrete-first per J1 §4.5).
    /// </summary>
    public const string NpcIdPayloadKey = "E4.NpcId";

    /// <summary>
    /// Slice 4 -- payload key consumed at <see cref="OnEnter"/> when E2
    /// drills here. Carries the <see cref="Vector2I"/> L1 grid cell the
    /// drill originated from. Logged for traceability ; not currently
    /// translated into an L2 camera focus (see class XML doc OriginCoord
    /// section). Mirrored on the E2 side as the same string literal so
    /// the contract is one writer (E2) and one reader (E3) keyed by an
    /// inline string -- the same shape as <see cref="NpcIdPayloadKey"/>.
    /// </summary>
    public const string OriginCoordPayloadKey = "E1.OriginCoord";

    /// <summary>
    /// Slice 5 -- the screen id of the drill target when L2 cap-Pushes.
    /// Stable string referenced by <see cref="OnDrillRequested"/> ; the
    /// SceneManager registration of E5 lives in <c>OpeningBootstrap</c>
    /// under the same id.
    /// </summary>
    private const string DrillTargetScreenId = "E3_DISTRICT";

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";

    // ---- Phase B (2026-05-16) : the Configure(string areaId) seam. ----
    //
    // The cité-level feuillet is no longer hardcoded to Halfgate at the
    // class scope ; an `areaId` parameter selects which area assets +
    // POI resource get loaded. For the MVP single-area MVP the resolver
    // has one entry ("halfgate") but the surface is shaped so a future
    // multi-area dispatcher (E1WorldMap → click city POI → drill into
    // E2AreaMap with the city's areaId) drops in without touching the
    // runtime shape.
    //
    // The areaId is threaded through `ScreenContext.Payload[AreaIdPayloadKey]`
    // by the caller (E1WorldMap.OnPoiPressed / E1WorldMap.OnDrillRequested) ;
    // E2AreaMap reads it in OnEnter and calls Configure(areaId). _Ready
    // falls back to DefaultAreaId so F6 (Godot editor "Run Current Scene")
    // and any future direct-instantiate path still produces a working
    // screen.
    public const string AreaIdPayloadKey = "E2.AreaId";
    public const string DefaultAreaId = "halfgate";

    // Shared chrome (banner / panels) is NOT per-area in the MVP -- the
    // same iso bandeau and side panels frame every cité. If a future
    // area authoritatively redesigns the chrome (Veylant rebuilt the
    // panels post-collapse, say), these become per-area too.
    private const string BannerTopAssetKey = "shared.banner_top";
    private const string PanelLeftAssetKey = "shared.panel_left";
    private const string PanelRightAssetKey = "shared.panel_right";
    private const string PanelBottomAssetKey = "e2.panel_bottom";

    /// <summary>
    /// Resolve an areaId to its (assetKey, poisResPath, useAreaGridLayer)
    /// triple. The MVP has exactly one entry ; throws if the areaId is
    /// unknown so a payload typo surfaces loud rather than silently
    /// swapping in a placeholder texture. Pure-static so it stays
    /// trivially testable.
    ///
    /// <para>
    /// <b>E2.1 (2026-05-16) -- area-grid layer rerouting.</b>
    /// <c>useAreaGridLayer = true</c> rewires the screen to render the
    /// 8x8 intra-area grid (via <see cref="BuildAreaGrid"/> in the
    /// partial file) instead of the legacy free-form POI plate. The
    /// area-grid background asset replaces the cite-level bitmap ;
    /// the legacy POI hotspots (marche, port, QG, npc candidates from
    /// <c>area_pois.tres</c>) are NOT spawned for that area. Halfgate
    /// is the only area on the area-grid path at E2.1 ; the legacy POI
    /// flow stays available for any future area that wants the
    /// free-form layout (none planned today). Flagged for Varn
    /// reconciliation : the legacy POIs may need either retirement
    /// (their roles fold into the grid + future cite-overlay) or
    /// promotion (KeyBuilding POIs become a dedicated layer above the
    /// grid). Out of E2.1 scope.
    /// </para>
    /// </summary>
    private static (string assetKey, string poisResPath, bool useAreaGridLayer) ResolveAreaAssets(string areaId)
    {
        return areaId switch
        {
            "halfgate" => ("e2.area_grid.background", "res://data/area_pois.tres", true),
            _ => throw new System.ArgumentException(
                $"[E2AreaMap] Unknown areaId '{areaId}'. Known : 'halfgate' (MVP single-area). " +
                $"Add a switch arm here when introducing a new area.",
                nameof(areaId)),
        };
    }

    /// <summary>
    /// The currently bound area. Set by Configure ; reset by _ExitTree
    /// so a re-instantiation starts at DefaultAreaId. Used by the
    /// preflight log + future diagnostic surfaces.
    /// </summary>
    private string _currentAreaId = DefaultAreaId;

    private const float BlockedFadeInSeconds = 0.1f;
    private const float BlockedHoldSeconds = 2.0f;
    private const float BlockedFadeOutSeconds = 0.2f;

    private MapPan2DComponent _panComponent = null!;

    private TextureRect _bannerTop = null!;
    private Label _bannerTitleLabel = null!;
    private Label _bannerSubtitleLabel = null!;

    private Label _visibilityLabel = null!;
    private Label _menaceLabel = null!;

    private PanelContainer _panelLeft = null!;
    private Label _panelLeftTitleLabel = null!;
    private Label _panelLeftBodyLabel = null!;

    private PanelContainer _panelRight = null!;
    private Label _panelRightTitleLabel = null!;
    private TabContainer _contractsTabContainer = null!;
    private Label _routineStubLabel = null!;
    private Label _mandateStubLabel = null!;

    private PanelContainer _panelBottom = null!;
    private Label _panelBottomTitleLabel = null!;
    private Label _panelBottomBodyLabel = null!;

    private Label _layerIndicatorLabel = null!;

    private Button _backButton = null!;
    private PanelContainer _blockedIndicator = null!;
    private Label _blockedIndicatorLabel = null!;

    private OpeningStrings _strings = null!;
    private AreaPois _poisResource = null!;

    /// <summary>
    /// Per-POI handler bag -- captured-reference signal-leak discipline
    /// (Risk #1) reused from E2 post-slice-1. Spawned hotspots are
    /// children of <see cref="MapPan2DComponent.PoiContainer"/>.
    /// </summary>
    private readonly Dictionary<Area2D, PoiAreaHandlers> _poiHandlers = new();

    /// <summary>
    /// Reverse lookup poi-id -&gt; hotspot Node2D root. Used so the
    /// hover handler can fetch the hotspot's <c>GlobalPosition</c> for
    /// the tooltip anchor without scanning the scene tree.
    /// </summary>
    private readonly Dictionary<string, Node2D> _poiHotspots = new();

    // Slice 4 -- captured handler reference for the climb signal so
    // _ExitTree can disconnect with the exact same delegate (Risk #1
    // signal-leak discipline).
    private MapPan2DComponent.ClimbRequestedEventHandler? _climbRequestedHandler;

    // Slice 5 -- captured handler reference for the drill signal so
    // _ExitTree can disconnect with the exact same delegate. Mirrors
    // E2's post-slice-3 wiring of DrillRequested ; we do NOT subscribe
    // to DrillBlocked because the always-true resolver guarantees
    // ZoomNavLogic returns DrillCandidate, never DrillBlocked.
    private MapPan2DComponent.DrillRequestedEventHandler? _drillRequestedHandler;

    private Tween? _blockedTween;

    public override void _Ready()
    {
        // -- _Ready does NODE BINDING + SIGNAL SUBSCRIPTION + CHROME STRING
        // -- ASSIGNMENT only. Area-specific work (texture load, pan-component
        // -- Configure, POI spawn) is deferred to the Configure(areaId) seam,
        // -- invoked at the end of _Ready with the DefaultAreaId fallback so
        // -- F6 launches still produce a working screen, and re-invoked by
        // -- OnEnter if the payload carries a different areaId. (Phase B
        // -- refactor 2026-05-16.)
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();

        _panComponent = GetNode<MapPan2DComponent>("MapPan2DComponent");

        _bannerTop = GetNode<TextureRect>("DecorationLayer/BannerTop");
        _bannerTitleLabel = GetNode<Label>("DecorationLayer/BannerTop/BannerTitleLabel");
        _bannerSubtitleLabel = GetNode<Label>("DecorationLayer/BannerTop/BannerSubtitleLabel");

        _visibilityLabel = GetNode<Label>("DecorationLayer/NMSubBanner/VisibilityLabel");
        _menaceLabel = GetNode<Label>("DecorationLayer/NMSubBanner/MenaceLabel");

        _panelLeft = GetNode<PanelContainer>("DecorationLayer/PanelLeft");
        _panelLeftTitleLabel = GetNode<Label>("DecorationLayer/PanelLeft/PanelLeftVBox/PanelLeftTitleLabel");
        _panelLeftBodyLabel = GetNode<Label>("DecorationLayer/PanelLeft/PanelLeftVBox/PanelLeftBodyLabel");

        _panelRight = GetNode<PanelContainer>("DecorationLayer/PanelRight");
        _panelRightTitleLabel = GetNode<Label>("DecorationLayer/PanelRight/PanelRightVBox/PanelRightTitleLabel");
        _contractsTabContainer = GetNode<TabContainer>("DecorationLayer/PanelRight/PanelRightVBox/ContractsTabContainer");
        _routineStubLabel = GetNode<Label>("DecorationLayer/PanelRight/PanelRightVBox/ContractsTabContainer/Routine/RoutineStubLabel");
        _mandateStubLabel = GetNode<Label>("DecorationLayer/PanelRight/PanelRightVBox/ContractsTabContainer/MandatsDesArpenteurs/MandateStubLabel");

        _panelBottom = GetNode<PanelContainer>("DecorationLayer/PanelBottom");
        _panelBottomTitleLabel = GetNode<Label>("DecorationLayer/PanelBottom/PanelBottomVBox/PanelBottomTitleLabel");
        _panelBottomBodyLabel = GetNode<Label>("DecorationLayer/PanelBottom/PanelBottomVBox/PanelBottomBodyLabel");

        _layerIndicatorLabel = GetNode<Label>("DecorationLayer/LayerIndicatorLabel");

        _backButton = GetNode<Button>("ChromeLayer/BackButton");
        _blockedIndicator = GetNode<PanelContainer>("ChromeLayer/BlockedIndicator");
        _blockedIndicatorLabel = GetNode<Label>("ChromeLayer/BlockedIndicator/BlockedIndicatorLabel");

        // Shared chrome textures (same for every area). AssetResolver
        // returns a deterministic placeholder if the file is missing.
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _bannerTop.Texture = assetResolver.Resolve(BannerTopAssetKey);
        _ = assetResolver.Resolve(PanelLeftAssetKey);
        _ = assetResolver.Resolve(PanelRightAssetKey);
        _ = assetResolver.Resolve(PanelBottomAssetKey);

        // Chrome strings -- shared across every area in the MVP. The
        // per-area title is set inside Configure() so a multi-area
        // future can swap the banner subtitle per cité if Varn revises
        // the spec.
        _panelLeftTitleLabel.Text = _strings.E2AreaMapPanelLeftTitle;
        _panelLeftBodyLabel.Text = _strings.E2AreaMapPanelLeftBody;
        _panelRightTitleLabel.Text = _strings.E2AreaMapPanelRightTitle;
        _routineStubLabel.Text = _strings.E2AreaMapTabRoutineStub;
        _mandateStubLabel.Text = _strings.E2AreaMapTabMandateStub;
        _panelBottomTitleLabel.Text = _strings.E2AreaMapPanelBottomTitle;
        _panelBottomBodyLabel.Text = _strings.E2AreaMapPanelBottomBody;
        _layerIndicatorLabel.Text = _strings.E2AreaMapLayerIndicator;
        _backButton.Text = _strings.E2AreaMapBackButton;
        _blockedIndicatorLabel.Text = _strings.E2AreaMapKeyBuildingBlockedIndicator;

        // Godot quirk: TabContainer reads each child Node's Name as the
        // tab label. Rename children to the Varn-locked libellés so the
        // .tscn stays readable. J4 D-J4-05 + Rune J4 pre-brief §11.2.
        var routineTab = _contractsTabContainer.GetNode<Control>("Routine");
        var mandateTab = _contractsTabContainer.GetNode<Control>("MandatsDesArpenteurs");
        routineTab.Name = _strings.E2AreaMapTabRoutine;
        mandateTab.Name = _strings.E2AreaMapTabMandate;

        _backButton.Pressed += OnBackPressed;

        // Slice 4 -- subscribe to the climb signal. Captured-reference
        // discipline : store the handler in a field so _ExitTree can
        // disconnect with the exact same delegate (Risk #1, mirrors E2).
        _climbRequestedHandler = OnClimbRequested;
        _panComponent.ClimbRequested += _climbRequestedHandler;

        // Slice 5 -- install the always-true drill resolver and subscribe
        // to DrillRequested. Decision α (Didier locked) : at L2 there is
        // no fog gate -- ANY cap-Push triggers a drill into E5.
        _panComponent.SetDrillTargetResolver(AlwaysTrueDrillResolver);
        _drillRequestedHandler = OnDrillRequested;
        _panComponent.DrillRequested += _drillRequestedHandler;

        // -- E2.1a + E2.1b + E2.1c strict scope (2026-05-16, Didier
        // -- lock + Rune extension at E2.1c per the E2.1c brief : "Chrome
        // -- toujours caché"). The iso 8x8 grid + (at E2.1b) the wash
        // -- with district tints + (at E2.1c) the per-mission POI markers
        // -- are the ONLY things the player should see at this jalon
        // -- ladder. Hide the two chrome CanvasLayers (DecorationLayer =
        // -- banner + sub-banner + Compagnie panel + Contrats panel +
        // -- bottom Journal panel + LayerIndicatorLabel ; ChromeLayer =
        // -- "Quitter la cité" BackButton + BlockedIndicator) by setting
        // -- their Visible to false. CanvasLayer.Visible propagates to
        // -- every descendant, so we do not have to enumerate the inner
        // -- Controls. Hide (not QueueFree) so flipping ScopeMode to
        // -- "E2.2" dégèle the chrome progressively without re-binding
        // -- any field reference -- _Ready already bound the labels and
        // -- the back button to fields above.
        // --
        // -- ESC ladder-up is NOT affected : SceneManager._UnhandledInput
        // -- maps ui_cancel to NavigateBack at the autoload layer (see
        // -- SceneManager.cs around line 831). The BackButton being
        // -- hidden simply removes the on-screen affordance ; the
        // -- keyboard shortcut keeps working because the Control root
        // -- has mouse_filter = Ignore and no _Input handler on this
        // -- screen claims ui_cancel.
        if (CurrentScopeMode == "E2.1a" || CurrentScopeMode == "E2.1b" || CurrentScopeMode == "E2.1c")
        {
            var decorationLayer = GetNode<CanvasLayer>("DecorationLayer");
            var chromeLayer = GetNode<CanvasLayer>("ChromeLayer");
            decorationLayer.Visible = false;
            chromeLayer.Visible = false;
            GD.Print(
                $"[E2AreaMap] {CurrentScopeMode} strict : hidden DecorationLayer + " +
                "ChromeLayer (banner, sub-banner, Compagnie panel, " +
                "Contrats panel, Journal panel, LayerIndicator, " +
                "BackButton, BlockedIndicator). ESC shortcut remains " +
                "active via SceneManager._UnhandledInput.");
        }

        // -- Phase B (2026-05-16) : invoke the Configure seam with the
        // -- DefaultAreaId fallback. If OnEnter later receives a different
        // -- areaId via payload, it calls Configure again to re-bind. For
        // -- the MVP single-area, the OnEnter call is a no-op (same id).
        Configure(DefaultAreaId);
    }

    /// <summary>
    /// Phase B seam (2026-05-16) -- bind this screen to the area
    /// identified by <paramref name="areaId"/>. Resolves the area's
    /// background texture asset key + POI resource path, loads them,
    /// applies the texture to the <see cref="MapPan2DComponent"/>, and
    /// (re)spawns the POI hotspots under the pan component's
    /// <see cref="MapPan2DComponent.PoiContainer"/>.
    ///
    /// <para>
    /// <b>Idempotency / re-entry contract.</b> Safe to call multiple
    /// times on the same instance. Each call disconnects + frees the
    /// existing POI hotspots (Risk #1 captured-reference discipline
    /// kept intact via <see cref="_poiHandlers"/>) before spawning the
    /// new area's POIs. The pan component's
    /// <see cref="MapPan2DComponent.Configure"/> overwrites the world
    /// texture + camera bounds, so the camera resets to the new image
    /// center -- intended : a re-Configure means the player is now
    /// looking at a different area, not the same one zoomed.
    /// </para>
    ///
    /// <para>
    /// <b>Per-area surface today (MVP single-area).</b>
    /// <list type="bullet">
    ///   <item>Background texture (AssetResolver-resolved by key).</item>
    ///   <item>POI resource (<c>res://data/&lt;area&gt;_pois.tres</c>).</item>
    ///   <item>Banner title + subtitle (currently shared via OpeningStrings,
    ///         flagged for Varn reconciliation if a future area wants
    ///         per-area chrome).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Out of scope (Phase B).</b> Continuous E2↔E2 transition (the
    /// "switch cité without climbing back to E1WorldMap" flow). The
    /// signature accepts the swap but the camera-state preservation,
    /// the fade animation, and the multi-area Configure-pump on
    /// SceneManager are deferred. Phase B only proves the seam works
    /// for the existing "drill from E1WorldMap to the one and only
    /// Halfgate" path -- callers always pass "halfgate" today.
    /// </para>
    /// </summary>
    /// <param name="areaId">A known area identifier. Throws
    /// <see cref="System.ArgumentException"/> if unknown -- a payload
    /// typo surfaces loud rather than silently swapping in a placeholder
    /// texture.</param>
    public void Configure(string areaId)
    {
        _currentAreaId = areaId;
        var (assetKey, poisResPath, useAreaGridLayer) = ResolveAreaAssets(areaId);

        // (Re-)load the POI resource for this area.
        _poisResource = ResourceLoader.Load<AreaPois>(poisResPath) ?? new AreaPois();

        // (Re-)resolve and apply the area's background texture. The pan
        // component's Configure overwrites the world texture + bounds,
        // so a re-Configure on a previously-bound instance resets the
        // camera to the new image center (intended).
        //
        // E2.1a strict scope (Didier lock 2026-05-16) : when the area
        // uses the iso area-grid layer, the area background bitmap is
        // NOT shown (the iso losanges are the visible surface). We pass
        // a 1x1 transparent placeholder to the pan component to satisfy
        // its Configure(texture, ...) contract, then hide the sprite,
        // and override the camera bounds to the iso bbox so pan + zoom
        // frame the whole grid. The legacy background path (any future
        // area without an iso grid) keeps the area bitmap visible.
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        Texture2D worldImage;
        Vector2 worldBoundsSize;
        Vector2 initialCenter;
        if (useAreaGridLayer)
        {
            worldImage = MakeTransparentPlaceholderTexture();
            worldBoundsSize = new Vector2(IsoBboxWidth, IsoBboxHeight);
            initialCenter = worldBoundsSize / 2f;
        }
        else
        {
            worldImage = assetResolver.Resolve(assetKey);
            worldBoundsSize = worldImage.GetSize();
            initialCenter = worldBoundsSize / 2f;
        }
        _panComponent.Configure(worldImage, initialCenter, worldBoundsSize);
        // Hide the background sprite when the iso grid owns the visible
        // surface. The MapPan2DComponent still owns the sprite node and
        // the world tree -- we just suppress its rendering at E2.1a.
        _panComponent.WorldMapSprite.Visible = !useAreaGridLayer;

        // Per-area chrome strings. Today both Banner title + subtitle
        // come from the shared OpeningStrings entries -- a multi-area
        // future would resolve these from a per-area OpeningStrings
        // slice or a separate AreaStrings resource. Out of scope for
        // Phase B (flagged for Varn reconciliation pass).
        _bannerTitleLabel.Text = _strings.E2AreaMapTitle;
        _bannerSubtitleLabel.Text = _strings.E2AreaMapSubtitle;
        _visibilityLabel.Text = _strings.E2AreaMapVisibilityLabel;
        _menaceLabel.Text = _strings.E2AreaMapMenaceLabel;

        // Tear down any pre-existing POI hotspots from a prior Configure
        // call. Disconnect handlers FIRST (Risk #1 captured-reference
        // discipline), then free the hotspot Node2D roots.
        foreach (var (area, handlers) in _poiHandlers)
        {
            if (area is null) continue;
            area.InputEvent -= handlers.InputEvent;
            area.MouseEntered -= handlers.MouseEntered;
            area.MouseExited -= handlers.MouseExited;
            area.GetParent()?.QueueFree();
        }
        _poiHandlers.Clear();
        _poiHotspots.Clear();

        // E2.1 path : when useAreaGridLayer is true, the legacy free-form
        // POI plate is replaced by the 8x8 area-grid render layer. The
        // legacy SpawnPois call is skipped so the marche/port/QG hotspots
        // from area_pois.tres do not coexist with the grid (their
        // image-space positions in the 3840x2160 frame are outside the
        // 2048x2048 grid bounds, so they would not visually render but
        // they would still register Area2D hit-tests at world coords
        // outside the camera view, surfacing nothing useful).
        if (useAreaGridLayer)
        {
            BuildAreaGrid();
            // E2.1a strict scope (Didier lock 2026-05-16) : the tuto
            // mission pop_effect is dégelé starting E2.1b. At E2.1a all
            // 64 cells stay in Fog. At E2.1b the A4.7 R3 projection
            // takes the 16 E1 footprint cells and writes the 64 E2
            // cells fog -> partial -- TileRevealRenderController Tweens
            // parchment_alpha 0 -> 0.55 over 300 ms per cell.
            if (ScopeMode != "E2.1a")
            {
                ApplyTutorialMissionPopEffect();
            }
        }
        else
        {
            SpawnPois(assetResolver);
        }

        // Phase 8 preflight self-check : surfaces the area binding +
        // signal wiring at the console. If the area resolver returned
        // a wrong key, the AssetResolver placeholder shows up here as
        // a tiny gray-square texture instead of the parchment cité --
        // immediate visible diagnostic.
        var (fogCount, partialCount, revealedCount) = useAreaGridLayer
            ? CountRevealStates()
            : (0, 0, 0);
        GD.Print(
            $"[E2AreaMap] Configure(areaId='{areaId}') -> " +
            $"useAreaGridLayer={useAreaGridLayer}, " +
            $"legacy_poi_count={_poiHandlers.Count}, " +
            $"area_grid_reveal=(fog={fogCount}, partial={partialCount}, revealed={revealedCount}), " +
            $"asset key='{assetKey}', pois='{poisResPath}', " +
            $"world image={_panComponent.WorldImageSize}, " +
            $"camera initialCenter={initialCenter}, " +
            $"climb wired={_climbRequestedHandler is not null}, " +
            $"drill wired={_drillRequestedHandler is not null} -> {DrillTargetScreenId}");
    }

    public override void _ExitTree()
    {
        // E2.1 -- tear down the area-grid layer first (disconnects
        // the cell-hover + npc-hover handlers, frees the AreaGridLayer
        // Node2D subtree which in turn frees the TileRevealRenderController
        // child whose own _ExitTree disconnects from GameState.
        // TILED reveal state in GameState.TileRevealStates persists --
        // that is the intended sparse-dict semantic (a future re-entry
        // to E2AreaMap on the same session would observe the already-
        // partial cells without re-popping the tuto mission).
        TearDownAreaGrid();

        // Disconnect chrome handlers first.
        if (_backButton is not null) _backButton.Pressed -= OnBackPressed;

        // Slice 4 + 5 -- disconnect the climb + drill signals using the
        // captured handler references. If the pan component is still
        // alive (it should be, the screen owns it as a child and tree
        // teardown is bottom-up), the disconnects no-op silently if
        // it's not. Also detach the drill resolver to break the closure
        // reference (the resolver is a method group, no closure capture,
        // but the SetDrillTargetResolver(null) call is the explicit
        // teardown counterpart of the SetDrillTargetResolver call in
        // _Ready -- mirrors E2's _ExitTree shape).
        if (_panComponent is not null)
        {
            if (_climbRequestedHandler is not null)
            {
                _panComponent.ClimbRequested -= _climbRequestedHandler;
            }
            if (_drillRequestedHandler is not null)
            {
                _panComponent.DrillRequested -= _drillRequestedHandler;
            }
            _panComponent.SetDrillTargetResolver(null);
        }
        _climbRequestedHandler = null;
        _drillRequestedHandler = null;

        // Disconnect every POI handler with the EXACT lambda reference
        // captured at wire time. Method-group disconnect would not match
        // because each lambda closed over a distinct PoiId (Risk #1).
        foreach (var (area, handlers) in _poiHandlers)
        {
            if (area is null) continue;
            area.InputEvent -= handlers.InputEvent;
            area.MouseEntered -= handlers.MouseEntered;
            area.MouseExited -= handlers.MouseExited;
            area.GetParent()?.QueueFree();
        }
        _poiHandlers.Clear();
        _poiHotspots.Clear();

        // Risk #3: cancel any pending tooltip timer so the autoload
        // does not surface a tooltip on the next screen mid-fade.
        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        tooltipController?.CancelTooltip();

        _blockedTween?.Kill();
        _blockedTween = null;
    }

    /// <summary>
    /// Slice 4 -- spawn POIs as <see cref="Node2D"/> hotspots under
    /// <see cref="MapPan2DComponent.PoiContainer"/>, each holding a
    /// <see cref="Sprite2D"/> for visuals and an <see cref="Area2D"/>+
    /// <see cref="CollisionShape2D"/> for click hit-testing. Same shape
    /// as E2 post-slice-1 -- positions in the source-image pixel frame
    /// (which is the world frame for the WorldRoot tree).
    /// </summary>
    private void SpawnPois(AssetResolver assetResolver)
    {
        foreach (var poi in _poisResource.Pois)
        {
            if (poi is null) continue;

            var hotspot = new Node2D
            {
                Name = $"Poi_{poi.PoiId}",
                Position = poi.Position,
                ZIndex = 5,
            };

            var sprite = new Sprite2D
            {
                Texture = assetResolver.Resolve(poi.AssetKey),
                Centered = true,
                Modulate = poi.IsClickable
                    ? new Color(1, 1, 1, 1)
                    : new Color(0.85f, 0.78f, 0.68f, 0.85f),
            };

            var area = new Area2D
            {
                Name = "Hitbox",
                InputPickable = true,
            };
            var shape = new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = poi.Size },
            };
            area.AddChild(shape);

            hotspot.AddChild(sprite);
            hotspot.AddChild(area);
            _panComponent.PoiContainer.AddChild(hotspot);

            // Capture the PoiId once per closure so the dispatch reads
            // the right entry. The lambdas are *kept* in _poiHandlers so
            // the _ExitTree disconnect uses the same references (Risk #1
            // reused J3+E2-slice-1 pattern).
            var poiId = poi.PoiId;

            Area2D.InputEventEventHandler inputHandler = (_, evt, _) =>
            {
                if (evt is InputEventMouseButton hit
                    && hit.Pressed
                    && hit.ButtonIndex == MouseButton.Left)
                {
                    OnPoiPressed(poiId);
                }
            };
            Action mouseEntered = () => OnPoiHoverIn(poiId);
            Action mouseExited = () => OnPoiHoverOut(poiId);

            area.InputEvent += inputHandler;
            area.MouseEntered += mouseEntered;
            area.MouseExited += mouseExited;

            _poiHandlers[area] = new PoiAreaHandlers(inputHandler, mouseEntered, mouseExited);
            _poiHotspots[poiId] = hotspot;
        }
    }

    private async void OnBackPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.NavigateBack();
    }

    /// <summary>
    /// Slice 4 -- ClimbRequested handler. The pan component fires this
    /// when the player Pulls the wheel at <see cref="MapPan2DComponent.ZoomMin"/>.
    /// On L2, climbing means popping back to the screen that pushed us
    /// here (E2 monde) -- <see cref="SceneManager.NavigateBack"/>.
    /// Hide-not-Free preserves E2's camera state across the round trip
    /// so when E2 is re-shown, the camera is still on the cell the
    /// player drilled from at the same zoom level (Hide preserves
    /// Camera2D.Position + Zoom for free -- the node tree never died).
    ///
    /// <para>
    /// The transition lock on the pan component is released after
    /// <see cref="SceneManager.NavigateBack"/> completes, regardless of
    /// outcome. Wrapped in <c>try/finally</c> against the rare
    /// throw path -- if NavigateBack ever blows up (registration
    /// missing, etc), the player still gets input back on next
    /// interaction. Defensive null-check mirrors E2's pattern : the
    /// pan component's <c>QueueFree</c> is deferred to end-of-frame
    /// so the field reference survives the await, but a future race
    /// could theoretically null it.
    /// </para>
    /// </summary>
    private async void OnClimbRequested()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        try
        {
            await sceneManager.NavigateBack();
        }
        finally
        {
            // E3 is being freed by NavigateBack ; the pan component
            // tween is killed in MapPan2DComponent._ExitTree. The
            // NotifyTransitionEnded call here is harmless on a node
            // about to be freed (idempotent flag write) but kept for
            // symmetry with E2's pattern -- if a future slice retains
            // E3 (Hide-not-Free climb), the lock release is still
            // needed.
            _panComponent?.NotifyTransitionEnded();
        }
    }

    /// <summary>
    /// Slice 5 -- DrillRequested handler. The pan component fires this
    /// when the player Pushes the wheel at <see cref="MapPan2DComponent.ZoomMax"/>
    /// AND the always-true resolver returns a state &gt;= Esquissée
    /// (which it always does, decision α). The signal carries a
    /// <see cref="Vector2I"/> coord -- the sentinel (0,0) returned by
    /// <see cref="AlwaysTrueDrillResolver"/> -- which we IGNORE here :
    /// the meaningful drill-origin info is the cursor's actual
    /// world-space position at the moment of the wheel event, which we
    /// fetch from the viewport directly. This keeps the payload
    /// contract honest (the position E5 logs is the position the
    /// player was inspecting on the L2 image, not a sentinel).
    ///
    /// <para>
    /// Routes through <see cref="SceneManager.NavigateTo"/> to E5
    /// (Hide-not-Free push : E3 is hidden, E5 is shown above ; on
    /// climb back, E3 is re-shown with its pan component state intact,
    /// preserving the L2 zoom + position the player drilled from --
    /// slice 5 livrable 4 verified by smoke #7).
    /// </para>
    ///
    /// <para>
    /// The transition lock on the pan component is released after
    /// NavigateTo completes. Wrapped in try/finally against the rare
    /// throw path. Same defensive shape as
    /// <see cref="OnClimbRequested"/>.
    /// </para>
    /// </summary>
    /// <param name="coord">Sentinel coord from the always-true resolver
    /// -- intentionally ignored. The meaningful position is the
    /// cursor's world-space coordinate at this exact frame.</param>
    private async void OnDrillRequested(Vector2I coord)
    {
        // Capture the actual cursor world-space position, not the
        // sentinel coord the always-true resolver returned. The pan
        // component already does this math internally to feed the
        // resolver, but does not expose it on the DrillRequested signal
        // (the signal carries the resolved coord, by design -- a future
        // fog-gated L2 would surface a meaningful Vector2I cell index).
        // We replicate it here for the OriginCoord payload.
        var viewport = GetViewport();
        var canvasTransform = viewport.GetCanvasTransform();
        var cursorWorld = canvasTransform.AffineInverse() * viewport.GetMousePosition();

        var sceneManager = GetNode<SceneManager>("/root/SceneManager");

        // Pack the L2 origin position into the payload bag. E5 reads it
        // back at OnEnter for traceability (logged-but-not-applied,
        // same shape as E2AreaMap's E1.OriginCoord handling -- the
        // L2-image-to-L3-image mapping is undefined for the MVP single-
        // quartier).
        var payload = new ScreenContext
        {
            Payload = new Dictionary<string, object>
            {
                [E3DistrictMap.OriginCoordPayloadKey] = cursorWorld,
            },
        };

        try
        {
            GD.Print(
                $"[E2AreaMap] DrillRequested -> {DrillTargetScreenId} " +
                $"at L2 cursor world=({cursorWorld.X:F1}, {cursorWorld.Y:F1}) " +
                $"(resolver sentinel coord={coord} ignored, decision α)");
            await sceneManager.NavigateTo(DrillTargetScreenId, payload);
        }
        finally
        {
            // Release the transition lock regardless of NavigateTo
            // outcome. E3 is hidden (not freed) by NavigateTo's
            // Hide-not-Free model, so the pan component is still alive
            // and this call lands cleanly.
            _panComponent?.NotifyTransitionEnded();
        }
    }

    /// <summary>
    /// Slice 5 -- always-true drill resolver. Decision α (Didier locked
    /// 2026-05-08) : at L2 there is no fog gate -- ANY cap-Push triggers
    /// a drill into E5. We feed <see cref="ZoomNavLogic.EvaluateWheelInput"/>
    /// a constant (GridCoord(0,0), TileKnowledgeLadder.Levee) pair so the
    /// drill candidacy gate (state &gt;= Esquissee) always passes. The
    /// coord (0,0) is a sentinel ; <see cref="OnDrillRequested"/>
    /// ignores it and captures the actual cursor world-space position
    /// instead.
    ///
    /// <para>
    /// Why a sentinel coord and not the actual cursor cell : there is
    /// NO L2 grid in this MVP (no fog tiles). Returning (0,0) is the
    /// minimal "anything &gt;= Esquissee" answer that satisfies the
    /// gate without inventing a fictional grid. If slice 5.5 later adds
    /// L2 fog tiles (Varn decision), this resolver gets replaced with a
    /// real fog-layer hit-test, exactly like E2's slice-3
    /// <c>ResolveDrillTarget</c>.
    /// </para>
    /// </summary>
    /// <param name="cursorWorldPosition">Cursor world position. Unused
    /// here (the resolver is constant) but part of the signature so
    /// the future fog-gated implementation drops in without an API
    /// change.</param>
    /// <returns>Always a non-null pair with state &gt;= Esquissee,
    /// guaranteeing <see cref="ZoomNavLogic.EvaluateWheelInput"/>
    /// returns <see cref="ZoomNavAction.DrillCandidate"/> at the cap.</returns>
    private static (GridCoord coord, TileKnowledgeLadder state)? AlwaysTrueDrillResolver(
        Vector2 cursorWorldPosition)
    {
        return (new GridCoord(0, 0), TileKnowledgeLadder.Levee);
    }

    private async void OnPoiPressed(string poiId)
    {
        var poi = FindPoi(poiId);
        if (poi is null)
        {
            GD.PushWarning($"[E2AreaMap] POI clicked but no entry found: {poiId}");
            return;
        }

        var result = PoiDispatchLogic.Dispatch(poi.ToDto());
        switch (result.Outcome)
        {
            case PoiDispatchOutcome.NavigateToScreen:
                GD.Print($"[E2AreaMap] POI clicked: {poiId} ({poi.Kind}) -> navigating to {result.TargetScreenId}");
                var sceneManager = GetNode<SceneManager>("/root/SceneManager");
                await sceneManager.NavigateTo(result.TargetScreenId!);
                break;

            case PoiDispatchOutcome.OpenModal:
                GD.Print($"[E2AreaMap] POI clicked: {poiId} ({poi.Kind}) -> opening modal {result.ModalId} for npc {result.NpcId}");
                var sceneManagerModal = GetNode<SceneManager>("/root/SceneManager");
                var ctx = new ScreenContext
                {
                    Payload = new Dictionary<string, object>
                    {
                        [NpcIdPayloadKey] = result.NpcId ?? string.Empty,
                    },
                };
                await sceneManagerModal.OpenModal(result.ModalId!, ctx);
                break;

            case PoiDispatchOutcome.ShowBlockedIndicator:
                GD.Print($"[E2AreaMap] POI clicked: {poiId} ({poi.Kind}) -> blocked, {result.IndicatorTextKey}");
                ShowBlockedFlash(result.IndicatorTextKey);
                break;
        }
    }

    private void OnPoiHoverIn(string poiId)
    {
        var poi = FindPoi(poiId);
        if (poi is null) return;

        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        if (tooltipController is null) return;

        var text = ResolveTooltipText(poi);
        if (string.IsNullOrEmpty(text)) return;

        var anchor = _poiHotspots.TryGetValue(poiId, out var hotspot)
            ? hotspot.GetGlobalTransformWithCanvas().Origin
            : Vector2.Zero;
        tooltipController.RequestTooltip(text, anchor);
    }

    private void OnPoiHoverOut(string poiId)
    {
        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        tooltipController?.CancelTooltip();
    }

    /// <summary>
    /// Resolve the tooltip text for a POI based on its <see cref="PoiKind"/>
    /// and tooltip key. District uses the §4.1 E3.J template with
    /// <c>[nom]</c> interpolation ; KeyBuilding QG uses E3.M verbatim ;
    /// NpcCandidate / NpcCompanion use E3.P verbatim (Varn lock).
    /// </summary>
    private string ResolveTooltipText(PoiDefinition poi)
    {
        if (string.IsNullOrEmpty(poi.TooltipKey))
            return string.Empty;

        return poi.TooltipKey switch
        {
            "E2AreaMapPoiDistrictTooltipTemplate" =>
                _strings.E2AreaMapPoiDistrictTooltipTemplate.Replace("[nom]", poi.DisplayName),
            "E2AreaMapPoiKeyBuildingHqTooltip" =>
                _strings.E2AreaMapPoiKeyBuildingHqTooltip,
            "E2AreaMapPoiCandidateTooltip" =>
                _strings.E2AreaMapPoiCandidateTooltip,
            _ => string.Empty,
        };
    }

    private void ShowBlockedFlash(string? indicatorTextKey)
    {
        // Restart any in-flight flash so a rapid second click resets
        // the tween rather than stacking labels (J3 pattern).
        _blockedTween?.Kill();

        // Refresh the label text in case the dispatch surfaced a
        // different key (KeyBuilding vs generic). Defensive: fall back
        // to the J4 stub text on null/unknown key.
        var text = indicatorTextKey switch
        {
            "E2AreaMapKeyBuildingBlockedIndicator" => _strings.E2AreaMapKeyBuildingBlockedIndicator,
            "E1WorldMapPoiBlockedIndicator" => _strings.E1WorldMapPoiBlockedIndicator,
            _ => _strings.E2AreaMapKeyBuildingBlockedIndicator,
        };
        _blockedIndicatorLabel.Text = text;

        _blockedIndicator.Modulate = new Color(1, 1, 1, 0);
        _blockedIndicator.Visible = true;

        _blockedTween = CreateTween();
        _blockedTween.TweenProperty(_blockedIndicator, "modulate:a", 1f, BlockedFadeInSeconds);
        _blockedTween.TweenInterval(BlockedHoldSeconds);
        _blockedTween.TweenProperty(_blockedIndicator, "modulate:a", 0f, BlockedFadeOutSeconds);
        _blockedTween.TweenCallback(Callable.From(HideBlockedAfterFade));
    }

    private void HideBlockedAfterFade()
    {
        _blockedIndicator.Visible = false;
    }

    private PoiDefinition? FindPoi(string poiId)
    {
        foreach (var poi in _poisResource.Pois)
        {
            if (poi is not null && poi.PoiId == poiId) return poi;
        }
        return null;
    }

    /// <summary>
    /// Slice 4 -- log the OriginCoord payload from E2 for traceability.
    /// The coord is not consumed visually (see class XML doc OriginCoord
    /// section : the L1-grid-coord-to-L2-image-position mapping is
    /// undefined for the MVP single-cité), but having the print
    /// statement makes the drill-then-arrive contract observable in
    /// the console -- a smoke-test affordance.
    /// </summary>
    /// <summary>
    /// Phase B (2026-05-16) -- OnEnter reads the areaId from the payload
    /// and re-Configures if it differs from the current binding.
    /// _Ready already invoked <c>Configure(DefaultAreaId)</c> as a
    /// fallback ; here we honour the caller's explicit choice.
    ///
    /// <para>
    /// Same OnEnter also logs the OriginCoord payload (slice 4 carryover)
    /// for traceability of the drill-origin contract from E1WorldMap.
    /// </para>
    /// </summary>
    public Task OnEnter(ScreenContext context, CancellationToken ct)
    {
        if (context.Payload.TryGetValue(AreaIdPayloadKey, out var areaIdRaw)
            && areaIdRaw is string areaId
            && !string.IsNullOrEmpty(areaId)
            && areaId != _currentAreaId)
        {
            GD.Print(
                $"[E2AreaMap] OnEnter: payload AreaId='{areaId}' differs from current " +
                $"'{_currentAreaId}' -- re-Configuring");
            Configure(areaId);
        }

        if (context.Payload.TryGetValue(OriginCoordPayloadKey, out var raw)
            && raw is Vector2I originCoord)
        {
            GD.Print(
                $"[E2AreaMap] OnEnter: OriginCoord = ({originCoord.X}, {originCoord.Y}) " +
                "(logged for traceability ; not used for L2 camera focus this slice)");
        }
        return Task.CompletedTask;
    }

    public Task OnExit(CancellationToken ct) => Task.CompletedTask;

    public Task OnSuspend(CancellationToken ct)
    {
        // Disable input + dim while E4 modal is open. Visibility stays
        // ON so the modal overlay can render above E3 -- modal invariant
        // J3-fix locked.
        ProcessMode = ProcessModeEnum.Disabled;
        Modulate = new Color(0.6f, 0.6f, 0.6f, 1.0f);
        return Task.CompletedTask;
    }

    public Task OnResume(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Inherit;
        Modulate = Colors.White;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Slice 4 -- per-POI handler bag. Three lambda references kept on
    /// each spawned hotspot so <c>_ExitTree</c> disconnects with the
    /// exact same Action references used at wire time. Same shape as
    /// E2 post-slice-1 (Risk #1 captured-reference signal-leak
    /// discipline).
    /// </summary>
    private readonly record struct PoiAreaHandlers(
        Area2D.InputEventEventHandler InputEvent,
        Action MouseEntered,
        Action MouseExited);

    /// <summary>
    /// E2.1a strict scope (2026-05-16) -- placeholder texture for the
    /// pan component when the iso area-grid owns the visible surface.
    /// <see cref="MapPan2DComponent.Configure(Texture2D, Vector2, Vector2)"/>
    /// requires a non-null texture (it copies the size into
    /// <c>WorldImageSize</c> and writes it onto the WorldMapSprite). At
    /// E2.1a we hide that sprite and use the bounds-overload to feed
    /// the camera the iso bbox directly. A 1x1 fully-transparent
    /// <see cref="ImageTexture"/> satisfies the contract without
    /// allocating a bitmap-sized empty image.
    /// </summary>
    private static Texture2D MakeTransparentPlaceholderTexture()
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
        return ImageTexture.CreateFromImage(image);
    }
}
