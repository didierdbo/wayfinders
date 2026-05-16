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
/// <b>E2.1c refonte (2026-05-16, Didier lock).</b> The first E2.1c pass
/// modelled the grid as a city-with-wall pattern (cardinal-symmetric ring)
/// and authored per-district centroids picked from the "primary zone"
/// of each district. Didier reviewed the visual result and ratified a
/// simpler MVE shape :
/// <list type="bullet">
///   <item><b>No per-district visual tint.</b> All 64 cells share the
///         same umber-terracotta wash (<c>district_tint</c> shader uniform
///         pushed to white = identity at the spawn site). The per-district
///         visual consistency will come from Mira's per-district bitmaps
///         at E2.1d+ -- the tint uniform stays in the shader, prepared
///         for that pass, but does not paint anything visible today.</item>
///   <item><b>District-to-cells = simple 2×2 blocks.</b> Each of the 6
///         districts owns a single, contiguous 2×2 block of 4 cells.
///         The 6 blocks together cover 24 of the 64 cells ; the
///         remaining 40 cells are unattributed at this jalon ("space
///         between quartiers" semantics for the MVE).</item>
///   <item><b>POI marker anchor = corner intersection of the 2×2 block</b>
///         = the geometric centre of the 4 cells, lands BETWEEN them.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Grid layout (2×2 block authoring per E2.1c refonte).</b> Letters
/// mark the 24 district cells ; dots mark the 40 unattributed cells.
/// </para>
/// <code>
///   col  0  1  2  3  4  5  6  7
/// row
///  0    .  .  .  G  G  .  .  .    G = Gateway       block (3,0)(4,0)(3,1)(4,1)
///  1    .  .  .  G  G  .  .  .    H = HinterlandAgri block (5,1)(6,1)(5,2)(6,2)
///  2    .  .  .  .  .  H  H  .    L = Littoral      block (0,3)(1,3)(0,4)(1,4)
///  3    L  L  .  I  I  .  .  .    I = Intramuros    block (3,3)(4,3)(3,4)(4,4)
///  4    L  L  .  I  I  .  .  .    W = Wall          block (3,5)(4,5)(3,6)(4,6)
///  5    .  O  O  W  W  .  .  .    O = Outskirts     block (1,5)(2,5)(1,6)(2,6)
///  6    .  O  O  W  W  .  .  .
///  7    .  .  .  .  .  .  .  .
/// </code>
///
/// <para>
/// <b>2×2 block placement rationale.</b>
/// <list type="bullet">
///   <item><b>Intramuros</b> -- centre of the 8×8 grid (rows 3-4, cols
///         3-4). The city core, true geometric centre.</item>
///   <item><b>Wall</b> -- immediately south of Intramuros (rows 5-6, cols
///         3-4). The masonry ring band, adjacent to the centre.</item>
///   <item><b>Gateway</b> -- north strip (rows 0-1, cols 3-4). The
///         cardinal "front door" of Halfgate, aligned with the city's
///         north-south axis through Intramuros.</item>
///   <item><b>Outskirts</b> -- south-west faubourg (rows 5-6, cols 1-2).
///         Outside the wall, west of it -- the workshop / road-approach
///         cluster.</item>
///   <item><b>HinterlandAgri</b> -- north-east fields (rows 1-2, cols
///         5-6). Outside the wall, beyond the gateway.</item>
///   <item><b>Littoral</b> -- west coast (rows 3-4, cols 0-1). The
///         seaward edge facing the open water.</item>
/// </list>
/// The 6 blocks are pairwise disjoint (no shared cells), which means
/// each cell belongs to at most one district. <see cref="CellsOfDistrict"/>
/// returns exactly 4 cells per district at E2.1c ; the unattributed 40
/// cells return <see cref="DistrictType.Outskirts"/> from
/// <see cref="ResolveDistrictType"/> via the defensive fallback path
/// (NOT because they are Outskirts -- because the helper is total).
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
    /// <see cref="DistrictType.Outskirts"/> for out-of-bounds coords AND
    /// for unattributed in-bounds cells (defensive : the helper is total
    /// so callers can be lazy about bounds). At E2.1c only 24 of the 64
    /// cells belong to a district block ; the other 40 fall through this
    /// fallback by design (MVE "space between quartiers" semantics).
    ///
    /// <para>
    /// See the class XML doc for the visual ASCII layout. The lookup is
    /// driven by <see cref="DistrictBlocks"/> -- each entry is a (top-left
    /// col, top-left row) anchor of a 2×2 block.
    /// </para>
    /// </summary>
    public static DistrictType ResolveDistrictType(GridCoord coord)
    {
        if (!IsInBounds(coord)) return DistrictType.Outskirts;

        foreach (var (district, blockCol, blockRow) in DistrictBlocks)
        {
            if (coord.Col >= blockCol && coord.Col < blockCol + BlockSize
                && coord.Row >= blockRow && coord.Row < blockRow + BlockSize)
            {
                return district;
            }
        }
        return DistrictType.Outskirts;
    }

    /// <summary>
    /// True if the cell sits inside one of the 6 Varn-locked
    /// <see cref="DistrictBlocks"/> 2×2 blocks (the 24 attributed cells),
    /// false otherwise (the 40 unattributed "space between quartiers"
    /// cells, plus every out-of-bounds coord).
    ///
    /// <para>
    /// <b>Why a separate helper and not "is ResolveDistrictType == X".</b>
    /// <see cref="ResolveDistrictType"/> is total by contract : it returns
    /// <see cref="DistrictType.Outskirts"/> as the defensive fallback for
    /// unattributed AND out-of-bounds coords, AND it returns
    /// <see cref="DistrictType.Outskirts"/> legitimately for the 4 cells of
    /// the actual Outskirts 2×2 block. The xUnit suite pins this contract
    /// (see <c>AreaGridLogicTests.ResolveDistrictType_falls_back_to_outskirts_for_unattributed_cells</c>).
    /// Callers that need to discriminate "this cell IS in a district block"
    /// from "this cell is unattributed and fell back to Outskirts" cannot
    /// do it on the enum alone -- the two cases collapse to the same value.
    /// This helper is the explicit seam : it scans the same
    /// <see cref="DistrictBlocks"/> table and returns a bool that does NOT
    /// suffer the fallback collision.
    /// </para>
    ///
    /// <para>
    /// <b>E2.1c patch consumer (2026-05-16).</b> The E2AreaMap tile spawn
    /// loop uses this helper to choose between the per-district iso bitmap
    /// (when true) and the E1 neutral fog bitmap (when false). Pre-patch,
    /// every cell rendered the E1 bitmap regardless ; the 24 district
    /// cells now read as district-tinted iso losanges while the 40
    /// unattributed cells continue to read as the fog substrate.
    /// </para>
    /// </summary>
    public static bool IsInDistrictBlock(GridCoord coord)
    {
        if (!IsInBounds(coord)) return false;

        foreach (var (_, blockCol, blockRow) in DistrictBlocks)
        {
            if (coord.Col >= blockCol && coord.Col < blockCol + BlockSize
                && coord.Row >= blockRow && coord.Row < blockRow + BlockSize)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Side length of each district's 2×2 block. Locked at 2 per the
    /// E2.1c refonte ; a future jalon may grow blocks district-by-district
    /// (Wall could be a 2×4 ring, Littoral a 1×4 strip etc.) -- bumping
    /// this constant alone is NOT the way to do that (the per-district
    /// shape becomes heterogeneous, which means dropping the constant and
    /// authoring per-district cell lists). Kept as a constant here to
    /// signal "every district is the same shape at E2.1c".
    /// </summary>
    private const int BlockSize = 2;

    /// <summary>
    /// District block authoring : one entry per district = (district,
    /// top-left col, top-left row) anchor of a 2×2 block. The 6 entries
    /// are pairwise disjoint -- no two blocks share a cell. Consumed by
    /// <see cref="ResolveDistrictType"/> (linear scan, 6 entries, cheap
    /// even on hover) and by <see cref="DistrictCentroid"/> (lookup by
    /// district enum).
    ///
    /// <para>
    /// <b>Why a tuple list and not a Dictionary.</b> The list is read
    /// in row-major scan order in <see cref="ResolveDistrictType"/> ;
    /// indexing by district is a sub-microsecond switch in
    /// <see cref="DistrictCentroid"/>. A Dictionary would add allocation
    /// overhead for no gain at 6 entries.
    /// </para>
    /// </summary>
    private static readonly (DistrictType District, int Col, int Row)[] DistrictBlocks = new[]
    {
        // Centre of the grid : Intramuros 2×2 at (3,3)(4,3)(3,4)(4,4).
        (DistrictType.Intramuros,     3, 3),
        // Immediately south of Intramuros : Wall 2×2 at (3,5)(4,5)(3,6)(4,6).
        (DistrictType.Wall,           3, 5),
        // North strip : Gateway 2×2 at (3,0)(4,0)(3,1)(4,1).
        (DistrictType.Gateway,        3, 0),
        // South-west faubourg : Outskirts 2×2 at (1,5)(2,5)(1,6)(2,6).
        (DistrictType.Outskirts,      1, 5),
        // North-east fields : HinterlandAgri 2×2 at (5,1)(6,1)(5,2)(6,2).
        (DistrictType.HinterlandAgri, 5, 1),
        // West coast : Littoral 2×2 at (0,3)(1,3)(0,4)(1,4).
        (DistrictType.Littoral,       0, 3),
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
    /// (matches the E2.1c Intramuros 2×2 block which is centred on this
    /// coord). Used by the legacy boot tuto footprint and by future
    /// autoload-life NPC spawn logic.
    /// </summary>
    public static GridCoord Centre => new(4, 4);

    /// <summary>
    /// Resolve the 4-cell footprint surrounding the centre of the grid
    /// (the canonical "POI footprint" for the Halfgate boot tuto
    /// mission). Returns Intramuros cells (3,3) (3,4) (4,3) (4,4) -- the
    /// four central tiles of the city, which match the Intramuros 2×2
    /// block at E2.1c. A4.2 reveal grammar consumes this list as
    /// <c>pop_effect.tiles_to_partial</c>.
    /// </summary>
    public static IReadOnlyList<GridCoord> CentralPoiFootprint() => _centralFootprint;

    private static readonly IReadOnlyList<GridCoord> _centralFootprint = new List<GridCoord>
    {
        new(3, 3), new(4, 3), new(3, 4), new(4, 4),
    };

    /// <summary>
    /// Enumerate every cell that resolves to the given
    /// <see cref="DistrictType"/>. Row-major order (matches
    /// <see cref="AllCells"/>). At E2.1c this returns exactly 4 cells per
    /// district (the 2×2 block). Useful for centroid sanity checks,
    /// mission footprint visualisation, and the future per-district
    /// hover dispatcher.
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
    /// POI marker (E2.1c refonte Didier lock 2026-05-16). Returns a
    /// FRACTIONAL position equal to the CORNER INTERSECTION of the 4 cells
    /// of the district's 2×2 block -- i.e. the geometric centre of the
    /// block, which lands BETWEEN the 4 cells. This is exactly where the
    /// marker should sit visually : pinned to a cell centre would read
    /// "this specific cell" instead of "this 2×2 zone".
    ///
    /// <para>
    /// <b>Centroid formula.</b> For a 2×2 block with top-left anchor
    /// (blockCol, blockRow), the corner intersection sits at
    /// (blockCol + 1.0, blockRow + 1.0) -- the (col, row) of the
    /// shared corner of the 4 cells. Example : Intramuros block anchored
    /// at (3, 3) covers (3,3)(4,3)(3,4)(4,4) ; the corner intersection
    /// is at (4.0, 4.0).
    /// </para>
    ///
    /// <para>
    /// <b>Out-of-bounds defensive return.</b> An unknown enum value
    /// (stale persisted int) returns the grid centre (4.0, 4.0) rather
    /// than throwing -- the caller can still spawn the marker without a
    /// crash and the centre position is the least surprising fallback.
    /// </para>
    /// </summary>
    public static (float Col, float Row) DistrictCentroid(DistrictType district)
    {
        foreach (var (d, col, row) in DistrictBlocks)
        {
            if (d == district)
            {
                // Corner intersection of the 2×2 block = (anchor + 1.0, anchor + 1.0).
                return (col + 1.0f, row + 1.0f);
            }
        }
        // Defensive fallback : grid centre (4.0, 4.0) = corner of the
        // Intramuros block, the most central position on the grid.
        return (4.0f, 4.0f);
    }
}
