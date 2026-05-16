"""
One-shot patch script applied to client/scenes/screens/E2AreaMap.cs to
wire the E2.1 area-grid render layer into the existing Configure path.
Idempotent : second run is a no-op because the search needles target
the pre-patch text.

Rune E2.1, 2026-05-16.
"""

from __future__ import annotations

import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
p = REPO / "client" / "scenes" / "screens" / "E2AreaMap.cs"
src = p.read_bytes()

# --- Patch 1: ResolveAreaAssets returns a 3-tuple including useAreaGridLayer ---

old1 = (
    b"    /// <summary>\n"
    b"    /// Resolve an areaId to its (assetKey, poisResPath) pair. The MVP\n"
    b"    /// has exactly one entry ; throws if the areaId is unknown so a\n"
    b"    /// payload typo surfaces loud rather than silently swapping in a\n"
    b"    /// placeholder texture. Pure-static so it stays trivially testable.\n"
    b"    /// </summary>\n"
    b"    private static (string assetKey, string poisResPath) ResolveAreaAssets(string areaId)\n"
    b"    {\n"
    b"        return areaId switch\n"
    b"        {\n"
    b'            "halfgate" => ("e2.area.base", "res://data/area_pois.tres"),\n'
    b"            _ => throw new System.ArgumentException(\n"
    b"                $\"[E2AreaMap] Unknown areaId '{areaId}'. Known : 'halfgate' (MVP single-area). \" +\n"
    b'                $"Add a switch arm here when introducing a new area.",\n'
    b"                nameof(areaId)),\n"
    b"        };\n"
    b"    }\n"
)

new1 = (
    b"    /// <summary>\n"
    b"    /// Resolve an areaId to its (assetKey, poisResPath, useAreaGridLayer)\n"
    b"    /// triple. The MVP has exactly one entry ; throws if the areaId is\n"
    b"    /// unknown so a payload typo surfaces loud rather than silently\n"
    b"    /// swapping in a placeholder texture. Pure-static so it stays\n"
    b"    /// trivially testable.\n"
    b"    ///\n"
    b"    /// <para>\n"
    b"    /// <b>E2.1 (2026-05-16) -- area-grid layer rerouting.</b>\n"
    b"    /// <c>useAreaGridLayer = true</c> rewires the screen to render the\n"
    b'    /// 8x8 intra-area grid (via <see cref="BuildAreaGrid"/> in the\n'
    b"    /// partial file) instead of the legacy free-form POI plate. The\n"
    b"    /// area-grid background asset replaces the cite-level bitmap ;\n"
    b"    /// the legacy POI hotspots (marche, port, QG, npc candidates from\n"
    b"    /// <c>area_pois.tres</c>) are NOT spawned for that area. Halfgate\n"
    b"    /// is the only area on the area-grid path at E2.1 ; the legacy POI\n"
    b"    /// flow stays available for any future area that wants the\n"
    b"    /// free-form layout (none planned today). Flagged for Varn\n"
    b"    /// reconciliation : the legacy POIs may need either retirement\n"
    b"    /// (their roles fold into the grid + future cite-overlay) or\n"
    b"    /// promotion (KeyBuilding POIs become a dedicated layer above the\n"
    b"    /// grid). Out of E2.1 scope.\n"
    b"    /// </para>\n"
    b"    /// </summary>\n"
    b"    private static (string assetKey, string poisResPath, bool useAreaGridLayer) ResolveAreaAssets(string areaId)\n"
    b"    {\n"
    b"        return areaId switch\n"
    b"        {\n"
    b'            "halfgate" => ("e2.area_grid.background", "res://data/area_pois.tres", true),\n'
    b"            _ => throw new System.ArgumentException(\n"
    b"                $\"[E2AreaMap] Unknown areaId '{areaId}'. Known : 'halfgate' (MVP single-area). \" +\n"
    b'                $"Add a switch arm here when introducing a new area.",\n'
    b"                nameof(areaId)),\n"
    b"        };\n"
    b"    }\n"
)

if old1 not in src:
    sys.exit("patch 1 needle not found")
src = src.replace(old1, new1, 1)

# --- Patch 2: Configure destructure to capture useAreaGridLayer ---

old_destr = b"        var (assetKey, poisResPath) = ResolveAreaAssets(areaId);\n"
new_destr = b"        var (assetKey, poisResPath, useAreaGridLayer) = ResolveAreaAssets(areaId);\n"
if old_destr not in src:
    sys.exit("destructure needle not found")
src = src.replace(old_destr, new_destr, 1)

