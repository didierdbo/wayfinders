using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Components;

/// <summary>
/// Polygon2D-per-cell fog renderer (slice 1 livrable 2 → slice 2 livrables
/// 1+2+3 → slice 3.5 iso refactor → slice 3.6 iso bitmap atlas → slice 3.6
/// design fix two-polygon carton → slice 3.6.1 carton-back uniform texture
/// hook — M3 / L1 World fondations / 2026-05-08).
/// Sits as a child of <see cref="MapPan2DComponent"/>'s world tree, above
/// the world map sprite and the POI container.
///
/// <para>
/// <b>Slice 3.6 design fix — two-polygon carton (back + face).</b>
/// The slice 3.6 hotfix gave the single Carton Polygon2D the source-map
/// texture for every state, then modulated it by a beige tint at full
/// alpha. Result : a beige-tinted source map at Inconnue, not an opaque
/// beige carton. The "carton hides → carte revealed" progression was
/// visually inverted (the user perceived Esquissée as more opaque
/// because the beige tint faded together with the alpha, exposing more
/// of the source map). This slice splits the carton into two stacked
/// Polygon2D children per cell so the back and the face can be
/// controlled independently :
/// <list type="bullet">
///   <item><b>CartonFace</b> (z 10) — Polygon2D <i>with</i> the source-map
///         texture and absolute-source UV coords (the slice 3.6 hotfix
///         math). Alpha resolved by
///         <see cref="TileKnowledgeStateHelpers.ResolveCartonFaceAlpha"/>.
///         Rendered <i>below</i> the back so the back masks it when the
///         cell is fully Inconnue.</item>
///   <item><b>CartonBack</b> (z 11) — Polygon2D, optionally textured by
///         <see cref="CartonBackTexture"/> (slice 3.6.1). When the texture
///         is null, modulated by <see cref="CartonBaseTint"/> (beige
///         opaque) — the original "dos de carte" behaviour. When set, the
///         same iso-losange bitmap is repeated identically on every
///         cell's back, modulate RGB clamped to white so the bitmap is
///         not tinted (alpha still resolved by
///         <see cref="TileKnowledgeStateHelpers.ResolveCartonBackAlpha"/>).
///         Acts as the visual blocker that hides face + world map below
///         it when the cell is Inconnue.</item>
/// </list>
/// The five-state visual table is now :
/// <list type="bullet">
///   <item><b>Inconnue</b> — back alpha 1.0, face alpha 0.0 → opaque beige
///         losange (or opaque CartonBackTexture losange if set), source
///         map fully hidden.</item>
///   <item><b>Pressentie</b> — back 0.7, face 0.3 → mostly back with a
///         hint of the source-map slice surfacing.</item>
///   <item><b>Esquissée</b> — back 0.4, face 0.6 → source map dominates,
///         back residual. Drill autorisé.</item>
///   <item><b>Levée</b> — back 0.0, face 0.0 → both polygons invisible,
///         the WorldMapSprite below shows the unclipped world map.</item>
///   <item><b>Scellée</b> — like Levée, plus a sceau placeholder.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>PaletteSwatch retired.</b> The 3-band palette swatch carried the
/// "hint of palette" diegetic signal at slice 2 when there was no source
/// map per cell. With CartonFace showing the actual source-map slice, the
/// swatch's role is fulfilled by the face. The swatch node is kept in the
/// scene for back-compat (any consumer that walks the cell tree by name
/// won't crash) but is set <c>Visible = false</c> at every state by
/// <see cref="TileKnowledgeStateHelpers.ShouldRenderPaletteSwatch"/>.
/// </para>
///
/// <para>
/// <b>Z-stack inside a cell (top to bottom in screen-space, i.e.
/// drawn-last is on top).</b>
/// <list type="bullet">
///   <item>Sceau (z 12) — only Scellée.</item>
///   <item>CartonBack (z 11) — beige opaque (or CartonBackTexture iso
///         losange), the visual blocker.</item>
///   <item>CartonFace (z 10) — source-map slice, surfaces under the back.</item>
///   <item>PaletteSwatch (z -1) — hidden by default.</item>
///   <item>Shadow (z -2) — under everything.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Slice 3.5 iso refactor preserved.</b> Every renderable is
/// <see cref="Polygon2D"/> with iso-diamond vertices. The hit-test math
/// agrees with the visual shape ; the
/// <see cref="GridProjection.IsoDiamondDown"/> default for L1 World is
/// unchanged.
/// </para>
///
/// <para>
/// <b>Slice 3.6 — Iso bitmap atlas.</b> <see cref="EnableSourceMapAtlas"/>
/// assigns the full source-map texture as <c>CartonFace.Texture</c> (every
/// cell shares the same <see cref="Texture2D"/> reference, no pixel
/// duplication) and sets <c>CartonFace.UV</c> to the four diamond
/// vertices in absolute source-map pixel coords. Each cell samples its
/// own slice ; the polygon shape clips out-of-diamond pixels.
/// </para>
///
/// <para>
/// <b>Slice 3.6.1 — CartonBack uniform texture (option A).</b> When
/// <see cref="CartonBackTexture"/> is non-null, every cell's
/// <c>CartonBack</c> shares the same <see cref="Texture2D"/> reference
/// and its UV is set to the four edge-midpoints of the bitmap (the same
/// iso-losange UV math as the legacy <c>SetCellBitmap</c> placeholder
/// path). The bitmap is expected to be authored as an iso losange
/// (width = 2 × height, e.g. 256×128 px) so the diamond vertices map
/// onto a clean centered slice with no skew. Same texture, same UV on
/// every cell — that is option A. UV in source coords (option B,
/// per-cell sampling of a world-spanning mask) is intentionally not
/// implemented here. Re-applying after an inspector change is one call
/// to <see cref="RefreshCartonBackTexture"/>.
/// </para>
///
/// <para>
/// <b>Tween discipline.</b> Each cell maintains at most one active tween
/// in <see cref="_activeCellTweens"/>. Before scheduling a new
/// transition (state change), the prior tween is killed. The slice 3.6
/// design fix runs all carton-affecting tweens (alpha, rotation, position,
/// scale) on both back and face in parallel, so the pliure-soulèvement
/// at Esquissée → Levée and the drill-blocked pulse stay visually coherent.
/// </para>
/// </summary>
public partial class FogTileLayer : Node2D
{
    /// <summary>
    /// Slice 1 placeholder cell size in world pixels. The E2.1 master
    /// 3840×2160 produces 30×17 = 510 cells in rect mode, ~600 cells in
    /// iso mode (cf. <see cref="FogTileGridLogic.ComputeGridSize"/>).
    /// </summary>
    [Export] public int CellSizePx { get; set; } = 128;

