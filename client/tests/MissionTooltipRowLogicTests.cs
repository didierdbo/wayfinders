// MVP coverage for MissionTooltipRowLogic.
//
// What this file pins :
//   1. Glyph mappings for the 2 M-10 mission types (Sc / Pa).
//   2. Difficulty 3-niveaux projection from the M-10 5-bucket lookup
//      (Varn-lock 2026-05-15 v2 §5 — defer 5-niveaux until post-playtest).
//   3. Hook truncation at 38 chars + ellipsis (Varn §2 v1).
//   4. Row format string : "[Sc ••·]  Une affaire ..." with 2 spaces
//      between bracket and hook (Varn spec example).
//   5. Filtering : exact-id match + descendant prefix match via the
//      injected isDescendantOf predicate.
//   6. Cap = 3 (Varn §3 v2 — display cap MVP equals emergence cap).
//   7. Empty hovered POI id returns empty result (legacy POIs without
//      a PoiId still hover, just no missions section).
//   8. Sort stability : missions are ordered by Id (string ordinal) so
//      a renderer regression on ordering surfaces here, not in a
//      runtime "why are missions in random order" bug at playtest.

using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests;

public sealed class MissionTooltipRowLogicTests
{
    // ====================================================================
    // Glyph mappings -- Varn §4 v2 + §5 v2 (MVP 3-niveaux)
    // ====================================================================

    [Fact]
    public void Type_glyph_maps_M10_lookup()
    {
        Assert.Equal("Sc", MissionTooltipRowLogic.TypeGlyph(WorldTickWireFormat.MissionType.ScoutRoute));
        Assert.Equal("Pa", MissionTooltipRowLogic.TypeGlyph(WorldTickWireFormat.MissionType.ParleyLocal));
    }

    [Fact]
    public void Type_glyph_unknown_falls_back_to_question_marks()
    {
        // M2+ guarantee : adding a new MissionType in the lookup
        // without updating the glyph map produces "??" in the UI,
        // not a silent crash. The "??" surfaces at playtest as a
        // visible canary.
        Assert.Equal("??", MissionTooltipRowLogic.TypeGlyph("recrutement"));
    }

    [Fact]
    public void Difficulty_dots_projects_5_buckets_onto_3_niveaux()
    {
        // very-low + low collapse to •·· (easy projection)
        Assert.Equal("•··", MissionTooltipRowLogic.DifficultyDots(WorldTickWireFormat.DifficultyBucket.VeryLow));
        Assert.Equal("•··", MissionTooltipRowLogic.DifficultyDots(WorldTickWireFormat.DifficultyBucket.Low));
        // mid -> ••·
        Assert.Equal("••·", MissionTooltipRowLogic.DifficultyDots(WorldTickWireFormat.DifficultyBucket.Mid));
        // high + very-high collapse to ••• (hard projection)
        Assert.Equal("•••", MissionTooltipRowLogic.DifficultyDots(WorldTickWireFormat.DifficultyBucket.High));
        Assert.Equal("•••", MissionTooltipRowLogic.DifficultyDots(WorldTickWireFormat.DifficultyBucket.VeryHigh));
    }

    // ====================================================================
    // Truncation -- Varn §2 v1 (38 chars + ellipsis)
    // ====================================================================

    [Fact]
    public void Truncate_short_hook_returns_unchanged_no_ellipsis()
    {
        const string hook = "Une courte affaire.";
        Assert.Equal(hook, MissionTooltipRowLogic.TruncateHook(hook));
        Assert.DoesNotContain("…", MissionTooltipRowLogic.TruncateHook(hook));
    }

    [Fact]
    public void Truncate_exactly_38_chars_returns_unchanged()
    {
        // 38 chars exactly -- on the boundary, no ellipsis added.
        var hook = new string('a', 38);
        Assert.Equal(38, hook.Length);
        Assert.Equal(hook, MissionTooltipRowLogic.TruncateHook(hook));
    }

    [Fact]
    public void Truncate_long_hook_cuts_at_38_plus_ellipsis()
    {
        // 50-char hook -> 38 + "…" = 39 total
        var hook = new string('a', 50);
        var result = MissionTooltipRowLogic.TruncateHook(hook);
        Assert.Equal(39, result.Length);
        Assert.EndsWith("…", result);
        Assert.StartsWith(new string('a', 38), result);
    }

