using Godot;
using Wayfinders.Client.Scenes.Iso;
using Wayfinders.Client.Services;
using Wayfinders.Client.Services.Dtos;
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
///         our board-readiness hop too. The fix : connect to
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
/// <b>The marker as a layer-3 occupant.</b> The marker node is attached via
/// <see cref="IsoBoard.AddOccupant"/> — the same Y-sorted occupants channel
/// the desk Company pawns use. It rides the maquette board, so it pans with
/// the eM map for free (it is in the board's local space, under the same
/// <c>MapCamera2D</c>). No per-frame code, no parallax — a city marker is
/// pinned <i>to the map</i>, it does not float.
/// </para>
///
/// <para>
/// <b>The marker is drawn in code, not a sprite (F6 visibility fix,
/// 2026-05-22, second round).</b> The earlier J5 build placed a
/// <see cref="Sprite2D"/> from <c>wf_e2_poi_cite_marker_active_512x512.png</c>
/// at scale 0.25. That asset is a small pin centred in a mostly-transparent
/// 512&#215;512 canvas — at scale 0.25 the actual painted pixels shrink to a
/// near-invisible speck on the 2048-wide eM mesh, which is exactly why the
/// F6 smoke could not see it. The marker is now a <see cref="MarkerPin"/>
/// nested <see cref="Node2D"/> that draws its own shape in <c>_Draw</c>: a
/// fat vivid pin (a filled head circle with a dark outline over a tapered
/// stem) sized in absolute pixels. It is independent of any PNG, cannot be
/// shrunk to nothing by a scale knob, and stands out hard against both the
/// sea-blue and the parchment-beige of the eM render. When the J6 mission
/// marker asset is signed off this can swap back to a sprite — but a code
/// pin is the correct call for a "must not be missable" diagnostic marker.
/// </para>
///
/// <para>
/// <b>Placement maths.</b> The city's world-metre
/// <see cref="WorldPositionDto"/> goes through
/// <see cref="WorldMapCalage.WorldMetresToRenderPixel"/> (the eM calage,
/// from Mira's <c>.calage.json</c> sidecar) to get a render-pixel on the
/// 2048&#215;1024 mesh. Because the maquette board's background sprite is
/// <c>Centered = false</c> at the board origin, that render-pixel <i>is</i>
/// the board-local pixel — the marker node's <c>Position</c>. All the
/// projection lives in the pure-C# <see cref="WorldMapCalage"/> — this node
/// only converts at the engine seam.
/// </para>
///
/// <para>
/// <b>Graceful degrade.</b> If <see cref="WorldMapService"/> reports a
/// failed load (backend was down at boot), or Halfgate is absent from the
/// referential, the node logs a warning and places nothing. The eM mesh
/// still renders — the map is a static asset, only the marker is
/// server-data-driven. No crash, no exception. The
/// <see cref="WorldMapService.WorldLoaded"/> signal fires on the failure
/// path too, so this node always stops waiting.
/// </para>
/// </summary>
public partial class HalfgateMarkerLayer : Node2D
{
    /// <summary>Console prefix for every diagnostic line this node emits.</summary>
    private const string Tag = "[HALFGATE-MARKER]";

    /// <summary>
    /// City slug to place a marker for. J5 ships exactly Halfgate ; exported
    /// so a future probe can re-point the node without a recompile.
    /// </summary>
    [Export]
    public string CityId { get; set; } = "halfgate";

    /// <summary>
    /// Radius of the marker pin head, in board-local (eM render) pixels. The
    /// eM mesh is 2048&#215;1024 ; a 26 px head is an unmissable pastille
    /// that does not swallow the coastline. Exported so the F6 smoke can
    /// tune it without a recompile.
    /// </summary>
    [Export]
    public float MarkerHeadRadiusPx { get; set; } = 26f;

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
        GD.Print($"{Tag} _Ready: HalfgateMarkerLayer entering tree, " +
                 $"CityId='{CityId}', headRadiusPx={MarkerHeadRadiusPx}.");

        // Condition 1 — board readiness. The parent IsoBoard builds its
        // background + projection in its own _Ready, which fires AFTER this
        // child's _Ready. Defer the board-readiness check one step so the
        // board is fully set up — see the class docstring.
        CallDeferred(MethodName.OnBoardReady);

        // Condition 2 — world referential. Subscribe to the load signal so
        // the marker is placed when the cache fills (it lands AFTER this
        // _Ready). If the cache is already warm, the board-readiness hop's
        // TryPlaceCityMarker covers it.
        _worldMapService =
            GetNodeOrNull<WorldMapService>("/root/WorldMapService");
        if (_worldMapService is null)
        {
            GD.PushWarning(
                $"{Tag} WorldMapService autoload missing — no city marker "
                + "placed.");
            return;
        }

