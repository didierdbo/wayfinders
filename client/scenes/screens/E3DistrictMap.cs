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
/// E5 District Map -- intra-cité quartier zoom (Halfgate, quartier du
/// marché). <b>Phase 9 slice 5</b> (2026-05-08) migrates the screen-space
/// TextureRect + CanvasLayer pattern (J5 P7) to a world-space
/// <see cref="MapPan2DComponent"/> instance, mirroring E2 / E3 shape post
/// slice-3 / slice-4. Net effect : on E5 the player can pan
/// (MMB/RMB/ZQSD), zoom continuous (wheel), and climb back to E3 via
/// PULL at <see cref="MapPan2DComponent.ZoomMin"/>.
///
/// <para>
/// <b>Slice 5 changes (2026-05-08).</b>
/// <list type="bullet">
///   <item>The Halfgate marché background bitmap is now the texture of
///         <c>MapPan2DComponent/WorldRoot/WorldMapSprite</c> (instead
///         of a screen-anchored <c>TextureRect</c> under
///         <c>BackgroundLayer</c> CanvasLayer). Pan + continuous zoom
///         are inherited from the component verbatim.</item>
///   <item>POI hotspots spawn under <see cref="MapPan2DComponent.PoiContainer"/>
///         as <see cref="Node2D"/> roots holding a <see cref="Sprite2D"/>
///         and an <see cref="Area2D"/>+<see cref="CollisionShape2D"/>
///         hitbox -- same shape as E2 post slice-1 / E3 post slice-4.
///         POI source coords (<c>halfgate_marche_pois.tres</c>) live in
///         the source-image pixel frame, which is the world frame for
///         the WorldRoot tree, so positions transfer 1-1 from the
///         pre-slice-5 J5 era.</item>
///   <item>Climb wiring : the pan component's <see cref="MapPan2DComponent.ClimbRequested"/>
///         signal is connected to <see cref="OnClimbRequested"/>, which
///         calls <see cref="SceneManager.NavigateBack"/> (popping back
///         to E3). Hide-not-Free model on the autoload preserves E3's
///         camera state across the L2-drill-then-L3-climb round trip --
///         no explicit snapshot needed (slice 5 livrable 4, mirrors
///         slice 4 livrable 3 verbatim).</item>
///   <item>The chrome layers (<c>DecorationLayer</c>, <c>ChromeLayer</c>)
///         remain on <see cref="CanvasLayer"/>s above the world tree --
///         deliberately exempt from pan and zoom (banner + back button +
///         journal panel always visible at fixed screen positions). The
///         "CanvasLayer hides Camera2D" trap from
///         feedback_godot_rendering_input_traps does NOT apply here
///         because the world content lives directly under the Control
///         root via <c>MapPan2DComponent</c>, not under a CanvasLayer.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Out of scope, slice 5 (Didier locks, ne pas re-arbitrer).</b>
/// <list type="bullet">
///   <item><b>(a)</b> No fog L2 nor fog L3 -- aucun système de tuiles
///         cette slice. If Varn wants fog later, slice 5.5 dédiée.</item>
///   <item><b>(α)</b> Drill libre L2→L3 sans prédicat "tuile connue" --
///         not relevant here (E5 = L3 = bottom of the cadastral stack
///         in MVP scope, no further drill below).</item>
///   <item>No L2↔L3 fade transition -- flagged as ongoing dette,
///         identical to slice 4's L1↔L2 dette. The brief slice 5
///         marks it as triviality-gated.</item>
///   <item>No combat tactique sur E5 -- "DistrictMap = 1 perso = 1
///         tuile tactical" est une autre milestone (M4+), pas slice 5.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Re-entry semantics (slice 5 livrable 4 + smoke #6/#7/#8).</b> The
/// SceneManager Hide-not-Free model preserves E3's <c>MapPan2DComponent</c>
/// state (camera Position + Zoom) across drill+climb round trips --
/// when the player climbs out of E5, E3's camera is exactly where it
/// was at the moment of the L2→L3 drill (zoom=ZoomMax centered on the
/// position the player was inspecting in Halfgate). E5 itself is freed
/// on each Pop (single instance Hide-not-Free, identical to E3 in
/// slice 4) so a re-drill from E3 instantiates a fresh E5 with a fresh
/// <c>MapPan2DComponent</c> at <c>ZoomDefault</c> centered on the
/// district image (no per-session state). Acceptable MVP shape.
/// </para>
///
/// <para>
/// <b>POI hotspot pattern (J5 D-J5-10 reused, slice 5 ported to
/// world-space).</b> POIs are <see cref="Node2D"/> hotspots spawned
/// from <c>res://data/halfgate_marche_pois.tres</c> at <c>_Ready</c>,
/// parented to <see cref="MapPan2DComponent.PoiContainer"/>. Position
/// + hit-box come from each <see cref="PoiDefinition"/>. The Risk #1
/// lambda-capture dictionary (<see cref="_poiHandlers"/>) keeps the
/// per-POI lambdas around so <c>_ExitTree</c> can disconnect them with
/// the exact references used at wire time -- captured-reference
/// signal-leak discipline, same shape as E2 / E3 post-world-space.
/// </para>
///
/// <para>
/// <b>Multi-Kind dispatch via PoiDispatchLogic (D-J5-10 unchanged).</b>
/// Click goes through the pure-C# helper :
/// <list type="bullet">
///   <item>NpcCandidate clickable (Kira reused cross-scene) -&gt;
///         <see cref="PoiDispatchOutcome.OpenModal"/> (E4) with the npc id
///         threaded through <c>ScreenContext.Payload["E4.NpcId"]</c></item>
///   <item>AmbientNpc (homme du marché) -&gt; defense-in-depth
///         <see cref="PoiDispatchOutcome.ShowBlockedIndicator"/>
///         regardless of <see cref="PoiDefinition.IsClickable"/></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Modal invariant (J3-fix locked, unchanged by slice 5).</b> Opening
/// E4 from a NpcCandidate click leaves E5 visible underneath -- the
/// modal sits on <see cref="SceneManager"/>'s <c>_modalLayer</c>
/// (CanvasLayer Layer=10). <see cref="OnSuspend"/> only disables input +
/// dims the screen ; never touches visibility.
/// </para>
///
/// <para>
/// <b>Esc handling.</b> Not bound here -- the SceneManager autoload
/// already maps <c>ui_cancel</c> to <see cref="SceneManager.NavigateBack"/>.
/// The "Revenir à la cité" button calls <see cref="SceneManager.NavigateBack"/>
/// too. PULL-at-zoom-min is a third equivalent path (slice 5).
/// </para>
///
/// <para>
/// <b>OriginCoord payload (slice 5 logged).</b> When E3 drills into E5,
/// the payload carries <c>"E3.OriginCoord" = Vector2</c> (the L2 image-space
/// world position the drill fired on). Slice 5 logs it at
/// <see cref="OnEnter"/> for traceability but does NOT translate it into
/// an L3 camera focus -- the L2-image-position-to-L3-image-position
/// mapping is undefined for the MVP single-quartier (different image
/// entirely) and would be arbitrary. E5 always opens centered on the
/// district image center via <see cref="MapPan2DComponent.Configure"/>
/// with the texture's geometric center. Same logged-but-not-applied
/// shape as E3's <c>E2.OriginCoord</c> handling in slice 4.
/// </para>
///
/// <para>
/// <b>Risk #3 mitigation (tooltip orphelin) reused J3.</b>
/// <see cref="HoverTooltipController.CancelTooltip"/> is called on
/// <c>_ExitTree</c> so a 600 ms-pending tooltip cannot fire on the next
/// screen.
/// </para>
/// </summary>
public partial class E3DistrictMap : Control, IScreen
{
    public string ScreenId => "E3_DISTRICT";

