using Godot;

namespace Wayfinders.Client.Services;

/// <summary>
/// P7 world-simulation tick autoload — placeholder for the
/// background-NPC behavior tick that drives the "vivant" feel of cities
/// and districts.
///
/// <para>
/// <b>J1 status: noop.</b> Slot reserved per Varn rule (pre-brief §2.4).
/// No <c>_Process(delta)</c> body in J1 — adding it would burn a per-frame
/// budget for nothing. Will be flipped on at jalon 4 (E3 city) when the
/// first NPC behavior state lands.
/// </para>
///
/// <para>
/// Implementation lands at jalon 4. Will expose:
/// <list type="bullet">
///   <item><c>TickInterval</c> Export (default 250 ms — slow tick by design, NPCs are not combat units).</item>
///   <item><c>Tick</c> signal that NPC behavior nodes subscribe to.</item>
/// </list>
/// </para>
/// </summary>
public partial class WorldSimTick : Node
{
    public override void _Ready()
    {
        GD.Print("[WorldSimTick] ready (J1 noop hook)");
    }
}
