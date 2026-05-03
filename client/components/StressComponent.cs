using Godot;

namespace Wayfinders.Client.Components;

/// <summary>
/// Sibling listener for cross-cutting reactions to health changes. Demonstrates
/// the cascade pattern banked at Phase 2 B3: <see cref="HealthComponent"/>
/// fires <c>HealthChanged</c>, <see cref="StressComponent"/> decides whether
/// stress applies. <see cref="HealthComponent"/> never says the word "stress."
///
/// <para>
/// Stub today. No caller surface exists yet (no UI listener, no AI listener),
/// so <c>StressChanged</c> is declared but not wired further. Mutation rules
/// land here when a Wayfinders gameplay reason shows up.
/// </para>
///
/// <para>
/// <b>Disconnection discipline (Phase 2 banked).</b> The <c>+=</c> in
/// <c>_Ready</c> is paired with a <c>-=</c> in <c>_ExitTree</c>. If the
/// publisher outlives this listener, a stale delegate would otherwise be
/// invoked on a freed node.
/// </para>
/// </summary>
public partial class StressComponent : Node
{
    [Signal] public delegate void StressChangedEventHandler(int newStress);

    private int _stress;
    private HealthComponent? _health;

    public override void _Ready()
    {
        // Sibling lookup: parent (Unit) holds both components as children.
        // Naming convention: child node named after the type ("HealthComponent").
        _health = GetParent().GetNodeOrNull<HealthComponent>(nameof(HealthComponent));
        if (_health == null) return;        // no health sibling, nothing to listen to

        _health.HealthChanged += OnHealthChanged;
    }

    public override void _ExitTree()
    {
        if (_health != null)
        {
            _health.HealthChanged -= OnHealthChanged;
        }
    }

    private void OnHealthChanged(int newHp, int maxHp)
    {
        // Decision point lives here, NOT inside HealthComponent.
        // When a Wayfinders gameplay rule lands (e.g. "stress bumps when HP drops below 25%"),
        // implement here, mutate `_stress`, EmitSignalStressChanged(_stress).
        // For now: stub - no caller exists yet (Decision 3 YAGNI hold from Phase 2 B3).
    }
}
