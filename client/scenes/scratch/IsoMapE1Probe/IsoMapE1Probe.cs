using Godot;

namespace Wayfinders.Client.Scenes.Scratch.IsoMapE1Probe;

/// <summary>
/// POC e1 A — vue monde Wayfinders, étape 0bis : <b>UNE SEULE TUILE</b>
/// centrée dans le viewport, zoom 2×, pour inspection visuelle du rendu
/// d'asset Mira v5 (<c>wf_e1_tile_neutral.png</c>, 256×130 RGBA).
///
/// <para>
/// <b>Mise à jour 2026-05-12 — passage v4 → v5 (reset iso 2:1 classique).</b>
/// v4 livrait 256×175 (face-top losange 256×105 "haute" + tranche 256×70), un
/// ratio non-standard qui forçait des strides bâtards (h=105) incompatibles avec
/// le pavage iso 2:1 classique des tactiques 2D. v5 revient à la convention
/// iso 2:1 stricte : face-top losange 256×128 (sommets TOP=(128,0),
/// RIGHT=(256,64), BOT_FACE=(128,128), LEFT=(0,64)) + tranche minimale 2 px
/// (y=128→130). Conséquences :
/// <list type="bullet">
///   <item><b>iso_w_stride = 128, iso_h_stride = 64.</b> Formule canonique :
///         <c>screen_x = (gx - gy) * 128 + ox</c>,
///         <c>screen_y = (gx + gy) * 64 + oy</c>. Demi-largeur face-top = 128 ;
///         demi-hauteur face-top = 64. Deux losanges adjacents partagent une
///         arête pixel-parfaite — pavage diamond classique sans recouvrement
///         ni trou.</item>
///   <item><b>Sprite2D Offset.Y = -1 dans les deux modes.</b> Avec
///         <c>Centered=true</c>, le pivot du Sprite2D est le centre géométrique
///         de la texture (128, 65). On veut que le pivot tombe sur le
///         <i>centre du losange face-top</i>, soit (128, 64). Donc
///         <c>Offset.Y = 64 - 65 = -1</c> (remonter la texture de 1 px pour
///         aligner le centre-losange sur la Position). Conséquence visuelle
///         attendue : la tranche de 2 px pend exactement sous le sol de la
///         cellule (y=128→130 en texture → 64→66 px sous le centre-losange à
///         l'écran).</item>
///   <item><b>Sprite2D Offset.Y single-tile = -1 aussi.</b> Plus de cas
///         spécial : un seul invariant d'alignement, valable mono et multi.
///         Ça simplifie le mental model et garantit qu'aucun "saut visuel"
///         n'apparaîtra au passage single→multi.</item>
///   <item><b>Preflight check 4/6 attend désormais 256×130</b> (vs 256×175
///         précédemment).</item>
///   <item><b>Z-sort multi-tuiles par (gx + gy) ascendant.</b> Implicite avec
///         Y-sort Godot dès que la Position.Y croît avec (gx + gy) — c'est le
///         cas par construction de la formule. Aucun tri manuel nécessaire.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Pourquoi le reset v4 → v5 le 2026-05-12.</b> v4 (face-top 256×105) tentait
/// de préserver la perspective MJ d'origine, mais le ratio non-iso-2:1 cassait
/// le pavage diamond standard : les strides 128/105 ne s'inscrivent pas dans
/// un grid entier, donc les arêtes ne se rejoignent pas pixel-parfait, et tout
/// algo de path/hover/click écrit en coords cellule (gx, gy) avec arithmétique
/// entière dérive d'un demi-pixel par cellule. v5 réaligne sur l'iso 2:1 strict
/// (h_stride entier, demi-stride entier = 64, deux fois le stride entier = 128),
/// au prix d'une face-top légèrement plus "carrée" qu'en source MJ. Décision
/// engine-shaped sur décision pixel-pure.
/// </para>
///
/// <para>
/// <b>Désactivations vs build multi-tuiles précédent.</b>
/// <list type="bullet">
///   <item>Boucle 24×24 court-circuitée par <c>DisplaySingleTileOnly = true</c>.
///         Un seul Sprite2D est instancié, à la position (0, 0) locale.</item>
///   <item>WorldRoot.Position n'est plus recalculé (pas de grille à recentrer) :
///         on laisse à (0, 0) directement.</item>
///   <item>Y-sort reste configuré dans la scène (.tscn) mais est inoffensif
///         sur 1 seul sprite — pas de tri à faire.</item>
///   <item>ShadowRoot3D reste dans la scène (Node3D vide) mais on n'instancie
///         aucun Node3D enfant — squelette 3D désactivé pour ce POC visuel.</item>
///   <item>Sprite2D.Offset = (0, -1) : on veut voir la texture entière
///         centrée pixel-parfait avec le centre du losange face-top aligné sur
///         la Position du sprite. La tranche de 2 px pend visuellement sous
///         le losange.</item>
///   <item>Zoom 2× au boot (au lieu de 0.31×) : la texture 256×130 occupe
///         512×260 px à l'écran, soit ~27% de la largeur d'un viewport
///         1920×1080 et ~24% de la hauteur. Confortable pour inspecter le
///         losange + la tranche + les bords alpha.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Critère d'acceptance visuel F6.</b>
/// <list type="bullet">
///   <item>Une seule tuile diamant visible, centrée dans le viewport, sur fond
///         magenta (#FF00FF).</item>
///   <item>Face-top losange + tranche bien différenciées (la tranche de 2 px
///         pend sous le losange dans la PNG, donc visible en bas de la tuile
///         à l'écran — à zoom 2× elle fait 4 px de haut).</item>
///   <item>Proportions v5 iso 2:1 strict : losange visiblement <i>plus écrasé</i>
///         que v4 (ratio largeur/hauteur face-top = 256/128 = 2, contre 256/105
///         ≈ 2.44 en v4). C'est la "vraie" silhouette iso classique XCOM /
///         Battle Brothers.</item>
///   <item>Bords nets, alpha propre (pas de halo, pas d'artefact, pas de
///         frange anti-aliasée parasite contre le magenta).</item>
///   <item>Aspect pencil/charcoal Mira v5 préservé (grain visible, pas écrasé
///         par le filtrage).</item>
///   <item>Preflight canary 6/6 OK dans l'output Godot.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Filtrage texture.</b> On garde <c>TextureFilterEnum.Nearest</c> pour
/// préserver le grain pencil/charcoal pixel-parfait au zoom 2×. Tout filtrage
/// linéaire baverait le trait et ferait perdre l'aspect MJ-rendered.
/// </para>
/// </summary>
public partial class IsoMapE1Probe : Node2D
{
    // --- Mode locked 2026-05-12 ---
    private static readonly bool DisplaySingleTileOnly = false;

