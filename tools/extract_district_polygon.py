"""
Extract a district footprint polygon from a black/white bitmap mask.

J2 deliverable -- Varn world-referential spec 2026-05-20 §5.1.

Pipeline (per district)
-----------------------
1. Read the district's B/W mask PNG (white = inside district, black = outside).
2. Trace the outer contour with marching squares (the pixel-corner edge-walk
   reused from Mira's Blender map-mesh script -- pure numpy, deterministic).
3. Snap every vertex onto the 2 m world grid: each pixel == one 2 m cell, so
   the snap is exact by construction (no rounding, no half-cells).
4. Convert pixel-corner coordinates to absolute world meters using the
   district's ``anchor`` (SW corner of the bitmap in world meters):

       world_x = anchor_x + corner_px_x * cell_size_m
       world_y = anchor_y + (bitmap_h - corner_px_y) * cell_size_m

   The Y flip is required because image rows grow DOWN (row 0 = top) while
   the world frame has Y growing NORTH from a SW origin (Varn spec §1.1).
5. Write ``<district_id>_mask.polygon.json`` next to the bitmap, in the exact
   shape the Python loader (``wayfinders/world/loader.py``) expects:

       {"vertices": [{"x": int, "y": int}, ...]}

   plus ``_anchor`` / ``_cell_size_m`` / generation metadata (ignored by the
   loader but kept for traceability).

Usage
-----
    python tools/extract_district_polygon.py <district_id>
    python tools/extract_district_polygon.py <district_id> --bitmap <path>

* No ``--bitmap``: the bitmap path is resolved from ``world.yaml`` via the
  district's ``footprint_bitmap`` field.
* ``--world-yaml`` overrides the default ``assets/world/world.yaml``.
* ``--simplify-eps`` sets the Douglas-Peucker tolerance in pixels. Default 0
  (no simplification -- keep full grid-edge fidelity, per Varn spec §5.1).
* ``--invert`` flips bitmap polarity if a source comes in inverted.

Exit codes
----------
* ``0`` -- sidecar written.
* ``2`` -- bad invocation / unreadable bitmap / no contour found.

Dependencies
------------
numpy only (already a ``wayfinders`` runtime dependency). PNG decoding is done
with the stdlib ``zlib`` module -- no Pillow, keeping the repo's stdlib-first
tooling discipline (see tools/README.md).
"""

from __future__ import annotations

import argparse
import json
import math
import struct
import sys
import zlib
from pathlib import Path
from typing import NamedTuple

import numpy as np
import yaml

# Repo root: tools/ sits directly under the repo root.
_REPO_ROOT = Path(__file__).resolve().parent.parent
_DEFAULT_WORLD_YAML = _REPO_ROOT / "assets" / "world" / "world.yaml"
_ASSETS_WORLD = _REPO_ROOT / "assets" / "world"

_PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


# ===========================================================================
# PNG decoding -- stdlib only (zlib for the IDAT stream, manual filter undo)
# ===========================================================================


def _paeth(a: int, b: int, c: int) -> int:
    """Paeth predictor (PNG filter type 4)."""
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    if pb <= pc:
        return b
    return c


def _decode_png_grayscale(path: Path) -> np.ndarray:
    """Decode a PNG into a HxW uint8 array of luminance values (0..255).

    Supports the color types a B/W district mask realistically uses:
    grayscale (0), RGB (2), palette (3), grayscale+alpha (4), RGBA (6),
    at 8-bit depth. Luminance is the R channel (the mask is grey-balanced),
    or the palette/grey value directly. Palette masks use the palette entry.
    """
    raw = path.read_bytes()
    if raw[:8] != _PNG_MAGIC:
        raise ValueError(f"{path} is not a PNG (bad magic bytes).")

    pos = 8
    width = height = bit_depth = color_type = 0
    idat = bytearray()
    palette: list[tuple[int, int, int]] = []

    while pos < len(raw):
        (length,) = struct.unpack(">I", raw[pos : pos + 4])
        ctype = raw[pos + 4 : pos + 8]
        data = raw[pos + 8 : pos + 8 + length]
        pos += 12 + length  # 4 len + 4 type + data + 4 crc

        if ctype == b"IHDR":
            width, height, bit_depth, color_type = struct.unpack(">IIBB", data[:10])
        elif ctype == b"PLTE":
            palette = [(data[i], data[i + 1], data[i + 2]) for i in range(0, len(data), 3)]
        elif ctype == b"IDAT":
            idat += data
        elif ctype == b"IEND":
            break

    if bit_depth != 8:
        raise ValueError(f"{path}: only 8-bit PNGs are supported, got bit_depth={bit_depth}.")

    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}.get(color_type)
    if channels is None:
        raise ValueError(f"{path}: unsupported PNG color_type {color_type}.")

    decompressed = zlib.decompress(bytes(idat))
    stride = width * channels
    out = np.zeros((height, width), dtype=np.uint8)
    prev_row = bytearray(stride)

    src = 0
    for y in range(height):
        filter_type = decompressed[src]
        src += 1
        row = bytearray(decompressed[src : src + stride])
        src += stride

        # Undo the per-row filter.
        for i in range(stride):
            a = row[i - channels] if i >= channels else 0
            b = prev_row[i]
            c = prev_row[i - channels] if i >= channels else 0
            if filter_type == 0:  # None
                pass
            elif filter_type == 1:  # Sub
                row[i] = (row[i] + a) & 0xFF
            elif filter_type == 2:  # Up
                row[i] = (row[i] + b) & 0xFF
            elif filter_type == 3:  # Average
                row[i] = (row[i] + ((a + b) >> 1)) & 0xFF
            elif filter_type == 4:  # Paeth
                row[i] = (row[i] + _paeth(a, b, c)) & 0xFF
            else:
                raise ValueError(f"{path}: unknown PNG filter type {filter_type}.")
        prev_row = row

        # Extract luminance per pixel.
        if color_type == 3:  # palette -> use palette R component
            for x in range(width):
                out[y, x] = palette[row[x]][0]
        else:  # grayscale / RGB / GA / RGBA -> first channel is luminance
            for x in range(width):
                out[y, x] = row[x * channels]

    return out


