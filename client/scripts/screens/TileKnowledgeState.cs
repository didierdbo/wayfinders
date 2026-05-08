namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Five-level knowledge spectrum for a single cartographic tile, locked
/// by Varn 2026-05-08 (`Tiles as Cartography and Fog`, §1.1, D-TILE-01 +
/// D-TILE-02). Replaces a binary "fog / no-fog" predicate with a
/// participe-passé-féminin ladder that matches the Cadastre voice
/// (see UI Labels §7.5 vocabulaires fermés).
///
/// <para>
/// <b>Slice 1 (M3 / L1 World fondations) — MVP carve-out.</b> The slice
/// implements only the two terminal states explicitly: <see cref="Inconnue"/>
/// renders a fully-opaque carton placeholder, <see cref="Levee"/> hides the
/// fog tile (alpha 0). The three intermediate states (<see cref="Pressentie"/>,
/// <see cref="Esquissee"/>, <see cref="Scellee"/>) exist in the enum so
/// the persistence schema is stable from day one, but their visual
/// distinction (palette quantifiée 3 teintes, silhouette sépia, sceau)
/// lands in slice 2 once the baking script (livrable 4) feeds runtime
/// consumption. Until then, anything not <see cref="Levee"/> is treated
/// as <see cref="Inconnue"/> for rendering purposes — cf. <see cref="IsHidden"/>.
/// </para>
///
/// <para>
/// <b>Integer values are part of the persistence contract.</b> Do not
/// renumber. The order encodes the ladder direction: a future
/// <c>level_a &gt;= level_b</c> comparison must remain monotonic so the
/// "le niveau permanent ne descend jamais" invariant (Varn §1.3 note
/// systémique) is expressible as a max() over states.
/// </para>
/// </summary>
public enum TileKnowledgeState
{
    /// <summary>
    /// Niveau 0. La zone existe peut-être. Le Cadastre n'a aucune entrée.
    /// Rendu MVP : carton beige-écru opaque (placeholder slice 1).
    /// </summary>
    Inconnue = 0,

    /// <summary>
    /// Niveau 1. On sait qu'il y a quelque chose là-bas (rumeur, longue-vue),
    /// aucun détail. Rendu cible : carton translucide ~70 % laissant
    /// remonter la couleur quantifiée. Rendu MVP slice 1 : traité comme
    /// <see cref="Inconnue"/> (opaque) en attendant le baking palette.
    /// </summary>
    Pressentie = 1,

    /// <summary>
    /// Niveau 2. Silhouette grossière tracée, seuil minimal du drill
    /// zoom-driven (Varn §1.4, ratifié D-TILE-03). Rendu cible : carton
    /// translucide ~50 % + silhouette à l'encre sépia. Rendu MVP slice 1 :
    /// traité comme <see cref="Inconnue"/>.
    /// </summary>
    Esquissee = 2,

    /// <summary>
    /// Niveau 3. La tuile a été arpentée. Carton retiré, sol iso visible.
    /// État "carte usuelle". Rendu MVP slice 1 : fog tile invisible
    /// (alpha 0) ; le geste-clé pliure-soulèvement Varn §4.5 arrive en
    /// slice 2.
    /// </summary>
    Levee = 3,

    /// <summary>
    /// Niveau 4. Tuile certifiée par un acte cadastral. Visuellement
    /// identique à <see cref="Levee"/> (sceau Cadastre discret, post-MVP)
    /// — le scellement ajoute une autorité administrative diégétique,
    /// pas de l'information.
    /// </summary>
    Scellee = 4,
}

/// <summary>
/// Pure-C# helpers around <see cref="TileKnowledgeState"/>. Godot-free so
/// xUnit pins the contract. Mirrors the
/// <see cref="LadderResolutionLogic"/> / <see cref="CameraPanLogic"/> /
/// <see cref="MapPanInputLogic"/> seam pattern: small static surface,
/// every consumer (the runtime store, the fog renderer, the future
/// save/load) calls these helpers instead of recoding the rules.
/// </summary>
public static class TileKnowledgeStateHelpers
{
    /// <summary>
    /// True iff the fog tile should be rendered as fully opaque carton
    /// for this knowledge state. MVP slice 1 contract: only
    /// <see cref="TileKnowledgeState.Levee"/> and
    /// <see cref="TileKnowledgeState.Scellee"/> hide the fog tile —
    /// every other state shows the placeholder carton.
    ///
    /// <para>
    /// Slice 2 will introduce graduated alpha + silhouette overlay for
    /// <see cref="TileKnowledgeState.Pressentie"/> /
    /// <see cref="TileKnowledgeState.Esquissee"/>, at which point this
    /// helper switches from a binary opaque/hidden to a per-state alpha
    /// resolver (or splits into <c>ResolveAlpha</c> +
    /// <c>ShowSilhouette</c>). The signature change will surface every
    /// consumer at compile time — that is the seam working as intended.
    /// </para>
    /// </summary>
    /// <param name="state">The current knowledge state for the tile.</param>
    /// <returns>True if the fog tile should be opaque, false if hidden.</returns>
    public static bool ShouldRenderOpaque(TileKnowledgeState state) =>
        state != TileKnowledgeState.Levee && state != TileKnowledgeState.Scellee;

    /// <summary>
    /// Inverse of <see cref="ShouldRenderOpaque"/>, named for the
    /// renderer's intent ("hide this fog tile"). Kept as a separate
    /// helper rather than a !-call-site so the call site reads as
    /// intent ("if hidden, set Visible=false") not negated state.
    /// </summary>
    public static bool IsHidden(TileKnowledgeState state) =>
        !ShouldRenderOpaque(state);

    /// <summary>
    /// Toggle helper for the slice 1 debug command (livrable 3 exit
    /// criterion). Cycles between <see cref="TileKnowledgeState.Inconnue"/>
    /// and <see cref="TileKnowledgeState.Levee"/> — the only two states
    /// the slice actually distinguishes visually. Anything else is
    /// canonicalised to <see cref="TileKnowledgeState.Inconnue"/> on
    /// toggle so the debug surface is fully closed-loop in MVP.
    /// </summary>
    public static TileKnowledgeState Toggle(TileKnowledgeState current) =>
        current == TileKnowledgeState.Levee
            ? TileKnowledgeState.Inconnue
            : TileKnowledgeState.Levee;
}
