# Six Room Test

Created 2026-09-08. Source: `Game/Content/Levels/sixRoomTest.json`.

## Brief And Scale

An enclosed exploration, physics and puzzle level based on the scale and interaction style of `interaction_test.json`. One large central hall connects directly to five distinct rooms. The 2026-09-08 puzzle pass adds a rolling shutter to each entrance, with a different mechanism for opening it. There are still no enemies. The original 700 entities and their transforms are preserved; 58 puzzle entities were added, for 758 total.

The reference floor is 18 x 28 m (504 square metres). The six new rooms total 2,496 square metres, plus 40 square metres of connections: 2,536 square metres, or 5.03 times the reference footprint. This compares gross floor area before subtracting walls and props. Player, weapon and crate scale is unchanged.

## Launch And Edit

- Run `LaunchGame.bat` or `LaunchSixRoomTest.bat` at the project root. Both explicitly open `sixRoomTest.json`.
- Or run `.\.dotnet\dotnet.exe run --project Game/Game.csproj -- --level Game/Content/Levels/sixRoomTest.json`. The October 5 toolchain update requires .NET 10; an installed compatible SDK can be used instead of the local executable.
- In HS2Editor, load `sixRoomTest.json` from the Levels panel, then use Play Selected Level.
- The project startup level remains `basementLevel.json`. Existing levels remain separate files.
- An explicit `--level` launch starts at the authored spawn with the default loadout. It no longer automatically restores an unrelated save's position and inventory. Ordinary launches without `--level` retain existing automatic save loading.
- Make future changes in the editor and save the source JSON. The generator records the initial design; it does not run at game startup or overwrite editor changes automatically.

## Layout

North is positive Z. Spawn is at (0, 0.06, -12), facing north.

```text
                       LOADING BAY
                         20 x 16
                            |
     MEDICAL SUPPLIES -- CENTRAL HALL -- UTILITY ROOM
         16 x 20          28 x 32          16 x 20
                            |
       FREIGHT STORE ------- + -------- WORKSHOP
         16 x 20                         16 x 20
                          SPAWN
```

This diagram is schematic. Each side room has its own entrance into the hall; none requires crossing another side room.

| Space | Size | Height | Identity and activity |
| --- | --- | --- | --- |
| Central Hall | 28 x 32 m | 5 m | Columns, beams and dispatch desk; broad running routes, starter crates and ammunition. |
| Freight Store | 16 x 20 m | 3.5 m | Three crate stacks, a central aisle, loose throwing props and perimeter supplies. |
| Workshop | 16 x 20 m | 3.5 m | Repair bench, console, arcade cabinet and visible upper-shelf batteries for gravity-gun reach testing. |
| Medical Supplies | 16 x 20 m | 3.5 m | Cooler light, a distinct floor finish, supply counter and additional pickups around the perimeter. |
| Utility Room | 16 x 20 m | 4 m | Pipe racks, electrical panels, multiple approaches around the equipment and loose props. Panels are scenery for now. |
| Loading Bay | 20 x 16 m | 4 m | Long throw/shot lanes, five crate targets, ammunition and two low platforms for jumping. |

Room bounds in metres, X then Z:

- Central Hall: [-14, 14], [-16, 16].
- Freight Store: [-32, -16], [-21, -1].
- Workshop: [16, 32], [-21, -1].
- Medical Supplies: [-32, -16], [1, 21].
- Utility Room: [16, 32], [1, 21].
- Loading Bay: [-10, 10], [18, 34].

Each connection is 4 m wide and 2 m long, with a 3.2 m opening height. Side entrances are at Z=-8 and Z=8. Wall thickness slightly reduces nominal clearance.

## Activities

- The central hall is a recognisable return point. Columns and equipment sit away from its main running route.
- Different floor finishes, light colours and prop arrangements distinguish the branches. Shutters now gate the original openings; the hall remains the return point.
- Once a room is unlocked, its optional activities remain: smash freight stacks, throw at distant crates, pull batteries from a high shelf, collect supplies, or jump onto the loading-bay platforms.
- There are now 27 breakable crates using the existing 100-health fracture/loot system: four crowbar hits or three pistol bullets. One additional movable crate is deliberately indestructible for the pressure-plate puzzle; four static wooden stands support puzzle items.
- Health packs use `FirstAidKit01.glb`; suit pickups use `Battery07.glb`. Both are dynamic and can be held by the gravity gun before collection with E. Health packs remain unconsumed at full health.
- Small half-metre wooden supply boxes grant 24 bullets each with E. They use the existing crate mesh as an ammo-container placeholder and are not damageable. Full-size crates are breakable. Ammunition goes directly to the weapon system.
- The default loadout supplies the crowbar, pistol and gravity gun. Weapons and ammo remain outside the inventory grid.

