"""
Halfgate top-down B&W architect plan generator.

Produces a single square PNG with black-on-white linework from a hardcoded
8x8 cell district map.  Each district type has its own render function.
A second transverse pass adds continuous wall, coastline, and hedgerow seams
across cell boundaries.

Usage:
    uv run --with pillow python tools/asset_generators/halfgate_topdown_plan.py
    uv run --with pillow python tools/asset_generators/halfgate_topdown_plan.py \
        --resolution 4096 --seed 7 --output path/to/out.png --show-grid

Convention: grid[row][col], row 0 = north, col 0 = west.
"""

from __future__ import annotations

import argparse
import math
import random
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont

# ---------------------------------------------------------------------------
# Hardcoded 8x8 district map (from Mira doc, section 3)
# Row 0 = north, col 0 = west
# I=Intramuros, W=Wall, G=Gateway, O=Outskirts, H=HinterlandAgri, L=Littoral, U=Urban
# ---------------------------------------------------------------------------
GRID: list[list[str]] = [
    ["H", "H", "O", "O", "U", "U", "U", "U"],  # row 0 - north
    ["H", "H", "O", "W", "W", "U", "U", "U"],  # row 1
    ["O", "W", "W", "I", "I", "W", "U", "U"],  # row 2
    ["O", "W", "I", "I", "I", "W", "W", "U"],  # row 3
    ["W", "I", "I", "I", "I", "I", "W", "U"],  # row 4
    ["W", "I", "I", "I", "I", "W", "U", "U"],  # row 5
    ["G", "G", "W", "W", "W", "U", "U", "L"],  # row 6
    ["G", "G", "U", "U", "L", "L", "L", "L"],  # row 7 - south
]

GRID_ROWS = 8
GRID_COLS = 8

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
BLACK = (0, 0, 0, 255)
WHITE = (255, 255, 255, 255)
GRAY = (180, 180, 180, 255)

WALL_THICKNESS_FRAC = 0.12  # wall band width as fraction of cell_size
TOWER_RADIUS_FRAC = 0.07  # corner tower radius as fraction of cell_size
LINE_W = 2  # base line width (scaled at high res)


def _lw(cell_size: int) -> int:
    """Scale line width proportionally to cell size (base = 512px cell)."""
    return max(1, round(LINE_W * cell_size / 512))


def _wt(cell_size: int) -> int:
    """Wall thickness in pixels."""
    return max(_lw(cell_size) * 3, int(cell_size * WALL_THICKNESS_FRAC))


def _tr(cell_size: int) -> int:
    """Tower radius in pixels."""
    return max(_lw(cell_size) * 2, int(cell_size * TOWER_RADIUS_FRAC))


# ---------------------------------------------------------------------------
# Helper: seeded RNG per cell
# ---------------------------------------------------------------------------
def cell_rng(base_seed: int, col: int, row: int) -> random.Random:
    return random.Random(base_seed ^ (row * 31 + col * 97))


def _neighbors(col: int, row: int) -> dict[str, str | None]:
    """Return district chars for N/S/E/W neighbors, or None if out of bounds."""
    result: dict[str, str | None] = {}
    for name, (dr, dc) in [("N", (-1, 0)), ("S", (1, 0)), ("E", (0, 1)), ("W", (0, -1))]:
        r2, c2 = row + dr, col + dc
        if 0 <= r2 < GRID_ROWS and 0 <= c2 < GRID_COLS:
            result[name] = GRID[r2][c2]
        else:
            result[name] = None
    return result


# ---------------------------------------------------------------------------
# Hatch helpers
# ---------------------------------------------------------------------------


