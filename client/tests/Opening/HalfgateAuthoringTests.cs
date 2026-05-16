using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the Varn-locked A4 authoring for the Halfgate E2.1 tutorial
/// mission + 3 POC NPCs + rumor hooks. Each test anchors one Varn lock
/// from the reveal grammar (A4.2) or the NPC schema (A4.3).
/// </summary>
public sealed class HalfgateAuthoringTests
{
    // ----- Mission A4.2 + A4.7 schema -----

    [Fact]
    public void Tutorial_mission_id_is_locked()
    {
        Assert.Equal("mission_tuto_halfgate", HalfgateMissionAuthoring.TutorialMissionId);
        Assert.Equal("mission_tuto_halfgate", HalfgateMissionAuthoring.Tutorial.MissionId);
    }

    [Fact]
    public void Tutorial_mission_uses_A4_7_pop_effect_layer_grammar()
    {
        // A4.7 lock : pop_effect.poi_visible = true ;
        // tiles_to_partial_layer is the new REQUIRED field replacing
        // the flat tiles_to_partial list. The A3.2 fields reveal_level /
        // reveal_tile_count are SUPERSEDED and must not appear in this
        // record's shape -- the record type itself enforces that
        // (no such fields exist).
        var m = HalfgateMissionAuthoring.Tutorial;
        Assert.True(m.PopEffectPoiVisible);
        Assert.NotNull(m.PopEffectTilesToPartialLayer);

        // The tuto mission lists its cells at the E1 layer (the 4x4
        // Halfgate footprint). Auto-projection cascades to E2 at write
        // time per A4.7.1 Q1 (Varn lock 2026-05-16).
        Assert.True(m.PopEffectTilesToPartialLayer.ContainsKey(RevealLayer.E1));
        Assert.Equal(16, m.PopEffectTilesToPartialLayer[RevealLayer.E1].Count);
        Assert.Equal(
            HalfgateMissionAuthoring.HalfgateE1Footprint,
            m.PopEffectTilesToPartialLayer[RevealLayer.E1]);
    }

    [Fact]
    public void Tutorial_mission_does_not_list_cells_at_other_layers()
    {
        // A4.7.8 anti-drift : explicit cross-layer listing in a single
        // mission's tiles_to_partial is a Varn-locked-spec violation.
        // The tuto mission must list ONLY the E1 layer ; E2 (and any
        // future E3) cells are produced by auto-projection.
        var m = HalfgateMissionAuthoring.Tutorial;
        Assert.Single(m.PopEffectTilesToPartialLayer);
        Assert.False(m.PopEffectTilesToPartialLayer.ContainsKey(RevealLayer.E2));
        Assert.False(m.PopEffectTilesToPartialLayer.ContainsKey(RevealLayer.E3));
    }

    [Fact]
    public void Tutorial_mission_e1_footprint_is_a_4x4_block()
    {
        // A4.7.6 worked example : the Halfgate footprint at E1 is 4x4
        // (= 16 cells). At pop time R3 projection produces 4x4 * 4 = 64
        // E2 cells -- exactly the full E2 8x8 Halfgate grid.
        var cells = HalfgateMissionAuthoring.HalfgateE1Footprint;
        Assert.Equal(16, cells.Count);
        Assert.Equal(16, cells.Distinct().Count());

        // The block is 4 wide x 4 tall.
        int minCol = cells.Min(c => c.Col);
        int maxCol = cells.Max(c => c.Col);
        int minRow = cells.Min(c => c.Row);
        int maxRow = cells.Max(c => c.Row);
        Assert.Equal(3, maxCol - minCol);
        Assert.Equal(3, maxRow - minRow);
    }

    [Fact]
    public void Tutorial_mission_success_rule_is_by_recruited_npc()
    {
        // A4.2 lock : E2.1 tuto is a recruitment-style mission, so
        // success_effect.reveal_rule = by_recruited_npc. Schema is
        // ready ; the mechanic itself lands at E2.2.
        Assert.Equal(RevealRule.ByRecruitedNpc,
            HalfgateMissionAuthoring.Tutorial.SuccessEffectRevealRule);
    }

    [Fact]
    public void Tutorial_mission_fail_rule_is_keep_partial()
    {
        // A4.2 EC4 lock : the MVE default for reveal_on_fail.rule is
        // keep_partial for ALL mission types. A4.7 EC10 extends this :
        // the cross-layer-projected cells also stay partial. Any drift
        // here breaks the EC4 / EC10 contract.
        Assert.Equal(RevealOnFail.KeepPartial,
            HalfgateMissionAuthoring.Tutorial.RevealOnFailRule);
    }

    [Fact]
    public void Tutorial_mission_narrative_hook_is_within_pratchett_tone_budget()
    {
        // Tonal DNA constraint : 60-140 chars, single line, ends with
        // a period. Drift would surface as truncation in the tooltip
        // or a parodic register slip.
        var hook = HalfgateMissionAuthoring.Tutorial.NarrativeHook;
        Assert.InRange(hook.Length, 40, 160);
        Assert.DoesNotContain('\n', hook);
        Assert.EndsWith(".", hook);
    }

