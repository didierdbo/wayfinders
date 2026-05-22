# ============================================================================
#  Wayfinders -- Meshes Jalon Runner  (Blender 5.1 headless bpy script)
# ----------------------------------------------------------------------------
#  Author : Mira (AI Media Generation Specialist)
#  Date   : 2026-05-22  (jalon "meshes" -- eM world map + eT district meshes)
#
#  WHAT THIS DOES
#  --------------
#  Re-uses the EXISTING Blender pipeline (map_terrain_mesh_script.py v3.1) as a
#  library. That script: bitmap silhouette -> traced contour -> extruded iso
#  mesh -> orthographic 2:1 iso camera. This runner does NOT re-implement any
#  of that. It:
#    1. imports map_terrain_mesh_script as a module (overrides its PARAMETERS
#       block per target),
#    2. builds the scene fresh for each of the 3 targets,
#    3. adds the render step the interactive script leaves to the user (F12),
#    4. writes the iso-render PNG straight into the Wayfinders repo,
#    5. emits a calage sidecar JSON next to each PNG.
#
#  3 TARGETS
#  ---------
#    A. eM world map   -- continent silhouette  (2944x1648 src -> 2048x1024 iso)
#    B. eT lower_quays -- district, COMMON-FRAME composite 200x250 px
#    C. eT high_wall   -- district, COMMON-FRAME composite 200x250 px
#
#  THE CALAGE / PUZZLE FIX
#  -----------------------
#  The two districts are rendered from ONE common world frame
#  ([91900,92300]x[37700,38200] = 400m x 500m). Both share the same iso
#  camera, the same meters-per-pixel and the same iso origin -> their PNGs
#  overlay pixel-exact in Godot and interlock as the puzzle. The districts are
#  rendered with a TRANSPARENT background (no sea/shadow plane) so only the
#  painted L-shape is opaque; the two PNGs can be stacked without one masking
#  the other. The world map keeps its opaque sea (it is the eM backdrop, it
#  interlocks with nothing).
#
#  OUTPUT FORMAT DECISION (Mira, locked 2026-05-22)
#  ------------------------------------------------
#  Iso-render PNGs, NOT exported OBJ/glTF meshes. Rationale:
#    - Godot IsoBoard already consumes a BackgroundTexturePath (a Sprite2D,
#      Centered=false, top-left pinned to a documented Position).
#    - MVP world is flat, zero elevation (Didier 2026-05-21) -> a 3D mesh adds
#      nothing playable; Godot 2D cannot import glTF into a Sprite2D anyway.
#    - Calage becomes a pure 2D pixel<->meter transform, documented in a
#      sidecar JSON. Coherent with the world-map + Game-Screen-decor pipeline.
#  The Blender mesh stays the PRODUCTION step (true 90deg cliffs, DNA shading);
#  we ship its RENDER, not its geometry.
#
#  HOW TO RUN (headless)
#  ---------------------
#    1. python composite_district_frame.py        (builds the composites)
#    2. "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" ^
#         --background --python "...\generate_meshes_jalon.py"
#
#  Re-runnable: each target rebuilds from a clean factory scene.
# ============================================================================

import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy

# --- Paths ------------------------------------------------------------------
MEDIA_DIR = Path(r"C:\Users\dbo\Desktop\PKA\Owner's Inbox\Media\wayfinders")
PIPELINE_PATH = MEDIA_DIR / "map_terrain_mesh_script.py"
REPO = Path(r"C:\Users\dbo\Desktop\didierdbo\wayfinders")

WORLD_SRC = MEDIA_DIR / "Pure_white_silhouette_map_of_a_single_flat_continent.png"
# Districts rendered from the COMMON frame composites (see module docstring
# and composite_district_frame.py). Both are 200x250 px.
LOWER_QUAYS_SRC = MEDIA_DIR / "lower_quays_commonframe.png"
HIGH_WALL_SRC = MEDIA_DIR / "high_wall_commonframe.png"

# Output dirs in the repo.
EM_OUT_DIR = REPO / "client" / "assets" / "wayfinders_visual_assets" / "e2"
ET_OUT_DIR = REPO / "client" / "assets" / "wayfinders_visual_assets" / "e3"

