# Game Implementation Notes

This file tracks completed gameplay-facing systems and the current implementation shape.

## Current Gameplay Project

The playable prototype lives in `Game` and is driven by `HL2GameModule`.

When the game is launched without `--level`, it now looks for the root `HS2Project.json` and loads its `StartupLevel` first. If that cannot be resolved, it falls back to `Content/Levels/interaction_test.json` and regenerates that template from `SimpleLevel.BuildInteractionTestFile` if missing. A hand-authored basement exploration slice lives at `Content/Levels/basementLevel.json` and is currently the project startup level.

A matching primitive/blockout version is generated as `Content/Levels/interaction_test_blockout.json`. The default template comes from `SimpleLevel.BuildInteractionTestBlockoutFile`, and the Debug window's `Load Blockout Practice` button also clones the currently loaded level in memory before stripping mesh/material paths and break-replacement model paths. This keeps the same layout and gameplay entities while rendering as editor-style blocks. The Debug window has a Practice Level Switcher with buttons for `Load Meshed Practice` and `Load Blockout Practice`; switching resets transient prototype state, respawns the player, and keeps the current level path active for F5 reload and F6 reset. In blockout presentation mode, runtime weapon viewmodels stay visible but force their primitive fallback geometry instead of imported GLBs, so the showcase can compare boxed layout/weapons vs imported/meshed presentation.

## Completed Systems

### 2026-10-06 Confirmed Performance Standard

The smoother flashlight result is user-confirmed. Main-scene/shadow batching is the production default, and all game batch launchers plus HS2Editor Play Selected Level now use Release. Visual Studio Debug remains available; choose Release for comparable performance testing. Preserve materials, shadows, collider alignment, movement and ready-before-replacement behavior. Main-view culling and the first visual-streaming milestone are now implemented below; see [ENGINE_TECHNOLOGY_ROADMAP.md](ENGINE_TECHNOLOGY_ROADMAP.md) for the remaining order.

### 2026-10-06 Visibility And Visual Streaming

Main-camera culling uses transformed primitive/model-part bounds, preserving off-camera shadow casters and weapon/UI rendering. Automatic XZ dependency zones prefetch nearby models/replacement assets; budgeted uploads, shared texture ownership, soft byte budgets, retention and fence-safe eviction manage distant resources. Gameplay entities and colliders remain resident, so streaming does not recreate IDs or reset inventory, puzzles, doors, lights or collected items. Held objects and live debris stay pinned. F2 editing uses full residency. Toolbar > Asset Streaming saves settings; existing levels receive defaults without conversion.

Normal-desktop Release walking runs remain at 60 FPS with the flashlight on/off and zero streaming waits. Main draws averaged 368 versus 762; world CPU time was 2.12 versus 3.72 ms with the light on. Combined door/crate/gravity/inventory stress also passed (120 peak fragment bodies), averaging 59.8 FPS with one frame above 33.3 ms. This six-room route retains all 32 shared models; a separate distant-area fixture verifies memory release/reload. See [STREAMING_GUIDE.md](STREAMING_GUIDE.md) and [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md). Full entity/physics streaming, occlusion and cold-start measurements remain future work. Continue testing with the existing Release launchers.

### 2026-10-06 Flashlight-Only Six-Room Test

`LaunchSixRoomFlashlightTest.bat` opens a separate `sixRoomFlashlightTest.json` copy with no scene lights or directional fill. Following the playtest, ambient strength is now 0.025 for faint room visibility. Press F to turn the initially-off flashlight on/off. Ceiling diffuser strips no longer glow; the 13 red/green puzzle indicators remain visible without adding room illumination. Moving shadow maps now batch their object-transform uploads to reduce driver overhead; movement speed and shadow quality are unchanged. Original six-room lighting, launchers, geometry and gameplay are unchanged. See [SIX_ROOM_TEST.md](SIX_ROOM_TEST.md) and [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md) for the variant, measurements and validation details.

Full-game performance follow-up: the main scene now batches object uniforms too, and this flashlight launcher builds/runs Release. A normal desktop walking/turning test averaged 60 FPS with the flashlight on and off, compared with 41.8 FPS using the old Release main-scene upload path. Debug's higher physics cost can compound slow rendering and hit the simulation time clamp; player speed and timestep settings were not changed. Close the old game and relaunch the flashlight batch file to test. See the validation report for precise conditions, image comparisons and remaining playtest limits.

