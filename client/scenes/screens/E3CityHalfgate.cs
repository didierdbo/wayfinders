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
/// (2026-05-08) migrates the screen-space TextureRect + CanvasLayer
/// pattern (P7 J3) to a world-space <see cref="MapPan2DComponent"/>
/// instance, mirroring E2's shape.
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
///         (<c>city_halfgate_pois.tres</c>) live in the source-image
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
/// <b>Out of scope, slice 4.</b>
/// <list type="bullet">
///   <item>No <see cref="FogTileLayer"/> at L2 -- the cité is fully
///         visible from arrival. A future slice 4.5 (Varn-validated)
///         could add cascade fog per §5.1, but MVP keeps L2 plain.</item>
///   <item>No L3 District drill from cap-Push at L2. With the resolver
///         intentionally unwired (no fog), <see cref="ZoomNavLogic"/>
///         silently no-ops on cap-Push at L2 (cellAtCursor is null,
///         DrillBlocked has no feedback target). Cap-Pull at L2 fires
///         <see cref="MapPan2DComponent.ClimbRequested"/> as expected.</item>
///   <item>No L1↔L2 fade transition -- flagged as ongoing dette. The
///         brief slice 4 marked it as triviality-gated ; SceneManager
///         currently has no fade hook in <see cref="SceneManager.NavigateTo"/>
///         and adding one would touch every IScreen consumer. Out of
///         this slice's risk budget.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>OriginCoord payload (slice 3 prep, slice 4 logged).</b> When E2
/// drills into E3, the payload carries <c>"E2.OriginCoord" = Vector2I</c>
/// (the L1 grid cell the drill fired on). Slice 4 logs it at <c>OnEnter</c>
/// for traceability but does NOT translate it into an L2 camera focus
/// -- the L1-grid-coord-to-L2-image-position mapping is undefined for
/// the MVP single-cité and would be arbitrary (the cell coord is in L1
/// grid space, not L2 city image space). E3 instead opens centered on
/// the city image center via <see cref="MapPan2DComponent.Configure"/>
/// with the texture's geometric center.
/// </para>
///
/// <para>
/// <b>Re-entry semantics (slice 4 livrable 3 + smoke #5/#6).</b> The
/// SceneManager Hide-not-Free model preserves E2's <c>MapPan2DComponent</c>
/// state (camera Position + Zoom) across drill+climb round trips --
/// when the player climbs out of E3, E2's camera is exactly where it
/// was at the moment of the drill (zoom=ZoomMax centered on the cell
/// the player was inspecting). E3 itself is freed on each Pop, so a
/// re-drill instantiates a fresh E3 with a fresh <c>MapPan2DComponent</c>
/// at <c>ZoomDefault</c> centered on the city image (no per-session
/// state). Acceptable MVP shape ; if Varn wants per-cité camera memory
/// later, a snapshot in <c>ScreenContext.Payload</c> is the entry seam.
/// </para>
///
/// <para>
/// <b>POI hotspot pattern (J3 D-J3-01 reused, slice 4 ported to
/// world-space).</b> POIs are <see cref="Node2D"/> hotspots spawned from
/// <c>res://data/city_halfgate_pois.tres</c> at <c>_Ready</c>, parented
/// to <c>MapPan2DComponent.PoiContainer</c>. Position + hit-box come
/// from each <see cref="PoiDefinition"/>. The Risk #1 lambda-capture
/// dictionary (<c>_poiHandlers</c>) keeps the per-POI lambdas around
/// so <c>_ExitTree</c> can disconnect them with the exact references
/// used at wire time -- captured-reference signal-leak discipline,
/// same shape as E2.
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
///         with the J4 stub key <c>E3KeyBuildingBlockedIndicator</c></item>
///   <item>AmbientNpc / Interactable -&gt; defense-in-depth blocked</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Modal invariant (J3-fix locked, unchanged by slice 4).</b> Opening
/// E4 from a NpcCandidate click leaves E3 visible underneath -- the
/// modal sits on <see cref="SceneManager"/>'s <c>_modalLayer</c>
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
public partial class E3CityHalfgate : Control, IScreen
{
    public string ScreenId => "E3_CITY_HALFGATE";

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
    public const string OriginCoordPayloadKey = "E2.OriginCoord";

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";
    private const string CityPoisResPath = "res://data/city_halfgate_pois.tres";
    private const string CityBackgroundAssetKey = "e3.halfgate.base";
    private const string BannerTopAssetKey = "e2.banner_top";
    private const string PanelLeftAssetKey = "e2.panel_left";
    private const string PanelRightAssetKey = "e2.panel_right";
    private const string PanelBottomAssetKey = "e3.panel_bottom";

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
    private CityHalfgatePois _poisResource = null!;

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
    // signal-leak discipline). DrillRequested / DrillBlocked are NOT
    // wired at L2 -- there is no L3 drill in scope this slice.
    private MapPan2DComponent.ClimbRequestedEventHandler? _climbRequestedHandler;

