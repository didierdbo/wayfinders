// M1 mission-emergence (etape 4b) — pin tests for the pure-C#
// cadence helper that drives the WorldSimTick autoload. The autoload
// itself is Godot-bound (Node, _Process(delta), CallDeferred) and is
// exercised end-to-end at etape 4d ; the cadence math is the
// regression-prone surface and is pinned here in isolation.
//
// What this file pins :
//   1. Single tick after one full interval of accumulated delta.
//   2. NO tick before the threshold.
//   3. Two ticks bumped on a single Advance when the delta exceeds
//      2 * interval — the long-stall scenario.
//   4. Subtract-after-bump (not zero-reset) — the determinism
//      contract that makes the cadence frame-rate-independent.
//   5. Determinism : same delta sequence, same tick count.
//   6. Constructor rejects non-positive intervals.
//   7. Negative deltas are rejected (Godot promises >= 0 ; we
//      defend against a corruption regression elsewhere).

using Wayfinders.Client.Services;

namespace Wayfinders.Client.Tests;

public sealed class WorldSimTickCadenceTests
{
    [Fact]
    public void Advance_below_interval_does_not_bump()
    {
        var c = new WorldSimTickCadence(1.0);
        var bumped = c.Advance(0.5);
        Assert.Equal(0, bumped);
        Assert.Equal(0, c.Tick);
        Assert.Equal(0.5, c.Accumulator);
    }

    [Fact]
    public void Advance_at_exactly_interval_bumps_once()
    {
        var c = new WorldSimTickCadence(1.0);
        var bumped = c.Advance(1.0);
        Assert.Equal(1, bumped);
        Assert.Equal(1, c.Tick);
        Assert.Equal(0.0, c.Accumulator);
    }

    [Fact]
    public void Advance_subtracts_interval_does_not_zero_accumulator()
    {
        // The frame-rate-independence contract : if a frame's delta
        // is 1.25 s and the interval is 1 s, we want to bump once
        // AND keep 0.25 s of remainder for the next frame. A
        // zero-reset implementation would silently drift the cadence
        // (5 ms per frame at 200 fps = 1 s drift per minute).
        var c = new WorldSimTickCadence(1.0);
        var bumped = c.Advance(1.25);
        Assert.Equal(1, bumped);
        Assert.Equal(1, c.Tick);
        Assert.Equal(0.25, c.Accumulator, precision: 10);
    }

    [Fact]
    public void Advance_with_long_stall_bumps_multiple_ticks()
    {
        // 3-second stall (Steam overlay opening, VRAM page-fault) :
        // the cadence should produce 3 ticks, not just 1. Otherwise
        // the world clock lags forever after every hiccup.
        var c = new WorldSimTickCadence(1.0);
        var bumped = c.Advance(3.0);
        Assert.Equal(3, bumped);
        Assert.Equal(3, c.Tick);
        Assert.Equal(0.0, c.Accumulator, precision: 10);
    }

    [Fact]
    public void Advance_partial_long_stall_keeps_remainder()
    {
        var c = new WorldSimTickCadence(1.0);
        var bumped = c.Advance(2.7);
        Assert.Equal(2, bumped);
        Assert.Equal(2, c.Tick);
        Assert.Equal(0.7, c.Accumulator, precision: 10);
    }

    [Fact]
    public void Advance_accumulates_across_multiple_calls()
    {
        var c = new WorldSimTickCadence(1.0);
        Assert.Equal(0, c.Advance(0.4));
        Assert.Equal(0, c.Tick);
        Assert.Equal(0, c.Advance(0.4));
        Assert.Equal(0, c.Tick);
        // Crossing 1.0 — accumulator was 0.8, +0.3 = 1.1, bump once.
        Assert.Equal(1, c.Advance(0.3));
        Assert.Equal(1, c.Tick);
        Assert.Equal(0.1, c.Accumulator, precision: 10);
    }

    [Fact]
    public void Same_delta_sequence_produces_same_tick_count()
    {
        var deltas = new[] { 0.16, 0.16, 0.16, 0.16, 0.16, 0.16, 0.16, 0.16 };
        var c1 = new WorldSimTickCadence(1.0);
        var c2 = new WorldSimTickCadence(1.0);
        foreach (var d in deltas) c1.Advance(d);
        foreach (var d in deltas) c2.Advance(d);
        Assert.Equal(c1.Tick, c2.Tick);
        Assert.Equal(c1.Accumulator, c2.Accumulator, precision: 10);
    }

    [Fact]
    public void Constructor_rejects_zero_interval()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new WorldSimTickCadence(0.0));
    }

    [Fact]
    public void Constructor_rejects_negative_interval()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new WorldSimTickCadence(-1.0));
    }

    [Fact]
    public void Advance_rejects_negative_delta()
    {
        var c = new WorldSimTickCadence(1.0);
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => c.Advance(-0.001));
    }

    [Fact]
    public void Tick_is_monotonically_increasing()
    {
        var c = new WorldSimTickCadence(0.5);
        var prev = c.Tick;
        for (var i = 0; i < 20; i++)
        {
            c.Advance(0.1);
            Assert.True(c.Tick >= prev, $"tick regressed at iteration {i}");
            prev = c.Tick;
        }
        // 20 * 0.1 = 2.0 s = exactly 4 ticks at 0.5 s interval.
        Assert.Equal(4, c.Tick);
    }

    [Fact]
    public void Custom_interval_smaller_than_one_second_works()
    {
        // Probe scene may use 0.1 s for diagnostics ; the helper
        // must work at sub-second cadence too.
        var c = new WorldSimTickCadence(0.1);
        var bumped = c.Advance(1.0);
        Assert.Equal(10, bumped);
        Assert.Equal(10, c.Tick);
    }

    [Fact]
    public void Custom_interval_five_seconds_bumps_once_per_five_seconds()
    {
        // Pin the M1-probe default (5 s/tick — Didier 2026-05-10
        // retune post-4d e2e). 4.99 s = no bump ; 5.00 s = one bump.
        // Frame-rate-independent : we feed the threshold via two
        // half-frames to mimic a real game loop.
        var c = new WorldSimTickCadence(5.0);
        Assert.Equal(0, c.Advance(2.5));
        Assert.Equal(0, c.Tick);
        Assert.Equal(0, c.Advance(2.49));
        Assert.Equal(0, c.Tick);
        // Crossing 5.0 — accumulator was 4.99, +0.02 = 5.01, bump once.
        Assert.Equal(1, c.Advance(0.02));
        Assert.Equal(1, c.Tick);
        Assert.Equal(0.01, c.Accumulator, precision: 10);
    }

    [Fact]
    public void Configurable_interval_propagates_to_IntervalSeconds()
    {
        // Defensive : the export-driven contract (Godot inspector
        // ----> WorldSimTickCadence ctor) is that whatever the
        // autoload's TickIntervalSeconds is set to, the cadence
        // helper honours it verbatim. M2 game-design will retune
        // this without recompile via the inspector ; this test pins
        // the seam.
        foreach (var interval in new[] { 0.1, 1.0, 5.0, 10.0, 30.0 })
        {
            var c = new WorldSimTickCadence(interval);
            Assert.Equal(interval, c.IntervalSeconds);
        }
    }
}
