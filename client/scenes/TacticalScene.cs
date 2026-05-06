using System.Collections.Generic;
using System.Linq;
using Godot;
using Wayfinders.Client.Simulation;
using Wayfinders.Client.Units;

namespace Wayfinders.Client.Scenes;

/// <summary>
/// First tactical scene (Phase 5 L1 → L5).
///
/// <para>
/// <b>What this layer ships now (L5 added on top of L1–L4):</b> in
/// addition to the iso 24×24 ground (L1+L3), simulation grid (L2), and
/// AStar pathfinding service (L4), three placeholder party units —
/// Kira, Brann, Edda — are spawned on the ridge plateau at startup.
/// Each unit writes its <see cref="Cell.OccupantId"/> on the simulation
/// grid; <see cref="PathfindingService"/> is then invalidated so
/// subsequent path queries treat occupied cells as solid (you cannot
/// path through a friendly unit). Clicks select a unit and preview a
/// path from that unit's cell.
/// </para>
///
/// <para>
/// <b>What this layer is still NOT:</b> no real movement (units cannot
/// be commanded to walk yet — L5 is spawn + select + preview only); no
/// real art (Mira's portrait set replaces <see cref="UnitMarker"/>
/// later); no turn manager, no AI, no API fetch (the trio is hardcoded
/// for the slice; L6 swaps in a fetch-from-FastAPI flow once the
/// roster endpoint returns mission-ready data).
/// </para>
///
/// <para>
/// <b>Visual / simulation / pathfinding — three layers, one direction.</b>
/// <see cref="SimulationGrid"/> is the source of truth (L2 lock). The
/// <see cref="TileMapLayer"/> for terrain (Ground) is a render of the
/// simulation. The <see cref="PathfindingService"/> is a query cache
/// derived from the simulation. The path-highlight overlay
/// (<c>PathHighlight</c>) is a render of pathfinding query results. The
/// <c>Units</c> branch is a render of <see cref="PartyUnit"/> instances
/// whose authoritative position lives in <see cref="Cell.OccupantId"/>.
/// None of the downstream layers ever feed back upstream — if the
/// simulation changes, we re-stamp the ground, call
/// <see cref="PathfindingService.Invalidate"/>, and reposition the
/// marker; we never read state back out of the renderers.
/// </para>
///
/// <para>
/// <b>Two TileMapLayers, two TileSets.</b> Each layer owns its own
/// <see cref="TileSet"/>. Sharing one would let any layer stamp any tile
/// — the kind of accidental coupling that causes "why is the path-highlight
/// drawing terrain?" bugs at 11pm. Per-layer TileSets make the boundaries
/// explicit and keep the placeholder atlases small. When Mira's iso art
/// lands the Ground TileSet becomes a <c>.tres</c> asset; the PathHighlight
/// TileSet stays code-generated because debug overlays are not designer
/// surface.
/// </para>
///
/// <para>
/// <b>Why the path overlay is a TileMapLayer, not <see cref="Line2D"/> or
/// <see cref="Sprite2D"/> markers.</b> Three options were on the table:
/// <list type="bullet">
///   <item><b><see cref="TileMapLayer"/> overlay</b> (chosen): reuses the
///   exact same coord system and iso transform as the gameplay grid;
///   <see cref="TileMapLayer.SetCell"/> is the API we already speak in
///   this scene; clearing the overlay is one
///   <see cref="TileMapLayer.Clear"/> call. Y-sort behavior matches the
///   ground layer for free.</item>
///   <item><b><see cref="Line2D"/></b>: cheap to draw, but each path step
///   would need <see cref="TileMapLayer.MapToLocal"/> conversion, and a
///   line drawn between iso tile centers feels off — the player reads the
///   grid as discrete tiles and a continuous line breaks that mental
///   model.</item>
///   <item><b><see cref="Sprite2D"/> markers</b>: N nodes per path step,
///   each manually placed. Verbose, lifecycle to manage, no idiomatic
///   advantage over a TileMapLayer.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>L5 click model — selection-driven, not free-form.</b> The L4 cycle
/// (any cell → any cell → reset) was a smoke test for the pathfinding
/// service. L5 replaces it with the real-game flow:
/// <list type="bullet">
///   <item>No selection, click a unit's cell → select that unit (cyan
///   ring).</item>
///   <item>No selection, click anywhere else → no-op (log).</item>
///   <item>Selection active, click an empty passable cell → preview path
///   from the selected unit's cell to the click. The selection ring
///   stays.</item>
///   <item>Selection active, click the selected unit's own cell →
///   deselect. Path preview is cleared.</item>
///   <item>Selection active, click a different unit's cell → switch
///   selection. Previous path preview is cleared; the new unit gets the
///   ring.</item>
///   <item>Selection active, click an impassable cell (boulder, occupied
///   by a different unit) → "no path" log, endpoint highlight only,
///   selection stays. (Note: clicking a different unit takes the switch-
///   selection branch above; the impassable-occupant case can only
///   happen if a non-party occupant exists, which is an L6+ situation.)</item>
///   <item>Selection active, click outside the grid → deselect. Path
///   preview cleared.</item>
/// </list>
/// The third click of the L4 cycle is gone — there is no "showing path"
/// terminal state anymore. As long as a unit is selected, every click
/// either updates the preview, switches selection, or deselects.
/// </para>
///
/// <para>
/// <b>L5 spawn coords — ridge plateau.</b> The three party units land at
/// <c>(3,5)</c>, <c>(4,5)</c>, <c>(5,5)</c> — anti-diagonals 8, 9, 10,
/// firmly inside the ridge plateau band (<c>x + y &lt; 12</c>) defined by
/// <see cref="SimulationGrid.BuildRidgeMission"/>. All three are walkable
/// elevation-2 cells with no boulders or partial-cover, so no terrain
/// rule is violated when their <see cref="Cell.OccupantId"/> is written.
/// They are adjacent on screen (since constant-y bands run along the SE
/// diagonal under <c>DiamondDown</c>, three cells with the same y read as
/// a small SE-going line on screen — close enough to read as "a party",
/// far enough apart to see each one). The arrangement is hardcoded for
/// the L5 slice; the real mission script will own placement when Varn's
/// designer hooks land.
/// </para>
///
/// <para>
/// <b>Y-sort and the Units branch.</b> The <c>Units</c>
/// <see cref="Node2D"/> has <c>y_sort_enabled = true</c>, and each
/// <see cref="UnitMarker"/> is positioned at
/// <see cref="TileMapLayer.MapToLocal"/> of its cell. Godot's Y-sort
/// renders children with smaller <c>position.y</c> behind children with
/// larger <c>position.y</c>, which on an iso projection is exactly the
/// "south is in front" rule a tactical RPG wants. No manual z-index
/// management; the engine does it.
/// </para>
///
/// <para>
/// <b>Hover-cell debug overlay (temporary).</b> A
/// <see cref="DebugOverlay/HoverCellLabel"/> in a screen-space
/// <see cref="CanvasLayer"/> shows the iso cell currently under the mouse,
/// updated every frame. This exists to let the owner calibrate "where I
/// think I clicked" against "what cell Godot resolved" while we diagnose
/// the click-mapping discrepancy reported on PR #15. Removing the overlay
/// is a one-line change in the <c>.tscn</c> plus the
/// <see cref="_Process"/> call below; planned chore once the iso-mapping
/// hypothesis is confirmed and (if needed) fixed.
/// </para>
///
/// <para>
/// <b>Camera centering.</b> A <c>Camera2D</c> child of the scene is
/// positioned at the center of the iso diamond and zoomed out to 0.65
/// so the full 24×24 grid fits in the default 1152×648 viewport.
/// Anchoring the layers at origin keeps <c>LocalToMap</c> arithmetic
/// clean (cell <c>(0,0)</c> at world <c>(0,0)</c>); the camera owns
/// presentation.
/// </para>
///
/// <para>
/// <b>TileLayout = DiamondDown (the iso-coherent neighbor convention).</b>
/// Both TileSets explicitly set
/// <see cref="TileSet.TileLayoutEnum.DiamondDown"/>. The Godot 4 default
/// (<see cref="TileSet.TileLayoutEnum.Stacked"/>) gives parity-dependent
/// neighborhoods on iso-shape tiles that confuse
/// <see cref="AStarGrid2D"/>; <see cref="TileSet.TileLayoutEnum.DiamondDown"/>
/// gives the classic iso-coherent <c>pixel = ((x-y)*TW/2, (x+y)*TH/2)</c>
/// projection where every cell-coord neighbor is a clean visual neighbor.
/// Side effect on <see cref="SimulationGrid.BuildRidgeMission"/>: with
/// <c>DiamondDown</c>, "screen vertical position" is governed by
/// <c>(x + y)</c>, not <c>y</c> alone. Bands of constant <c>y</c> appear
/// as SE-going diagonals on screen.
/// </para>
/// </summary>
public partial class TacticalScene : Node2D
{
    /// <summary>
    /// Map width in cells. 24×24 is the locked target for the slice
    /// (see <c>Rune - Arc 2 Phase 5 Pre-Brief - 2026-05-03.md</c>,
    /// changelog 2026-05-05).
    /// </summary>
    private const int MapWidth = 24;