    private Tween? _blockedTween;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();
        _poisResource = ResourceLoader.Load<CityHalfgatePois>(CityPoisResPath) ?? new CityHalfgatePois();

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

        // Background + decoration textures (AssetResolver returns a
        // deterministic placeholder if the file is missing, so this never
        // null-refs even on a fresh checkout). Pre-warm panel slots --
        // the StyleBoxFlat in the .tscn already gives us the parchment
        // look, the asset slot exists for the Mira swap later.
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        var cityTexture = assetResolver.Resolve(CityBackgroundAssetKey);
        _bannerTop.Texture = assetResolver.Resolve(BannerTopAssetKey);
        _ = assetResolver.Resolve(PanelLeftAssetKey);
        _ = assetResolver.Resolve(PanelRightAssetKey);
        _ = assetResolver.Resolve(PanelBottomAssetKey);

        // Slice 4 -- configure the pan component with the city texture
        // and an initial center on the geometric center of the image.
        // The OriginCoord payload (set by E2 at drill) is read in
        // OnEnter for traceability but does NOT participate in the
        // initial center : the L1-grid-coord-to-L2-image-position
        // mapping is undefined for the MVP single-cité (different image
        // entirely), so a literal-coord re-center here would be
        // arbitrary. E3 always opens centered on the city image.
        var initialCenter = cityTexture.GetSize() / 2f;
        _panComponent.Configure(cityTexture, initialCenter);

        // Strings -- single point of swap when Varn revises the .tres.
        _bannerTitleLabel.Text = _strings.E3Title;
        _bannerSubtitleLabel.Text = _strings.E3Subtitle;
        _visibilityLabel.Text = _strings.E3VisibilityLabel;
        _menaceLabel.Text = _strings.E3MenaceLabel;
        _panelLeftTitleLabel.Text = _strings.E3PanelLeftTitle;
        _panelLeftBodyLabel.Text = _strings.E3PanelLeftBody;
        _panelRightTitleLabel.Text = _strings.E3PanelRightTitle;
        _routineStubLabel.Text = _strings.E3TabRoutineStub;
        _mandateStubLabel.Text = _strings.E3TabMandateStub;
        _panelBottomTitleLabel.Text = _strings.E3PanelBottomTitle;
        _panelBottomBodyLabel.Text = _strings.E3PanelBottomBody;
        _layerIndicatorLabel.Text = _strings.E3LayerIndicator;
        _backButton.Text = _strings.E3BackButton;
        _blockedIndicatorLabel.Text = _strings.E3KeyBuildingBlockedIndicator;

        // Godot quirk: TabContainer reads each child Node's Name as the
        // tab label. We rename children here to the Varn-locked libellés
        // so the .tscn (where node names cannot easily contain spaces +
        // accents in editor authoring without quirks) stays readable.
        // J4 D-J4-05 + Rune J4 pre-brief §11.2 backend-trap doc.
        var routineTab = _contractsTabContainer.GetNode<Control>("Routine");
        var mandateTab = _contractsTabContainer.GetNode<Control>("MandatsDesArpenteurs");
        routineTab.Name = _strings.E3TabRoutine;
        mandateTab.Name = _strings.E3TabMandate;

