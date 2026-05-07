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
/// E2 World Map -- the Cadastre's first feuillet. J3 promoted this from
/// the J1 two-button stub to a proper screen ; P8.2 promotes it again
/// from a screen-space TextureRect background to a Camera2D-driven
/// world-space scrollable map.
///
/// <para>
/// <b>P8.2 architecture (M3 / Arc 3 / Phase 8.2).</b>
/// The screen hosts a true 2D world directly under the <c>Control</c> root:
/// <list type="bullet">
///   <item><c>WorldRoot</c> (Node2D) -- the world container.</item>
///   <item><c>WorldRoot/WorldMapSprite</c> (Sprite2D, centered=false)
///         -- the 3840x2160 native E2.1 master asset, top-left at (0,0)
///         in world coords.</item>
///   <item><c>WorldRoot/WorldCamera</c> (Camera2D, current=true)
///         -- viewport on the world. Limit{Left,Top,Right,Bottom} are set
///         at <c>_Ready</c> from the texture size so the visible rect can
///         never escape the image (no bord noir, ever).</item>
///   <item><c>WorldRoot/PoiContainer</c> (Node2D) -- POI hotspots are
///         world-space Node2D + Area2D + CollisionShape2D instead of
///         screen-space Control + TextureButton (P7-J3 pattern). The
///         migration is what lets pan + click coexist correctly: a hotspot
///         rooted in the world tree follows the camera for free, no
///         per-frame position rewriting.</item>
/// </list>
/// The previous P7-J3 <c>BackgroundLayer/WorldMapBackground</c> TextureRect
/// (stretch_mode KeepAspectCovered) and <c>PoiLayer</c> CanvasLayer were
/// removed -- they are obsoleted by the world-space pattern (D-P8.2-11
/// YAGNI: empty CanvasLayer 1 deleted). DecorationLayer (panneaux iso UI
/// bord, banner, layer indicator) and ChromeLayer (back button, blocked
/// indicator) are unchanged -- they sit above the world layer at
/// CanvasLayer 2/3 and naturally do not pan with the camera, which is
/// the right behaviour for HUD chrome.
/// </para>
///
/// <para>
/// <b>P8.2-fix -- WorldRoot must NOT live under a CanvasLayer (Godot trap).</b>
/// The original P8.2 commit parented <c>WorldRoot</c> under a
/// <c>BackgroundLayer (CanvasLayer, layer=0)</c>. Symptom: drag and ZQSD
/// updated <c>_worldCamera.Position</c> correctly (logic verified by 22
/// xUnit pure-logic tests), but the rendered image did not move. Root
/// cause: a <see cref="Camera2D"/> only affects the
/// <c>canvas_transform</c> of the <see cref="Viewport"/> it belongs to
/// via <c>World2D</c>. A <see cref="CanvasLayer"/> has its own independent
/// <c>canvas_transform</c> that ignores any Camera2D nested inside it,
/// AND a Camera2D parented under a CanvasLayer never reaches the default
/// canvas where the rest of the world lives. Net effect: the camera
/// silently affected nothing. Fix: <c>WorldRoot</c> is now a direct child
/// of the <c>Control</c> root (no <c>CanvasLayer</c> ancestor), so the
/// Camera2D drives the default Viewport canvas where <c>WorldMapSprite</c>
/// and the POI hotspots live. Backend-brain trap lesson logged: in Godot,
/// "putting a thing on a layer to organise the scene" is not free --
/// CanvasLayer changes the rendering pipeline path of its descendants.
/// </para>
///
/// <para>
/// <b>POI hotspot pattern (P8.2 D-P8.2-02 + D-P8.2-03 method b).</b>
/// POI are <see cref="Node2D"/> instances spawned from
/// <c>res://data/world_map_pois.tres</c> at <c>_Ready</c>, parented to
/// <c>WorldRoot/PoiContainer</c>. Each hotspot owns a
/// <see cref="Sprite2D"/> for the marker visual (centered=true so the
/// authored <c>Position</c> is the marker center, intuitive for hand
/// authoring) and an <see cref="Area2D"/> with a
/// <see cref="CollisionShape2D"/> + <see cref="RectangleShape2D"/> for
/// click + hover detection. Position + hit-box come from each
/// <see cref="PoiDefinition"/> -- the data file <c>world_map_pois.tres</c>
/// already authors world-space coordinates against the 3840x2160 image
/// (D-P8.2-03 method b confirmed: existing positions like Halfgate
/// (1820, 980), Veylant (960, 540), Caer-Mithren (2740, 700), Pelt
/// (1280, 1500) all sit inside the world image, no <c>.tres</c> rewrite
/// needed by P8.2). When Mira ships a final marker variant Didier
/// adjusts the <c>Position</c> values in the inspector ; no recompile.
/// </para>
///
/// <para>
/// <b>Why Area2D not TextureButton (P7-J3 -> P8.2 reversal).</b>
/// At J3 the rationale for <c>TextureButton</c> was "E2 lives in the
/// Control tree, switching part of it to Node2D would be a mismatch."
/// P8.2 changes the underlying screen architecture: the background is
/// no longer a stretched TextureRect, it is a world-space Sprite2D
/// under a Camera2D. POIs that need to follow that camera must live in
/// the same world. <see cref="Area2D"/>'s <c>InputEvent</c> /
/// <c>MouseEntered</c> / <c>MouseExited</c> signals are functional
/// equivalents of TextureButton's <c>Pressed</c> / <c>MouseEntered</c>
/// / <c>MouseExited</c> -- the dispatch logic upstream
/// (<see cref="PoiDispatchLogic"/>) is unchanged, so the J3/J4 contract
/// tests still pin the same outcomes.
/// </para>
///
/// <para>
/// <b>Lambda-capture trap pattern (Risk #1, backend-brain trap).</b>
/// Same problem as P7-J3: connecting <c>area.InputEvent +=
/// (_, evt, _) =&gt; OnPoiPressed(poi.PoiId)</c> captures a fresh closure
/// per POI which cannot be disconnected by method-group at <c>_ExitTree</c>.
/// Pattern preserved from J3: a dictionary mapping the spawned
/// <see cref="Area2D"/> to the three handlers wired on it (InputEvent,
/// MouseEntered, MouseExited), iterated at teardown for an exact
/// reference-match disconnect. The <c>InputEvent</c> handler signature
/// is <c>(Node viewport, InputEvent evt, long shapeIdx)</c> via a Godot
/// signal delegate -- we keep the matching delegate type in the
/// dictionary entry so the disconnect compiles. Without this discipline,
/// freeing E2 leaves dangling subscriptions on orphan Area2Ds and the
/// next hover or click on the autoloader's stale queue throws an
/// "object disposed" exception. xUnit cannot catch this (no scene tree
/// in tests).
/// </para>
///
/// <para>
/// <b>Pan input wiring (D-P8.2-04 + D-P8.2-05 + D-P8.2-06).</b>
/// Two input surfaces, both on this screen (not on SceneManager):
/// <list type="bullet">
///   <item>Drag clic-milieu on <c>_UnhandledInput</c> -- pressing
///         <see cref="MouseButton.Middle"/> starts a drag, motion
///         translates the camera 1:1 inverse (drag right => camera
///         pans left, image visually slides right under the cursor,
///         RTS convention). Released middle ends the drag.</item>
///   <item>ZQSD / WASD / arrows polled in <c>_Process(delta)</c> via
///         <see cref="Input.IsActionPressed"/> against the four
///         <c>ui_pan_left/right/up/down</c> InputMap actions defined
///         in <c>project.godot</c> (D-P8.2-06). Constant
///         <see cref="PanSpeedPxPerSec"/> = 800 px/s
///         (D-P8.2-05). Diagonal motion is normalised by
///         <see cref="CameraPanLogic.ResolvePanDirection"/> so two-key
///         simultaneous press does not scroll sqrt(2) faster.</item>
/// </list>
/// Camera2D's native <c>LimitLeft/Top/Right/Bottom</c> apply the actual
/// runtime clamp ; the pure-C# <see cref="CameraPanLogic"/> mirrors the
/// invariant for xUnit pinning.
/// </para>
///
/// <para>
/// <b>Modal-owns-input convention (D-P8.2-12, mirrors P8.1).</b>
/// When a modal is open (<see cref="SceneManager.ActiveModalId"/> not
/// null), middle-click drag and ZQSD pan are silently ignored. Same
/// rationale as P8.1 wheel-while-modal: the modal owns the input
/// surface, the underlying screen is suspended. Esc closes the modal,
/// pan resumes.
/// </para>
///
/// <para>
/// <b>No conflict with P8.1 ladder wheel (D-P8.2-16 + Risk #2).</b>
/// SceneManager handles <see cref="MouseButton.WheelUp"/> /
/// <see cref="MouseButton.WheelDown"/>. E2WorldMap handles
/// <see cref="MouseButton.Middle"/> + ZQSD. Disjoint constants on the
/// Godot side, disjoint handlers on the C# side. A drag clic-milieu in
/// progress does not absorb the wheel (we never call
/// <c>SetInputAsHandled</c> on a wheel event), so spinning the wheel
/// during a drag still triggers ladder navigation -- manual checklist
/// step 9 validates this.
/// </para>
///
/// <para>
/// <b>Halfgate-only navigation gate (Varn §6.D6.10 lock 1-cité MVP).</b>
/// Click dispatch goes through pure-C# <see cref="PoiDispatchLogic"/>:
/// clickable POI -&gt; SceneManager.NavigateTo ; non-clickable POI -&gt;
/// flash the bottom-center BlockedIndicator label for 2.3s
/// (100ms fade-in, 2000ms hold, 200ms fade-out). The flash matters --
/// a silent <c>GD.Print</c> would leave the player wondering whether
/// they hit a bug or a mechanic. Spec preserved verbatim from J3.
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
    private const string HalfgatePoiId = "halfgate";

    private const float BlockedFadeInSeconds = 0.1f;
    private const float BlockedHoldSeconds = 2.0f;
    private const float BlockedFadeOutSeconds = 0.2f;

    /// <summary>
    /// ZQSD / WASD / arrow pan speed in world pixels per second
    /// (D-P8.2-05). Sized so a sustained pan crosses the full 3840 px
    /// width of the E2.1 image in ~4.8s -- snappy enough to feel
    /// responsive, slow enough that a tap does not overshoot the
    /// intended POI.
    /// </summary>
    private const float PanSpeedPxPerSec = 800f;

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

    // P8.2 world-space hierarchy. WorldRoot is a direct child of the
    // Control root (NOT under a CanvasLayer -- see class docstring P8.2-fix
    // section: a Camera2D under a CanvasLayer affects nothing).
    private Node2D _worldRoot = null!;
    private Sprite2D _worldMapSprite = null!;
    private Camera2D _worldCamera = null!;
    private Node2D _poiContainer = null!;

    private OpeningStrings _strings = null!;
    private WorldMapPois _poisResource = null!;

    /// <summary>
    /// One entry per spawned POI Area2D. The three handler references
    /// are stored so <c>_ExitTree</c> can disconnect with the exact
    /// references used at wire time -- method-group disconnect would
    /// not match a closure capturing the per-POI id (Risk #1 carried
    /// over from P7-J3, restated for P8.2 because the migration to
    /// Area2D changed the signal types).
    /// </summary>
    private readonly Dictionary<Area2D, PoiAreaHandlers> _poiHandlers = new();

    /// <summary>
    /// Reverse lookup poi-id -&gt; hotspot Node2D root. Populated alongside
    /// <see cref="_poiHandlers"/> at spawn. Used by the hover handler so
    /// the tooltip can anchor at the POI's <c>GlobalPosition</c> without
    /// scanning the scene tree. The hotspot's <c>GlobalPosition</c>
    /// already accounts for camera pan because the hotspot is a child
    /// of the world tree.
    /// </summary>
    private readonly Dictionary<string, Node2D> _poiHotspots = new();

    private Tween? _blockedTween;

    // P8.2 drag state (clic-milieu pan). Idle when _isDragging is false.
    private bool _isDragging;
    private Vector2 _dragStartMouseViewport;
    private Vector2 _dragStartCameraWorld;

    public override void _Ready()
    {
        _strings = ResourceLoader.Load<OpeningStrings>(OpeningStringsResPath) ?? new OpeningStrings();
        _poisResource = ResourceLoader.Load<WorldMapPois>(WorldMapPoisResPath) ?? new WorldMapPois();

        // P8.2-fix: WorldRoot is now a direct child of the Control root.
        // Previously it was nested under a BackgroundLayer (CanvasLayer)
        // which silently broke the Camera2D's effect on rendering -- see
        // class docstring P8.2-fix section.
        _worldRoot = GetNode<Node2D>("WorldRoot");
        _worldMapSprite = GetNode<Sprite2D>("WorldRoot/WorldMapSprite");
        _worldCamera = GetNode<Camera2D>("WorldRoot/WorldCamera");
        _poiContainer = GetNode<Node2D>("WorldRoot/PoiContainer");

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

        // Background + decoration textures (AssetResolver returns a
        // deterministic placeholder if the file is missing, so this never
        // null-refs even on a fresh checkout).
        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        _worldMapSprite.Texture = assetResolver.Resolve(WorldMapBackgroundAssetKey);
        _bannerTop.Texture = assetResolver.Resolve(BannerTopAssetKey);

        // Panel iso frames are decorative -- the StyleBoxFlat in the .tscn
        // already gives us the parchment look. When Mira ships the real
        // panneau iso PNGs, swap the StyleBoxFlat for a NinePatchRect or
        // a TextureRect underneath each PanelContainer. J3 stub fixed-pos.
        _ = assetResolver.Resolve(PanelLeftAssetKey);   // pre-warm cache slot
        _ = assetResolver.Resolve(PanelRightAssetKey);  // pre-warm cache slot

        // Configure the camera against the loaded image dimensions. Done
        // after the texture set so GetSize() reads the real native size.
        // Camera2D.Limit* clamps the visible rect inside the image, the
        // canonical Godot pattern for "fenêtre sur un monde fini" (D-P8.2-07).
        var imageSize = _worldMapSprite.Texture.GetSize();
        _worldCamera.LimitLeft = 0;
        _worldCamera.LimitTop = 0;
        _worldCamera.LimitRight = (int)imageSize.X;
        _worldCamera.LimitBottom = (int)imageSize.Y;
        _worldCamera.MakeCurrent();

        // Initial camera position: centered on Halfgate POI if found
        // (D-P8.2-08, "tu commences là où l'action est"), else on image
        // center. The clamp is a no-op for valid Halfgate -- defense-doc'd
        // by CameraPanLogicTests.HalfgatePoiCenter_is_within_valid_camera_range.
        var halfgate = FindPoi(HalfgatePoiId);
        var initialCenter = halfgate?.Position ?? imageSize / 2f;
        _worldCamera.Position = initialCenter;

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

        GD.Print($"[E2WorldMap] ready, {_poiHandlers.Count} POI spawned, camera at {_worldCamera.Position}");
    }

    public override void _ExitTree()
    {
        // Disconnect chrome handlers first.
        if (_backButton is not null) _backButton.Pressed -= OnBackPressed;

        // Disconnect every POI handler with the EXACT reference captured
        // at wire time. Method-group disconnect would not match because
        // each closure closed over a distinct PoiId (Risk #1).
        foreach (var (area, handlers) in _poiHandlers)
        {
            if (area is null) continue;
            area.InputEvent -= handlers.InputEvent;
            area.MouseEntered -= handlers.MouseEntered;
            area.MouseExited -= handlers.MouseExited;
            // QueueFree the hotspot Node2D parent so the Sprite2D + Area2D
            // + CollisionShape2D children are reaped together. The hotspot
            // is the area's parent (see SpawnPois below).
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

    public override void _Process(double delta)
    {
        // ZQSD pan polling (D-P8.2-05 + D-P8.2-06). Done in _Process so
        // sustained key-hold produces continuous motion at a frame-rate-
        // independent speed. Skipped while a modal is open (D-P8.2-12).
        if (IsModalOpen()) return;

        var dir = CameraPanLogic.ResolvePanDirection(
            left:  Input.IsActionPressed("ui_pan_left"),
            right: Input.IsActionPressed("ui_pan_right"),
            up:    Input.IsActionPressed("ui_pan_up"),
            down:  Input.IsActionPressed("ui_pan_down"));

        if (dir.X == 0f && dir.Y == 0f) return;

        var current = ToPanVec2(_worldCamera.Position);
        var imageSize = ToPanVec2(_worldMapSprite.Texture.GetSize());
        var viewportSize = ToPanVec2(GetViewport().GetVisibleRect().Size);

        var advanced = CameraPanLogic.AdvanceCameraCenter(
            current, dir, PanSpeedPxPerSec, (float)delta, imageSize, viewportSize);

        _worldCamera.Position = ToVector2(advanced);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Drag clic-milieu pan (D-P8.2-04). We never call
        // SetInputAsHandled on the wheel events SceneManager listens for
        // (WheelUp / WheelDown), so the ladder still works during a drag
        // -- defense-doc'd by manual checklist step 9.
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Middle)
        {
            // Modal-owns-input mirror of P8.1 (D-P8.2-12). When a modal
            // is open we ignore the press entirely. If a drag was already
            // in progress when the modal opened (race window), we end it
            // here too -- robustness over correctness on this edge.
            if (IsModalOpen())
            {
                _isDragging = false;
                return;
            }

            if (mb.Pressed)
            {
                _isDragging = true;
                _dragStartMouseViewport = mb.Position;
                _dragStartCameraWorld = _worldCamera.Position;
            }
            else
            {
                _isDragging = false;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion mm && _isDragging)
        {
            // 1:1 inverse: if the mouse has moved +X since drag start,
            // the camera moves -X to make the image visually slide right
            // under the cursor (RTS / SimCity convention, D-P8.2-04).
            var delta = mm.Position - _dragStartMouseViewport;
            var desired = _dragStartCameraWorld - delta;

            var imageSize = ToPanVec2(_worldMapSprite.Texture.GetSize());
            var viewportSize = ToPanVec2(GetViewport().GetVisibleRect().Size);
            var clamped = CameraPanLogic.ClampCameraCenter(
                ToPanVec2(desired), imageSize, viewportSize);

            _worldCamera.Position = ToVector2(clamped);
            GetViewport().SetInputAsHandled();
        }
    }

    private void SpawnPois(AssetResolver assetResolver)
    {
        foreach (var poi in _poisResource.Pois)
        {
            if (poi is null) continue;

            // Hotspot root -- the Area2D's parent. QueueFree on this
            // node at _ExitTree reaps the sprite + area + shape together.
            var hotspot = new Node2D
            {
                Name = $"Poi_{poi.PoiId}",
                Position = poi.Position,
            };

            var sprite = new Sprite2D
            {
                Texture = assetResolver.Resolve(poi.AssetKey),
                Centered = true,
                // Tone down the suspended sprite without disabling the
                // node -- we still need the hover signal for the
                // suspended-cadastre tooltip.
                Modulate = poi.IsClickable
                    ? new Color(1, 1, 1, 1)
                    : new Color(0.85f, 0.78f, 0.68f, 0.85f),
            };

            // Area2D for click + hover detection. InputPickable=true is
            // the default but stated explicitly here so a future
            // refactor that disables input picking globally surfaces
            // this site as a known consumer.
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
            _poiContainer.AddChild(hotspot);

            // Capture the PoiId once per closure so the dispatch reads the
            // right entry. The handlers are *kept* in _poiHandlers so the
            // _ExitTree disconnect uses the same references (Risk #1).
            var poiId = poi.PoiId;

            // Area2D.InputEvent signature: (Node viewport, InputEvent evt, long shapeIdx).
            // Matches the InputEventEventHandler delegate Godot exposes
            // on Area2D in C#. We only fire on the press edge of the
            // left mouse button so a drag clic-milieu starting on a POI
            // does not also click it.
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

        // Anchor at the POI's world-space global position (which already
        // accounts for camera pan because the hotspot is a child of the
        // world tree). The tooltip controller adds its own offset so the
        // panel floats above the marker.
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

    /// <summary>
    /// True when a modal (E4 character sheet) is currently open. Used
    /// to gate pan input -- D-P8.2-12 mirrors P8.1's wheel-while-modal
    /// rule. Reads the autoload directly because this state is only
    /// authoritative on the SceneManager singleton.
    /// </summary>
    private bool IsModalOpen()
    {
        var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
        return sceneManager?.ActiveModalId is not null;
    }

    private static PanVec2 ToPanVec2(Vector2 v) => new(v.X, v.Y);
    private static Vector2 ToVector2(PanVec2 v) => new(v.X, v.Y);

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
    /// Keeps the three handler references wired on a POI Area2D so
    /// <c>_ExitTree</c> disconnects them with the exact same references
    /// used at wire time. See class doc Risk #1. The InputEvent handler
    /// uses Godot's typed delegate <see cref="Area2D.InputEventEventHandler"/>
    /// because the InputEvent signal carries a node + event + shape-idx
    /// triple, not just a unit Action like Pressed/MouseEntered/MouseExited.
    /// </summary>
    private readonly record struct PoiAreaHandlers(
        Area2D.InputEventEventHandler InputEvent,
        Action MouseEntered,
        Action MouseExited);
}
