// area_grid_tile_overlay.gdshader -- E2.1 Halfgate area-grid tile overlay
//
// Minimal canvas_item shader that renders a per-cell overlay on top of
// the base district tile. Used for the partial-state visual treatment :
// when a tile's reveal_state is Partial, this overlay paints the cell
// with a warm-cream parchment veil (terracotta umber tint, ~35-45 %
// alpha modulation by the `parchment_alpha` uniform).
//
// E2.1c addendum (2026-05-16, Option C — Rune-authored district tint).
// On top of the parchment wash, the shader now multiplies the base
// bitmap by a per-cell `district_tint` color. This lets us keep the
// single E1 iso losange bitmap for all 64 cells (alpha-corner discipline
// preserved -- the losange shape stays clean) while still rendering 6
// visually-distinct district zones. Mira will later replace this with
// proper per-district iso losange bitmaps ; at that point `district_tint`
// either stays as a subtle hue lift on top of the painted bitmap or is
// dialled to white (= identity) by the spawn site. The shader surface
// does not change either way.
//
// Wiring contract :
// - The shader is bound to a Sprite2D / TextureRect sitting ON TOP of
//   the base district tile (one Material per cell).
// - The `parchment_alpha` uniform is driven by TileRevealRenderController
//   via the 300 ms Tween on every `fog → partial` / `partial → revealed`
//   transition (the controller pushes the alpha value through the
//   OnRevealLevelChanged callback to the owner scene, which writes it
//   into this uniform).
// - The `district_tint` uniform is set ONCE at spawn time by
//   E2AreaMap.SpawnTile from the Varn-locked palette in
//   DistrictTypeHelpers.TintRgba. It stays constant for the lifetime of
//   the tile.
// - When parchment_alpha = 0.0, the cell shows nothing -- the base tile
//   is fully covered by fog (fog veil texture handled separately).
// - When parchment_alpha = 0.55 (the Varn-locked "partial" value), the
//   tile is visible through a soft umber wash.
// - When parchment_alpha = 1.0 (the "revealed" value), the wash falls
//   off to nothing and the base tile is fully visible.
//
// Fog-state semantic (Option C) :
// At Fog (parchment_alpha = 0.0) the consumer scene is expected to set
// district_tint = vec4(1.0) (identity) so the cell renders as the bare
// E1 bitmap -- the cadastral veil "rien deviné" reading. The tint only
// kicks in once the cell reaches Partial / Revealed. Today the spawn
// site sets district_tint per cell unconditionally and the fog state is
// carried by parchment_alpha == 0 + the upstream fog veil ; if E2.1c
// later wants strict cadastral neutrality at fog, swap the tint to white
// in the Fog branch of OnRevealLevelTweenStep.
//
// Note: this is a Rune-authored MVP shader, flagged for Mira audit
// post-Didier-validation of the placeholder render. Per the brief,
// Mira will refine the look (grain, edge feather, hue adjustments) ;
// the parametric surface (`parchment_alpha`, `district_tint`, the tint
// constants) stays stable so the audit is purely visual.
//
// References:
//   - Varn reconciliation 2026-05-16 §3.3 (300 ms Tween + derived float).
//   - TileRevealStateHelpers.ResolveRevealLevel (Fog=0.0 / Partial=0.55 / Revealed=1.0).
//   - DistrictTypeHelpers.TintRgba (Varn-locked palette, 6 entries).
//   - Wayfinders visual DNA : warm earth axis (terracotta + parchment + umber).

shader_type canvas_item;

// Parchment veil opacity, set per-instance by TileRevealRenderController.
// Range [0.0, 1.0]. The base tile is visible at 0.0 (full reveal) and
// "obscured" by the parchment overlay as the value rises, peaking around
// the Partial value (0.55) where the umber wash is most pronounced. As
// it rises further toward 1.0 (Revealed), the overlay fades back to
// nothing so the tile is fully visible -- this is the inversion vs.
// the cinematic shader which uses the reveal_level as a straight
// "show face B" alpha. Here, parchment_alpha == 0 OR == 1 means
// "no overlay", and the peak of the overlay sits at the partial value.
uniform float parchment_alpha : hint_range(0.0, 1.0) = 0.0;

// Per-cell district tint, set ONCE at spawn time by E2AreaMap.SpawnTile.
// Default white = identity (= "no tint" -- the bare bitmap). Multiplied
// against the base RGB BEFORE the parchment wash, so the wash colour
// (terracotta umber) reads consistently across all districts. Alpha
// component is ignored -- the source bitmap's alpha is the source of
// truth for the iso losange shape.
//
// Values authored in DistrictTypeHelpers.TintRgba per the Varn-locked
// 6-district palette (intramuros / wall / gateway / outskirts /
// hinterland_agri / littoral). Locked 2026-05-16, Option C Rune-pass.
uniform vec4 district_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);

