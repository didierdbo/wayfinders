using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// Étape A (2026-05-23, Rune) — places the active mission POIs on the
/// eM world map and surfaces a click signal for each one. Sibling of
/// <see cref="HalfgateMarkerLayer"/> in <c>GameScreen.tscn</c>, child of
/// the maquette <see cref="IsoBoard"/>.
///
/// <para>
/// <b>What this node owns.</b> One <see cref="Node2D"/> wired into the
/// shell with one concern : reading the active missions from
/// <see cref="MissionStore"/>, projecting each onto an eM render pixel
/// via <see cref="MissionPositionResolverLogic"/> +
/// <see cref="WorldMapCalage"/>, and reconciling a set of
/// <see cref="MissionPin"/> children against the live snapshot. It does
/// <i>not</i> own the missions themselves
/// (<see cref="MissionStore"/> is the authority) and it does <i>not</i>
/// own the eM map (<see cref="HalfgateMarkerLayer"/>'s class doc covers
/// the board / referential readiness story — we ride that same seam).
/// </para>
///
/// <para>
/// <b>Three readiness conditions (signal-driven, no polling).</b> A
/// mission POI placement needs three things that resolve at independent
/// times :
/// <list type="number">
///   <item><b>The board is set up.</b> Inherited from
///         <see cref="HalfgateMarkerLayer"/> : the parent
///         <see cref="IsoBoard"/>'s <c>_Ready</c> fires after this
///         child's, so we hop one <c>CallDeferred</c> step.</item>
///   <item><b>The world referential is loaded.</b> Needed to resolve
///         <c>target_poi → city → position</c>. We subscribe to
///         <see cref="WorldMapService.WorldLoaded"/> just like
///         <see cref="HalfgateMarkerLayer"/>.</item>
///   <item><b>The active mission list is populated.</b>
///         <see cref="MissionStore"/> polls <c>/api/missions/active</c>
///         on every <see cref="WorldSimTick.TickAdvanced"/> ; the cache
///         starts empty and fills on the first poll (or fires
///         <see cref="MissionStore.IngestEmerged"/> directly when a tick
///         emerges a mission). We subscribe to
///         <see cref="MissionStore.PendingMissionsChanged"/>.</item>
/// </list>
/// All three flags are checked on every event ; whichever resolves last
/// triggers the first placement pass. After that every
/// <see cref="MissionStore.PendingMissionsChanged"/> reconciles the
/// existing pins (add / remove / leave-alone) via
/// <see cref="MissionPoiLayerLogic.DiffMissions"/>.
/// </para>
///
/// <para>
/// <b>Cluster jitter (M1 norm : 3 recruit missions on Halfgate).</b>
/// All M1 recruit missions resolve to the same city centroid — three
/// pins perfectly stacked would be one visible pin and two hidden ones.
/// <see cref="MissionPoiLayerLogic.AssignClusterIndices"/> assigns each
/// mission an index within its city group (sorted by id ascending) and
/// <see cref="MissionPoiLayerLogic.ClusterSpiralOffset"/> spreads them
/// on a tight deterministic golden-angle spiral. Three M1 pins land
/// within ~22 px of each other on the 2048-wide eM mesh — clearly
/// clustered "in Halfgate", visibly distinct, click-disambiguatable.
/// </para>
///
/// <para>
/// <b>Code-drawn pins, not sprites.</b> Same call as
/// <see cref="HalfgateMarkerLayer"/>'s code pin : a
/// <see cref="MissionPin"/> draws its own diamond + halo in
/// <c>_Draw</c> so it cannot be shrunk to nothing by a sprite-scale
/// knob and is independent of any PNG asset. The visual is a small gold
/// losange with an umber outline (Wayfinders warm-earth DNA : ochre,
/// terracotta, umber) — distinguishable from the city marker's vermilion
/// pin, and reads against both the sea-blue and the parchment-beige of
/// Mira's eM render.
/// </para>
///
/// <para>
/// <b>Click signal.</b> <see cref="MissionPoiLayer"/> does <i>not</i>
/// handle the click itself — <c>GameScreen._UnhandledInput</c> owns the
/// shell's input cascade and uses <see cref="MapViewportInputRoutingLogic"/>
/// + <see cref="MissionPoiLayerLogic.PickClosestPin"/> to resolve a
/// screen click to a mission id, then calls <see cref="OnPinClicked"/>
/// on this node. The node emits <see cref="MissionPoiClicked"/> with the
/// mission id + the click's world position. Étape A scope : the signal
/// is logged. J7 will subscribe to drive the eM→eT bascule.
/// </para>
///
/// <para>
/// <b>Signal-disconnect discipline (trap #10).</b> We connect to two
/// long-lived autoloads ; both are explicitly disconnected on
/// <see cref="_ExitTree"/> against the cached service references — same
/// pattern as <see cref="HalfgateMarkerLayer"/> and the rest of the
/// shell.
/// </para>
/// </summary>
public partial class MissionPoiLayer : Node2D
{
    /// <summary>Console prefix for every diagnostic line this node emits.</summary>
    private const string Tag = "[MISSION-POI]";

