using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scenes.Ui;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scenes.Screens;

// =============================================================================
//  CARTOGRAPHER'S-DESK DIORAMA — THE LOCKED VIEWPORT ARCHITECTURE
//  (J3c-1septies, Rune, 2026-05-21 — THIS section is the single source of
//   truth; stop re-deriving the layout from the F6 capture.)
// =============================================================================
//
//  THE PROBLEM THAT KEPT REGRESSING. The desk must read as two brown corner
//  triangles; the central maquette diamond shows the pannable district behind
//  them. The desk FLOOR therefore HAS to be clipped to a triangle (else the
//  brown rectangle hides the maquette). But the PAWNS standing on that floor
//  must NOT be clipped: a pawn near the floor's edge has its body and head
//  sticking UP above the triangle of floor, into the maquette zone, and that
//  is correct — a piece on a board is taller than the board.
//
//  => Floor and entities CANNOT share one clipped render. They are split.
//
//  THE ARCHITECTURE — THREE SubViewports (NOT two — see "viewport count"
//  below). All three are direct children of this Control; never under
//  HudLayer.
//
//   1. MapViewportContainer / MapViewport  -- the pannable district maquette.
//        * SubViewportContainer clips it to the residual rect between the HUD
//          bands. Camera = MapCamera2D, pannable (MMB drag). One IsoBoard
//          (Maquette), small iso scale.
//
//   2. DeskFloorViewport                   -- the desk FLOOR + iso grid only.
//        * transparent_bg. One IsoBoard (DeskFloorBoard), large iso scale,
//          DrawPlaceholderFloor + DrawPlaceholderGrid on. NO pawns.
//        * Camera = DeskFloorCamera2D, immobile.
//        * Shown via DeskFloorTextureRect, which CARRIES desk_triangle_clip
//          .gdshader. The shader discard;s every fragment outside the two
//          bottom corner triangles. So the FLOOR is clipped to a triangle.
//          Correct — the desk floor must not hide the maquette.
//
//   3. DeskEntitiesViewport                -- the desk PAWNS only.
//        * transparent_bg. One IsoBoard (DeskEntitiesBoard), SAME large iso
//          scale as the floor board, grid + floor draw OFF. Carries the
//          DeskCompanyPawn occupants.
//        * Camera = DeskEntitiesCamera2D, immobile.
//        * Shown via DeskEntitiesTextureRect, which has NO shader — it is
//          NOT clipped. Drawn AFTER DeskFloorTextureRect in the tree, so the
//          pawns paint OVER the clipped floor. A pawn that overflows above
//          the floor triangle survives — that is the whole point.
//
//  WHY THE PAWN PLACEMENT NEVER DRIFTS (the bit that broke twice before).
//  Tentative A moved the pawns onto a screen-space node and hand-wrote a
//  desk-local -> screen reprojection that broke on every knob change. The
//  reprojection is GONE. DeskFloorBoard and DeskEntitiesBoard are the SAME
//  IsoBoard scene at the SAME TileWidthPx. DeskFloorCamera2D and
//  DeskEntitiesCamera2D are parked at the IDENTICAL Position and Zoom —
//  computed ONCE, in pure-C#, and applied to both. A Camera2D cannot live in
//  two viewports, so there are two camera NODES; they are kept mirror-
//  identical by construction, not by a sync signal. Result: cell (col,row)
//  projects to the SAME screen pixel in both viewports. A pawn's position is
//  DeskEntitiesBoard.CellToPixel(cell) and nothing else.
//
//  WHY THE SHARED CAMERA FRAME ALSO MAKES INPUT ROUTING TRIVIAL (J3c-2,
//  Rune, 2026-05-22). The desk's "three SubViewports" looked like it would
//  force three screen->world conversions for a click. It does not — for the
//  same reason the pawn placement never drifts. The FLOOR camera and the
//  ENTITIES camera share ONE Position/Zoom; the desk SubViewports and their
//  TextureRects are full-screen at the screen origin. So a screen pixel maps
//  to ONE desk-world point, by ONE affine map (DeskInputRoutingLogic). From
//  the desk-world point, one WorldToCell on the shared projection gives the
//  cell; the click is hit-tested against the cell's occupant. The maquette
//  viewport is a separate iso scale / pannable camera, but a desk click is
//  never a maquette click (the desk TextureRects sit on top) — desk vs
//  maquette routing is the J6 concern. J3c-2 owns only the desk side, and
//  it is one conversion, not three.
//
//  WHY THE DESK BOARDS ARE NO LONGER NUDGED (J3c-1octies, Rune, 2026-05-21).
//  DeskBoardOffsetX is 0; a non-zero leftover made the bottom desk wedges
//  asymmetric. Kept as a named constant so a future deliberate nudge has one
//  obvious, version-controlled home — applied to BOTH boards identically.
//
//  WHY THREE VIEWPORTS IS ACCEPTED (the locked decision was "two SubViewports
//  = maquette + desk"). One clipped render cannot satisfy both clipped-floor
//  and unclipped-entities, so the desk's single render world is split into
//  two co-located render worlds sharing one camera frame. The count goes
//  2 -> 3 and that is correct.
//
//  COORDINATE FLOW.
//    slot cell (TileCoordinate)
//      -> DeskEntitiesBoard.CellToPixel(cell)   [board-local pixels]
//      -> pawn.Position                          [board-local]
//      -> rendered under DeskEntitiesCamera2D    [-> DeskEntitiesViewport
//                                                 texture, transparent bg]
//      -> DeskEntitiesTextureRect, full screen, NO shader  [-> screen]
//  The floor flows the identical path through DeskFloorBoard /
//  DeskFloorCamera2D / DeskFloorViewport / DeskFloorTextureRect, the only
//  difference being the clip shader on the floor TextureRect.
//
//  INPUT FLOW (J3c-2, the inverse direction).
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
/// authoritative viewport architecture is the boxed comment above this class
/// — read it before touching the desk.
///
/// <para>
/// <b>What this slice delivers (J3c-1 + J3c-2).</b> J3c-1 stood up the
/// three-layer diorama: the clipped pannable maquette (J3a), the fixed HUD
/// frame (J3b), and the static iso desk (J3c-1). <b>J3c-2 makes the desk
/// interactive</b>: a left-click on a Company pawn selects it (the pawn
/// shows a discreet pion-sur-plateau selection cue); a left-click on a
/// free, occupiable desk cell while a pawn is selected emits a tactical
/// placement intent — <c>GameState</c> validates it and, only on accept,
/// the pawn animates to the new cell. Free pawn dragging is deferred
/// post-MVP (see the input section below).
/// </para>
///
/// <para>
/// <b>J3a — the pannable maquette.</b> A <see cref="SubViewportContainer"/>
/// hosting a <see cref="SubViewport"/> renders the maquette into an isolated
/// world, clipped to the container rect. A <see cref="Camera2D"/> inside the
/// SubViewport pans (MMB drag) — the camera moves, never the content.
/// </para>
///
/// <para>
/// <b>J3b — the fixed HUD frame.</b> A <see cref="CanvasLayer"/>
/// (<c>HudLayer</c>) carries two full-width <see cref="Panel"/> bands welded
/// to the screen top and bottom. Both bands are
/// <see cref="HudBandHeight"/> = 32 px.
/// </para>
///
/// <para>
/// <b>J3c-1 — the static iso desk, the THREE-viewport split.</b> The desk is
/// a SECOND iso space at a larger iso scale, rendered through TWO co-located
/// SubViewports: <c>DeskFloorViewport</c> (clipped floor) and
/// <c>DeskEntitiesViewport</c> (unclipped pawns). See the boxed architecture
/// comment for the full rationale.
/// </para>
///
/// <para>
/// <b>J3c-2 — the interactive desk (Rune, 2026-05-22).</b> The desk takes
/// left-click input. The state authority is <c>GameState</c> — the desk
/// grid occupancy, the selection, and the placement decision all live
/// there (NPC-autonomy lock 2026-05-09). <see cref="ConfigureDesk"/> hands
/// the desk board's <c>IsoGrid</c> to <c>GameState.AdoptDeskGrid</c> and
/// seeds the Company formation onto it. A click is routed in
/// <see cref="_UnhandledInput"/>:
/// <list type="number">
///   <item><see cref="DeskInputRoutingLogic"/> maps the screen pixel to a
///     desk-world point using the shared desk camera frame.</item>
///   <item>The desk board's <c>IsoProjection</c> turns the world point into
///     a cell.</item>
///   <item><see cref="DeskSelectionLogic"/> resolves the click to a member
///     id (or null), and computes the next selection.</item>
///   <item>If a pawn is already selected and the click hit a different free
///     cell, <c>GameState.RequestCompanyMemberPlacement</c> is called — the
///     tactical placement intent.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Input routing — _Input vs _UnhandledInput (trap #9).</b> The MMB
/// drag-pan stays in <see cref="_Input"/>: the pan wants the middle button
/// before anything else can claim it. The desk LEFT-click is handled in
/// <see cref="_UnhandledInput"/> — it is the lowest-priority interaction on
/// this screen, so it must fire only when no higher-priority node (a HUD
/// button, a future modal) has already consumed the event. This is the
/// PoI-router cascade discipline (memo project_wayfinders_poi_integration)
/// applied to the desk: gesture-priority code in <c>_Input</c>,
/// entity-selection fallthrough in <c>_UnhandledInput</c>.
/// </para>
///
/// <para>
/// <b>No drag gesture — trap #5 is not triggered (J3c-2 scope).</b> J3c-2
/// placement is by destination-CLICK, not by drag. A left press-and-release
/// in place is not a gesture and cannot conflict with the MMB drag-pan, so
/// no mutual-exclusion arbitration is needed. Free pawn dragging — which
/// WOULD add a left-drag gesture next to the MMB pan and so WOULD need
/// trap-#5 mutual exclusion — is deliberately deferred post-MVP: the PoC
/// chain (J8) needs only "a recruit appears in a slot", not free pawn
/// movement (roadmap J3c-2 scope note).
/// </para>
///
/// <para>
/// <b>The desk camera is immobile.</b> Both desk cameras are parked once in
/// <see cref="ConfigureDesk"/> and never moved.
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The maquette container, both
/// desk <see cref="SubViewport"/>s, and both desk <see cref="TextureRect"/>s
/// are direct children of this <see cref="Control"/> — never under
/// <c>HudLayer</c>.
/// </para>
/// </summary>
public partial class GameScreen : Control
{
    /// <summary>
    /// Autoload-injected opening flow must not draw its CanvasLayer over
    /// this scene when launched standalone for the J3a/J3b/J3c smoke
    /// (trap #1bis). The <c>.tscn</c> root carries
    /// <c>metadata/skip_opening_bootstrap</c>; this constant documents the
    /// contract for readers of the code.
    /// </summary>
    private const string SkipBootstrapMeta = "skip_opening_bootstrap";

