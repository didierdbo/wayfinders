using System;
using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# helper that formats <see cref="EmergentMissionDto"/> entries
/// into the tooltip "Affaires en cours" rows per Varn-lock 2026-05-15
/// v2 §2 (row format) + v2 §3-§4 (cap + tri + tronc + glyphs).
///
/// <para>
/// <b>Why a separate pure-C# class.</b> Godot-bound rendering code
/// cannot be exercised under xUnit (the test project is non-Godot
/// per <c>Wayfinders.Client.Tests.csproj</c>). Keeping the row
/// formatting and the mission filtering here means we can pin the
/// 38-char truncation, the FIFO ordering, the cap = 3, and the
/// glyph mapping in xUnit. The IsoMapE1Probe tooltip code just
/// consumes the precomputed strings.
/// </para>
///
/// <para>
/// <b>Row format (Varn §2 v1, kept).</b>
/// <c>[&lt;TypeGlyph&gt; &lt;DiffDots&gt;]  &lt;hook tronqué 38 chars + …&gt;</c>.
/// </para>
///
/// <para>
/// <b>Glyph mapping (Varn §4 v2 confirmed, §5 v2 MVP 3-niveaux).</b>
/// MissionType : <c>scout_route</c> → "Sc", <c>parley_local</c> → "Pa".
/// Difficulty (M-10 closed lookup → 3-niveaux d'affichage MVP) :
/// <c>very-low</c>/<c>low</c> → "·..", <c>mid</c> → "•·•", <c>high</c>/<c>very-high</c> → "•••".
/// We use the bullet "•" + dot "·" mapping locked Varn §5 v2 MVP.
/// </para>
///
/// <para>
/// <b>Tri (Varn §4 v1).</b> FIFO par spawn_tick croissant puis par
/// mission_id (tiebreak deterministe). MVP M1 : on n'a pas de
/// SpawnTick côté client DTO actuel ; l'ordre d'insertion dans
/// <see cref="GameState.PendingMissions"/> sert de FIFO de facto
/// (WorldSimTick.OnMissionEmergedDeferred.Add appose en queue). On
/// trie sur Id pour stabilité, en gardant l'ordre stable d'insertion
/// pour les égalités d'Id (impossible en pratique, mais defense en
/// profondeur). M2 : ajouter SpawnTick au DTO si Varn pousse les
/// missions de plusieurs ticks sur la même ms.
/// </para>
///
/// <para>
/// <b>Cap (Varn §3 v2).</b> 3 missions max affichées par POI. MVP
/// notre cap d'émergence (3 pending par feuille) == cap display,
/// donc pas de "+X autres" overflow rendu. Logique d'overflow
/// quand-même calculée et exposée via <see cref="FormatResult.OverflowCount"/>
/// pour activation triviale M3+ sans toucher la formatter.
/// </para>
/// </summary>
public static class MissionTooltipRowLogic
{
    /// <summary>Cap d'affichage MVP (Varn §3 v2).</summary>
    public const int DisplayCap = 3;

    /// <summary>Longueur dure de tronc hook + ellipsis (Varn §2 v1).
    /// La valeur est sur le hook seul ; on coupe à 38 chars puis on
    /// ajoute "…" (1 char Unicode horizontal-ellipsis).</summary>
    public const int HookTruncateChars = 38;

    /// <summary>Le caractère ellipsis Unicode (horizontal ellipsis U+2026).
    /// Pratchett-administrative register, pas "..." 3-points.</summary>
    public const string Ellipsis = "…";

    /// <summary>Type glyph mapping (Varn §4 v2).</summary>
    public static string TypeGlyph(string missionType) => missionType switch
    {
        WorldTickWireFormat.MissionType.ScoutRoute => "Sc",
        WorldTickWireFormat.MissionType.ParleyLocal => "Pa",
        _ => "??"
    };

