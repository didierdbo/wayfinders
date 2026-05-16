using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="TileRevealState"/> +
/// <see cref="TileRevealStateHelpers"/>, Phase C-bis-2 (Varn
/// reconciliation 2026-05-16 §3.2 + §3.3).
///
/// <para>
/// <b>Why these tests are load-bearing.</b> The 3-state vocab is the
/// authoritative source-of-truth for the face-B reveal subsystem and
/// the future runtime gameplay reveal events (NPC recruit known_tiles,
/// mission success_effect, reveal_on_fail). The derived float mapping
/// (Fog=0.0 / Partial=0.55 / Revealed=1.0) is pinned by Varn at the
/// Render layer. The ladder-to-reveal projection is the read-only
/// one-way bridge between the Cadastre 5-state and this 3-state ;
/// renumbering either vocab here breaks the bridge silently.
/// </para>
/// </summary>
public sealed class TileRevealStateTests
{
    [Fact]
    public void Enum_integer_values_are_pinned_for_persistence_compat()
    {
        // Persistence schema lock — Varn reconciliation §5.1 anchor
        // tile_reveal_state_enum. Renumbering would silently corrupt
        // session-level snapshots (and any future save format that
        // serialises GameState.TileRevealStates).
        Assert.Equal(0, (int)TileRevealState.Fog);
        Assert.Equal(1, (int)TileRevealState.Partial);
        Assert.Equal(2, (int)TileRevealState.Revealed);
    }

    [Fact]
    public void Resolve_reveal_level_returns_the_locked_float_mapping()
    {
        // Render contract lock — Varn reconciliation §3.3.
        // TileRevealRenderController uses these as Tween source/target.
        Assert.Equal(0.00f, TileRevealStateHelpers.ResolveRevealLevel(TileRevealState.Fog));
        Assert.Equal(0.55f, TileRevealStateHelpers.ResolveRevealLevel(TileRevealState.Partial));
        Assert.Equal(1.00f, TileRevealStateHelpers.ResolveRevealLevel(TileRevealState.Revealed));
    }

    [Fact]
    public void Reveal_level_constants_match_the_helper_lookup()
    {
        // The constants are the public face of the locked numbers ; the
        // helper switches on the enum. Cross-pin so a future refactor
        // that touches one but not the other fails loudly.
        Assert.Equal(TileRevealStateHelpers.RevealLevelFog,
                     TileRevealStateHelpers.ResolveRevealLevel(TileRevealState.Fog));
        Assert.Equal(TileRevealStateHelpers.RevealLevelPartial,
                     TileRevealStateHelpers.ResolveRevealLevel(TileRevealState.Partial));
        Assert.Equal(TileRevealStateHelpers.RevealLevelRevealed,
                     TileRevealStateHelpers.ResolveRevealLevel(TileRevealState.Revealed));
    }

    [Fact]
    public void Reveal_level_is_monotone_in_visibility_order()
    {
        // The "le niveau permanent ne descend jamais" invariant
        // (Varn §1.3 note systémique) requires the derived float to be
        // monotonically non-decreasing along the enum order. A clamp /
        // max() over states is only meaningful if this holds.
        Assert.True(TileRevealStateHelpers.RevealLevelFog
                    < TileRevealStateHelpers.RevealLevelPartial);
        Assert.True(TileRevealStateHelpers.RevealLevelPartial
                    < TileRevealStateHelpers.RevealLevelRevealed);
    }

    // ---------------------------------------------------------------
    // Ladder -> Reveal projection (Varn §2 D1 lock)
    // ---------------------------------------------------------------

    [Fact]
    public void Inconnue_projects_to_Fog()
    {
        Assert.Equal(TileRevealState.Fog,
                     TileRevealStateHelpers.LadderToReveal(TileKnowledgeLadder.Inconnue));
    }

    [Fact]
    public void Pressentie_projects_to_Fog_rumor_stays_diegetic()
    {
        // Varn §2 D1 : Pressentie carries the "rumeur, longue-vue"
        // visual in the FogTileLayer (back-alpha 0.7 + face-alpha 0.3)
        // but contributes ZERO alpha to the face-B reveal substrate.
        // The diegetic rumor signal is rendered by FogTileLayer, not
        // by the reveal layer.
        Assert.Equal(TileRevealState.Fog,
                     TileRevealStateHelpers.LadderToReveal(TileKnowledgeLadder.Pressentie));
    }

    [Fact]
    public void Esquissee_projects_to_Partial()
    {
        Assert.Equal(TileRevealState.Partial,
                     TileRevealStateHelpers.LadderToReveal(TileKnowledgeLadder.Esquissee));
    }

    [Fact]
    public void Levee_projects_to_Revealed()
    {
        Assert.Equal(TileRevealState.Revealed,
                     TileRevealStateHelpers.LadderToReveal(TileKnowledgeLadder.Levee));
    }

    [Fact]
    public void Scellee_projects_to_Revealed_sceau_is_independent()
    {
        // Varn §2 D1 : Scellée's sceau-de-cire is rendered independently
        // by FogTileLayer (ShouldRenderSceau predicate) ; it does NOT
        // affect the reveal substrate. Sceau presence + Revealed are
        // orthogonal axes from the reveal layer's point of view.
        Assert.Equal(TileRevealState.Revealed,
                     TileRevealStateHelpers.LadderToReveal(TileKnowledgeLadder.Scellee));
    }
}
