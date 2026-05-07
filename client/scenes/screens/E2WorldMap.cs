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
/// E2 World Map -- the Cadastre's first feuillet. J3 promotes this from
/// the J1 two-button stub to a proper screen: bureau-cadastral
/// background, top banner with title + subtitle, two iso fixed-pos
/// panels (left voix A "La compagnie", right voix D "Marges du Codex"),
/// bottom-right layer indicator, bottom-left "Refermer le feuillet"
/// back button, and a hotspot system spawning POI markers from
/// <see cref="WorldMapPois"/>.
///
/// <para>
/// <b>POI hotspot pattern (J3 D-J3-01).</b> POI are <c>TextureButton</c>
/// instances spawned from <c>res://data/world_map_pois.tres</c> at
/// <c>_Ready</c>, parented to <c>PoiLayer/PoiContainer</c>. Position +
/// hit-box come from each <see cref="PoiDefinition"/>. <c>Area2D</c> was
/// rejected (pre-brief §2.1): E2 lives in the Control tree, switching
/// part of it to Node2D world coordinates would be a mismatch ;
/// <c>TextureButton</c> ships with hover/click/disable signals built-in.
/// </para>
///
/// <para>
/// <b>Halfgate-only navigation gate (Varn §6.D6.10 lock 1-cité MVP).</b>
/// Click dispatch goes through pure-C# <see cref="PoiDispatchLogic"/>:
/// clickable POI -&gt; SceneManager.NavigateTo ; non-clickable POI -&gt;
/// flash the bottom-center BlockedIndicator label for 2.3s
/// (100ms fade-in, 2000ms hold, 200ms fade-out). The flash matters --
/// a silent <c>GD.Print</c> would leave the player wondering whether
/// they hit a bug or a mechanic. Spec: "pas de modale, pas un GD.Print
/// silencieux" (pre-brief §3.6).
/// </para>
///
/// <para>
/// <b>Lambda-capture trap pattern (Risk #1, backend-brain trap).</b>
/// The naive wire <c>button.Pressed += () => OnPoiPressed(poi.PoiId)</c>
/// captures a fresh lambda per POI, which cannot be disconnected by
/// method-group at <c>_ExitTree</c> -- you must keep the same Action
/// reference. Pattern adopted: a <see cref="Dictionary{TKey,TValue}"/>
/// from <c>TextureButton</c> to the three lambdas wired on it
/// (Pressed, MouseEntered, MouseExited). At teardown we iterate the
/// dictionary and disconnect each pair, then <c>QueueFree</c> the button.
/// Without the dictionary, freeing E2 leaves dangling subscriptions on
/// orphan buttons -- the next hover or click on the auto-loader's stale
/// queue throws an "object disposed" exception. xUnit cannot catch
/// this -- it relies on disconnection discipline. Documented in code
/// here so the pattern is teachable and reusable for E3 quartiers and
/// E5 ambient PNJ POIs at the next jalon.
/// </para>
///
/// <para>
/// <b>Risk #3 mitigation (tooltip orphelin).</b>
/// <see cref="HoverTooltipController.CancelTooltip"/> is called on
/// <c>_ExitTree</c>. If the user hovered a POI then hit Esc / Refermer
/// le feuillet within 600ms, the tooltip's pending timer would fire on
/// the next screen if not cancelled here.
/// </para>
///
/// <para>
/// <b>Esc handling (D-J3-11).</b> Not bound here -- the SceneManager
/// autoload already maps the engine-default <c>ui_cancel</c> action
/// (Esc) to <see cref="SceneManager.NavigateBack"/>, so Esc on E2
/// returns to E1 with no extra wiring. The "Refermer le feuillet"
/// button calls <see cref="SceneManager.NavigateBack"/> too -- same
/// intent, separate input surface.
/// </para>
/// </summary>
public partial class E2WorldMap : Control, IScreen
{
    public string ScreenId => "E2_WORLD";

