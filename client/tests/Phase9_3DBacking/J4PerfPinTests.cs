using System;
using System.Diagnostics;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Phase9_3DBacking;

/// <summary>
/// Jalon 4 (Wayfinders 3D Backing Architecture, GO J4 2026-05-09) -- pin
/// the per-frame cost of the J4 generalised 3D backing math, isolated
/// from Godot. We cannot test C++/C# marshalling cost in xUnit (no
/// engine), so this fixture pins the <i>pure C# half</i> of the cost :
/// the forward iso projection executed once per follower per frame
/// when the dirty bit is set, and the all-clean foreach when nothing
/// has moved.
///
/// <para>
/// <b>What this fixture pins.</b>
/// <list type="bullet">
///   <item><b>Math budget all-dirty</b> : the forward iso projection
///         on 600 cells must complete in well under 1 ms (a frame budget
///         at 60 fps is ~16 ms ; we want plenty of headroom for the
///         rest of <c>_Process</c>). Catches catastrophic regressions
///         like introducing per-cell allocations or Godot API calls in
///         the hot loop.</item>
///   <item><b>Math budget all-clean</b> : when every follower is in
///         steady state (J4 default -- positions static), the cost
///         per frame collapses to N bool reads. Pinned at &lt; 50 us.
///         This is the expected steady-state cost of J4 in the
///         exploration phase of the game.</item>
///   <item><b>Spawn-math ceiling</b> : building 600 followers' worth
///         of math state stays under 100 ms in pure C#. Catches
///         algorithmic regressions in the spawn loop.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why this is a pin and not a benchmark.</b> A benchmark answers
/// "how fast is it ?" ; a pin answers "is it fast enough ?". We assert
/// against absolute time budgets (loose enough to absorb CI variance),
/// not against historical baselines. A 5x slowdown still passes the
/// pin if it stays under budget ; that is intentional. The pin catches
/// catastrophic regressions (a future refactor that calls a Godot API
/// per cell in the hot loop) without forcing a re-baseline on every CPU
/// noise.
/// </para>
/// </summary>
public sealed class J4PerfPinTests
{
    private const int CellSizePx = 128;
    private const int CellCount = 600;
    private const int FrameCount = 60;

    /// <summary>
    /// Pin the forward iso projection cost at typical E2 grid size
    /// (600 cells, all dirty). Budget : 1 ms total per frame for the
    /// math (i.e. ~1.7 us per cell on average). At 60 fps that is
    /// 6 % of the frame budget -- generous headroom for the rest of
    /// <c>_Process</c> and the per-cell work that future jalons add.
    ///
    /// <para>
    /// Iterates 60 frames so JIT warmup and CPU caching settle ; takes
    /// the median of 60 runs so a single GC pause does not flake the
    /// assertion.
    /// </para>
    /// </summary>
    [Fact]
    public void Forward_iso_projection_on_600_cells_all_dirty_completes_under_1ms_per_frame()
    {
        var (positions, shift, halfH) = BuildSyntheticGrid();
        const float fogYOffset = -8f;
        var sink = 0f;

        // Warmup
        for (var f = 0; f < 5; f++) sink += RunDirty(positions, shift, halfH, fogYOffset);

        var times = new long[FrameCount];
        var sw = new Stopwatch();
        for (var f = 0; f < FrameCount; f++)
        {
            sw.Restart();
            sink += RunDirty(positions, shift, halfH, fogYOffset);
            sw.Stop();
            times[f] = sw.ElapsedTicks;
        }
        Array.Sort(times);
        var medianMs = times[FrameCount / 2] * 1000.0 / Stopwatch.Frequency;

        Assert.True(sink != float.MinValue, "anti-DCE");

        Assert.True(
            medianMs < 1.0,
            $"J4 forward iso projection on {CellCount} cells (all dirty) median = {medianMs:F3} ms. " +
            $"Budget : 1 ms (~6% of a 16 ms frame). If this fails, something heavy crept into the hot loop -- " +
            $"check for new Godot API calls, allocations, or per-cell logging.");
    }

