using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Scenes.Ui;
using Wayfinders.Client.Scripts.Screens;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// Root of the Game Screen Shell — the cartographer's-desk diorama in
/// which eM and eT are shown (roadmap <c>wayfinders-mvp-roadmap-eM-eT</c>).
/// As of J3c-1 this slice stands up three layers of the diorama: the
/// <b>clipped, pannable map maquette</b> (J3a), the <b>fixed top/bottom
/// HUD frame</b> (J3b), and the <b>static iso desk</b> (J3c-1). The desk
/// interactivity (pawn selection / placement, J3c-2) is a later milestone
/// and is not present here.
///
/// <para>
/// <b>What J3a delivers.</b>
/// <list type="number">
///   <item>A <see cref="SubViewportContainer"/> hosting a
///     <see cref="SubViewport"/> — the maquette is rendered into an
///     isolated render world and is clipped to the container's rect for
///     free.</item>
///   <item>A <see cref="Camera2D"/> <i>inside</i> the SubViewport. The
///     pan moves this camera — never the content.</item>
///   <item>An instance of <c>IsoBoard.tscn</c> (the J3-iso socle) as the
///     maquette content.</item>
///   <item>Mouse-drag panning, middle-button, clamped to the maquette
///     content extent.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What J3b adds — the fixed HUD frame.</b> A <see cref="CanvasLayer"/>
/// (<c>HudLayer</c>, high <c>layer</c> index) carries two full-width
/// <see cref="Panel"/> bands anchored to the top and bottom of the
/// screen. A <see cref="CanvasLayer"/> has its own canvas transform that
/// is <i>not</i> affected by any 2D camera — the HUD stays welded to the
/// screen no matter how the maquette pans inside its SubViewport. The two
/// bands are <see cref="HudBandHeight"/> = 32 px; the residual map rect is
/// the screen minus both bands.
/// </para>
///
/// <para>
/// <b>What J3c-1 adds — the static iso desk.</b> A <i>second</i>
/// <see cref="SubViewport"/> (<c>DeskViewport</c>), its own
/// <see cref="Camera2D"/> (<c>DeskCamera2D</c>), and its own
/// <c>IsoBoard.tscn</c> instance (<c>DeskBoard</c>). It is a genuine
/// <b>second iso space</b> at a larger iso scale than the maquette — the
/// Company pawns read big, foreground.
/// <list type="bullet">
///   <item><b>Two SubViewports, not one</b> (roadmap §"Choix techniques
///     Godot"). Two iso scales + two camera behaviours (the maquette
///     pans, the desk is fixed) + two clippings = two render worlds.
///     <b>This decision is unchanged by the J3c-1bis fixes below.</b></item>
///   <item><b>The desk camera is immobile.</b> <c>DeskCamera2D</c> is
///     parked once in <see cref="ConfigureDesk"/> so the Company formation
///     frames inside the bottom-left desk triangle, and never moved.</item>
///   <item><b>Larger iso scale.</b> The desk board's <c>TileWidthPx</c>
///     is authored larger than the maquette's, so the desk cells — and
///     the pawns on them — read big in the foreground.</item>
///   <item><b>Static.</b> J3c-1 lays out the ~7 Company slots
///     (<see cref="DeskSlotLayoutLogic"/>, Godot-free, xUnit-pinned) and
///     places one placeholder <see cref="DeskCompanyPawn"/> on each.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>J3c-1bis — the desk is a triangle, and the pawns ride inside it
/// (2026-05-21, Rune, after a 4th-5th F6 round-trip).</b> The desk must
/// read as a bottom-left <b>triangle</b>: it fills the wedge the central
/// maquette diamond does not cover. A <see cref="SubViewport"/> texture is
/// rectangular, so the desk <see cref="SubViewport"/> is shown via a plain
/// <see cref="TextureRect"/> running <c>desk_triangle_clip.gdshader</c>,
/// which <c>discard;</c>s every fragment on the maquette side of a diagonal
/// frontier.
///
/// <para>
/// <b>The bug the earlier J3c-1bis attempts kept re-breaking.</b> The
/// triangular clip runs on the <i>whole</i> <c>DeskViewport</c> texture —
/// floor <i>and</i> anything else rendered into that viewport. An earlier
/// fix concluded the pawns therefore had to live <i>outside</i> the desk
/// render world, on a separate screen-space <c>Node2D</c>, so the shader
/// would not slice their bodies. But moving the pawns out forced a
/// hand-written <i>desk-local → screen reprojection</i> (camera centre,
/// viewport half-size, layer origin). That reprojection is arithmetic that
/// composes the desk camera transform by hand, and it broke on every
/// iteration — pawns scattered across the maquette, off by a half-tile, an
/// anchor, a stale <c>Size</c>.
/// </para>
///
/// <para>
/// <b>The structural fix — the pawns ride <i>inside</i> the desk
/// SubViewport.</b> The desk pawns are now plain layer-3 occupants of the
/// <c>DeskBoard</c> (<see cref="IsoBoard.AddOccupant"/>): in the desk
/// board's own local space, under the desk's own <c>DeskCamera2D</c> — the
/// <b>same coordinate frame as the floor they stand on</b>. A pawn's
/// position is simply <c>DeskBoard.CellToPixel(slotCell)</c> and nothing
/// else. There is no reprojection left to break — the floor and the pawns
/// are one render world, one camera, one projection. The clip shader still
/// runs on the desk <c>TextureRect</c>, but the immobile desk camera is
/// parked so the whole formation frames well <i>inside</i> the kept
/// bottom-left triangle, far from the clip diagonal — so no pawn body is
/// near the cut and none is sliced. (If a later milestone places a pawn
/// hard against the clip diagonal, the answer is to widen the desk
/// triangle, not to re-introduce the reprojection.)
/// </para>
///
/// <para>
/// <b>What this changes structurally — acted consciously.</b> The previous
/// attempt's separate screen-space <c>DeskEntities</c> <see cref="Node2D"/>
/// is <b>removed</b>: it was a render node, not a render world, and it is
/// no longer needed. <c>DeskEntityLayerLogic</c> and its tests are removed
/// with it. <b>The two locked SubViewports are untouched</b> — there is
/// one maquette render world and one desk render world, exactly as before;
/// the change is that the desk pawns moved back <i>into</i> the desk one
/// instead of riding a third, screen-space node. Net node count drops by
/// one. The two-iso-scales decision is intact (two boards, two
/// <c>TileWidthPx</c>).
/// </para>
///
/// <list type="bullet">
///   <item><b>Full-screen desk rect — corner-to-corner triangle.</b> The
///     desk <see cref="SubViewport"/> and the <c>DeskTextureRect</c> are
///     both full screen. The shader clips a triangle inside its container
///     rect; a small container would bound the triangle with hard
///     horizontal / vertical edges (the "truncated triangle" bug).</item>
///   <item><b>Floor always fills the whole viewport — homogeneous brown
///     triangle (F6 fix, 2026-05-21, second round).</b> The desk must read
///     as ONE uniform brown triangle: every pixel of the clipped viewport
///     is floor, iso grid everywhere, no separate dark "viewport background"
///     band. The board fills an axis-aligned rect covering the whole
///     viewport — <i>never</i> the compact grid-bounding iso diamond, whose
///     rhombus corners left the viewport corners unpainted and let the dark
///     <c>DeskBackground</c> bleed through as a parasitic band. This screen
///     hands the board an explicit camera-derived rect via
///     <see cref="DeskFloorRectLogic"/> + <c>IsoBoard.SetDeskFloorFillRect</c>;
///     if that push were ever skipped the board still fills its own
///     viewport rect (<c>IsoBoard.ViewportFloorFillRect</c>) — the
///     homogeneous-floor guarantee does not depend on the wire. The board
///     fills it with a hard-edged <c>DrawRect</c>, not a feathered
///     <c>DrawColoredPolygon</c> — see the next item.</item>
///   <item><b>Uniform floor tint — the F6 "off-colour top-left point" fix
///     (2026-05-21).</b> The kept clip triangle showed a darker, more
///     saturated wedge near its top-left point. Two compounding causes,
///     both removed. (1) The board filled the floor with
///     <c>DrawColoredPolygon</c>, whose triangulator feathers the
///     rasterised edge; over the desk <c>transparent_bg</c> that feather
///     left a half-alpha rim and the dark <c>DeskBackground</c> showed
///     through — the board now fills with a hard-edged <c>DrawRect</c>, and
///     the <c>DeskBackground</c> panel is authored the SAME brown as
///     <c>IsoBoard.FloorFillColor</c> so a sub-pixel edge can never expose
///     a second colour.
///     (2) The clip frontier was derived from the maquette diamond's
///     <i>projected</i> edge, which depends on the maquette camera park;
///     a sub-pixel mismatch put a thin sliver of the desk's own top-left
///     corner on the <i>maquette</i> side of the frontier, so the shader
///     <c>discard;</c>ed it and the maquette terrain (a different brown)
///     showed through as the off-tint point. For the <i>static</i> J3c-1
///     desk the frontier is now the desk viewport's own fixed main
///     diagonal (top-left to bottom-right corner) — a geometric constant,
///     not a moving projection — so the kept triangle is exactly the
///     bottom-left half of the viewport and its top-left point is real
///     desk floor. See <see cref="ApplyDeskClipFrontier"/>.</item>
///   <item><b>NEAREST sampling.</b> The <c>DeskTextureRect</c> samples the
///     desk SubViewport texture with NEAREST filtering so the clip diagonal
///     is the shader's crisp discard line, no bilinear cross-edge blend.</item>
///   <item><b>Reliable screen size.</b> <see cref="_Ready"/> reads
///     <c>GetViewportRect().Size</c> — the real window rect, valid the
///     instant <c>_Ready</c> runs — never <c>DeskTextureRect.Size</c>,
///     which is the unresolved design-time default that early.</item>
///   <item><b>Static-frontier scope (J3c-1).</b> The clip frontier and the
///     floor rect are computed once in <see cref="ConfigureDesk"/>. The
///     desk does not pan (only the maquette does); the frontier is a fixed
///     viewport diagonal, so a maquette pan does not disturb it — the
///     static desk is correct by construction.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>State ownership.</b> Five pure-C# helpers, each Godot-free and
/// xUnit-pinned, carry the only arithmetic in this shell. (1) The pan
/// decision is <see cref="MapViewportPanLogic"/> (J3a). (2) The residual
/// central rectangle is <see cref="HudLayoutLogic"/> (J3b). (3) The
/// Company slot layout is <see cref="DeskSlotLayoutLogic"/> (J3c-1).
/// (4) The maquette/desk clip frontier is <see cref="DeskClipFrontierLogic"/>
/// (J3c-1bis). (5) The desk floor fill rect is
/// <see cref="DeskFloorRectLogic"/> (J3c-1bis). This node is the engine
/// seam only: it converts <see cref="InputEvent"/>s and scene geometry
/// into helper calls and applies the helper results onto Godot nodes.
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The maquette
/// <see cref="SubViewportContainer"/>, the desk <see cref="SubViewport"/>,
/// and the desk <see cref="TextureRect"/> are all direct children of this
/// <see cref="Control"/> root — <b>never</b> under the <c>HudLayer</c>
/// <see cref="CanvasLayer"/>. The <c>HudLayer</c> is the <i>only</i>
/// <see cref="CanvasLayer"/> in this scene and it carries <i>only</i> the
/// HUD. The desk pawns are occupants of the desk board, inside the desk
/// SubViewport — not a separate node under this root.
/// </para>
///
/// <para>
/// <b>Input routing (trap #9).</b> Pan tracking is wired in
/// <see cref="_Input"/>, not <see cref="Node._UnhandledInput"/>. The desk
/// <see cref="TextureRect"/> has <c>MouseFilter = Ignore</c>: the static
/// J3c-1 desk takes no input, so an MMB press over the desk corner still
/// reaches the maquette pan. J3c-2 will add pawn selection.
/// </para>
///
/// <para>
/// <b>Preflight (trap #2).</b> <see cref="_Ready"/> prints a fixed
/// diagnostics block — viewport size, content extent, clamp bounds,
/// camera start, the resolved pan button, the HUD band heights plus the
/// residual map rect, and (J3c-1 / bis) the desk viewport rect, the
/// resolved Company slot cells, the desk camera park, the clip frontier,
/// and the desk floor fill rect.
/// </para>
/// </summary>
public partial class GameScreen : Control
{
    /// <summary>
    /// Autoload-injected opening flow must not draw its CanvasLayer over
    /// this scene when launched standalone for the J3a/J3b/J3c smoke
    /// (trap #1bis). The <c>.tscn</c> root carries
    /// <c>metadata/skip_opening_bootstrap</c>; this constant documents
    /// the contract for readers of the code.
    /// </summary>
    private const string SkipBootstrapMeta = "skip_opening_bootstrap";

