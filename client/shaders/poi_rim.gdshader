// PR5 — POI rim light, terracotta côté soleil SO ~30° (CanvasItem shader).
//
// Pourquoi runtime, pas baked dans l'asset (extension règle 6 asset
// pipeline) :
//   - Même direction soleil pour TOUS les POI → un seul uniform partagé,
//     pas N bakes différents.
//   - Si la direction soleil change (ambiance crépusculaire mission night
//     E3, etc.), le rim s'adapte sans re-bake.
//
// Pourquoi pas un outline classique (anti-checklist locked) :
//   - Outline cartoon = trait noir/blanc uniforme = casse l'oil-painting
//     Bourgeon DNA. Pop color = jouet pour enfant.
//   - Rim light = highlight DIRECTIONNEL sur le bord côté lumière. C'est
//     le truc que peint un peintre quand le soleil rase le sujet — exactement
//     l'ingrédient #3 "pion sur plateau".
//
// Méthode (gradient-of-alpha approx) :
//   1. Échantillonner l'alpha de 4 voisins → estimer le gradient ∇α.
//   2. Normaliser ∇α → c'est la "normale" 2D du bord (pointe vers
//      l'extérieur de la silhouette).
//   3. dot(normal, light_dir) → cos(θ). Si normal aligné avec light_dir,
//      le bord est éclairé.
//   4. Rim factor = max(0, dot(normal, light_dir)) × edge_mask, où
//      edge_mask est fort sur les bords (|∇α| grand) et nul à l'intérieur
//      (alpha plein) ou à l'extérieur (alpha zéro).
//   5. Color out = lerp(texture, rim_color, rim × strength). Le sprite
//      reste NET au centre (le mix tend vers src quand rim=0).
//
// Trap subtle :
//   - On RIM seulement les pixels où alpha original > 0.05 (sinon on
//     teinte la transparence ambiante et le sprite a un halo). On ne RIM
//     pas non plus si alpha > 0.9 (l'intérieur de la silhouette doit
//     rester l'oil-painting de Mira). Bande de tolérance ≈ [0.05, 0.9].
//
// Bug fix (Rune, 2026-05-15, post-`a5dbd42`). Le contrat fragment
// canvas_item Godot 4 : `COLOR` à l'entrée fragment vaut
// `texture(TEXTURE, UV) * MODULATE`, PAS `MODULATE` seul. Le premier cut
// finissait par `COLOR = vec4(out_rgb, src.a) * COLOR`. Sur un pixel
// intérieur opaque, out_rgb ≈ src.rgb (rim≈0), src.a = 1.0, et le
// `* COLOR` multipliait à nouveau par `texture(TEXTURE, UV) * MODULATE` :
//   out_rgb_final = src.rgb × src.rgb (couleur au carré, plus sombre et
//                   gamma cassée)
//   out_a_final   = src.a × src.a × mod.a (alpha au carré : un bord à
//                   src.a=0.5 devient 0.25 → bord BEAUCOUP plus
//                   transparent → halo flou par mix avec le background)
// Le sprite paraissait alors "flou et délavé" alors que ce n'était PAS
// un blur du tout — juste un double-mul texture qui carrait l'alpha aux
// bords antialiasés. Fix : écrire COLOR sans multiplication par COLOR sur
// le RGB. On lit `COLOR.a` uniquement pour propager Modulate.a (fog edge).

shader_type canvas_item;

uniform vec2 texel = vec2(1.0 / 1076.0, 1.0 / 575.0);

// Direction de la lumière (UV-space, normalisée). SO 30° par défaut :
//   - "S" = Y+ en convention UV (Y down sur l'écran).
//   - "O" = X- en convention UV (West).
//   - 30° au-dessus de l'horizon = composante Y moindre.
//   → light_dir pointe FROM le soleil TO le pion. SO 30° = soleil au SO,
//     light_dir vise NE 30° below horizon. En UV : x=+sin(60°), y=-cos(60°)
//     ≈ (+0.87, -0.5). Normalize ensure it.
//   Pour le rim, on veut la normale qui POINTE VERS le soleil pour s'éclairer.
//   Donc light_dir = (-0.87, +0.5) (depuis pion vers soleil SO).
uniform vec2 light_dir = vec2(-0.866, 0.5);

// Couleur du rim : terracotta saturé sun-side. Décision micro Rune :
//   - #c87844 (200, 120, 68) — entre le #7d4e2c locked (terracotta sombre,
//     bord cadre tile_edge_template) et un ochre clair.
//   - 0.78, 0.47, 0.27 en float linéaire approximé sRGB.
//   - À valider par Didier au F6 toggle ; si trop chaud, baisser saturation.
uniform vec4 rim_color : source_color = vec4(0.78, 0.47, 0.27, 1.0);

// Intensité du rim. 0.6 = subtle (locked). 1.0 = bord cramé.
uniform float rim_strength : hint_range(0.0, 1.0) = 0.6;

void fragment() {
    vec4 src = texture(TEXTURE, UV);

    // Sample alpha gradient via 4 cardinaux. ∇α pointe FROM l'intérieur
    // de la silhouette VERS l'extérieur (alpha décroît vers le bord).
    // .a uniquement — on ne lit pas .rgb, on ne moyenne pas les couleurs.
    float aR = texture(TEXTURE, UV + vec2(texel.x, 0.0)).a;
    float aL = texture(TEXTURE, UV - vec2(texel.x, 0.0)).a;
    float aD = texture(TEXTURE, UV + vec2(0.0, texel.y)).a;
    float aU = texture(TEXTURE, UV - vec2(0.0, texel.y)).a;

    vec2 grad = vec2(aR - aL, aD - aU);
    float grad_mag = length(grad);

    // edge_mask : 0 dans le solide, 1 au bord, 0 dehors. ∇α est non-nul
    // uniquement sur les bords (où alpha varie).
    float edge_mask = smoothstep(0.02, 0.4, grad_mag);

    // Direction de la normale du bord : -∇α (pointe vers l'intérieur, donc
    // vers la silhouette — la lumière éclaire un bord dont la normale
    // pointe VERS le soleil).
    vec2 normal = -normalize(grad + vec2(0.0001, 0.0001)); // epsilon anti-div0

    // Dot avec light_dir. La normale du bord côté soleil a un dot positif
    // élevé avec light_dir.
    float lambert = max(dot(normal, normalize(light_dir)), 0.0);

    // Gate sur l'alpha source : ne teinte ni les pixels transparents purs
    // (halo) ni le centre de la silhouette (intérieur peinture).
    float alpha_gate = smoothstep(0.05, 0.2, src.a) * (1.0 - smoothstep(0.9, 1.0, src.a));

    // Rim est un SCALAIRE [0,1]. Pas de moyennage de samples colorés ici —
    // les samples ci-dessus ne servent qu'à calculer le gradient d'alpha.
    float rim = lambert * edge_mask * alpha_gate * rim_strength;

    // Mélange côté soleil. mix(src.rgb, rim_color.rgb, rim) blends
    // proprement sans cramer les hautes lumières existantes. Quand rim=0
    // (la grande majorité des pixels : intérieur + transparence), out_rgb
    // = src.rgb EXACTEMENT — le sprite reste NET.
    vec3 out_rgb = mix(src.rgb, rim_color.rgb, rim);

    // Écriture directe SANS `* COLOR` sur le RGB (voir bloc fix en tête).
    // L'alpha du sprite est préservée intégralement (src.a) ; on propage
    // Modulate.a via `COLOR.a` pour la cohabitation fog edge.
    COLOR = vec4(out_rgb, src.a * COLOR.a);
}
