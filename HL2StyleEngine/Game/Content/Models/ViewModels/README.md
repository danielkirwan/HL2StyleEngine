# Imported Model Assets

Converted `.glb` files currently live here and are copied into the game output by `Game.csproj`.

Weapon viewmodels used by current or fallback weapon definitions:

- `gravitygun.glb`
- `Scifi_Handgun_01.glb`
- `test_pistol.glb`
- `Crowbar.glb` - used by the first melee weapon and right-hand camera-mounted swing viewmodel pass. It is not a level hierarchy object; placement is tuned in the Debug window and locked into `WeaponDefinitions.cs`.

Player character test asset:

- `Future_Soldier_02.glb` - imported as the intended local player body source, but not drawn by default because it is a skinned character and the runtime currently supports static GLB meshes only. The editor/free-camera player marker uses the capsule placeholder until glTF skin/joint/animation support is implemented.

World prop test assets:

- `Breakable_Wooden_Crate.glb` - assigned to the current `Crate_*` rigid-body pickup props as the intact crate model.
- `DamagedCrate02.glb` through `DamagedCrate08.glb` - active broken replacement variants used by the object-health system when crates reach zero health.

The world renderer fits imported GLB bounds to the entity physics box when a rigid body uses `MeshPath`. If a model is missing or unsupported, the game falls back to the primitive box/sphere/capsule render.

The current breakable-crate pass keeps the replacement object physical and pickup-capable. Future debris assets can be stored on level entities through `BreakDebrisModelPaths`, then spawned when the debris spawning path is implemented.

Breakable-crate swap behavior: damaged crate GLBs are preloaded during runtime world rebuild, and the break path verifies/prepares the selected damaged model before assigning it to the entity. This should prevent the previous white primitive flash that happened while the damaged replacement was still loading. Treat this as the general replacement rule for future systems: warm up the incoming object first, then remove or hide the outgoing object. Dust, splinter, and debris-piece spawning are still the preferred long-term destruction presentation once those assets exist.

Practice level presentation: `interaction_test.json` is the meshed/default practice level that uses these imported crate and weapon viewmodel assets. `interaction_test_blockout.json` starts from the same default layout and can be refreshed from the currently loaded level when `Load Blockout Practice` is clicked; mesh/material/replacement paths are stripped, primitive blocks render instead, and runtime weapon viewmodels force their primitive fallback geometry instead of imported GLBs for before/after video comparison.