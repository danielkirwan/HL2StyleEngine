# Game Implementation Notes

This file tracks completed gameplay-facing systems and the current implementation shape.

## Current Gameplay Project

The playable prototype lives in `Game` and is driven by `HL2GameModule`.

The current default practice level is loaded from `Content/Levels/interaction_test.json` at runtime. Its display name is `Interaction Test`. If the level is missing, the game regenerates it from `SimpleLevel.BuildInteractionTestFile`. A hand-authored basement exploration slice now lives at `Content/Levels/basementLevel.json` and can be launched from HS2Editor or with the game `--level` argument.

A matching primitive/blockout version is generated as `Content/Levels/interaction_test_blockout.json`. The default template comes from `SimpleLevel.BuildInteractionTestBlockoutFile`, and the Debug window's `Load Blockout Practice` button also clones the currently loaded level in memory before stripping mesh/material paths and break-replacement model paths. This keeps the same layout and gameplay entities while rendering as editor-style blocks. The Debug window has a Practice Level Switcher with buttons for `Load Meshed Practice` and `Load Blockout Practice`; switching resets transient prototype state, respawns the player, and keeps the current level path active for F5 reload and F6 reset. In blockout presentation mode, runtime weapon viewmodels stay visible but force their primitive fallback geometry instead of imported GLBs, so the showcase can compare boxed layout/weapons vs imported/meshed presentation.

## Completed Systems

- Source-style first-person movement and camera.
- Runtime/editor level loading through `LevelEditorController`.
- Inventory, storage box, item collection, item use, item combining, stack splitting, discarding, and save/load persistence.
- RmlUi-backed gameplay UI path with ImGui gameplay overlay support. Health/suit, ammo, fallback crosshair, weapon selector, and loading overlay currently render through ImGui for stability; RmlUi remains the generated-document path for inventory, storage, prompts, pickup, and save/load panels.
- Typewriter-style save slots using ink ribbons.
- Locked doors/chests, puzzle slots, puzzle levers, puzzle doors, persistent solved/unlocked state, swing-open door toggling, and item expiration when locks are complete.
- Physics pickup/drop/throw for dynamic rigid bodies.
- Weapon framework with weapon-system-owned loadout, magazine/reserve ammo state, category selection, firing cooldowns, traces, melee swings, and viewmodel fallback geometry.
- First-pass GLB weapon model loading: weapon definitions try their `ModelAssetPath` before falling back to primitive viewmodels. Gravity Gun, pistol, and crowbar viewmodels are placed on the right-hand side of the screen for the current camera-mounted weapon pass; viewmodel placement maps positive local X to screen-right while rotation keeps a proper camera basis to avoid weapon orbiting when turning. The Debug window includes live viewmodel tuning sliders for model offset, model euler rotation, model scale, and muzzle offset, plus a copy-to-clipboard C# snippet for locking tuned values into `WeaponDefinitions.cs`.
- First-pass GLB world prop rendering: rigid bodies can point `MeshPath` at an imported `.glb`, and the renderer fits imported bounds to the entity size. GLB node/mesh names are preserved on loaded model parts, and world models can skip named parts at draw time. Static rigid bodies can now use `Shape = "Mesh"` to generate a triangle mesh collider from the same fitted GLB transform used for rendering, so doorframes and architectural models can collide through their real openings instead of a solid box. `Prop` entities are visual-only at runtime; use `RigidBody` when a model should block movement, traces, or physics. All current throwable `Crate_*` props use the imported breakable wooden crate model and still use primitive physics while alive.
- Local player character placeholder hook: `Future_Soldier_02.glb` still preloads through the shared GLB model cache, but the imported full-body mesh is disabled by default because it is a skinned character and the runtime currently renders static GLB meshes only. Editor/free-camera inspection now shows the player capsule placeholder until glTF skin/joint/animation support is implemented.
- Configurable object health for box and rigid-body entities, with editor-selected broken replacement models and save/load persistence for broken state. Visual-only props should be converted to rigid bodies if they need health, collision, or weapon hits. Current breakable wooden crates use named GLB fracture parts for staged damage, then collapse remaining pieces into short-lived falling/fading debris instead of swapping to damaged crate models.



