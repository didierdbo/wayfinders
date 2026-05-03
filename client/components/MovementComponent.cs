using Godot;
using Wayfinders.Client.Data.Units;

namespace Wayfinders.Client.Components;

/// <summary>
/// Grid movement state for one tactical unit. Sibling to <see cref="HealthComponent"/>
/// under a <c>Unit</c> parent. Mirrors the same shape: data lives on
/// <see cref="UnitData"/> (BaseSpeed), behavior lives here. No shared base
/// class, no <c>IMovable</c> interface — the components are siblings under a
/// <c>Unit</c> parent, not branches of a hierarchy.
///
/// <para>
/// <b>Grid coordinate convention.</b> <see cref="Vector2I"/> = simulation
/// grid cell (integer cell coords, what tactical rules reason about).
/// <see cref="Vector2"/> = pixel position (what the renderer draws). This
/// component speaks only in cells. Phase 5 will introduce a
/// <c>TileMapLayer</c> that translates cells to pixels for display; the
/// simulation grid stays integer-only.
/// </para>
/// </summary>
public partial class MovementComponent : Node
{
    [Signal] public delegate void MovedEventHandler(Vector2I from, Vector2I to);

    private UnitData _unitData = null!;
    private int _baseSpeed;
    private Vector2I _currentCell;

    public Vector2I CurrentCell => _currentCell;
    public int BaseSpeed => _baseSpeed;

    public void Configure(UnitData data)
    {
        _unitData = data;
    }

    public override void _Ready()
    {
        _baseSpeed = _unitData.BaseSpeed;
    }

    // Phase 5+ will replace this body with AStarGrid2D pathfinding, terrain costs, and
    // zone-of-control checks. For now: teleport-and-announce. The contract (signature +
    // signal) is what callers and listeners couple to, and that contract does not
    // change when the body gets smart.
    public void MoveTo(Vector2I targetCell)
    {
        if (targetCell == _currentCell) return;     // no-op: already there

        Vector2I from = _currentCell;
        _currentCell = targetCell;
        EmitSignalMoved(from, _currentCell);
    }
}
