using System;
using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Data.Units;

namespace Wayfinders.Client.Components;

/// <summary>
/// Hit-point and condition state for one tactical unit. Sibling component
/// under a <c>Unit</c> parent — never extended, never inherited from. Behavior
/// lives here; data comes in via <see cref="Configure(UnitData)"/>.
///
/// <para>
/// <b>Wiring contract (Phase 2 banked, unchanged for L5).</b> The parent
/// <c>Unit</c> calls <see cref="Configure(UnitData)"/> in its <c>_EnterTree</c>,
/// before this component's <c>_Ready</c> runs. The named method (rather than a
/// public setter) keeps the wiring step grep-able at the call site. If
/// <see cref="Configure(UnitData)"/> is never called, <c>_Ready</c> NREs on
/// purpose — that is the fail-loud signal.
/// </para>
/// </summary>
public partial class HealthComponent : Node
{
    [Signal] public delegate void HealthChangedEventHandler(int newHp, int maxHp);
    [Signal] public delegate void DiedEventHandler();
    [Signal] public delegate void ConditionAddedEventHandler(string condition);
    [Signal] public delegate void ConditionRemovedEventHandler(string condition);

    private UnitData _unitData = null!;
    private int _currentHp;
    private readonly List<string> _conditions = new();

    public void Configure(UnitData data)
    {
        _unitData = data;
    }

    public override void _Ready()
    {
        // _unitData was set by the parent's _EnterTree. If it is still null,
        // the wiring contract was violated - NRE here is the intended fail-loud signal.
        _currentHp = _unitData.MaxHp;
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0) return;            // no-op: nothing to apply (also negative-amount guard)
        if (_currentHp <= 0) return;        // no-op: already dead, no further state change

        bool wasAlive = _currentHp > 0;
        _currentHp = Math.Max(_currentHp - amount, 0);   // clamp at zero per brief

        EmitSignalHealthChanged(_currentHp, _unitData.MaxHp);

        if (wasAlive && _currentHp <= 0)    // alive -> dead transition only
        {
            EmitSignalDied();
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;                        // no-op: nothing to heal
        if (_currentHp <= 0) return;                    // no-op: heal does not revive
        if (_currentHp >= _unitData.MaxHp) return;      // no-op: already full

        _currentHp = Math.Min(_currentHp + amount, _unitData.MaxHp);
        EmitSignalHealthChanged(_currentHp, _unitData.MaxHp);
    }

    public void AddCondition(string condition)
    {
        if (string.IsNullOrEmpty(condition)) return;    // no-op: bad input
        if (_conditions.Contains(condition)) return;    // no-op: already present

        _conditions.Add(condition);
        EmitSignalConditionAdded(condition);
    }

    public void RemoveCondition(string condition)
    {
        if (string.IsNullOrEmpty(condition)) return;    // no-op: bad input

        bool removed = _conditions.Remove(condition);
        if (!removed) return;                           // no-op: was not present

        EmitSignalConditionRemoved(condition);
    }
}
