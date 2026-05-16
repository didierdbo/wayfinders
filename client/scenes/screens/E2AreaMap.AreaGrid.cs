using System;
using System.Collections.Generic;
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
/// <b>Scene shape (built at runtime by <see cref="BuildAreaGrid"/>).</b>
/// All nodes parented under <c>MapPan2DComponent.WorldRoot.AreaGridLayer</c>
/// (a freshly-spawned <see cref="Node2D"/> sibling of WorldMapSprite +
/// PoiContainer). Visual order from bottom to top :
/// </para>
/// <list type="number">
///   <item><c>WorldMapSprite</c> -- area background (already owned by
///         the pan component, set in <see cref="E2AreaMap.Configure"/>).</item>
///   <item><c>AreaGridLayer</c> -- the 8x8 grid container.
///         <list type="bullet">
///           <item><c>TileBaseLayer</c> -- 64 Sprite2D, one per cell,
///                 textured with the district_type-locked placeholder.
///                 ShaderMaterial referencing <c>area_grid_tile_overlay.gdshader</c>
///                 with per-cell <c>parchment_alpha</c> uniform.</item>
///           <item><c>TileFogLayer</c> -- 64 Sprite2D, one per cell,
///                 textured with the fog veil placeholder. Visible iff
///                 the cell's <see cref="TileRevealState"/> is
///                 <see cref="TileRevealState.Fog"/>.</item>
///           <item><c>TileHitTestLayer</c> -- 64 Area2D, one per cell,
///                 for hover detection (EC6 per-cell tooltip path).</item>
///           <item><c>PoiMarker</c> -- one Sprite2D at the grid centre,
///                 visible iff the tuto mission's
///                 <c>pop_effect.poi_visible</c> has fired (always true
///                 at E2.1 since the mission pops at boot).</item>
///           <item><c>NpcPortraitLayer</c> -- 3 Sprite2D + Area2D pairs,
///                 one per <see cref="HalfgateNpcAuthoring"/> NPC,
///                 spawned at the NPC's <c>PortraitCell</c> centre.</item>
///         </list>
///   </item>
/// </list>
///
/// <para>
/// <b>Reveal-substrate plumbing (Phase C-bis-3 pattern).</b> The owner
/// scene instantiates :
/// </para>
/// <list type="bullet">
///   <item>One <see cref="TileRevealStateLogic"/> (pure-C# state holder
///         + R8 encoder + CPU blur engine).</item>
///   <item>One <see cref="ImageTexture"/> backing the R8 reveal-level
///         buffer (an alternative to the per-cell shader uniform when
///         the cinematic shader is wanted ; at E2.1 we use the simpler
///         per-uniform path described below, but the seam is kept so
///         the cinematic shader could be wired later).</item>
///   <item>One <see cref="TileRevealRenderController"/> as a child
///         Node, with its <see cref="TileRevealRenderController.OnRevealLevelChanged"/>
///         callback wired to <see cref="OnRevealLevelTweenStep"/> here.
///         The controller subscribes to
///         <see cref="GameState.TileRevealStateChanged"/> and drives the
///         300 ms cubic-out Tween on every transition.</item>
/// </list>
///
/// <para>
/// <b>Per-uniform render path (E2.1 choice).</b> The 64 base-tile
/// Sprite2D each carry their own ShaderMaterial referencing the shared
/// <c>area_grid_tile_overlay.gdshader</c>. The
/// <see cref="OnRevealLevelTweenStep"/> callback writes the cell's
/// fresh <c>parchment_alpha</c> uniform directly on the matching
/// material. This avoids the R8 ImageTexture round-trip that the
/// cinematic uses ; for an 8x8 grid the simpler per-uniform approach
/// has the same visual result with less plumbing. The R8 texture path
/// stays available for E2.2+ if the cinematic shader is wanted on the
/// area-grid (it is not at E2.1).
/// </para>
///
/// <para>
/// <b>Fog visibility toggle.</b> The fog overlay Sprite2D is shown when
/// the cell's reveal state is Fog ; hidden when Partial or Revealed.
/// The transition is *not* tweened : the fog sprite is binary
/// visible/hidden at the cell level. The smooth fade is carried by the
/// base-tile shader's <c>parchment_alpha</c> Tween (which rises from
/// 0 to 0.55 over 300 ms when fog -> partial fires). At the moment
/// the controller emits the first step > 0, we hide the fog overlay ;
/// at the moment it emits 0 again (revert_to_fog rule), we show it.
/// </para>
///
/// <para>
/// <b>Hover tooltip composition (EC6).</b> The TileHitTestLayer Area2D
/// per cell connects <see cref="Area2D.MouseEntered"/> ->
/// <see cref="OnCellHoverIn"/> which reads the cell's reveal state from
/// GameState. If Fog : no tooltip. If Partial or Revealed : compose via
/// <see cref="HalfgateRumorHooks.ComposeCellTooltip"/> using the
/// mission's narrative hook (if any mission's tiles_to_partial includes
/// this cell) or the per-district fallback line.
/// </para>
///
/// <para>
/// <b>NPC portrait hover.</b> Each NPC has its own Area2D ; on hover
/// the tooltip reads the NPC's display_name, class, and
/// district_origin (A4.3 schema).
/// </para>
/// </summary>
public partial class E2AreaMap
{
    /// <summary>
    /// World-space size of one grid cell, in pixels. Locked at 256 to
    /// match the placeholder asset dimensions. If a future area uses
    /// larger tiles, bump here AND regenerate placeholders accordingly.
    /// </summary>
    private const int CellPixelSize = 256;

