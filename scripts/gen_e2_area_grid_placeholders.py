"""
Generate placeholder PNGs for the E2.1 Halfgate area-grid scene.

Outputs land under the godot_asset_override symlink so the in-game
AssetResolver picks them up from user:// at runtime, ahead of the
final Mira-authored bitmaps.

Naming convention adopted (Mira v8.1 §1 shape: wf_<ecran>_<nom>_<variant>_<res>.png):
- <ecran>     = "e2_grid" (sub-layer of the E2AreaMap screen, distinct
                 from "e2/" legacy folder which holds the WorldMap
                 background, and from "e3/" which holds the area's
                 cité-level background).
- <nom>       = role (tile, portrait, fog, background, poi_marker).
- <variant>   = district_type / npc id / role variant when applicable.
- <res>       = dimensions in WxH.

The script is idempotent: rerun overwrites without prompt.

Rune E2.1, 2026-05-16.
"""

from __future__ import annotations

import os
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

# Resolve the symlink target path (user://wayfinders_visual_assets/ in
# Godot vocabulary). Hard-coded for the dev environment ; the symlink
# under "Team Inbox/godot_asset_override/" resolves to the same place.
OUT_ROOT = Path(
    os.path.expandvars(
        r"%APPDATA%\Godot\app_userdata\Wayfinders\wayfinders_visual_assets\e2_area_grid"
    )
)


# ---------- Wayfinders palette (per project_wayfinders_visual_dna) ----------
# Warm earth axis : terracotta ochre + parchment cream + umber.
PARCHMENT_CREAM = (231, 213, 178)
TERRACOTTA = (175, 100, 70)
UMBER = (90, 60, 38)
DARK_UMBER = (55, 35, 22)
ASH_GREY = (130, 122, 110)

# Per-district teints. Distinct enough that a placeholder reading is
# unambiguous, but all sitting on the warm-earth axis so Mira can drop
# the real bitmaps later without a tonal jolt.
DISTRICT_COLORS: dict[str, tuple[int, int, int]] = {
    "intramuros": (190, 170, 130),  # parchment-warm
    "wall": (115, 95, 70),  # umber-stone
    "gateway": (160, 120, 80),  # warm passage
    "outskirts": (175, 150, 110),  # khaki faubourg
    "hinterland_agri": (155, 165, 100),  # field-green-ochre
    "littoral": (130, 150, 155),  # cool-grey water
}

# Placeholder dimensions (square tiles for E2.1 ; isometric reshaping is
# Mira's job post-validation).
TILE_DIM = (256, 256)  # one district tile sprite
PORTRAIT_DIM = (256, 384)  # 2:3 portrait, NPC vibe
BACKGROUND_DIM = (2048, 2048)  # 8x8 grid at 256 px/cell
FOG_DIM = (256, 256)  # one fog veil tile
POI_MARKER_DIM = (
    96,
    96,
)  # one POI marker icon (E2.1c refonte : sober solid dot, 40 px on 96 canvas)


def _load_font(size: int) -> ImageFont.ImageFont:
    """Best-effort font load. Falls back to PIL default if no system
    font available (rare on Windows, but the script must stay portable)."""
    candidates = [
        r"C:\Windows\Fonts\georgia.ttf",
        r"C:\Windows\Fonts\georgiab.ttf",
        r"C:\Windows\Fonts\arial.ttf",
    ]
    for path in candidates:
        if Path(path).exists():
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


def _draw_tile(
    out_path: Path,
    base_color: tuple[int, int, int],
    district_label: str,
    dim: tuple[int, int] = TILE_DIM,
) -> None:
    """Draw a tile placeholder : base color, soft inner shadow, district
    label centred, faint grid border. Mira will replace the bitmap proper ;
    this version is purely for layout + EC6 hover validation."""
    img = Image.new("RGBA", dim, (*base_color, 255))
    draw = ImageDraw.Draw(img)

    # Inner border (umber) — gives the tile a tangible "edge" so the
    # grid is readable when 64 of them tile up.
    border = 4
    draw.rectangle(
        [border, border, dim[0] - border - 1, dim[1] - border - 1],
        outline=DARK_UMBER,
        width=2,
    )

    # Inner texture hatch : terracotta dotted grid so each tile shows
    # some grain (mirrors the "grain à l'intérieur seulement" rule of
    # the visual DNA pipeline v2). Cheap, deterministic, no PRNG.
    for x in range(20, dim[0] - 20, 24):
        for y in range(20, dim[1] - 20, 24):
            draw.point((x, y), fill=(*TERRACOTTA, 128))

    # Centred district label.
    font = _load_font(28)
    text = district_label
    bbox = draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    draw.text(
        ((dim[0] - tw) / 2, (dim[1] - th) / 2 - 4),
        text,
        fill=DARK_UMBER,
        font=font,
    )

    # "PLACEHOLDER" subtitle so dev never confuses these with real assets.
    sub_font = _load_font(14)
    sub = "PLACEHOLDER"
    bbox = draw.textbbox((0, 0), sub, font=sub_font)
    tw = bbox[2] - bbox[0]
    draw.text(
        ((dim[0] - tw) / 2, dim[1] - 36),
        sub,
        fill=(*DARK_UMBER, 180) if False else ASH_GREY,
        font=sub_font,
    )

    img.save(out_path, "PNG")
    print(f"  wrote {out_path.name}  ({dim[0]}x{dim[1]})")


