using System.Numerics;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Services.Iso;

/// <summary>
/// The single source of truth for isometric coordinate conversion in the
/// J3-iso hybrid socle. Converts between two spaces:
///
/// <list type="bullet">
///   <item><b>cell</b> — discrete grid coordinate
///     (<see cref="TileCoordinate"/>), the logic space of
///     <see cref="IsoGrid"/>.</item>
///   <item><b>world / screen pixel</b> — continuous pixel position
///     (<see cref="Vector2"/>) inside the district's coordinate space,
///     where a layer-3 entity node is placed.</item>
/// </list>
///
/// <para>
/// <b>Why a dedicated immutable class.</b> Design doc §4 step 4: the
/// hybrid socle has <i>no per-cell render loop</i> — the floor bitmap and
/// the entity nodes draw themselves. What we DO write is coordinate
/// conversion, and it must live in exactly one place. Two iso conversions
/// scattered across two files diverge silently (the orthogonality rule of
/// trap #6). <see cref="CellToWorld"/> and <see cref="WorldToCell"/> are
/// exact inverses, pinned by round-trip tests.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Coordinates are
/// <see cref="TileCoordinate"/> (pure-C# integer pair) and
/// <see cref="System.Numerics.Vector2"/> — never <c>Godot.Vector2I</c> /
/// <c>Godot.Vector2</c> — so this projection is cherry-pickable into the
/// xUnit test host that has no GodotSharp. The Godot-bound <c>IsoBoard</c>
/// node converts to <c>Godot.Vector2</c> at the engine edge (one
/// <c>new Godot.Vector2(p.X, p.Y)</c>). Same seam discipline as
/// <c>FogTileGridLogic</c>.
/// </para>
///
/// <para>
/// <b>Projection convention — 2:1 diamond, DiamondDown.</b> A cell at
/// <c>(cx, cy)</c> projects to pixel
/// <c>( (cx - cy) * TileWidth/2 , (cx + cy) * TileHeight/2 )</c> plus the
/// anchor offset. This is the standard game-iso 2:1 dimetric layout —
/// the same one already locked for <c>IsoSocketGeometryLogic</c> (rhombus
/// twice as wide as tall) and described in <c>SimulationGrid</c>'s
/// <c>BuildRidgeMission</c> doc. Increasing <c>cx</c> moves down-right on
/// screen, increasing <c>cy</c> moves down-left. Godot screen Y points
/// down, which the <c>+TileHeight/2</c> term already respects — no Y
/// inversion is applied here (the FogYOffset bug of the 3D-backing memory
/// came from inverting Y twice; we invert it zero times and pin that with
/// a test).
/// </para>
///
/// <para>
/// <b>Centre, not corner.</b> <see cref="CellToWorld"/> returns the pixel
/// of the <i>centre</i> of the cell's diamond, and <see cref="WorldToCell"/>
/// is its exact inverse. This is deliberate and pinned: the centroid /
/// <c>+0.5</c> ambiguity is the E2.1c bug. An entity placed via
/// <see cref="CellToWorld"/> still needs its own foot-anchor offset
/// (the POI <c>anchor_pixel</c> pattern) — the projection gives the cell
/// centre, the entity sprite offsets its foot onto it.
/// </para>
///
/// <para>
/// <b>Extension seam (elevation).</b> MVP is a flat world. If elevation
/// arrives, it shifts the projected pixel by <c>-elevation * k</c> on Y.
/// Because every call site goes through <see cref="CellToWorld"/> rather
/// than inlining the affine formula, adding that term is a one-method
/// change — design doc §4 step 2 "keep the mapping behind a method".
/// </para>
/// </summary>
public sealed class IsoProjection
{
    /// <summary>
    /// Projected width of one cell diamond, in pixels (the full X span of
    /// the rhombus). Must be positive and even — an odd width makes the
    /// <c>TileWidth/2</c> half-step land on a sub-pixel and the round-trip
    /// drifts.
    /// </summary>
    public int TileWidth { get; }

    /// <summary>
    /// Projected height of one cell diamond, in pixels (the full Y span of
    /// the rhombus). For the locked 2:1 ratio this is
    /// <c>TileWidth / 2</c>. Must be positive and even.
    /// </summary>
    public int TileHeight { get; }

    /// <summary>
    /// Pixel position, in the district's coordinate space, of the centre
    /// of cell <c>(0, 0)</c>. Comes from the district sidecar
    /// (<c>AnchorPixel</c>, design doc §2) — never hard-coded so a
    /// re-cropped Blender re-render only changes the sidecar, not code.
    /// </summary>
    public Vector2 AnchorPixel { get; }

