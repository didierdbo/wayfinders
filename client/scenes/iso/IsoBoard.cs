using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Services.Iso;
using Wayfinders.Client.Utils;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scenes.Iso;

/// <summary>
/// Reusable root node of the J3-iso hybrid socle (design doc
/// <c>rune-iso-rendering-design-2026-05-20.md</c>, decision #1 validated
/// by Didier 2026-05-21). One <see cref="IsoBoard"/> instance is one
/// isometric space — the district maquette and the cartographer's desk
/// both instance this same scene (<c>IsoBoard.tscn</c>).
///
/// <para>
/// <b>What this socle is — and is not.</b> It is exactly three things:
/// <list type="number">
///   <item><b>Layer 1 — the floor.</b> One <see cref="Sprite2D"/> showing
///     the large Blender district bitmap. Not tiled, not clipped, not a
///     <c>TileMapLayer</c>: the GPU draws one textured quad. Held as the
///     <see cref="_background"/> child.</item>
///   <item><b>Layer 2 — the logical grid.</b> A pure-C# <see cref="IsoGrid"/>
///     (<c>Dictionary&lt;TileCoordinate, IsoCell&gt;</c>), zero visual,
///     authoritative. The <see cref="Grid"/> property exposes it; the
///     authoritative owner is <c>GameState</c>, which holds the board's
///     grid by reference once the district loads. Until then the board
///     carries a placeholder rectangle so the scene is runnable alone.</item>
///   <item><b>The projection.</b> One <see cref="IsoProjection"/>, the
///     single source of truth for cell ↔ pixel conversion.</item>
/// </list>
/// It is <b>not</b> a renderer. There is no per-cell draw loop. Layer-3
/// entities (character pawns, building sprites) attach as children of the
/// <see cref="_occupants"/> node — see <see cref="AddOccupant"/>. Their
/// per-frame logic, Y-sorting, panning, and input wiring are owned by
/// the entities themselves, not by this socle.
/// </para>
///
/// <para>
/// <b>Layer-3 occupants (J3c-1).</b> The board hosts entity nodes (a
/// character pawn, a building sprite) on a dedicated <c>Occupants</c>
/// child created lazily by <see cref="AddOccupant"/>. That child has
/// <c>YSortEnabled</c> so an occupant lower on screen draws over one
/// higher up — the standard iso depth sort, free, no per-cell draw loop.
/// The maquette uses this for POIs (J6); the J3c-1bis desk uses it for
/// the Company pawns — see the "desk pawns ride inside the desk
/// SubViewport" paragraph below.
/// </para>
///
/// <para>
/// <b>The engine seam.</b> The logic layer (<see cref="IsoGrid"/> /
/// <see cref="IsoProjection"/>) is Godot-free: it speaks
/// <see cref="TileCoordinate"/> and <see cref="System.Numerics.Vector2"/>
/// so it stays cherry-pickable into the xUnit host. This node is the only
/// place that converts to / from <see cref="Vector2I"/> /
/// <see cref="Vector2"/> — see <see cref="CellToPixel"/> and
/// <see cref="PixelToCell"/>. Same seam discipline as
/// <c>FogTileGridLogic</c> ↔ its renderer.
/// </para>
///
/// <para>
/// <b>CanvasLayer discipline (trap #1).</b> The background
/// <see cref="Sprite2D"/> lives directly under this <see cref="Node2D"/>,
/// never under a <see cref="CanvasLayer"/>. A <see cref="CanvasLayer"/>
/// has its own canvas transform that ignores the world <c>Camera2D</c> —
/// putting the floor there would freeze it to the screen while entities
/// pan. The HUD (a future sub-milestone) is the only legitimate
/// <see cref="CanvasLayer"/> citizen.
/// </para>
///
/// <para>
/// <b>Sprite anchoring (trap #8).</b> The background sprite is set to
/// <c>Centered = false</c> in code so its top-left sits at the node
/// origin. The projection's <c>AnchorPixel</c> is then a single documented
/// value, never an implicit "where did the inspector leave it". A future
/// district sidecar supplies the real <c>AnchorPixel</c>; the MVP
/// placeholder centres cell (0,0) at a known offset.
/// </para>
///
/// <para>
/// <b>Placeholder grid draw (J3a).</b> Until Mira's district floor bitmap
/// lands, the board has nothing visible to pan or clip-test against. With
/// <see cref="DrawPlaceholderGrid"/> enabled, <see cref="_Draw"/> strokes
/// the iso diamond outline of every grid cell — a developer-only visual
/// placeholder, off by default, on for the J3a maquette and the J3c-1
/// desk so the pan, the clipping, and the desk grid are observable. It is
/// <i>not</i> a renderer and <i>not</i> the per-cell draw loop the hybrid
/// forbids: it is one debug <see cref="_Draw"/> pass over the logical
/// grid, dropped the moment a real floor bitmap exists. With
/// <see cref="DrawPlaceholderFloor"/> the same pass first fills the
/// desk floor with a flat table-surface colour — the desk's placeholder
/// floor (see that property's doc for why it is here and not a UI Panel).
/// </para>
///
/// <para>
/// <b>Desk floor + grid both fill the whole viewport (J3c-1bis F6 fix,
/// 2026-05-21, sixth round — the instrumented diagnostic round).</b> The
/// desk must read as ONE homogeneous brown triangle with iso grid
/// everywhere. Two earlier rounds corrected the <i>floor</i> (it fills the
/// whole viewport rect) and changed nothing on screen — because the
/// visible bug was the <i>grid</i>, not the floor. The desk floor and the
/// <c>GameScreen.tscn</c> <c>DeskBackground</c> panel are authored the
/// SAME brown, so a floor-coverage gap is invisible by construction. What
/// was visible was a dark band <i>without grid</i> and a lighter triangle
/// <i>with grid</i>: the grid only striped the 8×8 logical
/// <see cref="Grid"/> (a compact iso rhombus well inside the viewport),
/// while the floor filled the whole viewport — so the wedge between the
/// grid rhombus edge and the clip diagonal was bare floor, no grid, and it
/// read darker (the grid's faint fill + lines lighten the rhombus). The
/// fix: when <see cref="DrawPlaceholderFloor"/> is on, <see cref="_Draw"/>
/// stripes the grid over <i>every</i> cell whose diamond touches the floor
/// rect (<see cref="DrawGridOverRect"/>), a purely visual superset of the
/// 8×8 logical grid — exactly as the floor rect is a visual superset of
/// the grid diamond. The logical <see cref="Grid"/> stays 8×8 (the
/// occupiable cells, J3c-1 scope).
/// </para>
///
/// <para>
/// The fill is a hard-edged <see cref="CanvasItem.DrawRect"/>, not a
/// <c>DrawColoredPolygon</c>: the polygon triangulator feathers the
/// rasterised boundary, and over the desk SubViewport's <c>transparent_bg</c>
/// that feather left a half-alpha rim. The belt-and-braces companion is
/// that <c>GameScreen</c>'s <c>DeskBackground</c> panel is authored the
/// <i>same</i> brown as <see cref="FloorFillColor"/>.
/// </para>
///
/// <para>
/// <b>[DESK-DIAG] instrumentation.</b> When <see cref="DrawPlaceholderFloor"/>
/// is on, every <see cref="_Draw"/> pass prints a <c>[DESK-DIAG]</c> block:
/// viewport render size, camera position, the floor rect (corners + source),
/// the striped grid cell count and pixel bounds, a <c>GRID-COVERS-FLOOR</c>
/// verdict, and the real RGBA of the floor / grid colours. The point: the
/// logs say without ambiguity which element produces a "dark band" and
/// where coverage stops. Kept in the code on purpose — if a fix regresses,
/// the next F6 Output answers it rather than another blind round-trip.
/// </para>
///
/// <para>
/// <b>Preflight (trap #2).</b> <see cref="_Ready"/> prints a fixed block
/// of diagnostics — background size, grid cell count, projection tile
/// size, the round-trip self-check of a known cell. A rendering bug in
/// this socle is then visible in the Godot Output on the first run, not
/// after a debugging session.
/// </para>
/// </summary>
public partial class IsoBoard : Node2D
{
    /// <summary>
    /// res:// path of the district floor bitmap. Exported so the maquette
    /// and the desk can each point this same scene at a different image.
    /// Loaded through <see cref="AssetLoader.LoadTextureWithUserOverride"/>
    /// — never <c>ResourceLoader.Load</c> directly (trap #13). Empty until
    /// Mira's district asset lands (build brief item #2): an empty path is
    /// a tolerated placeholder state, logged, not a crash.
    /// </summary>
    [Export]
    public string BackgroundTexturePath { get; set; } = string.Empty;