    /// <summary>
    /// Slice 3.5 -- grid projection mode. Default
    /// <see cref="GridProjection.IsoDiamondDown"/> for L1 World ; rect
    /// mode is preserved for any future overlay that doesn't need iso.
    /// </summary>
    [Export] public GridProjection Projection { get; set; } = GridProjection.IsoDiamondDown;

    /// <summary>
    /// Static vertical offset for the 'tuile flottante' effect (Varn §4.1).
    /// Negative because Y grows downward in world coords.
    /// </summary>
    [Export] public float FogYOffset { get; set; } = -8f;

    /// <summary>
    /// Static shadow offset (Varn §4.3). Modulate alpha 0.4, blur-by-margin
    /// (placeholder polygon is solid so no actual gaussian blur).
    /// </summary>
    [Export] public Vector2 ShadowOffset { get; set; } = new Vector2(6f, 10f);

    /// <summary>
    /// Z-index of the parent FogContainer.
    /// </summary>
    [Export] public int FogZIndex { get; set; } = 10;

    /// <summary>
    /// Pliure-soulèvement duration on <c>Esquissée → Levée</c>.
    /// </summary>
    [Export] public float PliureDurationSec { get; set; } = 0.80f;

    /// <summary>
    /// Generic crossfade duration for non-pliure transitions.
    /// </summary>
    [Export] public float CrossfadeDurationSec { get; set; } = 0.30f;

    /// <summary>Pliure rotation angle (degrees) at end of transition.</summary>
    [Export] public float PliureRotationDegrees { get; set; } = 25f;

    /// <summary>Vertical lift amount during pliure, expressed as a fraction of cell height.</summary>
    [Export] public float PliureLiftFractionOfCell { get; set; } = 0.25f;

    /// <summary>
    /// Carton beige-écru base modulate (Varn §4.2). Applied to
    /// <c>CartonBack</c> only when <see cref="CartonBackTexture"/> is null
    /// ; <c>CartonFace</c> stays at white modulate (1, 1, 1, alpha) so the
    /// source-map pixels are not tinted. When CartonBackTexture is set,
    /// CartonBack RGB is forced to white so the iso-losange bitmap is
    /// rendered untinted (alpha still drives visibility).
    /// </summary>
    [Export] public Color CartonBaseTint { get; set; } = new Color(0.91f, 0.85f, 0.72f, 1f);

    /// <summary>
    /// Slice 3.6.1 — option A "uniform mask per cell". When non-null, this
    /// iso-losange bitmap (authored as 2:1 ratio, e.g. 256×128 px) is
    /// repeated identically on every cell's <c>CartonBack</c>. UV is set
    /// to the four edge-midpoints of <c>GetSize()</c> so the diamond
    /// vertices map onto a centered, axis-aligned slice of the bitmap.
    /// Same texture reference is shared across all cells — no pixel
    /// duplication. When null, CartonBack falls back to a flat
    /// <see cref="CartonBaseTint"/>-modulated quad (the original behaviour).
    /// Read at <see cref="Configure"/> time ; call
    /// <see cref="RefreshCartonBackTexture"/> to re-apply after an
    /// inspector edit.
    /// </summary>
    [Export] public Texture2D? CartonBackTexture { get; set; }

    /// <summary>
    /// Sceau placeholder color — cire rouge stylisée (Varn §1.2).
    /// </summary>
    [Export] public Color SceauPlaceholderColor { get; set; } = new Color(0.635f, 0.22f, 0.18f, 0.85f);

    /// <summary>Sceau placeholder radius in world pixels (12-gon disc).</summary>
    [Export] public int SceauPlaceholderRadiusPx { get; set; } = 14;

    /// <summary>
    /// Jalon 3 (Wayfinders 3D Backing Architecture, locked 2026-05-09 +
    /// GO J3 same day) -- enable the 3D-authoritative backing for a
    /// single witness cell of the production grid. Default <c>true</c> :
    /// J3 ships with the witness on. Flip to <c>false</c> in the
    /// inspector to fall back to pure-2D pre-J3 behaviour without
    /// re-deploying (kill-switch). When false, the witness cell behaves
    /// exactly like every other cell of the grid and no 3D nodes are
    /// spawned -- safe rollback for production.
    /// </summary>
    [Export] public bool EnableJ3Witness { get; set; } = true;

    /// <summary>
    /// Jalon 3 -- the witness cell coord. Default (0, 0) : top-left iso
    /// pole of the grid. Chosen for minimum visual blast radius -- if
    /// the 3D-authoritative sync regresses, the drift shows at the
    /// visible edge of the world, not at the centre where it would
    /// distort the playable area. Per-instance override via [Export]
    /// for L2/L3 instances or playtest variants.
    /// </summary>
    [Export] public Vector2I J3WitnessCoord { get; set; } = new Vector2I(0, 0);

    private Node2D _fogContainer = null!;
    private TileKnowledgeStore? _knowledgeStore;
    private TileKnowledgeStore.KnowledgeChangedEventHandler? _knowledgeChangedHandler;
    private IFogPaletteSource? _paletteSource;

    private GridDimensions _dimensions = new(0, 0);

    /// <summary>
    /// Slice 3.6 — source map texture for the iso atlas. Stored at
    /// <see cref="EnableSourceMapAtlas"/> time so a re-enable / repopulate
    /// after <c>DespawnAll</c> can rebuild the per-cell UV against the
    /// same source. Null when atlas is not enabled.
    /// </summary>
    private Texture2D? _sourceMapTexture;

    /// <summary>
    /// Per-cell node bag, keyed by <see cref="GridCoord"/>.
    /// </summary>
    private readonly Dictionary<GridCoord, CellVisuals> _cells = new();

    /// <summary>
    /// Per-cell active tween. Killed and replaced on every state change.
    /// </summary>
    private readonly Dictionary<GridCoord, Tween> _activeCellTweens = new();

    /// <summary>
    /// Jalon 3 -- the 3D-authoritative follower for the witness cell, or
    /// null if <see cref="EnableJ3Witness"/> is false / not configured
    /// yet. The follower owns the witness Node3D, the Area3D pickup, and
    /// the Camera3D shadow follower ; <see cref="FogTileLayer"/> drives
    /// it via per-frame <see cref="_Process"/>.
    /// </summary>
    private Cell3DBackingFollower? _j3Follower;

