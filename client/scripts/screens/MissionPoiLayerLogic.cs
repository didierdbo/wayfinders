using System;
using System.Collections.Generic;
using Wayfinders.Client.Services.Dtos;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helpers extracted from
/// <see cref="MissionPoiLayer"/> (Étape A — missions affichées sur eM
/// comme POI cliquables, Rune, 2026-05-23). Three concerns live here :
/// <list type="number">
///   <item><b>Reconcile diff</b> — given the previous set of mission ids
///         on the layer and the new active list from
///         <see cref="MissionStore"/>, what gets added, removed, and
///         which positions move ? Same pattern as
///         <see cref="MissionTooltipRowLogic"/> / <see cref="MissionStoreLogic"/>
///         : the Godot-bound layer owns the node lifetime, the diff math
///         is here and xUnit-pinned.</item>
///   <item><b>Cluster offset</b> — when several missions share the same
///         city (the M1 norm : up to 3 recruit missions on Halfgate), the
///         per-pin world pixel is jittered by a small deterministic
///         offset so the pins don't perfectly stack. Deterministic over
///         the mission id so a re-emergence of the same set produces the
///         same on-screen layout.</item>
///   <item><b>Hit-test</b> — pick the pin closest to a click point,
///         within a generous per-pin pick radius. Returns the mission id
///         or null. The closest-wins rule disambiguates clusters
///         cleanly : a click on the overlapping zone of two pins resolves
///         to the nearer pin centre.</item>
/// </list>
///
/// <para>
/// <b>Why "closest pin within radius" and not Godot <c>Area2D</c>.</b>
/// <see cref="MissionPin"/>s are drawn in code with a fixed pick radius
/// in board-local pixels — same code-pin discipline as
/// <see cref="HalfgateMarkerLayer.MarkerPin"/>. An <c>Area2D</c> would
/// drag a <c>CollisionShape2D</c> per pin, a physics frame for the
/// query, and the cluster overlap would be resolved by the engine's
/// undocumented z-index order instead of the deterministic
/// nearest-centre rule. Code hit-test + closest-wins is simpler, cheaper
/// (it's three mission pins at most in M1), and pinnable here without
/// an engine — same trade-off as
/// <see cref="DeskSelectionLogic.ResolveClickedMember"/>.
/// </para>
///
/// <para>
/// <b>Godot-free by design.</b> Speaks
/// <see cref="EmergentMissionDto"/> + <see cref="WorldPositionDto"/> +
/// <see cref="System.Numerics.Vector2"/> only — every type already
/// cherry-picked into the test csproj.
/// </para>
/// </summary>
public static class MissionPoiLayerLogic
{
    /// <summary>
    /// Outcome of <see cref="DiffMissions"/> : the ids the layer must
    /// add, remove, and the ids that survive (so the caller can refresh
    /// per-pin state on those without recreating the nodes).
    /// </summary>
    /// <param name="Added">Mission ids in the new snapshot but absent from
    /// the previous one — caller spawns a node for each.</param>
    /// <param name="Removed">Mission ids in the previous snapshot but
    /// absent from the new one — caller frees the node for each.</param>
    /// <param name="Surviving">Mission ids present in both — caller may
    /// refresh per-pin state (position / tooltip) on these without a
    /// node churn.</param>
    public readonly record struct ReconcileDiff(
        IReadOnlyList<string> Added,
        IReadOnlyList<string> Removed,
        IReadOnlyList<string> Surviving);

    /// <summary>
    /// Compute the add / remove / survive sets between two mission-id
    /// snapshots. Deterministic iteration order (source order from each
    /// input list), so the F6 smoke log lines are stable across runs.
    ///
    /// <para>
    /// Null inputs are treated as empty. An id repeated in either input
    /// is processed once (the first occurrence) ; in practice the
    /// MissionStore never carries duplicates (server-side uniqueness on
    /// mission id), so this is defensive.
    /// </para>
    /// </summary>
    public static ReconcileDiff DiffMissions(
        IReadOnlyCollection<string>? previousIds,
        IReadOnlyList<EmergentMissionDto>? newMissions)
    {
        var prev = previousIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(previousIds, StringComparer.Ordinal);

        var newIds = new List<string>(newMissions?.Count ?? 0);
        if (newMissions is not null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mission in newMissions)
            {
                if (mission is null) continue;
                if (string.IsNullOrEmpty(mission.Id)) continue;
                if (seen.Add(mission.Id)) newIds.Add(mission.Id);
            }
        }

        var added = new List<string>();
        var surviving = new List<string>();
        var newIdSet = new HashSet<string>(newIds, StringComparer.Ordinal);
        foreach (var id in newIds)
        {
            if (prev.Contains(id)) surviving.Add(id);
            else added.Add(id);
        }