## Construction

Existing basement floors, ceilings, double-sided walls, pillars and beams form the architecture. Floor tiles use approximately 4 m modules to retain texture scale. The wall model's long axis is local Z and its thin axis is local X; horizontal wall runs rotate this shape through 90 degrees.

Continuous invisible slabs provide floor and ceiling collision for each room and connection. Visible floor/ceiling tiles are separate meshes. Rectangular wall modules use fitted box colliders. Entrances are real gaps with separate headers. The new rolling-door frames use actual static mesh colliders; the shutter leaves have thin fitted box colliders that travel with the leaves.

## Puzzle Pass And Solutions

All five rolling shutters stay open once released. Default weapons, existing architecture, lighting and supplies are unchanged. Controls use the existing E/item-use workflow; when the gravity gun holds a pickup, E collects it. Inventory Combine repairs the medical cable. No new control binding is required.

| Room | Puzzle | Solution and item locations |
| --- | --- | --- |
| Freight Store | Keyed override | Move or smash the cover crate near the southwest hall corner. The Maintenance Key is on the wooden stand at (-11.5, -13), X/Z. Use it at the panel beside Freight. |
| Workshop | Gravity-gun retrieval | Pull the Workshop Cable from the high hall shelf at (11.8, -11.9), then collect it with E while held. Install it in the Workshop panel. The shelf is outside the locked room. |
| Medical Supplies | Cable repair | Take the Damaged Cable from the hall stand at (-11.5, 12). Spare Wire is on the Workshop repair bench at (25.5, -15). Combine them, install the Repaired Cable at Medical, then pull the adjacent release lever. |
| Utility Room | Crate pressure plate | Place a crate on the grating plate at (10.5, 11.5). Let it settle, then pull the nearby lever while it remains weighted. An indestructible 12 kg crate starts beside it at (8, 11.5). |
| Loading Bay | Three power feeds | Collect Freight Feed from the Freight stand (-29, -10), Medical Feed from the Medical counter (-24.5, 15), and Utility Feed from the Utility stand (27, 15). Install each in its named socket beside Loading, then pull the master release. |

Freight, Workshop and Utility can be tackled independently from the hall. Workshop provides the part for Medical. Loading is the final gate and requires supplies from Freight, Medical and Utility. No required item is spawned through random crate loot or hidden behind its own gate. All seven world puzzle pickups are gravity-gun compatible, indestructible, and protected from inventory discard. Ingredients and keys consume one inventory slot each. The eight item definitions include the crafted Repaired Cable, which is not a separate world pickup.

Red/green indicator blocks show slot completion and lever readiness. Release levers have a separate latched-open indicator. The plate goes red again when its weight is removed, but an already-released Utility shutter stays open. Indicators change only their own primitive colours; there are no renderer, texture or light-switch changes. The cable meshes provide surface conduit dressing, not simulated electrical connections.

### Authoring And Runtime

- Added objects contain `SRPuzzle_` in their names. Filter this text in Hierarchy to locate the puzzle pass.
- Each shutter has `PuzzleDoor`, a unique `StateId`, and `LiftHeight = 3.35`. The defaults on older levels remain 3 metres. The leaf translates upward; it does not deform into a roll-up animation.
- Frames and leaves come from separate nodes in `Rollup Door 1.glb`. Derived assets are `Content/Models/Puzzles/RollupDoor1_Frame.glb` and `RollupDoor1_Leaf.glb`. Source materials, embedded images and transforms are retained. The original GLB is untouched, and the stationary frame has a real mesh collider.
- Slot controls use `PuzzleSlot`, `RequiredItem`, `ConsumesItem = true`, and optional shutter `Targets`. A completed slot can instead satisfy a release lever's `RequiredStates`.
- `PressurePlate` belongs on a horizontal static box-shaped rigid body. It uses that body's top surface and yaw-rotated footprint. `PressurePlateMinMass = 8` kg and `PressurePlateSettleSeconds = 0.35` are editable in the Inspector. Eligible dynamic bodies must rest on it; held bodies, airborne bodies, broken/hidden objects and the player do not count. Multiple supported bodies can contribute mass.
- `PuzzleIndicator` uses `RequiredStates` to control a small primitive's red/green colour. Use a non-colliding Prop with no mesh for these indicators. It is not a point-light switch.
- Slot completion and lever release use the existing `SolvedPuzzles` save list. Live plate occupancy is deliberately not saved: restoring an unfinished puzzle requires weighting it again. Restoring a solved Utility lever opens its shutter even without the weight.
- Runtime plate sensing lives in `Game/Puzzles/PressurePlateSensor.cs`; integration, indicators and configured lift height live in `HL2GameModule.Puzzles.cs`. The shared module only calls these helpers.
- The one-time `add-six-room-puzzles` authoring command appends to an existing layout and refuses a second application. It does not run at game startup. Edit the saved level in HS2Editor from now on; do not regenerate the initial level over those edits.

