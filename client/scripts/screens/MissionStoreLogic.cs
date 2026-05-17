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
/// </summary>
public static class MissionStoreLogic
{
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
}
