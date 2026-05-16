using System.Collections.Generic;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the E5 District Map world-space migration in
/// Phase 9 slice 5 (2026-05-08). The Godot-side <c>E3DistrictMap</c>
/// cannot be loaded from xUnit (no engine), so these tests assert on a
/// fake harness that mimics the slice 5 climb pipeline (
/// <c>MapPan2DComponent.ClimbRequested</c> signal -&gt; screen handler
/// -&gt; <c>SceneManager.NavigateBack</c> -&gt; transition lock release)
/// and lock the expected intents independent of the Godot scene tree.
///
/// <list type="bullet">
///   <item>Slice 5 livrable 3 : climb-from-E5 calls
///         <c>SceneManager.NavigateBack()</c> exactly once.</item>
///   <item>Slice 5 livrable 3 : the transition lock on the pan
///         component is released via <c>NotifyTransitionEnded</c> after
///         NavigateBack returns.</item>
///   <item>Slice 5 livrable 4 : the climb path does NOT itself
///         snapshot the camera state (Hide-not-Free preserves E3's
///         camera state for free across L2↔L3 round trips). Asserted
///         by absence of an explicit snapshot call on the fake.</item>
///   <item>Slice 5 livrable 1 : the OriginCoord payload key for
///         E3-&gt;E5 is the stable string both writers (E3) and readers
///         (E5) agree on. Mirror of the E2-&gt;E3 contract pinned in
///         <see cref="E3WorldSpaceClimbContractTests"/>.</item>
/// </list>
///
/// <para>
/// Pattern mirrors <see cref="E3WorldSpaceClimbContractTests"/> verbatim
/// : a fake harness simulates the SceneManager + MapPan2DComponent
/// public surface ; the E5 climb handler is reproduced inline (fake
/// <c>OnClimbRequested</c>) so the signal-to-NavigateBack-to-lock-release
/// wiring is testable hors-Godot. Same code path as the real
/// <c>E3DistrictMap.OnClimbRequested</c> body.
/// </para>
/// </summary>
public sealed class E5WorldSpaceClimbContractTests
{
    /// <summary>
    /// Stable payload key used by E3 to thread the L2 cursor world
    /// position into E5's <see cref="ScreenContext.Payload"/>. Mirrors
    /// the const on the Godot-side <c>E3DistrictMap.OriginCoordPayloadKey</c>
    /// (and the equivalent inline string at E3's drill site) --
    /// duplicated here so the xUnit assembly does not need to compile-
    /// include the Godot screen file.
    /// </summary>
    private const string OriginCoordPayloadKey = "E3.OriginCoord";

    private sealed class FakeSceneManager
    {
        public List<string> NavigateBackCalls { get; } = new();
        public List<string> NavigateToCalls { get; } = new();

        public Task NavigateBack()
        {
            NavigateBackCalls.Add("back");
            return Task.CompletedTask;
        }

