using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Scenes.Screens;

/// <summary>
/// E2.1 area-grid wiring partial. Owns the 8x8 intra-area grid render +
/// hover hit-test + reveal substrate + NPC portrait spawn + tooltip
/// composition. Kept in a separate partial file so the legacy
/// city-level POI dispatch in <see cref="E2AreaMap"/> stays readable.
///
/// <para>
/// <b>E2.1a strict re-scope (2026-05-16, Didier lock).</b> The first E2.1
/// pass landed all of {iso grid render, fog overlay, tuto mission pop
/// flipping the 4 central cells, NPC portrait spawn, district tooltips,
/// parchment shader overlay} in one shot. Smoke test surfaced four
/// regressions Didier flagged as "trop visible d'un coup" : tuiles
/// rendered orthogonally (not iso losange), 3 portraits visible too
/// early, mission tuto popping at boot, district tooltips on hover.
/// </para>
///
/// <para>
/// E2.1 is now re-decomposed into three jalons :
/// <list type="bullet">
///   <item><b>E2.1a</b> — iso 8x8 in pure Fog state, using the E1 tile
///         bitmap (<c>wf_e1_tile_neutral.png</c>). Pan + zoom basics.
///         <i>Nothing else visible.</i></item>
///   <item><b>E2.1b</b> — central POI marker + tuto mission pop_effect
///         flipping the 4 central cells to Partial. Parchment shader
///         overlay re-activates here.</item>
///   <item><b>E2.1c</b> — district tooltip composition (EC6 hover path)
///         + per-district tile bitmaps. NPC portraits stay out (those
///         land in E2.2 with the full Recruit + Company panel flow).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>E2.1c profond refonte (2026-05-16, Didier lock).</b> The mapping
/// district → cells dropped the 2×2 block constraint and switched to
/// per-district irregular shapes (see <see cref="AreaGridLogic"/>'s XML
/// doc). The render layer's consequences :
/// <list type="bullet">
///   <item><b>Every cell carries a placeholder bitmap.</b> 39 district
///         cells use the per-district iso bitmap
///         <c>wf_e2_grid_district_iso_&lt;suffix&gt;_256x132.png</c> ;
///         the remaining 25 unattributed neutral cells use a NEW
///         dedicated bitmap <c>wf_e2_grid_neutral_256x132.png</c>
///         (urban beige). The E1 fog bitmap is NO LONGER re-routed to the
///         neutral cells -- it stays reserved for the
///         <see cref="TileRevealState.Fog"/> visual state alone.</item>
///   <item><b>POI markers anchor on 1 of 16 zone-centres.</b> The 8×8
///         grid is partitioned into 16 axis-aligned 2×2 zones. Each
///         mission's POI marker lands at the centre of the locked zone
///         resolved by <see cref="AreaGridLogic.DistrictCentroid"/> --
///         a fractional grid coord of the form
///         <c>(2*zCol + 0.5, 2*zRow + 0.5)</c>. The zone is chosen so it
///         contains AT LEAST ONE cell of the target district (no
///         requirement that all 4 cells do).</item>
///   <item><b>Wash uniformity preserved.</b> The umber-terracotta wash
///         shader still applies to all 64 cells uniformly (parchment_alpha
///         tween 0 → 0.55 driven by the E1 tuto mission's R3 projection).
///         The district vs neutral distinction is carried by the base
///         bitmap, not by the shader uniforms.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Scope gating mechanism.</b> Every block that overshoots the E2.1a
/// scope is wrapped in <c>if (ScopeMode != "E2.1a") { ... }</c> rather
/// than deleted. The code stays at the rendezvous so when E2.1b ships,
/// we flip the constant and dégèle progressively.
/// </para>
///
/// <para>
/// <b>Iso placement.</b> Tiles are laid out using the same iso math as
/// <see cref="Wayfinders.Client.Scenes.Scratch.IsoMapE1Probe"/> :
/// <code>
///   screenX = (col - row) * IsoWStride        (IsoWStride = 128)
///   screenY = (col + row) * IsoHStride        (IsoHStride = 64)
/// </code>
/// Shifted into the positive quadrant via <see cref="IsoOriginOffsetX"/>
/// / <see cref="IsoOriginOffsetY"/>.
/// </para>
///
/// <para>
/// <b>Sprite.Offset.Y discipline.</b> Every iso tile (E1, district, AND
/// the new neutral placeholder) shares the 256×132 face-top losange + 4 px
/// slab geometry. <c>Sprite2D.Offset.Y = +2</c> (slab/2) aligns the
/// losange centre on the tile's Position.
/// </para>
/// </summary>
public partial class E2AreaMap
{
    /// <summary>
    /// E2.1 scope gate (Didier lock 2026-05-16). At E2.1a we render only
    /// the iso 8x8 grid in pure Fog state. Future values <c>"E2.1b"</c>,
    /// <c>"E2.1c"</c>, <c>"E2.2"</c> progressively dégèle the NPC
    /// portraits, the tuto mission pop, the hover tooltips, and the
    /// parchment overlay shader. Each gated block uses
    /// <c>if (ScopeMode != "E2.1a")</c> rather than being deleted so the
    /// code stays at the rendezvous for the flip.
    ///
    /// <para>
    /// <b>static readonly, not const.</b> A <c>const string</c> would
    /// trigger CS0162 "unreachable code detected" on every gated branch
    /// because the C# compiler constant-folds the literal-vs-literal
    /// comparison at build time. The static readonly form keeps the
    /// comparison runtime-evaluated, which is exactly the semantics we
    /// want : when the constant flips for E2.1b, no code disappears
    /// silently from a stale compile.
    /// </para>
    /// </summary>
    private static readonly string ScopeMode = "E2.1c";

    // -- Iso placement constants (mirror IsoMapE1Probe, see partial XML
    //    doc §"Iso placement"). The E1 tile bitmap is 256 wide x 132
    //    tall (face-top losange 256x128 + 4 px slab). Iso stride is the
    //    classic 2:1 ratio used everywhere in this codebase.

    /// <summary>Width of one iso step on the screen X axis.</summary>
    private const float IsoWStride = 128f;

    /// <summary>Height of one iso step on the screen Y axis.</summary>
    private const float IsoHStride = 64f;

    /// <summary>
    /// Sprite.Offset.Y for tile sprites : aligns the face-top losange
    /// centre on the sprite's Position by compensating for the 4 px slab
    /// at the bottom of the bitmap. Mirrors IsoMapE1Probe.SpriteOffsetY.
    /// </summary>
    private const float TileSpriteOffsetY = 2f;

    /// <summary>
    /// Texture width of the E1 tile bitmap, in pixels. Used for the bbox
    /// margin computation. The actual sprite uses
    /// <c>Centered = true</c>, so the bitmap extends half this distance
    /// to each side of its Position.
    /// </summary>
    private const int TileTextureWidthPx = 256;

    /// <summary>Texture height of the E1 tile bitmap, in pixels.</summary>
    private const int TileTextureHeightPx = 132;

    /// <summary>
    /// Iso bbox width for an 8x8 grid. Leftmost screen X is
    /// <c>(0 - (GridSize-1)) * IsoWStride = -896</c>, rightmost is
    /// <c>((GridSize-1) - 0) * IsoWStride = +896</c>, so the span is
    /// <c>1792 + tile width</c> = 2048 px once we add the half-tile
    /// extension on each side. With <see cref="IsoOriginOffsetX"/>
    /// shifting the leftmost tile centre to <c>+128</c>, the bbox sits
    /// in [0, 2048].
    /// </summary>
    private const int IsoBboxWidth =
        2 * (AreaGridLogic.GridSize - 1) * (int)IsoWStride + TileTextureWidthPx;

