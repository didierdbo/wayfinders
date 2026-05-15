using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Poi;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.scenes.probes;

/// <summary>
/// PR3 probe — verify that <see cref="PoiSpawner.SpawnAt"/> places a POI
/// Sprite2D on its iso tile with the anchor pixel aligned to the tile
/// center (i.e. the foot of the sprite "stands" on the tile).
///
/// <para>
/// <b>Run.</b> F6 on <c>PoiSpawnProbe.tscn</c>. Expected output panel :
/// <code>
/// [POI SPAWN PROBE] _Ready cellSize=128 gridDims=(24,24) centerTile=(12,12)
/// [POI SPAWNER] _Ready poiScene=loaded cellSize=128px gridDims=(24,24)
/// [POI SPAWNER] SpawnAt Halfgate tile=(12,12) worldPos=(...) parent=Node2D/WorldRoot
/// [POI SPAWN] Halfgate at tile=(set by spawner via Position) ... anchorAlignedOK
/// </code>
/// Visually : Halfgate sprite visible, foot anchored on the tile centered
/// in the viewport ; not centered on the sprite's bounding box. The camera
/// is recentered at runtime on the spawn tile so the result is framed
/// regardless of the iso shift values.
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

    public override void _Ready()
    {
        var spawner = GetNode<PoiSpawner>(SpawnerPath);
        var worldRoot = GetNode<Node2D>(WorldRootPath);
        var camera = GetNode<Camera2D>(WorldCameraPath);

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
        // delivered values (anchor, offset, parent type, etc.).
        var poi = spawner.SpawnAt(data, centerTile, worldRoot);

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
            $"poiPos=({poi.Position.X:F1},{poi.Position.Y:F1})");
    }
}
