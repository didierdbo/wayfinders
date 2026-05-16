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
/// and added the fog scaffold ; slice 2 (2026-05-08) wired the baked
/// palette source, painted palette swatches behind translucent cartons,
/// animated the pliure-soulèvement, and grew the debug surface to F /
/// Shift+F / Ctrl+F / Alt+F. Slice 3 (this revision) wires the zoom-driven
/// drill/climb pipeline : the wheel now drives Camera2D zoom continuously,
/// and at the limits dispatches drill (push at zoom max + tile >=
/// Esquissée) or climb (pull at zoom min) navigations through SceneManager.
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
///   <item>Slice 2 debug commands for the
///         <see cref="TileKnowledgeStore"/> :
///         <c>F</c> cycle next state on the cell under the cursor,
///         <c>Shift+F</c> cycle previous, <c>Ctrl+F</c> reset all to
///         Inconnue, <c>Alt+F</c> flip every cell to Levée.</item>
///   <item>Construction of the <see cref="BakedFogPaletteSource"/> from
///         the editor-baked PNG (slice 2 livrable 1). Falls back to
///         neutral-carton if the bake doesn't exist yet.</item>
///   <item><b>Slice 3.</b> Wires the drill-target resolver into
///         <see cref="MapPan2DComponent.SetDrillTargetResolver"/> so
///         the component can ask "what cell is under the cursor and what
///         is its knowledge state ?" without owning a fog-layer reference.
///         Subscribes to the component's <see cref="MapPan2DComponent.DrillRequested"/>,
///         <see cref="MapPan2DComponent.DrillBlocked"/>, and
///         <see cref="MapPan2DComponent.ClimbRequested"/> signals and
///         dispatches them to <see cref="SceneManager"/> /
///         <see cref="FogTileLayer.PlayDrillBlockedFeedback"/>.</item>
///   <item><b>Slice 3 livrable 5.</b> Debug HUD overlay toggled by the
///         <c>G</c> key (action <c>debug_toggle_zoom_overlay</c>,
///         physical_keycode 71 -- AZERTY-safe). Top-left CanvasLayer
///         holding a Label refreshed in <see cref="_Process"/> with the
///         current zoom, threshold deltas, cursor cell, knowledge state,
///         last <see cref="ZoomNavLogic"/> action, and debounce state.</item>
/// </list>
/// </para>
/// </summary>
public partial class E2WorldMap : Control, IScreen
{
    public string ScreenId => "E1_WORLD";

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

    /// <summary>
    /// Slice 3 livrable 5 debug action name. Bound to the G key in
    /// <c>project.godot</c> (physical_keycode 71). Toggles the zoom
    /// HUD overlay built in <see cref="BuildZoomOverlay"/>. AZERTY-safe
    /// by physical_keycode binding -- same pattern as slice 1+2's F
    /// binding.
    /// </summary>
    private const string DebugToggleZoomOverlayActionName = "debug_toggle_zoom_overlay";

    /// <summary>HUD label refresh cadence -- once every <c>n</c> frames.
    /// 6 frames at 60 fps = 10 Hz, fast enough to feel responsive,
    /// slow enough to not allocate a fresh string every frame.</summary>
    private const int ZoomOverlayRefreshEveryNFrames = 6;

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

    // Slice 3 -- captured handlers for the MapPan2DComponent zoom-nav
    // signals. Captured-reference disconnect at _ExitTree (Rune Risk #1
    // signal-leak discipline).
    private MapPan2DComponent.DrillRequestedEventHandler? _drillRequestedHandler;
    private MapPan2DComponent.DrillBlockedEventHandler? _drillBlockedHandler;
    private MapPan2DComponent.ClimbRequestedEventHandler? _climbRequestedHandler;