    /// <summary>
    /// Pin pick radius in board-local pixels — the per-pin hit-test
    /// window <c>GameScreen._UnhandledInput</c> feeds through
    /// <see cref="MissionPoiLayerLogic.PickClosestPin"/>. Generous on
    /// purpose : at the eM zoom and on the 2048×1024 mesh, a 22 px
    /// pick radius is roughly the visible footprint of the drawn pin
    /// plus a small comfort margin. Exported so the F6 smoke can tune
    /// without a recompile.
    /// </summary>
    [Export]
    public float PinPickRadiusPx { get; set; } = 22f;

    /// <summary>
    /// Visual half-size of the mission pin in board-local pixels. The
    /// drawn diamond's apex-to-apex extent is <c>2 * PinHalfSizePx</c>.
    /// Smaller than the city marker on purpose : the city pin is "you
    /// cannot miss it", the mission pin is "one of N things happening
    /// in this city".
    /// </summary>
    [Export]
    public float PinHalfSizePx { get; set; } = 12f;

    /// <summary>
    /// Cluster-spiral seed radius (passed through to
    /// <see cref="MissionPoiLayerLogic.ClusterSpiralOffset"/>). 14 px
    /// keeps three sibling pins clearly distinct without spreading them
    /// across half the eM mesh.
    /// </summary>
    [Export]
    public float ClusterSpiralBaseRadiusPx { get; set; } = 14f;

    /// <summary>
    /// Fired when the player left-clicks a mission POI on eM. The
    /// <c>worldPosition</c> is the click's projected maquette-board
    /// position (not the pin centre) — useful diagnostic for the F6
    /// smoke. Étape A : the signal is logged. J7 (eM→eT bascule) will
    /// subscribe.
    /// </summary>
    [Signal]
    public delegate void MissionPoiClickedEventHandler(string missionId, Vector2 worldPosition);

    /// <summary>
    /// Whether the parent <see cref="IsoBoard"/>'s own <c>_Ready</c> has
    /// run (its background texture and projection are up). See
    /// <see cref="HalfgateMarkerLayer"/>'s class doc — same deferred-hop
    /// pattern.
    /// </summary>
    private bool _boardReady;

    /// <summary>
    /// Cached <see cref="WorldMapService"/> autoload reference, captured
    /// at <c>_Ready</c> so <see cref="_ExitTree"/> can disconnect
    /// against the same reference.
    /// </summary>
    private WorldMapService? _worldMapService;

    /// <summary>
    /// Cached <see cref="MissionStore"/> autoload reference, captured at
    /// <c>_Ready</c> so <see cref="_ExitTree"/> can disconnect.
    /// </summary>
    private MissionStore? _missionStore;

    /// <summary>
    /// Map of mission id → live <see cref="MissionPin"/> node. The
    /// reconcile diff in <see cref="ReconcilePins"/> walks this against
    /// the new active list ; nodes for removed ids are queue-freed,
    /// nodes for surviving ids are repositioned (in case the cluster
    /// index changed when a sibling was added or removed).
    /// </summary>
    private readonly Dictionary<string, MissionPin> _pinsByMissionId = new();

