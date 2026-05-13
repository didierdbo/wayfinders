using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wayfinders.Client.Utils;

namespace Wayfinders.Client.Scenes.Scratch.IsoMapE1Probe;

/// <summary>
/// POC e1 — vue monde Wayfinders.
///
/// <para>
/// <b>Asset loading convention (locked 2026-05-13).</b> Tous les assets
/// visuels Wayfinders chargés par ce probe — et plus généralement par tout
/// nouveau code client C# — passent désormais par
/// <see cref="AssetLoader.LoadAssetOrPlaceholder"/>. Le code demande l'asset
/// par son <i>nom final</i> (ex. <c>wf_e1_halfgate_poi.png</c>) ; la fonction
/// charge le PNG final s'il existe, sinon le PNG <c>_PLACEHOLDER</c>
/// suffixé livré par Mira au même endroit, sinon log un error. Aucune
/// génération procédurale en C# n'est plus tolérée dans la code path de
/// production — les placeholders procéduraux vivent côté Mira
/// (<c>scripts/placeholder_generator.py</c>). Voir spec
/// <c>Owner's Inbox/mira-placeholder-framework-spec-2026-05-13-FR.md</c>.
/// </para>
///
/// <para>
/// <b>État 2026-05-13 (e1 D — hover + click POI Halfgate, post-bugfix
/// 2026-05-13-pm).</b> Tout l'état précédent est conservé (grille 24×24,
/// flip 3D simulé, fade-in POI). Ajouts :
/// <list type="bullet">
///   <item><b>Hover POI</b> : détection manuelle via <c>_Input</c>. La
///         position souris écran est convertie en monde via
///         <c>GetGlobalMousePosition()</c>, comparée au <c>GetRect()</c>
///         du sprite POI (transformé en global). Au hover, après un delay
///         de 250ms (anti-clignotement, spec Varn §6), un tooltip
///         parchemin diégétique apparaît au curseur avec fade-in 150ms +
///         scale 0.95→1.0.</item>
///   <item><b>Tooltip</b> : <c>CanvasLayer</c> séparé (z=100, hors grille
///         y-sort), composé d'un <c>TextureRect</c> parchemin, d'un
///         <c>TextureRect</c> sceau de cire, d'un <c>Label</c> titre gras
///         ("Halfgate") et de deux Labels lore. Position : curseur +
///         offset (24,24), basculée en quadrant opposé si débordement
///         viewport, jamais clampée hors visibilité.</item>
///   <item><b>Click POI</b> : même hit-test manuel que le hover. Le
///         tooltip disparaît, la caméra zoom vers 2.5× sur 1.0s
///         (ease_in_quad), puis un <c>ColorRect</c> noir fade in 400ms
///         (crossfade), puis <c>ChangeSceneToFile</c> vers
///         <c>res://scenes/scratch/E2Stub/E2Stub.tscn</c>.</item>
///   <item><b>Activation différée</b> : hover + click sont désactivés
///         (<c>_poiHoverEnabled=false</c>) jusqu'à la fin de la
///         cinématique (<c>_poiFadeInCompleted=true</c>, t≈5.55s). Évite
///         que le joueur clique sur un POI invisible pendant le flip.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Pourquoi <c>_Input</c> manuel et pas <c>Area2D</c>.</b>
/// La stratégie initiale e1 D utilisait un <c>Area2D</c> +
/// <c>CollisionShape2D</c> avec les signaux <c>mouse_entered</c> /
/// <c>mouse_exited</c> / <c>input_event</c>. Quatre bugs sont remontés au
/// premier F6 :
/// <list type="number">
///   <item>Tooltip ne s'allumait qu'à zoom élevé (le BackgroundRect
///         couvre tout le viewport avec <c>mouse_filter=Stop</c> par
///         défaut, ce qui mange le picking physique Area2D dans Godot
///         4.x — trap documenté
///         <c>feedback_godot_rendering_input_traps</c>).</item>
///   <item>Tooltip disparaissait en touchant un bord (clamp viewport
///         trop strict après bascule incomplète).</item>
///   <item>Tooltip ne réapparaissait qu'après re-zoom (la machine à
///         états <c>mouse_entered</c>/<c>mouse_exited</c> ne se
///         resettait pas proprement quand le picking ratait une frame).</item>
///   <item>Le clic n'arrivait jamais sur l'<c>input_event</c> de
///         l'Area2D pour la même raison que (1).</item>
/// </list>
/// Le contournement est de retirer complètement l'Area2D et de faire un
/// hit-test manuel dans <c>_Input</c> / <c>_Process</c> :
/// <c>GetGlobalMousePosition()</c> donne la position monde tenant compte
/// automatiquement de la caméra, puis on teste l'appartenance à la
/// <i>forme</i> du POI -- losange iso 4×4 cartes inscrit dans le bitmap
/// 1024×512, sommets en (centre±512, centre) et (centre, centre±256) --
/// via <c>|dx|/512 + |dy|/256 &lt;= 1</c> (cf.
/// <see cref="IsPointInPoiDiamond"/>). Le rectangulaire
/// <c>Rect2.HasPoint()</c> initial allumait le tooltip dans les coins
/// transparents du PNG ; corrigé 2026-05-13 pm. C'est simple, robuste,
/// immune au mouse_filter des Controls qui passent au-dessus, et fait
/// moins de magie engine. Voir aussi rappel mémoire
/// <c>feedback_godot_rendering_input_traps</c> §J9.
/// </para>
///
/// <para>
/// <b>Historique court.</b>
/// <list type="bullet">
///   <item><b>2026-05-12 (e1 A)</b> : grille 24×24 voile cadastral activée,
///         zoom 0.31×→2.0× molette OK, preflight 6/6 OK.</item>
///   <item><b>2026-05-13 (e1 B-1)</b> : flip 3D simulé en 2D, trigger t=3s,
///         zoom bloqué pendant l'animation, preflight 8/8.</item>
///   <item><b>2026-05-13 (e1 B-2)</b> : dépôt POI Halfgate fade-in 0.6s à
///         t≈4.95s. Preflight 10/10. Correction off-by-one centre (gx,gy)=(11.5,11.5).</item>
///   <item><b>2026-05-13 (framework placeholder)</b> : PoiTexture chargée via
///         AssetLoader (PNG <c>_PLACEHOLDER</c> Mira), plus de procédural C#.</item>
///   <item><b>2026-05-13 (e1 D)</b> : ajout hover-tooltip + click-zoom-crossfade
///         vers E2Stub. Preflight enrichi à 14/17.</item>
///   <item><b>2026-05-13 pm (e1 D bugfix)</b> : retrait Area2D, switch vers
///         hit-test manuel <c>_Input</c>. Repositionnement tooltip 4-quadrants
///         garanti visible. BackgroundRect <c>mouse_filter=Ignore</c>
///         pour bonne mesure. Preflight 11–14 réécrits.</item>
///   <item><b>2026-05-13 pm (e1 D diamond)</b> : hit-test rectangulaire
///         1024×512 → point-in-diamond losange iso 4×4 cartes (le tooltip
///         n'allume plus dans les coins transparents). Preflight 11/17
///         valide désormais les 4 sommets monde Top/Right/Bottom/Left
///         + self-test du prédicat (centre dedans, coin bbox dehors).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Critère d'acceptance visuel F6 (e1 D post-bugfix).</b>
/// <list type="bullet">
///   <item>Tout ce qui marchait avant marche toujours (grille, zoom, flip,
///         POI fade-in).</item>
///   <item>Après t≈5.55s, survol du POI Halfgate <b>au zoom de boot 0.31×</b>
///         → tooltip parchemin apparaît au curseur après 250ms avec
///         "Halfgate" en gras + 2 lignes lore. Il suit le curseur. Quitte
///         le POI → tooltip disparaît. Revient sur le POI → tooltip
///         réapparaît sans avoir à toucher la molette.</item>
///   <item>Approcher la souris d'un bord du viewport : le tooltip
///         <b>bascule</b> du côté opposé et <b>reste visible</b>.</item>
///   <item>Clic gauche sur le POI → zoom-in fluide vers 2.5× sur ~1s,
///         puis fondu noir, puis scène E2Stub magenta avec label
///         "E2 Halfgate Stub - Placeholder Scene".</item>
///   <item>Console output contient deux lignes <c>[PLACEHOLDER LOAD]</c>
///         supplémentaires (tooltip + seal) au boot, et un log
///         <c>[PROBE IsoMapE1Probe] click POI</c> au moment du clic.</item>
///   <item>Preflight 14/17 OK.</item>
/// </list>
/// </para>
/// </summary>
public partial class IsoMapE1Probe : Node2D
{
    // --- Mode locked 2026-05-13 ---
    private static readonly bool DisplaySingleTileOnly = false;

    // --- Paramètres grille ---
    private const int GridSize = 24;
    private const int CenterIndex = 12;

    // --- Texture Mira v5 : 256×130 (face-top losange 256×128 + tranche 256×2) ---
    private const int TileTextureWidthPx = 256;
    private const int TileFaceHeightPx = 128;
    private const int TileSliceHeightPx = 2;
    private const int TileTextureHeightPx = TileFaceHeightPx + TileSliceHeightPx; // 130

    // --- Stride iso 2D screen (iso 2:1 classique) ---
    private const float IsoWStride = 128f;
    private const float IsoHStride = 64f;

    // --- Sprite2D Offset.Y : aligner centre-losange face-top sur Position ---
    private const float SpriteOffsetY = TileFaceHeightPx / 2f - TileTextureHeightPx / 2f; // -1

    // --- Caméra : zoom inspection ---
    private const float ZoomInspect = 0.31f;
    private const float ZoomMin = 0.31f;
    private const float ZoomMax = 2.0f;
    private const float ZoomWheelStep = 1.1f;

    // --- Asset directory & names (framework placeholder Mira <-> Rune) ---
    private const string E1AssetDir = "res://assets/wayfinders_visual_assets/e1";
    private const string TileAssetName = "wf_e1_tile_neutral.png";
    private const string PoiHalfgateAssetName = "wf_e1_halfgate_poi.png";
    private const string TooltipParchmentAssetName = "wf_e1_tooltip_parchment.png";
    private const string SealWaxAssetName = "wf_e1_seal_wax.png";

    // --- Face-B Halfgate textures (16 lozenge patches 256×128, livraison Mira 2026-05-13) ---
    // Convention sidecar Mira (halfgate_face_b.txt) :
    //   filename = halfgate_face_b_{N}{M}.png
    //   N (col, 0..3) : 0 = west  (image-left)  ... 3 = east  (image-right)
    //   M (row, 0..3) : 0 = north (image-top)   ... 3 = south (image-bottom)
    // Mapping naïf vers la sim-grid (10..13, 10..13) :
    //   grid_col = 10 + N   (gx, axe est positif)
    //   grid_row = 10 + M   (gy, axe sud positif)  -- aligné car screenY=(gx+gy)*H
    // Si la validation visuelle F6 révèle l'archipel SE ailleurs qu'au coin SE
    // de la grille, mettre InvertFaceBMAxis=true pour inverser : grid_row = 13 - M.
    private const string FaceBAssetSubdir = "halfgate_face_b";
    private const string FaceBAssetDir = E1AssetDir + "/" + FaceBAssetSubdir;
    private const int FaceBTextureWidthPx = 256;
    private const int FaceBTextureHeightPx = 128;
    private static readonly bool InvertFaceBMAxis = false;

