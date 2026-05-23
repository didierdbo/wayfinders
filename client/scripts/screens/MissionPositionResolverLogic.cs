using System;
using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# resolver for "where does this mission live on the eM world
/// map ?" (Étape A — missions affichées sur eM comme POI cliquables, Rune,
/// 2026-05-23). Turns an <see cref="EmergentMissionDto.TargetPoi"/> into a
/// world-metre <see cref="WorldPositionDto"/> by walking the canonical
/// hierarchical id back to its parent city, then reading that city's
/// centroid from the world referential.
///
/// <para>
/// <b>Why this lives client-side, and not on the DTO.</b> The Pydantic
/// <c>EmergentMission</c> on Tess's side has a draft slot for
/// <c>position: Position | None</c> (J1 note, "derived from centroid of
/// the target_poi district in M1"). That slot is <b>not on the wire</b> —
/// <see cref="EmergentMissionDto"/> in
/// <c>client/services/Dtos/WorldTickDtos.cs</c> carries
/// <see cref="EmergentMissionDto.TargetPoi"/> and
/// <see cref="EmergentMissionDto.Region"/> but no <c>Position</c> field,
/// and a JSON dump of <c>/api/missions/active</c> against the live backend
/// would show the same. Two ways forward :
/// <list type="number">
///   <item>Block on Tess to surface <c>position</c> on the DTO. Backend
///         work, schema bump, both sides redeploy, then this resolver
///         becomes a pass-through. Right answer eventually, blocking for
///         Étape A.</item>
///   <item>Derive client-side from the existing
///         <see cref="EmergentMissionDto.TargetPoi"/> via the world
///         referential we already cache at boot
///         (<c>WorldMapService</c>). Zero new round-trip, no schema
///         change, lights up the F6 smoke today.</item>
/// </list>
/// We pick option (2). The resolver is intentionally <b>city-grained</b>
/// for eM : the M1 backend emits
/// <c>target_poi = "e2.halfgate.{district}"</c> with district slugs the
/// world referential does <i>not</i> know about yet (<c>gateway</c> /
/// <c>intramuros</c> / <c>littoral</c> live in the recruit roster, not
/// in <c>world.yaml</c>) — going to the district centroid would be a
/// dangling lookup. At eM zoom, one cluster per city is the right UX
/// anyway : the player sees "there's stuff happening in Halfgate", clicks
/// in, and the eM→eT bascule (future J7) is where the district resolves.
/// </para>
///
/// <para>
/// <b>The <c>target_poi</c> shape.</b> Canonical regex
/// <c>^e[1-3]\.[a-z0-9\-]+(\.[a-z0-9\-]+){0,2}$</c> (Varn-lock 2026-05-15
/// v2 §1, mirrored in <see cref="EmergentMissionDto.TargetPoi"/>). The
/// <b>second</b> dot-separated segment is the city slug — both for
/// <c>e1.halfgate</c> (e1 fallback when no E2 districts exist) and
/// <c>e2.halfgate.gateway</c> (M1 recruit). When the regex doesn't
/// match (drift / corrupt server / future schema bump), the resolver
/// returns <c>null</c> — the caller renders no POI for that mission and
/// logs the skip. Graceful degrade : the rest of the eM map still works.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Speaks only
/// <see cref="WorldCityDto"/> / <see cref="WorldPositionDto"/> /
/// <see cref="EmergentMissionDto"/> — all already cherry-pickable into
/// the xUnit host (the DTOs are records living in
/// <c>client/services/Dtos/</c> and were added to the test csproj at J5).
/// No <c>WorldMapService</c> reference here ; the Godot-bound
/// <see cref="MissionPoiLayer"/> reads
/// <see cref="WorldMapService.World"/> and passes
/// <see cref="WorldReferentialResponse.Cities"/> in.
/// </para>
/// </summary>
public static class MissionPositionResolverLogic
{
    /// <summary>
    /// Parse the city slug out of a canonical hierarchical
    /// <c>target_poi</c> string — the second dot-separated segment.
    /// Returns the slug for valid inputs, <c>null</c> for anything that
    /// doesn't fit the shape (null / empty / no dot / first segment not a
    /// layer prefix).
    ///
    /// <para>
    /// <b>What "valid" means here.</b> Two segments minimum, layer prefix
    /// matches <c>e[1-9]</c> (digit-loose so a future <c>e4</c>/<c>e5</c>
    /// roadmap doesn't break this parser), city slug is non-empty. We
    /// don't validate the exhaustive Varn regex here — the server-side
    /// Pydantic does that on emission, and a malformed <c>target_poi</c>
    /// is a server bug not a client surface.
    /// </para>
    /// </summary>
    /// <param name="targetPoi">A <see cref="EmergentMissionDto.TargetPoi"/>
    /// string from the wire.</param>
    /// <returns>The city slug (e.g. <c>"halfgate"</c>), or <c>null</c>.</returns>
    public static string? ExtractCitySlug(string? targetPoi)
    {
        if (string.IsNullOrEmpty(targetPoi)) return null;

        var firstDot = targetPoi.IndexOf('.');
        if (firstDot <= 0 || firstDot >= targetPoi.Length - 1) return null;

        var layerSegment = targetPoi.AsSpan(0, firstDot);
        if (!IsLayerPrefix(layerSegment)) return null;

        var rest = targetPoi.AsSpan(firstDot + 1);
        var secondDot = rest.IndexOf('.');
        var citySlug = secondDot < 0 ? rest : rest[..secondDot];
        if (citySlug.Length == 0) return null;
        return citySlug.ToString();
    }

