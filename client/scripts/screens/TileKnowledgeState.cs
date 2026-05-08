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
///
/// <para>
/// <b>Slice 3.6 design fix — two-polygon carton (back + face).</b>
/// Slice 3.6 hotfix <c>659b5e6</c> made the single Carton Polygon2D show
/// the source-map slice for every state, then modulated it by a beige
/// tint at full alpha. The result was a beige-tinted source map at
/// Inconnue, not an opaque beige carton. Symptom: the user perceived
/// Esquissée as "more opaque" than Inconnue because the beige tint
/// faded together with the alpha, exposing more of the source map
/// underneath. The progression "carton hides → carte revealed" was
/// visually inverted.
/// </para>
///
/// <para>
/// <b>Fix.</b> Split the carton into two stacked Polygon2D children per
/// cell:
/// <list type="bullet">
///   <item><b>CartonBack</b> — no texture, modulate beige opaque. Alpha
///         resolved by <see cref="ResolveCartonBackAlpha"/>. Acts as the
///         "dos de carte" that hides everything below it (face + world
///         map) when the cell is Inconnue.</item>
///   <item><b>CartonFace</b> — textured with the source map slice in
///         absolute source-map UV (the slice 3.6 hotfix UV math). Alpha
///         resolved by <see cref="ResolveCartonFaceAlpha"/>. Sits below
///         CartonBack in z-order so the back masks it when fully opaque,
///         and progressively exposes it as the player advances on the
///         knowledge ladder.</item>
/// </list>
/// </para>
/// </summary>
public static class TileKnowledgeStateHelpers
{
    // ---------------------------------------------------------------
    // CartonBack alpha table (the beige opaque "dos de carte")
    // ---------------------------------------------------------------

    /// <summary>
    /// CartonBack alpha at <see cref="TileKnowledgeState.Inconnue"/> —
    /// fully opaque (the master case, Varn §1.2 "le carton bouche tout").
    /// At this alpha the back fully masks both the face below it and the
    /// world map sprite further below.
    /// </summary>
    public const float CartonBackAlphaInconnue = 1.0f;

    /// <summary>
    /// CartonBack alpha at <see cref="TileKnowledgeState.Pressentie"/>.
    /// 70 % beige back + the face starts to peek through the residual 30 %.
    /// </summary>
    public const float CartonBackAlphaPressentie = 0.70f;

    /// <summary>
    /// CartonBack alpha at <see cref="TileKnowledgeState.Esquissee"/>.
    /// 40 % beige back ; the face dominates. The contrast Pressentie 0.7 vs
    /// Esquissée 0.4 is what carries the "drill becomes legal" signal.
    /// </summary>
    public const float CartonBackAlphaEsquissee = 0.40f;

    /// <summary>CartonBack alpha at <see cref="TileKnowledgeState.Levee"/> — invisible.</summary>
    public const float CartonBackAlphaLevee = 0.0f;

    /// <summary>CartonBack alpha at <see cref="TileKnowledgeState.Scellee"/> — invisible.</summary>
    public const float CartonBackAlphaScellee = 0.0f;

    // ---------------------------------------------------------------
    // CartonFace alpha table (the source-map slice)
    // ---------------------------------------------------------------

    /// <summary>
    /// CartonFace alpha at <see cref="TileKnowledgeState.Inconnue"/> —
    /// fully hidden. The back is opaque so the face is invisible anyway,
    /// but we set face alpha to 0 explicitly so the ordering is correct
    /// during transitions and so we don't pay for a draw call at this
    /// state (Polygon2D with alpha 0 still issues a draw — see
    /// <see cref="ShouldRenderCartonFace"/> for the visibility short-circuit).
    /// </summary>
    public const float CartonFaceAlphaInconnue = 0.0f;

    /// <summary>
    /// CartonFace alpha at <see cref="TileKnowledgeState.Pressentie"/>.
    /// 30 % visible face under a 70 % beige back ; the source-map slice
    /// starts to surface. This is the "rumeur, longue-vue" visual.
    /// </summary>
    public const float CartonFaceAlphaPressentie = 0.30f;

    /// <summary>
    /// CartonFace alpha at <see cref="TileKnowledgeState.Esquissee"/>.
    /// 60 % visible face under a 40 % beige back ; the slice is now
    /// the dominant signal. "Lisible mais pas révélé."
    /// </summary>
    public const float CartonFaceAlphaEsquissee = 0.60f;

    /// <summary>
    /// CartonFace alpha at <see cref="TileKnowledgeState.Levee"/> —
    /// invisible. At Levée the WorldMapSprite below shows the same pixels
    /// the face would show (UV in source coords); keeping the face hidden
    /// here avoids the duplicate draw and the seam between iso-clipped
    /// face and full world map sprite. Choice documented in M3/Phase 9
    /// slice 3.6 design fix brief.
    /// </summary>
    public const float CartonFaceAlphaLevee = 0.0f;

    /// <summary>CartonFace alpha at <see cref="TileKnowledgeState.Scellee"/> — same as Levée.</summary>
    public const float CartonFaceAlphaScellee = 0.0f;

    // ---------------------------------------------------------------
    // Back-compat aliases for the slice 2 single-carton vocabulary.
    // Kept for the slice 2 xUnit suite and any consumer that reads the
    // "is the carton blocking" signal without caring about the back/face
    // split. Resolved as the BACK alpha, since "the carton" semantically
    // is the opaque blocker, not the faded face.
    // ---------------------------------------------------------------

