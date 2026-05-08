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
/// E2 World Map -- the Cadastre's first feuillet. Slice 1 of L1 World
/// fondations (M3 / Arc 3 / Phase 9 slice 1, 2026-05-08) refactors this
/// screen along three axes :
/// <list type="bullet">
///   <item>The pan stack moves out of inline <c>_Ready</c> / <c>_Input</c>
///         / <c>_Process</c> code into a reusable
///         <see cref="MapPan2DComponent"/>. Every Phase 8.x invariant is
///         preserved verbatim (CanvasLayer trap, mouse_filter / _Input
///         precedence, AZERTY physical_keycode, wheel-during-drag
///         suppression with defense-in-depth, modal-owns-input,
///         post-drag grace, RMB threshold) ; behaviour is unchanged,
///         the code lives in a different file.</item>
///   <item>A new <see cref="FogTileLayer"/> renders Sprite2D-per-cell
///         fog placeholders above the iso ground (Varn 2026-05-08
///         <c>Tiles as Cartography and Fog</c> §1.2 / §4.6 stack).</item>
///   <item>A <see cref="TileKnowledgeStore"/> child node holds the
///         <see cref="TileKnowledgeState"/> dictionary (livrable 3
///         skeleton). Slice 1 wires only the binary
///         <see cref="TileKnowledgeState.Inconnue"/> / <see cref="TileKnowledgeState.Levee"/>
///         visuals — the three intermediate states are reserved in the
///         enum but treated as Inconnue in the renderer until slice 2.</item>
/// </list>
///
/// <para>
/// <b>What this screen still owns directly.</b>
/// <list type="bullet">
///   <item>POI spawn + hover tooltip + click dispatch (Halfgate-only
///         Varn §6.D6.10 lock). POIs are spawned as children of
///         <see cref="MapPan2DComponent.PoiContainer"/> so they pan with
///         the camera for free.</item>
///   <item>Decorative chrome (banner, panels, layer indicator, back
///         button, blocked indicator). All in CanvasLayer 2 / 3 above
///         the world tree, unchanged from pre-extraction.</item>
///   <item>The slice 1 debug toggle (F key) for the
///         <see cref="TileKnowledgeStore"/> — flips the cell under
///         the cursor between Inconnue (opaque fog) and Levée (hidden
///         fog). Validation surface for livrable 3 ; will be retired
///         when slice 2's actual zoom-driven drill ladders supply the
///         real state mutations.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why POIs stay here, not in the component.</b> POI spawn is tied
/// to <see cref="OpeningStrings"/> / <see cref="WorldMapPois"/> /
/// <see cref="PoiDispatchLogic"/> -- screen-specific data and routing
/// that does not belong inside a generic 2D pan component. The
/// component exposes the world tree as a public API ; the screen owns
/// what spawns into it. This keeps <see cref="MapPan2DComponent"/>
/// reusable for future scenes (E3 cité, ex-E5 quartier) that have
/// different POI grammars.
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
    private const string HalfgatePoiId = "halfgate";

    private const float BlockedFadeInSeconds = 0.1f;
    private const float BlockedHoldSeconds = 2.0f;
    private const float BlockedFadeOutSeconds = 0.2f;

    /// <summary>
    /// Slice 1 debug action name. Bound to the F key in
    /// <c>project.godot</c> ; toggles the knowledge state of the cell
    /// under the cursor between Inconnue and Levée (livrable 3 exit
    /// criterion). Held in a constant so a future rename surfaces every
    /// consumer at compile time.
    /// </summary>
    private const string DebugToggleFogActionName = "debug_toggle_fog_cell";

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

    private MapPan2DComponent _panComponent = null!;
    private FogTileLayer _fogTileLayer = null!;
    private TileKnowledgeStore _knowledgeStore = null!;

    private OpeningStrings _strings = null!;
    private WorldMapPois _poisResource = null!;

    /// <summary>
    /// Per-POI handler bag — same Risk #1 disconnect discipline as
    /// pre-extraction. Spawn parents under
    /// <see cref="MapPan2DComponent.PoiContainer"/>.
    /// </summary>
    private readonly Dictionary<Area2D, PoiAreaHandlers> _poiHandlers = new();

    /// <summary>Reverse lookup poi-id -&gt; hotspot Node2D root for hover tooltip anchor.</summary>
    private readonly Dictionary<string, Node2D> _poiHotspots = new();

    private Tween? _blockedTween;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();
        _poisResource = ResourceLoader.Load<WorldMapPois>(WorldMapPoisResPath) ?? new WorldMapPois();

        _panComponent = GetNode<MapPan2DComponent>("MapPan2DComponent");
        _fogTileLayer = GetNode<FogTileLayer>("MapPan2DComponent/WorldRoot/FogTileLayer");
        _knowledgeStore = GetNode<TileKnowledgeStore>("TileKnowledgeStore");

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

        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        var worldTexture = assetResolver.Resolve(WorldMapBackgroundAssetKey);
        _bannerTop.Texture = assetResolver.Resolve(BannerTopAssetKey);

        _ = assetResolver.Resolve(PanelLeftAssetKey);   // pre-warm cache slot
        _ = assetResolver.Resolve(PanelRightAssetKey);  // pre-warm cache slot

        // Configure the pan component with the world texture and an
        // initial center on Halfgate (D-P8.2-08, "tu commences là où
        // l'action est"). The component handles camera limits and
        // initial-clamp internally.
        var halfgate = FindPoi(HalfgatePoiId);
        var imageSize = worldTexture.GetSize();
        var initialCenter = halfgate?.Position ?? imageSize / 2f;
        _panComponent.Configure(worldTexture, initialCenter);

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

        // Configure the fog layer AFTER the pan component is configured
        // (so the world image size is known) and AFTER POIs are spawned
        // (so the z-index ordering on POIs is in place when the fog
        // layer's own z-index sits above). The placeholder texture is a
        // 1×1 white pixel — modulate-tinted to beige-écru carton inside
        // the fog renderer, scaled to cell size at spawn.
        var fogPlaceholder = MakePlaceholderCartonTexture();
        _fogTileLayer.Configure(_panComponent.WorldImageSize, _knowledgeStore, fogPlaceholder);

        // Pre-flight self-check on the slice 1 debug action. If the
        // project.godot binding is missing the F key will silently no-op
        // and livrable 3 cannot be demonstrated.
        if (!InputMap.HasAction(DebugToggleFogActionName))
        {
            GD.PushWarning(
                $"[E2WorldMap] preflight: {DebugToggleFogActionName} action missing from InputMap. " +
                "Slice 1 fog toggle (F key) will silently no-op.");
        }
    }

    public override void _ExitTree()
    {
        if (_backButton is not null) _backButton.Pressed -= OnBackPressed;

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

        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        tooltipController?.CancelTooltip();

        _blockedTween?.Kill();
        _blockedTween = null;
    }

    /// <summary>
    /// Slice 1 debug command — F key toggles the knowledge state of the
    /// cell under the cursor. Lives on <see cref="_UnhandledInput"/> so
    /// the pan component (which uses <see cref="_Input"/>) cannot
    /// accidentally swallow the keystroke, and so a future modal that
    /// claims input via Control's mouse_filter still suppresses the
    /// toggle when relevant.
    ///
    /// <para>
    /// The cursor's world-space position is computed by feeding the
    /// viewport-space mouse position through the WorldCamera's inverse
    /// transform. Camera2D exposes <c>GetCanvasTransform()</c> which is
    /// the same matrix Godot uses to project the world into the
    /// viewport — inverting it maps a viewport coord back to world
    /// coords correctly even when the camera is mid-pan.
    /// </para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (!InputMap.EventIsAction(@event, DebugToggleFogActionName, exactMatch: false)) return;
        if (IsModalOpen()) return;

        var viewport = GetViewport();
        var mouseViewportPos = viewport.GetMousePosition();
        var canvasTransform = viewport.GetCanvasTransform();
        var worldPos = canvasTransform.AffineInverse() * mouseViewportPos;

        var coord = _fogTileLayer.WorldPositionToCell(worldPos);
        if (coord is null)
        {
            GD.Print($"[E2WorldMap] debug toggle: cursor at {worldPos} is outside grid bounds");
            return;
        }

        var before = _knowledgeStore.GetState(coord.Value);
        _knowledgeStore.ToggleAtCell(coord.Value);
        var after = _knowledgeStore.GetState(coord.Value);
        GD.Print(
            $"[E2WorldMap] debug toggle: cell ({coord.Value.Col},{coord.Value.Row}) " +
            $"{before} -> {after}");
        viewport.SetInputAsHandled();
    }

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

    private string ResolveTooltipText(PoiDefinition poi)
    {
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

    private bool IsModalOpen()
    {
        var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
        return sceneManager?.ActiveModalId is not null;
    }

    /// <summary>
    /// Build a 1×1 white <see cref="ImageTexture"/> for the slice 1 fog
    /// placeholder. The fog renderer scales it to cell size and tints
    /// it via Modulate to the carton beige-écru. When slice 2 ships
    /// the real Mira parchment asset, swap this for an
    /// <c>AssetResolver.Resolve("e2.fog_carton_placeholder")</c> and
    /// drop a key in <c>asset_keys.json</c>.
    /// </summary>
    private static Texture2D MakePlaceholderCartonTexture()
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, new Color(1, 1, 1, 1));
        return ImageTexture.CreateFromImage(image);
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

    private readonly record struct PoiAreaHandlers(
        Area2D.InputEventEventHandler InputEvent,
        Action MouseEntered,
        Action MouseExited);
}