    /// <summary>
    /// The locked iso aspect ratio: a cell diamond is always exactly twice
    /// as wide on screen as it is tall. Matches
    /// <c>IsoSocketGeometryLogic.WidthOverHeight</c>.
    /// </summary>
    public const float WidthOverHeight = 2f;

    /// <summary>
    /// Build a projection. Use <see cref="Iso2To1"/> for the standard
    /// locked ratio rather than passing <paramref name="tileHeight"/> by
    /// hand.
    /// </summary>
    /// <param name="tileWidth">Cell diamond width in pixels. Positive, even.</param>
    /// <param name="tileHeight">Cell diamond height in pixels. Positive, even.</param>
    /// <param name="anchorPixel">Pixel of the centre of cell (0,0).</param>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// If either dimension is non-positive or odd.
    /// </exception>
    public IsoProjection(int tileWidth, int tileHeight, Vector2 anchorPixel)
    {
        if (tileWidth <= 0 || (tileWidth & 1) == 1)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(tileWidth),
                $"TileWidth must be positive and even; got {tileWidth}.");
        }
        if (tileHeight <= 0 || (tileHeight & 1) == 1)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(tileHeight),
                $"TileHeight must be positive and even; got {tileHeight}.");
        }

        TileWidth = tileWidth;
        TileHeight = tileHeight;
        AnchorPixel = anchorPixel;
    }

    /// <summary>
    /// Build a projection at the locked 2:1 ratio: pass the diamond width,
    /// height is derived as <c>width / 2</c>. The width must be a positive
    /// multiple of 4 so the derived height is itself even.
    /// </summary>
    public static IsoProjection Iso2To1(int tileWidth, Vector2 anchorPixel)
    {
        if (tileWidth <= 0 || tileWidth % 4 != 0)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(tileWidth),
                $"For a 2:1 projection TileWidth must be a positive multiple " +
                $"of 4 (so width and derived height stay even); got {tileWidth}.");
        }
        return new IsoProjection(tileWidth, tileWidth / 2, anchorPixel);
    }

    /// <summary>
    /// Project a grid coordinate to the pixel at the <i>centre</i> of that
    /// cell's diamond. The pixel a layer-3 entity is placed at (after the
    /// entity applies its own foot-anchor offset).
    /// </summary>
    public Vector2 CellToWorld(TileCoordinate cell)
    {
        float halfW = TileWidth * 0.5f;
        float halfH = TileHeight * 0.5f;
        float px = (cell.Col - cell.Row) * halfW;
        float py = (cell.Col + cell.Row) * halfH;
        return new Vector2(px, py) + AnchorPixel;
    }

    /// <summary>
    /// Inverse of <see cref="CellToWorld"/>: which cell's diamond contains
    /// <paramref name="worldPos"/>. Used for analytic picking — "the mouse
    /// is over this cell". The continuous result of the affine inverse is
    /// rounded to the nearest integer cell; a point exactly on a diamond
    /// edge resolves to one of the adjacent cells by the rounding rule
    /// (deterministic, pinned by tests).
    ///
    /// <para>
    /// Round-trip guarantee: for any cell <c>c</c>,
    /// <c>WorldToCell(CellToWorld(c)) == c</c>. Pinned by
    /// <c>IsoProjectionTests</c>.
    /// </para>
    /// </summary>
    public TileCoordinate WorldToCell(Vector2 worldPos)
    {
        Vector2 local = worldPos - AnchorPixel;
        float halfW = TileWidth * 0.5f;
        float halfH = TileHeight * 0.5f;

        // Invert the system:
        //   localX = (cx - cy) * halfW
        //   localY = (cx + cy) * halfH
        // => cx - cy = localX / halfW ; cx + cy = localY / halfH
        // => cx = (a + b) / 2 ; cy = (b - a) / 2
        float a = local.X / halfW;
        float b = local.Y / halfH;
        float cx = (a + b) * 0.5f;
        float cy = (b - a) * 0.5f;

        // AwayFromZero so the rule is symmetric across the origin and a
        // negative coord rounds the mirror of a positive one. Round-trip
        // identity does not depend on the mode (cell centres land exactly
        // on integers); the explicit mode just pins edge behaviour.
        return new TileCoordinate(
            (int)System.MathF.Round(cx, System.MidpointRounding.AwayFromZero),
            (int)System.MathF.Round(cy, System.MidpointRounding.AwayFromZero));
    }
}