    public override void _Ready()
    {
        GD.Print($"{Tag} _Ready: MissionPoiLayer entering tree, " +
                 $"pickRadiusPx={PinPickRadiusPx}, " +
                 $"halfSizePx={PinHalfSizePx}, " +
                 $"clusterRadiusPx={ClusterSpiralBaseRadiusPx}.");

        // Condition 1 — board readiness. Same deferred hop as the city
        // marker layer ; documented in HalfgateMarkerLayer's class doc.
        CallDeferred(MethodName.OnBoardReady);

        // Condition 2 — world referential. Required to resolve each
        // mission's target_poi -> city -> position. We subscribe even if
        // the cache is already warm ; the once-deferred OnBoardReady call
        // exercises the warm path through ReconcilePins.
        _worldMapService = GetNodeOrNull<WorldMapService>("/root/WorldMapService");
        if (_worldMapService is null)
        {
            GD.PushWarning(
                $"{Tag} WorldMapService autoload missing — no mission POIs " +
                $"will be placed.");
        }
        else
        {
            _worldMapService.WorldLoaded += OnWorldLoaded;
        }

        // Condition 3 — active missions list. MissionStore fires
        // PendingMissionsChanged on every cache mutation (poll-driven
        // replacement, IngestEmerged, RemoveById). The cache may already
        // be non-empty by the time we subscribe (the autoload's first
        // poll may have landed before this scene's _Ready) — the
        // board-readiness hop covers that warm-path case via
        // ReconcilePins.
        _missionStore = GetNodeOrNull<MissionStore>("/root/MissionStore");
        if (_missionStore is null)
        {
            GD.PushWarning(
                $"{Tag} MissionStore autoload missing — no mission POIs " +
                $"will be placed.");
        }
        else
        {
            _missionStore.PendingMissionsChanged += OnPendingMissionsChanged;
            GD.Print($"{Tag} subscribed to MissionStore.PendingMissionsChanged " +
                     $"(store.AllActive.Count={_missionStore.AllActive.Count} at subscribe).");
        }
    }

    public override void _ExitTree()
    {
        if (_worldMapService is not null && GodotObject.IsInstanceValid(_worldMapService))
        {
            _worldMapService.WorldLoaded -= OnWorldLoaded;
        }
        if (_missionStore is not null && GodotObject.IsInstanceValid(_missionStore))
        {
            _missionStore.PendingMissionsChanged -= OnPendingMissionsChanged;
        }
    }

    /// <summary>
    /// Board-readiness hop, deferred once from <see cref="_Ready"/>.
    /// Sets the flag and runs the reconcile pass — covers the
    /// already-loaded fast path for both the world referential AND the
    /// mission store (either or both may already be warm).
    /// </summary>
    private void OnBoardReady()
    {
        _boardReady = true;
        GD.Print($"{Tag} OnBoardReady: board-readiness hop fired (_boardReady=true).");
        ReconcilePins("board-readiness hop");
    }

    private void OnWorldLoaded()
    {
        GD.Print($"{Tag} OnWorldLoaded: WorldLoaded signal received on the main thread.");
        ReconcilePins("WorldLoaded signal");
    }

    private void OnPendingMissionsChanged()
    {
        ReconcilePins("PendingMissionsChanged signal");
    }

