using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helpers for the E2AreaMap 8×8 intra-area grid. Owns the
/// Varn-locked coord → <see cref="DistrictType"/> mapping for Halfgate
/// (the MVP single-area), the grid-size constant, and bound-checking
/// helpers used by the render layer + the mission reveal grammar
/// (A4.2 <c>pop_effect.tiles_to_partial</c> footprint resolution).
///
/// <para>
/// <b>Why pure-C# and not a Resource.</b> Same rationale as
/// <see cref="NpcCatalog"/> : the grid layout is engineering data, not
/// narrative copy. A <c>.tres</c> here would invite typos that break the
/// city-wall pattern silently. When the multi-area roadmap lands (E2 →
/// Veylant / Coastal / etc.), this gets refactored to a per-area
/// <c>Resource</c> ; for the MVP single-area the static catalog is enough.
/// </para>
///
/// <para>
/// <b>Grid layout (Varn-locked for Halfgate, 8×8).</b>
/// </para>
/// <code>
///   col  0  1  2  3  4  5  6  7
/// row
///  0    A  A  A  G  G  A  A  A    A = HinterlandAgri (north/south fields)
///  1    A  O  O  W  W  O  O  A    O = Outskirts
///  2    L  O  W  I  I  W  O  L    W = Wall ring
///  3    L  G  I  I  I  I  G  L    I = Intramuros (centre)
///  4    L  G  I  I  I  I  G  L    G = Gateway (cardinal piercings)
///  5    L  O  W  I  I  W  O  L    L = Littoral (E and W edges)
///  6    A  O  O  W  W  O  O  A
///  7    A  A  A  G  G  A  A  A
/// </code>
///
/// <para>
/// <b>Cardinal symmetry on purpose.</b> The pattern is N-S + E-W mirror
/// symmetric (the cité is roughly circular). This is the smallest layout
/// that exercises all 6 <see cref="DistrictType"/> values inside a single
/// area, which is the E2.1 acceptance criterion (hover any cell, see the
/// right district_type). Varn may revise the exact placements post-MVP
/// without changing the grid size or the closed lookup.
/// </para>
///
/// <para>
/// <b>Coordinate convention.</b> <see cref="GridCoord.Col"/> is the X
/// axis (0 = west, 7 = east) ; <see cref="GridCoord.Row"/> is the Y axis
/// (0 = north, 7 = south). Matches the Phase A/B/C pan + reveal pipeline,
/// which already addresses cells column-then-row.
/// </para>
/// </summary>
public static class AreaGridLogic
{
    /// <summary>
    /// Side length of the E2AreaMap intra-area grid. Locked at 8 (E2.1
    /// brief) : 4×4 footprint of E1 zoomed ×2. If a future area grows
    /// past 8×8, bump here and adapt <see cref="ResolveDistrictType"/>
    /// (and the placeholder tile texture set).
    /// </summary>
    public const int GridSize = 8;

    /// <summary>
    /// True if the cell is in the 8×8 grid bounds. Centralised so the
    /// callers (mission footprint resolution, NPC known_tiles validation,
    /// hover hit-test) all use the same check.
    /// </summary>
    public static bool IsInBounds(GridCoord coord) =>
        coord.Col >= 0 && coord.Col < GridSize
        && coord.Row >= 0 && coord.Row < GridSize;

    /// <summary>
    /// Resolve a cell coordinate to its Varn-locked
    /// <see cref="DistrictType"/> for Halfgate. Returns
    /// <see cref="DistrictType.Outskirts"/> for out-of-bounds coords
    /// (defensive : the caller should bounds-check first, but the helper
    /// stays total-by-default to avoid throwing on hover at the grid
    /// edge).
    ///
    /// <para>
    /// See the class XML doc for the visual ASCII layout. The
    /// implementation is a flat lookup table -- explicit per cell -- so
    /// reading the source matches reading the ASCII art.
    /// </para>
    /// </summary>
    public static DistrictType ResolveDistrictType(GridCoord coord)
    {
        if (!IsInBounds(coord)) return DistrictType.Outskirts;
        return _grid[coord.Row, coord.Col];
    }

