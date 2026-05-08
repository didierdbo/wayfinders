using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Components;

/// <summary>
/// Sprite2D-per-cell fog renderer (slice 1 livrable 2 → slice 2 livrables
/// 1+2+3 — M3 / L1 World fondations / 2026-05-08). Sits as a child of
/// <see cref="MapPan2DComponent"/>'s world tree, above the world map
/// sprite and the POI container. Each cell now has four sub-nodes :
/// shadow, palette swatch (3 quantified teintes), carton (modulated
/// alpha by knowledge state), and a sceau placeholder for
/// <see cref="TileKnowledgeState.Scellee"/>.
///
/// <para>
/// <b>Slice 2 visual contract (Varn 2026-05-08 + brief slice 2).</b>
/// <list type="bullet">
///   <item><b>Inconnue</b> — carton fully opaque, palette swatch hidden,
///         sceau hidden.</item>
///   <item><b>Pressentie</b> — carton ~70 % opaque, palette swatch
///         visible behind (3 teintes en bandes horizontales), sceau hidden.</item>
///   <item><b>Esquissée</b> — carton ~40 % opaque, palette swatch fully
///         visible, sceau hidden. Drill zoom-driven autorisé à partir
///         d'ici (Varn §1.4) — slice 3 wires the actual drill predicate.</item>
///   <item><b>Levée</b> — carton invisible, palette swatch hidden, sceau
///         hidden. Pliure-soulèvement animation joue sur la transition
///         <c>Esquissée → Levée</c> — 800 ms, rotation + lift + fade.</item>
///   <item><b>Scellée</b> — comme Levée plus un sceau placeholder
///         (cercle ColorRect modulé rouge cire) au centre de la cellule.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Architectural decision : Sprite2D-per-cell, not TileMapLayer.</b>
/// Documented in Rune's 2026-05-08 audit (mémoire 'Phase 9 audit',
/// verdict MVP-friendly avec discipline). TileMapLayer bakes one tile
/// per TileSet entry into a single batched draw — there is no per-tile
/// node to tween, fade, or anchor an animation off of. Slice 2's
/// pliure-soulèvement gesture-clé per tile (Varn §4.5 lock) requires
/// per-tile transforms : Sprite2D-per-cell affordance, not a
/// TileMapLayer one. The MVP grid (510 cells) is well within the JFDI
/// scaling ceiling for 2D draw calls — Varn §5.1 caps L1 at 40-80
/// tuiles MVP, so the 510 placeholder is already a 6-10× over-allocation.
/// </para>
///
/// <para>
/// <b>YSort trap (audit §6).</b> The world tree is not ysort-sorted at
/// E2 — POIs use static z_index ordering instead. The fog layer hangs
/// off the same parent as POIs but with a higher z_index (10 default),
/// so the stacking is deterministic and immune to the YSort + offset Y
/// trap. The slight Y offset for the 'flottement' (-8 px slice 1
/// placeholder) is applied to per-cell positions, not via a YSort key,
/// so it stays purely visual.
/// </para>
///
/// <para>
/// <b>Stacking spec (slice 2).</b>
/// <list type="bullet">
///   <item>z_index 0 -- world map sprite (existing).</item>
///   <item>z_index 5 -- POI container (existing).</item>
///   <item>z_index 10 -- FogContainer parent (this layer's root).</item>
///   <item>per-cell relative z_index : Shadow=-2, PaletteSwatch=-1,
///         Carton=0, Sceau=+1.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Tween discipline.</b> Each cell maintains at most one active tween
/// in <see cref="_activeCellTweens"/>. Before scheduling a new
/// transition (state change), the prior tween is killed. This is the
/// brief slice 2 livrable 3 leak-prevention requirement : "Pas de leak
/// Tween si la transition est interrompue (fog state re-toggle pendant
/// l'animation) — gérer proprement avec Kill() sur le tween précédent".
/// </para>
///
/// <para>
/// <b>Lifecycle.</b>
/// <see cref="Configure"/> is called by the consumer once the world
/// image is loaded and its size is known. Configure spawns one cell
/// subtree (shadow + swatch + carton + sceau) per grid cell, wires
/// them under <see cref="FogContainer"/>, paints palette swatches
/// from <see cref="IFogPaletteSource"/>, and refreshes their initial
/// visual state from <see cref="TileKnowledgeStore"/>. The
/// <c>KnowledgeChanged</c> subscription is captured so
/// <see cref="_ExitTree"/> disconnects with the exact handler reference
/// (Risk #1 signal-leak trap).
/// </para>
/// </summary>
public partial class FogTileLayer : Node2D
{
    /// <summary>
    /// Slice 1 placeholder cell size in world pixels. The E2.1 master
    /// 3840×2160 produces 30×17 = 510 cells. Final cell size is design
    /// territory (Varn §5.1 organique L1, slice 3+).
    /// </summary>
    [Export] public int CellSizePx { get; set; } = 128;

