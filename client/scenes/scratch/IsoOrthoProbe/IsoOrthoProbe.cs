using Godot;

namespace Wayfinders.Client.Scenes.Scratch.IsoOrthoProbe;

/// <summary>
/// POC IsoOrthoProbe — étape 4a du pivot iso-orthographique.
/// <para>
/// L'étape 3 a validé la projection iso 2:1 d'<i>une</i> tile (ratio
/// 2.0000 pur, axes purs, entrée pka.db id 1531). L'étape 4a lève
/// l'inconnue suivante sur le chemin M1 : <b>quand on pose deux tiles
/// côte à côte, le raccord visuel est-il propre, et quel est le pas
/// monde correct entre deux cellules de grid ?</b>
/// </para>
///
/// <para>
/// <b>Pourquoi ce test avant raycast/caméra/perso</b> : si le raccord
/// casse à 2 tiles (overdraw, gap d'1 px en ortho, z-fighting au plan
/// Y=0), tout le reste du M1 — pathfinding visualisé, hover, placement
/// de perso co-localisé, fog — est moot. Le raycast et le pan/zoom sont
/// de l'ergonomie ; le multi-tile est de l'inconnu graphique. On lève
/// l'inconnu graphique en premier.
/// </para>
///
/// <para>
/// <b>Le tile_edge_template.png</b> a été livré spécifiquement pour ce
/// test (bord N=ocre, S=bleu mer, E=vert bocage, W=gris falaise, 4
/// corner-brackets terracotta, centre transparent + légende
/// "EDGE TEMPLATE - verify color continuity with neighbor tile"). C'est
/// l'outil de diagnostic : on le pose en voisin de la tile terrain
/// halfgate, on regarde si les bords s'alignent pixel-parfait au
/// raccord. Si une bande de bord est coupée ou si on voit du blanc, le
/// pas monde <c>TileStepWorld</c> est faux.
/// </para>
///
/// <para>
/// <b>Choix du pas monde <c>TileStepWorld = 21.76</c></b> : la tile MJ
/// est 2176×1152 px avec <c>pixel_size=0.01</c> → 21.76 × 11.52 unités
/// monde. <b>Hypothèse de travail</b> : l'image entière représente UNE
/// cellule de grid (pas de marge artistique débordante). Donc deux
/// tiles voisines sur l'axe +X sont séparées de 21.76 unités. Si le
/// visuel montre un gap ou un chevauchement, on ajustera la constante
/// — c'est ce que la ligne preflight 11 vérifie en dump explicite.
/// On <b>ne</b> hardcode <b>pas</b> cette valeur ailleurs : elle vit
/// dans une seule const C#.
/// </para>
///
/// <para>
/// <b>Choix de l'axe : voisin sur +X monde</b>. Avec notre convention
/// iso (yaw=45° + pitch=-30°), un déplacement <c>+X</c> monde projette
/// en <i>droite-bas</i> écran ; un déplacement <c>-Z</c> projette en
/// <i>droite-haut</i>. On nomme la 2e tile <c>TileSprite3D_PlusX</c>
/// — explicite, sans pré-supposer "Nord/Sud" (ça vit dans Varn-land,
/// pas ici). Au visuel, le tile_edge_template doit apparaître <b>en
/// bas-à-droite</b> de la tile halfgate, et leur côté E↔W (bord
/// droit halfgate × bord gauche edge_template) doit se toucher
/// proprement.
/// </para>
///
/// <para>
/// <b>Cadrage caméra — recentrée sur la couture</b>. Itération précédente :
/// caméra centrée sur l'origine (tile 1), la tile 2 à <c>(21.76, 0, 0)</c>
/// projetait à ~1858 px de l'origine écran — totalement hors viewport
/// 1920×1080 à <c>Size=10</c>. Visuellement on ne voyait qu'un coin de
/// la halfgate et zéro edge_template. Inutile pour diagnostiquer le seam.
/// </para>
/// <para>
/// On garde la <i>basis</i> caméra (yaw 45° / pitch -30° / Size 10 — ce
/// qui détermine le ratio iso) et on translate la <i>position</i> du
/// vecteur world <c>(SeamWorldX, 0, 0) = (10.88, 0, 0)</c> — le mi-chemin
/// entre tile 1 (origin) et tile 2 (TileStepWorld). En ortho, translater
/// la caméra parallèlement au plan image translate le contenu visible
/// d'autant ; donc le point monde <c>(10.88, 0, 0)</c> — la couture
/// elle-même — tombe au centre du viewport. Les deux tiles se partagent
/// l'écran symétriquement et le raccord est sous le réticule, là où un
/// gap d'1 px se voit.
/// </para>
/// <para>
/// Position résultante : <c>(6.123725 + 10.88, 5, 6.123725 + 0) =
/// (17.003725, 5, 6.123725)</c>. La basis est identique au tscn de
/// l'étape 3 (intacte au float7 près). C'est la translation qui change,
/// rien d'autre — pas de re-derivation iso.
/// </para>
///
/// <para>
/// <b>Z-order coplanaire</b> : deux Sprite3D à <c>Y=0</c>, plan XZ.
/// Godot trie par profondeur caméra. La tile à <c>(+TileStep, 0, 0)</c>
/// est plus loin de la caméra (caméra en <c>+X,+Z</c>) → dessinée
/// <i>en premier</i>, donc <i>sous</i>. En ortho coplanaire c'est en
/// général OK — pas de tweak <c>render_priority</c> pour l'instant.
/// On observe d'abord ce que la pipeline fait naturellement.
/// </para>
///
/// <para>
/// <b>Clear color magenta (anti-palette)</b>. Itération précédente :
/// background Godot par défaut = gris foncé, indissociable visuellement
/// de la bande W=falaise grise de l'edge_template. Impossible de
/// trancher gap pixel-parfait vs alignement réel sur screenshot.
/// On installe un <c>WorldEnvironment</c> avec
/// <c>background_mode = Color</c> et <c>background_color = #FF00FF</c>
/// (magenta saturé, absent de toute palette Wayfinders : pas d'ocre,
/// pas de bleu mer, pas de vert bocage, pas de falaise grise, pas de
/// terracotta). Toute couleur non-magenta visible à l'écran =
/// contenu réel d'une tile. La bande W=falaise grise devient lisible
/// contre le magenta, et un éventuel gap d'1 px entre tiles ressort
/// en magenta vif. Anti-palette pure — c'est l'inverse d'une
/// décision artistique, c'est un outil de diagnostic.
/// </para>
///
/// <para>
/// <b>Critère d'acceptance visuel</b>
/// <list type="bullet">
///   <item>Background magenta uniforme partout sauf sur les deux tiles.</item>
///   <item>Tile halfgate à <i>gauche</i> du viewport (origine monde,
///         translation caméra +X a déplacé son rendu vers la gauche).</item>
///   <item>Tile edge_template à <i>droite</i> du viewport (axe +X
///         monde), bords visibles (bandes ocre/mer/bocage/falaise +
///         corner-brackets terracotta). La bande W=falaise grise est
///         maintenant lisible (gris contre magenta, plus contre gris).</item>
///   <item>Raccord centré dans le viewport, sous le réticule — c'est
///         là qu'un gap magenta d'1 px se voit en premier.</item>
///   <item>Pas de magenta visible entre les deux tiles à la couture.
///         Pas de chevauchement où l'edge_template écraserait la halfgate.</item>
///   <item>Pas de z-fighting (clignotement / lignes scintillantes au
///         raccord) — les deux sprites sont coplanaires <c>Y=0</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Preflight enrichi</b> : 13 lignes <c>[PROBE IsoOrthoProbe]</c>.
/// Les 10 premières sont l'invariant de l'étape 3 (préservé — sauf check 3
/// qui valide la position translatée). Trois nouvelles :
/// <list type="bullet">
///   <item>11 : 2e tile présente, à <c>(TileStepWorld, 0, 0)</c>,
///         rotation plat, texture edge_template assignée.</item>
///   <item>12 : le point monde de la couture <c>(SeamWorldX, 0, 0)</c>
///         projette au centre du viewport (à ±2px du centre). C'est la
///         garantie "la caméra regarde bien le raccord", sans laquelle
///         un seam-bug d'1 px hors champ est indétectable.</item>
///   <item>13 : <c>WorldEnvironment.Environment.BackgroundMode = Color</c>
///         et <c>BackgroundColor = magenta (#FF00FF)</c>. Garantit que
///         le clear color saturé anti-palette est bien actif — sinon
///         on retombe en gris défaut et le diagnostic visuel est moot.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Isolation</b> : aucun touch production. Le dossier scratch reste
/// la seule surface modifiée. Asset <c>tile_edge_template.png</c>
/// recopié depuis Owner's Inbox dans <c>assets/</c> pour que le scene
/// soit autocontenu (suppression dossier = retour bit-pour-bit).
/// </para>
/// </summary>
public partial class IsoOrthoProbe : Node3D
{
    // Iso 2:1 RPG tactique = yaw 45° + pitch -30°.
    // sin(-30°) = -0.5 → Z monde projette à ½ × X monde sur l'axe vertical écran.
    private const float ExpectedPitchDegrees = -30.0f;
    private const float ExpectedYawDegrees = 45.0f;