## Level Interaction Authoring

Locked doors, locked chests, puzzle slots, and puzzle doors are authored as interaction data on the level entity itself. The editor stores this as the entity's `Interaction` object in the level JSON; there is no separate interaction document to attach.

For a Rusted Key door, select the door/blocker entity in HS2Editor, add `LockedDoor`, set `RequiredItem` to `RustedKey`, and set a stable `StateId` such as `Door_Room1_RustedKey`. A key pickup named like `ItemKey_RustedKey` grants the matching `RustedKey` item id.

Runtime locked-door behavior now treats the selected locked door entity as a swing door. After the key is used, the lock state is saved, the door swings open by 90 degrees away from the player facing direction, and later interactions toggle it open/closed. Locked doors can optionally define `HingeLocalOffset` and `OpenAngleDeg` on the interaction; when set, the door rotates around that author-defined local hinge instead of the old inferred edge. Locked chests still use the older open/hide behavior. `Targets` are mainly for puzzle-slot and puzzle-lever workflows, where the interaction can open or move one or more named `PuzzleDoor` entities.

The editor inspector now only exposes the full target editor for interaction kinds that use it, and provides clear/remove controls for any stale target data.


## Swing Door Runtime

Locked-door entities no longer disappear when unlocked. Runtime registers each `LockedDoor` entity as a swing door, captures its closed pose, and animates toward a signed yaw target based on the player's facing direction when the door is opened. If `HingeLocalOffset` is non-zero, that local point is the real hinge anchor. If it is zero, the runtime falls back to the older inferred edge based on the door size.

The existing `OpenedDoors` save list now represents solved/unlocked lock states. When an unlocked door is loaded, it starts open. During swing animation, mesh-collider doors rebuild their mesh collider so collision follows the visible door instead of staying at the closed pose.

Door prefab rule: the doorframe/root remains normal static mesh architecture with no interaction. The child door owns the `LockedDoor` interaction. Placed prefab instances receive unique interaction `StateId` suffixes so multiple copies of one door prefab do not share the same lock state.


## Basement Level Slice

`Content/Levels/basementLevel.json` is a first compact basement/corridor test level built from the imported basement corridor, key, wire/cable, lever, rolling-door, crate, med-kit, and battery models.

The slice tests these linked mechanics:

- Player starts with the prototype weapon loadout through the existing default weapon system.
- A `BasementKey` pickup uses `Key01.glb` and is placed on a high shelf so the gravity gun can pull it down before collection.
- `LockedDoor_BasementKey` uses an explicit `HingeLocalOffset` so it swings from the mounted edge instead of rotating around its center. The first doorway uses separate left/right/header blocker pieces rather than one fitted door-wall mesh, so the opened door leaves a real walkable gap. A visual-only door-frame prop is layered over those collision pieces for presentation.
- The slice has been widened to a 6m corridor/room width. The rolling-door exit also uses split frame pieces and no longer has a static wall immediately behind it, leaving a walkable final room bay once the door lifts.
- Basement architecture GLBs are kept at neutral white tint in the level data. Darkness should come from the lighting pass, not from permanently multiplying texture colour down in `Color`.
- Modular floors, walls, and ceilings currently use box colliders for stable prototype movement while still rendering their GLB meshes. Dynamic crates/pickups start just above the floor with high friction and zero restitution to avoid idle bouncing.
- Three dynamic wire pickups, `WireA`, `WireB`, and `WireC`, can be pulled with the gravity gun, collected, and used on three `PuzzleSlot_*` circuit sockets.
- Each wire slot consumes its matching wire item and raises a hidden installed-wire model into place as feedback.
- `PuzzleLever_BasementCircuit` uses `RequiredStates` for the three wire slots. It only opens `PuzzleDoor_RollingDoorExit` after the circuit is complete.
- Several breakable/throwable crate rigid bodies are included for crowbar, pistol, gravity-gun blast, pickup, and throw testing.
## Mesh Collider Implementation

Static imported level geometry now has a first-pass triangle mesh collider path.