    // --- Paramètres grille (gardés pour réactivation future) ---
    private const int GridSize = 24;
    private const int CenterIndex = 12;

    // --- Texture Mira v5 : 256×130 (face-top losange 256×128 + tranche 256×2) ---
    // Sommets face-top : TOP=(128,0), RIGHT=(256,64), BOT_FACE=(128,128), LEFT=(0,64).
    private const int TileTextureWidthPx = 256;
    private const int TileFaceHeightPx = 128;
    private const int TileSliceHeightPx = 2;
    private const int TileTextureHeightPx = TileFaceHeightPx + TileSliceHeightPx; // 130

    // --- Stride iso 2D screen (iso 2:1 classique) ---
    // Formule Mira v5 : screen_x = (gx - gy) * 128 + ox ; screen_y = (gx + gy) * 64 + oy
    // iso_w_stride = 128 (demi-largeur face-top, inchangé v3/v4/v5)
    // iso_h_stride = 64  (demi-hauteur face-top, CHANGE v4→v5 : était 105)
    private const float IsoWStride = 128f;
    private const float IsoHStride = 64f;

    // --- Sprite2D Offset.Y : aligner centre-losange face-top sur Position ---
    // Avec Centered=true, le pivot du Sprite2D est le centre géométrique de la
    // texture (128, 65). On veut que le pivot tombe sur le centre du losange
    // face-top, soit pixel (128, 64). Donc Offset.Y = 64 - 65 = -1.
    //   Centre texture vertical = 130 / 2  = 65
    //   Centre face-top vertical = 128 / 2 = 64
    //   Offset.Y = 64 - 65 = -1 (remonter la texture de 1 px)
    // Identique en mode single et multi : un seul invariant d'alignement,
    // pas de saut visuel au passage single→multi. La tranche de 2 px pend
    // visuellement sous le sol de la cellule (y=128 → 64 px sous centre-losange ;
    // y=130 → 66 px sous centre-losange, soit 2 px sous le sol).
    private const float SpriteOffsetY = TileFaceHeightPx / 2f - TileTextureHeightPx / 2f; // -1

