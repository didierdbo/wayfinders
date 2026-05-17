using System.Collections.Generic;
using System.Threading;
using Godot;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Units;

namespace Wayfinders.Client.Scenes;

/// <summary>
/// Arc 1 ship-state scene. Replaces the L1-L4 verification scene
/// <c>HealthCheck</c>. On <c>_Ready</c> it fetches the unit roster from
/// <c>GET /api/units</c>, maps each <see cref="UnitDto"/> through
/// <see cref="UnitDtoMapper"/> into a <see cref="Wayfinders.Client.Data.Units.UnitData"/>,
/// instantiates a <see cref="Unit"/> for each, and renders a one-row
/// summary per unit in the on-screen list.
///
/// <para>
/// <b>End-to-end signal — Phase 3 closes here.</b> The path
/// <c>FastAPI -&gt; HttpClient -&gt; UnitDto -&gt; UnitDtoMapper -&gt;
/// UnitData -&gt; Unit -&gt; HealthComponent/MovementComponent</c> runs
/// every time this scene loads. Same components, same wiring contract,
/// same scene as Phase 2 — only the data source changed.
/// </para>
///
/// <para>
/// <b>Selection UI deferred.</b> The pre-brief mentions click-to-select
/// as an optional L5 feature. Held: it adds Input wiring, a
/// SelectionManager (autoload? sibling node? — nontrivial design call),
/// and 150+ lines of net-new surface. The roster ships read-only;
/// selection lights up alongside the first tactical scene where it
/// actually does something gameplay-meaningful.
/// </para>
///
/// <para>
/// <b>Two scene trees, one screen.</b>
/// <list type="bullet">
///   <item>
///     <c>UI</c> branch — the visible list of Labels in a
///     <c>VBoxContainer</c>. UI state.
///   </item>
///   <item>
///     <c>Units</c> branch — the spawned <see cref="Unit"/> nodes. They
///     are <see cref="Node"/> (not <see cref="Node2D"/>), so they have
///     no visible representation themselves. They hold simulation state
///     for damage, movement, stress — ready to be wired into a tactical
///     scene in Phase 5+. The UI labels and the unit nodes are
///     intentionally not coupled: a future portrait widget will read
///     from a unit node, the Label is just first-cut text.
///   </item>
/// </list>
/// </para>
/// </summary>
public partial class RosterScene : Control
{
    /// <summary>
    /// PackedScene reference to <c>Unit.tscn</c>. Wired through the
    /// editor's <c>[Export]</c> slot rather than <c>GD.Load</c> so a
    /// missing or renamed scene file fails in the inspector, not at
    /// runtime. The slot is filled in <c>RosterScene.tscn</c>.
    /// </summary>
    [Export] public PackedScene UnitScene { get; set; } = null!;

    private Label _statusLabel = null!;
    private VBoxContainer _unitList = null!;
    private Node _unitsContainer = null!;

    /// <summary>
    /// Screen-scoped <see cref="CancellationTokenSource"/> tied to this
    /// scene's lifetime. Cancelled and disposed in <c>_ExitTree</c> so an
    /// in-flight roster fetch is abandoned cleanly if the scene is
    /// unloaded mid-load (scene change, app exit, future router). Same
    /// discipline banked from L4 — <see cref="ApiClient"/>'s own
    /// shutdown token still fires on app exit; this token covers the
    /// scene-only-unloads case. Naming aligned on the Varn-locked
    /// canonical <c>_screenCts</c> 2026-05-17 Section C.
    /// </summary>
    private CancellationTokenSource? _screenCts;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("VBox/StatusLabel");
        _unitList = GetNode<VBoxContainer>("VBox/UnitList");
        _unitsContainer = GetNode<Node>("Units");

        _statusLabel.Text = "Roster: loading...";
        _screenCts = new CancellationTokenSource();

