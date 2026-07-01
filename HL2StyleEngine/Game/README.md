# Game Implementation Notes

This file tracks completed gameplay-facing systems and the current implementation shape.

## Current Gameplay Project

The playable prototype lives in `Game` and is driven by `HL2GameModule`.

The current test level is loaded from `Content/Levels/interaction_test.json` at runtime. If the level is missing, the game regenerates it from `SimpleLevel.BuildInteractionTestFile`.

## Completed Systems

- Source-style first-person movement and camera.
- Runtime/editor level loading through `LevelEditorController`.
- Inventory, storage box, item collection, item use, item combining, stack splitting, discarding, and save/load persistence.
- RmlUi-backed gameplay UI path with ImGui fallback/preview support.
- Typewriter-style save slots using ink ribbons.
- Locked doors/chests, puzzle slots, puzzle doors, persistent opened/solved state, and item expiration when locks are complete.
- Physics pickup/drop/throw for dynamic rigid bodies.
- Weapon framework with inventory-owned weapons, ammo use, weapon switching, firing cooldowns, traces, melee swings, and viewmodel fallback geometry.
- First-pass GLB weapon model loading: weapon definitions try their `ModelAssetPath` before falling back to primitive viewmodels. Gravity Gun, pistol, and crowbar viewmodels are placed on the right-hand side of the screen for the current camera-mounted weapon pass; viewmodel placement maps positive local X to screen-right while rotation keeps a proper camera basis to avoid weapon orbiting when turning. The Debug window includes live viewmodel tuning sliders for model offset, model euler rotation, model scale, and muzzle offset, plus a copy-to-clipboard C# snippet for locking tuned values into `WeaponDefinitions.cs`.
- First-pass GLB world prop rendering: rigid bodies can point `MeshPath` at an imported `.glb`, and the renderer fits imported bounds to the existing physics box. All current throwable `Crate_*` props use the imported breakable wooden crate model.
- Local player character placeholder hook: `Future_Soldier_02.glb` still preloads through the shared GLB model cache, but the imported full-body mesh is disabled by default because it is a skinned character and the runtime currently renders static GLB meshes only. Editor/free-camera inspection now shows the player capsule placeholder until glTF skin/joint/animation support is implemented.
- Configurable object health for box, rigid-body, and prop entities, with editor-selected broken replacement models, save/load persistence for broken state, and current crates swapping to random damaged crate GLBs while keeping physics/pickup enabled.

## Weapon System

Weapon logic has been split out of `HL2GameModule` into `Game.Weapons`.

- `Weapons/WeaponSystem.cs` owns equipped weapon state, switching, cooldowns, firing, ammo consumption, traces, center-screen melee hits, alternating melee swing variants, and fallback viewmodel rendering.
- `Weapons/WeaponDefinitions.cs` defines the prototype weapons, their tuning, inventory item ids, fallback viewmodel pieces, and model asset paths.
- `Weapons/IWeaponHost.cs` defines the world services weapons need without making the weapon system own gameplay state.
- `HL2GameModule.WeaponHost.cs` adapts the game module to the weapon system: input, inventory counts, raycasts, physics impulses, weapon damage, held objects, messages, primitive drawing hooks, and cached GLB model drawing.
- Weapon viewmodels are runtime camera-mounted objects, not level entities, so they do not appear in the editor hierarchy. Use the Debug window's Weapon Viewmodel Tuning section while the weapon is equipped to adjust placement and copy C# values back into `WeaponDefinitions.cs`.

Current prototype weapons:

- Gravity Gun: pulls dynamic objects from a longer attraction range, slows the pull based on prop mass, locks into held mode once the object reaches grab distance, keeps the viewmodel visible while holding, launches held objects with higher force, can secondary-fire a short electric blast that punts nearby physics objects and applies breakable-object damage, and waits for primary fire release after hand-thrown props so it does not immediately re-catch them.
- Test Pistol: hitscan weapon that consumes `Bullets` from inventory, applies impulse to dynamic targets, and routes bullet damage into the shared object-health path. The imported sci-fi handgun GLB now renders with base texture, imported normals, and simple metallic/roughness lighting; user playtest confirmed it looks much better than the flat texture-only pass.
- Crowbar: first melee weapon. `Crowbar.glb` is available in the default prototype loadout, uses a short center-screen melee trace, applies impulse and 35 melee damage through the shared object-health path, and plays a first-pass right-hand viewmodel swing animation.

