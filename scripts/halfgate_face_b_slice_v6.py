"""
Slice halfgate-zone-iso-1024x512.png into 16 iso-lozenge patches.

Spec v6 (Varn-locked 2026-05-13):
  - Source: 1024x512 RGBA already projected in iso by Didier in GIMP.
  - No warp, no rotation, no resampling.
  - Grid topology: cell (gx, gy) in [0..3] x [0..3]
      top_apex_x = 512 + (gx - gy) * 128
      top_apex_y =       (gx + gy) *  64
      bbox       = (top_apex_x - 128, top_apex_y, top_apex_x + 128, top_apex_y + 128)
  - Iso mask: pixel-perfect diamond, alpha=0 outside the lozenge.
  - Output: halfgate_face_b_<gx><gy>.png  (N=gx, M=gy), 256x128 RGBA.

Usage:
    python scripts/halfgate_face_b_slice_v6.py --input <source.png> --output <outdir>
"""

from __future__ import annotations

import argparse
import logging
import sys
from pathlib import Path

import numpy as np
from PIL import Image

logger = logging.getLogger(__name__)


def apply_iso_mask(patch: np.ndarray) -> np.ndarray:
    """Apply pixel-perfect iso lozenge mask to a 256x128 RGBA patch array.

    The lozenge has its top apex at column 127-128 (row 0), widens to the full
    width at row 63-64 (left apex at col 1, right apex at col 254), then narrows
    back to the bottom apex at row 127.

    Pixels outside the lozenge get alpha=0; pixels inside keep their source alpha.

    Args:
        patch: numpy array of shape (128, 256, 4), dtype uint8.  Modified in-place.

    Returns:
        The same array (modified in-place for efficiency).
    """
    h, w = patch.shape[:2]  # h=128, w=256
    for y in range(h):
        if y <= 63:
            # Top half: diamond widens from apex downward.
            first = 127 - 2 * y
            last = 128 + 2 * y
        else:
            # Bottom half: diamond narrows toward bottom apex.
            first = 2 * y - 127
            last = 382 - 2 * y
        if first > 0:
            patch[y, :first, 3] = 0
        if last < w - 1:
            patch[y, last + 1 :, 3] = 0
    return patch


def compute_top_apex(gx: int, gy: int) -> tuple[int, int]:
    """Return (top_apex_x, top_apex_y) for grid cell (gx, gy)."""
    return 512 + (gx - gy) * 128, (gx + gy) * 64


def slice_patch(src_arr: np.ndarray, gx: int, gy: int) -> np.ndarray:
    """Crop and mask a single iso patch from the source array.

    Args:
        src_arr: numpy array of shape (512, 1024, 4), dtype uint8.
        gx: grid x index [0..3].
        gy: grid y index [0..3].

    Returns:
        numpy array of shape (128, 256, 4), dtype uint8 with iso mask applied.
    """
    top_apex_x, top_apex_y = compute_top_apex(gx, gy)
    x0 = top_apex_x - 128
    y0 = top_apex_y
    x1 = top_apex_x + 128  # exclusive
    y1 = top_apex_y + 128  # exclusive
    patch = src_arr[y0:y1, x0:x1].copy()
    apply_iso_mask(patch)
    return patch


def slice_all(src_path: Path, out_dir: Path) -> None:
    """Slice source image into 16 iso patches and write them to out_dir.

    Args:
        src_path: Path to the 1024x512 RGBA source PNG.
        out_dir: Directory where the 16 output PNGs will be written.

    Raises:
        SystemExit: On invalid source dimensions or missing file.
    """
    if not src_path.exists():
        logger.error("Source file not found: %s", src_path)
        sys.exit(1)

    src = Image.open(src_path).convert("RGBA")
    if src.size != (1024, 512):
        logger.error(
            "Expected source 1024x512, got %dx%d: %s",
            src.size[0],
            src.size[1],
            src_path,
        )
        sys.exit(1)

    out_dir.mkdir(parents=True, exist_ok=True)
    src_arr = np.array(src)

    for gx in range(4):
        for gy in range(4):
            patch = slice_patch(src_arr, gx, gy)
            out_path = out_dir / f"halfgate_face_b_{gx}{gy}.png"
            Image.fromarray(patch, mode="RGBA").save(out_path, format="PNG")
            logger.info("Wrote %s", out_path)

    logger.info("Done — 16 patches written to %s", out_dir)


def main(argv: list[str] | None = None) -> None:
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    parser = argparse.ArgumentParser(
        description="Slice halfgate-zone-iso-1024x512.png into 16 iso patches (spec v6)."
    )
    parser.add_argument(
        "--input", required=True, type=Path, help="Path to 1024x512 RGBA source PNG."
    )
    parser.add_argument(
        "--output",
        required=True,
        type=Path,
        help="Output directory for the 16 halfgate_face_b_NM.png patches.",
    )
    args = parser.parse_args(argv)
    slice_all(args.input, args.output)


if __name__ == "__main__":
    main()