    /// <summary>
    /// Static vertical offset for the 'tuile flottante' effect (Varn §4.1).
    /// Negative because Y grows downward in world coords. The full Varn
    /// spec calls for ±0.04 hauteur de tuile désynchronisé sinusoïdal
    /// (§4.4 animation flottement) — slice 2 only ships the static
    /// offset, the breathing animation arrives in slice 3+ alongside
    /// the Reduce Motion toggle.
    /// </summary>
    [Export] public float FogYOffset { get; set; } = -8f;

    /// <summary>
    /// Static shadow offset (Varn §4.3). Modulate alpha 0.4, blur-by-margin
    /// (placeholder texture is solid so no actual gaussian blur — softer
    /// texture in slice 3+ supplies the diffusion).
    /// </summary>
    [Export] public Vector2 ShadowOffset { get; set; } = new Vector2(6f, 10f);

    /// <summary>
    /// Z-index of the parent FogContainer. World map sprite uses default
    /// 0, POIs at 5, this layer at 10 keeps it above everything (Varn
    /// §4.6 layer stack). Per-cell relative z indices are applied to
    /// Shadow / PaletteSwatch / Carton / Sceau children.
    /// </summary>
    [Export] public int FogZIndex { get; set; } = 10;

    /// <summary>
    /// Pliure-soulèvement duration on <c>Esquissée → Levée</c>. Locked by
    /// Varn D-TILE-09 §4.5 at 800 ms.
    /// </summary>
    [Export] public float PliureDurationSec { get; set; } = 0.80f;

    /// <summary>
    /// Generic crossfade duration for non-pliure transitions
    /// (Inconnue ↔ Pressentie ↔ Esquissée, Levée → Scellée, et tout retour
    /// vers un niveau inférieur). 300 ms par défaut, brief slice 2.
    /// </summary>
    [Export] public float CrossfadeDurationSec { get; set; } = 0.30f;

    /// <summary>Pliure rotation angle (degrees) at end of transition. ~25° reads as a fold without becoming gimmicky.</summary>
    [Export] public float PliureRotationDegrees { get; set; } = 25f;

    /// <summary>Vertical lift amount during pliure, expressed as a fraction of cell height (Varn §4.5 "à 0.25 hauteur").</summary>
    [Export] public float PliureLiftFractionOfCell { get; set; } = 0.25f;

    /// <summary>
    /// Carton beige-écru base modulate (Varn §4.2). Alpha is overwritten
    /// per-cell by the knowledge-state resolver ; only the RGB channels
    /// matter here.
    /// </summary>
    [Export] public Color CartonBaseTint { get; set; } = new Color(0.91f, 0.85f, 0.72f, 1f);

    /// <summary>
    /// Sceau placeholder color — cire rouge stylisée (Varn §1.2). Mira
    /// will spec the real asset post-moodboard. Alpha 0.85 keeps it
    /// readable without overpowering the iso ground beneath.
    /// </summary>
    [Export] public Color SceauPlaceholderColor { get; set; } = new Color(0.635f, 0.22f, 0.18f, 0.85f);

    /// <summary>Sceau placeholder side length in world pixels.</summary>
    [Export] public int SceauPlaceholderSizePx { get; set; } = 28;

    private Node2D _fogContainer = null!;
    private TileKnowledgeStore? _knowledgeStore;
    private TileKnowledgeStore.KnowledgeChangedEventHandler? _knowledgeChangedHandler;
    private IFogPaletteSource? _paletteSource;

    private GridDimensions _dimensions = new(0, 0);