    /// <summary>
    /// Fixed height of each HUD band, pixels — the same value for the top
    /// and the bottom band. <b>The single named source of truth</b> for the
    /// band height: it must match the <c>HudTop</c> panel's
    /// <c>offset_bottom</c> and the <c>HudBottom</c> panel's
    /// <c>-offset_top</c> in <c>GameScreen.tscn</c>.
    ///
    /// <para>
    /// J3c-1octies: this value is also fed to
    /// <c>DeskClipFrontierLogic.MaquetteDiamond</c> as the bottom HUD band
    /// height, so the maquette diamond's lower point lands flush on the
    /// bottom HUD band's top edge.
    /// </para>
    /// </summary>
    private const float HudBandHeight = 32f;

    /// <summary>
    /// <b>Desk-camera framing — knob A: the desk camera zoom.</b> A Godot
    /// <see cref="Camera2D"/> <c>Zoom</c> below 1.0 zooms OUT. 0.85 frames
    /// the 7-pawn block comfortably inside the thin bottom-left desk
    /// triangle. Applied to BOTH desk cameras (they share one frame).
    /// </summary>
    private const float DeskCameraZoom = 0.85f;

    /// <summary>
    /// <b>Desk-camera framing — knob B/C: where the Company formation's
    /// centroid lands on screen, as a fraction of the screen.</b> The
    /// formation sits in the bottom-LEFT desk triangle, parked low and
    /// close to the corner so the whole block stays clear of the clip
    /// hypotenuse.
    /// </summary>
    private const float DeskFormationScreenFracX = 0.20f;
    private const float DeskFormationScreenFracY = 0.85f;

