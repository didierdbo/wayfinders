"""Tests for tools/extract_district_polygon.py.

Covers:
  - PNG round-trip (stdlib writer -> stdlib decoder).
  - Marching-squares outer contour on a known rectangle and an L-shape.
  - Collinear-vertex pruning (a rectangle stays 4 vertices).
  - Pixel-corner -> world-meter conversion (anchor offset + Y flip).
  - The committed Halfgate sidecars: real shape, puzzle interlock.

The tool lives in tools/ (not an importable package), so we load it by path.
"""

from __future__ import annotations

import importlib.util
import struct
import zlib
from pathlib import Path

import numpy as np
import pytest

_REPO_ROOT = Path(__file__).resolve().parent.parent.parent
_TOOL_PATH = _REPO_ROOT / "tools" / "extract_district_polygon.py"
_HALFGATE = _REPO_ROOT / "assets" / "world" / "cities" / "halfgate"


def _load_tool():
    spec = importlib.util.spec_from_file_location("extract_district_polygon", _TOOL_PATH)
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


tool = _load_tool()


# ---------------------------------------------------------------------------
# PNG helpers
# ---------------------------------------------------------------------------


def _write_png_grayscale(path: Path, arr: np.ndarray) -> None:
    """Minimal 8-bit grayscale PNG writer (mirrors generate_district_masks)."""
    h, w = arr.shape
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw.extend(arr[y].tobytes())

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    ihdr = struct.pack(">IIBBBBB", w, h, 8, 0, 0, 0, 0)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )


# ---------------------------------------------------------------------------
# PNG round-trip
# ---------------------------------------------------------------------------


class TestPngRoundTrip:
    def test_decode_matches_written(self, tmp_path: Path) -> None:
        arr = np.zeros((8, 10), dtype=np.uint8)
        arr[2:6, 3:7] = 255
        png = tmp_path / "rt.png"
        _write_png_grayscale(png, arr)
        decoded = tool._decode_png_grayscale(png)
        assert decoded.shape == (8, 10)
        np.testing.assert_array_equal(decoded, arr)

    def test_load_binary_mask_thresholds(self, tmp_path: Path) -> None:
        arr = np.array([[0, 255], [255, 0]], dtype=np.uint8)
        png = tmp_path / "bin.png"
        _write_png_grayscale(png, arr)
        mask = tool.load_binary_mask(png)
        np.testing.assert_array_equal(mask, np.array([[0, 1], [1, 0]], dtype=np.uint8))


# ---------------------------------------------------------------------------
# Marching squares
# ---------------------------------------------------------------------------


class TestOuterContour:
    def test_rectangle_contour(self) -> None:
        mask = np.zeros((10, 10), dtype=np.uint8)
        mask[2:8, 3:7] = 1  # a 4x6 white rectangle
        contour = tool.outer_contour(mask)
        pruned = tool._drop_collinear(contour)
        # A rectangle has exactly 4 genuine corners.
        assert len(pruned) == 4
        xs = sorted({p[0] for p in pruned})
        ys = sorted({p[1] for p in pruned})
        assert xs == [3, 7]
        assert ys == [2, 8]

    def test_l_shape_has_six_corners(self) -> None:
        mask = np.zeros((12, 12), dtype=np.uint8)
        mask[2:10, 2:10] = 1
        mask[2:6, 6:10] = 0  # carve a top-right notch -> L-shape
        contour = tool.outer_contour(mask)
        pruned = tool._drop_collinear(contour)
        assert len(pruned) == 6

    def test_empty_mask_raises(self) -> None:
        with pytest.raises(ValueError, match="No contour"):
            tool.outer_contour(np.zeros((5, 5), dtype=np.uint8))


# ---------------------------------------------------------------------------
# Pixel-corner -> world meters
# ---------------------------------------------------------------------------


class TestPixelToWorld:
    def test_anchor_offset_and_y_flip(self) -> None:
        anchor = tool.Anchor(91900, 37700)
        # bitmap 150 px tall, cell 2 m.
        # Bottom-left corner (px=0, py=150) maps to the anchor itself.
        assert tool.pixel_corner_to_world(0, 150, anchor, 150, 2) == (91900, 37700)
        # Top-left corner (px=0, py=0) maps to anchor + full height north.
        assert tool.pixel_corner_to_world(0, 0, anchor, 150, 2) == (91900, 38000)
        # One cell east + one cell north of the SW corner.
        assert tool.pixel_corner_to_world(1, 149, anchor, 150, 2) == (91902, 37702)


# ---------------------------------------------------------------------------
# Committed Halfgate sidecars
# ---------------------------------------------------------------------------


class TestHalfgateSidecars:
    def test_both_masks_exist(self) -> None:
        assert (_HALFGATE / "lower_quays_mask.png").exists()
        assert (_HALFGATE / "high_wall_mask.png").exists()

    def test_lower_quays_is_l_shape(self) -> None:
        mask = tool.load_binary_mask(_HALFGATE / "lower_quays_mask.png")
        contour = tool._drop_collinear(tool.outer_contour(mask))
        assert len(contour) == 6  # L-shape, non-rectangular

    def test_high_wall_is_l_shape(self) -> None:
        mask = tool.load_binary_mask(_HALFGATE / "high_wall_mask.png")
        contour = tool._drop_collinear(tool.outer_contour(mask))
        assert len(contour) == 6

    def test_districts_interlock_no_overlap(self) -> None:
        """The two district polygons must not overlap (Varn spec §5.2).

        Sample the puzzle block on the 2 m grid and check no cell centre falls
        inside both polygons.
        """
        from wayfinders.world.loader import load_world_referential
        from wayfinders.world.models import _point_in_polygon

        ref = load_world_referential()
        lq = ref.districts["lower_quays"].footprint
        hw = ref.districts["high_wall"].footprint
        overlap = 0
        for wy in range(37701, 38200, 2):
            for wx in range(91901, 92300, 2):
                if _point_in_polygon(wx, wy, lq) and _point_in_polygon(wx, wy, hw):
                    overlap += 1
        assert overlap == 0