    /// <summary>
    /// <b>Flag override face B (locked 2026-05-13).</b> Par défaut <c>false</c> =
    /// chaque carte flippée révèle son patch côtier face-B (16 textures Mira
    /// distinctes). Si <c>true</c>, comportement legacy : face B = face A
    /// (même voile cadastral, swap no-op), utile pour A/B debug.
    /// </summary>
    private static readonly bool UseFaceBVoileCadastral = false;

    /// <summary>
    /// <b>Flag approche unifiee Halfgate (locked 2026-05-13 pm).</b> Par defaut
    /// <c>false</c> : les 16 patches face-B Mira contiennent deja la ville
    /// fortifiee dessinee dans la carte (livraison 2026-05-13 pm, sidecar
    /// <c>halfgate_face_b.txt</c> v2). Le Sprite2D POI separe est donc cree
    /// pour preserver la position + la hitbox losange (hover/click toujours
    /// actifs sur la zone 4x4 cartes) mais reste invisible (Modulate.A=0,
    /// jamais fade-in). Si <c>true</c> : comportement legacy, le sprite POI
    /// fade-in en surimpression apres le flip -- utile pour A/B debug ou pour
    /// tester un POI separe sur une autre map.
    /// </summary>
    private static readonly bool ShowPoiSeparateSprite = false;

    // --- Hover feedback e1 D (approche unifiee) ---
    // Modulate applique aux 16 sprites face-B quand la souris entre dans la
    // zone losange. Choisi en mode "le moins intrusif possible" : un boost
    // de luminosite tres leger (1.10 ~ +10%), sature legerement plus pour
    // matcher la teinte chaude du parchemin. Le retour a Colors.White se
    // fait a la sortie de la zone. Tween rapide 80ms pour eviter le pop.
    private static readonly Color HoverModulateOn = new Color(1.12f, 1.10f, 1.05f, 1f);
    private static readonly Color HoverModulateOff = Colors.White;
    private const double HoverModulateTweenSec = 0.08;


    // --- E2 transition target ---
    private const string E2StubScenePath = "res://scenes/scratch/E2Stub/E2Stub.tscn";

    // --- Background expected color ---
    private static readonly Color ExpectedClearColor = new Color(1f, 0f, 1f, 1f);

    /// <summary>
    /// <b>Flag A/B (locked 2026-05-13).</b> Par défaut <c>false</c> = le système
    /// de fallback Mira est actif (le code passe par <see cref="AssetLoader"/>,
    /// qui charge le PNG final si présent sinon le <c>_PLACEHOLDER</c>).
    /// </summary>
    private static readonly bool UseProceduralPlaceholder = false;

    // Chemin direct utilisé uniquement si UseProceduralPlaceholder=true (bypass).
    private const string TilePrimaryPath = E1AssetDir + "/" + TileAssetName;

    // --- Placeholder procédural tuile (gardé pour A/B debug bypass uniquement) ---
    private static readonly Color PlaceholderInterior = new Color(0.92f, 0.85f, 0.70f, 1f);
    private static readonly Color PlaceholderBorder = new Color(0.55f, 0.35f, 0.18f, 1f);
    private const int PlaceholderBorderPx = 2;

    // --- Reveal e1 B-1 ---
    private const int RevealMinGx = 10;
    private const int RevealMaxGx = 13;
    private const int RevealMinGy = 10;
    private const int RevealMaxGy = 13;
    private const int RevealTileCount = (RevealMaxGx - RevealMinGx + 1) * (RevealMaxGy - RevealMinGy + 1); // 16
    private const double RevealTriggerDelaySec = 3.0;
    private const double RevealPerCardDurationSec = 1.2;
    private const double RevealStaggerStepSec = 0.05;
    private const double RevealTotalDurationSec =
        RevealStaggerStepSec * (RevealTileCount - 1) + RevealPerCardDurationSec;

    // --- POI Halfgate e1 B-2 ---
    private const int PoiTextureWidthPx = 1024;
    private const int PoiTextureHeightPx = 512;
    // Demi-dimensions du losange iso inscrit dans le bitmap 1024×512 :
    // sommets en (centre±512, centre) et (centre, centre±256). Utilisés
    // par le hit-test point-in-diamond (bugfix rect->diamond 2026-05-13).
    private const float PoiDiamondHalfWidthPx = PoiTextureWidthPx / 2f;   // 512
    private const float PoiDiamondHalfHeightPx = PoiTextureHeightPx / 2f; // 256
    private const int PoiZIndex = 10;
    private const double PoiFadeInDurationSec = 0.6;

    // --- Tooltip e1 D ---
    private const int TooltipParchmentWidthPx = 320;
    private const int TooltipParchmentHeightPx = 180;
    private const int SealWaxWidthPx = 64;
    private const int SealWaxHeightPx = 64;
    private const double TooltipHoverDelaySec = 0.250;
    private const double TooltipFadeInDurationSec = 0.150;
    private static readonly Vector2 TooltipCursorOffset = new Vector2(24f, 24f);
    private const string TooltipTitleText = "Halfgate";
    private const string TooltipSubtitleText = "Cité-porte de la marche";
    private const string TooltipBodyText = "Comptoir fortifié à l'embranchement des routes commerciales.\nPremier point d'appui de la Compagnie.";

    // --- Click / zoom-in / crossfade e1 D ---
    private const double ClickZoomTargetZoom = 2.5;
    private const double ClickZoomDurationSec = 1.0;
    private const double ClickCrossfadeDurationSec = 0.4;

    // --- État runtime ---
    private Camera2D _camera = null!;
    private Node2D _worldRoot = null!;
    private Node2D _tileGrid = null!;
    private Node3D _shadowRoot3D = null!;
    private CanvasLayer _tooltipLayer = null!;
    private CanvasLayer _crossfadeLayer = null!;
    private ColorRect _crossfadeRect = null!;
    private float _currentZoom = ZoomInspect;
    private Texture2D _tileTexture = null!;
    private readonly Dictionary<Vector2I, Texture2D> _faceBTextures = new();
    private Sprite2D? _halfgatePoi;

    // --- Tooltip nodes ---
    private Control? _tooltipRoot;
    private TextureRect? _tooltipParchment;
    private TextureRect? _tooltipSeal;
    private Label? _tooltipTitleLabel;
    private Label? _tooltipSubtitleLabel;
    private Label? _tooltipBodyLabel;
    private Tween? _tooltipFadeTween;

    // --- Hover state (manual hit-test) ---
    // _poiHoverEnabled : false jusqu'à la fin de la cinématique, true ensuite.
    // _isHoveringPoi   : la souris est actuellement dans le rect monde du POI.
    // _isTooltipVisible: le tooltip est actuellement en train de s'afficher /
    //                   est affiché (passe à true au moment où on déclenche
    //                   ShowTooltip, repasse à false dans HideTooltip).
    // _pendingHoverToken : compteur monotone qui invalide les delays-timers
    //                     en cours quand l'état hover change. Une closure
    //                     timer ne tirera ShowTooltip que si son token
    //                     correspond encore au token courant ET que
    //                     _isHoveringPoi est toujours true.
    private bool _poiHoverEnabled;
    private bool _isHoveringPoi;
    private bool _isTooltipVisible;
    private long _pendingHoverToken;
    private Vector2 _lastCursorViewportPos;

    // --- Cursor + highlight state (approche unifiee, e1 D 2026-05-13 pm) ---
    // _currentCursorShape : derniere shape appliquee via Input.SetDefaultCursorShape.
    //                       Suivi explicite pour eviter de re-set la meme shape
    //                       chaque frame et pour la verification preflight.
    // _cursorShapeChangedOnce : passe a true des qu'on a applique PointingHand au
    //                           moins une fois -- utilise par le preflight pour
    //                           valider que la machinerie est cablee. Reset jamais.
    // _hoverHighlightTween : tween courant sur les 16 sprites face-B (modulate).
    //                       Recycle pour eviter de tween en parallele a l'aller
    //                       et au retour.
    private Input.CursorShape _currentCursorShape = Input.CursorShape.Arrow;
    private bool _cursorShapeChangedOnce;
    private Tween? _hoverHighlightTween;

    // --- Reveal state ---
    private readonly Dictionary<Vector2I, Sprite2D> _revealSprites = new();
    private bool _isRevealAnimating;
    private bool _revealCompleted;
    private bool _poiFadeInCompleted;
    private bool _clickInProgress;

    public override void _Ready()
    {
        GD.Print("[PROBE IsoMapE1Probe] scene started — e1 D + framework placeholder Mira (2026-05-13 hover + click POI Halfgate, manual hit-test bugfix)");

        _camera = GetNode<Camera2D>("WorldCamera");
        _worldRoot = GetNode<Node2D>("WorldRoot");
        _tileGrid = GetNode<Node2D>("WorldRoot/TileGrid");
        _shadowRoot3D = GetNode<Node3D>("ShadowRoot3D");
        _tooltipLayer = GetNode<CanvasLayer>("TooltipLayer");
        _crossfadeLayer = GetNode<CanvasLayer>("CrossfadeLayer");
        _crossfadeRect = GetNode<ColorRect>("CrossfadeLayer/CrossfadeRect");

        // 1. Charger la texture primaire (tuile cadastrale).
        _tileTexture = LoadPrimaryTile();

        // 1b. Charger les 16 textures face-B Halfgate (patches côtiers iso).
        //     Pas de chargement en mode SINGLE_TILE (gain temps boot debug).
        if (!DisplaySingleTileOnly && !UseFaceBVoileCadastral)
        {
            LoadFaceBTextures();
        }

        // 2. WorldRoot reste à l'origine si single, recentré si grille complète.
        _worldRoot.Position = Vector2.Zero;

        // 3. Instancier les sprites.
        if (DisplaySingleTileOnly)
        {
            SpawnSingleTile();
        }
        else
        {
            SpawnFullGrid();
            SpawnHalfgatePoi();
            SpawnTooltipUi();
        }

        // 4. Caméra centrée sur l'origine, zoom inspection.
        _camera.Position = Vector2.Zero;
        _camera.Zoom = new Vector2(_currentZoom, _currentZoom);
        _camera.MakeCurrent();

        // 5. Preflight canary.
        RunPreflight();

        // 6. Reveal e1 B-1 + dépôt POI e1 B-2 : trigger automatique après le délai
        //    d'observation, seulement si on est en mode grille.
        if (!DisplaySingleTileOnly)
        {
            _ = ScheduleRevealAsync();
        }
    }