    /// <summary>
    /// Slice 5 -- payload key consumed at <see cref="OnEnter"/> when E3
    /// drills here. Carries the <see cref="Vector2"/> L2 image-space
    /// world position the drill originated from. Logged for traceability ;
    /// not currently translated into an L3 camera focus (see class XML
    /// doc OriginCoord section). Mirrored on the E3 side as the same
    /// string literal so the contract is one writer (E3) and one reader
    /// (E5) keyed by an inline string -- the same shape as
    /// <see cref="E2AreaMap.NpcIdPayloadKey"/> and
    /// <see cref="E2AreaMap.OriginCoordPayloadKey"/>.
    /// </summary>
    public const string OriginCoordPayloadKey = "E3.OriginCoord";

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";
    private const string DistrictPoisResPath = "res://data/halfgate_marche_pois.tres";
    private const string DistrictBackgroundAssetKey = "e5.halfgate.marche.base";
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
    private HalfgateMarchePois _poisResource = null!;

    /// <summary>
    /// Slice 5 -- per-POI handler bag. Three lambda references kept on
    /// each spawned hotspot's <see cref="Area2D"/> so <c>_ExitTree</c>
    /// disconnects with the exact same references used at wire time.
    /// Same shape as E3 post-slice-4 (Risk #1 captured-reference
    /// signal-leak discipline).
    /// </summary>
    private readonly Dictionary<Area2D, PoiAreaHandlers> _poiHandlers = new();