- `RuntimeShapeKind.Mesh` and `WorldColliderShape.Mesh` carry a baked `MeshCollisionMesh` made from GLB triangles. Mesh colliders now apply a small collision skin so thin wall and doorframe triangles have enough physical thickness for player movement and ray/contact resolution.
- Runtime mesh colliders are generated for static `RigidBody` entities with `Shape = "Mesh"` and a `.glb` `MeshPath`.
- The collision baker uses the same `CreateBoundsFitTransform` path as GLB world rendering, so position, rotation, scale, and model bounds match the visible mesh.
- Player movement, dynamic box/sphere/capsule collision, weapon traces, interact traces, and gravity-gun targeting can resolve against mesh colliders.
- Mesh colliders are static-only for now. Dynamic/kinematic mesh-shaped rigid bodies fall back to box physics behavior; movable objects should continue to use primitive colliders until convex/dynamic mesh support exists.
- Doorframe workflow: make the frame a static mesh-collider rigid body, then add the actual door as a separate movable primitive-collider entity. The frame opening is walkable because the collider follows the frame mesh instead of its bounding box, while the frame itself gets mesh-triangle collision plus the collision skin.

## Lighting Direction

Current textured world rendering is still using a simple first-pass lighting model: a hard-coded directional light, low ambient light, imported mesh normals, and metallic/roughness factors. This is why identical wall models can look very different depending on rotation and placement: surfaces facing the fixed light are bright, while surfaces facing away can fall almost completely to ambient. Non-uniform model scale can make this worse until normals are transformed through a proper normal matrix.

Target lighting model:

- Keep a small global ambient term so textured surfaces never crush to black in normal indoor scenes.
- Keep an optional directional fill/sun light for broad readability, but do not depend on it for indoor rooms.
- Add runtime `PointLight` support from level data: position, colour, intensity, range, falloff, enabled state, and optional group name.
- Submit nearby active point lights to `BasicWorldRenderer` each frame and evaluate them in the textured model shader.
- Fix normal handling for scaled/rotated models by using a proper normal matrix or equivalent CPU/GPU normal transform path.
- Later add spot lights for flashlights, wall lamps, and directional fixtures once point lights are stable.

Gameplay/light-switch direction:

- Light fixture models are just visible props/meshes. They do not automatically cast light unless paired with one or more light entities.
- Light switches should be interactable entities/scripts that toggle one or more light entities by group name or explicit entity ID.
- Switches should support starts-on state, reusable toggle vs one-shot behavior, prompt text, optional sound/VFX hooks, and save/load persistence for changed light states.
- Door/chest/puzzle interaction patterns should be reused where possible: author the switch in the editor, attach a registered script/component, expose editable fields, and let runtime state persistence record the result.

First implementation should prioritize: ambient/fill correction, point lights in renderer, editor-authored light groups, one switch script that toggles a group, and save/load of changed light enabled states.


## Debug UI

The ImGui Debug window now starts closed during normal game launch. Press `F3` to toggle it when runtime diagnostics, level switching, or viewmodel tuning are needed.
## Gameplay UI Rendering

The combat HUD path is intentionally split while native RmlUi rendering is still being validated.

- Health/suit, ammo, fallback crosshair, weapon selector, and loading overlay are currently forced through the stable ImGui gameplay preview renderer, even when native RmlUi presentation is enabled.
- Inventory, storage, pickup, save/load, prompt, and generated RML document paths still exist in the RmlUi workflow.
- The Rml weapon-selector fallback is kept aligned, but the active gameplay selector should be treated as the ImGui version until native RmlUi text/layout rendering is reliable.
- The current weapon selector is text-only: no category headers, no icons yet, just weapon names inside translucent yellow Half-Life 2-style rectangles around the crosshair.

## Applied Weapon UI Fixes

The weapon UI polish pass is closed again after the following fixes:

- Ammo HUD initialization now loads an empty clip from reserve when an ammo weapon is equipped or already active. This fixes the observed pistol display where the clip showed `0` and reserve showed `8` until the first shot moved the values to clip `7` and reserve `0`.
- Health/suit and ammo HUD panels now use the same translucent dark yellow/black background fill and yellow border treatment as the weapon-switching rectangles. Panel borders are drawn one pixel inside their ImGui windows so top/left edges are not clipped.

