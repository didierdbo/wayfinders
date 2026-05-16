using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Varn-locked hardcoded authoring for the 3 POC NPCs hoverable on the
/// E2.1 Halfgate area map. Each NPC carries the A4.3-locked schema
/// extensions (<c>known_tiles : list[CellCoord]</c> and
/// <c>district_origin : DistrictType</c>) used by the future recruitment
/// reveal mechanic (E2.2).
///
/// <para>
/// <b>E2.1 scope.</b> Hover on portrait shows name + class +
/// district_origin tooltip. No recruit button, no <c>known_tiles</c>
/// merge -- those land at E2.2. The <c>known_tiles</c> lists are
/// authored now so the day E2.2 wires the Recruit button, the partial
/// reveal mechanic just works (Varn jalons §E2.1 "the recruitment
/// success path is wired but only fully demonstrated at E2.2").
/// </para>
///
/// <para>
/// <b>Why a static class.</b> Same concrete-first rationale as
/// <see cref="NpcCatalog"/> + <see cref="AreaGridLogic"/> +
/// <see cref="HalfgateMissionAuthoring"/>. The 3 NPCs are engineering
/// data ; promoting to a Godot Resource invites typos and adds
/// import-roundtrip cost for every <c>known_tiles</c> tweak. When E2.4
/// lands the ML incarnate endpoint, the NPC shape will live as a typed
/// DTO flowing from Tess's API, and this static authoring becomes the
/// offline / fallback path (Varn §E2.4 "<c>known_tiles</c> ARE applied
/// locally from the Godot Resource authored at E2.1").
/// </para>
///
/// <para>
/// <b>Three POC NPCs (Varn jalons §E2.1 placeholders).</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="ScoutMaeren"/> : Scout, district_origin =
///         Outskirts. known_tiles = ~6 cells of the western outskirts +
///         main road (paths-knower).</item>
///   <item><see cref="WarriorOlbrec"/> : Warrior, district_origin =
///         Wall. known_tiles = ~5 cells of the wall ring + west gate
///         vicinity.</item>
///   <item><see cref="ScholarVialis"/> : Scholar, district_origin =
///         Intramuros. known_tiles = ~8 cells spread across multiple
///         districts (the cartographer knows broadly, shallowly).</item>
/// </list>
///
/// <para>
/// All names are Varn placeholders per the open question §10 of the
/// E2 spec ; default if no rename = keep as-is.
/// </para>
/// </summary>
public static class HalfgateNpcAuthoring
{
    /// <summary>Locked NPC id for Maeren.</summary>
    public const string MaerenId = "npc_halfgate_01";

    /// <summary>Locked NPC id for Olbrec.</summary>
    public const string OlbrecId = "npc_halfgate_02";

    /// <summary>Locked NPC id for Vialis.</summary>
    public const string VialisId = "npc_halfgate_03";

    /// <summary>
    /// Scout. Western outskirts native, knows the paths in and around
    /// the western faubourgs + the main road. 6 cells of known_tiles.
    /// </summary>
    public static HalfgateNpc ScoutMaeren { get; } = new(
        NpcId: MaerenId,
        DisplayName: "Maeren d'Esca",
        Class: "Scout",
        DistrictOrigin: DistrictType.Outskirts,
        // 6 cells, mostly western outskirts + main road in/out.
        KnownTiles: new List<GridCoord>
        {
            new(1, 1), new(1, 2), new(1, 5), new(1, 6), new(0, 3), new(0, 4),
        },
        // Portrait sits on a western outskirts cell (a Scout would not
        // pose for her portrait under a gate post). The E2.1 portrait
        // hover tooltip reads display_name + class + district_origin off
        // this record.
        PortraitCell: new GridCoord(1, 2));

    /// <summary>
    /// Warrior. West-gate guard. Knows the wall ring around the western
    /// quadrant + the western gate. 5 cells of known_tiles.
    /// </summary>
    public static HalfgateNpc WarriorOlbrec { get; } = new(
        NpcId: OlbrecId,
        DisplayName: "Olbrec Cendrechant",
        Class: "Warrior",
        DistrictOrigin: DistrictType.Wall,
        // 5 cells, all in the western wall + west gate cluster.
        KnownTiles: new List<GridCoord>
        {
            new(1, 3), new(2, 2), new(3, 1), new(3, 6), new(2, 5),
        },
        // Portrait sits on the west gate.
        PortraitCell: new GridCoord(1, 3));

    /// <summary>
    /// Scholar, itinerant cartographer. Knows broad but shallow -- 8
    /// cells spread across multiple districts. Intramuros native.
    /// </summary>
    public static HalfgateNpc ScholarVialis { get; } = new(
        NpcId: VialisId,
        DisplayName: "Vialis the Dry",
        Class: "Scholar",
        DistrictOrigin: DistrictType.Intramuros,
        // 8 cells, spread : intramuros + 2 wall + 2 littoral + 2 gateway
        // + 1 hinterland. The cartographer knows everywhere a little.
        KnownTiles: new List<GridCoord>
        {
            new(3, 3), new(4, 4), new(2, 3), new(5, 4),
            new(0, 2), new(7, 5), new(3, 0), new(0, 0),
        },
        // Portrait sits on an intramuros cell, adjacent to the centre.
        PortraitCell: new GridCoord(3, 3));

    /// <summary>
    /// Enumerate the 3 POC NPCs in a stable order. Used by the E2AreaMap
    /// portrait spawn loop.
    /// </summary>
    public static IReadOnlyList<HalfgateNpc> All { get; } = new List<HalfgateNpc>
    {
        ScoutMaeren, WarriorOlbrec, ScholarVialis,
    };

    /// <summary>
    /// Lookup an NPC by id, or null if unknown. Defensive shape mirrors
    /// <see cref="NpcCatalog.Resolve"/>.
    /// </summary>
    public static HalfgateNpc? FindById(string? npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return null;
        foreach (var n in All) if (n.NpcId == npcId) return n;
        return null;
    }
}

/// <summary>
/// Pure-data record carrying one NPC entry. Encoded against the A4.3
/// schema. Used by <see cref="HalfgateNpcAuthoring"/> for the 3 POC NPCs ;
/// will become the local-fallback shape when E2.4 ships the ML
/// incarnate endpoint that returns the same fields server-side.
/// </summary>
/// <param name="NpcId">Stable NPC identifier (matches <see cref="NpcCatalog"/>
/// id convention).</param>
/// <param name="DisplayName">Player-facing name.</param>
/// <param name="Class">Class label (Scout / Warrior / Scholar at E2.1).</param>
/// <param name="DistrictOrigin">A4.3 : <c>district_origin</c>, one of the
/// 6 Varn-locked <see cref="DistrictType"/> values.</param>
/// <param name="KnownTiles">A4.3 : <c>known_tiles</c>, EXPLICIT list (not
/// computed). Merged into the Company's partial set on recruitment
/// (E2.2 wiring).</param>
/// <param name="PortraitCell">Grid cell where the portrait sprite is
/// rendered at E2.1. Pure E2.1-rendering concern -- not part of A4.3.</param>
public sealed record HalfgateNpc(
    string NpcId,
    string DisplayName,
    string Class,
    DistrictType DistrictOrigin,
    IReadOnlyList<GridCoord> KnownTiles,
    GridCoord PortraitCell);