    /// <summary>
    /// <b>Desk-board cosmetic nudge — the desk boards' local-X offset,
    /// pixels. Default 0 (J3c-1octies).</b> Applied to BOTH desk boards in
    /// <see cref="ConfigureDesk"/>. Kept as a named constant so a future
    /// deliberate nudge has one obvious, version-controlled home.
    ///
    /// <para>
    /// <b>J3c-2 — load-bearing for input routing.</b> The screen → desk-cell
    /// conversion (<see cref="DeskInputRoutingLogic"/> +
    /// <c>PixelToCell</c>) assumes the board-local space and the desk world
    /// coincide, which holds only while this nudge is 0. If a future nudge
    /// is dialled in, the routing must subtract it before
    /// <c>PixelToCell</c>; the [DESK-DIAG] block prints the live board
    /// positions so a non-zero nudge is visible.
    /// </para>
    /// </summary>
    private const float DeskBoardOffsetX = 0f;

    /// <summary>
    /// Shader uniform names on <c>desk_triangle_clip.gdshader</c> — the
    /// twin-corner clip on the FLOOR TextureRect. Held as constants so a
    /// rename surfaces at compile time.
    /// </summary>
    private const string ClipLeftPointUniform = "clip_left_point";
    private const string ClipLeftNormalUniform = "clip_left_normal";
    private const string ClipRightPointUniform = "clip_right_point";
    private const string ClipRightNormalUniform = "clip_right_normal";
    private const string ClipSplitUniform = "clip_split_u";

