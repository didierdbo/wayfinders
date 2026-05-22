using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
using Wayfinders.Client.Utils;
using SysVec2 = System.Numerics.Vector2;

namespace Wayfinders.Client.Scripts.Screens;

/// <summary>
/// J5 — places the Halfgate city marker on the eM world map.
///
/// <para>
/// <b>What this node is, and where it sits.</b> A small <see cref="Node2D"/>
/// dropped as a direct child of the maquette <see cref="IsoBoard"/> in
/// <c>GameScreen.tscn</c>. It owns one concern : reading the world
/// referential and placing the city marker(s) on the eM mesh. It is a
/// <b>node, not logic in GameScreen</b> — the marker concern is genuinely
/// separate from the four-layer shell, has its own lifetime, and a dedicated
/// node keeps <c>GameScreen._Ready</c> untouched (the screen shell is
/// load-bearing and large ; adding the marker as a sibling concern is the
/// composition-over-a-bigger-method call).
/// </para>
///
/// <para>
/// <b>Two independent readiness conditions (the J5 timing bug, fixed
/// 2026-05-22).</b> Placing the marker needs <i>both</i> of these, and they
/// resolve at different, unordered times :
/// <list type="number">
///   <item><b>The board is set up.</b> In Godot a child's <c>_Ready</c> fires
///         <i>before</i> its parent's. This node's parent is the maquette
///         <see cref="IsoBoard"/>, which only loads its eM background texture
///         and builds its projection inside its own <c>_Ready</c>. A single
///         <see cref="Node.CallDeferred"/> hop fixes that : the deferred call
///         runs after the whole subtree's <c>_Ready</c> pass, so
///         <see cref="IsoBoard.GetBackgroundTextureSizeOrZero"/> and
///         <see cref="IsoBoard.AddOccupant"/> are both safe.</item>
///   <item><b>The world referential is loaded.</b>
///         <see cref="WorldMapService"/> fetches <c>GET /api/world</c>
///         fire-and-forget at boot ; its cache lands via <c>CallDeferred</c>
///         <i>after</i> this whole subtree's <c>_Ready</c> pass — and after
///         our board-readiness hop too. The earlier J5 build read the cache
///         exactly once on the board-readiness hop, always saw it empty, and
///         placed nothing. The fix : connect to
///         <see cref="WorldMapService.WorldLoaded"/> and place the marker
///         when the data arrives — with an already-loaded fast path for the
///         case where the cache is warm before we subscribe.</item>
/// </list>
/// The placement is gated on both flags (<see cref="_boardReady"/> and the
/// world cache being non-null) and is triggered by whichever condition
/// resolves last. <see cref="_markerPlaced"/> guards against the
/// already-loaded fast path and the signal both firing — the marker is
/// built exactly once.
/// </para>
///
/// <para>
/// <b>The marker as a layer-3 occupant.</b> The marker sprite is attached
/// via <see cref="IsoBoard.AddOccupant"/> — the same Y-sorted occupants
/// channel the desk Company pawns use. It rides the maquette board, so it
/// pans with the eM map for free (it is in the board's local space, under
/// the same <c>MapCamera2D</c>). No per-frame code, no parallax — a city
/// marker is pinned <i>to the map</i>, it does not float.
/// </para>
///
/// <para>
/// <b>Placement maths.</b> The city's world-metre
/// <see cref="WorldPositionDto"/> goes through
/// <see cref="WorldMapCalage.WorldMetresToRenderPixel"/> (the eM calage,
/// from Mira's <c>.calage.json</c> sidecar) to get a render-pixel on the
/// 2048×1024 mesh. Because the maquette board's background sprite is
/// <c>Centered = false</c> at the board origin, that render-pixel <i>is</i>
/// the board-local pixel — the marker sprite's <c>Position</c>. The marker
/// sprite itself is <c>Centered = true</c> so its art centres on the city
/// point. All the projection lives in the pure-C# <see cref="WorldMapCalage"/>
/// — this node only converts at the engine seam.
/// </para>
///
/// <para>
/// <b>Graceful degrade.</b> If <see cref="WorldMapService"/> reports a
/// failed load (backend was down at boot), or Halfgate is absent from the
/// referential, or the marker texture fails to load, the node logs a warning
/// and places nothing. The eM mesh still renders — the map is a static
/// asset, only the marker is server-data-driven. No crash, no exception.
/// The <see cref="WorldMapService.WorldLoaded"/> signal fires on the failure
/// path too, so this node always stops waiting.
/// </para>
///
/// <para>
/// <b>J5 scope.</b> Only the static Halfgate <i>city</i> marker. The
/// clickable <i>mission</i> marker that pops after a few ticks
/// (roadmap J6 bullet 3, hit-test through the MapViewport) is deferred to
/// the next jalon — see the J5 report. This node is named generically
/// (<c>HalfgateMarkerLayer</c>) but is built to host the mission marker
/// later without a structural change.
/// </para>
/// </summary>
public partial class HalfgateMarkerLayer : Node2D
{
    /// <summary>
    /// City slug to place a marker for. J5 ships exactly Halfgate ; exported
    /// so a future probe can re-point the node without a recompile.
    /// </summary>
    [Export]
    public string CityId { get; set; } = "halfgate";

