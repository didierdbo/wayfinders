"""
Generate visual placeholder PNGs for every asset key listed in
``client/data/asset_keys.json``.

J2 deliverable -- see Rune pre-brief 2026-05-07 §5.

Usage
-----
    python tools/generate_asset_placeholders.py [--force]

The script is idempotent: existing PNGs are skipped unless ``--force`` is
passed. Each placeholder is a solid-color rectangle (palette derived from
Mira's locked palette, picked deterministically by hashing the asset key)
with three centered text lines:

    1. The asset key (e.g. ``e1.bureau``).
    2. The intended resolution parsed from the filename (e.g. ``3840x2160``).
    3. ``Mira placeholder -- overwrite me``.

Naming convention parsed from filename: ``..._<W>x<H>.png``. If the regex
fails the file is skipped with a clear warning -- the key map should be
fixed rather than silently sized to a default.

Path conventions
----------------
* Repo-relative root for outputs: ``client/assets/wayfinders_visual_assets/``.
  The script auto-detects the repo root by looking for ``client/data/asset_keys.json``
  starting at the script directory and walking up.
* Sub-directories are created as needed (``e1/``, ``e2/``, ...).

Dependencies
------------
* Pillow (PIL). If missing, the script prints ``pip install Pillow`` and
  exits with status 2.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:
    print("[gen] Pillow not installed. Install with:")
    print("      pip install Pillow --user")
    sys.exit(2)

# Mira locked palette (Mira v8.1 §8.1 + Wayfinders Visual Brief W4 amendment).
# Six muted earth tones aligned with the Cadastre-iso visual DNA. Picked by
# stable hashing on the asset key so the same key always lands the same color
# (dev pattern recognition).
PALETTE = [
    ("terracotta", (184, 90, 58)),  # #B85A3A
    ("ochre", (200, 146, 74)),  # #C8924A
    ("parchment", (232, 217, 184)),  # #E8D9B8
    ("dusty-teal", (92, 126, 122)),  # #5C7E7A
    ("faded-crimson", (156, 59, 59)),  # #9C3B3B
    ("weathered-grey", (112, 110, 102)),  # #706E66
]

# Dark stamp-ink color for the centered text overlay. Stays readable on every
# palette entry above.
TEXT_COLOR = (32, 26, 22)

RES_REGEX = re.compile(r"_(\d+)x(\d+)\.png$", re.IGNORECASE)


def repo_root_from(start: Path) -> Path | None:
    """Walk up from ``start`` until ``client/data/asset_keys.json`` is found."""
    for candidate in (start, *start.parents):
        if (candidate / "client" / "data" / "asset_keys.json").is_file():
            return candidate
    return None


def color_for_key(asset_key: str) -> tuple[str, tuple[int, int, int]]:
    """Stable picker: same key => same palette entry."""
    h = hashlib.md5(asset_key.encode("utf-8")).digest()
    return PALETTE[h[0] % len(PALETTE)]


def parse_resolution(filename: str) -> tuple[int, int] | None:
    match = RES_REGEX.search(filename)
    if not match:
        return None
    return int(match.group(1)), int(match.group(2))


def find_font(size_px: int) -> ImageFont.ImageFont:
    """Best-effort font lookup with a default fallback."""
    candidates = [
        # Common DejaVu shipped with most Pillow distributions.
        "DejaVuSans-Bold.ttf",
        "DejaVuSans.ttf",
        # Windows fallback.
        "C:/Windows/Fonts/arialbd.ttf",
        "C:/Windows/Fonts/arial.ttf",
    ]
    for name in candidates:
        try:
            return ImageFont.truetype(name, size_px)
        except OSError:
            continue
    return ImageFont.load_default()


def render_placeholder(
    out_path: Path,
    width: int,
    height: int,
    asset_key: str,
    palette_name: str,
    palette_rgb: tuple[int, int, int],
) -> None:
    out_path.parent.mkdir(parents=True, exist_ok=True)

    image = Image.new("RGB", (width, height), palette_rgb)
    draw = ImageDraw.Draw(image)

    # Pick a text size that scales with the smaller image dimension. We keep
    # all three text lines comfortably inside the placeholder regardless of
    # aspect ratio.
    base_size = max(24, min(width, height) // 12)
    title_font = find_font(base_size)
    subtitle_font = find_font(int(base_size * 0.65))
    footnote_font = find_font(int(base_size * 0.45))

    line1 = asset_key
    line2 = f"{width}x{height} ({palette_name})"
    line3 = "Mira placeholder -- overwrite me"

    # Compute total stack height to vertically center the text block.
    def text_size(text: str, font: ImageFont.ImageFont) -> tuple[int, int]:
        # Pillow >= 9 uses textbbox; older versions fallback to textsize.
        if hasattr(draw, "textbbox"):
            x0, y0, x1, y1 = draw.textbbox((0, 0), text, font=font)
            return x1 - x0, y1 - y0
        return draw.textsize(text, font=font)  # type: ignore[attr-defined]

    w1, h1 = text_size(line1, title_font)
    w2, h2 = text_size(line2, subtitle_font)
    w3, h3 = text_size(line3, footnote_font)

    gap = max(8, base_size // 6)
    total_h = h1 + gap + h2 + gap + h3
    start_y = (height - total_h) // 2

    draw.text(((width - w1) // 2, start_y), line1, fill=TEXT_COLOR, font=title_font)
    draw.text(((width - w2) // 2, start_y + h1 + gap), line2, fill=TEXT_COLOR, font=subtitle_font)
    draw.text(
        ((width - w3) // 2, start_y + h1 + gap + h2 + gap),
        line3,
        fill=TEXT_COLOR,
        font=footnote_font,
    )

    image.save(out_path, format="PNG", optimize=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing PNGs even if they already exist.",
    )
    args = parser.parse_args(argv)

    root = repo_root_from(Path(__file__).resolve().parent)
    if root is None:
        print("[gen][error] Could not locate repo root (no client/data/asset_keys.json found).")
        return 1

    keys_path = root / "client" / "data" / "asset_keys.json"
    assets_root = root / "client" / "assets" / "wayfinders_visual_assets"

    print(f"[gen] Reading {keys_path.relative_to(root)}")
    with keys_path.open("r", encoding="utf-8") as f:
        payload = json.load(f)

    keys: dict[str, str] = payload.get("keys", {})
    if not keys:
        print("[gen][error] No keys in asset_keys.json -- nothing to generate.")
        return 1

    created = 0
    skipped = 0
    failed = 0

    print(f"[gen] {len(keys)} keys -- generating into {assets_root.relative_to(root)}")
    for key, rel_path in sorted(keys.items()):
        out_path = assets_root / rel_path
        filename = out_path.name
        resolution = parse_resolution(filename)
        if resolution is None:
            print(f"[gen][skip] {key:30s} -> {rel_path} (no _WxH suffix found)")
            failed += 1
            continue
        width, height = resolution
        palette_name, palette_rgb = color_for_key(key)

        if out_path.exists() and not args.force:
            print(
                f"[gen][keep] {key:30s} -> {rel_path:60s} ({width}x{height}, {palette_name}) [exists]"
            )
            skipped += 1
            continue

        try:
            render_placeholder(out_path, width, height, key, palette_name, palette_rgb)
        except Exception as ex:
            print(f"[gen][fail] {key:30s} -> {rel_path}: {ex}")
            failed += 1
            continue
        print(f"[gen][ok]   {key:30s} -> {rel_path:60s} ({width}x{height}, {palette_name})")
        created += 1

    print(
        f"[gen] Done. {created} written, {skipped} skipped (use --force to rewrite), {failed} failed."
    )
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