    /// <summary>
    /// Reverse lookup poi-id -&gt; hotspot Node2D root, for the tooltip
    /// hover anchor.
    /// </summary>
    private readonly Dictionary<string, Node2D> _poiHotspots = new();

    // Slice 5 -- captured handler reference for the climb signal so
    // _ExitTree can disconnect with the exact same delegate (Risk #1
    // signal-leak discipline). DrillRequested / DrillBlocked are NOT
    // wired at L3 -- there is no L4 drill in scope, MVP bottom of the
    // cadastral stack. The component silently no-ops on cap-Push at L3
    // because we never call SetDrillTargetResolver.
    private MapPan2DComponent.ClimbRequestedEventHandler? _climbRequestedHandler;

    private Tween? _blockedTween;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();
        _poisResource = ResourceLoader.Load<HalfgateMarchePois>(DistrictPoisResPath) ?? new HalfgateMarchePois();

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

        // Background + decoration textures. AssetResolver returns a
        // deterministic placeholder if the file is missing, so this
        // never null-refs even on a fresh checkout.
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        var districtTexture = assetResolver.Resolve(DistrictBackgroundAssetKey);
        _bannerTop.Texture = assetResolver.Resolve(BannerTopAssetKey);
        _ = assetResolver.Resolve(PanelLeftAssetKey);
        _ = assetResolver.Resolve(PanelRightAssetKey);
        _ = assetResolver.Resolve(PanelBottomAssetKey);

        // Slice 5 -- configure the pan component with the district
        // texture and an initial center on the geometric center of the
        // image. The OriginCoord payload (set by E3 at drill) is read
        // in OnEnter for traceability but does NOT participate in the
        // initial center : the L2-image-position-to-L3-image-position
        // mapping is undefined for the MVP single-quartier (different
        // image entirely), so a literal-coord re-center here would be
        // arbitrary. E5 always opens centered on the district image.
        var initialCenter = districtTexture.GetSize() / 2f;
        _panComponent.Configure(districtTexture, initialCenter);

        // Strings -- single point of swap when Varn revises the .tres.
        _bannerTitleLabel.Text = _strings.E5Title;
        _bannerSubtitleLabel.Text = _strings.E5Subtitle;
        _visibilityLabel.Text = _strings.E5VisibilityLabel;
        _menaceLabel.Text = _strings.E5MenaceLabel;

        // 3 panels reuse identical to E3 (D-J5-08): same titles + same
        // bodies + same TabContainer wiring. The drill-down sentiment
        // comes from the title bandeau + layer indicator, not different
        // panels per quartier. Per-quartier panel content lands post-MVP.
        _panelLeftTitleLabel.Text = _strings.E3PanelLeftTitle;
        _panelLeftBodyLabel.Text = _strings.E3PanelLeftBody;
        _panelRightTitleLabel.Text = _strings.E3PanelRightTitle;
        _routineStubLabel.Text = _strings.E3TabRoutineStub;
        _mandateStubLabel.Text = _strings.E3TabMandateStub;
        _panelBottomTitleLabel.Text = _strings.E3PanelBottomTitle;
        _panelBottomBodyLabel.Text = _strings.E3PanelBottomBody;

        _layerIndicatorLabel.Text = _strings.E5LayerIndicator;
        _backButton.Text = _strings.E5BackButton;