// Tint constants. The overlay color is umber-warm so cells in partial
// state read as "fogged but with a hint of legibility". Tweakable via
// uniforms if Mira wants finer control later ; locked here for the MVP.
uniform vec4 parchment_tint : source_color = vec4(0.690, 0.522, 0.337, 1.0);

// Alpha threshold below which the source pixel is considered transparent
// "background" and the shader discards the parchment overlay entirely
// (writes a fully-transparent output). This guards the iso-losange shape
// from the d3d12 + LinearWithMipmaps + bilinear-sampling edge-bleed combo,
// AND guards future placeholder bitmaps that may have semi-opaque "halo"
// pixels at the corners. The E1 neutral bitmap has clean alpha=0 corners
// so the threshold never bites for it ; the guard is defensive for the
// E2.1c per-district bitmap dégelé.
const float ALPHA_DISCARD_THRESHOLD = 0.01;

void fragment() {
    // Read the base sprite's pixel (the district tile texture).
    vec4 base = texture(TEXTURE, UV);

    // Defensive alpha discard. If the source pixel is transparent (a
    // corner of the iso losange bitmap), skip the pixel entirely so it
    // contributes nothing to the framebuffer. Keeps the iso losange
    // shape visible even if a future bitmap has dirty alpha corners.
    //
    // GLSL/Godot trap : `return;` is NOT valid inside a fragment()
    // function -- the shader compiler rejects it ("E ->" in the Output
    // log) and the material silently becomes a no-op (attached, uniforms
    // updated, but no pixel customization). The GLSL-standard way to
    // short-circuit a fragment is `discard;` -- it tells the rasterizer
    // to drop this pixel without writing to COLOR at all.
    if (base.a < ALPHA_DISCARD_THRESHOLD) {
        discard;
    }

    // -- District tint pass (Option C, E2.1c Rune addendum).
    // Multiply the base RGB by the per-cell district tint. Identity
    // (district_tint = vec4(1.0)) leaves the bitmap untouched, which is
    // the default and the fog-state expectation. Multiplication preserves
    // the bitmap's tonal variations -- a darker brush stroke stays darker
    // relative to a lighter one within the same district. This is the
    // cheap-and-correct way to "colourise" a neutral bitmap without
    // flattening it.
    vec3 tinted_rgb = base.rgb * district_tint.rgb;

    // Compute the overlay weight : peaks at parchment_alpha == 0.55 (the
    // Varn-locked Partial value) and tapers to zero at both ends. The
    // formula is a triangular function centred on 0.55 with full reach
    // [0, 1.1]. Inverts cleanly at 0 (full fog, overlay invisible because
    // the underlying tile is covered by the fog veil texture upstream)
    // and at 1.0 (full reveal, overlay invisible by design).
    //
    // weight = max(0, 1 - 2 * abs(alpha - 0.55))
    //
    // alpha=0.00 -> weight = max(0, 1 - 1.10) = 0.0
    // alpha=0.27 -> weight ~ 0.44
    // alpha=0.55 -> weight = 1.0
    // alpha=0.82 -> weight ~ 0.46
    // alpha=1.00 -> weight = max(0, 1 - 0.90) = 0.10
    float weight = max(0.0, 1.0 - 2.0 * abs(parchment_alpha - 0.55));

    // Soft cap so the overlay never fully replaces the tile -- always
    // leaves at least 60% of the tile color visible at peak.
    float overlay_opacity = weight * 0.40;

    // Blend : overlay tint over the TINTED tile color. Mix takes the
    // (district-tinted) base toward the parchment tint by overlay_opacity,
    // then we keep the base alpha (sprite alpha is the source of truth
    // for shape, not the overlay).
    //
    // Order matters : district_tint multiplies first (changes the hue
    // of the bitmap), then the wash mixes toward terracotta over that.
    // Swapping the order would tint the wash itself by the district,
    // which fights the "fog reading" intent.
    //
    // d3d12 trap note (mirrors tile_reveal.gdshader header). canvas_item
    // fragment contract in Godot 4 : at function entry COLOR =
    // texture(TEXTURE, UV) * MODULATE. Writing `base.a * COLOR.a` thus
    // squares the texture alpha at AA edges (slight darkening). For the
    // E1 neutral bitmap with binary alpha (0 at corners, ~255 inside),
    // the squaring is invisible ; for any future bitmap with a smooth
    // alpha falloff at the edge, the discard above short-circuits the
    // squaring branch entirely. Keeping the multiplication preserves
    // the parent Modulate.a (fog edge cohabitation pattern, same as
    // tile_reveal.gdshader).
    vec3 blended_rgb = mix(tinted_rgb, parchment_tint.rgb, overlay_opacity);

    COLOR = vec4(blended_rgb, base.a * COLOR.a);
}
