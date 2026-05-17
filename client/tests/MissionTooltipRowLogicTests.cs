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
    // Halfgate E1 cross-layer alias -- Varn §A.8.D1 R9 (2026-05-17 bug B fix)
    // ====================================================================

    [Fact]
    public void HalfgateE1CrossLayer_e1_halfgate_matches_e2_halfgate_descendants()
    {
        // The whole point of the cross-layer predicate : when the player
        // hovers the E1 Halfgate POI, the tooltip must surface missions
        // emitted by the ML at the E2 layer (target_poi = "e2.halfgate.<district>").
        // Pure prefix-match would miss these because the layer prefix
        // changes from e1 to e2.
        var predicate = MissionTooltipRowLogic.HalfgateE1CrossLayerPredicate(PrefixMatch);

        Assert.True(predicate("e2.halfgate.gateway", "e1.halfgate"));
        Assert.True(predicate("e2.halfgate.intramuros", "e1.halfgate"));
        Assert.True(predicate("e2.halfgate.littoral", "e1.halfgate"));
        Assert.True(predicate("e2.halfgate", "e1.halfgate"));    // exact e2 root
    }

    [Fact]
    public void HalfgateE1CrossLayer_e1_halfgate_still_matches_itself_via_base_predicate()
    {
        // Backward compat : a legacy mission targeting "e1.halfgate"
        // directly (not e2.*) must still match when hovering e1.halfgate.
        // The base prefix-match handles this ; the wrapper must not regress it.
        var predicate = MissionTooltipRowLogic.HalfgateE1CrossLayerPredicate(PrefixMatch);
        Assert.True(predicate("e1.halfgate", "e1.halfgate"));
        Assert.True(predicate("e1.halfgate.extra", "e1.halfgate"));
    }

    [Fact]
    public void HalfgateE1CrossLayer_e1_halfgate_rejects_unrelated_regions()
    {
        // The alias is scoped to the Halfgate pair ; missions on other
        // E1 regions (e.g. e1.veylant) or on e2 sub-maps of other regions
        // must NOT leak into the E1 Halfgate tooltip.
        var predicate = MissionTooltipRowLogic.HalfgateE1CrossLayerPredicate(PrefixMatch);

        Assert.False(predicate("e1.veylant", "e1.halfgate"));
        Assert.False(predicate("e2.veylant.docks", "e1.halfgate"));
        Assert.False(predicate("", "e1.halfgate"));
        Assert.False(predicate("e2.halfgate2.gateway", "e1.halfgate")); // sibling-prefix trap
    }

    [Fact]
    public void HalfgateE1CrossLayer_non_halfgate_hovered_id_falls_back_to_base_predicate()
    {
        // When the hovered POI is NOT "e1.halfgate" (e.g. hovering an
        // E2 district marker directly), the wrapper must behave EXACTLY
        // like the injected base predicate -- no cross-layer expansion.
        // Otherwise hovering e2.halfgate.gateway would over-collect.
        var predicate = MissionTooltipRowLogic.HalfgateE1CrossLayerPredicate(PrefixMatch);

        // From e2.halfgate.gateway, only exact + same-layer-descendant match.
        Assert.True(predicate("e2.halfgate.gateway", "e2.halfgate.gateway"));
        Assert.True(predicate("e2.halfgate.gateway.tavern", "e2.halfgate.gateway"));
        // Cross-layer alias does NOT kick in for non-Halfgate ancestor.
        Assert.False(predicate("e2.halfgate.intramuros", "e2.halfgate.gateway"));
        Assert.False(predicate("e1.halfgate", "e2.halfgate.gateway"));
    }

    [Fact]
    public void Build_with_cross_layer_predicate_aggregates_e2_halfgate_missions_under_e1_halfgate_hover()
    {
        // End-to-end pin : hovered = "e1.halfgate" + missions emitted by
        // the M1 backend at e2.halfgate.<district> level -> all three
        // surface in the tooltip rows. This is the regression check for
        // Didier's bug B (R9, 2026-05-17) : the player sees the popped
        // mission in the E1 Halfgate tooltip.
        var missions = new[]
        {
            NewMission(id: "ml_kira", targetPoi: "e2.halfgate.gateway"),
            NewMission(id: "ml_dorn", targetPoi: "e2.halfgate.intramuros"),
            NewMission(id: "ml_vell", targetPoi: "e2.halfgate.littoral"),
            NewMission(id: "ml_other", targetPoi: "e1.veylant"),  // unrelated, excluded
        };

        var predicate = MissionTooltipRowLogic.HalfgateE1CrossLayerPredicate(PrefixMatch);
        var result = MissionTooltipRowLogic.Build("e1.halfgate", missions, predicate);

        Assert.Equal(3, result.TotalMatching);
        Assert.Equal(3, result.Rows.Count);
        // Sort is by Id ordinal, so dorn / kira / other-letters check.
        Assert.Contains("ml_dorn", result.Rows[0]);
        Assert.Contains("ml_kira", result.Rows[1]);
        Assert.Contains("ml_vell", result.Rows[2]);
    }

    [Fact]
    public void HalfgateE1CrossLayer_null_base_predicate_throws()
    {
        // Defensive : the wrapper must throw rather than silently degrade
        // if the caller forgets to inject the base predicate. This is
        // the same shape as Build's null-predicate guard.
        Assert.Throws<System.ArgumentNullException>(() =>
            MissionTooltipRowLogic.HalfgateE1CrossLayerPredicate(null!));
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