### 2026-10-06 Cooked Model Loading

The next asset milestone adds lossless LZ4 caches for imported model geometry, material values and named parts, referencing the existing shared texture caches. `CookAssets.bat` and Asset Importer > Compression > Cook models and textures create them; game/editor scene loading uses them automatically. All 376 current GLBs are cooked and compared against source-loader output. Source GLBs and authored levels are unchanged and remain authoritative. Missing, stale, corrupt or incompatible caches fall back safely to the original loader.

Three warm-filesystem Release runs reduced median serial CPU preparation of 28 directly referenced six-room models from 286.22 ms to 187.20 ms. A separate harness initialization-to-complete-GPU-readback measurement improved only from 1,577.65 ms to 1,555.51 ms; it is not an OS-launch-to-first-playable benchmark. Model cooking does not add room streaming, cached collision BVHs or skeletal animation. See [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md) for reproduction, regression coverage and remaining work. Restart the usual game/editor launchers to test; no project reset is required.

### 2026-10-06 Light Switches, Flicker And Shadow Reuse

PointLights now expose Light Group and flicker controls. Interaction > Add Light Switch creates a reusable E/controller interaction targeting a group and/or stable light IDs, with optional puzzle prerequisites. Player saves retain enabled-state overrides separately from authored defaults. Internal prefab light links remap on placement/apply; revert preserves IDs of surviving entities. Game and both editor views share the light calculation, with editors previewing authored states. See [LIGHTING_GUIDE.md](LIGHTING_GUIDE.md).

Shadow faces cull irrelevant casters and cache unchanged depth. Moving doors/lights, model swaps and hidden crate parts invalidate affected maps; intensity/flicker changes alone do not. GPU regression images compare the optimized path with uncached/unculled rendering, including six-room viewpoints. Existing level JSON, source models, player saves, inventory and locked weapon HUD layout are untouched. Validation and remaining stages are tracked in [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md).

### 2026-10-06 Compression, Lighting And Physics Foundations

The importer now has a Compression tab and `CookAssets.bat` cooks source GLB textures to shared BC7/BC5 mip chains with LZ4 disk compression. All 376 current GLBs were processed without failures. Cached textures are source-hash/version/checksum validated; missing/corrupt caches fall back to RGBA. Source models and authored levels are unchanged. Game/editor prepare images on workers and budget model-part GPU uploads. Object replacement now queues until the replacement is ready and retains the original on failure. Do not hide/destroy first and hope the next object loads in time.

World rendering now uses HDR, exposure/tone mapping, correct sRGB/linear texture handling and normal maps. F toggles a shadowed gameplay flashlight. Toolbar > Lighting exposes scene exposure/ambient/directional/shadows; PointLight Inspector adds Enabled, Cast Shadows, Spotlight and Cone Angle. Existing fixtures default to unshadowed, with at most two spots and one point light shadowed concurrently. The follow-ups above add groups/switches and visual streaming; baked GI and full entity/physics streaming remain future work. Primitive blockout geometry remains unlit.

Mesh contacts/rays use a per-mesh BVH while retaining original triangle order, collider shapes and the existing C# solver. Tests compare accelerated and linear results. RE:Dox, inventory, weapon UI and gameplay persistence stay in place; no native rewrite was needed.

Measured 56 level textures occupy 73.47 MiB of compressed mip data versus 293.85 MiB RGBA with equivalent mip chains. Warm serial CPU asset preparation had a three-run median of 271.46 ms cooked versus 7,129.09 ms for the new RGBA/mip fallback. This is not an old-engine startup comparison: the old path did not generate those mipmaps, and this test excludes GPU upload/physics/first playable frame. Actual six-room GPU capture loaded 32 model/weapon assets, shared 74 textures (94.8 MiB) and had zero fallback image decodes. See [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md) for all results and limitations.

### 2026-10-06 Inventory Visibility Repair And Engine Review