# --- Patch 3: branch SpawnPois vs BuildAreaGrid ---

old_branch = (
    b"        SpawnPois(assetResolver);\n"
    b"\n"
    b"        // Phase 8 preflight self-check : surfaces the area binding +\n"
)
new_branch = (
    b"        // E2.1 path : when useAreaGridLayer is true, the legacy free-form\n"
    b"        // POI plate is replaced by the 8x8 area-grid render layer. The\n"
    b"        // legacy SpawnPois call is skipped so the marche/port/QG hotspots\n"
    b"        // from area_pois.tres do not coexist with the grid (their\n"
    b"        // image-space positions in the 3840x2160 frame are outside the\n"
    b"        // 2048x2048 grid bounds, so they would not visually render but\n"
    b"        // they would still register Area2D hit-tests at world coords\n"
    b"        // outside the camera view, surfacing nothing useful).\n"
    b"        if (useAreaGridLayer)\n"
    b"        {\n"
    b"            BuildAreaGrid();\n"
    b"            ApplyTutorialMissionPopEffect();\n"
    b"        }\n"
    b"        else\n"
    b"        {\n"
    b"            SpawnPois(assetResolver);\n"
    b"        }\n"
    b"\n"
    b"        // Phase 8 preflight self-check : surfaces the area binding +\n"
)
if old_branch not in src:
    sys.exit("legacy-spawn branch needle not found")
src = src.replace(old_branch, new_branch, 1)

# --- Patch 4: extend the preflight log to surface area-grid status ---

old_log = (
    b"        GD.Print(\n"
    b"            $\"[E2AreaMap] Configure(areaId='{areaId}') -> {_poiHandlers.Count} POI spawned, \" +\n"
    b"            $\"asset key='{assetKey}', pois='{poisResPath}', \" +\n"
    b'            $"world image={_panComponent.WorldImageSize}, " +\n'
    b'            $"camera initialCenter={initialCenter}, " +\n'
    b'            $"climb wired={_climbRequestedHandler is not null}, " +\n'
    b'            $"drill wired={_drillRequestedHandler is not null} -> {DrillTargetScreenId}");\n'
)
new_log = (
    b"        var (fogCount, partialCount, revealedCount) = useAreaGridLayer\n"
    b"            ? CountRevealStates()\n"
    b"            : (0, 0, 0);\n"
    b"        GD.Print(\n"
    b"            $\"[E2AreaMap] Configure(areaId='{areaId}') -> \" +\n"
    b'            $"useAreaGridLayer={useAreaGridLayer}, " +\n'
    b'            $"legacy_poi_count={_poiHandlers.Count}, " +\n'
    b'            $"area_grid_reveal=(fog={fogCount}, partial={partialCount}, revealed={revealedCount}), " +\n'
    b"            $\"asset key='{assetKey}', pois='{poisResPath}', \" +\n"
    b'            $"world image={_panComponent.WorldImageSize}, " +\n'
    b'            $"camera initialCenter={initialCenter}, " +\n'
    b'            $"climb wired={_climbRequestedHandler is not null}, " +\n'
    b'            $"drill wired={_drillRequestedHandler is not null} -> {DrillTargetScreenId}");\n'
)
if old_log not in src:
    sys.exit("log needle not found")
src = src.replace(old_log, new_log, 1)

# --- Patch 5: _ExitTree should tear down the area-grid before the legacy POI cleanup ---

old_exit = (
    b"    public override void _ExitTree()\n    {\n        // Disconnect chrome handlers first.\n"
)
new_exit = (
    b"    public override void _ExitTree()\n"
    b"    {\n"
    b"        // E2.1 -- tear down the area-grid layer first (disconnects\n"
    b"        // the cell-hover + npc-hover handlers, frees the AreaGridLayer\n"
    b"        // Node2D subtree which in turn frees the TileRevealRenderController\n"
    b"        // child whose own _ExitTree disconnects from GameState.\n"
    b"        // TILED reveal state in GameState.TileRevealStates persists --\n"
    b"        // that is the intended sparse-dict semantic (a future re-entry\n"
    b"        // to E2AreaMap on the same session would observe the already-\n"
    b"        // partial cells without re-popping the tuto mission).\n"
    b"        TearDownAreaGrid();\n"
    b"\n"
    b"        // Disconnect chrome handlers first.\n"
)
if old_exit not in src:
    sys.exit("_ExitTree needle not found")
src = src.replace(old_exit, new_exit, 1)

p.write_bytes(src)
print(f"patched {p.name}, new size: {len(src)}")