Current crowbar viewmodel notes:

- The crowbar is a runtime camera-mounted viewmodel, not a level entity, so it is tuned through the Debug window's Weapon Viewmodel Tuning section.
- The static held orientation is locked in `WeaponDefinitions.cs`: model offset `(0.62, -0.42, 0.82)`, model euler `(0, 13, 0)`, model scale `1.15`, and muzzle offset `(0.62, -0.20, 1.02)`.
- Hit detection stays on the center-screen camera ray/crosshair. The white debug line is the melee trace, not the visual crowbar path.
- The visual swing is separate from the damage trace. It cycles through three right-hand arcs, moving forward in local `+Z` and left in local `-X` toward the crosshair before recovering.
- The current pass is tuned to feel closer to the Half-Life 2 crowbar reference: a fast forward strike, slight variation between hits, and a slower return to the held pose.

Current controls:

- `G` or gamepad right shoulder: cycle owned weapons. The current default cycle is Gravity Gun -> Test Pistol -> Crowbar.
- Left mouse or right trigger: primary fire, including crowbar swing when the crowbar is equipped.
- Right mouse or gamepad left shoulder: secondary action. With the Gravity Gun and no held object, this fires a short electric blast that punts a nearby physics object; while holding an object, it drops it.
- `E` or gamepad X: interact, hand-pickup, or drop.

Target weapon category selection:

The goal is to move from one global cycle button toward a Half-Life 2-style weapon bucket system. Each category is bound to a number key and a D-pad direction. Pressing the same category input repeatedly cycles through owned weapons in that category.

- Category 1 / D-pad Up: melee and grenades. Initial weapon: Crowbar.
- Category 2 / D-pad Right: small weapons. Examples: pistols and similar sidearms.
- Category 3 / D-pad Down: medium weapons. Examples: rifles, shotguns, and Gravity Gun.
- Category 4 / D-pad Left: big weapons. Examples: LMGs, launchers, and heavier weapons.

Selection behavior should be:

- If the player owns one weapon in a category, pressing that category equips it.
- If the player owns multiple weapons in a category, pressing the same category again cycles to the next owned weapon in that category.
- Empty categories should do nothing or show a short "No weapon in slot" style message.
- The current quick-cycle binding can remain as a debug/fallback control until category selection is implemented.

## Inventory Integration

Weapons and ammo are normal inventory items in `Inventory/ItemCatalog.cs`.

- `GravityGun`, `TestPistol`, and `Crowbar` are `Weapon` items.
- `Bullets` are `Ammo`.
- Using a weapon item from the inventory equips it.
- Fresh starts, level resets, and old pre-weapon saves receive the prototype weapon loadout.
- Future saves preserve ammo usage and do not refill bullets on load.

## Object Health and Breakable Props

Object health is now data-driven enough for the first crate-damage pass.

- `LevelEntityDef` stores `Damageable`, `MaxHealth`, `BreakReplacementModelPaths`, `BreakReplacementKeepsPhysics`, and `BreakDebrisModelPaths`.
- Runtime `Entity` mirrors health and broken state. Save data records broken objects by entity name and the replacement model chosen, so broken crates stay broken after load.
- The level editor inspector exposes damage settings for box, rigid-body, and prop entities. The model picker currently shows all `.glb` files under `Content/Models`; this should move toward folders/search as the model library grows.
- Current crate behavior is full health -> broken crate. Bullet hits from any weapon using the weapon damage path, Gravity Gun pulse/blast damage, and crowbar melee hits reduce health. The current `Crate_*` props have 60 health and choose a random `DamagedCrate02` through `DamagedCrate08` replacement while keeping physics/pickup enabled.
- Debris model lists are stored now for future use. Once debris assets and spawning are in, the broken object should replace the original and optionally spawn selected debris models.
- Planned damage sources should call the same object-health entry point: explosions and future enemy/environment impacts. Crowbar melee currently damages objects; hit sounds are still pending.

## Next Likely Work