    // Étape 4a : pas monde entre 2 tiles voisines sur axe X.
    // Hypothèse : image 2176×1152 px @ pixel_size=0.01 → 21.76 × 11.52 unités
    // monde, et l'image entière représente UNE cellule du grid logique.
    // Donc pas = 21.76 (largeur monde de la tile). Si le visuel révèle un
    // gap/overlap, on ajusture cette seule constante.
    private const float TileStepWorld = 21.76f;

    // Couture = mi-chemin world entre tile 1 (origin) et tile 2 (TileStepWorld).
    // La caméra est positionnée pour que ce point projette au centre du viewport.
    private const float SeamWorldX = TileStepWorld * 0.5f;  // 10.88

    // Position caméra de l'étape 3 (centrée sur l'origine), conservée comme
    // référence pour le calcul de translation. forward = (-√(3/8), -0.5, -√(3/8))
    // et -k·forward avec k=10 → (5√(3/2), 5, 5√(3/2)).
    private const float OriginCamXZ = 6.123725f;  // 5 * sqrt(3/2)
    private const float ExpectedCamY = 5.0f;

    // Position caméra étape 4a : on translate de +(SeamWorldX, 0, 0) en world.
    // X : 6.123725 + 10.88 = 17.003725
    // Y : inchangée (translation horizontale)
    // Z : 6.123725 + 0 = 6.123725
    private const float ExpectedCamX = OriginCamXZ + SeamWorldX;  // 17.003725
    private const float ExpectedCamZ = OriginCamXZ;               // 6.123725

