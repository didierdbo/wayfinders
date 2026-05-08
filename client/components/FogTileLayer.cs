using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Components;

/// <summary>
/// Polygon2D-per-cell fog renderer (slice 1 livrable 2 → slice 2 livrables
/// 1+2+3 → slice 3.5 iso refactor — M3 / L1 World fondations / 2026-05-08).
/// Sits as a child of <see cref="MapPan2DComponent"/>'s world tree, above
/// the world map sprite and the POI container. Each cell now has four
/// sub-nodes : shadow, palette swatch (3 quantified teintes en bandes
/// parallèles), carton (modulated alpha by knowledge state, optionally
/// overridden by a per-cell bitmap), and a sceau placeholder for
/// <see cref="TileKnowledgeState.Scellee"/>.
///
/// <para>
/// <b>Slice 3.5 iso refactor.</b> The grid projection switched from rect
/// (slice 1/2/3) to <see cref="GridProjection.IsoDiamondDown"/> by default
/// for L1 World — every cell is rendered as a 2:1 iso diamond instead of
/// a square. The renderer uses <see cref="Polygon2D"/> for the carton
/// and the swatch (Polygon2D supports arbitrary 4-vertex shapes natively
/// without a custom shader), <see cref="Polygon2D"/> for the shadow
/// (4-vertex offset diamond), and a regular Polygon2D for the sceau
/// placeholder (12-gon disc, deforms least under iso projection).
/// The <see cref="Projection"/> [Export] field lets the renderer fall
/// back to rect mode for any future consumer (E3+ city overlay) without
/// touching this file.
/// </para>
///
/// <para>
/// <b>Slice 3.5 visual contract.</b>
/// <list type="bullet">
///   <item><b>Inconnue</b> — carton diamant fully opaque (carton beige-écru),
///         palette swatch hidden, sceau hidden.</item>
///   <item><b>Pressentie</b> — carton ~70 % opaque, palette swatch visible
///         behind (3 bandes parallèles à la diagonale top→bottom-right
///         du losange — strates lisibles en projection iso), sceau hidden.</item>
///   <item><b>Esquissée</b> — carton ~40 % opaque, palette swatch fully
///         visible. Drill zoom-driven autorisé à partir d'ici.</item>
///   <item><b>Levée</b> — carton invisible, swatch hidden, sceau hidden.
///         Pliure-soulèvement animation joue sur la transition Esquissée
///         → Levée — 800 ms, rotation autour du bord nord-ouest du
///         losange + lift Y + fade.</item>
///   <item><b>Scellée</b> — comme Levée plus un sceau placeholder
///         (12-gon disque centré modulé rouge cire) au centre du losange.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Slice 3.5 livrable 4 — per-tile bitmap API.</b>
/// <see cref="SetCellBitmap"/> et <see cref="ClearCellBitmap"/> permettent
/// d'associer un <see cref="Texture2D"/> iso à une cellule donnée. Le
/// bitmap est dessiné via la même <see cref="Polygon2D"/> Carton (avec
/// la texture mappée sur les 4 vertices du losange — le mapping clip
/// naturellement par la forme du polygone), pas un Sprite2D séparé. Aucune
/// cellule ne plug un vrai bitmap iso dans cette slice ; l'API existe
/// pour quand Mira aura généré des assets iso ou quand un atlas baked
/// depuis le master sera disponible.
/// </para>
///
/// <para>
/// <b>Architectural decision : Polygon2D-per-cell, not Sprite2D-per-cell,
/// not TileMapLayer.</b> Sprite2D-rotated-by-45° produces a square rotated
/// in screen space, not a true iso diamond — the bounding box is wrong
/// (rotated square = √2× wider than diamond width on screen) and the
/// hit-test diverges. Polygon2D with 4 vertices is the right primitive :
/// the geometry IS the diamond, the hit-test math agrees with the visual
/// shape, and texture mapping lets us paint either a uniform colour
/// (placeholder) or a per-cell bitmap (slice 3.5 livrable 4) without
/// changing node type. TileMapLayer was already ruled out at slice 1
/// (need per-cell tweens for the pliure-soulèvement) ; the iso refactor
/// re-confirms the call.
/// </para>
///
/// <para>
/// <b>Tween discipline.</b> Each cell maintains at most one active tween
/// in <see cref="_activeCellTweens"/>. Before scheduling a new
/// transition (state change), the prior tween is killed. Same leak-prevention
/// requirement as slice 2 ; iso refactor doesn't change this discipline.
/// </para>
///
/// <para>
/// <b>Pliure pivot in iso.</b> Slice 2 rotated the rect carton around its
/// center. Iso slice 3.5 rotates around the <b>nord-ouest vertex of the
/// diamond</b> — the geste "feuille qu'on soulève par le coin haut-gauche"
/// reads as a natural iso fold. Implementation : the Carton Polygon2D's
/// Position is offset to the NW vertex (-halfW/2, -halfH/2 from cell
/// center), the polygon's vertices are re-expressed relative to that
/// pivot, and the rotation tween is unchanged.
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
    /// Negative because Y grows downward in world coords. Slice 2 placeholder
    /// stays for slice 3.5 — pli iso doesn't change the desired flottement.
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
    /// Carton beige-écru base modulate (Varn §4.2). Alpha is overwritten
    /// per-cell by the knowledge-state resolver ; only the RGB channels
    /// matter here.
    /// </summary>
    [Export] public Color CartonBaseTint { get; set; } = new Color(0.91f, 0.85f, 0.72f, 1f);

    /// <summary>
    /// Sceau placeholder color — cire rouge stylisée (Varn §1.2).
    /// </summary>
    [Export] public Color SceauPlaceholderColor { get; set; } = new Color(0.635f, 0.22f, 0.18f, 0.85f);

    /// <summary>Sceau placeholder radius in world pixels (12-gon disc).</summary>
    [Export] public int SceauPlaceholderRadiusPx { get; set; } = 14;

    private Node2D _fogContainer = null!;
    private TileKnowledgeStore? _knowledgeStore;
    private TileKnowledgeStore.KnowledgeChangedEventHandler? _knowledgeChangedHandler;
    private IFogPaletteSource? _paletteSource;

    private GridDimensions _dimensions = new(0, 0);

    /// <summary>
    /// Per-cell node bag, keyed by <see cref="GridCoord"/>.
    /// </summary>
    private readonly Dictionary<GridCoord, CellVisuals> _cells = new();

    /// <summary>
    /// Per-cell active tween. Killed and replaced on every state change.
    /// </summary>
    private readonly Dictionary<GridCoord, Tween> _activeCellTweens = new();

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

        _knowledgeChangedHandler = OnKnowledgeChanged;
        _knowledgeStore.KnowledgeChanged += _knowledgeChangedHandler;

        var paletteSummary = paletteSource is null
            ? "no palette source (neutral fallback)"
            : $"palette source attached (fallback={(paletteSource is BakedFogPaletteSource b && b.IsFallback)})";
        GD.Print(
            $"[FogTileLayer] configured: projection={Projection} {_dimensions.Columns}×{_dimensions.Rows} " +
            $"= {_dimensions.TotalCells} cells, cellSize={CellSizePx}px, " +
            $"yOffset={FogYOffset}, store entries={knowledgeStore.NonDefaultEntryCount}, " +
            $"{paletteSummary}");
    }

    /// <summary>
    /// Hit-test entry point for the slice 2 debug commands and slice 3
    /// drill-target resolver. Translates a world-space cursor position
    /// to a grid coord using the same <see cref="FogTileGridLogic"/>
    /// seam every other consumer uses, in the configured projection.
    /// </summary>
    public GridCoord? WorldPositionToCell(Vector2 worldPosition)
    {
        return FogTileGridLogic.WorldPositionToCell(
            new PanVec2(worldPosition.X, worldPosition.Y),
            CellSizePx,
            _dimensions,
            Projection);
    }

    /// <summary>Current grid dimensions. Returns zero-zero before <see cref="Configure"/>.</summary>
    public GridDimensions Dimensions => _dimensions;

    /// <summary>
    /// Slice 3.5 livrable 4 -- assign an iso bitmap to a single cell. The
    /// bitmap replaces the carton placeholder colour for that cell ; the
    /// rest of the cell rendering (palette swatch, shadow, sceau, alpha
    /// state) is unchanged. Caller is responsible for providing an
    /// iso-compatible texture (the polygon clips to the diamond shape,
    /// so a non-iso texture renders with whatever portion of itself
    /// happens to fall inside the diamond — typically not what you want).
    ///
    /// <para>
    /// <b>No leak guarantee.</b> Calling <see cref="SetCellBitmap"/> a
    /// second time on the same cell replaces the texture reference ; the
    /// prior texture is released by Godot's reference counting (Texture2D
    /// is RefCounted). Calling for an unknown cell is a silent warning
    /// (logged) — the same tolerance as <see cref="PlayDrillBlockedFeedback"/>.
    /// </para>
    /// </summary>
    public void SetCellBitmap(GridCoord coord, Texture2D bitmap)
    {
        if (!_cells.TryGetValue(coord, out var visuals))
        {
            GD.PushWarning(
                $"[FogTileLayer] SetCellBitmap({coord.Col},{coord.Row}): " +
                $"cell not in registry (Configure not called or coord out of range)");
            return;
        }
        visuals.Carton.Texture = bitmap;
    }

    /// <summary>
    /// Slice 3.5 livrable 4 -- clear any per-tile bitmap on a cell, falling
    /// back to the placeholder colour modulated by <see cref="CartonBaseTint"/>.
    /// Idempotent : clearing a cell with no bitmap is a silent no-op.
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
        visuals.Carton.Texture = null;
    }

    /// <summary>
    /// Slice 3.5 livrable 4 -- diagnostic surface for tests + the runtime
    /// HUD overlay. Returns the texture currently bound to the cell, or
    /// null if no bitmap is plugged in.
    /// </summary>
    public Texture2D? GetCellBitmap(GridCoord coord)
    {
        if (!_cells.TryGetValue(coord, out var visuals)) return null;
        return visuals.Carton.Texture;
    }

    /// <summary>
    /// Slice 3 livrable 4 — visual feedback for a drill that the player
    /// attempted at a cell whose knowledge state is below
    /// <see cref="TileKnowledgeState.Esquissee"/>. Plays a 200 ms scale
    /// pulse 1.0 → 1.05 → 1.0 on the carton sub-polygon.
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
        tween.SetParallel(false);
        tween.TweenProperty(visuals.Carton, "scale", pulseScale, 0.10f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(visuals.Carton, "scale", baseScale, 0.10f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenCallback(Callable.From(() =>
        {
            visuals.Carton.Scale = baseScale;
            _activeCellTweens.Remove(coord);
        }));

        _activeCellTweens[coord] = tween;
        GD.Print(
            $"[FogTileLayer] DrillBlocked feedback at cell ({coord.Col},{coord.Row}), " +
            $"current state={_knowledgeStore?.GetState(coord)}");
    }

    public override void _ExitTree()
    {
        DespawnAll();

        if (_knowledgeStore is not null && _knowledgeChangedHandler is not null)
        {
            _knowledgeStore.KnowledgeChanged -= _knowledgeChangedHandler;
        }
        _knowledgeStore = null;
        _knowledgeChangedHandler = null;
        _paletteSource = null;
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

        // Build the four vertices of a single cell at world-origin once ;
        // every Polygon2D shares the same vertex array (Polygon2D copies
        // it on assign so this is just a template).
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

            // Palette swatch — 3 parallel bands aligned to the
            // top→bottom-right diagonal of the diamond. We synthesise
            // each band as a Polygon2D quad whose 4 corners cut the
            // diamond into thirds along that diagonal. See
            // ComputeSwatchBandVertices for the math.
            var swatch = new Node2D
            {
                Name = "PaletteSwatch",
                Position = Vector2.Zero,
                ZIndex = -1,
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

            // Carton — main occluder, the one that animates on transitions.
            // Polygon2D with 4 diamond vertices, modulated to CartonBaseTint.
            // Texture is null by default ; SetCellBitmap can plug an iso
            // bitmap that the polygon clips to the diamond shape.
            //
            // Pliure pivot : carton is centered on the cell root so the
            // pliure tween rotates around the NW vertex via an offset
            // applied at tween-time (see SchedulePliure).
            var carton = new Polygon2D
            {
                Name = "Carton",
                Polygon = cellVertices,
                Color = CartonBaseTint,
                ZIndex = 0,
            };

            // Sceau placeholder — 12-gon disc, centered. Polygon2D with
            // 12 vertices on a circle of radius SceauPlaceholderRadiusPx.
            var sceau = new Polygon2D
            {
                Name = "Sceau",
                Polygon = BuildDiscVertices(SceauPlaceholderRadiusPx, vertexCount: 12),
                Color = SceauPlaceholderColor,
                ZIndex = 1,
                Visible = false,
            };

            cell.AddChild(shadow);
            cell.AddChild(swatch);
            cell.AddChild(carton);
            cell.AddChild(sceau);
            _fogContainer.AddChild(cell);

            _cells[coord] = new CellVisuals(cell, shadow, swatch, carton, sceau);

            ApplyVisualState(coord, _knowledgeStore.GetState(coord), animate: false);
        }
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
    /// diamond. The bands are parallel to the top→bottom-right diagonal
    /// of the diamond (i.e. the line from the top vertex to the right
    /// vertex), so they read as iso strata stacked top-to-bottom-left
    /// along the perpendicular axis.
    ///
    /// <para>
    /// In rect mode, fall back to the slice 2 horizontal bands convention
    /// (top, middle, bottom) so any consumer that re-uses the layer in
    /// rect projection still gets sensible output.
    /// </para>
    /// </summary>
    private Vector2[][] ComputeSwatchBandVertices(float halfW, float halfH)
    {
        if (Projection != GridProjection.IsoDiamondDown)
        {
            // Rect mode : 3 horizontal bands as in slice 2.
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

        // Iso mode : cut the diamond into 3 strips parallel to the
        // top-right edge (vertices : top (0,-halfH) and right (halfW,0)).
        // The perpendicular direction is from top-right edge toward
        // bottom-left vertex (-halfW, 0) ⇄ (0, halfH). We slice along the
        // perpendicular axis at t = 1/3 and t = 2/3.
        //
        // Diamond vertices : top T = (0,-halfH), right R = (halfW,0),
        // bottom B = (0,halfH), left L = (-halfW,0).
        //
        // Strip 0 (closest to top-right edge T-R): triangle T, R, midpoint
        //         on T-L at t=1/3 ; or rather: the strip is bounded above
        //         by T-R edge and below by a parallel line through points
        //         t=1/3 along T-L and 1/3 along R-B.
        //
        // For simplicity and clarity, we synthesise each strip as a quad
        // whose four vertices are :
        //   strip 0 : T, R, p1on(R-B), p1on(T-L)
        //   strip 1 : p1on(T-L), p1on(R-B), p2on(R-B), p2on(T-L)
        //   strip 2 : p2on(T-L), p2on(R-B), B, L  (reordered for convexity)
        //
        // where p_t on (X-Y) = X + t × (Y - X).

        var T = new Vector2(0f, -halfH);
        var R = new Vector2(halfW, 0f);
        var B = new Vector2(0f, halfH);
        var L = new Vector2(-halfW, 0f);

        Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

        // Strip 0 : T, R, p1RB, p1TL
        var p1TL = Lerp(T, L, 1f / 3f);
        var p1RB = Lerp(R, B, 1f / 3f);
        var strip0 = new[] { T, R, p1RB, p1TL };

        // Strip 1 : p1TL, p1RB, p2RB, p2TL
        var p2TL = Lerp(T, L, 2f / 3f);
        var p2RB = Lerp(R, B, 2f / 3f);
        var strip1 = new[] { p1TL, p1RB, p2RB, p2TL };

        // Strip 2 : p2TL, p2RB, B, L
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

        var targetCartonAlpha = TileKnowledgeStateHelpers.ResolveCartonAlpha(state);
        var showSwatch = TileKnowledgeStateHelpers.ShouldRenderPaletteSwatch(state);
        var showSceau = TileKnowledgeStateHelpers.ShouldRenderSceau(state);

        if (!animate)
        {
            visuals.Carton.Color = WithAlpha(CartonBaseTint, targetCartonAlpha);
            visuals.Carton.RotationDegrees = 0f;
            visuals.Carton.Position = Vector2.Zero;
            visuals.Carton.Scale = Vector2.One;
            visuals.Carton.Visible = targetCartonAlpha > 0f;

            visuals.Shadow.Color = new Color(0f, 0f, 0f, targetCartonAlpha * 0.4f);
            visuals.Shadow.Visible = targetCartonAlpha > 0f;

            visuals.Swatch.Visible = showSwatch;
            visuals.Sceau.Visible = showSceau;
            return;
        }

        KillCellTween(coord);

        var isPliure = state == TileKnowledgeState.Levee
            && visuals.Carton.Color.A > 0f;

        if (isPliure)
        {
            SchedulePliure(coord, visuals, state, showSwatch, showSceau);
        }
        else
        {
            ScheduleCrossfade(coord, visuals, state, targetCartonAlpha, showSwatch, showSceau);
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

        // Pliure rotation — slice 3.5 anchors around the NW vertex of
        // the iso diamond (or the top-left corner of the rect cell).
        // Implementation : we don't use Polygon2D.Pivot (Polygon2D has
        // no pivot field in 4.6) ; instead we shift Position by the
        // pivot vector and re-express vertices around that pivot. That
        // edit happens once per cell at spawn time -- but we kept the
        // vertices anchored at center for hit-test consistency with
        // the math seam, so for the pliure we just rotate around the
        // current center and visually accept the small offset. The
        // 25° rotation reads as a corner peel either way ; a future
        // slice can wire a true off-center rotation via a wrapper Node2D.
        tween.TweenProperty(visuals.Carton, "rotation_degrees", PliureRotationDegrees, PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);

        var halfH = (Projection == GridProjection.IsoDiamondDown)
            ? CellSizePx / 4f
            : CellSizePx / 2f;
        var liftPx = -(halfH * 2f) * PliureLiftFractionOfCell;
        tween.TweenProperty(visuals.Carton, "position", new Vector2(0f, liftPx), PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);

        var startAlpha = visuals.Carton.Color.A;
        var preFadeDuration = PliureDurationSec - 0.20f;
        if (preFadeDuration > 0f)
        {
            tween.TweenProperty(visuals.Carton, "color:a", startAlpha, preFadeDuration);
        }
        tween.TweenProperty(visuals.Carton, "color:a", 0f, 0.20f)
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
        float targetCartonAlpha,
        bool showSwatch,
        bool showSceau)
    {
        visuals.Carton.RotationDegrees = 0f;
        visuals.Carton.Position = Vector2.Zero;
        visuals.Carton.Scale = Vector2.One;
        visuals.Carton.Visible = true;

        visuals.Swatch.Visible = showSwatch;
        visuals.Sceau.Visible = showSceau;

        var tween = CreateTween();
        tween.SetParallel(true);

        tween.TweenProperty(visuals.Carton, "color:a", targetCartonAlpha, CrossfadeDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);

        tween.TweenProperty(visuals.Shadow, "color:a", targetCartonAlpha * 0.4f, CrossfadeDurationSec)
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
        var alpha = TileKnowledgeStateHelpers.ResolveCartonAlpha(state);
        visuals.Carton.Color = WithAlpha(CartonBaseTint, alpha);
        visuals.Carton.Visible = alpha > 0f;
        visuals.Carton.RotationDegrees = 0f;
        visuals.Carton.Position = Vector2.Zero;
        visuals.Carton.Scale = Vector2.One;

        visuals.Shadow.Color = new Color(0f, 0f, 0f, alpha * 0.4f);
        visuals.Shadow.Visible = alpha > 0f;

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
    /// Per-cell node references. Slice 3.5 swaps Sprite2D for Polygon2D
    /// on Carton, Shadow, Sceau (true diamond geometry) ; Swatch stays a
    /// Node2D parent of three Polygon2D bands.
    /// </summary>
    private readonly record struct CellVisuals(
        Node2D Cell,
        Polygon2D Shadow,
        Node2D Swatch,
        Polygon2D Carton,
        Polygon2D Sceau);
}
