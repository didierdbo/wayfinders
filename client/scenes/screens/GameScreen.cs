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
///     unchanged by the J3c-1bis fixes below</b> — see those paragraphs.</item>
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
///     board (<c>IsoBoard.DrawPlaceholderFloor</c>), below the grid.</item>
///   <item><b>Triangular clip by shader.</b> <c>DeskTextureRect</c> runs
///     <c>desk_triangle_clip.gdshader</c>, which <c>discard;</c>s every
///     fragment on the maquette side of the frontier line. The frontier
///     is computed in <see cref="DeskClipFrontierLogic"/> (Godot-free,
///     xUnit-pinned) from the maquette diamond's lower-left edge and the
///     desk rect, and pushed into the shader's uniforms. A shader clip is
///     the cleanest of the options: one fragment pass, an analytic
///     diagonal, no fragile mask-node hierarchy — and crucially it leaves
///     the locked "two SubViewports" decision <b>untouched</b>.</item>
///   <item><b>Full-screen desk rect — corner-to-corner triangle
///     (2026-05-21).</b> The desk <see cref="SubViewport"/> and the
///     <c>DeskTextureRect</c> are both <b>full screen</b> (1920x1080), not
///     a small bottom-left rectangle. The shader clips a triangle
///     <i>inside its container rect</i>; a small container would bound the
///     triangle with a hard horizontal top edge and a hard vertical right
///     edge — the F6 "truncated triangle" bug. With a full-screen rect the
///     clip diagonal runs corner to corner.</item>
///   <item><b>Floor fills the viewport, not the diamond (2026-05-21 F6
///     fix #1).</b> The desk floor was the iso diamond bounding the 8×8
///     desk grid — a compact rhombus whose upper-left edge sits well
///     inside the screen. Even after the shader cut the maquette wedge,
///     that diamond edge stayed visible as a parasitic top-left border.
///     The fix: <see cref="DeskFloorRectLogic"/> (Godot-free, xUnit-pinned)
///     computes a viewport-covering axis-aligned rect, fed to the board
///     via <c>IsoBoard.SetDeskFloorFillRect</c>. The floor now reaches the
///     screen edges; the only edge the player sees is the shader's clean
///     diagonal hypotenuse.</item>
///   <item><b>Pawns ride an un-clipped screen-space layer (2026-05-21 F6
///     fix #2).</b> The clip shader runs on the whole <c>DeskViewport</c>
///     texture — floor <i>and</i> pawns. So a pawn standing near the floor
///     edge had its body and head sliced off: only its feet survived. But
///     the clip must cut <b>only the floor</b> — a pawn is a vertical
///     element posed on the floor and legitimately rises above the floor's
///     diagonal edge. The fix: the <c>DeskViewport</c> keeps <i>only</i>
///     the floor + grid (the shader keeps clipping that); the pawns move
///     to <c>DeskEntities</c>, a Y-sorted <see cref="Node2D"/> parented
///     under this <see cref="Control"/> root, carrying <b>no</b> shader.
///     Its position (<see cref="DeskEntityLayerLogic"/>, Godot-free,
///     xUnit-pinned) puts a pawn placed at a desk-local cell pixel on the
///     exact screen pixel the clipped floor draws that cell — the two
///     layers share one coordinate frame, only the clip differs. The two
///     locked SubViewports are untouched: <c>DeskEntities</c> is a plain
///     node, not a third render world.</item>
///   <item><b>Reliable screen size — the F6 root cause (2026-05-21).</b>
///     A second F6 smoke caught the pawns landing on the maquette and a
///     translucent desk bleed along the clip diagonal. Both had ONE root
///     cause: the desk size was read from <c>DeskTextureRect.Size</c> in
///     <see cref="_Ready"/>, but a <see cref="Control"/> authored with
///     <c>anchors_preset 15</c> has not had its layout resolved that
///     early — <c>Size</c> was still the design-time default. The desk
///     viewport, the floor rect, the camera park, the entity-layer origin
///     and the clip frontier were all sized off that stale value: the
///     pawns mapped onto the wrong screen region, and the clip frontier
///     maths threw on a non-positive rect so the shader kept its
///     <c>(0,0)</c> default normal and clipped nothing. The fix:
///     <see cref="_Ready"/> reads <c>GetViewportRect().Size</c> — the
///     real window rect, valid the instant <c>_Ready</c> runs — and
///     passes it down to <see cref="ConfigureDesk"/> as the one screen
///     size every desk computation uses.</item>
///   <item><b>Static-frontier scope (J3c-1).</b> The clip frontier, the
///     floor rect, and the entity-layer origin are all computed once in
///     <see cref="ConfigureDesk"/> from the maquette's <i>parked</i>
///     camera position and the immobile desk camera. If the player pans
///     the maquette the diamond edge moves but the frontier does not
///     follow — a known, accepted limit of the <i>static</i> J3c-1 desk.
///     When the pan becomes a first-class interaction the recompute moves
///     into <see cref="_Input"/> — the helpers are already pure and ready.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>State ownership.</b> Six pure-C# helpers, each Godot-free and
/// xUnit-pinned, carry the only arithmetic in this shell. (1) The pan
/// decision is <see cref="MapViewportPanLogic"/> (J3a). (2) The residual
/// central rectangle is <see cref="HudLayoutLogic"/> (J3b). (3) The
/// Company slot layout is <see cref="DeskSlotLayoutLogic"/> (J3c-1).
/// (4) The maquette/desk clip frontier is <see cref="DeskClipFrontierLogic"/>
/// (J3c-1bis). (5) The desk floor fill rect is
/// <see cref="DeskFloorRectLogic"/> (J3c-1bis F6 fix #1). (6) The desk
/// entity-layer origin is <see cref="DeskEntityLayerLogic"/> (J3c-1bis F6
/// fix #2). This node is the engine seam only: it converts
/// <see cref="InputEvent"/>s and scene geometry into helper calls and
/// applies the helper results onto Godot nodes.
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The maquette
/// <see cref="SubViewportContainer"/>, the desk <see cref="SubViewport"/>,
/// the desk <see cref="TextureRect"/>, and the <c>DeskEntities</c> layer
/// are all direct children of this <see cref="Control"/> root — <b>never</b>
/// under the <c>HudLayer</c> <see cref="CanvasLayer"/>. A
/// <see cref="CanvasLayer"/> has its own canvas transform that ignores a
/// SubViewport's <see cref="Camera2D"/>; the <c>HudLayer</c> is the
/// <i>only</i> <see cref="CanvasLayer"/> in this scene and it carries
/// <i>only</i> the HUD. The desk floor and the desk entity layer sit
/// <i>behind</i> the HUD by the HUD's higher layer index.
/// </para>
///
/// <para>
/// <b>Input routing (trap #9).</b> Pan tracking is wired in
/// <see cref="_Input"/>, not <see cref="Node._UnhandledInput"/>. A
/// <see cref="SubViewportContainer"/> has <c>MouseFilter = Stop</c> and
/// would eat the motion events before <c>_UnhandledInput</c> ever fires.
/// <see cref="_Input"/> runs ahead of the GUI routing and is immune to
/// <c>mouse_filter</c>. The desk <see cref="TextureRect"/> has
/// <c>MouseFilter = Ignore</c>: the static J3c-1 desk takes no input, so
/// an MMB press over the desk corner still reaches the maquette pan. The
/// <c>DeskEntities</c> layer is a <see cref="Node2D"/> — it has no
/// <c>mouse_filter</c> and never intercepts input; the static pawns take
/// no input in J3c-1. J3c-2 will add pawn selection on that layer.
/// </para>
///
/// <para>
/// <b>Gesture mutual-exclusion (trap #5) — deliberately deferred.</b> J3a
/// has exactly one gesture (MMB-drag pan). There is no second gesture to
/// collide with yet, so no extra suppression is wired beyond marking the
/// MMB press/release handled.
/// </para>
///
/// <para>
/// <b>Preflight (trap #2).</b> <see cref="_Ready"/> prints a fixed
/// diagnostics block — viewport size, content extent, clamp bounds,
/// camera start, the resolved pan button, the HUD band heights plus the
/// residual map rect, and (J3c-1 / bis) the desk viewport rect, the
/// resolved Company slot cells, the desk camera park, the clip frontier,
/// the desk floor fill rect, and the desk entity-layer origin.
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
    ///
    /// <para>
    /// Adjusted 2026-05-21 (Didier) from 90 px to 32 px — slim bands. The
    /// residual map rect grows accordingly to <c>[0,32 1920x1016]</c> on a
    /// 1920x1080 screen.
    /// </para>
    /// </summary>
    private const float HudBandHeight = 32f;

    /// <summary>
    /// Where the centroid of the Company formation should land on screen,
    /// as a fraction of the desk viewport — used to park the immobile
    /// <c>DeskCamera2D</c> (J3c-1bis). The desk is the bottom-left
    /// triangle, so the formation is framed in the lower-left quadrant:
    /// X at ~28% from the left, Y at ~70% from the top.
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

    /// <summary>
    /// The un-clipped screen-space Y-sorted layer that hosts the desk
    /// pawns (J3c-1bis F6 fix #2). Created in <see cref="ConfigureDesk"/>
    /// as a direct child of this <see cref="Control"/> root — never under
    /// the clip-shader <c>DeskTextureRect</c>, never under the
    /// <c>HudLayer</c>. The clip shader only touches the
    /// <c>DeskViewport</c> texture (the floor); the pawns on this layer
    /// are drawn whole. See <see cref="DeskEntityLayerLogic"/>.
    /// </summary>
    private Node2D _deskEntities = null!;

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
        // the screen minus the two fixed HUD bands. The maths is
        // HudLayoutLogic (Godot-free, xUnit-pinned); this node only
        // applies the result to the SubViewportContainer's offsets.
        //
        // J3c-1bis F6 fix (2026-05-21): the screen size is read from
        // GetViewportRect, NOT from this Control's Size. At _Ready() a
        // Control authored with anchors_preset 15 has NOT had its layout
        // resolved yet — Size is still the design-time default (often
        // (0,0)), and the desk's full-screen DeskTextureRect carries the
        // same unresolved Size. Sizing the desk viewport, the floor rect,
        // the camera park, the clip frontier and the entity layer off that
        // stale value put the pawns on the maquette and made the clip
        // frontier maths throw (DeskClipFrontierLogic rejects a
        // non-positive rect) — the shader then kept its (0,0) default
        // normal and clipped nothing, so the desk rectangle bled
        // translucently over the maquette. GetViewportRect().Size is the
        // real window rect and is valid the instant _Ready runs.
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
    /// <c>DeskTextureRect</c>, parks the immobile desk camera, builds the
    /// un-clipped <c>DeskEntities</c> layer and places one placeholder
    /// pawn per Company slot on it, fills the desk floor to the viewport,
    /// and computes the triangular clip frontier the desk shader consumes.
    ///
    /// <para>
    /// All the arithmetic is pure-C# (Godot-free, xUnit-pinned); this
    /// method only converts to / from Godot types and applies results
    /// onto nodes. No pawn carries game state — J3c-1 is static.
    /// </para>
    ///
    /// <para>
    /// <b>Screen size is a parameter (J3c-1bis F6 fix, 2026-05-21).</b>
    /// The desk is sized off <paramref name="screen"/> — the real window
    /// rect from <c>GetViewportRect().Size</c>, taken in <see cref="_Ready"/>
    /// — NOT off <c>DeskTextureRect.Size</c>. At <c>_Ready()</c> the
    /// full-screen <c>DeskTextureRect</c>'s layout is not yet resolved, so
    /// its <c>Size</c> is stale; reading it sized the whole desk wrong and
    /// scattered the pawns onto the maquette. One screen size, taken from
    /// a reliable source, flows through every desk computation here.
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
        // full-screen. The DeskTextureRect is authored with anchors_preset
        // 15, so it WILL cover `screen` once layout resolves — but at
        // _Ready() its Size is not yet resolved. Drive both the viewport
        // render size and (below) every desk computation off the reliable
        // `screen` value, and force the DeskTextureRect to that size now so
        // the shader's UV space matches the desk viewport 1:1.
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

        // J3c-1bis F6 fix #2 (clip-frontier crispness): sample the desk
        // SubViewport texture with NEAREST filtering, not the default
        // bilinear. The DeskViewport has transparent_bg = true; the floor
        // rect covers it, but along the shader's discard diagonal a
        // bilinear tap blends the brown floor texel against a transparent
        // texel one pixel over, leaving a half-alpha fringe that reads as
        // the desk "bleeding" translucently onto the maquette. NEAREST
        // takes one texel, no cross-edge blend, so the clip boundary is
        // exactly the shader's crisp discard line.
        _deskTextureRect.TextureFilter = TextureFilterEnum.Nearest;

        // J3c-1bis F6 fix #1: fill the desk floor across the whole desk
        // viewport rather than the compact grid-bounding diamond, so no
        // parasitic diamond edge survives the triangular clip. The desk
        // camera's Position is the desk-local point shown at the viewport
        // centre, so it IS the camera centre in the board's local space.
        // The desk camera is parked further below; pass its FINAL parked
        // position to the floor-rect maths by computing the park first.
        // DeskFloorRectLogic (Godot-free, xUnit-pinned) computes the rect.
        var slots = DeskSlotLayoutLogic.CompanySlotCells();

        // J3c-1bis F6 fix #2: the desk pawns ride an un-clipped
        // screen-space layer, NOT the clipped DeskViewport. Build it as a
        // Y-sorted Node2D child of this Control root, added after the
        // DeskTextureRect so it draws on top of the clipped floor, and
        // before the HudLayer (a CanvasLayer always draws above it anyway).
        _deskEntities = new Node2D
        {
            Name = "DeskEntities",
            YSortEnabled = true,
        };
        AddChild(_deskEntities);

        // Sum the desk-local pixel of every slot cell first, so the desk
        // camera can be parked on the true pixel centroid of the formation
        // before any pawn or floor rect depends on the parked position.
        var formationCentre = Vector2.Zero;
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            formationCentre += _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
        }
        formationCentre /= slots.Count;

        // Park the immobile desk camera so the formation centroid lands in
        // the lower-left quadrant of the full-screen desk viewport — inside
        // the bottom-left clip triangle. A Camera2D's Position is the world
        // point shown at the screen centre. This is computed off `screen`
        // (the reliable window rect), not the stale DeskTextureRect.Size.
        var screenTarget = new Vector2(
            screen.X * DeskFormationScreenFracX,
            screen.Y * DeskFormationScreenFracY);
        _deskCamera.Position =
            formationCentre - (screenTarget - screen * 0.5f);

        // J3c-1bis F6 fix #1: now the desk camera is parked, compute the
        // viewport-covering floor fill rect from the FINAL camera position.
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(_deskCamera.Position), screenSize);
        _deskBoard.SetDeskFloorFillRect(floorRect);

        // J3c-1bis F6 fix #2: place the un-clipped entity layer so a pawn
        // at a desk-local cell pixel lands on the exact screen pixel the
        // clipped floor draws that cell. DeskEntityLayerLogic (Godot-free,
        // xUnit-pinned) computes the origin: screenSize/2 - deskCameraCentre.
        // It is taken off the SAME `screen` size as the floor rect and the
        // camera park, so the pawn layer, the floor and the clip frontier
        // all live in one coordinate frame — the F6 fix root cause.
        var layerOrigin = DeskEntityLayerLogic.LayerOrigin(
            ToSys(_deskCamera.Position), screenSize);
        _deskEntities.Position = new Vector2(layerOrigin.X, layerOrigin.Y);

        // One placeholder pawn per slot. The leader slot (index 0) renders
        // slightly larger. The pawn lives on _deskEntities at the SAME
        // desk-local pixel the floor's iso grid uses for that cell — the
        // entity layer's origin makes the two coordinate frames coincide,
        // so the pawn sits on the brown desk floor on its formation cell.
        // Y-sort on _deskEntities keeps a pawn lower on screen drawing over
        // one higher up; the leader (largest col+row) projects lowest, so
        // it draws foreground — front-of-formation, exactly the mockup.
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
            _deskEntities.AddChild(pawn);
        }

        ApplyDeskClipFrontier(screen);
    }

    /// <summary>
    /// J3c-1bis: compute the maquette/desk triangular-clip frontier and
    /// push it into the <c>DeskTextureRect</c>'s clip shader.
    ///
    /// <para>
    /// The frontier is the maquette diamond's lower-left edge — from its
    /// left apex to its bottom apex — expressed in <i>screen</i> pixels.
    /// The composition of the maquette camera transform and the container
    /// offsets is done here, then handed — as two screen points plus the
    /// desk rect — to <see cref="DeskClipFrontierLogic"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Screen size is a parameter (J3c-1bis F6 fix, 2026-05-21).</b>
    /// The desk rect handed to <see cref="DeskClipFrontierLogic"/> is the
    /// reliable <paramref name="screen"/> size, not <c>DeskTextureRect.Size</c>
    /// — an unresolved <c>Size</c> is non-positive, which would make the
    /// frontier maths throw, leaving the shader on its no-clip default.
    /// </para>
    /// </summary>
    /// <param name="screen">
    /// The real screen size in pixels, from <c>GetViewportRect().Size</c>.
    /// </param>
    private void ApplyDeskClipFrontier(Vector2 screen)
    {
        // The maquette grid-bounding diamond, in the maquette board's
        // local space: [top, right, bottom, left] apexes.
        var diamond = _maquette.GridBoundingDiamond();
        Vector2 leftApexLocal = diamond[3];
        Vector2 bottomApexLocal = diamond[2];

        // Maquette-local -> screen.
        Vector2 leftApexScreen = MaquetteLocalToScreen(leftApexLocal);
        Vector2 bottomApexScreen = MaquetteLocalToScreen(bottomApexLocal);

        // The desk TextureRect is full-screen at the screen origin. Use the
        // reliable `screen` size — at _Ready() DeskTextureRect.Size is not
        // yet resolved and would be non-positive (the F6 root cause).
        var deskOrigin = SysVec2.Zero;
        var deskSize = new SysVec2(screen.X, screen.Y);

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
    /// <see cref="SubViewportContainer"/>'s screen position.
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
        // (GetViewportRect, reliable at _Ready) — see the F6 fix note on
        // ConfigureDesk. Print `screen` so a regression to the stale
        // DeskTextureRect.Size is immediately visible.
        var screenSize = new SysVec2(screen.X, screen.Y);
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

        // J3c-1bis F6 fix #2: the desk pawns live on the un-clipped
        // DeskEntities layer. Print its origin and verify each pawn lands
        // on screen via DeskLocalToScreen — a pawn pushed off the screen
        // surfaces here. Note these are the WHOLE pawn positions: unlike
        // the clipped floor, nothing trims them.
        var layerOrigin = ToSys(_deskEntities.Position);
        GD.Print($"[GameScreen] preflight: DeskEntities origin={_deskEntities.Position} " +
                 $"ySort={_deskEntities.YSortEnabled} children={_deskEntities.GetChildCount()} " +
                 $"-- un-clipped pawn layer (J3c-1bis F6 fix #2)");
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = slots[i];
            var deskLocal = _deskBoard.CellToPixel(new Vector2I(cell.Col, cell.Row));
            var screenPx = DeskEntityLayerLogic.DeskLocalToScreen(
                ToSys(deskLocal), layerOrigin);
            bool onScreen = screenPx.X >= 0 && screenPx.X <= screen.X
                          && screenPx.Y >= 0 && screenPx.Y <= screen.Y;
            GD.Print($"[GameScreen] preflight:   desk pawn {i} cell={cell} " +
                     $"-> screen=({screenPx.X},{screenPx.Y}) onScreen={onScreen}");
            if (!onScreen)
            {
                GD.PushWarning($"[GameScreen] desk pawn {i} ({cell}) is " +
                               "outside the screen — it will not render.");
            }
        }

        // J3c-1bis F6 fix #1: the desk floor fill rect, computed off the
        // FINAL parked desk camera and the reliable `screen` size.
        var floorRect = DeskFloorRectLogic.Compute(
            ToSys(_deskCamera.Position), screenSize);
        GD.Print($"[GameScreen] preflight: desk floor fill rect topLeft=" +
                 $"({floorRect.TopLeft.X},{floorRect.TopLeft.Y}) " +
                 $"size=({floorRect.Size.X},{floorRect.Size.Y}) " +
                 $"-- viewport-covering, no parasitic diamond edge (J3c-1bis F6 fix #1)");

        var clipMaterial = (ShaderMaterial)_deskTextureRect.Material;
        GD.Print($"[GameScreen] preflight: desk clip frontier " +
                 $"point={clipMaterial.GetShaderParameter(ClipPointUniform)} " +
                 $"normal={clipMaterial.GetShaderParameter(ClipNormalUniform)} " +
                 $"-- triangular clip, FLOOR ONLY, NEAREST sampled (J3c-1bis)");
    }

    // --- engine seam: Godot.Vector2 <-> PanVec2 / System.Numerics --------

    private static PanVec2 ToPan(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(PanVec2 v) => new(v.X, v.Y);

    private static SysVec2 ToSys(Vector2 v) => new(v.X, v.Y);
}