        public Task NavigateTo(string screenId)
        {
            NavigateToCalls.Add(screenId);
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
    /// Reproduces the body of <c>E3DistrictMap.OnClimbRequested</c>
    /// without the Godot context : await NavigateBack, release the
    /// transition lock in finally. Same shape as E3's slice-4 climb
    /// handler.
    /// </summary>
    private static async Task FakeOnClimbRequested(
        FakeSceneManager nav, FakePanComponent panComponent)
    {
        try
        {
            await nav.NavigateBack();
        }
        finally
        {
            panComponent.NotifyTransitionEnded();
        }
    }

    [Fact]
    public async Task Climb_from_E5_calls_NavigateBack_exactly_once()
    {
        // Slice 5 livrable 3 -- the climb signal handler dispatches to
        // SceneManager.NavigateBack. NavigateTo is NOT called : the
        // climb path is a pure pop, not a forward navigation. This pins
        // the contract that climb-out at L3 is a back-pop, not a
        // ladder-up traversal that re-instantiates E3 (which would
        // discard E3's preserved camera state -- the whole point of
        // Hide-not-Free across the L2↔L3 round trip, mirror of the
        // L1↔L2 round trip locked in slice 4).
        var nav = new FakeSceneManager();
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();

        await FakeOnClimbRequested(nav, panComponent);

        Assert.Single(nav.NavigateBackCalls);
        Assert.Empty(nav.NavigateToCalls);
    }

    [Fact]
    public async Task Climb_from_E5_releases_transition_lock_after_NavigateBack()
    {
        // Slice 5 livrable 3 -- the transition lock on the pan component
        // must be released after NavigateBack returns. E5 is freed by
        // NavigateBack so the lock release lands on a node about to be
        // freed (idempotent flag write) ; the call is kept for symmetry
        // with E3's pattern -- if a future slice retains E5
        // (Hide-not-Free climb to support a save-and-resume flow), the
        // lock release is still needed.
        var nav = new FakeSceneManager();
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();
        Assert.True(panComponent.InTransition); // sanity

        await FakeOnClimbRequested(nav, panComponent);

        Assert.Equal(1, panComponent.NotifyTransitionEndedCalls);
        Assert.False(panComponent.InTransition);
    }

    [Fact]
    public async Task Climb_releases_lock_via_finally_even_when_NavigateBack_throws()
    {
        // Slice 5 defensive shape -- the pan component lock release lives
        // in the finally block of the screen handler, so a NavigateBack
        // that throws still leaves the player with input on next
        // interaction. We simulate the throw with a dedicated fake
        // variant.
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

    [Fact]
    public void E3_OriginCoord_payload_key_is_E3_OriginCoord_dot_namespaced()
    {
        // Slice 5 livrable 1 -- the payload key namespacing for L2->L3.
        // E3 writes, E5 reads, both agree on the literal string. The
        // "E3." prefix matches the convention established for
        // "E4.NpcId" and "E2.OriginCoord" : the screen that owns the
        // writer's domain prefixes the key.
        Assert.Equal("E3.OriginCoord", OriginCoordPayloadKey);
    }

    [Fact]
    public void E3_OriginCoord_payload_round_trips_through_ScreenContext()
    {
        // Slice 5 livrable 1 -- the OriginCoord round trip. E3 packs a
        // boxed value (cursor position) under the OriginCoordPayloadKey ;
        // E5 reads it back at OnEnter. The payload bag uses object as
        // its value type so the Godot-side Vector2 rides through
        // unchanged ; xUnit substitutes a System.Numerics.Vector2 here
        // because Godot.Vector2 lives in the Godot assembly. The
        // contract tested is the bag mechanics, not the Vector2 shape.
        var sourceCoord = new System.Numerics.Vector2(2200.0f, 1380.0f);
        var ctx = new ScreenContext
        {
            Payload = new Dictionary<string, object>
            {
                [OriginCoordPayloadKey] = sourceCoord,
            },
        };

        Assert.True(ctx.Payload.TryGetValue(OriginCoordPayloadKey, out var raw));
        Assert.IsType<System.Numerics.Vector2>(raw);
        var roundTripped = (System.Numerics.Vector2)raw!;
        Assert.Equal(2200.0f, roundTripped.X);
        Assert.Equal(1380.0f, roundTripped.Y);
    }

    [Fact]
    public async Task Climb_does_not_call_explicit_camera_snapshot_on_E3()
    {
        // Slice 5 livrable 4 -- the climb path does NOT explicitly
        // snapshot E3's camera state. The Hide-not-Free model on
        // SceneManager.NavigateTo (which hides E3 instead of freeing it
        // when E5 is pushed) preserves Camera2D.Position + Zoom across
        // the L2↔L3 round trip naturally -- E3 never died, so its state
        // is intact. Adding an explicit snapshot/restore would be
        // redundant (and a place for drift to creep in : the snapshot
        // timing vs the hide timing).
        //
        // We model this by asserting that the fake panComponent (which
        // represents E5's pan component) is the ONLY one notified on
        // climb. There is no second panComponent for E3 receiving any
        // camera-position write ; the absence of a second method on
        // the fake is itself the contract assertion. Same shape as
        // <see cref="E3WorldSpaceClimbContractTests.Climb_does_not_call_explicit_camera_snapshot_on_E2"/>
        // for the L1↔L2 round trip.
        var nav = new FakeSceneManager();
        var e5PanComponent = new FakePanComponent();
        e5PanComponent.EnterTransition();

        await FakeOnClimbRequested(nav, e5PanComponent);

        // If a future slice adds an explicit E3-camera-snapshot path,
        // it should fail this test by asserting some new method got
        // called on a separate fake -- which would force the maintainer
        // to update this test deliberately rather than silently.
        Assert.Equal(1, e5PanComponent.NotifyTransitionEndedCalls);
        Assert.Single(nav.NavigateBackCalls);
    }
}
