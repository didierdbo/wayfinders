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
/// E3 City Halfgate -- the Cadastre's Feuillet II for the unique MVP
/// cité (Varn §6.D6.10 lock 1-cité). J4 promotes this from the J1
/// three-button stub to a proper screen, copy-shape of E2 with cité-
/// specific add-ons (Rune J4 pre-brief D-J4-01).
///
/// <para>
/// <b>Add-ons vs E2.</b>
/// <list type="bullet">
///   <item><b>Sous-bandeau NM</b> (DecorationLayer): two static labels
///         <c>Visibilité 0/10.</c> + <c>Méfiance 0/10.</c> stub J4
///         (D-J4-04, real binding post-MVP).</item>
///   <item><b>Panneau bas Journal compagnie</b> (DecorationLayer): voix
///         A neutre stub (D-J4-06, voix C reserved post-MVP for the
///         real Journal mechanic).</item>
///   <item><b>TabContainer panneau droit</b> (DecorationLayer): two
///         tabs <c>Routine</c> / <c>Mandats des Arpenteurs</c> with
///         empty stub bodies (D-J4-05, D-J4-12 minimal styling).</item>
///   <item><b>POI multi-Kind dispatch</b>: District -&gt; E5,
///         NpcCandidate -&gt; E4 modal, KeyBuilding -&gt; blocked
///         flash. All three branches share the J3 lambda-capture
///         pattern (Risk #1) and the J3 HoverTooltipController autoload.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>POI hotspot pattern (J3 D-J3-01 reused).</b> POI are
/// <c>TextureButton</c> instances spawned from
/// <c>res://data/city_halfgate_pois.tres</c> at <c>_Ready</c>, parented
/// to <c>PoiLayer/PoiContainer</c>. Position + hit-box come from each
/// <see cref="PoiDefinition"/>. The same Risk #1 lambda-capture
/// dictionary keeps the per-POI lambdas around so <c>_ExitTree</c> can
/// disconnect them with the exact references used at wire time.
/// </para>
///
/// <para>
/// <b>Multi-Kind dispatch via PoiDispatchLogic.</b> Click goes through
/// the pure-C# helper extended in J4 with <see cref="PoiKind"/> branching:
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
/// <b>Modal invariant (J3-fix locked).</b> Opening E4 from a NpcCandidate
/// click leaves E3 visible underneath -- the modal sits on
/// <see cref="SceneManager"/>'s <c>_modalLayer</c> (CanvasLayer Layer=10).
/// The <c>OnSuspend</c> implementation here only disables input (and
/// dims the screen) -- it does NOT toggle visibility. Test
/// <see cref="E4ModalContractTests.OpenModal_E4_does_not_hide_E3_host"/>
/// pins this contract for the (E3, E4) couple specifically.
/// </para>
///
/// <para>
/// <b>Esc handling (D-J3-11 reused).</b> Not bound here -- the
/// SceneManager autoload already maps <c>ui_cancel</c> to
/// <see cref="SceneManager.NavigateBack"/>. The "Quitter la cité"
/// button calls <see cref="SceneManager.NavigateBack"/> too.
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
    private CityHalfgatePois _poisResource = null!;

    /// <summary>
    /// One entry per spawned POI button. The three Action references
    /// are stored so <c>_ExitTree</c> can disconnect with the exact
    /// lambdas used at wire time -- method-group disconnect would not
    /// match a closure capturing the per-POI id (Risk #1 reused J3).
    /// </summary>
    private readonly Dictionary<TextureButton, PoiHandlerSet> _poiHandlers = new();

    /// <summary>
    /// Reverse lookup poi-id -&gt; button. Populated alongside
    /// <see cref="_poiHandlers"/> at spawn. Used so the hover handler
    /// can fetch the button's <c>GlobalPosition</c> for the tooltip
    /// anchor without scanning the scene tree.
    /// </summary>
    private readonly Dictionary<string, TextureButton> _poiButtons = new();

    private Tween? _blockedTween;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();
        _poisResource = ResourceLoader.Load<CityHalfgatePois>(CityPoisResPath) ?? new CityHalfgatePois();

        _background = GetNode<TextureRect>("BackgroundLayer/CityBackground");

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

        // Background + decoration textures (AssetResolver returns a
        // deterministic placeholder if the file is missing, so this never
        // null-refs even on a fresh checkout). Pre-warm panel slots --
        // the StyleBoxFlat in the .tscn already gives us the parchment
        // look, the asset slot exists for the Mira swap later.
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _background.Texture = assetResolver.Resolve(CityBackgroundAssetKey);
        _bannerTop.Texture = assetResolver.Resolve(BannerTopAssetKey);
        _ = assetResolver.Resolve(PanelLeftAssetKey);
        _ = assetResolver.Resolve(PanelRightAssetKey);
        _ = assetResolver.Resolve(PanelBottomAssetKey);

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

        GD.Print($"[E3CityHalfgate] ready, {_poiHandlers.Count} POI spawned");
    }

    public override void _ExitTree()
    {
        // Disconnect chrome handlers first.
        if (_backButton is not null) _backButton.Pressed -= OnBackPressed;

        // Disconnect every POI handler with the EXACT lambda reference
        // captured at wire time. Method-group disconnect would not match
        // because each lambda closed over a distinct PoiId (Risk #1).
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

            // Capture the PoiId once per closure so the dispatch reads the
            // right entry. The lambdas are *kept* in _poiHandlers so the
            // _ExitTree disconnect uses the same references (Risk #1).
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

    public Task OnEnter(ScreenContext context, CancellationToken ct) => Task.CompletedTask;

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
    /// Keeps the three lambda references wired on a POI button so
    /// <c>_ExitTree</c> disconnects them with the exact same Action
    /// references used at wire time. See class doc Risk #1 (pattern J3).
    /// </summary>
    private readonly record struct PoiHandlerSet(
        Action Pressed,
        Action MouseEntered,
        Action MouseExited);
}