    [Fact]
    public void Truncate_empty_returns_empty()
    {
        Assert.Equal("", MissionTooltipRowLogic.TruncateHook(""));
    }

    // ====================================================================
    // Row format -- Varn §2 v1 example
    // ====================================================================

    [Fact]
    public void Format_row_matches_varn_spec_example()
    {
        var mission = NewMission(
            id: "m1", type: WorldTickWireFormat.MissionType.ScoutRoute,
            hook: "Une affaire de scout_route dans halfgate.",
            difficulty: WorldTickWireFormat.DifficultyBucket.Mid,
            targetPoi: "e1.halfgate");

        var row = MissionTooltipRowLogic.FormatRow(mission);

        // Bracket + 2 spaces + hook (truncated). Hook here is 41 chars
        // (over the 38-char cap) so we expect 38 chars then ellipsis.
        // First 38 chars of the hook = "Une affaire de scout_route dans halfga".
        Assert.StartsWith("[Sc ••·]  ", row);
        Assert.EndsWith("…", row);
        // Exact form pinned : prevents accidental layout drift.
        Assert.Equal("[Sc ••·]  Une affaire de scout_route dans halfga…", row);
    }

    [Fact]
    public void Format_row_parley_low_difficulty()
    {
        var mission = NewMission(
            id: "m2", type: WorldTickWireFormat.MissionType.ParleyLocal,
            hook: "Une affaire de parley_local dans h.",
            difficulty: WorldTickWireFormat.DifficultyBucket.Low,
            targetPoi: "e1.halfgate");

        var row = MissionTooltipRowLogic.FormatRow(mission);
        Assert.Equal("[Pa •··]  Une affaire de parley_local dans h.", row);
    }

    // ====================================================================
    // Build -- filter + sort + cap (Varn §2 + §3)
    // ====================================================================

    [Fact]
    public void Build_with_empty_poi_id_returns_empty()
    {
        var missions = new[] { NewMission(id: "m1", targetPoi: "e1.halfgate") };
        var result = MissionTooltipRowLogic.Build("", missions, PrefixMatch);
        Assert.Same(MissionTooltipRowLogic.FormatResult.Empty, result);
    }