    /// <summary>
    /// Fixed height of each HUD band, pixels — the same value for the top
    /// and the bottom band. <b>The single named source of truth</b> for
    /// the band height: it must match the <c>HudTop</c> panel's
    /// <c>offset_bottom</c> and the <c>HudBottom</c> panel's
    /// <c>-offset_top</c> in <c>GameScreen.tscn</c>.
    /// </summary>
    private const float HudBandHeight = 32f;

    /// <summary>
    /// Where the centroid of the Company formation should land on screen,
    /// as a fraction of the desk viewport — used to park the immobile
    /// <c>DeskCamera2D</c> (J3c-1bis). The desk is the bottom-left
    /// triangle, so the formation is framed in the lower-left quadrant.
    /// Both fractions are well inside the kept triangle, so the whole
    /// formation sits clear of the clip diagonal — no pawn is sliced.
    /// </summary>
    private const float DeskFormationScreenFracX = 0.26f;
    private const float DeskFormationScreenFracY = 0.72f;

    /// <summary>
    /// Shader uniform names on <c>desk_triangle_clip.gdshader</c>. Held as
    /// constants so a rename surfaces at compile time on this one line
    /// rather than as a silent no-op clip at runtime.
    /// </summary>
    private const string ClipPointUniform = "clip_line_point";
    private const string ClipNormalUniform = "clip_line_normal";

