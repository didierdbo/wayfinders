using System.Collections.Generic;
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
/// PR5 extension — the four "pion sur plateau" ingredients are tunable via
/// F1-F4 toggles. This is the A/B harness Didier uses at F6 ; without it
/// the visual tuning is undebuggable (trap §8 — "the eye decides, but
/// give the eye a switch").
///
/// <para>
/// <b>Run.</b> F6 on <c>PoiSpawnProbe.tscn</c>. Expected output panel :
/// <code>
/// [POI ROUTER] registered, autoload OK, signals declared ...
/// [POI SPAWN PROBE] _Ready cellSize=128 gridDims=(24,24) centerTile=(12,12)
/// [POI SPAWNER] _Ready poiScene=loaded cellSize=128px gridDims=(24,24)
/// [POI SPAWNER] SpawnAt Halfgate tile=(12,12) ... parallaxCamera=WorldCamera
/// [POI SPAWN] Halfgate basePos=... liftedPos=... anchorAlignedOK
/// [POI Halfgate] shadow=ON lift=2 rim=ON parallax=0.040 camera=wired (trap §8 — values delivered to screen)
/// [POI Halfgate] registered with router
/// [POI SPAWNER] SpawnAt HalfgateClone tile=(12,13) ...
/// [POI SPAWN] HalfgateClone ... anchorAlignedOK
/// [POI HalfgateClone] registered with router
/// </code>
/// </para>
///
/// <para>
/// <b>PR5 — F-key tuning matrix (Didier at F6).</b>
/// <list type="bullet">
///   <item><b>F1</b> — toggle Shadow ON/OFF on every POI. Halfgate should feel
///         "posed" with shadow, "sticker" without.</item>
///   <item><b>F2</b> — cycle Lift 0 → 2 → 3 → 0. 0 = sticker (locked anti),
///         2 = posed (locked target), 3 = jouet edge (locked anti).</item>
///   <item><b>F3</b> — toggle Rim ON/OFF. Look for the warm terracotta
///         highlight on the SW edge of the pillar.</item>
///   <item><b>F4</b> — cycle Parallax 0 → 0.03 → 0.05 → 0. Pan the camera
///         (MMB drag or arrows) and feel the POI drift relative to the
///         cadastral grid.</item>
///   <item><b>F1-F4 cumulés.</b> The HUD bottom-left shows the four current
///         values live so a screenshot captures the configuration.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>PR4 validation matrix (kept).</b>
/// <list type="bullet">
///   <item>Hover the pillar (opaque) → label "POI hover: Halfgate".</item>
///   <item>Hover the AABB corner (transparent) → label "POI: -".</item>
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
    [Export] public NodePath HudTuningLabelPath { get; set; } = new("Hud/TuningLabel");

    /// <summary>
    /// PR5 follow-up (2026-05-15) — disabled by default so Didier sees a
    /// single Halfgate when doing the visual A/B at F6. The clone was a
    /// PR4 artefact used to validate the Y-sort cascade ; it now reads as
    /// a rendering bug to a fresh eye because it shares the same texture
    /// at α 0.5 and offsets one tile south on the iso grid (≈ 64 px left
    /// + 32 px down at zoom 0.5). Flip back to <c>true</c> in the .tscn
    /// inspector to re-run the Y-sort cascade test from PR4.
    /// </summary>
    [Export] public bool SpawnClone { get; set; } = false;

    private Label? _hudLabel;
    private Label? _tuningLabel;
    private PoiInputRouter? _router;

    // PR5 — track every spawned POI so the F1-F4 toggles fan out to all
    // of them. The router has a similar list but it is private (locked
    // PR4) ; we keep a probe-local mirror to avoid widening the router's
    // public surface for a probe-only debug need.
    private readonly List<Poi> _spawned = new();

    public override void _Ready()
    {
        var spawner = GetNode<PoiSpawner>(SpawnerPath);
        var worldRoot = GetNode<Node2D>(WorldRootPath);
        var camera = GetNode<Camera2D>(WorldCameraPath);
        _hudLabel = GetNodeOrNull<Label>(HudLabelPath);
        _tuningLabel = GetNodeOrNull<Label>(HudTuningLabelPath);

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

        // PR5 — pass the camera so the POI parallax-follows it.
        var poi = spawner.SpawnAt(data, centerTile, worldRoot, camera);
        _spawned.Add(poi);

        // PR4 — spawn a second POI one tile south so the Y-sort tiebreaker
        // is observable. Reuses the same texture/sidecar (cheap clone) and
        // displays semi-transparent so Halfgate stays visually dominant.
        // PR5 — the clone also gets the camera ; both POIs parallax in
        // sync so the comparison stays clean.
        // PR5 follow-up (2026-05-15) — gated behind <see cref="SpawnClone"/>
        // so the default F6 run shows a single Halfgate for clean A/B
        // visual validation of the four "pion sur plateau" ingredients.
        Poi? clone = null;
        if (SpawnClone)
        {
            var cloneTile = new Vector2I(centerTile.X, centerTile.Y + 1);
            var cloneData = PoiSidecarLoader.Load("res://assets/poi/e1/wf_e1_halfgate_poi.png");
            cloneData.DisplayName = "HalfgateClone";
            clone = spawner.SpawnAt(cloneData, cloneTile, worldRoot, camera);
            clone.Modulate = new Color(1, 1, 1, 0.5f);
            _spawned.Add(clone);
        }

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

        var cloneLog = clone != null
            ? $"clonePos=({clone.Position.X:F1},{clone.Position.Y:F1})"
            : "clone=disabled (PR5 A/B mode)";
        GD.Print(
            $"[POI SPAWN PROBE] camera centered at " +
            $"({camera.Position.X:F1},{camera.Position.Y:F1}) " +
            $"poiPos=({poi.Position.X:F1},{poi.Position.Y:F1}) " +
            cloneLog);

        if (_hudLabel != null)
        {
            _hudLabel.Text = "POI: -";
        }

        UpdateTuningHud();
        GD.Print(
            "[POI SPAWN PROBE] PR5 toggles : F1=Shadow F2=Lift(0/2/3) " +
            "F3=Rim F4=Parallax(0/0.03/0.05). HUD bottom-left shows live values.");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // We intercept the four debug toggles. Use `Pressed && !Echo` so a
        // long press fires once. Memo trap §3 — `_UnhandledKeyInput` runs
        // AFTER any Control has had its say, which is what we want here
        // (the probe HUD must not steal keys from us).
        if (@event is not InputEventKey key) return;
        if (!key.Pressed || key.Echo) return;

        switch (key.Keycode)
        {
            case Key.F1:
                foreach (var p in _spawned) p.DebugToggleShadow();
                GD.Print("[PROBE] F1 — Shadow toggled");
                UpdateTuningHud();
                break;
            case Key.F2:
                foreach (var p in _spawned) p.DebugCycleLift();
                GD.Print("[PROBE] F2 — Lift cycled");
                UpdateTuningHud();
                break;
            case Key.F3:
                foreach (var p in _spawned) p.DebugToggleRim();
                GD.Print("[PROBE] F3 — Rim toggled");
                UpdateTuningHud();
                break;
            case Key.F4:
                foreach (var p in _spawned) p.DebugCycleParallax();
                GD.Print("[PROBE] F4 — Parallax cycled");
                UpdateTuningHud();
                break;
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

    private void UpdateTuningHud()
    {
        if (_tuningLabel == null || _spawned.Count == 0) return;
        // Read the first POI's state — all spawned share the same values
        // because the toggles fan out together.
        var p = _spawned[0];
        _tuningLabel.Text =
            $"Shadow: {(p.ShadowEnabled ? "ON " : "OFF")}  " +
            $"Lift: {p.LiftPx}  " +
            $"Rim: {(p.RimEnabled ? "ON " : "OFF")}  " +
            $"Parallax: {p.ParallaxStrength:F3}";
    }
}