    /// <summary>
    /// Locked 8×8 grid. Indexed as <c>_grid[row, col]</c>. The literal
    /// authorship mirrors the ASCII art in the class XML doc -- read top
    /// to bottom, left to right.
    /// </summary>
    private static readonly DistrictType[,] _grid = new DistrictType[GridSize, GridSize]
    {
        // row 0 (north hinterland strip)
        { DistrictType.HinterlandAgri, DistrictType.HinterlandAgri, DistrictType.HinterlandAgri, DistrictType.Gateway,        DistrictType.Gateway,        DistrictType.HinterlandAgri, DistrictType.HinterlandAgri, DistrictType.HinterlandAgri },
        // row 1 (outskirts ring + wall corners)
        { DistrictType.HinterlandAgri, DistrictType.Outskirts,      DistrictType.Outskirts,      DistrictType.Wall,           DistrictType.Wall,           DistrictType.Outskirts,      DistrictType.Outskirts,      DistrictType.HinterlandAgri },
        // row 2 (wall + intramuros begin)
        { DistrictType.Littoral,       DistrictType.Outskirts,      DistrictType.Wall,           DistrictType.Intramuros,     DistrictType.Intramuros,     DistrictType.Wall,           DistrictType.Outskirts,      DistrictType.Littoral       },
        // row 3 (intramuros core + east/west gates)
        { DistrictType.Littoral,       DistrictType.Gateway,        DistrictType.Intramuros,     DistrictType.Intramuros,     DistrictType.Intramuros,     DistrictType.Intramuros,     DistrictType.Gateway,        DistrictType.Littoral       },
        // row 4 (intramuros core + east/west gates -- mirror of row 3)
        { DistrictType.Littoral,       DistrictType.Gateway,        DistrictType.Intramuros,     DistrictType.Intramuros,     DistrictType.Intramuros,     DistrictType.Intramuros,     DistrictType.Gateway,        DistrictType.Littoral       },
        // row 5 (mirror of row 2)
        { DistrictType.Littoral,       DistrictType.Outskirts,      DistrictType.Wall,           DistrictType.Intramuros,     DistrictType.Intramuros,     DistrictType.Wall,           DistrictType.Outskirts,      DistrictType.Littoral       },
        // row 6 (mirror of row 1)
        { DistrictType.HinterlandAgri, DistrictType.Outskirts,      DistrictType.Outskirts,      DistrictType.Wall,           DistrictType.Wall,           DistrictType.Outskirts,      DistrictType.Outskirts,      DistrictType.HinterlandAgri },
        // row 7 (mirror of row 0)
        { DistrictType.HinterlandAgri, DistrictType.HinterlandAgri, DistrictType.HinterlandAgri, DistrictType.Gateway,        DistrictType.Gateway,        DistrictType.HinterlandAgri, DistrictType.HinterlandAgri, DistrictType.HinterlandAgri },
    };

    /// <summary>
    /// Enumerate every cell of the grid in row-major order. Useful for
    /// the render layer that needs to instantiate one tile sprite per
    /// cell, and for diagnostic walks that print the layout.
    /// </summary>
    public static IEnumerable<GridCoord> AllCells()
    {
        for (var row = 0; row < GridSize; row++)
        {
            for (var col = 0; col < GridSize; col++)
            {
                yield return new GridCoord(col, row);
            }
        }
    }

    /// <summary>
    /// Centre cell of the grid. For an 8×8 grid the geometric centre is
    /// between (3,3) and (4,4) ; we pick (4,4) as the canonical anchor
    /// (matches the Varn ASCII layout where intramuros occupies the
    /// 4-cell square centred on this coord). Used by the mission tuto
    /// boot footprint and by the future autoload-life NPC spawn logic.
    /// </summary>
    public static GridCoord Centre => new(4, 4);

    /// <summary>
    /// Resolve the 4-cell footprint surrounding the centre of the grid
    /// (the canonical "POI footprint" for the Halfgate boot tuto
    /// mission). Returns intramuros cells (3,3) (3,4) (4,3) (4,4) -- the
    /// four central tiles of the city. A4.2 reveal grammar consumes this
    /// list as <c>pop_effect.tiles_to_partial</c>.
    /// </summary>
    public static IReadOnlyList<GridCoord> CentralPoiFootprint() => _centralFootprint;