# Common district world frame (must match composite_district_frame.py).
DISTRICT_FRAME_SW = {"x": 91900, "y": 37700}
DISTRICT_FRAME_W_M = 400
DISTRICT_FRAME_H_M = 500
DISTRICT_FRAME_W_PX = 200
DISTRICT_FRAME_H_PX = 250


# ----------------------------------------------------------------------------
#  Import the existing pipeline WITHOUT auto-running its build()
# ----------------------------------------------------------------------------


def load_pipeline_module():
    """Load map_terrain_mesh_script as a module, stripping its trailing
    bare build() call so importing it does not build the wrong scene."""
    src = PIPELINE_PATH.read_text(encoding="utf-8")
    lines = src.splitlines()
    while lines and lines[-1].strip() in ("", "build()"):
        if lines[-1].strip() == "build()":
            lines.pop()
            break
        lines.pop()
    src_no_autorun = "\n".join(lines)

    spec = importlib.util.spec_from_loader("wf_pipeline", loader=None)
    mod = importlib.util.module_from_spec(spec)
    sys.modules["wf_pipeline"] = mod
    exec(compile(src_no_autorun, str(PIPELINE_PATH), "exec"), mod.__dict__)
    return mod


def reset_scene():
    """Start each target from a clean factory-empty scene."""
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ----------------------------------------------------------------------------
#  Target configuration
# ----------------------------------------------------------------------------

TARGETS = [
    {
        "key": "world",
        "label": "eM world map (continent)",
        "bitmap": WORLD_SRC,
        "bitmap_w": 2944,
        "bitmap_h": 1648,
        "render_w": 2048,
        "render_h": 1024,  # locked iso render (spec 1.2)
        "land_height": 0.16,
        "out_dir": EM_OUT_DIR,
        "out_png": "wf_e2_world_map_mesh_iso_2048x1024.png",
        "world_m_x": 500000,
        "world_m_y": 250000,
        "anchor_m": {"x": 0, "y": 0},
        "ignore_holes": True,
        "transparent": False,  # keep the opaque sea
    },
    {
        "key": "lower_quays",
        "label": "eT district -- Lower Quays (common frame)",
        "bitmap": LOWER_QUAYS_SRC,
        "bitmap_w": DISTRICT_FRAME_W_PX,
        "bitmap_h": DISTRICT_FRAME_H_PX,
        "render_w": 1600,
        "render_h": 800,  # 2:1 iso, 8x frame upscale
        "land_height": 0.18,
        "out_dir": ET_OUT_DIR,
        "out_png": "wf_e3_district_lower_quays_mesh_iso_1600x800.png",
        "world_m_x": DISTRICT_FRAME_W_M,
        "world_m_y": DISTRICT_FRAME_H_M,
        "anchor_m": dict(DISTRICT_FRAME_SW),
        "ignore_holes": True,
        "transparent": True,  # only the L-shape is opaque
    },
    {
        "key": "high_wall",
        "label": "eT district -- High Wall (common frame)",
        "bitmap": HIGH_WALL_SRC,
        "bitmap_w": DISTRICT_FRAME_W_PX,
        "bitmap_h": DISTRICT_FRAME_H_PX,
        "render_w": 1600,
        "render_h": 800,
        "land_height": 0.18,
        "out_dir": ET_OUT_DIR,
        "out_png": "wf_e3_district_high_wall_mesh_iso_1600x800.png",
        "world_m_x": DISTRICT_FRAME_W_M,
        "world_m_y": DISTRICT_FRAME_H_M,
        "anchor_m": dict(DISTRICT_FRAME_SW),
        "ignore_holes": True,
        "transparent": True,
    },
]


# ----------------------------------------------------------------------------
#  Configure the pipeline module's PARAMETERS for one target
# ----------------------------------------------------------------------------


