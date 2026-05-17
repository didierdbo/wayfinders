using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// J5 contract tests for the E4 fiche cadastrale Godot-free seams
/// (D-J5-02 + D-J5-11 + D-J5-12). The Godot-side scene (TabContainer
/// switching, TextureRect overlays, portrait composition) cannot be
/// loaded from xUnit, but every contract that survives a Godot-free
/// round-trip is pinned here:
/// <list type="bullet">
///   <item>Title interpolation building blocks: fiche number lookup +
///         display name lookup -&gt; the title template substitution
///         is mechanical from those two pieces.</item>
///   <item>Subtitle template feed (always Calm in J5).</item>
///   <item>Portrait key composition (Kira calm = the J5 always-on
///         path).</item>
///   <item>NpcId payload key cross-scene contract (E3 + E5 thread
///         through the same key).</item>
/// </list>
///
/// <para>
/// The visual side (caviardage overlay covers the right region, label
/// renders centered, TabContainer switches between onglets) is covered
/// by the Godot manual checklist (J5 closeout §4). The xUnit assembly
/// cannot load Godot Resource subclasses (OpeningStrings, PoiDefinition)
/// nor Control / Node descendants (E4CharacterSheet, E3DistrictMap),
/// so the visual rendering is intentionally outside the test surface.
/// Same boundary as J4's <see cref="E4ModalContractTests"/>.
/// </para>
/// </summary>
public sealed class E4FicheRealContractTests
{
    [Fact]
    public void Default_portrait_state_is_calm_for_kira()
    {
        // J5 always feeds NpcPortraitState.Calm (D-J5-02 + Risk #2).
        // The seam exists for J6+ state-driven switching but the
        // CURRENT behaviour is locked here: opening the modal for
        // Kira composes the calm key. When J6+ flips this, this test
        // surfaces the change explicitly.
        var key = NpcCatalog.LookupPortraitKey("kira", NpcPortraitState.Calm);
        Assert.Equal("e4.portrait.kira.calm", key);
    }

    [Fact]
    public void Title_interpolation_building_blocks_for_kira()
    {
        // The Godot-side E4CharacterSheet.OnOpen does:
        //   title = template.Replace("[xxx]", ficheNumber)
        //                   .Replace("[Nom officiel]", displayName);
        // We pin the inputs of that interpolation (the template lives
        // in OpeningStrings, Godot-bound, so it is verified manually).
        // For Kira: ficheNumber = "001", displayName = "Kira" ->
        // title = "Fiche cadastrale n°001 — Sujet : Kira."
        Assert.Equal("001", NpcCatalog.LookupFicheNumber("kira"));
        Assert.Equal("Kira", NpcCatalog.LookupDisplayName("kira"));
    }

    [Fact]
    public void Title_interpolation_building_blocks_for_dorn()
    {
        // For Dorn: ficheNumber = "002", displayName = "Dorn" ->
        // title = "Fiche cadastrale n°002 — Sujet : Dorn."
        // J5 differentiates Dorn from Kira so a tester can spot the
        // NPC at a glance.
        Assert.Equal("002", NpcCatalog.LookupFicheNumber("dorn"));
        Assert.Equal("Dorn", NpcCatalog.LookupDisplayName("dorn"));
    }

    [Fact]
    public void Title_interpolation_building_blocks_for_vell()
    {
        // For Vell (Varn §A.8.D3 M1 roster, district affinity littoral):
        // ficheNumber = "003", displayName = "Vell" ->
        // title = "Fiche cadastrale n°003 — Sujet : Vell."
        // Stateless single portrait (Mira ships e4.portrait.vell.calm
        // base only ; per-state variants deferred).
        Assert.Equal("003", NpcCatalog.LookupFicheNumber("vell"));
        Assert.Equal("Vell", NpcCatalog.LookupDisplayName("vell"));
    }

    [Fact]
    public void Title_interpolation_building_blocks_for_unknown_npc()
    {
        // Unknown NPCs surface as "Inconnu" with the "000" stamp so
        // a missing catalog entry is visibly different from a real
        // fiche -- dev catches the regression at a glance.
        Assert.Equal("000", NpcCatalog.LookupFicheNumber("ghost"));
        Assert.Equal(NpcCatalog.DefaultDisplayName, NpcCatalog.LookupDisplayName("ghost"));
    }

    [Fact]
    public void Subtitle_template_feed_is_calme_for_J5()
    {
        // The Godot side does:
        //   subtitle = string.Format(template, state.ToFrenchLowercase())
        // Where template = "Sujet observé : {0}." (Varn §5 E4.N).
        // J5 always feeds NpcPortraitState.Calm -> "calme".
        Assert.Equal("calme", NpcPortraitState.Calm.ToFrenchLowercase());
    }

    [Fact]
    public void NpcCatalog_count_is_three_in_M1()
    {
        // Sanity: M1 ships Kira + Dorn + Vell (Varn §A.8.D3 roster
        // locked 2026-05-17). When M2+ adds an entry, this test reminds
        // the author to also extend the manual checklist + the dispatch
        // tests.
        Assert.Equal(3, NpcCatalog.Count);
    }
}
