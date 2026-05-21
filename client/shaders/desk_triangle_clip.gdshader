// desk_triangle_clip.gdshader -- J3c-1quater desk TWIN-corner triangular clip.
//
// PROBLEM. The cartographer's-desk is the second iso space of the Game
// Screen Shell. Its content is rendered into its own SubViewport (the
// locked "two SubViewports, two iso scales" decision is untouched -- the
// desk keeps its private render world + camera, and there is STILL exactly
// one desk SubViewport: the two visible desk corners are two windows onto
// the SAME iso world, not two viewports). But a SubViewport's texture is,
// like any texture, RECTANGULAR. Drawn flat to the screen it reads as a
// brown rectangle.
//
// The mockup wants the desk as TWO mirrored brown triangles -- one in the
// bottom-LEFT screen corner, one in the bottom-RIGHT -- in vis-a-vis,
// separated by the lower point of the central maquette diamond. Each desk
// triangle is bounded by a screen edge (left or right), the screen bottom,
// and a HYPOTENUSE that is one lower edge of the iso maquette diamond. The
// maquette therefore reads as a centred iso diamond whose lower point sits
// on the screen's horizontal centre and reaches DOWN near the bottom HUD
// band, so the diamond owns the bulk of the screen and the two desk
// triangles are thin bottom corner wedges (J3c-1quater, after the F6
// capture: "le bureau prend presque toute la place, a ajuster"). The desk
// fills exactly the two lower corners that big diamond does not cover.
//
// The diamond's height -- hence how thin the desk corners are -- is driven
// by two named C# fractions (DeskClipFrontierLogic.MaquetteLowerPointScreenFracY
// / MaquetteSideApexScreenFracY); re-tuning the proportion is a C#-only
// change, this shader is unaffected.
//
// SOLUTION. The desk SubViewport is shown via a plain TextureRect (no
// SubViewportContainer -- a container is intrinsically rectangular). This
// shader runs on that TextureRect and `discard;`s every fragment that is
// NOT inside one of the two corner triangles, so the desk survives only in
// the bottom-left and bottom-right wedges.
//
// GEOMETRY. The two desk triangles are described by two hypotenuse lines
// plus one vertical split, all in the TextureRect's UV space ([0,1]x[0,1],
// origin top-left, +Y down):
//   - left  hypotenuse : from the diamond's left  side apex (UV x=0) down
//                        to the diamond's lower point (UV x = split_u).
//   - right hypotenuse : from the diamond's lower point (UV x = split_u)
//                        up to the diamond's right side apex (UV x=1).
//   - split_u          : the UV x of the diamond's lower point (~0.5).
// A fragment is KEPT when it is inside the LEFT corner triangle
//   (uv.x <= split_u  AND  it is on the desk side of the left hypotenuse)
// OR inside the RIGHT corner triangle
//   (uv.x >= split_u  AND  it is on the desk side of the right hypotenuse).
// The screen-edge sides of each triangle need no test: a fragment inside
// UV [0,1]x[0,1] is already inside the screen by construction.
//
// Each hypotenuse is one straight line, described by a point ON the line
// and a NORMAL pointing INTO the desk (the keep side). Need not be unit
// length -- only the sign of the dot product is used. The `split_u`
// guards each half-plane to its own corner so the unbounded half-plane
// of one hypotenuse can never bleed into the other corner.
//
// All uniforms are COMPUTED in C# (GameScreen.ApplyDeskClipFrontier) from
// the maquette diamond geometry and pushed in -- never hard-coded. The
// frontier tracks the real diamond; a re-authored maquette only changes
// the C# maths, not this shader (the "computed values, not magic numbers"
// discipline).
//
// GLSL/Godot trap. `return;` is NOT valid to short-circuit a fragment()
// in Godot 4 -- the compiler rejects it and the material silently becomes
// a no-op (visible as an "E ->" line in the Output log). The standard way
// to drop a fragment is `discard;`. Same note as area_grid_tile_overlay.
//
// canvas_item fragment contract. At function entry COLOR =
// texture(TEXTURE, UV) * MODULATE. We keep the SubViewport texture as-is
// (the desk world already drew its own brown floor + iso grid + pawns),
// so on a kept fragment we simply leave COLOR untouched.

shader_type canvas_item;

// A point on the LEFT desk triangle's hypotenuse, UV space.
uniform vec2 clip_left_point = vec2(0.0, 0.0);

// The LEFT hypotenuse normal, pointing toward the desk (keep) side. Only
// the sign of the dot product matters; magnitude is irrelevant.
uniform vec2 clip_left_normal = vec2(0.0, 0.0);

// A point on the RIGHT desk triangle's hypotenuse, UV space.
uniform vec2 clip_right_point = vec2(1.0, 0.0);

// The RIGHT hypotenuse normal, pointing toward the desk (keep) side.
uniform vec2 clip_right_normal = vec2(0.0, 0.0);

// UV x of the maquette diamond's lower point -- the vertical split that
// separates the bottom-left corner triangle from the bottom-right one.
// Default 0.5 so, absent a C# push, the split sits screen-centre.
uniform float clip_split_u = 0.5;

void fragment() {
    // Bottom-LEFT corner triangle: left of the split AND on the desk side
    // of the left hypotenuse.
    bool in_left =
        UV.x <= clip_split_u
        && dot(UV - clip_left_point, clip_left_normal) >= 0.0;

    // Bottom-RIGHT corner triangle: right of the split AND on the desk
    // side of the right hypotenuse.
    bool in_right =
        UV.x >= clip_split_u
        && dot(UV - clip_right_point, clip_right_normal) >= 0.0;

    // Drop every fragment that is in neither corner triangle -- that is the
    // central maquette diamond, which shows through behind the desk.
    if (!in_left && !in_right) {
        discard;
    }
    // Keep side: leave COLOR as the SubViewport texture * MODULATE. The
    // desk world (brown floor, iso grid, pawns) is already in TEXTURE.
}