        // Same Godot quirk as E3: TabContainer reads each child Node's
        // Name as the tab label. Rename children to the Varn-locked
        // libellés.
        var routineTab = _contractsTabContainer.GetNode<Control>("Routine");
        var mandateTab = _contractsTabContainer.GetNode<Control>("MandatsDesArpenteurs");
        routineTab.Name = _strings.E3TabRoutine;
        mandateTab.Name = _strings.E3TabMandate;

        _backButton.Pressed += OnBackPressed;

        SpawnPois(assetResolver);

        // Slice 5 -- subscribe to the climb signal. Captured-reference
        // discipline : store the handler in a field so _ExitTree can
        // disconnect with the exact same delegate (Risk #1, mirrors E3
        // post-slice-4). No DrillRequested / DrillBlocked subscription
        // and no SetDrillTargetResolver call : L3 is the bottom of the
        // MVP cadastral stack ; the component silently no-ops on
        // cap-Push at L3 (cellAtCursor is null because no resolver
        // wired, ZoomNavLogic returns DrillBlocked which we don't
        // listen to ; effectively a silent ceiling).
        _climbRequestedHandler = OnClimbRequested;
        _panComponent.ClimbRequested += _climbRequestedHandler;

        // Slice 5 -- preflight self-check (Phase 8 trap "preflight
        // GD.Print mandatory"). Surfaces the world-space migration's
        // success at boot : if the pan component or the climb signal
        // wiring is silently broken, the console line below is missing
        // / shows wrong values, giving an immediate visible diagnostic
        // before any user input.
        GD.Print(
            $"[E3DistrictMap] ready, {_poiHandlers.Count} POI spawned, " +
            $"world image={_panComponent.WorldImageSize}, " +
            $"camera initialCenter={initialCenter}, " +
            $"climb wired={_climbRequestedHandler is not null}");
    }

    public override void _ExitTree()
    {
        if (_backButton is not null) _backButton.Pressed -= OnBackPressed;

        // Slice 5 -- disconnect the climb signal using the captured
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
    /// Slice 5 -- spawn POIs as <see cref="Node2D"/> hotspots under
    /// <see cref="MapPan2DComponent.PoiContainer"/>, each holding a
    /// <see cref="Sprite2D"/> for visuals and an <see cref="Area2D"/>+
    /// <see cref="CollisionShape2D"/> for click hit-testing. Same shape
    /// as E3 post-slice-4 -- positions in the source-image pixel frame
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
            // reused J3+E2-slice-1+E3-slice-4 pattern).
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
    /// Slice 5 -- ClimbRequested handler. The pan component fires this
    /// when the player Pulls the wheel at <see cref="MapPan2DComponent.ZoomMin"/>.
    /// On L3, climbing means popping back to the screen that pushed us
    /// here (E3 cité Halfgate) -- <see cref="SceneManager.NavigateBack"/>.
    /// Hide-not-Free preserves E3's camera state across the round trip so
    /// when E3 is re-shown, the camera is still on the position the
    /// player drilled from at the same zoom level (Hide preserves
    /// Camera2D.Position + Zoom for free -- the node tree never died).
    ///
    /// <para>
    /// The transition lock on the pan component is released after
    /// <see cref="SceneManager.NavigateBack"/> completes, regardless of
    /// outcome. Wrapped in <c>try/finally</c> against the rare throw path
    /// -- if NavigateBack ever blows up (registration missing, etc), the
    /// player still gets input back on next interaction. Defensive
    /// null-check mirrors E3's pattern.
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
            // E5 is being freed by NavigateBack ; the pan component
            // tween is killed in MapPan2DComponent._ExitTree. The
            // NotifyTransitionEnded call here is harmless on a node
            // about to be freed (idempotent flag write) but kept for
            // symmetry with E2 / E3 -- if a future slice retains E5
            // (Hide-not-Free climb), the lock release is still needed.
            _panComponent?.NotifyTransitionEnded();
        }
    }

    private async void OnPoiPressed(string poiId)
    {
        var poi = FindPoi(poiId);
        if (poi is null)
        {
            GD.PushWarning($"[E3DistrictMap] POI clicked but no entry found: {poiId}");
            return;
        }

        var result = PoiDispatchLogic.Dispatch(poi.ToDto());
        switch (result.Outcome)
        {
            case PoiDispatchOutcome.NavigateToScreen:
                GD.Print($"[E3DistrictMap] POI clicked: {poiId} ({poi.Kind}) -> navigating to {result.TargetScreenId}");
                var sceneManagerNav = GetNode<SceneManager>("/root/SceneManager");
                await sceneManagerNav.NavigateTo(result.TargetScreenId!);
                break;

            case PoiDispatchOutcome.OpenModal:
                GD.Print($"[E3DistrictMap] POI clicked: {poiId} ({poi.Kind}) -> opening modal {result.ModalId} for npc {result.NpcId}");
                var sceneManagerModal = GetNode<SceneManager>("/root/SceneManager");
                var ctx = new ScreenContext
                {
                    Payload = new Dictionary<string, object>
                    {
                        // SAME payload key as E3 -- cross-scene contract
                        // verified by E5NavigationContractTests.
                        [E2AreaMap.NpcIdPayloadKey] = result.NpcId ?? string.Empty,
                    },
                };
                await sceneManagerModal.OpenModal(result.ModalId!, ctx);
                break;

            case PoiDispatchOutcome.ShowBlockedIndicator:
                GD.Print($"[E3DistrictMap] POI clicked: {poiId} ({poi.Kind}) -> blocked, {result.IndicatorTextKey}");
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
    /// Resolve the tooltip text for a POI based on its
    /// <see cref="PoiDefinition.TooltipKey"/>. J5 surfaces 2 keys:
    /// <c>E5AmbientHommeTooltip</c> (voix D Marges, première instance UI)
    /// and <c>E3PoiCandidateTooltip</c> (reuse, kira cross-scene).
    /// </summary>
    private string ResolveTooltipText(PoiDefinition poi)
    {
        if (string.IsNullOrEmpty(poi.TooltipKey))
            return string.Empty;

        return poi.TooltipKey switch
        {
            "E5AmbientHommeTooltip" => _strings.E5AmbientHommeTooltip,
            "E3PoiCandidateTooltip" => _strings.E3PoiCandidateTooltip,
            "E3PoiDistrictTooltipTemplate" =>
                _strings.E3PoiDistrictTooltipTemplate.Replace("[nom]", poi.DisplayName),
            _ => string.Empty,
        };
    }

    private void ShowBlockedFlash(string? indicatorTextKey)
    {
        _blockedTween?.Kill();

        var text = indicatorTextKey switch
        {
            "E2PoiBlockedIndicator" => _strings.E2PoiBlockedIndicator,
            "E3KeyBuildingBlockedIndicator" => _strings.E3KeyBuildingBlockedIndicator,
            _ => _strings.E2PoiBlockedIndicator,
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
    /// Slice 5 -- log the OriginCoord payload from E3 for traceability.
    /// The coord is not consumed visually (see class XML doc OriginCoord
    /// section : the L2-image-position-to-L3-image-position mapping is
    /// undefined for the MVP single-quartier), but having the print
    /// statement makes the drill-then-arrive contract observable in the
    /// console -- a smoke-test affordance, identical to E3's slice-4
    /// OnEnter shape.
    /// </summary>
    public Task OnEnter(ScreenContext context, CancellationToken ct)
    {
        if (context.Payload.TryGetValue(OriginCoordPayloadKey, out var raw)
            && raw is Vector2 originCoord)
        {
            GD.Print(
                $"[E3DistrictMap] OnEnter: OriginCoord = ({originCoord.X:F1}, {originCoord.Y:F1}) " +
                "(logged for traceability ; not used for L3 camera focus this slice)");
        }
        return Task.CompletedTask;
    }

    public Task OnExit(CancellationToken ct) => Task.CompletedTask;

    public Task OnSuspend(CancellationToken ct)
    {
        // Same shape as E2AreaMap.OnSuspend: disable input + dim
        // while E4 modal is open. Visibility stays ON so the modal
        // overlay can render above E5 -- modal invariant J3-fix locked.
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
    /// Slice 5 -- per-POI handler bag. Three references kept on each
    /// spawned hotspot's Area2D so <c>_ExitTree</c> disconnects with the
    /// exact same references used at wire time. Same shape as E3
    /// post-slice-4 (Risk #1 captured-reference signal-leak discipline).
    /// </summary>
    private readonly record struct PoiAreaHandlers(
        Area2D.InputEventEventHandler InputEvent,
        Action MouseEntered,
        Action MouseExited);
}