# ===========================================================================
# Bitmap -> binary mask
# ===========================================================================


def load_binary_mask(path: Path, threshold: float = 0.5, invert: bool = False) -> np.ndarray:
    """Return a HxW uint8 mask: 1 = inside district (white), 0 = outside (black).

    Row 0 is the TOP of the image (standard image order). The Y flip into the
    world frame happens later in ``pixel_corner_to_world``.
    """
    lum = _decode_png_grayscale(path)
    thr_255 = round(threshold * 255)
    mask = (lum >= thr_255).astype(np.uint8)
    if invert:
        mask = 1 - mask
    return mask


# ===========================================================================
# Marching squares -- pixel-corner edge walk
# ---------------------------------------------------------------------------
# Reused from Mira's Blender map-terrain-mesh script (v3.1). Each foreground
# pixel (i, j) occupies the unit square (j, i)-(j+1, i+1); boundary edges
# between a foreground pixel and a background pixel are collected, then walked
# in order. The result is an axis-aligned closed polygon whose corners sit on
# integer pixel-grid coordinates -- a chain of grid edges, exactly what the
# 2 m district grid requires (Varn spec §1.3: "no half-cells, no diagonals").
# ===========================================================================


def _trace_boundaries(mask: np.ndarray) -> list[list[tuple[int, int]]]:
    """Trace every closed boundary loop of the foreground in ``mask``.

    Returns a list of loops; each loop is a list of (x, y) pixel-corner points
    (first point not duplicated at the end). Multiple loops can appear when the
    mask has interior holes -- for a simple district silhouette there is one.
    """
    h, w = mask.shape
    edges: dict[tuple[int, int], list[tuple[int, int]]] = {}

    def add_edge(p0: tuple[int, int], p1: tuple[int, int]) -> None:
        edges.setdefault(p0, []).append(p1)

    for y in range(h):
        for x in range(w):
            if mask[y, x] != 1:
                continue
            # Emit a boundary edge wherever a neighbour is background / off-image.
            # Edge direction keeps the foreground pixel on the LEFT of the edge.
            if y == 0 or mask[y - 1, x] != 1:  # top edge
                add_edge((x + 1, y), (x, y))
            if y == h - 1 or mask[y + 1, x] != 1:  # bottom edge
                add_edge((x, y + 1), (x + 1, y + 1))
            if x == 0 or mask[y, x - 1] != 1:  # left edge
                add_edge((x, y), (x, y + 1))
            if x == w - 1 or mask[y, x + 1] != 1:  # right edge
                add_edge((x + 1, y + 1), (x + 1, y))

    loops: list[list[tuple[int, int]]] = []
    visited: set[tuple[tuple[int, int], tuple[int, int]]] = set()

    for start_p, nexts in edges.items():
        for next_p in nexts:
            if (start_p, next_p) in visited:
                continue
            poly = [start_p]
            cur, nxt = start_p, next_p
            safety = 0
            max_iter = w * h * 4 + 10
            while True:
                visited.add((cur, nxt))
                poly.append(nxt)
                if nxt == start_p:
                    break
                in_dx, in_dy = nxt[0] - cur[0], nxt[1] - cur[1]

                def turn_score(
                    c: tuple[int, int],
                    _nxt: tuple[int, int] = nxt,
                    _idx: tuple[int, int] = (in_dx, in_dy),
                ) -> tuple[int, int]:
                    out_dx, out_dy = c[0] - _nxt[0], c[1] - _nxt[1]
                    cross = _idx[0] * out_dy - _idx[1] * out_dx
                    dot = _idx[0] * out_dx + _idx[1] * out_dy
                    return (cross, -dot)

                cand = None
                for c in sorted(edges.get(nxt, []), key=turn_score):
                    if (nxt, c) not in visited:
                        cand = c
                        break
                if cand is None:
                    break
                cur, nxt = nxt, cand
                safety += 1
                if safety > max_iter:
                    break
            if len(poly) > 1 and poly[-1] == poly[0]:
                poly = poly[:-1]
            if len(poly) >= 3:
                loops.append(poly)

    return loops