    private SubViewportContainer _mapContainer = null!;
    private SubViewport _mapViewport = null!;
    private Camera2D _mapCamera = null!;
    private IsoBoard _maquette = null!;
    private CanvasLayer _hudLayer = null!;

    // The desk's THREE-viewport split (see the boxed architecture comment):
    // the FLOOR viewport (clipped) and the ENTITIES viewport (not clipped).
    private SubViewport _deskFloorViewport = null!;
    private Camera2D _deskFloorCamera = null!;
    private IsoBoard _deskFloorBoard = null!;
    private TextureRect _deskFloorTextureRect = null!;

    private SubViewport _deskEntitiesViewport = null!;
    private Camera2D _deskEntitiesCamera = null!;
    private IsoBoard _deskEntitiesBoard = null!;
    private TextureRect _deskEntitiesTextureRect = null!;

    private readonly MapViewportPanLogic _pan = new();

    /// <summary>
    /// The <c>GameState</c> autoload — the desk's selection / placement
    /// authority (J3c-2). Resolved in <see cref="_Ready"/>. The desk input
    /// in <see cref="_UnhandledInput"/> calls into it; it does not own the
    /// state.
    /// </summary>
    private GameState _gameState = null!;

    /// <summary>
    /// The desk screen size cached at <see cref="_Ready"/>, reused by the
    /// J3c-2 input routing so a click does not re-query
    /// <c>GetViewportRect</c> on every event. The desk SubViewports are
    /// full-screen so this is also the desk viewport size.
    /// </summary>
    private Vector2 _screenSize;