    /// <summary>
    /// Map height in cells.
    /// </summary>
    private const int MapHeight = 24;

    /// <summary>
    /// Iso tile texture width in pixels. Standard 2:1 iso ratio with
    /// <see cref="TileHeight"/>.
    /// </summary>
    private const int TileWidth = 64;

    /// <summary>
    /// Iso tile texture height in pixels. Half the width for the standard
    /// 2:1 iso projection.
    /// </summary>
    private const int TileHeight = 32;

    /// <summary>
    /// Atlas-source ID for the terrain tiles on the Ground layer. Single
    /// atlas source holds all three terrain variants at three atlas coords.
    /// </summary>
    private const int TerrainSourceId = 0;

    /// <summary>
    /// Atlas-source ID for the highlight tiles on the PathHighlight layer.
    /// Separate <see cref="TileSet"/> entirely, so the two layers cannot
    /// accidentally cross-stamp.
    /// </summary>
    private const int HighlightSourceId = 0;

    /// <summary>
    /// Atlas coordinate of the walkable-terrain tile inside the terrain
    /// atlas.
    /// </summary>
    private static readonly Vector2I WalkableAtlasCoord = new(0, 0);

    /// <summary>
    /// Atlas coordinate of the loose-scree terrain tile.
    /// </summary>
    private static readonly Vector2I LooseScreeAtlasCoord = new(1, 0);

