using Godot;

namespace Wayfinders.Client.Scenes.Scratch.E2Stub;

/// <summary>
/// E2Stub — placeholder destination scene for the e1 D POI click transition.
///
/// <para>
/// <b>Locked 2026-05-13 (e1 D).</b> The POI Halfgate click handler in
/// <c>IsoMapE1Probe</c> calls
/// <c>GetTree().ChangeSceneToFile("res://scenes/scratch/E2Stub/E2Stub.tscn")</c>
/// after a 1.0s zoom-in (current zoom → 2.5×) and a 0.4s crossfade overlay.
/// This stub exists only to prove the transition mechanic — the real e2
/// Halfgate scene lives elsewhere (out-of-POC e1).
/// </para>
///
/// <para>
/// <b>Visual contract.</b> Magenta background (Wayfinders preflight canary
/// color, same as IsoMapE1Probe BG) + centered Label
/// <c>"E2 Halfgate Stub - Placeholder Scene"</c> in white sans, large enough
/// to read at native resolution. Nothing else. If you see this scene, the
/// transition fired and the scene was successfully swapped in by Godot.
/// </para>
/// </summary>
public partial class E2Stub : Node2D
{
    public override void _Ready()
    {
        GD.Print("[PROBE E2Stub] scene started — e2 placeholder reached, transition from e1 D POI click confirmed");
    }
}
