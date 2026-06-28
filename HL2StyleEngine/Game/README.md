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
- Weapon framework with inventory-owned weapons, ammo use, weapon switching, firing cooldowns, traces, and viewmodel fallback geometry.
- First-pass GLB weapon model loading: weapon definitions try their `ModelAssetPath` before falling back to primitive viewmodels.

## Weapon System

Weapon logic has been split out of `HL2GameModule` into `Game.Weapons`.

- `Weapons/WeaponSystem.cs` owns equipped weapon state, switching, cooldowns, firing, ammo consumption, traces, and fallback viewmodel rendering.
- `Weapons/WeaponDefinitions.cs` defines the prototype weapons, their tuning, inventory item ids, fallback viewmodel pieces, and model asset paths.
- `Weapons/IWeaponHost.cs` defines the world services weapons need without making the weapon system own gameplay state.
- `HL2GameModule.WeaponHost.cs` adapts the game module to the weapon system: input, inventory counts, raycasts, physics impulses, held objects, messages, primitive drawing hooks, and cached GLB model drawing.

Current prototype weapons:

- Gravity Gun: pulls dynamic objects from a longer attraction range, slows the pull based on prop mass, locks into held mode once the object reaches grab distance, keeps the viewmodel visible while holding, and launches held objects with higher force.
- Test Pistol: hitscan weapon that consumes `Bullets` from inventory and applies impulse to dynamic targets. The imported sci-fi handgun GLB now renders with base texture, imported normals, and simple metallic/roughness lighting; user playtest confirmed it looks much better than the flat texture-only pass.

Planned next weapon:

- Crowbar: first melee weapon. The current weapon framework has inventory ownership, switching, cooldowns, traces, impulses, and viewmodels ready, but still needs a `Melee` weapon kind, short-range swing hit detection, and crowbar item/loadout data.

Current controls:

- `G` or gamepad right shoulder: cycle owned weapons.
- Left mouse or right trigger: primary fire.
- Right mouse or gamepad left shoulder: secondary/drop held object.
- `E` or gamepad X: interact, hand-pickup, or drop.

## Inventory Integration

Weapons and ammo are normal inventory items in `Inventory/ItemCatalog.cs`.

- `GravityGun` and `TestPistol` are `Weapon` items.
- `Bullets` are `Ammo`.
- Using a weapon item from the inventory equips it.
- Fresh starts, level resets, and old pre-weapon saves receive the prototype weapon loadout.
- Future saves preserve ammo usage and do not refill bullets on load.

## Next Likely Work

- Validate imported viewmodel scale/orientation with `test_pistol.glb` and tune each weapon definition's model offset/scale.
- Continue material polish from the validated lit/metallic pistol viewmodel with normal-map and emission support.
- Add reload behavior, reserve/clip ammo, and weapon HUD readouts.
- Add simple damageable targets/enemies so pistol hits have game consequences.
- Add crowbar melee: short-range swing trace, prop impulse, cooldown, feedback, and primitive fallback viewmodel before importing a crowbar GLB.
- Add gravity gun polish: hold beam effects, blocked pickup checks, mass-based throw tuning, and sound/VFX hooks.

## Model Asset Direction

The weapon definitions point at viewmodel assets:

- `Content/Models/ViewModels/gravitygun.glb`
- `Content/Models/ViewModels/test_pistol.glb`

The game now tries to load those GLB files through `BasicWorldRenderer`. If a file is missing or unsupported, the weapon renders primitive fallback geometry instead.

Current asset-import path:

- `Engine.AssetImporter` is a standalone Windows tool in the solution.
- The importer uses Blender to convert a selected source folder containing FBX and textures into a binary `.glb`.
- Converted files should normally be written to `Game/Content/Models/ViewModels`.
- `Game.csproj` copies model assets from `Content/Models` to the output folder.

Current limitation:

- GLB geometry, indices, node transforms, and material base-color factors are supported.
- Embedded GLB base-color textures are supported by the first-pass textured model renderer.
- Imported normals plus metallic/roughness texture data are used by the first material-lighting pass.
- Normal-map perturbation, emission, environment reflections, and fuller PBR behavior are still pending.
