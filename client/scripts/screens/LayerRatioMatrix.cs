using System;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Varn-locked closed lookup of integer zoom ratios between consecutive
/// reveal-substrate layers (AMENDMENT A4.7.4, 2026-05-16).
///
/// <para>
/// <b>Locked values (A4.7.8 anti-drift anchor).</b>
/// <list type="bullet">
///   <item><c>e1_to_e2 = 2</c> — 1 E1 cell ↔ 2×2 E2 cells = 4 cells.</item>
///   <item><c>e2_to_e3 = 2</c> — placeholder, refined post-POC.</item>
///   <item>Composition E1 → E3 = 2 × 2 = 4 (1 E1 cell = 4×4 E3 cells = 16).</item>
/// </list>
/// Changing these values requires a Varn pass per
/// <c>feedback_varn_locked_specs</c>.
/// </para>
///
/// <para>
/// <b>Why a static class.</b> The matrix is a closed lookup with three
/// scalars (post-POC there will be a fourth, e3_to_e4 if the design
/// ever recurses further). A static class beats a <c>Dictionary&lt;,&gt;</c>
/// here : compile-time constant access, no allocation, xUnit pin via
/// direct field reference. Same shape as <see cref="AreaGridLogic"/>.
/// </para>
///
/// <para>
/// <b>Linear vs. area factor.</b> The ratios stored here are the
/// <b>linear</b> integer factor (cell-coord scale). The <b>area</b>
/// factor is the square (1 E1 cell → 4 E2 cells, 1 E1 cell → 16 E3
/// cells). Top-down projection (<see cref="TileRevealProjector"/>)
/// enumerates ratio × ratio children per parent cell.
/// </para>
/// </summary>
public static class LayerRatioMatrix
{
    /// <summary>
    /// Linear ratio from E1 to E2. 1 E1 cell maps to 2×2 = 4 E2 cells.
    /// Locked under A4.7.1 Q2 (Varn lock 2026-05-16).
    /// </summary>
    public const int E1ToE2 = 2;

    /// <summary>
    /// Linear ratio from E2 to E3. Placeholder = 2 ; the actual rule
    /// lands when E3 enters scope. Reserved post-POC.
    /// </summary>
    public const int E2ToE3 = 2;

    /// <summary>
    /// Resolve the linear ratio between two consecutive layers in the
    /// top-down direction (parent → child). Returns the integer factor
    /// applied to each axis of the parent cell coord to obtain the
    /// child block range.
    ///
    /// <para>
    /// Throws <see cref="ArgumentException"/> when the requested step
    /// is not a one-layer-down move (e.g. E1 → E3 directly). The
    /// projector composes single-step ratios for multi-layer cascades
    /// (R3 calls itself recursively), so the per-step lookup is the
    /// canonical shape.
    /// </para>
    /// </summary>
    public static int ResolveRatio(RevealLayer from, RevealLayer to)
    {
        if (from == RevealLayer.E1 && to == RevealLayer.E2) return E1ToE2;
        if (from == RevealLayer.E2 && to == RevealLayer.E3) return E2ToE3;
        throw new ArgumentException(
            $"[LayerRatioMatrix] No single-step ratio for {from} -> {to}. " +
            $"Only consecutive top-down steps (E1->E2, E2->E3) are defined. " +
            $"For multi-layer cascades, walk one step at a time.",
            nameof(to));
    }

    /// <summary>
    /// True if <paramref name="to"/> is the immediate child layer of
    /// <paramref name="from"/>. Centralised so the projector and any
    /// future caller use the same definition of "one step deeper".
    /// </summary>
    public static bool IsImmediateChild(RevealLayer from, RevealLayer to) =>
        (from == RevealLayer.E1 && to == RevealLayer.E2)
        || (from == RevealLayer.E2 && to == RevealLayer.E3);

    /// <summary>
    /// True if <paramref name="to"/> is the immediate parent layer of
    /// <paramref name="from"/>. Used by R4 bottom-up aggregation (post-MVE).
    /// </summary>
    public static bool IsImmediateParent(RevealLayer from, RevealLayer to) =>
        (from == RevealLayer.E2 && to == RevealLayer.E1)
        || (from == RevealLayer.E3 && to == RevealLayer.E2);
}
