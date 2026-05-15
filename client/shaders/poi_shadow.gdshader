// PR5 — POI ombre portée runtime, blur 3-tap sur alpha (CanvasItem shader).
//
// Pourquoi runtime, pas baked dans l'asset (locked memo asset_pipeline,
// règle 6 + extension pion sur plateau) :
//   - Direction du soleil = paramètre runtime (varie potentiellement par
//     moment de la journée ou par mission). Bake = couplage figé.
//   - Si Mira itère sur l'asset POI (anchor, silhouette), il n'a pas à
//     re-baker une nouvelle ombre alignée.
//   - L'ombre suit l'alpha de la diégèse (fog edge) — propagé par C# en
//     `shadow.Modulate.A = poi.Modulate.A` (cf. Poi.cs).
//
// Pourquoi 3-tap blur, pas un vrai gaussian 9-tap :
//   - Coût : 5 texture samples / fragment au lieu de 9. Sur 2-8 POIs à
//     l'écran sur une iso E1, c'est trivial mais on n'a pas besoin de
//     plus de softness — l'asset a déjà des bords doux (oil painting).
//   - Si Didier trouve la softness sub-pixel ou trop sèche, on peut
//     élargir le kernel sans changer la structure du shader. Pour l'instant
//     5 taps (center + 4 cardinals) suffisent.
//
// Pourquoi `canvas_item` :
//   - PR3 a locké Sprite2D (2D, pas Spatial). `canvas_item` est le shader
//     type qui marche sur Sprite2D / Sprite3D / TextureRect / ...
//
// Trap §8 (computed values) :
//   - La softness *livrée à l'écran* dépend de `texel × scaleY × zoom`.
//     `texel` est l'inverse de la taille texture en pixels — calculé en
//     C# au _Ready et passé en uniform pour rester précis quel que soit
//     l'asset. Si jamais une variante POI livre une texture 2048x… au lieu
//     du 1076x575 canon, le shader ne tappera pas dans le vide.
//
// Bug fix (Rune, 2026-05-15, post-`a5dbd42`). Le contrat fragment
// canvas_item Godot 4 : `COLOR` à l'entrée fragment vaut
// `texture(TEXTURE, UV) * MODULATE`, PAS `MODULATE` seul. Le premier cut
// finissait par `COLOR = vec4(0, 0, 0, a * shadow_alpha) * COLOR`, ce qui
// (avec le COLOR de Godot) multiplie l'alpha-out par `src.a * mod.a` —
// passable, mais le commentaire trahissait la confusion ("le multiplicateur
// COLOR vient du Sprite2D.Modulate"). Convention prebrief stricte
// (poi-integration-pattern-prebrief.md ligne 345) : le shader FORCE le
// noir lui-même, indépendant de Modulate, et lit Modulate seulement via
// `COLOR.a` pour la cohabitation fog edge. Pas de re-multiplication par
// la texture du sprite.

shader_type canvas_item;

// Taille du texel en UV. Set par Poi.cs au _Ready :
//   texel = vec2(1.0/textureWidth, 1.0/textureHeight)
// Sans cet uniform on devrait hardcoder la taille → ne marche que pour Halfgate.
uniform vec2 texel = vec2(1.0 / 1076.0, 1.0 / 575.0);

// Densité de l'ombre. 0.35 = locked pion-sur-plateau (sub-conscient, pas
// "tâche noire"). Exposé pour tuning Didier au F6 via toggle clavier
// (cf. PoiSpawnProbe + Poi.ShadowAlpha export).
uniform float shadow_alpha : hint_range(0.0, 1.0) = 0.35;

void fragment() {
    // 5-tap box-ish blur sur l'alpha de la texture. On échantillonne le
    // centre + 4 voisins cardinaux pondérés. JAMAIS .rgb ici — l'ombre est
    // une silhouette, pas une copie teintée du sprite.
    float a = 0.0;
    a += texture(TEXTURE, UV).a * 0.5;
    a += texture(TEXTURE, UV + vec2(texel.x, 0.0)).a * 0.125;
    a += texture(TEXTURE, UV - vec2(texel.x, 0.0)).a * 0.125;
    a += texture(TEXTURE, UV + vec2(0.0, texel.y)).a * 0.125;
    a += texture(TEXTURE, UV - vec2(0.0, texel.y)).a * 0.125;

    // Output : noir solid, alpha = blur × densité × modulate.a.
    // - RGB forcé à 0 par le shader (PAS dépendant de Modulate.rgb).
    // - `COLOR.a` lu pour la cohabitation fog edge : si Poi.cs propage
    //   shadow.Modulate.A = poi.Modulate.A, le fog atténue l'ombre via
    //   ce canal sans réintroduire de couleur dans le RGB.
    COLOR = vec4(0.0, 0.0, 0.0, a * shadow_alpha * COLOR.a);
}