    public override void _Ready()
    {
        _fogContainer = new Node2D
        {
            Name = "FogContainer",
            ZIndex = FogZIndex,
        };
        AddChild(_fogContainer);
    }

    /// <summary>
    /// Jalon 3 -- per-frame sync of the 3D-authoritative witness cell.
    /// Reads the witness Node3D position via the follower, derives the
    /// 2D screen position via the forward iso projection, and applies
    /// it to the witness cell's <c>Node2D</c> in <see cref="_cells"/>.
    /// Also drives the follower Camera3D from the prod Camera2D.
    ///
    /// <para>
    /// J3 scope : the 3D node is static (constructor-positioned, never
    /// mutated this jalon), so the witness cell's screen position
    /// equals what <c>SpawnCells</c> originally computed. The path
    /// through the 3D node is what makes Option A
    /// (entity-3D-authoritative) live in runtime, not the visual
    /// outcome ; J6+ will start mutating <c>WitnessNode3D.Position</c>
    /// for animations.
    /// </para>
    /// </summary>
    public override void _Process(double delta)
    {
        if (_j3Follower is null) return;
        _j3Follower.ApplyFollowerSync();

        // Apply the 3D-derived 2D position to the witness cell's
        // Node2D. The witness cell still has its full pre-J3 visual
        // tree (Shadow, CartonFace, CartonBack, Sceau) -- only its
        // Position is now driven by the 3D-authoritative path.
        var coord = new GridCoord(J3WitnessCoord.X, J3WitnessCoord.Y);
        if (_cells.TryGetValue(coord, out var visuals))
        {
            visuals.Cell.Position = _j3Follower.WitnessCell2DPosition;
        }
    }

    /// <summary>
    /// One-shot configuration after the consumer knows the world size,
    /// has a knowledge store, and (optionally) has a palette source.
    /// </summary>
    public void Configure(
        Vector2 worldImageSize,
        TileKnowledgeStore knowledgeStore,
        Texture2D placeholderTexture,
        IFogPaletteSource? paletteSource = null)
    {
        DespawnAll();

        _knowledgeStore = knowledgeStore;
        _paletteSource = paletteSource;
        _dimensions = FogTileGridLogic.ComputeGridSize(
            new PanVec2(worldImageSize.X, worldImageSize.Y), CellSizePx, Projection);

        SpawnCells(placeholderTexture);

        TrySpawnJ3Follower();

        _knowledgeChangedHandler = OnKnowledgeChanged;
        _knowledgeStore.KnowledgeChanged += _knowledgeChangedHandler;

        var paletteSummary = paletteSource is null
            ? "no palette source (neutral fallback)"
            : $"palette source attached (fallback={(paletteSource is BakedFogPaletteSource b && b.IsFallback)})";
        var backTextureSummary = CartonBackTexture is null
            ? "CartonBackTexture=none (beige fallback)"
            : $"CartonBackTexture={CartonBackTexture.GetSize()} px (uniform per cell)";
        GD.Print(
            $"[FogTileLayer] configured: projection={Projection} {_dimensions.Columns}×{_dimensions.Rows} " +
            $"= {_dimensions.TotalCells} cells, cellSize={CellSizePx}px, " +
            $"yOffset={FogYOffset}, store entries={knowledgeStore.NonDefaultEntryCount}, " +
            $"{paletteSummary}, {backTextureSummary}");
    }

    /// <summary>
    /// Hit-test entry point. Translates a world-space cursor position to
    /// a grid coord using the same <see cref="FogTileGridLogic"/> seam
    /// every other consumer uses, in the configured projection.
    ///
    /// <para>
    /// 2026-05-09 hotfix : the renderer offsets every cell by
    /// <see cref="FogYOffset"/> on Y to fake the floating-tile effect
    /// (and, in the future, tile thickness). The hit-test must undo
    /// the same offset before inverting the iso projection, otherwise
    /// the player clicks the visual top of a tile and the math
    /// resolves to a cell one row up. Visible at -8 px, catastrophic
    /// once tile assets carry a thicker skirt. Pinned by
    /// <c>FogTileGridLogicTests.Iso_hit_test_compensates_for_render_y_offset</c>.
    /// </para>
    /// </summary>
    public GridCoord? WorldPositionToCell(Vector2 worldPosition)
    {
        return FogTileGridLogic.WorldPositionToCell(
            new PanVec2(worldPosition.X, worldPosition.Y - FogYOffset),
            CellSizePx,
            _dimensions,
            Projection);
    }

    /// <summary>Current grid dimensions. Returns zero-zero before <see cref="Configure"/>.</summary>
    public GridDimensions Dimensions => _dimensions;

    /// <summary>
    /// Slice 3.5 livrable 4 + slice 3.6 hotfix + slice 3.6 design fix --
    /// assign a bitmap to a single cell's <b>CartonFace</b>. The bitmap
    /// replaces the face placeholder (no texture) for that cell ; the
    /// CartonBack stays a flat beige modulated quad regardless. Other
    /// cell rendering (shadow, sceau, alphas) is unchanged.
    ///
    /// <para>
    /// <b>Two UV modes.</b>
    /// <list type="bullet">
    ///   <item><b><paramref name="sourceMapCellCenter"/> null</b> -- legacy
    ///         slice 3.5 mode. Caller passes a per-cell texture (e.g. a
    ///         future per-state Mira bitmap) ; UV is set to the four
    ///         edge-midpoints of <paramref name="bitmap"/>'s
    ///         <c>GetSize()</c>, mapping the diamond vertices onto the
    ///         centered, axis-aligned slice of the texture.</item>
    ///   <item><b><paramref name="sourceMapCellCenter"/> non-null</b> --
    ///         slice 3.6 source-map atlas mode. Caller passes the full
    ///         source map (same texture for every cell) plus this cell's
    ///         center expressed in source-map pixel coords. UV is set to
    ///         the four diamond vertices of the cell in <i>absolute
    ///         source-map pixel coords</i>, so each cell samples its own
    ///         slice of the source map.</item>
    /// </list>
    /// </para>
    /// </summary>
    public void SetCellBitmap(GridCoord coord, Texture2D bitmap, Vector2? sourceMapCellCenter = null)
    {
        if (!_cells.TryGetValue(coord, out var visuals))
        {
            GD.PushWarning(
                $"[FogTileLayer] SetCellBitmap({coord.Col},{coord.Row}): " +
                $"cell not in registry (Configure not called or coord out of range)");
            return;
        }
        visuals.CartonFace.Texture = bitmap;
        visuals.CartonFace.UV = sourceMapCellCenter is { } center
            ? ComputeIsoCartonUvInSourceCoords(center)
            : ComputeIsoCartonUv(bitmap);
    }

