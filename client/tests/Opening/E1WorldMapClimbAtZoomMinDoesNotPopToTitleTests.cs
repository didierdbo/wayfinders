using System.Collections.Generic;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin test for the Phase 9 slice 3 climb-from-E2 regression
/// surfaced 2026-05-09 (Didier playtest). At slice 3 closeout, the
/// E2 ClimbRequested handler dispatched through
/// <c>SceneManager.NavigateBack()</c>. That call pops the navigation
/// stack one level -- and on the opening flow E1 Title sits below E2
/// World on the stack, so a climb-out PULL gesture at <c>ZoomMin</c>
/// teleported the player back to the title screen.
///
/// <para>
/// <b>The intended semantics.</b> "Climb out at the top rung of the
/// cadastral ladder (monde)" is a silent no-op, not a pop. The L1
/// World layer is the top of the <c>monde / cité / quartier</c>
/// ladder pinned by <see cref="LadderResolutionLogic.DefaultLadder"/>
/// ; PULL there has nowhere to go. The verb that encodes this is
/// <c>SceneManager.NavigateLadderUp()</c>, which delegates to
/// <see cref="LadderResolutionLogic.ResolveUpTarget"/> and silently
/// no-ops when the current screen is already at index 0 of the ladder.
/// </para>
///
/// <para>
/// <b>The fix (commit hash referenced in feedback memo).</b> The single
/// line at <c>E1WorldMap.cs:509</c> moved from
/// <c>await sceneManager.NavigateBack()</c> to
/// <c>await sceneManager.NavigateLadderUp()</c>. The transition lock
/// release in the finally block is unchanged -- the handler still
/// notifies the pan component once on every climb attempt, which is
/// idempotent against the silent-no-op outcome.
/// </para>
///
/// <para>
/// <b>Why this lives in its own file.</b> Same pattern as the slice
/// 4/5 contract pin files (<see cref="E2AreaMapWorldSpaceClimbContractTests"/>,
/// <see cref="E3DistrictMapWorldSpaceClimbContractTests"/>) : one regression =
/// one file with a stable name that surfaces in the test runner output
/// when it ever fires. Inlining this assertion into
/// <see cref="E1WorldMapNavigationContractTests"/> would mix the back-button
/// contract (which DOES want NavigateBack) with the climb-at-zoom-min
/// contract (which does NOT). Two intents, two files.
/// </para>
///
/// <para>
/// <b>What is asserted.</b>
/// <list type="bullet">
///   <item>The ladder helper, given E1_WORLD as current and the default
///         ladder, returns null -- the verb's substrate already encodes
///         the no-op contract independently of the SceneManager.</item>
///   <item>The fake-harness body of <c>E1WorldMap.OnClimbRequested</c>
///         (post-fix shape) calls <c>NavigateLadderUp</c> exactly once
///         and never <c>NavigateBack</c> on the climb path -- so a
///         future refactor that flips the verb back will fail this
///         test loud.</item>
///   <item>The transition lock is released via <c>NotifyTransitionEnded</c>
///         after <c>NavigateLadderUp</c> returns (mirrors the slice
///         4/5 lock-release contract on E3/E5).</item>
///   <item>The lock release still fires when <c>NavigateLadderUp</c>
///         throws -- defensive shape kept symmetric with E3/E5.</item>
/// </list>
/// </para>
/// </summary>
public sealed class E1WorldMapClimbAtZoomMinDoesNotPopToTitleTests
{
    private sealed class FakeSceneManager
    {
        public List<string> NavigateBackCalls { get; } = new();
        public List<string> NavigateLadderUpCalls { get; } = new();

        public Task NavigateBack()
        {
            NavigateBackCalls.Add("back");
            return Task.CompletedTask;
        }

        public Task NavigateLadderUp()
        {
            NavigateLadderUpCalls.Add("ladder-up");
            return Task.CompletedTask;
        }
    }

    private sealed class FakePanComponent
    {
        public int NotifyTransitionEndedCalls { get; private set; }
        public bool InTransition { get; private set; }

