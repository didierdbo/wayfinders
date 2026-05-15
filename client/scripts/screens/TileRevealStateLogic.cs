using System;
using System.Collections.Generic;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Pure-C# state holder for the per-tile <c>reveal_level</c> map that drives
/// the boundary-fade shader on face-B Halfgate sprites (Varn spec
/// <c>varn-tile-reveal-shader-spec-2026-05-15.md</c>, locked 2026-05-15).
///
/// <para>
/// <b>Locks honored.</b>
/// <list type="bullet">
///   <item><b>§1</b> : <c>reveal_level</c> is a continuous <c>float</c>
///         clamped to <c>[0.0, 1.0]</c>, default <c>0.0</c>, per cell of
///         the 64×64 e1 grid. Continuum (not enum) so the same data
///         model carries a future partial-reveal system without
///         migration.</item>
///   <item><b>§2</b> : the boundary blend uses a CPU box-blur (~Gaussian
///         via <see cref="DefaultBlurPasses"/> passes) on the R8
///         reveal-map BEFORE upload. The shader then becomes a trivial
///         single texture lookup. This replaces the earlier shader-side
///         8-neighbour smoothstep composition (2026-05-15 a5dbd42 →
///         e99a1ce), which produced uneven feathering depending on each
///         tile's neighbourhood. See Larry mandate 2026-05-15 (CPU blur
///         rewrite).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Encoding choice.</b> The reveal map is encoded as a single-channel
/// R8 image of size <see cref="GridSize"/> × <see cref="GridSize"/> (64×64
/// for e1 = 4096 bytes total). After every write the runtime computes a
/// blurred snapshot via <see cref="ComputeBlurredRevealMap"/> and uploads
/// the 4 KB buffer via <c>ImageTexture.Update</c>. With 16 reveal events
/// during the cinematic and 3 box-blur passes per upload, total cost is
/// ~16 × 3 × 4096 = ~200k float operations — invisible.
/// </para>
///
/// <para>
/// <b>Why a Godot-free seam.</b> This file mirrors the pattern of
/// <see cref="FogTileGridLogic"/>, <see cref="CameraPanLogic"/>,
/// <see cref="MapPanInputLogic"/> : the runtime translates engine types
/// at the boundary, the math lives here, and xUnit pins the contract
/// without ever instantiating an engine. The blur operates on
/// <c>float[,]</c> buffers ; the runtime translates the resulting matrix
/// into R8 bytes pixel by pixel.
/// </para>
///
/// <para>
/// <b>Trap §8 — preflight logs delivered values.</b> The constants below
/// are referenced by both the shader (as uniform defaults) and the C#
/// runtime (as shader-parameter writes). If the two drift, the boundary
/// fade either gets too narrow (visible step) or so wide it bleeds into
/// neighbouring tiles' centres. The xUnit tests pin the locked values.
/// </para>
/// </summary>
public sealed class TileRevealStateLogic
{
    /// <summary>
    /// Side length of the reveal-map texture, in tiles. Locked to 64
    /// to match the e1 full-grid spawn in
    /// <c>IsoMapE1Probe.SpawnFullGrid</c>. Same constant on both sides
    /// of the seam : if the grid grows past 64×64, bump here AND in the
    /// shader's <c>texel</c> uniform default.
    /// </summary>
    public const int GridSize = 64;

    /// <summary>
    /// Default feather width as a fraction of tile width, locked at
    /// 30 % per Varn §2. KEPT for backwards-compat constant pinning even
    /// though the new shader (post-2026-05-15 CPU-blur rewrite) does not
    /// reference feather_width any more — the feathering is now baked
    /// into the R8 map by <see cref="ComputeBlurredRevealMap"/>. If a
    /// future shader rev reintroduces a feather uniform this constant
    /// stays the single source of truth.
    /// </summary>
    public const float DefaultFeatherWidth = 0.30f;

    /// <summary>
    /// Default number of 3×3 box-blur passes applied to the R8 reveal
    /// map before upload. Three passes of a unit-radius box approximate
    /// a Gaussian of σ ≈ √(passes × 1/3) ≈ 1.0 cell — enough spread to
    /// produce ~1 cell of feathering across the cluster boundary while
    /// keeping the centre of the 4×4 cluster at ~1.0. Bump to 5+ for
    /// softer halos ; drop to 1-2 for crisper edges. F5 A/B knob.
    /// </summary>
    public const int DefaultBlurPasses = 3;

    /// <summary>
    /// Lower bound of the reveal continuum. Below this, the shader
    /// produces alpha=0 at the tile centre. Same value on both sides
    /// of the seam.
    /// </summary>
    public const float RevealMin = 0.0f;

    /// <summary>
    /// Upper bound of the reveal continuum. At this, the shader
    /// produces alpha=1 at the tile centre. Same value on both sides
    /// of the seam.
    /// </summary>
    public const float RevealMax = 1.0f;