The missing inventory backgrounds were a native RmlUi styling regression, not a missing import or user setting. RCSS alpha values had used browser CSS's 0..1 range instead of RmlUi's 0..255 range. The inventory backdrop also collapsed to its containing content instead of covering the viewport. Alpha values now use the correct range and the backdrop receives explicit viewport dimensions. Inventory contents, capacities, stacking, overflow and save formats are unchanged.

`validate-inventory-visuals` now inspects actual native render geometry for the backdrop, all 64 cell backgrounds/borders and item tiles at 1300x775, 1920x1080 and 800x600. A live native preview confirmed the restored dark overlay, grids, thumbnails and selected-item description. No configuration reset is required; rebuild/relaunch normally to copy the corrected UI assets.

[ENGINE_TECHNOLOGY_ROADMAP.md](ENGINE_TECHNOLOGY_ROADMAP.md) records the source audit and remaining loading, horror-lighting, physics and C#/C++ direction. The subsequent authorized implementation is documented above; the inventory-only repair itself did not change those engine subsystems.

### 2026-10-05 RE:Dox Integration And Inventory Refresh

The production engine, game, editor and importer now target .NET 10. Levels load through RE:Dox with disposable, source-hash-validated binary DOX caches; levels, prefabs and player saves remain editable JSON and use flushed temporary-file replacement with `.bak` backups. Unversioned saves and existing level/prefab contracts remain readable. Script/undo/project configuration JSON retains its existing implementation.

Inventory now has compact continuous grids and actual item thumbnails in native RmlUi and the ImGui fallback, retaining an 8-by-4 main bag plus a separate 8-by-4 temporary overflow grid. Stacking, splitting, merging and combining preserve quantities. Closing inventory returns overflow to collectible world items near the player; dropped items are also included in saves. Weapons/ammo remain outside inventory and ammo crafting still reports the amount added to reserves. Storage never silently uses temporary overflow.

Root launchers prefer the local .NET 10 SDK; HS2Editor Play/importer launches do likewise. Visual Studio requires compatible .NET 10 tooling separately. No level geometry, world texture pipeline, lighting, weapon HUD or puzzle layouts were changed. Automated contract/container/input checks and native/fallback visual checks are recorded in [PERSISTENCE_AND_INVENTORY.md](PERSISTENCE_AND_INVENTORY.md), including remaining human playtest checks and the distinction between data-read speed and full level loading. [CAPCOM_RE2026_NOTES.md](CAPCOM_RE2026_NOTES.md) records the latest Japan conference research and suggested future priorities.

### 2026-10-02 REDox Save/Load Evaluation

Historical evaluation, superseded by the October 5 integration above: REDox was evaluated from pinned upstream source in an isolated benchmark, without changing production project targets, serializers, levels or player saves at that time. All five levels, the prefab and representative save-state contracts passed typed read/round-trip checks using its System.Text.Json compatibility settings. REDox required .NET 10 while the production engine still targeted .NET 8. The tested adapter was preview and its JSON reader did not accept comments.

For the 758-entity six-room level, warm file-read/deserialization fell from 7.35 ms with the then-current .NET 8 path to 4.52 ms with REDox JSON or 2.00 ms with binary DOX on .NET 10. However, five-run median asset-ready startup was 6.03 s at baseline, 6.32 s with the .NET 10 serializer control, 6.21 s with REDox JSON and 6.31 s with DOX. This did not demonstrate a full-startup improvement. It informed the later decision to retain authoring JSON and validate disposable runtime caches rather than replacing source files with binary data.

See [REDOX_EVALUATION.md](REDOX_EVALUATION.md) for measurements, compatibility limits, save migration/recovery requirements and the proposed integration. Reproducible code and raw results are in `Tools/REDoxBenchmark`.

### 2026-09-08 Inventory Combine Input Fix

The cable recipe was correct, but the menu-to-target input path had gaps. Mouse clicks with both press and release in one game frame were lost. Native menu clicks also used the previous frame's hovered row, and stationary mouse hover could override keyboard/controller selection. The fallback inventory could change the selected source while its action menu was open.

Mouse input now preserves both edges, native action clicks use the freshly hit-tested document, and menu/target selection is protected from stale or inactive pointer input. Selecting Combine preserves the source item until a separate target click or E/controller X confirmation. Quick clicks do not leave an unfinished inventory drag. The fallback action menu also accepts press-edge clicks.

