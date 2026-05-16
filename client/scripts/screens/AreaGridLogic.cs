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
}
