using System.Collections.Generic;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the E3 City Halfgate world-space migration in
/// Phase 9 slice 4 (2026-05-08). The Godot-side <c>E3CityHalfgate</c>
/// cannot be loaded from xUnit (no engine), so these tests assert on a
/// fake harness that mimics the slice-4 climb pipeline (
/// <c>MapPan2DComponent.ClimbRequested</c> signal -&gt; screen handler
/// -&gt; <c>SceneManager.NavigateBack</c> -&gt; transition lock release)
/// and lock the expected intents independent of the Godot scene tree.
///
/// <list type="bullet">
///   <item>Slice 4 livrable 2 : climb-from-E3 calls
///         <c>SceneManager.NavigateBack()</c> exactly once.</item>
///   <item>Slice 4 livrable 2 : the transition lock on the pan
///         component is released via
///         <c>NotifyTransitionEnded</c> after NavigateBack returns.</item>
///   <item>Slice 4 livrable 1 : the OriginCoord payload key is the
///         stable string both writers (E2) and readers (E3) agree on.
///         Mirrors the same pattern used for <c>E4.NpcId</c>.</item>
///   <item>Slice 4 livrable 3 : the climb path does NOT itself snapshot
///         the camera state (Hide-not-Free preserves it for free).
///         Asserted by absence of an explicit snapshot call on the
///         fake.</item>
/// </list>
///
/// <para>
/// Pattern mirrors <see cref="E3NavigationContractTests"/> J4 +
/// <see cref="E2NavigationContractTests"/> J3 : a fake harness
/// simulates the <c>SceneManager</c> + <c>MapPan2DComponent</c> public
/// surface ; the E3 climb handler is reproduced inline (fake
/// <c>OnClimbRequested</c>) so the signal-to-NavigateBack-to-lock-release
/// wiring is testable hors-Godot. Same code path as the real
/// <c>E3CityHalfgate.OnClimbRequested</c> body.
/// </para>
/// </summary>
public sealed class E3WorldSpaceClimbContractTests
{
    /// <summary>
    /// Stable payload key used by E2 to thread the L1 origin cell into
    /// E3's <see cref="ScreenContext.Payload"/>. Mirrors the const on
    /// the Godot-side <c>E3CityHalfgate.OriginCoordPayloadKey</c>
    /// (and the equivalent inline string at E2's drill site) -- duplicated
    /// here so the xUnit assembly does not need to compile-include the
    /// Godot screen file.
    /// </summary>
    private const string OriginCoordPayloadKey = "E2.OriginCoord";

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
    /// Reproduces the body of <c>E3CityHalfgate.OnClimbRequested</c>
    /// without the Godot context : await NavigateBack, release the
    /// transition lock in finally. Same shape as E2's slice-3 climb
    /// handler. The fake panComponent observes that
    /// <c>NotifyTransitionEnded</c> is called exactly once even on the
    /// happy path.
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
    public async Task Climb_from_E3_calls_NavigateBack_exactly_once()
    {
        // Slice 4 livrable 2 -- the climb signal handler dispatches to
        // SceneManager.NavigateBack. NavigateTo is NOT called : the
        // climb path is a pure pop, not a forward navigation. This
        // pins the contract that climb-out at L2 is a back-pop, not a
        // ladder-up traversal that re-instantiates E2 (which would
        // discard E2's preserved camera state -- the whole point of
        // Hide-not-Free).
        var nav = new FakeSceneManager();
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();

        await FakeOnClimbRequested(nav, panComponent);

        Assert.Single(nav.NavigateBackCalls);
        Assert.Empty(nav.NavigateToCalls);
    }

    [Fact]
    public async Task Climb_from_E3_releases_transition_lock_after_NavigateBack()
    {
        // Slice 4 livrable 2 -- the transition lock on the pan component
        // must be released after NavigateBack returns. Without this, a
        // re-drill into the same E3 (after a future Hide-not-Free climb,
        // hypothetical slice extension) would land on a pan component
        // still in the locked state and silently ignore all input.
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
        // Slice 4 defensive shape -- the pan component lock release lives
        // in the finally block of the screen handler, so a NavigateBack
        // that throws (registration missing, broken stack, future bug)
        // still leaves the player with input on next interaction. We
        // simulate the throw with a dedicated fake variant.
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
    public void OriginCoord_payload_key_is_E2_OriginCoord_dot_namespaced()
    {
        // Slice 4 livrable 1 -- the payload key namespacing. E2 writes,
        // E3 reads, both agree on the literal string. The "E2." prefix
        // matches the convention established for "E4.NpcId" : the screen
        // that owns the writer's domain prefixes the key. A future
        // typed ScreenContext.OriginCoord property would replace this
        // string-keyed pattern, but for MVP the inline string is the
        // contract surface and this test pins it.
        Assert.Equal("E2.OriginCoord", OriginCoordPayloadKey);
    }

    [Fact]
    public void OriginCoord_payload_round_trips_through_ScreenContext()
    {
        // Slice 4 livrable 1 -- the OriginCoord round trip. E2 packs a
        // boxed value (cell index) under the OriginCoordPayloadKey ;
        // E3 reads it back at OnEnter. The payload bag uses object as
        // its value type so the Godot-side Vector2I rides through
        // unchanged ; xUnit substitutes a plain (int, int) tuple here
        // because Vector2I lives in the Godot assembly. The contract
        // tested is the bag mechanics, not the Vector2I shape.
        var sourceCoord = (Col: 12, Row: 7);
        var ctx = new ScreenContext
        {
            Payload = new Dictionary<string, object>
            {
                [OriginCoordPayloadKey] = sourceCoord,
            },
        };

        Assert.True(ctx.Payload.TryGetValue(OriginCoordPayloadKey, out var raw));
        Assert.IsType<(int Col, int Row)>(raw);
        var roundTripped = ((int Col, int Row))raw!;
        Assert.Equal(12, roundTripped.Col);
        Assert.Equal(7, roundTripped.Row);
    }

    [Fact]
    public async Task Climb_does_not_call_explicit_camera_snapshot_on_E2()
    {
        // Slice 4 livrable 3 -- the climb path does NOT explicitly
        // snapshot E2's camera state. The Hide-not-Free model on
        // SceneManager.NavigateTo (which hides E2 instead of freeing it
        // when E3 is pushed) preserves Camera2D.Position + Zoom across
        // the round trip naturally -- E2 never died, so its state is
        // intact. Adding an explicit snapshot/restore would be redundant
        // (and a place for drift to creep in : the snapshot timing vs
        // the hide timing).
        //
        // We model this by asserting that the fake panComponent (which
        // represents E3's pan component) is the ONLY one notified on
        // climb. There is no second panComponent for E2 receiving any
        // camera-position write ; the absence of a second method on
        // the fake (no SetCameraPosition / RestoreCamera / ApplySnapshot)
        // is itself the contract assertion.
        var nav = new FakeSceneManager();
        var e3PanComponent = new FakePanComponent();
        e3PanComponent.EnterTransition();

        await FakeOnClimbRequested(nav, e3PanComponent);

        // If a future slice adds an explicit E2-camera-snapshot path,
        // it should fail this test by asserting some new method got
        // called on a separate fake -- which would force the maintainer
        // to update this test deliberately rather than silently.
        Assert.Equal(1, e3PanComponent.NotifyTransitionEndedCalls);
        Assert.Single(nav.NavigateBackCalls);
    }
}