def configure_pipeline(pipeline, target):
    """Overwrite the pipeline module's PARAMETERS block for one target."""
    pipeline.BITMAP_PATH = str(target["bitmap"])
    pipeline.INVERT_BITMAP = False
    pipeline.MESH_MODE = "traced"
    pipeline.BINARIZE_THRESHOLD = 0.5
    pipeline.CONTOUR_SIMPLIFY_EPSILON = 1.0
    pipeline.MIN_CONTOUR_AREA = 12.0
    pipeline.IGNORE_INTERIOR_HOLES = target["ignore_holes"]
    pipeline.MAX_HOLE_AREA_RATIO = 0.10
    pipeline.BEVEL_AMOUNT = 0.0

    # Geometry: PLANE_WIDTH fixed, aspect from the bitmap -> no stretch.
    pipeline.PLANE_WIDTH = 20.0
    pipeline.PLANE_ASPECT = target["bitmap_w"] / target["bitmap_h"]
    pipeline.LAND_HEIGHT = target["land_height"]
    pipeline.SEA_DEPTH = 0.0

    pipeline.RENDER_W = target["render_w"]
    pipeline.RENDER_H = target["render_h"]
    pipeline.RENDER_SAMPLES = 96
    pipeline.USE_CYCLES = True
    pipeline.CAM_ORTHO_SCALE = pipeline.PLANE_WIDTH * 1.10

    # DNA Wayfinders -- terracotta / ochre / parchment / umber.
    pipeline.LAND_COLOR_TOP = (0.78, 0.62, 0.42, 1.0)
    pipeline.LAND_COLOR_SIDE = (0.55, 0.35, 0.22, 1.0)
    pipeline.SEA_COLOR = (0.10, 0.18, 0.28, 1.0)
    pipeline.SHADOW_COLOR = (0.18, 0.14, 0.11, 1.0)


def add_lighting():
    """Factory-empty scenes carry no lights -- add a warm key sun + ambient."""
    sun_data = bpy.data.lights.new("Sun_Key", type="SUN")
    sun_data.energy = 3.2
    sun_data.angle = math.radians(2.0)
    sun_data.color = (1.0, 0.96, 0.88)
    sun = bpy.data.objects.new("Sun_Key", sun_data)
    bpy.context.scene.collection.objects.link(sun)
    # Raking from NE-up so cliffs throw a soft SW shadow (asset-v2 convention).
    sun.rotation_euler = (math.radians(52.0), 0.0, math.radians(215.0))

    world = bpy.data.worlds.new("WF_World")
    bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.42, 0.40, 0.36, 1.0)
        bg.inputs["Strength"].default_value = 0.55


def strip_sea_and_shadow(pipeline):
    """For transparent district renders: drop the sea + shadow planes the
    pipeline's build() always adds, so only the extruded L-shape is opaque."""
    for name in (pipeline.SEA_NAME, pipeline.SHADOW_NAME):
        if name in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)


# ----------------------------------------------------------------------------
#  Calage sidecar
# ----------------------------------------------------------------------------


