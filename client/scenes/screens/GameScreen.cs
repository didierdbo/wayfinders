using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scenes.Ui;
using Wayfinders.Client.Scripts.Screens;
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
//  below). All three are direct children of this Control; never under HudLayer.
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
//  desk-local -> screen reprojection; that reprojection broke on every knob
//  change. The reprojection is GONE. DeskFloorBoard and DeskEntitiesBoard are
//  the SAME IsoBoard scene at the SAME TileWidthPx. DeskFloorCamera2D and
//  DeskEntitiesCamera2D are parked at the IDENTICAL Position and Zoom —
//  computed ONCE, in pure-C#, and applied to both. A Camera2D cannot live in
//  two viewports, so there are two camera NODES; they are kept mirror-
//  identical by construction, not by a sync signal. Result: cell (col,row)
//  projects to the SAME screen pixel in both viewports. A pawn's position is
//  DeskEntitiesBoard.CellToPixel(cell) and nothing else — no reprojection,
//  nothing to drift. Moving the proportion knob
//  (MaquetteHalfWidthScreenFracX, the camera framing fracs) only re-runs the
//  SAME pure-C# placement for both boards and re-feeds the SAME clip shader —
//  the floor/entity split is structural and untouched. This is why it will
//  not re-clip a pawn.
//
//  WHY THE DESK BOARDS ARE NO LONGER NUDGED (J3c-1octies, Rune, 2026-05-21).
//  Earlier rounds applied a -32 px DeskBoardOffsetX to both desk boards — a
//  value Didier eyeballed in the editor BEFORE the maquette content was
//  centred (the J3c-1sexies left-lean fix). Once the clip diamond and the
//  maquette content were both centred on screenWidth/2, that leftover -32 px
//  shifted the desk grid pattern off-centre and made the bottom-left and
//  bottom-right desk wedges read with unequal-width grid bands — the
//  asymmetry Didier saw ("bande marron plus epaisse a droite qu'a gauche").
//  The nudge is now 0: the desk content is centred like everything else, so
//  the two desk corners are mirror-symmetric. DeskBoardOffsetX is kept as a
//  named constant (still applied to BOTH boards identically) so a future
//  deliberate nudge has one obvious, version-controlled home — but its
//  default is 0, not an eyeballed leftover.
//
//  WHY THREE VIEWPORTS IS ACCEPTED (the locked decision was "two SubViewports
//  = maquette + desk"). The REAL constraint behind that decision was never a
//  viewport count — it was "floor clipped, entities NOT clipped, two iso
//  scales, two camera behaviours". One clipped render cannot satisfy both
//  clipped-floor and unclipped-entities, so the desk's single render world is
//  split into two co-located render worlds sharing one camera frame. This is
//  the minimum structure that satisfies the actual constraint; the count goes
//  2 -> 3 and that is correct. (Rune, 2026-05-21, explicitly acting this.)
//
//  COORDINATE FLOW.
//    slot cell (TileCoordinate)
//      -> DeskEntitiesBoard.CellToPixel(cell)   [board-local pixels]
//      -> pawn.Position                          [board-local; the board
//                                                 carries the DeskBoardOffsetX
//                                                 nudge, 0 by default]
//      -> rendered under DeskEntitiesCamera2D    [-> DeskEntitiesViewport
//                                                 texture, transparent bg]
//      -> DeskEntitiesTextureRect, full screen, NO shader  [-> screen]
//  The floor flows the identical path through DeskFloorBoard /
//  DeskFloorCamera2D / DeskFloorViewport / DeskFloorTextureRect, the only
//  difference being the clip shader on the floor TextureRect.
//
// =============================================================================