    /// <summary>
    /// Projected cell-diamond width in pixels for this board's
    /// <see cref="IsoProjection"/>. Must be a positive multiple of 4 (the
    /// 2:1 ratio derives an even height). Exported so the maquette (small
    /// cells) and the desk (large cells) tune their own iso scale without
    /// a code change.
    /// </summary>
    [Export]
    public int TileWidthPx { get; set; } = 128;

    /// <summary>
    /// Placeholder grid width in cells, used by
    /// <see cref="IsoGrid.BuildRectangle"/> until a real district polygon
    /// sidecar is wired (roadmap J2 / Mira). Not the final district shape.
    /// </summary>
    [Export]
    public int PlaceholderGridWidth { get; set; } = 8;

    /// <summary>
    /// Placeholder grid height in cells. See
    /// <see cref="PlaceholderGridWidth"/>.
    /// </summary>
    [Export]
    public int PlaceholderGridHeight { get; set; } = 8;

    /// <summary>
    /// When true, <see cref="_Draw"/> strokes the iso diamond of every
    /// grid cell — a developer-only placeholder so the J3a maquette and
    /// J3c-1 desk are visible (pan + clip + desk grid observable) before
    /// Mira's floor bitmap lands. Off by default; the J3a/J3c-1
    /// <c>GameScreen.tscn</c> turns it on for its board instances. Drop
    /// it once a real floor bitmap exists.
    /// </summary>
    [Export]
    public bool DrawPlaceholderGrid { get; set; }

