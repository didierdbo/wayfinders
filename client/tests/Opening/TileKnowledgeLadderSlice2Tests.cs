using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Slice 2 tests for <see cref="TileKnowledgeLadderHelpers"/> additions
/// (M3 / L1 World fondations / 2026-05-08). Pins the per-state carton
/// alpha resolver, the swatch + sceau visibility predicates, and the
/// cycle-next / cycle-previous ladder walkers used by the slice 2
/// debug surface (livrable 4 F-key dispatcher).
///
/// <para>
/// <b>Slice 3.6 design fix touch-up.</b> The single-carton vocabulary
/// pinned by this fixture (<see cref="TileKnowledgeLadderHelpers.ResolveCartonAlpha"/>)
/// is preserved as a back-compat alias for the BACK alpha (since "the
/// carton" originally meant the opaque blocker). The PaletteSwatch
/// predicate is updated to reflect the retirement of the swatch — see
/// <see cref="Should_render_palette_swatch_is_retired"/>. The new back/face
/// resolvers are pinned in the dedicated slice 3.6 design-fix fixture
/// <c>TileKnowledgeLadderSlice36DesignFixTests</c>.
/// </para>
///
/// <para>
/// <b>Why these are load-bearing.</b> Slice 2's visual contract is
/// "every state has a distinct alpha + sceau combination". The renderer
/// reads these helpers at spawn and on every signal emission ; if the
/// helpers drift the four-state debug cycle starts looking like a
/// two-state toggle and the livrable 2 acceptance "tous les niveaux
/// observables visuellement" fails silently.
/// </para>
/// </summary>
public sealed class TileKnowledgeLadderSlice2Tests
{
    [Theory]
    [InlineData(TileKnowledgeLadder.Inconnue,   1.0f)]
    [InlineData(TileKnowledgeLadder.Pressentie, 0.70f)]
    [InlineData(TileKnowledgeLadder.Esquissee,  0.40f)]
    [InlineData(TileKnowledgeLadder.Levee,      0.0f)]
    [InlineData(TileKnowledgeLadder.Scellee,    0.0f)]
    public void Resolve_carton_alpha_pins_slice_2_table(TileKnowledgeLadder state, float expected)
    {
        // Slice 2 visual contract — preserved as a back-compat alias for
        // the slice 3.6 design fix CartonBack alpha. "The carton" meant
        // the opaque blocker before the back/face split, and the back is
        // the opaque blocker now.
        Assert.Equal(expected, TileKnowledgeLadderHelpers.ResolveCartonAlpha(state), precision: 3);
    }

    [Fact]
    public void Carton_alpha_is_strictly_decreasing_across_visible_states()
    {
        // Monotonicity invariant — the ladder Inconnue → Pressentie →
        // Esquissée must show progressively less back. Locks the order
        // at the test layer so a swap of two values fails here, not in
        // a manual smoke test.
        var inc = TileKnowledgeLadderHelpers.ResolveCartonAlpha(TileKnowledgeLadder.Inconnue);
        var pre = TileKnowledgeLadderHelpers.ResolveCartonAlpha(TileKnowledgeLadder.Pressentie);
        var esq = TileKnowledgeLadderHelpers.ResolveCartonAlpha(TileKnowledgeLadder.Esquissee);
        var lev = TileKnowledgeLadderHelpers.ResolveCartonAlpha(TileKnowledgeLadder.Levee);

        Assert.True(inc > pre, $"Inconnue alpha {inc} should be > Pressentie {pre}");
        Assert.True(pre > esq, $"Pressentie alpha {pre} should be > Esquissée {esq}");
        Assert.True(esq > lev, $"Esquissée alpha {esq} should be > Levée {lev}");
    }

    [Theory]
    [InlineData(TileKnowledgeLadder.Inconnue,   false)]
    [InlineData(TileKnowledgeLadder.Pressentie, false)]
    [InlineData(TileKnowledgeLadder.Esquissee,  false)]
    [InlineData(TileKnowledgeLadder.Levee,      false)]
    [InlineData(TileKnowledgeLadder.Scellee,    false)]
    public void Should_render_palette_swatch_is_retired(
        TileKnowledgeLadder state, bool expected)
    {
        // Slice 3.6 design fix — the PaletteSwatch is retired. The
        // CartonFace polygon now carries the diegetic "hint of palette"
        // signal at Pressentie / Esquissée by showing the actual
        // source-map slice. The swatch node stays in the scene tree for
        // back-compat (consumers that walk by name don't crash) but is
        // never shown. If a future slice wants the swatch back (e.g.
        // for a stylised parchemin overlay), it can flip this predicate
        // in one place.
        Assert.Equal(expected, TileKnowledgeLadderHelpers.ShouldRenderPaletteSwatch(state));
    }

