using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Tests for the J5 <see cref="NpcCatalog"/> pure-C# helpers (the data
/// + lookup logic that the Godot autoload <c>NpcRegistry</c> wraps).
/// Same separation pattern as <see cref="PoiDispatchLogicKindTests"/>:
/// the engine-side wiring is one thin shim, the contract is here.
///
/// <para>
/// Surface covered:
/// <list type="bullet">
///   <item>Known NPC id (kira / dorn / vell) -&gt; correct display name
///         + portrait key composition.</item>
///   <item>Unknown / null / empty NPC id -&gt; default fallback name +
///         default portrait key (cohérence J3/J4 blocked wording).</item>
///   <item>Per-state portrait composition: stateful entry (Kira) gets
///         the suffix appended ; stateless entries (Dorn, Vell) ignore
///         the state argument.</item>
///   <item><see cref="NpcPortraitState.ToFrenchLowercase"/> mapping
///         for the Varn E4.N subtitle template substitution (J5 always
///         feeds <c>calme</c>).</item>
///   <item>Fiche number lookup (the "[xxx]" slot in the Varn E4.A
///         title template).</item>
/// </list>
/// </para>
/// </summary>
public sealed class NpcCatalogTests
{
    [Fact]
    public void Resolve_known_npc_kira_returns_display_name()
    {
        var entry = NpcCatalog.Resolve("kira");

        Assert.Equal("Kira", entry.DisplayName);
        Assert.Equal("e4.portrait.kira", entry.PortraitKeyBase);
        Assert.True(entry.HasPerStatePortraits);
    }

    [Fact]
    public void Resolve_known_npc_dorn_is_stateless()
    {
        var entry = NpcCatalog.Resolve("dorn");

        Assert.Equal("Dorn", entry.DisplayName);
        Assert.Equal("e4.portrait.dorn", entry.PortraitKeyBase);
        // Dorn has a single portrait in J5 (no per-state variants
        // shipped) -- stateless flag drives LookupPortraitKey to return
        // the bare base, no suffix.
        Assert.False(entry.HasPerStatePortraits);
    }

    [Fact]
    public void Resolve_known_npc_vell_is_stateless()
    {
        // Vell joins the M1 roster (Varn §A.8.D3, district affinity
        // littoral). Stateless single portrait like Dorn -- Mira ships
        // only e4.portrait.vell.calm base for M1, per-state variants
        // deferred to post-MVP if narrative beats demand them.
        var entry = NpcCatalog.Resolve("vell");

        Assert.Equal("Vell", entry.DisplayName);
        Assert.Equal("e4.portrait.vell", entry.PortraitKeyBase);
        Assert.False(entry.HasPerStatePortraits);
    }

    [Fact]
    public void Resolve_unknown_npc_returns_default_entry()
    {
        var entry = NpcCatalog.Resolve("inconnu_ne_existe_pas");

        // The default entry wears the catalog's default display name
        // and the default portrait key. It is stateless by design --
        // we never compose a per-state suffix on the fallback path.
        Assert.Equal(NpcCatalog.DefaultDisplayName, entry.DisplayName);
        Assert.Equal(NpcCatalog.DefaultPortraitKey, entry.PortraitKeyBase);
        Assert.False(entry.HasPerStatePortraits);
    }

    [Fact]
    public void Resolve_null_id_returns_default_entry()
    {
        var entry = NpcCatalog.Resolve(null);

        Assert.Equal(NpcCatalog.DefaultDisplayName, entry.DisplayName);
        Assert.Equal(NpcCatalog.DefaultPortraitKey, entry.PortraitKeyBase);
    }

    [Fact]
    public void Resolve_empty_string_id_returns_default_entry()
    {
        var entry = NpcCatalog.Resolve("");

        Assert.Equal(NpcCatalog.DefaultDisplayName, entry.DisplayName);
    }

    [Fact]
    public void LookupPortraitKey_stateful_kira_calm_appends_calm_suffix()
    {
        // Kira ships the 3-state portraits (calm/alert/wounded). J5
        // exercises only Calm, but the seam is here: the asset key is
        // composed by appending the state suffix to the catalog's
        // base. Tests for Alert/Wounded below pin the contract for
        // post-MVP state-driven switching.
        Assert.Equal(
            "e4.portrait.kira.calm",
            NpcCatalog.LookupPortraitKey("kira", NpcPortraitState.Calm));
    }

    [Fact]
    public void LookupPortraitKey_stateful_kira_alert_appends_alert_suffix()
    {
        Assert.Equal(
            "e4.portrait.kira.alert",
            NpcCatalog.LookupPortraitKey("kira", NpcPortraitState.Alert));
    }

