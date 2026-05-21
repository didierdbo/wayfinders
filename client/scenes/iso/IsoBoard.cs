using Godot;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Services.Iso;
using Wayfinders.Client.Utils;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scenes.Iso;

/// <summary>
/// Reusable root node of the J3-iso hybrid socle (design doc
/// <c>rune-iso-rendering-design-2026-05-20.md</c>, decision #1 validated
/// by Didier 2026-05-21). One <see cref="IsoBoard"/> instance is one
/// isometric space — the district maquette and, later, the cartographer's
/// desk both instance this same scene (<c>IsoBoard.tscn</c>).
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
/// entities (character pawns, building sprites), Y-sorting, panning, and
/// input are deliberately out of this socle — they are later sub-milestones
/// (design doc §4, build brief item 4).
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
/// placeholder, off by default, on for the J3a maquette so the pan and
/// the SubViewport clipping are observable. It is <i>not</i> a renderer
/// and <i>not</i> the per-cell draw loop the hybrid forbids: it is one
/// debug <see cref="_Draw"/> pass over the logical grid, dropped the
/// moment a real floor bitmap exists.
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
    /// grid cell — a developer-only placeholder so the J3a maquette is
    /// visible (pan + clip observable) before Mira's floor bitmap lands.
    /// Off by default; the J3a <c>GameScreen.tscn</c> turns it on for its
    /// <c>Maquette</c> instance. Drop it once a real floor bitmap exists.
    /// </summary>
    [Export]
    public bool DrawPlaceholderGrid { get; set; }

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

    // Placeholder-grid stroke colours — warm earth, Wayfinders visual DNA
    // (terracotta / parchment / umber). Developer placeholder only.
    private static readonly Color GridLineColor = new(0.62f, 0.40f, 0.27f, 0.85f);
    private static readonly Color GridFillColor = new(0.91f, 0.85f, 0.72f, 0.10f);

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

        if (DrawPlaceholderGrid)
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
    /// Developer placeholder draw (see <see cref="DrawPlaceholderGrid"/>).
    /// Strokes the iso diamond outline of every cell using the board's
    /// own <see cref="IsoProjection"/> — the projection is the single
    /// source of truth, this draw does not re-derive any iso maths. One
    /// pass at <see cref="_Ready"/>, not per-frame; the grid is static.
    /// </summary>
    public override void _Draw()
    {
        if (!DrawPlaceholderGrid || Grid is null || Projection is null)
        {
            return;
        }

        float halfW = Projection.TileWidth * 0.5f;
        float halfH = Projection.TileHeight * 0.5f;

        foreach (var cell in Grid.AllCells())
        {
            var centre = CellToPixel(new Vector2I(cell.Coord.Col, cell.Coord.Row));
            // Four diamond apexes around the cell centre.
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
                 $"drawPlaceholder={DrawPlaceholderGrid}");
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