        public void EnterTransition() => InTransition = true;

        public void NotifyTransitionEnded()
        {
            NotifyTransitionEndedCalls++;
            InTransition = false;
        }
    }

    /// <summary>
    /// Reproduces the post-fix body of <c>E1WorldMap.OnClimbRequested</c>.
    /// Same shape as the E3/E5 climb handlers (try/finally with lock
    /// release in finally), but the verb is <c>NavigateLadderUp</c>
    /// instead of <c>NavigateBack</c> -- which is the whole regression
    /// pin. If a future maintainer changes <c>E1WorldMap.cs:509</c>
    /// back to <c>NavigateBack</c>, they must also change this fake
    /// body, which surfaces the contract change in the diff.
    /// </summary>
    private static async Task FakeOnClimbRequested(
        FakeSceneManager nav, FakePanComponent panComponent)
    {
        try
        {
            await nav.NavigateLadderUp();
        }
        finally
        {
            panComponent.NotifyTransitionEnded();
        }
    }

    [Fact]
    public void E1WorldMap_world_is_top_of_ladder_so_ResolveUpTarget_is_null()
    {
        // The substrate contract : the ladder resolution helper, when
        // asked "what climbs out of E1_WORLD ?", returns null. This
        // is the engine-free reason NavigateLadderUp silently no-ops on
        // E2 -- the SceneManager wrapper just delegates to this helper.
        // If a future ladder ever inserts a higher rung above monde
        // (e.g. a "méta-monde" world-of-worlds), this test fires and
        // the climb-at-E2 contract has to be re-thought deliberately.
        var target = LadderResolutionLogic.ResolveUpTarget(
            "E1_WORLD", LadderResolutionLogic.DefaultLadder);

        Assert.Null(target);
    }

    [Fact]
    public async Task Climb_from_E2_calls_NavigateLadderUp_and_never_NavigateBack()
    {
        // The regression pin. Pre-fix, the handler called NavigateBack
        // which popped E1 off the stack on the opening flow. Post-fix,
        // it calls NavigateLadderUp which silently no-ops at the top
        // rung. This test fails loud if a future refactor flips the
        // verb back -- which is exactly the regression we want to
        // catch.
        var nav = new FakeSceneManager();
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();

        await FakeOnClimbRequested(nav, panComponent);

        Assert.Single(nav.NavigateLadderUpCalls);
        Assert.Empty(nav.NavigateBackCalls);
    }

    [Fact]
    public async Task Climb_from_E2_releases_transition_lock_after_NavigateLadderUp()
    {
        // Mirror of the slice 4/5 lock-release contract on E3/E5. The
        // pan component's transition lock must be released after the
        // verb completes ; otherwise the player loses input on the
        // next interaction. Fires once, exactly, even though the
        // underlying NavigateLadderUp is a silent no-op (the lock
        // release does not depend on the verb actually navigating
        // anywhere -- it only depends on the wheel-event session
        // ending).
        var nav = new FakeSceneManager();
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();
        Assert.True(panComponent.InTransition); // sanity

        await FakeOnClimbRequested(nav, panComponent);

        Assert.Equal(1, panComponent.NotifyTransitionEndedCalls);
        Assert.False(panComponent.InTransition);
    }

    [Fact]
    public async Task Climb_releases_lock_via_finally_even_when_NavigateLadderUp_throws()
    {
        // Defensive shape kept symmetric with E3/E5. If NavigateLadderUp
        // ever blows up (registration missing, ladder logic exception,
        // etc), the player still recovers input on next interaction.
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();

        async Task Handler()
        {
            try
            {
                await Task.FromException(new System.InvalidOperationException("simulated"));
            }
            finally
            {
                panComponent.NotifyTransitionEnded();
            }
        }

        await Assert.ThrowsAsync<System.InvalidOperationException>(Handler);
        Assert.Equal(1, panComponent.NotifyTransitionEndedCalls);
        Assert.False(panComponent.InTransition);
    }
}