    /// <summary>
    /// Idempotent reconcile : drop pins for missions that left the
    /// active set, spawn pins for missions that joined it, and update
    /// the position of every pin (cluster indices may have shifted when
    /// a sibling came or went). Cheap — three pins at most in M1, six
    /// even in a stretched-out scenario.
    /// </summary>
    /// <param name="trigger">Which event resolved last — logged so the
    /// F6 smoke shows the trigger ordering.</param>
    private void ReconcilePins(string trigger)
    {
        if (!_boardReady)
        {
            GD.Print($"{Tag} reconcile skipped (trigger='{trigger}') : board not ready yet.");
            return;
        }
        if (_worldMapService is null || _worldMapService.World is null)
        {
            GD.Print($"{Tag} reconcile skipped (trigger='{trigger}') : world referential not loaded yet.");
            return;
        }
        if (_missionStore is null)
        {
            GD.Print($"{Tag} reconcile skipped (trigger='{trigger}') : MissionStore unavailable.");
            return;
        }

        var board = GetParentOrNull<IsoBoard>();
        if (board is null)
        {
            GD.PushWarning($"{Tag} reconcile failed (trigger='{trigger}') : parent is not an IsoBoard.");
            return;
        }

        var meshSize = board.GetBackgroundTextureSizeOrZero();
        if (meshSize == Vector2.Zero)
        {
            GD.PushWarning(
                $"{Tag} reconcile failed (trigger='{trigger}') : maquette has no eM background texture.");
            return;
        }

        var citiesById = MissionPositionResolverLogic.BuildCityIndex(_worldMapService.World.Cities);
        var snapshot = _missionStore.AllActive;
        var diff = MissionPoiLayerLogic.DiffMissions(_pinsByMissionId.Keys, snapshot);

        GD.Print(
            $"{Tag} reconcile (trigger='{trigger}') : previous={_pinsByMissionId.Count}, " +
            $"snapshot={snapshot.Count}, added={diff.Added.Count}, " +
            $"removed={diff.Removed.Count}, surviving={diff.Surviving.Count}.");

        // 1. Remove pins whose missions left the active set.
        foreach (var id in diff.Removed)
        {
            if (_pinsByMissionId.TryGetValue(id, out var pin))
            {
                pin.QueueFree();
                _pinsByMissionId.Remove(id);
                GD.Print($"{Tag}   removed pin for mission={id}.");
            }
        }

        // 2. Compute cluster indices over the WHOLE snapshot — adding or
        //    removing a sibling shifts the indices of the survivors, so
        //    we re-project all pin positions on every reconcile.
        var indexed = MissionPoiLayerLogic.AssignClusterIndices(snapshot);
        var calage = WorldMapCalage.ForEmWorldMesh();

        foreach (var (mission, citySlug, indexInCluster) in indexed)
        {
            var pos = MissionPositionResolverLogic.ResolveForCity(mission, citiesById);
            if (pos is null)
            {
                GD.PushWarning(
                    $"{Tag} mission={mission.Id} target_poi='{mission.TargetPoi}' : " +
                    $"city '{citySlug}' not in referential — no POI placed.");
                continue;
            }

            var basePixel = calage.WorldMetresToRenderPixel(
                MissionPositionResolverLogic.ToWorldMetres(pos));
            var jitter = MissionPoiLayerLogic.ClusterSpiralOffset(
                indexInCluster, ClusterSpiralBaseRadiusPx);
            var finalPixel = basePixel + jitter;
            var finalPos = new Vector2(finalPixel.X, finalPixel.Y);

            if (_pinsByMissionId.TryGetValue(mission.Id, out var existing))
            {
                // Surviving pin — update its position (cluster index may
                // have shifted) and refresh its mission slot.
                existing.Position = finalPos;
                existing.MissionId = mission.Id;
                existing.WorldPositionMetres = new Vector2(pos.X, pos.Y);
                existing.QueueRedraw();
            }
            else
            {
                var pin = new MissionPin
                {
                    Name = $"MissionPin_{mission.Id}",
                    MissionId = mission.Id,
                    WorldPositionMetres = new Vector2(pos.X, pos.Y),
                    HalfSizePx = PinHalfSizePx,
                    Position = finalPos,
                    Visible = true,
                };
                board.AddOccupant(pin);
                _pinsByMissionId[mission.Id] = pin;
                GD.Print(
                    $"{Tag}   added pin for mission={mission.Id} city='{citySlug}' " +
                    $"clusterIndex={indexInCluster} worldM=({pos.X},{pos.Y}) " +
                    $"basePx=({basePixel.X:0.0},{basePixel.Y:0.0}) " +
                    $"jitterPx=({jitter.X:0.0},{jitter.Y:0.0}) " +
                    $"finalPx=({finalPixel.X:0.0},{finalPixel.Y:0.0}).");
            }
        }
    }

    /// <summary>
    /// Snapshot of every live pin's mission id and board-local pixel
    /// centre, in the order they sit in <see cref="_pinsByMissionId"/>.
    /// The <c>GameScreen</c> click router calls this on every left-click
    /// to drive <see cref="MissionPoiLayerLogic.PickClosestPin"/> — keeps
    /// the hit-test math Godot-free and the engine integration thin.
    /// </summary>
    public IReadOnlyList<(string MissionId, SysVec2 CentrePx)> SnapshotPinCentres()
    {
        var result = new List<(string, SysVec2)>(_pinsByMissionId.Count);
        foreach (var kv in _pinsByMissionId)
        {
            var p = kv.Value.Position;
            result.Add((kv.Key, new SysVec2(p.X, p.Y)));
        }
        return result;
    }