    /// <summary>
    /// World-space total size of the 8x8 grid (cell × grid). Used to
    /// size the AreaGridLayer + the pan component bounds.
    /// </summary>
    private const int GridPixelSize = CellPixelSize * AreaGridLogic.GridSize;

    /// <summary>
    /// Path of the area-grid tile overlay shader. Loaded once per
    /// Configure call.
    /// </summary>
    private const string AreaGridTileShaderPath = "res://shaders/area_grid_tile_overlay.gdshader";

    /// <summary>
    /// Marker shown above the centre POI footprint when the boot tuto
    /// mission has fired its pop_effect (poi_visible = true).
    /// </summary>
    private const string AreaGridPoiMarkerAssetKey = "e2.area_grid.poi_marker";

    /// <summary>
    /// Fog veil tile texture (reused on every cell whose reveal state
    /// is Fog).
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
    /// Per-cell base-tile Sprite2D, keyed by grid coord. The
    /// ShaderMaterial bound here is the per-cell instance carrying the
    /// parchment_alpha uniform.
    /// </summary>
    private readonly Dictionary<GridCoord, Sprite2D> _tileBaseSprites = new();

    /// <summary>Per-cell fog overlay Sprite2D, toggled by reveal state.</summary>
    private readonly Dictionary<GridCoord, Sprite2D> _tileFogSprites = new();

    /// <summary>Per-cell Area2D hit-test, owns the MouseEntered handler.</summary>
    private readonly Dictionary<GridCoord, Area2D> _tileHitAreas = new();

    /// <summary>Captured handlers per cell so _ExitTree disconnects cleanly.</summary>
    private readonly Dictionary<GridCoord, (Action mouseEntered, Action mouseExited)> _tileHitHandlers = new();

    /// <summary>Per-NPC portrait root Node2D, keyed by NPC id.</summary>
    private readonly Dictionary<string, Node2D> _npcPortraitRoots = new();

    /// <summary>Per-NPC captured handlers for clean disconnect.</summary>
    private readonly Dictionary<string, (Action mouseEntered, Action mouseExited)> _npcHoverHandlers = new();

    /// <summary>
    /// The reveal-render controller for the area-grid. Spawned as a
    /// child of <see cref="_areaGridLayer"/> in <see cref="BuildAreaGrid"/>,
    /// torn down in <see cref="TearDownAreaGrid"/>. Subscribes to
    /// GameState.TileRevealStateChanged via its own _Ready/_ExitTree --
    /// we just wire the callback after AddChild.
    /// </summary>
    private TileRevealRenderController? _areaGridRevealController;

    /// <summary>
    /// Shader resource loaded once per Configure call. Each per-cell
    /// ShaderMaterial references this shared resource ; the per-instance
    /// uniform is `parchment_alpha`.
    /// </summary>
    private Shader? _areaGridTileShader;