        // Fire-and-forget the async load. The method body itself owns
        // its lifetime via _screenCts and pattern-matches on Result -
        // no exception escapes for the caller to handle.
        _ = LoadRosterAsync(_screenCts.Token);
    }

    public override void _ExitTree()
    {
        _screenCts?.Cancel();
        _screenCts?.Dispose();
        _screenCts = null;
    }

    private async System.Threading.Tasks.Task LoadRosterAsync(CancellationToken ct)
    {
        // Reach the autoload via the scene-tree singleton path.
        var api = GetNode<ApiClient>("/root/ApiClient");

        var result = await api.GetUnitsAsync(ct);

        // === L4 main-thread hop ===
        // ApiClient uses ConfigureAwait(false), so the line above resumed
        // on a thread-pool thread. Hop back to main before touching scene
        // state. Awaiting ProcessFrame guarantees the continuation runs
        // on the main thread.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // === main-thread-safe below this line ===

        // The switch expression is exhaustive over the sealed Result hierarchy
        // AND the sealed ApiError hierarchy - TreatWarningsAsErrors makes a
        // missed variant a build break.
        switch (result)
        {
            case Result<IReadOnlyList<UnitDto>, ApiError>.Success { Value: var units }:
                RenderRoster(units);
                break;

            case Result<IReadOnlyList<UnitDto>, ApiError>.Failure { Error: ApiError.NotReachable na }:
                _statusLabel.Text = $"Roster: server unreachable ({na.Reason}).";
                break;

            case Result<IReadOnlyList<UnitDto>, ApiError>.Failure { Error: ApiError.ServerError se }:
                _statusLabel.Text = $"Roster: server returned {se.StatusCode}.";
                break;

            case Result<IReadOnlyList<UnitDto>, ApiError>.Failure { Error: ApiError.DeserializationError de }:
                _statusLabel.Text = $"Roster: could not parse response ({de.Reason}).";
                break;

            case Result<IReadOnlyList<UnitDto>, ApiError>.Failure { Error: ApiError.Cancelled }:
                // Scene exit hit before the roster came back. Nothing to
                // render - the scene is already on its way out. Avoid
                // touching nodes (they may be queued-free already).
                return;

            default:
                _statusLabel.Text = "Roster: unknown result shape.";
                break;
        }
    }

    /// <summary>
    /// Spawn-from-collection: iterate the server-provided DTOs, map each
    /// to a domain <see cref="Wayfinders.Client.Data.Units.UnitData"/>,
    /// instantiate a <see cref="Unit"/> per entry, configure it, and add
    /// it to the scene tree. Also append a one-line label for visible
    /// confirmation.
    ///
    /// <para>
    /// <b>Spawn ordering matters.</b> <c>Configure</c> is called BEFORE
    /// <c>AddChild</c>. If we added first, <c>_EnterTree</c> on the
    /// <see cref="Unit"/> would fire with a null <c>UnitData</c> and the
    /// component wiring would fail. The pattern is: instantiate detached,
    /// configure, attach.
    /// </para>
    /// </summary>
    private void RenderRoster(IReadOnlyList<UnitDto> units)
    {
        if (units.Count == 0)
        {
            _statusLabel.Text = "Roster: server returned an empty roster.";
            return;
        }

        _statusLabel.Text = $"Roster: {units.Count} unit(s) loaded.";

        for (int i = 0; i < units.Count; i++)
        {
            var dto = units[i];
            var unitData = UnitDtoMapper.ToUnitData(dto);

            // Spawn-time configuration path: Configure BEFORE AddChild.
            // See the Unit class doc for why ordering is load-bearing.
            var unit = UnitScene.Instantiate<Unit>();
            unit.Name = $"Unit_{i}_{unitData.DisplayName}";
            unit.Configure(unitData);
            _unitsContainer.AddChild(unit);

            // UI side: one row per unit, summarised.
            var row = new Label
            {
                Text = $"{unitData.DisplayName}  -  HP {unitData.MaxHp}  -  Speed {unitData.BaseSpeed}",
            };
            _unitList.AddChild(row);
        }
    }
}