    /// <summary>
    /// Iso bbox height for an 8x8 grid. Topmost screen Y is
    /// <c>(0 + 0) * IsoHStride = 0</c>, bottommost is
    /// <c>((GridSize-1) + (GridSize-1)) * IsoHStride = +896</c>, so the
    /// span is <c>896 + tile height</c> = 1028 px once we add the
    /// half-tile extension. With <see cref="IsoOriginOffsetY"/> shifting
    /// the topmost tile centre to <c>+66</c>, the bbox sits in
    /// [0, 1028].
    /// </summary>
    private const int IsoBboxHeight =
        2 * (AreaGridLogic.GridSize - 1) * (int)IsoHStride + TileTextureHeightPx;

    /// <summary>
    /// Horizontal offset applied to every tile Position so the iso bbox
    /// sits in the positive quadrant. Equals half the bbox width = 1024.
    /// </summary>
    private const float IsoOriginOffsetX = IsoBboxWidth / 2f;

    /// <summary>
    /// Vertical offset applied to every tile Position so the iso bbox
    /// sits in the positive quadrant. Equals half the tile height = 66.
    /// </summary>
    private const float IsoOriginOffsetY = TileTextureHeightPx / 2f;

    /// <summary>
    /// E1 tile bitmap key in the asset map. Resolves to
    /// <c>res://assets/wayfinders_visual_assets/e1/wf_e1_tile_neutral.png</c>
    /// (256x132, face-top losange + 4 px slab). At E2.1c profond refonte
    /// (Didier lock 2026-05-16) this asset is RESERVED for the
    /// <see cref="TileRevealState.Fog"/> state alone : the spawn loop
    /// no longer routes it to unattributed cells. Kept as a constant
    /// because the E2.1b fog overlay degelé (E2.2+) will re-consume it
    /// for the fog veil layer.
    /// </summary>
    private const string AreaGridTileFogAssetKey = "e1.tile_neutral";

    /// <summary>
    /// Neutral iso tile bitmap key (E2.1c profond refonte, Didier lock
    /// 2026-05-16). Resolves to
    /// <c>res://assets/wayfinders_visual_assets/e2_area_grid/wf_e2_grid_neutral_256x132.png</c>
    /// -- the new dedicated placeholder for the 25 unattributed
    /// "space-between-quartiers" cells. Same 256×132 face-top losange
    /// geometry as the E1 fog bitmap (alpha mask reused at script-gen
    /// time, see <c>scripts/gen_e2_area_grid_placeholders.py</c>) but a
    /// distinct base colour (urban sand-beige) so the unattributed cells
    /// read visually distinct from BOTH the per-district iso bitmaps AND
    /// the E1 fog veil.
    ///
    /// <para>
    /// <b>Why a new asset and not a reuse of E1's bitmap.</b> Didier's
    /// 2026-05-16 brief explicitly forbids reusing <c>e1.tile_neutral</c>
    /// for the unattributed cells : "on prend un placeholder neutre par
    /// défaut mais pas celui du fog". The E1 fog bitmap is the visual
    /// vocabulary of the FOG reveal state ; giving it a second meaning
    /// (= "space between quartiers") collapses the two semantics on the
    /// same pixels and makes the future fog overlay degelé (E2.2+) read
    /// as a no-op. New bitmap, new semantic, clean seam.
    /// </para>
    /// </summary>
    private const string AreaGridTileNeutralAssetKey = "e2.area_grid.tile.neutral";

    /// <summary>
    /// World-space size of one grid cell, in pixels. Pre-E2.1a (orthogonal
    /// layout) used this for tile placement -- still referenced by the
    /// gated NPC portrait spawn for cell-centre computation. Kept at 256.
    /// </summary>
    private const int CellPixelSize = 256;

    /// <summary>
    /// World-space total size of the 8x8 grid (cell × grid). Legacy
    /// orthogonal bbox -- only consumed by the gated NPC portrait
    /// PortraitCell-to-world translation. Iso layout uses
    /// <see cref="IsoBboxWidth"/> / <see cref="IsoBboxHeight"/> instead.
    /// </summary>
    private const int GridPixelSize = CellPixelSize * AreaGridLogic.GridSize;

    /// <summary>
    /// Path of the area-grid tile overlay shader. Loaded once per
    /// Configure call. <b>Unused at E2.1a</b> (the parchment overlay is
    /// dégelé in E2.1b) but kept here so the flip is a one-line constant
    /// change.
    /// </summary>
    private const string AreaGridTileShaderPath = "res://shaders/area_grid_tile_overlay.gdshader";

    /// <summary>
    /// Marker shown above the centre POI footprint when the boot tuto
    /// mission has fired its pop_effect. <b>Unused at E2.1a</b> (the POI
    /// marker is dégelé in E2.1b alongside the mission pop).
    /// </summary>
    private const string AreaGridPoiMarkerAssetKey = "e2.area_grid.poi_marker";

    /// <summary>
    /// Fog veil tile texture from the E2 placeholder set. <b>Unused at
    /// E2.1a</b> -- at E2.1a all 64 tiles use the E1 bitmap directly as
    /// the fog state. The E2 fog overlay layer is dégelé in E2.1b.
    /// </summary>
    private const string AreaGridFogAssetKey = "e2.area_grid.fog";

    // -- Runtime nodes spawned by BuildAreaGrid. Field-bag pattern
    //    (no nested struct) to keep field surface flat and the
    //    _ExitTree disconnect path straightforward.

    private Node2D? _areaGridLayer;
    private Node2D? _tileBaseLayer;
    private Node2D? _tileFogLayer;
    private Node2D? _tileHitTestLayer;
    private Sprite2D? _poiMarker;
    private Node2D? _npcPortraitLayer;

    /// <summary>
    /// E2-layer POI marker hotspot roots, one per active
    /// <see cref="HalfgateE2Mission"/>. Spawned by
    /// <c>SpawnE2MissionPoiMarkers</c> at E2.1c+ ; each hotspot sits at
    /// the iso-screen position of its target district's centroid
    /// (resolved via <see cref="AreaGridLogic.DistrictCentroid"/>).
    ///
    /// <para>
    /// <b>E2.2 step 1 (2026-05-17) refonte.</b> Pre-E2.2 the markers were
    /// bare <see cref="Sprite2D"/> nodes (lecture-only at E2.1c). Step 1
    /// wraps each marker in a <see cref="Node2D"/> hotspot with a
    /// <see cref="Sprite2D"/> visual + <see cref="Area2D"/> hit-test so
    /// hover surfaces the mission tooltip. The hotspot Node2D is what
    /// this list now holds ; the inner Sprite2D and Area2D live as
    /// children, parallel to E1WorldMap's POI spawn pattern.
    /// </para>
    ///
    /// <para>
    /// <b>Per-marker lookups land in two sibling dicts.</b>
    /// <see cref="_e2MarkerHotspots"/> keys hotspot roots by
    /// <see cref="HalfgateE2Mission.PoiId"/> so the hover handler can
    /// fetch <c>GlobalPosition</c> for the tooltip anchor without
    /// scanning the scene tree. <see cref="_e2MarkerHandlers"/> keeps the
    /// captured <see cref="Action"/> references so <c>TearDownAreaGrid</c>
    /// disconnects with the exact same delegates used at wire time
    /// (Risk #1 captured-reference signal-leak discipline, same shape as
    /// <c>_tileHitHandlers</c>).
    /// </para>
    /// </summary>
    private readonly List<Node2D> _e2PoiMarkers = new();