    public override void _ExitTree()
    {
        // Si on a touche le cursor shape pendant cette scene, on remet Arrow
        // avant de partir -- sinon la scene suivante (E2Stub ou autre) heriterait
        // d'un PointingHand colle qu'elle ne saurait pas reset.
        if (_currentCursorShape != Input.CursorShape.Arrow)
        {
            Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        }
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        // Hit-test manuel hover POI : exécuté chaque frame une fois le POI
        // activé. C'est l'équivalent d'un mouse_entered/mouse_exited fait à
        // la main, sans dépendre du physics_object_picking de Godot ni des
        // Controls qui passent au-dessus avec mouse_filter=Stop.
        if (_poiHoverEnabled && !_clickInProgress)
        {
            UpdateHoverState();
        }

        // Tooltip suit le curseur — calcul à chaque frame tant qu'il est visible.
        // Position en coordonnées viewport (pas world) car le tooltip vit dans
        // un CanvasLayer (indépendant de la caméra/zoom).
        if (_isTooltipVisible && _tooltipRoot is not null && IsInstanceValid(_tooltipRoot))
        {
            _lastCursorViewportPos = GetViewport().GetMousePosition();
            UpdateTooltipPosition();
        }
    }

    /// <summary>
    /// Hit-test souris-vs-POI en coordonnées monde. <c>GetGlobalMousePosition()</c>
    /// gère automatiquement la transformation viewport → world via la caméra
    /// courante, donc fonctionne identiquement à tous les niveaux de zoom.
    ///
    /// <para>
    /// <b>Diamond, not Rect2 (bugfix 2026-05-13).</b> Le PNG POI est un
    /// rectangle 1024×512 mais la <i>forme visible</i> est le losange iso
    /// 4×4 cartes qui occupe exactement l'enveloppe du bitmap, sommets aux
    /// 4 milieux d'arêtes. Le hit-test précédent utilisait
    /// <c>Rect2.HasPoint()</c> sur le bounding-box rectangulaire, ce qui
    /// allumait le tooltip dans les coins transparents. On teste maintenant
    /// l'appartenance au losange par <c>|dx|/hw + |dy|/hh &lt;= 1</c> avec
    /// (hw, hh) = (<see cref="PoiDiamondHalfWidthPx"/>,
    /// <see cref="PoiDiamondHalfHeightPx"/>) = (512, 256) — exactement les
    /// demi-dimensions du bitmap puisque le losange iso inscrit dans un
    /// 1024×512 a ses sommets en (centre±512, centre) et (centre, centre±256).
    /// </para>
    /// </summary>
    private void UpdateHoverState()
    {
        if (_halfgatePoi is null || !IsInstanceValid(_halfgatePoi))
        {
            return;
        }

        Vector2 worldMouse = GetGlobalMousePosition();
        bool insideNow = IsPointInPoiDiamond(worldMouse);

        if (insideNow && !_isHoveringPoi)
        {
            OnPoiHoverEnter();
        }
        else if (!insideNow && _isHoveringPoi)
        {
            OnPoiHoverExit();
        }
    }

    /// <summary>
    /// Point-in-diamond test : <c>|dx|/halfwidth + |dy|/halfheight &lt;= 1</c>
    /// où <c>(dx, dy)</c> est l'offset entre <paramref name="worldPoint"/>
    /// et le centre monde du losange (= <see cref="Sprite2D.GlobalPosition"/>
    /// du POI, sprite Centered=true Offset=zero). Convention inclusive sur
    /// les bords pour matcher la sémantique de <c>Rect2.HasPoint()</c>
    /// remplacée, et éviter qu'un pixel pile sur un sommet ne soit
    /// considéré hors-forme.
    /// </summary>
    private bool IsPointInPoiDiamond(Vector2 worldPoint)
    {
        if (_halfgatePoi is null || !IsInstanceValid(_halfgatePoi))
        {
            return false;
        }

        Vector2 center = _halfgatePoi.GlobalPosition;
        float dx = Mathf.Abs(worldPoint.X - center.X);
        float dy = Mathf.Abs(worldPoint.Y - center.Y);
        return (dx / PoiDiamondHalfWidthPx) + (dy / PoiDiamondHalfHeightPx) <= 1f;
    }

    /// <summary>
    /// Renvoie les 4 sommets monde du losange iso POI Halfgate, dans
    /// l'ordre Top, Right, Bottom, Left. Utilisé par le preflight pour
    /// pinner la géométrie de hit-test (et plus tard pour le debug-draw).
    /// </summary>
    private (Vector2 Top, Vector2 Right, Vector2 Bottom, Vector2 Left) GetPoiDiamondVertices()
    {
        Vector2 c = _halfgatePoi!.GlobalPosition;
        return (
            Top:    new Vector2(c.X,                              c.Y - PoiDiamondHalfHeightPx),
            Right:  new Vector2(c.X + PoiDiamondHalfWidthPx,      c.Y),
            Bottom: new Vector2(c.X,                              c.Y + PoiDiamondHalfHeightPx),
            Left:   new Vector2(c.X - PoiDiamondHalfWidthPx,      c.Y));
    }

    private void OnPoiHoverEnter()
    {
        _isHoveringPoi = true;
        _lastCursorViewportPos = GetViewport().GetMousePosition();

        // Curseur main au survol de la zone losange (feedback de cliquabilite).
        SetCursorShape(Input.CursorShape.PointingHand);

        // Highlight subtil collectif sur les 16 sprites face-B (modulate
        // plus lumineux + legerement plus chaud). Mode unifie uniquement,
        // pour ne pas double-feedback avec le sprite POI fade-in.
        if (!ShowPoiSeparateSprite)
        {
            ApplyHoverHighlight(on: true);
        }

        // Delai 250ms anti-clignotement, garde-fou via token monotone.
        long myToken = ++_pendingHoverToken;
        var timer = GetTree().CreateTimer(TooltipHoverDelaySec);
        timer.Timeout += () =>
        {
            // On affiche seulement si :
            // (1) le hover est toujours actif au moment ou le delay expire,
            // (2) personne n'a invalide ce token entre-temps (mouse-exit puis
            //     re-enter relance un nouveau token, _pendingHoverToken avance),
            // (3) on n'est pas en plein zoom-out de clic.
            if (_isHoveringPoi && myToken == _pendingHoverToken && !_clickInProgress)
            {
                ShowTooltip();
            }
        };
    }

    private void OnPoiHoverExit()
    {
        _isHoveringPoi = false;

        // Retour curseur fleche.
        SetCursorShape(Input.CursorShape.Arrow);

        // Retour des 16 sprites face-B a leur modulate neutre.
        if (!ShowPoiSeparateSprite)
        {
            ApplyHoverHighlight(on: false);
        }

        // Invalide tout delay-timer en cours en avancant le token. Le timer
        // qui tirera plus tard fera la check myToken == _pendingHoverToken,
        // verra qu'il a ete revoque, et ne fera rien.
        _pendingHoverToken++;
        HideTooltip();
    }

    /// <summary>
    /// Applique ou retire le highlight modulate sur les 16 sprites face-B
    /// (cellules de la zone reveal 4x4). Tween rapide (~80ms) pour eviter
    /// le pop visuel et le scintillement quand le curseur oscille sur le
    /// bord du losange. Tween recycle a chaque appel pour ne jamais
    /// laisser deux tweens contradictoires actifs en meme temps.
    /// </summary>
    private void ApplyHoverHighlight(bool on)
    {
        if (_revealSprites.Count == 0)
        {
            return;
        }

        Color target = on ? HoverModulateOn : HoverModulateOff;

        _hoverHighlightTween?.Kill();
        _hoverHighlightTween = CreateTween();
        _hoverHighlightTween.SetParallel(true);
        _hoverHighlightTween.SetProcessMode(Tween.TweenProcessMode.Idle);

        foreach (var kv in _revealSprites)
        {
            var s = kv.Value;
            if (!IsInstanceValid(s))
            {
                continue;
            }
            _hoverHighlightTween.TweenProperty(s, "modulate", target, HoverModulateTweenSec)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
        }
    }

    /// <summary>
    /// Wrapper around <c>Input.SetDefaultCursorShape</c> qui evite les calls
    /// redondants et tracke le dernier shape pour le preflight 17/17.
    /// </summary>
    private void SetCursorShape(Input.CursorShape shape)
    {
        if (_currentCursorShape == shape)
        {
            return;
        }
        _currentCursorShape = shape;
        Input.SetDefaultCursorShape(shape);
        if (shape == Input.CursorShape.PointingHand)
        {
            _cursorShapeChangedOnce = true;
        }
    }


    public override void _Input(InputEvent @event)
    {
        // Click POI : hit-test manuel sur l'événement clic gauche.
        // Cette branche tourne aussi quand un Control au-dessus a
        // mouse_filter=Stop : _Input est appelé AVANT _gui_input et
        // _unhandled_input, donc on capte le clic en amont du système Control.
        if (@event is InputEventMouseButton mb
            && mb.Pressed
            && mb.ButtonIndex == MouseButton.Left
            && _poiHoverEnabled
            && !_clickInProgress
            && _halfgatePoi is not null
            && IsInstanceValid(_halfgatePoi))
        {
            Vector2 worldMouse = GetGlobalMousePosition();
            if (IsPointInPoiDiamond(worldMouse))
            {
                GD.Print("[PROBE IsoMapE1Probe] click POI Halfgate — starting zoom-in + crossfade to E2Stub");
                GetViewport().SetInputAsHandled();
                _ = HandlePoiClickAsync();
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (_isRevealAnimating || _clickInProgress)
            {
                return;
            }

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

                if (gx >= RevealMinGx && gx <= RevealMaxGx
                    && gy >= RevealMinGy && gy <= RevealMaxGy)
                {
                    _revealSprites[new Vector2I(gx, gy)] = sprite;
                }
            }
        }
    }

    /// <summary>
    /// Convertit une position de grille fractionnaire (gx, gy) en position
    /// écran iso dans le repère local de <c>_tileGrid</c>.
    /// </summary>
    private static Vector2 GridToScreen(float gx, float gy)
    {
        return new Vector2((gx - gy) * IsoWStride, (gx + gy) * IsoHStride);
    }