    [Fact]
    public void Build_with_null_missions_returns_empty()
    {
        var result = MissionTooltipRowLogic.Build("e1.halfgate", null!, PrefixMatch);
        Assert.Equal(0, result.TotalMatching);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Build_filters_to_exact_match_only_when_pure_prefix()
    {
        // Pure prefix-match semantics : descendants are detected ONLY
        // when the canonical id string truly extends the hovered id
        // with a "." separator. Varn-spec example IDs cross layers
        // (e1.halfgate vs e2.halfgate.docks) which do NOT share a
        // prefix-with-dot relationship -- the layer prefix changes.
        //
        // In M1 (cap-e1-only emergence), all target_poi values are
        // e1.* and the rule degenerates to exact match. M2+ when
        // emergence opens to e2/e3, proper tree traversal via
        // PoiTreeNodeDto.Parent must replace pure prefix-match.
        // The MissionTooltipRowLogic helper is predicate-injected so
        // the smarter PoiTreeService implementation can land then
        // without re-touching this helper.
        var missions = new[]
        {
            NewMission(id: "m1", targetPoi: "e1.halfgate"),                       // exact match  -> included
            NewMission(id: "m2", targetPoi: "e2.halfgate.docks"),                 // cross-layer -> excluded (MVP limit)
            NewMission(id: "m3", targetPoi: "e1.veylant"),                        // unrelated   -> excluded
            NewMission(id: "m4", targetPoi: "e1.halfgate-clone"),                 // shared chars,
                                                                                  //   no "." boundary -> excluded
            NewMission(id: "m5", targetPoi: ""),                                  // empty       -> excluded
        };

        var result = MissionTooltipRowLogic.Build("e1.halfgate", missions, PrefixMatch);

        Assert.Equal(1, result.TotalMatching);
        Assert.Single(result.Rows);
        Assert.Equal(0, result.OverflowCount);
    }

    [Fact]
    public void Build_caps_at_3_rows_overflow_reflected()
    {
        var missions = Enumerable.Range(0, 7)
            .Select(i => NewMission(id: $"m{i:D2}", targetPoi: "e1.halfgate"))
            .ToList();

        var result = MissionTooltipRowLogic.Build("e1.halfgate", missions, PrefixMatch);

        Assert.Equal(7, result.TotalMatching);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(4, result.OverflowCount);
    }

    [Fact]
    public void Build_orders_rows_by_id_ordinal()
    {
        // Missions inserted in non-sorted order -- the helper must
        // re-sort by Id so the rendered tooltip is deterministic.
        var missions = new[]
        {
            NewMission(id: "ccc", targetPoi: "e1.halfgate"),
            NewMission(id: "aaa", targetPoi: "e1.halfgate"),
            NewMission(id: "bbb", targetPoi: "e1.halfgate"),
        };

        var result = MissionTooltipRowLogic.Build("e1.halfgate", missions, PrefixMatch);

        Assert.Equal(3, result.Rows.Count);
        // The Id is encoded into the hook for this test so we can read
        // the order from the formatted row.
        Assert.Contains("aaa", result.Rows[0]);
        Assert.Contains("bbb", result.Rows[1]);
        Assert.Contains("ccc", result.Rows[2]);
    }

    [Fact]
    public void Build_pure_prefix_includes_only_same_layer_deeper_ids()
    {
        // With pure prefix-match (MVP), a hovered POI matches itself
        // and any target_poi whose string extends it with "." + more
        // segments AT THE SAME LAYER PREFIX. The Varn cross-layer
        // example (e2.halfgate.docks ancestor of e3.halfgate.docks.taverne)
        // does NOT work via pure prefix because the layer prefix changes.
        // This test pins what pure prefix actually detects -- a
        // hypothetical same-layer hierarchy like "reg.sub.sub2".
        var missions = new[]
        {
            NewMission(id: "m1", targetPoi: "e1.halfgate"),                       // exact
            NewMission(id: "m2", targetPoi: "e1.halfgate.extra"),                 // same-layer deeper -> included
            NewMission(id: "m3", targetPoi: "e2.halfgate.docks"),                 // cross-layer       -> excluded
        };

        var result = MissionTooltipRowLogic.Build("e1.halfgate", missions, PrefixMatch);

        // m1 (exact) + m2 (same-layer-deeper). m3 fails pure prefix.
        Assert.Equal(2, result.TotalMatching);
    }

    [Fact]
    public void Build_ignores_missions_with_empty_target_poi()
    {
        // Defensive : legacy mission rows that pre-date the migration
        // (TargetPoi = "" default) should not match anything, even
        // when the hovered POI is "e1.halfgate". Otherwise the
        // tooltip would surface garbage during the rollout.
        var missions = new[]
        {
            NewMission(id: "m1", targetPoi: ""),
            NewMission(id: "m2", targetPoi: "e1.halfgate"),
        };

        var result = MissionTooltipRowLogic.Build("e1.halfgate", missions, PrefixMatch);

        Assert.Equal(1, result.TotalMatching);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    /// <summary>
    /// Local prefix-match implementation matching
    /// <c>PoiTreeService.IsDescendantOf</c> -- string-prefix only,
    /// no tree consultation. Encodes the §2 aggregation rule
    /// directly so the helper can be exercised here without a
    /// real PoiTreeService instance.
    /// </summary>
    private static bool PrefixMatch(string candidate, string ancestor)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(ancestor)) return false;
        if (candidate == ancestor) return true;
        return candidate.StartsWith(ancestor + ".", System.StringComparison.Ordinal);
    }

    private static EmergentMissionDto NewMission(
        string id,
        string type = WorldTickWireFormat.MissionType.ScoutRoute,
        string hook = "",
        string difficulty = WorldTickWireFormat.DifficultyBucket.Mid,
        string targetPoi = "")
    {
        // If hook is unset, encode the id so test-by-row reads can
        // assert ordering by looking for the id substring.
        if (string.IsNullOrEmpty(hook)) hook = $"hook for {id}";
        return new EmergentMissionDto(
            Id: id,
            Type: type,
            NarrativeHook: hook,
            EligiblePersonas: System.Array.Empty<string>(),
            Difficulty: difficulty,
            Region: "halfgate",
            DeadlineTicks: null,
            Outcome: null,
            Seed: 0L,
            TargetPoi: targetPoi);
    }
}