Use either Damaged Cable or Spare Wire, choose Combine, then click the other ingredient. The result is one Repaired Cable. Wrong/self/empty targets and cancellation consume nothing. Existing Scrap + Gunpowder crafting still adds 12 bullets directly to weapon reserves with a notification, not an inventory stack. Level geometry, puzzle requirements, rendering and recipes are unchanged.

`validate-inventory-input` in `Tools/LevelAuthoring` covers native document hit testing, normal/quick clicks, move-and-click menu selection, both recipe orders, keyboard/controller selection, cancellation, ammo crafting and the ImGui fallback. These are automated CPU UI/input tests, not a new manual playthrough. The full solution build and `validate-six-room-test` also pass.

### 2026-09-08 Five-Room Puzzle Pass

`sixRoomTest.json` now gates its five room entrances with rolling shutters: Freight keyed override, Workshop gravity-gun cable retrieval, Medical inventory cable repair, Utility crate pressure plate and Loading Bay three-feed circuit. The original 700 entities remain intact, with 58 additions. There are 27 breakable crates plus an indestructible movable puzzle weight. Required pickups are deterministic, gravity-gun compatible and protected from discard. No enemies were added.

The rolling-door frame and leaf are derived separately from existing `Rollup Door 1.glb`, retaining embedded materials. The static frame uses mesh collision; only the shutter and its fitted collider lift. `PuzzleDoor.LiftHeight` is now configurable (3.35 m here, legacy default 3 m). `PressurePlate` exposes minimum mass and settle time; occupancy is transient, while releasing its gated lever persists and latches the door open. `PuzzleIndicator` displays red/green state using primitive colour. These interaction kinds and fields are editable in the shared Inspector. No renderer, shader, weapon-control or original layout changes were made.

New catalogue items and the `DamagedCable + SpareWire -> RepairedCable` recipe reuse existing inventory combining. Plate sensing is isolated in `Puzzles/PressurePlateSensor.cs`, with runtime integration in `HL2GameModule.Puzzles.cs`. Existing solved-state persistence restores slots, indicators and released shutters, but never persists live plate occupancy.

Full solution build and runtime/layout regression checks passed. Live textures/shutters/panels were inspected; normal Debug stationary capture measured about 45.4 FPS at 1920x1009. A full manual playthrough and puzzle-feel review remain. See `SIX_ROOM_TEST.md` for solutions, editor settings, verification limits and the play-test checklist.

### 2026-09-08 Launch, F2 Panels And Performance

Both Visual Studio profiles in `Properties/launchSettings.json` now pass `--level Game/Content/Levels/sixRoomTest.json`. Set Game as the startup project and use either Game or Game - Font Preview. `LaunchGame.bat` and `LaunchSixRoomTest.bat` target the same level. This does not change `HS2Project.json` or the standalone editor's startup scene.

F2 retains the shared Toolbar, Hierarchy and Inspector, including entity transforms, interactions, scripts and snapping. The saved game layout had the Toolbar and Inspector collapsed; they are expanded on first entry to F2 without discarding saved positions. F2 now has View > Restore Editor Panels for resetting their positions, sizes and docking. View > Debug / Weapon Tools (or F3) opens the existing debug window, weapon tuning and practice-level switcher. That window remains closed at normal game startup. Content Browser, project management, prefab browsing and UI management are standalone HS2Editor panels, not integrated F2 panels. Panel recovery is in `HL2GameModule.EditorPanels.cs`.

The larger scene exposed quadratic interaction-definition searches in both per-frame interaction prompts and fixed-step puzzle-door updates, even with no puzzles present. Runtime entities now bind directly to their authored definitions during each world rebuild; reload and returning from F2 rebuild the bindings. Spawned pickups have no authored definition. Physics also caches the AABB of each immutable world collider and rejects distant dynamic-support candidates before expensive support geometry tests. Changing a collider pose constructs new bounds, so this is not a cache of old world transforms.