    /// <summary>
    /// Called by <c>GameScreen._UnhandledInput</c> when a left-click
    /// resolves to a mission POI hit (see
    /// <see cref="MissionPoiLayerLogic.PickClosestPin"/>). Logs the hit
    /// and emits <see cref="MissionPoiClicked"/> for downstream
    /// subscribers (J7 eM→eT bascule).
    /// </summary>
    /// <param name="missionId">The mission id the click resolved to.</param>
    /// <param name="boardLocalClick">The click point in the maquette
    /// board's local pixel space (post-routing) — the future bascule
    /// consumer may want it for an animation seed.</param>
    public void OnPinClicked(string missionId, Vector2 boardLocalClick)
    {
        if (string.IsNullOrEmpty(missionId)) return;
        GD.Print(
            $"{Tag} clicked mission={missionId} worldPos=" +
            $"({boardLocalClick.X:0.0},{boardLocalClick.Y:0.0}) " +
            $"(maquette-board local pixel ; eM→eT bascule will resolve at J7).");
        EmitSignal(SignalName.MissionPoiClicked, missionId, boardLocalClick);
    }

    /// <summary>
    /// The mission POI visual — a small gold losange with an umber
    /// outline, drawn entirely in code so it is independent of any PNG
    /// asset (same discipline as
    /// <see cref="HalfgateMarkerLayer.MarkerPin"/>). The losange shape
    /// nods at the rhombus-iso world it sits on ; the gold-on-blue and
    /// gold-on-parchment contrast keeps it legible against Mira's eM
    /// render.
    ///
    /// <para>
    /// <b>Anchoring.</b> The pin's <c>Position</c> is the centre of the
    /// drawn losange. Same convention as the city marker's tip — the
    /// hit-test simply asks "is the click within
    /// <c>PinPickRadiusPx</c> of <c>Position</c> ?" so a centre anchor
    /// is the cleanest.
    /// </para>
    /// </summary>
    public partial class MissionPin : Node2D
    {
        /// <summary>The mission this pin renders. Set by
        /// <see cref="MissionPoiLayer.ReconcilePins"/>.</summary>
        public string MissionId { get; set; } = string.Empty;

        /// <summary>World-metre position the pin projects from. Stored
        /// for downstream diagnostics ; the pin's on-screen
        /// <c>Position</c> is the projected pixel, jitter included.</summary>
        public Vector2 WorldPositionMetres { get; set; }

        /// <summary>Half the apex-to-apex extent of the drawn diamond,
        /// board-local pixels.</summary>
        public float HalfSizePx { get; set; } = 12f;

        // Wayfinders warm-earth DNA : a saturated gold/ochre fill (the
        // mission "treasure to find" colour cue), an umber-dark outline,
        // a soft parchment-cream highlight to sell the bead.
        private static readonly Color FillColor = new(0.94f, 0.74f, 0.20f, 1f);
        private static readonly Color OutlineColor = new(0.18f, 0.09f, 0.05f, 1f);
        private static readonly Color HighlightColor = new(1f, 0.96f, 0.84f, 0.85f);

        public override void _Draw()
        {
            float s = HalfSizePx;
            float outline = Mathf.Max(2f, s * 0.22f);

            // Outline = a slightly larger diamond filled in umber so the
            // gold fill sits inside a dark frame, identical to the city
            // pin's outline-then-fill order.
            var outlineDiamond = new[]
            {
                new Vector2(0f, -s - outline),
                new Vector2(s + outline, 0f),
                new Vector2(0f, s + outline),
                new Vector2(-s - outline, 0f),
            };
            DrawColoredPolygon(outlineDiamond, OutlineColor);

            var diamond = new[]
            {
                new Vector2(0f, -s),
                new Vector2(s, 0f),
                new Vector2(0f, s),
                new Vector2(-s, 0f),
            };
            DrawColoredPolygon(diamond, FillColor);

            // A small offset highlight so the diamond reads as a 3D bead
            // not a flat tile — sells the pin against Mira's painted
            // render.
            DrawCircle(new Vector2(-s * 0.20f, -s * 0.20f), s * 0.22f, HighlightColor);

            // A tiny dark pip at the centre so the pin's anchor is
            // unambiguous at a glance — mirrors the city pin's centre
            // dot.
            DrawCircle(Vector2.Zero, Mathf.Max(1.5f, s * 0.10f), OutlineColor);
        }
    }
}