    /// <summary>Difficulty dots mapping (Varn §5 v2 MVP 3-niveaux,
    /// projection du closed lookup M-10 5-buckets sur l'affichage).
    /// "·" = U+00B7 middle dot, "•" = U+2022 bullet.</summary>
    public static string DifficultyDots(string difficulty) => difficulty switch
    {
        WorldTickWireFormat.DifficultyBucket.VeryLow => "•··",  // •··
        WorldTickWireFormat.DifficultyBucket.Low => "•··",      // •··
        WorldTickWireFormat.DifficultyBucket.Mid => "••·",      // ••·
        WorldTickWireFormat.DifficultyBucket.High => "•••",     // •••
        WorldTickWireFormat.DifficultyBucket.VeryHigh => "•••", // •••
        _ => "???"
    };

    /// <summary>Truncate a narrative hook to <see cref="HookTruncateChars"/>
    /// + ellipsis. If the hook is already ≤ cap, returns it unchanged
    /// (no trailing ellipsis on short strings, Pratchett admin clean).</summary>
    public static string TruncateHook(string hook)
    {
        if (string.IsNullOrEmpty(hook)) return "";
        if (hook.Length <= HookTruncateChars) return hook;
        return hook.Substring(0, HookTruncateChars) + Ellipsis;
    }

    /// <summary>Format a single row per the Varn §2 v1 lock.</summary>
    public static string FormatRow(EmergentMissionDto mission)
    {
        if (mission is null) throw new ArgumentNullException(nameof(mission));
        var glyph = TypeGlyph(mission.Type);
        var dots = DifficultyDots(mission.Difficulty);
        var hook = TruncateHook(mission.NarrativeHook);
        // Two spaces between bracket and hook per Varn spec example.
        return $"[{glyph} {dots}]  {hook}";
    }

    /// <summary>
    /// Filter + sort + cap the pending missions for a given POI's
    /// canonical id. Returns the up-to-3 formatted rows and the
    /// overflow count (reserved for M3+ "+X autres affaires" display).
    /// </summary>
    /// <param name="hoveredPoiId">The POI being hovered (PoiData.PoiId).
    /// Empty string returns empty result.</param>
    /// <param name="pendingMissions">GameState.PendingMissions snapshot.
    /// Caller may pass an IEnumerable from any container ; we materialise
    /// internally once.</param>
    /// <param name="isDescendantOf">Predicate delegated to
    /// <see cref="PoiTreeService.IsDescendantOf"/>. Injected so this
    /// helper stays Godot-free and the tree-shape contract is testable
    /// in isolation.</param>
    public static FormatResult Build(
        string hoveredPoiId,
        IEnumerable<EmergentMissionDto> pendingMissions,
        Func<string, string, bool> isDescendantOf)
    {
        if (string.IsNullOrEmpty(hoveredPoiId)) return FormatResult.Empty;
        if (pendingMissions is null) return FormatResult.Empty;
        if (isDescendantOf is null) throw new ArgumentNullException(nameof(isDescendantOf));

        // Filter : exact match OR descendant (Varn §2 aggregation).
        // We materialise to a list once for stable ordering + count.
        var matching = pendingMissions
            .Where(m => m is not null
                        && !string.IsNullOrEmpty(m.TargetPoi)
                        && isDescendantOf(m.TargetPoi, hoveredPoiId))
            .ToList();

        // Tri : FIFO sur Id (substitut de SpawnTick en MVP — la liste
        // PendingMissions est de facto append-only via WorldSimTick.Add,
        // donc l'ordre d'insertion EST le SpawnTick croissant. On stabilise
        // sur Id pour pinable test ; preserves insertion-order via OrderBy
        // qui est stable en .NET).
        matching.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        var taken = matching.Take(DisplayCap).Select(FormatRow).ToList();
        var overflow = Math.Max(0, matching.Count - DisplayCap);

        return new FormatResult(taken, overflow, matching.Count);
    }

    /// <summary>Result of <see cref="Build"/>.</summary>
    /// <param name="Rows">Up to <see cref="DisplayCap"/> formatted rows.</param>
    /// <param name="OverflowCount">Number of additional matching
    /// missions beyond the cap (0 in M1 because cap == display).</param>
    /// <param name="TotalMatching">Total matching missions before
    /// capping. Useful for "show / hide section" decision : hide when
    /// 0.</param>
    public sealed record FormatResult(
        IReadOnlyList<string> Rows,
        int OverflowCount,
        int TotalMatching)
    {
        public static readonly FormatResult Empty = new(Array.Empty<string>(), 0, 0);
    }
}