    /// <summary>
    /// Build the 8x8 grid render + hover hit-test + reveal-substrate
    /// wiring. Idempotent : safe to call after a prior teardown ; called
    /// from <see cref="E2AreaMap.Configure"/> after the pan-component
    /// background is set. The pan-component's WorldRoot.AreaGridLayer
    /// is created here (it does not pre-exist in the .tscn).
    /// </summary>
    private void BuildAreaGrid()
    {
        TearDownAreaGrid();

        // 1. Load shader resource (defensive : log error and skip the
        //    overlay if missing -- the grid still renders with base tiles
        //    + fog overlay, just no parchment fade).
        _areaGridTileShader = ResourceLoader.Load<Shader>(AreaGridTileShaderPath);
        if (_areaGridTileShader is null)
        {
            GD.PushError(
                $"[E2AreaMap] BuildAreaGrid: failed to load shader at {AreaGridTileShaderPath} -- " +
                $"parchment overlay disabled this run, base tiles + fog still render.");
        }

        // 2. Spawn the AreaGridLayer Node2D under MapPan2DComponent.WorldRoot.
        _areaGridLayer = new Node2D { Name = "AreaGridLayer", ZIndex = 1 };
        _panComponent.WorldRoot.AddChild(_areaGridLayer);

        _tileBaseLayer = new Node2D { Name = "TileBaseLayer", ZIndex = 0 };
        _tileFogLayer = new Node2D { Name = "TileFogLayer", ZIndex = 1 };
        _tileHitTestLayer = new Node2D { Name = "TileHitTestLayer", ZIndex = 2 };
        _areaGridLayer.AddChild(_tileBaseLayer);
        _areaGridLayer.AddChild(_tileFogLayer);
        _areaGridLayer.AddChild(_tileHitTestLayer);

        var assetResolver = GetNode<AssetResolver>("/root/AssetResolver");
        var fogTexture = assetResolver.Resolve(AreaGridFogAssetKey);

        // 3. Spawn the 64 tile sprites + fog overlays + hit-test areas.
        foreach (var coord in AreaGridLogic.AllCells())
        {
            SpawnTile(coord, assetResolver, fogTexture);
        }

        // 4. POI marker at the central footprint centre (between cells
        //    (3,3) and (4,4)). Visible iff the tuto mission popped
        //    poi_visible=true (always true at E2.1 boot).
        _poiMarker = new Sprite2D
        {
            Name = "PoiMarker",
            Texture = assetResolver.Resolve(AreaGridPoiMarkerAssetKey),
            Centered = true,
            ZIndex = 5,
            // Centre of the 4-cell central footprint.
            Position = new Vector2(GridPixelSize / 2f, GridPixelSize / 2f),
            // Hidden by default ; ApplyTutorialMissionPopEffect flips it
            // on if the mission has poi_visible = true.
            Visible = false,
        };
        _areaGridLayer.AddChild(_poiMarker);

        // 5. NPC portraits.
        _npcPortraitLayer = new Node2D { Name = "NpcPortraitLayer", ZIndex = 6 };
        _areaGridLayer.AddChild(_npcPortraitLayer);
        foreach (var npc in HalfgateNpcAuthoring.All)
        {
            SpawnNpcPortrait(npc, assetResolver);
        }

        // 6. Spawn the reveal-render controller as a child Node and wire
        //    the OnRevealLevelChanged callback. Mirrors the
        //    Phase C-bis-3 instantiation pattern.
        _areaGridRevealController = new TileRevealRenderController
        {
            Name = "AreaGridRevealController",
            OnRevealLevelChanged = OnRevealLevelTweenStep,
        };
        _areaGridLayer.AddChild(_areaGridRevealController);

        GD.Print(
            $"[E2AreaMap] BuildAreaGrid: {_tileBaseSprites.Count} tiles, " +
            $"{_npcPortraitRoots.Count} portraits, " +
            $"shader_loaded={_areaGridTileShader is not null}, " +
            $"render_controller_wired=true");
    }

