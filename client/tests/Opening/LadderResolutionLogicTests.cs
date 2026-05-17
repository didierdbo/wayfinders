using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="LadderResolutionLogic"/>. Pins the P8.1
/// ladder semantics so the contract surfaces here when post-MVP layers
/// (`bâtiment`, `tactique`, `méta`) eventually extend the ladder.
/// Godot-free by design — see <c>Wayfinders.Client.Tests.csproj</c> for
/// the `Compile Include` line.
///
/// <para>
/// <b>Ladder shape update (2026-05-17, Rune).</b> Following the scenes
/// cleanup pass, the <c>monde</c> rung (E1_WORLD) has been dropped from
/// the default ladder. <c>E1WorldMap</c> was dead code — the production
/// "Nouvelle Partie" path direct-loads <c>IsoMapE1Probe</c> via
/// <c>ChangeSceneToFile</c>, never registering <c>E1_WORLD</c>. The
/// ladder is now <c>cité / quartier</c> only ; PULL on <c>E2_AREA</c>
/// is a silent no-op (top of ladder). If <c>E1_WORLD</c> is ever
/// re-introduced as a real registered screen, restore the top rung
/// and the related tests below.
/// </para>
///
/// <para>
/// <b>Coverage strategy.</b>
/// <list type="bullet">
///   <item>Round-trip every rung in both directions (down and up).</item>
///   <item>Both extremes (top up, bottom down) return null = silent no-op.</item>
///   <item>Off-ladder screen ids (E1 title, E4 modal id, E1_WORLD legacy,
///         garbage) return null.</item>
/// </list>
/// </para>
/// </summary>
public sealed class LadderResolutionLogicTests
{
    [Fact]
    public void DefaultLadder_has_two_rungs_in_cadastral_order()
    {
        // Defense doc: lock the MVP ladder shape so a silent insertion
        // (e.g. a future "région/cité/quartier" attempt) breaks a test
        // rather than ships unannounced.
        var ladder = LadderResolutionLogic.DefaultLadder;

        Assert.Equal(2, ladder.Count);
        Assert.Equal("cité", ladder[0].LayerName);
        Assert.Equal("E2_AREA", ladder[0].ScreenId);
        Assert.Equal("quartier", ladder[1].LayerName);
        Assert.Equal("E3_DISTRICT", ladder[1].ScreenId);
    }

    [Fact]
    public void ResolveDownTarget_from_cite_returns_quartier()
    {
        var target = LadderResolutionLogic.ResolveDownTarget(
            "E2_AREA", LadderResolutionLogic.DefaultLadder);

        Assert.Equal("E3_DISTRICT", target);
    }

    [Fact]
    public void ResolveDownTarget_from_quartier_is_silent_noop()
    {
        // Bottom of the ladder -- no deeper layer in MVP (no `bâtiment`,
        // no `tactique`). Vision §P2 lock: those layers are post-MVP.
        var target = LadderResolutionLogic.ResolveDownTarget(
            "E3_DISTRICT", LadderResolutionLogic.DefaultLadder);

        Assert.Null(target);
    }

    [Fact]
    public void ResolveUpTarget_from_quartier_returns_cite()
    {
        var target = LadderResolutionLogic.ResolveUpTarget(
            "E3_DISTRICT", LadderResolutionLogic.DefaultLadder);

        Assert.Equal("E2_AREA", target);
    }

    [Fact]
    public void ResolveUpTarget_from_cite_is_silent_noop()
    {
        // Top of the ladder (post 2026-05-17 monde-rung removal) -- no
        // parent layer in MVP. The cité→monde climb used to navigate to
        // E1_WORLD ; now that E1_WORLD is unregistered, PULL on E2 is
        // a silent no-op. Vision §P2 reserves the `méta` layer for
        // "quitter le jeu", which Esc on E1Title already covers.
        var target = LadderResolutionLogic.ResolveUpTarget(
            "E2_AREA", LadderResolutionLogic.DefaultLadder);

        Assert.Null(target);
    }

    [Theory]
    [InlineData("E1_TITLE")]
    [InlineData("E1_WORLD")]   // legacy id, no longer in ladder (2026-05-17)
    [InlineData("E4_CHARACTER_SHEET")]
    [InlineData("E_DOES_NOT_EXIST")]
    [InlineData("")]
    public void Resolve_offladder_screen_returns_null_in_both_directions(string offLadder)
    {
        // E1 (title), E1_WORLD (legacy, since monde-rung removal) and
        // E4 (modal) are not part of the ladder by design -- the ladder
        // is the cadastral spine of E2/E3/E5 only. A wheel event landing
        // on any of them (or fired while E4 is open and the modal owns
        // input is bypassed) must produce a null target -- silent no-op.
        Assert.Null(LadderResolutionLogic.ResolveDownTarget(
            offLadder, LadderResolutionLogic.DefaultLadder));
        Assert.Null(LadderResolutionLogic.ResolveUpTarget(
            offLadder, LadderResolutionLogic.DefaultLadder));
    }

    [Fact]
    public void Resolve_with_null_current_returns_null_in_both_directions()
    {
        // SceneManager.CurrentScreenId is nullable (empty stack). Helper
        // must short-circuit cleanly rather than throw.
        Assert.Null(LadderResolutionLogic.ResolveDownTarget(
            null, LadderResolutionLogic.DefaultLadder));
        Assert.Null(LadderResolutionLogic.ResolveUpTarget(
            null, LadderResolutionLogic.DefaultLadder));
    }
}