    [Fact]
    public void LookupPortraitKey_stateful_kira_wounded_appends_wounded_suffix()
    {
        Assert.Equal(
            "e4.portrait.kira.wounded",
            NpcCatalog.LookupPortraitKey("kira", NpcPortraitState.Wounded));
    }

    [Fact]
    public void LookupPortraitKey_stateless_dorn_ignores_state_argument()
    {
        // Calm and Wounded must produce the SAME key for Dorn -- the
        // stateless flag drives the catalog to return the bare base.
        // This locks the behaviour: when J6+ ships per-state Dorn
        // portraits, this test will surface and remind the author to
        // flip HasPerStatePortraits on the catalog entry.
        var keyCalm = NpcCatalog.LookupPortraitKey("dorn", NpcPortraitState.Calm);
        var keyWounded = NpcCatalog.LookupPortraitKey("dorn", NpcPortraitState.Wounded);

        Assert.Equal("e4.portrait.dorn", keyCalm);
        Assert.Equal("e4.portrait.dorn", keyWounded);
    }

    [Fact]
    public void LookupPortraitKey_stateless_vell_ignores_state_argument()
    {
        // Same contract as Dorn -- stateless flag drives the catalog
        // to return the bare base for every state argument. When Mira
        // ships per-state Vell variants, flip HasPerStatePortraits and
        // this test surfaces the change.
        var keyCalm = NpcCatalog.LookupPortraitKey("vell", NpcPortraitState.Calm);
        var keyWounded = NpcCatalog.LookupPortraitKey("vell", NpcPortraitState.Wounded);

        Assert.Equal("e4.portrait.vell", keyCalm);
        Assert.Equal("e4.portrait.vell", keyWounded);
    }

    [Fact]
    public void LookupPortraitKey_unknown_npc_returns_default_key_no_suffix()
    {
        // Unknown NPCs return the default key, not "<default>.alert".
        // The default entry is stateless -- no suffix composition.
        var key = NpcCatalog.LookupPortraitKey("ghost", NpcPortraitState.Alert);
        Assert.Equal(NpcCatalog.DefaultPortraitKey, key);
    }

    [Fact]
    public void LookupDisplayName_known_and_unknown()
    {
        Assert.Equal("Kira", NpcCatalog.LookupDisplayName("kira"));
        Assert.Equal("Dorn", NpcCatalog.LookupDisplayName("dorn"));
        Assert.Equal("Vell", NpcCatalog.LookupDisplayName("vell"));
        Assert.Equal(NpcCatalog.DefaultDisplayName, NpcCatalog.LookupDisplayName("inconnu"));
    }

    [Fact]
    public void NpcPortraitState_french_lowercase_mapping()
    {
        // Locked Varn §5 E4.N template feed -- the subtitle line on the
        // E4 fiche reads "Sujet observé : {0}." -- {0} substituted with
        // these French lowercase values. J5 always feeds calme.
        Assert.Equal("calme", NpcPortraitState.Calm.ToFrenchLowercase());
        Assert.Equal("tendu", NpcPortraitState.Alert.ToFrenchLowercase());
        Assert.Equal("blessé", NpcPortraitState.Wounded.ToFrenchLowercase());
    }

    [Fact]
    public void LookupFicheNumber_known_npc_kira_returns_001()
    {
        // M1 NPC -> fiche cadastrale number map. Used by E4 modal title
        // interpolation. Kira / Dorn / Vell pre-numbered ; future NPCs
        // need an entry to escape the generic "000" stamp.
        Assert.Equal("001", NpcCatalog.LookupFicheNumber("kira"));
    }

    [Fact]
    public void LookupFicheNumber_known_npc_dorn_returns_002()
    {
        // Dorn pre-numbered in J5 so a future test playthrough can spot
        // the right NPC at a glance. The J4 stub hardcoded "001" for
        // every modal open.
        Assert.Equal("002", NpcCatalog.LookupFicheNumber("dorn"));
    }

    [Fact]
    public void LookupFicheNumber_known_npc_vell_returns_003()
    {
        // Vell joins the roster at fiche n°003 (Varn §A.8.D3 M1 lock,
        // district affinity littoral).
        Assert.Equal("003", NpcCatalog.LookupFicheNumber("vell"));
    }

    [Fact]
    public void LookupFicheNumber_unknown_npc_returns_000_stamp()
    {
        // Unknown NPCs get the generic "000" stamp -- visibly distinct
        // from real fiches so dev spots a missing catalog entry.
        Assert.Equal("000", NpcCatalog.LookupFicheNumber("ghost"));
        Assert.Equal("000", NpcCatalog.LookupFicheNumber(null));
        Assert.Equal("000", NpcCatalog.LookupFicheNumber(""));
    }
}