    // --- Caméra : zoom inspection ---
    // Zoom 2× : 256×130 texture → 512×260 px à l'écran. Confortable sur
    // 1920×1080, laisse de la marge magenta tout autour pour inspecter les
    // bords alpha.
    private const float ZoomInspect = 0.31f;
    private const float ZoomMin = 0.31f;
    private const float ZoomMax = 2.0f;
    private const float ZoomWheelStep = 1.1f;

    // --- Texture sourcing ---
    private static readonly bool UseProceduralPlaceholder = false;
    private const string TilePrimaryPath = "res://assets/wayfinders_visual_assets/e1/wf_e1_tile_neutral.png";

    // --- Background expected color ---
    private static readonly Color ExpectedClearColor = new Color(1f, 0f, 1f, 1f);

    // --- Visuel placeholder procédural (gardé pour A/B debug) ---
    private static readonly Color PlaceholderInterior = new Color(0.92f, 0.85f, 0.70f, 1f);
    private static readonly Color PlaceholderBorder = new Color(0.55f, 0.35f, 0.18f, 1f);
    private const int PlaceholderBorderPx = 2;

    // --- État runtime ---
    private Camera2D _camera = null!;
    private Node2D _worldRoot = null!;
    private Node2D _tileGrid = null!;
    private Node3D _shadowRoot3D = null!;
    private float _currentZoom = ZoomInspect;
    private Texture2D _tileTexture = null!;

    public override void _Ready()
    {
        GD.Print("[PROBE IsoMapE1Probe] scene started — SINGLE TILE INSPECTION MODE (2026-05-12 v5 256×130 iso 2:1 classique)");

        _camera = GetNode<Camera2D>("WorldCamera");
        _worldRoot = GetNode<Node2D>("WorldRoot");
        _tileGrid = GetNode<Node2D>("WorldRoot/TileGrid");
        _shadowRoot3D = GetNode<Node3D>("ShadowRoot3D");

        // 1. Charger la texture primaire.
        _tileTexture = LoadPrimaryTile();

        // 2. WorldRoot reste à l'origine — pas de grille à recentrer.
        _worldRoot.Position = Vector2.Zero;

        // 3. Instancier UN SEUL sprite (ou la grille complète si on rebranche).
        if (DisplaySingleTileOnly)
        {
            SpawnSingleTile();
        }
        else
        {
            SpawnFullGrid();
        }

        // 4. Caméra centrée sur l'origine, zoom inspection.
        _camera.Position = Vector2.Zero;
        _camera.Zoom = new Vector2(_currentZoom, _currentZoom);
        _camera.MakeCurrent();

        // 5. Preflight canary simplifié.
        RunPreflight();
    }

    private void SpawnSingleTile()
    {
        var sprite = new Sprite2D
        {
            Name = "Tile_single",
            Texture = _tileTexture,
            Centered = true,
            Offset = new Vector2(0f, SpriteOffsetY),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Position = Vector2.Zero,
        };
        _tileGrid.AddChild(sprite);
    }