## Weapon System

Weapon logic has been split out of `HL2GameModule` into `Game.Weapons`.

- `Weapons/WeaponSystem.cs` owns the weapon loadout, equipped weapon state, category selection, magazine/reserve ammo, cooldowns, firing, ammo consumption, traces, center-screen melee hits, alternating melee swing variants, and fallback viewmodel rendering.
- `Weapons/WeaponDefinitions.cs` defines the prototype weapons, their tuning, inventory item ids, fallback viewmodel pieces, and model asset paths.
- `Weapons/IWeaponHost.cs` defines the world services weapons need without making the weapon system own gameplay state.
- `HL2GameModule.WeaponHost.cs` adapts the game module to the weapon system: category input, primary/secondary input, raycasts, physics impulses, weapon damage, held objects, messages, primitive drawing hooks, and cached GLB model drawing.
- Weapon viewmodels are runtime camera-mounted objects, not level entities, so they do not appear in the editor hierarchy. Use the Debug window's Weapon Viewmodel Tuning section while the weapon is equipped to adjust placement and copy C# values back into `WeaponDefinitions.cs`.

Current prototype weapons:

- Gravity Gun: pulls dynamic objects from a longer attraction range, slows the pull based on prop mass, locks into held mode once the object reaches grab distance, keeps the viewmodel visible while holding, launches held objects with higher force, can secondary-fire a short electric blast that punts nearby physics objects and applies breakable-object damage, and waits for primary fire release after hand-thrown props so it does not immediately re-catch them.
- Test Pistol: hitscan weapon that consumes weapon-system `Bullets` ammo, tracks current magazine and reserve ammo for the HUD, applies impulse to dynamic targets, and routes bullet damage into the shared object-health path. The imported sci-fi handgun GLB now renders with base texture, imported normals, and simple metallic/roughness lighting; user playtest confirmed it looks much better than the flat texture-only pass.
- Crowbar: first melee weapon. `Crowbar.glb` is available in the default prototype loadout, uses a short center-screen melee trace, applies impulse and 35 melee damage through the shared object-health path, and plays a first-pass right-hand viewmodel swing animation.

Current crowbar viewmodel notes:

- The crowbar is a runtime camera-mounted viewmodel, not a level entity, so it is tuned through the Debug window's Weapon Viewmodel Tuning section.
- The static held orientation is locked in `WeaponDefinitions.cs`: model offset `(0.62, -0.42, 0.82)`, model euler `(0, 13, 0)`, model scale `1.15`, and muzzle offset `(0.62, -0.20, 1.02)`.
- Hit detection stays on the center-screen camera ray/crosshair. The white debug line is the melee trace, not the visual crowbar path.
- The visual swing is separate from the damage trace. It cycles through three right-hand arcs, moving forward in local `+Z` and left in local `-X` toward the crosshair before recovering.
- The current pass is tuned to feel closer to the Half-Life 2 crowbar reference: a fast forward strike, slight variation between hits, and a slower return to the held pose.

Current controls:

- `1` or D-pad Up: small weapons. Current weapon: Test Pistol.
- `2` or D-pad Right: medium weapons. Empty until SMGs, shotguns, or rifles are added.
- `3` or D-pad Down: Crowbar and Gravity Gun. Repeated presses swap immediately between owned weapons in this category.
- `4` or D-pad Left: throwables and heavy weapons. Empty until grenades, launchers, or heavy weapons are added.
- Left mouse or right trigger: primary fire, including crowbar swing when the crowbar is equipped.
- Right mouse or gamepad left shoulder: secondary action. With the Gravity Gun and no held object, this fires a short electric blast that punts a nearby physics object; while holding an object, it drops it.
- `E` or gamepad X: interact, hand-pickup, or drop.

Selection behavior:

- Category selection is immediate; there is no confirmation delay.
- Only owned weapons appear in the selector overlay.
- The selector is centered around the crosshair in four category positions and uses Half-Life 2-style translucent yellow rectangles.
- Current selector entries are text-only weapon-name blocks until real weapon icon artwork is added; category header text is intentionally hidden in the gameplay overlay.
- If an owned ammo weapon has zero magazine and reserve ammo, its selector entry is shown in red.
- Empty categories do nothing and do not show placeholder weapons.
- The old `G` / right-shoulder quick-cycle binding has been removed.