        _worldMapService.WorldLoaded += OnWorldLoaded;
        GD.Print($"{Tag} _Ready: subscribed to WorldMapService.WorldLoaded " +
                 $"(service.IsLoaded={_worldMapService.IsLoaded} at subscribe " +
                 $"time — already-loaded fast path will fire via the board hop " +
                 $"if true).");
    }

    public override void _ExitTree()
    {
        // Disconnect the signal explicitly (trap #10). A Godot [Signal]
        // auto-disconnects when the subscriber node is freed, but being
        // explicit keeps the lifetime contract visible and is correct even
        // if this node is reparented rather than freed.
        if (_worldMapService is not null
            && GodotObject.IsInstanceValid(_worldMapService))
        {
            _worldMapService.WorldLoaded -= OnWorldLoaded;
        }
    }

    /// <summary>
    /// Board-readiness hop. Marks condition 1 satisfied, then attempts the
    /// placement — which only proceeds if the world referential is also
    /// loaded. This call ALSO covers the already-loaded fast path.
    /// </summary>
    private void OnBoardReady()
    {
        _boardReady = true;
        GD.Print($"{Tag} OnBoardReady: board-readiness hop fired " +
                 $"(_boardReady=true).");
        TryPlaceCityMarker("board-readiness hop");
    }

    /// <summary>
    /// Handler for <see cref="WorldMapService.WorldLoaded"/>. Fires on the
    /// main thread once the boot load completes (success or failure).
    /// </summary>
    private void OnWorldLoaded()
    {
        GD.Print($"{Tag} OnWorldLoaded: WorldLoaded signal RECEIVED on the " +
                 $"main thread.");
        TryPlaceCityMarker("WorldLoaded signal");
    }

    /// <summary>
    /// Places the marker iff both readiness conditions hold and it has not
    /// been placed yet. Idempotent and cheap to call from either trigger —
    /// the board-readiness hop or the world-loaded signal, in any order.
    /// </summary>
    /// <param name="trigger">
    /// Which of the two triggers called in — logged so the F6 smoke shows
    /// which condition resolved last.
    /// </param>
    private void TryPlaceCityMarker(string trigger)
    {
        bool worldCachePresent =
            _worldMapService is not null && _worldMapService.World is not null;
        GD.Print($"{Tag} TryPlaceCityMarker (trigger='{trigger}'): " +
                 $"readiness — boardReady={_boardReady}, " +
                 $"worldCachePresent={worldCachePresent} " +
                 $"(service.IsLoaded={_worldMapService?.IsLoaded.ToString() ?? "n/a"}, " +
                 $"service.LoadFailed={_worldMapService?.LoadFailed.ToString() ?? "n/a"}), " +
                 $"markerAlreadyPlaced={_markerPlaced}.");

        if (_markerPlaced)
        {
            GD.Print($"{Tag} marker NOT placed: already placed once " +
                     $"(idempotent guard).");
            return;
        }
        if (!_boardReady || _worldMapService is null
            || _worldMapService.World is null)
        {
            GD.Print($"{Tag} marker NOT placed (yet): a readiness condition " +
                     $"is still pending — the other trigger will call back in.");
            return;
        }

        var board = GetParentOrNull<IsoBoard>();
        if (board is null)
        {
            GD.PushWarning(
                $"{Tag} marker NOT placed because: parent is not an IsoBoard " +
                $"— the marker layer must be a child of the maquette board.");
            return;
        }

        // The eM mesh must be loaded as the board background — the marker
        // calage is relative to that 2048x1024 render.
        var meshSize = board.GetBackgroundTextureSizeOrZero();
        if (meshSize == Vector2.Zero)
        {
            GD.PushWarning(
                $"{Tag} marker NOT placed because: maquette board has no eM " +
                $"background texture — cannot calage the city marker.");
            return;
        }

        var city = _worldMapService.FindCity(CityId);
        if (city is null)
        {
            // World load failed OR the id is unknown. Degrade gracefully.
            _markerPlaced = true;
            GD.PushWarning(
                $"{Tag} marker NOT placed because: city '{CityId}' is not in " +
                $"the world referential (backend down at boot, or unknown id) " +
                $"— eM map renders without a city marker.");
            return;
        }

        // World metres -> render pixel on the eM mesh, via the pure-C#
        // calage. The board background sprite is Centered=false at the board
        // origin, so the render pixel IS the board-local pixel.
        var calage = WorldMapCalage.ForEmWorldMesh();
        var pixel = calage.WorldMetresToRenderPixel(
            new SysVec2(city.Position.X, city.Position.Y));
        var markerPos = new Vector2(pixel.X, pixel.Y);

        bool inFrame = pixel.X >= 0f && pixel.X <= meshSize.X
                    && pixel.Y >= 0f && pixel.Y <= meshSize.Y;

        var marker = new MarkerPin
        {
            Name = $"CityMarker_{city.Id}",
            Position = markerPos,
            Visible = true,
            HeadRadiusPx = MarkerHeadRadiusPx,
        };

        // Attach as a layer-3 occupant — the Y-sorted channel the desk pawns
        // use. The Occupants node is added to the board AFTER its Background
        // sprite, so the marker draws OVER the eM mesh.
        board.AddOccupant(marker);
        _markerPlaced = true;

        GD.Print(
            $"{Tag} marker PLACED for '{city.Name}': " +
            $"worldPos=({city.Position.X},{city.Position.Y}) m " +
            $"-> renderPixel=({pixel.X:0.0},{pixel.Y:0.0}) " +
            $"on the {meshSize.X:0}x{meshSize.Y:0} eM mesh.");
        GD.Print(
            $"{Tag}   marker node: Position={marker.Position} " +
            $"headRadiusPx={MarkerHeadRadiusPx} " +
            $"approxFootprintPx=~{MarkerHeadRadiusPx * 2f + 8f:0}x" +
            $"{MarkerHeadRadiusPx * 3.4f:0} Visible={marker.Visible} " +
            $"parentLayer='Occupants' (Y-sorted, drawn OVER the eM mesh).");
        GD.Print(
            $"{Tag}   in-frame check: renderPixel inside [0..{meshSize.X:0}] x " +
            $"[0..{meshSize.Y:0}] => IN-FRAME={inFrame}. " +
            $"NOTE: whether it is in the VISIBLE rect also depends on the " +
            $"MapCamera2D pan — the camera parks on the mesh centre " +
            $"({meshSize.X * 0.5f:0},{meshSize.Y * 0.5f:0}); Halfgate at " +
            $"({pixel.X:0},{pixel.Y:0}) sits left-and-low of centre. If the " +
            $"marker is not on screen, MMB-pan down-left to it, or it is the " +
            $"camera framing, not this node.");
        if (!inFrame)
        {
            GD.PushWarning(
                $"{Tag} marker render pixel ({pixel.X:0.0},{pixel.Y:0.0}) is " +
                $"OUTSIDE the eM mesh bounds — the Halfgate world position is " +
                $"off the rendered frame (a data/calage bug, not a draw bug).");
        }
    }

    /// <summary>
    /// The city marker visual — a vivid pin drawn entirely in code so it can
    /// never be shrunk to an invisible speck by a sprite-scale knob (the F6
    /// bug) and is independent of any PNG asset.
    ///
    /// <para>
    /// <b>Shape.</b> A tapered dark stem rising from the city point, topped
    /// by a filled circular head with a dark outline and a small white
    /// highlight. The colours are picked to read hard against both the
    /// sea-blue and the parchment-beige of Mira's eM render — a saturated
    /// vermilion head, an umber-dark outline (Wayfinders warm-earth DNA, but
    /// pushed to full contrast on purpose: this is a "you cannot miss it"
    /// marker).
    /// </para>
    ///
    /// <para>
    /// <b>Anchoring.</b> The node's <c>Position</c> is the city point on the
    /// eM render. The pin is drawn so the <i>tip of the stem</i> sits at the
    /// origin (the standard map-pin convention — the pin points at the
    /// place), with the head floating above it.
    /// </para>
    /// </summary>
    public partial class MarkerPin : Node2D
    {
        /// <summary>Radius of the pin head circle, board-local pixels.</summary>
        public float HeadRadiusPx { get; set; } = 26f;

        // Wayfinders warm-earth DNA, pushed to full contrast for a
        // must-not-be-missable diagnostic marker.
        private static readonly Color HeadColor = new(0.86f, 0.22f, 0.10f, 1f);
        private static readonly Color OutlineColor = new(0.18f, 0.09f, 0.05f, 1f);
        private static readonly Color HighlightColor = new(1f, 0.95f, 0.88f, 0.9f);

        public override void _Draw()
        {
            float r = HeadRadiusPx;
            float outline = Mathf.Max(3f, r * 0.18f);
            // The stem rises from the origin (the city point) up to the head
            // centre, which floats one-and-a-bit head-radii above.
            float stemLen = r * 2.4f;
            var headCentre = new Vector2(0f, -stemLen);

            // Stem — a tapered dark triangle: wide-ish at the head, a point
            // at the city spot.
            float stemHalfW = r * 0.36f;
            var stem = new[]
            {
                new Vector2(0f, 0f),                          // tip = city point
                new Vector2(-stemHalfW, headCentre.Y + r * 0.3f),
                new Vector2(stemHalfW, headCentre.Y + r * 0.3f),
            };
            DrawColoredPolygon(stem, OutlineColor);

            // Head — dark outline disc, then the vermilion fill on top.
            DrawCircle(headCentre, r + outline, OutlineColor);
            DrawCircle(headCentre, r, HeadColor);

            // A small offset highlight so the head reads as a 3D bead, not a
            // flat dot — sells the pin against a busy render.
            DrawCircle(
                headCentre + new Vector2(-r * 0.32f, -r * 0.32f),
                r * 0.28f, HighlightColor);

            // A tiny dark dot exactly on the city point so the pin's anchor
            // is unambiguous at a glance.
            DrawCircle(Vector2.Zero, Mathf.Max(2f, r * 0.10f), OutlineColor);
        }
    }
}