    private void SpawnFullGrid()
    {
        // Réservé : retour au pavage 24×24 quand l'inspection visuelle aura
        // validé la tuile v5. Stride iso (w=128, h=64) iso 2:1 classique.
        float gridMinScreenY = 0f;
        float gridMaxScreenY = (GridSize - 1) * 2f * IsoHStride;
        float gridCenterScreenY = (gridMinScreenY + gridMaxScreenY) * 0.5f;
        _worldRoot.Position = new Vector2(0f, -gridCenterScreenY);

        for (int gx = 0; gx < GridSize; gx++)
        {
            for (int gy = 0; gy < GridSize; gy++)
            {
                float screenX = (gx - gy) * IsoWStride;
                float screenY = (gx + gy) * IsoHStride;

                var sprite = new Sprite2D
                {
                    Name = $"Tile_{gx}_{gy}",
                    Texture = _tileTexture,
                    Centered = true,
                    Offset = new Vector2(0f, SpriteOffsetY),
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    Position = new Vector2(screenX, screenY),
                };
                _tileGrid.AddChild(sprite);
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            float newZoom = _currentZoom;
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                newZoom = _currentZoom * ZoomWheelStep;
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                newZoom = _currentZoom / ZoomWheelStep;
            }
            else
            {
                return;
            }

            newZoom = Mathf.Clamp(newZoom, ZoomMin, ZoomMax);
            if (!Mathf.IsEqualApprox(newZoom, _currentZoom))
            {
                _currentZoom = newZoom;
                _camera.Zoom = new Vector2(_currentZoom, _currentZoom);
            }
        }
    }

    private Texture2D LoadPrimaryTile()
    {
        if (UseProceduralPlaceholder)
        {
            GD.Print("[PROBE IsoMapE1Probe] LoadPrimaryTile: UseProceduralPlaceholder=true, using procedural texture (A/B debug)");
            return BuildPlaceholderTexture(TileTextureWidthPx, TileTextureHeightPx);
        }

        var tex = GD.Load<Texture2D>(TilePrimaryPath);
        if (tex is null)
        {
            GD.PushError($"[PROBE IsoMapE1Probe] FAILED to load primary tile: {TilePrimaryPath}");
        }
        return tex!;
    }

