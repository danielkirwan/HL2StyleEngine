# Project Overview

Last updated: 2026-05-01

## Purpose

This project is a custom C# game engine and game prototype built to capture the feel of a Half-Life 2 style experience.

The goal is not to copy Source exactly. The goal is to build a modern, understandable codebase that can deliver:

- grounded first-person movement
- believable physics interaction and prop behavior
- readable, purposeful level spaces
- an art direction that leans toward industrial, physical, Source-era realism
- clear separation between reusable engine systems and game-specific behavior

## Game Target

The current game target for this engine is a grounded horror game built on top of the Half-Life 2 inspired interaction and movement foundation.

The intended direction combines:

- Half-Life 2 style physical interaction and object handling
- horror lighting and atmosphere
- Resident Evil inspired structure for maps, inventory, and save-point style progression

The recommended scope is still to reach that through a vertical slice first, not by building every survival-horror system at once.

## What The Project Is Trying To Achieve

The north star is a playable game and engine foundation where movement, collision, props, and world interaction feel physical and authored rather than floaty or arcade-like.

The main target qualities are:

- strong movement feel inspired by Half-Life 2 and Source-era FPS games
- physics props that look convincing when pushed, thrown, dropped, or stacked
- a fast iteration workflow with clear engine/game separation
- tooling that can grow into a practical in-engine editor and content workflow
- visuals and world building that support the same grounded tone as the gameplay

## Editor Direction

The project now has two editor goals, not one:

- a proper standalone level editor that is practical for building levels from scratch
- an in-scene runtime editor that stays available for playtest-time adjustment and rapid feel tuning

The standalone editor should become the main environment for layout, object placement, and level construction. The in-scene editor should remain as a fast iteration tool for checking scale, gameplay flow, physics feel, and small live adjustments while the game is running.

## Current Stage

The project is in a foundation-plus-prototyping stage.

Core engine structure is in place, the runtime and editor exist, and the project is now spending more time on feel-critical systems such as movement, collision, dynamic props, and debug tooling. The current work is moving from broad placeholder behavior toward more faithful physical behavior.

## Solution Layout

- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Core`
  Shared math, timing, and low-level utilities.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Platform`
  Windowing and platform-facing services.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Input`
  Input systems and input-related abstractions.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Render`
  Rendering systems and graphics integration.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Runtime`
  Runtime host, main loop, and module lifecycle.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics`
  Collision, stepping, and simple physics bodies.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Editor`
  Editor systems and debug/editor-facing tooling.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game`
  Game-specific logic, gameplay state, and current prototype behavior.

## Where The Project Is Right Now

Implemented or substantially present:

- custom engine structure in C# on .NET 8
- separate engine and game projects
- runtime loop and editor/runtime split
- rendering and debug UI foundations
- first-person movement foundations
- fixed-step simulation
- basic world collision and simple dynamic body stepping
- dynamic prop runtime state for visual rotation and angular settling work
- debug collider display for box, sphere, and capsule shapes

Current active gameplay focus:

- harden the small Resident Evil-inspired key, locked-door, locked-chest, and save-point interaction loop
- keep the approved context interaction scheme: `E` / controller `X` uses nearby gameplay objects first, then falls back to physics pickup
- use a separate interaction test level so gameplay work can progress without disturbing the physics test room
- keep dynamic prop behavior at good-enough prototype quality while higher-level game flow is built around it
- return to stack and pile stability later when the interaction loop has more surrounding gameplay context

## Active Workstream

The main active workstream is now hardening the first horror interaction loop.

The current implementation state is:

- a separate `interaction_test.json` runtime level
- key items that can be collected into a simple inventory
- locked doors that check the inventory before opening
- locked chest-style test objects that use the same reusable lock/item rule
- typewriter save points that require ink ribbons
- prototype save persistence for collected keys, opened doors, ink count, save count, and player position
- non-ink key/item expiry when every matching locked door or chest has been opened
- first-pass item data model with item definitions, item types, slot footprints, stack limits, counts, and saveable inventory stacks
- first-pass pickup/examine overlay for collected items
- locked chests can now grant prototype reward items from the item catalog
- the temporary inventory overlay now shows grid slots, stack counts, item footprints, and selected-item descriptions
- opening inventory pauses gameplay and allows mouse hover or controller/keyboard navigation over the inventory grid
- inventory item stacks now have stable grid slot indices and save/load those positions
- inventory items can now be picked up inside the inventory screen, moved to another grid slot, validated against their slot footprint, placed, or cancelled
- multi-slot inventory items now visibly occupy their covered grid slots in the UI preview, and occupied items can swap positions when their footprints fit after the exchange
- a developer reset hotkey for clean interaction-test runs
- a small multi-room key route that is playable and starts shaping the horror vertical slice
- controller parity for new player-facing actions