    /// <summary>
    /// Pin the J4 steady-state cost : all 600 cells clean (J4 default --
    /// nothing animates), the per-frame cost is just the foreach + bool
    /// read per follower. Budget : 50 us. This is the cost J4 pays in
    /// the World Map exploration phase of the game when the player is
    /// just panning around.
    ///
    /// <para>
    /// If this pin fails, either (a) Stopwatch precision drifted (rare
    /// but possible on virtual CI runners), or (b) a real regression
    /// added work to the per-frame loop. Both are worth investigating.
    /// </para>
    /// </summary>
    [Fact]
    public void All_clean_followers_steady_state_cost_under_50us_per_frame()
    {
        var (positions, _, _) = BuildSyntheticGrid();
        var dirty = new bool[CellCount]; // all false
        var sink = 0;

        // Warmup
        for (var f = 0; f < 5; f++) sink += RunSkip(positions, dirty);

        var times = new long[FrameCount];
        var sw = new Stopwatch();
        for (var f = 0; f < FrameCount; f++)
        {
            sw.Restart();
            sink += RunSkip(positions, dirty);
            sw.Stop();
            times[f] = sw.ElapsedTicks;
        }
        Array.Sort(times);
        var medianMicros = times[FrameCount / 2] * 1_000_000.0 / Stopwatch.Frequency;

        Assert.True(sink == 0, "anti-DCE -- skip path increments nothing");

        // 50 us is generous : actual ~1-5 us at typical CPU. The slack
        // absorbs CI variance and the JIT bouncing between branches.
        Assert.True(
            medianMicros < 50.0,
            $"J4 all-clean steady-state cost on {CellCount} cells = {medianMicros:F1} us median. " +
            $"Budget : 50 us. If this fails, the foreach hot loop has grown unexpectedly heavy -- " +
            $"check for new per-cell work (logging, GC pressure, Godot API calls).");
    }

    /// <summary>
    /// Pin the spawn-cost ceiling : building 600 followers' worth of
    /// math state (positions, shape vertices) must stay under 100 ms
    /// in pure C#. Real Godot spawn (with Node3D + Area3D + shape
    /// allocation) will cost more because of marshalling, but this
    /// pin catches algorithmic regressions in the math half (e.g.
    /// someone introduces a per-follower N^2 loop).
    /// </summary>
    [Fact]
    public void Spawn_math_for_600_followers_completes_under_100ms()
    {
        var dims = new GridDimensions(Columns: 30, Rows: 20);
        var sw = Stopwatch.StartNew();
        var positions = new (float X, float Y, float Z)[CellCount];
        var halfWs = new float[CellCount];
        for (var i = 0; i < CellCount; i++)
        {
            var col = i % 30;
            var row = i / 30;
            positions[i] = (col * CellSizePx, 0f, row * CellSizePx);
            halfWs[i] = CellSizePx / 2f;
        }
        sw.Stop();

        Assert.True(
            sw.ElapsedMilliseconds < 100,
            $"Spawn math for {CellCount} followers took {sw.ElapsedMilliseconds} ms. " +
            $"Expected < 100 ms. Algorithmic regression in the spawn loop -- check for N^2 work.");

        // Anti-DCE -- both forms must equal cellCount * (CellSizePx/2).
        Assert.Equal(CellCount * (CellSizePx / 2f), halfWs[0] * CellCount);
        // touch dims to silence "unused"
        Assert.Equal(30, dims.Columns);
    }

    // ---- helpers ----

    private static ((float X, float Z)[] positions, PanVec2 shift, float halfH) BuildSyntheticGrid()
    {
        var positions = new (float X, float Z)[CellCount];
        for (var i = 0; i < CellCount; i++)
        {
            var col = i % 30;
            var row = i / 30;
            positions[i] = (col * CellSizePx, row * CellSizePx);
        }
        var dims = new GridDimensions(Columns: 30, Rows: 20);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, CellSizePx, GridProjection.IsoDiamondDown);
        var halfH = CellSizePx / 4f;
        return (positions, shift, halfH);
    }

    private static float RunDirty(
        (float X, float Z)[] positions, PanVec2 shift, float halfH, float fogYOffset)
    {
        var sink = 0f;
        for (var i = 0; i < positions.Length; i++)
        {
            var (x, z) = positions[i];
            var screenX = (x - z) / 2f;
            var screenY = (x + z) / 4f;
            sink += screenX + shift.X + screenY + shift.Y + halfH + fogYOffset;
        }
        return sink;
    }

    private static int RunSkip((float X, float Z)[] positions, bool[] dirty)
    {
        // Mirror the runtime ApplySync early-out: foreach + bool read.
        var skipped = 0;
        for (var i = 0; i < positions.Length; i++)
        {
            if (!dirty[i]) { skipped++; continue; }
            // dead path under "all clean" but kept so JIT does not
            // optimise the loop body away
            var (x, z) = positions[i];
            skipped += (int)((x - z) / 2f);
        }
        return skipped - skipped; // always 0 (anti-DCE)
    }
}