    private static readonly IReadOnlyList<GridCoord> _centralFootprint = new List<GridCoord>
    {
        new(3, 3), new(4, 3), new(3, 4), new(4, 4),
    };

    /// <summary>
    /// Enumerate every cell that resolves to the given
    /// <see cref="DistrictType"/>. Row-major order (matches
    /// <see cref="AllCells"/>). Useful for centroid sanity checks,
    /// mission footprint visualisation, and the future per-district
    /// hover dispatcher.
    ///
    /// <para>
    /// <b>Why iterator (yield) and not a List.</b> Callers either consume
    /// the result once (sanity check loop, the most frequent case) or
    /// pass it through a LINQ chain that materialises lazily. Returning
    /// a pre-built List would force one allocation per call ; yield
    /// costs only a state machine which the JIT inlines aggressively.
    /// </para>
    /// </summary>
    public static IEnumerable<GridCoord> CellsOfDistrict(DistrictType district)
    {
        foreach (var c in AllCells())
        {
            if (ResolveDistrictType(c) == district)
            {
                yield return c;
            }
        }
    }

    /// <summary>
    /// Resolve the canonical (col, row) centroid anchor for a district's
    /// POI marker (E2.1c Rune lock 2026-05-16). Returns a FRACTIONAL
    /// position because districts with even cell counts have a true
    /// centroid that falls BETWEEN cells, which is exactly where the
    /// marker should sit visually (a marker pinned to a cell centre
    /// would read "this specific cell" instead of "this district").
    ///
    /// <para>
    /// <b>Disjoint-zone tie-break rule (Varn-locked authoring choice).</b>
    /// Three of the 6 districts span two disjoint zones :
    /// <list type="bullet">
    ///   <item><see cref="DistrictType.Wall"/> -- inner ring (rows 2 + 5,
    ///         cols 2 + 5) + corner cells (rows 1 + 6, cols 3 + 4).</item>
    ///   <item><see cref="DistrictType.Gateway"/> -- two N/S strips
    ///         (rows 0 + 7, cols 3 + 4) and two E/W single-cell gates
    ///         (rows 3 + 4, cols 1 + 6).</item>
    ///   <item><see cref="DistrictType.Littoral"/> -- two coastal strips
    ///         (col 0 and col 7, rows 2..5).</item>
    /// </list>
    ///
    /// <para>
    /// For these, the bare geometric centroid lands at the cardinal
    /// symmetry point (3.5, 3.5), which is the WORST possible POI
    /// position : three markers would stack on the grid centre,
    /// occluding the Intramuros marker and reading as one POI instead
    /// of three. The Varn-locked anchor lookup below picks the centroid
    /// of the PRIMARY zone of each district, chosen for visual clarity :
    /// </para>
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item><see cref="DistrictType.Intramuros"/> -- true geometric
    ///         centroid of the 12 contiguous Intramuros cells. The
    ///         centroid lands at (3.5, 3.5) -- exactly the city
    ///         centre.</item>
    ///   <item><see cref="DistrictType.Wall"/> -- centroid of the NORTH
    ///         wall zone (4 cells : (3,1) (4,1) (2,2) (5,2)). Resolves
    ///         to (3.5, 1.5) -- reads "north wall section".</item>
    ///   <item><see cref="DistrictType.Gateway"/> -- centroid of the
    ///         NORTH gateway strip (2 cells : (3,0) (4,0)). Resolves to
    ///         (3.5, 0) -- reads "north gate".</item>
    ///   <item><see cref="DistrictType.Outskirts"/> -- centroid of the
    ///         NW outskirts cluster (3 cells : (1,1) (2,1) (1,2)).
    ///         Resolves to (~1.33, ~1.33) -- reads "north-west
    ///         faubourg".</item>
    ///   <item><see cref="DistrictType.HinterlandAgri"/> -- centroid of
    ///         the NORTH-WEST agri sub-cluster (3 cells : (0,0) (1,0)
    ///         (2,0)). Resolves to (1, 0) -- reads "north-west
    ///         fields".</item>
    ///   <item><see cref="DistrictType.Littoral"/> -- centroid of the
    ///         WEST coastal strip (4 cells : col=0 rows 2..5). Resolves
    ///         to (0, 3.5) -- reads "west coast".</item>
    /// </list>
    ///
    /// <para>
    /// <b>Out-of-bounds defensive return.</b> An unknown enum value
    /// (stale persisted int) returns the grid centre (3.5, 3.5) rather
    /// than throwing -- the caller can still spawn the marker without
    /// a crash and the centre position is the least surprising fallback.
    /// </para>
    ///
    /// <para>
    /// <b>Why a static lookup and not a runtime computation.</b> The
    /// district topology is Varn-locked authoring data, not engineered
    /// geometry. Re-deriving the "which zone is primary" rule at runtime
    /// would either need extra metadata (a zone-id per cell, off-spec)
    /// or a connected-components algorithm whose tie-break rule would
    /// have to BE the lookup anyway. Authoring the 6 entries directly
    /// is the readable path -- a Varn reading the source can sanity-
    /// check the centroids against the ASCII layout in seconds.
    /// </para>
    /// </summary>
    public static (float Col, float Row) DistrictCentroid(DistrictType district) => district switch
    {
        // Intramuros : true centroid of 12 contiguous central cells.
        // 12 cells = 4 inner ((3,3) (4,3) (3,4) (4,4)) + 8 ring around
        // them. Sum col = 3+4+3+4 (inner) + 3+4+2+5+2+5+3+4 (ring) = 14+28=42.
        // Sum row = 3+3+4+4 (inner) + 2+2+3+3+4+4+5+5 (ring) = 14+28=42.
        // Mean = (42/12, 42/12) = (3.5, 3.5).
        DistrictType.Intramuros     => (3.5f, 3.5f),

        // Wall NORTH zone : (3,1) (4,1) (2,2) (5,2). Mean = ((3+4+2+5)/4,
        // (1+1+2+2)/4) = (3.5, 1.5). Visually reads "north wall section",
        // which is the strongest masonry stripe of the Halfgate ring.
        DistrictType.Wall           => (3.5f, 1.5f),

        // Gateway NORTH strip : (3,0) (4,0). Mean = (3.5, 0). Reads
        // "north gate". The Gateway district has 3 other zones (south
        // strip + east + west single gates) ; the north strip is the
        // narratively dominant one (the cardinal "front door" of
        // Halfgate, per Varn's roadmap-from-the-coast description).
        DistrictType.Gateway        => (3.5f, 0.0f),

        // Outskirts NW cluster : (1,1) (2,1) (1,2). Mean = ((1+2+1)/3,
        // (1+1+2)/3) = (4/3, 4/3) ~= (1.333, 1.333). Reads "north-west
        // faubourg".
        DistrictType.Outskirts      => (4.0f / 3.0f, 4.0f / 3.0f),

        // HinterlandAgri NW sub-cluster : row 0 cols 0,1,2. Mean =
        // ((0+1+2)/3, 0) = (1, 0). The full N+S+corners zone has 20
        // cells with a true centroid of (3.5, 3.5) (cardinal symmetry),
        // so we deliberately pick the NW corner sub-cluster for the
        // marker. Reads "north-west fields".
        DistrictType.HinterlandAgri => (1.0f, 0.0f),

        // Littoral WEST strip : (0,2) (0,3) (0,4) (0,5). Mean = (0,
        // (2+3+4+5)/4) = (0, 3.5). Reads "west coast / quays". The east
        // strip is the mirror but the west is the narratively dominant
        // one (where the docks face the open sea per Varn's
        // micro-topology).
        DistrictType.Littoral       => (0.0f, 3.5f),

        // Defensive : unknown enum value (stale persisted int) returns
        // the grid centre rather than throwing. The marker still spawns
        // somewhere visible while a higher layer logs the typo.
        _ => (3.5f, 3.5f),
    };
}