    /// <summary>
    /// When true, <see cref="_Draw"/> fills the desk floor with a flat
    /// warm-earth "table surface" colour <i>before</i> the per-cell grid
    /// strokes, and (J3c-1bis F6 sixth round) stripes the grid over the
    /// whole floor rect rather than only the 8×8 logical grid.
    ///
    /// <para>
    /// <b>Which rect is filled.</b> Always an axis-aligned rect covering
    /// the <i>whole</i> board viewport — never the compact grid-bounding
    /// diamond. If <c>GameScreen</c> has called
    /// <see cref="SetDeskFloorFillRect"/> the fill uses that explicit
    /// rect; otherwise the board derives its own viewport-covering rect
    /// (<see cref="ViewportFloorFillRect"/>). The desk floor is therefore
    /// a homogeneous full-viewport surface in both cases.
    /// </para>
    ///
    /// <para>
    /// <b>Why this is a board <see cref="_Draw"/> and not a UI Panel.</b>
    /// The J3c-1 desk originally carried its brown surface as a
    /// <c>Panel</c> child of the desk's <c>SubViewportContainer</c> — an
    /// <i>opaque UI overlay</i> that sat ON TOP of the SubViewport texture
    /// and hid the iso grid. The desk surface belongs <i>in the iso world</i>,
    /// drawn below the grid in the same render pass, not as a screen-space
    /// Panel. Off by default; the desk board turns it on. Dropped once a
    /// real desk-floor bitmap exists, exactly like
    /// <see cref="DrawPlaceholderGrid"/>.
    /// </para>
    /// </summary>
    [Export]
    public bool DrawPlaceholderFloor { get; set; }

    /// <summary>
    /// The layer-2 logical grid for this board. Authoritative game data;
    /// the authoritative <i>owner</i> is <c>GameState</c>, which adopts
    /// this reference when a district loads. Null until <see cref="_Ready"/>
    /// has run.
    /// </summary>
    public IsoGrid? Grid { get; private set; }

    /// <summary>
    /// The cell ↔ pixel projection for this board. Single source of truth;
    /// layer-3 placement and analytic picking both go through it. Null
    /// until <see cref="_Ready"/> has run.
    /// </summary>
    public IsoProjection? Projection { get; private set; }

    private Sprite2D _background = null!;

    /// <summary>
    /// Lazily-created Y-sorted parent for layer-3 occupant nodes (the J6
    /// maquette POIs and the J3c-1 desk Company pawns). Created on the
    /// first <see cref="AddOccupant"/> call so a board with no occupants
    /// does not carry an empty node. Y-sort makes an occupant lower on
    /// screen draw over one higher up — the standard iso depth sort.
    /// </summary>
    private Node2D? _occupants;

    /// <summary>
    /// The explicit desk-floor fill rect, in this board's local pixel
    /// space, set by <c>GameScreen</c> via <see cref="SetDeskFloorFillRect"/>
    /// (J3c-1bis F6 fix). Null on the maquette board and before the desk
    /// is configured — in which case <see cref="_Draw"/> falls back to the
    /// board's own viewport-covering rect (<see cref="ViewportFloorFillRect"/>),
    /// never to a grid-bounding diamond. A struct-typed nullable so a
    /// missing rect is an explicit "not pushed yet" rather than a
    /// zero-sized rect that would draw nothing.
    /// </summary>
    private DeskFloorRectLogic.FloorRect? _deskFloorFillRect;

    // Placeholder-grid stroke colours — warm earth, Wayfinders visual DNA
    // (terracotta / parchment / umber). Developer placeholder only.
    private static readonly Color GridLineColor = new(0.62f, 0.40f, 0.27f, 0.85f);
    private static readonly Color GridFillColor = new(0.91f, 0.85f, 0.72f, 0.10f);

    /// <summary>
    /// Placeholder desk-floor fill — a flat warm umber-brown table surface,
    /// drawn IN the iso world (below the grid) instead of as an opaque UI
    /// Panel.
    ///
    /// <para>
    /// <b>This must stay in sync with <c>GameScreen.tscn</c>'s
    /// <c>DeskBackground</c> panel colour.</b> The desk SubViewport is
    /// <c>transparent_bg</c>; the <c>DeskBackground</c> Panel sits behind
    /// the <c>DeskTextureRect</c>. If the panel were a different brown,
    /// any sub-pixel gap between this floor fill and the screen edge would
    /// expose a second colour and read as a demarcation. Authored equal,
    /// there is no second colour — the desk is one homogeneous brown
    /// whatever the floor rect rounds to. The J3c-1bis "desk = one uniform
    /// triangle" contract depends on these two values being identical.
    /// </para>
    /// </summary>
    public static readonly Color FloorFillColor = new(0.27f, 0.19f, 0.12f, 1f);

    public override void _Ready()
    {
        _background = GetNode<Sprite2D>("Background");
        // Trap #8: anchor the floor sprite top-left at the node origin so
        // the projection AnchorPixel is one documented value.
        _background.Centered = false;

        LoadBackground();

        // MVP placeholder grid + projection. When the district sidecar is
        // wired, BuildRectangle is replaced by a polygon-driven builder and
        // AnchorPixel comes from the sidecar — the property shapes here do
        // not change.
        Grid = IsoGrid.BuildRectangle(PlaceholderGridWidth, PlaceholderGridHeight);
        Projection = IsoProjection.Iso2To1(TileWidthPx, ComputeAnchorPixel());

        if (DrawPlaceholderGrid || DrawPlaceholderFloor)
        {
            QueueRedraw();
        }

        Preflight();
    }

