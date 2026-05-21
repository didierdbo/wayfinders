// desk_triangle_clip.gdshader -- J3c-1bis desk corner triangular clip.
//
// PROBLEM. The cartographer's-desk is the second iso space of the Game
// Screen Shell, parked bottom-left. Its content is rendered into its own
// SubViewport (the locked "two SubViewports, two iso scales" decision is
// untouched -- the desk keeps its private render world + camera). But a
// SubViewport's texture is, like any texture, RECTANGULAR. Drawn flat to
// the screen it reads as a brown rectangle in the corner. The mockup
// wants the desk as a TRIANGLE in the bottom-left corner: the desk fills
// exactly the wedge the central maquette diamond does NOT cover, and the
// maquette/desk frontier is the diagonal edge of that iso diamond.
//
// SOLUTION. The desk SubViewport is shown via a plain TextureRect (no
// SubViewportContainer -- a container is intrinsically rectangular). This
// shader runs on that TextureRect and `discard;`s every fragment on the
// MAQUETTE side of the frontier line, so the desk survives only inside
// the bottom-left triangle.
//
// The frontier is one straight line. It is described by two uniforms in
// the TextureRect's UV space ([0,1]x[0,1], origin top-left, +Y down):
//   - `clip_line_point`  : any point ON the frontier line.
//   - `clip_line_normal` : the line's normal, pointing INTO the desk
//                          (the keep side). Need not be unit length --
//                          only the sign of the dot product is used.
// A fragment at UV `p` is kept when dot(p - point, normal) >= 0, i.e. it
// is on the desk side of the line; otherwise it is discarded.
//
// Both uniforms are COMPUTED in C# (GameScreen.ConfigureDesk) from the
// maquette diamond geometry and pushed in -- never hard-coded. The
// frontier therefore tracks the real diamond edge, and a re-authored
// maquette grid size only changes the C# maths, not this shader (the
// "trap: computed values, not magic numbers" discipline).
//
// GLSL/Godot trap. `return;` is NOT valid to short-circuit a fragment()
// in Godot 4 -- the compiler rejects it and the material silently becomes
// a no-op (visible as an "E ->" line in the Output log). The standard way
// to drop a fragment is `discard;`. Same note as area_grid_tile_overlay.
//
// canvas_item fragment contract. At function entry COLOR =
// texture(TEXTURE, UV) * MODULATE. We keep the SubViewport texture as-is
// (the desk world already drew its own brown floor + iso grid + pawns),
// so on the keep side we simply leave COLOR untouched.

shader_type canvas_item;

// A point on the maquette/desk frontier line, in this TextureRect's UV
// space. Set per-frame-once by GameScreen.ConfigureDesk.
uniform vec2 clip_line_point = vec2(1.0, 0.0);

// The frontier line normal, pointing toward the desk (keep) side. Sign of
// the dot product is all that matters; magnitude is irrelevant. Default
// points up-right so that, absent a C# push, nothing is clipped away by
// accident on a degenerate setup -- a visible rectangle is a louder bug
// than a silently-empty desk.
uniform vec2 clip_line_normal = vec2(0.0, 0.0);

void fragment() {
    // Signed position relative to the frontier line. Negative => the
    // fragment is on the maquette side of the diagonal => not part of the
    // desk triangle => drop it so the maquette behind shows through.
    float side = dot(UV - clip_line_point, clip_line_normal);
    if (side < 0.0) {
        discard;
    }
    // Keep side: leave COLOR as the SubViewport texture * MODULATE. The
    // desk world (brown floor, iso grid, pawns) is already in TEXTURE.
}