At the central-hall spawn, a 1920x1009 isolated Debug capture improved from 226.39 ms/frame (4.4 FPS) to 16.58 ms/frame (about 60 FPS). Physics fell from 206.49 to 7.34 ms/frame; normal update work fell from 13.50 to 1.65 ms/frame. A final capture from the normal Visual Studio Debug output measured 24.74 ms/frame (40.4 FPS), with 10.81 ms physics and 6.39 ms submission/presentation wait. These are bounded stationary captures, not a locked-60 claim or a guarantee for every viewpoint or large debris burst. No level geometry, textures, lighting, collider shapes, simulation rates or crate counts were removed. See `SIX_ROOM_TEST.md` for profiling and regression checks.

Verification: normal Game Debug build passed with zero warnings/errors; 1,000 randomized collider-bound cases plus mesh contact passed; six-room asset/serialization/traversal/settling validation passed; live game textures remained intact. Automated F2 key injection did not register, so the new panel recovery menu still needs a manual UI check. The standalone editor layout was not changed by this fix.

### 2026-09-08 Six-Room Test Level

`Content/Levels/sixRoomTest.json` adds an enclosed 2,536-square-metre layout based on the 504-square-metre `interaction_test.json` floor, approximately 5.03 times its footprint. A large hall connects to freight, workshop, medical, utility and loading rooms. The initial open exploration pass included three default weapons, 26 breakable crates, gravity-gun pickups and supplies; the later five-puzzle pass above adds gated shutters and one breakable cover crate. Root-level `LaunchSixRoomTest.bat` plays it directly; the project startup level remains unchanged. See `SIX_ROOM_TEST.md` for layout and validation.

Explicit `--level` launches now begin at the authored spawn with the default loadout instead of automatically loading another level's saved player position and inventory. Normal launches without `--level` keep existing auto-load behaviour.

Levels can opt into point lighting with `UsePointLights`, exposed as Scene Point Lights in the editor toolbar. The test level enables it; previous levels default to disabled. Textured models use up to 32 nearby lights in game and editor, with colour/intensity/range falloff and inverse-transpose normal handling. The October lighting updates add opt-in shadows, groups, switches and flicker.

### Gameplay Systems

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
- Runtime primitive rendering skips untextured entities whose colour alpha is zero. GLB renderables are not hidden by alpha, so visible imported walls and props cannot disappear because of a colour setting. Collision-only helpers should use primitive box colliders with no `MeshPath`; the in-game editor mode draws selected GLB scene meshes before falling back to primitive debug boxes, and selected objects use the same outline/corner marker style as the standalone editor so textures remain visible while editing.
- Local player character placeholder hook: `Future_Soldier_02.glb` still preloads through the shared GLB model cache, but the imported full-body mesh is disabled by default because it is a skinned character and the runtime currently renders static GLB meshes only. Editor/free-camera inspection now shows the player capsule placeholder until glTF skin/joint/animation support is implemented.
- Configurable object health for box and rigid-body entities, with editor-selected broken replacement models and save/load persistence for broken state. Visual-only props should be converted to rigid bodies if they need health, collision, or weapon hits. Current breakable wooden crates use named GLB fracture parts for staged damage, then collapse remaining pieces into short-lived falling/fading debris instead of swapping to damaged crate models.



## Level Interaction Authoring

Locked doors, locked chests, puzzle slots, and puzzle doors are authored as interaction data on the level entity itself. The editor stores this as the entity's `Interaction` object in the level JSON; there is no separate interaction document to attach.

For a Rusted Key door, select the door/blocker entity in HS2Editor, add `LockedDoor`, set `RequiredItem` to `RustedKey`, and set a stable `StateId` such as `Door_Room1_RustedKey`. A key pickup named like `ItemKey_RustedKey` grants the matching `RustedKey` item id.

Runtime locked-door behavior now treats the selected locked door entity as a swing door. After the key is used, the lock state is saved, the door swings open by 90 degrees away from the player facing direction, and later interactions toggle it open/closed. Locked doors can optionally define `HingeLocalOffset` and `OpenAngleDeg` on the interaction; when set, the door rotates around that author-defined local hinge instead of the old inferred edge. Locked chests still use the older open/hide behavior. `Targets` are mainly for puzzle-slot and puzzle-lever workflows, where the interaction can open or move one or more named `PuzzleDoor` entities.