    /// <summary>Back-compat alias for <see cref="CartonBackAlphaInconnue"/>.</summary>
    public const float CartonAlphaInconnue = CartonBackAlphaInconnue;

    /// <summary>Back-compat alias for <see cref="CartonBackAlphaPressentie"/>.</summary>
    public const float CartonAlphaPressentie = CartonBackAlphaPressentie;

    /// <summary>Back-compat alias for <see cref="CartonBackAlphaEsquissee"/>.</summary>
    public const float CartonAlphaEsquissee = CartonBackAlphaEsquissee;

    /// <summary>Back-compat alias for <see cref="CartonBackAlphaLevee"/>.</summary>
    public const float CartonAlphaLevee = CartonBackAlphaLevee;

    /// <summary>Back-compat alias for <see cref="CartonBackAlphaScellee"/>.</summary>
    public const float CartonAlphaScellee = CartonBackAlphaScellee;

    /// <summary>
    /// Slice 3.6 design fix — CartonBack alpha resolver. Returns the
    /// steady-state alpha for the beige opaque dos de carte. The back
    /// is the visual blocker that hides both the face and the world map
    /// sprite below it when the cell is Inconnue, and progressively
    /// fades as the player advances on the knowledge ladder.
    /// </summary>
    public static float ResolveCartonBackAlpha(TileKnowledgeState state) => state switch
    {
        TileKnowledgeState.Inconnue   => CartonBackAlphaInconnue,
        TileKnowledgeState.Pressentie => CartonBackAlphaPressentie,
        TileKnowledgeState.Esquissee  => CartonBackAlphaEsquissee,
        TileKnowledgeState.Levee      => CartonBackAlphaLevee,
        TileKnowledgeState.Scellee    => CartonBackAlphaScellee,
        _ => CartonBackAlphaInconnue,
    };

    /// <summary>
    /// Slice 3.6 design fix — CartonFace alpha resolver. Returns the
    /// steady-state alpha for the source-map slice (textured face). The
    /// face is hidden at Inconnue (back is fully opaque so it'd be invisible
    /// anyway) and at Levée+ (the WorldMapSprite below carries the same
    /// pixels). It surfaces progressively at Pressentie 0.30 and
    /// Esquissée 0.60 so the player gets a real preview-of-the-map signal
    /// at the intermediate states.
    /// </summary>
    public static float ResolveCartonFaceAlpha(TileKnowledgeState state) => state switch
    {
        TileKnowledgeState.Inconnue   => CartonFaceAlphaInconnue,
        TileKnowledgeState.Pressentie => CartonFaceAlphaPressentie,
        TileKnowledgeState.Esquissee  => CartonFaceAlphaEsquissee,
        TileKnowledgeState.Levee      => CartonFaceAlphaLevee,
        TileKnowledgeState.Scellee    => CartonFaceAlphaScellee,
        _ => CartonFaceAlphaInconnue,
    };

    /// <summary>
    /// Back-compat alias for <see cref="ResolveCartonBackAlpha"/>. Slice 2
    /// callers that asked "what's the carton alpha at this state ?" get
    /// the BACK alpha (the opaque blocker), since that's what the
    /// "carton" originally meant before the slice 3.6 design fix split it
    /// into back + face. The slice 2 xUnit suite pins these values
    /// (1.0 / 0.7 / 0.4 / 0.0 / 0.0).
    /// </summary>
    public static float ResolveCartonAlpha(TileKnowledgeState state) =>
        ResolveCartonBackAlpha(state);

    /// <summary>
    /// Slice 3.6 design fix — visibility short-circuit for CartonFace. True
    /// iff the face contributes any visible pixels at this state. Used by
    /// the renderer to skip the per-frame draw call when alpha would be 0.
    /// Equivalent to <c>ResolveCartonFaceAlpha(state) &gt; 0f</c>, named
    /// for the renderer's intent.
    /// </summary>
    public static bool ShouldRenderCartonFace(TileKnowledgeState state) =>
        ResolveCartonFaceAlpha(state) > 0f;

    /// <summary>
    /// Slice 3.6 design fix — visibility short-circuit for CartonBack.
    /// Same idiom as <see cref="ShouldRenderCartonFace"/>.
    /// </summary>
    public static bool ShouldRenderCartonBack(TileKnowledgeState state) =>
        ResolveCartonBackAlpha(state) > 0f;

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
    /// be visible behind the carton for this cell.
    ///
    /// <para>
    /// <b>Slice 3.6 design fix.</b> With the new CartonFace polygon
    /// showing the actual source-map slice at Pressentie and Esquissée,
    /// the PaletteSwatch's diegetic role ("hint of palette through the
    /// translucent carton") is now fulfilled by the face itself. The
    /// swatch is therefore retired by default — the renderer keeps the
    /// node in the scene for back-compat with any consumer that still
    /// reads it, but always sets <c>Visible = false</c>. This predicate
    /// returns false for every state to lock that behaviour at the
    /// helper layer ; if a future slice wants the swatch back (e.g. for
    /// a stylised parchemin overlay), it can flip this in one place.
    /// </para>
    ///
    /// <para>
    /// <b>Slice 2 callers.</b> The slice 2 xUnit suite pinned the
    /// previous table (true at Pressentie + Esquissée, false elsewhere).
    /// That suite is updated as part of the slice 3.6 design fix to
    /// reflect the retirement of the swatch ; the slice 2 visual contract
    /// is superseded by slice 3.6's two-polygon carton.
    /// </para>
    /// </summary>
    public static bool ShouldRenderPaletteSwatch(TileKnowledgeState state) => false;

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