    /// <summary>
    /// Atlas coordinate of the blocked-terrain tile (boulder / palisade
    /// footing).
    /// </summary>
    private static readonly Vector2I BlockedAtlasCoord = new(2, 0);

    /// <summary>
    /// Atlas coordinate of the path-step highlight tile (cyan, semi-
    /// transparent).
    /// </summary>
    private static readonly Vector2I PathStepAtlasCoord = new(0, 0);

    /// <summary>
    /// Atlas coordinate of the endpoint highlight tile (yellow, semi-
    /// transparent). Used for both <c>from</c> and <c>to</c> markers.
    /// </summary>
    private static readonly Vector2I EndpointAtlasCoord = new(1, 0);

    /// <summary>
    /// Lookup from <see cref="TerrainType"/> to atlas coord on the terrain
    /// atlas. Built once at static init; switched lookup is one
    /// <see cref="Dictionary{TKey, TValue}.TryGetValue"/> call. A
    /// <c>switch</c> expression would be equivalent here — the dictionary
    /// version reads more like a data table, which is what this is.
    /// </summary>
    private static readonly IReadOnlyDictionary<TerrainType, Vector2I>
        TerrainAtlasLookup = new Dictionary<TerrainType, Vector2I>
        {
            [TerrainType.Walkable] = WalkableAtlasCoord,
            [TerrainType.LooseScree] = LooseScreeAtlasCoord,
            [TerrainType.Blocked] = BlockedAtlasCoord,
        };

