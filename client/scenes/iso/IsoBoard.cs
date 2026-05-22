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
/// <c>Centered = false</c> in code so its top-left sits at the sprite's
/// own <c>Position</c>. The MVP maquette leaves that position at the node
/// origin (cell-0 anchored from there); the J3c desk instead snaps the
/// sprite's top-left onto the desk floor rect's top-left — see
/// <see cref="AlignBackgroundToFloorRect"/> — so Mira's wood-floor bitmap
/// registers pixel-exact on the same rect the flat aplat used to fill.
/// Either way the placement is one documented value, never an implicit
/// "where did the inspector leave it".
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
/// <b>Desk floor — flat aplat OR Mira's wood bitmap (J3c-3 wire-in,
/// 2026-05-22).</b> The placeholder aplat (<see cref="DrawPlaceholderFloor"/>'s
/// <see cref="CanvasItem.DrawRect"/> in <see cref="FloorFillColor"/>) was a
/// stand-in until Mira's desk-floor texture landed. It has now landed
/// (<c>wf_e1_desk_wood_floor.png</c>, authored at the exact desk floor-rect
/// size). When a <see cref="BackgroundTexturePath"/> is set on a
/// placeholder-floor board, <see cref="LoadBackground"/> loads it into the
/// layer-1 <see cref="_background"/> sprite, <see cref="AlignBackgroundToFloorRect"/>
/// snaps that sprite's top-left onto the floor rect's top-left, and
/// <see cref="_Draw"/> then SKIPS the flat aplat — the wood bitmap is the
/// floor, the aplat would only hide it. The aplat path stays in the code
/// as the tolerated no-texture placeholder state (an empty path) — exactly
/// the Mira ↔ Rune placeholder discipline: the day the asset arrives, the
/// code serves it with no structural change.
/// </para>
///
/// <para>
/// <b>Desk floor + grid both fill the whole viewport (J3c-1bis F6 fix,
/// 2026-05-21, sixth round — the instrumented diagnostic round).</b> The
/// desk must read as ONE homogeneous surface with iso grid everywhere. Two
/// earlier rounds corrected the <i>floor</i> (it fills the whole viewport
/// rect) and changed nothing on screen — because the visible bug was the
/// <i>grid</i>, not the floor. The fix: when <see cref="DrawPlaceholderGrid"/>
/// is on together with <see cref="DrawPlaceholderFloor"/>, <see cref="_Draw"/>
/// stripes the grid over <i>every</i> cell whose diamond touches the floor
/// rect (<see cref="DrawGridOverRect"/>), a purely visual superset of the
/// 8×8 logical grid. The logical <see cref="Grid"/> stays 8×8 (the
/// occupiable cells, J3c-1 scope).
/// </para>
///
/// <para>
/// <b>[DESK-DIAG] instrumentation.</b> When <see cref="DrawPlaceholderFloor"/>
/// is on, every <see cref="_Draw"/> pass prints a <c>[DESK-DIAG]</c> block:
/// viewport render size, camera position, the floor rect (corners + source),
/// the striped grid cell count and pixel bounds, a <c>GRID-COVERS-FLOOR</c>
/// verdict, whether the floor is the wood bitmap or the placeholder aplat,
/// and the real RGBA of the floor / grid colours. The point: the logs say
/// without ambiguity which element produces a "dark band" and where
/// coverage stops. Kept in the code on purpose.
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
    /// res:// path of the floor bitmap. Exported so the maquette and the
    /// desk can each point this same scene at a different image. Loaded
    /// through <see cref="AssetLoader.LoadTextureWithUserOverride"/> —
    /// never <c>ResourceLoader.Load</c> directly (trap #13). Empty is a
    /// tolerated placeholder state, logged, not a crash: the maquette runs
    /// without it until the district bitmap lands, and a placeholder-floor
    /// board falls back to the flat <see cref="FloorFillColor"/> aplat.
    /// On the J3c desk this points at Mira's <c>wf_e1_desk_wood_floor.png</c>.
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
    /// J3c-1 desk are visible (pan + clip + desk grid observable). Off by
    /// default; the J3a/J3c <c>GameScreen.tscn</c> turns it on. Since the
    /// J3c-3 wood-floor wire-in the grid is ATTENUATED to a faint hairline
    /// (see <see cref="GridLineColor"/>) so it reads as a developer overlay
    /// on the wood, not a paint layer over it — J3c-2 click placement still
    /// wants the cell lattice readable. Drop it once cell placement is
    /// self-evident from the wood and the pawns.
    /// </summary>
    [Export]
    public bool DrawPlaceholderGrid { get; set; }

    /// <summary>
    /// When true, the board treats itself as the desk floor: <see cref="_Draw"/>
    /// either fills the whole desk floor rect with the flat warm-earth
    /// <see cref="FloorFillColor"/> aplat (the no-texture placeholder
    /// state) OR — once a <see cref="BackgroundTexturePath"/> is wired —
    /// SKIPS that aplat because the layer-1 wood bitmap already covers the
    /// rect (J3c-3). With <see cref="DrawPlaceholderGrid"/> also on, the
    /// grid is then striped over the whole floor rect, not only the 8×8
    /// logical grid.
    ///
    /// <para>
    /// <b>Which rect.</b> Always an axis-aligned rect covering the
    /// <i>whole</i> board viewport — never the compact grid-bounding
    /// diamond. If <c>GameScreen</c> has called
    /// <see cref="SetDeskFloorFillRect"/> the fill / texture-alignment uses
    /// that explicit rect; otherwise the board derives its own
    /// viewport-covering rect (<see cref="ViewportFloorFillRect"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Why a board <see cref="_Draw"/> and not a UI Panel.</b> The desk
    /// surface belongs <i>in the iso world</i>, drawn / textured below the
    /// grid in the same render pass, not as a screen-space Panel that would
    /// sit ON TOP of the SubViewport texture and hide the iso grid. Off by
    /// default; the desk floor board turns it on.
    /// </para>
    /// </summary>
    [Export]
    public bool DrawPlaceholderFloor { get; set; }

    /// <summary>
    /// <b>TEMPORARY twin-corner desk-floor framing placeholder (Rune,
    /// 2026-05-22 — F6-readability fix). Off by default.</b>
    ///
    /// <para>
    /// When true together with <see cref="DrawPlaceholderFloor"/>,
    /// <see cref="_Draw"/> paints the desk floor aplat and stripes the iso
    /// grid <i>only inside the two bottom-corner desk wedges</i>
    /// (<see cref="DeskTrianglePlaceholderLogic"/>) instead of across the
    /// whole viewport rect. The screen centre is then left unpainted, so the
    /// layer-B maquette diamond shows through.
    /// </para>
    ///
    /// <para>
    /// <b>Why this exists.</b> The 4-layer rework removed
    /// <c>desk_triangle_clip.gdshader</c> — the GPU UV clip that used to cut
    /// the desk SubViewport render into two corner triangles. With the clip
    /// gone, the desk floor renders as a full opaque rectangle that hides the
    /// maquette: the F6 smoke became unreadable. Mira's Blender decor (layers
    /// A + D) will carry the desk-floor shape for real via its two alpha
    /// holes — but until it lands the F6 placeholder must stay interpretable.
    /// This flag is the gap-filler: a CPU draw-time framing, no shader, no
    /// material, no UV uniforms. It is <b>not</b> a return of the removed
    /// clip — it just decides which cells / which aplat to skip in
    /// <see cref="_Draw"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Delete-me marker.</b> When Mira's decor lands, drop this flag, the
    /// <see cref="_deskTriangles"/> field, <see cref="SetDeskTrianglePlaceholder"/>,
    /// and the whole <see cref="DeskTrianglePlaceholderLogic"/> helper +
    /// its tests. The desk floor then renders full-rect again, framed by the
    /// decor's alpha — exactly the shipped path.
    /// </para>
    /// </summary>
    [Export]
    public bool DeskTrianglePlaceholderClip { get; set; }

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
    /// never to a grid-bounding diamond.
    /// </summary>
    private DeskFloorRectLogic.FloorRect? _deskFloorFillRect;

    /// <summary>
    /// <b>TEMPORARY</b> — the two bottom-corner desk wedges the placeholder
    /// floor is clipped to, in this board's local pixel space, set by
    /// <c>GameScreen</c> via <see cref="SetDeskTrianglePlaceholder"/>. Null
    /// unless <see cref="DeskTrianglePlaceholderClip"/> is on. Deleted with
    /// the rest of the placeholder when Mira's Blender decor lands.
    /// </summary>
    private DeskTrianglePlaceholderLogic.DeskTriangles? _deskTriangles;

    /// <summary>
    /// True once a real floor bitmap is loaded into <see cref="_background"/>
    /// (<see cref="BackgroundTexturePath"/> was set and resolved). On a
    /// placeholder-floor board this flips the floor from the flat
    /// <see cref="FloorFillColor"/> aplat to Mira's wood bitmap: when true,
    /// <see cref="_Draw"/> skips the aplat <see cref="CanvasItem.DrawRect"/>
    /// because the layer-1 sprite already covers the rect (J3c-3).
    /// </summary>
    private bool _hasFloorBitmap;

    // Placeholder-grid stroke colours — warm earth, Wayfinders visual DNA
    // (terracotta / parchment / umber). Developer placeholder only.
    //
    // J3c-3 (Mira wood-floor wire-in, 2026-05-22): the desk floor is no
    // longer a flat brown aplat — it carries Mira's wf_e1_desk_wood_floor
    // bitmap. The debug grid is KEPT (J3c-2 click placement still needs the
    // cell lattice readable for development) but ATTENUATED, so it reads as
    // a faint developer overlay on the wood rather than a paint layer
    // competing with it. The fill alpha drops to 0 (the diamonds no longer
    // tint the wood) and the line alpha drops to a thin dark hairline. The
    // aplat-era values (0.85 / 0.10 alpha) were sized for a flat surface
    // with nothing else on it; over a real texture they muddy the wood.
    // Drop the whole grid (set DrawPlaceholderGrid = false on the desk
    // board) once J3c-2 cell placement is self-evident from the wood +
    // pawns and no longer needs the debug lattice.
    private static readonly Color GridLineColor = new(0.20f, 0.13f, 0.08f, 0.30f);
    private static readonly Color GridFillColor = new(0.91f, 0.85f, 0.72f, 0.0f);

    /// <summary>
    /// Placeholder desk-floor fill — a flat warm umber-brown table surface,
    /// drawn IN the iso world (below the grid) instead of as an opaque UI
    /// Panel. <b>This is now a fallback only</b>: it is drawn only on a
    /// placeholder-floor board that has no <see cref="BackgroundTexturePath"/>
    /// (the tolerated no-texture state). The J3c desk has Mira's wood
    /// bitmap, so this aplat is skipped there (J3c-3).
    ///
    /// <para>
    /// <b>Still kept in sync with <c>GameScreen.tscn</c>'s
    /// <c>DeskBackground</c> panel colour.</b> When the aplat is the
    /// fallback floor, any sub-pixel gap between it and the screen edge
    /// must not expose a second brown. Authored equal, there is no second
    /// colour. With the wood bitmap in place the bitmap covers the rect, so
    /// <c>DeskBackground</c> stops mattering for the floor — but it remains
    /// the correct neutral backstop the same colour family.
    /// </para>
    /// </summary>
    public static readonly Color FloorFillColor = new(0.27f, 0.19f, 0.12f, 1f);

    public override void _Ready()
    {
        _background = GetNode<Sprite2D>("Background");
        // Trap #8: anchor the floor sprite top-left at the sprite's own
        // Position so the placement is one documented value.
        _background.Centered = false;

        LoadBackground();

        // MVP placeholder grid + projection. When the district sidecar is
        // wired, BuildRectangle is replaced by a polygon-driven builder and
        // AnchorPixel comes from the sidecar — the property shapes here do
        // not change.
        Grid = IsoGrid.BuildRectangle(PlaceholderGridWidth, PlaceholderGridHeight);
        Projection = IsoProjection.Iso2To1(TileWidthPx, ComputeAnchorPixel());

        // J3c-3: on a placeholder-floor board carrying the wood bitmap,
        // register the sprite onto the floor rect. SetDeskFloorFillRect has
        // not necessarily run yet (GameScreen calls it after _Ready), so
        // align against the board's own viewport-derived rect now; the
        // later explicit push re-aligns it exactly.
        if (_hasFloorBitmap && DrawPlaceholderFloor)
        {
            if (ViewportFloorFillRect() is { } rect)
            {
                AlignBackgroundToFloorRect(rect);
            }
        }

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
    /// desk camera and the viewport size, then calls this.
    ///
    /// <para>
    /// J3c-3: when a floor bitmap is loaded this also re-registers the
    /// layer-1 wood sprite onto the new rect (<see cref="AlignBackgroundToFloorRect"/>),
    /// so the wood texture stays pixel-exact on the floor rect even though
    /// <see cref="_Ready"/> already aligned it once against the board's own
    /// viewport-derived rect.
    /// </para>
    ///
    /// <para>
    /// This is an <i>optional refinement</i>, not a load-bearing wire: if
    /// it is never called the board still fills the whole viewport via
    /// <see cref="ViewportFloorFillRect"/> and aligns the sprite there.
    /// </para>
    /// </summary>
    /// <param name="rect">
    /// The viewport-covering floor rect, from
    /// <see cref="DeskFloorRectLogic.Compute"/>.
    /// </param>
    public void SetDeskFloorFillRect(DeskFloorRectLogic.FloorRect rect)
    {
        _deskFloorFillRect = rect;
        if (_hasFloorBitmap && DrawPlaceholderFloor)
        {
            AlignBackgroundToFloorRect(rect);
        }
        if (IsInsideTree() && DrawPlaceholderFloor)
        {
            QueueRedraw();
        }
    }

    /// <summary>
    /// <b>TEMPORARY (Rune, 2026-05-22 F6-readability fix).</b> Hand the board
    /// the two bottom-corner desk wedges its placeholder floor + grid must be
    /// clipped to. Only meaningful while <see cref="DeskTrianglePlaceholderClip"/>
    /// is on; <c>GameScreen.ConfigureDesk</c> computes the wedges with
    /// <see cref="DeskTrianglePlaceholderLogic.Build"/> from the immobile desk
    /// camera + viewport size and calls this.
    ///
    /// <para>
    /// This is the gap-filler between the removal of
    /// <c>desk_triangle_clip.gdshader</c> and the arrival of Mira's Blender
    /// decor. When the decor lands, delete this method, the
    /// <see cref="_deskTriangles"/> field, the
    /// <see cref="DeskTrianglePlaceholderClip"/> flag, and the helper.
    /// </para>
    /// </summary>
    /// <param name="triangles">
    /// The two bottom-corner desk wedges, from
    /// <see cref="DeskTrianglePlaceholderLogic.Build"/>.
    /// </param>
    public void SetDeskTrianglePlaceholder(
        DeskTrianglePlaceholderLogic.DeskTriangles triangles)
    {
        _deskTriangles = triangles;
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
    /// when no background texture is set (the tolerated placeholder state).
    /// The board owns its texture, so the board exposes its size — callers
    /// that need the maquette content extent (e.g. <c>GameScreen</c>'s pan
    /// clamp) ask the board rather than reaching into the
    /// <see cref="Sprite2D"/> child. Safe to call only after
    /// <see cref="_Ready"/>.
    /// </summary>
    public Vector2 GetBackgroundTextureSizeOrZero()
        => _background.Texture?.GetSize() ?? Vector2.Zero;

    /// <summary>
    /// The board's own viewport-covering floor rect, in this board's local
    /// pixel space — the fallback the desk floor uses when <c>GameScreen</c>
    /// has not pushed an explicit <see cref="SetDeskFloorFillRect"/>.
    ///
    /// <para>
    /// Composes the same maths as <see cref="DeskFloorRectLogic.Compute"/>
    /// — the visible world rect is <c>cameraCentre ± viewportSize/2</c>
    /// grown by the safety slack — from the board's <i>own</i> active
    /// camera and SubViewport, so it is correct even before any external
    /// push. The wood bitmap is authored at exactly this rect's size, so
    /// the same rect both placed the old aplat and now places the bitmap.
    /// </para>
    ///
    /// <para>
    /// Returns null if the board is not inside the tree, has no viewport,
    /// or has no active <see cref="Camera2D"/>. Safe to call only after
    /// <see cref="_Ready"/>.
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
        // centre; the visible rect is cameraCentre ± visibleSize/2).
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
    /// of the four corner cells offset by a half-diamond.
    ///
    /// <para>
    /// <b>Not used by the desk floor.</b> The desk floor fills a
    /// full-viewport rect, never this diamond. Kept as a public geometry
    /// helper for callers that genuinely need the grid's iso bounding
    /// rhombus. Safe to call only after <see cref="_Ready"/>.
    /// </para>
    /// </summary>
    public Vector2[] GridBoundingDiamond()
    {
        var proj = Projection!;
        float halfH = proj.TileHeight * 0.5f;
        float halfW = proj.TileWidth * 0.5f;
        int w = PlaceholderGridWidth;
        int h = PlaceholderGridHeight;

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
    /// <see cref="DrawPlaceholderFloor"/>).
    ///
    /// <para>
    /// <b>Floor.</b> On a placeholder-floor board: if Mira's wood bitmap is
    /// loaded (<see cref="_hasFloorBitmap"/>) the flat <see cref="FloorFillColor"/>
    /// aplat is SKIPPED — the layer-1 <see cref="_background"/> sprite,
    /// snapped onto the floor rect by <see cref="AlignBackgroundToFloorRect"/>,
    /// IS the floor (J3c-3). Only when no bitmap is wired is the aplat
    /// <see cref="CanvasItem.DrawRect"/> drawn, as the tolerated no-texture
    /// fallback. The floor rect itself (used either way for the grid
    /// superset) is still resolved from the explicit push or the board's
    /// own viewport.
    /// </para>
    ///
    /// <para>
    /// <b>Grid.</b> With the grid flag, strokes the iso diamond outline of
    /// every cell; on a placeholder-floor board the grid is striped over
    /// the whole floor rect (<see cref="DrawGridOverRect"/>), not only the
    /// 8×8 logical grid. The grid is attenuated (see <see cref="GridLineColor"/>)
    /// so it reads as a faint developer overlay on the wood.
    /// </para>
    /// </summary>
    public override void _Draw()
    {
        if (Grid is null || Projection is null)
        {
            return;
        }

        // Resolve the desk floor rect. Used to PLACE the aplat (no-texture
        // fallback) and, either way, to size the grid superset. The wood
        // bitmap is drawn by the layer-1 Sprite2D, not here — when it is
        // loaded the aplat DrawRect below is skipped so it cannot hide it.
        DeskFloorRectLogic.FloorRect? floorRect = null;
        if (DrawPlaceholderFloor)
        {
            floorRect = _deskFloorFillRect ?? ViewportFloorFillRect();
            if (floorRect is { } rect && !_hasFloorBitmap)
            {
                // No-texture placeholder state: the flat aplat is the floor.
                if (DeskTrianglePlaceholderClip && _deskTriangles is { } tris)
                {
                    // TEMPORARY (Rune, 2026-05-22 F6 fix): the desk floor is
                    // framed to the two bottom-corner wedges, NOT the whole
                    // viewport rect. Filling the full rect here is exactly
                    // what hid the layer-B maquette after the clip shader
                    // was removed. Two triangle fills leave the screen
                    // centre unpainted so the maquette shows through. When
                    // Mira's Blender decor lands this branch is deleted and
                    // the full-rect DrawRect below is the only path.
                    DrawColoredPolygon(
                        ToGodotPoly(DeskTrianglePlaceholderLogic.Vertices(
                            tris.Left)),
                        FloorFillColor);
                    DrawColoredPolygon(
                        ToGodotPoly(DeskTrianglePlaceholderLogic.Vertices(
                            tris.Right)),
                        FloorFillColor);
                }
                else
                {
                    DrawRect(
                        new Rect2(
                            rect.TopLeft.X, rect.TopLeft.Y,
                            rect.Size.X, rect.Size.Y),
                        FloorFillColor,
                        filled: true);
                }
            }
        }

        if (!DrawPlaceholderGrid)
        {
            DiagDraw(floorRect, gridCellsDrawn: 0, Vector2.Zero, Vector2.Zero);
            return;
        }

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
    /// left without a grid. Returns the cell count drawn and, via out
    /// params, the pixel-space bounds of the striped grid.
    /// </summary>
    private int DrawGridOverRect(
        DeskFloorRectLogic.FloorRect rect, float halfW, float halfH,
        out Vector2 gridMin, out Vector2 gridMax)
    {
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
                if (centre.X + halfW < left || centre.X - halfW > right
                    || centre.Y + halfH < top || centre.Y - halfH > bottom)
                {
                    continue;
                }
                // TEMPORARY (Rune, 2026-05-22 F6 fix): when the desk floor is
                // framed to the two bottom-corner wedges, only stripe a cell
                // whose centre falls inside one of them — the grid then
                // reads as "the desk lives in the corners", not a full-screen
                // square lattice over the maquette. Deleted with the rest of
                // the placeholder when Mira's decor lands.
                if (DeskTrianglePlaceholderClip && _deskTriangles is { } tris
                    && !DeskTrianglePlaceholderLogic.Contains(
                        tris, new SysVec2(centre.X, centre.Y)))
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

    /// <summary>
    /// Engine seam: convert an array of <see cref="System.Numerics.Vector2"/>
    /// polygon vertices (from the Godot-free
    /// <see cref="DeskTrianglePlaceholderLogic"/>) into the
    /// <see cref="Vector2"/>[] a <see cref="CanvasItem.DrawColoredPolygon"/>
    /// call expects. Temporary — removed with the placeholder.
    /// </summary>
    private static Vector2[] ToGodotPoly(SysVec2[] points)
    {
        var result = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            result[i] = new Vector2(points[i].X, points[i].Y);
        }
        return result;
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
        // J3c-3: GridFillColor alpha is 0 — over the wood bitmap the
        // diamonds no longer tint the floor; only the hairline outline is
        // visible. The DrawColoredPolygon call is kept (cost negligible,
        // and a future debug build can re-raise the fill alpha) but it
        // paints nothing while the alpha stays 0.
        if (GridFillColor.A > 0f)
        {
            DrawColoredPolygon(diamond, GridFillColor);
        }
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
    /// Snap the layer-1 <see cref="_background"/> sprite so its top-left
    /// corner registers on the desk floor rect's top-left corner (J3c-3).
    ///
    /// <para>
    /// The sprite is <c>Centered = false</c>, so its <c>Position</c> IS its
    /// top-left in the board's local pixel space. The floor rect's top-left
    /// is at <c>cameraCentre - viewportSize/2 - slack</c> — a NEGATIVE
    /// offset from the board origin — so the sprite cannot just sit at the
    /// origin. Mira authored <c>wf_e1_desk_wood_floor.png</c> at exactly the
    /// floor rect's size (slack included), so this single snap registers
    /// the wood pixel-exact on the same rect the flat aplat used to fill;
    /// the triangular clip shader on <c>DeskFloorTextureRect</c> then carves
    /// the corner triangles out of it, identically to the aplat.
    /// </para>
    /// </summary>
    private void AlignBackgroundToFloorRect(DeskFloorRectLogic.FloorRect rect)
    {
        _background.Position = new Vector2(rect.TopLeft.X, rect.TopLeft.Y);
    }

    /// <summary>
    /// J3c-1bis instrumented diagnostic (prefix <c>[DESK-DIAG]</c>). Prints,
    /// at every <see cref="_Draw"/> pass of a placeholder-floor board (the
    /// desk), the exact floor rect, the grid cell count striped and its
    /// pixel bounds, a <c>GRID-COVERS-FLOOR</c> verdict, whether the floor
    /// is Mira's wood bitmap or the placeholder aplat, and the colours.
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
        GD.Print($"[DESK-DIAG]   floor source: " +
                 $"{(_hasFloorBitmap ? $"WOOD BITMAP ('{BackgroundTexturePath}', " +
                     $"size={_background.Texture?.GetSize().ToString() ?? "(none)"}, " +
                     $"spriteTopLeft={_background.Position}) — flat aplat SKIPPED"
                   : "FLAT APLAT (no BackgroundTexturePath — placeholder state)")}");
        if (floorRect is { } fr)
        {
            GD.Print($"[DESK-DIAG]   floor rect: topLeft=({fr.TopLeft.X},{fr.TopLeft.Y}) " +
                     $"size=({fr.Size.X},{fr.Size.Y}) " +
                     $"bottomRight=({fr.TopLeft.X + fr.Size.X},{fr.TopLeft.Y + fr.Size.Y}) " +
                     $"source={(_deskFloorFillRect is null ? "ViewportFloorFillRect-fallback" : "GameScreen-push")}");
        }
        else
        {
            GD.Print("[DESK-DIAG]   floor rect: NULL — floor rect not resolved " +
                     "this pass (no pushed rect, no camera). THIS WOULD BE A DARK ZONE.");
        }
        GD.Print($"[DESK-DIAG]   iso grid: cellsDrawn={gridCellsDrawn} " +
                 $"pixelBounds min=({gridMin.X},{gridMin.Y}) max=({gridMax.X},{gridMax.Y})");
        if (DeskTrianglePlaceholderClip)
        {
            GD.Print($"[DESK-DIAG]   desk floor framing: " +
                     $"TEMPORARY twin-corner triangle placeholder " +
                     $"(clip={(_deskTriangles is null ? "NOT SET" : "set")}) " +
                     $"-- floor + grid drawn only in the two bottom-corner " +
                     $"wedges; screen centre left clear for the maquette. " +
                     $"Removed when Mira's Blender decor lands.");
        }
        if (floorRect is { } f2 && gridCellsDrawn > 0)
        {
            bool gridCoversFloor =
                gridMin.X <= f2.TopLeft.X && gridMin.Y <= f2.TopLeft.Y
                && gridMax.X >= f2.TopLeft.X + f2.Size.X
                && gridMax.Y >= f2.TopLeft.Y + f2.Size.Y;
            GD.Print($"[DESK-DIAG]   GRID-COVERS-FLOOR={gridCoversFloor} " +
                     $"(false => bare-floor wedge with no grid)");
        }
        GD.Print($"[DESK-DIAG]   colours: FloorFillColor(rgba)=" +
                 $"({FloorFillColor.R},{FloorFillColor.G},{FloorFillColor.B},{FloorFillColor.A}) " +
                 $"GridLineColor(rgba)=({GridLineColor.R},{GridLineColor.G},{GridLineColor.B},{GridLineColor.A}) " +
                 $"GridFillColor(rgba)=({GridFillColor.R},{GridFillColor.G},{GridFillColor.B},{GridFillColor.A})");
    }

    /// <summary>
    /// Load and assign the layer-1 floor bitmap. An empty path is a
    /// tolerated placeholder state (no district / desk-floor texture wired)
    /// — logged, not fatal: the maquette runs without it and a
    /// placeholder-floor board falls back to the flat aplat. A non-empty
    /// path that fails to resolve is a real error. On success
    /// <see cref="_hasFloorBitmap"/> flips true, which makes <see cref="_Draw"/>
    /// skip the placeholder aplat (J3c-3).
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
        _hasFloorBitmap = true;
        GD.Print($"[IsoBoard] floor bitmap loaded '{BackgroundTexturePath}' " +
                 $"size={texture.GetSize()} — placeholder aplat will be skipped.");
    }

    /// <summary>
    /// MVP anchor: the pixel where the centre of cell (0,0) sits. For the
    /// placeholder rectangle, cell (0,0) is the top apex of the diamond
    /// block, so it sits half a grid-width in from the left of the floor.
    /// When a district sidecar is wired this whole method is replaced by a
    /// sidecar read (design doc §2).
    ///
    /// <para>
    /// J3c-3 note: this anchor is unchanged by the wood-floor wire-in. The
    /// projection (cell ↔ pixel) is independent of where the layer-1 sprite
    /// sits — <see cref="AlignBackgroundToFloorRect"/> moves only the
    /// sprite, never the projection — so the J3c-2 click routing and the
    /// pawn placement are untouched.
    /// </para>
    /// </summary>
    private SysVec2 ComputeAnchorPixel()
    {
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
                 $"centered={_background.Centered} " +
                 $"spriteTopLeft={_background.Position} " +
                 $"hasFloorBitmap={_hasFloorBitmap}");
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