The intended inventory direction is Resident Evil Requiem-inspired:

- item pickup can show a focused item-collected/examine screen with the world blurred behind it
- the inventory should become a grid-based case rather than a plain list
- every inventory item should define a slot footprint, stack limit, display name, description, and item type
- stackable items can occupy one footprint while holding a count up to their stack limit
- keys and puzzle objects are reusable until their authored uses are exhausted, then they are removed from inventory with feedback
- ink ribbons remain consumable save resources and are not part of the reusable-key expiry rule
- locked chests should later reveal items such as puzzle objects, upgrade materials, or other pickups
- safe storage should let the player transfer items between inventory and a shared global box, retrievable from any safe storage point
- RmlUi is the preferred long-term gameplay UI interface candidate for themed inventory, item examine, save/storage, and puzzle UI, pending a focused integration spike
- ImGui should remain useful for editor/debug tooling and short-term gameplay prototyping, but it should not become the final survival-horror menu presentation layer
- `Engine.UI` now provides the gameplay UI integration seam, with a first `GameplayUiLayer` and RmlUi backend facade
- RmlUi content assets are starting under `Game/Content/UI`, with staged inventory styling and generated runtime RML for the native bridge spike
- the expected native bridge contract is now defined under `Native/HS2RmlUiBridge`, and `Engine.UI` can bind those exports when a bridge DLL is present
- the native-to-managed render-command handoff is now defined for RmlUi overlay data, but managed Veldrid draw consumption is still a scaffold
- the first managed Veldrid draw consumer exists for RmlUi render commands, using dynamic buffers, scissor rectangles, indexed draws, and a fallback white texture
- dedicated RmlUi UI shaders and managed texture-id lookup are in place, though real native texture upload is still pending
- the game now publishes inventory, pickup modal, prompt, message, selection, and save-count state to `Engine.UI`; ImGui is now only the gameplay UI fallback when RmlUi is not ready
- the generated RmlUi document path targets `Content/UI/Runtime/gameplay_ui.rml` and can live-refresh through the optional `hs2_rmlui_set_document_body` bridge export once the native DLL implements it
- an `Engine.UI` ImGui-backed preview renderer now lets the interaction test level show the new RmlUi-style gameplay UI layout before the native RmlUi DLL exists

The recent physics work remains important but is now parked as a foundation rather than the only active task.

Previous runtime prop rotation and collision work moved collision handling away from a pure AABB mindset and toward shape-aware colliders:

- `WorldCollider` now carries shape data and rotation
- dynamic box and capsule stepping passes rotation into collision resolution
- a new `ShapeCollision` layer handles rotated shape tests
- the collision path has started retaining contact manifolds so support, spin, and toppling can use contact points instead of only a single normal
- runtime and editor debug drawing now show sphere and capsule colliders properly
- dynamic contact and runtime world collider construction use shape-aware colliders

That work is aimed at fixing several visible problems:

- capsules and spheres behaving like hidden boxes
- props settling into implausible orientations
- boxes popping into awkward floor rotations
- capsules not tipping or resting believably
- stacked boxes resting at angles, on corners, or with too much low-speed jitter

## Known Current Limitations