    /// <summary>
    /// Hardcoded L5 party trio. Pratchett-flavored canonical names from
    /// the slice (see <c>project_wayfinders_tonal_dna.md</c>); positions
    /// are on the ridge plateau (<c>x + y &lt; 12</c>). The Ids are
    /// placeholder slugs — when L6 swaps in the FastAPI fetch flow, the
    /// server will issue real opaque ids.
    /// </summary>
    private static readonly PartyUnit[] PartyRoster =
    {
        new(Id: "kira",  DisplayName: "Kira",  Position: new Vector2I(3, 5)),
        new(Id: "brann", DisplayName: "Brann", Position: new Vector2I(4, 5)),
        new(Id: "edda",  DisplayName: "Edda",  Position: new Vector2I(5, 5)),
    };

    /// <summary>
    /// Body color per party unit. Distinct enough to tell at a glance
    /// while the placeholder visual ships; replaced wholesale when Mira
    /// delivers portraits.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Color> PartyColors =
        new Dictionary<string, Color>
        {
            ["kira"]  = new Color(0.85f, 0.55f, 0.30f), // warm orange
            ["brann"] = new Color(0.45f, 0.70f, 0.50f), // muted green
            ["edda"]  = new Color(0.55f, 0.55f, 0.85f), // muted violet
        };

    private TileMapLayer _ground = null!;
    private TileMapLayer _pathHighlight = null!;
    private Node2D _unitsRoot = null!;

    /// <summary>
    /// Screen-space label that displays the iso cell currently under the
    /// mouse cursor. Temporary diagnostic surface — see class-doc note on
    /// the hover-cell debug overlay.
    /// </summary>
    private Label _hoverCellLabel = null!;

    /// <summary>
    /// Authoritative game state for the tactical map. Built once in
    /// <see cref="_Ready"/> and held for the lifetime of the scene.
    /// </summary>
    private SimulationGrid _simulation = null!;

    /// <summary>
    /// Pathfinding service derived from <see cref="_simulation"/>. Same
    /// lifetime as the scene; rebuilt via
    /// <see cref="PathfindingService.Invalidate"/> when the simulation
    /// mutates.
    /// </summary>
    private PathfindingService _pathfinding = null!;

    /// <summary>
    /// Live <see cref="UnitMarker"/> nodes by unit id. Used for visual
    /// updates (selection ring toggle, future movement). The cell
    /// positions live on the simulation grid (<see cref="Cell.OccupantId"/>);
    /// this map is the rendering side's lookup.
    /// </summary>
    private readonly Dictionary<string, UnitMarker> _markers = new();

    /// <summary>
    /// Live <see cref="PartyUnit"/> records by id. Mirrors
    /// <see cref="_markers"/> for sim-side data (current cell, name).
    /// Updated when units move (L6+).
    /// </summary>
    private readonly Dictionary<string, PartyUnit> _units = new();

    /// <summary>
    /// Id of the currently-selected unit, or <c>null</c> if no selection.
    /// The selected unit's marker shows a cyan ring; clicks on empty
    /// cells preview a path from this unit's cell.
    /// </summary>
    private string? _selectedUnitId;

    public override void _Ready()
    {
        _ground = GetNode<TileMapLayer>("Ground");
        _pathHighlight = GetNode<TileMapLayer>("PathHighlight");
        _unitsRoot = GetNode<Node2D>("Units");
        _hoverCellLabel = GetNode<Label>("DebugOverlay/HoverCellLabel");

        _ground.TileSet = BuildTerrainTileSet();
        _pathHighlight.TileSet = BuildHighlightTileSet();

        // Build the simulation first; the visual stamp pass below reads
        // from it. This ordering — sim before visual — is the lesson the
        // visual/simulation split is meant to teach.
        _simulation = SimulationGrid.BuildRidgeMission();

        StampGroundFromSimulation();

        _pathfinding = new PathfindingService(_simulation);

        // L5: populate the party. Writes OccupantId on three plateau
        // cells, instantiates a marker per unit, then invalidates the
        // pathfinder so subsequent queries see the now-occupied cells as
        // solid.
        SpawnParty();

        GD.Print(
            $"TacticalScene: rendered {MapWidth}x{MapHeight} iso grid from " +
            $"simulation; {_markers.Count} units spawned; " +
            $"pathfinding service ready. " +
            $"Click a unit to select; click a cell to preview a path.");
    }