## Inventory Integration

Weapons and ammo are now owned by the weapon system, not by the visible inventory grid.

- `GravityGun`, `TestPistol`, `Crowbar`, and `Bullets` still exist as item ids in `Inventory/ItemCatalog.cs` so pickups, recipes, save migration, and debug spawning can identify them.
- Weapon and ammo item ids are migrated out of inventory/storage containers and granted to `WeaponSystem` instead of being shown in the item case.
- Combining `Scrap` with `Gunpowder` creates `Bullets`, but the result is added directly to weapon reserve ammo and shown as a short notification rather than an inventory stack.
- Saves now persist `WeaponStates` separately from inventory, including owned weapons, current magazine ammo, and reserve ammo.
- Fresh starts, level resets, and old saves still receive or migrate the prototype weapon loadout.

## Object Health and Breakable Props

Object health is now data-driven enough for the first crate-damage pass.

- `LevelEntityDef` stores `Damageable`, `MaxHealth`, `BreakReplacementModelPaths`, `BreakReplacementKeepsPhysics`, and `BreakDebrisModelPaths`.
- Runtime `Entity` mirrors health and broken state. Save data records broken objects by entity name, with an optional replacement model path for systems that still swap models; current wooden crates save an empty replacement path and stay removed after load.
- The level editor inspector exposes damage settings for box and rigid-body entities; visual-only props should not be used for damageable gameplay objects. The model picker currently shows all `.glb` files under `Content/Models`; the standalone editor content browser can preview selected GLBs and drag them into replacement/debris model lists. This should move toward folders/search as the model library grows.
- Current crate behavior is staged visual damage -> fracture collapse. `Breakable_Wooden_Crate.glb` contains an intact `Cube_Cube_001` mesh plus `Voronoi_Fracture` child meshes named `Cube_Cube_001_fracturepart*`. The runtime hides fracture parts while the crate is undamaged. On bullet, crowbar, Gravity Gun pulse, or Gravity Gun blast damage, the hit point activates the fracture view, hides the intact mesh, and hides the nearest visible fracture part by GLB node name. The crate still uses one box collider/rigid body while alive. Crates use 100 health with crate-specific tuning: crowbar melee deals 25 damage, so 4 hits destroy; bullet damage deals 34 damage, so 3 pistol rounds destroy; future shotgun/explosion damage kinds can break instantly. At zero health the original crate is hidden, no damaged crate model is swapped in, and the remaining visible fracture parts become transient debris with simple gravity, light drift/spin, a 3-second lifetime, and a fade-out before removal.
- Fracture debris is drawn per named GLB part around that part's own local center, rather than around the original crate center. Landing support is resolved per fragment from colliders underneath it while excluding the crate that just broke, so debris should no longer appear to hang on the removed source collider.
- Broken crates now have a standard 1-in-5 chance to spawn an immediate-use pickup. The drop chance rises when player health is below 25 and also gets a smaller suit-low bonus when suit charge is below 25. Reward type is weighted from current player need: low health strongly favors `HealthPack`, low suit favors `SuitBattery`. `HealthPack` restores 25 health and uses `FirstAidKit01.glb`; `SuitBattery` restores 15 suit charge and uses `Battery07.glb`. If the relevant stat is already full, the pickup is not consumed.
- The meshed and blockout practice level templates now include three extra `Crate_MiddleCorridor_*` test crates in the middle corridor for concentrated fracture testing.
- Debris model lists are stored now for future use. Once debris assets and spawning are in, the broken object should replace the original and optionally spawn selected debris models.
- Previous visual bug: when crates broke and swapped to a damaged replacement model, the current/intact model could flash white before the new model appeared.
- Replacement/warm-up rule: any future system that swaps one runtime object for another should prepare the incoming object first, then replace or hide the outgoing object only after the replacement is ready. If the replacement cannot be prepared, keep the original visible or fail gracefully instead of briefly rendering an incorrect fallback.
- Current crate zero-health behavior avoids the damaged-model swap entirely. The general replacement/warm-up rule still applies to future systems that do replace one object with another.
- Longer-term crate destruction can move from visual-only falling GLB parts to real debris-piece spawning once debris assets exist, with dust/splinter particles added for impact cover and feel.
- Planned damage sources should call the same object-health entry point and pass a hit point when available so named-part fracture visuals can update. Explosions and future enemy/environment impacts should use the same path. Crowbar melee currently damages objects; hit sounds are still pending.