    /// <summary>
    /// <c>res://</c> path of the eM city marker art. Loaded through
    /// <see cref="AssetLoader.LoadTextureWithUserOverride"/> so a
    /// <c>user://</c> hot-swap works without a rebuild (trap #13).
    /// </summary>
    [Export]
    public string MarkerTexturePath { get; set; } =
        "res://assets/wayfinders_visual_assets/e2/wf_e2_poi_cite_marker_active_512x512.png";

    /// <summary>
    /// Uniform scale applied to the marker sprite. The marker art is
    /// 512×512 — on the 2048-wide eM mesh that is a quarter of the map
    /// width at scale 1. 0.25 brings it to a ~128 px pin, readable without
    /// swallowing the coastline. Exported so the F6 smoke can tune it.
    /// </summary>
    [Export]
    public float MarkerScale { get; set; } = 0.25f;

    /// <summary>
    /// True once the parent <see cref="IsoBoard"/> has finished its own
    /// <c>_Ready</c> (its eM background texture and projection are up). Set
    /// by the deferred board-readiness hop fired from <see cref="_Ready"/>.
    /// </summary>
    private bool _boardReady;

    /// <summary>
    /// True once the marker has been built and attached. Guards against the
    /// already-loaded fast path and the <see cref="WorldMapService.WorldLoaded"/>
    /// signal both triggering a placement — the marker is built exactly once.
    /// </summary>
    private bool _markerPlaced;

    /// <summary>
    /// Cached handle to the world referential service, kept so
    /// <see cref="_ExitTree"/> can disconnect the signal even if the node
    /// tree is being torn down. Null if the autoload was missing at
    /// <c>_Ready</c>.
    /// </summary>
    private WorldMapService? _worldMapService;

    public override void _Ready()
    {
        // Condition 1 — board readiness. The parent IsoBoard builds its
        // background + projection in its own _Ready, which fires AFTER this
        // child's _Ready. Defer the board-readiness check one step so the
        // board is fully set up — see the class docstring.
        CallDeferred(MethodName.OnBoardReady);

        // Condition 2 — world referential. Subscribe to the load signal so
        // the marker is placed when the cache fills (it lands AFTER this
        // _Ready). If the cache is already warm (a fast boot, or an editor
        // hot-reload re-fired the load), the placement still happens via
        // the board-readiness hop's TryPlaceCityMarker call below — the
        // FindCity there will already return a city.
        _worldMapService =
            GetNodeOrNull<WorldMapService>("/root/WorldMapService");
        if (_worldMapService is null)
        {
            GD.PushWarning(
                "[HalfgateMarkerLayer] WorldMapService autoload missing — "
                + "no city marker placed.");
            return;
        }

        // Subscribe BEFORE checking IsLoaded so there is no gap : if the
        // load completes between the IsLoaded read and the Connect call,
        // the signal still catches it. The reverse order could miss the
        // emission. The already-loaded branch below covers the case where
        // the load finished before we even got here.
        _worldMapService.WorldLoaded += OnWorldLoaded;
    }

    public override void _ExitTree()
    {
        // Disconnect the signal explicitly (trap #10). A Godot [Signal]
        // auto-disconnects when the subscriber node is freed, but being
        // explicit keeps the lifetime contract visible and is correct even
        // if this node is reparented rather than freed. Guard against the
        // autoload already being gone on app shutdown.
        if (_worldMapService is not null
            && GodotObject.IsInstanceValid(_worldMapService))
        {
            _worldMapService.WorldLoaded -= OnWorldLoaded;
        }
    }

    /// <summary>
    /// Board-readiness hop. Marks condition 1 satisfied, then attempts the
    /// placement — which only proceeds if the world referential is also
    /// loaded. This call ALSO covers the already-loaded fast path : if the
    /// world cache filled before this node subscribed, <see cref="OnWorldLoaded"/>
    /// never fired for us, but the cache is non-null here so the placement
    /// goes through.
    /// </summary>
    private void OnBoardReady()
    {
        _boardReady = true;
        TryPlaceCityMarker();
    }