    /// <summary>
    /// Update the hover-cell debug label every frame. Uses the exact same
    /// screen → cell conversion chain as the click handler
    /// (<see cref="GetGlobalMousePosition"/> → <see cref="Node2D.ToLocal"/>
    /// → <see cref="TileMapLayer.LocalToMap"/>) so any discrepancy between
    /// "what I see" and "what I click" surfaces here too — the overlay
    /// shares the alleged bug with the click path, which is the point.
    /// </summary>
    public override void _Process(double delta)
    {
        Vector2 worldPos = GetGlobalMousePosition();
        Vector2 layerLocalPos = _ground.ToLocal(worldPos);
        Vector2I coord = _ground.LocalToMap(layerLocalPos);

        string status = _simulation.IsInBounds(coord) ? "in" : "out";
        _hoverCellLabel.Text =
            $"hover cell: {coord} ({status})\n" +
            $"world: {worldPos.X:F0}, {worldPos.Y:F0}";
    }

    /// <summary>
    /// L5 click handler — selection-driven, see class-doc table for the
    /// full rule set. Shape:
    /// <list type="number">
    ///   <item>Convert click to cell coord.</item>
    ///   <item>If out of grid → clear selection and preview, log, return.</item>
    ///   <item>If clicked cell holds a unit → select / switch / deselect.</item>
    ///   <item>If clicked cell is empty and we have a selection → preview
    ///   path from selected unit's cell to clicked cell.</item>
    ///   <item>If clicked cell is empty and no selection → log no-op.</item>
    /// </list>
    ///
    /// <para>
    /// Listens on <c>_UnhandledInput</c> so a future pause menu or HUD can
    /// intercept events first; the tactical world only sees clicks that no
    /// UI ate.
    /// </para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb)
        {
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
        {
            return;
        }

        Vector2 worldPos = GetGlobalMousePosition();
        Vector2 layerLocalPos = _ground.ToLocal(worldPos);
        Vector2I coord = _ground.LocalToMap(layerLocalPos);

        if (!_simulation.IsInBounds(coord))
        {
            GD.Print($"TacticalScene: clicked outside grid (cell {coord}). Deselecting.");
            DeselectUnit();
            return;
        }

        Cell clicked = _simulation.GetCell(coord);

        // Branch 1: clicked a unit's cell. Three sub-cases — same unit
        // selected (deselect), no selection or different unit selected
        // (select / switch).
        if (clicked.OccupantId is string occupantId)
        {
            if (occupantId == _selectedUnitId)
            {
                GD.Print(
                    $"TacticalScene: deselecting {_units[occupantId].DisplayName} " +
                    $"({occupantId}) at {coord}.");
                DeselectUnit();
            }
            else
            {
                SelectUnit(occupantId);
            }
            return;
        }

        // Branch 2: clicked an empty cell. With a selection, preview a
        // path; without, log a no-op so the player learns the rule.
        if (_selectedUnitId is null)
        {
            GD.Print(
                $"TacticalScene: clicked empty cell {coord} with no selection. " +
                $"Click a unit first.");
            return;
        }

        PreviewPathTo(coord);
    }

    /// <summary>
    /// Set <paramref name="unitId"/> as the current selection. Handles
    /// the switch-selection case (a different unit was previously
    /// selected) by clearing the previous ring and any active path
    /// preview before applying the new selection.
    /// </summary>
    private void SelectUnit(string unitId)
    {
        // Switch-selection cleanup: clear the previous unit's ring and
        // any path preview, since the preview's "from" is no longer
        // valid.
        if (_selectedUnitId is string previousId && previousId != unitId)
        {
            _markers[previousId].SetSelected(false);
            ClearHighlights();
        }

        _selectedUnitId = unitId;
        _markers[unitId].SetSelected(true);

        var unit = _units[unitId];
        GD.Print(
            $"TacticalScene: selected {unit.DisplayName} ({unitId}) at " +
            $"{unit.Position}. Click a cell to preview a path.");
    }

    /// <summary>
    /// Drop the current selection (if any) and clear the path preview.
    /// Idempotent: safe to call when nothing is selected.
    /// </summary>
    private void DeselectUnit()
    {
        if (_selectedUnitId is string id)
        {
            _markers[id].SetSelected(false);
            _selectedUnitId = null;
        }
        ClearHighlights();
    }