The editor inspector now only exposes the full target editor for interaction kinds that use it, and provides clear/remove controls for any stale target data.


## Swing Door Runtime

Locked-door entities no longer disappear when unlocked. Runtime registers each `LockedDoor` entity as a swing door, captures its closed pose, and animates toward a signed yaw target based on the player's side of the door when the door is opened. If `HingeLocalOffset` is non-zero, that local point is the real hinge anchor and is scaled by the door transform scale before runtime calculates the pivot. If it is zero, the runtime falls back to the older inferred edge based on the door size.

The existing `OpenedDoors` save list now represents solved/unlocked lock states. When an unlocked door is loaded, it starts open. During swing animation, mesh-collider doors rebuild their mesh collider so collision follows the visible door instead of staying at the closed pose.

Door prefab rule: the doorframe/root remains normal static mesh architecture with no interaction. The child door owns the `LockedDoor` interaction. Placed prefab instances receive unique interaction `StateId` suffixes so multiple copies of one door prefab do not share the same lock state.


## Basement Level Slice

`Content/Levels/basementLevel.json` is a first compact basement/corridor test level built from the imported basement corridor, key, wire/cable, lever, rolling-door, crate, med-kit, and battery models.

The slice tests these linked mechanics:

- Player starts with the prototype weapon loadout through the existing default weapon system.
- A `BasementKey` pickup uses `Key01.glb` and remains a dynamic pickup so the gravity gun can pull it into hold range before collection. Lightweight world pickups get extra damping and settle their angular velocity while supported, which avoids shelf jitter without taking them out of gravity-gun physics.
- `LockedDoor_BasementKey` uses an explicit `HingeLocalOffset` so it swings from the mounted edge instead of rotating around its center. Runtime scales explicit hinge offsets by the door transform scale before calculating the pivot, so scaled doors keep their hinge on the visible edge. The first doorway uses separate left/right/header blocker pieces rather than one fitted door-wall mesh, so the opened door leaves a real walkable gap. A visual-only door-frame prop is layered over those collision pieces for presentation.
- The slice has been widened to a 6m corridor/room width. The rolling-door exit also uses split frame pieces and no longer has a static wall immediately behind it, leaving a walkable final room bay once the door lifts.
- Basement architecture GLBs are kept at neutral white tint in the level data. Darkness should come from the lighting pass, not from permanently multiplying texture colour down in `Color`.
- Modular floors, walls, and most straight wall runs currently use box colliders for stable prototype movement while still rendering their GLB meshes. Any GLB architecture that must be rotated/scaled for correct visual texture orientation but should collide with the actual model shape, such as the basement start/final end walls, should use `Shape = "Mesh"` while staying static. Doorway and rolling-door primitive blockers are collision-only helper pieces with no `MeshPath` and transparent colour alpha so they do not appear as grey blocks over the textured architecture. Dynamic crates/pickups start just above the floor with high friction and zero restitution to avoid idle bouncing.
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

Textured world rendering retains directional fill, ambient light and metallic/roughness factors. Since 2026-09-08, levels with `UsePointLights` enabled also evaluate editor-authored point lights. The new `sixRoomTest.json` enables this path. Non-uniform model scale now uses an inverse-transpose normal matrix. Legacy levels keep point lights disabled until enabled in the toolbar, so differently facing surfaces can still vary under the original directional fill.

Target lighting model:

- Keep a small global ambient term so textured surfaces never crush to black in normal indoor scenes.
- Keep an optional directional fill/sun light for broad readability, but do not depend on it for indoor rooms.
- Implemented: point-light position, colour, intensity, range, falloff, named groups, authored flicker and persistent enabled overrides. Intensity zero disables an individual light.
- Implemented: inverse-transpose normal handling for scaled/rotated models.
- Implemented: spot lights, a gameplay flashlight and budgeted point/spot shadows with per-face culling and caching.

Gameplay/light-switch direction:

- Light fixture models are just visible props/meshes. They do not automatically cast light unless paired with one or more light entities.
- Implemented: `LightSwitch` interaction data on the switch object, targeting a named light group or explicit light IDs. It uses the shared gameplay interaction binding and supports optional puzzle prerequisites.
- Implemented: authored starts-on state, reusable toggles, prompt text and saved changed light states. One-shot switches and sound/VFX hooks remain future additions.
- Switches are authored through the Interaction inspector, not a separate script document. Level saves retain defaults; player saves retain runtime overrides.

