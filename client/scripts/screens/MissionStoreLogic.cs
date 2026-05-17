using System;
using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# projection helpers extracted from the
/// <c>MissionStore</c> autoload so xUnit can pin the layer-prefix +
/// exact-poi match contract without spinning up a Godot runtime. Same
/// separation pattern as <see cref="PoiDispatchLogic"/> /
/// <see cref="MissionPanelLogic"/> : the Godot-bound autoload owns the
/// polling lifecycle + signal emission ; the matching rules live here
/// and stay testable.
///
/// <para>
/// <b>Layer prefix matching (Varn-lock §2 aggregation contract).</b>
/// A mission's <see cref="EmergentMissionDto.TargetPoi"/> matches
/// <paramref name="layerPrefix"/> iff
/// <see cref="EmergentMissionDto.TargetPoi"/> equals the prefix exactly
/// OR starts with <c>layerPrefix + "."</c>. This is the same rule
/// <see cref="Wayfinders.Client.Services.PoiTreeService.IsDescendantOf"/>
/// implements ; we duplicate it here as a pure helper to keep the test
/// surface Godot-free.
/// </para>
///
/// <para>
/// <b>§A.8.D3 recruit cap math (Varn-lock 2026-05-17 strict mode).</b>
/// <see cref="CountRecruitMissions"/> + <see cref="EnforceRecruitCap"/>
/// implement the belt-and-braces N=3 cap that the Godot-bound
/// <c>MissionStore</c> autoload applies in both
/// <c>IngestEmerged</c> and the poll reconciliation. Server-side
/// <c>try_add</c> is the primary enforcement ; the client defends
/// against a race or a future test fixture that bypasses the cap.
/// </para>
/// </summary>
public static class MissionStoreLogic
{
    /// <summary>
    /// Recruit-mission cap N=3 (Varn-lock §A.8.D3, 2026-05-17).
    /// Mirrors the server-side <c>RECRUIT_MISSION_CAP</c>. Public so
    /// xUnit pins can assert against the same constant the runtime
    /// applies.
    /// </summary>
    public const int RecruitMissionCap = 3;

    /// <summary>
    /// Return every mission in <paramref name="missions"/> whose
    /// <see cref="EmergentMissionDto.TargetPoi"/> equals
    /// <paramref name="poiId"/> exactly. Stable iteration order
    /// (source order). Empty <paramref name="poiId"/> or null input
    /// yields an empty list.
    /// </summary>
    public static IReadOnlyList<EmergentMissionDto> GetMissionsForPoi(
        IEnumerable<EmergentMissionDto>? missions,
        string poiId)
    {
        if (missions is null) return System.Array.Empty<EmergentMissionDto>();
        if (string.IsNullOrEmpty(poiId)) return System.Array.Empty<EmergentMissionDto>();

        var result = new List<EmergentMissionDto>();
        foreach (var mission in missions)
        {
            if (mission is null) continue;
            if (mission.TargetPoi == poiId) result.Add(mission);
        }
        return result;
    }

    /// <summary>
    /// Return every mission whose <see cref="EmergentMissionDto.TargetPoi"/>
    /// is <paramref name="layerPrefix"/> or a descendant of it (per the
    /// canonical hierarchical-id rule : <c>id == prefix</c> OR
    /// <c>id.StartsWith(prefix + ".")</c>). Empty inputs yield an empty
    /// list. Stable iteration order (source order).
    /// </summary>
    public static IReadOnlyList<EmergentMissionDto> GetMissionsForLayer(
        IEnumerable<EmergentMissionDto>? missions,
        string layerPrefix)
    {
        if (missions is null) return System.Array.Empty<EmergentMissionDto>();
        if (string.IsNullOrEmpty(layerPrefix)) return System.Array.Empty<EmergentMissionDto>();

        var prefixWithDot = layerPrefix + ".";
        var result = new List<EmergentMissionDto>();
        foreach (var mission in missions)
        {
            if (mission is null) continue;
            if (string.IsNullOrEmpty(mission.TargetPoi)) continue;
            if (mission.TargetPoi == layerPrefix
                || mission.TargetPoi.StartsWith(prefixWithDot, StringComparison.Ordinal))
            {
                result.Add(mission);
            }
        }
        return result;
    }

    /// <summary>
    /// Count the recruit-type missions in <paramref name="missions"/>.
    /// Mirrors the autoload's internal counter used by the cap-defense
    /// path. Null / empty input returns 0.
    /// </summary>
    public static int CountRecruitMissions(IEnumerable<EmergentMissionDto>? missions)
    {
        if (missions is null) return 0;
        var n = 0;
        foreach (var mission in missions)
        {
            if (mission is null) continue;
            if (mission.Type == WorldTickWireFormat.MissionType.Recruit) n++;
        }
        return n;
    }

    /// <summary>
    /// Trim <paramref name="snapshot"/> to at most
    /// <see cref="RecruitMissionCap"/> recruit-type missions. Non-recruit
    /// missions pass through untouched. If the snapshot already obeys
    /// the cap the same reference is returned (callers may compare by
    /// reference to detect a no-op).
    /// </summary>
    public static IReadOnlyList<EmergentMissionDto> EnforceRecruitCap(
        IReadOnlyList<EmergentMissionDto>? snapshot)
    {
        if (snapshot is null) return System.Array.Empty<EmergentMissionDto>();
        if (CountRecruitMissions(snapshot) <= RecruitMissionCap) return snapshot;

        var trimmed = new List<EmergentMissionDto>(snapshot.Count);
        var recruitsSoFar = 0;
        foreach (var mission in snapshot)
        {
            if (mission is null) continue;
            if (mission.Type == WorldTickWireFormat.MissionType.Recruit)
            {
                if (recruitsSoFar >= RecruitMissionCap) continue; // drop surplus
                recruitsSoFar++;
            }
            trimmed.Add(mission);
        }
        return trimmed;
    }

    /// <summary>
    /// Return true iff <paramref name="snapshot"/> contains at least
    /// one mission whose <see cref="EmergentMissionDto.TargetPoi"/>
    /// matches <paramref name="layerPrefix"/> or one of its descendants.
    /// Convenience over <see cref="GetMissionsForLayer"/> for the
    /// E1 reveal-gating boolean check (Varn §A.8.D1).
    /// </summary>
    public static bool HasAnyMissionForLayer(
        IEnumerable<EmergentMissionDto>? missions,
        string layerPrefix)
    {
        if (missions is null || string.IsNullOrEmpty(layerPrefix)) return false;
        var prefixWithDot = layerPrefix + ".";
        foreach (var mission in missions)
        {
            if (mission is null) continue;
            var tp = mission.TargetPoi;
            if (string.IsNullOrEmpty(tp)) continue;
            if (tp == layerPrefix || tp.StartsWith(prefixWithDot, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