/// <summary>
/// Root of the Game Screen Shell — the cartographer's-desk diorama in which
/// eM and eT are shown (roadmap <c>wayfinders-mvp-roadmap-eM-eT</c>). The
/// authoritative viewport architecture is the boxed comment above this class
/// — read it before touching the desk.
///
/// <para>
/// <b>What this slice delivers (J3c-1).</b> Three layers of the diorama: the
/// <b>clipped, pannable map maquette</b> (J3a), the <b>fixed top/bottom HUD
/// frame</b> (J3b), and the <b>static iso desk</b> (J3c-1) — the desk floor
/// clipped to two brown corner triangles, the Company pawns standing on it
/// fully visible even where they overflow above the floor triangle. Desk
/// interactivity (pawn selection / placement, J3c-2) is a later milestone.
/// </para>
///
/// <para>
/// <b>J3a — the pannable maquette.</b> A <see cref="SubViewportContainer"/>
/// hosting a <see cref="SubViewport"/> renders the maquette into an isolated
/// world, clipped to the container rect for free. A <see cref="Camera2D"/>
/// <i>inside</i> the SubViewport pans (MMB drag) — the camera moves, never
/// the content. The content is an <c>IsoBoard.tscn</c> instance.
/// </para>
///
/// <para>
/// <b>J3b — the fixed HUD frame.</b> A <see cref="CanvasLayer"/>
/// (<c>HudLayer</c>) carries two full-width <see cref="Panel"/> bands welded
/// to the screen top and bottom — a <see cref="CanvasLayer"/> has its own
/// canvas transform unaffected by any 2D camera. Both bands are
/// <see cref="HudBandHeight"/> = 32 px.
/// </para>
///
/// <para>
/// <b>J3c-1 — the static iso desk, the THREE-viewport split (J3c-1septies,
/// 2026-05-21, Rune — the architecture that ends the clipped-pawn
/// regression).</b> The desk is a SECOND iso space at a larger iso scale, and
/// it is rendered through TWO co-located SubViewports rather than one:
/// <list type="bullet">
///   <item><b><c>DeskFloorViewport</c></b> — the desk floor + iso grid only.
///     Its <c>DeskFloorTextureRect</c> carries <c>desk_triangle_clip.gdshader</c>,
///     so the FLOOR is clipped to the two bottom corner triangles. The floor
///     <i>must</i> be clipped — an unclipped brown rectangle would hide the
///     maquette.</item>
///   <item><b><c>DeskEntitiesViewport</c></b> — the Company pawns only. Its
///     <c>DeskEntitiesTextureRect</c> has NO shader: the pawns are NOT
///     clipped. It is drawn after the floor TextureRect, so the pawns paint
///     over the clipped floor. A pawn near the floor edge whose body / head
///     overflows ABOVE the floor triangle stays fully visible — the locked
///     rule (Didier, repeated across J3c-1bis..sexies): the triangular clip
///     is for the floor ONLY, entities standing on it are never clipped.</item>
/// </list>
/// The two desk boards (<c>DeskFloorBoard</c>, <c>DeskEntitiesBoard</c>) are
/// the same <c>IsoBoard.tscn</c> at the same <see cref="DeskBoardOffsetX"/>
/// (0 by default) and tile scale; the two desk cameras are parked at an
/// identical Position/Zoom computed once in pure-C#. So a slot cell projects
/// to the same screen pixel in both — a pawn's placement is
/// <c>DeskEntitiesBoard.CellToPixel(cell)</c> with NO reprojection (the
/// reprojection is what broke the abandoned screen-space attempt).
/// </para>
///
/// <para>
/// <b>The desk camera is immobile.</b> Both desk cameras are parked once in
/// <see cref="ConfigureDesk"/> so the Company formation frames inside the
/// bottom-left desk triangle, and never moved.
/// </para>
///
/// <para>
/// <b>The clip shader and the diamond.</b> A <see cref="SubViewport"/>
/// texture is rectangular; the floor SubViewport is shown via a plain
/// <see cref="TextureRect"/> running <c>desk_triangle_clip.gdshader</c>,
/// which <c>discard;</c>s every fragment in <i>neither</i> bottom corner
/// triangle — i.e. the central maquette diamond. The maquette diamond is a
/// strict iso 2:1 rhombus that overflows the screen sideways; it is sized by
/// <c>DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX</c> and its lower
/// point is placed flush on the bottom HUD band's top edge (J3c-1octies — so
/// no desk strip shows below the diamond). The clip geometry is computed by
/// <see cref="DeskClipFrontierLogic"/> (Godot-free, xUnit-pinned) from the
/// diamond apexes — it works in screen-UV [0,1] and is wholly independent of
/// the desk boards, so a board transform or a knob change never re-clips a
/// pawn.
/// </para>
///
/// <para>
/// <b>The maquette content is centred on screen (J3c-1sexies fix,
/// 2026-05-21).</b> The clip diamond is centred on <c>screenWidth/2</c>; the
/// maquette CONTENT must be too. <see cref="_Ready"/> parks
/// <c>MapCamera2D</c> on the placeholder diamond's <i>geometric centre</i>
/// (<see cref="MaquetteContentLogic.DiamondCentre"/>) — NOT on the content
/// bounding box centre, which for the iso placeholder is offset from the
/// diamond centre and made the maquette lean left.
/// </para>
///
/// <list type="bullet">
///   <item><b>Full-screen desk rects.</b> Both desk <see cref="SubViewport"/>s
///     and both desk <see cref="TextureRect"/>s are full screen so the two
///     corner triangles reach the real screen corners.</item>
///   <item><b>Floor AND grid both fill the whole viewport.</b> The desk reads
///     as uniform brown with iso grid everywhere inside both kept triangles —
///     <c>DeskFloorBoard</c> stripes the grid over the whole floor rect (see
///     <c>IsoBoard.DrawGridOverRect</c>); the <c>DeskBackground</c> panel is
///     the same brown so any sub-pixel gap is invisible.</item>
///   <item><b>NEAREST sampling.</b> Both desk <see cref="TextureRect"/>s
///     sample with NEAREST filtering — crisp clip edges, crisp pawns.</item>
///   <item><b>Reliable screen size.</b> <see cref="_Ready"/> reads
///     <c>GetViewportRect().Size</c> — the real window rect, valid the
///     instant <c>_Ready</c> runs.</item>
/// </list>
///
/// <para>
/// <b>[DESK-DIAG] instrumentation.</b> <see cref="ConfigureDesk"/> /
/// <see cref="ApplyDeskClipFrontier"/> / <see cref="Preflight"/> print a
/// <c>[DESK-DIAG]</c> block: the two desk viewport / TextureRect sizes, the
/// shared desk camera park + zoom, the maquette diamond apexes, the clip
/// hypotenuses + split, the floor fill rect, and a per-pawn on-screen check.
/// Kept on purpose — a regressed fix is diagnosed from the next F6 Output.
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The maquette container, both
/// desk <see cref="SubViewport"/>s, and both desk <see cref="TextureRect"/>s
/// are direct children of this <see cref="Control"/> — never under
/// <c>HudLayer</c>.
/// </para>
///
/// <para>
/// <b>Input routing (trap #9).</b> Pan tracking is wired in
/// <see cref="_Input"/>. Both desk <see cref="TextureRect"/>s have
/// <c>MouseFilter = Ignore</c>, so an MMB press over a desk corner still
/// reaches the maquette pan.
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
    /// bottom HUD band's top edge. If this changes, the diamond's lower
    /// point follows automatically — no magic FracY to re-tune.
    /// </para>
    /// </summary>
    private const float HudBandHeight = 32f;

    /// <summary>
    /// <b>Desk-camera framing — knob A: the desk camera zoom.</b>
    ///
    /// <para>
    /// A Godot <see cref="Camera2D"/> <c>Zoom</c> below 1.0 zooms OUT — it
    /// shows more of the desk world, so the Company formation's screen
    /// footprint is smaller. 0.85 = the formation reads at 85% of its native
    /// desk-iso size, giving the 7-pawn block a comfortable margin inside the
    /// thin bottom-left desk triangle.
    /// </para>
    ///
    /// <para>
    /// <b>Applied to BOTH desk cameras.</b> <c>DeskFloorCamera2D</c> and
    /// <c>DeskEntitiesCamera2D</c> get the identical zoom — see the boxed
    /// architecture comment: the two desk render worlds share one camera
    /// frame so cell-to-pixel agrees between them.
    /// </para>
    /// </summary>
    private const float DeskCameraZoom = 0.85f;

    /// <summary>
    /// <b>Desk-camera framing — knob B/C: where the Company formation's
    /// centroid lands on screen, as a fraction of the screen.</b>
    ///
    /// <para>
    /// The Company pawns sit in the bottom-LEFT desk triangle. The formation
    /// is parked low and close to the bottom-left corner —
    /// <c>(0.20, 0.85)</c> — so the whole 7-pawn block stays clear of the
    /// clip hypotenuse above it. The pawns can now safely sit ON or NEAR the
    /// hypotenuse: they ride the unclipped <c>DeskEntitiesViewport</c>, so an
    /// overflow above the floor triangle is rendered, not sliced. The
    /// fraction is kept conservative anyway so the formation reads as
    /// foreground inside the desk.
    /// </para>
    /// </summary>
    private const float DeskFormationScreenFracX = 0.20f;
    private const float DeskFormationScreenFracY = 0.85f;

    /// <summary>
    /// <b>Desk-board cosmetic nudge — the desk boards' local-X offset,
    /// pixels. Default 0 (J3c-1octies).</b> Applied to BOTH
    /// <c>DeskFloorBoard.Position.X</c> and <c>DeskEntitiesBoard.Position.X</c>
    /// in <see cref="ConfigureDesk"/>.
    ///
    /// <para>
    /// <b>Why it is 0 now.</b> Earlier rounds carried a -32 px shift Didier
    /// eyeballed in the editor — but that was dialled in BEFORE the maquette
    /// content was centred on <c>screenWidth/2</c> (the J3c-1sexies left-lean
    /// fix). Once the clip diamond and the maquette content were both
    /// centred, the leftover -32 px shifted the desk grid pattern off-centre
    /// and made the bottom-left and bottom-right desk wedges read with
    /// unequal-width grid bands — the asymmetry Didier saw at the bottom of
    /// the screen ("bande marron plus epaisse a droite qu'a gauche"). With
    /// the nudge at 0 the desk content is centred like the rest of the
    /// diorama and the two desk corners are mirror-symmetric.
    /// </para>
    ///
    /// <para>
    /// <b>Why it is kept as a named constant.</b> A deliberate future nudge
    /// (a real asset that genuinely needs an off-centre anchor) has one
    /// obvious, version-controlled home. The constant lives in code, not in
    /// the <c>.tscn</c>: <see cref="ConfigureDesk"/> owns the desk runtime
    /// configuration. Its default is now an explicit, justified 0 — not an
    /// eyeballed leftover.
    /// </para>
    ///
    /// <para>
    /// <b>Why it must be applied to BOTH desk boards identically.</b> The
    /// floor board and the entities board MUST agree on cell-to-pixel (see
    /// the boxed architecture comment). Giving one board the nudge and not
    /// the other would slide the pawns off the floor grid. The two cameras
    /// stay un-nudged siblings, so the whole desk content slides together on
    /// screen.
    /// </para>
    /// </summary>
    private const float DeskBoardOffsetX = 0f;

    /// <summary>
    /// Shader uniform names on <c>desk_triangle_clip.gdshader</c> — the
    /// twin-corner clip on the FLOOR TextureRect. Held as constants so a
    /// rename surfaces at compile time on this one line rather than as a
    /// silent no-op clip at runtime.
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

        // J3b: size the map viewport to the residual central rectangle —
        // the screen minus the two fixed HUD bands.
        //
        // J3c-1bis F6 fix: the screen size is read from GetViewportRect, NOT
        // from this Control's Size. At _Ready() a Control authored with
        // anchors_preset 15 has NOT had its layout resolved yet — Size is
        // still the design-time default. GetViewportRect().Size is the real
        // window rect and is valid the instant _Ready runs.
        var screen = GetViewportRect().Size;
        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
        ApplyResidualRect(residual);

        // The SubViewport's render size follows the (now residual) container
        // rect, so the maquette is clipped exactly to the central panel
        // between the HUD bands.
        _mapViewport.Size = (Vector2I)_mapContainer.Size;

        // Park the maquette camera on the content's GEOMETRIC CENTRE so the
        // maquette is centred on screen.
        //
        // J3c-1sexies F6 fix (2026-05-21): the camera was parked on
        // `contentSize * 0.5` — the content BOUNDING-BOX centre. For the iso
        // placeholder grid the bounding box and the diamond's real geometric
        // centre do not coincide (the diamond is anchored so x >= 0, which
        // shifts the bounding box), so the maquette leaned left of the clip
        // diamond. Now the camera is parked on the diamond's geometric
        // centre via MaquetteContentLogic (placeholder path) — or on the
        // bitmap centre once Mira's floor texture exists (then bounding box
        // == geometric centre and the shortcut is exact).
        var viewport = ViewportSize();
        var contentCentre = MaquetteContentCentre();
        var clampedStart = MapViewportPanLogic.ClampToContent(
            ToPan(contentCentre), ToPan(ContentSize()), ToPan(viewport));
        _mapCamera.Position = ToGodot(clampedStart);

        ConfigureDesk(screen);

        Preflight(ContentSize(), viewport, clampedStart, screen);
    }

    /// <summary>
    /// Input seam (trap #9): routes the middle-button drag-pan into
    /// <see cref="MapViewportPanLogic"/> and applies the clamped result onto
    /// the SubViewport's <see cref="Camera2D"/>.
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
    /// J3c-1 / J3c-1septies: stand up the static iso desk across its TWO
    /// co-located SubViewports — the floor viewport (clipped) and the
    /// entities viewport (not clipped). See the boxed architecture comment
    /// at the top of the file for the full rationale.
    ///
    /// <para>
    /// Applies the cosmetic local-X nudge to BOTH desk boards (0 by default
    /// since J3c-1octies — see <see cref="DeskBoardOffsetX"/>), sizes both
    /// desk SubViewports, wires the floor SubViewport's texture (with the
    /// clip shader) and the entities SubViewport's texture (no shader) onto
    /// their TextureRects, computes ONE desk camera Position/Zoom in pure-C#
    /// and parks BOTH desk cameras at that identical frame, fills the floor,
    /// places one placeholder pawn per Company slot as a layer-3 occupant of
    /// the ENTITIES board, and computes the twin-corner clip the floor shader
    /// consumes.
    /// </para>
    ///
    /// <para>
    /// All the arithmetic is pure-C# (Godot-free, xUnit-pinned); this method
    /// only converts to / from Godot types and applies results onto nodes.
    /// No pawn carries game state — J3c-1 is static.
    /// </para>
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
    /// </param>
    private void ConfigureDesk(Vector2 screen)
    {
        // The cosmetic local-X nudge, applied to BOTH desk boards IDENTICALLY
        // so the floor board and the entities board keep agreeing on
        // cell-to-pixel — see DeskBoardOffsetX. A board's whole subtree
        // (pawns, grid, floor) rides the offset; the desk cameras are
        // un-nudged siblings, so the content slides together on screen.
        //
        // J3c-1octies: DeskBoardOffsetX is 0 — the desk content is centred
        // like the maquette content. A non-zero leftover was what made the
        // bottom desk wedges asymmetric (Didier's "bande marron plus epaisse
        // a droite").
        var boardNudge = new Vector2(DeskBoardOffsetX, 0f);
        _deskFloorBoard.Position = boardNudge;
        _deskEntitiesBoard.Position = boardNudge;

        // Both desk SubViewports and both desk TextureRects are full-screen,
        // so the clip shader's UV space matches the floor viewport 1:1 and
        // the entities viewport overlays the screen exactly.
        var screenSize = new SysVec2(screen.X, screen.Y);
        _deskFloorViewport.Size = (Vector2I)screen;
        _deskEntitiesViewport.Size = (Vector2I)screen;
        _deskFloorTextureRect.Size = screen;
        _deskEntitiesTextureRect.Size = screen;

        // Show each desk SubViewport's render target through its TextureRect.
        // The FLOOR TextureRect carries the clip shader (floor clipped to two
        // triangles). The ENTITIES TextureRect has NO shader — the pawns are
        // not clipped. The entities rect is drawn AFTER the floor rect in the
        // .tscn tree, so the pawns paint over the clipped floor.
        _deskFloorTextureRect.Texture = _deskFloorViewport.GetTexture();
        _deskEntitiesTextureRect.Texture = _deskEntitiesViewport.GetTexture();

        // The static desk takes no input in J3c-1. Ignore on BOTH desk rects
        // means an MMB press over a desk corner still reaches the maquette
        // pan, whichever rect is on top.
        _deskFloorTextureRect.MouseFilter = MouseFilterEnum.Ignore;
        _deskEntitiesTextureRect.MouseFilter = MouseFilterEnum.Ignore;

        // NEAREST sampling on both desk textures — the clip boundary is the
        // shader's crisp discard line, the pawns are crisp, no cross-edge
        // blend.
        _deskFloorTextureRect.TextureFilter = TextureFilterEnum.Nearest;
        _deskEntitiesTextureRect.TextureFilter = TextureFilterEnum.Nearest;

        // A modest camera zoom-out so the Company block frames comfortably
        // inside the bottom-left desk triangle. A Camera2D Zoom < 1 shows
        // MORE world, so the visible desk-local rect is `screen / zoom`.
        var deskZoom = new Vector2(DeskCameraZoom, DeskCameraZoom);
        _deskFloorCamera.Zoom = deskZoom;
        _deskEntitiesCamera.Zoom = deskZoom;
        var deskVisibleSize = screenSize / DeskCameraZoom;

        // Resolve the Company formation cells (Godot-free, xUnit-pinned).
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // The desk camera Position is computed ONCE, then applied to BOTH
        // desk cameras — the two render worlds share one camera frame, so a
        // slot cell projects to the same screen pixel in both. The floor
        // board and the entities board are the same IsoBoard scene at the
        // same tile scale, so CellToPixel agrees; using the FLOOR board to
        // resolve the centroid (or the entities board — same result) is
        // arbitrary, the answer is identical.
        var formationCentre = Vector2.Zero;
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            formationCentre +=
                _deskFloorBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
        }
        formationCentre /= slots.Count;

        // Park the immobile desk camera so the formation centroid lands in
        // the lower-left of the full-screen desk viewport. A Camera2D's
        // Position is the world point shown at the screen centre; with a zoom
        // of `z` a screen offset `d` from centre maps to a world offset
        // `d / z`. The formation should appear at `screen * frac`, i.e.
        // `screen*frac - screen*0.5` screen-pixels from centre.
        //
        // `formationCentre` is the centroid of the slot cells in a board's
        // LOCAL space (CellToPixel); with DeskBoardOffsetX at 0 the board
        // local space and the desk world coincide, so no offset term is
        // needed here.
        var screenOffsetFromCentre = new Vector2(
            screen.X * DeskFormationScreenFracX - screen.X * 0.5f,
            screen.Y * DeskFormationScreenFracY - screen.Y * 0.5f);
        var deskCameraPos =
            formationCentre - screenOffsetFromCentre / DeskCameraZoom;
        _deskFloorCamera.Position = deskCameraPos;
        _deskEntitiesCamera.Position = deskCameraPos;

        // Now the desk camera is parked, compute the viewport-covering floor
        // fill rect from the FINAL camera position. With the zoom-out the
        // desk world the camera shows is `screen/zoom`, so the floor must
        // cover that larger desk-local rect. DeskFloorRectLogic (Godot-free,
        // xUnit-pinned) computes the rect; only the FLOOR board fills it.
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(deskCameraPos),
            new SysVec2(deskVisibleSize.X, deskVisibleSize.Y));
        _deskFloorBoard.SetDeskFloorFillRect(floorRect);

        // Place one placeholder pawn per slot as a layer-3 occupant of the
        // ENTITIES board (NOT the floor board). The pawn lives inside the
        // unclipped DeskEntitiesViewport, in the entities board's own local
        // space under the entities camera — the SAME coordinate frame as the
        // floor (the two boards/cameras are mirror-identical), so its
        // position is simply CellToPixel of the slot cell. Because the
        // entities viewport carries NO clip shader, a pawn whose body / head
        // overflows above the floor triangle is rendered, not sliced — the
        // locked rule.
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
                isLeader: DeskSlotLayoutLogic.IsLeaderSlot(i));
            _deskEntitiesBoard.AddOccupant(pawn);
        }

        ApplyDeskClipFrontier(screen);
    }

    /// <summary>
    /// Compute the maquette/desk <b>twin-corner</b> triangular clip frontier
    /// and push it into the <c>DeskFloorTextureRect</c>'s clip shader.
    ///
    /// <para>
    /// <b>The clip runs on the FLOOR TextureRect only.</b> Only the desk
    /// floor is clipped to two corner triangles — the desk pawns ride the
    /// separate unclipped <c>DeskEntitiesViewport</c> (see the boxed
    /// architecture comment). The frontier follows the centred maquette
    /// diamond: the two clip hypotenuses are the diamond's two LOWER edges,
    /// and a vertical split at the diamond's lower point bounds each corner
    /// triangle to its own side. The diamond's lower point is placed flush on
    /// the bottom HUD band's top edge (J3c-1octies) so no desk strip shows
    /// below the diamond. All of it is derived in
    /// <see cref="DeskClipFrontierLogic"/> (Godot-free, xUnit-pinned).
    /// </para>
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
    /// </param>
    private void ApplyDeskClipFrontier(Vector2 screen)
    {
        // The desk floor TextureRect is full-screen at the screen origin.
        var deskOrigin = SysVec2.Zero;
        var deskSize = new SysVec2(screen.X, screen.Y);

        // The centred maquette diamond: a strict iso 2:1 rhombus overflowing
        // the screen sideways. Its lower point is placed flush on the BOTTOM
        // HUD band's top edge (HudBandHeight) — J3c-1octies — so no strip of
        // desk floor shows between the diamond and the bottom HUD.
        var diamond = DeskClipFrontierLogic.MaquetteDiamond(
            deskSize, HudBandHeight, HudBandHeight);

        // The two corner-triangle half-planes + the vertical split.
        var frontier = DeskClipFrontierLogic.Compute(
            diamond, deskOrigin, deskSize);

        // The clip shader lives on the FLOOR TextureRect — that is what makes
        // the FLOOR (and only the floor) a triangle.
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

        // [DESK-DIAG] — the twin-corner clip in BOTH spaces.
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
        // [DESK-DIAG] J3c-1nonies — the EXACT mirror symmetry Didier asked
        // for ("il faut que ce soit exact"). Paste these lines after the F6
        // to confirm the bottom zone is a perfect left/right mirror.
        {
            float centreX = screen.X * 0.5f;
            float leftGap = centreX - diamond.LeftApex.X;
            float rightGap = diamond.RightApex.X - centreX;
            GD.Print($"[DESK-DIAG] SYMMETRY pointe basse: lowerPoint.X=" +
                     $"{diamond.LowerPoint.X} vs screenW/2={centreX} " +
                     $"=> delta={diamond.LowerPoint.X - centreX} " +
                     $"(must be 0 — pointe basse pile au centre)");
            GD.Print($"[DESK-DIAG] SYMMETRY apex: leftGap={leftGap} " +
                     $"rightGap={rightGap} delta={leftGap - rightGap} " +
                     $"| apexY left={diamond.LeftApex.Y} right=" +
                     $"{diamond.RightApex.Y} (gaps + Ys must be equal)");
            GD.Print($"[DESK-DIAG] SYMMETRY normals: leftNormal=(" +
                     $"{frontier.LeftNormalUv.X},{frontier.LeftNormalUv.Y}) " +
                     $"rightNormal=({frontier.RightNormalUv.X}," +
                     $"{frontier.RightNormalUv.Y}) => nx must be opposite " +
                     $"(sum={frontier.LeftNormalUv.X + frontier.RightNormalUv.X}), " +
                     $"ny equal (delta=" +
                     $"{frontier.LeftNormalUv.Y - frontier.RightNormalUv.Y})");
            GD.Print($"[DESK-DIAG] SYMMETRY points: rightPoint=(" +
                     $"{frontier.RightPointUv.X},{frontier.RightPointUv.Y}) " +
                     $"vs mirror-of-left=(" +
                     $"{2f * frontier.SplitU - frontier.LeftPointUv.X}," +
                     $"{frontier.LeftPointUv.Y}) — must coincide; splitU=" +
                     $"{frontier.SplitU} must be 0.5");
        }
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

        // Placeholder: the iso-diamond of the IsoBoard's placeholder grid
        // spans (w+h) half-widths across and (w+h) half-heights tall.
        int w = _maquette.PlaceholderGridWidth;
        int h = _maquette.PlaceholderGridHeight;
        float halfW = _maquette.TileWidthPx * 0.5f;
        float halfH = _maquette.TileWidthPx * 0.25f; // 2:1 ratio
        return new Vector2((w + h) * halfW, (w + h) * halfH);
    }

    /// <summary>
    /// The point the <c>MapCamera2D</c> must be parked on so the maquette
    /// content sits centred on screen — the content's GEOMETRIC centre.
    ///
    /// <para>
    /// J3c-1sexies (2026-05-21): with a real floor bitmap the geometric
    /// centre is the bitmap-rect centre (<c>size * 0.5</c>). For the iso
    /// placeholder grid it is the diamond's geometric centre, which is NOT
    /// the bounding-box centre — see <see cref="MaquetteContentLogic"/> for
    /// why the difference made the maquette lean left of the clip diamond.
    /// </para>
    /// </summary>
    private Vector2 MaquetteContentCentre()
    {
        var texture = _maquette.GetBackgroundTextureSizeOrZero();
        if (texture != Vector2.Zero)
        {
            // A rectangular bitmap: bounding box centre IS the geometric
            // centre.
            return texture * 0.5f;
        }

        // Placeholder iso grid: the geometric centre of the diamond, NOT the
        // bounding-box centre (the J3c-1sexies left-lean fix).
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

        // J3c-1sexies: the camera is parked on the content GEOMETRIC centre,
        // not the bounding-box centre. Print both so a left-lean regression
        // is visible in the Output: if the two diverge and the camera is on
        // the wrong one, the maquette will lean.
        var geomCentre = MaquetteContentCentre();
        var boxCentre = content * 0.5f;
        GD.Print($"[GameScreen] preflight: maquette content geometric centre=" +
                 $"{geomCentre} bounding-box centre={boxCentre} " +
                 $"-- camera parked on the GEOMETRIC centre (J3c-1sexies " +
                 $"left-lean fix); a divergence here is why box-centre leaned");

        float halfVx = viewport.X * 0.5f;
        float halfVy = viewport.Y * 0.5f;
        GD.Print($"[GameScreen] preflight: camera clamp X=[{halfVx},{content.X - halfVx}] " +
                 $"Y=[{halfVy},{content.Y - halfVy}] start={cameraStart}");
        GD.Print($"[GameScreen] preflight: pan button={MapViewportPanLogic.PanButton} " +
                 $"state={_pan.State} -- _Input wired (trap #9)");

        // J3b HUD diagnostics.
        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
        GD.Print($"[GameScreen] preflight: HudLayer layer={_hudLayer.Layer} " +
                 $"bands top={HudBandHeight} bottom={HudBandHeight} " +
                 $"-- CanvasLayer overlay (trap #1, legitimate)");
        GD.Print($"[GameScreen] preflight: residual map rect=" +
                 $"[{residual.X},{residual.Y} {residual.Width}x{residual.Height}]");
        if (!HudLayoutLogic.HasUsableResidual(
                screen.X, screen.Y, HudBandHeight, HudBandHeight))
        {
            GD.PushWarning("[GameScreen] HUD bands leave no usable central " +
                           "rectangle — the maquette would be invisible.");
        }

        // ------------------------------------------------------------------
        // [DESK-DIAG] — the THREE-viewport desk: the floor viewport (clipped)
        // and the entities viewport (not clipped). The next blocks recense
        // every desk element with its real runtime rect / size / position.
        // ------------------------------------------------------------------
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // (0) The proportion knob that decides maquette vs desk balance.
        GD.Print($"[DESK-DIAG] proportion knob: " +
                 $"MaquetteHalfWidthScreenFracX=" +
                 $"{DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX} " +
                 $"IsoEdgeSlope={DeskClipFrontierLogic.IsoEdgeSlope} " +
                 $"DeskCameraZoom={DeskCameraZoom} " +
                 $"formationFrac=({DeskFormationScreenFracX}," +
                 $"{DeskFormationScreenFracY}) " +
                 $"-- diamond lower point DERIVED from bottom HUD band " +
                 $"({HudBandHeight}px), no magic FracY");

        // (0b) Desk-board cosmetic nudge — applied to BOTH desk boards.
        GD.Print($"[DESK-DIAG] DeskBoard cosmetic nudge: DeskBoardOffsetX=" +
                 $"{DeskBoardOffsetX} => DeskFloorBoard.Position=" +
                 $"{_deskFloorBoard.Position} DeskEntitiesBoard.Position=" +
                 $"{_deskEntitiesBoard.Position} -- BOTH boards nudged " +
                 $"identically; 0 keeps the desk content centred so the two " +
                 $"bottom desk wedges are mirror-symmetric (J3c-1octies)");

        // (1) The two desk viewports + the two desk TextureRects.
        GD.Print($"[DESK-DIAG] DeskFloorViewport (CLIPPED): " +
                 $"size={_deskFloorViewport.Size} " +
                 $"transparent_bg={_deskFloorViewport.TransparentBg}");
        GD.Print($"[DESK-DIAG] DeskEntitiesViewport (NOT clipped): " +
                 $"size={_deskEntitiesViewport.Size} " +
                 $"transparent_bg={_deskEntitiesViewport.TransparentBg}");
        GD.Print($"[DESK-DIAG] DeskFloorTextureRect: " +
                 $"Size={_deskFloorTextureRect.Size} " +
                 $"hasShaderMaterial={_deskFloorTextureRect.Material is ShaderMaterial} " +
                 $"-- carries desk_triangle_clip (the FLOOR is the triangle)");
        GD.Print($"[DESK-DIAG] DeskEntitiesTextureRect: " +
                 $"Size={_deskEntitiesTextureRect.Size} " +
                 $"hasMaterial={_deskEntitiesTextureRect.Material is not null} " +
                 $"-- NO shader: the pawns are never clipped, even on overflow");

        // (2) The shared desk camera frame — parked identically on BOTH desk
        // cameras. A divergence here would mean the pawns drift off the floor.
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
                         "pawns will not sit on the floor grid. This breaks " +
                         "the three-viewport architecture invariant.");
        }
        var deskVisibleSize = screen / DeskCameraZoom;
        var visTL = camPos - deskVisibleSize * 0.5f;
        var visBR = camPos + deskVisibleSize * 0.5f;

        // (3) DeskBackground panel — behind the desk TextureRects.
        var deskBg = GetNodeOrNull<Panel>("DeskBackground");
        GD.Print($"[DESK-DIAG] DeskBackground Panel: present={deskBg is not null} " +
                 $"-- authored bg_color must equal IsoBoard.FloorFillColor=" +
                 $"({IsoBoard.FloorFillColor.R},{IsoBoard.FloorFillColor.G}," +
                 $"{IsoBoard.FloorFillColor.B},{IsoBoard.FloorFillColor.A})");

        // (4) The desk floor fill rect — only the FLOOR board fills it.
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(camPos),
            new SysVec2(deskVisibleSize.X, deskVisibleSize.Y));
        GD.Print($"[DESK-DIAG] desk floor fill rect (DeskFloorRectLogic): " +
                 $"topLeft=({floorRect.TopLeft.X},{floorRect.TopLeft.Y}) " +
                 $"size=({floorRect.Size.X},{floorRect.Size.Y}) " +
                 $"edgeSlack={DeskFloorRectLogic.EdgeSlackPx}px");

        // (5) The twin-corner clip frontier.
        var screenSize = new SysVec2(screen.X, screen.Y);
        var diamond = DeskClipFrontierLogic.MaquetteDiamond(
            screenSize, HudBandHeight, HudBandHeight);
        var frontier = DeskClipFrontierLogic.Compute(
            diamond, SysVec2.Zero, screenSize);
        GD.Print($"[DESK-DIAG] maquette diamond (screen px): " +
                 $"topApex=({diamond.TopApex.X},{diamond.TopApex.Y}) " +
                 $"leftApex=({diamond.LeftApex.X},{diamond.LeftApex.Y}) " +
                 $"rightApex=({diamond.RightApex.X},{diamond.RightApex.Y}) " +
                 $"lowerPoint=({diamond.LowerPoint.X},{diamond.LowerPoint.Y}) " +
                 $"-- lowerPoint.Y flush on bottom HUD band top edge " +
                 $"(screenH-{HudBandHeight}={screen.Y - HudBandHeight})");
        GD.Print($"[DESK-DIAG] twin clip (UV): " +
                 $"leftPoint=({frontier.LeftPointUv.X},{frontier.LeftPointUv.Y}) " +
                 $"rightPoint=({frontier.RightPointUv.X},{frontier.RightPointUv.Y}) " +
                 $"splitU={frontier.SplitU}");
        GD.Print($"[DESK-DIAG] SYMMETRY (J3c-1nonies, F6 confirm): " +
                 $"pointe-basse.X={diamond.LowerPoint.X} (screenW/2=" +
                 $"{screen.X * 0.5f}) | apex gaps L=" +
                 $"{screen.X * 0.5f - diamond.LeftApex.X} R=" +
                 $"{diamond.RightApex.X - screen.X * 0.5f} | normals nx-sum=" +
                 $"{frontier.LeftNormalUv.X + frontier.RightNormalUv.X} " +
                 $"ny-delta={frontier.LeftNormalUv.Y - frontier.RightNormalUv.Y} " +
                 $"splitU={frontier.SplitU} -- all deltas must be 0, splitU 0.5");

        GD.Print($"[DESK-DIAG] >> see also IsoBoard '{_deskFloorBoard.Name}' " +
                 $"[DESK-DIAG] _Draw lines for grid coverage vs floor rect.");

        // J3c-1 desk diagnostics.
        GD.Print($"[GameScreen] preflight: screen={screen} (GetViewportRect) " +
                 $"-- three-viewport desk: floor (clipped) + entities (not)");
        GD.Print($"[GameScreen] preflight: desk iso tile=" +
                 $"{_deskFloorBoard.TileWidthPx}px (entities board tile=" +
                 $"{_deskEntitiesBoard.TileWidthPx}px, MUST match) " +
                 $"(maquette tile={_maquette.TileWidthPx}px)");
        GD.Print($"[GameScreen] preflight: Company slots count={slots.Count} " +
                 $"leader=slot{DeskSlotLayoutLogic.LeaderSlotIndex} " +
                 $"cells=[{string.Join(", ", slots)}]");
        if (slots.Count != DeskSlotLayoutLogic.CompanySlotCount)
        {
            GD.PushWarning("[GameScreen] Company slot count drifted from " +
                           $"{DeskSlotLayoutLogic.CompanySlotCount}.");
        }

        // Desk pawns: layer-3 occupants of the ENTITIES board. Verify each
        // pawn's desk-local cell pixel agrees between the floor board and the
        // entities board (the architecture invariant) and is on-screen.
        GD.Print($"[GameScreen] preflight: desk pawns are layer-3 occupants " +
                 $"of DeskEntitiesBoard (inside the UNCLIPPED entities " +
                 $"SubViewport) -- no screen-space reprojection, never sliced");
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
                     $"floorLocal=({floorLocal.X},{floorLocal.Y}) " +
                     $"boardsAgree={boardsAgree} onScreen={onScreen}");
            if (!boardsAgree)
            {
                GD.PushError($"[GameScreen] desk pawn {i} ({cell}) projects " +
                             "differently on the floor and entities boards — " +
                             "the two-board architecture invariant broke.");
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
                 $"splitU={clipMaterial.GetShaderParameter(ClipSplitUniform)} " +
                 $"-- floor clipped to two triangles, entities NOT clipped");
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 / System.Numerics --------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);

    private static SysVec2 ToSys(Vector2 v) => new(v.X, v.Y);
}
