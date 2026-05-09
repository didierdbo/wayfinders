# Jalon 3 -- 3D Backing first prod cell -- Walkthrough Didier

**Status:** landed 2026-05-09 (code shipped in commit `3b46c4a` -- bundled with Coda's substep 13 API commit due to a session-mode collision ; J3 code is intact, see `Cell3DBackingFollower.cs` + `FogTileLayer.cs` diff + `J3CellAuthorityTests.cs`).

**Architecture doc:** `Owner's Inbox/Godot Coaching/Wayfinders - 3D Backing Architecture - 2026-05-09.md` (J3 section + Pattern K + Pattern L).

## What J3 ships

The first **production** cell of the E2 World Map grid is now driven by a 3D-authoritative `Node3D` (Option A locked). Witness cell : **(0, 0)** -- top-left iso pole, chosen for minimum visual blast radius.

The data flow on the witness cell :

```
WitnessNode3D.Position = (0*128, 0, 0*128) [3D AUTHORITY]
        |
        | forward iso projection : screenX = (X-Z)/2 ; screenY = (X+Z)/4
        v
WitnessCell2DPosition (Vector2 in pixel coords)
        |
        | + iso shift + halfH + FogYOffset
        v
fogTileLayer._cells[(0,0)].Cell.Position [2D CONSUMER, set every _Process frame]
```

Pickup is now via 3D `Area3D + ConvexPolygonShape3D rhombus prism` (Pattern J inv. 7). Every other cell of the grid keeps its pre-J3 path entirely (2D `WorldPositionToCell` math).

A `Camera3D` follower (current=true) shadows the prod `Camera2D` each frame, with the locked zoom formula `Camera3D.Size = (viewportH / Camera2D.Zoom.Y) * sqrt(2)` (pinned by `J3CellAuthorityTests.Camera3D_Size_matches_viewportH_over_Camera2D_zoom_times_sqrt2`).

## Test sequence

1. **Pull `dev` to latest.**
2. **Open Godot, run E2 World Map** (the normal flow : E1 → E2).
3. **Open the Output panel.** At startup, search for the J3 canary block :
   ```
   [J3 Cell3DBacking] enabled root Viewport.PhysicsObjectPicking
   [J3 CANARY OK inv.1] Viewport.PhysicsObjectPicking = true
   [J3 CANARY OK inv.2] FollowerCamera3D.Current=true position=(...)
   [J3 CANARY OK inv.3] WitnessArea3D.InputRayPickable=true
   [J3 CANARY OK inv.4] WitnessArea3D.CollisionLayer = 1
   [J3 CANARY OK inv.5] WitnessShape3D ready (Shape != null, Disabled=false)
   [J3 CANARY INFO inv.6] Camera2D anchored ... px from witness center -- frustum check informational only
   [J3 CANARY OK inv.7] Rhombus prism poles correct (halfW=64)
   [FogTileLayer J3] spawned witness 3D follower at cell (0, 0) -- Pattern A in runtime, Option A locked. ...
   ```
   All `[CANARY OK]`, no `[CANARY FAIL]`. `[CANARY INFO inv.6]` is expected (Camera2D anchored on Halfgate, not on (0,0) -- the distance is informational only at startup).
4. **Pan to the top-left corner of the map** (the witness cell (0,0) is at the top-left iso pole of the grid). The cell renders identically to its neighbours (parchment carton, knowledge state Inconnue) -- no visual difference vs pre-J3, that is intentional. The data flow now passes through the 3D Node, but the visual outcome is unchanged.
5. **Hover over cell (0,0).** No visible color change (we did not wire the 3D hover signals to the 2D rendering this jalon -- that is J5/J6 scope). But in the Output panel :
   ```
   [J3 Cell3DBacking] hover ENTER on witness cell (0, 0) -- Area3D fired
   [J3 Cell3DBacking] hover EXIT on witness cell (0, 0)
   ```
   Hover a neighbouring cell (e.g. (1,0) or (0,1)) -- you do NOT see those logs. Neighbours stay on the pre-J3 2D pickup path.
6. **Click cell (0,0) :** Output panel :
   ```
   [J3 Cell3DBacking] click on witness cell (0, 0) @ 3D world=(0, 0, 0)
   ```
   The 3D world coord of the click is the centre of the witness Node3D.
7. **F debug command on cell (0,0) :** press F. The knowledge cycle (Inconnue → Pressentie → Esquissée → Levée → Scellée) still works because F uses `WorldPositionToCell` (2D path, intact for all cells including the witness). Clean cohabitation.
8. **Zoom test.** Wheel push to zoom in (up to drill or before), then wheel pull to zoom out. The witness cell (0,0) must stay anchored at the same world point -- no visual drift, no jump vs neighbours. If it "breathes" or "hops" relative to its neighbours during zoom, the zoom-sync formula is failing -- log it. Pinned formula : `Camera3D.Size = (viewportH / Camera2D.Zoom.Y) * sqrt(2)`.
9. **Climb test.** Wheel pull at floor (ZoomMin) → climb-out, which silently no-ops (you are at the top of the ladder). No crash.
10. **Exit + re-entry.** NavigateBack to E1, then re-enter E2 -- you should see all `[CANARY OK]` lines again at startup, and the witness cell respawns at (0, 0). No double-spawn (the Output panel shouldn't be flooded with logs).

## Kill-switch

If something visible regresses, flip `EnableJ3Witness` to `false` on the `FogTileLayer` node in the inspector :

```
E2WorldMap → MapPan2DComponent → WorldRoot → FogTileLayer → [Inspector] → EnableJ3Witness
```

The witness cell falls back to pre-J3 behaviour immediately (no recompile, no redeploy). Pattern L locked (cf. archi doc §4).

## Known limitations (J4+ scope)

- Only **one** cell is 3D-backed. The other ~600 cells of the grid stay on the pre-J3 2D path.
- The witness `Node3D.Position` is **static** this jalon -- no animations yet. J6+ will start mutating it for tile lift / rotation / drag-drop.
- The 3D hover signals are NOT wired to the visual rendering -- the witness does not change tint at hover. J5/J6 will plug them in.
- `Cell3DBackingFollower.FindParentCamera2D` walks up the scene tree. If the layer is instantiated outside an `MapPan2DComponent` parent (e.g. test harness), the follower spawns without Camera2D tracking -- degraded mode loggued but not crashed.

## Pin tests

`client/tests/Phase9_3DBacking/J3CellAuthorityTests.cs` : 10 pure-C# pin tests.

- `Witness_2D_position_derived_from_3D_authority_matches_canonical_pre_J3_position` -- 4 cells (0,0), (5,5), (15,8), (29,16).
- `Camera3D_Size_matches_viewportH_over_Camera2D_zoom_times_sqrt2` -- 4 (zoom, viewport) configs.
- `Dropping_sqrt2_factor_breaks_the_zoom_sync_so_the_factor_is_load_bearing` -- sanity guard.
- `Rhombus_prism_poles_anchor_on_cell_size_not_sprite_width` -- Pattern J inv. 7 numerical check.

Total suite : **431/431 green** (421 baseline J2 + 10 J3).

## Reversibility

`git revert` of the J3 code removes :
- `client/components/Cell3DBackingFollower.cs` (new file).
- `client/components/FogTileLayer.cs` -- ~140 lines added (J3 hooks only ; the rest of the file is unchanged).
- `client/tests/Phase9_3DBacking/J3CellAuthorityTests.cs` (new file).

Restores J2 state with no residue.

**Caveat** : the J3 code is bundled inside commit `3b46c4a` (which also carries Coda's API substep 13 from a parallel session). A simple `git revert 3b46c4a` would also revert Coda's API work. If a clean J3-only revert is needed, use `git checkout 7fff6b4 -- client/components/FogTileLayer.cs` (J2 state of FogTileLayer) + `git rm client/components/Cell3DBackingFollower.cs* client/tests/Phase9_3DBacking/J3CellAuthorityTests.cs*`. Lessons-learned for J4+ : declare session mode + delegate one agent at a time per the session-mode coordination rule.