- gameplay interactions are still authored by name prefixes such as `ItemKey_`, `LockedDoor_`, `LockedChest_`, `ItemInkRibbon_`, and `SavePoint_`
- the inventory UI is still a simple list rather than a proper inspect/combine/use survival-horror inventory
- inventory items now have data-model support for slot footprints, stack limits, categories, descriptions, stable slot indices, moving between valid grid slots, and swapping with occupied items when footprints fit, but item rotation/storage transfer is not implemented yet
- the item-collected/examine screen is a first pass and still needs final Resident Evil-style presentation and pause behavior
- the current inventory UI is still an ImGui prototype rather than the final themed menu layer
- RmlUi is set up at the project/asset/backend-seam level with generated gameplay RML, but it is not rendering until the native bridge DLL exists
- the current in-game test path uses an `Engine.UI` preview renderer for the new UI layout; this is intentionally temporary and should be replaced by the native RmlUi renderer when the bridge lands
- the native bridge named `HS2RmlUiBridge` is defined but not implemented yet
- the first Veldrid render-command consumer is implemented, but it still needs native render-command production, native texture upload/binding, richer input routing, font/assets, and RmlUi-side mouse/controller focus events
- safe storage boxes and inventory/storage transfer are not implemented yet
- locked chests currently disappear after granting prototype rewards rather than playing an authored open animation/state
- save/load is prototype-local JSON state, not a full slot/save-profile system
- `F6` reset is currently keyboard-only as a developer hotkey until a controller debug chord is chosen
- collision for rotated capsules versus boxes is orientation-aware, but still approximate rather than a full rigid-body contact manifold solution
- some support, picking, and ray logic may still rely on AABB-style fallbacks or broadphase approximations
- there are existing unrelated build blockers in the wider workspace, so a clean full `Game` build is not currently the validation signal
- parts of the current prop rotation system are still visual/runtime approximations layered over simplified collision

## Immediate Next Steps

- validate multi-lock key expiry, especially the Service Key staying useful across the save-office door and two supply chests before being removed
- validate save reload behavior after keys are collected, locks are opened, and a key has expired
- validate pickup/examine overlay flow on key pickups, ink ribbons, and chest rewards
- validate chest rewards for the Service Key supply chests: Scrap and Crank Handle
- validate inventory pause, mouse hover descriptions, and controller/keyboard grid navigation in game
- validate the latest inventory modal fix: opening inventory should stop camera/movement input and show a visible cursor for mouse hover
- validate the `Engine.UI` preview path in the interaction test level: prompts, item-collected overlay, inventory grid, mouse hover, and controller/keyboard selection
- polish prompt/readability feedback for locked doors, locked chests, expired keys, ink ribbon count, and typewriter state
- evaluate whether to keep custom ImGui/Veldrid UI for tooling only and move gameplay menus to a dedicated game UI library
- implement the RmlUi native bridge and Veldrid overlay renderer before building the final inventory/storage UI
- implement the C++ `HS2RmlUiBridge` DLL against the staged C ABI
- add native texture upload/delete exports so RmlUi font/image texture ids bind real Veldrid textures
- implement `hs2_rmlui_set_document_body` so generated inventory/pickup/prompt RML refreshes live
- implement native render-command production in `HS2RmlUiBridge` and validate it against the managed Veldrid consumer
- load the generated RmlUi gameplay document and validate mouse/controller focus navigation through the new `Engine.UI` layer
- extend slot-indexed inventory movement into item rotation and storage transfer
- tune interaction prompt readability and raycast distance
- replace the first-pass name-prefix interaction prototype with level-authored components if the flow feels right
- build a proper inventory screen with item descriptions and selected-item focus
- add shared safe storage after the item data model exists, so transfer/retrieve works with the same slot and stack rules
- start shaping the interaction test into a small horror vertical-slice room sequence
- keep multi-box pile stability and mixed box/capsule stack behavior parked unless it blocks the interaction loop
- extend the new contact-manifold path so support and angular response are driven more consistently by contact points
- remove remaining AABB fallback logic where exact rotated shape behavior matters
- improve support and settling logic for tipped capsules and resting boxes
- define and build a real standalone level editor workflow for creating levels more efficiently
- keep and improve the current in-scene play editor so runtime tweaking remains part of the workflow
- get a cleaner full-project build signal once unrelated workspace issues are addressed
- continue moving from placeholder physics behavior toward feel-driven gameplay behavior
- shape the first horror vertical slice once the interaction and physics foundation is stable enough to support it

## Reference Handover

Future chats should treat these files as the handover set:

- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT_GUIDELINES.md`
- `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`

When meaningful work is completed, update `WORKLOG.md` first, then refresh `PROJECT.md` if the project direction or current stage has changed.