    private const float ExpectedIsoRatio = 2.0f;

    // Clear color anti-palette : magenta saturé (#FF00FF). Aucune couleur de
    // la palette Wayfinders ne s'en approche — tout pixel non-magenta à
    // l'écran est nécessairement du contenu de tile.
    private static readonly Color ExpectedClearColor = new Color(1f, 0f, 1f, 1f);

    public override void _Ready()
    {
        // --- Résolution des nodes par chemin direct. ---
        var camera = GetNode<Camera3D>("Camera3D");
        var tileSprite = GetNode<Sprite3D>("TileSprite3D");
        var tileSpritePlusX = GetNode<Sprite3D>("TileSprite3D_PlusX");
        var worldEnv = GetNode<WorldEnvironment>("WorldEnvironment");

        // --- Canary preflight : 13 lignes [PROBE IsoOrthoProbe] dans Output. ---
        var viewportSize = GetViewport().GetVisibleRect().Size;

        // 1. Camera projection mode.
        var camProj = camera.Projection;
        var camOk = camProj == Camera3D.ProjectionType.Orthogonal;
        GD.Print($"[PROBE IsoOrthoProbe] 1/13 Camera.Projection = {camProj} -- {(camOk ? "OK" : "FAIL (expected Orthogonal)")}");

        // 2. Camera ortho size.
        var camSize = camera.Size;
        var sizeOk = Mathf.IsEqualApprox(camSize, 10.0f);
        GD.Print($"[PROBE IsoOrthoProbe] 2/13 Camera.Size = {camSize:F3} -- {(sizeOk ? "OK" : "FAIL (expected 10.0)")}");

        // 3. Camera position. La rotation fixe l'angle ; la position fixe
        // ce qui est centré dans le viewport. Étape 4a : translatée de
        // +(SeamWorldX, 0, 0) par rapport à l'étape 3, pour que la couture
        // entre tile 1 et tile 2 tombe au centre du viewport.
        // Tolérance ±0.001.
        var camPos = camera.Position;
        var posOk = Mathf.Abs(camPos.X - ExpectedCamX) < 0.001f
            && Mathf.Abs(camPos.Y - ExpectedCamY) < 0.001f
            && Mathf.Abs(camPos.Z - ExpectedCamZ) < 0.001f;
        GD.Print($"[PROBE IsoOrthoProbe] 3/13 Camera.Position = {camPos.ToString("F4")} -- {(posOk ? "OK" : $"FAIL (expected ({ExpectedCamX:F4}, {ExpectedCamY:F4}, {ExpectedCamZ:F4}) so seam ({SeamWorldX:F3},0,0) is viewport-centered)")}");

        // 4. Camera rotation iso 2:1. EulerOrder YXZ Godot default.
        // Tolérance ±0.01° (basis stockée en float7 dans le .tscn).
        var camRotDeg = camera.RotationDegrees;
        var pitchOk = Mathf.Abs(camRotDeg.X - ExpectedPitchDegrees) < 0.01f;
        var yawOk = Mathf.Abs(camRotDeg.Y - ExpectedYawDegrees) < 0.01f;
        var rollOk = Mathf.Abs(camRotDeg.Z) < 0.01f;
        var rotOk = pitchOk && yawOk && rollOk;
        GD.Print($"[PROBE IsoOrthoProbe] 4/13 Camera.RotationDegrees = {camRotDeg.ToString("F3")} -- {(rotOk ? "OK" : $"FAIL (expected pitch={ExpectedPitchDegrees:F2}, yaw={ExpectedYawDegrees:F2}, roll=0 iso 2:1)")}");

        // 5. Sprite3D pixel_size.
        var pxSize = tileSprite.PixelSize;
        var pxOk = Mathf.IsEqualApprox(pxSize, 0.01f);
        GD.Print($"[PROBE IsoOrthoProbe] 5/13 Sprite3D.PixelSize = {pxSize:F4} -- {(pxOk ? "OK" : "FAIL (expected 0.01)")}");

        // 6. Sprite3D texture filter — Nearest (no bilinear blur on pixel art).
        var filterMode = tileSprite.TextureFilter;
        var filterOk = filterMode == BaseMaterial3D.TextureFilterEnum.Nearest;
        GD.Print($"[PROBE IsoOrthoProbe] 6/13 Sprite3D.TextureFilter = {filterMode} -- {(filterOk ? "OK" : "FAIL (expected Nearest)")}");

        // 7. Sprite3D texture present.
        var tex = tileSprite.Texture;
        var texOk = tex is not null;
        var texDims = tex is null ? "null" : $"{tex.GetWidth()}x{tex.GetHeight()}";
        GD.Print($"[PROBE IsoOrthoProbe] 7/13 Sprite3D.Texture = {texDims} -- {(texOk ? "OK" : "FAIL (texture not assigned)")}");

        // 8. Viewport size.
        var vpOk = viewportSize.X > 0 && viewportSize.Y > 0;
        GD.Print($"[PROBE IsoOrthoProbe] 8/13 Viewport size = {viewportSize.X:F0}x{viewportSize.Y:F0} -- {(vpOk ? "OK" : "FAIL (zero viewport)")}");

        // --- Checks géométriques iso 2:1 via UnprojectPosition. ---
        //
        // On projette trois points monde — l'origine, +1 sur X, +1 sur Z —
        // en coords écran, et on dérive deux diagonales :
        //   diag (X-Z) = deltaX - deltaZ  : doit être pure-horizontale écran
        //   diag (X+Z) = deltaX + deltaZ  : doit être pure-verticale écran
        // Et |diag(X-Z)| / |diag(X+Z)| doit valoir ≈ 2.0 (iso 2:1).

        var screenOrigin = camera.UnprojectPosition(Vector3.Zero);
        var screenPlusX = camera.UnprojectPosition(new Vector3(1f, 0f, 0f));
        var screenPlusZ = camera.UnprojectPosition(new Vector3(0f, 0f, 1f));

        var deltaX = screenPlusX - screenOrigin;
        var deltaZ = screenPlusZ - screenOrigin;

        // 9. Sanity : deltas non nuls (sinon caméra dégénérée ou points hors frustum).
        var deltasNonZero = deltaX.Length() > 0.01f && deltaZ.Length() > 0.01f;
        GD.Print($"[PROBE IsoOrthoProbe] 9/13 UnprojectPosition deltaX={deltaX.ToString("F2")} deltaZ={deltaZ.ToString("F2")} -- {(deltasNonZero ? "OK" : "FAIL (zero delta, camera or frustum broken)")}");

        // 10. Ratio iso 2:1 + pureté des axes.
        var diagXminusZ = deltaX - deltaZ;  // world (+1, 0, -1) → écran
        var diagXplusZ = deltaX + deltaZ;   // world (+1, 0, +1) → écran

        var horizLen = diagXminusZ.Length();
        var vertLen = diagXplusZ.Length();
        var ratio = vertLen > 0.001f ? horizLen / vertLen : 0f;

        // Pureté : composante hors axe < 0.5px (tolérance arrondi float).
        var horizPurity = Mathf.Abs(diagXminusZ.Y) < 0.5f;
        var vertPurity = Mathf.Abs(diagXplusZ.X) < 0.5f;
        var purityOk = horizPurity && vertPurity;

        // Ratio : tolérance ±0.05 (~2.5%) pour absorber arrondi basis float7.
        var ratioOk = Mathf.Abs(ratio - ExpectedIsoRatio) < 0.05f;

        var iso2to1Ok = ratioOk && purityOk;
        GD.Print($"[PROBE IsoOrthoProbe] 10/13 Iso 2:1 ratio={ratio:F4} (horiz={horizLen:F2}px, vert={vertLen:F2}px, cross-residu X-Z.y={diagXminusZ.Y:F3}, X+Z.x={diagXplusZ.X:F3}) -- {(iso2to1Ok ? "OK" : $"FAIL (expected ratio≈{ExpectedIsoRatio:F1} + axes purs ; check pitch=-30° yaw=45°)")}");

        // --- Étape 4a : checks multi-tile. ---

        // 11. 2e tile : position (TileStep, 0, 0), rotation plat (-90° X),
        // texture présente.
        var plusXPos = tileSpritePlusX.Position;
        var posPlusXOk = Mathf.Abs(plusXPos.X - TileStepWorld) < 0.001f
            && Mathf.Abs(plusXPos.Y) < 0.001f
            && Mathf.Abs(plusXPos.Z) < 0.001f;
        var plusXRotX = tileSpritePlusX.RotationDegrees.X;
        var plusXRotOk = Mathf.Abs(plusXRotX - (-90f)) < 0.01f;
        var plusXTex = tileSpritePlusX.Texture;
        var plusXTexOk = plusXTex is not null;
        var plusXTexDims = plusXTex is null ? "null" : $"{plusXTex.GetWidth()}x{plusXTex.GetHeight()}";
        var tile2Ok = posPlusXOk && plusXRotOk && plusXTexOk;
        GD.Print($"[PROBE IsoOrthoProbe] 11/13 Tile+X pos={plusXPos.ToString("F3")} rotX={plusXRotX:F2} tex={plusXTexDims} -- {(tile2Ok ? "OK" : $"FAIL (expected pos=({TileStepWorld:F2},0,0), rotX=-90, texture set)")}");

        // 12. Seam centré viewport. Le point monde (SeamWorldX, 0, 0) — la
        // couture entre tile 1 et tile 2 — doit projeter au centre du
        // viewport, à ±2px près. C'est l'invariant qui garantit que la
        // caméra "regarde" bien le raccord et pas l'origine ou le coin
        // d'une tile. Sans ça, un gap d'1px peut tomber hors champ et
        // passer inaperçu.
        var screenSeam = camera.UnprojectPosition(new Vector3(SeamWorldX, 0f, 0f));
        var viewportCenter = viewportSize * 0.5f;
        var seamOffset = screenSeam - viewportCenter;
        var seamOk = Mathf.Abs(seamOffset.X) < 2f && Mathf.Abs(seamOffset.Y) < 2f;
        GD.Print($"[PROBE IsoOrthoProbe] 12/13 Seam ({SeamWorldX:F3},0,0) → screen={screenSeam.ToString("F1")} (viewport center={viewportCenter.ToString("F1")}, offset={seamOffset.ToString("F1")}) -- {(seamOk ? "OK" : $"FAIL (expected seam at viewport center ±2px ; camera position likely wrong)")}");

        // 13. Clear color anti-palette. WorldEnvironment doit exposer un
        // Environment avec BackgroundMode=Color et BackgroundColor=magenta
        // (#FF00FF). Sans ce check, on peut croire à un raccord propre alors
        // qu'on regarde juste du gris défaut Godot qui mange la bande W
        // grise de l'edge_template.
        // Godot 4.x : BackgroundMode est un enum BGMode ; Color = 1.
        //
        // Null-flow : Environment est un Resource? — il peut légitimement être
        // null (WorldEnvironment sans ressource assignée). On branche sur
        // `is not null` pour que le compilateur narrow le type dans la branche
        // true ; un bool intermédiaire `envPresent` ne suffit pas (le flow
        // analysis nullable de C# ne suit pas les bools de portée locale).
        // FAIL clair en cas d'absence plutôt que NRE silencieux.
        var env = worldEnv.Environment;
        bool clearOk;
        string bgModeStr;
        string bgColorStr;
        if (env is not null)
        {
            var bgMode = env.BackgroundMode;
            var bgColor = env.BackgroundColor;
            var bgModeOk = bgMode == Godot.Environment.BGMode.Color;
            var bgColorOk = Mathf.IsEqualApprox(bgColor.R, ExpectedClearColor.R)
                && Mathf.IsEqualApprox(bgColor.G, ExpectedClearColor.G)
                && Mathf.IsEqualApprox(bgColor.B, ExpectedClearColor.B);
            clearOk = bgModeOk && bgColorOk;
            bgModeStr = bgMode.ToString();
            bgColorStr = $"({bgColor.R:F2},{bgColor.G:F2},{bgColor.B:F2})";
        }
        else
        {
            clearOk = false;
            bgModeStr = "no-env";
            bgColorStr = "no-env";
        }
        GD.Print($"[PROBE IsoOrthoProbe] 13/13 WorldEnvironment bgMode={bgModeStr} bgColor={bgColorStr} -- {(clearOk ? "OK" : $"FAIL (expected WorldEnvironment.Environment non-null with BackgroundMode=Color, BackgroundColor=magenta (1,0,1) to disambiguate W=grey cliff band from default grey background)")}");
    }
}