    // Slice 3 livrable 5 -- HUD overlay nodes. Built lazily on first G
    // toggle, freed on _ExitTree.
    private CanvasLayer? _zoomOverlayLayer;
    private Label? _zoomOverlayLabel;
    private bool _zoomOverlayVisible;
    private int _zoomOverlayFrameCounter;

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
        //
        // 2026-05-09 fix : we pass explicit world BOUNDS computed from the
        // iso fog grid extent, not the source bitmap size. The fog grid
        // (cellSize 128, IsoDiamondDown projection) extends past the
        // bitmap on both axes (e.g. 4224x2112 for a 2048x1024 source).
        // Pre-fix, the camera limits tracked the bitmap and viewport.Y
        // (1080) > bitmap.Y (1024) snapped Camera2D.Y to image-center via
        // ClampCameraCenter's snap-to-center branch, freezing vertical pan.
        // Bounds = max(bitmap, fogGrid) so any future asset that exceeds
        // the grid still keeps a pannable world.
        var halfgate = FindPoi(HalfgatePoiId);
        var imageSize = worldTexture.GetSize();
        var initialCenter = halfgate?.Position ?? imageSize / 2f;

        var fogGridDimensions = FogTileGridLogic.ComputeGridSize(
            new PanVec2(imageSize.X, imageSize.Y),
            _fogTileLayer.CellSizePx,
            _fogTileLayer.Projection);
        var fogGridBounds = FogTileGridLogic.ComputeGridWorldBounds(
            fogGridDimensions,
            _fogTileLayer.CellSizePx,
            _fogTileLayer.Projection);
        var worldBounds = new Vector2(
            Mathf.Max(imageSize.X, fogGridBounds.X),
            Mathf.Max(imageSize.Y, fogGridBounds.Y));
        GD.Print(
            $"[E2WorldMap] world bounds = max(bitmap {imageSize}, " +
            $"fogGrid ({fogGridBounds.X}, {fogGridBounds.Y}) at " +
            $"{fogGridDimensions.Columns}x{fogGridDimensions.Rows} cells x " +
            $"{_fogTileLayer.CellSizePx}px {_fogTileLayer.Projection}) = {worldBounds}");

