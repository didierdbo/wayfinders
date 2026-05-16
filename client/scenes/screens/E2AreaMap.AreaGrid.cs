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
///   <item><b>E2.1a (this commit)</b> — iso 8x8 in pure Fog state,
///         using the E1 tile bitmap (<c>wf_e1_tile_neutral.png</c>).
///         Pan + zoom basics. <i>Nothing else visible.</i></item>
///   <item><b>E2.1b (next)</b> — central POI marker + tuto mission
///         pop_effect flipping the 4 central cells to Partial. Parchment
///         shader overlay re-activates here.</item>
///   <item><b>E2.1c (after)</b> — district tooltip composition (EC6
///         hover path) + per-district tile bitmaps. NPC portraits stay
///         out (those land in E2.2 with the full Recruit + Company panel
///         flow).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Scope gating mechanism.</b> Every block that overshoots the E2.1a
/// scope is wrapped in <c>if (ScopeMode != "E2.1a") { ... }</c> rather
/// than deleted. The code stays at the rendezvous so when E2.1b ships,
/// we flip the constant and dégèle progressively. The four gated blocks
/// at E2.1a :
/// <list type="number">
///   <item>NPC portrait spawn loop (the 3 portraits).</item>
///   <item>Tuto mission <c>pop_effect</c> (the 4 fog->partial flips +
///         the POI marker visibility flip).</item>
///   <item>Per-cell tile hit-test Area2D + hover handlers (no tooltips
///         in E2.1a).</item>
///   <item>Reveal substrate (per-cell parchment shader material +
///         <see cref="TileRevealRenderController"/> child). Since
///         nothing flips fog->partial in E2.1a, the controller would
///         have nothing to render anyway, but gating it explicitly
///         keeps the boot log clean and removes a moving part from
///         the smoke test.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Iso placement (E2.1a, replaces the orthogonal layout of the first
/// E2.1 pass).</b> Tiles are laid out using the same iso math as
/// <see cref="Wayfinders.Client.Scenes.Scratch.IsoMapE1Probe"/> :
/// <code>
///   screenX = (col - row) * IsoWStride        (IsoWStride = 128)
///   screenY = (col + row) * IsoHStride        (IsoHStride = 64)
/// </code>
/// The raw iso math produces negative X values when row &gt; col
/// (south-west tiles). The pan component's Camera2D bounds are
/// positive-only ([0..W] x [0..H]), so we shift the grid into the
/// positive quadrant via <see cref="IsoOriginOffsetX"/> applied to every
/// tile Position. The shifted bbox is exactly
/// <see cref="IsoBboxWidth"/> x <see cref="IsoBboxHeight"/>, passed
/// verbatim to <see cref="MapPan2DComponent.Configure(Texture2D, Vector2, Vector2)"/>
/// as worldBoundsSize so the camera can pan over the whole grid and
/// nothing else.
/// </para>
///
/// <para>
/// <b>Tile texture (E2.1a + E2.1b).</b> All 64 tiles use the E1 neutral
/// fog bitmap (<c>e1.tile_neutral</c> = <c>wf_e1_tile_neutral.png</c>,
/// 256x132 RGBA with transparent corners). E2.1a renders the grid in
/// pure Fog state (no shader). E2.1b adds the parchment overlay shader
/// + the A4.7 tuto pop projection ; the base bitmap stays the same E1
/// neutral so the iso losange shape is preserved (the per-district
/// 256x256 placeholder bitmaps have no transparency at their corners,
/// so routing E2.1b to them would render every cell as a brique
/// rectangulaire labelled with the district name -- exactly the
/// regression Didier flagged in the E2.1b smoke). The per-district
/// bitmaps re-enter the picture at E2.1c when the per-district visual
/// treatment dégelés.
/// </para>
///
/// <para>
/// <b>Sprite.Offset.Y discipline.</b> The E1 tile is a 256x132 bitmap
/// = face-top losange 256x128 + 4 px bottom slab. To align the losange
/// centre on the tile's Position, we set <c>Sprite2D.Offset.Y =
/// <see cref="TileSpriteOffsetY"/> = +2</c> (= slab/2). Same correction
/// IsoMapE1Probe applies (see SpriteOffsetY constant there).
/// </para>
///
/// <para>
/// <b>Scene shape (E2.1a final).</b> Nodes parented under
/// <c>MapPan2DComponent.WorldRoot.AreaGridLayer</c>. The WorldMapSprite
/// is hidden : at E2.1a there is no area background bitmap behind the
/// grid (the iso losange IS the visible surface).
/// </para>
/// <list type="number">
///   <item><c>WorldMapSprite</c> -- hidden at E2.1a (Configure() sets
///         <c>WorldMapSprite.Visible = false</c>).</item>
///   <item><c>AreaGridLayer</c> -- the 8x8 grid container.
///         <list type="bullet">
///           <item><c>TileBaseLayer</c> -- 64 Sprite2D positioned in iso
///                 space, all textured with the E1 fog bitmap. No
///                 ShaderMaterial at E2.1a (the parchment overlay is
///                 dégelé in E2.1b).</item>
///         </list>
///   </item>
/// </list>
///
/// <para>
/// <b>Future scope (kept in source, gated).</b> When E2.1b lights up :
/// flip <see cref="ScopeMode"/> to <c>"E2.1b"</c>, and re-enable the
/// mission pop_effect block + the POI marker + the reveal substrate.
/// E2.1c flips it to <c>"E2.1c"</c> and re-enables hit-test + tooltips
/// + per-district tile bitmaps. The tile placement (iso math) is the
/// E2.1a foundation that every subsequent jalon builds on.
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
    /// Without this shift, the south-west tile (col=0, row=7) would land
    /// at screen X = -896, which the Camera2D positive-only limits
    /// cannot frame.
    /// </summary>
    private const float IsoOriginOffsetX = IsoBboxWidth / 2f;

    /// <summary>
    /// Vertical offset applied to every tile Position so the iso bbox
    /// sits in the positive quadrant. Equals half the tile height = 66.
    /// The north-most tile (col=0, row=0) lands at screen Y = 0 in raw
    /// iso math ; +66 lifts it to <c>+66</c> so its top edge sits at 0.
    /// </summary>
    private const float IsoOriginOffsetY = TileTextureHeightPx / 2f;

    /// <summary>
    /// E1 tile bitmap key in the asset map. Resolves to
    /// <c>res://assets/wayfinders_visual_assets/e1/wf_e1_tile_neutral.png</c>
    /// (256x132, face-top losange + 4 px slab). Reused at E2.1a because
    /// Didier explicitly asked for the "même bitmap que E1" so the L1
    /// to L2 transition is visually continuous.
    /// </summary>
    private const string AreaGridTileFogAssetKey = "e1.tile_neutral";

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
    /// E2-layer POI markers, one per active <see cref="HalfgateE2Mission"/>.
    /// Spawned by <c>SpawnE2MissionPoiMarkers</c> at E2.1c+ ; each marker
    /// sits at the iso-screen position of its target district's centroid
    /// (resolved via <see cref="AreaGridLogic.DistrictCentroid"/>). The
    /// list is kept so <see cref="TearDownAreaGrid"/> can clear refs
    /// without enumerating the AreaGridLayer children -- the layer
    /// QueueFree cascade already frees the sprites, but the list lets us
    /// log the count + nullify the captured refs in one pass.
    ///
    /// <para>
    /// <b>Why a List, not a Dictionary by mission_id.</b> The E2.1c
    /// renderer never looks markers up by id (no hover, no click, no
    /// tooltip ; the brief is strict on lecture-only). When E2.2 wires
    /// hover dispatch this becomes a Dictionary keyed by mission_id or
    /// by district. For now the list mirrors the static authoring order
    /// from <see cref="HalfgateE2MissionAuthoring.All"/>.
    /// </para>
    /// </summary>
    private readonly List<Sprite2D> _e2PoiMarkers = new();

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
        // At E2.1a / E2.1b / E2.1c we use the E1 tile bitmap for all
        // 64 cells (Didier brief : "même bitmap que E1") and we do not
        // render a separate fog veil (the base sprite + parchment shader
        // carry every visible state). The fogTexture variable is kept
        // for future degelé at E2.2 ; it resolves to the E1 bitmap until
        // then as a defensive default.
        var fogTexture = (ScopeMode == "E2.1a" || ScopeMode == "E2.1b" || ScopeMode == "E2.1c")
            ? assetResolver.Resolve(AreaGridTileFogAssetKey)
            : assetResolver.Resolve(AreaGridFogAssetKey);
        var tileE1Texture = assetResolver.Resolve(AreaGridTileFogAssetKey);

        // 4. Spawn the 64 tile sprites in iso space.
        foreach (var coord in AreaGridLogic.AllCells())
        {
            SpawnTile(coord, assetResolver, fogTexture, tileE1Texture);
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
        //    AreaGridLogic.DistrictCentroid, and spawns one Sprite2D per
        //    mission at the iso-screen position of that centroid. No
        //    hover, no click, no tooltip -- lecture only at E2.1c
        //    (interactivity lands at E2.2+).
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

        GD.Print(
            $"[E2AreaMap] BuildAreaGrid: scope={ScopeMode}, " +
            $"tiles={_tileBaseSprites.Count} (iso, all fog), " +
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
    /// Iso-translate a FRACTIONAL grid position (e.g. a district centroid
    /// like (3.5, 3.5) for Intramuros) into the same iso-placed
    /// world-space the integer cell centres live in. Same formula as
    /// <see cref="IsoCellCentre"/> ; the only difference is the input
    /// type. Used by <see cref="SpawnE2MissionPoiMarkers"/> to land a
    /// POI marker BETWEEN cells when the district centroid is fractional.
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
    /// Spawn one POI marker per active E2-layer mission (E2.1c
    /// deliverable, Didier brief 2026-05-16). Reads the mock authoring
    /// in <see cref="HalfgateE2MissionAuthoring.All"/>, resolves each
    /// mission's <see cref="HalfgateE2Mission.TargetDistrict"/> to a
    /// fractional centroid via <see cref="AreaGridLogic.DistrictCentroid"/>,
    /// translates the centroid into iso-screen space via
    /// <see cref="IsoFractionalCentre"/>, and instantiates one
    /// <see cref="Sprite2D"/> per mission under
    /// <see cref="_areaGridLayer"/>.
    ///
    /// <para>
    /// <b>Scope strict at E2.1c.</b> The brief locks lecture-only :
    /// </para>
    /// <list type="bullet">
    ///   <item>No hover -- no Area2D hit-test attached.</item>
    ///   <item>No click -- no input handler.</item>
    ///   <item>No tooltip -- the narrative_hook is authored but not
    ///         surfaced.</item>
    /// </list>
    ///
    /// <para>
    /// Each of these dégèle at E2.2 or later via the same kind of
    /// scope-mode flip that opened E2.1c (a one-liner
    /// <c>if (ScopeMode != ...)</c> in this method).
    /// </para>
    ///
    /// <para>
    /// <b>ZIndex discipline.</b> ZIndex = 5 puts the markers above the
    /// tile base layer (ZIndex 0 on _tileBaseLayer) and above the
    /// reveal-controller's overlay tweens. The legacy single
    /// <see cref="_poiMarker"/> uses ZIndex = 5 too ; that field is null
    /// at E2.1c so they do not coexist, and when both are visible at
    /// E2.2+ the per-mission markers iso-Y-sort against the central one
    /// naturally.
    /// </para>
    ///
    /// <para>
    /// <b>Marker texture.</b> Resolved via the existing
    /// <see cref="AreaGridPoiMarkerAssetKey"/> (= "e2.area_grid.poi_marker"
    /// = wf_e2_grid_poi_marker_192x192.png, an RGBA terracotta circle
    /// with transparent corners). Same key the legacy single marker
    /// uses ; sharing the asset is intentional at E2.1c -- when E2.2
    /// dégèle a per-mission badge state (active vs suspended), the key
    /// dispatches per <see cref="HalfgateE2Mission"/> state.
    /// </para>
    /// </summary>
    private void SpawnE2MissionPoiMarkers(AssetResolver assetResolver)
    {
        var markerTexture = assetResolver.Resolve(AreaGridPoiMarkerAssetKey);

        foreach (var mission in HalfgateE2MissionAuthoring.All)
        {
            var centroid = AreaGridLogic.DistrictCentroid(mission.TargetDistrict);
            var screenCentre = IsoFractionalCentre(centroid.Col, centroid.Row);

            var marker = new Sprite2D
            {
                // Name encodes mission_id so the remote scene inspector
                // makes the marker easy to find. Stable across runs.
                Name = $"E2PoiMarker_{mission.MissionId}",
                Texture = markerTexture,
                Centered = true,
                ZIndex = 5,
                Position = screenCentre,
                // LinearWithMipmaps matches the tile rendering : at the
                // default zoom the marker's ring edge reads cleaner than
                // Nearest. The 192x192 source bitmap is over-sampled at
                // the rendered ~48 px so mipmaps win on the downscale.
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                Visible = true,
            };
            _areaGridLayer!.AddChild(marker);
            _e2PoiMarkers.Add(marker);
        }

        GD.Print(
            $"[E2AreaMap] SpawnE2MissionPoiMarkers : spawned " +
            $"{_e2PoiMarkers.Count} marker(s) for active E2 missions " +
            $"({string.Join(", ", HalfgateE2MissionAuthoring.All.Select(m => m.MissionId))}).");
    }

    /// <summary>
    /// Spawn one cell's tile sprite at its iso-placed world centre. At
    /// E2.1a the sprite is a plain Sprite2D with the E1 fog bitmap and
    /// no ShaderMaterial. Past E2.1a, the sprite carries a per-cell
    /// ShaderMaterial (parchment_alpha uniform) and the fog overlay +
    /// hit-test Area2D are spawned alongside.
    /// </summary>
    private void SpawnTile(
        GridCoord coord,
        AssetResolver assetResolver,
        Texture2D fogTexture,
        Texture2D tileE1Texture)
    {
        // -- Base tile sprite. At E2.1a + E2.1b + E2.1c, all 64 cells
        //    use the E1 bitmap (Didier lock 2026-05-16, E2.1b smoke fix +
        //    Rune extension at E2.1c : the Option C district_tint shader
        //    path delivers the 6 visually-distinct district zones from
        //    the single neutral bitmap, so the per-district placeholder
        //    bitmaps stay untouched until proper Mira-painted iso losange
        //    versions land at E2.2+). The per-district placeholder bitmaps
        //    are 256x256 fully-opaque coloured squares with the district
        //    name stamped on them ; routing here to them produced visible
        //    "Hinterland / Gateway / Littoral / Land Agri" rectangles
        //    around the grid pourtour AND made every cell render as a
        //    brique rectangulaire (no losange shape) because those PNGs
        //    have no transparent corners. The E1 neutral bitmap (256x132
        //    RGBA, transparent corners) is the correct base for the
        //    district_tint shader multiplication path.
        var baseTexture = (ScopeMode == "E2.1a" || ScopeMode == "E2.1b" || ScopeMode == "E2.1c")
            ? tileE1Texture
            : assetResolver.Resolve(
                $"e2.area_grid.tile.{DistrictTypeHelpers.AssetKeySuffix(AreaGridLogic.ResolveDistrictType(coord))}");

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
            // Per-cell district tint (Rune lock 2026-05-16, Option C).
            // Set once at spawn ; the shader multiplies the base bitmap
            // by this colour so all 64 cells share the E1 iso losange
            // bitmap yet render as 6 visually-distinct district zones.
            // The pure-C# TintRgba lives in DistrictTypeHelpers and stays
            // Godot-free ; we wrap into Godot.Color at the seam here.
            var district = AreaGridLogic.ResolveDistrictType(coord);
            var tint = DistrictTypeHelpers.TintRgba(district);
            material.SetShaderParameter("district_tint", new Color(tint.R, tint.G, tint.B, tint.A));
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
    ///
    /// <para>
    /// <b>Mock-mission-active at E2.1b.</b> The MVE pipeline does not yet
    /// have an "active missions on Halfgate" queue plumbed end-to-end
    /// (that lands at E2.6 with the ML mission generator). For the E2.1b
    /// smoke this method is invoked unconditionally from
    /// <see cref="E2AreaMap.Configure"/> when the iso area-grid layer is
    /// active : the tuto mission's pop_effect is the canonical "mission
    /// just popped" simulation. When E2.6 wires the real queue, this
    /// call moves behind <c>GameState.PendingMissions.Where(...).Any()</c>
    /// or equivalent and the per-mission write becomes a loop over the
    /// real queue.
    /// </para>
    ///
    /// <para>
    /// <b>Wire steps.</b>
    /// </para>
    /// <list type="number">
    ///   <item>Read <c>mission.PopEffectTilesToPartialLayer[E1]</c> --
    ///         the 16 cells of the Halfgate 4x4 footprint.</item>
    ///   <item>Run <see cref="TileRevealProjector.ProjectTopDown"/>
    ///         with SourceLayer=E1, TargetLayer=E2, TargetState=Partial.
    ///         The projector applies the no-downgrade clamp per cell.</item>
    ///   <item>Apply each (cell, newState) write via
    ///         <see cref="GameState.SetTileRevealState"/>. The signal
    ///         <c>TileRevealStateChanged</c> fires per cell ;
    ///         <see cref="TileRevealRenderController"/> picks it up and
    ///         Tweens parchment_alpha 0 -&gt; 0.55 over 300 ms.</item>
    /// </list>
    ///
    /// <para>
    /// <b>POI marker.</b> The mission's <c>poi_visible</c> flag toggles
    /// <see cref="_poiMarker"/>.Visible -- but at E2.1b that field is
    /// null (POI marker deferred to E2.1c). The null-check below is the
    /// gate ; when E2.1c degelés the marker, the field is non-null and
    /// the flip lands automatically.
    /// </para>
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
    /// the same cell at the centre of the tutorial pop. A
    /// <see cref="HashSet{T}"/> guards "first step seen" so the canary
    /// fires once per Tween instead of once per step (a 300ms Tween at
    /// 60fps would emit ~18 steps -> 18 log lines per cell, flooding the
    /// console).
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
    ///
    /// <para>
    /// <b>E2.1b smoke canary (2026-05-16).</b> Didier reports the wash
    /// never visually applies despite <c>e2_cells_written=64</c>. This
    /// callback is the last leg of the signal -> Tween -> shader path :
    /// if the controller-side canary in
    /// <see cref="TileRevealRenderController"/> fires but this one does
    /// not, the consumer wiring (<c>OnRevealLevelChanged</c> delegate) is
    /// broken ; if both fire but the wash stays invisible, the SHADER
    /// SIDE is the culprit (uniform name mismatch, Material instance
    /// stale, formula too subtle to be visible). The canary logs the
    /// first step (~0.0) and the converged-near-target step (>=0.54)
    /// once per cell, gated on the sentinel cell (4,4) to avoid flooding
    /// the console with 64-cell output.
    /// </para>
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
            ? area.GlobalPosition
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
    ///
    /// <para>
    /// <b>A4.7 schema-consumer.</b> The mission lists cells at the E1
    /// layer ; we resolve the E1 parent of the hovered E2 cell via
    /// <see cref="TileRevealProjector.ResolveParentCoord"/> and check
    /// membership in the mission's E1 footprint. Cheaper than the
    /// forward-project + membership-set approach when the footprint is
    /// small and the hover-cell check runs per mouse-move event.
    /// </para>
    /// </summary>
    private static string? ResolveMissionHookForCell(GridCoord coord)
    {
        var tuto = HalfgateMissionAuthoring.Tutorial;
        // The hover coord lives at E2. The mission's footprint lives at
        // E1. Inverse-project the hover coord (E2 -> E1 parent).
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
            ? root.GlobalPosition
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
        // E2.1c per-mission marker refs : the _areaGridLayer QueueFree
        // above already disposes the Sprite2D nodes ; clearing the list
        // drops the captured refs so a re-Configure starts fresh.
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
