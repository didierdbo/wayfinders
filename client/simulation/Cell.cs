using System.Text.Json.Serialization;

namespace Wayfinders.Client.Simulation;

/// <summary>
/// Immutable per-cell state for the tactical simulation grid. One
/// <see cref="Cell"/> per <c>Vector2I</c> in <see cref="SimulationGrid"/>.
///
/// <para>
/// <b>What this is.</b> The data half of the visual/simulation split that
/// is the load-bearing lesson of Phase 5. The visual layer
/// (<see cref="Godot.TileMapLayer"/>) only knows about textures and atlas
/// coords. <see cref="Cell"/> is what the game logic reads to answer
/// "can a unit walk here? is anyone standing on it? how high is it? does
/// it grant cover for stealth?". The two layers are kept in sync via
/// explicit code paths, never by reading tile data back from the renderer.
/// </para>
///
/// <para>
/// <b>Record, not struct.</b> A 24×24 grid is 576 cells — small enough
/// that allocation pressure is irrelevant, and reference semantics avoid
/// the value-type copy footguns where mutating a returned cell silently
/// updates nothing. The <c>record</c> shape gives us value-equality
/// (two cells with identical fields compare equal — useful for diffs and
/// tests), <c>init</c>-only properties (immutable after construction),
/// and concise <c>with</c>-expression updates for the
/// <see cref="SimulationGrid.SetCell"/> flow ("give me this cell but with
/// a new occupant").
/// </para>
///
/// <para>
/// <b>Field selection rationale (Phase 5 L2 scope).</b> The four fields
/// below are the minimum set that lets L4 pathfinding and the UC1 stealth
/// flow operate without a second migration. Anything more speculative
/// (lighting, fog-of-war, scent, faction control) is YAGNI for now and
/// trivially added later via a new <c>init</c> property — records do not
/// punish growth.
/// </para>
///
/// <list type="bullet">
///   <item>
///     <term><see cref="Terrain"/></term>
///     <description>What kind of ground this is. Drives pathfinding cost
///     in L4 (loose scree slows; blocked is impassable) and the visual
///     tile picked in L3. Three values match Varn's MVP slice lock —
///     walkable / blocked / loose-scree — no more, no fewer.</description>
///   </item>
///   <item>
///     <term><see cref="OccupantId"/></term>
///     <description>Server-issued unit ID of the unit standing on this
///     cell, or <c>null</c> if empty. <c>string</c> rather than
///     <c>int</c> because the FastAPI side is moving toward UUID-shaped
///     IDs and we want the client to not care which shape it gets — the
///     wire is opaque to us.</description>
///   </item>
///   <item>
///     <term><see cref="Elevation"/></term>
///     <description>Integer height in tile-units. Ridge cells are
///     elevated relative to the approach corridor; UC1 stealth scoring
///     reads elevation when computing line-of-sight against sentinels.
///     Iso 2.5D is rendering convention (cf. Phase 5 pre-brief), but
///     gameplay-relevant verticality lives here, not in
///     <see cref="Godot.TileMapLayer"/>.</description>
///   </item>
///   <item>
///     <term><see cref="Cover"/></term>
///     <description>Cover level granted to a unit standing on this cell
///     against ranged attacks and stealth detection rolls. The UC1
///     classifier's context renderer reads this to feed prose like
///     "Kira crouches behind the boulder" into the ML pipeline.</description>
///   </item>
/// </list>
///
/// <para>
/// <b>JSON-serializable for L5+ persistence.</b> The
/// <see cref="JsonConstructorAttribute"/> on the primary constructor lets
/// <c>System.Text.Json</c> source-gen reconstruct <see cref="Cell"/> from
/// disk when save/load lands. Enums serialize as their string names
/// (configured at the serializer-options layer when persistence ships)
/// rather than ordinal ints — diff-friendly save files, and adding a new
/// terrain variant in the middle of the enum does not silently rewrite
/// every saved game.
/// </para>
///
/// <para>
/// <b>AOT note.</b> Records are AOT-clean. The compiler-generated
/// equality, <c>ToString</c>, and <c>with</c>-clone helpers are ordinary
/// strongly-typed code. The only AOT trap would be reflection-based JSON
/// at save-time — that is why <see cref="Wayfinders.Client.Services.Dtos.ApiJsonContext"/>
/// pattern will extend to a save-context partial class when persistence
/// ships, not now.
/// </para>
/// </summary>
/// <param name="Terrain">Terrain category for this cell.</param>
/// <param name="OccupantId">
/// Unit ID of the occupant, or <c>null</c> if the cell is empty.
/// </param>
/// <param name="Elevation">
/// Integer height in tile-units. Defaults to 0 for the approach corridor;
/// ridge cells use higher values.
/// </param>
/// <param name="Cover">
/// Cover level granted to a unit standing on this cell.
/// </param>
public sealed record Cell(
    [property: JsonPropertyName("terrain")] TerrainType Terrain,
    [property: JsonPropertyName("occupant_id")] string? OccupantId,
    [property: JsonPropertyName("elevation")] int Elevation,
    [property: JsonPropertyName("cover")] CoverLevel Cover
)
{
    /// <summary>
    /// Convenience factory for the most common cell shape: empty walkable
    /// ground, no elevation, no cover. Equivalent to
    /// <c>new Cell(TerrainType.Walkable, null, 0, CoverLevel.None)</c>
    /// but cheaper to read at call sites that stamp 500+ cells in a loop.
    /// </summary>
    public static Cell EmptyWalkable { get; } =
        new(TerrainType.Walkable, null, 0, CoverLevel.None);

    /// <summary>
    /// True if a unit can path through this cell. Read by L4
    /// pathfinding's cost function; pre-computed here to keep the
    /// pathfinding code free of <c>switch</c> statements over terrain
    /// kinds.
    /// </summary>
    public bool IsPassable => Terrain != TerrainType.Blocked;

    /// <summary>
    /// True if no unit currently stands on this cell. Movement
    /// destinations require both <see cref="IsPassable"/> and
    /// <see cref="IsEmpty"/>.
    /// </summary>
    public bool IsEmpty => OccupantId is null;
}

