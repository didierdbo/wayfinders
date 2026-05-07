using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E5 District Map -- intra-cité quartier zoom (Halfgate, quartier du
/// marché). J5 promotes this from the J1 three-button stub to a real
/// screen, copy-shape of E3 (D-J5-08) with quartier-specific add-ons:
/// <list type="bullet">
///   <item>Background <c>e5.halfgate.marche.base</c> (D-J5-09 + Mira §7
///         E5.1 naming convention).</item>
///   <item>Title <c>Halfgate -- Quartier du marché. Feuillet II.b --
///         plan détaillé.</c> (Varn §6 E5.A).</item>
///   <item>Layer indicator <c>Pile cadastrale : quartier.</c> (Varn
///         §7.5 vocabulaire ferme extension D-J5-07).</item>
///   <item>Back button <c>Revenir à la cité</c> (Varn §6 E5.C).</item>
///   <item>2 POIs: 1 AmbientNpc (homme du marché, Varn E5.I voix D
///         première instance UI persistante) + 1 NpcCandidate Kira
///         cross-scene (D-J5-10 reuse).</item>
/// </list>
///
/// <para>
/// <b>Reuse identique des 3 panneaux E3 (D-J5-08).</b> PanelLeft
/// (compagnie), PanelRight (contrats + TabContainer 2 onglets),
/// PanelBottom (journal voix A neutre stub). Same strings, same layout,
/// same StyleBox. Le sentiment de drill-down vient du bandeau title +
/// layer indicator + le content per-quartier (post-MVP), pas de
/// nouveaux panneaux.
/// </para>
///
/// <para>
/// <b>POI multi-Kind dispatch (D-J5-10).</b> Reuses
/// <see cref="PoiDispatchLogic"/> verbatim, including the J4 defense-in-depth
/// for AmbientNpc (the dispatcher routes to ShowBlockedIndicator
/// regardless of <see cref="PoiDefinition.IsClickable"/>). The
/// NpcCandidate (kira) opens E4 modal exactly as from E3 -- same
/// payload contract <see cref="E3CityHalfgate.NpcIdPayloadKey"/>
/// thread.
/// </para>
///
/// <para>
/// <b>Modal invariant (J3-fix locked).</b> Opening E4 from a
/// NpcCandidate click leaves E5 visible underneath -- the modal sits on
/// <see cref="SceneManager"/>'s <c>_modalLayer</c> (CanvasLayer
/// Layer=10). <see cref="OnSuspend"/> only disables input + dims the
/// screen ; never touches visibility. Same shape as
/// <see cref="E3CityHalfgate.OnSuspend"/>.
/// </para>
///
/// <para>
/// <b>Esc handling.</b> Not bound here -- the SceneManager autoload
/// already maps <c>ui_cancel</c> to <see cref="SceneManager.NavigateBack"/>.
/// The "Revenir à la cité" button calls <see cref="SceneManager.NavigateBack"/>
/// too (the navigation stack pops back to E3, which is what was at the
/// top before E5 was pushed).
/// </para>
///
/// <para>
/// <b>Risk #1 mitigation reused J3 (lambda-capture per POI).</b> Each
/// POI button gets its own dispatcher lambdas captured in
/// <see cref="_poiHandlers"/> ; <see cref="_ExitTree"/> disconnects
/// with the EXACT references used at wire time (method-group disconnect
/// would not match a closure capturing per-POI id).
/// </para>
///
/// <para>
/// <b>Risk #3 mitigation reused J3 (tooltip orphelin).</b>
/// <see cref="HoverTooltipController.CancelTooltip"/> is called on
/// <c>_ExitTree</c> so a 600ms-pending tooltip cannot fire on the next
/// screen.
/// </para>
/// </summary>
public partial class E5DistrictMap : Control, IScreen
{
    public string ScreenId => "E5_DISTRICT";

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

    private TextureRect _background = null!;
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

    private Control _poiContainer = null!;

    private OpeningStrings _strings = null!;
    private HalfgateMarchePois _poisResource = null!;

    /// <summary>
    /// One entry per spawned POI button, mirrors J4 E3 pattern. Three
    /// Action references are stored so <c>_ExitTree</c> can disconnect
    /// with the exact lambdas used at wire time -- method-group
    /// disconnect would not match a closure capturing the per-POI id.
    /// </summary>
    private readonly Dictionary<TextureButton, PoiHandlerSet> _poiHandlers = new();

    /// <summary>
    /// Reverse lookup poi-id -&gt; button for the hover anchor.
    /// </summary>
    private readonly Dictionary<string, TextureButton> _poiButtons = new();

    private Tween? _blockedTween;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();
        _poisResource = ResourceLoader.Load<HalfgateMarchePois>(DistrictPoisResPath) ?? new HalfgateMarchePois();

        _background = GetNode<TextureRect>("BackgroundLayer/DistrictBackground");

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

        _poiContainer = GetNode<Control>("PoiLayer/PoiContainer");

        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _background.Texture = assetResolver.Resolve(DistrictBackgroundAssetKey);
        _bannerTop.Texture = assetResolver.Resolve(BannerTopAssetKey);
        _ = assetResolver.Resolve(PanelLeftAssetKey);
        _ = assetResolver.Resolve(PanelRightAssetKey);
        _ = assetResolver.Resolve(PanelBottomAssetKey);

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

        GD.Print($"[E5DistrictMap] ready, {_poiHandlers.Count} POI spawned");
    }

    public override void _ExitTree()
    {
        if (_backButton is not null) _backButton.Pressed -= OnBackPressed;

        // Disconnect every POI handler with the EXACT lambda references
        // captured at wire time (Risk #1 reused J3/J4).
        foreach (var (button, handlers) in _poiHandlers)
        {
            if (button is null) continue;
            button.Pressed -= handlers.Pressed;
            button.MouseEntered -= handlers.MouseEntered;
            button.MouseExited -= handlers.MouseExited;
            button.QueueFree();
        }
        _poiHandlers.Clear();
        _poiButtons.Clear();

        // Risk #3: cancel any pending tooltip timer so the autoload
        // does not surface a tooltip on the next screen mid-fade.
        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        tooltipController?.CancelTooltip();

        _blockedTween?.Kill();
        _blockedTween = null;
    }

    private void SpawnPois(AssetResolver assetResolver)
    {
        foreach (var poi in _poisResource.Pois)
        {
            if (poi is null) continue;

            var button = new TextureButton
            {
                TextureNormal = assetResolver.Resolve(poi.AssetKey),
                Position = poi.Position,
                Size = poi.Size,
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = poi.IsClickable
                    ? CursorShape.PointingHand
                    : CursorShape.Forbidden,
                Modulate = poi.IsClickable
                    ? new Color(1, 1, 1, 1)
                    : new Color(0.85f, 0.78f, 0.68f, 0.85f),
            };

            var poiId = poi.PoiId;
            Action pressed = () => OnPoiPressed(poiId);
            Action mouseEntered = () => OnPoiHoverIn(poiId);
            Action mouseExited = () => OnPoiHoverOut(poiId);

            button.Pressed += pressed;
            button.MouseEntered += mouseEntered;
            button.MouseExited += mouseExited;

            _poiContainer.AddChild(button);
            _poiHandlers[button] = new PoiHandlerSet(pressed, mouseEntered, mouseExited);
            _poiButtons[poiId] = button;
        }
    }

    private async void OnBackPressed()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        await sceneManager.NavigateBack();
    }

    private async void OnPoiPressed(string poiId)
    {
        var poi = FindPoi(poiId);
        if (poi is null)
        {
            GD.PushWarning($"[E5DistrictMap] POI clicked but no entry found: {poiId}");
            return;
        }

        var result = PoiDispatchLogic.Dispatch(poi.ToDto());
        switch (result.Outcome)
        {
            case PoiDispatchOutcome.NavigateToScreen:
                GD.Print($"[E5DistrictMap] POI clicked: {poiId} ({poi.Kind}) -> navigating to {result.TargetScreenId}");
                var sceneManagerNav = GetNode<SceneManager>("/root/SceneManager");
                await sceneManagerNav.NavigateTo(result.TargetScreenId!);
                break;

            case PoiDispatchOutcome.OpenModal:
                GD.Print($"[E5DistrictMap] POI clicked: {poiId} ({poi.Kind}) -> opening modal {result.ModalId} for npc {result.NpcId}");
                var sceneManagerModal = GetNode<SceneManager>("/root/SceneManager");
                var ctx = new ScreenContext
                {
                    Payload = new Dictionary<string, object>
                    {
                        // SAME payload key as E3 -- cross-scene contract
                        // verified by E5NavigationContractTests.
                        [E3CityHalfgate.NpcIdPayloadKey] = result.NpcId ?? string.Empty,
                    },
                };
                await sceneManagerModal.OpenModal(result.ModalId!, ctx);
                break;

            case PoiDispatchOutcome.ShowBlockedIndicator:
                GD.Print($"[E5DistrictMap] POI clicked: {poiId} ({poi.Kind}) -> blocked, {result.IndicatorTextKey}");
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

        var anchor = _poiButtons.TryGetValue(poiId, out var button)
            ? button.GlobalPosition
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

    public Task OnEnter(ScreenContext context, CancellationToken ct) => Task.CompletedTask;

    public Task OnExit(CancellationToken ct) => Task.CompletedTask;

    public Task OnSuspend(CancellationToken ct)
    {
        // Same shape as E3CityHalfgate.OnSuspend: disable input + dim
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
    /// Keeps the three lambda references wired on a POI button so
    /// <c>_ExitTree</c> disconnects them with the exact same Action
    /// references used at wire time. Pattern reused J3/J4.
    /// </summary>
    private readonly record struct PoiHandlerSet(
        Action Pressed,
        Action MouseEntered,
        Action MouseExited);
}
