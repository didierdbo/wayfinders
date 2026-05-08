using System.Collections.Generic;
using Godot;
using Wayfinders.Client.Scripts.Screens;
using Wayfinders.Client.Services;

namespace Wayfinders.Client.Components;

/// <summary>
/// Sprite2D-per-cell fog renderer (slice 1 / livrable 2 — M3 / L1 World
/// fondations). Sits as a child of <see cref="MapPan2DComponent"/>'s
/// world tree, above the world map sprite and the POI container. One
/// <see cref="Sprite2D"/> per grid cell renders the carton placeholder ;
/// a sibling shadow Sprite2D underneath emulates the Varn §4.3 ombre
/// portée douce. Visibility per cell is driven by
/// <see cref="TileKnowledgeStore"/> via the
/// <c>KnowledgeChanged</c> signal.
///
/// <para>
/// <b>Architectural decision : Sprite2D-per-cell, not TileMapLayer.</b>
/// Documented in Rune's 2026-05-08 audit (mémoire 'Phase 9 audit',
/// verdict MVP-friendly avec discipline). TileMapLayer is the engine-
/// natif default for grid rendering, but it bakes one tile per
/// TileSet entry into a single batched draw — there's no per-tile node
/// to tween, fade individually, or anchor an animation off of. Slice 2
/// needs the pliure-soulèvement gesture-clé per tile (Varn §4.5 lock)
/// which requires per-tile transforms : that's a Sprite2D-per-cell
/// affordance, not a TileMapLayer one. The MVP grid (30×17 = 510
/// cells) is small enough that the per-tile draw call cost is
/// dwarfed by the rest of the E2 frame budget — the JFDI scaling
/// ceiling is well above what L1 World will ever need (Varn §5.1
/// caps L1 at 40-80 tuiles MVP, so the 510 placeholder is already a
/// 6-10× over-allocation).
/// </para>
///
/// <para>
/// <b>YSort trap (audit §6).</b> Even though the fog tile sits 'above'
/// the iso ground in the layer-stack mental model, this layer is not
/// ysort-sorted because the world tree is not ysort-sorted either at
/// E2 — POIs use static z_index ordering instead. The fog layer hangs
/// off the same parent as POIs but with a higher z_index, so the
/// stacking is deterministic and immune to the YSort + offset Y trap
/// (audit §6 : if you ysort-enable a parent and add a Y offset to a
/// child, the child can sort in the wrong order). The slight Y offset
/// for the 'flottement' (-8 px slice 1 placeholder) is applied to the
/// per-tile Sprite2D position, not via a YSort key, so it's purely
/// visual.
/// </para>
///
/// <para>
/// <b>Stacking spec (slice 1).</b>
/// <list type="bullet">
///   <item>z_index 0 -- world map sprite (existing).</item>
///   <item>z_index 5 -- POI container (existing).</item>
///   <item>z_index 9 -- shadow sprites (this layer, child of FogContainer).</item>
///   <item>z_index 10 -- fog sprites (this layer, parent FogContainer node).</item>
/// </list>
/// The whole layer is a single CanvasItem subtree ; slice 2's bulk
/// transitions (zoom-driven layer cross-fade, Varn §4.5 pliure)
/// modulate the parent <see cref="FogContainer"/> in one tween call.
/// </para>
///
/// <para>
/// <b>Lifecycle.</b>
/// <see cref="Configure"/> is called by the consumer (E2WorldMap or a
/// future scene) once the world image is loaded and its size is known.
/// Configure spawns one Sprite2D pair (carton + shadow) per grid cell,
/// wires them under <see cref="FogContainer"/>, and refreshes their
/// initial visibility from <see cref="TileKnowledgeStore"/>. The
/// SignalName.KnowledgeChanged subscription is captured so
/// <see cref="_ExitTree"/> disconnects with the exact handler reference
/// (Risk #1, signal-leak trap, repeated from P7-J3 / P8.2).
/// </para>
/// </summary>
public partial class FogTileLayer : Node2D
{
    /// <summary>
    /// Slice 1 placeholder cell size in world pixels. Chosen so the
    /// E2.1 master 3840×2160 produces 30×17 = 510 cells — dense enough
    /// to validate the architecture, sparse enough that a per-tile
    /// debug toggle reads as a single visible cell change. Final cell
    /// size is design territory (Varn §5.1 organique L1) and lands in
    /// slice 2+ once Mira's master and the biome atlas ship.
    /// </summary>
    [Export] public int CellSizePx { get; set; } = 128;