/// <summary>
/// What kind of ground a <see cref="Cell"/> represents. Three values,
/// matching Varn's MVP slice lock (one biome — ridge — three terrain
/// kinds). New variants land via Pratchett-flavored designer requests,
/// not Rune speculation.
///
/// <para>
/// <b>Why a flat enum, not a class hierarchy.</b> Java reflex says
/// <c>abstract Terrain</c> with subclasses. Godot/C# reflex says enum +
/// data lookup. Terrain behavior (movement cost, cover bonus, sound
/// modifier) is a function of the enum value, not a virtual method on a
/// subclass — the data is small and finite, the lookup is O(1), and the
/// switch is exhaustive at compile time when paired with the C# 9+
/// switch expression.
/// </para>
/// </summary>
public enum TerrainType
{
    /// <summary>Default ground. Free movement, no special effects.</summary>
    Walkable = 0,

    /// <summary>
    /// Impassable. Boulders, cliffs, palisade walls. Pathfinding skips
    /// these entirely.
    /// </summary>
    Blocked = 1,

    /// <summary>
    /// Loose scree on the ridge slope. Passable but costs more movement
    /// and degrades stealth (noise). The ridge mission's signature
    /// terrain — the reason the approach is interesting at all.
    /// </summary>
    LooseScree = 2,
}

/// <summary>
/// Cover level granted to a unit standing on a cell. Three-stop ladder
/// (none / partial / full) rather than a continuous percentage — easier
/// for the designer-modifier table to reason about, easier for the UC1
/// classifier's context prose to render ("Kira ducks behind the
/// half-wall" reads better than "Kira gains 47% cover").
/// </summary>
public enum CoverLevel
{
    /// <summary>No cover. Default.</summary>
    None = 0,

    /// <summary>
    /// Half-cover: low boulders, scrub. Reduces detection rolls and
    /// ranged hit chance, but leaves the upper torso exposed.
    /// </summary>
    Partial = 1,

    /// <summary>
    /// Full cover: standing boulder, palisade footing, tall scree. The
    /// "actually behind something" tier.
    /// </summary>
    Full = 2,
}
