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
/// <b>Why a deferred placement (the node-lifetime detail that matters).</b>
/// In Godot a child's <c>_Ready</c> fires <i>before</i> its parent's. This
/// node's parent is the maquette <see cref="IsoBoard"/> — and the board only
/// loads its eM background texture and builds its projection inside its own
/// <c>_Ready</c>. So at the moment <em>this</em> node's <c>_Ready</c> runs,
/// the board is not yet set up. The fix is one <see cref="Node.CallDeferred"/>
/// hop : the deferred call runs after the whole subtree's <c>_Ready</c> pass
/// has completed, so <see cref="IsoBoard.GetBackgroundTextureSizeOrZero"/>
/// and <see cref="IsoBoard.AddOccupant"/> are both safe to call.
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
/// <b>Graceful degrade.</b> If <see cref="WorldMapService"/> has no cached
/// world (backend was down at boot), or Halfgate is absent from the
/// referential, or the marker texture fails to load, the node logs a warning
/// and places nothing. The eM mesh still renders — the map is a static
/// asset, only the marker is server-data-driven. No crash, no exception.
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

    public override void _Ready()
    {
        // The parent IsoBoard builds its background + projection in its own
        // _Ready, which fires AFTER this child's _Ready. Defer the placement
        // one step so the board is fully set up — see the class docstring.
        CallDeferred(MethodName.PlaceCityMarker);
    }

    private void PlaceCityMarker()
    {
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

        var worldMapService =
            GetNodeOrNull<WorldMapService>("/root/WorldMapService");
        if (worldMapService is null)
        {
            GD.PushWarning(
                "[HalfgateMarkerLayer] WorldMapService autoload missing — "
                + "no city marker placed.");
            return;
        }

        var city = worldMapService.FindCity(CityId);
        if (city is null)
        {
            // World not loaded yet (backend down at boot) OR the id is
            // unknown. Either way: degrade gracefully, the map still shows.
            GD.PushWarning(
                $"[HalfgateMarkerLayer] city '{CityId}' not in the world "
                + $"referential (backend down at boot, or unknown id) — "
                + $"eM map renders without a city marker.");
            return;
        }

        var marker = BuildMarker(city);
        if (marker is null)
        {
            return;
        }

        // Attach as a layer-3 occupant — the Y-sorted channel the desk
        // pawns use. The marker now rides the maquette board and pans with
        // the eM map for free.
        board.AddOccupant(marker);

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