## HS2Editor First Pass

The standalone editor app now exists under `HS2Editor`. The full target and roadmap are documented in `Engine.Editor/README.md`.

Implemented direction: separate executable, existing renderer plus ImGui, project/content browser, level manager, 3D viewport, hierarchy, inspector, model assignment, direct GLB drag/drop placement into the Scene as static rigid bodies with box colliders, GLB MeshPath drops onto selected boxes/props/rigid bodies, editor Scene GLB drawing through a colour-only no-texture render model for stable placement previews, collider/blockout overlay for tuning the physical volume against the visible mesh, stable Content Browser drag tracking, prefab JSONs, registered script attachment through the existing inspector path, basic UI file management, asset importer launch, and game launch from the selected level.

The root `HS2Project.json` stores the project name, content root, startup level, recent levels, Blender path placeholder, asset importer project path, game project path, and preferences. Root launchers are available for `LaunchEditor.bat`, `LaunchAssetImporter.bat`, and `LaunchGame.bat`.

The first pass still uses path-based asset references because the current level format already does. GUID/meta asset identity should be added later when content browser rename/move support and prefab references need stable asset ids.

## Next Likely Work

- Grow the standalone `HS2Editor` app beyond the first pass: richer object creation palettes, visual UI preview, lighting placement/preview tools, GUID/meta asset ids, and embedded play mode.
- Split or retarget the player character into first-person hands/arms so weapon placement can move from camera-mounted offsets to right-hand/arm placement and animation.
- Use the Debug window viewmodel tuning sliders to validate imported viewmodel scale/orientation with `test_pistol.glb`, `gravitygun.glb`, and `Crowbar.glb`, then copy good values back into `WeaponDefinitions.cs`.
- Tune the selector once more weapons exist in each category and replace text-only blocks with proper weapon icon artwork.
- Add crowbar hit sounds, stronger hit feedback, and animation polish once the player hands/arms model exists.
- Add manual reload behavior and reload feedback.
- Add explosion damage volumes and route explosion hits through the shared object-health path.
- Add impact damage for launched physics props, so Gravity Gun-thrown crates can damage or break when they hit walls/objects hard enough.
- Add debris spawning from `BreakDebrisModelPaths`, plus folders/search in the model picker once the model library grows.
- Add crate break VFX such as dust/splinters to support the fracture collapse and make impacts feel better.
- Add renderer lighting polish: stronger ambient/fill, proper normal-matrix handling for scaled models, point lights from level data, light groups, and switch-controlled light state. Continue material polish from the validated lit/metallic pistol viewmodel with normal-map, emission, environment reflection, and fuller PBR support.
- Add simple damageable targets/enemies so bullet, melee, impact, and explosion damage have more gameplay consequences.
- Add gravity gun polish: hold beam effects, blocked pickup checks, mass-based throw tuning, and sound/VFX hooks.
- Replace placeholder health/suit values with a real player damage and armor system.

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




## Runtime Prefab And Parent Transform Notes

Level entities now carry prefab metadata for editor-created instances: prefab asset path, prefab instance id, and prefab source entity id. Runtime gameplay ignores the editor metadata but can load levels containing prefab instances.

When the game rebuilds runtime entities from a level, parent chains are baked into each entity's world transform. This makes grouped prefab content, such as a doorframe with a child door, appear in-game where it appears in the editor. Static GLB mesh colliders are also built from the baked world transform so parented architecture collides in the right place.

This is a load-time bake. If a future system moves a parent object at runtime, child inheritance during play will need a live parent-transform update pass.

See `Game/LEVEL_DESIGN_GUIDE.md` for horror, Bioshock-like, and Half-Life 2-like level pacing and encounter guidance.
