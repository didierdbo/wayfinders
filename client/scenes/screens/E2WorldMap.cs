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
/// fondations refactored the pan stack into <see cref="MapPan2DComponent"/>
/// and added the fog scaffold ; slice 2 (this revision, 2026-05-08) wires
/// the baked palette source, paints palette swatches behind translucent
/// cartons, animates the pliure-soulèvement, and grows the debug surface
/// to F / Shift+F / Ctrl+F / Alt+F.
///
/// <para>
/// <b>What this screen owns directly.</b>
/// <list type="bullet">
///   <item>POI spawn + hover tooltip + click dispatch (Halfgate-only
///         Varn §6.D6.10 lock). POIs are children of
///         <see cref="MapPan2DComponent.PoiContainer"/>.</item>
///   <item>Decorative chrome (banner, panels, layer indicator, back
///         button, blocked indicator). All in CanvasLayer 2 / 3 above
///         the world tree, unchanged.</item>
///   <item>The slice 2 debug commands for the
///         <see cref="TileKnowledgeStore"/> :
///         <c>F</c> cycle next state on the cell under the cursor,
///         <c>Shift+F</c> cycle previous, <c>Ctrl+F</c> reset all to
///         Inconnue, <c>Alt+F</c> flip every cell to Levée. Validation
///         surface for livrable 4. Will be retired once slice 3+'s
///         zoom-driven drill ladders supply the real state mutations.</item>
///   <item>Construction of the <see cref="BakedFogPaletteSource"/> from
///         the editor-baked PNG (slice 2 livrable 1). Falls back to
///         neutral-carton if the bake doesn't exist yet — the slice 2
///         scaffold is observable on a fresh checkout pre-bake.</item>
/// </list>
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

    /// <summary>
    /// Path of the editor-baked palette PNG. Default matches the slice 1
    /// <see cref="Wayfinders.Client.Tools.PaletteQuantizerTool"/> output
    /// for the E2.1 master at 128 px cells. Pre-bake (fresh checkout) the
    /// path resolves to nothing and the
    /// <see cref="BakedFogPaletteSource"/> falls back to neutral-carton —
    /// no crash, slice 2 scaffold remains observable.
    /// </summary>
    private const string BakedPaletteResPath = "res://assets/baked/e2_world_palette_3teintes_128px.png";

    private const float BlockedFadeInSeconds = 0.1f;
    private const float BlockedHoldSeconds = 2.0f;
    private const float BlockedFadeOutSeconds = 0.2f;

    /// <summary>
    /// Slice 1+2 debug action name. Bound to the F key in
    /// <c>project.godot</c>. The slice 2 handler reads modifier state
    /// off the InputEventKey to dispatch between cycle-next /
    /// cycle-previous / reset / flip-all (livrable 4 exit criterion).
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
        // l'action est").
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
        // layer's own z-index sits above). Slice 2 wires the baked
        // palette source : compute grid dimensions from the world image
        // size first, then construct the source against that expected
        // shape so a stale bake (different cell size / world image)
        // triggers fallback rather than silent off-by-one.
        var fogPlaceholder = MakePlaceholderCartonTexture();
        var expectedDimensions = FogTileGridLogic.ComputeGridSize(
            new PanVec2(_panComponent.WorldImageSize.X, _panComponent.WorldImageSize.Y),
            _fogTileLayer.CellSizePx);
        var paletteSource = new BakedFogPaletteSource(BakedPaletteResPath, expectedDimensions);
        _fogTileLayer.Configure(_panComponent.WorldImageSize, _knowledgeStore, fogPlaceholder, paletteSource);

        // Pre-flight self-check on the slice 2 debug action. If the
        // project.godot binding is missing, the F-key surface silently
        // no-ops and livrable 4 cannot be demonstrated.
        if (!InputMap.HasAction(DebugToggleFogActionName))
        {
            GD.PushWarning(
                $"[E2WorldMap] preflight: {DebugToggleFogActionName} action missing from InputMap. " +
                "Slice 2 fog cycle commands (F / Shift+F / Ctrl+F / Alt+F) will silently no-op.");
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
    /// Slice 2 debug surface (livrable 4). The F key dispatches based on
    /// modifier state read off the <see cref="InputEventKey"/> :
    /// <list type="bullet">
    ///   <item><b>F (no modifier)</b> — cycle next state on the cell
    ///         under the cursor (Inconnue → Pressentie → Esquissée → Levée
    ///         → Scellée → Inconnue).</item>
    ///   <item><b>Shift+F</b> — cycle previous state on the cell under
    ///         the cursor.</item>
    ///   <item><b>Ctrl+F</b> — reset every non-default cell to Inconnue
    ///         (bulk wipe).</item>
    ///   <item><b>Alt+F</b> — flip every cell of the grid to Levée
    ///         (global event smoke test, brief slice 2 livrable 4).</item>
    /// </list>
    ///
    /// <para>
    /// <b>Why <see cref="_UnhandledInput"/>, not <see cref="_Input"/>.</b>
    /// The pan component's <c>_Input</c> handles MMB/RMB drag and
    /// wheel-during-drag suppression — running there would force
    /// careful coordination to avoid the keystroke being swallowed by
    /// the pan component's <c>SetInputAsHandled</c> path. Running on
    /// <c>_UnhandledInput</c> means a future modal that claims input
    /// via Control's mouse_filter still suppresses the toggle, and the
    /// pan component's wheel-during-drag suppression cannot accidentally
    /// catch a key event. Same precedence reasoning as slice 1.
    /// </para>
    ///
    /// <para>
    /// <b>Modifier order matters.</b> We check Alt before Ctrl before
    /// Shift so that Ctrl+Alt+F (a future combined binding) does not
    /// silently take the Ctrl branch. For slice 2 the modifiers are
    /// mutually exclusive : the brief lists four discrete combos. The
    /// dispatcher logs the chosen path so console output makes the
    /// active modifier obvious during smoke tests.
    /// </para>
    ///
    /// <para>
    /// <b>InputMap action match (AZERTY-safe).</b> The action
    /// <c>debug_toggle_fog_cell</c> is bound on physical_keycode 70 (F)
    /// per project.godot, so <see cref="InputMap.EventIsAction"/>
    /// returns true regardless of modifier state on the action's
    /// definition. The modifier check happens after the action match,
    /// not as part of it — same pattern as slice 1, AZERTY-safe by
    /// inheritance from the action binding.
    /// </para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (!InputMap.EventIsAction(@event, DebugToggleFogActionName, exactMatch: false)) return;
        if (IsModalOpen()) return;

        var viewport = GetViewport();

        if (key.AltPressed)
        {
            // Alt+F : flip every cell to Levée. No cursor-cell lookup
            // needed.
            var dimensions = _fogTileLayer.Dimensions;
            _knowledgeStore.SetAllToLevee(dimensions);
            GD.Print(
                $"[E2WorldMap] debug Alt+F: flipped every cell to Levée " +
                $"({dimensions.Columns}×{dimensions.Rows} = {dimensions.TotalCells} cells)");
            viewport.SetInputAsHandled();
            return;
        }

        if (key.CtrlPressed)
        {
            // Ctrl+F : reset every non-default cell to Inconnue.
            var before = _knowledgeStore.NonDefaultEntryCount;
            _knowledgeStore.Clear();
            GD.Print(
                $"[E2WorldMap] debug Ctrl+F: reset all non-default cells to Inconnue " +
                $"(was {before} entries, now 0)");
            viewport.SetInputAsHandled();
            return;
        }

        // F or Shift+F : cell-under-cursor cycle. Resolve the cursor's
        // world-space position via the viewport's canvas transform.
        var mouseViewportPos = viewport.GetMousePosition();
        var canvasTransform = viewport.GetCanvasTransform();
        var worldPos = canvasTransform.AffineInverse() * mouseViewportPos;

        var coord = _fogTileLayer.WorldPositionToCell(worldPos);
        if (coord is null)
        {
            GD.Print($"[E2WorldMap] debug F: cursor at {worldPos} is outside grid bounds");
            return;
        }

        var before2 = _knowledgeStore.GetState(coord.Value);
        if (key.ShiftPressed)
        {
            _knowledgeStore.CyclePreviousAtCell(coord.Value);
        }
        else
        {
            _knowledgeStore.CycleNextAtCell(coord.Value);
        }
        var after = _knowledgeStore.GetState(coord.Value);
        var direction = key.ShiftPressed ? "Shift+F (prev)" : "F (next)";
        GD.Print(
            $"[E2WorldMap] debug {direction}: cell ({coord.Value.Col},{coord.Value.Row}) " +
            $"{before2} -> {after}");
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
    /// Build a 1×1 white <see cref="ImageTexture"/> for the slice 1+2 fog
    /// placeholder. The fog renderer scales it to cell size and tints
    /// it via Modulate to the carton beige-écru. When slice 3+ ships
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
