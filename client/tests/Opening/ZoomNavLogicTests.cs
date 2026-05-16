using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pure-C# tests for <see cref="ZoomNavLogic"/>. Pins slice 3 (M3 / Phase 9
/// / 2026-05-08) zoom-driven navigation semantics on L1 World :
/// <list type="bullet">
///   <item>Below threshold : every wheel event is continuous zoom.</item>
///   <item>At ZoomMax + Push + tile >= Esquissée : drill candidate.</item>
///   <item>At ZoomMax + Push + tile below Esquissée : drill blocked.</item>
///   <item>At ZoomMin + Pull : climb candidate.</item>
///   <item>Asymmetric hysteresis : 95 % in vs 120 % out.</item>
///   <item>Debounce : 250 ms post-transition swallows every wheel event.</item>
/// </list>
///
/// <para>
/// <b>Coverage strategy.</b> Each branch of <see cref="ZoomNavLogic.EvaluateWheelInput"/>
/// gets at least one positive test (the action fires) and one boundary
/// test (one tick before / after the inflection). Same density as
/// <see cref="MapPanInputLogicTests"/> -- the helper is small but the
/// brief-locked thresholds are part of the contract and must not drift
/// silently.
/// </para>
///
/// <para>
/// <b>Vocabulary.</b> Tests use <c>Pull</c> / <c>Push</c> exclusively. The
/// raw Godot <c>WheelUp</c> / <c>WheelDown</c> labels never appear in
/// this file ; the translation lives at the runtime seam
/// (<c>MapPan2DComponent._Input</c>) and is exercised through manual
/// integration, not xUnit.
/// </para>
/// </summary>
public sealed class ZoomNavLogicTests
{
    private static readonly ZoomThresholds DefaultThresholds = ZoomThresholds.Default;

    [Fact]
    public void Default_thresholds_compute_inflection_lines()
    {
        Assert.Equal(0.5f, DefaultThresholds.ZoomMin);
        Assert.Equal(2.0f, DefaultThresholds.ZoomMax);
        Assert.Equal(1.9f, DefaultThresholds.ZoomInThreshold, precision: 4);
        Assert.Equal(0.6f, DefaultThresholds.ZoomOutThreshold, precision: 4);
    }

    [Fact]
    public void Push_below_zoom_max_returns_continuous_zoom()
    {
        // Currently at ZoomDefault (1.0), well below ZoomMax (2.0). A
        // Push event should zoom continuously, never fire drill.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 1.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.ZoomContinuous, outcome.Action);
    }

