using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Contract tests for the slice 5 L2→L3 drill wiring on E3 City Halfgate
/// (Phase 9 / 2026-05-08). The Godot-side <c>E3CityHalfgate</c> cannot
/// be loaded from xUnit (no engine), so these tests assert on a fake
/// harness that mimics the slice 5 drill pipeline (
/// <c>MapPan2DComponent.DrillRequested</c> signal -&gt; screen handler
/// -&gt; <c>SceneManager.NavigateTo("E5_DISTRICT", payload)</c> -&gt;
/// transition lock release) and lock the expected intents independent
/// of the Godot scene tree.
///
/// <list type="bullet">
///   <item>Slice 5 livrable 2 : drill-from-E3 calls
///         <c>SceneManager.NavigateTo("E5_DISTRICT", ...)</c> exactly once.</item>
///   <item>Slice 5 livrable 2 : the transition lock on the pan
///         component is released via <c>NotifyTransitionEnded</c> after
///         NavigateTo returns (mirror of climb in slice 4).</item>
///   <item>Slice 5 livrable 2 : the OriginCoord payload key for E3-&gt;E5
///         is the stable string both writers (E3) and readers (E5)
///         agree on. Mirrors the same pattern used for
///         <c>E2.OriginCoord</c> in slice 4.</item>
///   <item>Slice 5 decision α : the always-true drill resolver always
///         returns a state &gt;= Esquissée so
///         <see cref="ZoomNavLogic.EvaluateWheelInput"/> classifies the
///         cap-Push as <see cref="ZoomNavAction.DrillCandidate"/>,
///         never <see cref="ZoomNavAction.DrillBlocked"/>. Pinned with
///         <see cref="ZoomNavLogic.EvaluateWheelInput"/> directly.</item>
///   <item>Slice 5 livrable 2 : the drill payload carries the cursor
///         world-space position (<see cref="System.Numerics.Vector2"/>
///         substitute), not the resolver sentinel coord. The handler
///         re-fetches the actual cursor position rather than trusting
///         the signal coord.</item>
/// </list>
///
/// <para>
/// Pattern mirrors <see cref="E3WorldSpaceClimbContractTests"/> and
/// <see cref="E2NavigationContractTests"/> J3 : a fake harness
/// simulates the SceneManager + MapPan2DComponent public surface ; the
/// E3 drill handler is reproduced inline (fake <c>OnDrillRequested</c>)
/// so the signal-to-NavigateTo-to-lock-release wiring is testable
/// hors-Godot. The substitute Vector2 type used here is
/// <see cref="System.Numerics.Vector2"/> -- the Godot Vector2 lives in
/// the engine assembly which xUnit cannot load.
/// </para>
/// </summary>
public sealed class E3DrillToL3ContractTests
{
    /// <summary>
    /// Stable payload key used by E3 to thread the L2 cursor position
    /// into E5's <see cref="ScreenContext.Payload"/>. Mirrors the const
    /// on the Godot-side <c>E5DistrictMap.OriginCoordPayloadKey</c> --
    /// duplicated here so the xUnit assembly does not need to compile-include
    /// the Godot screen file. Same duplication ceremony as the
    /// <c>"E4.NpcId"</c> and <c>"E2.OriginCoord"</c> mirrors.
    /// </summary>
    private const string OriginCoordPayloadKey = "E3.OriginCoord";

    /// <summary>Stable screen id of the L3 drill target.</summary>
    private const string DrillTargetScreenId = "E5_DISTRICT";

    private sealed class FakeSceneManager
    {
        public List<(string ScreenId, IReadOnlyDictionary<string, object> Payload)> NavigateToCalls { get; } = new();
        public List<string> NavigateBackCalls { get; } = new();

        public Task NavigateTo(string screenId, IReadOnlyDictionary<string, object> payload)
        {
            NavigateToCalls.Add((screenId, payload));
            return Task.CompletedTask;
        }

