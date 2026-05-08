using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="TileKnowledgeState"/> +
/// <see cref="TileKnowledgeStateHelpers"/> (slice 1 / livrable 3 — M3 /
/// L1 World fondations / 2026-05-08). Pins the MVP rendering contract:
/// only <see cref="TileKnowledgeState.Levee"/> and
/// <see cref="TileKnowledgeState.Scellee"/> hide the fog tile, every
/// other state renders opaque carton placeholder. The five-level
/// integer ladder Varn §1.1 locked stays stable across refactors so
/// the persistence schema does not break when slice 2 lands.
///
/// <para>
/// <b>Why these tests are load-bearing.</b> The slice 1 visual
/// distinction is binary (Inconnue vs Levée), but the enum carries
/// five values because the persistence shape must be ready for slice
/// 2's intermediate states. If a future refactor "simplifies" the
/// enum to a 2-value boolean-equivalent before slice 2 ships, the
/// save format breaks silently — these tests are the canary that
/// surfaces the regression at xUnit time.
/// </para>
/// </summary>
public sealed class TileKnowledgeStateTests
{
    [Fact]
    public void Enum_integer_values_are_pinned_for_persistence_compat()
    {
        // Persistence schema lock — Varn §1.1 + slice 1 brief : the
        // integer cast of each state is the on-disk value. Renumbering
        // would silently corrupt save files. If this test fails,
        // someone reordered the enum.
        Assert.Equal(0, (int)TileKnowledgeState.Inconnue);
        Assert.Equal(1, (int)TileKnowledgeState.Pressentie);
        Assert.Equal(2, (int)TileKnowledgeState.Esquissee);
        Assert.Equal(3, (int)TileKnowledgeState.Levee);
        Assert.Equal(4, (int)TileKnowledgeState.Scellee);
    }

    [Fact]
    public void Inconnue_renders_opaque()
    {
        Assert.True(TileKnowledgeStateHelpers.ShouldRenderOpaque(TileKnowledgeState.Inconnue));
        Assert.False(TileKnowledgeStateHelpers.IsHidden(TileKnowledgeState.Inconnue));
    }

    [Fact]
    public void Pressentie_renders_opaque_in_slice_1_mvp()
    {
        // MVP carve-out : intermediate states are treated as Inconnue
        // until slice 2 introduces graduated alpha. If this assertion
        // changes, slice 2 has shipped — update the helper signature
        // and these tests in the same diff.
        Assert.True(TileKnowledgeStateHelpers.ShouldRenderOpaque(TileKnowledgeState.Pressentie));
    }

    [Fact]
    public void Esquissee_renders_opaque_in_slice_1_mvp()
    {
        Assert.True(TileKnowledgeStateHelpers.ShouldRenderOpaque(TileKnowledgeState.Esquissee));
    }

    [Fact]
    public void Levee_is_hidden()
    {
        Assert.False(TileKnowledgeStateHelpers.ShouldRenderOpaque(TileKnowledgeState.Levee));
        Assert.True(TileKnowledgeStateHelpers.IsHidden(TileKnowledgeState.Levee));
    }

    [Fact]
    public void Scellee_is_hidden_same_as_levee()
    {
        // Varn §1.2 : Levée and Scellée are visually equivalent in the
        // fog-tile rendering — the sceau lands on the iso ground, not
        // on the carton. The helper does not distinguish them.
        Assert.False(TileKnowledgeStateHelpers.ShouldRenderOpaque(TileKnowledgeState.Scellee));
        Assert.True(TileKnowledgeStateHelpers.IsHidden(TileKnowledgeState.Scellee));
    }

    [Fact]
    public void Toggle_inconnue_to_levee()
    {
        Assert.Equal(TileKnowledgeState.Levee, TileKnowledgeStateHelpers.Toggle(TileKnowledgeState.Inconnue));
    }

    [Fact]
    public void Toggle_levee_to_inconnue()
    {
        Assert.Equal(TileKnowledgeState.Inconnue, TileKnowledgeStateHelpers.Toggle(TileKnowledgeState.Levee));
    }

    [Theory]
    [InlineData(TileKnowledgeState.Pressentie)]
    [InlineData(TileKnowledgeState.Esquissee)]
    [InlineData(TileKnowledgeState.Scellee)]
    public void Toggle_canonicalises_intermediate_states_to_levee(TileKnowledgeState start)
    {
        // Slice 1 MVP : the toggle treats anything-not-Levée as
        // Inconnue-equivalent and flips it to Levée. Intermediate
        // states should never be entered by the slice 1 debug
        // command, but if they are (e.g. a future feature seeds
        // Pressentie before slice 2 ships) the toggle stays predictable.
        Assert.Equal(TileKnowledgeState.Levee, TileKnowledgeStateHelpers.Toggle(start));
    }
}