    private const string OpeningStringsResPath = "res://data/opening_strings.tres";
    private const string WorldMapPoisResPath = "res://data/world_map_pois.tres";
    private const string WorldMapBackgroundAssetKey = "e2.world_map";
    private const string BannerTopAssetKey = "e2.banner_top";
    private const string PanelLeftAssetKey = "e2.panel_left";
    private const string PanelRightAssetKey = "e2.panel_right";

    private const float BlockedFadeInSeconds = 0.1f;
    private const float BlockedHoldSeconds = 2.0f;
    private const float BlockedFadeOutSeconds = 0.2f;

    private TextureRect _background = null!;
    private TextureRect _bannerTop = null!;
    private Label _bannerTitleLabel = null!;
    private Label _bannerSubtitleLabel = null!;

    private PanelContainer _panelLeft = null!;
    private Label _panelLeftTitleLabel = null!;
    private Label _panelLeftBodyLabel = null!;

    private PanelContainer _panelRight = null!;
    private Label _panelRightTitleLabel = null!;
    private Label _panelRightBodyLabel = null!;

    private Label _layerIndicatorLabel = null!;

    private Button _backButton = null!;
    private PanelContainer _blockedIndicator = null!;
    private Label _blockedIndicatorLabel = null!;

    private Control _poiContainer = null!;

    private OpeningStrings _strings = null!;
    private WorldMapPois _poisResource = null!;

    /// <summary>
    /// One entry per spawned POI button. The three Action references
    /// are stored so <c>_ExitTree</c> can disconnect with the exact
    /// lambdas used at wire time -- method-group disconnect would not
    /// match a closure capturing the per-POI id.
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
        _poisResource = ResourceLoader.Load<WorldMapPois>(WorldMapPoisResPath) ?? new WorldMapPois();

        _background = GetNode<TextureRect>("BackgroundLayer/WorldMapBackground");
        _bannerTop = GetNode<TextureRect>("DecorationLayer/BannerTop");
        _bannerTitleLabel = GetNode<Label>("DecorationLayer/BannerTop/BannerTitleLabel");
        _bannerSubtitleLabel = GetNode<Label>("DecorationLayer/BannerTop/BannerSubtitleLabel");

        _panelLeft = GetNode<PanelContainer>("DecorationLayer/PanelLeft");
        _panelLeftTitleLabel = GetNode<Label>("DecorationLayer/PanelLeft/PanelLeftVBox/PanelLeftTitleLabel");
        _panelLeftBodyLabel = GetNode<Label>("DecorationLayer/PanelLeft/PanelLeftVBox/PanelLeftBodyLabel");

        _panelRight = GetNode<PanelContainer>("DecorationLayer/PanelRight");
        _panelRightTitleLabel = GetNode<Label>("DecorationLayer/PanelRight/PanelRightVBox/PanelRightTitleLabel");
        _panelRightBodyLabel = GetNode<Label>("DecorationLayer/PanelRight/PanelRightVBox/PanelRightBodyLabel");

        _layerIndicatorLabel = GetNode<Label>("DecorationLayer/LayerIndicatorLabel");

        _backButton = GetNode<Button>("ChromeLayer/BackButton");
        _blockedIndicator = GetNode<PanelContainer>("ChromeLayer/BlockedIndicator");
        _blockedIndicatorLabel = GetNode<Label>("ChromeLayer/BlockedIndicator/BlockedIndicatorLabel");

        _poiContainer = GetNode<Control>("PoiLayer/PoiContainer");

        // Background + decoration textures (AssetResolver returns a
        // deterministic placeholder if the file is missing, so this never
        // null-refs even on a fresh checkout).
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _background.Texture = assetResolver.Resolve(WorldMapBackgroundAssetKey);
        _bannerTop.Texture = assetResolver.Resolve(BannerTopAssetKey);

        // Panel iso frames are decorative -- the StyleBoxFlat in the .tscn
        // already gives us the parchment look. When Mira ships the real
        // panneau iso PNGs, swap the StyleBoxFlat for a NinePatchRect or
        // a TextureRect underneath each PanelContainer. J3 stub fixed-pos.
        _ = assetResolver.Resolve(PanelLeftAssetKey);   // pre-warm cache slot
        _ = assetResolver.Resolve(PanelRightAssetKey);  // pre-warm cache slot