        _panComponent.Configure(worldTexture, initialCenter, worldBounds);

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
            _fogTileLayer.CellSizePx,
            _fogTileLayer.Projection);
        var paletteSource = new BakedFogPaletteSource(BakedPaletteResPath, expectedDimensions);
        _fogTileLayer.Configure(_panComponent.WorldImageSize, _knowledgeStore, fogPlaceholder, paletteSource);

        // Slice 3.6 -- enable the iso atlas mode. Each fog cell now
        // displays a slice of the source world map texture (clipped to
        // its iso diamond shape via Polygon2D) instead of the uniform
        // carton beige. Same texture as MapPan2DComponent.WorldMapSprite
        // -- the AtlasTexture region coords are in the same world frame
        // as the WorldRoot tree, so each cell's bbox indexes the
        // correct slice of the source pixels.
        _fogTileLayer.EnableSourceMapAtlas(_panComponent.WorldMapSprite.Texture);

        // Slice 3 -- wire the drill-target resolver. The pan component
        // calls this every time a wheel event reaches the cap-Push
        // branch ; we look up the cell the cursor is over via the fog
        // layer's hit-test seam and read the knowledge store for its
        // current state. This keeps the pan component decoupled from
        // either system (single delegate, no node references).
        _panComponent.SetDrillTargetResolver(ResolveDrillTarget);

        // Slice 3 -- subscribe to the zoom-nav signals. Captured-reference
        // discipline : store the handler in a field so _ExitTree can
        // disconnect with the exact same delegate (Risk #1).
        _drillRequestedHandler = OnDrillRequested;
        _drillBlockedHandler = OnDrillBlocked;
        _climbRequestedHandler = OnClimbRequested;
        _panComponent.DrillRequested += _drillRequestedHandler;
        _panComponent.DrillBlocked += _drillBlockedHandler;
        _panComponent.ClimbRequested += _climbRequestedHandler;

        // Pre-flight self-check on the slice 2 + 3 debug actions. If the
        // project.godot bindings are missing, the F / G surfaces silently
        // no-op and livrables 4 + 5 cannot be demonstrated.
        if (!InputMap.HasAction(DebugToggleFogActionName))
        {
            GD.PushWarning(
                $"[E2WorldMap] preflight: {DebugToggleFogActionName} action missing from InputMap. " +
                "Slice 2 fog cycle commands (F / Shift+F / Ctrl+F / Alt+F) will silently no-op.");
        }
        if (!InputMap.HasAction(DebugToggleZoomOverlayActionName))
        {
            GD.PushWarning(
                $"[E2WorldMap] preflight: {DebugToggleZoomOverlayActionName} action missing from InputMap. " +
                "Slice 3 livrable 5 zoom overlay (G) will silently no-op.");
        }
    }

    public override void _ExitTree()
    {
        if (_backButton is not null) _backButton.Pressed -= OnBackPressed;

        // Slice 3 -- disconnect zoom-nav signals using the captured
        // handler references. If the pan component is still alive
        // (it should be, the screen owns it as a child and tree teardown
        // is bottom-up), the disconnects no-op silently if it's not.
        if (_panComponent is not null)
        {
            if (_drillRequestedHandler is not null) _panComponent.DrillRequested -= _drillRequestedHandler;
            if (_drillBlockedHandler is not null) _panComponent.DrillBlocked -= _drillBlockedHandler;
            if (_climbRequestedHandler is not null) _panComponent.ClimbRequested -= _climbRequestedHandler;
            _panComponent.SetDrillTargetResolver(null);
        }
        _drillRequestedHandler = null;
        _drillBlockedHandler = null;
        _climbRequestedHandler = null;

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

        // Slice 3 livrable 5 -- HUD overlay teardown. CanvasLayer +
        // Label children are tree-children of this screen so QueueFree
        // would be redundant ; the references are nulled for safety.
        _zoomOverlayLabel = null;
        _zoomOverlayLayer = null;
    }

    public override void _Process(double delta)
    {
        // Slice 3 livrable 5 -- HUD overlay refresh. Cheap enough to
        // run in _Process (one string allocation every 6 frames) ;
        // skipped entirely when the overlay is hidden.
        if (!_zoomOverlayVisible || _zoomOverlayLabel is null) return;

        _zoomOverlayFrameCounter++;
        if (_zoomOverlayFrameCounter < ZoomOverlayRefreshEveryNFrames) return;
        _zoomOverlayFrameCounter = 0;

        UpdateZoomOverlayText();
    }

    /// <summary>
    /// Slice 1+2 debug surface (F key) + slice 3 livrable 5 debug surface
    /// (G key). The dispatcher checks the matched action first, then
    /// branches.
    ///
    /// <para>
    /// <b>F (slice 2)</b> — cell-cursor cycle / Shift+F prev / Ctrl+F
    /// reset / Alt+F flip-all (see slice 2 doc).
    /// </para>
    ///
    /// <para>
    /// <b>G (slice 3 livrable 5)</b> — toggle the zoom HUD overlay.
    /// First press lazily builds the overlay nodes ; subsequent toggles
    /// flip <see cref="CanvasLayer.Visible"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Why <see cref="_UnhandledInput"/>, not <see cref="_Input"/>.</b>
    /// The pan component's <c>_Input</c> handles MMB/RMB drag and
    /// wheel-during-drag suppression -- running there would force
    /// careful coordination to avoid the keystroke being swallowed by
    /// the pan component's <c>SetInputAsHandled</c> path. Running on
    /// <c>_UnhandledInput</c> means a future modal that claims input
    /// via Control's mouse_filter still suppresses the toggle, and the
    /// pan component's wheel-during-drag suppression cannot accidentally
    /// catch a key event. Same precedence reasoning as slice 2.
    /// </para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (IsModalOpen()) return;

        var viewport = GetViewport();

        // Slice 3 livrable 5 -- G toggle.
        if (InputMap.EventIsAction(@event, DebugToggleZoomOverlayActionName, exactMatch: false))
        {
            ToggleZoomOverlay();
            viewport.SetInputAsHandled();
            return;
        }

        // Slice 1+2 debug action match.
        if (!InputMap.EventIsAction(@event, DebugToggleFogActionName, exactMatch: false)) return;

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

    /// <summary>
    /// Slice 3 -- delegate body wired into the pan component. Looks up
    /// the cell under the cursor and pairs it with its current knowledge
    /// state. Returns null when the cursor is outside the grid (the
    /// pan component then suppresses the drill candidate silently).
    /// </summary>
    private (GridCoord coord, TileKnowledgeState state)? ResolveDrillTarget(Vector2 cursorWorldPosition)
    {
        var coord = _fogTileLayer.WorldPositionToCell(cursorWorldPosition);
        if (coord is null) return null;
        var state = _knowledgeStore.GetState(coord.Value);
        return (coord.Value, state);
    }

    /// <summary>
    /// Slice 3 -- DrillRequested handler. Translates the Vector2I coord
    /// into a screen-context payload and routes through SceneManager
    /// to E3 City Halfgate. The transition lock on the pan component
    /// stays active until <see cref="MapPan2DComponent.NotifyTransitionEnded"/>
    /// is called from the await continuation.
    /// </summary>
    private async void OnDrillRequested(Vector2I coord)
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");

        // Pack the origin coord into the payload bag. Slice 4+ uses
        // this for two purposes : (a) the climb-return logic to snap
        // the L1 camera back to the originating cell, (b) the L2
        // viewport entry point ("you came from this corner of the
        // world map"). Today the slice doesn't read it back -- pre-
        // staged for slice 4.
        var payload = new ScreenContext
        {
            Payload = new Dictionary<string, object>
            {
                ["E2.OriginCoord"] = coord,
            },
        };

        try
        {
            await sceneManager.NavigateTo("E2_AREA", payload);
        }
        finally
        {
            // Release the transition lock on the pan component
            // regardless of NavigateTo's outcome. If NavigateTo throws
            // (registration missing, etc), we still want the player to
            // recover input on the next interaction.
            _panComponent.NotifyTransitionEnded();
        }
    }

    /// <summary>
    /// Slice 3 -- DrillBlocked handler. No navigation, just the visual
    /// pulse on the cell the player tried to drill into. The fog layer
    /// owns the tween (livrable 4 lock) so the screen just plumbs the
    /// signal through.
    /// </summary>
    private void OnDrillBlocked(Vector2I coord)
    {
        _fogTileLayer.PlayDrillBlockedFeedback(new GridCoord(coord.X, coord.Y));
    }

    /// <summary>
    /// Slice 3 -- ClimbRequested handler. Routes through
    /// <see cref="SceneManager.NavigateLadderUp"/> -- on L1 World, "climb
    /// out" is "go up one rung of the cadastral ladder", and L1 monde
    /// is the top rung. <see cref="LadderResolutionLogic.ResolveUpTarget"/>
    /// returns null at the top rung, so NavigateLadderUp silently no-ops
    /// here. That is the intent : there is nothing above monde.
    ///
    /// <para>
    /// <b>Regression fix (2026-05-09).</b> Pre-fix, this handler called
    /// <c>SceneManager.NavigateBack()</c>, which pops the navigation
    /// stack one level. On the opening flow E1 Title sits below E2 World,
    /// so a PULL gesture at <see cref="MapPan2DComponent.ZoomMin"/>
    /// teleported the player back to E1. The verb was wrong : back-pop
    /// is the Esc/back-button semantics ; the wheel ladder semantics
    /// are <c>NavigateLadderUp</c>, which respects the ladder spine and
    /// silently no-ops at the top rung. Pinned by
    /// <see cref="Wayfinders.Client.Tests.Opening.E2ClimbAtZoomMinDoesNotPopToE1Tests"/>.
    /// </para>
    ///
    /// <para>
    /// The transition lock is released after the verb returns, regardless
    /// of whether it actually navigated anywhere -- the wheel-event
    /// session ends either way.
    /// </para>
    /// </summary>
    private async void OnClimbRequested()
    {
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");
        try
        {
            await sceneManager.NavigateLadderUp();
        }
        finally
        {
            _panComponent.NotifyTransitionEnded();
        }
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

    /// <summary>
    /// J6 hotfix (2026-05-09 evening, locked) -- back-button click handler
    /// "Refermer le feuillet". The button label and intent are clear : the
    /// player closes the current feuillet (E2 World Map) and returns to
    /// whatever was below it. In production navigation (E1 -&gt; E2),
    /// <c>SceneManager.NavigateBack</c> pops the stack and shows E1 again.
    ///
    /// <para>
    /// <b>Why the F6 path matters.</b> When Godot launches E2 directly via
    /// <c>main_scene</c> or "Run Current Scene", the SceneManager stack is
    /// empty (the bootstrap registered every screen but did not push any) ;
    /// <c>NavigateBack</c> early-returns silently. J5 mistakenly mapped
    /// that path to <c>GetTree().Quit()</c>, which closes the entire editor
    /// app -- a destructive surprise. J6 corrects this : in F6 mode we
    /// <c>NavigateTo("E1_TITLE")</c> instead, simulating the upstream
    /// scene the player would see if they had reached E2 from E1. The
    /// editor stays open, the user can re-enter E2 by clicking
    /// "Nouvelle Partie" on E1, and F6 testing has a non-destructive exit
    /// path. The <c>Quit</c> behaviour was wrong : the BackButton's
    /// vocabulary is "fermer le feuillet", not "fermer le jeu".
    /// </para>
    ///
    /// <para>
    /// <b>Defense-in-depth.</b> If <c>E1_TITLE</c> is not registered (e.g.
    /// the bootstrap was gated by the <c>skip_opening_bootstrap</c> meta
    /// flag), <c>NavigateTo</c> emits its own warning and the click logs
    /// a diagnostic but otherwise becomes a no-op -- the editor still does
    /// not close. The unconditional <c>GD.Print</c> at entry stays so the
    /// user can confirm the <c>Pressed</c> signal fires (rules out an
    /// invisible Control overlay swallowing the click).
    /// </para>
    /// </summary>
    private async void OnBackPressed()
    {
        GD.Print("[E2WorldMap J6] BackButton clicked (Refermer le feuillet) -- delegating to SceneManager");
        var sceneManager = GetNode<SceneManager>("/root/SceneManager");

        // F6 fallback : in production, E2 sits above E1 on the stack and
        // NavigateBack pops to E1. In F6 mode, the stack is empty and
        // NavigateBack early-returns silently. NavigateTo("E1_TITLE")
        // simulates the upstream screen so the player sees a sensible
        // transition instead of a dead button OR a Quit (J5 regression).
        if (sceneManager.CurrentScreenId is null)
        {
            GD.Print(
                "[E2WorldMap J6] BackButton in F6 mode (SceneManager stack empty) " +
                "-- routing to E1_TITLE instead of quitting the editor");
            try
            {
                await sceneManager.NavigateTo("E1_TITLE");
            }
            catch (System.Exception e)
            {
                // E1 might not be registered (skip_opening_bootstrap meta).
                // Log and stay on E2 ; closing the editor app from a
                // BackButton click is never the right answer.
                GD.PushWarning(
                    $"[E2WorldMap J6] F6-mode NavigateTo(E1_TITLE) failed : {e.Message}. " +
                    $"Staying on E2 -- close the editor manually if you want to exit.");
            }
            return;
        }

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

    /// <summary>
    /// Slice 3 livrable 5 -- toggle the zoom HUD overlay. Lazily builds
    /// the CanvasLayer + Label on first call ; subsequent calls flip
    /// the layer's visibility.
    /// </summary>
    private void ToggleZoomOverlay()
    {
        if (_zoomOverlayLayer is null)
        {
            BuildZoomOverlay();
        }
        _zoomOverlayVisible = !_zoomOverlayVisible;
        if (_zoomOverlayLayer is not null)
        {
            _zoomOverlayLayer.Visible = _zoomOverlayVisible;
        }
        if (_zoomOverlayVisible)
        {
            UpdateZoomOverlayText(); // immediate refresh so the first frame is populated
        }
        GD.Print($"[E2WorldMap] zoom overlay toggled -> {_zoomOverlayVisible}");
    }

    /// <summary>
    /// Slice 3 livrable 5 -- build the zoom HUD overlay. CanvasLayer
    /// at layer 5 (above the world tree at 0/5/10, below the modal
    /// layer at 10 owned by SceneManager). Top-left anchored Label
    /// with a translucent dark background panel for readability over
    /// the parchment palette of the world image.
    /// </summary>
    private void BuildZoomOverlay()
    {
        _zoomOverlayLayer = new CanvasLayer
        {
            Name = "ZoomOverlayLayer",
            Layer = 5,
            Visible = false,
        };
        AddChild(_zoomOverlayLayer);

        var panel = new PanelContainer
        {
            Name = "ZoomOverlayPanel",
            Position = new Vector2(16, 16),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0.92f),
        };
        // Lightweight stylebox -- dark teal-grey to read against
        // parchment cream. Inline to avoid a .tres file for a debug-only
        // surface.
        var styleBox = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.10f, 0.13f, 0.88f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(0.91f, 0.85f, 0.72f, 0.5f),
            ContentMarginLeft = 12,
            ContentMarginTop = 8,
            ContentMarginRight = 12,
            ContentMarginBottom = 8,
        };
        panel.AddThemeStyleboxOverride("panel", styleBox);
        _zoomOverlayLayer.AddChild(panel);

        _zoomOverlayLabel = new Label
        {
            Name = "ZoomOverlayLabel",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = "(zoom overlay)",
        };
        _zoomOverlayLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.85f, 1f));
        panel.AddChild(_zoomOverlayLabel);
    }

    /// <summary>
    /// Slice 3 livrable 5 -- compose the HUD label content. Pulls
    /// every diagnostic surface off the pan component + the cursor's
    /// current cell + knowledge state, formats into a 6-line block.
    /// Allocation-friendly : a single string per refresh (every
    /// <see cref="ZoomOverlayRefreshEveryNFrames"/> frames).
    /// </summary>
    private void UpdateZoomOverlayText()
    {
        if (_zoomOverlayLabel is null) return;

        var zoom = _panComponent.CurrentZoom;
        var zoomMin = _panComponent.ZoomMin;
        var zoomMax = _panComponent.ZoomMax;
        var inThreshold = zoomMax * _panComponent.ZoomInThresholdRatio;
        var outThreshold = zoomMin * _panComponent.ZoomOutThresholdRatio;
        var deltaIn = inThreshold - zoom;        // positive = how far below the drill-in line
        var deltaOut = zoom - outThreshold;       // positive = how far above the climb-out line

        // Cell + state under cursor. Reuse the same hit-test path the
        // pan component uses, so the overlay can't drift from the
        // resolver's view.
        var viewport = GetViewport();
        var mouseViewportPos = viewport.GetMousePosition();
        var canvasTransform = viewport.GetCanvasTransform();
        var worldPos = canvasTransform.AffineInverse() * mouseViewportPos;
        var coord = _fogTileLayer.WorldPositionToCell(worldPos);
        var coordText = coord is { } c ? $"({c.Col}, {c.Row})" : "(outside)";
        var stateText = coord is { } cs
            ? _knowledgeStore.GetState(cs).ToString()
            : "—";

        // Debounce surface : the pan component exposes IsDebouncing
        // (live boolean) + LastTransitionTimeMs (timestamp). Compute
        // the remaining ms for a more useful HUD readout.
        string debounceText;
        if (_panComponent.IsDebouncing)
        {
            var nowMs = Time.GetTicksMsec();
            var remaining = _panComponent.ZoomPostTransitionDebounceMs - (nowMs - _panComponent.LastTransitionTimeMs);
            debounceText = $"ON {remaining}ms";
        }
        else
        {
            debounceText = "OFF";
        }

        _zoomOverlayLabel.Text =
            $"Zoom : {zoom:F2}x  (min {zoomMin:F2}, max {zoomMax:F2})\n" +
            $"Δin   : {deltaIn:+0.00;-0.00;0.00}   Δout : {deltaOut:+0.00;-0.00;0.00}\n" +
            $"Cell  : {coordText}\n" +
            $"State : {stateText}\n" +
            $"Action: {_panComponent.LastZoomAction}\n" +
            $"Debounce: {debounceText}";
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
