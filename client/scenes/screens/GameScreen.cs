using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scenes.Ui;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scenes.Screens;

// =============================================================================
//  GAME SCREEN SHELL — THE FOUR-LAYER DIORAMA
//  (Rune, 2026-05-22 — supersedes the J3c-1septies "three-SubViewport +
//   twin-corner clip shader" architecture. Read this box before touching
//   the screen; do NOT re-derive the layout from an F6 capture.)
// =============================================================================
//
//  WHAT CHANGED, AND WHY. Up to J3c-2 the cartographer's-desk read as two
//  brown corner triangles carved out of a rectangular SubViewport texture by
//  the desk_triangle_clip.gdshader. That shader, and the pure-C#
//  DeskClipFrontierLogic that fed its five UV uniforms, were ALWAYS a
//  scaffold: a way to cut a rectangular render into triangles WITHOUT a
//  painted asset. Didier validated the four-layer model on 2026-05-22
//  (design doc rune-game-screen-3d-decor-design-2026-05-22.md): the desk
//  is no longer cut by a shader — it is FRAMED by a Blender decor that
//  carries its own shape in two passes. The scaffold is removed: the
//  decor IS its own shape.
//
//  THE FOUR LAYERS — draw order, back to front: A -> B -> C -> D -> HUD.
//
//   A. BACK DECOR  (LayerABackDecor)
//        A full-screen TextureRect. In the shipped game this carries the
//        Blender "back" render pass — the cartographer's desk-furniture, the
//        recess floor, everything painted BEHIND the map. FROZEN — never
//        moves. RIGHT NOW it is a GREY PLACEHOLDER (a flat ColorRect-style
//        texture stamped "LAYER A — back decor placeholder") so the layout
//        can be locked before Mira's costly Blender render lands.
//
//   B. MAQUETTE  (MapViewportContainer / MapViewport / Maquette)
//        The pannable district maquette — UNCHANGED from J3a/J3b. A
//        SubViewportContainer clips the maquette to the residual rect
//        between the HUD bands; MapCamera2D pans on MMB drag. The container
//        is nudged DOWN by MapRecessDepthPx (default 8) — a purely cosmetic
//        "the map sits slightly in a recess" offset (see MapRecessDepthPx).
//
//   C. DESK BOARD  (DeskFloorViewport + DeskEntitiesViewport)
//        The interactive desk: the iso floor board + the 7 Company pawns +
//        the J3c-2 click selection / placement. DECIDED BY DIDIER (brief
//        rune-game-screen-decor-production-brief-2026-05-22.md, option Beta):
//        the desk floor STAYS a live Godot IsoBoard — it is NOT painted into
//        the frozen decor. The shipped Blender decor (A + D) FRAMES the desk
//        zone; the two triangles keep their live iso grid + interactive
//        pawns. The two-board / one-shared-camera-frame split is preserved
//        verbatim (it is what makes the pawns sit on the floor and the
//        J3c-2 routing a single affine map) — only the clip shader is gone.
//        The floor TextureRect now carries NO material.
//
//        TEMPORARY GAP-FILLER (Rune, 2026-05-22 — F6-readability fix).
//        Removing the clip shader left the desk floor rendering as a FULL
//        OPAQUE RECTANGLE that buried the layer-B maquette: the F6 smoke
//        became unreadable. Until Mira's Blender decor carries the
//        desk-floor shape, the placeholder desk floor is framed to the two
//        bottom-corner wedges by a CPU draw-time clip
//        (DeskTrianglePlaceholderLogic + IsoBoard.DeskTrianglePlaceholderClip).
//        This is NOT the removed clip shader returning — it is a draw-time
//        decision (which cells / which aplat to skip), no shader, no
//        material, no UV uniforms. The desk wood bitmap is also dropped for
//        the gap (a full-rect Sprite2D cannot be carved by a CPU clip); the
//        placeholder aplat returns. When Mira's decor lands, delete the
//        flag, the helper, and re-load the wood bitmap.
//
//   D. FRONT DECOR  (LayerDFrontDecor)
//        A full-screen TextureRect with alpha. In the shipped game this
//        carries the Blender "front" pass — the recess lip, the carved
//        furniture edges that must paint OVER the map and the desk floor to
//        sell the "the map is inset" illusion. FROZEN. Drawn AFTER C and
//        BEFORE the HUD. RIGHT NOW it is a MOSTLY-TRANSPARENT placeholder
//        carrying only a THIN iso-diamond liseré around the maquette window
//        (the "losange-creux") plus an on-screen label — NOT a full-screen
//        opaque grid (that grid was exactly what buried the maquette in the
//        first 4-layer F6). The liseré is semi-transparent on purpose, so
//        the F6 smoke can VERIFY layer D really does paint over both B (the
//        maquette) and C (the desk wedges) while hiding almost nothing.
//        MouseFilter = Ignore: it is purely visual and must never eat a
//        click.
//
//  HUD. The CanvasLayer HUD (layer 10) is unchanged — two 32 px bands,
//  always above everything including layer D.
//
//  WHY THE DESK STILL HAS TWO SUBVIEWPORTS (the part that must NOT regress).
//  The floor and the pawns are split into two co-located SubViewports —
//  DeskFloorViewport and DeskEntitiesViewport — sharing ONE camera frame.
//  This is NOT about the (now-removed) clip: a pawn standing near the
//  shipped decor's recess edge has its body and head extending UP, above
//  the desk zone, into the maquette zone — a figurine is taller than the
//  table it stands on. The pawns therefore ride their own UNCLIPPED
//  viewport, drawn after the floor, so an overflowing pawn is never sliced.
//  DeskFloorCamera2D and DeskEntitiesCamera2D are parked at the IDENTICAL
//  Position and Zoom — computed ONCE in pure-C# and applied to both — so
//  cell (col,row) projects to the SAME pixel in both viewports and a pawn's
//  position is DeskEntitiesBoard.CellToPixel(cell) and nothing else. The
//  [DESK-DIAG] preflight asserts AGREE=true; do not remove it.
//
//  WHY J3c-2 SURVIVES UNTOUCHED. The click pipeline — _UnhandledInput ->
//  DeskInputRoutingLogic.ScreenToDeskWorld (the shared desk camera frame)
//  -> DeskEntitiesBoard.PixelToCell -> DeskSelectionLogic -> GameState
//  authority — depends only on the shared desk camera frame and the desk
//  board's projection. None of that involved the clip shader. The desk
//  SubViewports and their TextureRects are still full-screen at the screen
//  origin, so a screen pixel still maps to ONE desk-world point by ONE
//  affine map. J3c-2 is preserved verbatim.
//
//  COORDINATE FLOW (unchanged from J3c-1septies).
//    slot cell (TileCoordinate)
//      -> DeskEntitiesBoard.CellToPixel(cell)   [board-local pixels]
//      -> pawn.Position                          [board-local]
//      -> rendered under DeskEntitiesCamera2D    [-> DeskEntitiesViewport
//                                                 texture, transparent bg]
//      -> DeskEntitiesTextureRect, full screen, NO material  [-> screen]
//  The floor flows the identical path through DeskFloorBoard /
//  DeskFloorCamera2D / DeskFloorViewport / DeskFloorTextureRect — the floor
//  TextureRect now carries NO material (the clip shader is gone).
//
//  INPUT FLOW (J3c-2, the inverse direction — unchanged).
//    screen pixel (left-click in _UnhandledInput)
//      -> DeskInputRoutingLogic.ScreenToDeskWorld    [desk-world pixels]
//      -> DeskEntitiesBoard.PixelToCell(world)        [desk cell]
//      -> DeskSelectionLogic.ResolveClickedMember     [member id or null]
//      -> GameState (the authority): SetSelectedCompanyMember
//                                    or RequestCompanyMemberPlacement
//      -> GameState emits CompanyMemberSelected / ...PlacementConfirmed
//      -> DeskCompanyPawn views react (highlight / tween).
//
// =============================================================================