Next lighting priorities: baked indirect light/probes, emission, light gizmos and representative moving/debris performance profiles. See `LIGHTING_GUIDE.md` for current controls and limitations.


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
- `E` or gamepad X: interact, hand-pickup, collect a held world item, or drop.

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
- Fracture debris is drawn per named GLB part around that part's own local center, rather than around the original crate center. Landing support is resolved per fragment from colliders underneath it while excluding the crate that just broke, so debris should no longer appear to hang on the removed source collider. Debris also carries the source crate tint so broken pieces keep the same brownish presentation instead of shifting toward grey.
- Broken crates now have a standard 1-in-5 chance to spawn an immediate-use pickup. The drop chance rises when player health is below 25 and also gets a smaller suit-low bonus when suit charge is below 25. Reward type is weighted from current player need: low health strongly favors `HealthPack`, low suit favors `SuitBattery`. `HealthPack` restores 25 health and uses `FirstAidKit01.glb`; `SuitBattery` restores 15 suit charge and uses `Battery07.glb`. If the relevant stat is already full, the pickup is not consumed.
- Placed loose world pickups can also resolve from their model/name: `FirstAidKit*` assets collect as `HealthPack`, and `Battery07`/battery-named assets collect as `SuitBattery`. If a gravity-gun-held object resolves as a world item, pressing interact collects it instead of dropping it. These pickups should stay dynamic when they need gravity-gun pull/hold behavior.
- The meshed and blockout practice level templates now include three extra `Crate_MiddleCorridor_*` test crates in the middle corridor for concentrated fracture testing.
- Debris model lists are stored now for future use. Once debris assets and spawning are in, the broken object should replace the original and optionally spawn selected debris models.
- Previous visual bug: when crates broke and swapped to a damaged replacement model, the current/intact model could flash white before the new model appeared.
- Replacement/warm-up rule: any future system that swaps one runtime object for another should prepare the incoming object first, then replace or hide the outgoing object only after the replacement is ready. If the replacement cannot be prepared, keep the original visible or fail gracefully instead of briefly rendering an incorrect fallback.
- Current crate zero-health behavior avoids the damaged-model swap entirely. The general replacement/warm-up rule still applies to future systems that do replace one object with another.
- Longer-term crate destruction can move from visual-only falling GLB parts to real debris-piece spawning once debris assets exist, with dust/splinter particles added for impact cover and feel.
- Planned damage sources should call the same object-health entry point and pass a hit point when available so named-part fracture visuals can update. Explosions and future enemy/environment impacts should use the same path. Crowbar melee currently damages objects; hit sounds are still pending.

## HS2Editor First Pass

The standalone editor app now exists under `HS2Editor`. The full target and roadmap are documented in `Engine.Editor/README.md`.

Implemented direction: separate executable, existing renderer plus ImGui, project/content browser, level manager, 3D viewport, hierarchy, inspector, model assignment, direct GLB drag/drop placement into the Scene as static rigid bodies with box colliders, GLB MeshPath drops onto selected boxes/props/rigid bodies, textured editor Scene GLB drawing for placement previews, non-destructive outline selection, first-pass `V` corner snapping, collider/blockout overlay for tuning the physical volume against the visible mesh, stable Content Browser drag tracking, prefab JSONs, registered script attachment through the existing inspector path, basic UI file management, asset importer launch, and game launch from the selected level.

The root `HS2Project.json` stores the project name, content root, startup level, recent levels, Blender path placeholder, asset importer project path, game project path, and preferences. Root launchers are available for `LaunchEditor.bat`, `LaunchAssetImporter.bat`, and `LaunchGame.bat`. `LaunchGame.bat` now explicitly opens `sixRoomTest.json`, as does `LaunchSixRoomTest.bat`; the editor's configured startup level remains `basementLevel.json`.

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
- Continue the implemented HDR/normal-map/shadow/switch lighting with baked indirect light, emission, environment reflection and fuller PBR support. Keep measured shadow budgets and editor/runtime parity.
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