    /// <summary>
    /// Reverse lookup poi_id -> hotspot Node2D root. Keyed by
    /// <see cref="HalfgateE2Mission.PoiId"/> (e.g.
    /// <c>"halfgate.intramuros"</c>). The hover handler reads
    /// <c>GlobalPosition</c> off the hotspot for the tooltip anchor.
    /// Empty at E2.1a / E2.1b ; populated from E2.1c onward.
    /// </summary>
    private readonly Dictionary<string, Node2D> _e2MarkerHotspots = new();

    /// <summary>
    /// Per-marker captured handlers so <c>TearDownAreaGrid</c> can
    /// disconnect with the exact lambda references used at wire time.
    /// Keyed by <see cref="HalfgateE2Mission.PoiId"/>. Method-group
    /// disconnect would not match because each lambda closes over a
    /// distinct PoiId (Risk #1, same pattern as
    /// <see cref="_tileHitHandlers"/>). Empty at E2.1a / E2.1b ;
    /// populated from E2.2 step 1 (2026-05-17) onward.
    /// </summary>
    private readonly Dictionary<string, (Action mouseEntered, Action mouseExited)> _e2MarkerHandlers = new();

    /// <summary>
    /// Per-cell base-tile Sprite2D, keyed by grid coord. At E2.1a the
    /// ShaderMaterial slot is empty (no parchment overlay) ; from E2.1b
    /// onward each sprite carries its per-cell ShaderMaterial with the
    /// <c>parchment_alpha</c> uniform.
    /// </summary>
    private readonly Dictionary<GridCoord, Sprite2D> _tileBaseSprites = new();

    /// <summary>Per-cell fog overlay Sprite2D, toggled by reveal state.
    /// Empty at E2.1a (the fog overlay layer is dégelé in E2.1b).</summary>
    private readonly Dictionary<GridCoord, Sprite2D> _tileFogSprites = new();

    /// <summary>Per-cell Area2D hit-test, owns the MouseEntered handler.
    /// Empty at E2.1a (hit-test layer is dégelé in E2.1c).</summary>
    private readonly Dictionary<GridCoord, Area2D> _tileHitAreas = new();

    /// <summary>Captured handlers per cell so _ExitTree disconnects cleanly.
    /// Empty at E2.1a.</summary>
    private readonly Dictionary<GridCoord, (Action mouseEntered, Action mouseExited)> _tileHitHandlers = new();

    /// <summary>Per-NPC portrait root Node2D, keyed by NPC id. Empty at
    /// E2.1a -- portraits land in E2.2.</summary>
    private readonly Dictionary<string, Node2D> _npcPortraitRoots = new();

    /// <summary>Per-NPC captured handlers. Empty at E2.1a.</summary>
    private readonly Dictionary<string, (Action mouseEntered, Action mouseExited)> _npcHoverHandlers = new();

    /// <summary>
    /// The reveal-render controller for the area-grid. Null at E2.1a
    /// (gated -- no fog->partial transitions happen, nothing to render).
    /// </summary>
    private TileRevealRenderController? _areaGridRevealController;

    /// <summary>
    /// Shader resource. Null at E2.1a (parchment overlay gated).
    /// </summary>
    private Shader? _areaGridTileShader;

