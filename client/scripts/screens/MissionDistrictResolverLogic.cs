using System;
using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# resolver that turns a mission (<see cref="EmergentMissionDto"/>)
/// into the district slug the eM→eT bascule should land on (Étape B —
/// bascule eM → eT au clic mission, Rune, 2026-05-23).
///
/// <para>
/// <b>The mismatch we navigate.</b> The M1 backend emits
/// <c>target_poi = "e2.halfgate.{district}"</c> with district slugs the
/// world referential does NOT know about :
/// <c>gateway</c> / <c>intramuros</c> / <c>littoral</c> are recruit-roster
/// slugs (Varn §A.10 / tests/api/test_pick_recruit_mission.py) ; the
/// canonical <c>world.yaml</c> Halfgate districts are
/// <c>lower_quays</c> and <c>high_wall</c>. A naive 3ʳᵈ-segment lookup
/// against <see cref="WorldReferentialResponse.Districts"/> finds nothing
/// and the bascule would silently no-op on every M1 mission. We resolve
/// this with a documented fallback chain rather than blocking on a Varn /
/// Tess reconciliation.
/// </para>
///
/// <para>
/// <b>The resolution cascade.</b>
/// <list type="number">
///   <item><b>Exact match.</b> Parse the 3ʳᵈ dot-separated segment of
///         <see cref="EmergentMissionDto.TargetPoi"/>. If that segment
///         names a district in <see cref="WorldReferentialResponse.Districts"/>
///         AND that district's <see cref="WorldDistrictDto.ParentCityId"/>
///         matches the mission's city, return it. This is the path the
///         eventual schema bump (recruit slugs unified with world.yaml)
///         will hit unchanged.</item>
///   <item><b>First-of-city fallback.</b> The 3ʳᵈ segment exists but
///         doesn't match a known district. Fall back to the first id in
///         <see cref="WorldCityDto.DistrictIds"/> — for M1 Halfgate this
///         is <c>lower_quays</c>, the only district with a shipped eT
///         mesh. Logged at the call site so the F6 smoke shows the
///         fallback path.</item>
///   <item><b>City-level <c>target_poi</c>.</b> <c>e1.halfgate</c> with no
///         3ʳᵈ segment — also falls through to first-of-city. The mission
///         landed on the eM map ; eT picks the first playable district.
///         Same as the second branch in practice.</item>
///   <item><b>Total failure.</b> Mission has no <c>target_poi</c>, the
///         city is unknown, or the city has zero districts → return null.
///         Caller logs and stays on eM (graceful degrade).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why this is client-side and not on the DTO.</b> Same reasoning as
/// <see cref="MissionPositionResolverLogic"/> : the
/// <see cref="EmergentMissionDto"/> already crossed the wire with the
/// recruit-slug shape ; a clean server fix would bump
/// <c>target_poi</c> to one of the world.yaml slugs, which is a Varn /
/// Tess schema concern out of scope for Étape B. The client derives the
/// answer it needs from data it already caches (the world referential
/// loaded at boot), no new round-trip, no new schema.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Speaks only
/// <see cref="EmergentMissionDto"/> / <see cref="WorldCityDto"/> /
/// <see cref="WorldDistrictDto"/> — all already cherry-picked into the
/// xUnit host. The Godot-bound <see cref="MaquetteModeController"/>
/// reads the cached <see cref="WorldReferentialResponse"/> off the
/// <c>WorldMapService</c> autoload and passes the lists in.
/// </para>
/// </summary>
public static class MissionDistrictResolverLogic
{
    /// <summary>
    /// Which branch of the resolution cascade produced the answer.
    /// Surfaced so the caller can log the path the F6 smoke saw —
    /// "exact match" is the happy day, "first-of-city fallback" is the
    /// expected M1 path, "failure" is the degrade-to-stay-on-eM case.
    /// </summary>
    public enum ResolutionMethod
    {
        /// <summary>3ʳᵈ <c>target_poi</c> segment named a real district.</summary>
        ExactMatch,

        /// <summary>3ʳᵈ segment missing or unknown ; took the city's first district.</summary>
        FirstOfCityFallback,

        /// <summary>No usable answer ; caller should stay on eM.</summary>
        Failure,
    }

    /// <summary>
    /// Outcome of <see cref="ResolveDistrict"/> : the resolved district id
    /// (or null on failure) and the branch of the cascade that produced
    /// it. The caller logs both so the F6 smoke shows the path.
    /// </summary>
    /// <param name="DistrictId">The resolved district slug, or null on
    /// failure.</param>
    /// <param name="Method">Which branch of the cascade ran.</param>
    /// <param name="Diagnostic">A short human-readable hint for the log
    /// — empty on the happy path.</param>
    public readonly record struct ResolutionOutcome(
        string? DistrictId,
        ResolutionMethod Method,
        string Diagnostic);