    /// <summary>
    /// Cree le Sprite2D POI Halfgate centre sur l'imprint 4x4. Le sprite sert
    /// a la fois de marqueur visuel (mode legacy <see cref="ShowPoiSeparateSprite"/>=true)
    /// et de "container de hitbox" (mode unifie par defaut, le sprite reste
    /// invisible mais sa Position + GetRect alimentent <see cref="IsPointInPoiDiamond"/>).
    ///
    /// <para>
    /// <b>Mode unifie (defaut).</b> Modulate.A=0, Visible=true. On garde
    /// Visible=true parce que <c>UpdateHoverState</c> n'a besoin que de la
    /// Position monde + GlobalPosition -- pas du rendu. Mettre Visible=false
    /// fonctionne aussi (le sprite skip son draw) ; on prefere Modulate.A=0
    /// pour rester coherent avec l'ancien chemin de fade-in (et permettre
    /// un toggle runtime ulterieur sans toucher la visibilite).
    /// </para>
    /// </summary>
    private void SpawnHalfgatePoi()
    {
        var poiTexture = LoadHalfgatePoiTexture();

        float imprintCenterGx = (RevealMinGx + RevealMaxGx) * 0.5f; // 11.5
        float imprintCenterGy = (RevealMinGy + RevealMaxGy) * 0.5f; // 11.5
        Vector2 imprintCenterScreen = GridToScreen(imprintCenterGx, imprintCenterGy); // (0, 1472)

        _halfgatePoi = new Sprite2D
        {
            Name = "HalfgatePoi",
            Texture = poiTexture,
            Centered = true,
            Offset = Vector2.Zero,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Position = imprintCenterScreen,
            ZIndex = PoiZIndex,
            ZAsRelative = false,
            // Approche unifiee (defaut) : Modulate.A=0 -> jamais visible, mais
            // Position + GetRect intacts pour le hit-test losange.
            // Mode legacy (ShowPoiSeparateSprite=true) : on demarre invisible
            // aussi, puis FadeInPoiAsync ramene a 1.0 apres la cinematique.
            Modulate = new Color(1f, 1f, 1f, 0f),
        };

        _tileGrid.AddChild(_halfgatePoi);
    }

