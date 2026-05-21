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
///     free. No <c>clip_contents</c> on a plain <c>Control</c>: that
///     would clip the draw but give neither an isolated camera nor a
///     private coordinate space (roadmap §"Choix techniques Godot").</item>
///   <item>A <see cref="Camera2D"/> <i>inside</i> the SubViewport. The
///     pan moves this camera — never the content. The maquette keeps its
///     absolute world coordinates, so the iso projection of J3-iso stays
///     valid with no recompute.</item>
///   <item>An instance of <c>IsoBoard.tscn</c> (the J3-iso socle) as the
///     maquette content. Until Mira's district bitmap lands the board
///     runs on its placeholder rectangle — a tolerated state.</item>
///   <item>Mouse-drag panning, middle-button, clamped to the maquette
///     content extent so the player cannot scroll into the void.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What J3b adds — the fixed HUD frame.</b> A <see cref="CanvasLayer"/>
/// (<c>HudLayer</c>, high <c>layer</c> index) carries two full-width
/// <see cref="Panel"/> bands anchored to the top and bottom of the
/// screen. This is the <b>first legitimate <see cref="CanvasLayer"/></b>
/// of the shell. A <see cref="CanvasLayer"/> has its own canvas transform
/// that is <i>not</i> affected by any 2D camera — so the HUD stays welded
/// to the screen no matter how the maquette pans inside its SubViewport.
/// That is exactly the behaviour we want for a permanent overlay, and it
/// is free: no per-frame work, no maths on the bands themselves.
/// <list type="bullet">
///   <item><b>Clipped behind the HUD.</b> Two independent guarantees that
///     the maquette never bleeds over the HUD. (1) <c>HudLayer.layer</c>
///     is higher than the default-layer maquette, so the HUD draws last.
///     (2) <see cref="_Ready"/> re-sizes the
///     <see cref="SubViewportContainer"/> to the <i>residual central
///     rectangle</i> — the screen minus both bands — so the maquette is
///     physically clipped to the gap between the bands. Belt and braces.</item>
///   <item><b>Slim 32 px bands (2026-05-21, Didier).</b> The two bands
///     were re-authored from 90 px to <see cref="HudBandHeight"/> = 32 px,
///     so the residual map rect grows to <c>[0,32 1920x1016]</c>. The
///     single source of truth for that height is <see cref="HudBandHeight"/>
///     — both the top and the bottom band read it, and the
///     <c>GameScreen.tscn</c> panel offsets are authored to the same value.</item>
///   <item><b>Placeholder, swap-ready.</b> The bands are
///     <see cref="Panel"/>s with a flat parchment <c>StyleBoxFlat</c>.
///     Mira's real parchment liseré (J3-assets) swaps in by replacing the
///     <c>theme_override_styles/panel</c> with a <see cref="TextureRect"/>
///     child — the band node names and rects do not change, the J7 / HUD
///     content milestones key off the same <c>HudTop</c> / <c>HudBottom</c>
///     nodes. The two test <see cref="Label"/>s are placeholders for that
///     future content (district name in J7, etc.).</item>
/// </list>
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
///     pans, the desk is fixed) + two clippings = two render worlds. A
///     single SubViewport would force hand-scaling nodes and juggling two
///     iso origins. Two concrete nodes, no <c>IsoSpace</c> abstraction —
///     the anti-over-architecture line of the roadmap. <b>This decision is
///     unchanged by the J3c-1bis triangular-clip fix below</b> — see that
///     paragraph.</item>
///   <item><b>The desk camera is immobile.</b> <c>DeskCamera2D</c> is
///     parked once in <see cref="ConfigureDesk"/> so the Company formation
///     frames inside the bottom-left desk triangle, and never moved —
///     only the maquette pans (roadmap J3c-1).</item>
///   <item><b>Larger iso scale.</b> The desk board's <c>TileWidthPx</c>
///     is authored larger than the maquette's, so the desk cells — and
///     the pawns on them — read big in the foreground. The illusion of
///     "two scales" comes from two boards with different tile sizes, not
///     from hand-scaling any node.</item>
///   <item><b>Static.</b> J3c-1 lays out the ~7 Company slots
///     (<see cref="DeskSlotLayoutLogic"/>, Godot-free, xUnit-pinned) and
///     places one placeholder <see cref="DeskCompanyPawn"/> on each.
///     Nothing is selectable or draggable yet — that is J3c-2.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>J3c-1bis — the desk is a triangle, not a rectangle.</b> An F6 smoke
/// test caught two desk bugs. (1) The desk read as a <i>flat brown
/// rectangle</i>: it was hosted in a <see cref="SubViewportContainer"/>
/// carrying an opaque <see cref="Panel"/> child whose <c>StyleBoxFlat</c>
/// painted ON TOP of the SubViewport texture, hiding the iso grid and the
/// pawns rendered inside it. (2) The mockup wants the desk as a
/// <b>triangle</b> in the bottom-left corner — it fills the wedge the
/// central maquette diamond does not cover, with the maquette/desk
/// frontier being the lower-left edge of the maquette's iso diamond — but
/// a <see cref="SubViewportContainer"/> is intrinsically rectangular.
/// <list type="bullet">
///   <item><b>SubViewportContainer dropped, plain TextureRect kept.</b>
///     The desk <see cref="SubViewport"/> now hangs directly under this
///     <see cref="Control"/> root and is shown via a plain
///     <see cref="TextureRect"/> (<c>DeskTextureRect</c>) whose
///     <c>Texture</c> is the SubViewport's render target, assigned in
///     <see cref="ConfigureDesk"/>. The opaque <c>Panel</c> is gone — the
///     desk's brown surface is now drawn <i>in the iso world</i> by the
///     board (<c>IsoBoard.DrawPlaceholderFloor</c>), below the grid, so
///     the grid and pawns sit on top of it.</item>
///   <item><b>Triangular clip by shader.</b> <c>DeskTextureRect</c> runs
///     <c>desk_triangle_clip.gdshader</c>, which <c>discard;</c>s every
///     fragment on the maquette side of the frontier line. The frontier
///     is computed in <see cref="DeskClipFrontierLogic"/> (Godot-free,
///     xUnit-pinned) from the maquette diamond's lower-left edge and the
///     desk rect, and pushed into the shader's uniforms. A shader clip is
///     the cleanest of the options: one fragment pass, an analytic
///     diagonal, no fragile mask-node hierarchy — and crucially it leaves
///     the locked "two SubViewports" decision <b>untouched</b>: the desk
///     keeps its private render world, camera, and iso scale; only how
///     its texture is <i>shown</i> changed (a TextureRect with a clip
///     shader instead of a SubViewportContainer).</item>
///   <item><b>Full-screen desk rect — corner-to-corner triangle
///     (2026-05-21, J3c-1bis F6 fix).</b> The desk <see cref="SubViewport"/>
///     and the <c>DeskTextureRect</c> are both <b>full screen</b>
///     (1920x1080), not a small bottom-left rectangle. The shader clips a
///     triangle <i>inside its container rect</i>; a 620x420 container
///     therefore bounded the triangle with a hard horizontal top edge and
///     a hard vertical right edge — the F6 "truncated triangle" bug. With
///     a full-screen rect the clip diagonal runs corner to corner and the
///     desk triangle is the true bottom-left wedge, its hypotenuse the
///     maquette diamond edge with no parasitic straight borders. The
///     desk's iso content (floor, grid, pawns) still occupies only the
///     bottom-left of that full-screen render — the immobile
///     <c>DeskCamera2D</c> frames it there — and the shader discards the
///     empty top-right anyway.</item>
///   <item><b>Static-frontier scope (J3c-1).</b> The clip frontier is
///     computed once in <see cref="ConfigureDesk"/> from the maquette's
///     <i>parked</i> camera position. If the player pans the maquette the
///     diamond edge moves but the frontier does not follow — a known,
///     accepted limit of the <i>static</i> J3c-1 desk (the desk takes no
///     input and the shell ships parked-centred). When the pan becomes a
///     first-class interaction the frontier recompute moves into
///     <see cref="_Input"/> alongside the pan apply — a one-call addition,
///     the helper is already pure and ready.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>State ownership.</b> Four pure-C# helpers, each Godot-free and
/// xUnit-pinned, carry the only arithmetic in this shell. (1) The pan
/// decision — when a drag is live, where the clamped camera centre lands
/// — is <see cref="MapViewportPanLogic"/> (J3a). (2) The residual
/// central rectangle the maquette viewport must occupy — screen minus the
/// two HUD bands — is <see cref="HudLayoutLogic"/> (J3b). (3) The Company
/// slot layout — which iso cell each of the ~7 pawns sits on — is
/// <see cref="DeskSlotLayoutLogic"/> (J3c-1). (4) The maquette/desk clip
/// frontier line is <see cref="DeskClipFrontierLogic"/> (J3c-1bis). This
/// node is the engine seam only: it converts <see cref="InputEvent"/>s and
/// scene geometry into helper calls and applies the helper results onto
/// Godot nodes. Same logic-vs-node split as J3-iso (<c>IsoBoard</c> ↔
/// <c>IsoProjection</c>) and P8.2 (<c>E1WorldMap</c> ↔ <c>CameraPanLogic</c>).
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The maquette
/// <see cref="SubViewportContainer"/>, the desk <see cref="SubViewport"/>,
/// and the desk <see cref="TextureRect"/> are all direct children of this
/// <see cref="Control"/> root — <b>never</b> under the <c>HudLayer</c>
/// <see cref="CanvasLayer"/>. A <see cref="CanvasLayer"/> has its own
/// canvas transform that ignores a SubViewport's <see cref="Camera2D"/>;
/// putting pannable world content under it would silently break the pan.
/// The <c>HudLayer</c> is the <i>only</i> <see cref="CanvasLayer"/> in
/// this scene and it carries <i>only</i> the HUD — fixed content is
/// exactly what a <see cref="CanvasLayer"/> is for. The desk sits
/// <i>behind</i> the HUD by tree order plus the HUD's higher layer index
/// — no maths needed.
/// </para>
///
/// <para>
/// <b>Input routing (trap #9).</b> Pan tracking is wired in
/// <see cref="_Input"/>, not <see cref="Node._UnhandledInput"/>. A
/// <see cref="SubViewportContainer"/> has <c>MouseFilter = Stop</c> and
/// would eat the motion events before <c>_UnhandledInput</c> ever fires —
/// the exact freeze of trap #9. <see cref="_Input"/> runs ahead of the
/// GUI routing and is immune to <c>mouse_filter</c>. Motion events are
/// <b>not</b> marked handled: J3a owns the pan <i>response</i>, not the
/// event — future hover / selection systems on this surface must still
/// see the same motion. The desk <see cref="TextureRect"/> has
/// <c>MouseFilter = Ignore</c>: the static J3c-1 desk takes no input, and
/// an <c>Ignore</c> filter means an MMB press over the desk corner still
/// reaches the maquette pan. J3c-2 will flip this to <c>Stop</c> when the
/// desk gains pawn selection. The desk <c>TextureRect</c> is now
/// full-screen, but <c>Ignore</c> keeps it transparent to input — the
/// maquette pan still receives every event regardless of the rect size.
/// </para>
///
/// <para>
/// <b>Gesture mutual-exclusion (trap #5) — deliberately deferred.</b> J3a
/// has exactly one gesture (MMB-drag pan). There is no second gesture to
/// collide with yet, so no extra suppression is wired beyond marking the
/// MMB press/release handled. When J3c-2 adds left-click pawn selection
/// the trap #5 defense gets added <i>then</i>, against a real second
/// gesture — not speculatively now.
/// </para>
///
/// <para>
/// <b>Preflight (trap #2).</b> <see cref="_Ready"/> prints a fixed
/// diagnostics block — viewport size, content extent, clamp bounds,
/// camera start, the resolved pan button, the HUD band heights plus the
/// residual map rect, and (J3c-1) the desk viewport rect, the resolved
/// Company slot cells, the desk camera park, and the resolved clip
/// frontier. A pan, clipping, HUD-layout, desk-slot, or clip-frontier bug
/// is then visible in the Godot Output on the first run.
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
    /// <c>-offset_top</c> in <c>GameScreen.tscn</c>. Held here so
    /// <see cref="HudLayoutLogic"/> sizes the residual map rect to exactly
    /// the band the scene draws, and so the magic number is not duplicated
    /// across the top/bottom pair. When Mira's parchment liseré lands the
    /// asset's authored height is propagated from this one constant.
    ///
    /// <para>
    /// Adjusted 2026-05-21 (Didier) from 90 px to 32 px — slim bands. The
    /// residual map rect grows accordingly to
    /// <c>[0,32 1920x1016]</c> on a 1920x1080 screen.
    /// </para>
    /// </summary>
    private const float HudBandHeight = 32f;

    /// <summary>
    /// Where the centroid of the Company formation should land on screen,
    /// as a fraction of the desk viewport — used to park the immobile
    /// <c>DeskCamera2D</c> (J3c-1bis). The desk is the bottom-left
    /// triangle, so the formation is framed in the lower-left quadrant:
    /// X at ~28% from the left, Y at ~70% from the top. These are the only
    /// two framing magic numbers; held named here rather than buried in
    /// <see cref="ConfigureDesk"/> so a re-frame is one obvious edit. They
    /// are a <i>framing</i> choice (where in the triangle the pawns sit),
    /// not slot geometry — slot geometry is <see cref="DeskSlotLayoutLogic"/>.
    /// </summary>
    private const float DeskFormationScreenFracX = 0.28f;
    private const float DeskFormationScreenFracY = 0.70f;

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
        // the screen minus the two fixed HUD bands. This is the second of
        // two guarantees that the maquette never bleeds over the HUD: the
        // HudLayer's higher layer index already draws the bands last, and
        // this re-size physically clips the maquette to the gap between
        // them. The maths is HudLayoutLogic (Godot-free, xUnit-pinned);
        // this node only applies the result to the SubViewportContainer's
        // offsets. The CanvasLayer HUD bands themselves need no code —
        // their .tscn anchors keep them welded to the screen edges.
        var screen = Size;
        var residual = HudLayoutLogic.ResidualMapRect(
            screen.X, screen.Y, HudBandHeight, HudBandHeight);
        ApplyResidualRect(residual);

        // The SubViewport's render size follows the (now residual)
        // container rect, so the maquette is clipped exactly to the
        // central panel between the HUD bands. Stretch keeps the two in
        // lockstep.
        _mapViewport.Size = (Vector2I)_mapContainer.Size;

        // Park the camera centred on the maquette content, then clamp so
        // an authored-out-of-bounds start (or a content smaller than the
        // viewport) is corrected before the first frame.
        var content = ContentSize();
        var viewport = ViewportSize();
        var start = new Vector2(content.X * 0.5f, content.Y * 0.5f);
        var clampedStart = MapViewportPanLogic.ClampToContent(
            ToPan(start), ToPan(content), ToPan(viewport));
        _mapCamera.Position = ToGodot(clampedStart);

        ConfigureDesk();

        Preflight(content, viewport, clampedStart, screen);
    }

    /// <summary>
    /// Input seam (trap #9): routes the middle-button drag-pan into
    /// <see cref="MapViewportPanLogic"/> and applies the clamped result
    /// onto the SubViewport's <see cref="Camera2D"/>.
    ///
    /// <para>
    /// Events arriving here are already in the <see cref="SubViewport"/>'s
    /// coordinate space because the <see cref="SubViewportContainer"/>
    /// forwards them transformed — so the helper receives maquette-space
    /// positions directly, no manual conversion.
    /// </para>
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
            // The button press/release IS owned by the pan gesture —
            // mark it handled so a future click-select on this surface
            // does not also fire from the same MMB event.
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
            // pan owns its response, not the motion event itself. Hover
            // and selection systems (J3c-2, J6) must still see motion.
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
    /// onto the <see cref="SubViewportContainer"/>'s offsets. The
    /// container uses corner anchors at the screen origin in the
    /// <c>.tscn</c>, so positioning is offset-driven. Kept as one method
    /// so the J3b residual-rect application has a single, named seam — a
    /// future window-resize handler would call exactly this.
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
    /// <c>DeskTextureRect</c>, parks the immobile desk camera so the
    /// Company formation frames inside the bottom-left desk triangle,
    /// places one placeholder pawn per Company slot, and computes the
    /// triangular clip frontier the desk shader consumes.
    ///
    /// <para>
    /// The slot cells are pure-C# maths (Godot-free, xUnit-pinned); this
    /// method only converts each <c>TileCoordinate</c> to a Godot pixel
    /// through the desk board's own <see cref="IsoProjection"/> and
    /// instances a <see cref="DeskCompanyPawn"/> there. The clip frontier
    /// is likewise <see cref="DeskClipFrontierLogic"/> maths; this method
    /// only feeds it the scene geometry and writes the result into the
    /// shader uniforms. No pawn carries game state — J3c-1 is static.
    /// </para>
    /// </summary>
    private void ConfigureDesk()
    {
        // J3c-1bis: the desk viewport and the DeskTextureRect are both
        // full-screen (authored in GameScreen.tscn with anchors_preset 15).
        // The shader clips its triangle INSIDE the container rect — a
        // small rect would bound the triangle with a hard horizontal /
        // vertical border (the F6 "truncated triangle" bug). A full-screen
        // rect lets the clip diagonal run corner to corner. The render
        // size follows the rect so the rendered desk world maps 1:1 onto
        // the screen before the triangular shader clip drops the maquette
        // wedge.
        _deskViewport.Size = (Vector2I)_deskTextureRect.Size;

        // Show the desk SubViewport's render target through the plain
        // TextureRect. No SubViewportContainer (intrinsically rectangular)
        // — the TextureRect carries the clip shader that makes the desk a
        // triangle. The locked "two SubViewports" decision is intact: the
        // desk still has its own render world below.
        _deskTextureRect.Texture = _deskViewport.GetTexture();

        // The static desk takes no input in J3c-1. An Ignore filter means
        // an MMB press landing over the desk corner still reaches the
        // maquette pan underneath — and stays true even though the
        // TextureRect is now full-screen, because Ignore is transparent to
        // input regardless of rect size. J3c-2 flips this to Stop for
        // selection (and then scopes the hit-test to the clipped triangle).
        _deskTextureRect.MouseFilter = MouseFilterEnum.Ignore;

        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // One placeholder pawn per slot. The leader slot (index 0) renders
        // slightly larger — foreground-prominent — per the mockup; the
        // rest are the formation behind it. Y-sort on the IsoBoard's
        // Occupants node keeps a pawn lower on screen drawing over one
        // higher up. The pawn pixel positions are summed here so the
        // desk camera can be parked on the true pixel centroid below.
        var formationCentre = Vector2.Zero;
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var pixel = _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
            formationCentre += pixel;
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
        formationCentre /= slots.Count;

        // Park the immobile desk camera so the formation centroid lands in
        // the lower-left quadrant of the full-screen desk viewport — i.e.
        // inside the bottom-left clip triangle, where the desk is kept.
        // A Camera2D's Position is the world point shown at the screen
        // centre; to put `formationCentre` at the screen fraction target
        // the camera parks at `formationCentre - (target - halfViewport)`.
        // Computed once — the desk camera never moves (only the maquette
        // pans, roadmap J3c-1).
        Vector2 deskViewport = _deskTextureRect.Size;
        var screenTarget = new Vector2(
            deskViewport.X * DeskFormationScreenFracX,
            deskViewport.Y * DeskFormationScreenFracY);
        _deskCamera.Position =
            formationCentre - (screenTarget - deskViewport * 0.5f);

        ApplyDeskClipFrontier();
    }

    /// <summary>
    /// J3c-1bis: compute the maquette/desk triangular-clip frontier and
    /// push it into the <c>DeskTextureRect</c>'s clip shader.
    ///
    /// <para>
    /// The frontier is the maquette diamond's lower-left edge — from its
    /// left apex to its bottom apex — expressed in <i>screen</i> pixels.
    /// The maquette diamond is in the maquette board's local space; the
    /// maquette <see cref="Camera2D"/> maps it to the
    /// <see cref="SubViewport"/> render, and the
    /// <see cref="SubViewportContainer"/> offsets place that render on
    /// screen. The composition of those two transforms is done here, then
    /// handed — as two screen points plus the desk rect — to
    /// <see cref="DeskClipFrontierLogic"/>, which returns the UV-space
    /// point + normal the shader needs.
    /// </para>
    ///
    /// <para>
    /// Computed once: J3c-1 ships the maquette parked-centred and the desk
    /// is static (see the class doc, "static-frontier scope"). When the
    /// pan becomes interactive this same call moves into <see cref="_Input"/>.
    /// </para>
    /// </summary>
    private void ApplyDeskClipFrontier()
    {
        // The maquette grid-bounding diamond, in the maquette board's
        // local space: [top, right, bottom, left] apexes.
        var diamond = _maquette.GridBoundingDiamond();
        Vector2 leftApexLocal = diamond[3];
        Vector2 bottomApexLocal = diamond[2];

        // Maquette-local -> screen. The maquette camera is centred on
        // _mapCamera.Position; a world point P lands on the SubViewport at
        // (P - cameraCentre) + halfViewport, and the SubViewport render is
        // placed on screen at the container's offset (OffsetLeft/Top).
        Vector2 leftApexScreen = MaquetteLocalToScreen(leftApexLocal);
        Vector2 bottomApexScreen = MaquetteLocalToScreen(bottomApexLocal);

        // The desk TextureRect is full-screen: its origin is the screen
        // origin and its size is the screen size. The clip frontier is
        // therefore expressed against the whole screen rect, which is what
        // makes the triangle run corner to corner.
        var deskOrigin = new SysVec2(
            _deskTextureRect.OffsetLeft, _deskTextureRect.OffsetTop);
        var deskSize = new SysVec2(
            _deskTextureRect.Size.X, _deskTextureRect.Size.Y);

        var frontier = DeskClipFrontierLogic.Compute(
            ToSys(leftApexScreen), ToSys(bottomApexScreen), deskOrigin, deskSize);

        var material = (ShaderMaterial)_deskTextureRect.Material;
        material.SetShaderParameter(
            ClipPointUniform, new Vector2(frontier.PointUv.X, frontier.PointUv.Y));
        material.SetShaderParameter(
            ClipNormalUniform, new Vector2(frontier.NormalUv.X, frontier.NormalUv.Y));
    }

    /// <summary>
    /// Map a point in the maquette board's local pixel space to a screen
    /// pixel: through the maquette <see cref="Camera2D"/> into the
    /// <see cref="SubViewport"/> render, then offset by the
    /// <see cref="SubViewportContainer"/>'s screen position. The single
    /// place this transform composition lives, for the J3c-1bis clip.
    /// </summary>
    private Vector2 MaquetteLocalToScreen(Vector2 maquetteLocal)
    {
        Vector2 halfViewport = _mapContainer.Size * 0.5f;
        Vector2 inViewport = maquetteLocal - _mapCamera.Position + halfViewport;
        var containerOrigin = new Vector2(
            _mapContainer.OffsetLeft, _mapContainer.OffsetTop);
        return inViewport + containerOrigin;
    }

    /// <summary>
    /// The pannable maquette content extent in world pixels. Prefers the
    /// real <see cref="IsoBoard"/> floor bitmap size; falls back to the
    /// placeholder iso-diamond bounding box while Mira's district asset is
    /// not yet wired (J2). The clamp is computed against this extent so
    /// the camera never pans past the maquette into the void.
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
        // Mirror the clamp the runtime will enforce so a too-small content
        // or a bad start is visible in the log, not at the first drag.
        float halfVx = viewport.X * 0.5f;
        float halfVy = viewport.Y * 0.5f;
        GD.Print($"[GameScreen] preflight: camera clamp X=[{halfVx},{content.X - halfVx}] " +
                 $"Y=[{halfVy},{content.Y - halfVy}] start={cameraStart}");
        GD.Print($"[GameScreen] preflight: pan button={MapViewportPanLogic.PanButton} " +
                 $"state={_pan.State} -- _Input wired (trap #9)");

        // J3b HUD diagnostics: the CanvasLayer index, the band heights,
        // and the residual map rect. A collapsed residual (HUD bands
        // larger than the screen) is a design error — shout it.
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

        // J3c-1 desk diagnostics: the second SubViewport rect, the desk
        // iso scale vs the maquette scale, the resolved Company slot
        // cells, the desk camera park, and the J3c-1bis triangular-clip
        // frontier. A wrong desk viewport size, a slot count drift, an
        // off-screen camera park, or a degenerate clip frontier surfaces
        // here on first run.
        var slots = DeskSlotLayoutLogic.CompanySlotCells();
        GD.Print($"[GameScreen] preflight: DeskViewport size={_deskViewport.Size} " +
                 $"DeskTextureRect rect={_deskTextureRect.Size} " +
                 $"mouseFilter={_deskTextureRect.MouseFilter} (J3c-1 static, full-screen)");
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
        // The desk camera park, and where each pawn lands on screen — so a
        // pawn pushed off the full-screen desk viewport surfaces here.
        GD.Print($"[GameScreen] preflight: DeskCamera2D parked at " +
                 $"{_deskCamera.Position} -- formation framed bottom-left " +
                 $"(frac {DeskFormationScreenFracX},{DeskFormationScreenFracY})");
        Vector2 deskHalf = _deskTextureRect.Size * 0.5f;
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var worldPx = _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
            var screenPx = worldPx - _deskCamera.Position + deskHalf;
            bool onScreen = screenPx.X >= 0 && screenPx.X <= _deskTextureRect.Size.X
                          && screenPx.Y >= 0 && screenPx.Y <= _deskTextureRect.Size.Y;
            GD.Print($"[GameScreen] preflight:   desk pawn {i} cell={cell} " +
                     $"-> screen={screenPx} onScreen={onScreen}");
            if (!onScreen)
            {
                GD.PushWarning($"[GameScreen] desk pawn {i} ({cell}) is " +
                               "outside the desk viewport — it will not render.");
            }
        }
        var clipMaterial = (ShaderMaterial)_deskTextureRect.Material;
        GD.Print($"[GameScreen] preflight: desk clip frontier " +
                 $"point={clipMaterial.GetShaderParameter(ClipPointUniform)} " +
                 $"normal={clipMaterial.GetShaderParameter(ClipNormalUniform)} " +
                 $"-- triangular clip (J3c-1bis)");
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 / System.Numerics --------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);

    private static SysVec2 ToSys(Vector2 v) => new(v.X, v.Y);
}