    /// <summary>
    /// Engine-seam helper: project a grid cell to the Godot pixel where a
    /// layer-3 entity node would be placed. The only place
    /// <see cref="IsoProjection"/>'s <see cref="System.Numerics.Vector2"/>
    /// result is converted to <see cref="Vector2"/>. Throws if the board
    /// has not run <see cref="_Ready"/> yet.
    /// </summary>
    public Vector2 CellToPixel(Vector2I cell)
    {
        var sys = Projection!.CellToWorld(new TileCoordinate(cell.X, cell.Y));
        return new Vector2(sys.X, sys.Y);
    }

    /// <summary>
    /// Engine-seam helper: which grid cell a Godot pixel falls in. Inverse
    /// of <see cref="CellToPixel"/>. The only place a <see cref="Vector2"/>
    /// is converted into <see cref="IsoProjection"/>'s
    /// <see cref="System.Numerics.Vector2"/> input. Throws if the board has
    /// not run <see cref="_Ready"/> yet.
    /// </summary>
    public Vector2I PixelToCell(Vector2 pixel)
    {
        var coord = Projection!.WorldToCell(new SysVec2(pixel.X, pixel.Y));
        return new Vector2I(coord.Col, coord.Row);
    }

    /// <summary>
    /// J3c-1bis F6 fix: hand the board an explicit desk-floor fill rect, in
    /// this board's local pixel space, so <see cref="_Draw"/> fills the
    /// whole desk viewport from a camera-derived rect rather than from the
    /// board's own viewport rect. <c>GameScreen.ConfigureDesk</c> computes
    /// the rect with <see cref="DeskFloorRectLogic"/> from the immobile
    /// desk camera and the viewport size, then calls this. Triggers a
    /// redraw so the new fill is picked up even though <see cref="_Ready"/>
    /// already ran one draw pass.
    ///
    /// <para>
    /// This is an <i>optional refinement</i>, not a load-bearing wire: if
    /// it is never called the board still fills the whole viewport via
    /// <see cref="ViewportFloorFillRect"/>. The explicit rect only lets the
    /// fill be derived from the parked camera (so the rect is exact even if
    /// the camera is off-centre); the homogeneous-floor guarantee does not
    /// depend on it.
    /// </para>
    /// </summary>
    /// <param name="rect">
    /// The viewport-covering floor rect, from
    /// <see cref="DeskFloorRectLogic.Compute"/>.
    /// </param>
    public void SetDeskFloorFillRect(DeskFloorRectLogic.FloorRect rect)
    {
        _deskFloorFillRect = rect;
        if (IsInsideTree() && DrawPlaceholderFloor)
        {
            QueueRedraw();
        }
    }

    /// <summary>
    /// Attach a layer-3 entity node (a POI sprite, a desk Company pawn) to
    /// this board. The occupant becomes a child of the lazily-created
    /// Y-sorted <c>Occupants</c> node, so it depth-sorts against the other
    /// occupants for free. The caller positions the occupant — typically
    /// via <see cref="CellToPixel"/> for the target cell.
    ///
    /// <para>
    /// The socle parents the node; it does <b>not</b> own the occupant's
    /// state. Which mission a POI belongs to, which Company member a desk
    /// pawn is — that is authoritative <c>GameState</c> data (NPC-autonomy
    /// lock 2026-05-09) — the occupant node is a view.
    /// </para>
    ///
    /// <para>
    /// <b>Desk pawns ride inside the desk SubViewport (J3c-1bis,
    /// 2026-05-21, Rune).</b> An earlier J3c-1bis attempt moved the desk
    /// pawns OUT of the desk's render world onto a separate screen-space
    /// <c>Node2D</c>, to dodge the triangular clip shader slicing them.
    /// That forced a hand-written desk-local → screen reprojection that
    /// broke on every iteration. The reprojection is now gone: the desk
    /// pawns are plain occupants on this node, in the desk board's own
    /// space, under the desk's own <c>Camera2D</c> — the same coordinate
    /// frame as the floor they stand on, so their placement is the
    /// <see cref="CellToPixel"/> of their slot cell and nothing else. The
    /// clip shader still runs on the desk <c>TextureRect</c>, but it cuts
    /// the maquette-side wedge only; the parked desk camera frames the
    /// formation well inside the kept triangle, so no pawn is near the
    /// clip diagonal and none is sliced.
    /// </para>
    /// </summary>
    /// <param name="occupant">The entity node to attach. Not null.</param>
    public void AddOccupant(Node2D occupant)
    {
        System.ArgumentNullException.ThrowIfNull(occupant);

        if (_occupants is null)
        {
            _occupants = new Node2D
            {
                Name = "Occupants",
                YSortEnabled = true,
            };
            AddChild(_occupants);
        }
        _occupants.AddChild(occupant);
    }

    /// <summary>
    /// The layer-1 floor bitmap size in pixels, or <see cref="Vector2.Zero"/>
    /// when no background texture is set (the tolerated placeholder state
    /// before Mira's district asset lands). The board owns its texture, so
    /// the board exposes its size — callers that need the maquette content
    /// extent (e.g. <c>GameScreen</c>'s pan clamp) ask the board rather
    /// than reaching into the <see cref="Sprite2D"/> child. Safe to call
    /// only after <see cref="_Ready"/>.
    /// </summary>
    public Vector2 GetBackgroundTextureSizeOrZero()
        => _background.Texture?.GetSize() ?? Vector2.Zero;

