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

## Weapon System

Weapon logic has been split out of `HL2GameModule` into `Game.Weapons`.

- `Weapons/WeaponSystem.cs` owns equipped weapon state, switching, cooldowns, firing, ammo consumption, traces, and fallback viewmodel rendering.
- `Weapons/WeaponDefinitions.cs` defines the prototype weapons, their tuning, inventory item ids, fallback viewmodel pieces, and future model asset paths.
- `Weapons/IWeaponHost.cs` defines the world services weapons need without making the weapon system own gameplay state.
- `HL2GameModule.WeaponHost.cs` adapts the game module to the weapon system: input, inventory counts, raycasts, physics impulses, held objects, messages, and primitive drawing hooks.

Current prototype weapons:

- Gravity Gun: grabs dynamic objects at range, keeps the viewmodel visible while holding, and launches held objects with higher force.
- Test Pistol: hitscan weapon that consumes `Bullets` from inventory and applies impulse to dynamic targets.

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

- Replace fallback primitive viewmodels with real model loading once the asset path is ready.
- Add reload behavior, reserve/clip ammo, and weapon HUD readouts.
- Add simple damageable targets/enemies so pistol hits have game consequences.
- Add gravity gun polish: hold beam effects, blocked pickup checks, mass-based throw tuning, and sound/VFX hooks.