    /// <summary>
    /// Build the 8x8 grid render at the current <see cref="ScopeMode"/>.
    /// At E2.1a this is just the 64 iso-placed fog tiles ; from E2.1b
    /// onward this also wires the POI marker, mission pop, hit-test
    /// layer, parchment overlay, etc. Idempotent : safe to call after a
    /// prior teardown.
    /// </summary>
    private void BuildAreaGrid()
    {
        TearDownAreaGrid();

        // 1. Shader load -- gated past E2.1a. Defensive : log error and
        //    skip the overlay if missing once the gate opens.
        if (ScopeMode != "E2.1a")
        {
            _areaGridTileShader = ResourceLoader.Load<Shader>(AreaGridTileShaderPath);
            if (_areaGridTileShader is null)
            {
                GD.PushError(
                    $"[E2AreaMap] BuildAreaGrid: failed to load shader at {AreaGridTileShaderPath} -- " +
                    $"parchment overlay disabled this run, base tiles + fog still render.");
            }
        }

        // 2. Spawn the AreaGridLayer Node2D under MapPan2DComponent.WorldRoot.
        _areaGridLayer = new Node2D { Name = "AreaGridLayer", ZIndex = 1 };
        _panComponent.WorldRoot.AddChild(_areaGridLayer);

        _tileBaseLayer = new Node2D { Name = "TileBaseLayer", ZIndex = 0 };
        // Y-sort matches the IsoMapE1Probe discipline : tiles with larger
        // Position.Y (closer iso) paint above tiles with smaller Position.Y
        // (further iso) -- otherwise the south tiles of the grid would
        // peek through the north tiles' bottom slab.
        _tileBaseLayer.YSortEnabled = true;
        _areaGridLayer.AddChild(_tileBaseLayer);

        // 3. Hit-test layer + fog overlay layer.
        // Deferred until E2.2 (district hover + click + tooltips). Brief
        // 2026-05-16 explicitly locks "pas de hover, pas de tooltip, pas
        // d'interactivité" at E2.1c -- the per-mission POI markers added
        // at E2.1c are lecture-only Sprite2D, no Area2D attached. The
        // parchment overlay shader on each base sprite carries the
        // partial-state visual at E2.1b + E2.1c entirely -- no separate
        // fog veil needed either.
        if (ScopeMode != "E2.1a" && ScopeMode != "E2.1b" && ScopeMode != "E2.1c")
        {
            _tileFogLayer = new Node2D { Name = "TileFogLayer", ZIndex = 1 };
            _tileHitTestLayer = new Node2D { Name = "TileHitTestLayer", ZIndex = 2 };
            _areaGridLayer.AddChild(_tileFogLayer);
            _areaGridLayer.AddChild(_tileHitTestLayer);
        }

        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        // At E2.1a / E2.1b / E2.1c the fog overlay layer is gated off (see
        // step 3) ; the fogTexture variable is kept for future E2.2 degelé
        // when a separate fog veil layer re-enters the render.
        // -- E2.1a/b : every cell uses the E1 fog bitmap as its base
        //    (Didier brief, visual continuity from L1 to L2).
        // -- E2.1c profond refonte (2026-05-16) : the spawn loop branches
        //    per cell between (a) the per-district iso bitmap for the 39
        //    attributed cells and (b) the NEW neutral placeholder for the
        //    25 unattributed cells. The E1 fog bitmap is no longer routed
        //    to neutral cells -- it stays reserved for the
        //    TileRevealState.Fog visual state alone (Didier 2026-05-16 :
        //    "on prend un placeholder neutre par défaut mais pas celui du
        //    fog").
        var fogTexture = (ScopeMode == "E2.1a" || ScopeMode == "E2.1b" || ScopeMode == "E2.1c")
            ? assetResolver.Resolve(AreaGridTileFogAssetKey)
            : assetResolver.Resolve(AreaGridFogAssetKey);
        var tileE1Texture = assetResolver.Resolve(AreaGridTileFogAssetKey);
        var tileNeutralTexture = assetResolver.Resolve(AreaGridTileNeutralAssetKey);

        // 4. Spawn the 64 tile sprites in iso space.
        foreach (var coord in AreaGridLogic.AllCells())
        {
            SpawnTile(coord, assetResolver, fogTexture, tileE1Texture, tileNeutralTexture);
        }

        // 5. Legacy single-POI-marker -- the field is kept for the
        //    future "central tuto POI" that flips visible on a specific
        //    success event. NOT touched at E2.1c -- the E2.1c brief
        //    explicitly delivers per-district markers via the loop in
        //    step 5-bis, not a single central marker. Gated to E2.2+ for
        //    the eventual end-of-tuto reveal moment.
        if (ScopeMode != "E2.1a" && ScopeMode != "E2.1b" && ScopeMode != "E2.1c")
        {
            _poiMarker = new Sprite2D
            {
                Name = "PoiMarker",
                Texture = assetResolver.Resolve(AreaGridPoiMarkerAssetKey),
                Centered = true,
                ZIndex = 5,
                Position = IsoCellCentre(AreaGridLogic.Centre),
                Visible = false,
            };
            _areaGridLayer.AddChild(_poiMarker);
        }

        // 5-bis. Per-district POI markers, one per active E2 mission --
        //    THE E2.1c deliverable (Didier brief 2026-05-16). Reads the
        //    mock authoring in HalfgateE2MissionAuthoring.All, resolves
        //    each mission's TargetDistrict to a centroid anchor via
        //    AreaGridLogic.DistrictCentroid (= one of 16 zone-centres at
        //    E2.1c profond refonte), and spawns one Sprite2D per mission
        //    at the iso-screen position of that centroid. No hover, no
        //    click, no tooltip -- lecture only at E2.1c (interactivity
        //    lands at E2.2+).
        if (ScopeMode != "E2.1a" && ScopeMode != "E2.1b")
        {
            SpawnE2MissionPoiMarkers(assetResolver);
        }

        // 6. NPC portraits -- gated past E2.1c (land in E2.2 with the
        //    Recruit + Company panel flow). Didier brief 2026-05-16 :
        //    "Pas de portraits PNJ" at E2.1b AND E2.1c (the latter
        //    delivers per-district markers only, lecture-only -- the
        //    recruit flow that needs the portraits arrives at E2.2).
        if (ScopeMode != "E2.1a" && ScopeMode != "E2.1b" && ScopeMode != "E2.1c")
        {
            _npcPortraitLayer = new Node2D { Name = "NpcPortraitLayer", ZIndex = 6 };
            _areaGridLayer.AddChild(_npcPortraitLayer);
            foreach (var npc in HalfgateNpcAuthoring.All)
            {
                SpawnNpcPortrait(npc, assetResolver);
            }
        }

        // 7. Reveal-render controller -- gated past E2.1a. With no
        //    fog->partial transitions in E2.1a, the controller would
        //    subscribe to GameState.TileRevealStateChanged and receive
        //    no events anyway, but gating it explicitly keeps the boot
        //    log clean.
        if (ScopeMode != "E2.1a")
        {
            _areaGridRevealController = new TileRevealRenderController
            {
                Name = "AreaGridRevealController",
                OnRevealLevelChanged = OnRevealLevelTweenStep,
            };
            _areaGridLayer.AddChild(_areaGridRevealController);
        }

        // Diagnostic surface : count district vs neutral cells so the
        // boot log surfaces the visual-ratio invariant Didier locked at
        // E2.1c profond refonte (39 district + 25 neutral = 64 total).
        int districtCellCount = 0, neutralCellCount = 0;
        foreach (var c in AreaGridLogic.AllCells())
        {
            if (AreaGridLogic.IsInDistrict(c)) districtCellCount++;
            else neutralCellCount++;
        }

        GD.Print(
            $"[E2AreaMap] BuildAreaGrid: scope={ScopeMode}, " +
            $"tiles={_tileBaseSprites.Count} " +
            $"(district={districtCellCount}, neutral={neutralCellCount}), " +
            $"portraits={_npcPortraitRoots.Count}, " +
            $"hit_test_cells={_tileHitAreas.Count}, " +
            $"shader_loaded={_areaGridTileShader is not null}, " +
            $"poi_marker={_poiMarker is not null}, " +
            $"e2_poi_markers={_e2PoiMarkers.Count}, " +
            $"render_controller={_areaGridRevealController is not null}");
    }