    /// <summary>
    /// The board's own viewport-covering floor rect, in this board's local
    /// pixel space — the fallback the desk floor fills when
    /// <c>GameScreen</c> has not pushed an explicit
    /// <see cref="SetDeskFloorFillRect"/>.
    ///
    /// <para>
    /// <b>Why this exists.</b> Earlier the fallback was the grid-bounding
    /// iso diamond — a compact rhombus that left the viewport corners
    /// unpainted, so the dark <c>DeskBackground</c> bled through and the
    /// desk read as two brown zones. The floor must always cover the whole
    /// clipped viewport; the fallback must therefore be a full-viewport
    /// rect, not a diamond. This composes the same maths as
    /// <see cref="DeskFloorRectLogic.Compute"/> — the visible world rect
    /// is <c>cameraCentre ± viewportSize/2</c> grown by the safety slack —
    /// from the board's <i>own</i> active camera and SubViewport, so it is
    /// correct even before any external push.
    /// </para>
    ///
    /// <para>
    /// Returns null if the board is not inside the tree, has no viewport,
    /// or has no active <see cref="Camera2D"/> — in which case the floor
    /// is simply not drawn for that pass (it cannot place a rect without a
    /// camera frame). Safe to call only after <see cref="_Ready"/>.
    /// </para>
    /// </summary>
    public DeskFloorRectLogic.FloorRect? ViewportFloorFillRect()
    {
        if (!IsInsideTree())
        {
            return null;
        }

        var viewport = GetViewport();
        var camera = viewport?.GetCamera2D();
        if (viewport is null || camera is null)
        {
            return null;
        }

        // Visible size in world units = the SubViewport render size scaled
        // by the camera zoom (Position is the world point at the viewport
        // centre; the visible rect is cameraCentre ± visibleSize/2). Zoom
        // is (1,1) for the static desk, but composing it keeps this correct
        // if a zoom is ever authored.
        var renderSize = viewport.GetVisibleRect().Size;
        var zoom = camera.Zoom;
        if (zoom.X <= 0f || zoom.Y <= 0f || renderSize.X <= 0f || renderSize.Y <= 0f)
        {
            return null;
        }
        var visibleSize = new SysVec2(renderSize.X / zoom.X, renderSize.Y / zoom.Y);
        var cameraCentre = new SysVec2(camera.Position.X, camera.Position.Y);
        return DeskFloorRectLogic.Compute(cameraCentre, visibleSize);
    }

    /// <summary>
    /// The four apexes (top, right, bottom, left) of the iso diamond that
    /// bounds the whole placeholder grid, in this board's local pixel
    /// space. Composed purely from <see cref="IsoProjection.CellToWorld"/>
    /// of the four corner cells offset by a half-diamond — no new iso
    /// maths, just composition of the already-pinned projection.
    ///
    /// <para>
    /// <b>No longer used by the desk floor fill.</b> The desk floor now
    /// always fills a full-viewport rect, never this diamond — the diamond
    /// left the viewport corners unpainted (the F6 two-brown-zones bug).
    /// Kept as a public geometry helper for callers that genuinely need the
    /// grid's iso bounding rhombus (a future pan-clamp on a diamond-shaped
    /// district). Safe to call only after <see cref="_Ready"/>.
    /// </para>
    /// </summary>
    public Vector2[] GridBoundingDiamond()
    {
        var proj = Projection!;
        float halfH = proj.TileHeight * 0.5f;
        float halfW = proj.TileWidth * 0.5f;
        int w = PlaceholderGridWidth;
        int h = PlaceholderGridHeight;

        // Corner cell CENTRES. The grid is the rectangle (0,0)..(w-1,h-1);
        // its iso projection is a diamond whose four apexes sit a half-
        // diamond beyond the four corner-cell centres.
        var topCellCentre = CellToPixel(new Vector2I(0, 0));
        var rightCellCentre = CellToPixel(new Vector2I(w - 1, 0));
        var bottomCellCentre = CellToPixel(new Vector2I(w - 1, h - 1));
        var leftCellCentre = CellToPixel(new Vector2I(0, h - 1));

        return new[]
        {
            topCellCentre + new Vector2(0f, -halfH),    // top apex
            rightCellCentre + new Vector2(halfW, 0f),   // right apex
            bottomCellCentre + new Vector2(0f, halfH),  // bottom apex
            leftCellCentre + new Vector2(-halfW, 0f),   // left apex
        };
    }