    /// <summary>
    /// Compute and render a path preview from the selected unit's cell
    /// to <paramref name="targetCoord"/>. Caller guarantees a unit is
    /// selected and the target cell is empty (the click handler enforces
    /// both).
    ///
    /// <para>
    /// On empty path (target unreachable — boulder, or clipped behind
    /// other solids), shows only the destination endpoint highlight and
    /// logs the failure. The selection stays — the player can click
    /// somewhere else without re-selecting.
    /// </para>
    /// </summary>
    private void PreviewPathTo(Vector2I targetCoord)
    {
        // Selection invariant: if we got here, a unit is selected. The
        // null-forgiving operator is safe; the click dispatch guards
        // this branch.
        string selectedId = _selectedUnitId!;
        Vector2I origin = _units[selectedId].Position;
        Cell targetCell = _simulation.GetCell(targetCoord);

        // Re-stamp clears any previous preview. Cheap (one Clear call).
        ClearHighlights();

        Vector2I[] path = _pathfinding.FindPath(origin, targetCoord);

        if (path.Length == 0)
        {
            GD.Print(
                $"TacticalScene: no path from {selectedId} at {origin} " +
                $"to {targetCoord} (Terrain={targetCell.Terrain}). " +
                $"Click another cell or click {selectedId} to deselect.");
            // Mark the unreachable destination so the player sees what
            // they aimed at. The unit's own cell does not need a
            // highlight — the cyan ring already says "this is the
            // origin".
            StampHighlight(targetCoord, EndpointAtlasCoord);
            return;
        }

        float cost = _pathfinding.ComputePathCost(path);
        GD.Print(
            $"TacticalScene: preview {selectedId} {origin} -> {targetCoord}, " +
            $"length {path.Length} cells, cost {cost:F1}. " +
            $"Coords: {string.Join(", ", path.Select(p => p.ToString()))}");

        // Endpoints get the endpoint highlight; the intermediate steps
        // get the path-step highlight. The unit's marker (with its cyan
        // ring) sits on top of the origin endpoint highlight — readable
        // by design.
        for (int i = 1; i < path.Length - 1; i++)
        {
            StampHighlight(path[i], PathStepAtlasCoord);
        }
        StampHighlight(path[0], EndpointAtlasCoord);
        StampHighlight(path[^1], EndpointAtlasCoord);
    }

    private void ClearHighlights()
    {
        _pathHighlight.Clear();
    }

    private void StampHighlight(Vector2I coord, Vector2I atlasCoord)
    {
        // Diagnostic log kept in place after the L4 path-highlight gap bug
        // (see commit message of fix). The label tells you at a glance
        // which atlas variant landed at which cell — if a future stamp
        // ever fails to land, the missing log line is the smoking gun.
        string label = atlasCoord == PathStepAtlasCoord ? "cyan"
                     : atlasCoord == EndpointAtlasCoord ? "yellow"
                     : atlasCoord.ToString();
        GD.Print($"TacticalScene.StampHighlight: cell={coord} atlas={label}");
        _pathHighlight.SetCell(coord, HighlightSourceId, atlasCoord);
    }

    /// <summary>
    /// Stamp the Ground layer from the simulation grid. Single direction
    /// — read sim, write tile.
    /// </summary>
    private void StampGroundFromSimulation()
    {
        foreach (var (coord, cell) in _simulation.GetAllCells())
        {
            Vector2I atlasCoord = TerrainAtlasLookup[cell.Terrain];
            _ground.SetCell(coord, TerrainSourceId, atlasCoord);
        }
    }