    /// <summary>
    /// Slice 3.5 livrable 4 -- clear any per-tile bitmap on a cell's
    /// CartonFace, falling back to the placeholder (no texture, alpha
    /// resolver still in charge of visibility). Idempotent. Also clears
    /// the explicit UV array so the face renders as a flat modulated
    /// colour without spurious sampling.
    /// </summary>
    public void ClearCellBitmap(GridCoord coord)
    {
        if (!_cells.TryGetValue(coord, out var visuals))
        {
            GD.PushWarning(
                $"[FogTileLayer] ClearCellBitmap({coord.Col},{coord.Row}): " +
                $"cell not in registry");
            return;
        }
        visuals.CartonFace.Texture = null;
        visuals.CartonFace.UV = System.Array.Empty<Vector2>();
    }

    /// <summary>
    /// Slice 3.5 livrable 4 -- diagnostic surface for tests + the runtime
    /// HUD overlay. Returns the texture currently bound to the cell's
    /// CartonFace, or null if no bitmap is plugged in.
    /// </summary>
    public Texture2D? GetCellBitmap(GridCoord coord)
    {
        if (!_cells.TryGetValue(coord, out var visuals)) return null;
        return visuals.CartonFace.Texture;
    }

    /// <summary>
    /// Slice 3.6 livrable 2 + hotfix + design fix -- enable the source-map
    /// atlas mode. For every cell in the grid, assign the full
    /// <paramref name="sourceMap"/> texture as the cell's <b>CartonFace</b>
    /// texture and set its UV to the four diamond vertices of the cell
    /// expressed in absolute source-map pixel coords. CartonBack stays
    /// untouched (no texture, beige modulate).
    ///
    /// <para>
    /// <b>Why this dropped AtlasTexture.</b> The first slice 3.6 attempt
    /// gave each cell its own <c>AtlasTexture</c> with a per-cell
    /// <c>Region</c> + UV in region-local pixel coords. At E2 smoke-test
    /// every cell rendered the same green slice (~the top-left of the
    /// source map). Root cause : Godot 4's <c>Polygon2D</c> +
    /// <c>AtlasTexture</c> path does <i>not</i> honour the AtlasTexture's
    /// region offset when computing texel coords from the UV array. The
    /// hotfix dropped AtlasTexture and computes UV in absolute source-map
    /// coords. Single shared texture allocation preserved.
    /// </para>
    ///
    /// <para>
    /// Idempotent : calling twice rebuilds the UV arrays from the latest
    /// state. Re-callable after a <c>DespawnAll</c> + <c>SpawnCells</c>
    /// cycle.
    /// </para>
    /// </summary>
    public void EnableSourceMapAtlas(Texture2D sourceMap)
    {
        if (sourceMap is null)
        {
            GD.PushWarning("[FogTileLayer] EnableSourceMapAtlas: sourceMap is null, ignored");
            return;
        }
        _sourceMapTexture = sourceMap;

        var isoShift = FogTileGridLogic.ComputeIsoOriginShift(_dimensions, CellSizePx, Projection);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var assigned = 0;
        foreach (var (coord, _) in _cells)
        {
            var center = FogTileGridLogic.ComputeCellCenter(coord, CellSizePx, Projection);
            var sourceCenter = new Vector2(center.X + isoShift.X, center.Y + isoShift.Y);
            SetCellBitmap(coord, sourceMap, sourceCenter);
            assigned++;
        }
        sw.Stop();
        GD.Print(
            $"[FogTileLayer] EnableSourceMapAtlas: assigned {assigned} cells with source-map UV " +
            $"on CartonFace in {sw.ElapsedMilliseconds} ms (source size = {sourceMap.GetSize()}, " +
            $"projection = {Projection}, cellSize = {CellSizePx}px) -- single shared texture, " +
            $"per-cell absolute UV, CartonBack untouched");
    }

    /// <summary>
    /// Slice 3.6 livrable 2 -- disable the source-map atlas mode by
    /// clearing the per-cell bitmap on every cell's CartonFace, falling
    /// back to the face placeholder (no texture). CartonBack is unaffected.
    /// Idempotent.
    /// </summary>
    public void DisableSourceMapAtlas()
    {
        _sourceMapTexture = null;
        foreach (var (coord, _) in _cells)
        {
            ClearCellBitmap(coord);
        }
        GD.Print("[FogTileLayer] DisableSourceMapAtlas: reverted all CartonFace to placeholder");
    }

    /// <summary>
    /// Slice 3.6.1 -- re-apply the current <see cref="CartonBackTexture"/>
    /// (or clear it, if null) to every existing cell's CartonBack, then
    /// resync each cell's modulate RGB + alpha for the current knowledge
    /// state. Cheap helper for "Didier dropped a new PNG into the
    /// inspector between two runs and wants it live without re-Configure".
    /// Idempotent. No-op if Configure was never called.
    /// </summary>
    public void RefreshCartonBackTexture()
    {
        if (_cells.Count == 0)
        {
            GD.Print("[FogTileLayer] RefreshCartonBackTexture: no cells (Configure not called yet) — no-op");
            return;
        }

        var applied = 0;
        foreach (var (coord, visuals) in _cells)
        {
            ApplyCartonBackTextureBinding(visuals.CartonBack);
            // Resync modulate (RGB depends on texture presence) for the
            // current state ; skip animation since this is a static
            // re-skin, not a state transition.
            var state = _knowledgeStore?.GetState(coord) ?? TileKnowledgeState.Inconnue;
            ApplyVisualState(coord, state, animate: false);
            applied++;
        }
        var summary = CartonBackTexture is null
            ? "cleared (beige fallback)"
            : $"re-bound to {CartonBackTexture.GetSize()} px";
        GD.Print($"[FogTileLayer] RefreshCartonBackTexture: {applied} cells {summary}");
    }