        _backButton.Pressed += OnBackPressed;

        SpawnPois(assetResolver);

        // Slice 4 -- subscribe to the climb signal. Captured-reference
        // discipline : store the handler in a field so _ExitTree can
        // disconnect with the exact same delegate (Risk #1, mirrors E2).
        // No DrillRequested / DrillBlocked subscription : without a fog
        // resolver wired (no L2 fog this slice), the component silently
        // no-ops on cap-Push at L2 ; subscribing would dead-code. When
        // L3 drill ships (slice 5+), wire DrillRequested then.
        _climbRequestedHandler = OnClimbRequested;
        _panComponent.ClimbRequested += _climbRequestedHandler;

        // Slice 4 -- preflight self-check (Phase 8 trap "preflight
        // GD.Print mandatory"). Surfaces the world-space migration's
        // success at boot : if the pan component or the climb signal
        // wiring is silently broken, the console line below is missing
        // / shows wrong values, giving an immediate visible diagnostic
        // before any user input.
        GD.Print(
            $"[E3CityHalfgate] ready, {_poiHandlers.Count} POI spawned, " +
            $"world image={_panComponent.WorldImageSize}, " +
            $"camera initialCenter={initialCenter}, " +
            $"climb wired={_climbRequestedHandler is not null}");
    }

    public override void _ExitTree()
    {
        // Disconnect chrome handlers first.
        if (_backButton is not null) _backButton.Pressed -= OnBackPressed;

        // Slice 4 -- disconnect the climb signal using the captured
        // handler reference. If the pan component is still alive (it
        // should be, the screen owns it as a child and tree teardown
        // is bottom-up), the disconnect no-ops silently if it's not.
        if (_panComponent is not null && _climbRequestedHandler is not null)
        {
            _panComponent.ClimbRequested -= _climbRequestedHandler;
        }
        _climbRequestedHandler = null;

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

    private async void OnPoiPressed(string poiId)
    {
        var poi = FindPoi(poiId);
        if (poi is null)
        {
            GD.PushWarning($"[E3CityHalfgate] POI clicked but no entry found: {poiId}");
            return;
        }

        var result = PoiDispatchLogic.Dispatch(poi.ToDto());
        switch (result.Outcome)
        {
            case PoiDispatchOutcome.NavigateToScreen:
                GD.Print($"[E3CityHalfgate] POI clicked: {poiId} ({poi.Kind}) -> navigating to {result.TargetScreenId}");
                var sceneManager = GetNode<SceneManager>("/root/SceneManager");
                await sceneManager.NavigateTo(result.TargetScreenId!);
                break;

            case PoiDispatchOutcome.OpenModal:
                GD.Print($"[E3CityHalfgate] POI clicked: {poiId} ({poi.Kind}) -> opening modal {result.ModalId} for npc {result.NpcId}");
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
                GD.Print($"[E3CityHalfgate] POI clicked: {poiId} ({poi.Kind}) -> blocked, {result.IndicatorTextKey}");
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
            ? hotspot.GlobalPosition
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
            "E3PoiDistrictTooltipTemplate" =>
                _strings.E3PoiDistrictTooltipTemplate.Replace("[nom]", poi.DisplayName),
            "E3PoiKeyBuildingHqTooltip" =>
                _strings.E3PoiKeyBuildingHqTooltip,
            "E3PoiCandidateTooltip" =>
                _strings.E3PoiCandidateTooltip,
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
            "E3KeyBuildingBlockedIndicator" => _strings.E3KeyBuildingBlockedIndicator,
            "E2PoiBlockedIndicator" => _strings.E2PoiBlockedIndicator,
            _ => _strings.E3KeyBuildingBlockedIndicator,
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
    public Task OnEnter(ScreenContext context, CancellationToken ct)
    {
        if (context.Payload.TryGetValue(OriginCoordPayloadKey, out var raw)
            && raw is Vector2I originCoord)
        {
            GD.Print(
                $"[E3CityHalfgate] OnEnter: OriginCoord = ({originCoord.X}, {originCoord.Y}) " +
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
}