    /// <summary>
    /// Developer placeholder draw (see <see cref="DrawPlaceholderGrid"/>,
    /// <see cref="DrawPlaceholderFloor"/>). With the floor flag, first
    /// fills the desk floor with the flat table-surface colour over the
    /// <i>whole</i> board viewport — the explicit camera-derived rect if
    /// <c>GameScreen</c> set one, otherwise the board's own viewport rect.
    /// With the grid flag, then strokes the iso diamond outline of every
    /// cell. On a placeholder-floor board (the desk) the grid is striped
    /// over the <i>whole floor rect</i> (<see cref="DrawGridOverRect"/>),
    /// not only the 8×8 logical grid — the J3c-1bis F6 sixth-round fix for
    /// the "dark band" (bare floor with no grid between the grid rhombus
    /// and the clip diagonal). One pass at <see cref="_Ready"/> (plus one
    /// on <see cref="SetDeskFloorFillRect"/>), not per-frame.
    /// </summary>
    public override void _Draw()
    {
        if (Grid is null || Projection is null)
        {
            return;
        }

        // Placeholder desk floor — the warm table surface, drawn FIRST so
        // the grid strokes sit on top of it. This is the in-world
        // replacement for the old opaque UI Panel that hid the desk
        // SubViewport content.
        //
        // J3c-1bis F6 fix (2026-05-21, Rune, second round): the floor is
        // ALWAYS a full-viewport axis-aligned rect — never the grid-bounding
        // diamond. The rect comes from GameScreen's explicit camera-derived
        // push if present, otherwise from the board's own viewport
        // (ViewportFloorFillRect). The fill is a hard-edged DrawRect.
        DeskFloorRectLogic.FloorRect? floorRect = null;
        if (DrawPlaceholderFloor)
        {
            floorRect = _deskFloorFillRect ?? ViewportFloorFillRect();
            if (floorRect is { } rect)
            {
                DrawRect(
                    new Rect2(
                        rect.TopLeft.X, rect.TopLeft.Y,
                        rect.Size.X, rect.Size.Y),
                    FloorFillColor,
                    filled: true);
            }
        }

        if (!DrawPlaceholderGrid)
        {
            DiagDraw(floorRect, gridCellsDrawn: 0, Vector2.Zero, Vector2.Zero);
            return;
        }

        // J3c-1bis F6 DIAGNOSTIC FIX (2026-05-21, Rune, sixth round).
        //
        // The previous two fixes corrected the FLOOR (it now fills the whole
        // viewport rect). They changed nothing on screen because the visible
        // bug is the GRID, not the floor. The desk floor and the desk
        // DeskBackground panel are authored the SAME brown (FloorFillColor ==
        // DeskBackground bg_color = 0.27,0.19,0.12) — so a floor-coverage gap
        // is by construction invisible. What Didier sees is:
        //   - a "dark brown band WITHOUT grid"  : floor painted, grid absent
        //   - a "lighter brown triangle WITH grid" : floor + iso grid stacked
        // The grid only striped the 8x8 LOGICAL Grid (a compact iso rhombus
        // well inside the viewport). The floor fills the whole viewport. The
        // wedge between the grid rhombus edge and the clip diagonal is bare
        // floor with no grid on it — THE DARK BAND. The grid's faint
        // 0.10-alpha fill plus its 0.85-alpha lines is what makes the rhombus
        // read as a "lighter" triangle; bare floor reads darker.
        //
        // The fix: when DrawPlaceholderFloor is on (the desk), stripe the
        // grid over EVERY cell whose diamond touches the floor rect, not
        // just the 8x8 logical Grid. The logical Grid stays 8x8 (the
        // occupiable cells, J3c-1 scope) — this is a purely visual superset,
        // exactly as the floor rect is a visual superset of the grid diamond.
        float halfW = Projection.TileWidth * 0.5f;
        float halfH = Projection.TileHeight * 0.5f;

        int cellsDrawn;
        Vector2 gridMin = Vector2.Zero, gridMax = Vector2.Zero;
        if (DrawPlaceholderFloor && floorRect is { } fr)
        {
            cellsDrawn = DrawGridOverRect(fr, halfW, halfH, out gridMin, out gridMax);
        }
        else
        {
            cellsDrawn = 0;
            bool first = true;
            foreach (var cell in Grid.AllCells())
            {
                var centre = CellToPixel(new Vector2I(cell.Coord.Col, cell.Coord.Row));
                DrawCellDiamond(centre, halfW, halfH);
                cellsDrawn++;
                AccumulateBounds(centre, halfW, halfH, ref first, ref gridMin, ref gridMax);
            }
        }

        DiagDraw(floorRect, cellsDrawn, gridMin, gridMax);
    }

