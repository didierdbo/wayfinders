using Godot;
using Wayfinders.Client.Simulation;

namespace Wayfinders.Client.Scenes;

/// <summary>
/// First tactical scene (Phase 5 L1 + L2).
///
/// <para>
/// <b>What this layer ships (L1 + L2):</b> a 24×24 isometric
/// <see cref="TileMapLayer"/> rendered with a programmatically-generated
/// placeholder tile, Y-sort plumbing wired so future units occlude
/// correctly, a parallel logical <see cref="SimulationGrid"/> seeded with
/// the UC1 ridge-mission shape, and a click-to-cell debug surface that
/// prints both the <see cref="Vector2I"/> cell coordinate of the tile
/// under the mouse <i>and</i> the matching <see cref="Cell"/> payload —
/// proof that the visual and logical halves are kept in sync.
/// </para>
///
/// <para>
/// <b>What this layer is NOT:</b> no terrain visual variation yet (L3),
/// no pathfinding (L4), no units on the grid (L5). L2 stops at "the iso
/// grid renders, the simulation grid exists, and the click reports
/// both."
/// </para>
///
/// <para>
/// <b>Visual grid vs simulation grid (the load-bearing lesson, now
/// realized).</b> <see cref="TileMapLayer"/> is what the player
/// <i>sees</i>. The game <i>state</i> — who occupies a cell, terrain
/// type, elevation, cover — lives in <see cref="SimulationGrid"/>, a
/// plain C# class held as a field on this scene. The
/// <see cref="TileMapLayer"/> is never the source of truth for game
/// state. Going forward (L3+) the two halves are kept in sync via
/// explicit code paths: when the simulation says "this cell is loose
/// scree", L3's stamp-pass writes the matching atlas coord to the visual
/// layer. The reverse never happens.
/// </para>
///
/// <para>
/// <b>Why <see cref="SimulationGrid"/> is held as a field, not an
/// autoload.</b> The grid's lifetime is the lifetime of one tactical
/// encounter — when the scene exits the tree, the grid is gone with it.
/// An autoload would imply "shared across scenes" semantics that are
/// wrong here. If a future feature needs cross-scene grid state (e.g.
/// returning to a paused mission), that is a save/load concern, not an
/// autoload concern.
/// </para>
///
/// <para>
/// <b>Why the TileSet is built in code.</b> Mira has not delivered art yet
/// and we do not want a binary placeholder asset cluttering the repo. The
/// <see cref="TileSet"/> is generated at <c>_Ready</c> from a programmatic
/// <see cref="Image"/> — a 64×32 iso diamond filled with a placeholder color
/// plus a darker border for visibility. Once real art lands the TileSet
/// becomes a <c>.tres</c> referencing real textures; the rest of this code
/// does not change.
/// </para>
///
/// <para>
/// <b>Iso math is the engine's job.</b> The cell↔pixel conversion uses
/// <see cref="TileMapLayer.LocalToMap(Vector2)"/> and
/// <see cref="TileMapLayer.MapToLocal(Vector2I)"/>. Hand-rolled
/// <c>screenX = (cellX - cellY) * tileWidth/2</c> trigonometry is a
/// code-review red flag — the helpers exist for a reason and they handle
/// the iso shape, the tile size, and the layer transform together.
/// </para>
///
/// <para>
/// <b>Y-sort cascade.</b> The <see cref="Node2D"/> root sets
/// <see cref="CanvasItem.YSortEnabled"/>. The <see cref="TileMapLayer"/>
/// has its own per-layer y-sort flag set on the node. Future units (L5)
/// will be siblings of <c>Ground</c> under a Y-sorted parent, and their
/// global Y will determine their draw order against the tiles and against
/// each other — the standard 2.5D iso convention.
/// </para>
/// </summary>
public partial class TacticalScene : Node2D
{
    /// <summary>
    /// Map width in cells. 24×24 is the locked target for the slice
    /// (see <c>Rune - Arc 2 Phase 5 Pre-Brief - 2026-05-03.md</c>,
    /// changelog 2026-05-05).
    /// </summary>
    private const int MapWidth = 24;

    /// <summary>
    /// Map height in cells.
    /// </summary>
    private const int MapHeight = 24;

    /// <summary>
    /// Iso tile texture width in pixels. Standard 2:1 iso ratio with
    /// <see cref="TileHeight"/>. The placeholder diamond is drawn inside
    /// this rectangle.
    /// </summary>
    private const int TileWidth = 64;

    /// <summary>
    /// Iso tile texture height in pixels. Half the width for the standard
    /// 2:1 iso projection.
    /// </summary>
    private const int TileHeight = 32;

    /// <summary>
    /// Atlas-source ID we register the placeholder tile under. Single
    /// source for L1; L3 will introduce more.
    /// </summary>
    private const int PlaceholderSourceId = 0;

    /// <summary>
    /// The cell coordinate inside the atlas texture at which our single
    /// placeholder tile sits. The atlas is 1×1 tiles wide so this is
    /// always (0, 0).
    /// </summary>
    private static readonly Vector2I PlaceholderAtlasCoord = new(0, 0);

    private TileMapLayer _ground = null!;

    /// <summary>
    /// Authoritative game state for the tactical map. Built once in
    /// <see cref="_Ready"/> and held for the lifetime of the scene. Not
    /// a <see cref="Node"/> — see class doc.
    /// </summary>
    private SimulationGrid _simulation = null!;

