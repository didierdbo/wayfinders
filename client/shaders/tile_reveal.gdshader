// Tile reveal shader — boundary fade between revealed and unrevealed
// tiles (Varn spec varn-tile-reveal-shader-spec-2026-05-15.md, locked
// 2026-05-15). Applies to the face-B Halfgate Sprite2D overlay placed
// on each of the 16 cells of the e1 cluster ([30..33]², gx/gy).
//
// What this shader does, in one sentence : it modulates the face-B
// sprite's alpha by sampling a 64×64 R8 reveal-map texture at the
// tile's own grid coord (and at the 8 neighbours), then smoothsteps
// between self and neighbour values within the inner 30 % feather band
// along each edge. Centre of a fully revealed cluster reads alpha=1 ;
// edges of the cluster against unknown neighbours fade to alpha=0
// over a 30 %-of-tile-width band. Pratchett-compatible : encre
// administrative qui s estompe aux marges, pas un sortilège.
//
// Architecture (option b "texture lookup", per Larry mandate 2026-05-15) :
//   - The reveal_map is an R8 ImageTexture, 64×64, owned by the
//     IsoMapE1Probe scene. SetTileRevealLevel(gx, gy, value) writes
//     pixel (gx, gy) with byte = round(value * 255), then calls
//     reveal_map_texture.Update(image). The shader samples this
//     texture via texture(reveal_map, (tile_coord + 0.5) / 64.0).r.
//   - tile_coord is a per-sprite uniform (a vec2 holding the cell's
//     grid coord, e.g. vec2(30.0, 30.0) for the SW corner of the
//     Halfgate cluster). +0.5 lands the UV in the centre of the
//     pixel, away from the half-pixel boundary where sampler filtering
//     could pick the wrong cell.
//   - Eight neighbour samples : 4 cardinals (N/S/E/W) + 4 diagonals
//     (NE/SE/SW/NW). The diagonals matter for Varn §3 edge case
//     "tuile isolée révélée" (radial fade across 8 directions, not
//     just cardinal — without the diagonals the corners read as
//     sharp 90° angles).
//
// d3d12 trap notes (PR5 history, 2026-05-15 commit a5dbd42 / d549277) :
//   - canvas_item fragment contract in Godot 4 : at function entry,
//     COLOR = texture(TEXTURE, UV) * MODULATE. NOT MODULATE alone.
//     Writing `COLOR = vec4(...) * COLOR` double-multiplies by the
//     sprite texture and the alpha gets squared at the AA edges.
//     This shader writes COLOR directly from a freshly-sampled
//     texture(TEXTURE, UV), and uses ONLY COLOR.a (the modulate
//     alpha) for fog propagation — never re-multiplying RGB.
//   - The poi_shadow + poi_rim shaders disabled themselves in PR5
//     because of an unexplained mismatch between "all wiring correct"
//     and "rendered pixels do not match COLOR write". The mismatch
//     was never root-caused without a RenderDoc capture. If THIS
//     shader exhibits the same symptom on Didier's d3d12 Forward+
//     pipeline (silent fail, visible blackness or pass-through), the
//     fallback is documented in IsoMapE1Probe.UseRevealShader = false
//     — flip the toggle, ship without the boundary fade, re-open
//     the d3d12 mystery in a follow-up PR.
//
// Trap §8 (preflight delivered values) :
//   - feather_width is a uniform with default 0.3 ; the runtime sets
//     it from TileRevealStateLogic.DefaultFeatherWidth = 0.30 as a
//     single source of truth. If the runtime forgets to set it the
//     shader still defaults to spec ; if it sets a different value
//     the F6 toggle can A/B without a recompile.

shader_type canvas_item;

// The 64×64 R8 reveal-map texture. Owned by IsoMapE1Probe ; written
// by SetTileRevealLevel. Filter NEAREST (set in C# via
// texture_repeat=DISABLED + filter=NEAREST) so the shader reads back
// the exact byte the runtime wrote, not a bilinear blend of two cells.
uniform sampler2D reveal_map : filter_nearest, repeat_disable;

// The grid coord of THIS tile, e.g. vec2(30.0, 30.0) for the SW corner
// of the Halfgate 4×4 cluster. Set in C# at sprite spawn time. Float
// (not int) so the +0.5 pixel-centre math stays in the type system.
uniform vec2 tile_coord = vec2(0.0, 0.0);

// Size of the reveal-map texture, in cells. Must match
// TileRevealStateLogic.GridSize (64). Carried as a uniform so a future
// grid-size bump doesn't need a shader recompile — just a uniform set.
uniform float grid_size = 64.0;

// Feather width as a fraction of tile width. Locked at 0.30 per
// Varn §2. Same value as TileRevealStateLogic.DefaultFeatherWidth.
uniform float feather_width : hint_range(0.0, 0.5) = 0.30;

