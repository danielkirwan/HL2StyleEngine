# Six Room Test

Created 2026-09-08. Source: `Game/Content/Levels/sixRoomTest.json`.

## Brief And Scale

An enclosed exploration and physics level based on the scale and interaction style of `interaction_test.json`. One large central hall connects directly to five distinct rooms. All five entrances are open. No enemies, keys, locks, circuit puzzles or progression gates are included in this pass.

The reference floor is 18 x 28 m (504 square metres). The six new rooms total 2,496 square metres, plus 40 square metres of connections: 2,536 square metres, or 5.03 times the reference footprint. This compares gross floor area before subtracting walls and props. Player, weapon and crate scale is unchanged.

## Launch And Edit

- Run `LaunchGame.bat` or `LaunchSixRoomTest.bat` at the project root. Both explicitly open `sixRoomTest.json`.
- Or run `dotnet run --project Game/Game.csproj -- --level Game/Content/Levels/sixRoomTest.json`.
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
- Different floor finishes, light colours and prop arrangements distinguish the branches. Bright openings allow a preview from the hall.
- Activities are optional: smash freight stacks, throw at distant crates, pull batteries from a high shelf, collect supplies, or jump onto the loading-bay platforms.
- There are 26 breakable crates using the existing 100-health fracture/loot system: four crowbar hits or three pistol bullets.
- Health packs use `FirstAidKit01.glb`; suit pickups use `Battery07.glb`. Both are dynamic and can be held by the gravity gun before collection with E. Health packs remain unconsumed at full health.
- Small half-metre wooden supply boxes grant 24 bullets each with E. They use the existing crate mesh as an ammo-container placeholder and are not damageable. Full-size crates are breakable. Ammunition goes directly to the weapon system.
- The default loadout supplies the crowbar, pistol and gravity gun. Weapons and ammo remain outside the inventory grid.

## Construction

Existing basement floors, ceilings, double-sided walls, pillars and beams form the architecture. Floor tiles use approximately 4 m modules to retain texture scale. The wall model's long axis is local Z and its thin axis is local X; horizontal wall runs rotate this shape through 90 degrees.

Continuous invisible slabs provide floor and ceiling collision for each room and connection. Visible floor/ceiling tiles are separate meshes. Rectangular wall modules use fitted box colliders. Entrances are real gaps with separate headers, so no full-wall collider crosses a doorway. Future detailed doorframes can use the engine's static mesh-collider path.

## Lighting

`UsePointLights: true` enables lighting from level data. This level has 51 lights and simple fixtures assembled from existing beams with small diffuser strips. The renderer evaluates up to 32 nearby point lights on textured meshes using colour, intensity, range, normals and smooth distance falloff. The editor uses the same path.

The Toolbar's Scene Point Lights checkbox is saved with the level and supports undo. Select an individual PointLight to tune position, colour, intensity and range in the Inspector. Intensity zero disables that light. Existing levels default to point lights off until explicitly enabled.

Lighting is currently unshadowed and can pass through walls. Light groups, switches, spotlights, shadows and emission maps are future work. Non-uniformly scaled model normals now use the inverse-transpose matrix.

## Validation And Follow-Up

Run `dotnet run --project Tools/LevelAuthoring -- validate-six-room-test` to check the saved level. It loads JSON and GLBs through the engine loaders, checks unique names/IDs and assets, flood-fills player-radius clearance, checks the sealed perimeter, tests all five return routes with the production player motor, and tests prop settling with production box physics. These checks passed for the initial layout.

The generator command `build-six-room-test` refuses to overwrite an existing level. `--replace-generated` explicitly overwrites it and must not be used on editor changes worth keeping. Deliberate changes to the room arrangement may require corresponding updates to the validation expectations.

Live game inspection confirmed textured architecture, a lit central hall, visible open entrances and player movement input. The editor controller also loads all 700 authored transforms and preserves the lighting flag through serialization. The standalone editor's existing saved panel layout opened collapsed/overlapping during verification, preventing a complete visual inspection of this level in that app. The dedicated game launcher provides a direct testing route. A full manual playthrough and judgement of scale/feel remain part of the next design review.

Add puzzles only after the room layout is accepted; independent door leaves can later be fitted into the existing openings.