    /// <summary>
    /// Per-cell node bag, keyed by <see cref="GridCoord"/>. The renderer
    /// looks up by coord on every signal refresh ; one entry per cell
    /// holds direct references to every animatable sub-node so the
    /// transition handler does not have to traverse the tree.
    /// </summary>
    private readonly Dictionary<GridCoord, CellVisuals> _cells = new();

    /// <summary>
    /// Per-cell active tween. Killed and replaced on every state change
    /// — see class doc "Tween discipline".
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
    /// Idempotent in the sense that calling it twice tears down +
    /// respawns ; in slice 2 the consumer only calls it once per scene
    /// entry.
    /// </summary>
    /// <param name="worldImageSize">World-space dimensions of the master
    /// image the fog covers.</param>
    /// <param name="knowledgeStore">Source of truth for per-cell state.</param>
    /// <param name="placeholderTexture">1×1 white-pixel carton texture
    /// (modulated to <see cref="CartonBaseTint"/> at runtime).</param>
    /// <param name="paletteSource">Quantified palette source. Pass null
    /// to render with the neutral-carton fallback for every cell —
    /// slice 2 fallback path when the bake doesn't exist yet.</param>
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
            new PanVec2(worldImageSize.X, worldImageSize.Y), CellSizePx);

        SpawnCells(placeholderTexture);

        // Subscribe AFTER spawn so the initial state read inside SpawnCells
        // never races against a refresh signal arriving before all cells
        // are wired.
        _knowledgeChangedHandler = OnKnowledgeChanged;
        _knowledgeStore.KnowledgeChanged += _knowledgeChangedHandler;

        var paletteSummary = paletteSource is null
            ? "no palette source (neutral fallback)"
            : $"palette source attached (fallback={(paletteSource is BakedFogPaletteSource b && b.IsFallback)})";
        GD.Print(
            $"[FogTileLayer] configured: {_dimensions.Columns}×{_dimensions.Rows} " +
            $"= {_dimensions.TotalCells} cells, cellSize={CellSizePx}px, " +
            $"yOffset={FogYOffset}, store entries={knowledgeStore.NonDefaultEntryCount}, " +
            $"{paletteSummary}");
    }

    /// <summary>
    /// Hit-test entry point for the slice 2 debug commands (livrable 4).
    /// Translates a world-space cursor position to a grid coord using
    /// the same <see cref="FogTileGridLogic"/> seam every other consumer
    /// uses.
    /// </summary>
    public GridCoord? WorldPositionToCell(Vector2 worldPosition)
    {
        return FogTileGridLogic.WorldPositionToCell(
            new PanVec2(worldPosition.X, worldPosition.Y),
            CellSizePx,
            _dimensions);
    }

    /// <summary>Current grid dimensions. Returns zero-zero before <see cref="Configure"/>.</summary>
    public GridDimensions Dimensions => _dimensions;

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

        var textureSize = placeholderTexture.GetSize();
        if (textureSize.X <= 0 || textureSize.Y <= 0)
        {
            GD.PushWarning(
                "[FogTileLayer] placeholder texture has zero size — cells will not spawn. " +
                "Re-call Configure with a valid texture.");
            return;
        }

        var cartonScale = new Vector2(CellSizePx / textureSize.X, CellSizePx / textureSize.Y);
        var halfCell = CellSizePx / 2f;
        var swatchBandHeight = CellSizePx / 3f;
        var sceauHalfSize = SceauPlaceholderSizePx / 2f;

        foreach (var coord in FogTileGridLogic.EnumerateCells(_dimensions))
        {
            var center = FogTileGridLogic.ComputeCellCenter(coord, CellSizePx);
            var cellPosition = new Vector2(center.X, center.Y + FogYOffset);

            // Cell root — every per-cell sub-node is a child of this so
            // a future "lift the entire cell" effect (slice 3+ flottement
            // animation Varn §4.4) tweens this single position. Slice 2
            // animates the carton sub-sprite directly though — see
            // PliureCarton.
            var cell = new Node2D
            {
                Name = $"Cell_{coord.Col}_{coord.Row}",
                Position = cellPosition,
            };

            // Shadow — z_index -2 relative, behind the swatch and carton.
            // Modulate alpha 0.4 for the soft drop shadow (Varn §4.3
            // 30-40 % range, picked upper end for slice 1 visibility).
            var shadow = new Sprite2D
            {
                Name = "Shadow",
                Texture = placeholderTexture,
                Centered = true,
                Scale = cartonScale,
                Position = ShadowOffset,
                Modulate = new Color(0f, 0f, 0f, 0.4f),
                ZIndex = -2,
            };

            // Palette swatch — 3 horizontal bands (darkest top, brightest
            // bottom) each (cellSize × cellSize/3). Held as a Node2D
            // parent with 3 ColorRect children. Position is centered :
            // the parent sits at (0, 0) relative to the cell root, the
            // ColorRects are positioned in local coords from -halfCell
            // upward.
            var swatch = new Node2D
            {
                Name = "PaletteSwatch",
                Position = Vector2.Zero,
                ZIndex = -1,
            };
            var palette = ResolvePalette(coord);
            for (int band = 0; band < 3; band++)
            {
                var rgb = palette[band];
                var rect = new ColorRect
                {
                    Name = $"Band_{band}",
                    Color = new Color(rgb.R, rgb.G, rgb.B, 1f),
                    Position = new Vector2(-halfCell, -halfCell + band * swatchBandHeight),
                    Size = new Vector2(CellSizePx, swatchBandHeight),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                swatch.AddChild(rect);
            }

            // Carton — main occluder, the one that animates on transitions.
            // Modulate alpha is the per-state value resolved from
            // TileKnowledgeStateHelpers ; rotation/scale/position are
            // mutated by the pliure tween.
            var carton = new Sprite2D
            {
                Name = "Carton",
                Texture = placeholderTexture,
                Centered = true,
                Scale = cartonScale,
                Position = Vector2.Zero,
                Modulate = CartonBaseTint,
                ZIndex = 0,
            };

            // Sceau placeholder — small filled square ColorRect. A real
            // circle texture lands in slice 3+ when Mira specs the
            // sceau de cire. Centered on the cell root, hidden by
            // default ; only shown when state == Scellée.
            var sceau = new ColorRect
            {
                Name = "Sceau",
                Color = SceauPlaceholderColor,
                Position = new Vector2(-sceauHalfSize, -sceauHalfSize),
                Size = new Vector2(SceauPlaceholderSizePx, SceauPlaceholderSizePx),
                MouseFilter = Control.MouseFilterEnum.Ignore,
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

    /// <summary>
    /// Single entry point for "make the cell look like <paramref name="state"/>".
    /// When <paramref name="animate"/> is false (initial spawn), values
    /// are written instantaneously. When true (signal-driven update),
    /// the appropriate tween is scheduled and any in-flight tween on
    /// the same cell is killed first.
    /// </summary>
    private void ApplyVisualState(GridCoord coord, TileKnowledgeState state, bool animate)
    {
        if (!_cells.TryGetValue(coord, out var visuals)) return;

        var targetCartonAlpha = TileKnowledgeStateHelpers.ResolveCartonAlpha(state);
        var showSwatch = TileKnowledgeStateHelpers.ShouldRenderPaletteSwatch(state);
        var showSceau = TileKnowledgeStateHelpers.ShouldRenderSceau(state);

        if (!animate)
        {
            visuals.Carton.Modulate = WithAlpha(CartonBaseTint, targetCartonAlpha);
            visuals.Carton.RotationDegrees = 0f;
            visuals.Carton.Position = Vector2.Zero;
            visuals.Carton.Scale = SteadyScale();
            visuals.Carton.Visible = targetCartonAlpha > 0f;

            visuals.Shadow.Modulate = new Color(0f, 0f, 0f, targetCartonAlpha * 0.4f);
            visuals.Shadow.Visible = targetCartonAlpha > 0f;

            visuals.Swatch.Visible = showSwatch;
            visuals.Sceau.Visible = showSceau;
            return;
        }

        // Kill any prior tween on this cell — tween discipline.
        KillCellTween(coord);

        // The pliure-soulèvement is the only animation with structural
        // changes (rotation, lift, scale) ; everything else is alpha
        // crossfade. Detect the pliure case explicitly.
        var isPliure = state == TileKnowledgeState.Levee
            && visuals.Carton.Modulate.A > 0f;

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

        // Carton rotation : 0 → PliureRotationDegrees over the full
        // PliureDurationSec. Reads as a corner peel.
        tween.TweenProperty(visuals.Carton, "rotation_degrees", PliureRotationDegrees, PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);

        // Carton lift : translate Y by -lift amount (Y grows downward).
        var liftPx = -CellSizePx * PliureLiftFractionOfCell;
        tween.TweenProperty(visuals.Carton, "position", new Vector2(0f, liftPx), PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);

        // Carton fade : alpha hold for the first 600 ms, then drop to
        // 0 over the last 200 ms. Uses two sequential tweens chained
        // by delay : the first tween waits 600 ms doing nothing, the
        // second fades over 200 ms.
        var startAlpha = visuals.Carton.Modulate.A;
        var preFadeDuration = PliureDurationSec - 0.20f;
        if (preFadeDuration > 0f)
        {
            tween.TweenProperty(visuals.Carton, "modulate:a", startAlpha, preFadeDuration);
        }
        tween.TweenProperty(visuals.Carton, "modulate:a", 0f, 0.20f)
            .SetDelay(System.MathF.Max(preFadeDuration, 0f));

        // Shadow rétraction parallèle (alpha 1→0 sur la même 800 ms).
        // Brief slice 2 livrable 3 : "L'ombre portée se rétracte en
        // parallèle".
        tween.TweenProperty(visuals.Shadow, "modulate:a", 0f, PliureDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);

        // Swatch + sceau finalisation : appliqués à la fin du tween.
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
        // Reset any leftover pliure-state on the carton (rotation /
        // position / scale) — for any non-pliure transition the carton
        // sits in steady-state pose. Done instantaneously, alpha is
        // tweened separately below.
        visuals.Carton.RotationDegrees = 0f;
        visuals.Carton.Position = Vector2.Zero;
        visuals.Carton.Scale = SteadyScale();
        visuals.Carton.Visible = true; // force visible during crossfade ; finalization hides if needed

        // Swatch toggle is instantaneous — slice 2 doesn't tween the
        // swatch's appear/disappear (the carton's alpha crossfade
        // carries the perception of palette becoming visible). Sceau
        // is also instantaneous (drop animation Varn §4.5 400 ms is
        // slice 3+ work).
        visuals.Swatch.Visible = showSwatch;
        visuals.Sceau.Visible = showSceau;

        var tween = CreateTween();
        tween.SetParallel(true);

        tween.TweenProperty(visuals.Carton, "modulate:a", targetCartonAlpha, CrossfadeDurationSec)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);

        // Shadow alpha tracks carton alpha (×0.4 dimming).
        tween.TweenProperty(visuals.Shadow, "modulate:a", targetCartonAlpha * 0.4f, CrossfadeDurationSec)
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
        visuals.Carton.Modulate = WithAlpha(CartonBaseTint, alpha);
        visuals.Carton.Visible = alpha > 0f;
        // Reset pliure transform so the next state-change starts from
        // the steady pose (a re-Toggle while invisible should not leave
        // the carton rotated by 25° if it later becomes visible again).
        visuals.Carton.RotationDegrees = 0f;
        visuals.Carton.Position = Vector2.Zero;
        visuals.Carton.Scale = SteadyScale();

        visuals.Shadow.Modulate = new Color(0f, 0f, 0f, alpha * 0.4f);
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

    private Vector2 SteadyScale()
    {
        // Recompute on demand rather than caching — keeps the field count
        // down. Texture is the placeholder, fixed at 1×1, so this is
        // always (CellSizePx, CellSizePx). If a future refactor swaps
        // the texture for a real asset, this method centralises the
        // recompute.
        return new Vector2(CellSizePx, CellSizePx);
    }

    private static Color WithAlpha(Color baseColor, float alpha)
    {
        return new Color(baseColor.R, baseColor.G, baseColor.B, alpha);
    }

    /// <summary>
    /// Per-cell node references. Held so the transition handler can
    /// mutate sub-nodes without traversing the scene tree on every
    /// signal emission.
    /// </summary>
    private readonly record struct CellVisuals(
        Node2D Cell,
        Sprite2D Shadow,
        Node2D Swatch,
        Sprite2D Carton,
        ColorRect Sceau);
}