void fragment() {
    // 1. Compute UV into the reveal-map for self + 8 neighbours.
    //    The +0.5 lands at the cell's pixel centre ; dividing by
    //    grid_size maps cell coord → [0, 1] UV space.
    float inv_grid = 1.0 / grid_size;
    vec2 self_uv = (tile_coord + vec2(0.5)) * inv_grid;

    float self_level = texture(reveal_map, self_uv).r;

    // 4 cardinal neighbours (N is gy-1, S is gy+1 ; the iso projection
    // doesn't matter at the data layer — the grid is a plain rect of
    // cells, the iso visual is a layout convention upstream).
    float n_level = texture(reveal_map, self_uv + vec2(0.0, -inv_grid)).r;
    float s_level = texture(reveal_map, self_uv + vec2(0.0,  inv_grid)).r;
    float e_level = texture(reveal_map, self_uv + vec2( inv_grid, 0.0)).r;
    float w_level = texture(reveal_map, self_uv + vec2(-inv_grid, 0.0)).r;

    // 4 diagonal neighbours, for Varn §3 "tuile isolée" radial fade.
    // In the cluster case these read 1.0 for interior tiles and a
    // mix of 0/1 for corner tiles, which is what the smoothstep
    // composition below needs.
    float ne_level = texture(reveal_map, self_uv + vec2( inv_grid, -inv_grid)).r;
    float nw_level = texture(reveal_map, self_uv + vec2(-inv_grid, -inv_grid)).r;
    float se_level = texture(reveal_map, self_uv + vec2( inv_grid,  inv_grid)).r;
    float sw_level = texture(reveal_map, self_uv + vec2(-inv_grid,  inv_grid)).r;

    // 2. Smoothstep blend along each edge. UV is the fragment's
    //    position WITHIN this sprite's quad, in [0, 1]. The feather
    //    band is the inner 30 % from each edge ; outside this band
    //    (i.e. in the central 40 % core of the tile) the blend is
    //    pure self_level. Within the band the blend ramps toward the
    //    relevant neighbour.
    //
    //    For each edge :
    //      - smoothstep(0, feather_width, UV.x) ramps 0 → 1 as the
    //        fragment moves from the W edge into the interior.
    //        Inverted (1 - …) it ramps 1 → 0, peaking at the W edge.
    //      - We weight the W neighbour by this inverted ramp.
    //    Same logic for the 3 other cardinals.
    float w_weight = 1.0 - smoothstep(0.0, feather_width, UV.x);
    float e_weight = smoothstep(1.0 - feather_width, 1.0, UV.x);
    float n_weight = 1.0 - smoothstep(0.0, feather_width, UV.y);
    float s_weight = smoothstep(1.0 - feather_width, 1.0, UV.y);

    // Diagonal weights : product of two cardinals' weights. NW = N × W
    // and so on. At the exact corner pixel, both cardinals = 1.0 and
    // the diagonal weight = 1.0 ; the diagonal neighbour dominates.
    float nw_weight = n_weight * w_weight;
    float ne_weight = n_weight * e_weight;
    float sw_weight = s_weight * w_weight;
    float se_weight = s_weight * e_weight;

    // 3. Compose : start from self_level (the centre value), then
    //    LERP toward each neighbour by its weight. We use additive
    //    mix-style weighted contributions rather than chained mix()
    //    so 8 neighbours compose symmetrically (chained mix is
    //    order-dependent and the spec doesn't pick an order).
    //
    //    The "neighbour pull" is the sum over neighbours of
    //    weight × (neighbour_level - self_level). At the centre,
    //    every weight is 0 (we're outside every feather band) so
    //    blend = self_level. At an edge mid-fragment, the relevant
    //    cardinal weight is large and the blend lerps toward that
    //    neighbour. At a corner fragment, the diagonal weight peaks
    //    and the blend lerps toward the diagonal neighbour.
    float neighbour_pull =
          w_weight  * (w_level  - self_level)
        + e_weight  * (e_level  - self_level)
        + n_weight  * (n_level  - self_level)
        + s_weight  * (s_level  - self_level)
        + nw_weight * (nw_level - self_level)
        + ne_weight * (ne_level - self_level)
        + sw_weight * (sw_level - self_level)
        + se_weight * (se_level - self_level);

    // The 4 cardinal + 4 diagonal weights can sum to > 1 near a
    // corner (cardinals + their product diagonal both fire), which
    // would over-correct the blend past the neighbour values. We
    // normalise by clamping the total weight to 1.0 — the neighbour
    // pull is then bounded by max(neighbour - self) regardless of
    // how many bands overlap.
    float total_weight = w_weight + e_weight + n_weight + s_weight
        + nw_weight + ne_weight + sw_weight + se_weight;
    if (total_weight > 1.0) {
        neighbour_pull /= total_weight;
    }

    float blended_level = self_level + neighbour_pull;
    // Clamp to the legal continuum. The smoothstep math is bounded
    // already, but float arithmetic accumulation can drift a hair
    // past [0, 1] ; the clamp keeps the COLOR.a write tidy.
    blended_level = clamp(blended_level, 0.0, 1.0);

    // 4. Apply blended_level to the face-B sprite's alpha. The
    //    sprite's RGB passes through untouched (the diegetic art is
    //    Mira's, the shader only modulates visibility). We sample
    //    TEXTURE freshly rather than reading COLOR.rgb — see the
    //    d3d12 trap note at the top of this file. Reading COLOR.rgb
    //    would also work in principle (it = src.rgb * mod.rgb), but
    //    every shader in this codebase that worked under d3d12
    //    samples TEXTURE directly ; we follow the convention.
    vec4 src = texture(TEXTURE, UV);

    // COLOR.a carries the parent Modulate.a (fog edge cohabitation
    // pattern, mirrors poi_shadow.gdshader L70). If the face-B sprite
    // is being faded by fog or by a global tween, the boundary alpha
    // multiplies — no need for a separate uniform.
    COLOR = vec4(src.rgb, src.a * blended_level * COLOR.a);
}