    /// <summary>
    /// Translate a grid coord into the iso-placed world-space centre of
    /// the cell. Same formula IsoMapE1Probe uses, shifted by
    /// (<see cref="IsoOriginOffsetX"/>, <see cref="IsoOriginOffsetY"/>)
    /// to land in the camera's positive quadrant.
    /// </summary>
    private static Vector2 IsoCellCentre(GridCoord coord)
    {
        float screenX = (coord.Col - coord.Row) * IsoWStride + IsoOriginOffsetX;
        float screenY = (coord.Col + coord.Row) * IsoHStride + IsoOriginOffsetY;
        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// Iso-translate a FRACTIONAL grid position (e.g. a district zone
    /// centroid like (2.5, 2.5) for Intramuros) into the same iso-placed
    /// world-space the integer cell centres live in. Same formula as
    /// <see cref="IsoCellCentre"/> ; the only difference is the input
    /// type. Used by <see cref="SpawnE2MissionPoiMarkers"/> to land a
    /// POI marker BETWEEN cells (= at a zone centre).
    ///
    /// <para>
    /// <b>Why a second method and not a single overload.</b> Keeping
    /// <see cref="IsoCellCentre"/> on <see cref="GridCoord"/> protects
    /// the integer-cell call sites from accidental fractional drift (the
    /// E2 cell-spawn loop in <see cref="SpawnTile"/> calls IsoCellCentre
    /// 64 times -- any silent (float, float) cast there would shift every
    /// tile by sub-pixel rounding and the iso losanges would never align
    /// edge-to-edge again). The two-method seam is the explicit signal
    /// "this caller intentionally uses a fractional anchor".
    /// </para>
    /// </summary>
    private static Vector2 IsoFractionalCentre(float col, float row)
    {
        float screenX = (col - row) * IsoWStride + IsoOriginOffsetX;
        float screenY = (col + row) * IsoHStride + IsoOriginOffsetY;
        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// Spawn one POI marker hotspot per active E2-layer mission. Reads
    /// the mock authoring in <see cref="HalfgateE2MissionAuthoring.All"/>,
    /// resolves each mission's <see cref="HalfgateE2Mission.TargetDistrict"/>
    /// to a fractional zone-centre centroid via
    /// <see cref="AreaGridLogic.DistrictCentroid"/>, translates the
    /// centroid into iso-screen space via <see cref="IsoFractionalCentre"/>,
    /// and instantiates one <see cref="Node2D"/> hotspot per mission
    /// under <see cref="_areaGridLayer"/>.
    ///
    /// <para>
    /// <b>E2.1c profond refonte (2026-05-16).</b> Each centroid is
    /// guaranteed to sit on one of the 16 zone-centres
    /// (= <c>(2*zCol + 0.5, 2*zRow + 0.5)</c>), AND the picked zone is
    /// guaranteed to contain at least one cell of the target district.
    /// </para>
    ///
    /// <para>
    /// <b>E2.2 step 1 (2026-05-17 -- this slice).</b> The bare Sprite2D
    /// at E2.1c is now a <see cref="Node2D"/> hotspot containing a
    /// <see cref="Sprite2D"/> visual + <see cref="Area2D"/> hit-test
    /// (with <see cref="CollisionShape2D"/> + <see cref="RectangleShape2D"/>).
    /// MouseEntered fires <see cref="OnE2PoiMarkerHoverIn"/>, MouseExited
    /// fires <see cref="OnE2PoiMarkerHoverOut"/>. The shape matches the
    /// marker's visible footprint with a small fudge factor for player
    /// comfort -- losange centres are not pixel-precise hit targets.
    /// </para>
    ///
    /// <para>
    /// <b>Per-marker hit-box size.</b> The POI marker bitmap is 192x192
    /// pixels (Mira-baked anchor + ring). We pick a 128x96 rectangular
    /// hit-box centered on the marker so the player can hover slightly
    /// above or below the ring without losing the tooltip. Smaller than
    /// the bitmap (192) so two adjacent markers' hit-boxes do not
    /// overlap when the zoom factor squishes them visually closer.
    /// </para>
    ///
    /// <para>
    /// <b>What still does NOT happen at E2.2 step 1.</b> No click handler
    /// (left-click is step 3's "open mission panel" affordance). No
    /// in-tooltip hover-mission-highlight (step 2). No mission-panel
    /// slide-in (step 3). The hotspot is hover-only this step.
    /// </para>
    /// </summary>
    private void SpawnE2MissionPoiMarkers(AssetResolver assetResolver)
    {
        var markerTexture = assetResolver.Resolve(AreaGridPoiMarkerAssetKey);

        foreach (var mission in HalfgateE2MissionAuthoring.All)
        {
            var centroid = AreaGridLogic.DistrictCentroid(mission.TargetDistrict);
            var screenCentre = IsoFractionalCentre(centroid.Col, centroid.Row);

            // Hotspot root carries the Position so children sit relative
            // to it. ZIndex stacks the marker above the tile layer (the
            // tiles are at ZIndex = col within Y-sort, max 7 ; marker at
            // 5 reads above any tile at the same Y-sort band).
            var hotspot = new Node2D
            {
                // Name encodes mission_id so the remote scene inspector
                // makes the marker easy to find. Stable across runs.
                Name = $"E2PoiMarker_{mission.MissionId}",
                Position = screenCentre,
                ZIndex = 5,
            };

            var sprite = new Sprite2D
            {
                Name = "Visual",
                Texture = markerTexture,
                Centered = true,
                // LinearWithMipmaps matches the tile rendering : at the
                // default zoom the marker's ring edge reads cleaner than
                // Nearest. The 192x192 source bitmap is over-sampled at
                // the rendered ~48 px so mipmaps win on the downscale.
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                Visible = true,
            };
            hotspot.AddChild(sprite);

            // E2.2 step 1 -- Area2D hit-test for hover. Mirrors the E1
            // POI spawn pattern in E1WorldMap.SpawnPois and the E2 legacy
            // SpawnPois in this same partial's E2AreaMap.cs. The shape is
            // a RectangleShape2D sized smaller than the visible bitmap so
            // adjacent markers do not overlap their hit-boxes at low zoom.
            var area = new Area2D
            {
                Name = "Hitbox",
                InputPickable = true,
            };
            var shape = new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = new Vector2(128f, 96f) },
            };
            area.AddChild(shape);
            hotspot.AddChild(area);

            _areaGridLayer!.AddChild(hotspot);

            // Capture poi_id once per closure so the hover handler reads
            // the right marker entry. Lambdas kept in _e2MarkerHandlers
            // so the _ExitTree path disconnects with the exact references
            // used here (Risk #1 captured-reference discipline).
            var poiId = mission.PoiId;
            Action mouseEntered = () => OnE2PoiMarkerHoverIn(poiId);
            Action mouseExited = () => OnE2PoiMarkerHoverOut(poiId);
            area.MouseEntered += mouseEntered;
            area.MouseExited += mouseExited;

            _e2PoiMarkers.Add(hotspot);
            _e2MarkerHotspots[poiId] = hotspot;
            _e2MarkerHandlers[poiId] = (mouseEntered, mouseExited);
        }

        GD.Print(
            $"[E2AreaMap] SpawnE2MissionPoiMarkers : spawned " +
            $"{_e2PoiMarkers.Count} hotspot(s) for active E2 missions " +
            $"({string.Join(", ", HalfgateE2MissionAuthoring.All.Select(m => m.MissionId))}), " +
            $"hover wired={_e2MarkerHandlers.Count}.");
    }

    /// <summary>
    /// E2.2 step 2 (2026-05-17) -- hover-in handler on an E2 POI marker.
    /// Builds the structured <see cref="MissionTooltipPayload"/> for the
    /// hovered POI and feeds it to
    /// <see cref="HoverTooltipController.RequestMissionTooltip"/> -- the
    /// new "rows are interactive Buttons" path. The legacy single-string
    /// path (<see cref="HalfgateE2MissionTooltip.Compose"/> +
    /// <c>RequestTooltip</c>) is no longer the route for the E2 POI
    /// markers ; it stays consumed by E1 POI / E2 cell / E2 NPC portrait
    /// / E3 POI which still want the simple Label render.
    ///
    /// <para>
    /// <b>Click callback wiring (step 2 scaffold, step 3 swap-in).</b> The
    /// <c>onRowClicked</c> delegate is wired to <see cref="OnMissionRowClicked"/>
    /// which logs the missionId and returns. Step 3 will swap the body
    /// for the mission-panel slide-in. The seam is the delegate ; no
    /// further controller surface is needed.
    /// </para>
    ///
    /// <para>
    /// <b>Empty-mission contract.</b> If no mission is found for the
    /// poi_id (drift between spawn + lookup), <c>RequestMissionTooltip</c>
    /// is called with a payload whose Rows list is empty AND the
    /// controller treats that as a no-op (no tooltip surfaces). Symmetric
    /// with the step 1 empty-string short-circuit.
    /// </para>
    ///
    /// <para>
    /// <b>District display name resolution.</b> The hovered marker's
    /// district is taken from the FIRST matching mission. At E2.1c+ each
    /// marker is one mission, so the choice is unambiguous. When E2.2+
    /// lands multiple missions per POI, the marker's
    /// <see cref="HalfgateE2Mission.TargetDistrict"/> is identical
    /// across siblings (the POI IS the district anchor), so picking the
    /// first is still the right answer.
    /// </para>
    /// </summary>
    private void OnE2PoiMarkerHoverIn(string poiId)
    {
        var missions = HalfgateE2MissionTooltip
            .FindByPoiId(HalfgateE2MissionAuthoring.All, poiId)
            .ToList();
        if (missions.Count == 0) return;

        var districtLabel = DistrictTypeHelpers.DisplayName(missions[0].TargetDistrict);
        var payload = MissionTooltipRows.Compose(districtLabel, missions);
        if (payload.Rows.Count == 0) return;

        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        if (tooltipController is null) return;

        var anchor = _e2MarkerHotspots.TryGetValue(poiId, out var hotspot)
            ? hotspot.GetGlobalTransformWithCanvas().Origin
            : Vector2.Zero;
        tooltipController.RequestMissionTooltip(payload, anchor, OnMissionRowClicked);
    }

    /// <summary>
    /// E2.2 step 2 (2026-05-17) -- hover-out handler. Sends the cancel
    /// to the mission-tooltip path (NOT the simple <c>CancelTooltip</c>
    /// path). The cancel respects the sticky bridge : if the cursor is
    /// on the tooltip itself, the controller keeps the tooltip alive ;
    /// the actual fade-out only runs when both the POI marker AND the
    /// tooltip are un-hovered.
    /// </summary>
    private void OnE2PoiMarkerHoverOut(string _)
    {
        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        tooltipController?.CancelMissionTooltip();
    }

