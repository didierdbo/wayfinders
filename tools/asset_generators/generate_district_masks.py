"""
Generate the Halfgate district silhouette masks (J2 deliverable).

Varn world-referential spec 2026-05-20 §5: each district is defined by a
black/white bitmap mask (white = inside district, black = outside) painted in
its own absolute world frame at 1 pixel = 2 m. The two M1 districts of
Halfgate -- ``lower_quays`` and ``high_wall`` -- must interlock like puzzle
pieces (spec §5.2): no overlap, no gap along their shared boundary.

This script paints both masks from a single shared world-frame description so
the interlock is correct by construction, then writes each mask cropped to its
own ``anchor`` (the SW corner registered in world.yaml).

Geometry (world meters, 2 m grid)
---------------------------------
Both districts live inside the puzzle block [91900, 92300] x [37700, 38200].
The block is split by an L-shaped seam:

    y=38200  +-------------------+
             |                   |
             |     high_wall     |   high_wall is the upper-right L:
             |                   |   the whole top band + a right-side
    y=37960  +-----------+       |   arm that drops down to y=37820.
             |           |       |
             | lower_    | h_wall |  lower_quays is the lower-left L:
             | quays     | arm    |  the whole bottom band + a left-side
    y=37820  |           +-------+   block that rises to y=37960.
             |                   |
    y=37700  +-------------------+
           x=91900   x=92120   x=92300

The seam is a step at (x=92120, y=37820)-(y=37960). The two silhouettes share
their boundary pixels exactly; their interior pixels never overlap.

Output
------
* ``assets/world/cities/halfgate/lower_quays_mask.png`` -- 150x150 px (300x300 m)
* ``assets/world/cities/halfgate/high_wall_mask.png``   -- 200x250 px (400x500 m)

Each PNG is 8-bit grayscale, hard black/white (no anti-aliasing -- the spec
mandates a hard threshold at authoring time).

Dependencies
------------
numpy only (a wayfinders runtime dependency). PNG is written with the stdlib
``zlib`` module -- no Pillow, matching the repo's stdlib-first tooling.
"""

from __future__ import annotations

import struct
import zlib
from pathlib import Path

import numpy as np

_REPO_ROOT = Path(__file__).resolve().parent.parent.parent
_HALFGATE_DIR = _REPO_ROOT / "assets" / "world" / "cities" / "halfgate"

CELL_M = 2  # 1 pixel = 2 m (Varn spec §1.3)

# --- District registrations (must match world.yaml) -----------------------
# anchor = SW corner of the bitmap in world meters.
LOWER_QUAYS = {
    "id": "lower_quays",
    "anchor": (91900, 37700),
    "size_px": (150, 150),  # 300 m x 300 m
}
HIGH_WALL = {
    "id": "high_wall",
    "anchor": (92000, 37700),
    "size_px": (150, 250),  # 300 m x 500 m
}

# --- Puzzle block, in world meters -----------------------------------------
BLOCK_X0, BLOCK_X1 = 91900, 92300
BLOCK_Y0, BLOCK_Y1 = 37700, 38200
SEAM_X = 92120  # vertical seam between lower_quays' raised block and h_wall arm
SEAM_Y_LO = 37820  # lower_quays left block rises to here
SEAM_Y_HI = 37960  # high_wall band drops to here


# ===========================================================================
# Minimal stdlib PNG writer (8-bit grayscale)
# ===========================================================================


def _write_png_grayscale(path: Path, arr: np.ndarray) -> None:
    """Write a HxW uint8 array as an 8-bit grayscale PNG (filter type 0)."""
    h, w = arr.shape
    raw = bytearray()
    for y in range(h):
        raw.append(0)  # filter type: None
        raw.extend(arr[y].tobytes())

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    ihdr = struct.pack(">IIBBBBB", w, h, 8, 0, 0, 0, 0)  # 8-bit, grayscale
    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png)


# ===========================================================================
# District silhouette painting in world frame
# ===========================================================================


def _world_membership(district_id: str, wx: int, wy: int) -> bool:
    """True if world cell (wx, wy) belongs to ``district_id``.

    ``wx``/``wy`` are the SW corner of a 2 m cell. The L-shapes are defined so
    every cell of the puzzle block belongs to exactly one district -- the
    interlock is exact, no overlap, no gap.
    """
    if district_id == "lower_quays":
        # Lower-left L: full bottom band, plus a left block rising to SEAM_Y_HI.
        in_bottom_band = BLOCK_X0 <= wx < BLOCK_X1 and BLOCK_Y0 <= wy < SEAM_Y_LO
        in_left_block = BLOCK_X0 <= wx < SEAM_X and SEAM_Y_LO <= wy < SEAM_Y_HI
        return in_bottom_band or in_left_block

    if district_id == "high_wall":
        # Upper-right L: full top band, plus a right arm dropping to SEAM_Y_LO.
        in_top_band = BLOCK_X0 <= wx < BLOCK_X1 and SEAM_Y_HI <= wy < BLOCK_Y1
        in_right_arm = SEAM_X <= wx < BLOCK_X1 and SEAM_Y_LO <= wy < SEAM_Y_HI
        return in_top_band or in_right_arm

    raise ValueError(f"Unknown district id: {district_id}")


def paint_mask(district: dict) -> np.ndarray:
    """Render a district's silhouette mask in its own bitmap frame.

    Returns a HxW uint8 array, 255 = inside (white), 0 = outside (black).
    Row 0 is the TOP of the image; the bitmap's bottom row maps to the
    district anchor (SW corner) in world meters.
    """
    ax, ay = district["anchor"]
    w, h = district["size_px"]
    arr = np.zeros((h, w), dtype=np.uint8)
    for py in range(h):
        for px in range(w):
            # Pixel (px, py): row 0 is top, so world Y is flipped.
            wx = ax + px * CELL_M
            wy = ay + (h - 1 - py) * CELL_M
            if _world_membership(district["id"], wx, wy):
                arr[py, px] = 255
    return arr


# ===========================================================================
# Interlock self-check
# ===========================================================================


def _verify_interlock() -> None:
    """Assert the two L-shapes tile the puzzle block with no overlap, no gap."""
    overlap = 0
    gap = 0
    for wy in range(BLOCK_Y0, BLOCK_Y1, CELL_M):
        for wx in range(BLOCK_X0, BLOCK_X1, CELL_M):
            in_lq = _world_membership("lower_quays", wx, wy)
            in_hw = _world_membership("high_wall", wx, wy)
            if in_lq and in_hw:
                overlap += 1
            if not in_lq and not in_hw:
                gap += 1
    if overlap or gap:
        raise AssertionError(
            f"Interlock check FAILED: {overlap} overlapping cells, {gap} gap cells."
        )
    print("Interlock check OK: puzzle block fully tiled, no overlap, no gap.")


# ===========================================================================
# Main
# ===========================================================================


def main() -> int:
    _HALFGATE_DIR.mkdir(parents=True, exist_ok=True)
    _verify_interlock()

    for district in (LOWER_QUAYS, HIGH_WALL):
        arr = paint_mask(district)
        white = int((arr == 255).sum())
        out = _HALFGATE_DIR / f"{district['id']}_mask.png"
        _write_png_grayscale(out, arr)
        h, w = arr.shape
        print(f"OK: wrote {out} ({w}x{h} px, {white} white cells = {white * CELL_M * CELL_M} m^2).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
