using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="LadderResolutionLogic"/>. Pins the P8.1
/// ladder semantics (`monde / cité / quartier`) so the contract surfaces
/// here when post-MVP layers (`bâtiment`, `tactique`, `méta`) eventually
/// extend the ladder. Godot-free by design — see
/// <c>Wayfinders.Client.Tests.csproj</c> for the `Compile Include` line.
///
/// <para>
/// <b>Coverage strategy.</b>
/// <list type="bullet">
///   <item>Round-trip every rung in both directions (down and up).</item>
///   <item>Both extremes (top up, bottom down) return null = silent no-op.</item>
///   <item>Off-ladder screen ids (E1 title, E4 modal id, garbage) return null.</item>
/// </list>
/// </para>
/// </summary>
public sealed class LadderResolutionLogicTests
{
    [Fact]
    public void DefaultLadder_has_three_rungs_in_cadastral_order()
    {
        // Defense doc: lock the MVP ladder shape so a silent insertion
        // (e.g. a future "monde/région/cité/quartier" attempt) breaks
        // a test rather than ships unannounced.
        var ladder = LadderResolutionLogic.DefaultLadder;

        Assert.Equal(3, ladder.Count);
        Assert.Equal("monde", ladder[0].LayerName);
        Assert.Equal("E2_WORLD", ladder[0].ScreenId);
        Assert.Equal("cité", ladder[1].LayerName);
        Assert.Equal("E3_CITY_HALFGATE", ladder[1].ScreenId);
        Assert.Equal("quartier", ladder[2].LayerName);
        Assert.Equal("E5_DISTRICT", ladder[2].ScreenId);
    }

    [Fact]
    public void ResolveDownTarget_from_monde_returns_cite()
    {
        var target = LadderResolutionLogic.ResolveDownTarget(
            "E2_WORLD", LadderResolutionLogic.DefaultLadder);

        Assert.Equal("E3_CITY_HALFGATE", target);
    }

    [Fact]
    public void ResolveDownTarget_from_cite_returns_quartier()
    {
        var target = LadderResolutionLogic.ResolveDownTarget(
            "E3_CITY_HALFGATE", LadderResolutionLogic.DefaultLadder);

        Assert.Equal("E5_DISTRICT", target);
    }

    [Fact]
    public void ResolveDownTarget_from_quartier_is_silent_noop()
    {
        // Bottom of the ladder -- no deeper layer in MVP (no `bâtiment`,
        // no `tactique`). Vision §P2 lock: those layers are post-MVP.
        var target = LadderResolutionLogic.ResolveDownTarget(
            "E5_DISTRICT", LadderResolutionLogic.DefaultLadder);

        Assert.Null(target);
    }

    [Fact]
    public void ResolveUpTarget_from_quartier_returns_cite()
    {
        var target = LadderResolutionLogic.ResolveUpTarget(
            "E5_DISTRICT", LadderResolutionLogic.DefaultLadder);

        Assert.Equal("E3_CITY_HALFGATE", target);
    }

    [Fact]
    public void ResolveUpTarget_from_cite_returns_monde()
    {
        var target = LadderResolutionLogic.ResolveUpTarget(
            "E3_CITY_HALFGATE", LadderResolutionLogic.DefaultLadder);

        Assert.Equal("E2_WORLD", target);
    }

    [Fact]
    public void ResolveUpTarget_from_monde_is_silent_noop()
    {
        // Top of the ladder -- no parent layer in MVP. Vision §P2 reserves
        // the `méta` layer for "quitter le jeu", which Esc on E1 already
        // covers ; see D-P8.1-04 in the pre-brief.
        var target = LadderResolutionLogic.ResolveUpTarget(
            "E2_WORLD", LadderResolutionLogic.DefaultLadder);

        Assert.Null(target);
    }

    [Theory]
    [InlineData("E1_TITLE")]
    [InlineData("E4_CHARACTER_SHEET")]
    [InlineData("E_DOES_NOT_EXIST")]
    [InlineData("")]
    public void Resolve_offladder_screen_returns_null_in_both_directions(string offLadder)
    {
        // E1 (title) and E4 (modal) are not part of the ladder by design --
        // the ladder is the cadastral spine of E2/E3/E5 only. A wheel event
        // landing on E1 (or fired while E4 is open and the modal owns input
        // is bypassed) must produce a null target -- silent no-op.
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