- Split or retarget the player character into first-person hands/arms so weapon placement can move from camera-mounted offsets to right-hand/arm placement and animation.
- Use the Debug window viewmodel tuning sliders to validate imported viewmodel scale/orientation with `test_pistol.glb`, `gravitygun.glb`, and `Crowbar.glb`, then copy good values back into `WeaponDefinitions.cs`.
- Add weapon category selection: number keys `1`-`4`, D-pad Up/Right/Down/Left, and repeated-press cycling within each category.
- Add crowbar hit sounds, stronger hit feedback, and animation polish once the player hands/arms model exists.
- Add reload behavior, reserve/clip ammo, and weapon HUD readouts.
- Add explosion damage volumes and route explosion hits through the shared object-health path.
- Add impact damage for launched physics props, so Gravity Gun-thrown crates can damage or break when they hit walls/objects hard enough.
- Add debris spawning from `BreakDebrisModelPaths`, plus folders/search in the model picker once the model library grows.
- Continue material polish from the validated lit/metallic pistol viewmodel with normal-map, emission, environment reflection, and fuller PBR support.
- Add simple damageable targets/enemies so bullet, melee, impact, and explosion damage have more gameplay consequences.
- Add gravity gun polish: hold beam effects, blocked pickup checks, mass-based throw tuning, and sound/VFX hooks.

## Animation Import Direction

Mixamo/Unity FBX animation files should be treated as source animation assets, not runtime assets yet.

- The asset importer now has a dedicated Animations tab that can use Blender to convert animation FBX files to GLB while preserving armatures/actions where Blender supports them, but the runtime renderer currently loads static mesh geometry/material data only.
- To use Mixamo weapon/arms animations in-game, the engine needs glTF skin and animation support: skeleton joints, inverse bind matrices, animation samplers/channels, an animation player, and skinned mesh rendering.
- The recommended pipeline is FBX source -> Blender validation/retargeting -> GLB with mesh, armature, and clips -> engine animation loader/player.
- Mixamo clips should share the same skeleton as the player arms/hands model. If they do not, retarget them in Blender before export.
- Unity `.fbx` animation clips are usable as FBX source files. Unity `.anim` files are Unity-specific and should be exported/converted to FBX or recreated as glTF animation clips before this engine can consume them.
## Model Asset Direction

The weapon definitions point at viewmodel assets:

- `Content/Models/ViewModels/gravitygun.glb`
- `Content/Models/ViewModels/test_pistol.glb`
- `Content/Models/ViewModels/Crowbar.glb`

Player character test asset currently in the same imported-model folder:

- `Content/Models/ViewModels/Future_Soldier_02.glb` - preloaded as the intended local player body source, but not drawn by default because this imported character needs skinned mesh support. The editor/free-camera player marker currently uses the capsule placeholder instead.

World prop test assets currently in the same imported-model folder:

- `Content/Models/ViewModels/Breakable_Wooden_Crate.glb`
- `Content/Models/ViewModels/DamagedCrate02.glb` through `Content/Models/ViewModels/DamagedCrate08.glb`

The game now tries to load those GLB files through `BasicWorldRenderer`. If a weapon file is missing or unsupported, the weapon renders primitive fallback geometry instead. Runtime world entities with a `.glb` `MeshPath` fall back to their primitive physics shape while the model is missing or still loading.

Current asset-import path:

- `Engine.AssetImporter` is a standalone Windows tool in the solution.
- The importer uses Blender to convert a selected source folder containing FBX and textures into a binary `.glb`.
- Converted model files should normally be written to `Game/Content/Models/ViewModels`; converted animation GLBs should normally be written to `Game/Content/Animations`.
- Multi-FBX folders can be converted with the importer batch option, which writes one GLB per FBX and uses material/FBX names to match textures.
- Damaged crate sets should be imported with batch conversion so variants such as `DamagedCrate02` through `DamagedCrate08` become separate GLBs for damage-state spawning.
- The current damaged crate batch was checked after import: all seven damaged crate GLBs contain textured mesh data; `DamagedCrate08` follows its embedded source material name and currently uses the `DamagedCrates1to4` texture set.
- `Game.csproj` copies model assets from `Content/Models` and animation GLBs from `Content/Animations` to the output folder.

Current limitation:

- GLB geometry, indices, node transforms, and material base-color factors are supported.
- Embedded GLB base-color textures are supported by the first-pass textured model renderer.
- Imported normals plus metallic/roughness texture data are used by the first material-lighting pass.
- Normal-map perturbation, emission, environment reflections, skeletal skinning, animation clips, and fuller PBR behavior are still pending.
