using System.Collections.Generic;
using System.Linq;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin the E2.2 step 1 tooltip composer + the per-POI mission lookup.
/// Composer is pure-C# (no Godot binding) ; the tests anchor the layout
/// shape (header, bullet glyph, indent, blank-line separator) so a silent
/// edit surfaces as a red step. The Godot-bound hover wiring in
/// <c>E2AreaMap.AreaGrid.cs</c> (Area2D + tooltip controller call) is
/// exercised via the E2.2 step 1 manual F6 smoke (hover the 3 markers,
/// observe the tooltips after 600 ms).
/// </summary>
public sealed class HalfgateE2MissionTooltipTests
{
    [Fact]
    public void Compose_with_null_missions_returns_empty()
    {
        // The hover handler relies on the empty-string contract to decide
        // "do not surface a tooltip" (matches the spec : pas de tooltip
        // si pas de mission). A non-empty return here would surface a
        // degenerate "Intramuros\n" tooltip with no body.
        Assert.Equal(string.Empty, HalfgateE2MissionTooltip.Compose("Intramuros", null));
    }

    [Fact]
    public void Compose_with_empty_mission_list_returns_empty()
    {
        Assert.Equal(
            string.Empty,
            HalfgateE2MissionTooltip.Compose(
                "Intramuros",
                System.Array.Empty<HalfgateE2Mission>()));
    }

    [Fact]
    public void Compose_with_one_mission_starts_with_district_header()
    {
        var text = HalfgateE2MissionTooltip.Compose(
            "Intramuros",
            new[] { HalfgateE2MissionAuthoring.TutorialIntramuros });

        // Header is the FIRST line, nothing decorative before it.
        Assert.StartsWith("Intramuros\n", text);
    }

    [Fact]
    public void Compose_with_one_mission_includes_display_name_and_hook()
    {
        var mission = HalfgateE2MissionAuthoring.TutorialIntramuros;
        var text = HalfgateE2MissionTooltip.Compose("Intramuros", new[] { mission });

        // Both Varn-authored mission strings must round-trip into the
        // final tooltip surface. The composer is not allowed to truncate
        // or paraphrase.
        Assert.Contains(mission.DisplayName, text);
        Assert.Contains(mission.NarrativeHook, text);
    }

    [Fact]
    public void Compose_with_one_mission_prefixes_display_name_with_bullet()
    {
        var mission = HalfgateE2MissionAuthoring.TutorialGateway;
        var text = HalfgateE2MissionTooltip.Compose("Gateway", new[] { mission });

        // Bullet glyph (locked at "- ") must appear immediately before
        // the DisplayName. A silent change to a different glyph would
        // shift the visual rhythm of the tooltip.
        Assert.Contains(HalfgateE2MissionTooltip.MissionBullet + mission.DisplayName, text);
    }

    [Fact]
    public void Compose_with_one_mission_indents_the_narrative_hook()
    {
        var mission = HalfgateE2MissionAuthoring.TutorialLittoral;
        var text = HalfgateE2MissionTooltip.Compose("Littoral", new[] { mission });

        // The narrative hook must sit on its own line, indented under the
        // mission DisplayName. The "\n<indent><hook>" substring is the
        // pinned shape.
        Assert.Contains("\n" + HalfgateE2MissionTooltip.HookIndent + mission.NarrativeHook, text);
    }

    [Fact]
    public void Compose_with_two_missions_separates_them_with_a_blank_line()
    {
        // Forward-compatibility check : E2.1c has one mission per POI,
        // but the E2.2+ schema can land multiple. The composer must keep
        // a blank line between missions so the wall of text stays
        // legible.
        var missions = new[]
        {
            HalfgateE2MissionAuthoring.TutorialIntramuros,
            HalfgateE2MissionAuthoring.TutorialGateway,
        };
        var text = HalfgateE2MissionTooltip.Compose("Intramuros", missions);

        // Both mission bodies must be present.
        Assert.Contains(missions[0].DisplayName, text);
        Assert.Contains(missions[1].DisplayName, text);
        Assert.Contains(missions[0].NarrativeHook, text);
        Assert.Contains(missions[1].NarrativeHook, text);

        // Pin the separator : the second mission's bullet line sits after
        // the first mission's narrative hook + a blank line.
        var separator =
            missions[0].NarrativeHook
            + "\n\n"
            + HalfgateE2MissionTooltip.MissionBullet
            + missions[1].DisplayName;
        Assert.Contains(separator, text);
    }