### Play-Test Checklist

1. Fresh launch: all five shutters are closed; the hall, key, retrieval cable and pressure-plate weight are accessible.
2. Wrong items do not consume inventory or open gates. Repair works whichever ingredient is selected first.
3. Gravity-gun hold plus E collects the cable rather than dropping it. Check whether the high cable is easy enough to spot and pull clear of its shelf.
4. A held crate does not power the plate. A dropped, settled crate does. Remove it before pulling the lever and the lever should lose power; remove it after release and the door should remain open.
5. All raised shutters leave clear walk-through gaps and their frames remain in place. Check the approach and scale from both sides.
6. Loading requires all three feeds and a deliberate lever pull. Backtracking through completed rooms stays available.
7. F6 resets the level for another run. Existing save/load should restore completed mechanisms without treating an empty plate as occupied.

Automated checks pass for all 758 entity transforms, assets, clearance, prop settling, actual runtime puzzle operations, symmetric crafting, wrong-item rejection, prerequisite gating, plate sensing/release/latching, indicator colours and serialized solved-state restoration. The production player motor is blocked by all five closed shutters and traverses all five actual mesh-frame openings after release. All panels are targetable from the hall and the gravity-gun query targets the high cable. These are not a substitute for a full manual playthrough or a judgement of puzzle feel.

The whole solution built with zero warnings/errors. The separate authoring-tool build succeeded with a NU1900 warning because NuGet vulnerability metadata was unavailable. Live inspection confirmed textured architecture, shutters and control panels. A bounded normal Debug capture at 1920x1009 (180 frames after 120 warmup) measured 22.04 ms/frame, about 45.4 FPS, p95 29.54 ms; physics averaged 13.00 ms and world submission 6.64 ms. This is a stationary capture, not a performance guarantee throughout the level.

## Inventory Repair Follow-Up (2026-09-08)

For the Medical puzzle, collect Damaged Cable and Spare Wire, open the inventory, select either ingredient and choose Combine. Click the other ingredient to produce one Repaired Cable. Keyboard/controller users can select the second ingredient and confirm with E/X. Esc or I/Back cancels without consuming either item. Install the repaired cable at the Medical socket and pull its release lever.

Fixed the menu-to-target input path: quick press/release clicks are retained even within a single game frame; native menu clicks use current hit testing rather than last frame's row; a stationary pointer no longer steals keyboard/controller selection; opening the item menu preserves its source. The recipe and level data have not changed.

After building the solution and `Tools/LevelAuthoring`, run `.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Debug/net10.0/LevelAuthoring.dll validate-inventory-input`. This exercises the native RmlUi document and ImGui fallback input paths, both ingredient orders, invalid/self/empty targets, cancel, overflow dragging and existing ammo crafting. Native hit testing uses the bridge from the Game Debug output; this CPU harness does not submit GPU frames or replace a manual playtest. The original puzzle harness tested crafting directly and did not cover the menu-to-target click sequence. The October 5 inventory refresh adds temporary overflow, returning remaining items to the world when inventory closes. Level geometry and puzzle requirements are unchanged; see `PERSISTENCE_AND_INVENTORY.md` for current persistence/toolchain details.

## Lighting

`UsePointLights: true` enables lighting from level data. This level has 51 lights and simple fixtures assembled from existing beams with small diffuser strips. The renderer evaluates up to 32 nearby point lights on textured meshes using colour, intensity, range, normals and smooth distance falloff. The editor uses the same path.

The Toolbar's Scene Point Lights checkbox is saved with the level and supports undo. Select an individual PointLight to tune position, colour, intensity and range in the Inspector. Intensity zero disables that light. Existing levels default to point lights off until explicitly enabled.

