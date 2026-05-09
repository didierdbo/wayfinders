"""
Validate that user-supplied PNGs in Godot's ``user://wayfinders_visual_assets/``
directory carry an alpha channel (RGBA, PNG color_type 6).

Why this exists
---------------
Godot's ``CanvasItem.modulate.a`` cross-fades silently no-op when the source
texture has no alpha channel: the GPU has nothing to multiply against. A PNG
saved as RGB (color_type 2) or palette (color_type 3) renders fine but cannot
fade. This script catches the mistake before it reaches the scene tree.

Usage
-----
    python tools/validate_user_assets.py [path]

* No arg: auto-detect the per-platform Godot user data dir for project name
  ``Wayfinders`` and scan ``<user_data>/wayfinders_visual_assets/``.
* Arg given: scan that directory recursively.
* Env override: ``WAYFINDERS_USER_ASSETS`` wins over auto-detection.

Exit codes
----------
* ``0`` -- all PNGs are RGBA, or no PNG found.
* ``1`` -- at least one PNG is missing an alpha channel.
* ``2`` -- bad invocation / unreadable directory.

Pure stdlib. No Pillow dependency (kept out of the repo's pyproject.toml on
purpose; manual IHDR parse is 10 lines of struct.unpack).
"""

from __future__ import annotations

import os
import struct
import sys
from pathlib import Path

# PNG color_type values (IHDR byte offset 9)
_COLOR_TYPES = {
    0: ("grayscale", False),
    2: ("RGB no alpha", False),
    3: ("palette", False),  # palette MAY carry tRNS but this script flags it
    4: ("grayscale + alpha", True),
    6: ("RGBA", True),
}

_PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


def _supports_color() -> bool:
    return sys.stdout.isatty() and os.environ.get("NO_COLOR") is None


def _paint(text: str, code: str) -> str:
    if not _supports_color():
        return text
    return f"\x1b[{code}m{text}\x1b[0m"


def default_user_assets_dir() -> Path:
    """Return the platform-appropriate Godot user_data path for Wayfinders."""
    project = "Wayfinders"
    if sys.platform.startswith("win"):
        base = Path(os.environ.get("APPDATA", Path.home() / "AppData" / "Roaming"))
        root = base / "Godot" / "app_userdata" / project
    elif sys.platform == "darwin":
        root = Path.home() / "Library" / "Application Support" / "Godot" / "app_userdata" / project
    else:  # linux / bsd
        xdg = os.environ.get("XDG_DATA_HOME")
        base = Path(xdg) if xdg else Path.home() / ".local" / "share"
        root = base / "godot" / "app_userdata" / project
    return root / "wayfinders_visual_assets"


def read_color_type(png_path: Path) -> int | None:
    """Return color_type from the IHDR chunk, or None if not a valid PNG."""
    try:
        with png_path.open("rb") as f:
            header = f.read(8)
            if header != _PNG_MAGIC:
                return None
            # IHDR length (4 bytes, must be 13) + type "IHDR" (4 bytes) + data (13)
            length_bytes = f.read(4)
            chunk_type = f.read(4)
            if chunk_type != b"IHDR":
                return None
            (length,) = struct.unpack(">I", length_bytes)
            if length != 13:
                return None
            data = f.read(13)
            # data layout: width(4) height(4) bit_depth(1) color_type(1) ...
            return data[9]
    except OSError:
        return None


def scan(root: Path) -> int:
    if not root.exists():
        print(f"[validate_user_assets] directory not found: {root}", file=sys.stderr)
        return 0  # treat missing dir as "no PNG" -> 0 per spec
    if not root.is_dir():
        print(f"[validate_user_assets] not a directory: {root}", file=sys.stderr)
        return 2

    pngs = sorted(p for p in root.rglob("*.png") if p.is_file())
    print(f"[validate_user_assets] scanning {root}")
    print(f"[validate_user_assets] {len(pngs)} PNG file(s) found")
    if not pngs:
        return 0

    bad = 0
    for png in pngs:
        rel = png.relative_to(root).as_posix()
        ct = read_color_type(png)
        if ct is None:
            verdict = _paint("UNREADABLE / not PNG", "31")  # red
            print(f"  - {rel:<60s} color_type=?  {verdict}")
            bad += 1
            continue
        label, has_alpha = _COLOR_TYPES.get(ct, (f"unknown({ct})", False))
        if has_alpha:
            verdict = _paint(f"{label} OK", "32")  # green
        else:
            verdict = _paint(f"{label} -- NO ALPHA, modulate.a will no-op", "31")
            bad += 1
        print(f"  - {rel:<60s} color_type={ct}  {verdict}")

    print()
    if bad:
        print(_paint(f"[validate_user_assets] FAIL: {bad} PNG(s) without alpha", "31"))
        return 1
    print(_paint("[validate_user_assets] OK: all PNGs are RGBA", "32"))
    return 0


def main(argv: list[str]) -> int:
    if len(argv) > 2:
        print(__doc__, file=sys.stderr)
        return 2

    if len(argv) == 2:
        target = Path(argv[1]).expanduser().resolve()
    elif "WAYFINDERS_USER_ASSETS" in os.environ:
        target = Path(os.environ["WAYFINDERS_USER_ASSETS"]).expanduser().resolve()
    else:
        target = default_user_assets_dir()

    return scan(target)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