def _signed_area(poly: list[tuple[int, int]]) -> float:
    """Shoelace signed area in pixel-corner space."""
    n = len(poly)
    s = 0.0
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        s += x1 * y2 - x2 * y1
    return s * 0.5


def outer_contour(mask: np.ndarray) -> list[tuple[int, int]]:
    """Return the largest-area boundary loop (the outer district silhouette).

    A district mask is a single connected silhouette; the outer loop is the
    one with the largest absolute area. Interior holes (if any) are dropped --
    a district footprint is a simple polygon (Varn spec §2.2).
    """
    loops = _trace_boundaries(mask)
    if not loops:
        raise ValueError("No contour found -- bitmap is empty or all-background.")
    return max(loops, key=lambda p: abs(_signed_area(p)))


# ===========================================================================
# Douglas-Peucker simplification (optional -- default eps=0 keeps grid edges)
# ===========================================================================


def _perp_dist(p: tuple[int, int], a: tuple[int, int], b: tuple[int, int]) -> float:
    ax, ay = a
    bx, by = b
    px, py = p
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return math.hypot(px - ax, py - ay)
    t = ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)
    t = max(0.0, min(1.0, t))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def simplify_closed(poly: list[tuple[int, int]], epsilon: float) -> list[tuple[int, int]]:
    """Iterative Douglas-Peucker on a closed polygon. epsilon<=0 returns a copy."""
    if epsilon <= 0 or len(poly) < 4:
        return list(poly)
    pts = [*list(poly), poly[0]]
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        i0, i1 = stack.pop()
        if i1 <= i0 + 1:
            continue
        max_d, max_i = -1.0, -1
        for k in range(i0 + 1, i1):
            d = _perp_dist(pts[k], pts[i0], pts[i1])
            if d > max_d:
                max_d, max_i = d, k
        if max_d > epsilon:
            keep[max_i] = True
            stack.append((i0, max_i))
            stack.append((max_i, i1))
    simplified = [p for p, k in zip(pts, keep, strict=True) if k]
    if len(simplified := simplified) > 1 and simplified[-1] == simplified[0]:
        simplified = simplified[:-1]
    return simplified


def _drop_collinear(poly: list[tuple[int, int]]) -> list[tuple[int, int]]:
    """Remove vertices that lie on a straight run between their neighbours.

    Marching squares emits one corner per pixel edge, so a straight side of N
    cells produces N+1 collinear corners. Keeping only the run endpoints gives
    a compact, faithful grid polygon (still exact -- every kept vertex is a
    real grid corner).
    """
    n = len(poly)
    if n < 3:
        return list(poly)
    kept: list[tuple[int, int]] = []
    for i in range(n):
        prev_p = poly[i - 1]
        cur = poly[i]
        nxt = poly[(i + 1) % n]
        cross = (cur[0] - prev_p[0]) * (nxt[1] - cur[1]) - (cur[1] - prev_p[1]) * (nxt[0] - cur[0])
        if cross != 0:  # genuine corner
            kept.append(cur)
    return kept if len(kept) >= 3 else list(poly)


# ===========================================================================
# Pixel-corner -> world meters
# ===========================================================================


class Anchor(NamedTuple):
    x: int
    y: int


def pixel_corner_to_world(
    px: int, py: int, anchor: Anchor, bitmap_h: int, cell_size_m: int
) -> tuple[int, int]:
    """Map a pixel-corner coordinate to absolute world meters.

    The bitmap anchor is the SW corner of the bitmap in the world frame.
    Image rows grow DOWN; the world Y axis grows NORTH -- hence the flip.
    Each pixel is exactly ``cell_size_m`` metres, so the result lands on the
    2 m grid by construction (no rounding).
    """
    world_x = anchor.x + px * cell_size_m
    world_y = anchor.y + (bitmap_h - py) * cell_size_m
    return world_x, world_y