    /// <summary>
    /// Slice 1 placeholder vertical offset for the 'tuile flottante'
    /// effect (Varn §4.1). Negative because Y grows downward in
    /// world coords. The full Varn spec calls for ±0.04 hauteur de
    /// tuile désynchronisé sinusoïdal (§4.4 animation flottement) —
    /// slice 1 only ships the static offset, the breathing animation
    /// arrives in slice 2 alongside the transition animations.
    /// </summary>
    [Export] public float FogYOffset { get; set; } = -8f;

    /// <summary>
    /// Slice 1 placeholder shadow offset (Varn §4.3). Statique, modulate
    /// alpha 0.4, blur-by-margin (the placeholder texture is solid so
    /// no actual gaussian blur — a softer texture in slice 2 will
    /// supply the diffusion).
    /// </summary>
    [Export] public Vector2 ShadowOffset { get; set; } = new Vector2(6f, 10f);

    /// <summary>
    /// Z-index of the parent FogContainer. The world map sprite uses
    /// the default 0, POIs are at 5 (set in spawn), this layer at 10
    /// keeps it above everything (Varn §4.6 layer stack). The shadows
    /// inside the container use a child relative offset.
    /// </summary>
    [Export] public int FogZIndex { get; set; } = 10;

    private Node2D _fogContainer = null!;
    private TileKnowledgeStore? _knowledgeStore;
    private TileKnowledgeStore.KnowledgeChangedEventHandler? _knowledgeChangedHandler;

    private GridDimensions _dimensions = new(0, 0);

    /// <summary>
    /// Per-cell fog Sprite2D index, keyed by <see cref="GridCoord"/>.
    /// The renderer looks up by coord on every signal refresh ; the
    /// shadow Sprite2D is reachable via the fog sprite's child list
    /// (kept as the carton's only child Node2D so the lookup costs
    /// one indirection instead of a second dictionary).
    /// </summary>
    private readonly Dictionary<GridCoord, Sprite2D> _fogSprites = new();

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
    /// One-shot configuration after the consumer knows the world size
    /// and has a knowledge store reference. Idempotent in the sense
    /// that calling it twice tears down + respawns ; in slice 1 the
    /// consumer only calls it once per scene entry.
    /// </summary>
    /// <param name="worldImageSize">World-space dimensions of the
    /// master image the fog covers (typically the same value the
    /// camera limits use).</param>
    /// <param name="knowledgeStore">Source of truth for per-cell
    /// state ; the renderer subscribes to its signal.</param>
    /// <param name="placeholderTexture">Carton texture for the opaque
    /// fog tile. Slice 1 ships a 1-pixel solid white tinted via
    /// <c>Modulate</c> ; slice 2 receives the parchemin asset Mira
    /// produces post-moodboard sign-off.</param>
    public void Configure(
        Vector2 worldImageSize,
        TileKnowledgeStore knowledgeStore,
        Texture2D placeholderTexture)
    {
        DespawnAll();

        _knowledgeStore = knowledgeStore;
        _dimensions = FogTileGridLogic.ComputeGridSize(
            new PanVec2(worldImageSize.X, worldImageSize.Y), CellSizePx);

        SpawnFogSprites(placeholderTexture);

        // Subscribe AFTER spawn so the initial state is read from the
        // store inside SpawnFogSprites without racing a refresh signal
        // that arrives before all sprites are wired.
        _knowledgeChangedHandler = OnKnowledgeChanged;
        _knowledgeStore.KnowledgeChanged += _knowledgeChangedHandler;

        GD.Print(
            $"[FogTileLayer] configured: {_dimensions.Columns}×{_dimensions.Rows} " +
            $"= {_dimensions.TotalCells} cells, cellSize={CellSizePx}px, " +
            $"yOffset={FogYOffset}, store entries={knowledgeStore.NonDefaultEntryCount}");
    }

    /// <summary>
    /// Hit-test entry point for the slice 1 debug toggle (livrable 3).
    /// Translates a world-space cursor position to a grid coord using
    /// the same <see cref="FogTileGridLogic"/> seam every other consumer
    /// uses — guarantees the debug command and the render layer agree
    /// on cell boundaries by construction.
    /// </summary>
    public GridCoord? WorldPositionToCell(Vector2 worldPosition)
    {
        return FogTileGridLogic.WorldPositionToCell(
            new PanVec2(worldPosition.X, worldPosition.Y),
            CellSizePx,
            _dimensions);
    }

