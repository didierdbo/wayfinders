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
/// <b>E2.1c profond refonte (2026-05-16, Didier lock).</b> The first
/// E2.1c pass modelled the grid as 6 disjoint 2×2 district blocks (24
/// attributed cells, 40 unattributed). Didier reviewed the visual result
/// and ratified two structural changes that drop the 2×2 block constraint :
/// <list type="bullet">
///   <item><b>District-to-cells = explicit irregular shapes.</b> Each
///         district owns a Varn-locked LIST of cells, not a 2×2 block.
///         Intramuros is an 11-cell central body, Wall an 8-cell ring,
///         Outskirts a 6-cell SW faubourg, Littoral a 6-cell west coast,
///         Gateway a 4-cell north strip, HinterlandAgri a 4-cell NE
///         field cluster. Total = 39 attributed cells ; the remaining 25
///         are unattributed ("space between quartiers"), routed to a
///         dedicated neutral placeholder bitmap at render time -- NOT to
///         the E1 fog bitmap (that one stays reserved for the
///         <see cref="TileRevealState.Fog"/> visual state).</item>
///   <item><b>POI marker anchor = 1 of 16 zone-centres.</b> The 8×8 grid
///         is partitioned into 16 axis-aligned 2×2 zones. A POI marker
///         sits at the geometric centre of ONE zone (= the (col, row)
///         pair <c>(2*zCol + 1.0, 2*zRow + 1.0)</c> with
///         <c>zCol, zRow ∈ [0, 3]</c>). A district is "eligible" for a
///         zone if AT LEAST ONE of the zone's 4 cells belongs to the
///         district -- no requirement that all 4 cells do.
///         <see cref="DistrictCentroid"/> returns the Varn-locked zone
///         pick per district ; the choice is encoded as a single zone per
///         district (not a list of eligible zones) so the lookup is
///         total + deterministic + xUnit-pinned.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Grid layout (E2.1c profond refonte).</b> Letters mark the 39
/// district cells ; dots mark the 25 neutral cells.
/// </para>
/// <code>
///   col  0  1  2  3  4  5  6  7
/// row
///  0    .  .  .  G  G  .  .  .    G = Gateway        (4 cells)
///  1    .  .  .  G  G  H  H  .    H = HinterlandAgri (4 cells)
///  2    L  .  I  I  I  H  H  .    L = Littoral       (6 cells)
///  3    L  L  I  I  I  W  W  .    I = Intramuros    (11 cells)
///  4    L  L  I  I  I  W  W  .    W = Wall           (8 cells)
///  5    L  O  O  I  I  W  .  .    O = Outskirts      (6 cells)
///  6    .  O  O  W  W  W  .  .    .  = Neutral      (25 cells)
///  7    .  O  O  .  .  .  .  .
/// </code>
///
/// <para>
/// <b>District placement rationale.</b>
/// <list type="bullet">
///   <item><b>Intramuros</b> -- the walled centre. 3×3 core at cols 2-4,
///         rows 2-4, plus a 2-cell southward bulge at (3,5)(4,5) so the
///         city body reads asymmetric (the wall hugs more tightly to the
///         south, the gate punctures the north).</item>
///   <item><b>Wall</b> -- the masonry ring. Wraps Intramuros on the east
///         (cols 5-6, rows 3-4) and the south (cols 3-5 row 6, plus
///         (5,5)). 8 cells, deliberately L-shaped so the ring reads as
///         "city is defended SE first" rather than uniformly
///         concentric.</item>
///   <item><b>Gateway</b> -- the north cardinal piercing. (3,0)(4,0)(3,1)(4,1).
///         Aligned with the Intramuros central column so the gate-to-core
///         axis is clean from above.</item>
///   <item><b>HinterlandAgri</b> -- the NE field cluster, outside the wall
///         beyond the gate. Cols 5-6, rows 1-2.</item>
///   <item><b>Outskirts</b> -- the SW faubourg, workshop / road-approach
///         shape. Cols 1-2, rows 5-7. 2×3 block tilted south because the
///         shoreline pushes the city east.</item>
///   <item><b>Littoral</b> -- the west coast. Col 0 (full strip rows 2-5)
///         + the col 1 bulge at rows 3-4. Reads as "marshy edge that
///         creeps inland in the middle".</item>
///   <item><b>Neutral (25 cells)</b> -- the unattributed space :
///         intra-mural gaps, behind-the-wall buffers, beyond-the-fields
///         dust, north-quadrant approach. Visually rendered with a
///         dedicated <c>e2.area_grid.tile.neutral</c> bitmap (urban beige)
///         -- distinct from the E1 fog bitmap which now stays reserved for
///         the <see cref="TileRevealState.Fog"/> state alone.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Zone-centre POI placements (Varn-locked).</b> Each of the 16
/// axis-aligned 2×2 zones has a centre at fractional grid coord
/// <c>(2*zCol + 1.0, 2*zRow + 1.0)</c>. Among the zones eligible for each
/// district (= containing ≥ 1 cell of the district), one is locked as the
/// canonical POI anchor :
/// <list type="bullet">
///   <item><b>Intramuros</b> -- zone (1,1) = centre (3.0, 3.0). The only
///         zone where all 4 cells are Intramuros (the geometric heart of
///         the centre body).</item>
///   <item><b>Wall</b> -- zone (2,2) = centre (5.0, 5.0). Reads as
///         "SE corner of the ring" -- the wall's most loaded segment.</item>
///   <item><b>Gateway</b> -- zone (1,0) = centre (3.0, 1.0). All Gateway
///         cells live in this zone or zone (2,0) ; (1,0) gives a
///         centroid that lines up with the Intramuros (3,3) on a clean
///         N-S axis. Visually : "gate above the core".</item>
///   <item><b>Outskirts</b> -- zone (0,2) = centre (1.0, 5.0). The
///         northern edge of the SW faubourg ; centroid pulls slightly
///         toward Intramuros so the marker reads as "outskirts adjacent
///         to the wall", not "lost in the SW corner".</item>
///   <item><b>HinterlandAgri</b> -- zone (2,0) = centre (5.0, 1.0).
///         Mirror image of Gateway's anchor on the east side. Lines up
///         with the Wall anchor on a clean N-S axis.</item>
///   <item><b>Littoral</b> -- zone (0,1) = centre (1.0, 3.0). The
///         densest Littoral zone (3 of 4 cells = Littoral). Mirror image
///         of Wall's anchor on the west side.</item>
/// </list>
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
    /// Side length of one POI zone, in cells. Locked at 2 : the 8×8 grid
    /// is partitioned into a 4×4 lattice of axis-aligned 2×2 zones (16
    /// zones, 16 zone-centres). A POI marker sits at the centre of one
    /// zone, NOT at a cell centre, so the visual reads as "this zone of
    /// the city" rather than "this specific tile". A future jalon that
    /// wants per-cell markers (e.g. a quest objective on one tile) would
    /// bypass <see cref="DistrictCentroid"/> entirely, not change this
    /// constant.
    /// </summary>
    public const int ZoneSize = 2;

    /// <summary>
    /// Number of zones per axis = <see cref="GridSize"/> / <see cref="ZoneSize"/>
    /// = 4. The grid has 4×4 = 16 zones total.
    /// </summary>
    public const int ZonesPerAxis = GridSize / ZoneSize;

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
    /// so callers can be lazy about bounds).
    ///
    /// <para>
    /// At E2.1c profond refonte only 39 of the 64 cells belong to a
    /// district ; the other 25 fall through this fallback by design
    /// ("space between quartiers" semantics). The unattributed-vs-actual-
    /// Outskirts ambiguity is settled by <see cref="IsInDistrict"/> --
    /// see its XML doc.
    /// </para>
    /// </summary>
    public static DistrictType ResolveDistrictType(GridCoord coord)
    {
        if (!IsInBounds(coord)) return DistrictType.Outskirts;

        foreach (var (district, cells) in DistrictCellsTable)
        {
            foreach (var c in cells)
            {
                if (c == coord) return district;
            }
        }
        return DistrictType.Outskirts;
    }

    /// <summary>
    /// True if the cell is explicitly authored into one of the 6
    /// district cell lists (the 39 attributed cells), false otherwise
    /// (the 25 unattributed neutral cells + every out-of-bounds coord).
    ///
    /// <para>
    /// <b>Why a separate helper and not "is ResolveDistrictType == X".</b>
    /// <see cref="ResolveDistrictType"/> is total by contract : it returns
    /// <see cref="DistrictType.Outskirts"/> as the defensive fallback for
    /// unattributed AND out-of-bounds coords, AND it returns
    /// <see cref="DistrictType.Outskirts"/> legitimately for the 6 cells of
    /// the actual Outskirts faubourg. Callers that need to discriminate
    /// "this cell IS in a district" from "this cell is unattributed and
    /// fell back to Outskirts" cannot do it on the enum alone -- the two
    /// cases collapse to the same value. This helper is the explicit seam :
    /// it scans the same <see cref="DistrictCellsTable"/> and returns a
    /// bool that does NOT suffer the fallback collision.
    /// </para>
    ///
    /// <para>
    /// <b>E2.1c profond patch consumer (2026-05-16).</b> The E2AreaMap
    /// tile spawn loop uses this helper to choose between the per-district
    /// iso bitmap (when true) and the dedicated NEUTRAL iso bitmap (when
    /// false). The 25 neutral cells use a new
    /// <c>e2.area_grid.tile.neutral</c> placeholder ; the E1 fog bitmap
    /// is no longer reused for them (it stays reserved for the
    /// <see cref="TileRevealState.Fog"/> state alone).
    /// </para>
    /// </summary>
    public static bool IsInDistrict(GridCoord coord)
    {
        if (!IsInBounds(coord)) return false;

        foreach (var (_, cells) in DistrictCellsTable)
        {
            foreach (var c in cells)
            {
                if (c == coord) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// District cell authoring : one entry per district, each a
    /// Varn-locked list of <see cref="GridCoord"/> cells. The 6 lists are
    /// pairwise disjoint -- no cell appears in two districts. Total = 39
    /// cells ; the remaining 25 are the unattributed neutral set.
    ///
    /// <para>
    /// <b>Why a tuple list and not a Dictionary.</b> The list is read in
    /// district-declaration order in <see cref="ResolveDistrictType"/>
    /// (early-exit per cell), and indexed by district in
    /// <see cref="CellsOfDistrict"/>. A Dictionary would add allocation
    /// overhead for no gain at 6 entries. The lists themselves are
    /// readonly arrays so the authoring cannot drift at runtime.
    /// </para>
    ///
    /// <para>
    /// <b>Authoring shape rationale (per the class XML doc grid layout).</b>
    /// Districts are no longer uniform 2×2 blocks ; each district has a
    /// shape that reads visually as its narrative role (Intramuros = a
    /// compact body, Wall = an L-shaped ring, Littoral = a vertical strip
    /// with a midriff bulge, etc.). The shape matters more than the cell
    /// count : Mira's per-district bitmap pass at E2.1d+ will paint each
    /// shape distinctly, so changing a cell here means changing the
    /// district silhouette the player will eventually see.
    /// </para>
    /// </summary>
    private static readonly (DistrictType District, GridCoord[] Cells)[] DistrictCellsTable = new[]
    {
        // Intramuros (11 cells) -- 3×3 core cols 2-4 rows 2-4 + 2-cell
        // south bulge (3,5)(4,5). The bulge makes the body asymmetric so
        // the wall reads as "tighter to the south".
        (DistrictType.Intramuros, new GridCoord[]
        {
            new(2, 2), new(3, 2), new(4, 2),
            new(2, 3), new(3, 3), new(4, 3),
            new(2, 4), new(3, 4), new(4, 4),
            new(3, 5), new(4, 5),
        }),

        // Wall (8 cells) -- L-shaped ring east + south of Intramuros.
        // East flank cols 5-6 rows 3-4 (4 cells) + south flank
        // (3,6)(4,6)(5,5)(5,6) (4 cells). Reads as "defended SE first".
        (DistrictType.Wall, new GridCoord[]
        {
            new(5, 3), new(6, 3),
            new(5, 4), new(6, 4),
            new(5, 5),
            new(3, 6), new(4, 6), new(5, 6),
        }),

        // Gateway (4 cells) -- north strip aligned with Intramuros central
        // column. (3,0)(4,0)(3,1)(4,1).
        (DistrictType.Gateway, new GridCoord[]
        {
            new(3, 0), new(4, 0),
            new(3, 1), new(4, 1),
        }),

        // Outskirts (6 cells) -- SW faubourg, cols 1-2 rows 5-7. 2×3 block
        // tilted south because the shoreline pushes the city east.
        (DistrictType.Outskirts, new GridCoord[]
        {
            new(1, 5), new(2, 5),
            new(1, 6), new(2, 6),
            new(1, 7), new(2, 7),
        }),

        // HinterlandAgri (4 cells) -- NE field cluster, cols 5-6 rows 1-2.
        // Outside the wall, beyond the gate.
        (DistrictType.HinterlandAgri, new GridCoord[]
        {
            new(5, 1), new(6, 1),
            new(5, 2), new(6, 2),
        }),

        // Littoral (6 cells) -- west coast. Col 0 full strip rows 2-5 (4
        // cells) + col 1 midriff bulge rows 3-4 (2 cells). Reads as
        // "marshy edge that creeps inland in the middle".
        (DistrictType.Littoral, new GridCoord[]
        {
            new(0, 2), new(0, 3), new(0, 4), new(0, 5),
            new(1, 3), new(1, 4),
        }),
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
    /// between (3,3) and (4,4) ; we pick (4,4) as the canonical anchor.
    /// Used by the legacy boot tuto footprint and by future autoload-life
    /// NPC spawn logic. At E2.1c profond refonte (4,4) sits inside the
    /// Intramuros 11-cell body, which is the intended semantic.
    /// </summary>
    public static GridCoord Centre => new(4, 4);

    /// <summary>
    /// Resolve the 4-cell footprint surrounding the centre of the grid
    /// (the canonical "POI footprint" for the Halfgate boot tuto
    /// mission). Returns (3,3)(3,4)(4,3)(4,4) -- the four central tiles,
    /// all in the Intramuros 11-cell body at E2.1c profond refonte. A4.2
    /// reveal grammar consumes this list as
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
    /// <see cref="AllCells"/>). Cell counts at E2.1c profond refonte :
    /// Intramuros 11, Wall 8, Gateway 4, Outskirts 31 (= 6 authored + 25
    /// neutral fallback), HinterlandAgri 4, Littoral 6.
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
    /// POI marker (E2.1c profond refonte Didier lock 2026-05-16). Returns
    /// a FRACTIONAL position that is GUARANTEED to be one of the 16 zone
    /// centres on the 8×8 grid -- i.e.
    /// <c>(2*zCol + 1.0, 2*zRow + 1.0)</c> for some integer
    /// <c>zCol, zRow ∈ [0, 3]</c>. The chosen zone is also guaranteed to
    /// contain at least one cell of the district (the
    /// "zone-eligibility" invariant pinned by
    /// <c>District_centroid_zone_contains_at_least_one_cell_of_district</c>
    /// in <see cref="HalfgateE2MissionAuthoringTests"/>).
    ///
    /// <para>
    /// <b>Why one zone per district and not "any eligible zone".</b> The
    /// lookup must be total + deterministic so the spawn code can pin
    /// every test step without authoring randomness. Encoding one zone
    /// per district as a hardcoded switch makes the choice xUnit-visible
    /// (a silent edit shows up red), keeps the spawn loop free of
    /// "first match wins" ordering surprises, and lets us re-author the
    /// pick at any time by editing the switch -- the zone-eligibility
    /// invariant test catches a typo (a zone with zero cells of that
    /// district).
    /// </para>
    ///
    /// <para>
    /// <b>Locked zone picks (see class XML doc §Zone-centre POI placements
    /// for the narrative rationale).</b>
    /// <list type="bullet">
    ///   <item><b>Intramuros</b> → zone (1,1), centroid (3.0, 3.0).</item>
    ///   <item><b>Wall</b> → zone (2,2), centroid (5.0, 5.0).</item>
    ///   <item><b>Gateway</b> → zone (1,0), centroid (3.0, 1.0).</item>
    ///   <item><b>Outskirts</b> → zone (0,2), centroid (1.0, 5.0).</item>
    ///   <item><b>HinterlandAgri</b> → zone (2,0), centroid (5.0, 1.0).</item>
    ///   <item><b>Littoral</b> → zone (0,1), centroid (1.0, 3.0).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Out-of-bounds defensive return.</b> An unknown enum value
    /// (stale persisted int) returns zone (1,1)'s centroid (3.0, 3.0) --
    /// the most central position on the 16-slot lattice -- rather than
    /// throwing. The caller can still spawn the marker without a crash.
    /// </para>
    /// </summary>
    public static (float Col, float Row) DistrictCentroid(DistrictType district)
    {
        var (zCol, zRow) = district switch
        {
            DistrictType.Intramuros     => (1, 1),
            DistrictType.Wall           => (2, 2),
            DistrictType.Gateway        => (1, 0),
            DistrictType.Outskirts      => (0, 2),
            DistrictType.HinterlandAgri => (2, 0),
            DistrictType.Littoral       => (0, 1),
            _                            => (1, 1),
        };
        // Zone (zCol, zRow) covers cells (2*zCol, 2*zRow) through
        // (2*zCol+1, 2*zRow+1). Its centre is the (col, row) at
        // (2*zCol + 1.0, 2*zRow + 1.0) -- exactly between the 4 cells.
        return (ZoneSize * zCol + 1.0f, ZoneSize * zRow + 1.0f);
    }

    /// <summary>
    /// Enumerate every cell that belongs to the 2×2 zone identified by
    /// <paramref name="zCol"/>, <paramref name="zRow"/>. Returns 4 cells
    /// in row-major order : (2*zCol, 2*zRow), (2*zCol+1, 2*zRow),
    /// (2*zCol, 2*zRow+1), (2*zCol+1, 2*zRow+1). Out-of-range zone
    /// indices yield an empty enumeration (defensive total contract).
    ///
    /// <para>
    /// <b>Why a helper and not inline arithmetic.</b> The
    /// <see cref="DistrictCentroid"/> contract pins the zone-eligibility
    /// invariant ("the picked zone contains at least one cell of the
    /// district"). The test pinning that invariant needs to walk the
    /// zone's 4 cells ; centralising the enumeration here means the
    /// (zCol, zRow) → cell-list math is defined in ONE place and the
    /// test cannot diverge from the centroid math.
    /// </para>
    /// </summary>
    public static IEnumerable<GridCoord> CellsOfZone(int zCol, int zRow)
    {
        if (zCol < 0 || zCol >= ZonesPerAxis || zRow < 0 || zRow >= ZonesPerAxis)
        {
            yield break;
        }
        for (var dr = 0; dr < ZoneSize; dr++)
        {
            for (var dc = 0; dc < ZoneSize; dc++)
            {
                yield return new GridCoord(ZoneSize * zCol + dc, ZoneSize * zRow + dr);
            }
        }
    }
}
