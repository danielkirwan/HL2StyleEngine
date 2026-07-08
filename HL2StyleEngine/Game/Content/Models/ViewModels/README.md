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

- `Breakable_Wooden_Crate.glb` - assigned to the current `Crate_*` rigid-body pickup props. It contains one intact crate mesh named `Cube_Cube_001` and a `Voronoi_Fracture` group with `Cube_Cube_001_fracturepart*` child meshes. Runtime rendering keeps the fracture meshes hidden until the crate is damaged, then hides the intact mesh and hides the nearest visible fracture mesh for each weapon hit point. Current crate health is 100: crowbar hits deal 25 crate damage, pistol/bullet hits deal 34 crate damage, and future shotgun/explosion damage kinds can break instantly.
- `DamagedCrate02.glb` through `DamagedCrate08.glb` - imported damaged variants kept for future experiments, but the current breakable wooden crate does not swap to them at zero health.
- `FirstAidKit01.glb` - used by the crate reward `HealthPack` pickup, which restores health immediately.
- `Battery07.glb` - used by the crate reward `SuitBattery` pickup, which restores suit charge immediately.

The world renderer fits imported GLB bounds to the entity physics box when a rigid body uses `MeshPath`. If a model is missing or unsupported, the game falls back to the primitive box/sphere/capsule render.

The current breakable-crate pass is visual-only while alive: the crate keeps one box collider/rigid body while named GLB parts are hidden for local damage feedback. At zero health, the original crate is hidden and the remaining visible fracture parts are rendered as transient falling/fading debris for 3 seconds before removal. Future debris assets can be stored on level entities through `BreakDebrisModelPaths`, then spawned when the debris spawning path is implemented.

Breakable-crate zero-health behavior: the current wooden crate does not use a damaged-model replacement. It hides the original entity and lets remaining named fracture parts fall, fade, and remove themselves. Treat the old white-flash lesson as the general replacement rule for future systems that do swap objects: warm up the incoming object first, then remove or hide the outgoing object.

Current fracture debris renders each named GLB fragment around its own local center and resolves landing support below that fragment while ignoring the removed source crate. This keeps pieces from appearing attached to the old full-box volume after the crate collider is hidden.

Practice level presentation: `interaction_test.json` is the meshed/default practice level that uses these imported crate and weapon viewmodel assets. `interaction_test_blockout.json` starts from the same default layout and can be refreshed from the currently loaded level when `Load Blockout Practice` is clicked; mesh/material/replacement paths are stripped, primitive blocks render instead, and runtime weapon viewmodels force their primitive fallback geometry instead of imported GLBs for before/after video comparison.