    private static ImageTexture BuildPlaceholderTexture(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float halfW = w * 0.5f;
        float halfH = h * 0.5f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = Mathf.Abs(x - halfW) / halfW;
                float ny = Mathf.Abs(y - halfH) / halfH;
                float d = nx + ny;
                if (d > 1.0f)
                {
                    continue;
                }

                float borderNorm = (float)PlaceholderBorderPx / Mathf.Min(halfW, halfH);
                if (d > 1.0f - borderNorm)
                {
                    img.SetPixel(x, y, PlaceholderBorder);
                }
                else
                {
                    img.SetPixel(x, y, PlaceholderInterior);
                }
            }
        }

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// Preflight canary single-tile : 6 lignes <c>[PROBE IsoMapE1Probe]</c>.
    /// On vérifie uniquement les invariants pertinents pour 1 tuile centrée,
    /// adaptés aux dimensions v5 (256×130) et stride iso 2:1 classique (w=128, h=64).
    /// </summary>
    private void RunPreflight()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;

        // 1. Exactly 1 sprite under TileGrid.
        int tileCount = _tileGrid.GetChildCount();
        bool countOk = DisplaySingleTileOnly ? tileCount == 1 : tileCount == GridSize * GridSize;
        GD.Print($"[PROBE IsoMapE1Probe] 1/6 TileGrid.children = {tileCount} (expected {(DisplaySingleTileOnly ? 1 : GridSize * GridSize)}) -- {(countOk ? "OK" : "FAIL")}");

        // 2. Sprite position = viewport center (Sprite at (0,0) in world,
        //    Camera2D at (0,0) → sprite tombe au centre écran).
        var theTile = _tileGrid.GetChild<Sprite2D>(0);
        Vector2 spritePos = theTile?.Position ?? Vector2.One;
        bool posOk = theTile is not null
            && Mathf.IsEqualApprox(spritePos.X, 0f)
            && Mathf.IsEqualApprox(spritePos.Y, 0f);
        GD.Print($"[PROBE IsoMapE1Probe] 2/6 Tile.Position = {spritePos.ToString("F2")} (expected (0, 0) → centre viewport via Camera2D@origin) -- {(posOk ? "OK" : "FAIL")}");

        // 3. Camera2D current + zoom inspection.
        bool camOk = _camera.IsCurrent()
            && Mathf.IsEqualApprox(_camera.Zoom.X, ZoomInspect)
            && Mathf.IsEqualApprox(_camera.Zoom.Y, ZoomInspect)
            && _camera.Position.IsEqualApprox(Vector2.Zero);
        GD.Print($"[PROBE IsoMapE1Probe] 3/6 Camera2D current={_camera.IsCurrent()} zoom={_camera.Zoom.ToString("F2")} pos={_camera.Position.ToString("F2")} (expected current=true zoom=({ZoomInspect:F2},{ZoomInspect:F2}) pos=(0,0)) -- {(camOk ? "OK" : "FAIL")}");

        // 4. Texture dimensions v5 (256×130) + source type.
        var tex = theTile?.Texture;
        int texW = tex?.GetWidth() ?? 0;
        int texH = tex?.GetHeight() ?? 0;
        string texTypeName = tex?.GetType().Name ?? "null";
        bool texDimsOk = texW == TileTextureWidthPx && texH == TileTextureHeightPx;
        bool texSourceOk = UseProceduralPlaceholder
            ? texTypeName == "ImageTexture"
            : texTypeName == "CompressedTexture2D";
        bool texOk = texDimsOk && texSourceOk;
        string expectedType = UseProceduralPlaceholder ? "ImageTexture (procedural)" : "CompressedTexture2D (real PNG RGBA v5)";
        GD.Print($"[PROBE IsoMapE1Probe] 4/6 Tile.Texture = {texW}x{texH} type={texTypeName} (expected {TileTextureWidthPx}x{TileTextureHeightPx} v5 {expectedType}) -- {(texOk ? "OK" : "FAIL")}");

        // 5. TextureFilter Nearest (grain pencil préservé au zoom 2×) +
        //    Sprite2D Offset cohérent (même invariant single/multi).
        float expectedOffsetY = SpriteOffsetY;
        float actualOffsetY = theTile?.Offset.Y ?? float.NaN;
        bool filterOk = theTile is not null
            && theTile.TextureFilter == CanvasItem.TextureFilterEnum.Nearest;
        bool offsetOk = theTile is not null
            && Mathf.IsEqualApprox(actualOffsetY, expectedOffsetY);
        bool filterOffsetOk = filterOk && offsetOk;
        GD.Print($"[PROBE IsoMapE1Probe] 5/6 Tile.TextureFilter={(theTile?.TextureFilter.ToString() ?? "null")} (expected Nearest) ; Tile.Offset.Y={actualOffsetY:F2} (expected {expectedOffsetY:F2} = -1 pour aligner centre-losange (128,64) sur Position) -- {(filterOffsetOk ? "OK" : "FAIL")}");

        // 6. Background magenta + ShadowRoot3D vide + stride iso v5 sanity.
        var bgLayer = GetNodeOrNull<CanvasLayer>("BackgroundLayer");
        var bgRect = bgLayer?.GetNodeOrNull<ColorRect>("BackgroundRect");
        Color bgColor = bgRect?.Color ?? Colors.Black;
        bool bgOk = bgRect is not null
            && Mathf.IsEqualApprox(bgColor.R, ExpectedClearColor.R)
            && Mathf.IsEqualApprox(bgColor.G, ExpectedClearColor.G)
            && Mathf.IsEqualApprox(bgColor.B, ExpectedClearColor.B);
        int shadowCount = _shadowRoot3D.GetChildCount();
        bool shadowEmpty = shadowCount == 0;
        // Stride sanity : v5 attend IsoWStride=128 + IsoHStride=64 (iso 2:1 classique).
        bool strideOk = Mathf.IsEqualApprox(IsoWStride, 128f) && Mathf.IsEqualApprox(IsoHStride, 64f);
        bool bgShadowOk = bgOk && shadowEmpty && strideOk;
        GD.Print($"[PROBE IsoMapE1Probe] 6/6 BG=({bgColor.R:F2},{bgColor.G:F2},{bgColor.B:F2}) expected magenta ; ShadowRoot3D.children={shadowCount} expected 0 ; iso stride=(w={IsoWStride:F0}, h={IsoHStride:F0}) expected (128, 64) v5 -- {(bgShadowOk ? "OK" : "FAIL")}");

        // Info diag.
        GD.Print($"[PROBE IsoMapE1Probe] info viewport={viewportSize.X:F0}x{viewportSize.Y:F0} ; zoom={_currentZoom:F2}× (wheel up/down, bounds {ZoomMin:F2}×—{ZoomMax:F2}×) ; mode=SINGLE_TILE ; v5 dims=256×130 (face-top 256×128 iso 2:1 + tranche 256×2) ; texture path={TilePrimaryPath}");
    }
}