def hatch_rect(
    draw: ImageDraw.ImageDraw,
    x0: int,
    y0: int,
    x1: int,
    y1: int,
    spacing: int,
    line_w: int,
    direction: str = "diagonal",  # "h", "v", "diagonal", "cross"
) -> None:
    """Fill a rectangle with hatch lines, clipped to the rectangle."""
    if x1 <= x0 or y1 <= y0:
        return
    w = x1 - x0
    h = y1 - y0

    if direction in ("h", "cross"):
        y = y0 + spacing
        while y < y1:
            draw.line([(x0, y), (x1, y)], fill=BLACK, width=line_w)
            y += spacing
    if direction in ("v", "cross"):
        x = x0 + spacing
        while x < x1:
            draw.line([(x, y0), (x, y1)], fill=BLACK, width=line_w)
            x += spacing
    if direction == "diagonal":
        # NW-SE diagonals across the rectangle
        for k in range(-(h), w + h, spacing):
            # line: y = x - k (shifted) clipped to rect
            # At y=y0: x = y0 + k;  at y=y1: x = y1 + k
            ax = x0 + k
            ay = y0
            bx = x0 + k + h
            by = y1
            # clip to [x0,x1]
            pts = []
            if ax < x0:
                ay_new = y0 + (x0 - ax)
                ax = x0
                ay = ay_new
            if bx > x1:
                by_new = y1 - (bx - x1)
                bx = x1
                by = by_new
            if ax <= x1 and bx >= x0 and ay <= y1 and by >= y0:
                pts = [(ax, ay), (bx, by)]
            if len(pts) == 2:
                draw.line(pts, fill=BLACK, width=line_w)


# ---------------------------------------------------------------------------
# District renderers
# ---------------------------------------------------------------------------