/// <summary>
/// Root of the Game Screen Shell — the cartographer's-desk diorama in which
/// eM and eT are shown (roadmap <c>wayfinders-mvp-roadmap-eM-eT</c>). The
/// authoritative <b>four-layer</b> architecture is the boxed comment above
/// this class — read it before touching the screen.
///
/// <para>
/// <b>The four layers (draw order back-to-front: A → B → C → D → HUD).</b>
/// <list type="bullet">
///   <item><b>A — back decor.</b> A full-screen <see cref="TextureRect"/>
///     (<c>LayerABackDecor</c>). Frozen. Carries the Blender back-pass in
///     the shipped game; a <b>grey placeholder</b> for now.</item>
///   <item><b>B — maquette.</b> The pannable district maquette
///     (<c>MapViewportContainer</c>) — unchanged from J3a/J3b, nudged down
///     by <see cref="MapRecessDepthPx"/>.</item>
///   <item><b>C — desk board.</b> The interactive iso desk + the 7 Company
///     pawns + J3c-2 click selection/placement — preserved functionally.
///     The desk floor stays a live <see cref="IsoBoard"/> (Didier's option
///     Beta), framed by — not painted into — the decor.</item>
///   <item><b>D — front decor.</b> A full-screen alpha
///     <see cref="TextureRect"/> (<c>LayerDFrontDecor</c>). Frozen. Carries
///     the Blender front-pass (recess lip) in the shipped game; a
///     <b>mostly-transparent placeholder — a thin iso-diamond liseré around
///     the maquette window plus an on-screen label</b> for now, so the F6
///     smoke can verify it paints over B and C without burying them.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What the 4-layer rework removed (Rune, 2026-05-22).</b> The
/// <c>desk_triangle_clip.gdshader</c> and its pure-C#
/// <c>DeskClipFrontierLogic</c> are gone. They were a scaffold — a way to
/// cut a rectangular SubViewport render into two corner triangles without a
/// painted asset. The four-layer model replaces that with a Blender front
/// decor (layer D) that carries its own alpha-cut shape: the decor IS its
/// own shape, so the procedural clip has nothing left to do. The desk
/// floor <see cref="TextureRect"/> now carries no material.
/// </para>
///
/// <para>
/// <b>What survives untouched.</b> The pannable maquette (J3a), the fixed
/// HUD frame (J3b), the desk's two-SubViewport / one-shared-camera split,
/// and the whole J3c-2 click pipeline (<see cref="DeskInputRoutingLogic"/>
/// → <c>PixelToCell</c> → <see cref="DeskSelectionLogic"/> →
/// <c>GameState</c>). The two desk SubViewports are kept because a pawn is
/// taller than the desk floor and must be able to overflow above it without
/// being sliced — that reason is independent of the (now-removed) clip.
/// </para>
///
/// <para>
/// <b>Input routing — _Input vs _UnhandledInput (trap #9).</b> The MMB
/// drag-pan stays in <see cref="_Input"/>: the pan wants the middle button
/// before anything else can claim it. The desk LEFT-click is handled in
/// <see cref="_UnhandledInput"/> — the lowest-priority interaction on this
/// screen, fired only when no higher-priority node has consumed the event.
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The two decor
/// <see cref="TextureRect"/>s, the maquette container, and both desk
/// <see cref="SubViewport"/>s / <see cref="TextureRect"/>s are direct
/// children of this <see cref="Control"/> — never under <c>HudLayer</c>.
/// </para>
/// </summary>
public partial class GameScreen : Control
{
    /// <summary>
    /// Autoload-injected opening flow must not draw its CanvasLayer over
    /// this scene when launched standalone for the F6 smoke (trap #1bis).
    /// The <c>.tscn</c> root carries <c>metadata/skip_opening_bootstrap</c>;
    /// this constant documents the contract for readers of the code.
    /// </summary>
    private const string SkipBootstrapMeta = "skip_opening_bootstrap";

    /// <summary>
    /// Fixed height of each HUD band, pixels — the same value for the top
    /// and the bottom band. <b>The single named source of truth</b> for the
    /// band height: it must match the <c>HudTop</c> panel's
    /// <c>offset_bottom</c> and the <c>HudBottom</c> panel's
    /// <c>-offset_top</c> in <c>GameScreen.tscn</c>.
    /// </summary>
    private const float HudBandHeight = 32f;