    /// <summary>
    /// Stripe the placeholder iso grid over every cell whose diamond
    /// intersects <paramref name="rect"/> — the desk's visual grid superset
    /// (J3c-1bis F6 sixth-round fix). The logical <see cref="Grid"/> stays
    /// 8×8; this fills the whole floor rect so no painted-floor wedge is
    /// left without a grid (the "dark band" bug). Returns the cell count
    /// drawn and, via out params, the pixel-space bounds of the striped grid.
    /// </summary>
    private int DrawGridOverRect(
        DeskFloorRectLogic.FloorRect rect, float halfW, float halfH,
        out Vector2 gridMin, out Vector2 gridMax)
    {
        // The four rect corners map to four cells; the covering cell range
        // is their integer bounding box, grown one ring so edge diamonds
        // that only partially overlap the rect are still included.
        var c0 = PixelToCell(new Vector2(rect.TopLeft.X, rect.TopLeft.Y));
        var c1 = PixelToCell(new Vector2(
            rect.TopLeft.X + rect.Size.X, rect.TopLeft.Y));
        var c2 = PixelToCell(new Vector2(
            rect.TopLeft.X, rect.TopLeft.Y + rect.Size.Y));
        var c3 = PixelToCell(new Vector2(
            rect.TopLeft.X + rect.Size.X, rect.TopLeft.Y + rect.Size.Y));

        int minCol = Mathf.Min(Mathf.Min(c0.X, c1.X), Mathf.Min(c2.X, c3.X)) - 1;
        int maxCol = Mathf.Max(Mathf.Max(c0.X, c1.X), Mathf.Max(c2.X, c3.X)) + 1;
        int minRow = Mathf.Min(Mathf.Min(c0.Y, c1.Y), Mathf.Min(c2.Y, c3.Y)) - 1;
        int maxRow = Mathf.Max(Mathf.Max(c0.Y, c1.Y), Mathf.Max(c2.Y, c3.Y)) + 1;

        float left = rect.TopLeft.X;
        float top = rect.TopLeft.Y;
        float right = rect.TopLeft.X + rect.Size.X;
        float bottom = rect.TopLeft.Y + rect.Size.Y;

        int drawn = 0;
        bool first = true;
        gridMin = Vector2.Zero;
        gridMax = Vector2.Zero;
        for (int col = minCol; col <= maxCol; col++)
        {
            for (int row = minRow; row <= maxRow; row++)
            {
                var centre = CellToPixel(new Vector2I(col, row));
                // Cull cells whose diamond cannot touch the rect at all.
                if (centre.X + halfW < left || centre.X - halfW > right
                    || centre.Y + halfH < top || centre.Y - halfH > bottom)
                {
                    continue;
                }
                DrawCellDiamond(centre, halfW, halfH);
                drawn++;
                AccumulateBounds(centre, halfW, halfH, ref first, ref gridMin, ref gridMax);
            }
        }
        return drawn;
    }

