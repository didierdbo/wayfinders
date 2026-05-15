using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Poi;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.scenes.probes;

/// <summary>
/// PR3 probe — verify that <see cref="PoiSpawner.SpawnAt"/> places a POI
/// Sprite2D on its iso tile with the anchor pixel aligned to the tile
/// center (i.e. the foot of the sprite "stands" on the tile).
/// PR4 extension — verify that <see cref="PoiInputRouter"/> cascades hover
/// + click events with pixel-perfect bitmap hit-test and Y-sort descending.
///
/// <para>
/// <b>Run.</b> F6 on <c>PoiSpawnProbe.tscn</c>. Expected output panel :
/// <code>
/// [POI ROUTER] registered, autoload OK, signals declared ...
/// [POI SPAWN PROBE] _Ready cellSize=128 gridDims=(24,24) centerTile=(12,12)
/// [POI SPAWNER] _Ready poiScene=loaded cellSize=128px gridDims=(24,24)
/// [POI SPAWNER] SpawnAt Halfgate tile=(12,12) worldPos=(...) parent=Node2D/WorldRoot
/// [POI SPAWN] Halfgate ... anchorAlignedOK
/// [POI Halfgate] registered with router
/// [POI SPAWNER] SpawnAt HalfgateClone tile=(12,13) ...
/// [POI SPAWN] HalfgateClone ... anchorAlignedOK
/// [POI HalfgateClone] registered with router
/// </code>
/// Visually : Halfgate sprite + a translucent clone one tile south. The
/// HUD label in the top-left shows "POI: -" by default and flips to
/// "POI hover: Halfgate" / "POI click: Halfgate" as the cursor moves.
/// When the cursor sits on the row where Halfgate and HalfgateClone
/// overlap, the front (larger Y) wins.
/// </para>
///
/// <para>
/// <b>Validation matrix (Didier at F6).</b>
/// <list type="bullet">
///   <item>Hover the pillar (opaque) → label "POI hover: Halfgate".</item>
///   <item>Hover the AABB corner (transparent) → label "POI: -" (miss,
///         fallthrough natural).</item>
///   <item>Hover outside the AABB → label stays "POI: -".</item>
///   <item>Left-click on the pillar → console "[PROBE] Click on Halfgate".</item>
///   <item>Hold middle button and drag → no hover updates fire while
///         dragging (trap §5 ergonomic skip).</item>
///   <item>Cursor at the intersection of Halfgate and HalfgateClone →
///         HalfgateClone wins (placed one row south, larger world Y).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Scratch.</b> Slated for deletion in PR6, kept committed for review
/// continuity (same pattern as <c>PoiLoadProbe</c> / <c>PoiFootprintProbe</c>).
/// </para>
/// </summary>
public partial class PoiSpawnProbe : Node2D
{
    [Export] public NodePath SpawnerPath { get; set; } = new("PoiSpawner");
    [Export] public NodePath WorldRootPath { get; set; } = new("WorldRoot");
    [Export] public NodePath WorldCameraPath { get; set; } = new("WorldRoot/WorldCamera");
    [Export] public NodePath HudLabelPath { get; set; } = new("Hud/Label");

    private Label? _hudLabel;
    private PoiInputRouter? _router;

    public override void _Ready()
    {
        var spawner = GetNode<PoiSpawner>(SpawnerPath);
        var worldRoot = GetNode<Node2D>(WorldRootPath);
        var camera = GetNode<Camera2D>(WorldCameraPath);
        _hudLabel = GetNodeOrNull<Label>(HudLabelPath);

        // Center the spawn on the grid for visual symmetry.
        var centerTile = new Vector2I(
            spawner.GridDimensions.X / 2,
            spawner.GridDimensions.Y / 2);

        GD.Print(
            $"[POI SPAWN PROBE] _Ready " +
            $"cellSize={spawner.CellSizePx} " +
            $"gridDims=({spawner.GridDimensions.X},{spawner.GridDimensions.Y}) " +
            $"centerTile=({centerTile.X},{centerTile.Y})");

        // Load Halfgate via the PR1+PR2 sidecar loader.
        var data = PoiSidecarLoader.Load("res://assets/poi/e1/wf_e1_halfgate_poi.png");

        // Spawn at the center tile. The Poi instance's _Ready will log the
        // delivered values (anchor, offset, parent type, etc.) and the
        // PR4 hook will register it with the input router.
        var poi = spawner.SpawnAt(data, centerTile, worldRoot);

        // PR4 — spawn a second POI one tile south so the Y-sort tiebreaker
        // is observable. Reuses the same texture/sidecar (cheap clone) and
        // displays semi-transparent so Halfgate stays visually dominant.
        var cloneTile = new Vector2I(centerTile.X, centerTile.Y + 1);
        var cloneData = PoiSidecarLoader.Load("res://assets/poi/e1/wf_e1_halfgate_poi.png");
        cloneData.DisplayName = "HalfgateClone";
        var clone = spawner.SpawnAt(cloneData, cloneTile, worldRoot);
        clone.Modulate = new Color(1, 1, 1, 0.5f);

        // PR4 — wire the router signals to the HUD label and console log.
        _router = GetNodeOrNull<PoiInputRouter>("/root/PoiInputRouter");
        if (_router != null)
        {
            _router.PoiHovered += OnPoiHovered;
            _router.PoiClicked += OnPoiClicked;
        }
        else
        {
            GD.PrintErr("[POI SPAWN PROBE] PoiInputRouter autoload NOT found at /root");
        }

        // Frame the camera on the spawn so visual verification is one F6
        // away. We point at the POI's tile center in world coords — same
        // math the spawner used, deliberately re-derived here so the probe
        // does not depend on Poi.Position being already-set at this frame.
        var dims = new GridDimensions(spawner.GridDimensions.X, spawner.GridDimensions.Y);
        var coord = new GridCoord(centerTile.X, centerTile.Y);
        var cellCenter = FogTileGridLogic.ComputeCellCenter(
            coord, spawner.CellSizePx, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, spawner.CellSizePx, GridProjection.IsoDiamondDown);
        camera.Position = new Vector2(cellCenter.X + shift.X, cellCenter.Y + shift.Y);

        GD.Print(
            $"[POI SPAWN PROBE] camera centered at " +
            $"({camera.Position.X:F1},{camera.Position.Y:F1}) " +
            $"poiPos=({poi.Position.X:F1},{poi.Position.Y:F1}) " +
            $"clonePos=({clone.Position.X:F1},{clone.Position.Y:F1})");

        if (_hudLabel != null)
        {
            _hudLabel.Text = "POI: -";
        }
    }

    public override void _ExitTree()
    {
        // Disconnection discipline (methodology trap #10) — the probe is
        // scene-local, the router is autoload (lives forever). If we do
        // not detach the subscribers, the next probe instance double-fires.
        if (_router != null)
        {
            _router.PoiHovered -= OnPoiHovered;
            _router.PoiClicked -= OnPoiClicked;
        }
    }

    private void OnPoiHovered(string displayName)
    {
        if (_hudLabel != null)
        {
            _hudLabel.Text = $"POI hover: {displayName}";
        }
    }

    private void OnPoiClicked(string displayName)
    {
        if (_hudLabel != null)
        {
            _hudLabel.Text = $"POI click: {displayName}";
        }
        GD.Print($"[PROBE] Click on {displayName}");
    }
}