    public override void _Ready()
    {
        _ground = GetNode<TileMapLayer>("Ground");

        // The TileSet is generated in code — see class doc for why.
        _ground.TileSet = BuildPlaceholderTileSet();

        // Stamp the 24×24 diamond. Every cell points at the single
        // placeholder atlas coord. L3 will vary this per terrain type.
        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                _ground.SetCell(
                    new Vector2I(x, y),
                    PlaceholderSourceId,
                    PlaceholderAtlasCoord);
            }
        }

        // Build the simulation grid. The visual layer above is rendered
        // uniformly for L2; L3 will read terrain back from the simulation
        // and pick the matching atlas coord per cell.
        _simulation = SimulationGrid.BuildRidgeMission();

        GD.Print(
            $"TacticalScene: rendered {MapWidth}x{MapHeight} iso grid; " +
            $"simulation grid initialized with ridge-mission terrain.");
    }

    /// <summary>
    /// Click-to-cell debug surface. Listens on <c>_UnhandledInput</c> so a
    /// future pause menu or HUD can intercept events first; the tactical
    /// world only sees clicks that no UI ate.
    ///
    /// <para>
    /// The conversion chain is screen → world → layer-local → cell.
    /// <see cref="Node2D.GetGlobalMousePosition"/> handles the screen → world
    /// step (camera transform applied). <see cref="Node2D.ToLocal(Vector2)"/>
    /// on the <see cref="TileMapLayer"/> handles world → layer-local.
    /// <see cref="TileMapLayer.LocalToMap(Vector2)"/> handles the iso math.
    /// Each step has a single owner; we do not collapse them.
    /// </para>
    ///
    /// <para>
    /// L2 addition: once we have a clicked cell coord, we read the
    /// <see cref="Cell"/> from <see cref="_simulation"/> and print it
    /// alongside. This is the smoke test for the visual↔logical mapping —
    /// click on a boulder cell, see <c>Terrain=Blocked</c>; click on the
    /// scree band, see <c>Terrain=LooseScree, Elevation=1</c>; click on a
    /// designer-placed partial-cover spot, see <c>Cover=Partial</c>. If
    /// the two ever drift, this print is where it shows.
    /// </para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb)
        {
            return;
        }

        // Only react on the press edge of the left button. Releases and
        // right-clicks are out of scope for L1/L2.
        if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
        {
            return;
        }

        Vector2 worldPos = GetGlobalMousePosition();
        Vector2 layerLocalPos = _ground.ToLocal(worldPos);
        Vector2I coord = _ground.LocalToMap(layerLocalPos);

        if (!_simulation.IsInBounds(coord))
        {
            GD.Print($"TacticalScene: clicked outside grid (cell {coord}).");
            return;
        }

        Cell cell = _simulation.GetCell(coord);
        GD.Print(
            $"TacticalScene: clicked cell {coord} -> " +
            $"Terrain={cell.Terrain}, " +
            $"Elevation={cell.Elevation}, " +
            $"Cover={cell.Cover}, " +
            $"OccupantId={cell.OccupantId ?? "<empty>"}.");
    }

    /// <summary>
    /// Build a one-tile <see cref="TileSet"/> in iso shape, backed by a
    /// programmatically-drawn placeholder texture. No binary asset
    /// dependency; everything reproducible from source.
    ///
    /// <para>
    /// <see cref="TileSet.TileShape"/> = isometric is what makes the layer
    /// render diamonds; <see cref="TileSet.TileLayout"/> stays at the
    /// default <c>Stacked</c> which matches Godot's standard iso layout
    /// where each row is offset half a tile.
    /// </para>
    /// </summary>
    private static TileSet BuildPlaceholderTileSet()
    {
        var tileSet = new TileSet
        {
            TileShape = TileSet.TileShapeEnum.Isometric,
            TileSize = new Vector2I(TileWidth, TileHeight),
        };

        var atlas = new TileSetAtlasSource
        {
            Texture = BuildPlaceholderTileTexture(),
            TextureRegionSize = new Vector2I(TileWidth, TileHeight),
        };

        // Register the single placeholder tile at atlas coord (0, 0).
        atlas.CreateTile(PlaceholderAtlasCoord);

        tileSet.AddSource(atlas, PlaceholderSourceId);
        return tileSet;
    }

    /// <summary>
    /// Draw a 64×32 iso diamond placeholder into an <see cref="Image"/> and
    /// wrap it in an <see cref="ImageTexture"/>. Solid fill plus a one-pixel
    /// border so adjacent tiles read as separate at a glance.
    ///
    /// <para>
    /// The diamond is the set of pixels satisfying
    /// <c>|x - cx| / (w/2) + |y - cy| / (h/2) &lt;= 1</c> where <c>(cx, cy)</c>
    /// is the texture center. That is the standard "1.0-bounded L1 norm"
    /// that defines a diamond / rhombus.
    /// </para>
    /// </summary>
    private static ImageTexture BuildPlaceholderTileTexture()
    {
        var image = Image.CreateEmpty(TileWidth, TileHeight, false, Image.Format.Rgba8);

        // Transparent background so non-diamond pixels do not draw.
        image.Fill(new Color(0, 0, 0, 0));

        var fill = new Color(0.45f, 0.55f, 0.40f); // muted ridge-grass green
        var border = new Color(0.20f, 0.25f, 0.15f); // darker outline

        float halfW = TileWidth / 2f;
        float halfH = TileHeight / 2f;
        float cx = halfW;
        float cy = halfH;

        for (int y = 0; y < TileHeight; y++)
        {
            for (int x = 0; x < TileWidth; x++)
            {
                float nx = Mathf.Abs(x + 0.5f - cx) / halfW;
                float ny = Mathf.Abs(y + 0.5f - cy) / halfH;
                float d = nx + ny;

                if (d > 1.0f)
                {
                    continue; // outside the diamond, leave transparent
                }

                // The outer 8% of the diamond is the border ring.
                bool onBorder = d > 0.92f;
                image.SetPixel(x, y, onBorder ? border : fill);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