        var removed = new List<string>();
        // Iterate previous in its original order to keep the diff stable.
        if (previousIds is not null)
        {
            var seenRemoved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in previousIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (newIdSet.Contains(id)) continue;
                if (seenRemoved.Add(id)) removed.Add(id);
            }
        }

        return new ReconcileDiff(added, removed, surviving);
    }

    /// <summary>
    /// Compute a small deterministic 2D pixel jitter for a mission that
    /// shares its base world position with siblings. The jitter is
    /// derived from the mission id (stable hash + golden-angle spread)
    /// so two boots with the same mission set produce the same on-screen
    /// layout — important for the F6 smoke being repeatable.
    ///
    /// <para>
    /// <b>The spiral.</b> Pin <c>0</c> sits at the base position (no
    /// offset). Pins <c>1..N</c> sit on a tightening spiral around it :
    /// angle <c>(index × 2.39996...)</c> rad (golden-angle), radius
    /// <c>baseRadius × sqrt(index)</c>. With <c>baseRadius = 14 px</c>
    /// the three M1 recruit pins land within ~22 px of each other —
    /// distinguishable, still clearly clustered "in Halfgate".
    /// </para>
    ///
    /// <para>
    /// <b>Why index, not id-hash, for the angle.</b> The index is the
    /// position of this mission within its city group (sorted by id
    /// ascending). The id is used only to <i>find</i> the index — the
    /// spiral itself is determined by the index. This guarantees three
    /// pins always spread to <c>(0)</c>, <c>(r·1·θ)</c>,
    /// <c>(r·√2·2θ)</c>, never collide on each other no matter what id
    /// hashes happen to be.
    /// </para>
    /// </summary>
    /// <param name="indexInCluster">0-based index of this mission within
    /// its same-city cluster, after the cluster is sorted by mission id
    /// ascending.</param>
    /// <param name="baseRadiusPx">Spiral seed radius in maquette-board
    /// pixels. Defaults to 14 px (the value <see cref="MissionPoiLayer"/>
    /// ships with).</param>
    /// <returns>The pixel offset to add to the base world pixel.</returns>
    public static SysVec2 ClusterSpiralOffset(int indexInCluster, float baseRadiusPx = 14f)
    {
        if (indexInCluster <= 0) return SysVec2.Zero;
        // Golden angle in radians : the same constant used by point-spread
        // packers since the 70s — densest pseudo-uniform spread on a disk.
        const float goldenAngleRad = 2.39996323f;
        float r = baseRadiusPx * MathF.Sqrt(indexInCluster);
        float a = goldenAngleRad * indexInCluster;
        return new SysVec2(r * MathF.Cos(a), r * MathF.Sin(a));
    }

    /// <summary>
    /// Group missions by their resolved city slug and assign each a
    /// stable index inside the group (sorted by mission id ascending).
    /// Caller feeds the index into <see cref="ClusterSpiralOffset"/>.
    ///
    /// <para>
    /// Missions whose city slug cannot be extracted (corrupt
    /// <c>target_poi</c>) are skipped from the output — the caller logs
    /// the skip and places no POI for them.
    /// </para>
    /// </summary>
    /// <param name="missions">The active mission list from
    /// <see cref="MissionStore"/>.</param>
    /// <returns>One entry per mission with a resolvable city slug :
    /// (mission, slug, indexInCluster).</returns>
    public static IReadOnlyList<(EmergentMissionDto Mission, string CitySlug, int IndexInCluster)>
        AssignClusterIndices(IReadOnlyList<EmergentMissionDto>? missions)
    {
        if (missions is null || missions.Count == 0)
        {
            return Array.Empty<(EmergentMissionDto, string, int)>();
        }

        // Step 1 : bucket by city slug. Skip missions with no resolvable slug.
        var buckets = new Dictionary<string, List<EmergentMissionDto>>(StringComparer.Ordinal);
        foreach (var m in missions)
        {
            if (m is null) continue;
            var slug = MissionPositionResolverLogic.ExtractCitySlug(m.TargetPoi);
            if (slug is null) continue;
            if (!buckets.TryGetValue(slug, out var bucket))
            {
                bucket = new List<EmergentMissionDto>();
                buckets[slug] = bucket;
            }
            bucket.Add(m);
        }

        // Step 2 : sort each bucket by id, emit the indexed tuples.
        var result = new List<(EmergentMissionDto, string, int)>();
        foreach (var (slug, bucket) in buckets)
        {
            bucket.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            for (int i = 0; i < bucket.Count; i++)
            {
                result.Add((bucket[i], slug, i));
            }
        }
        return result;
    }

    /// <summary>
    /// Pick the mission id whose pin centre is closest to
    /// <paramref name="clickPoint"/> within <paramref name="pickRadiusPx"/>
    /// in board-local pixels. Closest-wins disambiguation : in a cluster
    /// of overlapping pins, the click resolves to the pin whose centre
    /// is nearest, not to whichever happens to z-index on top.
    ///
    /// <para>
    /// <b>Return value semantics.</b> <c>null</c> means no pin was hit ;
    /// the click should fall through to lower-priority handlers (the
    /// desk-click branch). The returned id is verbatim from the input
    /// — same string, same reference, no allocation.
    /// </para>
    /// </summary>
    /// <param name="clickPoint">The click point in maquette-board local
    /// pixels (post-routing).</param>
    /// <param name="pinCentres">Each pin's board-local pixel centre and
    /// its mission id.</param>
    /// <param name="pickRadiusPx">Per-pin pick radius. A pin is a
    /// candidate iff <c>distance(click, centre) ≤ radius</c>.</param>
    /// <returns>The hit mission id, or <c>null</c>.</returns>
    public static string? PickClosestPin(
        SysVec2 clickPoint,
        IReadOnlyList<(string MissionId, SysVec2 CentrePx)> pinCentres,
        float pickRadiusPx)
    {
        if (pinCentres is null || pinCentres.Count == 0) return null;
        if (pickRadiusPx <= 0f) return null;

        string? hit = null;
        float bestSqDist = pickRadiusPx * pickRadiusPx;
        foreach (var (id, centre) in pinCentres)
        {
            if (string.IsNullOrEmpty(id)) continue;
            float dx = clickPoint.X - centre.X;
            float dy = clickPoint.Y - centre.Y;
            float sq = (dx * dx) + (dy * dy);
            if (sq <= bestSqDist)
            {
                bestSqDist = sq;
                hit = id;
            }
        }
        return hit;
    }
}
