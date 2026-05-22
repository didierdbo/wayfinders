# ============================================================================
#  Wayfinders -- District common-frame compositor  (jalon "meshes", eT)
# ----------------------------------------------------------------------------
#  Author : Mira (AI Media Generation Specialist)
#  Date   : 2026-05-22
#
#  WHY THIS EXISTS -- the calage problem
#  -------------------------------------
#  The two M1 districts have DIFFERENT bitmap frames:
#    lower_quays : 150x150 px  -> 300m x 300m, anchor SW (91900, 37700)
#    high_wall   : 150x250 px  -> 300m x 500m, anchor SW (92000, 37700)
#  If each is rendered in its own frame, the iso camera frames a different
#  rectangle each time -> different meters-per-pixel, different iso origin.
#  The two PNGs would NOT overlay pixel-exact in Godot, so the puzzle would
#  not interlock cleanly.
#
#  THE FIX -- one common world frame
#  ---------------------------------
#  Both districts live inside the block [91900,92300] x [37700,38200]
#  (world.yaml districts comment). That block is 400m x 500m. At 2 m/px that
#  is a 200 x 250 px canvas. We composite EACH district mask, at its true
#  offset, into a black 200x250 canvas. Then both are rendered from the SAME
#  common frame: identical iso camera, meters-per-pixel and iso origin. The
#  two iso-render PNGs then overlay pixel-exact in Godot -- posed at the SAME
#  top-left they interlock as the puzzle by construction.
#
#  Axis note: world Y grows NORTH (up); image Y grows DOWN. The SW corner of
#  the world frame is the BOTTOM-LEFT of the image. We composite with that
#  flip so the silhouettes land correctly.
#
#  OUTPUT
#  ------
#    lower_quays_commonframe.png   200x250 px  (black bg, white silhouette)
#    high_wall_commonframe.png     200x250 px
#  Both written into Mira's media workspace, consumed by the Blender runner.
# ============================================================================

from pathlib import Path

from PIL import Image

MEDIA_DIR = Path(r"C:\Users\dbo\Desktop\PKA\Owner's Inbox\Media\wayfinders")
REPO = Path(r"C:\Users\dbo\Desktop\didierdbo\wayfinders")
HALFGATE = REPO / "assets" / "world" / "cities" / "halfgate"

# --- Common world frame (from world.yaml districts comment) -----------------
CELL_M = 2  # meters per pixel
FRAME_SW = (91900, 37700)  # SW corner, world meters
FRAME_W_M = 400  # 92300 - 91900
FRAME_H_M = 500  # 38200 - 37700
FRAME_W_PX = FRAME_W_M // CELL_M  # 200
FRAME_H_PX = FRAME_H_M // CELL_M  # 250

DISTRICTS = [
    {
        "id": "lower_quays",
        "mask": HALFGATE / "lower_quays_mask.png",
        "anchor": (91900, 37700),  # SW of this district's own bitmap
    },
    {
        "id": "high_wall",
        "mask": HALFGATE / "high_wall_mask.png",
        "anchor": (92000, 37700),
    },
]


def composite_one(district: dict) -> Path:
    """Embed one district mask into the shared common-frame canvas."""
    mask = Image.open(district["mask"]).convert("L")
    mask_w, mask_h = mask.size

    # Offset of this district's SW corner inside the common frame, in pixels.
    off_x_m = district["anchor"][0] - FRAME_SW[0]
    off_y_m = district["anchor"][1] - FRAME_SW[1]
    off_x_px = off_x_m // CELL_M
    off_y_px_from_sw = off_y_m // CELL_M

    # Image paste is top-down. The district SW corner sits off_y_px_from_sw
    # pixels ABOVE the frame SW (north). Convert to a top-down paste Y.
    paste_x = off_x_px
    paste_top = FRAME_H_PX - off_y_px_from_sw - mask_h

    canvas = Image.new("L", (FRAME_W_PX, FRAME_H_PX), 0)  # black bg
    canvas.paste(mask, (paste_x, paste_top))

    out_path = MEDIA_DIR / f"{district['id']}_commonframe.png"
    canvas.save(out_path)
    white = sum(1 for p in canvas.get_flattened_data() if p >= 128)
    print(
        f"[Mira] {district['id']}: mask {mask_w}x{mask_h} pasted at "
        f"({paste_x},{paste_top}) -> {out_path}  ({white} white px)"
    )
    return out_path


def main() -> None:
    print(
        f"[Mira] Common world frame: SW={FRAME_SW}, "
        f"{FRAME_W_M}m x {FRAME_H_M}m = {FRAME_W_PX}x{FRAME_H_PX} px"
    )
    for district in DISTRICTS:
        composite_one(district)
    print("[Mira] Common-frame composites done.")


if __name__ == "__main__":
    main()
