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
///   <item><b>§2</b> : the boundary blend uses smoothstep with feather
///         width = 30 % of tile width. The feather constant is exposed
///         to the shader as a uniform ; the logic layer keeps the
///         default value as a constant for documentation but does not
///         enforce it (the shader is the source of truth at render
///         time).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Encoding choice.</b> The reveal map is encoded as a single-channel
/// R8 image of size <see cref="GridSize"/> × <see cref="GridSize"/> (64×64
/// for e1 = 4096 bytes total). The shader samples this texture instead
/// of receiving a per-tile uniform array, because at scale (e.g. a 64×64
/// or larger grid) per-frame uniform-array writes through marshalling are
/// expensive while one image upload on rare reveal events is essentially
/// free. The fragment then reads self + 8 neighbours by texture lookups
/// at UV offsets of <c>1/GridSize</c>. See Larry mandate 2026-05-15 for
/// the option-(b) rationale.
/// </para>
///
/// <para>
/// <b>Why a Godot-free seam.</b> This file mirrors the pattern of
/// <see cref="FogTileGridLogic"/>, <see cref="CameraPanLogic"/>,
/// <see cref="MapPanInputLogic"/> : the runtime translates engine types
/// at the boundary, the math lives here, and xUnit pins the contract
/// without ever instantiating an engine. <c>Image.SetPixel</c> would
/// otherwise drag a Godot dependency into a logic layer that has none.
/// The logic returns "what byte to write where" ; the runtime applies it.
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
    /// 30 % per Varn §2. The shader carries this as a tunable uniform ;
    /// this constant is the spec-locked default that the runtime
    /// writes into the uniform at material setup.
    /// </summary>
    public const float DefaultFeatherWidth = 0.30f;

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
}