        // Strings -- single point of swap when Varn revises the .tres.
        _bannerTitleLabel.Text = _strings.E2Title;
        _bannerSubtitleLabel.Text = _strings.E2Subtitle;
        _panelLeftTitleLabel.Text = _strings.E2PanelLeftTitle;
        _panelLeftBodyLabel.Text = _strings.E2PanelLeftBody;
        _panelRightTitleLabel.Text = _strings.E2PanelRightTitle;
        _panelRightBodyLabel.Text = _strings.E2PanelRightBody;
        _layerIndicatorLabel.Text = _strings.E2LayerIndicator;
        _backButton.Text = _strings.E2BackButton;
        _blockedIndicatorLabel.Text = _strings.E2PoiBlockedIndicator;

        _backButton.Pressed += OnBackPressed;

        SpawnPois(assetResolver);

        GD.Print($"[E2WorldMap] ready, {_poiHandlers.Count} POI spawned");
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
                // Tone down the suspended sprite without disabling the
                // node -- Disabled would suppress the hover signal we
                // need for the suspended-cadastre tooltip.
                Modulate = poi.IsClickable
                    ? new Color(1, 1, 1, 1)
                    : new Color(0.85f, 0.78f, 0.68f, 0.85f),
            };

            // Capture the PoiId once per closure so the dispatch reads the
            // right entry. The lambdas are *kept* in _poiHandlers so the
            // _ExitTree disconnect uses the same references.
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
            GD.PushWarning($"[E2WorldMap] POI clicked but no entry found: {poiId}");
            return;
        }

        var result = PoiDispatchLogic.Dispatch(poi.ToDto());
        switch (result.Outcome)
        {
            case PoiDispatchOutcome.NavigateToScreen:
                GD.Print($"[E2WorldMap] POI clicked: {poiId} -> navigating to {result.TargetScreenId}");
                var sceneManager = GetNode<SceneManager>("/root/SceneManager");
                await sceneManager.NavigateTo(result.TargetScreenId!);
                break;

            case PoiDispatchOutcome.ShowBlockedIndicator:
                GD.Print($"[E2WorldMap] POI clicked: {poiId} -> blocked, Cadastre suspendu");
                ShowBlockedFlash();
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

        // Anchor at the POI's global top-left ; the tooltip controller adds
        // its own offset so the panel floats above the marker.
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

    private string ResolveTooltipText(PoiDefinition poi)
    {
        // Halfgate uses its dedicated key (Varn E2.F instantiated).
        // Other POIs use the §3.1 E2.G template with [Nom] interpolation.
        if (!string.IsNullOrEmpty(poi.TooltipKey))
        {
            return poi.TooltipKey switch
            {
                "E2PoiHalfgateTooltip" => _strings.E2PoiHalfgateTooltip,
                _ => _strings.E2PoiCitySuspendedTooltipTemplate.Replace("[Nom]", poi.DisplayName),
            };
        }
        return _strings.E2PoiCitySuspendedTooltipTemplate.Replace("[Nom]", poi.DisplayName);
    }

    private void ShowBlockedFlash()
    {
        // Restart any in-flight flash so a rapid second click resets
        // the tween rather than stacking labels.
        _blockedTween?.Kill();

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
        ProcessMode = ProcessModeEnum.Disabled;
        return Task.CompletedTask;
    }

    public Task OnResume(CancellationToken ct)
    {
        ProcessMode = ProcessModeEnum.Inherit;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Keeps the three lambda references wired on a POI button so
    /// <c>_ExitTree</c> disconnects them with the exact same Action
    /// references used at wire time. See class doc Risk #1.
    /// </summary>
    private readonly record struct PoiHandlerSet(
        Action Pressed,
        Action MouseEntered,
        Action MouseExited);
}