    /// <summary>
    /// Resolve a mission to a district id via the cascade documented on
    /// the class. The mission's city is extracted by
    /// <see cref="MissionPositionResolverLogic.ExtractCitySlug"/> (we
    /// reuse that parser rather than duplicating it — same Varn-locked
    /// regex contract).
    /// </summary>
    /// <param name="mission">The mission to resolve. Null returns failure.</param>
    /// <param name="cities">All cities from
    /// <see cref="WorldReferentialResponse.Cities"/>. Null / empty
    /// returns failure.</param>
    /// <param name="districts">All districts from
    /// <see cref="WorldReferentialResponse.Districts"/>. Null / empty
    /// short-circuits to failure (no districts to land on, regardless of
    /// the mission's shape).</param>
    /// <returns>
    /// A <see cref="ResolutionOutcome"/> carrying the resolved district id
    /// (or null) and the cascade branch.
    /// </returns>
    public static ResolutionOutcome ResolveDistrict(
        EmergentMissionDto? mission,
        IReadOnlyList<WorldCityDto>? cities,
        IReadOnlyList<WorldDistrictDto>? districts)
    {
        if (mission is null)
        {
            return new ResolutionOutcome(
                null, ResolutionMethod.Failure, "mission is null");
        }
        if (districts is null || districts.Count == 0)
        {
            return new ResolutionOutcome(
                null, ResolutionMethod.Failure,
                "world referential has zero districts");
        }
        if (cities is null || cities.Count == 0)
        {
            return new ResolutionOutcome(
                null, ResolutionMethod.Failure,
                "world referential has zero cities");
        }

        var citySlug = MissionPositionResolverLogic.ExtractCitySlug(mission.TargetPoi);
        if (citySlug is null)
        {
            return new ResolutionOutcome(
                null, ResolutionMethod.Failure,
                $"target_poi '{mission.TargetPoi}' has no parsable city slug");
        }

        WorldCityDto? city = null;
        for (int i = 0; i < cities.Count; i++)
        {
            var c = cities[i];
            if (c is null) continue;
            if (c.Id == citySlug) { city = c; break; }
        }
        if (city is null)
        {
            return new ResolutionOutcome(
                null, ResolutionMethod.Failure,
                $"city '{citySlug}' not in world referential");
        }

        // Branch 1 — exact 3ʳᵈ-segment match against the world referential
        // districts (filtered to those owned by the resolved city, so a
        // future name collision across cities can't resolve cross-city).
        var thirdSegment = ExtractThirdSegment(mission.TargetPoi);
        if (!string.IsNullOrEmpty(thirdSegment))
        {
            for (int i = 0; i < districts.Count; i++)
            {
                var d = districts[i];
                if (d is null) continue;
                if (d.Id == thirdSegment && d.ParentCityId == city.Id)
                {
                    return new ResolutionOutcome(
                        d.Id, ResolutionMethod.ExactMatch, string.Empty);
                }
            }
        }

        // Branch 2 — first-of-city fallback. Take the first id in the
        // city's DistrictIds that actually resolves in the districts list
        // (defensive : the referential is server-validated, but a stale
        // / corrupt cache must not crash the bascule).
        if (city.DistrictIds is not null && city.DistrictIds.Count > 0)
        {
            for (int i = 0; i < city.DistrictIds.Count; i++)
            {
                var candidate = city.DistrictIds[i];
                if (string.IsNullOrEmpty(candidate)) continue;
                for (int j = 0; j < districts.Count; j++)
                {
                    var d = districts[j];
                    if (d is null) continue;
                    if (d.Id == candidate && d.ParentCityId == city.Id)
                    {
                        var diag = string.IsNullOrEmpty(thirdSegment)
                            ? $"target_poi has no 3rd segment ; took first-of-city"
                            : $"target_poi 3rd segment '{thirdSegment}' not a known " +
                              $"world.yaml district ; took first-of-city";
                        return new ResolutionOutcome(
                            d.Id, ResolutionMethod.FirstOfCityFallback, diag);
                    }
                }
            }
        }

        // Branch 3 — total failure : the city is known but has no
        // resolvable district. Should not happen against a valid
        // referential ; degrade gracefully.
        return new ResolutionOutcome(
            null, ResolutionMethod.Failure,
            $"city '{city.Id}' has no resolvable district in the referential");
    }

    /// <summary>
    /// Return the 3ʳᵈ dot-separated segment of a canonical hierarchical
    /// <c>target_poi</c> — the district slug candidate. Null / empty
    /// when there are fewer than three segments. We accept the same
    /// loose layer prefix as
    /// <see cref="MissionPositionResolverLogic.ExtractCitySlug"/> ; if
    /// that returns null, this returns null too (no parsable shape).
    /// </summary>
    /// <param name="targetPoi">An <see cref="EmergentMissionDto.TargetPoi"/>
    /// string from the wire.</param>
    /// <returns>The 3ʳᵈ segment, or null.</returns>
    public static string? ExtractThirdSegment(string? targetPoi)
    {
        if (string.IsNullOrEmpty(targetPoi)) return null;
        if (MissionPositionResolverLogic.ExtractCitySlug(targetPoi) is null) return null;

        // Skip past the first two segments.
        var firstDot = targetPoi.IndexOf('.');
        if (firstDot <= 0) return null;
        var rest = targetPoi.AsSpan(firstDot + 1);
        var secondDot = rest.IndexOf('.');
        if (secondDot < 0) return null;

        var afterSecondDot = rest[(secondDot + 1)..];
        var thirdDot = afterSecondDot.IndexOf('.');
        var thirdSegment = thirdDot < 0 ? afterSecondDot : afterSecondDot[..thirdDot];
        if (thirdSegment.Length == 0) return null;
        return thirdSegment.ToString();
    }
}