    /// <summary>
    /// Current grid dimensions (for diagnostics + tests). Returns a
    /// zero-zero sentinel if <see cref="Configure"/> has not been
    /// called — caller should not try to enumerate cells until after
    /// configure.
    /// </summary>
    public GridDimensions Dimensions => _dimensions;

    public override void _ExitTree()
    {
        DespawnAll();

        // Disconnect with the EXACT reference captured at wire time.
        // Method-group disconnect would not match because the assignment
        // through the EventHandler delegate caches a wrapped target.
        // Same Risk #1 trap as P7-J3 POI signals.
        if (_knowledgeStore is not null && _knowledgeChangedHandler is not null)
        {
            _knowledgeStore.KnowledgeChanged -= _knowledgeChangedHandler;
        }
        _knowledgeStore = null;
        _knowledgeChangedHandler = null;
    }

    private void SpawnFogSprites(Texture2D placeholderTexture)
    {
        if (_knowledgeStore is null) return;

        // Scale a 1×1 placeholder texture up to cell size. When slice 2
        // ships the real carton parchment asset Mira produces, the
        // texture is whatever resolution the artist authored ; this
        // scaling logic is unchanged because sprite Scale is set
        // proportionally from the texture's actual pixel size.
        var textureSize = placeholderTexture.GetSize();
        if (textureSize.X <= 0 || textureSize.Y <= 0)
        {
            GD.PushWarning(
                "[FogTileLayer] placeholder texture has zero size — falling back " +
                "to a 1×1 white pixel proxy. Spawn will continue but tiles will " +
                "be invisible until Configure is re-called with a real texture.");
            return;
        }

        var scaleX = CellSizePx / textureSize.X;
        var scaleY = CellSizePx / textureSize.Y;
        var spriteScale = new Vector2(scaleX, scaleY);

        foreach (var coord in FogTileGridLogic.EnumerateCells(_dimensions))
        {
            var center = FogTileGridLogic.ComputeCellCenter(coord, CellSizePx);
            var fogPosition = new Vector2(center.X, center.Y + FogYOffset);

            // Shadow sits BELOW the carton (z_index 9 vs 10), offset
            // toward the bottom-right to match the Varn §4.3 lumière
            // ambiante. Modulate alpha 0.4 = subtle (Varn §4.3 30-40 %
            // sweet spot, picked the upper end for slice 1 visibility).
            var shadow = new Sprite2D
            {
                Name = $"Shadow_{coord.Col}_{coord.Row}",
                Texture = placeholderTexture,
                Centered = true,
                Scale = spriteScale,
                Position = fogPosition + ShadowOffset,
                Modulate = new Color(0f, 0f, 0f, 0.4f),
                ZIndex = -1,  // relative to FogContainer's z_index 10 -> effective 9
            };

            var fog = new Sprite2D
            {
                Name = $"Fog_{coord.Col}_{coord.Row}",
                Texture = placeholderTexture,
                Centered = true,
                Scale = spriteScale,
                Position = fogPosition,
                Modulate = new Color(0.91f, 0.85f, 0.72f, 0.95f),  // beige-écru carton Varn §4.2
                ZIndex = 0,  // relative to FogContainer's z_index 10
            };
            fog.AddChild(shadow);
            _fogContainer.AddChild(fog);

            _fogSprites[coord] = fog;

            // Initial visibility from the store. Cells not in the dict
            // return Inconnue (default) which is opaque.
            ApplyVisibility(coord, _knowledgeStore.GetState(coord));
        }
    }

    private void DespawnAll()
    {
        foreach (var (_, sprite) in _fogSprites)
        {
            sprite?.QueueFree();
        }
        _fogSprites.Clear();
    }

    private void OnKnowledgeChanged(int col, int row, int newState)
    {
        var coord = new GridCoord(col, row);
        ApplyVisibility(coord, (TileKnowledgeState)newState);
    }

    private void ApplyVisibility(GridCoord coord, TileKnowledgeState state)
    {
        if (!_fogSprites.TryGetValue(coord, out var fog) || fog is null) return;
        // Slice 1 binary: hidden when Levée/Scellée, opaque otherwise.
        // Slice 2 will introduce graduated alpha + silhouette overlay
        // for Pressentie / Esquissée — the seam point is here.
        fog.Visible = TileKnowledgeStateHelpers.ShouldRenderOpaque(state);
    }
}