    /// <summary>
    /// <b>The recess-depth knob — the cosmetic vertical offset of layer B
    /// (the maquette), in pixels. Default 8.</b>
    ///
    /// <para>
    /// The four-layer model sells "the map sits slightly in a recess of the
    /// cartographer's desk". Part of that illusion is that the maquette is
    /// painted a few pixels LOWER than where a flush layout would put it —
    /// just enough that the shipped front decor's recess lip (layer D) reads
    /// as being above the map surface.
    /// </para>
    ///
    /// <para>
    /// <b>This is 100% cosmetic.</b> It shifts the
    /// <see cref="SubViewportContainer"/> rect down by this many pixels; it
    /// does NOT touch <see cref="IsoCell"/>, the iso projection, or any game
    /// logic. The game world stays flat — there is no elevation in the
    /// simulation. Raise it for a deeper-looking recess, set it to 0 for a
    /// flush map. Kept as a named constant so the value has one obvious,
    /// version-controlled home and the F6 smoke can be re-run at different
    /// depths without hunting through the scene.
    /// </para>
    ///
    /// <para>
    /// <b>Bounds.</b> It is clamped into the residual map rect by
    /// <see cref="HudLayoutLogic"/> indirectly — the container is simply
    /// offset, the SubViewport size follows — so a large value just makes
    /// the maquette window shorter; it never produces a negative rect while
    /// the value stays well below the residual height.
    /// </para>
    /// </summary>
    private const float MapRecessDepthPx = 8f;

    /// <summary>
    /// <b>Desk-camera framing — knob A: the desk camera zoom.</b> A Godot
    /// <see cref="Camera2D"/> <c>Zoom</c> below 1.0 zooms OUT. 0.85 frames
    /// the 7-pawn block comfortably inside the desk zone. Applied to BOTH
    /// desk cameras (they share one frame).
    /// </summary>
    private const float DeskCameraZoom = 0.85f;

    /// <summary>
    /// <b>Desk-camera framing — knob B/C: where the Company formation's
    /// centroid lands on screen, as a fraction of the screen.</b> The
    /// formation sits in the lower-LEFT of the screen, parked low and close
    /// to the corner.
    /// </summary>
    private const float DeskFormationScreenFracX = 0.20f;
    private const float DeskFormationScreenFracY = 0.85f;

    /// <summary>
    /// <b>Desk-board cosmetic nudge — the desk boards' local-X offset,
    /// pixels. Default 0.</b> Applied to BOTH desk boards in
    /// <see cref="ConfigureDesk"/>. Kept as a named constant so a future
    /// deliberate nudge has one obvious, version-controlled home.
    ///
    /// <para>
    /// <b>J3c-2 — load-bearing for input routing.</b> The screen → desk-cell
    /// conversion (<see cref="DeskInputRoutingLogic"/> + <c>PixelToCell</c>)
    /// assumes the board-local space and the desk world coincide, which
    /// holds only while this nudge is 0. If a future nudge is dialled in,
    /// the routing must subtract it before <c>PixelToCell</c>.
    /// </para>
    /// </summary>
    private const float DeskBoardOffsetX = 0f;

    // --- Layer A: back decor ------------------------------------------------
    private TextureRect _layerABackDecor = null!;

    // --- Layer B: the pannable maquette ------------------------------------
    private SubViewportContainer _mapContainer = null!;
    private SubViewport _mapViewport = null!;
    private Camera2D _mapCamera = null!;
    private IsoBoard _maquette = null!;

    // --- Layer C: the interactive desk -------------------------------------
    // The floor and the pawns are split into two co-located SubViewports
    // sharing one camera frame so a pawn that overflows above the desk floor
    // is never sliced (see the boxed architecture comment).
    private SubViewport _deskFloorViewport = null!;
    private Camera2D _deskFloorCamera = null!;
    private IsoBoard _deskFloorBoard = null!;
    private TextureRect _deskFloorTextureRect = null!;

    private SubViewport _deskEntitiesViewport = null!;
    private Camera2D _deskEntitiesCamera = null!;
    private IsoBoard _deskEntitiesBoard = null!;
    private TextureRect _deskEntitiesTextureRect = null!;

    // --- Layer D: front decor ----------------------------------------------
    private TextureRect _layerDFrontDecor = null!;

    // --- HUD ----------------------------------------------------------------
    private CanvasLayer _hudLayer = null!;

    private readonly MapViewportPanLogic _pan = new();

    /// <summary>
    /// The <c>GameState</c> autoload — the desk's selection / placement
    /// authority (J3c-2). Resolved in <see cref="_Ready"/>.
    /// </summary>
    private GameState _gameState = null!;

    /// <summary>
    /// The desk screen size cached at <see cref="_Ready"/>, reused by the
    /// J3c-2 input routing so a click does not re-query
    /// <c>GetViewportRect</c> on every event.
    /// </summary>
    private Vector2 _screenSize;

    public override void _Ready()
    {
        _layerABackDecor = GetNode<TextureRect>("LayerABackDecor");

        _mapContainer = GetNode<SubViewportContainer>("MapViewportContainer");
        _mapViewport = GetNode<SubViewport>("MapViewportContainer/MapViewport");
        _mapCamera = GetNode<Camera2D>("MapViewportContainer/MapViewport/MapCamera2D");
        _maquette = GetNode<IsoBoard>("MapViewportContainer/MapViewport/Maquette");

        _deskFloorViewport = GetNode<SubViewport>("DeskFloorViewport");
        _deskFloorCamera = GetNode<Camera2D>("DeskFloorViewport/DeskFloorCamera2D");
        _deskFloorBoard = GetNode<IsoBoard>("DeskFloorViewport/DeskFloorBoard");
        _deskFloorTextureRect = GetNode<TextureRect>("DeskFloorTextureRect");

        _deskEntitiesViewport = GetNode<SubViewport>("DeskEntitiesViewport");
        _deskEntitiesCamera =
            GetNode<Camera2D>("DeskEntitiesViewport/DeskEntitiesCamera2D");
        _deskEntitiesBoard =
            GetNode<IsoBoard>("DeskEntitiesViewport/DeskEntitiesBoard");
        _deskEntitiesTextureRect = GetNode<TextureRect>("DeskEntitiesTextureRect");

        _layerDFrontDecor = GetNode<TextureRect>("LayerDFrontDecor");

        _hudLayer = GetNode<CanvasLayer>("HudLayer");

        // J3c-2: the desk selection / placement authority. An autoload, so
        // it is up before any GameScreen exists.
        _gameState = GetNode<GameState>("/root/GameState");

        // J3b: size the map viewport to the residual central rectangle —
        // the screen minus the two fixed HUD bands — then nudge it DOWN by
        // the cosmetic recess depth. The screen size is read from
        // GetViewportRect (the real window rect, valid at _Ready).
        var screen = GetViewportRect().Size;
        _screenSize = screen;
        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
        ApplyResidualRect(residual);
        _mapViewport.Size = (Vector2I)_mapContainer.Size;

        // Layer A / D placeholders fill the whole screen. Layer D is given
        // the residual map rect so it can draw a THIN iso-diamond liseré
        // around the maquette window — not a full-screen opaque grid.
        ConfigureDecorPlaceholders(screen, residual);

        // Park the maquette camera on the content's GEOMETRIC CENTRE so the
        // maquette is centred on screen.
        var viewport = ViewportSize();
        var contentCentre = MaquetteContentCentre();
        var clampedStart = MapViewportPanLogic.ClampToContent(
            ToPan(contentCentre), ToPan(ContentSize()), ToPan(viewport));
        _mapCamera.Position = ToGodot(clampedStart);

        ConfigureDesk(screen);

        Preflight(ContentSize(), viewport, clampedStart, screen);
    }

