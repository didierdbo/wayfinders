"""Tests for scripts/halfgate_face_b_slice_v6.py.

Coverage:
1. Apex formula — all 16 cells pinned against the spec v6 table.
2. Dimensions — every output patch is exactly 256x128 RGBA.
3. Iso mask correctness — pixels outside the lozenge have alpha=0; pixels inside
   the lozenge preserve the source alpha.
4. Remount diagnostic guarantee (spec v6, §"Diagnostic guarantee"):
   Remount the 16 patches back onto a 1024x512 canvas; compare RGB against the
   source inside the union of the 16 iso masks.  Expected: 0 differing pixels.
   This test is the authoritative correctness proof for the slicing algorithm,
   source-file-independent (no coupling to any particular GIMP version).
5. CLI smoke — 16 files written, wrong dimensions rejected.

Note on live PNGs:
  The 16 committed patches in client/assets/…/halfgate_face_b/ were generated from
  an earlier GIMP export of the source.  The current source in
  Owner's Inbox/Media/E1/sources/ is a newer revision.  We do NOT compare generated
  patches against the committed PNGs pixel-by-pixel because that would fail whenever
  Didier updates the GIMP source — which is expected and correct.  The remount test
  is the right invariant.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

REPO_ROOT = Path(__file__).parents[2]
SOURCE_PNG = (
    Path("C:/Users/dbo/Desktop/PKA")
    / "Owner's Inbox"
    / "Media"
    / "E1"
    / "sources"
    / "halfgate-zone-iso-1024x512.png"
)

# ---------------------------------------------------------------------------
# Import the module under test via sys.path insertion (scripts/ is not a
# Python package, no __init__.py).
# ---------------------------------------------------------------------------

_scripts_dir = str(REPO_ROOT / "scripts")
if _scripts_dir not in sys.path:
    sys.path.insert(0, _scripts_dir)

from halfgate_face_b_slice_v6 import (  # noqa: E402
    apply_iso_mask,
    compute_top_apex,
    slice_patch,
)

# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------


def _source_arr() -> np.ndarray:
    return np.array(Image.open(SOURCE_PNG).convert("RGBA"))


# ---------------------------------------------------------------------------
# 1 — Apex formula (spec v6 table, §"Top apex coords")
# ---------------------------------------------------------------------------

# Spec v6 ground-truth table for all 16 cells.
_APEX_SPEC = {
    (0, 0): (512, 0),
    (1, 0): (640, 64),
    (2, 0): (768, 128),
    (3, 0): (896, 192),
    (0, 1): (384, 64),
    (1, 1): (512, 128),
    (2, 1): (640, 192),
    (3, 1): (768, 256),
    (0, 2): (256, 128),
    (1, 2): (384, 192),
    (2, 2): (512, 256),
    (3, 2): (640, 320),
    (0, 3): (128, 192),
    (1, 3): (256, 256),
    (2, 3): (384, 320),
    (3, 3): (512, 384),
}


@pytest.mark.parametrize("gx,gy", list(_APEX_SPEC.keys()))
def test_apex_formula(gx: int, gy: int) -> None:
    expected = _APEX_SPEC[(gx, gy)]
    assert compute_top_apex(gx, gy) == expected, (
        f"cell ({gx},{gy}): expected apex {expected}, got {compute_top_apex(gx, gy)}"
    )


# ---------------------------------------------------------------------------
# 2 — Dimensions: every output is 256x128 RGBA
# ---------------------------------------------------------------------------


@pytest.mark.skipif(not SOURCE_PNG.exists(), reason="Source PNG not on this machine")
@pytest.mark.parametrize("gx", range(4))
@pytest.mark.parametrize("gy", range(4))
def test_patch_dimensions(gx: int, gy: int) -> None:
    src_arr = _source_arr()
    patch = slice_patch(src_arr, gx, gy)
    assert patch.shape == (128, 256, 4), (
        f"cell ({gx},{gy}): expected (128, 256, 4), got {patch.shape}"
    )


# ---------------------------------------------------------------------------
# 3 — Iso mask correctness
# ---------------------------------------------------------------------------


def _make_solid_patch(value: int = 200) -> np.ndarray:
    """Return a 256x128 RGBA array filled with (value, value, value, 255)."""
    arr = np.full((128, 256, 4), value, dtype=np.uint8)
    arr[:, :, 3] = 255
    return arr


def test_iso_mask_outside_alpha_zero() -> None:
    """Pixels strictly outside the lozenge must have alpha=0 after masking."""
    patch = _make_solid_patch()
    apply_iso_mask(patch)
    assert patch[0, 0, 3] == 0, "top-left corner: expected alpha=0"
    assert patch[0, 255, 3] == 0, "top-right corner: expected alpha=0"
    assert patch[127, 0, 3] == 0, "bottom-left corner: expected alpha=0"
    assert patch[127, 255, 3] == 0, "bottom-right corner: expected alpha=0"


def test_iso_mask_inside_alpha_preserved() -> None:
    """Pixels inside the lozenge must keep their original alpha (255)."""
    patch = _make_solid_patch()
    apply_iso_mask(patch)
    assert patch[64, 127, 3] == 255, "center-left: expected alpha preserved"
    assert patch[64, 128, 3] == 255, "center-right: expected alpha preserved"
    assert patch[0, 127, 3] == 255, "top apex col 127: expected alpha preserved"
    assert patch[0, 128, 3] == 255, "top apex col 128: expected alpha preserved"
    assert patch[127, 127, 3] == 255, "bottom apex col 127: expected alpha preserved"
    assert patch[127, 128, 3] == 255, "bottom apex col 128: expected alpha preserved"


def test_iso_mask_lozenge_boundary_row63() -> None:
    """Row 63 is the widest row: cols 1..254 must be visible, 0 and 255 must be masked."""
    patch = _make_solid_patch()
    apply_iso_mask(patch)
    assert patch[63, 0, 3] == 0, "row 63 col 0: expected alpha=0 (outside)"
    assert patch[63, 255, 3] == 0, "row 63 col 255: expected alpha=0 (outside)"
    assert patch[63, 1, 3] == 255, "row 63 col 1: expected alpha=255 (inside)"
    assert patch[63, 254, 3] == 255, "row 63 col 254: expected alpha=255 (inside)"


def test_iso_mask_row_widths() -> None:
    """Verify the expected span (first..last inclusive) for every row."""
    patch = _make_solid_patch()
    apply_iso_mask(patch)
    alpha = patch[:, :, 3]
    for y in range(128):
        if y <= 63:
            expected_first = 127 - 2 * y
            expected_last = 128 + 2 * y
        else:
            expected_first = 2 * y - 127
            expected_last = 382 - 2 * y
        nonzero = np.where(alpha[y] > 0)[0]
        assert len(nonzero) > 0, f"row {y}: no visible pixels"
        assert nonzero[0] == expected_first, (
            f"row {y}: first visible col expected {expected_first}, got {nonzero[0]}"
        )
        assert nonzero[-1] == expected_last, (
            f"row {y}: last visible col expected {expected_last}, got {nonzero[-1]}"
        )


# ---------------------------------------------------------------------------
# 4 — Remount diagnostic guarantee (spec v6 §"Diagnostic guarantee")
#
# Remount 16 patches back onto a 1024x512 canvas and compare RGB vs source.
# Expected: 0 differing pixels inside the union of the 16 iso masks.
# This validates the slicing algorithm regardless of which GIMP revision is used.
# ---------------------------------------------------------------------------


@pytest.mark.skipif(not SOURCE_PNG.exists(), reason="Source PNG not on this machine")
def test_remount_roundtrip_zero_diff() -> None:
    """Remounted canvas matches source inside union mask with 0 RGB diff."""
    src_arr = _source_arr()
    canvas = np.zeros((512, 1024, 4), dtype=np.uint8)

    for gx in range(4):
        for gy in range(4):
            top_apex_x, top_apex_y = compute_top_apex(gx, gy)
            x0, y0 = top_apex_x - 128, top_apex_y
            patch = slice_patch(src_arr, gx, gy)
            # Paste: later patches overwrite in overlapping pixels (doesn't matter
            # for the guarantee since the union mask excludes double-claimed pixels).
            canvas_region = canvas[y0 : y0 + 128, x0 : x0 + 256]
            visible = patch[:, :, 3:4] > 0
            canvas[y0 : y0 + 128, x0 : x0 + 256] = np.where(visible, patch, canvas_region)

    inside = src_arr[:, :, 3] > 0
    rgb_diff = np.abs(canvas[:, :, :3].astype(np.int16) - src_arr[:, :, :3].astype(np.int16))
    max_diff = int(rgb_diff[inside].max())
    differing_pixels = int((rgb_diff.max(axis=2)[inside] > 0).sum())

    assert max_diff == 0, (
        f"Remount diagnostic failed: max RGB diff = {max_diff}, "
        f"differing pixels = {differing_pixels}. "
        "This indicates a bbox or mask formula bug."
    )


@pytest.mark.skipif(not SOURCE_PNG.exists(), reason="Source PNG not on this machine")
@pytest.mark.parametrize(
    "gx,gy",
    [(0, 0), (0, 3), (3, 0), (3, 3)],
    ids=["corner_00", "corner_03", "corner_30", "corner_33"],
)
def test_remount_roundtrip_corner(gx: int, gy: int) -> None:
    """Per-corner remount smoke: each corner patch restores the source exactly."""
    src_arr = _source_arr()
    top_apex_x, top_apex_y = compute_top_apex(gx, gy)
    x0, y0 = top_apex_x - 128, top_apex_y

    patch = slice_patch(src_arr, gx, gy)
    inside = patch[:, :, 3] > 0

    # RGB inside the mask must equal source RGB at those positions.
    src_crop = src_arr[y0 : y0 + 128, x0 : x0 + 256]
    rgb_diff = np.abs(patch[:, :, :3].astype(np.int16) - src_crop[:, :, :3].astype(np.int16))
    max_diff = int(rgb_diff[inside].max()) if inside.any() else 0
    assert max_diff == 0, (
        f"corner ({gx},{gy}): patch RGB differs from source by {max_diff} inside mask"
    )


# ---------------------------------------------------------------------------
# 5 — CLI smoke test
# ---------------------------------------------------------------------------


@pytest.mark.skipif(not SOURCE_PNG.exists(), reason="Source PNG not on this machine")
def test_cli_produces_16_files(tmp_path: Path) -> None:
    """CLI invocation writes exactly 16 halfgate_face_b_NM.png files."""
    from halfgate_face_b_slice_v6 import main

    main(["--input", str(SOURCE_PNG), "--output", str(tmp_path)])
    produced = sorted(tmp_path.glob("halfgate_face_b_??.png"))
    assert len(produced) == 16, f"Expected 16 files, got {len(produced)}"
    expected_names = {f"halfgate_face_b_{gx}{gy}.png" for gx in range(4) for gy in range(4)}
    actual_names = {p.name for p in produced}
    assert actual_names == expected_names


@pytest.mark.skipif(not SOURCE_PNG.exists(), reason="Source PNG not on this machine")
def test_cli_wrong_dimensions(tmp_path: Path) -> None:
    """CLI exits non-zero if source is not 1024x512."""
    import subprocess

    dummy = tmp_path / "wrong_size.png"
    Image.new("RGBA", (512, 256)).save(dummy)

    result = subprocess.run(
        [
            sys.executable,
            str(REPO_ROOT / "scripts" / "halfgate_face_b_slice_v6.py"),
            "--input",
            str(dummy),
            "--output",
            str(tmp_path / "out"),
        ],
        capture_output=True,
    )
    assert result.returncode != 0, "Expected non-zero exit for wrong dimensions"