Lighting is currently unshadowed and can pass through walls. Light groups, switches, spotlights, shadows and emission maps are future work. Non-uniformly scaled model normals now use the inverse-transpose matrix.

## Validation And Follow-Up

### September 8 Performance Fix

Visual Studio's Game and Game - Font Preview profiles, `LaunchGame.bat`, and `LaunchSixRoomTest.bat` all select this level explicitly. The standalone editor still follows `HS2Project.json`.

The initial 700-entity layout exposed repeated full-list searches for interaction definitions in per-frame and fixed-step code. The resulting slow frames caused multiple physics catch-up ticks, magnifying the cost. Rebuilding a direct runtime-entity-to-definition lookup at level load removes those searches. Immutable collider bounds are calculated once per collider, and dynamic support checks discard distant candidates before constructing support polygons. Physics tick/substep counts, lights, meshes and level content are unchanged.

Measured at the unchanged central-hall spawn in Debug, 1920x1009, VSync enabled, 120 warmup frames followed by 180 measured frames. The initial comparison used an isolated output folder to avoid a temporary Visual Studio build-file lock; the final capture used the normal Debug output with temporary probes removed:

| CPU frame stage | Before (isolated) | After (isolated) | Final normal Debug |
| --- | ---: | ---: | ---: |
| Update | 13.50 ms | 1.65 ms | 1.70 ms |
| Physics, including catch-up ticks | 206.49 ms | 7.34 ms | 10.81 ms |
| World submission | 6.17 ms | 5.63 ms | 5.75 ms |
| Submit/presentation wait | 0.12 ms | 1.88 ms | 6.39 ms |
| Total measured frame stages | 226.39 ms | 16.58 ms | 24.74 ms |
| Approximate frame rate | 4.4 FPS | 60.3 FPS | 40.4 FPS |

The capture includes render submission/presentation waits, but is not a GPU timestamp profile. It excludes event pumping and startup asset loading. The remaining variation and presentation wait have not been fully profiled; this is not a locked-60 result. Do not treat the stationary result as a full-level or debris stress-test guarantee. Renderer frustum culling, tighter light selection and broader physics spatial indexing remain future optimizations; the measured bottleneck in this report was CPU interaction/physics work.

For a bounded repeat, set the process-local environment variable `HS2_PROFILE_FRAMES=180`, then launch the game with this level. `EngineHost` discards 120 warmup frames, prints frame-stage averages and p95 to stdout, and exits the game automatically. Leave the variable unset for normal play; it is not set in any launcher or Visual Studio profile.

Additional regression command after building `Tools/LevelAuthoring`: `.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Debug/net10.0/LevelAuthoring.dll validate-collider-bounds`. This checks randomized rotated/scaled/moved boxes, spheres, capsules, default bounds and mesh contact. Run `validate-six-room-test` with the same executable for the saved level's geometry, assets, traversal and settling checks.

F2's shared panels are still available. View > Restore Editor Panels resets their layout; View > Debug / Weapon Tools exposes the existing F3 window. Collapsed Toolbar/Inspector panels are expanded on the first F2 entry. This does not add the standalone Content Browser or prefab/UI management to F2. Live game textures were checked; automated F2 keyboard injection did not register during verification, so manual confirmation of the recovery menu remains outstanding.

Run `dotnet run --project Tools/LevelAuthoring -- validate-six-room-test` to check the saved level. General layout checks treat shutters as open; the additional puzzle harness builds production runtime colliders and separately tests the closed and opened gates, mesh frames and progression. `validate-six-room-puzzles` runs only the puzzle harness.

The generator command `build-six-room-test` refuses to overwrite an existing level. `--replace-generated` explicitly overwrites it and must not be used on editor changes worth keeping. Deliberate changes to the room arrangement may require corresponding updates to the validation expectations.

Live game inspection confirmed textured architecture, a lit central hall, visible open entrances and player movement input. The editor controller also loads all 700 authored transforms and preserves the lighting flag through serialization. The standalone editor's existing saved panel layout opened collapsed/overlapping during verification, preventing a complete visual inspection of this level in that app. The dedicated game launcher provides a direct testing route. A full manual playthrough and judgement of scale/feel remain part of the next design review.

The puzzle pass above supersedes the initial open-door exploration brief. Manual feedback on discovery, cable retrieval, crate placement and backtracking is the next design review.