    /// <summary>
    /// E2.2 step 2 (2026-05-17) -- click handler stub on a mission row.
    /// Wired into the tooltip controller via the
    /// <c>RequestMissionTooltip</c> callback parameter. At step 2 the
    /// body is a no-op-with-log : the spec is "row is connectable, not
    /// connected to a panel yet". Step 3 will swap the body for the
    /// mission-panel slide-in (the seam is THIS delegate -- the
    /// controller does not need any further surface).
    /// </summary>
    private void OnMissionRowClicked(string missionId)
    {
        GD.Print(
            $"[E2AreaMap] OnMissionRowClicked : missionId='{missionId}' " +
            $"(step 2 scaffold -- no panel yet, slide-in lands at step 3).");
    }

    /// <summary>
    /// Spawn one cell's tile sprite at its iso-placed world centre.
    ///
    /// <para>
    /// <b>Base bitmap selection (E2.1c profond refonte, Didier lock
    /// 2026-05-16).</b>
    /// </para>
    /// <list type="bullet">
    ///   <item>At <b>E2.1a + E2.1b</b>, all 64 cells use the E1 fog bitmap
    ///         (visual continuity with the L1 tuto, no per-district
    ///         identity at these jalons).</item>
    ///   <item>At <b>E2.1c (profond)</b>, the 39 cells covered by the
    ///         district cell lists (see
    ///         <see cref="AreaGridLogic.IsInDistrict"/>) use the
    ///         per-district iso bitmap
    ///         <c>wf_e2_grid_district_iso_&lt;suffix&gt;_256x132.png</c> ;
    ///         the 25 remaining unattributed cells use the NEW dedicated
    ///         neutral placeholder
    ///         <c>wf_e2_grid_neutral_256x132.png</c>. The E1 fog bitmap
    ///         is NO LONGER re-routed to the neutral cells (it stays
    ///         reserved for the fog reveal state alone).</item>
    ///   <item>At <b>E2.2+</b>, every cell uses its per-district bitmap.
    ///         The unattributed-cell branch then needs an explicit
    ///         district assignment or the contract widens here.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Iso losange shape preserved across all three bitmap families.</b>
    /// The neutral placeholder is generated from the same 256×132 face-top
    /// losange + 4 px slab geometry as the E1 fog bitmap (its alpha mask
    /// is reused at script-gen time -- see
    /// <c>scripts/gen_e2_area_grid_placeholders.py</c>). The per-district
    /// iso bitmaps share the same geometry. <c>Sprite.Offset.Y = +2</c>
    /// applies uniformly.
    /// </para>
    /// </summary>
    private void SpawnTile(
        GridCoord coord,
        AssetResolver assetResolver,
        Texture2D fogTexture,
        Texture2D tileE1Texture,
        Texture2D tileNeutralTexture)
    {
        Texture2D baseTexture;
        if (ScopeMode == "E2.1a" || ScopeMode == "E2.1b")
        {
            // Every cell renders the E1 fog bitmap : no per-district
            // visual identity at these jalons.
            baseTexture = tileE1Texture;
        }
        else if (ScopeMode == "E2.1c")
        {
            // E2.1c profond refonte (2026-05-16) : split the 64 cells
            // into 39 district cells (per-district iso bitmap) + 25
            // unattributed cells (NEW neutral iso bitmap). `IsInDistrict`
            // is the explicit discriminator -- see its XML doc for why
            // we don't use `ResolveDistrictType` here (the Outskirts
            // fallback collides with the legit Outskirts cells).
            baseTexture = AreaGridLogic.IsInDistrict(coord)
                ? assetResolver.Resolve(
                    $"e2.area_grid.tile.{DistrictTypeHelpers.AssetKeySuffix(AreaGridLogic.ResolveDistrictType(coord))}")
                : tileNeutralTexture;
        }
        else
        {
            // E2.2+ : every cell uses its per-district bitmap (the
            // unattributed cells will need a district assignment by
            // then, or the contract will need explicit handling here).
            baseTexture = assetResolver.Resolve(
                $"e2.area_grid.tile.{DistrictTypeHelpers.AssetKeySuffix(AreaGridLogic.ResolveDistrictType(coord))}");
        }

        var cellCentre = IsoCellCentre(coord);

        var baseSprite = new Sprite2D
        {
            Name = $"TileBase_{coord.Col}_{coord.Row}",
            Texture = baseTexture,
            Centered = true,
            // Y-sort alignment : the bitmap is face-top 256x128 + 4 px
            // slab. Offset.Y = +2 (slab/2) puts the losange centre on
            // Position so Y-sort orders against the right reference.
            Offset = new Vector2(0f, TileSpriteOffsetY),
            Position = cellCentre,
            // LinearWithMipmaps matches IsoMapE1Probe -- the iso losange
            // edges read cleaner than Nearest at the default zoom.
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
            // Per-tile ZIndex tiebreaker within a Y-sort band : larger
            // col paints above smaller col at the same Position.Y. Same
            // strategy IsoMapE1Probe uses.
            ZIndex = coord.Col,
        };
        if (ScopeMode != "E2.1a" && _areaGridTileShader is not null)
        {
            var material = new ShaderMaterial { Shader = _areaGridTileShader };
            material.SetShaderParameter("parchment_alpha", 0.0f);
            // Per-cell district tint -- E2.1c refonte (Didier lock
            // 2026-05-16) : pushed to identity white. The original Option
            // C path multiplied the base bitmap by a per-district tint to
            // synthesize 6 visually-distinct zones from a single neutral
            // bitmap. Didier reviewed the visual result and ratified a
            // simpler MVE shape : uniform umber-terracotta wash across all
            // 64 cells, with per-district visual identity to come from
            // the per-district base bitmap (district cells) and from the
            // new neutral placeholder (unattributed cells). The tint
            // uniform STAYS in the shader (no surface churn for Mira's
            // pass) ; we push white = vec4(1,1,1,1) = identity so the
            // multiplication is a no-op at E2.1c.
            material.SetShaderParameter("district_tint", new Color(1.0f, 1.0f, 1.0f, 1.0f));
            baseSprite.Material = material;
        }
        _tileBaseLayer!.AddChild(baseSprite);
        _tileBaseSprites[coord] = baseSprite;

        // -- Fog overlay + hit-test deferred until E2.2 (brief
        //    2026-05-16 locks "no hover, no tooltip, no interactivity"
        //    at E2.1c). At E2.1a + E2.1b + E2.1c the base sprite alone
        //    carries the visible state ; the fog veil + hover hit-test
        //    land alongside the recruit + tooltip dégelé.
        if (ScopeMode == "E2.1a" || ScopeMode == "E2.1b" || ScopeMode == "E2.1c") return;

        var fogSprite = new Sprite2D
        {
            Name = $"TileFog_{coord.Col}_{coord.Row}",
            Texture = fogTexture,
            Centered = true,
            Offset = new Vector2(0f, TileSpriteOffsetY),
            Position = cellCentre,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Visible = true,
        };
        _tileFogLayer!.AddChild(fogSprite);
        _tileFogSprites[coord] = fogSprite;

        // -- Hit-test Area2D. Captured-reference handlers so _ExitTree
        //    can disconnect with the exact same delegate.
        var hitArea = new Area2D
        {
            Name = $"TileHit_{coord.Col}_{coord.Row}",
            Position = cellCentre,
            InputPickable = true,
        };
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(CellPixelSize, CellPixelSize) },
        };
        hitArea.AddChild(shape);
        _tileHitTestLayer!.AddChild(hitArea);
        _tileHitAreas[coord] = hitArea;

        var capturedCoord = coord;
        Action mouseEntered = () => OnCellHoverIn(capturedCoord);
        Action mouseExited = () => OnCellHoverOut(capturedCoord);
        hitArea.MouseEntered += mouseEntered;
        hitArea.MouseExited += mouseExited;
        _tileHitHandlers[coord] = (mouseEntered, mouseExited);
    }

    /// <summary>
    /// Spawn one NPC's portrait + hover hit-test. <b>Gated at E2.1a</b>
    /// (never called from <see cref="BuildAreaGrid"/> while
    /// <c>ScopeMode == "E2.1a"</c>) ; kept here for E2.2.
    /// </summary>
    private void SpawnNpcPortrait(HalfgateNpc npc, AssetResolver assetResolver)
    {
        var portraitKey = $"e2.area_grid.portrait.{npc.NpcId}";
        var portraitTexture = assetResolver.Resolve(portraitKey);

        // Portrait positioning uses iso math too once we land in E2.2,
        // but the legacy orthogonal anchor is kept here pending the
        // E2.2 redesign : the PortraitCell field gives a grid coord,
        // and iso-translating it is a one-liner when the gate opens.
        var cellCentre = IsoCellCentre(npc.PortraitCell);

        var root = new Node2D
        {
            Name = $"Npc_{npc.NpcId}",
            Position = cellCentre,
            ZIndex = 6,
        };

        var sprite = new Sprite2D
        {
            Name = "Portrait",
            Texture = portraitTexture,
            Centered = true,
            // Lift the portrait above the tile (diegetic "person
            // standing on the cell"). E2.2 will tune this on the real
            // Mira bitmaps.
            Offset = new Vector2(0f, -32f),
        };
        root.AddChild(sprite);

        var hitArea = new Area2D
        {
            Name = "Hitbox",
            InputPickable = true,
            Position = new Vector2(0f, -32f),
        };
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(256f, 384f) },
        };
        hitArea.AddChild(shape);
        root.AddChild(hitArea);

        _npcPortraitLayer!.AddChild(root);
        _npcPortraitRoots[npc.NpcId] = root;

        var capturedId = npc.NpcId;
        Action mouseEntered = () => OnNpcPortraitHoverIn(capturedId);
        Action mouseExited = () => OnNpcPortraitHoverOut(capturedId);
        hitArea.MouseEntered += mouseEntered;
        hitArea.MouseExited += mouseExited;
        _npcHoverHandlers[npc.NpcId] = (mouseEntered, mouseExited);
    }

    /// <summary>
    /// Fire the boot tutorial mission's <c>pop_effect</c> per the A4.7
    /// cross-layer projection grammar. <b>Gated at E2.1a</b> ; runs at
    /// E2.1b + later. The mission lists its cells at the E1 layer
    /// (<c>tiles_to_partial_layer[E1]</c>) ; the projector cascades to
    /// the E2 8x8 grid at write time (R3 top-down + EC1/EC2/EC11
    /// no-downgrade clamp via <see cref="TileRevealProjector.ProjectTopDown"/>).
    /// </summary>
    private void ApplyTutorialMissionPopEffect()
    {
        var mission = HalfgateMissionAuthoring.Tutorial;
        var gameState = GetNode<GameState>("/root/GameState");

        if (mission.PopEffectPoiVisible && _poiMarker is not null)
        {
            _poiMarker.Visible = true;
        }

        // A4.7 R1 + R3 : the mission lists its E1 cells ; the projector
        // produces the E2 writes. The mission MUST carry an E1 layer
        // entry per A4.7.2 (auto-projection mode). A schema slip would
        // surface as an empty write set at E2 -- defensive log surfaces
        // that.
        if (!mission.PopEffectTilesToPartialLayer.TryGetValue(RevealLayer.E1, out var e1Cells)
            || e1Cells.Count == 0)
        {
            GD.PushWarning(
                $"[E2AreaMap] ApplyTutorialMissionPopEffect : mission " +
                $"{mission.MissionId} has no E1 layer cells in " +
                $"tiles_to_partial_layer -- nothing to project. " +
                $"Check HalfgateMissionAuthoring against A4.7.2 schema.");
            return;
        }

        var projection = TileRevealProjector.ProjectTopDown(new ProjectTopDownArgs
        {
            SourceLayer = RevealLayer.E1,
            TargetLayer = RevealLayer.E2,
            SourceCells = e1Cells,
            TargetState = TileRevealState.Partial,
            // Read the current E2 state from GameState. The projector's
            // no-downgrade clamp skips cells already at >= Partial.
            GetCurrentTargetState = c => gameState.GetTileRevealState(
                new Vector2I(c.Col, c.Row)),
        });

        int written = 0;
        foreach (var (cell, newState) in projection.CellsToWrite)
        {
            gameState.SetTileRevealState(new Vector2I(cell.Col, cell.Row), newState);
            written++;
        }

        GD.Print(
            $"[E2AreaMap] ApplyTutorialMissionPopEffect (A4.7 projection) : " +
            $"mission={mission.MissionId}, " +
            $"e1_source_cells={e1Cells.Count}, " +
            $"e2_cells_written={written} (fog->partial via R3 top-down + clamp), " +
            $"poi_marker_visible={mission.PopEffectPoiVisible} (marker null={(_poiMarker is null)})");
    }

    /// <summary>
    /// E2.1b smoke canary cell : the center of the 8x8 area grid. Matches
    /// <c>TileRevealRenderController.CanaryCell</c> so the controller-side
    /// signal/Tween trace and the consumer-side shader-set trace align on
    /// the same cell at the centre of the tutorial pop.
    /// </summary>
    private static readonly Vector2I CanaryCellVec = new Vector2I(4, 4);
    private readonly HashSet<Vector2I> _canaryFirstStepSeen = new();

    /// <summary>
    /// Callback fired by <see cref="TileRevealRenderController"/> on every
    /// Tween step. Translates the controller-side <see cref="Vector2I"/>
    /// cell coord into the local <see cref="GridCoord"/> form, fetches the
    /// per-cell Sprite2D from <see cref="_tileBaseSprites"/>, and pushes
    /// the fresh <c>revealLevel</c> into the sprite's ShaderMaterial under
    /// the <c>parchment_alpha</c> uniform name.
    /// </summary>
    private void OnRevealLevelTweenStep(Vector2I cellVec, float revealLevel)
    {
        var coord = new GridCoord(cellVec.X, cellVec.Y);

        bool sentinelHit = cellVec == CanaryCellVec;
        bool first = sentinelHit && _canaryFirstStepSeen.Add(cellVec);
        bool nearTarget = sentinelHit && revealLevel >= 0.54f;

        var hasSprite = _tileBaseSprites.TryGetValue(coord, out var baseSprite);
        var hasShaderMaterial = hasSprite && baseSprite!.Material is ShaderMaterial;

        if (hasSprite && baseSprite!.Material is ShaderMaterial material)
        {
            material.SetShaderParameter("parchment_alpha", revealLevel);
        }

        if (_tileFogSprites.TryGetValue(coord, out var fogSprite))
        {
            fogSprite.Visible = revealLevel < 0.05f;
        }

        if (first || nearTarget)
        {
            GD.Print(
                $"[E2AreaMap] CANARY shader-set cell=({cellVec.X},{cellVec.Y}) " +
                $"revealLevel={revealLevel:F3} " +
                $"sprite_found={hasSprite} shader_material={hasShaderMaterial} " +
                $"phase={(first ? "FIRST_STEP" : "NEAR_TARGET")}");
        }
    }

    /// <summary>
    /// Hover-in on a cell. <b>Gated at E2.1a</b> -- the hit-test Area2D
    /// per cell is not spawned, so this never fires at E2.1a. Kept here
    /// for E2.1c dégelé.
    /// </summary>
    private void OnCellHoverIn(GridCoord coord)
    {
        var gameState = GetNode<GameState>("/root/GameState");
        var state = gameState.GetTileRevealState(new Vector2I(coord.Col, coord.Row));
        if (state == TileRevealState.Fog)
        {
            var tooltip = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
            tooltip?.CancelTooltip();
            return;
        }

        var district = AreaGridLogic.ResolveDistrictType(coord);
        var missionHook = ResolveMissionHookForCell(coord);
        var text = HalfgateRumorHooks.ComposeCellTooltip(district, missionHook);

        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        if (tooltipController is null) return;

        var anchor = _tileHitAreas.TryGetValue(coord, out var area)
            ? area.GetGlobalTransformWithCanvas().Origin
            : Vector2.Zero;
        tooltipController.RequestTooltip(text, anchor);
    }

    private void OnCellHoverOut(GridCoord _)
    {
        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        tooltipController?.CancelTooltip();
    }

    /// <summary>
    /// Resolve the mission narrative hook for an E2 cell. <b>Inert at
    /// E2.1a + E2.1b</b> (never called -- the hover gate is closed
    /// until E2.1c). Kept here for E2.1c dégelé.
    /// </summary>
    private static string? ResolveMissionHookForCell(GridCoord coord)
    {
        var tuto = HalfgateMissionAuthoring.Tutorial;
        var parent = TileRevealProjector.ResolveParentCoord(
            RevealLayer.E2, coord, RevealLayer.E1);
        if (tuto.PopEffectTilesToPartialLayer.TryGetValue(RevealLayer.E1, out var e1Cells))
        {
            foreach (var c in e1Cells)
            {
                if (c == parent) return tuto.NarrativeHook;
            }
        }
        return null;
    }

    /// <summary>
    /// Hover-in on an NPC portrait. <b>Gated at E2.1a</b>. Kept here for
    /// E2.2 dégelé.
    /// </summary>
    private void OnNpcPortraitHoverIn(string npcId)
    {
        var npc = HalfgateNpcAuthoring.FindById(npcId);
        if (npc is null) return;

        var districtLabel = DistrictTypeHelpers.DisplayName(npc.DistrictOrigin);
        var text = $"{npc.DisplayName}\n{npc.Class} -- from {districtLabel}";

        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        if (tooltipController is null) return;

        var anchor = _npcPortraitRoots.TryGetValue(npcId, out var root)
            ? root.GetGlobalTransformWithCanvas().Origin
            : Vector2.Zero;
        tooltipController.RequestTooltip(text, anchor);
    }

    private void OnNpcPortraitHoverOut(string _)
    {
        var tooltipController = GetNodeOrNull<HoverTooltipController>("/root/HoverTooltipController");
        tooltipController?.CancelTooltip();
    }

    /// <summary>
    /// Tear down the area-grid tree + every captured signal handler.
    /// Mandatory before re-Configure (idempotency) and on _ExitTree.
    /// Safe to call at any <see cref="ScopeMode"/> -- iterates the
    /// captured handler dicts, which are empty at E2.1a.
    /// </summary>
    private void TearDownAreaGrid()
    {
        foreach (var (coord, handlers) in _tileHitHandlers)
        {
            if (_tileHitAreas.TryGetValue(coord, out var area) && IsInstanceValid(area))
            {
                area.MouseEntered -= handlers.mouseEntered;
                area.MouseExited -= handlers.mouseExited;
            }
        }
        _tileHitHandlers.Clear();

        foreach (var (npcId, handlers) in _npcHoverHandlers)
        {
            if (_npcPortraitRoots.TryGetValue(npcId, out var root) && IsInstanceValid(root))
            {
                var hit = root.GetNodeOrNull<Area2D>("Hitbox");
                if (hit is not null)
                {
                    hit.MouseEntered -= handlers.mouseEntered;
                    hit.MouseExited -= handlers.mouseExited;
                }
            }
        }
        _npcHoverHandlers.Clear();

        if (_areaGridLayer is not null && IsInstanceValid(_areaGridLayer))
        {
            _areaGridLayer.QueueFree();
        }
        _areaGridLayer = null;
        _tileBaseLayer = null;
        _tileFogLayer = null;
        _tileHitTestLayer = null;
        _npcPortraitLayer = null;
        _poiMarker = null;
        // E2.2 step 1 (2026-05-17) -- disconnect every E2 marker hover
        // handler with the EXACT lambda reference captured at wire time.
        // Method-group disconnect would not match because each lambda
        // closed over a distinct poi_id (Risk #1, same pattern as
        // _tileHitHandlers above). The _areaGridLayer QueueFree below
        // disposes the Area2D nodes, but the disconnect must happen
        // FIRST so the tear-down does not race with a pending hover
        // signal queued in the same frame.
        foreach (var (poiId, handlers) in _e2MarkerHandlers)
        {
            if (!_e2MarkerHotspots.TryGetValue(poiId, out var hotspot)) continue;
            if (!IsInstanceValid(hotspot)) continue;
            var area = hotspot.GetNodeOrNull<Area2D>("Hitbox");
            if (area is null) continue;
            area.MouseEntered -= handlers.mouseEntered;
            area.MouseExited -= handlers.mouseExited;
        }
        _e2MarkerHandlers.Clear();
        _e2MarkerHotspots.Clear();

        // E2.1c per-mission marker refs : the _areaGridLayer QueueFree
        // below already disposes the hotspot Node2D subtrees ; clearing
        // the list drops the captured refs so a re-Configure starts fresh.
        _e2PoiMarkers.Clear();
        _areaGridRevealController = null;
        _areaGridTileShader = null;

        _tileBaseSprites.Clear();
        _tileFogSprites.Clear();
        _tileHitAreas.Clear();
        _npcPortraitRoots.Clear();
        _canaryFirstStepSeen.Clear();
    }

    /// <summary>
    /// Diagnostic surface : reveal-state distribution at the moment of
    /// the call. At E2.1a every cell is in Fog (nothing flips), so the
    /// expected output is (64, 0, 0).
    /// </summary>
    private (int fog, int partial, int revealed) CountRevealStates()
    {
        var gameState = GetNode<GameState>("/root/GameState");
        int fog = 0, partial = 0, revealed = 0;
        foreach (var c in AreaGridLogic.AllCells())
        {
            switch (gameState.GetTileRevealState(new Vector2I(c.Col, c.Row)))
            {
                case TileRevealState.Fog:      fog++; break;
                case TileRevealState.Partial:  partial++; break;
                case TileRevealState.Revealed: revealed++; break;
            }
        }
        return (fog, partial, revealed);
    }

    /// <summary>
    /// Expose the current E2.1 scope value for tests pinning the
    /// Didier-locked scope gate. Test-only surface ; kept on the partial
    /// so it stays beside the constant.
    /// </summary>
    internal static string CurrentScopeMode => ScopeMode;
}