    /// <summary>
    /// Slice 3.6 livrable 1 -- compute the UV array for an iso-diamond
    /// CartonFace when textured by a generic <see cref="Texture2D"/> (no
    /// per-cell source-map mapping). Returns four UV points in pixel
    /// coords matching the order of <see cref="BuildCellVertices"/> :
    /// top, right, bottom, left.
    /// </summary>
    private static Vector2[] ComputeIsoCartonUv(Texture2D texture)
    {
        var size = texture.GetSize();
        var rW = size.X;
        var rH = size.Y;
        return new[]
        {
            new Vector2(rW * 0.5f, 0f),
            new Vector2(rW,        rH * 0.5f),
            new Vector2(rW * 0.5f, rH),
            new Vector2(0f,        rH * 0.5f),
        };
    }

    /// <summary>
    /// Slice 3.6 hotfix -- compute the UV array for an iso-diamond
    /// CartonFace when textured by the shared source map and sampled at
    /// the cell's absolute pixel coords. Returns four UV points in
    /// source-map pixel coords (top, right, bottom, left vertices).
    /// </summary>
    private Vector2[] ComputeIsoCartonUvInSourceCoords(Vector2 cellCenterInSourceCoords)
    {
        var (top, right, bottom, left) = FogTileGridLogic.ComputeIsoCartonUvInSourceCoords(
            new PanVec2(cellCenterInSourceCoords.X, cellCenterInSourceCoords.Y),
            CellSizePx,
            Projection);
        return new[]
        {
            new Vector2(top.X,    top.Y),
            new Vector2(right.X,  right.Y),
            new Vector2(bottom.X, bottom.Y),
            new Vector2(left.X,   left.Y),
        };
    }