    /// <summary>
    /// Spawn one cell's base tile + fog overlay + hit-test area. The
    /// base tile carries its own ShaderMaterial (per-instance
    /// `parchment_alpha` uniform). Fog overlay is initially visible
    /// (cell defaults to Fog) and is hidden as soon as the reveal
    /// substrate emits a > 0 alpha for this cell.
    /// </summary>
    private void SpawnTile(GridCoord coord, AssetResolver assetResolver, Texture2D fogTexture)
    {
        var district = AreaGridLogic.ResolveDistrictType(coord);
        var tileKey = $"e2.area_grid.tile.{DistrictTypeHelpers.AssetKeySuffix(district)}";
        var tileTexture = assetResolver.Resolve(tileKey);

        // World-space top-left of the cell.
        var cellTopLeft = new Vector2(coord.Col * CellPixelSize, coord.Row * CellPixelSize);
        var cellCentre = cellTopLeft + new Vector2(CellPixelSize / 2f, CellPixelSize / 2f);

        // -- Base tile sprite + shader material.
        var baseSprite = new Sprite2D
        {
            Name = $"TileBase_{coord.Col}_{coord.Row}",
            Texture = tileTexture,
            Centered = true,
            Position = cellCentre,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        if (_areaGridTileShader is not null)
        {
            var material = new ShaderMaterial { Shader = _areaGridTileShader };
            material.SetShaderParameter("parchment_alpha", 0.0f);
            baseSprite.Material = material;
        }
        _tileBaseLayer!.AddChild(baseSprite);
        _tileBaseSprites[coord] = baseSprite;

        // -- Fog overlay sprite. Same world position, visible by default.
        var fogSprite = new Sprite2D
        {
            Name = $"TileFog_{coord.Col}_{coord.Row}",
            Texture = fogTexture,
            Centered = true,
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

        // Closure-capture coord once per cell (mistake pattern : the
        // outer var would all point to the last iteration's value).
        var capturedCoord = coord;
        Action mouseEntered = () => OnCellHoverIn(capturedCoord);
        Action mouseExited = () => OnCellHoverOut(capturedCoord);
        hitArea.MouseEntered += mouseEntered;
        hitArea.MouseExited += mouseExited;
        _tileHitHandlers[coord] = (mouseEntered, mouseExited);
    }

    /// <summary>
    /// Spawn one NPC's portrait + hover hit-test. Portrait is parented
    /// under the NpcPortraitLayer ; hit-test is a sibling Area2D so the
    /// hover tooltip reads the NPC entry.
    /// </summary>
    private void SpawnNpcPortrait(HalfgateNpc npc, AssetResolver assetResolver)
    {
        var portraitKey = $"e2.area_grid.portrait.{npc.NpcId}";
        var portraitTexture = assetResolver.Resolve(portraitKey);

        var cellCentre = new Vector2(
            npc.PortraitCell.Col * CellPixelSize + CellPixelSize / 2f,
            npc.PortraitCell.Row * CellPixelSize + CellPixelSize / 2f);

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
            // Slight upward offset so the portrait reads "above" the
            // tile rather than sitting flat on it -- diegetic "person
            // standing on the cell". Locked at -32 px (~12 % of cell
            // height) for the MVP placeholder ; Mira will tune post-
            // bitmap delivery.
            Offset = new Vector2(0f, -32f),
        };
        root.AddChild(sprite);

        // Hover hit area sized to the portrait bbox (256 x 384).
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

        // Captured handlers.
        var capturedId = npc.NpcId;
        Action mouseEntered = () => OnNpcPortraitHoverIn(capturedId);
        Action mouseExited = () => OnNpcPortraitHoverOut(capturedId);
        hitArea.MouseEntered += mouseEntered;
        hitArea.MouseExited += mouseExited;
        _npcHoverHandlers[npc.NpcId] = (mouseEntered, mouseExited);
    }

    /// <summary>
    /// Fire the boot tutorial mission's pop_effect : flip the 4
    /// central footprint cells from <see cref="TileRevealState.Fog"/> to
    /// <see cref="TileRevealState.Partial"/>, and show the POI marker
    /// (poi_visible = true). The GameState.SetTileRevealState calls
    /// emit signals that the render controller subscribes to ; the
    /// controller drives the 300 ms Tween, which calls back into
    /// <see cref="OnRevealLevelTweenStep"/> here for the actual visual
    /// update.
    /// </summary>
    private void ApplyTutorialMissionPopEffect()
    {
        var mission = HalfgateMissionAuthoring.Tutorial;
        var gameState = GetNode<GameState>("/root/GameState");

        if (mission.PopEffectPoiVisible && _poiMarker is not null)
        {
            _poiMarker.Visible = true;
        }

        // EC1 guard : already-partial cells are no-op on a second pop_effect.
        // GameState.SetTileRevealState is idempotent so the guard is implicit
        // there (silent no-op if current == new), but the log surfaces what
        // happened so smoke-test reading is unambiguous.
        int flipped = 0;
        foreach (var cell in mission.PopEffectTilesToPartial)
        {
            // GameState API speaks Godot Vector2I (engine-marshal-safe) ;
            // our authoring layer speaks GridCoord (pure-C# record struct).
            // The translation happens at this single seam.
            var cellVec = new Vector2I(cell.Col, cell.Row);
            var before = gameState.GetTileRevealState(cellVec);
            gameState.SetTileRevealState(cellVec, TileRevealState.Partial);
            var after = gameState.GetTileRevealState(cellVec);
            if (before != after) flipped++;
        }

        GD.Print(
            $"[E2AreaMap] ApplyTutorialMissionPopEffect: mission={mission.MissionId} " +
            $"flipped={flipped} cells fog->partial, poi_marker_visible={mission.PopEffectPoiVisible}");
    }

    /// <summary>
    /// Callback fired by <see cref="TileRevealRenderController"/> on
    /// every Tween step. Translates the cell's freshly-interpolated
    /// reveal_level into a per-cell shader uniform write + a fog
    /// overlay visibility toggle.
    /// </summary>
    private void OnRevealLevelTweenStep(Vector2I cellVec, float revealLevel)
    {
        var coord = new GridCoord(cellVec.X, cellVec.Y);

        // Update the base tile shader uniform. The shader maps the
        // raw float into a "parchment overlay weight" that peaks at
        // 0.55 (Partial) and tapers to 0 at both ends -- see
        // area_grid_tile_overlay.gdshader for the formula.
        if (_tileBaseSprites.TryGetValue(coord, out var baseSprite)
            && baseSprite.Material is ShaderMaterial material)
        {
            material.SetShaderParameter("parchment_alpha", revealLevel);
        }

        // Toggle fog overlay : visible only at reveal_level ~ 0.
        // Threshold tuned at 0.05 -- the first Tween step that crosses
        // it hides the fog, well before parchment peaks at 0.55.
        if (_tileFogSprites.TryGetValue(coord, out var fogSprite))
        {
            fogSprite.Visible = revealLevel < 0.05f;
        }
    }

    /// <summary>
    /// Hover-in on a cell. EC6 path : if the cell is in Fog state, no
    /// tooltip ; otherwise compose the district + rumor hook tooltip.
    /// </summary>
    private void OnCellHoverIn(GridCoord coord)
    {
        var gameState = GetNode<GameState>("/root/GameState");
        var state = gameState.GetTileRevealState(new Vector2I(coord.Col, coord.Row));
        if (state == TileRevealState.Fog)
        {
            // EC6 : fog cells do not surface a tooltip. Cancel any
            // in-flight tooltip request (e.g. if the cursor moved fast
            // from a partial cell to a fog cell).
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
    /// Resolve the mission narrative hook for a cell : if any mission's
    /// tiles_to_partial footprint includes this coord, return that
    /// mission's hook. Otherwise return null so the rumor-hooks helper
    /// uses the per-district fallback. At E2.1 there is exactly one
    /// mission (the tuto) ; the lookup is a contains-check on its
    /// footprint.
    /// </summary>
    private static string? ResolveMissionHookForCell(GridCoord coord)
    {
        var tuto = HalfgateMissionAuthoring.Tutorial;
        foreach (var c in tuto.PopEffectTilesToPartial)
        {
            if (c.Col == coord.Col && c.Row == coord.Row) return tuto.NarrativeHook;
        }
        return null;
    }

    /// <summary>
    /// Hover-in on an NPC portrait. The tooltip shows display_name,
    /// class, and district_origin label (A4.3 schema).
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
    /// Mirrors the captured-reference signal-leak discipline used for
    /// the legacy POI handlers in <see cref="E2AreaMap"/>.
    /// </summary>
    private void TearDownAreaGrid()
    {
        // Disconnect every tile hit-test handler first.
        foreach (var (coord, handlers) in _tileHitHandlers)
        {
            if (_tileHitAreas.TryGetValue(coord, out var area) && IsInstanceValid(area))
            {
                area.MouseEntered -= handlers.mouseEntered;
                area.MouseExited -= handlers.mouseExited;
            }
        }
        _tileHitHandlers.Clear();

        // Disconnect every NPC portrait handler.
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

        // Free the AreaGridLayer subtree (this also frees the
        // RenderController which disconnects from GameState in its own
        // _ExitTree).
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
        _areaGridRevealController = null;
        _areaGridTileShader = null;

        _tileBaseSprites.Clear();
        _tileFogSprites.Clear();
        _tileHitAreas.Clear();
        _npcPortraitRoots.Clear();
    }

    /// <summary>
    /// Diagnostic surface for the E2.1 smoke test : how many tiles are
    /// in each reveal state at the moment of the call. Surfaces in the
    /// preflight log.
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
}
