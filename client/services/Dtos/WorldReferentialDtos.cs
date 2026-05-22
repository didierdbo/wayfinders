using System.Collections.Generic;

namespace Wayfinders.Client.Services.Dtos;

/// <summary>
/// Wire-format DTOs for <c>GET /api/world</c> — the J4 world referential
/// endpoint (server schema in <c>wayfinders/api/world_referential_models.py</c>,
/// Tess commit 55487d7).
///
/// <para>
/// <b>What this file is.</b> A field-for-field mirror of Coda's Pydantic
/// models. Five immutable records :
/// <list type="bullet">
///   <item><see cref="WorldReferentialResponse"/> — the top-level envelope.</item>
///   <item><see cref="WorldDto"/> — the world bounding box (max_x / max_y).</item>
///   <item><see cref="WorldCityDto"/> — one city (id, name, position,
///         district ids, optional eM silhouette bitmap).</item>
///   <item><see cref="WorldDistrictDto"/> — one district (id, name, parent
///         city, anchor, footprint polygon, cell size).</item>
///   <item><see cref="WorldPositionDto"/> — a 2D world position in integer
///         metres.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Naming policy.</b> Wire is snake_case ; C# is PascalCase ; the uniform
/// <c>SnakeCaseLower</c> policy on <see cref="ApiJsonContext"/> handles the
/// mapping. New fields the server adds map automatically (additive change is
/// safe per the Varn spec lock 2026-05-20).
/// </para>
///
/// <para>
/// <b>schema_version contract.</b> <see cref="WorldReferentialResponse.SchemaVersion"/>
/// mirrors <c>world.yaml</c>'s schema version. The client knows which version
/// it understands (<c>ApiClient.SupportedWorldSchemaVersion</c>) and rejects
/// anything else with a typed <c>ApiError.DeserializationError</c> rather than
/// silently mis-parsing a future breaking change. This is the integration-time
/// canary if Coda bumps the schema without the C# side catching up.
/// </para>
///
/// <para>
/// <b>Why distinct names from <c>WorldTickDtos</c>.</b> The tick endpoint
/// already owns the short names <c>EmergentMissionDto</c> etc. ; the
/// referential records carry the <c>World</c> prefix
/// (<see cref="WorldCityDto"/>, <see cref="WorldPositionDto"/>) so the two
/// boundary surfaces never collide in the <c>Dtos</c> namespace. The server
/// names them <c>CityDto</c> / <c>PositionDto</c> ; the snake_case wire field
/// names are what cross the boundary, so the C# type name is free to
/// disambiguate.
/// </para>
/// </summary>
/// <param name="X">East-west coordinate in world metres. 0 &lt;= X &lt; world.max_x.</param>
/// <param name="Y">North-south coordinate in world metres. 0 &lt;= Y &lt; world.max_y.</param>
public sealed record WorldPositionDto(
    int X,
    int Y
);

/// <summary>
/// World bounding-box dimensions. Locked at max_x = 500 000 m (500 km),
/// max_y = 250 000 m (250 km). Valid coordinate range is
/// <c>[0, MaxX) x [0, MaxY)</c>.
/// </summary>
/// <param name="MaxX">World width in metres (east-west). Maps from <c>max_x</c>.</param>
/// <param name="MaxY">World height in metres (north-south). Maps from <c>max_y</c>.</param>
public sealed record WorldDto(
    int MaxX,
    int MaxY
);

/// <summary>
/// A city in the world referential.
///
/// <para>
/// <see cref="Position"/> is the centroid anchor in world metres — used as
/// the city marker on layer eM (J5 places the Halfgate marker from exactly
/// this value). <see cref="Bitmap"/> is an optional path to the eM
/// silhouette asset relative to <c>assets/world/</c> ; null until the asset
/// is produced.
/// </para>
/// </summary>
/// <param name="Id">Stable slug identifier (e.g. <c>"halfgate"</c>).</param>
/// <param name="Name">Display name (e.g. <c>"Halfgate"</c>).</param>
/// <param name="Position">City centroid in world metres.</param>
/// <param name="DistrictIds">Ordered list of district ids belonging to this
/// city. Maps from <c>district_ids</c>.</param>
/// <param name="Bitmap">Optional eM silhouette asset path, or null.</param>
public sealed record WorldCityDto(
    string Id,
    string Name,
    WorldPositionDto Position,
    IReadOnlyList<string> DistrictIds,
    string? Bitmap
);

/// <summary>
/// A district within a city.
///
/// <para>
/// <see cref="Anchor"/> is the SW corner of the district bitmap in absolute
/// world metres — used by layer eT to pose the district mesh.
/// <see cref="Footprint"/> is the ordered polygon (≥ 3 vertices) in world
/// metres, derived from the committed <c>*.polygon.json</c> sidecar. The
/// client is orientation-agnostic (CW or CCW both fine).
/// </para>
/// </summary>
/// <param name="Id">Stable slug identifier (e.g. <c>"lower_quays"</c>).</param>
/// <param name="Name">Display name (e.g. <c>"Lower Quays"</c>).</param>
/// <param name="ParentCityId">Id of the owning city. Maps from
/// <c>parent_city_id</c>.</param>
/// <param name="Anchor">SW corner of the district bitmap in absolute world
/// metres.</param>
/// <param name="Footprint">Ordered polygon vertices in world metres
/// (≥ 3 vertices).</param>
/// <param name="CellSizeM">Grid cell size in metres. Locked at 2 m.
/// Maps from <c>cell_size_m</c>.</param>
public sealed record WorldDistrictDto(
    string Id,
    string Name,
    string ParentCityId,
    WorldPositionDto Anchor,
    IReadOnlyList<WorldPositionDto> Footprint,
    int CellSizeM
);

/// <summary>
/// Response body for <c>GET /api/world</c> — the top-level envelope for the
/// world referential.
///
/// <para>
/// The payload is static per server version ; the Godot client caches this
/// at boot (see <c>WorldMapService</c>). <see cref="Cities"/> and
/// <see cref="Districts"/> are server-sorted by id ascending for stable
/// diffs and caches.
/// </para>
/// </summary>
/// <param name="SchemaVersion">Schema version of the underlying
/// <c>world.yaml</c> (currently 1). The client rejects unknown versions —
/// see <c>ApiClient.SupportedWorldSchemaVersion</c>. Maps from
/// <c>schema_version</c>.</param>
/// <param name="World">World bounding box.</param>
/// <param name="Cities">All cities, ordered by id ascending.</param>
/// <param name="Districts">All districts across all cities, ordered by id
/// ascending.</param>
public sealed record WorldReferentialResponse(
    int SchemaVersion,
    WorldDto World,
    IReadOnlyList<WorldCityDto> Cities,
    IReadOnlyList<WorldDistrictDto> Districts
);