    /// <summary>
    /// Hardcode-spawn the L5 trio onto the simulation grid and the scene
    /// tree, then invalidate the pathfinder once so it picks up the new
    /// occupancy.
    ///
    /// <para>
    /// <b>Spawn order is load-bearing.</b> First, write
    /// <see cref="Cell.OccupantId"/> on each unit's cell — the simulation
    /// is the source of truth, so it gets written first. Second, build
    /// the visual marker per unit and add it to the <c>Units</c> branch.
    /// Third, call <see cref="PathfindingService.Invalidate"/> ONCE at
    /// the end — invalidating per-unit would re-seed the entire grid
    /// three times for nothing.
    /// </para>
    ///
    /// <para>
    /// <b>Spawn-cell sanity.</b> Each spawn cell is read first; if it is
    /// already occupied or impassable, we log and skip rather than
    /// silently overwriting. The hardcoded trio respects this constraint
    /// by construction; the guard exists because the L6+ flow will
    /// eventually feed dynamic positions into this method and a
    /// position-assignment bug in mission scripting should fail loud,
    /// not corrupt the grid.
    /// </para>
    /// </summary>
    private void SpawnParty()
    {
        foreach (var unit in PartyRoster)
        {
            if (!_simulation.IsInBounds(unit.Position))
            {
                GD.PushError(
                    $"SpawnParty: unit {unit.Id} position {unit.Position} " +
                    $"is out of bounds. Skipping.");
                continue;
            }

            Cell existing = _simulation.GetCell(unit.Position);
            if (!existing.IsPassable)
            {
                GD.PushError(
                    $"SpawnParty: unit {unit.Id} target cell {unit.Position} " +
                    $"is impassable (Terrain={existing.Terrain}). Skipping.");
                continue;
            }
            if (!existing.IsEmpty)
            {
                GD.PushError(
                    $"SpawnParty: unit {unit.Id} target cell {unit.Position} " +
                    $"is already occupied by '{existing.OccupantId}'. Skipping.");
                continue;
            }

            // Simulation side: write the occupant.
            _simulation.SetCell(unit.Position, existing with { OccupantId = unit.Id });

            // Visual side: instantiate a marker, position it via the same
            // MapToLocal the ground layer uses (so it lands on tile
            // center), color it, attach it under the Y-sorted Units root.
            var marker = new UnitMarker
            {
                Name = $"Unit_{unit.Id}",
                Position = _ground.MapToLocal(unit.Position),
            };
            if (PartyColors.TryGetValue(unit.Id, out Color color))
            {
                marker.SetBodyColor(color);
            }
            _unitsRoot.AddChild(marker);
            _markers[unit.Id] = marker;
            _units[unit.Id] = unit;

            GD.Print(
                $"SpawnParty: {unit.DisplayName} ({unit.Id}) spawned at " +
                $"{unit.Position} (screen {marker.Position}).");
        }

        // Pathfinder picks up the three new solids in one re-seed.
        _pathfinding.Invalidate();
    }

    /// <summary>
    /// Build the Ground layer's <see cref="TileSet"/>: one atlas source,
    /// three iso-diamond tiles at distinct atlas coords, one per
    /// <see cref="TerrainType"/>. The atlas texture is laid out as a
    /// 3-tile horizontal strip (192×32 pixels).
    /// </summary>
    private static TileSet BuildTerrainTileSet()
    {
        var tileSet = new TileSet
        {
            TileShape = TileSet.TileShapeEnum.Isometric,
            TileLayout = TileSet.TileLayoutEnum.DiamondDown,
            TileSize = new Vector2I(TileWidth, TileHeight),
        };

        var atlas = new TileSetAtlasSource
        {
            Texture = BuildTerrainAtlasTexture(),
            TextureRegionSize = new Vector2I(TileWidth, TileHeight),
        };

        atlas.CreateTile(WalkableAtlasCoord);
        atlas.CreateTile(LooseScreeAtlasCoord);
        atlas.CreateTile(BlockedAtlasCoord);

        tileSet.AddSource(atlas, TerrainSourceId);
        return tileSet;
    }

    /// <summary>
    /// Build the PathHighlight layer's <see cref="TileSet"/>: two iso-
    /// diamond tiles, drawn as semi-transparent overlays so the terrain
    /// underneath stays readable through the highlight.
    /// </summary>
    private static TileSet BuildHighlightTileSet()
    {
        var tileSet = new TileSet
        {
            TileShape = TileSet.TileShapeEnum.Isometric,
            TileLayout = TileSet.TileLayoutEnum.DiamondDown,
            TileSize = new Vector2I(TileWidth, TileHeight),
        };

        var atlas = new TileSetAtlasSource
        {
            Texture = BuildHighlightAtlasTexture(),
            TextureRegionSize = new Vector2I(TileWidth, TileHeight),
        };

        atlas.CreateTile(PathStepAtlasCoord);
        atlas.CreateTile(EndpointAtlasCoord);

        tileSet.AddSource(atlas, HighlightSourceId);
        return tileSet;
    }

