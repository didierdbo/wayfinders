# tools/

Repository-level developer scripts. Pure Python, stdlib-first.

## Inventory

- **`generate_asset_placeholders.py`** -- generate solid-color placeholder PNGs
  for every key in `client/data/asset_keys.json` (Mira J2 deliverable).
  Idempotent; pass `--force` to overwrite.

- **`validate_user_assets.py`** -- scan Godot's `user://wayfinders_visual_assets/`
  for PNGs missing an alpha channel (RGBA / color_type 6). PNGs without alpha
  silently break `CanvasItem.modulate.a` cross-fades.
  Auto-detects the per-platform Godot user data dir; pass a path or set
  `WAYFINDERS_USER_ASSETS` to override. Exits `1` on failure -- pre-commit /
  CI ready.

  ```
  python tools/validate_user_assets.py
  python tools/validate_user_assets.py /custom/path
  ```
