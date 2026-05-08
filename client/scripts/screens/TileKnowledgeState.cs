namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Five-level knowledge spectrum for a single cartographic tile, locked
/// by Varn 2026-05-08 (`Tiles as Cartography and Fog`, §1.1, D-TILE-01 +
/// D-TILE-02). Replaces a binary "fog / no-fog" predicate with a
/// participe-passé-féminin ladder that matches the Cadastre voice
/// (see UI Labels §7.5 vocabulaires fermés).
///
/// <para>
/// <b>Slice 1 (M3 / L1 World fondations) — MVP carve-out (now superseded).</b>
/// Slice 1 only distinguished <see cref="Inconnue"/> vs <see cref="Levee"/>.
/// Slice 2 (this file's current state) introduces the three intermediate
/// renderings (carton translucent + palette quantifiée 3 teintes apparente
/// pour <see cref="Pressentie"/> / <see cref="Esquissee"/> + sceau placeholder
/// pour <see cref="Scellee"/>) — see <see cref="ResolveCartonAlpha"/>.
/// The terminal-states-only contract that pinned slice 1 is now relaxed ;
/// every state has its own visual.
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
    /// Rendu : carton beige-écru opaque.
    /// </summary>
    Inconnue = 0,

    /// <summary>
    /// Niveau 1. On sait qu'il y a quelque chose là-bas (rumeur, longue-vue),
    /// aucun détail. Rendu : carton translucide ~70 % laissant
    /// remonter la couleur quantifiée 3 teintes.
    /// </summary>
    Pressentie = 1,

    /// <summary>
    /// Niveau 2. Silhouette grossière tracée, seuil minimal du drill
    /// zoom-driven (Varn §1.4, ratifié D-TILE-03). Rendu : carton
    /// translucide ~40 %, palette plus visible. Pas encore le rendu
    /// silhouette sépia (slice 3+) mais lisible comme "lisible mais
    /// pas révélé".
    /// </summary>
    Esquissee = 2,

    /// <summary>
    /// Niveau 3. La tuile a été arpentée. Carton retiré, sol iso visible.
    /// État "carte usuelle". Geste-clé pliure-soulèvement Varn §4.5
    /// joué à la transition <c>Esquissee → Levee</c> (slice 2).
    /// </summary>
    Levee = 3,

    /// <summary>
    /// Niveau 4. Tuile certifiée par un acte cadastral. Visuellement
    /// identique à <see cref="Levee"/> côté carton (absent), plus un
    /// sceau de cire placeholder (cercle modulé rouge cire) au centre
    /// de la cellule. Asset Mira final post-moodboard.
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
    /// Carton alpha at <see cref="TileKnowledgeState.Inconnue"/> — fully
    /// opaque (the master case, Varn §1.2 "le carton bouche tout").
    /// </summary>
    public const float CartonAlphaInconnue = 1.0f;

    /// <summary>
    /// Carton alpha at <see cref="TileKnowledgeState.Pressentie"/>. Varn
    /// §1.2 "carton translucide ~70 %" → 70 % visible carton means alpha
    /// 0.70 on top of the palette swatch. The 3 teintes show through.
    /// </summary>
    public const float CartonAlphaPressentie = 0.70f;

    /// <summary>
    /// Carton alpha at <see cref="TileKnowledgeState.Esquissee"/>. Varn
    /// §1.2 "carton translucide ~50 %", brief slice 2 calibre à ~0.4
    /// pour que la palette soit nettement plus lisible qu'à Pressentie
    /// — l'opposition lisible/non-lisible est la vraie information à ce
    /// niveau (le drill devient autorisé). Silhouette sépia (Varn §3.4
    /// 1.2 s plume) reportée slice 3+.
    /// </summary>
    public const float CartonAlphaEsquissee = 0.40f;

    /// <summary>
    /// Carton alpha at <see cref="TileKnowledgeState.Levee"/> — invisible.
    /// The pliure-soulèvement animation (Varn §4.5, brief slice 2 livrable
    /// 3) carries the transition ; the steady-state value is 0.
    /// </summary>
    public const float CartonAlphaLevee = 0.0f;

    /// <summary>
    /// Carton alpha at <see cref="TileKnowledgeState.Scellee"/> — same as
    /// Levée (the scellement adds a sceau on the iso ground, it does not
    /// alter the carton state ; the carton is already gone by the time we
    /// reach Scellée).
    /// </summary>
    public const float CartonAlphaScellee = 0.0f;

    /// <summary>
    /// Slice 2 alpha resolver — replaces the slice 1 binary
    /// <see cref="ShouldRenderOpaque"/>. Returns the steady-state carton
    /// alpha for a given knowledge state. The renderer uses this both
    /// for the initial spawn and for crossfade tween targets ; the
    /// pliure-soulèvement at <c>Esquissee → Levee</c> tweens to 0 over
    /// 800 ms while the rotation/translation play in parallel.
    /// </summary>
    public static float ResolveCartonAlpha(TileKnowledgeState state) => state switch
    {
        TileKnowledgeState.Inconnue   => CartonAlphaInconnue,
        TileKnowledgeState.Pressentie => CartonAlphaPressentie,
        TileKnowledgeState.Esquissee  => CartonAlphaEsquissee,
        TileKnowledgeState.Levee      => CartonAlphaLevee,
        TileKnowledgeState.Scellee    => CartonAlphaScellee,
        _ => CartonAlphaInconnue,
    };

    /// <summary>
    /// True iff a sceau de cire placeholder should be rendered over this
    /// cell. Slice 2 contract : only <see cref="TileKnowledgeState.Scellee"/>
    /// shows the sceau ; <see cref="TileKnowledgeState.Levee"/> and below
    /// hide it. Pinned by xUnit so a future "everything below Scellée
    /// hides the sceau" refactor stays explicit.
    /// </summary>
    public static bool ShouldRenderSceau(TileKnowledgeState state) =>
        state == TileKnowledgeState.Scellee;

    /// <summary>
    /// True iff the palette swatch (3 teintes quantified colors) should
    /// be visible behind the carton for this cell. At <see cref="TileKnowledgeState.Inconnue"/>
    /// the carton is fully opaque so the swatch is hidden — we
    /// short-circuit the swatch's own Visible flag rather than relying
    /// on the carton's alpha to occlude it (cheaper, cleaner, no risk
    /// of a partial-alpha edge bleed).
    /// </summary>
    public static bool ShouldRenderPaletteSwatch(TileKnowledgeState state) =>
        state == TileKnowledgeState.Pressentie || state == TileKnowledgeState.Esquissee;

    /// <summary>
    /// Slice 1 binary contract, retained for back-compat with the slice 1
    /// xUnit suite. Returns true for any state whose carton alpha is
    /// strictly greater than zero — i.e. the carton is at least partially
    /// visible. Slice 2 callers prefer
    /// <see cref="ResolveCartonAlpha"/> directly.
    /// </summary>
    public static bool ShouldRenderOpaque(TileKnowledgeState state) =>
        ResolveCartonAlpha(state) > 0f;

    /// <summary>
    /// Inverse of <see cref="ShouldRenderOpaque"/>, named for the
    /// renderer's intent ("hide this fog tile"). The slice 1 callers
    /// keep using this ; slice 2 callers also have access to
    /// <see cref="ResolveCartonAlpha"/> for graduated alpha.
    /// </summary>
    public static bool IsHidden(TileKnowledgeState state) =>
        !ShouldRenderOpaque(state);

    /// <summary>
    /// Slice 1 toggle helper, retained for back-compat. Cycles between
    /// <see cref="TileKnowledgeState.Inconnue"/> and
    /// <see cref="TileKnowledgeState.Levee"/>. Slice 2 debug surface
    /// uses <see cref="CycleNext"/> / <see cref="CyclePrevious"/> instead
    /// to walk the full ladder.
    /// </summary>
    public static TileKnowledgeState Toggle(TileKnowledgeState current) =>
        current == TileKnowledgeState.Levee
            ? TileKnowledgeState.Inconnue
            : TileKnowledgeState.Levee;

    /// <summary>
    /// Cycle one step forward through the ladder, wrapping
    /// <c>Scellee → Inconnue</c>. Slice 2 livrable 4 : F key on the
    /// cell under the cursor walks Inconnue → Pressentie → Esquissée →
    /// Levée → Scellée → Inconnue, so every state's rendering can be
    /// observed without restarting the game (slice 2 livrable 2
    /// acceptance criterion).
    /// </summary>
    public static TileKnowledgeState CycleNext(TileKnowledgeState current) => current switch
    {
        TileKnowledgeState.Inconnue   => TileKnowledgeState.Pressentie,
        TileKnowledgeState.Pressentie => TileKnowledgeState.Esquissee,
        TileKnowledgeState.Esquissee  => TileKnowledgeState.Levee,
        TileKnowledgeState.Levee      => TileKnowledgeState.Scellee,
        TileKnowledgeState.Scellee    => TileKnowledgeState.Inconnue,
        _ => TileKnowledgeState.Inconnue,
    };

    /// <summary>
    /// Cycle one step backward through the ladder, wrapping
    /// <c>Inconnue → Scellee</c>. Slice 2 livrable 4 : Shift+F goes the
    /// other direction, useful when the playtester overshoots and wants
    /// to step back one rung.
    /// </summary>
    public static TileKnowledgeState CyclePrevious(TileKnowledgeState current) => current switch
    {
        TileKnowledgeState.Inconnue   => TileKnowledgeState.Scellee,
        TileKnowledgeState.Pressentie => TileKnowledgeState.Inconnue,
        TileKnowledgeState.Esquissee  => TileKnowledgeState.Pressentie,
        TileKnowledgeState.Levee      => TileKnowledgeState.Esquissee,
        TileKnowledgeState.Scellee    => TileKnowledgeState.Levee,
        _ => TileKnowledgeState.Inconnue,
    };
}