def render_urban(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    cs: int,
    seed: int,
    col: int,
    row: int,
    **_: Any,
) -> None:
    """Generic urban blocks: rectangular building footprints with central lane."""
    rng = cell_rng(seed, col, row)
    lw = _lw(cs)
    pad = cs // 10

    road_w = max(4 * lw, cs // 12)
    road_x = cx + cs // 2 - road_w // 2

    for block_col in range(2):
        if block_col == 0:
            bx0 = cx + pad
            bx1 = road_x - lw
        else:
            bx0 = road_x + road_w + lw
            bx1 = cx + cs - pad
        if bx1 <= bx0 + cs // 20:
            continue

        n_buildings = rng.randint(2, 4)
        block_h = cs - 2 * pad
        y = cy + pad
        for _i in range(n_buildings):
            h = block_h // n_buildings + rng.randint(-(cs // 30), cs // 30)
            h = max(cs // 14, h)
            if y + h > cy + cs - pad:
                h = cy + cs - pad - y
            if h < cs // 20:
                break
            draw.rectangle([bx0, y, bx1, y + h], outline=BLACK, width=lw)
            y += h + lw * 2


def render_intramuros(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    cs: int,
    seed: int,
    col: int,
    row: int,
    **_: Any,
) -> None:
    """Dense packed buildings (solid-filled footprints) with narrow alleys."""
    rng = cell_rng(seed, col, row)
    lw = _lw(cs)
    pad = cs // 14
    alley = max(lw * 2, cs // 22)

    n = rng.randint(4, 7)
    placed: list[tuple[int, int, int, int]] = []

    for _ in range(n):
        for _attempt in range(50):
            w = rng.randint(cs // 8, cs // 4)
            h = rng.randint(cs // 8, cs // 4)
            x0 = cx + pad + rng.randint(0, max(0, cs - 2 * pad - w))
            y0 = cy + pad + rng.randint(0, max(0, cs - 2 * pad - h))
            x1, y1 = x0 + w, y0 + h
            overlap = any(
                x0 < px1 + alley and x1 > px0 - alley and y0 < py1 + alley and y1 > py0 - alley
                for px0, py0, px1, py1 in placed
            )
            if not overlap:
                placed.append((x0, y0, x1, y1))
                break

    for x0, y0, x1, y1 in placed:
        # Solid black fill — dense urban mass
        draw.rectangle([x0, y0, x1, y1], fill=BLACK, outline=BLACK, width=lw)

    # Thin white alleys (already implicit from gaps), but add a few cross-lines
    # to suggest courtyard streets
    if len(placed) >= 2:
        alley_y = cy + cs // 2 + rng.randint(-cs // 10, cs // 10)
        draw.line([(cx + pad, alley_y), (cx + cs - pad, alley_y)], fill=WHITE, width=max(1, alley))


def render_outskirts(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    cs: int,
    seed: int,
    col: int,
    row: int,
    **_: Any,
) -> None:
    """Loose buildings, slightly spread out."""
    rng = cell_rng(seed, col, row)
    lw = _lw(cs)
    pad = cs // 8

    n = rng.randint(2, 4)
    placed: list[tuple[int, int, int, int]] = []
    gap = cs // 12
    for _ in range(n):
        for _attempt in range(30):
            w = rng.randint(cs // 9, cs // 5)
            h = rng.randint(cs // 9, cs // 5)
            x0 = cx + pad + rng.randint(0, max(0, cs - 2 * pad - w))
            y0 = cy + pad + rng.randint(0, max(0, cs - 2 * pad - h))
            x1, y1 = x0 + w, y0 + h
            overlap = any(
                x0 < px1 + gap and x1 > px0 - gap and y0 < py1 + gap and y1 > py0 - gap
                for px0, py0, px1, py1 in placed
            )
            if not overlap:
                placed.append((x0, y0, x1, y1))
                break

    for x0, y0, x1, y1 in placed:
        draw.rectangle([x0, y0, x1, y1], outline=BLACK, width=lw)


def render_hinterland(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    cs: int,
    seed: int,
    col: int,
    row: int,
    **_: Any,
) -> None:
    """Farm parcels with alternating horizontal/vertical hatch, dotted hedge borders."""
    rng = cell_rng(seed, col, row)
    lw = _lw(cs)
    pad = cs // 14

    nx = rng.choice([2, 3])
    ny = rng.choice([2, 3])
    parcel_w = (cs - 2 * pad) // nx
    parcel_h = (cs - 2 * pad) // ny
    hatch_sp = max(3 * lw, cs // 20)
    inner = lw + 1

    for pi in range(nx):
        for pj in range(ny):
            px0 = cx + pad + pi * parcel_w
            py0 = cy + pad + pj * parcel_h
            px1 = px0 + parcel_w - lw
            py1 = py0 + parcel_h - lw
            draw.rectangle([px0, py0, px1, py1], outline=BLACK, width=lw)
            direction = "h" if (pi + pj) % 2 == 0 else "v"
            hatch_rect(
                draw,
                px0 + inner,
                py0 + inner,
                px1 - inner,
                py1 - inner,
                hatch_sp,
                max(1, lw - 1),
                direction=direction,
            )


def render_littoral(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    cs: int,
    seed: int,
    col: int,
    row: int,
    neighbors: dict[str, str | None],
    **_: Any,
) -> None:
    """Coastal cells: quays, wave glyphs, boats.  Coastline drawn in transverse pass."""
    rng = cell_rng(seed, col, row)
    lw = _lw(cs)

    # Coast is at ~30% from west — sea is east of that
    coast_x = cx + int(0.30 * cs)

    # Light fill on sea side (east of coast) using fine horizontal hatch
    hatch_sp = max(4 * lw, cs // 16)
    hatch_rect(draw, coast_x, cy, cx + cs, cy + cs, hatch_sp, max(1, lw - 1), direction="h")

    # Wave glyphs
    n_waves = rng.randint(3, 6)
    for _ in range(n_waves):
        wx = coast_x + rng.randint(cs // 8, int(cs * 0.65))
        wy = cy + rng.randint(cs // 10, cs - cs // 10)
        wave_r = max(cs // 24, lw * 3)
        wave_pts = [
            (wx - wave_r, wy),
            (wx - wave_r // 2, wy - wave_r // 2),
            (wx, wy),
            (wx + wave_r // 2, wy - wave_r // 2),
            (wx + wave_r, wy),
        ]
        draw.line(wave_pts, fill=BLACK, width=max(1, lw - 1))

    # Quay stubs
    n_quays = rng.randint(1, 3)
    quay_len = cs // 6
    quay_tick = cs // 16
    for _ in range(n_quays):
        qy = cy + rng.randint(cs // 8, cs - cs // 8)
        qx_start = coast_x + rng.randint(-cs // 25, cs // 25)
        draw.line([(qx_start, qy), (qx_start + quay_len, qy)], fill=BLACK, width=lw * 2)
        draw.line([(qx_start, qy - quay_tick), (qx_start, qy + quay_tick)], fill=BLACK, width=lw)
        draw.line(
            [(qx_start + quay_len, qy - quay_tick), (qx_start + quay_len, qy + quay_tick)],
            fill=BLACK,
            width=lw,
        )

    # Boat triangles
    if rng.random() < 0.55:
        bx = coast_x + rng.randint(cs // 10, int(cs * 0.55))
        by = cy + rng.randint(cs // 6, cs - cs // 6)
        bw = max(cs // 16, lw * 5)
        bh = max(cs // 22, lw * 3)
        draw.polygon(
            [(bx, by - bh), (bx - bw, by + bh), (bx + bw, by + bh)],
            outline=BLACK,
            fill=WHITE,
        )
        # Mast
        draw.line([(bx, by - bh), (bx, by - bh - bh * 2)], fill=BLACK, width=lw)


def render_wall(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    cs: int,
    seed: int,
    col: int,
    row: int,
    **_: Any,
) -> None:
    """
    Curtain wall cell.

    Outer face (facing non-wall/non-gateway): thick solid band with crenellation
    notches (white slits).
    Inner area: cross-hatch to indicate masonry fill.
    Corner towers at outer-face corners.
    """
    lw = _lw(cs)
    wt = _wt(cs)
    tr = _tr(cs)
    nbrs = _neighbors(col, row)

    def is_outer(d: str) -> bool:
        n = nbrs.get(d)
        return n not in ("W", "G")

    north_outer = is_outer("N")
    south_outer = is_outer("S")
    east_outer = is_outer("E")
    west_outer = is_outer("W")

    # Cross-hatch the whole cell first (masonry infill)
    hatch_sp = max(4 * lw, cs // 14)
    hatch_rect(draw, cx, cy, cx + cs, cy + cs, hatch_sp, max(1, lw - 1), direction="cross")

    # Solid outer-face bands
    if north_outer:
        draw.rectangle([cx, cy, cx + cs, cy + wt], fill=BLACK)
    if south_outer:
        draw.rectangle([cx, cy + cs - wt, cx + cs, cy + cs], fill=BLACK)
    if east_outer:
        draw.rectangle([cx + cs - wt, cy, cx + cs, cy + cs], fill=BLACK)
    if west_outer:
        draw.rectangle([cx, cy, cx + wt, cy + cs], fill=BLACK)

    # Crenellation notches (white slits into the solid band)
    notch_w = max(lw * 2, cs // 16)
    notch_sp = notch_w * 3
    notch_depth = wt // 2

    if north_outer:
        x = cx + notch_sp
        while x + notch_w < cx + cs:
            draw.rectangle([x, cy, x + notch_w, cy + notch_depth], fill=WHITE)
            x += notch_sp
    if south_outer:
        x = cx + notch_sp
        while x + notch_w < cx + cs:
            draw.rectangle([x, cy + cs - notch_depth, x + notch_w, cy + cs], fill=WHITE)
            x += notch_sp
    if west_outer:
        y = cy + notch_sp
        while y + notch_w < cy + cs:
            draw.rectangle([cx, y, cx + notch_depth, y + notch_w], fill=WHITE)
            y += notch_sp
    if east_outer:
        y = cy + notch_sp
        while y + notch_w < cy + cs:
            draw.rectangle([cx + cs - notch_depth, y, cx + cs, y + notch_w], fill=WHITE)
            y += notch_sp

    # Corner towers where two outer faces meet
    corners = [
        ("N", "W", cx, cy),
        ("N", "E", cx + cs, cy),
        ("S", "W", cx, cy + cs),
        ("S", "E", cx + cs, cy + cs),
    ]
    for d1, d2, tx, ty in corners:
        if is_outer(d1) and is_outer(d2):
            draw.ellipse([tx - tr, ty - tr, tx + tr, ty + tr], fill=BLACK, outline=BLACK)
            inner_r = max(1, tr // 2)
            draw.ellipse([tx - inner_r, ty - inner_r, tx + inner_r, ty + inner_r], fill=WHITE)


def render_gateway(
    draw: ImageDraw.ImageDraw,
    cx: int,
    cy: int,
    cs: int,
    seed: int,
    col: int,
    row: int,
    **_: Any,
) -> None:
    """
    Gateway 2x2 block (rows 6-7, cols 0-1).

    Visual: flanking barbican towers on the NE/NW of the gatehouse, a central
    archway passage (two parallel thick lines with portcullis bars), bridge deck
    on the south exit row, moat stub on east/west.
    """
    lw = _lw(cs)
    wt = _wt(cs)
    tr = int(_tr(cs) * 2.0)  # bigger barbican towers

    # Cross-hatch background (same masonry feel as wall)
    hatch_sp = max(4 * lw, cs // 14)
    hatch_rect(draw, cx, cy, cx + cs, cy + cs, hatch_sp, max(1, lw - 1), direction="cross")

    # The gate passage is vertical (N-S), centered across col 0+1
    # Each cell handles its half; the passage runs at ~40-60% of each cell width
    arch_half_w = cs // 5
    if col == 0:
        # West half: left flank wall + portcullis left jamb
        draw.rectangle([cx, cy, cx + wt, cy + cs], fill=BLACK)  # west flank
        jamb_x = cx + cs - arch_half_w - lw
        draw.rectangle(
            [jamb_x, cy, jamb_x + lw * 3, cy + cs], fill=BLACK
        )  # right jamb of west half
        if row == 6:
            # Portcullis bars (horizontal) in upper gateway row
            bar_x0 = jamb_x + lw * 3
            bar_x1 = cx + cs
            for i in range(4):
                by = cy + cs // 5 + i * cs // 6
                draw.line([(bar_x0, by), (bar_x1, by)], fill=BLACK, width=lw)
    elif col == 1:
        # East half: right flank wall + portcullis right jamb
        draw.rectangle([cx + cs - wt, cy, cx + cs, cy + cs], fill=BLACK)  # east flank
        jamb_x = cx + arch_half_w - lw * 3
        draw.rectangle([jamb_x, cy, jamb_x + lw * 3, cy + cs], fill=BLACK)  # left jamb of east half
        if row == 6:
            # Portcullis bars in upper gateway row
            bar_x0 = cx
            bar_x1 = jamb_x
            for i in range(4):
                by = cy + cs // 5 + i * cs // 6
                draw.line([(bar_x0, by), (bar_x1, by)], fill=BLACK, width=lw)

    # Bridge on south exit row (row 7)
    if row == 7:
        if col == 0:
            # Bridge parapet on west side
            draw.rectangle([cx + wt, cy, cx + wt + lw * 2, cy + cs], fill=BLACK)
        elif col == 1:
            # Bridge parapet on east side
            draw.rectangle([cx + cs - wt - lw * 2, cy, cx + cs - wt, cy + cs], fill=BLACK)
            # Bridge deck cross-planks
            for i in range(5):
                py = cy + i * (cs // 5)
                draw.line([(cx - cs // 5, py), (cx + cs + cs // 5, py)], fill=BLACK, width=lw)

    # Barbican towers at corners of the 2x2 block
    if row == 6 and col == 0:
        _draw_tower(draw, cx, cy, tr)
    if row == 6 and col == 1:
        _draw_tower(draw, cx + cs, cy, tr)
    if row == 7 and col == 0:
        _draw_tower(draw, cx, cy + cs, tr)
    if row == 7 and col == 1:
        _draw_tower(draw, cx + cs, cy + cs, tr)


def _draw_tower(
    draw: ImageDraw.ImageDraw,
    tx: int,
    ty: int,
    tr: int,
) -> None:
    draw.ellipse([tx - tr, ty - tr, tx + tr, ty + tr], fill=BLACK, outline=BLACK)
    inner_r = max(1, tr // 2)
    draw.ellipse([tx - inner_r, ty - inner_r, tx + inner_r, ty + inner_r], fill=WHITE)


# ---------------------------------------------------------------------------
# Transverse pass: continuous coastline across L cells
# ---------------------------------------------------------------------------


def draw_continuous_coastline(
    draw: ImageDraw.ImageDraw,
    cs: int,
    seed: int,
    image_size: int,
) -> None:
    """
    Single sinuous coastline traced continuously through all Littoral columns.

    Groups L cells by column, draws one smooth polyline per column.
    The x-anchor is fixed at ~30% from west edge of the cell, with small
    per-point jitter for naturalism.  Line is thicker than interior elements.
    """
    lw = _lw(cs)

    col_groups: dict[int, list[int]] = defaultdict(list)
    for row in range(GRID_ROWS):
        for col in range(GRID_COLS):
            if GRID[row][col] == "L":
                col_groups[col].append(row)

    for col, rows in col_groups.items():
        rows_sorted = sorted(rows)
        coast_frac = 0.30 + cell_rng(seed, col, 999).uniform(-0.04, 0.04)
        cx_base = col * cs + int(coast_frac * cs)

        pts = []
        for row in rows_sorted:
            rng = cell_rng(seed, col, row)
            cy = row * cs
            # 5 pts per cell for smoother curve
            for k in range(5):
                y_frac = k / 4.0
                x_jit = rng.randint(-cs // 16, cs // 16)
                pts.append((cx_base + x_jit, cy + int(y_frac * cs)))

        if len(pts) >= 2:
            draw.line(pts, fill=BLACK, width=lw * 3)


# ---------------------------------------------------------------------------
# Transverse pass: hedgerows at H-cell boundaries
# ---------------------------------------------------------------------------


def draw_hedgerows(draw: ImageDraw.ImageDraw, cs: int, seed: int) -> None:
    """
    Dotted hedge lines at edges of H cells that face non-H cells.
    """
    lw = _lw(cs)
    dot_r = max(1, lw)
    dot_sp = max(3 * lw, cs // 20)

    def dotted(x0: int, y0: int, x1: int, y1: int) -> None:
        length = math.hypot(x1 - x0, y1 - y0)
        n = max(2, int(length / dot_sp))
        for i in range(n + 1):
            t = i / n
            dx = int(x0 + t * (x1 - x0))
            dy = int(y0 + t * (y1 - y0))
            draw.ellipse([dx - dot_r, dy - dot_r, dx + dot_r, dy + dot_r], fill=BLACK)

    # Horizontal seams (between row r and row r+1)
    for row in range(GRID_ROWS - 1):
        for col in range(GRID_COLS):
            top, bot = GRID[row][col], GRID[row + 1][col]
            if "H" in (top, bot) and top != bot:
                y = (row + 1) * cs
                dotted(col * cs, y, col * cs + cs, y)

    # Vertical seams (between col c and col c+1)
    for row in range(GRID_ROWS):
        for col in range(GRID_COLS - 1):
            left, right = GRID[row][col], GRID[row][col + 1]
            if "H" in (left, right) and left != right:
                x = (col + 1) * cs
                dotted(x, row * cs, x, row * cs + cs)


# ---------------------------------------------------------------------------
# Compass rose (NE corner)
# ---------------------------------------------------------------------------


def draw_compass(draw: ImageDraw.ImageDraw, image_size: int, cs: int) -> None:
    lw = _lw(cs)
    size = max(cs // 2, image_size // 22)
    margin = image_size // 40
    cx = image_size - margin - size // 2
    cy = margin + size // 2

    # Background circle
    draw.ellipse(
        [cx - size // 2, cy - size // 2, cx + size // 2, cy + size // 2],
        fill=WHITE,
        outline=BLACK,
        width=lw,
    )

    for angle_deg, _label, filled in [
        (0, "N", True),
        (90, "E", False),
        (180, "S", True),
        (270, "W", False),
    ]:
        angle = math.radians(angle_deg - 90)
        perp = math.radians(angle_deg)
        tip_x = cx + int(math.cos(angle) * size // 2)
        tip_y = cy + int(math.sin(angle) * size // 2)
        base_w = size // 10
        b1x = cx + int(math.cos(perp) * base_w)
        b1y = cy + int(math.sin(perp) * base_w)
        b2x = cx - int(math.cos(perp) * base_w)
        b2y = cy - int(math.sin(perp) * base_w)
        fill_color = BLACK if filled else WHITE
        draw.polygon([(tip_x, tip_y), (b1x, b1y), (b2x, b2y)], fill=fill_color, outline=BLACK)

    # N label
    font_size = max(10, size // 4)
    try:
        font = ImageFont.truetype("arial.ttf", font_size)
    except OSError:
        font = ImageFont.load_default()
    draw.text((cx - font_size // 3, cy - size // 2 - font_size - lw), "N", fill=BLACK, font=font)


# ---------------------------------------------------------------------------
# Scale bar (SE corner)
# ---------------------------------------------------------------------------


def draw_scale_bar(draw: ImageDraw.ImageDraw, image_size: int, cs: int) -> None:
    lw = _lw(cs)
    bar_w = image_size // 8
    bar_h = max(4 * lw, image_size // 120)
    margin = image_size // 40
    x0 = image_size - margin - bar_w
    y0 = image_size - margin - bar_h * 4
    x1 = x0 + bar_w
    y1 = y0 + bar_h

    n_seg = 8  # one per cell
    seg_w = bar_w // n_seg
    for i in range(n_seg):
        sx0 = x0 + i * seg_w
        sx1 = sx0 + seg_w
        fill = BLACK if i % 2 == 0 else WHITE
        draw.rectangle([sx0, y0, sx1, y1], fill=fill, outline=BLACK, width=lw)

    font_size = max(8, bar_h * 2)
    try:
        font = ImageFont.truetype("arial.ttf", font_size)
    except OSError:
        font = ImageFont.load_default()
    draw.text((x0, y1 + lw * 2), "0", fill=BLACK, font=font)
    draw.text((x1 - font_size * 2, y1 + lw * 2), "8 cells", fill=BLACK, font=font)


# ---------------------------------------------------------------------------
# Grid overlay (debug)
# ---------------------------------------------------------------------------


def draw_grid(draw: ImageDraw.ImageDraw, cs: int, image_size: int) -> None:
    lw = max(1, _lw(cs) - 1)
    for i in range(GRID_COLS + 1):
        draw.line([(i * cs, 0), (i * cs, image_size)], fill=GRAY, width=lw)
    for i in range(GRID_ROWS + 1):
        draw.line([(0, i * cs), (image_size, i * cs)], fill=GRAY, width=lw)


# ---------------------------------------------------------------------------
# Main render orchestrator
# ---------------------------------------------------------------------------

RENDERERS = {
    "U": render_urban,
    "I": render_intramuros,
    "O": render_outskirts,
    "H": render_hinterland,
    "L": render_littoral,
    "W": render_wall,
    "G": render_gateway,
}


def generate(
    resolution: int = 4096,
    seed: int = 42,
    output: str | None = None,
    show_grid: bool = False,
) -> Path:
    assert resolution % 8 == 0, f"Resolution {resolution} must be divisible by 8"
    cs = resolution // 8

    img = Image.new("RGBA", (resolution, resolution), WHITE)
    draw = ImageDraw.Draw(img)

    # Per-cell pass
    for row in range(GRID_ROWS):
        for col in range(GRID_COLS):
            district = GRID[row][col]
            cx = col * cs
            cy = row * cs
            renderer = RENDERERS.get(district)
            if renderer:
                renderer(
                    draw=draw,
                    cx=cx,
                    cy=cy,
                    cs=cs,
                    seed=seed,
                    col=col,
                    row=row,
                    neighbors=_neighbors(col, row),
                )

    # Transverse passes (drawn on top of per-cell)
    draw_continuous_coastline(draw, cs, seed, resolution)
    draw_hedgerows(draw, cs, seed)

    # Cartographic overlays
    draw_compass(draw, resolution, cs)
    draw_scale_bar(draw, resolution, cs)

    if show_grid:
        draw_grid(draw, cs, resolution)

    img_rgb = img.convert("RGB")

    if output is None:
        out_path = Path(
            r"C:\Users\dbo\Desktop\PKA\Owner's Inbox\Media\wayfinders\halfgate-topdown-v1"
            r"\halfgate-topdown-procedural-v1.png"
        )
    else:
        out_path = Path(output)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    img_rgb.save(str(out_path), "PNG", optimize=False)
    print(f"Saved: {out_path}  ({resolution}x{resolution} px)")
    return out_path


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate a top-down B&W architect plan of Halfgate (8x8 grid)."
    )
    parser.add_argument(
        "--resolution",
        type=int,
        default=4096,
        help="Output image size in pixels (divisible by 8, default 4096).",
    )
    parser.add_argument(
        "--seed", type=int, default=42, help="RNG seed for reproducible jitter (default 42)."
    )
    parser.add_argument("--output", type=str, default=None, help="Output PNG path.")
    parser.add_argument(
        "--show-grid", action="store_true", help="Overlay 8x8 grid lines (gray, debug)."
    )

    args = parser.parse_args()

    if args.resolution % 8 != 0:
        print(f"Error: --resolution {args.resolution} not divisible by 8.", file=sys.stderr)
        sys.exit(1)

    generate(
        resolution=args.resolution,
        seed=args.seed,
        output=args.output,
        show_grid=args.show_grid,
    )


if __name__ == "__main__":
    main()
