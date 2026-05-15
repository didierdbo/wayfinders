using Godot;
using Wayfinders.Client.Data;
using Wayfinders.Client.Scripts.Screens;
using Vector2 = Godot.Vector2;

namespace Wayfinders.Client.Scripts.Poi;

/// <summary>
/// PR3 — POI scene instantiator.
///
/// <para>
/// <b>Node, not autoload (locked 2026-05-15, prebrief §5#2).</b> The spawner
/// is a plain <see cref="Node"/> placed under whatever world root owns the
/// scene tree of the slice (probe, M1 slice, ...). Reasons :
/// <list type="bullet">
///   <item>Scene-local lifetime — autoload would leak the spawner across
///         scene changes, which is exactly the bug we already hit on
///         <c>IsoMapE1Probe</c> (trap §1bis, autoload-injected
///         <c>CanvasLayer</c>).</item>
///   <item>No global state — the spawner is stateless ; every call to
///         <see cref="SpawnAt"/> is independent. No reason to hoist it
///         to the autoload tier.</item>
///   <item>Testable seam — wire-up at the call site, not implicitly via
///         <c>GetNode("/root/PoiSpawner")</c>. Coda's lane convention.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>API.</b> <see cref="SpawnAt"/> is the single entry point. The caller
/// owns the <see cref="PoiData"/> (loaded via <c>PoiSidecarLoader</c>) and
/// the iso tile coord ; the spawner does the scene instantiation +
/// data-injection + tile→world projection + <c>AddChild</c>. The returned
/// node is a <see cref="Poi"/> Sprite2D ready for further wiring. PR5
/// adds an optional <paramref name="parallaxCamera"/> argument so the
/// spawned POI knows which camera to read for the parallax pan ; passing
/// <c>null</c> keeps the POI parallax-disabled regardless of its
/// <c>ParallaxStrength</c> export.
/// </para>
///
/// <para>
/// <b>Tile→world projection.</b> Delegates to <c>FogTileGridLogic</c> (the
/// same helper used by <c>FogTileLayer</c> and the per-tile bitmap API).
/// We stay on the locked iso convention <c>IsoDiamondDown</c> with the
/// origin shift so the POI lands at the same world coords as the fog
/// tile under it.
/// </para>
/// </summary>
public partial class PoiSpawner : Node
{
    /// <summary>
    /// Path to the <c>Poi.tscn</c> scene loaded once at <c>_Ready</c>.
    /// Kept as an <see cref="Export"/> so a probe / slice can swap to a
    /// derived scene without recompiling. Defaults to the canonical path.
    /// </summary>
    [Export] public PackedScene? PoiScene { get; set; }

    /// <summary>
    /// Iso cell side in pixels. Locked at 128 across the runtime
    /// (matches <c>FogTileLayer.CellSizePx</c> default and the sidecar
    /// loader default). Kept exposed for probe overrides.
    /// </summary>
    [Export] public int CellSizePx { get; set; } = 128;

    /// <summary>
    /// Grid dimensions used to compute the iso origin shift. The shift
    /// is <c>(rows × halfW, 0)</c>, so the columns count does not matter
    /// for the X shift but we accept the full dimensions to keep the
    /// API symmetric with <c>FogTileGridLogic.ComputeIsoOriginShift</c>.
    /// Probe-set ; production will read from the active grid service.
    /// </summary>
    [Export] public Vector2I GridDimensions { get; set; } = new(24, 24);

    public override void _Ready()
    {
        if (PoiScene == null)
        {
            // Lazy-load the canonical scene if the export was not wired up.
            PoiScene = GD.Load<PackedScene>("res://scenes/poi/Poi.tscn");
        }

        GD.Print(
            $"[POI SPAWNER] _Ready " +
            $"poiScene={(PoiScene != null ? "loaded" : "NULL")} " +
            $"cellSize={CellSizePx}px " +
            $"gridDims=({GridDimensions.X},{GridDimensions.Y})");
    }

    /// <summary>
    /// Spawn a POI scene at the given iso tile, parented under
    /// <paramref name="parent"/>. The <paramref name="data"/> is injected on
    /// the instance <b>before</b> <c>AddChild</c> so <c>Poi._Ready</c> sees
    /// a non-null <c>PoiData</c>. PR5 — optional <paramref name="parallaxCamera"/>
    /// is forwarded to <see cref="Poi.SetParallaxCamera"/> after AddChild ;
    /// when null, the POI runs with parallax disabled (same effect as
    /// <c>Poi.ParallaxStrength = 0</c>).
    /// </summary>
    /// <param name="data">The POI's runtime data (loaded via sidecar).</param>
    /// <param name="tile">Iso (col, row) tile coordinate.</param>
    /// <param name="parent">World-tree parent node (must NOT be a CanvasLayer).</param>
    /// <param name="parallaxCamera">Optional camera the POI follows for parallax.</param>
    /// <returns>The spawned <see cref="Poi"/> Sprite2D node.</returns>
    public Poi SpawnAt(PoiData data, Vector2I tile, Node parent, Camera2D? parallaxCamera = null)
    {
        if (PoiScene == null)
        {
            PoiScene = GD.Load<PackedScene>("res://scenes/poi/Poi.tscn");
        }

        var instance = PoiScene!.Instantiate<Poi>();
        instance.PoiData = data;
        instance.Position = TileToWorld(tile);
        instance.Name = $"Poi_{data.DisplayName}";

        parent.AddChild(instance);

        // PR5 — wire the parallax camera AFTER AddChild so _Ready has fired
        // (the parallax target is read in _Process which only ticks after
        // _Ready, but we set the field early so the first _Process frame
        // sees a non-null camera and does not snap-jump).
        if (parallaxCamera != null)
        {
            instance.SetParallaxCamera(parallaxCamera);
        }

        GD.Print(
            $"[POI SPAWNER] SpawnAt {data.DisplayName} " +
            $"tile=({tile.X},{tile.Y}) " +
            $"worldPos=({instance.Position.X:F1},{instance.Position.Y:F1}) " +
            $"parent={parent.GetType().Name}/{parent.Name} " +
            $"parallaxCamera={(parallaxCamera != null ? parallaxCamera.Name.ToString() : "none")}");

        return instance;
    }

    /// <summary>
    /// Iso tile → world position. Uses the same projection as
    /// <c>FogTileLayer</c> so a POI lands on the same world coords as its
    /// underlying fog cell : <c>ComputeCellCenter + ComputeIsoOriginShift</c>.
    /// </summary>
    private Vector2 TileToWorld(Vector2I tile)
    {
        var dims = new GridDimensions(GridDimensions.X, GridDimensions.Y);
        var coord = new GridCoord(tile.X, tile.Y);
        var center = FogTileGridLogic.ComputeCellCenter(
            coord, CellSizePx, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, CellSizePx, GridProjection.IsoDiamondDown);
        return new Vector2(center.X + shift.X, center.Y + shift.Y);
    }
}