    /// <summary>
    /// Construit le tooltip diégétique (parchemin + sceau + 3 Labels) sous
    /// <c>TooltipLayer</c>. Invisible au boot (<c>visible=false</c>).
    /// </summary>
    private void SpawnTooltipUi()
    {
        var parchmentTex = AssetLoader.LoadAssetOrPlaceholder(E1AssetDir, TooltipParchmentAssetName);
        var sealTex = AssetLoader.LoadAssetOrPlaceholder(E1AssetDir, SealWaxAssetName);

        _tooltipRoot = new Control
        {
            Name = "TooltipRoot",
            CustomMinimumSize = new Vector2(TooltipParchmentWidthPx, TooltipParchmentHeightPx),
            Size = new Vector2(TooltipParchmentWidthPx, TooltipParchmentHeightPx),
            MouseFilter = Control.MouseFilterEnum.Ignore, // le tooltip ne mange pas les inputs
            Visible = false,
            Modulate = new Color(1f, 1f, 1f, 0f),
            PivotOffset = new Vector2(TooltipParchmentWidthPx / 2f, TooltipParchmentHeightPx / 2f),
            Scale = new Vector2(0.95f, 0.95f),
        };
        _tooltipLayer.AddChild(_tooltipRoot);

        _tooltipParchment = new TextureRect
        {
            Name = "ParchmentBg",
            Texture = parchmentTex,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        _tooltipRoot.AddChild(_tooltipParchment);

        _tooltipSeal = new TextureRect
        {
            Name = "SealWax",
            Texture = sealTex,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetLeft = TooltipParchmentWidthPx - SealWaxWidthPx - 12f,
            OffsetTop = TooltipParchmentHeightPx - SealWaxHeightPx - 12f,
            OffsetRight = TooltipParchmentWidthPx - 12f,
            OffsetBottom = TooltipParchmentHeightPx - 12f,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        _tooltipRoot.AddChild(_tooltipSeal);

        _tooltipTitleLabel = new Label
        {
            Name = "TitleLabel",
            Text = TooltipTitleText,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetLeft = 20f,
            OffsetTop = 14f,
            OffsetRight = TooltipParchmentWidthPx - 20f,
            OffsetBottom = 46f,
        };
        _tooltipTitleLabel.AddThemeFontSizeOverride("font_size", 24);
        _tooltipTitleLabel.AddThemeColorOverride("font_color", new Color(0.18f, 0.10f, 0.05f, 1f));
        _tooltipRoot.AddChild(_tooltipTitleLabel);

        _tooltipSubtitleLabel = new Label
        {
            Name = "SubtitleLabel",
            Text = TooltipSubtitleText,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetLeft = 20f,
            OffsetTop = 48f,
            OffsetRight = TooltipParchmentWidthPx - 20f,
            OffsetBottom = 74f,
        };
        _tooltipSubtitleLabel.AddThemeFontSizeOverride("font_size", 14);
        _tooltipSubtitleLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.20f, 0.10f, 1f));
        _tooltipRoot.AddChild(_tooltipSubtitleLabel);

        _tooltipBodyLabel = new Label
        {
            Name = "BodyLabel",
            Text = TooltipBodyText,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetLeft = 20f,
            OffsetTop = 82f,
            OffsetRight = TooltipParchmentWidthPx - 20f,
            OffsetBottom = TooltipParchmentHeightPx - 20f,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _tooltipBodyLabel.AddThemeFontSizeOverride("font_size", 12);
        _tooltipBodyLabel.AddThemeColorOverride("font_color", new Color(0.25f, 0.15f, 0.08f, 1f));
        _tooltipRoot.AddChild(_tooltipBodyLabel);
    }

    /// <summary>
    /// Charge la texture POI Halfgate via le framework placeholder Mira.
    /// </summary>
    private Texture2D LoadHalfgatePoiTexture()
    {
        var tex = AssetLoader.LoadAssetOrPlaceholder(E1AssetDir, PoiHalfgateAssetName);
        if (tex is null)
        {
            GD.PushError($"[PROBE IsoMapE1Probe] LoadHalfgatePoiTexture: AssetLoader returned null for {PoiHalfgateAssetName} — falling back to empty PlaceholderTexture2D");
            return new PlaceholderTexture2D();
        }
        return tex;
    }

    // ------------------------------------------------------------------------
    // Tooltip show/hide/position — e1 D
    // ------------------------------------------------------------------------

    private void ShowTooltip()
    {
        if (_tooltipRoot is null || !IsInstanceValid(_tooltipRoot))
        {
            return;
        }

        _isTooltipVisible = true;
        _tooltipRoot.Visible = true;
        UpdateTooltipPosition();

        _tooltipFadeTween?.Kill();
        _tooltipFadeTween = CreateTween();
        _tooltipFadeTween.SetParallel(true);
        _tooltipFadeTween.TweenProperty(_tooltipRoot, "modulate:a", 1.0f, TooltipFadeInDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _tooltipFadeTween.TweenProperty(_tooltipRoot, "scale", Vector2.One, TooltipFadeInDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }

    private void HideTooltip()
    {
        if (_tooltipRoot is null || !IsInstanceValid(_tooltipRoot))
        {
            return;
        }

        _isTooltipVisible = false;
        _tooltipFadeTween?.Kill();
        _tooltipFadeTween = null;
        _tooltipRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _tooltipRoot.Scale = new Vector2(0.95f, 0.95f);
        _tooltipRoot.Visible = false;
    }

    /// <summary>
    /// Positionnement tooltip 4-quadrants : essaye d'abord bas-droite (le
    /// défaut, curseur + offset), puis bas-gauche, puis haut-droite, puis
    /// haut-gauche. Choisit le premier quadrant qui tient entièrement dans
    /// le viewport. Si aucun ne tient (viewport plus petit que le tooltip,
    /// cas pathologique), force un clamp visible — jamais hors écran.
    /// Différent de l'ancienne version qui basculait puis re-clampait, ce
    /// qui pouvait faire collapse le tooltip sur un bord.
    /// </summary>
    private void UpdateTooltipPosition()
    {
        if (_tooltipRoot is null || !IsInstanceValid(_tooltipRoot))
        {
            return;
        }

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        Vector2 tooltipSize = new Vector2(TooltipParchmentWidthPx, TooltipParchmentHeightPx);
        Vector2 cursor = _lastCursorViewportPos;
        Vector2 off = TooltipCursorOffset;

        // 4 quadrants candidats, dans l'ordre de préférence.
        // Bas-droite (défaut) : top-left = cursor + (off.X, off.Y)
        // Bas-gauche           : top-left = cursor + (-off.X - W, off.Y)
        // Haut-droite          : top-left = cursor + (off.X, -off.Y - H)
        // Haut-gauche          : top-left = cursor + (-off.X - W, -off.Y - H)
        Span<Vector2> candidates = stackalloc Vector2[4];
        candidates[0] = cursor + new Vector2(off.X, off.Y);
        candidates[1] = cursor + new Vector2(-off.X - tooltipSize.X, off.Y);
        candidates[2] = cursor + new Vector2(off.X, -off.Y - tooltipSize.Y);
        candidates[3] = cursor + new Vector2(-off.X - tooltipSize.X, -off.Y - tooltipSize.Y);

        Vector2 chosen = candidates[0];
        bool found = false;
        for (int i = 0; i < 4; i++)
        {
            Vector2 c = candidates[i];
            if (c.X >= 0f
                && c.Y >= 0f
                && c.X + tooltipSize.X <= viewportSize.X
                && c.Y + tooltipSize.Y <= viewportSize.Y)
            {
                chosen = c;
                found = true;
                break;
            }
        }

        if (!found)
        {
            // Aucun quadrant ne fit (viewport pathologiquement petit). On
            // clamp à dans-les-bornes pour garantir visibilité totale, quitte
            // à recouvrir le curseur.
            chosen = new Vector2(
                Mathf.Clamp(candidates[0].X, 0f, Math.Max(0f, viewportSize.X - tooltipSize.X)),
                Mathf.Clamp(candidates[0].Y, 0f, Math.Max(0f, viewportSize.Y - tooltipSize.Y))
            );
        }

        _tooltipRoot.Position = chosen;
    }

    // ------------------------------------------------------------------------
    // Click POI → zoom-in → crossfade → ChangeSceneToFile
    // ------------------------------------------------------------------------

    private async Task HandlePoiClickAsync()
    {
        _clickInProgress = true;
        HideTooltip();

        // Désactive le hover-tracking pendant la transition.
        _poiHoverEnabled = false;
        _isHoveringPoi = false;
        _pendingHoverToken++;

        // Reset curseur + highlight face-B avant le zoom-in (sinon le pointer
        // hand reste fige pendant la transition et le highlight reste applique).
        SetCursorShape(Input.CursorShape.Arrow);
        if (!ShowPoiSeparateSprite)
        {
            ApplyHoverHighlight(on: false);
        }


        // 1. Zoom-in caméra : zoom courant → 2.5× sur 1.0s, ease_in_quad.
        var zoomTween = CreateTween();
        zoomTween.SetProcessMode(Tween.TweenProcessMode.Idle);
        Vector2 targetZoom = new Vector2((float)ClickZoomTargetZoom, (float)ClickZoomTargetZoom);
        zoomTween.TweenProperty(_camera, "zoom", targetZoom, ClickZoomDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        // Centre la caméra sur le POI au passage (donne le sentiment de "tomber dedans").
        if (_halfgatePoi is not null && IsInstanceValid(_halfgatePoi))
        {
            Vector2 poiGlobalPos = _halfgatePoi.GlobalPosition;
            zoomTween.Parallel().TweenProperty(_camera, "global_position", poiGlobalPos, ClickZoomDurationSec)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
        }

        await ToSignal(zoomTween, Tween.SignalName.Finished);

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        // 2. Crossfade : fade-in du ColorRect noir sur 400ms.
        var fadeTween = CreateTween();
        fadeTween.SetProcessMode(Tween.TweenProcessMode.Idle);
        fadeTween.TweenProperty(_crossfadeRect, "color", new Color(0f, 0f, 0f, 1f), ClickCrossfadeDurationSec)
            .SetTrans(Tween.TransitionType.Linear);

        await ToSignal(fadeTween, Tween.SignalName.Finished);

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        // 3. Switch scene vers E2Stub.
        GD.Print($"[PROBE IsoMapE1Probe] crossfade complete — calling ChangeSceneToFile({E2StubScenePath})");
        var err = GetTree().ChangeSceneToFile(E2StubScenePath);
        if (err != Error.Ok)
        {
            GD.PushError($"[PROBE IsoMapE1Probe] ChangeSceneToFile failed with error {err} — E2Stub.tscn introuvable ?");
        }
    }

    // ------------------------------------------------------------------------
    // Reveal e1 B-1 — flip 3D simulé en 2D
    // ------------------------------------------------------------------------

    private async Task ScheduleRevealAsync()
    {
        var timer = GetTree().CreateTimer(RevealTriggerDelaySec);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        GD.Print($"[PROBE IsoMapE1Probe] reveal trigger fired at t≈{RevealTriggerDelaySec:F1}s — starting 4×4 spiral flip on {_revealSprites.Count} tiles");

        _isRevealAnimating = true;

        var spiralOrder = BuildSpiralOrder();

        for (int i = 0; i < spiralOrder.Count; i++)
        {
            var (cell, sprite) = spiralOrder[i];
            double startDelay = RevealStaggerStepSec * i;
            FlipSprite(sprite, cell, startDelay);
        }

        var endTimer = GetTree().CreateTimer(RevealTotalDurationSec);
        await ToSignal(endTimer, SceneTreeTimer.SignalName.Timeout);

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        _revealCompleted = true;
        GD.Print($"[PROBE IsoMapE1Probe] flip completed at t≈{RevealTriggerDelaySec + RevealTotalDurationSec:F2}s — starting POI Halfgate fade-in ({PoiFadeInDurationSec:F2}s)");

        await FadeInPoiAsync();

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        _isRevealAnimating = false;
        _poiFadeInCompleted = true;

        // Active le hover/click manuel maintenant que le POI est visible.
        _poiHoverEnabled = true;

        GD.Print($"[PROBE IsoMapE1Probe] POI fade-in completed at t≈{RevealTriggerDelaySec + RevealTotalDurationSec + PoiFadeInDurationSec:F2}s — zoom unlocked, hover/click POI enabled (e1 D, manual hit-test)");
    }

    private async Task FadeInPoiAsync()
    {
        if (_halfgatePoi is null || !IsInstanceValid(_halfgatePoi))
        {
            GD.PushError("[PROBE IsoMapE1Probe] FadeInPoiAsync: _halfgatePoi is null or freed, skipping fade-in");
            return;
        }

        // Approche unifiee (defaut) : la ville est deja dans les 16 patches
        // face-B. On ne fade-in pas le sprite POI separe -- il reste a
        // Modulate.A=0 pour servir uniquement de hitbox losange.
        if (!ShowPoiSeparateSprite)
        {
            GD.Print($"[PROBE IsoMapE1Probe] FadeInPoiAsync: SKIPPED (ShowPoiSeparateSprite=false, approche unifiee -- la ville est dessinee dans les 16 patches face-B Mira ; le Sprite2D POI reste invisible mais sa hitbox losange est active)");
            // On respecte tout de meme le timing : delay equivalent pour que
            // _poiFadeInCompleted bascule au meme t qu'avant -- evite de
            // changer la fenetre d'activation hover/click.
            var skipTimer = GetTree().CreateTimer(PoiFadeInDurationSec);
            await ToSignal(skipTimer, SceneTreeTimer.SignalName.Timeout);
            return;
        }

        var tween = CreateTween();
        tween.SetProcessMode(Tween.TweenProcessMode.Idle);
        tween.TweenProperty(_halfgatePoi, "modulate:a", 1.0f, PoiFadeInDurationSec)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);

        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private List<(Vector2I cell, Sprite2D sprite)> BuildSpiralOrder()
    {
        var ordered = new List<(Vector2I cell, Sprite2D sprite)>(_revealSprites.Count);
        foreach (var kv in _revealSprites)
        {
            ordered.Add((kv.Key, kv.Value));
        }

        ordered.Sort((a, b) =>
        {
            int dxA = a.cell.X - CenterIndex;
            int dyA = a.cell.Y - CenterIndex;
            int dxB = b.cell.X - CenterIndex;
            int dyB = b.cell.Y - CenterIndex;

            int ringA = Math.Max(Math.Abs(dxA), Math.Abs(dyA));
            int ringB = Math.Max(Math.Abs(dxB), Math.Abs(dyB));
            if (ringA != ringB)
            {
                return ringA.CompareTo(ringB);
            }

            double angleA = Math.Atan2(dyA, dxA);
            double angleB = Math.Atan2(dyB, dxB);
            return angleA.CompareTo(angleB);
        });

        return ordered;
    }

    private void FlipSprite(Sprite2D sprite, Vector2I cell, double startDelay)
    {
        double halfDuration = RevealPerCardDurationSec * 0.5;

        var tween = CreateTween();
        tween.SetProcessMode(Tween.TweenProcessMode.Idle);

        if (startDelay > 0.0)
        {
            tween.TweenInterval(startDelay);
        }

        tween.TweenProperty(sprite, "scale:y", 0.0f, halfDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);

        tween.TweenCallback(Callable.From(() => OnHalfFlipReached(sprite, cell)));

        // Bugfix 2026-05-13 : seconde moitié du flip remonte vers +1.0f
        // (et pas -1.0f). La texture face-B a déjà été swappée au midpoint,
        // donc atterrir à -1.0f laissait chaque carte flippée verticalement
        // de façon permanente (labels (gx,gy) lisibles à l'envers au F6).
        // L'illusion 3D reste lisible : scale.y passe par 0 puis se redéploie
        // vers le haut, ce qui se lit comme un dévoilement de la face B.
        tween.TweenProperty(sprite, "scale:y", 1.0f, halfDuration)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
    }

    private void OnHalfFlipReached(Sprite2D sprite, Vector2I cell)
    {
        if (!IsInstanceValid(sprite))
        {
            return;
        }

        // Texture swap face A → face B au midpoint du flip.
        // - UseFaceBVoileCadastral=true  : legacy, face B = face A (voile cadastral).
        // - UseFaceBVoileCadastral=false : face B = patch côtier Mira correspondant
        //   à la cellule (gx,gy) ∈ [10..13]² mappé sur (N,M) ∈ [0..3]².
        Texture2D swapped;
        if (UseFaceBVoileCadastral)
        {
            swapped = _tileTexture;
        }
        else
        {
            var key = GetFaceBKey(cell);
            if (_faceBTextures.TryGetValue(key, out var faceBTex) && faceBTex is not null)
            {
                swapped = faceBTex;
            }
            else
            {
                // Pas de face-B chargée pour cette cellule -- on retombe sur la
                // voile cadastrale plutôt que de planter le flip. Le preflight
                // 15/17 aurait déjà signalé l'erreur au boot.
                GD.PushWarning($"[PROBE IsoMapE1Probe] OnHalfFlipReached: no face-B texture for cell {cell} (key={key}) — falling back to tile_neutral");
                swapped = _tileTexture;
            }
        }

        sprite.Texture = swapped;

        if (cell == new Vector2I(CenterIndex, CenterIndex))
        {
            GD.Print($"[PROBE IsoMapE1Probe] half-flip reached on center cell {cell} — texture swap A→B applied (face-B mode={(UseFaceBVoileCadastral ? "voile_cadastral_legacy" : "halfgate_patches_mira")})");
        }
    }

    // ------------------------------------------------------------------------
    // Face-B Halfgate textures (16 patches) — 2026-05-13
    // ------------------------------------------------------------------------

    /// <summary>
    /// Calcule la clé <c>(N, M)</c> pour la cellule sim-grid donnée, en tenant
    /// compte du flag <see cref="InvertFaceBMAxis"/>. Renvoyée comme
    /// <c>Vector2I(N, M)</c> pour la lookup dans <c>_faceBTextures</c>.
    /// <para>
    /// Mapping (par défaut, InvertFaceBMAxis=false) :
    /// <code>
    /// N = gx - RevealMinGx       (0..3, 0=west)
    /// M = gy - RevealMinGy       (0..3, 0=north)
    /// </code>
    /// Si InvertFaceBMAxis=true (validation visuelle inversée) :
    /// <code>
    /// M = RevealMaxGy - gy       (0..3, 0=south)
    /// </code>
    /// </para>
    /// </summary>
    private static Vector2I GetFaceBKey(Vector2I cell)
    {
        int n = cell.X - RevealMinGx;
        int m = InvertFaceBMAxis
            ? (RevealMaxGy - cell.Y)
            : (cell.Y - RevealMinGy);
        return new Vector2I(n, m);
    }

    /// <summary>
    /// Charge les 16 textures face-B Halfgate via <see cref="AssetLoader"/>.
    /// Le mapping (N,M) → cellule sim-grid (gx,gy) est :
    /// <code>
    /// gx = RevealMinGx + N
    /// gy = (InvertFaceBMAxis ? RevealMaxGy - M : RevealMinGy + M)
    /// </code>
    /// Stocke chaque texture indexée par <c>Vector2I(N, M)</c> (pas (gx, gy)) :
    /// la lookup au moment du flip passe par <see cref="GetFaceBKey"/> qui
    /// applique l'inversion d'axe si configurée.
    /// </summary>
    private void LoadFaceBTextures()
    {
        _faceBTextures.Clear();

        int loaded = 0;
        int missing = 0;
        int placeholderFallback = 0;

        for (int n = 0; n < 4; n++)
        {
            for (int m = 0; m < 4; m++)
            {
                string assetName = $"halfgate_face_b_{n}{m}.png";
                var tex = AssetLoader.LoadAssetOrPlaceholder(FaceBAssetDir, assetName);

                if (tex is null)
                {
                    missing++;
                    GD.PushError($"[FACE_B LOAD] {assetName} -> MISSING in {FaceBAssetDir}");
                    continue;
                }

                // Détection placeholder fallback : AssetLoader log déjà un warning,
                // mais on veut un compte ici pour le preflight 15/17.
                // Heuristique : le type est CompressedTexture2D dans les deux cas ;
                // on ne peut pas distinguer trivialement -- on s'appuie sur la
                // pré-condition "les 16 fichiers existent" validée par le sidecar.
                // Le test "aucune fallback" est donc fait dans le preflight via
                // FileAccess.FileExists sur chaque chemin final.

                int gx = RevealMinGx + n;
                int gy = InvertFaceBMAxis ? (RevealMaxGy - m) : (RevealMinGy + m);
                _faceBTextures[new Vector2I(n, m)] = tex;
                loaded++;
                GD.Print($"[FACE_B LOAD] {assetName} -> wayfinders/{FaceBAssetSubdir}/{assetName} (N={n},M={m}) → grid ({gx},{gy})");
            }
        }

        GD.Print($"[FACE_B LOAD] summary: loaded={loaded}/16 missing={missing} ; mapping={(InvertFaceBMAxis ? "INVERTED (M=3-row)" : "IDENTITY v3 (N,M->gx,gy : gx=10+N, gy=10+M)")} ; invert_M_axis={InvertFaceBMAxis} ; override_voile_cadastral={UseFaceBVoileCadastral}");
        _ = placeholderFallback; // réservé pour audit futur si besoin de marquer le naming `_PLACEHOLDER`.
    }

    // ------------------------------------------------------------------------
    // Texture loading — tuile cadastrale
    // ------------------------------------------------------------------------

    private Texture2D LoadPrimaryTile()
    {
        if (UseProceduralPlaceholder)
        {
            GD.Print("[PROBE IsoMapE1Probe] LoadPrimaryTile: UseProceduralPlaceholder=true → bypass framework Mira, génération procédurale C# (A/B debug)");
            return BuildPlaceholderTexture(TileTextureWidthPx, TileTextureHeightPx);
        }

        var tex = AssetLoader.LoadAssetOrPlaceholder(E1AssetDir, TileAssetName);
        if (tex is null)
        {
            GD.PushError($"[PROBE IsoMapE1Probe] LoadPrimaryTile: AssetLoader returned null for {TileAssetName} — falling back to procedural C# texture");
            return BuildPlaceholderTexture(TileTextureWidthPx, TileTextureHeightPx);
        }
        return tex;
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

    // ------------------------------------------------------------------------
    // Preflight canary — 15 lignes pour e1 D + face-B Halfgate (2026-05-13)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Preflight canary : 17 lignes <c>[PROBE IsoMapE1Probe]</c>. Etend les
    /// 15 lignes e1 D (post-bugfix diamond + face-B) avec 2 lignes 2026-05-13 pm :
    /// 16/17 = bascule approche unifiee (ShowPoiSeparateSprite=false : Sprite2D
    /// POI invisible mais hitbox losange preservee, la ville est dessinee dans
    /// les 16 patches face-B Mira) ; 17/17 = machinerie cursor shape
    /// (Arrow <-> PointingHand) cablee pour le feedback de cliquabilite.
    /// </summary>
    private void RunPreflight()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;

        // 1. Sprite count.
        int tileCount = _tileGrid.GetChildCount();
        int expectedCount = DisplaySingleTileOnly ? 1 : GridSize * GridSize + 1;
        bool countOk = tileCount == expectedCount;
        GD.Print($"[PROBE IsoMapE1Probe] 1/17 TileGrid.children = {tileCount} (expected {expectedCount} = {GridSize}×{GridSize} tiles + 1 POI) -- {(countOk ? "OK" : "FAIL")}");

        // 2. First sprite position invariant.
        var firstTile = _tileGrid.GetChild<Sprite2D>(0);
        Vector2 firstPos = firstTile?.Position ?? Vector2.One;
        bool posOk = firstTile is not null
            && Mathf.IsEqualApprox(firstPos.X, 0f)
            && Mathf.IsEqualApprox(firstPos.Y, 0f);
        GD.Print($"[PROBE IsoMapE1Probe] 2/17 Tile[0].Position = {firstPos.ToString("F2")} (expected (0, 0)) -- {(posOk ? "OK" : "FAIL")}");

        // 3. Camera2D current + zoom inspection.
        bool camOk = _camera.IsCurrent()
            && Mathf.IsEqualApprox(_camera.Zoom.X, ZoomInspect)
            && Mathf.IsEqualApprox(_camera.Zoom.Y, ZoomInspect)
            && _camera.Position.IsEqualApprox(Vector2.Zero);
        GD.Print($"[PROBE IsoMapE1Probe] 3/17 Camera2D current={_camera.IsCurrent()} zoom={_camera.Zoom.ToString("F2")} pos={_camera.Position.ToString("F2")} (expected current=true zoom=({ZoomInspect:F2},{ZoomInspect:F2}) pos=(0,0)) -- {(camOk ? "OK" : "FAIL")}");

        // 4. Texture dimensions v5 (256×130) + source type.
        var tex = firstTile?.Texture;
        int texW = tex?.GetWidth() ?? 0;
        int texH = tex?.GetHeight() ?? 0;
        string texTypeName = tex?.GetType().Name ?? "null";
        bool texDimsOk = texW == TileTextureWidthPx && texH == TileTextureHeightPx;
        bool texSourceOk = UseProceduralPlaceholder
            ? texTypeName == "ImageTexture"
            : texTypeName == "CompressedTexture2D";
        bool texOk = texDimsOk && texSourceOk;
        string expectedType = UseProceduralPlaceholder
            ? "ImageTexture (procedural bypass)"
            : "CompressedTexture2D (PNG via AssetLoader)";
        GD.Print($"[PROBE IsoMapE1Probe] 4/17 Tile[0].Texture = {texW}x{texH} type={texTypeName} (expected {TileTextureWidthPx}x{TileTextureHeightPx} v5 {expectedType}) -- {(texOk ? "OK" : "FAIL")}");

        // 5. TextureFilter Nearest + Sprite2D Offset cohérent.
        float expectedOffsetY = SpriteOffsetY;
        float actualOffsetY = firstTile?.Offset.Y ?? float.NaN;
        bool filterOk = firstTile is not null
            && firstTile.TextureFilter == CanvasItem.TextureFilterEnum.Nearest;
        bool offsetOk = firstTile is not null
            && Mathf.IsEqualApprox(actualOffsetY, expectedOffsetY);
        bool filterOffsetOk = filterOk && offsetOk;
        GD.Print($"[PROBE IsoMapE1Probe] 5/17 Tile[0].TextureFilter={(firstTile?.TextureFilter.ToString() ?? "null")} (expected Nearest) ; Tile[0].Offset.Y={actualOffsetY:F2} (expected {expectedOffsetY:F2}) -- {(filterOffsetOk ? "OK" : "FAIL")}");

        // 6. Background magenta + mouse_filter=Ignore + ShadowRoot3D vide + stride iso v5 sanity.
        //    Le mouse_filter=Ignore est critique : avec mouse_filter=Stop par défaut,
        //    le BackgroundRect (qui couvre tout le viewport) intercepterait
        //    tous les inputs souris et masquerait le hit-test manuel du POI.
        var bgLayer = GetNodeOrNull<CanvasLayer>("BackgroundLayer");
        var bgRect = bgLayer?.GetNodeOrNull<ColorRect>("BackgroundRect");
        Color bgColor = bgRect?.Color ?? Colors.Black;
        bool bgColorOk = bgRect is not null
            && Mathf.IsEqualApprox(bgColor.R, ExpectedClearColor.R)
            && Mathf.IsEqualApprox(bgColor.G, ExpectedClearColor.G)
            && Mathf.IsEqualApprox(bgColor.B, ExpectedClearColor.B);
        bool bgMouseFilterOk = bgRect is not null
            && bgRect.MouseFilter == Control.MouseFilterEnum.Ignore;
        int shadowCount = _shadowRoot3D.GetChildCount();
        bool shadowEmpty = shadowCount == 0;
        bool strideOk = Mathf.IsEqualApprox(IsoWStride, 128f) && Mathf.IsEqualApprox(IsoHStride, 64f);
        bool bgShadowOk = bgColorOk && bgMouseFilterOk && shadowEmpty && strideOk;
        GD.Print($"[PROBE IsoMapE1Probe] 6/17 BG=({bgColor.R:F2},{bgColor.G:F2},{bgColor.B:F2}) expected magenta ; BG mouse_filter={(bgRect?.MouseFilter.ToString() ?? "null")} (expected Ignore) ; ShadowRoot3D.children={shadowCount} expected 0 ; iso stride=(w={IsoWStride:F0}, h={IsoHStride:F0}) expected (128, 64) -- {(bgShadowOk ? "OK" : "FAIL")}");

        // 7. Reveal sprites identifiés.
        bool revealCountOk = DisplaySingleTileOnly || _revealSprites.Count == RevealTileCount;
        bool revealCellsOk = true;
        bool revealSpritesAliveOk = true;
        if (!DisplaySingleTileOnly)
        {
            for (int gx = RevealMinGx; gx <= RevealMaxGx; gx++)
            {
                for (int gy = RevealMinGy; gy <= RevealMaxGy; gy++)
                {
                    var key = new Vector2I(gx, gy);
                    if (!_revealSprites.TryGetValue(key, out var s))
                    {
                        revealCellsOk = false;
                    }
                    else if (!IsInstanceValid(s))
                    {
                        revealSpritesAliveOk = false;
                    }
                }
            }
        }
        bool revealIdentOk = revealCountOk && revealCellsOk && revealSpritesAliveOk;
        GD.Print($"[PROBE IsoMapE1Probe] 7/17 RevealSprites.count = {_revealSprites.Count} (expected {(DisplaySingleTileOnly ? 0 : RevealTileCount)}) ; cells_present={revealCellsOk} sprites_alive={revealSpritesAliveOk} ; emprise=(gx,gy)∈[{RevealMinGx},{RevealMaxGx}]×[{RevealMinGy},{RevealMaxGy}] around Halfgate ({CenterIndex},{CenterIndex}) -- {(revealIdentOk ? "OK" : "FAIL")}");

        // 8. Timings reveal cohérents spec Varn.
        double expectedTotal = 0.05 * (RevealTileCount - 1) + 1.2;
        bool timingsOk = RevealTileCount == 16
            && Mathf.IsEqualApprox((float)RevealPerCardDurationSec, 1.2f)
            && Mathf.IsEqualApprox((float)RevealStaggerStepSec, 0.05f)
            && Mathf.IsEqualApprox((float)RevealTotalDurationSec, (float)expectedTotal)
            && Mathf.IsEqualApprox((float)RevealTriggerDelaySec, 3.0f);
        GD.Print($"[PROBE IsoMapE1Probe] 8/17 Reveal timings: perCard={RevealPerCardDurationSec:F2}s stagger={RevealStaggerStepSec:F2}s total={RevealTotalDurationSec:F2}s trigger_delay={RevealTriggerDelaySec:F2}s (expected 1.20/0.05/1.95/3.00 per Varn spec §5) ; zoom_blocked_during_reveal={true} (cinematic mode locked 2026-05-12) -- {(timingsOk ? "OK" : "FAIL")}");

        // 9. POI Halfgate placement.
        float imprintCenterGx = (RevealMinGx + RevealMaxGx) * 0.5f;
        float imprintCenterGy = (RevealMinGy + RevealMaxGy) * 0.5f;
        Vector2 expectedPoiPos = GridToScreen(imprintCenterGx, imprintCenterGy);
        bool poiExists = _halfgatePoi is not null && IsInstanceValid(_halfgatePoi);
        bool poiParentOk = poiExists && _halfgatePoi!.GetParent() == _tileGrid;
        bool poiPosOk = poiExists
            && Mathf.IsEqualApprox(_halfgatePoi!.Position.X, expectedPoiPos.X)
            && Mathf.IsEqualApprox(_halfgatePoi.Position.Y, expectedPoiPos.Y);
        bool poiZOk = poiExists
            && _halfgatePoi!.ZIndex == PoiZIndex
            && _halfgatePoi.ZAsRelative == false;
        bool poiCenteredOk = poiExists && _halfgatePoi!.Centered;

        bool poiCoverageOk = false;
        string coverageDetail = "n/a";
        if (poiExists)
        {
            Vector2 c = _halfgatePoi!.Position;
            float halfW = PoiTextureWidthPx * 0.5f;
            float halfH = PoiTextureHeightPx * 0.5f;

            Vector2 cornerTopOuter    = GridToScreen(RevealMinGx, RevealMinGy) + new Vector2(0f, -IsoHStride);
            Vector2 cornerRightOuter  = GridToScreen(RevealMaxGx, RevealMinGy) + new Vector2(IsoWStride, 0f);
            Vector2 cornerBottomOuter = GridToScreen(RevealMaxGx, RevealMaxGy) + new Vector2(0f, IsoHStride);
            Vector2 cornerLeftOuter   = GridToScreen(RevealMinGx, RevealMaxGy) + new Vector2(-IsoWStride, 0f);

            float dTop    = Mathf.Abs(cornerTopOuter.X    - c.X) / halfW + Mathf.Abs(cornerTopOuter.Y    - c.Y) / halfH;
            float dRight  = Mathf.Abs(cornerRightOuter.X  - c.X) / halfW + Mathf.Abs(cornerRightOuter.Y  - c.Y) / halfH;
            float dBottom = Mathf.Abs(cornerBottomOuter.X - c.X) / halfW + Mathf.Abs(cornerBottomOuter.Y - c.Y) / halfH;
            float dLeft   = Mathf.Abs(cornerLeftOuter.X   - c.X) / halfW + Mathf.Abs(cornerLeftOuter.Y   - c.Y) / halfH;

            const float eps = 1e-3f;
            bool topIn    = dTop    <= 1f + eps;
            bool rightIn  = dRight  <= 1f + eps;
            bool bottomIn = dBottom <= 1f + eps;
            bool leftIn   = dLeft   <= 1f + eps;
            poiCoverageOk = topIn && rightIn && bottomIn && leftIn;
            coverageDetail = $"top_d={dTop:F3} right_d={dRight:F3} bottom_d={dBottom:F3} left_d={dLeft:F3} (all expected ≤ 1.000)";
        }

        bool poiPlacementOk = poiExists && poiParentOk && poiPosOk && poiZOk && poiCenteredOk && poiCoverageOk;
        string poiPosStr = poiExists ? _halfgatePoi!.Position.ToString("F2") : "n/a";
        int poiZIdxActual = poiExists ? _halfgatePoi!.ZIndex : -999;
        bool poiZAbsActual = poiExists && _halfgatePoi!.ZAsRelative == false;
        GD.Print($"[PROBE IsoMapE1Probe] 9/17 PoiHalfgate: exists={poiExists} parent_is_TileGrid={poiParentOk} pos={poiPosStr} (expected ({expectedPoiPos.X:F2},{expectedPoiPos.Y:F2}) = geometric center of 4×4 imprint at grid ({imprintCenterGx:F1},{imprintCenterGy:F1})) ZIndex={poiZIdxActual} z_absolute={poiZAbsActual} (expected ZIndex={PoiZIndex} absolute) centered={(poiExists ? _halfgatePoi!.Centered : false)} ; coverage 4 corners {coverageDetail} -- {(poiPlacementOk ? "OK" : "FAIL")}");

        // 10. POI texture : dimensions + source + alpha init + fade duration.
        var poiTex = poiExists ? _halfgatePoi!.Texture : null;
        int poiTexW = poiTex?.GetWidth() ?? 0;
        int poiTexH = poiTex?.GetHeight() ?? 0;
        string poiTexTypeName = poiTex?.GetType().Name ?? "null";
        bool poiTexDimsOk = poiTexW == PoiTextureWidthPx && poiTexH == PoiTextureHeightPx;
        bool poiTexSourceOk = poiTexTypeName == "CompressedTexture2D";
        float poiInitialAlpha = poiExists ? _halfgatePoi!.Modulate.A : -1f;
        bool poiAlphaInitOk = poiExists && Mathf.IsEqualApprox(poiInitialAlpha, 0f);
        bool poiFadeDurOk = Mathf.IsEqualApprox((float)PoiFadeInDurationSec, 0.6f);
        bool poiTextureOk = poiTexDimsOk && poiTexSourceOk && poiAlphaInitOk && poiFadeDurOk;
        GD.Print($"[PROBE IsoMapE1Probe] 10/17 PoiTexture: {poiTexW}x{poiTexH} type={poiTexTypeName} (expected {PoiTextureWidthPx}x{PoiTextureHeightPx} RGBA, CompressedTexture2D via AssetLoader) ; initial Modulate.A={poiInitialAlpha:F2} (expected 0.00 invisible pre-fade) ; fade_in_duration={PoiFadeInDurationSec:F2}s (expected 0.60s) -- {(poiTextureOk ? "OK" : "FAIL")}");

        // 11. Hit-test manuel POI : flag _poiHoverEnabled=false au boot, sera
        //     mis à true à la fin de la cinématique (t≈5.55s). Vérifie aussi
        //     la géométrie du losange iso (4 sommets monde Top/Right/Bottom/Left)
        //     -- bugfix 2026-05-13 : on teste point-in-diamond, plus le rect
        //     1024×512 (qui allumait le tooltip dans les coins transparents).
        bool hoverDisabledAtBoot = _poiHoverEnabled == false;
        Vector2 poiCenter = poiExists ? _halfgatePoi!.GlobalPosition : Vector2.Zero;
        var (vTop, vRight, vBottom, vLeft) = poiExists
            ? GetPoiDiamondVertices()
            : (Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero);
        bool vTopOk = poiExists
            && Mathf.IsEqualApprox(vTop.X, poiCenter.X)
            && Mathf.IsEqualApprox(vTop.Y, poiCenter.Y - PoiDiamondHalfHeightPx);
        bool vRightOk = poiExists
            && Mathf.IsEqualApprox(vRight.X, poiCenter.X + PoiDiamondHalfWidthPx)
            && Mathf.IsEqualApprox(vRight.Y, poiCenter.Y);
        bool vBottomOk = poiExists
            && Mathf.IsEqualApprox(vBottom.X, poiCenter.X)
            && Mathf.IsEqualApprox(vBottom.Y, poiCenter.Y + PoiDiamondHalfHeightPx);
        bool vLeftOk = poiExists
            && Mathf.IsEqualApprox(vLeft.X, poiCenter.X - PoiDiamondHalfWidthPx)
            && Mathf.IsEqualApprox(vLeft.Y, poiCenter.Y);
        // Sanity self-test du prédicat : le centre est dedans, un coin du
        // bounding-box rectangulaire (le piège originel) est dehors.
        bool centerInside = poiExists && IsPointInPoiDiamond(poiCenter);
        Vector2 bboxCorner = poiCenter + new Vector2(PoiDiamondHalfWidthPx, PoiDiamondHalfHeightPx);
        bool bboxCornerOutside = poiExists && !IsPointInPoiDiamond(bboxCorner);
        bool hitTestOk = hoverDisabledAtBoot
            && vTopOk && vRightOk && vBottomOk && vLeftOk
            && centerInside && bboxCornerOutside;
        GD.Print($"[PROBE IsoMapE1Probe] 11/17 Hit-test manuel POI (diamond): _poiHoverEnabled={_poiHoverEnabled} (expected false at boot, true at t≈{RevealTriggerDelaySec + RevealTotalDurationSec + PoiFadeInDurationSec:F2}s) ; vertices Top=({vTop.X:F0},{vTop.Y:F0}) Right=({vRight.X:F0},{vRight.Y:F0}) Bottom=({vBottom.X:F0},{vBottom.Y:F0}) Left=({vLeft.X:F0},{vLeft.Y:F0}) (expected center=({poiCenter.X:F0},{poiCenter.Y:F0}) ± hw={PoiDiamondHalfWidthPx:F0} hh={PoiDiamondHalfHeightPx:F0}) ; predicate self-test center_inside={centerInside} bbox_corner_outside={bboxCornerOutside} (expected true/true) -- {(hitTestOk ? "OK" : "FAIL")}");

        // 12. Tooltip root nodes : Control + parchment + seal + 3 Labels.
        bool tooltipRootOk = _tooltipRoot is not null && IsInstanceValid(_tooltipRoot)
            && _tooltipRoot.GetParent() == _tooltipLayer
            && _tooltipRoot.Visible == false
            && Mathf.IsEqualApprox(_tooltipRoot.Modulate.A, 0f);
        bool parchmentNodeOk = _tooltipParchment is not null && IsInstanceValid(_tooltipParchment)
            && _tooltipParchment.GetParent() == _tooltipRoot;
        bool sealNodeOk = _tooltipSeal is not null && IsInstanceValid(_tooltipSeal)
            && _tooltipSeal.GetParent() == _tooltipRoot;
        bool titleNodeOk = _tooltipTitleLabel is not null && IsInstanceValid(_tooltipTitleLabel)
            && _tooltipTitleLabel.Text == TooltipTitleText;
        bool subtitleNodeOk = _tooltipSubtitleLabel is not null && IsInstanceValid(_tooltipSubtitleLabel)
            && _tooltipSubtitleLabel.Text == TooltipSubtitleText;
        bool bodyNodeOk = _tooltipBodyLabel is not null && IsInstanceValid(_tooltipBodyLabel)
            && _tooltipBodyLabel.Text == TooltipBodyText;
        bool tooltipNodesOk = tooltipRootOk && parchmentNodeOk && sealNodeOk && titleNodeOk && subtitleNodeOk && bodyNodeOk;
        GD.Print($"[PROBE IsoMapE1Probe] 12/17 TooltipRoot: root_ok={tooltipRootOk} parchment={parchmentNodeOk} seal={sealNodeOk} title='{(_tooltipTitleLabel?.Text ?? "")}' subtitle='{(_tooltipSubtitleLabel?.Text ?? "")}' (expected hidden at boot under TooltipLayer with all 5 children) ; hover_delay={TooltipHoverDelaySec:F3}s fade_in={TooltipFadeInDurationSec:F3}s (expected 0.250/0.150 per Varn spec §6) -- {(tooltipNodesOk ? "OK" : "FAIL")}");

        // 13. Tooltip textures via AssetLoader (parchment 320×180 + seal 64×64).
        var parchTex = _tooltipParchment?.Texture;
        int parchW = parchTex?.GetWidth() ?? 0;
        int parchH = parchTex?.GetHeight() ?? 0;
        string parchTypeName = parchTex?.GetType().Name ?? "null";
        bool parchDimsOk = parchW == TooltipParchmentWidthPx && parchH == TooltipParchmentHeightPx;
        bool parchSourceOk = parchTypeName == "CompressedTexture2D";

        var sealTex = _tooltipSeal?.Texture;
        int sealW = sealTex?.GetWidth() ?? 0;
        int sealH = sealTex?.GetHeight() ?? 0;
        string sealTypeName = sealTex?.GetType().Name ?? "null";
        bool sealDimsOk = sealW == SealWaxWidthPx && sealH == SealWaxHeightPx;
        bool sealSourceOk = sealTypeName == "CompressedTexture2D";

        bool tooltipTexturesOk = parchDimsOk && parchSourceOk && sealDimsOk && sealSourceOk;
        GD.Print($"[PROBE IsoMapE1Probe] 13/17 Tooltip textures: parchment={parchW}x{parchH} type={parchTypeName} (expected {TooltipParchmentWidthPx}x{TooltipParchmentHeightPx} CompressedTexture2D via AssetLoader) ; seal={sealW}x{sealH} type={sealTypeName} (expected {SealWaxWidthPx}x{SealWaxHeightPx} CompressedTexture2D via AssetLoader) -- {(tooltipTexturesOk ? "OK" : "FAIL")}");

        // 14. E2Stub.tscn existence sur disque + crossfade rect prêt.
        bool e2StubExists = FileAccess.FileExists(E2StubScenePath);
        bool crossfadeRectOk = _crossfadeRect is not null && IsInstanceValid(_crossfadeRect)
            && Mathf.IsEqualApprox(_crossfadeRect.Color.A, 0f);
        bool crossfadeMouseFilterOk = _crossfadeRect is not null
            && _crossfadeRect.MouseFilter == Control.MouseFilterEnum.Ignore;
        bool e2AndCrossfadeOk = e2StubExists && crossfadeRectOk && crossfadeMouseFilterOk;
        GD.Print($"[PROBE IsoMapE1Probe] 14/17 Transition target: e2_stub_scene='{E2StubScenePath}' exists={e2StubExists} ; CrossfadeRect ready={crossfadeRectOk} alpha_init={(_crossfadeRect?.Color.A ?? -1f):F2} mouse_filter={(_crossfadeRect?.MouseFilter.ToString() ?? "null")} (expected file_exists=true, alpha=0.00, mouse_filter=Ignore) ; click sequence = zoom 1.00s → crossfade 0.40s → ChangeSceneToFile -- {(e2AndCrossfadeOk ? "OK" : "FAIL")}");

        // 15. Face-B Halfgate : 16 textures chargées, dimensions 256×128,
        //     aucune fallback sur _PLACEHOLDER (les 16 PNGs Mira existent
        //     bien dans res://assets/.../halfgate_face_b/). Test sauté si
        //     mode SINGLE_TILE ou si UseFaceBVoileCadastral=true (override).
        bool faceBExpected = !DisplaySingleTileOnly && !UseFaceBVoileCadastral;
        bool faceBCountOk = !faceBExpected || _faceBTextures.Count == 16;
        bool faceBDimsOk = true;
        bool faceBNoPlaceholderFallbackOk = true;
        int faceBFinalCount = 0;
        int faceBPlaceholderCount = 0;
        if (faceBExpected)
        {
            for (int n = 0; n < 4; n++)
            {
                for (int m = 0; m < 4; m++)
                {
                    string assetName = $"halfgate_face_b_{n}{m}.png";
                    string finalPath = $"{FaceBAssetDir}/{assetName}";
                    if (FileAccess.FileExists(finalPath))
                    {
                        faceBFinalCount++;
                    }
                    else
                    {
                        faceBNoPlaceholderFallbackOk = false;
                        string placeholderPath = $"{FaceBAssetDir}/halfgate_face_b_{n}{m}_PLACEHOLDER.png";
                        if (FileAccess.FileExists(placeholderPath))
                        {
                            faceBPlaceholderCount++;
                        }
                    }

                    if (_faceBTextures.TryGetValue(new Vector2I(n, m), out var fbTex)
                        && fbTex is not null)
                    {
                        if (fbTex.GetWidth() != FaceBTextureWidthPx
                            || fbTex.GetHeight() != FaceBTextureHeightPx)
                        {
                            faceBDimsOk = false;
                        }
                    }
                }
            }
        }
        bool faceBOk = faceBCountOk && faceBDimsOk && faceBNoPlaceholderFallbackOk;
        string faceBSkipReason = !faceBExpected
            ? (DisplaySingleTileOnly ? "skipped (SINGLE_TILE mode)" : "skipped (UseFaceBVoileCadastral=true override)")
            : "active";
        GD.Print($"[PROBE IsoMapE1Probe] 15/17 Face-B Halfgate: status={faceBSkipReason} ; loaded={_faceBTextures.Count}/16 final_pngs_on_disk={faceBFinalCount}/16 placeholder_fallbacks={faceBPlaceholderCount}/16 (expected 16/16 final, 0/16 placeholder) ; dims_all_256x128={faceBDimsOk} ; mapping={(InvertFaceBMAxis ? "INVERTED" : "IDENTITY v3")} (gx,gy)→(N=gx-{RevealMinGx},M={(InvertFaceBMAxis ? $"{RevealMaxGy}-gy" : $"gy-{RevealMinGy}")}) invert_M_axis={InvertFaceBMAxis} -- {(faceBOk ? "OK" : "FAIL")}");

        // 16. POI sprite hidden + hitbox preserved (approche unifiee).
        //     En mode unifie (ShowPoiSeparateSprite=false, defaut 2026-05-13 pm) :
        //     le Sprite2D POI doit avoir Modulate.A=0 au boot ET apres la
        //     cinematique (jamais fade-in), Visible doit rester true pour
        //     ne pas casser la sceance d'edition, et sa Position + GetRect
        //     doivent etre intacts pour alimenter le hit-test losange.
        //     En mode legacy (ShowPoiSeparateSprite=true) : Modulate.A=0
        //     au boot (preflight pre-cinematique), passera a 1.0 apres fade-in.
        bool unifiedMode = !ShowPoiSeparateSprite;
        bool poiSpriteHiddenOk;
        bool poiSpriteVisibleFlagOk;
        bool poiSpriteHitboxOk;
        if (_halfgatePoi is not null && IsInstanceValid(_halfgatePoi))
        {
            poiSpriteHiddenOk = Mathf.IsEqualApprox(_halfgatePoi.Modulate.A, 0f);
            poiSpriteVisibleFlagOk = _halfgatePoi.Visible == true;
            // Hitbox preservee = position non-zero (centre de l'imprint 4x4)
            // ET texture toujours assignee (sinon GetRect renverrait du vide).
            poiSpriteHitboxOk = _halfgatePoi.Texture is not null
                && !_halfgatePoi.Position.IsEqualApprox(Vector2.Zero);
        }
        else
        {
            poiSpriteHiddenOk = false;
            poiSpriteVisibleFlagOk = false;
            poiSpriteHitboxOk = false;
        }
        bool unifiedOk = poiSpriteHiddenOk && poiSpriteVisibleFlagOk && poiSpriteHitboxOk;
        GD.Print($"[PROBE IsoMapE1Probe] 16/17 Unified Halfgate (ShowPoiSeparateSprite={ShowPoiSeparateSprite}): mode={(unifiedMode ? "UNIFIED (city embedded in 16 face-B patches)" : "LEGACY (separate POI sprite, fade-in)")} ; PoiSprite.Modulate.A={(_halfgatePoi?.Modulate.A ?? -1f):F2} (expected 0.00 at boot, kept 0.00 post-cinematic in unified mode) ; PoiSprite.Visible={(_halfgatePoi?.Visible ?? false)} (expected true -- hitbox preserved) ; hitbox_preserved (texture+pos)={poiSpriteHitboxOk} -- {(unifiedOk ? "OK" : "FAIL")}");

        // 17. Cursor shape machinery cablee.
        //     Au boot : Arrow, jamais touche PointingHand encore.
        //     En cours de session : la transition Arrow <-> PointingHand
        //     est faite par SetCursorShape(...) ; _cursorShapeChangedOnce
        //     passe true des qu'on a applique PointingHand au moins une
        //     fois (= preuve que le hover est entre dans le losange).
        //     Au boot le test verifie juste que la machinerie est en place.
        bool cursorBootShapeOk = _currentCursorShape == Input.CursorShape.Arrow;
        bool cursorNotYetChangedOk = _cursorShapeChangedOnce == false;
        bool cursorMachineryOk = cursorBootShapeOk && cursorNotYetChangedOk;
        GD.Print($"[PROBE IsoMapE1Probe] 17/17 Cursor shape machinery: _currentCursorShape={_currentCursorShape} (expected Arrow at boot) ; _cursorShapeChangedOnce={_cursorShapeChangedOnce} (expected false at boot, will flip true on first hover enter into losange) ; transition wired via SetCursorShape(...) in OnPoiHoverEnter/OnPoiHoverExit -- {(cursorMachineryOk ? "OK" : "FAIL")}");

        // Info diag.
        GD.Print($"[PROBE IsoMapE1Probe] info viewport={viewportSize.X:F0}x{viewportSize.Y:F0} ; zoom={_currentZoom:F2}× (wheel up/down, bounds {ZoomMin:F2}×—{ZoomMax:F2}×) ; mode={(DisplaySingleTileOnly ? "SINGLE_TILE" : "FULL_GRID_24x24")} ; v5 dims=256×130 ; flip approach=Scale.Y 2D simulation ; framework=Mira placeholder v1.0 (UseProceduralPlaceholder={UseProceduralPlaceholder}) ; e1 D cinematic timeline = boot t=0 → flip starts t=3.00s → flip ends t≈{RevealTriggerDelaySec + RevealTotalDurationSec:F2}s → POI fade-in ends t≈{RevealTriggerDelaySec + RevealTotalDurationSec + PoiFadeInDurationSec:F2}s → hover/click POI enabled (manual hit-test in _Process/_Input)");
    }
}