    /// <summary>
    /// Input seam — GESTURE priority (trap #9): the middle-button drag-pan.
    /// Stays in <see cref="_Input"/> so the pan claims the MMB before any
    /// node further down can. The desk LEFT-click selection is NOT here —
    /// it is a lower-priority entity interaction and lives in
    /// <see cref="_UnhandledInput"/>.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton button
            && button.ButtonIndex == MouseButton.Middle)
        {
            var pos = ToPan(button.Position);
            var centre = ToPan(_mapCamera.Position);
            if (button.Pressed)
            {
                _pan.OnPress(pos, centre);
            }
            else
            {
                _pan.OnRelease(centre);
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            var outcome = _pan.OnMotion(
                ToPan(motion.Position), ToPan(ContentSize()), ToPan(ViewportSize()));
            if (outcome.CameraMoved)
            {
                _mapCamera.Position = ToGodot(outcome.Centre);
            }
            // Deliberately NOT SetInputAsHandled on motion (trap #9): the pan
            // owns its response, not the motion event itself.
        }
    }

    /// <summary>
    /// Input seam — ENTITY-SELECTION fallthrough (trap #9, J3c-2). The desk
    /// LEFT-click is handled here, not in <see cref="_Input"/>: it is the
    /// lowest-priority interaction on this screen, so it must fire only
    /// when no higher-priority node has already consumed the event.
    ///
    /// <para>
    /// A left-click is acted on at the RELEASE edge. The whole routing +
    /// selection + placement decision is pure-C#
    /// (<see cref="DeskInputRoutingLogic"/>, <see cref="DeskSelectionLogic"/>,
    /// <see cref="DeskPlacementLogic"/>); this method only converts engine
    /// types and calls the <c>GameState</c> authority.
    /// </para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton button
            || button.ButtonIndex != MouseButton.Left
            || button.Pressed)
        {
            // Only a left-button RELEASE is a desk click.
            return;
        }

        HandleDeskLeftClick(button.Position);
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Route one desk left-click through the J3c-2 pipeline: screen pixel
    /// → desk-world → desk cell → member id → <c>GameState</c> authority.
    /// </summary>
    /// <param name="screenPos">
    /// The left-click position in screen pixels, from
    /// <c>InputEventMouseButton.Position</c>.
    /// </param>
    private void HandleDeskLeftClick(Vector2 screenPos)
    {
        // 1. Screen pixel -> desk-world point, using the shared desk camera
        //    frame (the floor and entities cameras are mirror-identical).
        var deskWorld = DeskInputRoutingLogic.ScreenToDeskWorld(
            ToSys(screenPos),
            ToSys(_screenSize),
            ToSys(_deskEntitiesCamera.Position),
            ToSys(_deskEntitiesCamera.Zoom));

        // 2. Desk-world point -> desk cell, via the desk board's projection.
        var clickedCellV =
            _deskEntitiesBoard.PixelToCell(new Vector2(deskWorld.X, deskWorld.Y));
        var clickedCell = new TileCoordinate(clickedCellV.X, clickedCellV.Y);

        // 3. Which member (if any) the click resolved to.
        var clickedMember = DeskSelectionLogic.ResolveClickedMember(
            clickedCell, _gameState.DeskCellOccupants);

        var currentSelection = _gameState.SelectedCompanyMemberId;

        // 4a. The click hit a pawn -> selection transition.
        if (clickedMember is not null)
        {
            var next = DeskSelectionLogic.NextSelection(
                currentSelection, clickedMember);
            _gameState.SetSelectedCompanyMember(next);
            GD.Print($"[GameScreen] desk click: cell={clickedCell} hit pawn " +
                     $"'{clickedMember}' -> selection now " +
                     $"'{next ?? "(none)"}'.");
            return;
        }

        // 4b. The click hit no pawn. If a pawn is selected, this is a
        //     placement intent for it; otherwise it is a plain deselect.
        if (currentSelection is not null)
        {
            var verdict = _gameState.RequestCompanyMemberPlacement(
                currentSelection, clickedCell);
            GD.Print($"[GameScreen] desk click: cell={clickedCell} is a " +
                     $"placement intent for '{currentSelection}' -> verdict " +
                     $"{verdict}.");
            if (verdict == DeskPlacementLogic.PlacementVerdict.Accepted
                || verdict == DeskPlacementLogic.PlacementVerdict.NoOp)
            {
                _gameState.SetSelectedCompanyMember(null);
            }
        }
        else
        {
            _gameState.SetSelectedCompanyMember(null);
        }
    }

    public override void _ExitTree()
    {
        // Cut any live drag cleanly so no ambiguous state survives a scene
        // change — same reset discipline as MapPanInputLogic.
        _pan.Reset();
    }

    /// <summary>
    /// Apply a <see cref="HudRect"/> (from <see cref="HudLayoutLogic"/>) onto
    /// the layer-B <see cref="SubViewportContainer"/>'s offsets, shifting it
    /// DOWN by the cosmetic <see cref="MapRecessDepthPx"/>.
    ///
    /// <para>
    /// The residual rect is the screen minus the two HUD bands. The recess
    /// nudge moves the maquette window down by a few pixels (purely
    /// cosmetic) and trims an equal amount off its bottom so the container
    /// never overlaps the bottom HUD band.
    /// </para>
    /// </summary>
    private void ApplyResidualRect(HudRect rect)
    {
        _mapContainer.OffsetLeft = rect.X;
        _mapContainer.OffsetTop = rect.Y + MapRecessDepthPx;
        _mapContainer.OffsetRight = rect.X + rect.Width;
        // Keep the bottom edge where it was: the window is shorter by the
        // recess depth, it does not push into the bottom HUD band.
        _mapContainer.OffsetBottom = rect.Y + rect.Height;
    }

    /// <summary>
    /// Stand up the layer-A and layer-D placeholders so the F6 smoke reads
    /// the four-layer structure WITHOUT the (removed) clip scaffold and
    /// WITHOUT the full-screen opaque grid that used to bury the maquette.
    ///
    /// <para>
    /// <b>Layer A — back decor.</b> An opaque warm-grey fill, full screen.
    /// A <c>LayerALabel</c> child (authored in <c>GameScreen.tscn</c>) names
    /// it on screen so Didier identifies the zone — "COUCHE A — décor
    /// arrière (placeholder, sera le rendu Blender)". Frozen.
    /// </para>
    ///
    /// <para>
    /// <b>Layer D — front decor.</b> NOT a full-screen opaque grid anymore
    /// (that grid is exactly what made the F6 unreadable). It is now a
    /// mostly-transparent texture carrying only a THIN semi-transparent
    /// iso-diamond <i>liseré</i> traced around the maquette window — the
    /// "losange-creux" through which layers B and C show. The
    /// <c>LayerDLabel</c> child names it. The liseré proves layer D paints
    /// OVER B and C (it draws on top of the maquette diamond edge) while
    /// hiding almost nothing. Frozen, <c>MouseFilter = Ignore</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Temporary.</b> When Mira delivers the real Blender passes this
    /// method is replaced by loading the two render textures; the labels and
    /// the procedural liseré are dropped. The rest of the scene is
    /// unaffected.
    /// </para>
    /// </summary>
    /// <param name="screen">The real screen size in pixels.</param>
    /// <param name="residual">
    /// The residual maquette rect (screen minus the two HUD bands) — the
    /// window the layer-D liseré is traced around.
    /// </param>
    private void ConfigureDecorPlaceholders(Vector2 screen, HudRect residual)
    {
        var size = (Vector2I)screen;

        // Layer A — opaque warm-grey, "back decor" stand-in. The on-screen
        // LayerALabel (in the .tscn) identifies the zone.
        _layerABackDecor.Texture = MakeFlatPlaceholder(
            size, new Color(0.30f, 0.29f, 0.31f, 1f));
        _layerABackDecor.MouseFilter = MouseFilterEnum.Ignore;

        // Layer D — a thin semi-transparent iso-diamond liseré around the
        // maquette window, transparent everywhere else. The F6 smoke can
        // SEE the liseré sit OVER the maquette diamond and the desk wedges,
        // proving the draw order, without burying B or C.
        _layerDFrontDecor.Texture = MakeDiamondLiserePlaceholder(size, residual);
        // Purely visual — never eats a click (a decor TextureRect must not
        // block the desk's _UnhandledInput routing).
        _layerDFrontDecor.MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Build a flat single-colour <see cref="ImageTexture"/> at the given
    /// size — the layer-A placeholder fill.
    /// </summary>
    private static ImageTexture MakeFlatPlaceholder(Vector2I size, Color fill)
    {
        var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        image.Fill(fill);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// <b>TEMPORARY (Rune, 2026-05-22 F6-readability fix).</b> Build the
    /// layer-D placeholder texture: fully transparent everywhere except a
    /// thin semi-transparent iso-diamond <i>liseré</i> inscribed in the
    /// residual maquette rect — the "losange-creux" outline.
    ///
    /// <para>
    /// The diamond is the iso 2:1 rhombus inscribed in the residual rect:
    /// apexes at the top, right, bottom and left mid-edges. The liseré is a
    /// few pixels thick. It is drawn so the F6 smoke confirms layer D paints
    /// over the maquette (B) and the desk wedges (C) — the previous
    /// full-screen opaque grid hid both. Replaced by Mira's Blender
    /// front-pass texture; the rest of the scene is unaffected.
    /// </para>
    /// </summary>
    private static ImageTexture MakeDiamondLiserePlaceholder(
        Vector2I size, HudRect residual)
    {
        const int thickness = 5;
        var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f)); // fully transparent

        var liseColor = new Color(0.82f, 0.78f, 0.66f, 0.55f);

        // The iso-diamond apexes inscribed in the residual rect.
        float cx = residual.X + residual.Width * 0.5f;
        float cy = residual.Y + residual.Height * 0.5f;
        float halfW = residual.Width * 0.5f;
        float halfH = residual.Height * 0.5f;

        var top = new Vector2(cx, residual.Y);
        var right = new Vector2(residual.X + residual.Width, cy);
        var bottom = new Vector2(cx, residual.Y + residual.Height);
        var left = new Vector2(residual.X, cy);

        StrokeSegment(image, size, top, right, thickness, liseColor);
        StrokeSegment(image, size, right, bottom, thickness, liseColor);
        StrokeSegment(image, size, bottom, left, thickness, liseColor);
        StrokeSegment(image, size, left, top, thickness, liseColor);

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// <b>TEMPORARY</b> — stamp a thick line segment into an
    /// <see cref="Image"/> for the layer-D liseré. A simple per-pixel walk:
    /// the liseré is a one-shot placeholder, not a hot path.
    /// </summary>
    private static void StrokeSegment(
        Image image, Vector2I size, Vector2 a, Vector2 b,
        int thickness, Color color)
    {
        float length = a.DistanceTo(b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length));
        int half = thickness / 2;
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            var p = a.Lerp(b, t);
            int px = Mathf.RoundToInt(p.X);
            int py = Mathf.RoundToInt(p.Y);
            for (int dx = -half; dx <= half; dx++)
            {
                for (int dy = -half; dy <= half; dy++)
                {
                    int x = px + dx;
                    int y = py + dy;
                    if (x >= 0 && x < size.X && y >= 0 && y < size.Y)
                    {
                        image.SetPixel(x, y, color);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Layer C — stand up the iso desk across its two SubViewports and hand
    /// its grid to the <c>GameState</c> authority. See the boxed
    /// architecture comment for the rationale.
    ///
    /// <para>
    /// Preserved verbatim from J3c-1septies / J3c-2 except: the floor
    /// TextureRect no longer carries the (removed) clip shader material, and
    /// <c>ApplyDeskClipFrontier</c> is gone. Everything else — the cosmetic
    /// board nudge, the shared camera frame, the floor fill rect, the
    /// <c>GameState.AdoptDeskGrid</c> seeding, the pawn placement — is
    /// unchanged.
    /// </para>
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
    /// </param>
    private void ConfigureDesk(Vector2 screen)
    {
        // The cosmetic local-X nudge, applied to BOTH desk boards IDENTICALLY
        // so the floor board and the entities board keep agreeing on
        // cell-to-pixel — see DeskBoardOffsetX (0 by default).
        var boardNudge = new Vector2(DeskBoardOffsetX, 0f);
        _deskFloorBoard.Position = boardNudge;
        _deskEntitiesBoard.Position = boardNudge;

        // Both desk SubViewports and both desk TextureRects are full-screen.
        var screenSize = new SysVec2(screen.X, screen.Y);
        _deskFloorViewport.Size = (Vector2I)screen;
        _deskEntitiesViewport.Size = (Vector2I)screen;
        _deskFloorTextureRect.Size = screen;
        _deskEntitiesTextureRect.Size = screen;

        // Show each desk SubViewport's render target through its TextureRect.
        // NEITHER TextureRect carries a material now — the clip shader is
        // gone. The entities rect is drawn after the floor rect so the
        // pawns paint over the floor.
        _deskFloorTextureRect.Texture = _deskFloorViewport.GetTexture();
        _deskEntitiesTextureRect.Texture = _deskEntitiesViewport.GetTexture();

        // Both desk rects keep MouseFilter = Ignore: the desk click is
        // routed in _UnhandledInput from the raw InputEvent, and Ignore
        // means an MMB press over the desk still reaches the maquette pan.
        _deskFloorTextureRect.MouseFilter = MouseFilterEnum.Ignore;
        _deskEntitiesTextureRect.MouseFilter = MouseFilterEnum.Ignore;

        // NEAREST sampling on both desk textures.
        _deskFloorTextureRect.TextureFilter = TextureFilterEnum.Nearest;
        _deskEntitiesTextureRect.TextureFilter = TextureFilterEnum.Nearest;

        // A modest camera zoom-out so the Company block frames comfortably
        // in the lower-left of the screen.
        var deskZoom = new Vector2(DeskCameraZoom, DeskCameraZoom);
        _deskFloorCamera.Zoom = deskZoom;
        _deskEntitiesCamera.Zoom = deskZoom;
        var deskVisibleSize = screenSize / DeskCameraZoom;

        // Resolve the Company formation cells (Godot-free, xUnit-pinned).
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // The desk camera Position is computed ONCE, then applied to BOTH
        // desk cameras — the two render worlds share one camera frame.
        var formationCentre = Vector2.Zero;
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            formationCentre +=
                _deskFloorBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
        }
        formationCentre /= slots.Count;

        // Park the immobile desk camera so the formation centroid lands in
        // the lower-left of the full-screen desk viewport.
        var screenOffsetFromCentre = new Vector2(
            screen.X * DeskFormationScreenFracX - screen.X * 0.5f,
            screen.Y * DeskFormationScreenFracY - screen.Y * 0.5f);
        var deskCameraPos =
            formationCentre - screenOffsetFromCentre / DeskCameraZoom;
        _deskFloorCamera.Position = deskCameraPos;
        _deskEntitiesCamera.Position = deskCameraPos;

        // Compute the viewport-covering floor fill rect from the FINAL
        // camera position; only the FLOOR board fills it.
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(deskCameraPos),
            new SysVec2(deskVisibleSize.X, deskVisibleSize.Y));
        _deskFloorBoard.SetDeskFloorFillRect(floorRect);

        // TEMPORARY (Rune, 2026-05-22 — F6-readability fix). The
        // desk_triangle_clip shader is gone; until Mira's Blender decor
        // carries the desk-floor shape, frame the placeholder desk floor to
        // the two bottom-corner wedges with a CPU draw-time clip. Without
        // this the desk floor renders as a full opaque rectangle that hides
        // the layer-B maquette — the exact F6 bug. The wedges are built in
        // the desk board's local pixel space: the visible viewport rect's
        // top-left is cameraCentre - deskVisibleSize/2 (slack excluded — the
        // slack only pads the fill rect, the wedges hug the real viewport).
        var deskVisibleTopLeft = new SysVec2(
            deskCameraPos.X - deskVisibleSize.X * 0.5f,
            deskCameraPos.Y - deskVisibleSize.Y * 0.5f);
        var deskTriangles = DeskTrianglePlaceholderLogic.Build(
            new SysVec2(deskVisibleSize.X, deskVisibleSize.Y),
            deskVisibleTopLeft.X,
            deskVisibleTopLeft.Y);
        _deskFloorBoard.SetDeskTrianglePlaceholder(deskTriangles);

        // J3c-2: hand the desk board's logical grid to the GameState
        // authority and seed the Company formation onto it.
        if (_deskEntitiesBoard.Grid is { } deskGrid)
        {
            _gameState.AdoptDeskGrid(deskGrid, slots);
        }
        else
        {
            GD.PushError("[GameScreen] desk entities board has no Grid at " +
                         "ConfigureDesk — J3c-2 desk interaction is disabled.");
        }

        // Place one placeholder pawn per slot as a layer-3 occupant of the
        // ENTITIES board (NOT the floor board).
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var pixel =
                _deskEntitiesBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
            var pawn = new DeskCompanyPawn
            {
                Name = $"DeskPawn{i}",
                Position = pixel,
            };
            pawn.Configure(
                slotIndex: i,
                isLeader: DeskSlotLayoutLogic.IsLeaderSlot(i),
                memberId: GameState.DeskMemberIdForSlot(i),
                board: _deskEntitiesBoard);
            _deskEntitiesBoard.AddOccupant(pawn);
        }
    }

    /// <summary>
    /// The pannable maquette content extent in world pixels. Prefers the real
    /// <see cref="IsoBoard"/> floor bitmap size; falls back to the
    /// placeholder iso-diamond bounding box while Mira's district asset is
    /// not yet wired (J2).
    /// </summary>
    private Vector2 ContentSize()
    {
        var texture = _maquette.GetBackgroundTextureSizeOrZero();
        if (texture != Vector2.Zero)
        {
            return texture;
        }

        int w = _maquette.PlaceholderGridWidth;
        int h = _maquette.PlaceholderGridHeight;
        float halfW = _maquette.TileWidthPx * 0.5f;
        float halfH = _maquette.TileWidthPx * 0.25f; // 2:1 ratio
        return new Vector2((w + h) * halfW, (w + h) * halfH);
    }

    /// <summary>
    /// The point the <c>MapCamera2D</c> must be parked on so the maquette
    /// content sits centred on screen — the content's GEOMETRIC centre.
    /// </summary>
    private Vector2 MaquetteContentCentre()
    {
        var texture = _maquette.GetBackgroundTextureSizeOrZero();
        if (texture != Vector2.Zero)
        {
            return texture * 0.5f;
        }

        var centre = MaquetteContentLogic.DiamondCentre(
            _maquette.PlaceholderGridWidth,
            _maquette.PlaceholderGridHeight,
            _maquette.TileWidthPx);
        return new Vector2(centre.X, centre.Y);
    }

    /// <summary>The SubViewport visible-rect size in world pixels.</summary>
    private Vector2 ViewportSize() => _mapContainer.Size;

    /// <summary>Trap #2 preflight: a fixed diagnostics block at <see cref="_Ready"/>.</summary>
    private void Preflight(Vector2 content, Vector2 viewport, PanVec2 cameraStart, Vector2 screen)
    {
        GD.Print($"[GameScreen] preflight: skip_bootstrap=" +
                 $"{HasMeta(SkipBootstrapMeta)}");

        // ------------------------------------------------------------------
        // [4-LAYER] — the four-layer diorama, draw order A -> B -> C -> D.
        // ------------------------------------------------------------------
        GD.Print($"[4-LAYER] draw order A->B->C->D->HUD | " +
                 $"A back decor (placeholder)='{_layerABackDecor.Name}' " +
                 $"index={_layerABackDecor.GetIndex()} | " +
                 $"D front decor (placeholder)='{_layerDFrontDecor.Name}' " +
                 $"index={_layerDFrontDecor.GetIndex()}");
        GD.Print($"[4-LAYER] layer A texture={_layerABackDecor.Texture?.GetSize()} " +
                 $"opaque grey placeholder (labelled 'COUCHE A') | layer D " +
                 $"texture={_layerDFrontDecor.Texture?.GetSize()} mostly-" +
                 $"transparent placeholder: thin iso-diamond liseré + label " +
                 $"'COUCHE D' (verify it paints OVER B and C in F6 without " +
                 $"burying them)");
        if (_layerABackDecor.GetIndex() >= _mapContainer.GetIndex()
            || _layerDFrontDecor.GetIndex() <= _deskEntitiesTextureRect.GetIndex())
        {
            GD.PushWarning("[GameScreen] 4-layer draw order is wrong — A must " +
                           "be before B and D must be after the desk layers.");
        }

        GD.Print($"[4-LAYER] recess knob: MapRecessDepthPx={MapRecessDepthPx} " +
                 $"(cosmetic vertical nudge of layer B; game logic stays flat)");

        GD.Print($"[GameScreen] preflight: SubViewport size={_mapViewport.Size} " +
                 $"container rect={_mapContainer.Size} " +
                 $"container offsetTop={_mapContainer.OffsetTop} " +
                 $"(= residual top + recess {MapRecessDepthPx})");
        GD.Print($"[GameScreen] preflight: maquette content extent={content} " +
                 $"(from {( _maquette.GetBackgroundTextureSizeOrZero() != Vector2.Zero ? "bitmap" : "placeholder grid")})");

        var geomCentre = MaquetteContentCentre();
        var boxCentre = content * 0.5f;
        GD.Print($"[GameScreen] preflight: maquette content geometric centre=" +
                 $"{geomCentre} bounding-box centre={boxCentre}");

        float halfVx = viewport.X * 0.5f;
        float halfVy = viewport.Y * 0.5f;
        GD.Print($"[GameScreen] preflight: camera clamp X=[{halfVx},{content.X - halfVx}] " +
                 $"Y=[{halfVy},{content.Y - halfVy}] start={cameraStart}");
        GD.Print($"[GameScreen] preflight: pan button={MapViewportPanLogic.PanButton} " +
                 $"state={_pan.State} -- _Input wired (trap #9, MMB gesture)");

        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
        GD.Print($"[GameScreen] preflight: HudLayer layer={_hudLayer.Layer} " +
                 $"bands top={HudBandHeight} bottom={HudBandHeight}");
        GD.Print($"[GameScreen] preflight: residual map rect=" +
                 $"[{residual.X},{residual.Y} {residual.Width}x{residual.Height}]");
        if (!HudLayoutLogic.HasUsableResidual(
                screen.X, screen.Y, HudBandHeight, HudBandHeight))
        {
            GD.PushWarning("[GameScreen] HUD bands leave no usable central " +
                           "rectangle — the maquette would be invisible.");
        }

        // ------------------------------------------------------------------
        // [DESK-DIAG] — layer C, the two-SubViewport desk.
        // ------------------------------------------------------------------
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        GD.Print($"[DESK-DIAG] desk framing: DeskCameraZoom={DeskCameraZoom} " +
                 $"formationFrac=({DeskFormationScreenFracX}," +
                 $"{DeskFormationScreenFracY})");

        GD.Print($"[DESK-DIAG] DeskBoard cosmetic nudge: DeskBoardOffsetX=" +
                 $"{DeskBoardOffsetX} => DeskFloorBoard.Position=" +
                 $"{_deskFloorBoard.Position} DeskEntitiesBoard.Position=" +
                 $"{_deskEntitiesBoard.Position}");

        GD.Print($"[DESK-DIAG] DeskFloorViewport: size={_deskFloorViewport.Size} " +
                 $"transparent_bg={_deskFloorViewport.TransparentBg} " +
                 $"floor TextureRect material=" +
                 $"{(_deskFloorTextureRect.Material is null ? "NONE (clip shader removed)" : "PRESENT")}");
        GD.Print($"[DESK-DIAG] DeskEntitiesViewport: size={_deskEntitiesViewport.Size} " +
                 $"transparent_bg={_deskEntitiesViewport.TransparentBg}");

        var camPos = _deskFloorCamera.Position;
        var entCamPos = _deskEntitiesCamera.Position;
        bool camerasAgree = camPos == entCamPos
                          && _deskFloorCamera.Zoom == _deskEntitiesCamera.Zoom;
        GD.Print($"[DESK-DIAG] desk cameras: floorCam Position={camPos} " +
                 $"zoom={_deskFloorCamera.Zoom} | entitiesCam Position={entCamPos} " +
                 $"zoom={_deskEntitiesCamera.Zoom} | AGREE={camerasAgree}");
        if (!camerasAgree)
        {
            GD.PushError("[GameScreen] the two desk cameras diverged — the " +
                         "pawns will not sit on the floor grid AND the J3c-2 " +
                         "click routing (one shared camera frame) is invalid.");
        }
        var deskVisibleSize = screen / DeskCameraZoom;
        var visTL = camPos - deskVisibleSize * 0.5f;
        var visBR = camPos + deskVisibleSize * 0.5f;

        // J3c-2 — desk authority state.
        GD.Print($"[DESK-DIAG] J3c-2 desk authority: GameState.DeskGrid=" +
                 $"{(_gameState.DeskGrid is null ? "NULL" : "adopted")} " +
                 $"occupants={_gameState.DeskCellOccupants.Count} " +
                 $"selection='{_gameState.SelectedCompanyMemberId ?? "(none)"}'");

        // J3c-2 — round-trip the screen->cell routing on each slot's screen
        // pixel, so a routing regression is visible in the Output.
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var cellV = new Vector2I(cell.Col, cell.Row);
            var worldLocal = _deskEntitiesBoard.CellToPixel(cellV);
            var worldOffset = worldLocal - camPos;
            var screenPx = new Vector2(
                worldOffset.X * DeskCameraZoom + screen.X * 0.5f,
                worldOffset.Y * DeskCameraZoom + screen.Y * 0.5f);
            var routed = DeskInputRoutingLogic.ScreenToDeskWorld(
                ToSys(screenPx), ToSys(screen),
                ToSys(camPos), ToSys(_deskEntitiesCamera.Zoom));
            var routedCell =
                _deskEntitiesBoard.PixelToCell(new Vector2(routed.X, routed.Y));
            bool routeOk = routedCell == cellV;
            GD.Print($"[GameScreen] preflight:   J3c-2 routing slot {i} " +
                     $"cell={cell} -> screenPx=({screenPx.X:0},{screenPx.Y:0}) " +
                     $"-> routedCell=({routedCell.X},{routedCell.Y}) " +
                     $"ROUND-TRIP-OK={routeOk}");
            if (!routeOk)
            {
                GD.PushError($"[GameScreen] J3c-2 click routing for slot {i} " +
                             "does not round-trip — a desk click would select " +
                             "the wrong cell.");
            }
        }

        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(camPos),
            new SysVec2(deskVisibleSize.X, deskVisibleSize.Y));
        GD.Print($"[DESK-DIAG] desk floor fill rect: " +
                 $"topLeft=({floorRect.TopLeft.X},{floorRect.TopLeft.Y}) " +
                 $"size=({floorRect.Size.X},{floorRect.Size.Y})");

        // ------------------------------------------------------------------
        // [MIRA-LAYOUT] — the frozen desk-floor rect Mira needs to size the
        // wf_e1_desk_wood_floor.png texture against (production brief §5.1 /
        // §7.2 desk_floor_rect). This is THE number the layout-lock hands
        // to Mira: the exact rect the IsoBoard floor texture must cover.
        // ------------------------------------------------------------------
        GD.Print($"[MIRA-LAYOUT] desk_floor_rect (FROZEN, layer C IsoBoard " +
                 $"floor texture size for wf_e1_desk_wood_floor.png): " +
                 $"top_left_px=[{floorRect.TopLeft.X:0},{floorRect.TopLeft.Y:0}] " +
                 $"size_px=[{floorRect.Size.X:0},{floorRect.Size.Y:0}] " +
                 $"(desk board local pixels, immobile DeskCamera2D, " +
                 $"DeskCameraZoom={DeskCameraZoom}, screen={screen.X:0}x{screen.Y:0})");

        GD.Print($"[GameScreen] preflight: Company slots count={slots.Count} " +
                 $"leader=slot{DeskSlotLayoutLogic.LeaderSlotIndex} " +
                 $"cells=[{string.Join(", ", slots)}]");
        if (slots.Count != DeskSlotLayoutLogic.CompanySlotCount)
        {
            GD.PushWarning("[GameScreen] Company slot count drifted from " +
                           $"{DeskSlotLayoutLogic.CompanySlotCount}.");
        }

        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var cellV = new Vector2I(cell.Col, cell.Row);
            var floorLocal = _deskFloorBoard.CellToPixel(cellV);
            var entLocal = _deskEntitiesBoard.CellToPixel(cellV);
            bool boardsAgree = floorLocal == entLocal;
            bool onScreen = entLocal.X >= visTL.X && entLocal.X <= visBR.X
                          && entLocal.Y >= visTL.Y && entLocal.Y <= visBR.Y;
            GD.Print($"[GameScreen] preflight:   desk pawn {i} cell={cell} " +
                     $"-> entitiesLocal=({entLocal.X},{entLocal.Y}) " +
                     $"boardsAgree={boardsAgree} onScreen={onScreen}");
            if (!boardsAgree)
            {
                GD.PushError($"[GameScreen] desk pawn {i} ({cell}) projects " +
                             "differently on the floor and entities boards.");
            }
            if (!onScreen)
            {
                GD.PushWarning($"[GameScreen] desk pawn {i} ({cell}) is " +
                               "outside the desk viewport — it will not render.");
            }
        }
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 / System.Numerics --------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);

    private static SysVec2 ToSys(Vector2 v) => new(v.X, v.Y);
}