    /// <summary>
    /// Handler for <see cref="WorldMapService.WorldLoaded"/>. Fires on the
    /// main thread once the boot load completes (success or failure). The
    /// placement only proceeds if the board is also ready — if the signal
    /// arrives before the board-readiness hop, the marker is placed by
    /// <see cref="OnBoardReady"/> instead. Either way, <see cref="_markerPlaced"/>
    /// ensures it happens once.
    /// </summary>
    private void OnWorldLoaded()
    {
        TryPlaceCityMarker();
    }

    /// <summary>
    /// Places the marker iff both readiness conditions hold and it has not
    /// been placed yet. Idempotent and cheap to call from either trigger —
    /// the board-readiness hop or the world-loaded signal, in any order.
    /// </summary>
    private void TryPlaceCityMarker()
    {
        if (_markerPlaced)
        {
            return;
        }
        if (!_boardReady || _worldMapService is null)
        {
            // One of the two conditions is still pending — the other
            // trigger will call back in.
            return;
        }

        var board = GetParentOrNull<IsoBoard>();
        if (board is null)
        {
            GD.PushWarning(
                "[HalfgateMarkerLayer] parent is not an IsoBoard — the marker "
                + "layer must be a child of the maquette board. Skipping.");
            return;
        }

        // The eM mesh must be loaded as the board background — the marker
        // calage is relative to that 2048×1024 render. If it is not there,
        // the board fell back to its placeholder state ; placing a marker
        // against a non-existent map would be meaningless.
        var meshSize = board.GetBackgroundTextureSizeOrZero();
        if (meshSize == Vector2.Zero)
        {
            GD.PushWarning(
                "[HalfgateMarkerLayer] maquette board has no eM background "
                + "texture — cannot calage the city marker. Skipping.");
            return;
        }

        var city = _worldMapService.FindCity(CityId);
        if (city is null)
        {
            // World load failed (backend down at boot — LoadFailed true) OR
            // the id is unknown. Either way: degrade gracefully, the map
            // still shows. Mark as placed so a second WorldLoaded emission
            // (idempotent re-load in dev) does not re-warn.
            _markerPlaced = true;
            GD.PushWarning(
                $"[HalfgateMarkerLayer] city '{CityId}' not in the world "
                + $"referential (backend down at boot, or unknown id) — "
                + $"eM map renders without a city marker.");
            return;
        }

        var marker = BuildMarker(city);
        if (marker is null)
        {
            // Texture load failed — BuildMarker already logged. Mark placed
            // so we do not retry on a re-emission.
            _markerPlaced = true;
            return;
        }

        // Attach as a layer-3 occupant — the Y-sorted channel the desk
        // pawns use. The marker now rides the maquette board and pans with
        // the eM map for free.
        board.AddOccupant(marker);
        _markerPlaced = true;

        GD.Print(
            $"[HalfgateMarkerLayer] placed '{city.Name}' marker at world "
            + $"({city.Position.X},{city.Position.Y}) m -> render pixel "
            + $"{marker.Position} on the {meshSize} eM mesh.");
    }

    /// <summary>
    /// Build the marker <see cref="Sprite2D"/> for a city : load its art,
    /// project its world position through the eM calage, position it.
    /// Returns null (with a logged warning) if the texture fails to load.
    /// </summary>
    private Sprite2D? BuildMarker(WorldCityDto city)
    {
        var texture = AssetLoader.LoadTextureWithUserOverride(MarkerTexturePath);
        if (texture is null)
        {
            GD.PushError(
                $"[HalfgateMarkerLayer] failed to load marker texture "
                + $"'{MarkerTexturePath}' — no marker for '{city.Name}'.");
            return null;
        }

        // World metres -> render pixel on the eM mesh, via the pure-C#
        // calage (numbers from Mira's .calage.json sidecar). The board's
        // background sprite is Centered=false at the board origin, so the
        // render pixel IS the board-local pixel.
        var calage = WorldMapCalage.ForEmWorldMesh();
        var pixel = calage.WorldMetresToRenderPixel(
            new SysVec2(city.Position.X, city.Position.Y));

        return new Sprite2D
        {
            Name = $"CityMarker_{city.Id}",
            Texture = texture,
            // Centered: the marker art centres on the city point.
            Centered = true,
            Position = new Vector2(pixel.X, pixel.Y),
            Scale = new Vector2(MarkerScale, MarkerScale),
        };
    }
}