def _draw_portrait(
    out_path: Path,
    display_name: str,
    class_label: str,
    accent_color: tuple[int, int, int],
) -> None:
    """Draw a portrait placeholder : parchment background, accent
    silhouette band, name + class label. Vertical 2:3 ratio (256x384)
    so the portrait reads as a portrait and not a tile."""
    dim = PORTRAIT_DIM
    img = Image.new("RGBA", dim, (*PARCHMENT_CREAM, 255))
    draw = ImageDraw.Draw(img)

    # Parchment border.
    draw.rectangle(
        [0, 0, dim[0] - 1, dim[1] - 1],
        outline=DARK_UMBER,
        width=3,
    )

    # Accent silhouette band (a horizontal stripe at portrait shoulder
    # height) -- distinguishes the 3 NPCs at a glance.
    band_top = int(dim[1] * 0.55)
    draw.rectangle(
        [16, band_top, dim[0] - 17, dim[1] - 60],
        fill=(*accent_color, 255),
        outline=DARK_UMBER,
        width=2,
    )

    # Silhouette head (oval).
    head_w, head_h = 100, 130
    head_x = (dim[0] - head_w) // 2
    head_y = int(dim[1] * 0.18)
    draw.ellipse(
        [head_x, head_y, head_x + head_w, head_y + head_h],
        fill=(*accent_color, 220),
        outline=DARK_UMBER,
        width=2,
    )

    # Name + class.
    name_font = _load_font(20)
    class_font = _load_font(14)
    sub_font = _load_font(12)

    name = display_name
    bbox = draw.textbbox((0, 0), name, font=name_font)
    nw = bbox[2] - bbox[0]
    draw.text(
        ((dim[0] - nw) / 2, dim[1] - 56),
        name,
        fill=DARK_UMBER,
        font=name_font,
    )

    bbox = draw.textbbox((0, 0), class_label, font=class_font)
    cw = bbox[2] - bbox[0]
    draw.text(
        ((dim[0] - cw) / 2, dim[1] - 32),
        class_label,
        fill=DARK_UMBER,
        font=class_font,
    )

    bbox = draw.textbbox((0, 0), "PLACEHOLDER", font=sub_font)
    pw = bbox[2] - bbox[0]
    draw.text(
        ((dim[0] - pw) / 2, dim[1] - 16),
        "PLACEHOLDER",
        fill=ASH_GREY,
        font=sub_font,
    )

    img.save(out_path, "PNG")
    print(f"  wrote {out_path.name}  ({dim[0]}x{dim[1]})")