    private SubViewportContainer _mapContainer = null!;
    private SubViewport _mapViewport = null!;
    private Camera2D _mapCamera = null!;
    private IsoBoard _maquette = null!;
    private CanvasLayer _hudLayer = null!;

    private SubViewport _deskViewport = null!;
    private Camera2D _deskCamera = null!;
    private IsoBoard _deskBoard = null!;
    private TextureRect _deskTextureRect = null!;

    private readonly MapViewportPanLogic _pan = new();

    public override void _Ready()
    {
        _mapContainer = GetNode<SubViewportContainer>("MapViewportContainer");
        _mapViewport = GetNode<SubViewport>("MapViewportContainer/MapViewport");
        _mapCamera = GetNode<Camera2D>("MapViewportContainer/MapViewport/MapCamera2D");
        _maquette = GetNode<IsoBoard>("MapViewportContainer/MapViewport/Maquette");
        _hudLayer = GetNode<CanvasLayer>("HudLayer");

        _deskViewport = GetNode<SubViewport>("DeskViewport");
        _deskCamera = GetNode<Camera2D>("DeskViewport/DeskCamera2D");
        _deskBoard = GetNode<IsoBoard>("DeskViewport/DeskBoard");
        _deskTextureRect = GetNode<TextureRect>("DeskTextureRect");

        // J3b: size the map viewport to the residual central rectangle —
        // the screen minus the two fixed HUD bands.
        //
        // J3c-1bis F6 fix: the screen size is read from GetViewportRect,
        // NOT from this Control's Size. At _Ready() a Control authored with
        // anchors_preset 15 has NOT had its layout resolved yet — Size is
        // still the design-time default. GetViewportRect().Size is the real
        // window rect and is valid the instant _Ready runs.
        var screen = GetViewportRect().Size;
        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
        ApplyResidualRect(residual);

        // The SubViewport's render size follows the (now residual)
        // container rect, so the maquette is clipped exactly to the
        // central panel between the HUD bands.
        _mapViewport.Size = (Vector2I)_mapContainer.Size;

        // Park the camera centred on the maquette content, then clamp so
        // an authored-out-of-bounds start is corrected before the first
        // frame.
        var content = ContentSize();
        var viewport = ViewportSize();
        var start = new Vector2(content.X * 0.5f, content.Y * 0.5f);
        var clampedStart = MapViewportPanLogic.ClampToContent(
            ToPan(start), ToPan(content), ToPan(viewport));
        _mapCamera.Position = ToGodot(clampedStart);

        ConfigureDesk(screen);

        Preflight(content, viewport, clampedStart, screen);
    }