    [Fact]
    public void Push_above_threshold_below_max_still_continuous_zoom()
    {
        // Above the 95 % drill-candidacy line (1.9) but below the cap
        // (2.0) -- continuous zoom still wins. Drill only fires AT the
        // cap, not at the threshold (Q2 brief tranche : threshold-fire
        // would steal user agency, cap-fire is the deliberate ceiling
        // event).
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 1.95f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Esquissee,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.ZoomContinuous, outcome.Action);
    }

    [Fact]
    public void Push_at_zoom_max_with_known_tile_fires_drill_candidate()
    {
        // Cap-Push : the player is at ZoomMax and pushes again, the
        // cell under the cursor is at the minimum drill threshold
        // (Esquissée per Varn §1.4). Drill candidate fires.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 2.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Esquissee,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.DrillCandidate, outcome.Action);
    }

    [Fact]
    public void Push_at_zoom_max_with_levee_tile_fires_drill_candidate()
    {
        // Higher knowledge than the minimum gate also drills -- the
        // gate is a floor, not a band.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 2.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Levee,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.DrillCandidate, outcome.Action);
    }

    [Fact]
    public void Push_at_zoom_max_with_inconnue_tile_fires_drill_blocked()
    {
        // Cap-Push but the cell is unknown -- the drill cannot proceed
        // (Varn §1.4 lock). Runtime plays the "tuile veut être vue mais
        // pas encore" feedback.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 2.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.DrillBlocked, outcome.Action);
    }

    [Fact]
    public void Push_at_zoom_max_with_pressentie_tile_fires_drill_blocked()
    {
        // Pressentie is below the Esquissée gate (Varn §1.4 line) --
        // the player has heard a rumor but has not surveyed yet. Drill
        // is blocked ; the player must walk a unit there or
        // patient-investigate first.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 2.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Pressentie,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.DrillBlocked, outcome.Action);
    }

    [Fact]
    public void Pull_above_zoom_min_returns_continuous_zoom()
    {
        // Currently zoomed out a bit (0.8), well above ZoomMin (0.5).
        // A Pull event continues to zoom out, never fires climb.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 0.8f,
            gesture: WheelGesture.Pull,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.ZoomContinuous, outcome.Action);
    }

    [Fact]
    public void Pull_at_zoom_min_fires_climb_candidate()
    {
        // Floor-Pull : the player is at ZoomMin and pulls again. Climb
        // candidate fires regardless of tile knowledge (climbing always
        // succeeds when the ladder allows -- the gate is on drill in,
        // not climb out, per Varn §1.4 asymmetry rationale).
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 0.5f,
            gesture: WheelGesture.Pull,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.ClimbCandidate, outcome.Action);
    }

    [Fact]
    public void Pull_below_threshold_above_floor_still_continuous_zoom()
    {
        // Below the 120 % climb-candidacy line (0.6) but above the
        // floor (0.5). Same agency-preservation pattern as the
        // drill-in side : the threshold is a candidacy line, the
        // climb only fires AT the floor.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 0.55f,
            gesture: WheelGesture.Pull,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.ZoomContinuous, outcome.Action);
    }

    [Fact]
    public void Drill_candidate_fires_at_max_minus_epsilon_too()
    {
        // Floating-point accumulation across tween steps could leave
        // the camera at ZoomMax - 1e-7 -- the epsilon guard ensures
        // the cap-Push branch still fires.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 2.0f - 1e-5f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Esquissee,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.DrillCandidate, outcome.Action);
    }

    [Fact]
    public void Climb_candidate_fires_at_min_plus_epsilon_too()
    {
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 0.5f + 1e-5f,
            gesture: WheelGesture.Pull,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.ClimbCandidate, outcome.Action);
    }

    [Fact]
    public void Debounce_swallows_wheel_within_window()
    {
        // 100 ms after a transition, well inside the 250 ms window.
        // No matter the gesture, no matter the tile, no matter the
        // zoom : the action is Debounced.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 2.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Esquissee,
            lastTransitionTimeMs: 1000,
            currentTimeMs: 1100,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.Debounced, outcome.Action);
    }

    [Fact]
    public void Debounce_releases_at_window_boundary()
    {
        // Exactly 250 ms after a transition -- the window has just
        // expired (strict < in the predicate). The next wheel event
        // is processed normally.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 1.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 1000,
            currentTimeMs: 1250,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.ZoomContinuous, outcome.Action);
    }

    [Fact]
    public void Debounce_does_not_fire_at_session_start()
    {
        // lastTransitionTimeMs == 0 indicates "no transition has
        // happened yet". Without the explicit guard, the predicate
        // `currentTimeMs - 0 < 250` would block every wheel event in
        // the first 250 ms of the game. The guard ensures that pristine
        // sessions are unaffected by the debounce.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 1.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 0,
            currentTimeMs: 50,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.ZoomContinuous, outcome.Action);
    }

    [Fact]
    public void Debounce_holds_across_drill_then_climb()
    {
        // Sequence : drill at t=1000 -> 100 ms later the player Pulls
        // hard. Even though the gesture would normally fire ClimbCandidate
        // (zoom is at the floor), the debounce window dominates.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 0.5f,
            gesture: WheelGesture.Pull,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Inconnue,
            lastTransitionTimeMs: 1000,
            currentTimeMs: 1100,
            thresholds: DefaultThresholds);

        Assert.Equal(ZoomNavAction.Debounced, outcome.Action);
    }

    [Theory]
    [InlineData(TileKnowledgeLadder.Inconnue,   ZoomNavAction.DrillBlocked)]
    [InlineData(TileKnowledgeLadder.Pressentie, ZoomNavAction.DrillBlocked)]
    [InlineData(TileKnowledgeLadder.Esquissee,  ZoomNavAction.DrillCandidate)]
    [InlineData(TileKnowledgeLadder.Levee,      ZoomNavAction.DrillCandidate)]
    [InlineData(TileKnowledgeLadder.Scellee,    ZoomNavAction.DrillCandidate)]
    public void Drill_gate_fires_only_for_states_at_or_above_esquissee(
        TileKnowledgeLadder state,
        ZoomNavAction expectedAction)
    {
        // Pinned ladder : Inconnue (0), Pressentie (1), Esquissée (2),
        // Levée (3), Scellée (4). The drill gate is "state >= Esquissée".
        // This @Theory acts as the canonical reference for that line,
        // and will catch any future drift if Varn revises the ladder.
        var outcome = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 2.0f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: state,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: DefaultThresholds);

        Assert.Equal(expectedAction, outcome.Action);
    }

    [Fact]
    public void Custom_thresholds_are_respected()
    {
        // Per-instance override -- a hypothetical L3 quartier with
        // tighter zoom range. The helper should compute its inflection
        // lines from the supplied thresholds, not from defaults.
        var custom = new ZoomThresholds(
            ZoomMin: 0.8f,
            ZoomDefault: 1.0f,
            ZoomMax: 1.5f,
            ZoomInThresholdRatio: 0.95f,
            ZoomOutThresholdRatio: 1.20f,
            PostTransitionDebounceMs: 250);

        var atCustomMax = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 1.5f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Esquissee,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: custom);

        var belowCustomMax = ZoomNavLogic.EvaluateWheelInput(
            currentZoom: 1.49f,
            gesture: WheelGesture.Push,
            tileKnowledgeAtCursor: TileKnowledgeLadder.Esquissee,
            lastTransitionTimeMs: 0,
            currentTimeMs: 1000,
            thresholds: custom);

        Assert.Equal(ZoomNavAction.DrillCandidate, atCustomMax.Action);
        Assert.Equal(ZoomNavAction.ZoomContinuous, belowCustomMax.Action);
    }
}