    // ----- NPC A4.3 schema -----

    [Fact]
    public void Three_poc_npcs_are_authored()
    {
        Assert.Equal(3, HalfgateNpcAuthoring.All.Count);
    }

    [Fact]
    public void Npc_ids_match_the_locked_naming_convention()
    {
        // A4.3 + spec §15 lock : npc_halfgate_0X ids.
        var ids = HalfgateNpcAuthoring.All.Select(n => n.NpcId).ToList();
        Assert.Contains("npc_halfgate_01", ids);
        Assert.Contains("npc_halfgate_02", ids);
        Assert.Contains("npc_halfgate_03", ids);
    }

    [Theory]
    [InlineData("npc_halfgate_01", DistrictType.Outskirts)]   // Scout
    [InlineData("npc_halfgate_02", DistrictType.Wall)]        // Warrior
    [InlineData("npc_halfgate_03", DistrictType.Intramuros)]  // Scholar
    public void Npc_district_origin_is_locked(string npcId, DistrictType expected)
    {
        var npc = HalfgateNpcAuthoring.FindById(npcId);
        Assert.NotNull(npc);
        Assert.Equal(expected, npc!.DistrictOrigin);
    }

    [Fact]
    public void Npc_known_tiles_are_all_in_bounds()
    {
        // A4.3 lock : known_tiles must reference real cells. An out-of-
        // bounds entry would silently no-op on the future recruit-merge
        // path (E2.2 wiring) without surfacing the typo.
        foreach (var npc in HalfgateNpcAuthoring.All)
        {
            foreach (var tile in npc.KnownTiles)
            {
                Assert.True(AreaGridLogic.IsInBounds(tile),
                    $"NPC {npc.NpcId} has out-of-bounds known_tile {tile}");
            }
        }
    }

    [Fact]
    public void Npc_known_tiles_counts_match_the_authoring_brief()
    {
        // Varn jalons §E2.1 specifies approximate counts (Scout ~6-8,
        // Warrior ~4-6, Scholar ~8-10). Pin the exact authored counts
        // so a future edit surfaces the change instead of drifting
        // silently.
        Assert.Equal(6, HalfgateNpcAuthoring.ScoutMaeren.KnownTiles.Count);
        Assert.Equal(5, HalfgateNpcAuthoring.WarriorOlbrec.KnownTiles.Count);
        Assert.Equal(8, HalfgateNpcAuthoring.ScholarVialis.KnownTiles.Count);
    }

    [Fact]
    public void Npc_portrait_cells_are_all_in_bounds_and_distinct()
    {
        var portraitCells = HalfgateNpcAuthoring.All.Select(n => n.PortraitCell).ToList();
        foreach (var c in portraitCells)
        {
            Assert.True(AreaGridLogic.IsInBounds(c),
                $"NPC portrait cell {c} is out of bounds");
        }
        Assert.Equal(portraitCells.Count, portraitCells.Distinct().Count());
    }

    [Fact]
    public void Find_by_id_returns_null_for_unknown_id()
    {
        Assert.Null(HalfgateNpcAuthoring.FindById("npc_does_not_exist"));
        Assert.Null(HalfgateNpcAuthoring.FindById(null));
        Assert.Null(HalfgateNpcAuthoring.FindById(string.Empty));
    }

    // ----- Rumor hooks (EC6 fallback) -----

    [Fact]
    public void Every_district_type_has_a_fallback_rumor_line()
    {
        // EC6 lock : the per-district fallback line is the safety net
        // for any partial/revealed cell whose hover doesn't match a
        // mission narrative. Missing a line would surface as an empty
        // tooltip half.
        foreach (var d in System.Enum.GetValues<DistrictType>())
        {
            var line = HalfgateRumorHooks.ResolveFallback(d);
            Assert.False(string.IsNullOrEmpty(line),
                $"District {d} has no fallback rumor hook");
            Assert.InRange(line.Length, 40, 200);
        }
    }

    [Fact]
    public void Compose_cell_tooltip_uses_mission_hook_when_supplied()
    {
        var mission = "A footing has been observed.";
        var text = HalfgateRumorHooks.ComposeCellTooltip(DistrictType.Intramuros, mission);
        Assert.Contains("Intramuros", text);
        Assert.Contains(mission, text);
    }

    [Fact]
    public void Compose_cell_tooltip_falls_back_when_mission_hook_null_or_empty()
    {
        // The fallback line lands when the cell is in a partial set NOT
        // owned by any mission with a narrative_hook (NPC known_tiles
        // partial reveals -- E2.2 wiring).
        var fallback = HalfgateRumorHooks.ResolveFallback(DistrictType.Littoral);
        var nullText = HalfgateRumorHooks.ComposeCellTooltip(DistrictType.Littoral, null);
        var emptyText = HalfgateRumorHooks.ComposeCellTooltip(DistrictType.Littoral, string.Empty);
        Assert.Contains("Littoral", nullText);
        Assert.Contains(fallback, nullText);
        Assert.Contains(fallback, emptyText);
    }
}
