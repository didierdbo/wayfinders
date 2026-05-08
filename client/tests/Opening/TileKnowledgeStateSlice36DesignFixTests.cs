using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Slice 3.6 design fix tests for <see cref="TileKnowledgeStateHelpers"/>
/// (M3 / Phase 9 / 2026-05-08). Pins the two-polygon carton vocabulary
/// introduced to fix the obscuration semantic broken by the slice 3.6
/// hotfix <c>659b5e6</c>.
///
/// <para>
/// <b>Bug context.</b> The slice 3.6 hotfix made the single Carton
/// Polygon2D show the source-map slice for every state, then modulated
/// it by a beige tint at full alpha. Result : a beige-tinted source map
/// at Inconnue, not an opaque beige carton. The user perceived
/// Esquissée as more opaque than Inconnue because the beige tint faded
/// together with the alpha, exposing more of the source map. The
/// progression "carton hides → carte revealed" was visually inverted.
/// </para>
///
/// <para>
/// <b>Fix.</b> Two stacked Polygon2D children per cell — CartonBack
/// (no texture, beige opaque) and CartonFace (textured, source-map
/// slice). The two have independent alpha resolvers, pinned here. At
/// every state the visual is a deterministic combination :
/// <list type="bullet">
///   <item>Inconnue : back 1.0 + face 0.0 → opaque beige losange.</item>
///   <item>Pressentie : back 0.7 + face 0.3 → mostly beige, source-map
///         slice peeking through.</item>
///   <item>Esquissée : back 0.4 + face 0.6 → source-map dominates.</item>
///   <item>Levée : back 0.0 + face 0.0 → both invisible, world map
///         sprite below carries the visual.</item>
///   <item>Scellée : same as Levée + sceau placeholder.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why these tests are load-bearing.</b> The two resolvers are the
/// single source of truth for "what does the cell look like at state X ?".
/// If a future slice "cleans up" by collapsing them back into one (a
/// classic dry-up reflex), or swaps two values, the visual contract
/// silently regresses to either the slice 2 single-carton rendering
/// (no source-map preview at intermediate states) or worse, the broken
/// hotfix rendering (beige-tinted source map at Inconnue). Both fail
/// here, not in a manual smoke test.
/// </para>
/// </summary>
public sealed class TileKnowledgeStateSlice36DesignFixTests
{
    // -------------------------------------------------------------------
    // CartonBack alpha — the beige opaque dos de carte.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(TileKnowledgeState.Inconnue,   1.0f)]
    [InlineData(TileKnowledgeState.Pressentie, 0.70f)]
    [InlineData(TileKnowledgeState.Esquissee,  0.40f)]
    [InlineData(TileKnowledgeState.Levee,      0.0f)]
    [InlineData(TileKnowledgeState.Scellee,    0.0f)]
    public void Resolve_carton_back_alpha_pins_design_fix_table(
        TileKnowledgeState state, float expected)
    {
        // CartonBack is the beige opaque blocker — same numeric values as
        // the slice 2 single-carton table (back-compat with the slice 2
        // F-key debug expectations and ResolveCartonAlpha alias).
        Assert.Equal(expected, TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state), precision: 3);
    }

    [Fact]
    public void Carton_back_alpha_is_strictly_decreasing_through_visible_states()
    {
        // Monotonicity invariant — the back must hide LESS as the player
        // advances. A swap of Pressentie / Esquissée would flip the
        // legibility ladder ; this test catches it.
        var inc = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(TileKnowledgeState.Inconnue);
        var pre = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(TileKnowledgeState.Pressentie);
        var esq = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(TileKnowledgeState.Esquissee);
        var lev = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(TileKnowledgeState.Levee);

        Assert.True(inc > pre, $"Inconnue back alpha {inc} should be > Pressentie {pre}");
        Assert.True(pre > esq, $"Pressentie back alpha {pre} should be > Esquissée {esq}");
        Assert.True(esq > lev, $"Esquissée back alpha {esq} should be > Levée {lev}");
    }

    // -------------------------------------------------------------------
    // CartonFace alpha — the source-map slice.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(TileKnowledgeState.Inconnue,   0.0f)]
    [InlineData(TileKnowledgeState.Pressentie, 0.30f)]
    [InlineData(TileKnowledgeState.Esquissee,  0.60f)]
    [InlineData(TileKnowledgeState.Levee,      0.0f)]
    [InlineData(TileKnowledgeState.Scellee,    0.0f)]
    public void Resolve_carton_face_alpha_pins_design_fix_table(
        TileKnowledgeState state, float expected)
    {
        // CartonFace is the textured source-map slice. Hidden at
        // Inconnue (back is fully opaque so face would be invisible
        // anyway, and we save a draw call). Hidden at Levée+ (the
        // WorldMapSprite carries the visual). Surfaces progressively
        // at Pressentie 0.30 / Esquissée 0.60.
        Assert.Equal(expected, TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(state), precision: 3);
    }

    [Fact]
    public void Carton_face_alpha_is_strictly_increasing_from_inconnue_to_esquissee()
    {
        // Monotonicity invariant — the face must surface MORE as the
        // player advances from Inconnue to Esquissée. The opposite of
        // the back's monotonicity ; together they encode the "carton
        // hides → carte revealed" progression.
        var inc = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(TileKnowledgeState.Inconnue);
        var pre = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(TileKnowledgeState.Pressentie);
        var esq = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(TileKnowledgeState.Esquissee);

        Assert.True(inc < pre, $"Inconnue face alpha {inc} should be < Pressentie {pre}");
        Assert.True(pre < esq, $"Pressentie face alpha {pre} should be < Esquissée {esq}");
    }

    // -------------------------------------------------------------------
    // Joint invariants — the two resolvers have to behave coherently as a
    // pair, not just individually.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(TileKnowledgeState.Inconnue)]
    [InlineData(TileKnowledgeState.Pressentie)]
    [InlineData(TileKnowledgeState.Esquissee)]
    [InlineData(TileKnowledgeState.Levee)]
    [InlineData(TileKnowledgeState.Scellee)]
    public void Back_alpha_is_always_in_unit_interval(TileKnowledgeState state)
    {
        // No alpha can ever escape [0, 1]. Catches a future "set to 1.5
        // for emphasis" bug.
        var alpha = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state);
        Assert.InRange(alpha, 0f, 1f);
    }

    [Theory]
    [InlineData(TileKnowledgeState.Inconnue)]
    [InlineData(TileKnowledgeState.Pressentie)]
    [InlineData(TileKnowledgeState.Esquissee)]
    [InlineData(TileKnowledgeState.Levee)]
    [InlineData(TileKnowledgeState.Scellee)]
    public void Face_alpha_is_always_in_unit_interval(TileKnowledgeState state)
    {
        var alpha = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(state);
        Assert.InRange(alpha, 0f, 1f);
    }

    [Fact]
    public void Back_and_face_alphas_disagree_at_inconnue()
    {
        // Sanity — at Inconnue the back must be the visible one and the
        // face must be hidden. This is THE invariant the slice 3.6
        // hotfix violated (it had the source-map face fully opaque,
        // beige-tinted, with no separate back). If a future refactor
        // collapses the two resolvers, this test fails.
        var back = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(TileKnowledgeState.Inconnue);
        var face = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(TileKnowledgeState.Inconnue);

        Assert.Equal(1.0f, back, precision: 3);
        Assert.Equal(0.0f, face, precision: 3);
    }

    [Fact]
    public void Both_alphas_are_zero_at_levee_and_scellee()
    {
        // Sanity — at Levée and Scellée both polygons must be invisible
        // (the WorldMapSprite below carries the visual). A non-zero
        // alpha here would draw a redundant clipped slice over the
        // unclipped sprite, producing a faint seam at the diamond edge.
        foreach (var state in new[] { TileKnowledgeState.Levee, TileKnowledgeState.Scellee })
        {
            Assert.Equal(0.0f, TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state), precision: 3);
            Assert.Equal(0.0f, TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(state), precision: 3);
        }
    }

    [Fact]
    public void Every_state_has_a_visible_marker_either_carton_or_sceau()
    {
        // Existence invariant — every cell must produce SOME visual at
        // every state, either through the carton (back or face) or
        // through the sceau (Scellée only — the WorldMapSprite carries
        // Levée). Catches a regression where someone zeros all four
        // resolvers for a state and the cell becomes invisible.
        foreach (TileKnowledgeState state in System.Enum.GetValues(typeof(TileKnowledgeState)))
        {
            var back = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state);
            var face = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(state);
            var sceau = TileKnowledgeStateHelpers.ShouldRenderSceau(state);
            // Levée is intentionally invisible — the world map sprite
            // below shows. So we only check that the FogTileLayer-owned
            // visuals are present at every state EXCEPT Levée.
            if (state == TileKnowledgeState.Levee) continue;
            Assert.True(back > 0f || face > 0f || sceau,
                $"State {state} has no visible carton + no sceau — would render invisible.");
        }
    }

    [Fact]
    public void Visibility_short_circuits_match_alpha_strictly_positive()
    {
        // The renderer uses ShouldRenderCartonBack / ShouldRenderCartonFace
        // as cheap visibility short-circuits before issuing draw calls.
        // They must agree exactly with "alpha > 0", otherwise the runtime
        // may draw transparent polygons (cheap but wasteful) or skip
        // visible ones (bug).
        foreach (TileKnowledgeState state in System.Enum.GetValues(typeof(TileKnowledgeState)))
        {
            var backAlpha = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state);
            var faceAlpha = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(state);

            Assert.Equal(backAlpha > 0f, TileKnowledgeStateHelpers.ShouldRenderCartonBack(state));
            Assert.Equal(faceAlpha > 0f, TileKnowledgeStateHelpers.ShouldRenderCartonFace(state));
        }
    }

    [Fact]
    public void Resolve_carton_alpha_alias_matches_back_resolver()
    {
        // Back-compat — the old single-carton vocabulary is preserved as
        // an alias for the BACK resolver. If a future refactor makes
        // ResolveCartonAlpha point at face (or worse, at face + back
        // combined), the slice 2 xUnit suite breaks and so does this
        // explicit alias check.
        foreach (TileKnowledgeState state in System.Enum.GetValues(typeof(TileKnowledgeState)))
        {
            Assert.Equal(
                TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state),
                TileKnowledgeStateHelpers.ResolveCartonAlpha(state),
                precision: 3);
        }
    }

    [Fact]
    public void All_five_states_produce_distinct_visual_signatures()
    {
        // The "tous les niveaux observables visuellement" acceptance
        // criterion (slice 2 livrable 2, preserved at slice 3.6 design
        // fix). The triple (back alpha, face alpha, sceau visible) must
        // be unique per state — if any two states collapse into the
        // same signature, the F-key cycle starts looking like skipping.
        var signatures = new System.Collections.Generic.HashSet<(float, float, bool)>();
        foreach (TileKnowledgeState state in System.Enum.GetValues(typeof(TileKnowledgeState)))
        {
            var sig = (
                TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state),
                TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(state),
                TileKnowledgeStateHelpers.ShouldRenderSceau(state));
            Assert.True(signatures.Add(sig),
                $"State {state} has the same (back, face, sceau) signature as a prior state — visually indistinguishable.");
        }
        Assert.Equal(5, signatures.Count);
    }
}