    private readonly float[,] _revealLevels;

    /// <summary>
    /// Construct a fresh state with every tile at <see cref="RevealMin"/>.
    /// </summary>
    public TileRevealStateLogic()
    {
        _revealLevels = new float[GridSize, GridSize];
        // C# default-initialises floats to 0.0f which matches RevealMin.
        // Explicit fill would be redundant ; the test fixture pins it.
    }

    /// <summary>
    /// Read the current reveal_level for cell <paramref name="gx"/>,
    /// <paramref name="gy"/>. Returns <see cref="RevealMin"/> for any
    /// out-of-bounds coordinate (the shader's neighbour-sample logic
    /// has the same out-of-bounds default ; tiles outside the world
    /// always read as unknown).
    /// </summary>
    public float GetTileRevealLevel(int gx, int gy)
    {
        if (gx < 0 || gx >= GridSize || gy < 0 || gy >= GridSize)
        {
            return RevealMin;
        }
        return _revealLevels[gx, gy];
    }

    /// <summary>
    /// Set a tile's reveal_level. Returns the byte that should be
    /// written into the R8 image's pixel <c>(gx, gy)</c> so the shader
    /// reads back the same float when it samples the texture (R8 packs
    /// the 0..1 range to 0..255 ; <see cref="EncodeRevealByte"/> is
    /// the same packing).
    ///
    /// <para>
    /// Throws <see cref="ArgumentOutOfRangeException"/> on out-of-bounds
    /// coordinates : a caller asking to set a tile outside the grid
    /// has a bug (vs. a reader, which can legitimately ask "what about
    /// past the edge ?"). Mirrors the asymmetry in
    /// <see cref="FogTileGridLogic.WorldPositionToCell"/>.
    /// </para>
    /// </summary>
    public byte SetTileRevealLevel(int gx, int gy, float value)
    {
        if (gx < 0 || gx >= GridSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gx), gx, $"gx must be in [0, {GridSize})");
        }
        if (gy < 0 || gy >= GridSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gy), gy, $"gy must be in [0, {GridSize})");
        }
        var clamped = ClampRevealLevel(value);
        _revealLevels[gx, gy] = clamped;
        return EncodeRevealByte(clamped);
    }

    /// <summary>
    /// Clamp a float into <c>[RevealMin, RevealMax]</c>. Centralised so
    /// the runtime, the tests, and any future caller agree on the
    /// clamp policy (saturating, not throwing — NaN and out-of-range
    /// values become legal endpoints rather than crashes).
    /// </summary>
    public static float ClampRevealLevel(float value)
    {
        if (float.IsNaN(value)) return RevealMin;
        if (value <= RevealMin) return RevealMin;
        if (value >= RevealMax) return RevealMax;
        return value;
    }

    /// <summary>
    /// Pack a clamped reveal_level into the R8 byte that the shader
    /// reads back via <c>texture(reveal_map, uv).r</c>. The packing is
    /// the canonical Godot R8 unorm encoding : <c>byte = round(value *
    /// 255)</c>. Rounding (not truncation) keeps the encode/decode
    /// round-trip symmetric ; truncation would bias every value
    /// slightly low and the cluster boundary would fade fractionally
    /// further in than the spec.
    /// </summary>
    public static byte EncodeRevealByte(float clampedValue)
    {
        // Defensive : if a caller passed in a non-clamped value, clamp
        // here too. Cheap, and catches a class of misuse silently.
        var v = ClampRevealLevel(clampedValue);
        var scaled = v * 255.0f;
        // MathF.Round defaults to ToEven (banker's rounding) ; for unsigned
        // pixel packing we want AwayFromZero so 0.5 → 128 not 0. The
        // R8 sample read back is (b / 255.0), so 128/255 ≈ 0.5019, close
        // enough to 0.5 for our shader's smoothstep tolerance.
        return (byte)MathF.Round(scaled, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Enumerate every cell of the reveal grid in row-major order
    /// (gy outer, gx inner). Used by the runtime at setup time to
    /// initialise the texture's pixel buffer in a single pass.
    /// </summary>
    public IEnumerable<(int gx, int gy)> EnumerateAllCells()
    {
        for (int gy = 0; gy < GridSize; gy++)
        {
            for (int gx = 0; gx < GridSize; gx++)
            {
                yield return (gx, gy);
            }
        }
    }

    /// <summary>
    /// Helper for the runtime : given a list of cells whose reveal
    /// becomes <see cref="RevealMax"/> in one batch (e.g. the 16 cells
    /// of the 4×4 e1 cluster at the flip midpoint), return the byte
    /// writes to apply, in order. Pure : no side effects, no state
    /// mutation. The caller can choose to either iterate this and call
    /// <see cref="SetTileRevealLevel"/> per cell (the simple path) or
    /// bulk-write directly into a raw pixel buffer. Both paths agree
    /// on encoding by construction.
    /// </summary>
    public static IEnumerable<(int gx, int gy, byte encodedByte)> ComputeBatchSetToMax(
        IEnumerable<(int gx, int gy)> cells)
    {
        if (cells is null) yield break;
        var encoded = EncodeRevealByte(RevealMax);
        foreach (var (gx, gy) in cells)
        {
            yield return (gx, gy, encoded);
        }
    }

    // ------------------------------------------------------------------------
    // CPU Gaussian-ish blur over the reveal map (Larry mandate 2026-05-15)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Apply <paramref name="passes"/> iterations of a 3×3 box blur to
    /// <paramref name="src"/> and return a fresh buffer holding the
    /// blurred result. N applications of a unit-radius box ≈ a
    /// Gaussian of σ ≈ √(N / 3) cells (central-limit). Three passes
    /// give σ ≈ 1, which produces ~1 cell of feathering at the cluster
    /// boundary — the visual target.
    ///
    /// <para>
    /// <b>Edge handling.</b> Out-of-bounds samples clamp to the
    /// boundary cell (CLAMP_TO_EDGE), NOT to zero. Reasoning : if the
    /// cluster ever touches the world edge (gx=0 or gx=63), clamp-to-
    /// zero would introduce a spurious dark border ; clamp-to-edge
    /// keeps the boundary value stable on its own side. For the
    /// current Wayfinders e1 case the cluster sits well inside [30,33]
    /// so both behaviours coincide ; the choice is for forward
    /// compatibility.
    /// </para>
    ///
    /// <para>
    /// <b>Ping-pong buffers.</b> Two <c>float[,]</c> buffers swap each
    /// pass to avoid in-place corruption. <paramref name="src"/> is
    /// not mutated. The returned buffer is freshly allocated even when
    /// <paramref name="passes"/> = 0 (defensive copy so the caller can
    /// not subsequently mutate the input and see the output change).
    /// </para>
    ///
    /// <para>
    /// <b>Cost.</b> A 64×64 grid × 3 passes × 9 samples per pixel =
    /// ~110k FLOPs. The cinematic triggers 16 reveal events ; total
    /// cost ~1.8M FLOPs over ~2 seconds — well below the 16 ms frame
    /// budget for any single event.
    /// </para>
    /// </summary>
    public static float[,] BlurRevealMap(float[,] src, int passes = DefaultBlurPasses)
    {
        if (src is null) throw new ArgumentNullException(nameof(src));
        int w = src.GetLength(0);
        int h = src.GetLength(1);

        // Defensive : 0 passes still returns a copy (not the original
        // reference) — the contract is "this method returns a fresh
        // buffer". A caller who wants the original can just keep their
        // own reference.
        var a = new float[w, h];
        Array.Copy(src, a, src.Length);
        if (passes <= 0) return a;

        var b = new float[w, h];

        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < h; y++)
            {
                int ym1 = y > 0 ? y - 1 : 0;          // clamp-to-edge
                int yp1 = y < h - 1 ? y + 1 : h - 1;
                for (int x = 0; x < w; x++)
                {
                    int xm1 = x > 0 ? x - 1 : 0;
                    int xp1 = x < w - 1 ? x + 1 : w - 1;

                    float sum = a[xm1, ym1] + a[x, ym1] + a[xp1, ym1]
                              + a[xm1, y  ] + a[x, y  ] + a[xp1, y  ]
                              + a[xm1, yp1] + a[x, yp1] + a[xp1, yp1];
                    b[x, y] = sum * (1.0f / 9.0f);
                }
            }
            // Swap : next pass reads from b, writes back into a.
            (a, b) = (b, a);
        }
        // After the final swap, the most recent result lives in a.
        return a;
    }

    /// <summary>
    /// Return a fresh <c>float[GridSize, GridSize]</c> holding the raw
    /// reveal-level matrix. Useful for tests and for the runtime to
    /// feed into <see cref="BlurRevealMap"/>. Defensive copy : caller
    /// mutations do not affect internal state.
    /// </summary>
    public float[,] SnapshotRevealMap()
    {
        var snap = new float[GridSize, GridSize];
        Array.Copy(_revealLevels, snap, _revealLevels.Length);
        return snap;
    }

    /// <summary>
    /// Convenience wrapper : snapshot the current raw reveal map and
    /// run <see cref="BlurRevealMap"/> on it with the supplied pass
    /// count (default <see cref="DefaultBlurPasses"/>). The runtime
    /// calls this after every reveal-event write and uploads the
    /// resulting buffer into the R8 ImageTexture pixel by pixel.
    /// </summary>
    public float[,] ComputeBlurredRevealMap(int passes = DefaultBlurPasses)
    {
        return BlurRevealMap(SnapshotRevealMap(), passes);
    }
}
