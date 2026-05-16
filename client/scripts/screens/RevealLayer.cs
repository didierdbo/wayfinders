namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Varn-locked closed lookup for the layer at which a reveal-substrate
/// transition is expressed (AMENDMENT A4.7, 2026-05-16).
///
/// <para>
/// <b>Locked under A4.7.4.</b> The enum string set <c>[e1, e2, e3]</c> is
/// the canonical authoring vocabulary (mission YAML, NPC YAML, Resource
/// schemas). The integer values below are persistence + signal-wire
/// integers — do not renumber.
/// </para>
///
/// <para>
/// <b>Why an enum and not a string.</b> The auto-projection cascade
/// (R3 top-down + R4 bottom-up) walks layers by relative offset
/// (L+1, L-1). The enum's integer ordering encodes the layer ladder
/// directly so the cascade math is a simple <c>(int)layer + 1</c> /
/// <c>(int)layer - 1</c> without a string switch. A4.7.8 anti-drift
/// anchor : these integer values are part of the persistence contract
/// once saves land in M2.
/// </para>
///
/// <para>
/// <b>Scope at A4.7 lock.</b> E1 and E2 are wired ; E3 is reserved
/// (placeholder ratio in <see cref="LayerRatioMatrix"/>). The MVE
/// exercises only the E1 → E2 top-down path ; E2 → E1 bottom-up
/// aggregation and any E3 path are post-MVE.
/// </para>
/// </summary>
public enum RevealLayer
{
    /// <summary>
    /// World monde scale. The E1WorldMap + IsoMapE1Probe cinematic
    /// canvas. 24×24 cells at MVE scale (Varn POC scope).
    /// </summary>
    E1 = 0,

    /// <summary>
    /// Intra-area scale (zoom ×2 from E1). The E2AreaMap 8×8 grid for
    /// the Halfgate POC area. 1 E1 cell = 2×2 E2 cells (4 cells) per
    /// <see cref="LayerRatioMatrix.E1ToE2"/>.
    /// </summary>
    E2 = 1,

    /// <summary>
    /// Intra-quartier scale (zoom ×2 from E2, placeholder). Reserved
    /// for post-MVE. The ratio in <see cref="LayerRatioMatrix.E2ToE3"/>
    /// is a placeholder ; the actual rule lands when E3 enters scope.
    /// </summary>
    E3 = 2,
}