    public override void _Ready()
    {
        _mapContainer = GetNode<SubViewportContainer>("MapViewportContainer");
        _mapViewport = GetNode<SubViewport>("MapViewportContainer/MapViewport");
        _mapCamera = GetNode<Camera2D>("MapViewportContainer/MapViewport/MapCamera2D");
        _maquette = GetNode<IsoBoard>("MapViewportContainer/MapViewport/Maquette");
        _hudLayer = GetNode<CanvasLayer>("HudLayer");

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

        // J3c-2: the desk selection / placement authority. An autoload, so
        // it is up before any GameScreen exists.
        _gameState = GetNode<GameState>("/root/GameState");

        // J3b: size the map viewport to the residual central rectangle —
        // the screen minus the two fixed HUD bands. The screen size is read
        // from GetViewportRect (the real window rect, valid at _Ready).
        var screen = GetViewportRect().Size;
        _screenSize = screen;
        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
        ApplyResidualRect(residual);
        _mapViewport.Size = (Vector2I)_mapContainer.Size;

        // Park the maquette camera on the content's GEOMETRIC CENTRE so the
        // maquette is centred on screen (J3c-1sexies left-lean fix).
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
    /// when no higher-priority node (a HUD button, a future modal) has
    /// already consumed the event — that is exactly what
    /// <c>_UnhandledInput</c> guarantees. Same cascade discipline as the
    /// PoI router (memo project_wayfinders_poi_integration).
    ///
    /// <para>
    /// A left-click is acted on at the RELEASE edge (a press that is part
    /// of a future drag would be cancelled before release; J3c-2 has no
    /// drag, but releasing-edge is the correct selection convention and
    /// keeps the contract right for the day drag lands). The whole routing
    /// + selection + placement decision is pure-C#
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
    ///
    /// <para>
    /// The decision tree, all of it pure-C#:
    /// <list type="bullet">
    ///   <item>Click hit a pawn → that becomes (or toggles off) the
    ///     selection: <c>GameState.SetSelectedCompanyMember</c>.</item>
    ///   <item>Click hit a free cell AND a pawn is currently selected →
    ///     a placement intent for the selected pawn:
    ///     <c>GameState.RequestCompanyMemberPlacement</c>. The pawn moves
    ///     only if the authority accepts.</item>
    ///   <item>Click hit bare floor with nothing selected → deselect
    ///     (a harmless no-op if nothing was selected).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="screenPos">
    /// The left-click position in screen pixels, from
    /// <c>InputEventMouseButton.Position</c>.
    /// </param>
    private void HandleDeskLeftClick(Vector2 screenPos)
    {
        // 1. Screen pixel -> desk-world point, using the shared desk camera
        //    frame (the floor and entities cameras are mirror-identical, so
        //    either serves — see the boxed architecture comment).
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
            // On an accepted move the pawn is now on the target cell and
            // the gesture is done — clear the selection so the next click
            // starts fresh. A rejected / off-grid intent keeps the
            // selection so the player can retry.
            if (verdict == DeskPlacementLogic.PlacementVerdict.Accepted
                || verdict == DeskPlacementLogic.PlacementVerdict.NoOp)
            {
                _gameState.SetSelectedCompanyMember(null);
            }
        }
        else
        {
            // Nothing selected, clicked bare floor — explicit deselect
            // (idempotent no-op if nothing was selected).
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
    /// the <see cref="SubViewportContainer"/>'s offsets.
    /// </summary>
    private void ApplyResidualRect(HudRect rect)
    {
        _mapContainer.OffsetLeft = rect.X;
        _mapContainer.OffsetTop = rect.Y;
        _mapContainer.OffsetRight = rect.X + rect.Width;
        _mapContainer.OffsetBottom = rect.Y + rect.Height;
    }

    /// <summary>
    /// J3c-1 / J3c-1septies / J3c-2: stand up the iso desk across its three
    /// SubViewports and hand its grid to the <c>GameState</c> authority.
    /// See the boxed architecture comment for the full rationale.
    ///
    /// <para>
    /// Applies the cosmetic local-X nudge to BOTH desk boards (0 by default),
    /// sizes both desk SubViewports, wires the floor SubViewport's texture
    /// (with the clip shader) and the entities SubViewport's texture (no
    /// shader) onto their TextureRects, computes ONE desk camera
    /// Position/Zoom and parks BOTH desk cameras at it, fills the floor,
    /// places one placeholder pawn per Company slot as a layer-3 occupant
    /// of the ENTITIES board, and computes the twin-corner clip.
    /// </para>
    ///
    /// <para>
    /// <b>J3c-2 addition.</b> Once the desk boards are ready, the desk
    /// board's <c>IsoGrid</c> is handed to <c>GameState.AdoptDeskGrid</c>,
    /// which seeds the Company formation onto it as authoritative occupancy
    /// — the desk grid becomes <c>GameState</c>-owned. Each pawn is
    /// <c>Configure</c>d with its member id and a reference to the entities
    /// board so it can resolve a placement confirmation to a target pixel.
    /// </para>
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
    /// </param>
    private void ConfigureDesk(Vector2 screen)
    {
        // The cosmetic local-X nudge, applied to BOTH desk boards IDENTICALLY
        // so the floor board and the entities board keep agreeing on
        // cell-to-pixel — see DeskBoardOffsetX (0 since J3c-1octies).
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
        // The FLOOR TextureRect carries the clip shader; the ENTITIES
        // TextureRect has NO shader and is drawn after the floor rect.
        _deskFloorTextureRect.Texture = _deskFloorViewport.GetTexture();
        _deskEntitiesTextureRect.Texture = _deskEntitiesViewport.GetTexture();

        // Both desk rects keep MouseFilter = Ignore: the desk click is
        // routed in _UnhandledInput from the raw InputEvent (the desk-world
        // conversion needs the screen pixel, not a Control-local one), and
        // Ignore means an MMB press over a desk corner still reaches the
        // maquette pan. The desk does NOT consume input through these
        // TextureRects — it consumes it through _UnhandledInput.
        _deskFloorTextureRect.MouseFilter = MouseFilterEnum.Ignore;
        _deskEntitiesTextureRect.MouseFilter = MouseFilterEnum.Ignore;

        // NEAREST sampling on both desk textures.
        _deskFloorTextureRect.TextureFilter = TextureFilterEnum.Nearest;
        _deskEntitiesTextureRect.TextureFilter = TextureFilterEnum.Nearest;

        // A modest camera zoom-out so the Company block frames comfortably
        // inside the bottom-left desk triangle.
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

        // J3c-2: hand the desk board's logical grid to the GameState
        // authority and seed the Company formation onto it. After this the
        // desk occupancy is GameState-owned; the click pipeline in
        // _UnhandledInput reads GameState.DeskCellOccupants.
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
        // ENTITIES board (NOT the floor board). The pawn lives inside the
        // unclipped DeskEntitiesViewport, in the entities board's own local
        // space — the SAME coordinate frame as the floor.
        //
        // J3c-2: each pawn is Configure'd with its authoritative member id
        // (GameState.DeskMemberIdForSlot) and the entities board, so it can
        // (a) tell whether a selection / placement signal is about itself
        // and (b) resolve a placement confirmation cell to a pixel.
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

        ApplyDeskClipFrontier(screen);
    }

    /// <summary>
    /// Compute the maquette/desk <b>twin-corner</b> triangular clip frontier
    /// and push it into the <c>DeskFloorTextureRect</c>'s clip shader. The
    /// clip runs on the FLOOR TextureRect only — the desk pawns ride the
    /// separate unclipped <c>DeskEntitiesViewport</c>. All geometry is
    /// derived in <see cref="DeskClipFrontierLogic"/> (Godot-free,
    /// xUnit-pinned).
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
    /// </param>
    private void ApplyDeskClipFrontier(Vector2 screen)
    {
        var deskOrigin = SysVec2.Zero;
        var deskSize = new SysVec2(screen.X, screen.Y);

        var diamond = DeskClipFrontierLogic.MaquetteDiamond(
            deskSize, HudBandHeight, HudBandHeight);

        var frontier = DeskClipFrontierLogic.Compute(
            diamond, deskOrigin, deskSize);

        var material = (ShaderMaterial)_deskFloorTextureRect.Material;
        material.SetShaderParameter(
            ClipLeftPointUniform,
            new Vector2(frontier.LeftPointUv.X, frontier.LeftPointUv.Y));
        material.SetShaderParameter(
            ClipLeftNormalUniform,
            new Vector2(frontier.LeftNormalUv.X, frontier.LeftNormalUv.Y));
        material.SetShaderParameter(
            ClipRightPointUniform,
            new Vector2(frontier.RightPointUv.X, frontier.RightPointUv.Y));
        material.SetShaderParameter(
            ClipRightNormalUniform,
            new Vector2(frontier.RightNormalUv.X, frontier.RightNormalUv.Y));
        material.SetShaderParameter(ClipSplitUniform, frontier.SplitU);

        // [DESK-DIAG] — the twin-corner clip.
        GD.Print($"[DESK-DIAG] proportion knob: " +
                 $"MaquetteHalfWidthScreenFracX=" +
                 $"{DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX} " +
                 $"IsoEdgeSlope={DeskClipFrontierLogic.IsoEdgeSlope} " +
                 $"-- iso slope is LOCKED, not a knob; the lower point is " +
                 $"DERIVED from the bottom HUD band ({HudBandHeight}px)");
        GD.Print($"[DESK-DIAG] maquette diamond (screen px): " +
                 $"topApex=({diamond.TopApex.X},{diamond.TopApex.Y}) " +
                 $"leftApex=({diamond.LeftApex.X},{diamond.LeftApex.Y}) " +
                 $"rightApex=({diamond.RightApex.X},{diamond.RightApex.Y}) " +
                 $"lowerPoint=({diamond.LowerPoint.X},{diamond.LowerPoint.Y}) " +
                 $"-- lowerPoint.Y should equal screenH-{HudBandHeight}=" +
                 $"{screen.Y - HudBandHeight} (flush on the bottom HUD band)");
        GD.Print($"[DESK-DIAG] diamond edge slopes (must all be iso 0.5): " +
                 $"topLeft={DeskClipFrontierLogic.EdgeSlope(diamond.TopApex, diamond.LeftApex)} " +
                 $"topRight={DeskClipFrontierLogic.EdgeSlope(diamond.TopApex, diamond.RightApex)} " +
                 $"bottomLeft={DeskClipFrontierLogic.EdgeSlope(diamond.LeftApex, diamond.LowerPoint)} " +
                 $"bottomRight={DeskClipFrontierLogic.EdgeSlope(diamond.LowerPoint, diamond.RightApex)}");
        GD.Print($"[DESK-DIAG] twin clip (UV pushed to FLOOR shader): " +
                 $"leftPoint=({frontier.LeftPointUv.X},{frontier.LeftPointUv.Y}) " +
                 $"leftNormal=({frontier.LeftNormalUv.X},{frontier.LeftNormalUv.Y}) " +
                 $"rightPoint=({frontier.RightPointUv.X},{frontier.RightPointUv.Y}) " +
                 $"rightNormal=({frontier.RightNormalUv.X},{frontier.RightNormalUv.Y}) " +
                 $"splitU={frontier.SplitU}");
        GD.Print($"[DESK-DIAG] clip applies to the FLOOR viewport ONLY -- " +
                 $"the DeskEntitiesViewport (pawns) carries NO shader, so a " +
                 $"pawn overflowing above the floor triangle is NOT clipped");
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
        GD.Print($"[GameScreen] preflight: SubViewport size={_mapViewport.Size} " +
                 $"container rect={_mapContainer.Size}");
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
        // [DESK-DIAG] — the THREE-viewport desk.
        // ------------------------------------------------------------------
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        GD.Print($"[DESK-DIAG] proportion knob: " +
                 $"MaquetteHalfWidthScreenFracX=" +
                 $"{DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX} " +
                 $"DeskCameraZoom={DeskCameraZoom} " +
                 $"formationFrac=({DeskFormationScreenFracX}," +
                 $"{DeskFormationScreenFracY})");

        GD.Print($"[DESK-DIAG] DeskBoard cosmetic nudge: DeskBoardOffsetX=" +
                 $"{DeskBoardOffsetX} => DeskFloorBoard.Position=" +
                 $"{_deskFloorBoard.Position} DeskEntitiesBoard.Position=" +
                 $"{_deskEntitiesBoard.Position}");

        GD.Print($"[DESK-DIAG] DeskFloorViewport (CLIPPED): " +
                 $"size={_deskFloorViewport.Size} " +
                 $"transparent_bg={_deskFloorViewport.TransparentBg}");
        GD.Print($"[DESK-DIAG] DeskEntitiesViewport (NOT clipped): " +
                 $"size={_deskEntitiesViewport.Size} " +
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
        // pixel, so a routing regression is visible in the Output: the slot
        // cell projected to a screen pixel must route back to the same cell.
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var cellV = new Vector2I(cell.Col, cell.Row);
            // desk-world pixel of the slot -> screen pixel (inverse of the
            // routing): screenPos = (worldOffset * zoom) + screenCentre.
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

        var clipMaterial = (ShaderMaterial)_deskFloorTextureRect.Material!;
        GD.Print($"[GameScreen] preflight: desk twin clip (FLOOR shader) " +
                 $"leftPoint={clipMaterial.GetShaderParameter(ClipLeftPointUniform)} " +
                 $"rightPoint={clipMaterial.GetShaderParameter(ClipRightPointUniform)} " +
                 $"splitU={clipMaterial.GetShaderParameter(ClipSplitUniform)}");
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 / System.Numerics --------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);

    private static SysVec2 ToSys(Vector2 v) => new(v.X, v.Y);
}