    [Theory]
    [InlineData(TileKnowledgeLadder.Inconnue,   false)]
    [InlineData(TileKnowledgeLadder.Pressentie, false)]
    [InlineData(TileKnowledgeLadder.Esquissee,  false)]
    [InlineData(TileKnowledgeLadder.Levee,      false)]
    [InlineData(TileKnowledgeLadder.Scellee,    true)]
    public void Should_render_sceau_only_at_scellee(
        TileKnowledgeLadder state, bool expected)
    {
        // Sceau de cire is the only visual distinction between Levée
        // and Scellée at slice 2 (Varn §1.2). Brief slice 2 livrable 2 :
        // "Scellée = comme Levée plus sceau placeholder".
        Assert.Equal(expected, TileKnowledgeLadderHelpers.ShouldRenderSceau(state));
    }

    [Theory]
    [InlineData(TileKnowledgeLadder.Inconnue,   TileKnowledgeLadder.Pressentie)]
    [InlineData(TileKnowledgeLadder.Pressentie, TileKnowledgeLadder.Esquissee)]
    [InlineData(TileKnowledgeLadder.Esquissee,  TileKnowledgeLadder.Levee)]
    [InlineData(TileKnowledgeLadder.Levee,      TileKnowledgeLadder.Scellee)]
    [InlineData(TileKnowledgeLadder.Scellee,    TileKnowledgeLadder.Inconnue)]
    public void Cycle_next_walks_the_ladder_with_wrap(
        TileKnowledgeLadder start, TileKnowledgeLadder expected)
    {
        // Slice 2 livrable 4 F-key contract : Inconnue → Pressentie →
        // Esquissée → Levée → Scellée → Inconnue. The wrap matters
        // for the "tous les niveaux observables" criterion : a single
        // F repeated 5 times returns to the starting state.
        Assert.Equal(expected, TileKnowledgeLadderHelpers.CycleNext(start));
    }

    [Theory]
    [InlineData(TileKnowledgeLadder.Inconnue,   TileKnowledgeLadder.Scellee)]
    [InlineData(TileKnowledgeLadder.Pressentie, TileKnowledgeLadder.Inconnue)]
    [InlineData(TileKnowledgeLadder.Esquissee,  TileKnowledgeLadder.Pressentie)]
    [InlineData(TileKnowledgeLadder.Levee,      TileKnowledgeLadder.Esquissee)]
    [InlineData(TileKnowledgeLadder.Scellee,    TileKnowledgeLadder.Levee)]
    public void Cycle_previous_walks_the_ladder_backwards_with_wrap(
        TileKnowledgeLadder start, TileKnowledgeLadder expected)
    {
        // Shift+F dual of CycleNext.
        Assert.Equal(expected, TileKnowledgeLadderHelpers.CyclePrevious(start));
    }

    [Fact]
    public void Cycle_next_then_previous_returns_to_start()
    {
        // Inverse property — composing the two should be identity for
        // every state. Catches an accidental asymmetry (e.g. a
        // CycleNext that skips Esquissée when CyclePrevious doesn't).
        foreach (TileKnowledgeLadder start in System.Enum.GetValues(typeof(TileKnowledgeLadder)))
        {
            var roundTrip = TileKnowledgeLadderHelpers.CyclePrevious(
                TileKnowledgeLadderHelpers.CycleNext(start));
            Assert.Equal(start, roundTrip);
        }
    }

    [Fact]
    public void Cycle_next_five_times_is_identity()
    {
        // Five-step closure : five next-clicks returns to start.
        // Enforces the wrap convention.
        foreach (TileKnowledgeLadder start in System.Enum.GetValues(typeof(TileKnowledgeLadder)))
        {
            var s = start;
            for (int i = 0; i < 5; i++)
            {
                s = TileKnowledgeLadderHelpers.CycleNext(s);
            }
            Assert.Equal(start, s);
        }
    }
}
