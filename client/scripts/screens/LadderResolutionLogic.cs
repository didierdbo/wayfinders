using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# decision helper for the P8.1 layer ladder navigation
/// (`monde / cité / quartier`). Maps a (currentScreenId, ladder) pair
/// to the target screen id when the player triggers molette up / molette
/// down, or to <c>null</c> when the move is a silent no-op (top of ladder
/// for up, bottom of ladder for down, or current screen not part of the
/// ladder at all -- e.g. E1, E4 modal host).
///
/// <para>
/// <b>Why a separate helper, not a method on SceneManager.</b> Same
/// pattern as <see cref="ContinueGatingLogic"/> at J2 and
/// <see cref="PoiDispatchLogic"/> at J3/J4: the rule is a small switch
/// but it is also <i>contractual</i>. Encoding it once in a Godot-free
/// class lets xUnit pin the ladder semantics. When the ladder eventually
/// extends post-MVP (layer `bâtiment` / `tactique` / `méta` -- vision §P2),
/// the test suite will surface every consumer that depended on the
/// strict 3-layer MVP shape.
/// </para>
///
/// <para>
/// <b>Stack/ladder alignment invariant (Pre-brief Risk #1).</b>
/// In MVP, every navigation between E2 / E3 / E5 either passes through
/// the ladder helpers or through the existing POI-click path, both of
/// which preserve the invariant `stack order == ladder order` on the
/// active spine (E2 below, then E3, then E5 on top). Molette UP in
/// SceneManager delegates to <c>NavigateBack</c> which simply pops -- so
/// it relies on the spine being ladder-aligned. Defense doc: see
/// <c>LadderInputContractTests</c>.
/// </para>
///
/// <para>
/// <b>Why no `LadderResolutionResult` record.</b> The signal we need is
/// a single nullable <c>string?</c> (target screen id or no-op). A record
/// would just wrap the same value -- callers branch on null or string,
/// not on a richer outcome enum. If post-MVP gains a third "no-op but
/// surface a hint" case (e.g. molette UP on E2 opens the Quitter modal),
/// we promote to a record then.
/// </para>
/// </summary>
public static class LadderResolutionLogic
{
    /// <summary>
    /// Default MVP ladder: <c>monde / cité / quartier</c>. Aligns with
    /// Varn UI Labels v1 §7.5 vocabulary lock and the Halfgate-only
    /// 1-cité / 1-quartier MVP shape (D6.10 + D-J5-09).
    /// </summary>
    /// <remarks>
    /// Order is bottom-up: index 0 is the top of the ladder (largest
    /// scope = world map) ; the last index is the deepest layer (most
    /// granular = quartier). Molette DOWN walks index N → N+1, molette
    /// UP walks index N → N-1. The directional vocabulary mirrors how
    /// the player thinks ("descendre dans la ville", "remonter au monde")
    /// rather than the array index.
    /// </remarks>
    public static readonly IReadOnlyList<LayerRung> DefaultLadder = new[]
    {
        new LayerRung("monde",    "E2_WORLD"),
        new LayerRung("cité",     "E3_CITY_HALFGATE"),
        new LayerRung("quartier", "E5_DISTRICT"),
    };

    /// <summary>
    /// Resolve the target screen id when the player triggers molette
    /// DOWN (drill into a more granular layer). Returns <c>null</c> when
    /// the move is a silent no-op:
    /// <list type="bullet">
    ///   <item>The current screen is the bottom rung (e.g. quartier).</item>
    ///   <item>The current screen is not part of the ladder at all
    ///         (e.g. E1 title, an unregistered screen).</item>
    /// </list>
    /// </summary>
    public static string? ResolveDownTarget(
        string? currentScreenId,
        IReadOnlyList<LayerRung> ladder)
    {
        if (currentScreenId is null) return null;

        var index = IndexOfRung(currentScreenId, ladder);
        if (index < 0) return null;                     // not in ladder
        if (index >= ladder.Count - 1) return null;     // already at bottom
        return ladder[index + 1].ScreenId;
    }

    /// <summary>
    /// Resolve the target screen id when the player triggers molette
    /// UP (climb to a less granular layer). Returns <c>null</c> when
    /// the move is a silent no-op:
    /// <list type="bullet">
    ///   <item>The current screen is the top rung (e.g. monde).</item>
    ///   <item>The current screen is not part of the ladder at all.</item>
    /// </list>
    /// </summary>
    public static string? ResolveUpTarget(
        string? currentScreenId,
        IReadOnlyList<LayerRung> ladder)
    {
        if (currentScreenId is null) return null;

        var index = IndexOfRung(currentScreenId, ladder);
        if (index <= 0) return null;                    // not in ladder, or already at top
        return ladder[index - 1].ScreenId;
    }

    private static int IndexOfRung(string screenId, IReadOnlyList<LayerRung> ladder)
    {
        for (var i = 0; i < ladder.Count; i++)
        {
            if (ladder[i].ScreenId == screenId) return i;
        }
        return -1;
    }
}

/// <summary>
/// One rung of the layer ladder. <see cref="LayerName"/> is the
/// player-facing cadastral label (Varn §7.5 vocabulary, in French) ;
/// <see cref="ScreenId"/> is the SceneManager registration key. The
/// pair is recorded together so the ladder definition reads like a
/// table mapping "which layer name belongs with which screen".
/// </summary>
public sealed record LayerRung(string LayerName, string ScreenId);