    /// <summary>
    /// Slice 3.6 livrable 4 — visual feedback for a drill that the player
    /// attempted at a cell whose knowledge state is below
    /// <see cref="TileKnowledgeState.Esquissee"/>. Plays a 200 ms scale
    /// pulse 1.0 → 1.05 → 1.0 on both CartonBack and CartonFace in
    /// parallel so the pulse reads as the cell pulsing as a unit.
    /// </summary>
    public void PlayDrillBlockedFeedback(GridCoord coord)
    {
        if (!_cells.TryGetValue(coord, out var visuals))
        {
            GD.PushWarning(
                $"[FogTileLayer] PlayDrillBlockedFeedback({coord.Col},{coord.Row}): " +
                $"cell not in registry (Configure not called or coord out of range)");
            return;
        }

        KillCellTween(coord);

        var baseScale = Vector2.One;
        var pulseScale = baseScale * 1.05f;

        var tween = CreateTween();
        tween.SetParallel(true);

        // Phase 1 : ramp up to peak — 100 ms on both polygons in parallel.
        tween.TweenProperty(visuals.CartonBack, "scale", pulseScale, 0.10f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(visuals.CartonFace, "scale", pulseScale, 0.10f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);

        // Chain — phase 2 : back to base over the second 100 ms.
        tween.Chain();
        tween.TweenProperty(visuals.CartonBack, "scale", baseScale, 0.10f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(visuals.CartonFace, "scale", baseScale, 0.10f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);

        tween.Chain().TweenCallback(Callable.From(() =>
        {
            visuals.CartonBack.Scale = baseScale;
            visuals.CartonFace.Scale = baseScale;
            _activeCellTweens.Remove(coord);
        }));

        _activeCellTweens[coord] = tween;
        GD.Print(
            $"[FogTileLayer] DrillBlocked feedback at cell ({coord.Col},{coord.Row}), " +
            $"current state={_knowledgeStore?.GetState(coord)}");
    }

    /// <summary>
    /// Jalon 3 -- spawn the <see cref="Cell3DBackingFollower"/> for the
    /// witness cell when <see cref="EnableJ3Witness"/> is true. No-op
    /// otherwise. The follower owns the witness Node3D, the Area3D
    /// pickup volume, and the Camera3D shadow follower ; it spawns
    /// them as children of this <c>FogTileLayer</c> Node2D so they
    /// share the world-tree pan but live in 3D space.
    ///
    /// <para>
    /// The Camera2D is read from the parent <c>MapPan2DComponent</c>
    /// (search up the scene tree for a child <c>WorldCamera</c>
    /// Camera2D, then any Camera2D). If the layer is not parented
    /// under a <c>MapPan2DComponent</c> (e.g. unit test harness), the
    /// follower spawns without a Camera2D reference and the per-frame
    /// sync degrades to identity (the witness still anchors at its
    /// canonical screen position, just without zoom/pan tracking).
    /// </para>
    /// </summary>
    private void TrySpawnJ3Follower()
    {
        if (!EnableJ3Witness) return;
        if (_j3Follower is not null)
        {
            _j3Follower.Dispose();
            _j3Follower = null;
        }

        var witness = new GridCoord(J3WitnessCoord.X, J3WitnessCoord.Y);
        if (witness.Col < 0 || witness.Col >= _dimensions.Columns ||
            witness.Row < 0 || witness.Row >= _dimensions.Rows)
        {
            GD.PushWarning(
                $"[FogTileLayer J3] witness coord ({witness.Col}, {witness.Row}) " +
                $"out of grid bounds ({_dimensions.Columns}x{_dimensions.Rows}) -- skip J3 spawn");
            return;
        }

        var shift = FogTileGridLogic.ComputeIsoOriginShift(_dimensions, CellSizePx, Projection);

        Camera2D? parentCamera = FindParentCamera2D();

        _j3Follower = new Cell3DBackingFollower(
            host: this,
            witnessCell: witness,
            cellSizePx: CellSizePx,
            isoOriginShift: shift,
            fogYOffset: FogYOffset,
            camera2D: parentCamera);

        _j3Follower.Spawn();

        var anchorSummary = parentCamera is null
            ? "no Camera2D found (no zoom/pan tracking)"
            : $"Camera2D={parentCamera.Name}";
        GD.Print(
            $"[FogTileLayer J3] spawned witness 3D follower at cell " +
            $"({witness.Col}, {witness.Row}) -- Pattern A in runtime, Option A locked. " +
            $"Hover/click on this cell now flows through Area3D physics picking. " +
            $"{anchorSummary}.");
    }

    /// <summary>
    /// Jalon 3 -- walk up the scene tree to find the parent Camera2D.
    /// Looks for a child named <c>WorldCamera</c> first (the
    /// <see cref="MapPan2DComponent"/> convention) ; falls back to any
    /// direct Camera2D child for test harnesses that do not follow the
    /// naming. Returns null if no Camera2D is found in the ancestor
    /// chain.
    /// </summary>
    private Camera2D? FindParentCamera2D()
    {
        var node = GetParent();
        while (node is not null)
        {
            var cam = node.GetNodeOrNull<Camera2D>("WorldCamera");
            if (cam is not null) return cam;
            foreach (var child in node.GetChildren())
            {
                if (child is Camera2D c) return c;
            }
            node = node.GetParent();
        }
        return null;
    }

    public override void _ExitTree()
    {
        if (_j3Follower is not null)
        {
            _j3Follower.Dispose();
            _j3Follower = null;
        }

        DespawnAll();

        if (_knowledgeStore is not null && _knowledgeChangedHandler is not null)
        {
            _knowledgeStore.KnowledgeChanged -= _knowledgeChangedHandler;
        }
        _knowledgeStore = null;
        _knowledgeChangedHandler = null;
        _paletteSource = null;
        _sourceMapTexture = null;
    }

    private void SpawnCells(Texture2D placeholderTexture)
    {
        if (_knowledgeStore is null) return;

        var halfW = CellSizePx / 2f;
        var halfH = (Projection == GridProjection.IsoDiamondDown)
            ? CellSizePx / 4f
            : CellSizePx / 2f;

        var isoOriginShift = FogTileGridLogic.ComputeIsoOriginShift(
            _dimensions, CellSizePx, Projection);

        Vector2[] cellVertices = BuildCellVertices(halfW, halfH);

        foreach (var coord in FogTileGridLogic.EnumerateCells(_dimensions))
        {
            var center = FogTileGridLogic.ComputeCellCenter(coord, CellSizePx, Projection);
            var cellPosition = new Vector2(
                center.X + isoOriginShift.X,
                center.Y + isoOriginShift.Y + FogYOffset);

            var cell = new Node2D
            {
                Name = $"Cell_{coord.Col}_{coord.Row}",
                Position = cellPosition,
            };

            // Shadow — diamond polygon offset by ShadowOffset, modulate
            // alpha 0.4. z_index -2 relative to cell.
            var shadow = new Polygon2D
            {
                Name = "Shadow",
                Polygon = cellVertices,
                Position = ShadowOffset,
                Color = new Color(0f, 0f, 0f, 0.4f),
                ZIndex = -2,
            };

            // Palette swatch — kept in the scene for back-compat but
            // hidden by default (slice 3.6 design fix retired the swatch ;
            // CartonFace carries the diegetic role now). The bands are
            // still synthesised so a future slice can flip
            // ShouldRenderPaletteSwatch back on without re-spawning cells.
            var swatch = new Node2D
            {
                Name = "PaletteSwatch",
                Position = Vector2.Zero,
                ZIndex = -1,
                Visible = false,
            };
            var palette = ResolvePalette(coord);
            var bandVerticesArrays = ComputeSwatchBandVertices(halfW, halfH);
            for (int band = 0; band < 3; band++)
            {
                var rgb = palette[band];
                var bandPolygon = new Polygon2D
                {
                    Name = $"Band_{band}",
                    Polygon = bandVerticesArrays[band],
                    Color = new Color(rgb.R, rgb.G, rgb.B, 1f),
                };
                swatch.AddChild(bandPolygon);
            }

            // CartonFace — Polygon2D with iso-diamond vertices, no texture
            // by default. EnableSourceMapAtlas / SetCellBitmap plug a
            // texture + UV. Modulate stays white (1, 1, 1, alpha) so the
            // source-map pixels are not tinted ; alpha is resolved by
            // ResolveCartonFaceAlpha. z_index 10 — above shadow + swatch,
            // below back.
            var cartonFace = new Polygon2D
            {
                Name = "CartonFace",
                Polygon = cellVertices,
                Color = new Color(1f, 1f, 1f, 0f),
                ZIndex = 10,
            };

            // CartonBack — Polygon2D with iso-diamond vertices. Slice
            // 3.6.1 : optionally textured by CartonBackTexture (shared
            // across all cells, iso-losange UV). When no texture, falls
            // back to a flat CartonBaseTint-modulated quad. Alpha is
            // resolved by ResolveCartonBackAlpha. z_index 11 — above
            // face so it masks face when fully opaque.
            var cartonBack = new Polygon2D
            {
                Name = "CartonBack",
                Polygon = cellVertices,
                Color = CartonBaseTint,
                ZIndex = 11,
            };
            ApplyCartonBackTextureBinding(cartonBack);

            // Sceau placeholder — 12-gon disc, centered. z_index 12, above
            // both carton polygons. Visible only at Scellée.
            var sceau = new Polygon2D
            {
                Name = "Sceau",
                Polygon = BuildDiscVertices(SceauPlaceholderRadiusPx, vertexCount: 12),
                Color = SceauPlaceholderColor,
                ZIndex = 12,
                Visible = false,
            };

            cell.AddChild(shadow);
            cell.AddChild(swatch);
            cell.AddChild(cartonFace);
            cell.AddChild(cartonBack);
            cell.AddChild(sceau);
            _fogContainer.AddChild(cell);

            _cells[coord] = new CellVisuals(cell, shadow, swatch, cartonFace, cartonBack, sceau);

            ApplyVisualState(coord, _knowledgeStore.GetState(coord), animate: false);
        }
    }

    /// <summary>
    /// Slice 3.6.1 — bind the current <see cref="CartonBackTexture"/> (or
    /// clear it) on a single CartonBack Polygon2D. Same texture reference
    /// is shared across all cells when set ; UV is the four edge-midpoints
    /// of the bitmap (iso-losange, top/right/bottom/left), matching the
    /// vertex order from <see cref="BuildCellVertices"/>. When the export
    /// is null, the texture and UV are cleared so the polygon renders as
    /// a flat modulated quad (legacy behaviour).
    /// </summary>
    private void ApplyCartonBackTextureBinding(Polygon2D cartonBack)
    {
        if (CartonBackTexture is { } texture)
        {
            cartonBack.Texture = texture;
            cartonBack.UV = ComputeIsoCartonUv(texture);
        }
        else
        {
            cartonBack.Texture = null;
            cartonBack.UV = System.Array.Empty<Vector2>();
        }
    }

    /// <summary>
    /// Slice 3.6.1 — resolve the modulate RGB applied to CartonBack. When
    /// <see cref="CartonBackTexture"/> is set, RGB is forced to white so
    /// the iso-losange bitmap is rendered untinted. When null, RGB is the
    /// configured <see cref="CartonBaseTint"/> (beige). Alpha is owned by
    /// the caller.
    /// </summary>
    private Color ResolveCartonBackTintRgb()
    {
        return CartonBackTexture is null
            ? CartonBaseTint
            : new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>
    /// Build the 4 vertices of a single cell relative to its center.
    /// Iso : top, right, bottom, left (clockwise from top, diamond shape).
    /// Rect : top-left, top-right, bottom-right, bottom-left (square).
    /// </summary>
    private Vector2[] BuildCellVertices(float halfW, float halfH)
    {
        if (Projection == GridProjection.IsoDiamondDown)
        {
            return new[]
            {
                new Vector2(0f, -halfH),  // top
                new Vector2(halfW, 0f),   // right
                new Vector2(0f, halfH),   // bottom
                new Vector2(-halfW, 0f),  // left
            };
        }
        return new[]
        {
            new Vector2(-halfW, -halfH),
            new Vector2(halfW, -halfH),
            new Vector2(halfW, halfH),
            new Vector2(-halfW, halfH),
        };
    }

    /// <summary>
    /// Build the three palette-swatch band vertex arrays for the iso
    /// diamond. Retained from slice 3.5 for back-compat ; the swatch is
    /// hidden by default at slice 3.6 design fix but the bands are still
    /// synthesised so a future flip of
    /// <see cref="TileKnowledgeStateHelpers.ShouldRenderPaletteSwatch"/>
    /// works without re-spawning cells.
    /// </summary>
    private Vector2[][] ComputeSwatchBandVertices(float halfW, float halfH)
    {
        if (Projection != GridProjection.IsoDiamondDown)
        {
            var bandH = halfH * 2f / 3f;
            var result = new Vector2[3][];
            for (int b = 0; b < 3; b++)
            {
                var top = -halfH + b * bandH;
                var bot = top + bandH;
                result[b] = new[]
                {
                    new Vector2(-halfW, top),
                    new Vector2(halfW, top),
                    new Vector2(halfW, bot),
                    new Vector2(-halfW, bot),
                };
            }
            return result;
        }

        var T = new Vector2(0f, -halfH);
        var R = new Vector2(halfW, 0f);
        var B = new Vector2(0f, halfH);
        var L = new Vector2(-halfW, 0f);

        Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

        var p1TL = Lerp(T, L, 1f / 3f);
        var p1RB = Lerp(R, B, 1f / 3f);
        var strip0 = new[] { T, R, p1RB, p1TL };

        var p2TL = Lerp(T, L, 2f / 3f);
        var p2RB = Lerp(R, B, 2f / 3f);
        var strip1 = new[] { p1TL, p1RB, p2RB, p2TL };

        var strip2 = new[] { p2TL, p2RB, B, L };

        return new[] { strip0, strip1, strip2 };
    }

    /// <summary>
    /// Build a regular n-gon disc with given radius, vertices ordered
    /// counter-clockwise starting from angle 0 (right). Used for the
    /// sceau placeholder.
    /// </summary>
    private static Vector2[] BuildDiscVertices(int radius, int vertexCount)
    {
        var result = new Vector2[vertexCount];
        var twoPi = System.MathF.PI * 2f;
        for (int i = 0; i < vertexCount; i++)
        {
            var angle = i * twoPi / vertexCount;
            result[i] = new Vector2(
                radius * System.MathF.Cos(angle),
                radius * System.MathF.Sin(angle));
        }
        return result;
    }

    private PaletteRgb[] ResolvePalette(GridCoord coord)
    {
        return _paletteSource?.GetPaletteForCell(coord) ?? PaletteRgb.FallbackPalette();
    }

    private void DespawnAll()
    {
        foreach (var (_, tween) in _activeCellTweens)
        {
            if (tween is not null && tween.IsValid())
            {
                tween.Kill();
            }
        }
        _activeCellTweens.Clear();

        foreach (var (_, visuals) in _cells)
        {
            visuals.Cell?.QueueFree();
        }
        _cells.Clear();
    }

    private void OnKnowledgeChanged(int col, int row, int newState)
    {
        var coord = new GridCoord(col, row);
        ApplyVisualState(coord, (TileKnowledgeState)newState, animate: true);
    }

    private void ApplyVisualState(GridCoord coord, TileKnowledgeState state, bool animate)
    {
        if (!_cells.TryGetValue(coord, out var visuals)) return;

        var targetBackAlpha = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state);
        var targetFaceAlpha = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(state);
        var showSwatch = TileKnowledgeStateHelpers.ShouldRenderPaletteSwatch(state);
        var showSceau = TileKnowledgeStateHelpers.ShouldRenderSceau(state);
        var backRgb = ResolveCartonBackTintRgb();

        if (!animate)
        {
            visuals.CartonBack.Color = WithAlpha(backRgb, targetBackAlpha);
            visuals.CartonBack.RotationDegrees = 0f;
            visuals.CartonBack.Position = Vector2.Zero;
            visuals.CartonBack.Scale = Vector2.One;
            visuals.CartonBack.Visible = targetBackAlpha > 0f;

            visuals.CartonFace.Color = new Color(1f, 1f, 1f, targetFaceAlpha);
            visuals.CartonFace.RotationDegrees = 0f;
            visuals.CartonFace.Position = Vector2.Zero;
            visuals.CartonFace.Scale = Vector2.One;
            visuals.CartonFace.Visible = targetFaceAlpha > 0f;

            // Shadow tied to the back's opacity — when the back is gone
            // the cell as a whole is gone (Levée / Scellée), no shadow.
            visuals.Shadow.Color = new Color(0f, 0f, 0f, targetBackAlpha * 0.4f);
            visuals.Shadow.Visible = targetBackAlpha > 0f;

            visuals.Swatch.Visible = showSwatch;
            visuals.Sceau.Visible = showSceau;
            return;
        }

        KillCellTween(coord);

        // Pliure plays only on Esquissée → Levée, when the back is the
        // last visible carton-blocker. The trigger condition is "we are
        // transitioning to Levée AND the back is still drawn" — using
        // back alpha (not face alpha) is correct since the back is the
        // visual blocker that "lifts off".
        var isPliure = state == TileKnowledgeState.Levee
            && visuals.CartonBack.Color.A > 0f;

        if (isPliure)
        {
            SchedulePliure(coord, visuals, state, showSwatch, showSceau);
        }
        else
        {
            ScheduleCrossfade(coord, visuals, state, targetBackAlpha, targetFaceAlpha, showSwatch, showSceau);
        }
    }

    private void SchedulePliure(
        GridCoord coord,
        CellVisuals visuals,
        TileKnowledgeState targetState,
        bool showSwatch,
        bool showSceau)
    {
        var tween = CreateTween();
        tween.SetParallel(true);

        // Pliure rotation + lift — applied to both back and face in
        // parallel so the cell reads as a single sheet lifting (the back
        // is the visual sheet ; the face slides off underneath as it
        // fades). The two polygons share Position / Rotation tweens with
        // the same curve.
        var halfH = (Projection == GridProjection.IsoDiamondDown)
            ? CellSizePx / 4f
            : CellSizePx / 2f;
        var liftPx = -(halfH * 2f) * PliureLiftFractionOfCell;
        var liftPos = new Vector2(0f, liftPx);

        tween.TweenProperty(visuals.CartonBack, "rotation_degrees", PliureRotationDegrees, PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(visuals.CartonBack, "position", liftPos, PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);

        tween.TweenProperty(visuals.CartonFace, "rotation_degrees", PliureRotationDegrees, PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(visuals.CartonFace, "position", liftPos, PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);

        // Alpha fade — back fades out over the full duration with a
        // hold-then-fade two-stage curve preserved from slice 2 ; face
        // fades out in parallel (face starts at whatever alpha the
        // current state had, fades to 0 in step with the back).
        var startBackAlpha = visuals.CartonBack.Color.A;
        var startFaceAlpha = visuals.CartonFace.Color.A;
        var preFadeDuration = PliureDurationSec - 0.20f;
        if (preFadeDuration > 0f)
        {
            tween.TweenProperty(visuals.CartonBack, "color:a", startBackAlpha, preFadeDuration);
            tween.TweenProperty(visuals.CartonFace, "color:a", startFaceAlpha, preFadeDuration);
        }
        tween.TweenProperty(visuals.CartonBack, "color:a", 0f, 0.20f)
            .SetDelay(System.MathF.Max(preFadeDuration, 0f));
        tween.TweenProperty(visuals.CartonFace, "color:a", 0f, 0.20f)
            .SetDelay(System.MathF.Max(preFadeDuration, 0f));

        tween.TweenProperty(visuals.Shadow, "color:a", 0f, PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);

        tween.Chain().TweenCallback(Callable.From(() =>
        {
            FinalizeCell(visuals, targetState, showSwatch, showSceau);
            _activeCellTweens.Remove(coord);
        }));

        _activeCellTweens[coord] = tween;
    }

    private void ScheduleCrossfade(
        GridCoord coord,
        CellVisuals visuals,
        TileKnowledgeState targetState,
        float targetBackAlpha,
        float targetFaceAlpha,
        bool showSwatch,
        bool showSceau)
    {
        // Reset transform on both polygons (a previous pliure may have
        // left rotation / position non-zero on the cached visuals).
        visuals.CartonBack.RotationDegrees = 0f;
        visuals.CartonBack.Position = Vector2.Zero;
        visuals.CartonBack.Scale = Vector2.One;
        visuals.CartonBack.Visible = true;

        visuals.CartonFace.RotationDegrees = 0f;
        visuals.CartonFace.Position = Vector2.Zero;
        visuals.CartonFace.Scale = Vector2.One;
        visuals.CartonFace.Visible = true;

        visuals.Swatch.Visible = showSwatch;
        visuals.Sceau.Visible = showSceau;

        var tween = CreateTween();
        tween.SetParallel(true);

        tween.TweenProperty(visuals.CartonBack, "color:a", targetBackAlpha, CrossfadeDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(visuals.CartonFace, "color:a", targetFaceAlpha, CrossfadeDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);

        tween.TweenProperty(visuals.Shadow, "color:a", targetBackAlpha * 0.4f, CrossfadeDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);

        tween.Chain().TweenCallback(Callable.From(() =>
        {
            FinalizeCell(visuals, targetState, showSwatch, showSceau);
            _activeCellTweens.Remove(coord);
        }));

        _activeCellTweens[coord] = tween;
    }

    private void FinalizeCell(CellVisuals visuals, TileKnowledgeState state, bool showSwatch, bool showSceau)
    {
        var backAlpha = TileKnowledgeStateHelpers.ResolveCartonBackAlpha(state);
        var faceAlpha = TileKnowledgeStateHelpers.ResolveCartonFaceAlpha(state);
        var backRgb = ResolveCartonBackTintRgb();

        visuals.CartonBack.Color = WithAlpha(backRgb, backAlpha);
        visuals.CartonBack.Visible = backAlpha > 0f;
        visuals.CartonBack.RotationDegrees = 0f;
        visuals.CartonBack.Position = Vector2.Zero;
        visuals.CartonBack.Scale = Vector2.One;

        visuals.CartonFace.Color = new Color(1f, 1f, 1f, faceAlpha);
        visuals.CartonFace.Visible = faceAlpha > 0f;
        visuals.CartonFace.RotationDegrees = 0f;
        visuals.CartonFace.Position = Vector2.Zero;
        visuals.CartonFace.Scale = Vector2.One;

        visuals.Shadow.Color = new Color(0f, 0f, 0f, backAlpha * 0.4f);
        visuals.Shadow.Visible = backAlpha > 0f;

        visuals.Swatch.Visible = showSwatch;
        visuals.Sceau.Visible = showSceau;
    }

    private void KillCellTween(GridCoord coord)
    {
        if (_activeCellTweens.TryGetValue(coord, out var tween))
        {
            if (tween is not null && tween.IsValid())
            {
                tween.Kill();
            }
            _activeCellTweens.Remove(coord);
        }
    }

    private static Color WithAlpha(Color baseColor, float alpha)
    {
        return new Color(baseColor.R, baseColor.G, baseColor.B, alpha);
    }

    /// <summary>
    /// Per-cell node references. Slice 3.6 design fix splits the single
    /// Carton Polygon2D into <c>CartonFace</c> (textured, source-map slice)
    /// and <c>CartonBack</c> (no texture, beige opaque dos de carte).
    /// </summary>
    private readonly record struct CellVisuals(
        Node2D Cell,
        Polygon2D Shadow,
        Node2D Swatch,
        Polygon2D CartonFace,
        Polygon2D CartonBack,
        Polygon2D Sceau);
}