    /// <summary>
    /// Draw a 192×32 atlas containing three 64×32 iso diamonds side by
    /// side: walkable (muted green), loose-scree (sandy gray with stipple),
    /// blocked (dark slate). Each diamond is the standard L1-norm
    /// rhombus, with a darker border ring for tile separation.
    /// </summary>
    private static ImageTexture BuildTerrainAtlasTexture()
    {
        const int TileCount = 3;
        int atlasWidth = TileWidth * TileCount;
        var image = Image.CreateEmpty(atlasWidth, TileHeight, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        DrawIsoDiamond(
            image,
            originX: WalkableAtlasCoord.X * TileWidth,
            fill: new Color(0.45f, 0.55f, 0.40f),
            border: new Color(0.20f, 0.25f, 0.15f),
            stipple: false);

        DrawIsoDiamond(
            image,
            originX: LooseScreeAtlasCoord.X * TileWidth,
            fill: new Color(0.65f, 0.60f, 0.50f),
            border: new Color(0.35f, 0.30f, 0.20f),
            stipple: true);

        DrawIsoDiamond(
            image,
            originX: BlockedAtlasCoord.X * TileWidth,
            fill: new Color(0.25f, 0.25f, 0.30f),
            border: new Color(0.10f, 0.10f, 0.15f),
            stipple: false);

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Draw a 128×32 atlas containing two 64×32 iso diamonds: path-step
    /// (cyan, alpha) and endpoint (yellow, alpha). Both are semi-
    /// transparent so the terrain stays readable underneath.
    /// </summary>
    private static ImageTexture BuildHighlightAtlasTexture()
    {
        const int TileCount = 2;
        int atlasWidth = TileWidth * TileCount;
        var image = Image.CreateEmpty(atlasWidth, TileHeight, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        DrawIsoDiamond(
            image,
            originX: PathStepAtlasCoord.X * TileWidth,
            fill: new Color(0.30f, 0.80f, 0.90f, 0.55f),
            border: new Color(0.10f, 0.40f, 0.55f, 0.85f),
            stipple: false);

        DrawIsoDiamond(
            image,
            originX: EndpointAtlasCoord.X * TileWidth,
            fill: new Color(0.95f, 0.85f, 0.20f, 0.65f),
            border: new Color(0.60f, 0.45f, 0.05f, 0.95f),
            stipple: false);

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Draw a single 64×32 iso diamond into <paramref name="image"/>
    /// starting at horizontal offset <paramref name="originX"/>. The
    /// diamond is the L1-norm rhombus
    /// <c>|x - cx| / (w/2) + |y - cy| / (h/2) &lt;= 1</c>; the outer 8% is
    /// drawn with the border color. If <paramref name="stipple"/> is true,
    /// every fourth pixel of the fill is darkened — gives the loose-scree
    /// tile a visible "rough" texture without needing real art.
    /// </summary>
    private static void DrawIsoDiamond(
        Image image,
        int originX,
        Color fill,
        Color border,
        bool stipple)
    {
        float halfW = TileWidth / 2f;
        float halfH = TileHeight / 2f;
        float cx = halfW;
        float cy = halfH;

        Color stippleColor = new(
            fill.R * 0.7f,
            fill.G * 0.7f,
            fill.B * 0.7f,
            fill.A);

        for (int y = 0; y < TileHeight; y++)
        {
            for (int x = 0; x < TileWidth; x++)
            {
                float nx = Mathf.Abs(x + 0.5f - cx) / halfW;
                float ny = Mathf.Abs(y + 0.5f - cy) / halfH;
                float d = nx + ny;

                if (d > 1.0f)
                {
                    continue;
                }

                bool onBorder = d > 0.92f;
                Color c;
                if (onBorder)
                {
                    c = border;
                }
                else if (stipple && ((x + y) % 3 == 0))
                {
                    c = stippleColor;
                }
                else
                {
                    c = fill;
                }

                image.SetPixel(originX + x, y, c);
            }
        }
    }
}