def write_calage_sidecar(target, png_path: Path) -> Path:
    """Emit <png>.calage.json: the pixel<->meter contract Rune wires at J5."""
    render_w, render_h = target["render_w"], target["render_h"]
    world_x, world_y = target["world_m_x"], target["world_m_y"]
    is_world = target["key"] == "world"

    render_px_per_m_x = render_w / world_x
    render_px_per_m_y = render_h / world_y

    sidecar = {
        "_comment": (
            "Calage sidecar for the Wayfinders 'meshes' jalon. Tells Rune "
            "how this iso-render PNG registers onto a Godot IsoBoard at J5. "
            "Produced by Mira 2026-05-22. Regeneratable -- do not hand-edit."
        ),
        "_generator": "tools/asset_generators/generate_meshes_jalon.py",
        "target": target["key"],
        "layer": "eM" if is_world else "eT",
        "render_png": png_path.name,
        "render_size_px": {"w": render_w, "h": render_h},
        "background_transparent": target["transparent"],
        "iso_projection": {
            "ratio": "2:1",
            "camera": "orthographic, rot_x=60deg, rot_z=45deg",
            "note": (
                "Render canvas is 2:1 (w = 2*h). The PNG is an iso view of a "
                "flat extruded slab -- the silhouette IS the land/district "
                "outline."
            ),
        },
        "world_frame_m": {
            "size_x_m": world_x,
            "size_y_m": world_y,
            "frame_sw_anchor_m": target["anchor_m"],
            "anchor_note": (
                "SW corner of the rendered world frame, in world meters "
                "(world.yaml convention: origin SW, X east, Y north)."
            ),
        },
        "ground_scale": {
            "meters_per_src_px_x": world_x / target["bitmap_w"],
            "meters_per_src_px_y": world_y / target["bitmap_h"],
            "src_bitmap_size_px": {
                "w": target["bitmap_w"],
                "h": target["bitmap_h"],
            },
        },
        "render_scale": {
            "render_px_per_world_m_x": round(render_px_per_m_x, 6),
            "render_px_per_world_m_y": round(render_px_per_m_y, 6),
            "note": (
                "Pixels of the RENDERED PNG per world meter. Use this to size "
                "the Sprite2D against the eT/eM camera."
            ),
        },
        "godot_placement": {
            "sprite_centered": False,
            "background_texture_path_target": "IsoBoard.BackgroundTexturePath",
        },
    }

    if is_world:
        sidecar["godot_placement"]["note"] = (
            "eM backdrop. Wire as IsoBoard.BackgroundTexturePath; Sprite2D "
            "Centered=false, top-left at the board origin. Cities pin as POIs "
            "via city.position (world meters) -> render pixels using "
            "render_scale. Halfgate placeholder (92000,38000) is tunable "
            "until this coastline render is signed off (Varn spec section 7)."
        )
    else:
        sidecar["godot_placement"]["note"] = (
            "eT district. BOTH district PNGs share this identical common "
            "world frame, camera and scale -- pose BOTH Sprite2D nodes at the "
            "SAME top-left and they overlay pixel-exact and interlock as the "
            "puzzle (zero-overlap verified at composite time). Background is "
            "transparent so stacking them does not mask either silhouette. "
            "Each district's own anchor and outline polygon live in "
            "assets/world/cities/halfgate/<id>_mask.polygon.json."
        )
        sidecar["puzzle_interlock"] = {
            "common_frame": True,
            "districts_in_frame": ["lower_quays", "high_wall"],
            "shared_block_m": "[91900,92300] x [37700,38200]",
            "verified": (
                "composite_district_frame.py reports 0 px overlap between "
                "the two silhouettes in the common frame."
            ),
        }

    sidecar_path = png_path.with_name(png_path.name + ".calage.json")
    sidecar_path.write_text(json.dumps(sidecar, indent=2), encoding="utf-8")
    print(f"[Mira] Calage sidecar -> {sidecar_path}")
    return sidecar_path


# ----------------------------------------------------------------------------
#  Build + render one target
# ----------------------------------------------------------------------------


def build_one(pipeline, target) -> Path:
    """Build the scene, render the iso PNG, emit the calage sidecar."""
    print(f"\n[Mira] ===== TARGET: {target['label']} =====")
    reset_scene()
    configure_pipeline(pipeline, target)

    pipeline.build()

    if target["transparent"]:
        strip_sea_and_shadow(pipeline)

    add_lighting()

    out_dir = Path(target["out_dir"])
    out_dir.mkdir(parents=True, exist_ok=True)
    png_path = out_dir / target["out_png"]
    scn = bpy.context.scene
    scn.render.filepath = str(png_path)
    scn.render.image_settings.file_format = "PNG"
    scn.render.image_settings.color_mode = "RGBA"
    scn.render.film_transparent = bool(target["transparent"])
    print(f"[Mira] Rendering (transparent={target['transparent']}) -> {png_path}")
    bpy.ops.render.render(write_still=True)

    write_calage_sidecar(target, png_path)
    print(f"[Mira] Done: {target['label']}")
    return png_path


def main() -> None:
    print("[Mira] Loading existing pipeline as a library...")
    pipeline = load_pipeline_module()
    print("[Mira] Pipeline loaded (map_terrain_mesh_script v3.1).")

    results = []
    for target in TARGETS:
        try:
            png = build_one(pipeline, target)
            results.append((target["key"], str(png), "OK"))
        except Exception as exc:
            import traceback

            traceback.print_exc()
            results.append((target["key"], "-", f"FAILED: {exc}"))

    print("\n[Mira] ===== JALON MESHES SUMMARY =====")
    for key, png, status in results:
        print(f"[Mira]   {key:14s} {status}  {png}")
    print("[Mira] =================================")


main()