    /// <summary>Stroke one iso cell diamond around <paramref name="centre"/>.</summary>
    private void DrawCellDiamond(Vector2 centre, float halfW, float halfH)
    {
        var diamond = new[]
        {
            centre + new Vector2(0f, -halfH),     // top
            centre + new Vector2(halfW, 0f),      // right
            centre + new Vector2(0f, halfH),      // bottom
            centre + new Vector2(-halfW, 0f),     // left
        };
        DrawColoredPolygon(diamond, GridFillColor);
        DrawPolyline(
            new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] },
            GridLineColor, 2f, antialiased: true);
    }

    /// <summary>
    /// Grow the running pixel-space bounding box by one cell diamond.
    /// </summary>
    private static void AccumulateBounds(
        Vector2 centre, float halfW, float halfH,
        ref bool first, ref Vector2 min, ref Vector2 max)
    {
        var lo = new Vector2(centre.X - halfW, centre.Y - halfH);
        var hi = new Vector2(centre.X + halfW, centre.Y + halfH);
        if (first)
        {
            min = lo;
            max = hi;
            first = false;
            return;
        }
        min = new Vector2(Mathf.Min(min.X, lo.X), Mathf.Min(min.Y, lo.Y));
        max = new Vector2(Mathf.Max(max.X, hi.X), Mathf.Max(max.Y, hi.Y));
    }

    /// <summary>
    /// J3c-1bis instrumented diagnostic (prefix <c>[DESK-DIAG]</c>). Prints,
    /// at every <see cref="_Draw"/> pass of a board that fills a placeholder
    /// floor (the desk), the exact rect the floor filled, the grid cell
    /// count striped and its pixel bounds, a <c>GRID-COVERS-FLOOR</c>
    /// verdict, and the two real RGBA colours. The point: the logs reveal
    /// without ambiguity which element produces the "dark band" and where
    /// grid coverage stops versus where the floor stops. Kept in the code
    /// on purpose — if a fix ever regresses, the next F6 Output gives the
    /// answer rather than another blind round-trip.
    /// </summary>
    private void DiagDraw(
        DeskFloorRectLogic.FloorRect? floorRect, int gridCellsDrawn,
        Vector2 gridMin, Vector2 gridMax)
    {
        if (!DrawPlaceholderFloor)
        {
            return;
        }

        var vp = GetViewport();
        var cam = vp?.GetCamera2D();
        GD.Print($"[DESK-DIAG] IsoBoard._Draw '{Name}': " +
                 $"viewportRenderSize={(vp?.GetVisibleRect().Size.ToString() ?? "(none)")} " +
                 $"camera2DPos={(cam?.Position.ToString() ?? "(none)")}");
        if (floorRect is { } fr)
        {
            GD.Print($"[DESK-DIAG]   floor rect: topLeft=({fr.TopLeft.X},{fr.TopLeft.Y}) " +
                     $"size=({fr.Size.X},{fr.Size.Y}) " +
                     $"bottomRight=({fr.TopLeft.X + fr.Size.X},{fr.TopLeft.Y + fr.Size.Y}) " +
                     $"source={(_deskFloorFillRect is null ? "ViewportFloorFillRect-fallback" : "GameScreen-push")}");
            GD.Print($"[DESK-DIAG]   floor 4 corners: " +
                     $"TL=({fr.TopLeft.X},{fr.TopLeft.Y}) " +
                     $"TR=({fr.TopLeft.X + fr.Size.X},{fr.TopLeft.Y}) " +
                     $"BR=({fr.TopLeft.X + fr.Size.X},{fr.TopLeft.Y + fr.Size.Y}) " +
                     $"BL=({fr.TopLeft.X},{fr.TopLeft.Y + fr.Size.Y})");
        }
        else
        {
            GD.Print("[DESK-DIAG]   floor rect: NULL — floor NOT drawn this pass " +
                     "(no pushed rect, and ViewportFloorFillRect could not " +
                     "resolve a camera). THIS WOULD BE A DARK ZONE.");
        }
        GD.Print($"[DESK-DIAG]   iso grid: cellsDrawn={gridCellsDrawn} " +
                 $"pixelBounds min=({gridMin.X},{gridMin.Y}) max=({gridMax.X},{gridMax.Y})");
        if (floorRect is { } f2 && gridCellsDrawn > 0)
        {
            // The decisive comparison: does the striped grid actually reach
            // the floor rect edges? A grid bound well inside the floor rect
            // IS the dark-band bug — floor with no grid between the two.
            bool gridCoversFloor =
                gridMin.X <= f2.TopLeft.X && gridMin.Y <= f2.TopLeft.Y
                && gridMax.X >= f2.TopLeft.X + f2.Size.X
                && gridMax.Y >= f2.TopLeft.Y + f2.Size.Y;
            GD.Print($"[DESK-DIAG]   GRID-COVERS-FLOOR={gridCoversFloor} " +
                     $"(false => bare-floor wedge with no grid = the dark band)");
        }
        GD.Print($"[DESK-DIAG]   colours: FloorFillColor(rgba)=" +
                 $"({FloorFillColor.R},{FloorFillColor.G},{FloorFillColor.B},{FloorFillColor.A}) " +
                 $"GridLineColor(rgba)=({GridLineColor.R},{GridLineColor.G},{GridLineColor.B},{GridLineColor.A}) " +
                 $"GridFillColor(rgba)=({GridFillColor.R},{GridFillColor.G},{GridFillColor.B},{GridFillColor.A})");
    }

    /// <summary>
    /// Load and assign the layer-1 floor bitmap. An empty path is a
    /// tolerated placeholder state (Mira asset not yet delivered) — logged,
    /// not fatal. A non-empty path that fails to resolve is a real error.
    /// </summary>
    private void LoadBackground()
    {
        if (string.IsNullOrEmpty(BackgroundTexturePath))
        {
            GD.Print("[IsoBoard] no BackgroundTexturePath set — running " +
                     "without a floor bitmap (placeholder state).");
            return;
        }

        var texture = AssetLoader.LoadTextureWithUserOverride(BackgroundTexturePath);
        if (texture is null)
        {
            GD.PushError($"[IsoBoard] failed to load background " +
                         $"'{BackgroundTexturePath}'.");
            return;
        }
        _background.Texture = texture;
    }

    /// <summary>
    /// MVP anchor: the pixel where the centre of cell (0,0) sits. For the
    /// placeholder rectangle, cell (0,0) is the top apex of the diamond
    /// block, so it sits half a grid-width in from the left of the floor.
    /// When a district sidecar is wired this whole method is replaced by a
    /// sidecar read (design doc §2).
    /// </summary>
    private SysVec2 ComputeAnchorPixel()
    {
        // Place (0,0) so the full diamond of PlaceholderGridWidth ×
        // PlaceholderGridHeight cells fits with x>=0: the left-most cell is
        // (0, height-1), which projects to localX = -(height-1)*halfW.
        float halfW = TileWidthPx * 0.5f;
        float xOffset = (PlaceholderGridHeight - 1) * halfW;
        return new SysVec2(xOffset, 0f);
    }

    /// <summary>
    /// Trap #2 preflight: a fixed diagnostics block printed once at
    /// <see cref="_Ready"/>. Includes a projection round-trip self-check
    /// on a known non-trivial cell — if cell ↔ pixel conversion ever
    /// drifts, the mismatch is in the Output log on the first run.
    /// </summary>
    private void Preflight()
    {
        var grid = Grid!;
        var proj = Projection!;

        GD.Print($"[IsoBoard] preflight: background size=" +
                 $"{_background.Texture?.GetSize().ToString() ?? "(none)"} " +
                 $"centered={_background.Centered}");
        GD.Print($"[IsoBoard] preflight: grid cells={grid.Count} " +
                 $"placeholder={PlaceholderGridWidth}x{PlaceholderGridHeight} " +
                 $"drawGrid={DrawPlaceholderGrid} drawFloor={DrawPlaceholderFloor}");
        GD.Print($"[IsoBoard] preflight: projection tile=" +
                 $"{proj.TileWidth}x{proj.TileHeight} anchor={proj.AnchorPixel}");

        var probeCell = new Vector2I(3, 2);
        var px = CellToPixel(probeCell);
        var roundTrip = PixelToCell(px);
        if (roundTrip == probeCell)
        {
            GD.Print($"[IsoBoard] preflight: projection round-trip OK " +
                     $"{probeCell} -> {px} -> {roundTrip}");
        }
        else
        {
            GD.PushError($"[IsoBoard] preflight FAIL: projection round-trip " +
                         $"{probeCell} -> {px} -> {roundTrip} (expected {probeCell})");
        }
    }
}