    [Fact]
    public void Compose_preserves_mission_order_from_caller()
    {
        // The composer is not allowed to sort. The caller
        // (HalfgateE2MissionAuthoring.All static order, or a future
        // priority-sorted upstream) owns ordering. We pin Littoral
        // BEFORE Intramuros to surface a sort regression.
        var missions = new[]
        {
            HalfgateE2MissionAuthoring.TutorialLittoral,
            HalfgateE2MissionAuthoring.TutorialIntramuros,
        };
        var text = HalfgateE2MissionTooltip.Compose("Halfgate", missions);

        var littoralIdx = text.IndexOf(missions[0].DisplayName);
        var intramurosIdx = text.IndexOf(missions[1].DisplayName);

        Assert.True(littoralIdx >= 0);
        Assert.True(intramurosIdx >= 0);
        Assert.True(littoralIdx < intramurosIdx,
            $"Mission order was not preserved : Littoral idx={littoralIdx} " +
            $"should be before Intramuros idx={intramurosIdx}.");
    }

    // ---- FindByPoiId ----

    [Fact]
    public void FindByPoiId_returns_empty_when_no_match()
    {
        // A typo'd poi_id (drift between marker spawn + hover lookup)
        // must yield an empty enumeration -- the composer then returns
        // string.Empty -- the hover handler then skips RequestTooltip.
        // Defensive : a silent fall-through to "all missions" would be a
        // bad surprise.
        var matches = HalfgateE2MissionTooltip.FindByPoiId(
            HalfgateE2MissionAuthoring.All,
            "halfgate.does_not_exist");
        Assert.Empty(matches);
    }

    [Fact]
    public void FindByPoiId_returns_the_single_mission_for_an_e2_1c_poi()
    {
        // E2.1c invariant : one mission per POI, one POI per district.
        // The lookup must return exactly one entry per Varn-locked poi_id.
        foreach (var expected in HalfgateE2MissionAuthoring.All)
        {
            var matches = HalfgateE2MissionTooltip
                .FindByPoiId(HalfgateE2MissionAuthoring.All, expected.PoiId)
                .ToList();
            Assert.Single(matches);
            Assert.Equal(expected.MissionId, matches[0].MissionId);
        }
    }

    [Fact]
    public void FindByPoiId_returns_multiple_missions_for_a_shared_poi_id()
    {
        // Forward-compatibility : E2.2+ may land multiple missions on the
        // same POI (e.g. two simultaneous Gateway contracts). The lookup
        // must enumerate all matches without dedup, in source order. We
        // synthesise a duplicate-poi_id set to pin this without touching
        // the Varn-locked HalfgateE2MissionAuthoring.All.
        var custom = new List<HalfgateE2Mission>
        {
            HalfgateE2MissionAuthoring.TutorialIntramuros,
            HalfgateE2MissionAuthoring.TutorialGateway,
            // Synthesised second Intramuros-anchored mission, same PoiId
            // as TutorialIntramuros, distinct MissionId.
            new(
                MissionId: "mission_synthetic_intramuros_second",
                DisplayName: "Intramuros : a second concern",
                NarrativeHook: "Another matter has surfaced, also concerning the inner ledgers.",
                PoiLayer: RevealLayer.E2,
                PoiId: "halfgate.intramuros",
                TargetDistrict: DistrictType.Intramuros,
                PopEffectTilesToPartialLayer:
                    new Dictionary<RevealLayer, IReadOnlyList<GridCoord>>(),
                SuccessEffectRevealRule: MissionRevealRule.None),
        };

        var matches = HalfgateE2MissionTooltip
            .FindByPoiId(custom, "halfgate.intramuros")
            .ToList();

        Assert.Equal(2, matches.Count);
        Assert.Equal(
            HalfgateE2MissionAuthoring.IntramurosMissionId,
            matches[0].MissionId);
        Assert.Equal(
            "mission_synthetic_intramuros_second",
            matches[1].MissionId);
    }
}