    /// <summary>
    /// Input seam (trap #9): routes the middle-button drag-pan into
    /// <see cref="MapViewportPanLogic"/> and applies the clamped result
    /// onto the SubViewport's <see cref="Camera2D"/>.
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
            // Deliberately NOT SetInputAsHandled on motion (trap #9): the
            // pan owns its response, not the motion event itself.
        }
    }

    public override void _ExitTree()
    {
        // Cut any live drag cleanly so no ambiguous state survives a
        // scene change — same reset discipline as MapPanInputLogic.
        _pan.Reset();
    }

    /// <summary>
    /// Apply a <see cref="HudRect"/> (from <see cref="HudLayoutLogic"/>)
    /// onto the <see cref="SubViewportContainer"/>'s offsets.
    /// </summary>
    private void ApplyResidualRect(HudRect rect)
    {
        _mapContainer.OffsetLeft = rect.X;
        _mapContainer.OffsetTop = rect.Y;
        _mapContainer.OffsetRight = rect.X + rect.Width;
        _mapContainer.OffsetBottom = rect.Y + rect.Height;
    }

    /// <summary>
    /// J3c-1 / J3c-1bis: stand up the static iso desk. Sizes the
    /// <c>DeskViewport</c>'s render target, wires its texture onto the
    /// <c>DeskTextureRect</c>, parks the immobile desk camera, fills the
    /// desk floor to the viewport, places one placeholder pawn per Company
    /// slot as a layer-3 occupant of the desk board, and computes the
    /// triangular clip frontier the desk shader consumes.
    ///
    /// <para>
    /// <b>The pawns ride inside the desk SubViewport (J3c-1bis, Rune).</b>
    /// The pawns are <see cref="IsoBoard.AddOccupant"/> children of the
    /// desk board, in the desk board's own local space under the desk
    /// camera — the same coordinate frame as the floor. Each pawn's
    /// position is <c>DeskBoard.CellToPixel(slotCell)</c>: no reprojection,
    /// nothing to drift. The immobile desk camera frames the formation well
    /// inside the kept clip triangle, so the clip shader does not touch a
    /// pawn body.
    /// </para>
    ///
    /// <para>
    /// All the arithmetic is pure-C# (Godot-free, xUnit-pinned); this
    /// method only converts to / from Godot types and applies results
    /// onto nodes. No pawn carries game state — J3c-1 is static.
    /// </para>
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
    /// Both the desk viewport and the full-screen <c>DeskTextureRect</c>
    /// cover exactly this rect.
    /// </param>
    private void ConfigureDesk(Vector2 screen)
    {
        // J3c-1bis: the desk viewport and the DeskTextureRect are both
        // full-screen. Drive the viewport render size and force the
        // DeskTextureRect's Size off the reliable `screen` value so the
        // shader's UV space matches the desk viewport 1:1.
        var screenSize = new SysVec2(screen.X, screen.Y);
        _deskViewport.Size = (Vector2I)screen;
        _deskTextureRect.Size = screen;

        // Show the desk SubViewport's render target through the plain
        // TextureRect. The TextureRect carries the clip shader; the locked
        // "two SubViewports" decision is intact.
        _deskTextureRect.Texture = _deskViewport.GetTexture();

        // The static desk takes no input in J3c-1. An Ignore filter means
        // an MMB press over the desk corner still reaches the maquette pan.
        _deskTextureRect.MouseFilter = MouseFilterEnum.Ignore;

        // J3c-1bis: sample the desk SubViewport texture with NEAREST
        // filtering, not bilinear. transparent_bg + a bilinear tap along
        // the shader's discard diagonal blends a floor texel against a
        // transparent texel one pixel over, leaving a half-alpha fringe
        // that reads as the desk "bleeding" onto the maquette. NEAREST
        // takes one texel, no cross-edge blend — the clip boundary is
        // exactly the shader's crisp discard line.
        _deskTextureRect.TextureFilter = TextureFilterEnum.Nearest;

        // Resolve the Company formation cells (Godot-free, xUnit-pinned).
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // Sum the desk-local pixel of every slot cell so the desk camera
        // can be parked on the true pixel centroid of the formation before
        // anything depends on the parked position.
        var formationCentre = Vector2.Zero;
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            formationCentre += _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
        }
        formationCentre /= slots.Count;

        // Park the immobile desk camera so the formation centroid lands in
        // the lower-left quadrant of the full-screen desk viewport — well
        // inside the bottom-left clip triangle, clear of the clip diagonal.
        // A Camera2D's Position is the world point shown at the screen
        // centre. Computed off `screen` (the reliable window rect).
        var screenTarget = new Vector2(
            screen.X * DeskFormationScreenFracX,
            screen.Y * DeskFormationScreenFracY);
        _deskCamera.Position =
            formationCentre - (screenTarget - screen * 0.5f);

        // J3c-1bis: now the desk camera is parked, compute the
        // viewport-covering floor fill rect from the FINAL camera position.
        // DeskFloorRectLogic (Godot-free, xUnit-pinned) computes the rect.
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(_deskCamera.Position), screenSize);
        _deskBoard.SetDeskFloorFillRect(floorRect);

        // J3c-1bis: place one placeholder pawn per slot as a layer-3
        // occupant of the desk board. The pawn lives INSIDE the desk
        // SubViewport, in the desk board's own local space under the desk
        // camera — the SAME coordinate frame as the floor. Its position is
        // simply the desk board's CellToPixel of the slot cell; there is
        // no desk-local -> screen reprojection (the fragile step the
        // earlier J3c-1bis attempts kept re-breaking). IsoBoard.AddOccupant
        // parents the pawn under a Y-sorted Occupants node, so a pawn lower
        // on screen draws over one higher up — the leader (largest col+row)
        // projects lowest and draws foreground, exactly the mockup.
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var pixel = _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
            var pawn = new DeskCompanyPawn
            {
                Name = $"DeskPawn{i}",
                Position = pixel,
            };
            pawn.Configure(
                slotIndex: i,
                isLeader: DeskSlotLayoutLogic.IsLeaderSlot(i));
            _deskBoard.AddOccupant(pawn);
        }

        ApplyDeskClipFrontier(screen);
    }

    /// <summary>
    /// J3c-1bis: compute the maquette/desk triangular-clip frontier and
    /// push it into the <c>DeskTextureRect</c>'s clip shader.
    ///
    /// <para>
    /// <b>The frontier is the desk viewport's fixed main diagonal.</b> For
    /// the <i>static</i> J3c-1 desk the maquette/desk frontier is the desk
    /// viewport's own main diagonal — from the top-left corner to the
    /// bottom-right corner — a geometric constant of the full-screen desk
    /// rect. An earlier attempt derived the frontier from the maquette
    /// diamond's <i>projected</i> lower-left edge; because that projection
    /// depends on the maquette camera park, a sub-pixel mismatch put a thin
    /// sliver of the desk's own top-left corner on the maquette side of the
    /// frontier — the shader <c>discard;</c>ed it and the maquette terrain
    /// showed through as the F6 "off-tint top-left point". The fixed
    /// diagonal makes the kept triangle exactly the bottom-left half of the
    /// viewport, with a real desk-floor top-left point. This is the same
    /// diagonal <c>DeskClipFrontierLogicTests</c> already pins. When the
    /// maquette/desk frontier must follow a pannable maquette (a later
    /// milestone) the projected-edge derivation returns, recomputed in
    /// <see cref="_Input"/> — <see cref="DeskClipFrontierLogic"/> is
    /// already pure and rect-agnostic.
    /// </para>
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
    /// </param>
    private void ApplyDeskClipFrontier(Vector2 screen)
    {
        // The desk TextureRect is full-screen at the screen origin. Use the
        // reliable `screen` size — at _Ready() DeskTextureRect.Size is not
        // yet resolved and would be non-positive.
        var deskOrigin = SysVec2.Zero;
        var deskSize = new SysVec2(screen.X, screen.Y);

        // The static frontier: the desk viewport's main diagonal, top-left
        // corner to bottom-right corner. DeskClipFrontierLogic orients the
        // normal so the bottom-left desk triangle is the keep side.
        var edgeStart = deskOrigin;            // top-left corner
        var edgeEnd = deskOrigin + deskSize;   // bottom-right corner

        var frontier = DeskClipFrontierLogic.Compute(
            edgeStart, edgeEnd, deskOrigin, deskSize);

        var material = (ShaderMaterial)_deskTextureRect.Material;
        material.SetShaderParameter(
            ClipPointUniform, new Vector2(frontier.PointUv.X, frontier.PointUv.Y));
        material.SetShaderParameter(
            ClipNormalUniform, new Vector2(frontier.NormalUv.X, frontier.NormalUv.Y));
    }

    /// <summary>
    /// The pannable maquette content extent in world pixels. Prefers the
    /// real <see cref="IsoBoard"/> floor bitmap size; falls back to the
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

        // J3c-1 / bis desk diagnostics. The desk is sized off `screen`
        // (GetViewportRect, reliable at _Ready).
        var slots = DeskSlotLayoutLogic.CompanySlotCells();
        GD.Print($"[GameScreen] preflight: screen={screen} (GetViewportRect) " +
                 $"DeskViewport size={_deskViewport.Size} " +
                 $"DeskTextureRect rect={_deskTextureRect.Size} " +
                 $"mouseFilter={_deskTextureRect.MouseFilter} " +
                 $"textureFilter={_deskTextureRect.TextureFilter} (J3c-1 static, full-screen)");
        GD.Print($"[GameScreen] preflight: desk iso tile={_deskBoard.TileWidthPx}px " +
                 $"(maquette tile={_maquette.TileWidthPx}px) " +
                 $"-- two iso scales, distinct SubViewports");
        GD.Print($"[GameScreen] preflight: Company slots count={slots.Count} " +
                 $"leader=slot{DeskSlotLayoutLogic.LeaderSlotIndex} " +
                 $"cells=[{string.Join(", ", slots)}]");
        if (slots.Count != DeskSlotLayoutLogic.CompanySlotCount)
        {
            GD.PushWarning("[GameScreen] Company slot count drifted from " +
                           $"{DeskSlotLayoutLogic.CompanySlotCount}.");
        }
        GD.Print($"[GameScreen] preflight: DeskCamera2D parked at " +
                 $"{_deskCamera.Position} -- formation framed bottom-left " +
                 $"(frac {DeskFormationScreenFracX},{DeskFormationScreenFracY})");

        // J3c-1bis: the desk pawns are layer-3 occupants of the desk board,
        // inside the desk SubViewport, in the desk board's own space. Print
        // each pawn's desk-local cell pixel and verify it is inside the
        // viewport rect the desk camera shows — a pawn outside that rect
        // would not render. No reprojection: the pixel IS the placement.
        var cameraCentre = _deskCamera.Position;
        var visibleTopLeft = cameraCentre - screen * 0.5f;
        var visibleBottomRight = cameraCentre + screen * 0.5f;
        GD.Print($"[GameScreen] preflight: desk pawns are layer-3 occupants " +
                 $"of DeskBoard (inside the desk SubViewport) -- no screen-space " +
                 $"reprojection (J3c-1bis fix)");
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var deskLocal = _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
            bool onScreen = deskLocal.X >= visibleTopLeft.X
                          && deskLocal.X <= visibleBottomRight.X
                          && deskLocal.Y >= visibleTopLeft.Y
                          && deskLocal.Y <= visibleBottomRight.Y;
            GD.Print($"[GameScreen] preflight:   desk pawn {i} cell={cell} " +
                     $"-> deskLocal=({deskLocal.X},{deskLocal.Y}) onScreen={onScreen}");
            if (!onScreen)
            {
                GD.PushWarning($"[GameScreen] desk pawn {i} ({cell}) is " +
                               "outside the desk viewport — it will not render.");
            }
        }

        // J3c-1bis: the desk floor fill rect, computed off the FINAL parked
        // desk camera and the reliable `screen` size.
        var screenSize = new SysVec2(screen.X, screen.Y);
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(_deskCamera.Position), screenSize);
        GD.Print($"[GameScreen] preflight: desk floor fill rect topLeft=" +
                 $"({floorRect.TopLeft.X},{floorRect.TopLeft.Y}) " +
                 $"size=({floorRect.Size.X},{floorRect.Size.Y}) " +
                 $"-- viewport-covering, hard-edged DrawRect (J3c-1bis)");

        var clipMaterial = (ShaderMaterial)_deskTextureRect.Material;
        GD.Print($"[GameScreen] preflight: desk clip frontier " +
                 $"point={clipMaterial.GetShaderParameter(ClipPointUniform)} " +
                 $"normal={clipMaterial.GetShaderParameter(ClipNormalUniform)} " +
                 $"-- fixed viewport diagonal, NEAREST sampled (J3c-1bis)");
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 / System.Numerics --------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);

    private static SysVec2 ToSys(Vector2 v) => new(v.X, v.Y);
}