# ===========================================================================
# world.yaml lookup
# ===========================================================================


def _district_entry(world_yaml: Path, district_id: str) -> dict[str, object]:
    """Return the raw district mapping from world.yaml, or raise."""
    with world_yaml.open(encoding="utf-8") as fh:
        raw = yaml.safe_load(fh)
    for d in raw.get("districts", []) or []:
        if isinstance(d, dict) and d.get("id") == district_id:
            return d
    raise ValueError(
        f"District id '{district_id}' not found in {world_yaml}. "
        f"Known ids: {[d.get('id') for d in raw.get('districts', []) or []]}"
    )


# ===========================================================================
# Main extraction
# ===========================================================================


def extract(
    district_id: str,
    world_yaml: Path = _DEFAULT_WORLD_YAML,
    bitmap_path: Path | None = None,
    simplify_eps: float = 0.0,
    invert: bool = False,
) -> Path:
    """Extract one district's polygon sidecar. Returns the written path."""
    entry = _district_entry(world_yaml, district_id)

    anchor_raw = entry.get("anchor")
    if not isinstance(anchor_raw, dict):
        raise ValueError(f"District '{district_id}' has no valid 'anchor' in world.yaml.")
    anchor = Anchor(int(anchor_raw["x"]), int(anchor_raw["y"]))

    cell_size_m = int(entry.get("cell_size_m", 2))
    if cell_size_m <= 0:
        raise ValueError(f"District '{district_id}' cell_size_m must be positive.")

    assets_world = world_yaml.resolve().parent
    if bitmap_path is None:
        footprint_bitmap = entry.get("footprint_bitmap")
        if not isinstance(footprint_bitmap, str):
            raise ValueError(f"District '{district_id}' has no 'footprint_bitmap' in world.yaml.")
        bitmap_path = assets_world / footprint_bitmap

    if not bitmap_path.exists():
        raise FileNotFoundError(f"Bitmap not found: {bitmap_path}")

    mask = load_binary_mask(bitmap_path, invert=invert)
    bitmap_h, bitmap_w = mask.shape

    contour_px = outer_contour(mask)
    contour_px = simplify_closed(contour_px, simplify_eps)
    contour_px = _drop_collinear(contour_px)

    vertices = [
        {
            "x": (wx_wy := pixel_corner_to_world(px, py, anchor, bitmap_h, cell_size_m))[0],
            "y": wx_wy[1],
        }
        for px, py in contour_px
    ]

    sidecar = {
        "_comment": (
            f"Derived by tools/extract_district_polygon.py from "
            f"{bitmap_path.name}. Marching-squares outer contour, snapped to "
            f"the {cell_size_m} m world grid. Regeneratable -- do not hand-edit."
        ),
        "_district_id": district_id,
        "_anchor": {"x": anchor.x, "y": anchor.y},
        "_cell_size_m": cell_size_m,
        "_bitmap": bitmap_path.name,
        "_bitmap_size_px": {"w": bitmap_w, "h": bitmap_h},
        "_generator": "tools/extract_district_polygon.py",
        "vertices": vertices,
    }

    sidecar_path = bitmap_path.with_name(bitmap_path.stem + ".polygon.json")
    sidecar_path.write_text(json.dumps(sidecar, indent=2) + "\n", encoding="utf-8")
    return sidecar_path


def _build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Extract a district footprint polygon from a B/W bitmap mask.",
    )
    parser.add_argument("district_id", help="District id as declared in world.yaml.")
    parser.add_argument(
        "--bitmap",
        type=Path,
        default=None,
        help="Override the bitmap path (default: resolved from world.yaml).",
    )
    parser.add_argument(
        "--world-yaml",
        type=Path,
        default=_DEFAULT_WORLD_YAML,
        help="Path to world.yaml (default: assets/world/world.yaml).",
    )
    parser.add_argument(
        "--simplify-eps",
        type=float,
        default=0.0,
        help="Douglas-Peucker tolerance in pixels (default 0 -- full fidelity).",
    )
    parser.add_argument(
        "--invert",
        action="store_true",
        help="Flip bitmap polarity (use if white=outside in the source).",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_arg_parser().parse_args(argv)
    try:
        sidecar_path = extract(
            district_id=args.district_id,
            world_yaml=args.world_yaml,
            bitmap_path=args.bitmap,
            simplify_eps=args.simplify_eps,
            invert=args.invert,
        )
    except (ValueError, FileNotFoundError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2

    data = json.loads(sidecar_path.read_text(encoding="utf-8"))
    n = len(data["vertices"])
    print(f"OK: wrote {sidecar_path} ({n} vertices).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
