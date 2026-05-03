using Godot;
using Wayfinders.Client.Components;
using Wayfinders.Client.Data.Units;

namespace Wayfinders.Client.Units;

/// <summary>
/// Assembly point for a tactical actor. Owns the <see cref="UnitData"/> reference
/// and wires it onto its component children. Does NOT own behavior:
/// damage lives in <see cref="HealthComponent"/>, movement in
/// <see cref="MovementComponent"/>, stress in <see cref="StressComponent"/>.
/// <see cref="Unit"/> is a thin coordinator, not a god-object.
///
/// <para>
/// <b>Root type is <see cref="Node"/> (not <see cref="Node2D"/>) on purpose.</b>
/// The simulation-vs-rendering split says cell coordinates live on
/// <see cref="MovementComponent"/> (<see cref="Vector2I"/>), pixel positions
/// belong to a future renderer that attaches as a child. Making the logical
/// <see cref="Unit"/> a <see cref="Node"/> keeps that split honest — we do
/// not pre-bake a <see cref="Vector2"/> Position into the actor's identity.
/// </para>
///
/// <para>
/// <b>Two configuration paths (L5).</b>
/// <list type="number">
///   <item>
///   <b>Editor / <c>[Export]</c> path (Phase 2 sandbox shape):</b>
///   drop a <c>.tres</c> on the inspector slot, the field is non-null
///   when <c>_EnterTree</c> runs, components are wired automatically.
///   Not used in the public <c>wayfinders</c> repo today (no <c>.tres</c>
///   files exist), but the slot is preserved for future tests, modding,
///   or saved-game restoration.
///   </item>
///   <item>
///   <b>Spawn-time path (L5 production):</b> caller instantiates the
///   scene via <c>PackedScene.Instantiate&lt;Unit&gt;()</c>, calls
///   <see cref="Configure(UnitData)"/> while the unit is still detached,
///   then <c>AddChild(unit)</c>. Order is critical:
///   <c>Configure</c> MUST happen before <c>AddChild</c>, because
///   <c>AddChild</c> fires <c>_EnterTree</c> top-down and the parent
///   forwards <c>Configure</c> to the children there. If you forget,
///   the child's <c>_Ready</c> fires with a null <see cref="UnitData"/>
///   and NREs — that is the fail-loud signal.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Lifecycle note.</b> <c>_EnterTree</c> runs top-down (parent before
/// children); <c>_Ready</c> runs bottom-up (children before parent). The
/// components read <see cref="UnitData"/> in their <c>_Ready</c>, so wiring
/// MUST happen before that — i.e. in <c>_EnterTree</c>, not <c>_Ready</c>.
/// Wiring in <c>_Ready</c> would NRE because the children's <c>_Ready</c>
/// already ran.
/// </para>
/// </summary>
public partial class Unit : Node
{
    /// <summary>
    /// Domain shape this unit was configured with. Set by either path
    /// described in the type doc: <c>[Export]</c> in the editor, or
    /// <see cref="Configure(UnitData)"/> at spawn time.
    /// </summary>
    [Export] public UnitData? UnitData { get; set; }

    /// <summary>
    /// Spawn-time configuration. Set the data while the unit is still
    /// detached, then call <c>AddChild(unit)</c>. The <see cref="UnitData"/>
    /// field is then non-null when <c>_EnterTree</c> runs and the
    /// component-wiring path executes the same way as the editor path.
    /// </summary>
    public void Configure(UnitData data)
    {
        UnitData = data;
    }

    public override void _EnterTree()
    {
        if (UnitData == null)
        {
            // Fail-loud: a Unit instance with no UnitData is a bug.
            // Better to crash with a clear message than to NRE one frame
            // later inside a component's _Ready.
            GD.PushError(
                $"Unit '{Name}' has no UnitData. Call Configure(UnitData) before AddChild, " +
                "or assign the [Export] slot in the Inspector.");
            return;
        }

        // Sibling lookup by node name. The .tscn names children after their type
        // (HealthComponent, MovementComponent, StressComponent) - same convention
        // StressComponent uses to find HealthComponent. Node names are the API.
        GetNode<HealthComponent>(nameof(HealthComponent)).Configure(UnitData);
        GetNode<MovementComponent>(nameof(MovementComponent)).Configure(UnitData);
        // StressComponent has no UnitData dependency - it is a sibling listener,
        // not a data consumer. Nothing to wire here. (Phase 2 B3 lesson:
        // not every component takes data.)
    }
}
