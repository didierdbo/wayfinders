namespace Wayfinders.Client.Services;

/// <summary>
/// Pure-C# cadence helper for <see cref="WorldSimTick"/>. Owns the
/// <c>delta-accumulator → tick-bumped?</c> contract so xUnit can pin
/// it without a Godot runtime.
///
/// <para>
/// <b>Why a helper.</b> The cadence is one of those places where a
/// hand-rolled <c>delta</c> accumulator hides off-by-one and
/// over-tick bugs : if the accumulator is reset to zero after a tick
/// (instead of subtracting the interval), variable frame times
/// silently drift the cadence. A regression that changes the reset
/// rule from "subtract" to "set to zero" would fail to fire here
/// but pass at runtime if Didier's machine ran at exactly 60 FPS.
/// Better caught by an xUnit pin.
/// </para>
///
/// <para>
/// <b>Determinism contract.</b> Same sequence of <c>delta</c> inputs
/// always produces the same sequence of tick bumps. The helper holds
/// no time state of its own beyond the accumulator (no
/// <c>DateTime.UtcNow</c>, no <c>OS.GetTicksMsec</c>) — every input
/// crosses the boundary explicitly. This lets the WorldSimTick
/// autoload feed it from <c>_Process(delta)</c> in production and the
/// test feed it from a synthetic delta sequence in xUnit, with the
/// same code path.
/// </para>
///
/// <para>
/// <b>Multiple ticks per Advance.</b> If a frame stalls badly
/// (Windows VRAM page-fault, Steam overlay opening, etc.) and a
/// single <c>delta</c> exceeds <see cref="IntervalSeconds"/> by more
/// than a factor of 2, we still want the tick counter to advance
/// proportionally — otherwise a 3-second hiccup would only produce
/// one tick instead of 3 and the world cadence would lag forever.
/// <see cref="Advance"/> returns the number of ticks that bumped on
/// the call, and the autoload calls
/// <see cref="ApiClient.TickAsync"/> once per bumped tick (or
/// coalesces if a backpressure budget is exceeded — that policy
/// lives in <see cref="WorldSimTick"/>, not here).
/// </para>
/// </summary>
public sealed class WorldSimTickCadence
{
    /// <summary>Wall-clock interval between ticks. Locked at 1 s by
    /// Didier's 2026-05-10 ratification (memory
    /// <c>project_wayfinders_mission_emergence.md</c>) — gives the
    /// game-loop pace a "real second" feel and removes API-burst
    /// risk that 1-tick-per-frame would create.</summary>
    public double IntervalSeconds { get; }

    /// <summary>Accumulated frame deltas modulo
    /// <see cref="IntervalSeconds"/>. Reset by subtraction (NOT zero)
    /// when a tick bumps — see class-level docstring for the
    /// rationale.</summary>
    public double Accumulator { get; private set; }

    /// <summary>Monotonically increasing tick count. Incremented by
    /// <see cref="Advance"/> ; never reset.</summary>
    public int Tick { get; private set; }

    public WorldSimTickCadence(double intervalSeconds)
    {
        if (intervalSeconds <= 0.0)
            throw new System.ArgumentOutOfRangeException(
                nameof(intervalSeconds), "interval must be > 0");
        IntervalSeconds = intervalSeconds;
    }

    /// <summary>
    /// Feed a frame delta and return the number of ticks that bumped
    /// on this call (typically 0 or 1, but can be 2+ on a long stall).
    /// The post-call <see cref="Tick"/> property reflects the new
    /// counter.
    /// </summary>
    public int Advance(double delta)
    {
        if (delta < 0.0)
            throw new System.ArgumentOutOfRangeException(
                nameof(delta), "delta must be >= 0");

        Accumulator += delta;
        var bumped = 0;
        while (Accumulator >= IntervalSeconds)
        {
            Accumulator -= IntervalSeconds;
            Tick++;
            bumped++;
        }
        return bumped;
    }
}