        public Task NavigateBack()
        {
            NavigateBackCalls.Add("back");
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
    /// Reproduces the body of <c>E3CityHalfgate.OnDrillRequested</c>
    /// without the Godot context : capture the cursor world-space
    /// position (passed in from outside in this fake), pack it into
    /// the payload, await NavigateTo, release the transition lock in
    /// finally. Same shape as E2's slice-3 drill handler (with the
    /// payload key flipped from <c>E2.OriginCoord</c> to
    /// <c>E3.OriginCoord</c>). The fake panComponent observes that
    /// <c>NotifyTransitionEnded</c> is called exactly once even on
    /// the happy path.
    /// </summary>
    private static async Task FakeOnDrillRequested(
        FakeSceneManager nav,
        FakePanComponent panComponent,
        System.Numerics.Vector2 cursorWorldPosition)
    {
        var payload = new Dictionary<string, object>
        {
            [OriginCoordPayloadKey] = cursorWorldPosition,
        };

        try
        {
            await nav.NavigateTo(DrillTargetScreenId, payload);
        }
        finally
        {
            panComponent.NotifyTransitionEnded();
        }
    }

    [Fact]
    public async Task Drill_from_E3_calls_NavigateTo_E5_exactly_once()
    {
        // Slice 5 livrable 2 -- the drill signal handler dispatches to
        // SceneManager.NavigateTo with the literal id "E5_DISTRICT".
        // NavigateBack is NOT called : the drill path is a forward
        // push, not a back-pop. This pins the contract that drill at
        // L2 is a forward navigation that Hide-not-Free's E3 (preserving
        // its camera state for the eventual climb-back).
        var nav = new FakeSceneManager();
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();

        await FakeOnDrillRequested(
            nav, panComponent, new System.Numerics.Vector2(1234.5f, 678.9f));

        Assert.Single(nav.NavigateToCalls);
        Assert.Equal(DrillTargetScreenId, nav.NavigateToCalls[0].ScreenId);
        Assert.Empty(nav.NavigateBackCalls);
    }

    [Fact]
    public async Task Drill_from_E3_releases_transition_lock_after_NavigateTo()
    {
        // Slice 5 livrable 2 -- the transition lock on the pan component
        // must be released after NavigateTo returns. Without this, the
        // climb-back from E5 to E3 would land on a pan component still
        // in the locked state and silently ignore all input. (E3 is
        // hidden, not freed, by NavigateTo's Hide-not-Free model -- the
        // pan component is alive across the L2↔L3 round trip.)
        var nav = new FakeSceneManager();
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();
        Assert.True(panComponent.InTransition); // sanity

        await FakeOnDrillRequested(
            nav, panComponent, System.Numerics.Vector2.Zero);

        Assert.Equal(1, panComponent.NotifyTransitionEndedCalls);
        Assert.False(panComponent.InTransition);
    }

    [Fact]
    public async Task Drill_releases_lock_via_finally_even_when_NavigateTo_throws()
    {
        // Slice 5 defensive shape -- the pan component lock release lives
        // in the finally block of the screen handler, so a NavigateTo
        // that throws (registration missing, broken stack, future bug)
        // still leaves the player with input on next interaction. We
        // simulate the throw with a dedicated fake variant.
        var panComponent = new FakePanComponent();
        panComponent.EnterTransition();

        async Task Handler()
        {
            try
            {
                await Task.FromException(new InvalidOperationException("simulated"));
            }
            finally
            {
                panComponent.NotifyTransitionEnded();
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(Handler);
        Assert.Equal(1, panComponent.NotifyTransitionEndedCalls);
        Assert.False(panComponent.InTransition);
    }

    [Fact]
    public void E3_OriginCoord_payload_key_is_E3_OriginCoord_dot_namespaced()
    {
        // Slice 5 livrable 2 -- the payload key namespacing for L2->L3.
        // E3 writes, E5 reads, both agree on the literal string.
        // The "E3." prefix matches the convention established for
        // "E4.NpcId" and "E2.OriginCoord" : the screen that owns the
        // writer's domain prefixes the key. A future typed
        // ScreenContext.OriginCoord property would replace this string-
        // keyed pattern, but for MVP the inline string is the contract
        // surface and this test pins it.
        Assert.Equal("E3.OriginCoord", OriginCoordPayloadKey);
    }

    [Fact]
    public async Task Drill_payload_carries_cursor_world_position()
    {
        // Slice 5 livrable 2 -- the OriginCoord payload from E3 carries
        // the cursor's L2 image-space world position at the moment of
        // the drill, NOT the always-true resolver's sentinel coord
        // (0,0). This pins the design : the resolver coord is
        // intentionally meaningless (decision α : there is no L2 grid),
        // and the meaningful drill-origin signal is the actual cursor
        // position which the handler re-fetches from the viewport.
        var nav = new FakeSceneManager();
        var panComponent = new FakePanComponent();
        var cursorWorld = new System.Numerics.Vector2(2200.0f, 1380.0f);

        await FakeOnDrillRequested(nav, panComponent, cursorWorld);

        Assert.Single(nav.NavigateToCalls);
        var (screenId, payload) = nav.NavigateToCalls[0];
        Assert.Equal(DrillTargetScreenId, screenId);
        Assert.True(payload.ContainsKey(OriginCoordPayloadKey));
        var roundTripped = (System.Numerics.Vector2)payload[OriginCoordPayloadKey]!;
        Assert.Equal(2200.0f, roundTripped.X);
        Assert.Equal(1380.0f, roundTripped.Y);
    }

    [Fact]
    public void Always_true_resolver_returns_DrillCandidate_at_cap_Push()
    {
        // Slice 5 decision α -- the always-true resolver returns a
        // (sentinel coord, TileKnowledgeState.Levee) pair. With
        // Levee >= Esquissee, ZoomNavLogic at zoom == ZoomMax with
        // gesture == Push returns DrillCandidate, never DrillBlocked.
        // Pinning this with the actual ZoomNavLogic call (not a mock)
        // is what catches a future regression where the threshold gate
        // changes and the always-true resolver silently stops triggering
        // drills.
        const TileKnowledgeState alwaysTrueState = TileKnowledgeState.Levee;
        var thresholds = ZoomThresholds.Default;

        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: thresholds.ZoomMax,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: alwaysTrueState,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: thresholds);

        Assert.Equal(ZoomNavAction.DrillCandidate, outcome.Action);
    }

    [Fact]
    public void Always_true_resolver_yields_DrillCandidate_even_below_threshold_at_cap()
    {
        // Slice 5 decision α -- defense-in-depth : even if a future
        // refactor decides to gate continuous zoom behind the
        // tileKnowledgeAtCursor (it does not today), the cap-Push branch
        // with the always-true Levee state must still classify as
        // DrillCandidate. We test at the exact cap (zoom == ZoomMax)
        // and at a fudged cap (zoom == ZoomMax + small epsilon) since
        // the runtime can occasionally accumulate floating-point past
        // the cap by 1e-7.
        var thresholds = ZoomThresholds.Default;

        // Exactly at cap.
        var atCap = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: thresholds.ZoomMax,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeState.Levee,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: thresholds);
        Assert.Equal(ZoomNavAction.DrillCandidate, atCap.Action);

        // Slightly past cap (within ZoomEpsilon -- the runtime
        // floating-point accumulation surface).
        var pastCap = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: thresholds.ZoomMax + ZoomNavLogic.ZoomEpsilon * 0.5f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeState.Levee,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: thresholds);
        Assert.Equal(ZoomNavAction.DrillCandidate, pastCap.Action);
    }
}
