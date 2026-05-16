# POI Integration Pattern - Quick Reference

**Status:** Locked PR6 (2026-05-15). MVP good for the 6-8 POI per E1 mission scope.
**Origin:** `Owner's Inbox/Godot Coaching/poi-integration-pattern-prebrief.md` (full historical context).

## How to add a POI in 5 steps

### 1. Drop the asset + sidecar

Mira delivers the cleaned PNG to `client/assets/poi/<episode>/<name>.png`
alongside a `<name>.meta.json` sidecar:

```json
{ "anchor_pixel": [538, 573], "display_name": "Halfgate" }
```

`anchor_pixel` is the bitmap (x, y) of the foot-on-tile point. The
runtime aligns this pixel to the iso tile centre.

### 2. (Optional) Save a `.tres` PoiData

For POIs reused across scenes, save the resolved `PoiData` as a
`.tres` next to `client/data/`. MVP can skip this and rely on the
sidecar PNG path field on the spawn entry (see step 3) — `PoiSidecarLoader`
builds the `PoiData` at scene `_Ready`.

### 3. Add a `PoiSpawnEntry` to the scene's `PoisToSpawn` Array

Open the target scene in Godot (`M1Slice.tscn` today). On the root
node's inspector:

- Locate `PoisToSpawn` (Array of Resources).
- Click `+` to add an entry. Choose `New PoiSpawnEntry`.
- Either:
  - assign a saved `Data` `.tres`, **or**
  - set `SidecarPngPath` to `res://assets/poi/e1/<name>.png` (the loader
    derives `.meta.json` automatically).
- Set `Tile` to the iso `(col, row)` where the POI's foot should land.
- (Optional) `DisplayNameOverride` if you want a distinct identity for
  click-dispatch when the same data is spawned at multiple tiles.

### 4. F6 the scene - verify visually

Expected `Output` log lines:

```
[POI SPAWNER] _Ready poiScene=loaded cellSize=128px gridDims=(24,24)
[POI ROUTER] registered, autoload OK ...
[M1 SLICE POI] spawned 'Halfgate' at tile=(12,12) worldPos=(...)
[M1 SLICE POI] spawned 'HalfgateAnnex' at tile=(14,13) worldPos=(...)
[M1 SLICE POI] WirePoisAndRouter done : 2/2 POI(s) spawned.
```

Hover the sprite -> `[POI ROUTER]` emits `PoiHovered` (no log by default,
attach a subscriber to observe). Click -> `[M1Slice] POI clicked : <name>` +
`SceneManager.NavigateTo(...)` dispatch.

### 5. If a POI does not Y-sort correctly

Two POIs overlap and the wrong one claims the click? The router uses
`GlobalPosition.Y` descending (front = larger Y in iso convention). Check
the spawned positions in the log line above; if Y values are inverted from
the iso projection, the tile coord is wrong, not the router.

## Architecture (one-screen recap)

```
[PoiData : Resource]    [PoiSpawnEntry : Resource]    [PoiInputRouter : autoload]
   Texture, anchor,        Data + Tile +                 _Input cascade,
   alpha-mask,             SidecarPngPath               Y-sort descending,
   footprint cache           |                          bitmap hit-test,
       ^                     v                          fallthrough on miss
       |              [Scene root [Export]                      |
       |                Array<PoiSpawnEntry>]                   |
       |                     |                                  v
       +------ [PoiSpawner : Node child] -- SpawnAt -->  [Poi : Sprite2D]
                                                          ParallaxStrength,
                                                          shadow child,
                                                          lift 2px
```

- **Data layer** : `PoiData` describes the POI ; `PoiSpawnEntry` pairs it
  with a per-scene tile.
- **Spawn** : `PoiSpawner` (per-scene `Node`, not autoload) wires data ->
  scene -> world position.
- **Input** : `PoiInputRouter` (autoload) consumes `_Input` events,
  bitmap-hit-tests, emits `PoiHovered` / `PoiClicked`. Subscribers
  disconnect in `_ExitTree` (methodology trap #10).
- **Visual** : `Poi.cs` carries the four pion-sur-plateau ingredients
  (shadow, lift, rim PR5.X, parallax). Locked memo
  `project_wayfinders_pion_sur_plateau`.

## Known debts (tracked, not yet shipped)

- **Rim shader (PR5.X)** : the terracotta sun-side highlight is deferred.
  The `poi_rim.gdshader` file is on disk but not wired. PR5 fell back to
  Modulate-only shadow after a Forward+ d3d12 fragment-output disconnect ;
  reopen with a frame-capture (RenderDoc) when PR5.X starts. See `Poi.cs`
  XML doc "PR5.X" paragraph.
- **No Camera2D in M1Slice** : M1Slice is a Compagnie+Mission UI scene
  with only the Compagnie SubViewport `Camera3D`. POI parallax is therefore
  disabled in this scene (null camera passed to spawner). Visual validation
  of parallax stays on `PoiSpawnProbe`. Dette : when an iso world scene
  with a Camera2D is integrated as the M1 production play surface, pass
  that camera into `PoiSpawner.SpawnAt`.
- **Single SceneManager dispatch target** : `M1Slice.PoiClickNavigateTo`
  is one string for all POIs. When a second POI needs a different screen
  id, swap to a `Dictionary<string, string>` keyed by DisplayName.
- **J3/J4 `PoiDefinition`** : the older 2D-pixel POI model on `E1WorldMap`
  is **not migrated**. M2 decision per prebrief anti-checklist.
