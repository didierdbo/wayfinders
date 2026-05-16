using Wayfinders.Client.Scripts.Screens;
using Xunit;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for <see cref="OpeningBootstrapLogic.Decide"/>. The
/// runtime <c>OpeningBootstrap</c> autoload cannot be exercised from xUnit
/// (no Godot engine in the test harness, no scene tree, no
/// <c>GetTree().CurrentScene</c>), so we extract the decision into a
/// pure-C# seam and pin its truth table here.
///
/// <para>
/// <b>Why this matters.</b> The 2026-05-09 J4 double-spawn bug was a
/// silent regression : at boot, the FogTileLayer J4 canary printed twice
/// (1089 cells × 2 spawns) because OpeningBootstrap pushed E1 on top of
/// an F6-launched E1WorldMap, then the next "Nouvelle Partie" click
/// instantiated a SECOND E2 underneath. Pan visually froze, F debug
/// dispatched to the wrong knowledge store, every singleton-shaped piece
/// of state was doubled. The decision rule has THREE branches now (skip /
/// register-only / register-and-boot) and the tests below pin every cell
/// of the truth table -- a future refactor that loses the
/// <c>RegisterOnly</c> branch will fail
/// <see cref="RegisterOnly_when_main_scene_is_a_production_screen"/>
/// before it ships.
/// </para>
/// </summary>
public sealed class OpeningBootstrapLogicTests
{
    [Fact]
    public void SkipBootstrap_when_skip_meta_is_set()
    {
        // Probe scene path : Tile3DBackingProbe and friends carry the
        // skip_opening_bootstrap meta. The autoload must do nothing --
        // no registration, no NavigateTo. Pre-J4 single gate, kept.
        var action = OpeningBootstrapLogic.Decide(
            hasSkipMeta: true,
            isRegisteredScreen: false);

        Assert.Equal(OpeningBootstrapAction.SkipBootstrap, action);
    }

    [Fact]
    public void SkipBootstrap_takes_precedence_over_isRegisteredScreen()
    {
        // Defensive : a probe scene whose root happens to also implement
        // IScreen (e.g. a future probe inheriting from a screen base)
        // still wants the skip-everything path. The meta wins.
        var action = OpeningBootstrapLogic.Decide(
            hasSkipMeta: true,
            isRegisteredScreen: true);

        Assert.Equal(OpeningBootstrapAction.SkipBootstrap, action);
    }

    [Fact]
    public void RegisterOnly_when_main_scene_is_a_production_screen()
    {
        // F6 / "Run Current Scene" path : the active main scene is itself
        // a registered production screen (E1WorldMap, E2AreaMap, ...).
        // The autoload must register every screen and modal (so drill /
        // climb / POI dispatch keep working from the F6-launched screen)
        // but MUST NOT fire NavigateTo("E1_TITLE") -- that would push E1
        // on top and double-instantiate the screen on the next click.
        // This is the J4 double-spawn fix.
        var action = OpeningBootstrapLogic.Decide(
            hasSkipMeta: false,
            isRegisteredScreen: true);

        Assert.Equal(OpeningBootstrapAction.RegisterOnly, action);
    }

    [Fact]
    public void RegisterAndBoot_when_main_scene_is_neither()
    {
        // Canonical boot path : main_scene is a boot scene or a non-screen
        // root (RosterScene, BootScene, ...). The autoload registers the
        // screens then NavigateTo("E1_TITLE") -- pre-J4 default behaviour.
        var action = OpeningBootstrapLogic.Decide(
            hasSkipMeta: false,
            isRegisteredScreen: false);

        Assert.Equal(OpeningBootstrapAction.RegisterAndBoot, action);
    }
}
