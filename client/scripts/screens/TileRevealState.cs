namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Three-state reveal substrate for the GameState-authoritative tile
/// visibility model, locked by Varn 2026-05-16 (reveal-layer reconciliation
/// §3.2). Orthogonal to <see cref="TileKnowledgeLadder"/> : the ladder
/// drives the player-facing Cadastre fog rendering (FogTileLayer), this
/// enum drives the face-B reveal substrate (continuous float [0,1] on the
/// R8 reveal_map, sampled by <c>tile_reveal.gdshader</c>).
///
/// <para>
/// <b>Why two parallel vocabularies (Varn-locked §2 D1).</b>
/// </para>
/// <list type="bullet">
///   <item>The 5-state ladder is the authoritative source-of-truth in
///         scenes where the fog tile layer renders (E1WorldMap,
///         E2AreaMap). It carries Cadastre-specific signals (Pressentie's
///         rumeur visual, Scellée's sceau, the Esquissée drill-zoom gate).</item>
///   <item>The 3-state reveal is the authoritative source-of-truth in
///         scenes that consume the face-B reveal subsystem (currently
///         IsoMapE1Probe cinematic ; future runtime gameplay events :
///         NPC recruitment partial reveal, mission <c>success_effect</c>
///         flipping tiles to <c>Revealed</c>, mission <c>reveal_on_fail</c>
///         going <c>Partial → Fog</c>).</item>
/// </list>
///
/// <para>
/// <b>Integer values are part of the persistence contract.</b> Do not
/// renumber. The order encodes the monotone visibility ladder so the
/// "le niveau permanent ne descend jamais" invariant (Varn §1.3 note
/// systémique) is expressible as a max() over states when needed.
/// </para>
///
/// <para>
/// <b>Render mapping (locked, see <c>TileRevealRenderController</c>).</b>
/// <c>Fog = 0.0</c>, <c>Partial = 0.55</c>, <c>Revealed = 1.0</c>. The
/// derived float is pushed to the R8 reveal_map via the controller's
/// 300 ms cubic-out Tween for gameplay transitions ; the cinematic uses
/// its own event-batched mechanism (Varn §2 D3) and does not go through
/// the controller.
/// </para>
/// </summary>
public enum TileRevealState
{
    /// <summary>
    /// The tile is not revealed at all. Default value for cells absent
    /// from the <c>GameState.TileRevealStates</c> dictionary (sparse).
    /// Derived float = 0.0 ; the face-B overlay alpha is fully zero,
    /// only the face-A neutral tile shows.
    /// </summary>
    Fog = 0,

    /// <summary>
    /// The tile is partially revealed (NPC recruit known_tiles ; mission
    /// reveal_on_fail keeping a partial silhouette). Derived float =
    /// 0.55 ; the face-B overlay alpha is at 55 %, the cluster-boundary
    /// CPU blur diffuses ~1 cell into neighbours.
    /// </summary>
    Partial = 1,

    /// <summary>
    /// The tile is fully revealed (player visited ; mission success ;
    /// cinematic flip terminal state). Derived float = 1.0 ; the face-B
    /// overlay alpha is fully opaque.
    /// </summary>
    Revealed = 2,
}

/// <summary>
/// Pure-C# helpers around <see cref="TileRevealState"/>. Godot-free so
/// xUnit pins the contract. Mirrors the
/// <see cref="TileKnowledgeLadderHelpers"/> seam.
///
/// <para>
/// <b>Two derived projections.</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="LadderToReveal"/> — the read-only one-way projection
///         from the 5-state Cadastre ladder to the 3-state reveal
///         substrate (Varn §2 D1). Used by anything that needs to ask
///         "given the player-facing knowledge state, what is the implied
///         reveal-substrate level ?". The reverse direction is NOT defined
///         (the projection is lossy : <c>Inconnue / Pressentie → Fog</c>
///         both, <c>Levée / Scellée → Revealed</c> both).</item>
///   <item><see cref="ResolveRevealLevel"/> — the locked float mapping
///         <c>{Fog: 0.0, Partial: 0.55, Revealed: 1.0}</c>. Used by
///         <c>TileRevealRenderController</c> as the Tween source / target
///         values, and by any downstream test that pins the contract.</item>
/// </list>
/// </summary>
public static class TileRevealStateHelpers
{
    // -------------------------------------------------------------
    // Derived float mapping (Varn §3.3 lock)
    // -------------------------------------------------------------

    /// <summary>Derived reveal_level at <see cref="TileRevealState.Fog"/>.</summary>
    public const float RevealLevelFog = 0.0f;

    /// <summary>
    /// Derived reveal_level at <see cref="TileRevealState.Partial"/>.
    /// 0.55, not 0.5, because the CPU pre-blur (3-pass box) at the
    /// cluster boundary attenuates centre values by ~5-10 %. Locking
    /// at 0.55 keeps the visual centre of a partial cluster at ~0.5
    /// after the blur — the visual target Varn locked.
    /// </summary>
    public const float RevealLevelPartial = 0.55f;

    /// <summary>Derived reveal_level at <see cref="TileRevealState.Revealed"/>.</summary>
    public const float RevealLevelRevealed = 1.0f;

    /// <summary>
    /// Resolve a <see cref="TileRevealState"/> to its locked derived
    /// float reveal_level. Used by <c>TileRevealRenderController</c> to
    /// pick Tween source / target values, and by any downstream test
    /// that pins the mapping.
    /// </summary>
    public static float ResolveRevealLevel(TileRevealState state) => state switch
    {
        TileRevealState.Fog      => RevealLevelFog,
        TileRevealState.Partial  => RevealLevelPartial,
        TileRevealState.Revealed => RevealLevelRevealed,
        _ => RevealLevelFog,
    };

    // -------------------------------------------------------------
    // Ladder -> Reveal projection (Varn §2 D1 lock, read-only one-way)
    // -------------------------------------------------------------

    /// <summary>
    /// Project a Cadastre 5-state ladder value into the 3-state reveal
    /// substrate. Varn-locked mapping (§2 D1) :
    /// <list type="bullet">
    ///   <item><c>Inconnue   → Fog</c></item>
    ///   <item><c>Pressentie → Fog</c>     (rumor stays diegetic, no alpha
    ///         contribution to the reveal substrate)</item>
    ///   <item><c>Esquissée  → Partial</c></item>
    ///   <item><c>Levée      → Revealed</c></item>
    ///   <item><c>Scellée    → Revealed</c> (sceau is independent)</item>
    /// </list>
    ///
    /// <para>
    /// <b>Lossy + one-way.</b> The reverse direction is NOT defined ;
    /// Fog maps back to {Inconnue, Pressentie} and Revealed maps back
    /// to {Levée, Scellée} both. Callers that need the reverse have
    /// usually mis-modelled the problem and should keep the two
    /// vocabularies independent (write to each authoritatively from
    /// their respective owners).
    /// </para>
    /// </summary>
    public static TileRevealState LadderToReveal(TileKnowledgeLadder ladder) => ladder switch
    {
        TileKnowledgeLadder.Inconnue   => TileRevealState.Fog,
        TileKnowledgeLadder.Pressentie => TileRevealState.Fog,
        TileKnowledgeLadder.Esquissee  => TileRevealState.Partial,
        TileKnowledgeLadder.Levee      => TileRevealState.Revealed,
        TileKnowledgeLadder.Scellee    => TileRevealState.Revealed,
        _ => TileRevealState.Fog,
    };
}