    /// <summary>
    /// True when <paramref name="segment"/> looks like a Varn layer
    /// prefix (<c>e</c> followed by one ASCII digit). Loose on the digit
    /// to keep the parser future-proof — Varn-lock pins
    /// <c>e1</c>/<c>e2</c>/<c>e3</c> today, and a strict match against
    /// those exact three values would silently drop any future
    /// <c>e4</c>-prefixed mission instead of letting the city lookup
    /// surface the unknown. Tight failure point :
    /// <see cref="ResolveForCity"/> returns null on a missing city, so a
    /// stray <c>e7.foo</c> still resolves to "no POI" rather than
    /// crashing.
    /// </summary>
    private static bool IsLayerPrefix(ReadOnlySpan<char> segment)
    {
        return segment.Length == 2
            && segment[0] == 'e'
            && segment[1] is >= '0' and <= '9';
    }

    /// <summary>
    /// Resolve one mission to a world-metre position, or <c>null</c>
    /// when the lookup fails (missing <c>target_poi</c>, unknown city,
    /// empty referential).
    ///
    /// <para>
    /// <b>The lookup chain.</b>
    /// <see cref="EmergentMissionDto.TargetPoi"/> →
    /// <see cref="ExtractCitySlug"/> →
    /// <paramref name="citiesById"/>[slug] →
    /// <see cref="WorldCityDto.Position"/>. The dictionary is keyed by
    /// <see cref="WorldCityDto.Id"/> (the same lower-case slug the server
    /// emits) so the match is exact, no case-folding gymnastics.
    /// </para>
    /// </summary>
    /// <param name="mission">The mission to resolve. Null returns null.</param>
    /// <param name="citiesById">Cities indexed by id (the
    /// <see cref="WorldReferentialResponse.Cities"/> list flattened into
    /// a dictionary by the caller, see
    /// <see cref="BuildCityIndex"/>).</param>
    /// <returns>The mission's world-metre position, or <c>null</c>.</returns>
    public static WorldPositionDto? ResolveForCity(
        EmergentMissionDto? mission,
        IReadOnlyDictionary<string, WorldCityDto>? citiesById)
    {
        if (mission is null || citiesById is null || citiesById.Count == 0) return null;

        var slug = ExtractCitySlug(mission.TargetPoi);
        if (slug is null) return null;

        return citiesById.TryGetValue(slug, out var city) ? city.Position : null;
    }

    /// <summary>
    /// Build an immutable city-id → city index from
    /// <see cref="WorldReferentialResponse.Cities"/>. Lives here (not on
    /// the DTO) so xUnit can pin the lookup contract without spinning up
    /// <c>WorldMapService</c>, and so the resolver stays Godot-free.
    /// Duplicates would be a server bug ; if one slips through, the
    /// first occurrence wins and the second is silently dropped (the
    /// dictionary semantics) — the world referential's server-side
    /// ordering is by id ascending, deterministic per session.
    /// </summary>
    /// <param name="cities">The cities list from the cached
    /// referential. Null / empty returns an empty index.</param>
    /// <returns>A read-only dictionary keyed by city id.</returns>
    public static IReadOnlyDictionary<string, WorldCityDto> BuildCityIndex(
        IReadOnlyList<WorldCityDto>? cities)
    {
        if (cities is null || cities.Count == 0)
        {
            return new Dictionary<string, WorldCityDto>(0);
        }
        var index = new Dictionary<string, WorldCityDto>(cities.Count, StringComparer.Ordinal);
        foreach (var city in cities)
        {
            if (city is null || string.IsNullOrEmpty(city.Id)) continue;
            index.TryAdd(city.Id, city);
        }
        return index;
    }

    /// <summary>
    /// Engine-seam helper : pack a <see cref="WorldPositionDto"/> into a
    /// <see cref="System.Numerics.Vector2"/> ready for
    /// <see cref="WorldMapCalage.WorldMetresToRenderPixel"/>. Kept here
    /// to keep the conversion in one place instead of dropping it in
    /// every caller.
    /// </summary>
    public static SysVec2 ToWorldMetres(WorldPositionDto position)
        => new(position.X, position.Y);
}
