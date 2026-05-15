// Tile reveal shader — single-lookup variant after the 2026-05-15
// CPU-blur rewrite (Larry mandate). The feathering that used to live
// here as an 8-neighbour smoothstep composition has moved CPU-side :
// TileRevealStateLogic.BlurRevealMap pre-blurs the R8 reveal-map before
// every Update(), so this shader becomes a trivial single texture
// lookup at the tile's own grid coord.
//
// Why the move :
//   - The shader-side composition produced uneven feathering depending
//     on each tile's neighbourhood configuration (corners vs edges vs
//     interior of a 4×4 cluster), because the 8-neighbour weighted sum
//     reads different combinations of revealed/unrevealed levels at
//     each tile. A box blur on the underlying R8 map is configuration-
//     independent — every cluster shape gets the same isotropic halo.
//   - The update is rare (16 events during the e1 cinematic, then
//     nothing in MVP) and operates on a 64×64 buffer = 4 KB. Three
//     box-blur passes are ~110k FLOPs — invisible against the 16 ms
//     frame budget.
//   - The shader stays trivial (one texture sample), which sidesteps
//     the entire class of d3d12 forward-pipeline render mismatches we
//     hit on poi_shadow + poi_rim earlier.
//
// d3d12 trap notes (preserved from earlier rev) :
//   - canvas_item fragment contract in Godot 4 : at function entry
//     COLOR = texture(TEXTURE, UV) * MODULATE. Writing `COLOR =
//     vec4(...) * COLOR` re-applies the modulate term and squares
//     alpha at AA edges. This shader writes COLOR directly from a
//     freshly-sampled TEXTURE — no `* COLOR` at the end.
//   - The reveal_map sampler keeps filter=NEAREST so the shader reads
//     back the exact byte the runtime wrote into pixel (gx, gy),
//     unblended.

shader_type canvas_item;

// The 64×64 R8 reveal-map texture, pre-blurred CPU-side. Owned by
// IsoMapE1Probe ; written by SetTileRevealLevel + ComputeBlurredRevealMap.
// Filter NEAREST so the shader reads back the exact pre-blurred byte
// at this tile's grid coord, without a second bilinear smoothing pass
// that would over-soften the edges.
uniform sampler2D reveal_map : filter_nearest, repeat_disable;

// The grid coord of THIS tile, e.g. vec2(30.0, 30.0) for the SW corner
// of the Halfgate 4×4 cluster. Set in C# at sprite spawn time. Float
// (not int) so the +0.5 pixel-centre math stays in the type system.
uniform vec2 tile_coord = vec2(0.0, 0.0);

// Size of the reveal-map texture, in cells. Must match
// TileRevealStateLogic.GridSize (64). Carried as a uniform so a future
// grid-size bump doesn't need a shader recompile — just a uniform set.
uniform float grid_size = 64.0;

void fragment() {
    // Sample the (already blurred) reveal level at this tile's pixel
    // centre. The +0.5 lands the UV at the centre of the cell so the
    // NEAREST filter unambiguously picks pixel (gx, gy) — landing
    // exactly on a boundary could otherwise round to the wrong cell.
    vec2 self_uv = (tile_coord + vec2(0.5)) / grid_size;
    float blended_level = texture(reveal_map, self_uv).r;

    // Apply the reveal level to the face-B sprite's alpha. RGB passes
    // through untouched — the shader only modulates visibility, never
    // recolours. COLOR.a carries the parent Modulate.a (fog edge
    // cohabitation pattern). Writing COLOR directly from a freshly
    // sampled TEXTURE avoids the d3d12 modulate-squaring trap.
    vec4 src = texture(TEXTURE, UV);
    COLOR = vec4(src.rgb, src.a * blended_level * COLOR.a);
}