def _draw_background(out_path: Path) -> None:
    """The 8x8 grid background plate. Solid warm-earth wash so the
    individual district tiles read above it. The actual cell rendering
    happens at runtime in Godot ; this background is the "paper" beneath
    them. 2048x2048 = 8x8 cells at 256 px/cell."""
    dim = BACKGROUND_DIM
    img = Image.new("RGBA", dim, (*PARCHMENT_CREAM, 255))
    draw = ImageDraw.Draw(img)

    # Outer parchment frame.
    draw.rectangle(
        [0, 0, dim[0] - 1, dim[1] - 1],
        outline=DARK_UMBER,
        width=8,
    )

    # 8x8 grid reference lines (subtle, just to ground the placeholders
    # visually when the cells overlay).
    for i in range(1, 8):
        offset = i * (dim[0] // 8)
        draw.line([offset, 0, offset, dim[1]], fill=(*ASH_GREY, 140), width=1)
        draw.line([0, offset, dim[0], offset], fill=(*ASH_GREY, 140), width=1)

    # Title in upper-left corner.
    font = _load_font(48)
    draw.text((48, 32), "E2.1 / Halfgate / 8x8", fill=DARK_UMBER, font=font)

    sub_font = _load_font(20)
    draw.text(
        (48, 96),
        "PLACEHOLDER (1 tile = 256 px ; 8 col x 8 row)",
        fill=ASH_GREY,
        font=sub_font,
    )

    img.save(out_path, "PNG")
    print(f"  wrote {out_path.name}  ({dim[0]}x{dim[1]})")


def _draw_fog(out_path: Path) -> None:
    """Fog veil tile : opaque umber with a parchment hatch. Used for
    fog-state cells. Reused (sample-tiled) across the whole 8x8 grid."""
    dim = FOG_DIM
    img = Image.new("RGBA", dim, (*DARK_UMBER, 255))
    draw = ImageDraw.Draw(img)

    # Parchment-tone hatch lines -- the "cadastral veil" texture.
    for i in range(-dim[0], dim[0] * 2, 12):
        draw.line(
            [i, 0, i + dim[1], dim[1]],
            fill=(*PARCHMENT_CREAM, 60),
            width=1,
        )

    # Label.
    font = _load_font(20)
    text = "FOG"
    bbox = draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    draw.text(
        ((dim[0] - tw) / 2, (dim[1] - th) / 2),
        text,
        fill=PARCHMENT_CREAM,
        font=font,
    )
    img.save(out_path, "PNG")
    print(f"  wrote {out_path.name}  ({dim[0]}x{dim[1]})")


def _draw_poi_marker(out_path: Path) -> None:
    """POI marker icon -- a sober solid terracotta dot, no ring.

    E2.1c refonte (Didier lock 2026-05-16) : the first pass drew an
    outer terracotta ring + inner dot, which read as a UI target icon
    ("icone cible") rather than a diegetic POI anchor on the parchment.
    Refonte drops the outer ring entirely and keeps only a solid filled
    circle, ~40 px diameter on the 96x96 RGBA canvas. The fill is
    terracotta (matching the parchment wash umber-terracotta family) with
    a single 2 px DARK_UMBER outline for the alpha cleanup edge -- enough
    contrast against the wash, no halo, no rim, no white ring.

    Sizing : 96x96 canvas with a 40 px diameter dot leaves enough
    transparent padding around the dot for Godot's LinearWithMipmaps
    downscaling to stay clean at the rendered ~24-32 px on-screen size.
    """
    dim = POI_MARKER_DIM
    img = Image.new("RGBA", dim, (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    cx, cy = dim[0] // 2, dim[1] // 2
    # Dot radius : 20 px on a 96x96 canvas = 40 px diameter, leaves a
    # generous transparent border for clean alpha during downscale.
    r_dot = 20
    draw.ellipse(
        [cx - r_dot, cy - r_dot, cx + r_dot, cy + r_dot],
        fill=(*TERRACOTTA, 255),
        outline=DARK_UMBER,
        width=2,
    )

    img.save(out_path, "PNG")
    print(f"  wrote {out_path.name}  ({dim[0]}x{dim[1]})")


def main() -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    print(f"[gen_e2_area_grid_placeholders] writing under {OUT_ROOT}")

    # 6 district tile placeholders.
    for suffix, color in DISTRICT_COLORS.items():
        label = suffix.replace("_", " ").title()
        out = OUT_ROOT / f"wf_e2_grid_tile_{suffix}_256x256.png"
        _draw_tile(out, color, label)

    # 3 NPC portrait placeholders. Accent colors tied to district_origin
    # for visual quick-read.
    portrait_specs = [
        (
            "wf_e2_grid_portrait_maeren_256x384.png",
            "Maeren d'Esca",
            "Scout",
            DISTRICT_COLORS["outskirts"],
        ),
        (
            "wf_e2_grid_portrait_olbrec_256x384.png",
            "Olbrec C.",
            "Warrior",
            DISTRICT_COLORS["wall"],
        ),
        (
            "wf_e2_grid_portrait_vialis_256x384.png",
            "Vialis the Dry",
            "Scholar",
            DISTRICT_COLORS["intramuros"],
        ),
    ]
    for fname, name, klass, accent in portrait_specs:
        _draw_portrait(OUT_ROOT / fname, name, klass, accent)

    # 1 background plate.
    _draw_background(OUT_ROOT / "wf_e2_grid_background_2048x2048.png")

    # 1 fog veil tile.
    _draw_fog(OUT_ROOT / "wf_e2_grid_fog_256x256.png")

    # 1 POI marker icon.
    _draw_poi_marker(OUT_ROOT / "wf_e2_grid_poi_marker_96x96.png")

    print("[gen_e2_area_grid_placeholders] done")


if __name__ == "__main__":
    main()
