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
///     <b>This decision is unchanged by the J3c-1bis / ter / quater fixes
///     below — there is still exactly ONE desk SubViewport; the two
///     visible desk corners are two windows onto that one iso world,
///     carved by the clip shader, not two viewports.</b></item>
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
/// <b>J3c-1ter — the desk is TWO mirrored corner triangles, the maquette
/// is a centred diamond (2026-05-21, Rune, after the in-game capture
/// clarified the shape).</b> The desk must read as two brown triangles,
/// one in the bottom-left screen corner and one in the bottom-right, in
/// vis-à-vis. Each desk triangle is bounded by a screen edge (left or
/// right), the screen bottom, and a <b>hypotenuse</b> = one lower edge of
/// the central maquette diamond. The maquette therefore reads as a centred
/// iso <b>diamond</b> whose lower point falls below the screen middle and
/// whose top apex is cropped by the top HUD band.
///
/// <para>
/// <b>J3c-1quinquies — re-lock the diamond to a strict iso 2:1 angle
/// (2026-05-21, Rune, regression fix on commit 5f9aeb5).</b> J3c-1quater
/// grew the maquette by driving the diamond's vertical extent from a free
/// screen-height fraction — which BROKE the iso angle: with the side apexes
/// at 0.30 and the lower point at 0.94 of a 1080-tall screen, a lower edge
/// ran at slope ≈ 0.72 instead of the iso 2:1 slope of 0.5, so the diamond
/// outline stopped being parallel to the iso grid drawn inside it. The fix
/// re-locks the diamond to a strict iso 2:1 rhombus — every edge slope is
/// exactly 0.5 (<c>DeskClipFrontierLogic.IsoEdgeSlope</c>) — and keeps the
/// maquette large by letting the diamond OVERFLOW the screen horizontally
/// (the side apexes fall off-screen, x &lt; 0 and x &gt; screenWidth). The
/// diamond is sized by ONE named knob —
/// <c>DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX</c> (default 0.95,
/// the half-width as a fraction of the screen width; &gt; 0.5 = sideways
/// overflow) — and placed vertically by <c>MaquetteLowerPointScreenFracY</c>
/// (default 0.92). The half-height is DERIVED from the locked iso slope,
/// never free. The maquette diamond owns the bulk of the screen; the desk
/// is two thin bottom corner wedges. Didier re-tunes the proportion by
/// editing those two constants alone. The desk camera framing
/// (<see cref="DeskCameraZoom"/>, <see cref="DeskFormationScreenFracX"/>,
/// <see cref="DeskFormationScreenFracY"/>) is re-balanced so the 7-pawn
/// Company still fits, fully visible, inside the now-thinner bottom-left
/// triangle.
/// </para>
///
/// <para>
/// A <see cref="SubViewport"/> texture is rectangular, so the desk
/// <see cref="SubViewport"/> is shown via a plain <see cref="TextureRect"/>
/// running <c>desk_triangle_clip.gdshader</c>, which <c>discard;</c>s every
/// fragment that is in <i>neither</i> corner triangle — that is the central
/// diamond, which shows the pannable maquette through behind. The shader
/// takes two hypotenuse half-planes plus one vertical split (the diamond's
/// lower point) so each unbounded half-plane is confined to its own corner.
/// The clip geometry is computed by <see cref="DeskClipFrontierLogic"/>
/// (Godot-free, xUnit-pinned) from the diamond apexes.
/// </para>
///
/// <para>
/// <b>The structural fix (carried from J3c-1bis) — the pawns ride
/// <i>inside</i> the desk SubViewport.</b> The desk pawns are plain
/// layer-3 occupants of the <c>DeskBoard</c> (<see cref="IsoBoard.AddOccupant"/>):
/// in the desk board's own local space, under the desk's own
/// <c>DeskCamera2D</c> — the <b>same coordinate frame as the floor they
/// stand on</b>. A pawn's position is simply
/// <c>DeskBoard.CellToPixel(slotCell)</c> and nothing else. The clip shader
/// runs on the desk <c>TextureRect</c>, but the immobile desk camera is
/// parked (and modestly zoomed out) so the whole formation frames well
/// inside the kept bottom-left triangle, far from the clip hypotenuse.
/// </para>
///
/// <list type="bullet">
///   <item><b>Full-screen desk rect.</b> The desk <see cref="SubViewport"/>
///     and the <c>DeskTextureRect</c> are both full screen so the two
///     corner triangles reach the real screen corners.</item>
///   <item><b>Floor AND grid both fill the whole viewport — homogeneous
///     brown.</b> The desk must read as uniform brown with iso grid
///     everywhere inside both kept triangles. The desk floor and the
///     <c>DeskBackground</c> panel are authored the SAME brown, so a
///     floor-coverage gap is invisible by construction; the board stripes
///     the iso grid over the <i>whole floor rect</i> (a visual superset of
///     the 8×8 logical grid — see <c>IsoBoard.DrawGridOverRect</c>).</item>
///   <item><b>NEAREST sampling.</b> The <c>DeskTextureRect</c> samples the
///     desk SubViewport texture with NEAREST filtering so the clip
///     hypotenuses are the shader's crisp discard lines.</item>
///   <item><b>Reliable screen size.</b> <see cref="_Ready"/> reads
///     <c>GetViewportRect().Size</c> — the real window rect, valid the
///     instant <c>_Ready</c> runs — never <c>DeskTextureRect.Size</c>.</item>
///   <item><b>Static-frontier scope (J3c-1).</b> The clip frontier and the
///     floor rect are computed once in <see cref="ConfigureDesk"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>[DESK-DIAG] instrumentation (kept and adapted to the J3c-1quater
/// geometry).</b> <see cref="ConfigureDesk"/> / <see cref="ApplyDeskClipFrontier"/>
/// / <see cref="Preflight"/> print a <c>[DESK-DIAG]</c> block: the desk
/// viewport / TextureRect sizes, the desk camera park AND zoom, the
/// maquette diamond apexes in screen pixels (with the two proportion
/// fractions that produced them), the two clip hypotenuses + the split
/// pushed to the shader, and the desk floor fill rect's four corners. Read
/// together with the board's own <c>[DESK-DIAG]</c> lines, the logs say
/// without ambiguity which element produces a coverage gap. Kept in the
/// code on purpose — a regressed fix is then diagnosed from the next F6
/// Output rather than another blind round-trip.
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The maquette
/// <see cref="SubViewportContainer"/>, the desk <see cref="SubViewport"/>,
/// and the desk <see cref="TextureRect"/> are all direct children of this
/// <see cref="Control"/> root — <b>never</b> under the <c>HudLayer</c>
/// <see cref="CanvasLayer"/>.
/// </para>
///
/// <para>
/// <b>Input routing (trap #9).</b> Pan tracking is wired in
/// <see cref="_Input"/>, not <see cref="Node._UnhandledInput"/>. The desk
/// <see cref="TextureRect"/> has <c>MouseFilter = Ignore</c>.
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
    /// <b>J3c-1quater desk-camera framing — knob A: the desk camera zoom.</b>
    ///
    /// <para>
    /// A Godot <see cref="Camera2D"/> <c>Zoom</c> below 1.0 zooms OUT — it
    /// shows more of the desk world, so the Company formation's footprint
    /// on screen is smaller. 0.85 = the formation reads at 85% of its
    /// native desk-iso size.
    /// </para>
    ///
    /// <para>
    /// <b>Why a slight zoom-out (J3c-1quater).</b> The desk board's iso tile
    /// is large (256 px); the 7-pawn Company block spans ~640 px of desk
    /// space. Once J3c-1quater shrank the desk to a thin bottom-left
    /// triangle, the full-scale block grazed the clip hypotenuse. A modest
    /// 0.85 zoom-out gives the whole formation a comfortable margin inside
    /// the triangle while the pawns stay clearly large and legible — the
    /// desk's foreground role is unchanged. Raise toward 1.0 for bigger
    /// pawns at the cost of margin; lower it for more margin.
    /// </para>
    /// </summary>
    private const float DeskCameraZoom = 0.85f;

    /// <summary>
    /// <b>J3c-1quater desk-camera framing — knob B/C: where the Company
    /// formation's centroid lands on screen, as a fraction of the screen.</b>
    ///
    /// <para>
    /// The Company pawns sit in the bottom-LEFT desk triangle. J3c-1quater
    /// made that triangle thinner (the maquette diamond grew), so the
    /// formation is parked lower and closer to the bottom-left corner than
    /// in J3c-1ter — <c>(0.20, 0.85)</c> instead of <c>(0.26, 0.78)</c> —
    /// so the whole 7-pawn block stays clear of the clip hypotenuse above
    /// it and the screen edges around it. Re-tune together with
    /// <see cref="DeskCameraZoom"/> if the formation changes.
    /// </para>
    /// </summary>
    private const float DeskFormationScreenFracX = 0.20f;
    private const float DeskFormationScreenFracY = 0.85f;

    /// <summary>
    /// Shader uniform names on <c>desk_triangle_clip.gdshader</c> — the
    /// J3c-1quater twin-corner clip. Held as constants so a rename surfaces
    /// at compile time on this one line rather than as a silent no-op clip
    /// at runtime.
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
    /// J3c-1 / J3c-1quater: stand up the static iso desk. Sizes the
    /// <c>DeskViewport</c>'s render target, wires its texture onto the
    /// <c>DeskTextureRect</c>, zooms and parks the immobile desk camera,
    /// fills the desk floor to the viewport, places one placeholder pawn
    /// per Company slot as a layer-3 occupant of the desk board, and
    /// computes the twin-corner triangular clip frontier the desk shader
    /// consumes.
    ///
    /// <para>
    /// All the arithmetic is pure-C# (Godot-free, xUnit-pinned); this
    /// method only converts to / from Godot types and applies results
    /// onto nodes. No pawn carries game state — J3c-1 is static.
    /// </para>
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
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
        // an MMB press over a desk corner still reaches the maquette pan.
        _deskTextureRect.MouseFilter = MouseFilterEnum.Ignore;

        // J3c-1bis: sample the desk SubViewport texture with NEAREST
        // filtering, not bilinear — the clip boundary is exactly the
        // shader's crisp discard line, no cross-edge blend.
        _deskTextureRect.TextureFilter = TextureFilterEnum.Nearest;

        // J3c-1quater: a modest camera zoom-out so the Company block frames
        // comfortably inside the now-thinner bottom-left desk triangle.
        // A Camera2D Zoom < 1 shows MORE world, so the desk-local visible
        // rect is `screen / zoom` — used below for the floor rect.
        _deskCamera.Zoom = new Vector2(DeskCameraZoom, DeskCameraZoom);
        var deskVisibleSize = screenSize / DeskCameraZoom;

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
        // the lower-left of the full-screen desk viewport — well inside the
        // bottom-left clip triangle, clear of the clip hypotenuse. A
        // Camera2D's Position is the world point shown at the screen
        // centre; with a zoom of `z` a screen offset `d` from centre maps
        // to a world offset `d / z`. The formation should appear at
        // `screen * frac`, i.e. `screen*frac - screen*0.5` screen-pixels
        // from centre, which is `(screen*frac - screen*0.5) / z` in desk
        // world space.
        var screenOffsetFromCentre = new Vector2(
            screen.X * DeskFormationScreenFracX - screen.X * 0.5f,
            screen.Y * DeskFormationScreenFracY - screen.Y * 0.5f);
        _deskCamera.Position =
            formationCentre - screenOffsetFromCentre / DeskCameraZoom;

        // J3c-1bis/quater: now the desk camera is parked, compute the
        // viewport-covering floor fill rect from the FINAL camera position.
        // With the zoom-out the desk world the camera shows is `screen/zoom`,
        // so the floor must cover that larger desk-local rect.
        // DeskFloorRectLogic (Godot-free, xUnit-pinned) computes the rect.
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(_deskCamera.Position),
            new SysVec2(deskVisibleSize.X, deskVisibleSize.Y));
        _deskBoard.SetDeskFloorFillRect(floorRect);

        // J3c-1bis: place one placeholder pawn per slot as a layer-3
        // occupant of the desk board. The pawn lives INSIDE the desk
        // SubViewport, in the desk board's own local space under the desk
        // camera — the SAME coordinate frame as the floor. Its position is
        // simply the desk board's CellToPixel of the slot cell; there is
        // no desk-local -> screen reprojection.
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
    /// J3c-1quater: compute the maquette/desk <b>twin-corner</b> triangular
    /// clip frontier and push it into the <c>DeskTextureRect</c>'s clip
    /// shader.
    ///
    /// <para>
    /// <b>The frontier follows the centred maquette diamond.</b> The
    /// maquette reads as a centred iso diamond whose left/right side apexes
    /// touch the screen edges and whose lower point reaches near the bottom
    /// HUD band — so the maquette owns the bulk of the screen. The desk is
    /// the two thin bottom corners that diamond does not cover; the two
    /// clip hypotenuses are the diamond's two LOWER edges, and a vertical
    /// split at the diamond's lower point bounds each corner triangle to
    /// its own side. The diamond's vertical extent is fraction-driven
    /// (<c>DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX</c> sizes the
    /// strict iso 2:1 diamond, which overflows the screen sideways;
    /// <c>MaquetteLowerPointScreenFracY</c> places it). All of it is derived in
    /// <see cref="DeskClipFrontierLogic"/> (Godot-free, xUnit-pinned); this
    /// method only feeds the screen size in and writes uniforms out.
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

        // The centred maquette diamond: side apexes on the screen edges,
        // lower point near the bottom HUD band, top apex cropped by the HUD.
        var diamond = DeskClipFrontierLogic.MaquetteDiamond(
            deskSize, HudBandHeight);

        // The two corner-triangle half-planes + the vertical split.
        var frontier = DeskClipFrontierLogic.Compute(
            diamond, deskOrigin, deskSize);

        var material = (ShaderMaterial)_deskTextureRect.Material;
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

        // [DESK-DIAG] J3c-1quater — the twin-corner clip in BOTH spaces. The
        // shader works in UV [0,1]; this prints the screen-pixel diamond
        // apexes (the maquette shape) AND the UV half-planes + split pushed,
        // plus the two proportion fractions that produced the diamond, so a
        // reader can see both kept corners without re-deriving signs.
        GD.Print($"[DESK-DIAG] proportion knobs: " +
                 $"MaquetteHalfWidthScreenFracX=" +
                 $"{DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX} " +
                 $"MaquetteLowerPointScreenFracY=" +
                 $"{DeskClipFrontierLogic.MaquetteLowerPointScreenFracY} " +
                 $"IsoEdgeSlope={DeskClipFrontierLogic.IsoEdgeSlope} " +
                 $"-- bigger half-width = bigger maquette (overflows sideways), " +
                 $"thinner desk corners; iso slope is LOCKED, not a knob");
        GD.Print($"[DESK-DIAG] maquette diamond (screen px): " +
                 $"topApex=({diamond.TopApex.X},{diamond.TopApex.Y}) " +
                 $"leftApex=({diamond.LeftApex.X},{diamond.LeftApex.Y}) " +
                 $"rightApex=({diamond.RightApex.X},{diamond.RightApex.Y}) " +
                 $"lowerPoint=({diamond.LowerPoint.X},{diamond.LowerPoint.Y}) " +
                 $"-- side apexes overflow off-screen, diamond owns the screen");
        GD.Print($"[DESK-DIAG] diamond edge slopes (must all be iso 0.5): " +
                 $"topLeft={DeskClipFrontierLogic.EdgeSlope(diamond.TopApex, diamond.LeftApex)} " +
                 $"topRight={DeskClipFrontierLogic.EdgeSlope(diamond.TopApex, diamond.RightApex)} " +
                 $"bottomLeft={DeskClipFrontierLogic.EdgeSlope(diamond.LeftApex, diamond.LowerPoint)} " +
                 $"bottomRight={DeskClipFrontierLogic.EdgeSlope(diamond.LowerPoint, diamond.RightApex)} " +
                 $"-- regression guard: any value != 0.5 means the iso angle broke");
        GD.Print($"[DESK-DIAG] twin clip (UV pushed to shader): " +
                 $"leftPoint=({frontier.LeftPointUv.X},{frontier.LeftPointUv.Y}) " +
                 $"leftNormal=({frontier.LeftNormalUv.X},{frontier.LeftNormalUv.Y}) " +
                 $"rightPoint=({frontier.RightPointUv.X},{frontier.RightPointUv.Y}) " +
                 $"rightNormal=({frontier.RightNormalUv.X},{frontier.RightNormalUv.Y}) " +
                 $"splitU={frontier.SplitU}");
        GD.Print($"[DESK-DIAG] twin clip: fragment KEPT when " +
                 $"(uv.x<=split AND leftSide>=0) OR (uv.x>=split AND " +
                 $"rightSide>=0) -- two thin bottom corners = desk, " +
                 $"centre diamond = maquette");
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

        // ------------------------------------------------------------------
        // [DESK-DIAG] J3c-1quater — instrumented desk diagnostic, adapted to
        // the grown-maquette / thin-corner-desk geometry. The next blocks
        // recense every element that draws into the desk zone, with its real
        // runtime rect / size / position so the Output says without
        // ambiguity what produces a gap or a clipped pawn.
        // ------------------------------------------------------------------
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // (0) The proportion knobs that decide maquette vs desk balance.
        GD.Print($"[DESK-DIAG] proportion knobs: " +
                 $"MaquetteHalfWidthScreenFracX=" +
                 $"{DeskClipFrontierLogic.MaquetteHalfWidthScreenFracX} " +
                 $"MaquetteLowerPointScreenFracY=" +
                 $"{DeskClipFrontierLogic.MaquetteLowerPointScreenFracY} " +
                 $"IsoEdgeSlope={DeskClipFrontierLogic.IsoEdgeSlope} " +
                 $"DeskCameraZoom={DeskCameraZoom} " +
                 $"formationFrac=({DeskFormationScreenFracX}," +
                 $"{DeskFormationScreenFracY})");

        // (1) DeskViewport + DeskTextureRect — the render target and the
        // screen-space surface the clip shader runs on.
        GD.Print($"[DESK-DIAG] DeskViewport: size={_deskViewport.Size} " +
                 $"transparent_bg={_deskViewport.TransparentBg} " +
                 $"renderTarget visibleRect={_deskViewport.GetVisibleRect().Size}");
        GD.Print($"[DESK-DIAG] DeskTextureRect: Size={_deskTextureRect.Size} " +
                 $"GlobalPosition={_deskTextureRect.GlobalPosition} " +
                 $"textureFilter={_deskTextureRect.TextureFilter} " +
                 $"mouseFilter={_deskTextureRect.MouseFilter} " +
                 $"hasMaterial={_deskTextureRect.Material is not null}");

        // (2) DeskCamera2D — the desk's immobile, zoomed-out camera. Its
        // Position is the world point shown at the viewport centre; with a
        // zoom of `z` the visible desk-local world rect is
        // Position +- (screen / z) / 2.
        var camPos = _deskCamera.Position;
        var deskVisibleSize = screen / DeskCameraZoom;
        var visTL = camPos - deskVisibleSize * 0.5f;
        var visBR = camPos + deskVisibleSize * 0.5f;
        GD.Print($"[DESK-DIAG] DeskCamera2D: Position={camPos} zoom={_deskCamera.Zoom} " +
                 $"=> visible desk-local world size=({deskVisibleSize.X}," +
                 $"{deskVisibleSize.Y}) rect TL=({visTL.X},{visTL.Y}) " +
                 $"BR=({visBR.X},{visBR.Y})");

        // (3) DeskBackground panel — the Panel BEHIND the DeskTextureRect.
        // Authored brown must equal IsoBoard.FloorFillColor or any uncovered
        // sliver shows a second colour. Print both so a drift is loud.
        var deskBg = GetNodeOrNull<Panel>("DeskBackground");
        GD.Print($"[DESK-DIAG] DeskBackground Panel: present={deskBg is not null} " +
                 $"Size={(deskBg?.Size.ToString() ?? "(none)")} " +
                 $"-- authored bg_color must equal IsoBoard.FloorFillColor=" +
                 $"({IsoBoard.FloorFillColor.R},{IsoBoard.FloorFillColor.G}," +
                 $"{IsoBoard.FloorFillColor.B},{IsoBoard.FloorFillColor.A})");

        // (4) The desk floor fill rect, computed off the FINAL parked desk
        // camera and the ZOOM-EXPANDED visible size — what the board fills.
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(camPos),
            new SysVec2(deskVisibleSize.X, deskVisibleSize.Y));
        GD.Print($"[DESK-DIAG] desk floor fill rect (DeskFloorRectLogic): " +
                 $"topLeft=({floorRect.TopLeft.X},{floorRect.TopLeft.Y}) " +
                 $"size=({floorRect.Size.X},{floorRect.Size.Y}) " +
                 $"bottomRight=({floorRect.TopLeft.X + floorRect.Size.X}," +
                 $"{floorRect.TopLeft.Y + floorRect.Size.Y}) " +
                 $"edgeSlack={DeskFloorRectLogic.EdgeSlackPx}px");

        // (5) The twin-corner clip frontier — the maquette diamond apexes and
        // the two UV half-planes + split pushed to the shader. The two desk
        // triangles are the thin bottom-left and bottom-right corners; the
        // big centre diamond is discarded so the maquette shows through.
        var screenSize = new SysVec2(screen.X, screen.Y);
        var diamond = DeskClipFrontierLogic.MaquetteDiamond(
            screenSize, HudBandHeight);
        var frontier = DeskClipFrontierLogic.Compute(
            diamond, SysVec2.Zero, screenSize);
        GD.Print($"[DESK-DIAG] maquette diamond (screen px): " +
                 $"topApex=({diamond.TopApex.X},{diamond.TopApex.Y}) " +
                 $"leftApex=({diamond.LeftApex.X},{diamond.LeftApex.Y}) " +
                 $"rightApex=({diamond.RightApex.X},{diamond.RightApex.Y}) " +
                 $"lowerPoint=({diamond.LowerPoint.X},{diamond.LowerPoint.Y})");
        GD.Print($"[DESK-DIAG] diamond edge slopes (must all be iso 0.5): " +
                 $"topLeft={DeskClipFrontierLogic.EdgeSlope(diamond.TopApex, diamond.LeftApex)} " +
                 $"topRight={DeskClipFrontierLogic.EdgeSlope(diamond.TopApex, diamond.RightApex)} " +
                 $"bottomLeft={DeskClipFrontierLogic.EdgeSlope(diamond.LeftApex, diamond.LowerPoint)} " +
                 $"bottomRight={DeskClipFrontierLogic.EdgeSlope(diamond.LowerPoint, diamond.RightApex)}");
        GD.Print($"[DESK-DIAG] twin clip (UV): " +
                 $"leftPoint=({frontier.LeftPointUv.X},{frontier.LeftPointUv.Y}) " +
                 $"leftNormal=({frontier.LeftNormalUv.X},{frontier.LeftNormalUv.Y}) " +
                 $"rightPoint=({frontier.RightPointUv.X},{frontier.RightPointUv.Y}) " +
                 $"rightNormal=({frontier.RightNormalUv.X},{frontier.RightNormalUv.Y}) " +
                 $"splitU={frontier.SplitU}");

        // The IsoBoard's own [DESK-DIAG] _Draw lines (grid cell count, grid
        // pixel bounds, GRID-COVERS-FLOOR verdict) print from IsoBoard.cs at
        // every desk-board _Draw pass. Read them together with the lines
        // above: floor rect vs grid bounds tells the coverage story.
        GD.Print($"[DESK-DIAG] >> see also IsoBoard '{_deskBoard.Name}' " +
                 $"[DESK-DIAG] _Draw lines for grid coverage vs floor rect.");

        // J3c-1 / quater desk diagnostics (the existing preflight block).
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
                 $"{_deskCamera.Position} zoom={DeskCameraZoom} " +
                 $"-- formation framed bottom-left " +
                 $"(frac {DeskFormationScreenFracX},{DeskFormationScreenFracY})");

        // J3c-1bis/quater: verify each pawn's desk-local cell pixel is inside
        // the zoom-expanded viewport rect the desk camera shows.
        GD.Print($"[GameScreen] preflight: desk pawns are layer-3 occupants " +
                 $"of DeskBoard (inside the desk SubViewport) -- no screen-space " +
                 $"reprojection (J3c-1bis fix), all in the bottom-left corner");
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var deskLocal = _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
            bool onScreen = deskLocal.X >= visTL.X
                          && deskLocal.X <= visBR.X
                          && deskLocal.Y >= visTL.Y
                          && deskLocal.Y <= visBR.Y;
            GD.Print($"[GameScreen] preflight:   desk pawn {i} cell={cell} " +
                     $"-> deskLocal=({deskLocal.X},{deskLocal.Y}) onScreen={onScreen}");
            if (!onScreen)
            {
                GD.PushWarning($"[GameScreen] desk pawn {i} ({cell}) is " +
                               "outside the desk viewport — it will not render.");
            }
        }

        GD.Print($"[GameScreen] preflight: desk floor fill rect topLeft=" +
                 $"({floorRect.TopLeft.X},{floorRect.TopLeft.Y}) " +
                 $"size=({floorRect.Size.X},{floorRect.Size.Y}) " +
                 $"-- viewport-covering, hard-edged DrawRect (J3c-1bis)");

        var clipMaterial = (ShaderMaterial)_deskTextureRect.Material!;
        GD.Print($"[GameScreen] preflight: desk twin clip " +
                 $"leftPoint={clipMaterial.GetShaderParameter(ClipLeftPointUniform)} " +
                 $"leftNormal={clipMaterial.GetShaderParameter(ClipLeftNormalUniform)} " +
                 $"rightPoint={clipMaterial.GetShaderParameter(ClipRightPointUniform)} " +
                 $"rightNormal={clipMaterial.GetShaderParameter(ClipRightNormalUniform)} " +
                 $"splitU={clipMaterial.GetShaderParameter(ClipSplitUniform)} " +
                 $"-- two thin corner triangles, NEAREST sampled (J3c-1quater)");
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 / System.Numerics --------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);

    private static SysVec2 ToSys(Vector2 v) => new(v.X, v.Y);
}
