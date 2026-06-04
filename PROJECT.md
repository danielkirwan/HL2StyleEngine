# Project Overview

Last updated: 2026-05-28

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

Core engine structure is in place, the runtime and editor exist, and the current priority is turning the prototype into a small playable horror interaction loop. Physics polish remains important, but box stacking and pile stability are parked at good-enough prototype quality while inventory, keys, doors, puzzle objects, save points, storage, and player-facing UI are hardened.

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
- `Engine.UI` gameplay UI seam with ImGui fallback/preview support and native RmlUi integration work in progress
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
- key items that can be collected into the prototype inventory
- locked doors that check the inventory before opening
- locked chest-style test objects that use the same reusable lock/item rule
- level entities can now carry authored `Interaction` data for locks, chests, puzzle slots, puzzle doors, required items, state ids, target doors, success prompts, consume rules, and rewards
- typewriter save points that require ink ribbons
- typewriter save points now open a four-slot save panel after choosing an Ink Ribbon
- filled save slots now ask for overwrite confirmation before spending an Ink Ribbon or replacing the old save
- `Escape` now opens a first-pass pause menu with a Load Save option that lists the local save slots
- pause, save-slot, load-slot, inventory action, and storage panels now use native RmlUi hovered `data-slot` elements for mouse hover/click selection alongside keyboard/controller navigation, with old fixed hitboxes kept only as a non-native fallback
- save slots persist collected keys, opened doors, solved puzzles, ink count, storage, inventory layout, save count, player position, playtime, level/area display name, save-point name, difficulty placeholder, and profile placeholder data
- non-ink key/item expiry when every matching locked door or chest has been opened
- first-pass item data model with item definitions, item types, slot footprints, stack limits, counts, and saveable inventory stacks
- pickup/examine presentation for collected items, now moving through the native RmlUi path
- locked chests can now grant prototype reward items from the item catalog
- the inventory overlay now shows grid slots, stack counts, item footprints, and selected-item descriptions
- opening inventory pauses gameplay and allows mouse hover or controller/keyboard navigation over the inventory grid
- inventory item stacks now have stable grid slot indices and save/load those positions
- inventory items can now be picked up inside the inventory screen, moved to another grid slot, validated against their slot footprint, placed, or cancelled
- multi-slot inventory items now visibly occupy their covered grid slots in the UI preview, and occupied items can swap positions when their footprints fit after the exchange
- non-square inventory items can now rotate with `R` / controller `Y`, including rotated placement validation and save/load persistence
- inventory navigation now treats covered multi-slot cells as part of the same item when browsing, while still allowing cell-level targeting during item placement
- stackable inventory items can now merge, keep source overflow when a target stack fills, and split a chosen amount with `Q` / controller `LeftShoulder`
- mouse inventory movement now supports press-hold-release dragging for move, merge, and swap testing
- a developer item spawn prompt on `T` can create test pickups from text commands such as `spawn ink x3`, `spawn crank`, `spawn gunpowder x3`, or `spawn testkey`
- a `Test Key` / `MasterKey` exists for prototype testing and can open any authored lock without being consumed
- the inventory has a first-pass action menu for `Use`, `Examine`, `Move`, `Combine`, `Split`, and `Discard`
- inventory combine mode highlights the selected source item, valid combine targets, and invalid occupied items
- combine recipes are now catalog data with optional preview names/descriptions; the current prototype recipe is `Scrap` + `Gunpowder` creating `Bullets x12`
- valid combine targets now show a result preview through the shared `Engine.UI` state before the player confirms the recipe
- a first shared item-box storage flow exists in the save office, with partial-stack transfer for stackable items
- inventory discard is now enabled for disposable stacks behind a confirmation panel, while keys, puzzle items, and future weapons are protected from accidental discard
- inventory action, split, discard, storage, and storage-transfer overlays now show clearer item identity, stack, footprint, and description feedback with item icons or fallback icon text badges
- locked doors, locked chests, and typewriter saves now open a focused usable-item list; locks/chests show key/puzzle candidates, and typewriters show ink ribbons
- item pickup and `Examine` now share a larger focused item presentation panel
- the utility room now has a first crank-slot puzzle: using the Crank Handle raises a matching door over time and saves that solved state
- the crank-door route now leads into a small service alcove containing a cataloged Fuse puzzle item
- the Fuse can now be used on a power panel to raise a second powered gate from the crank alcove
- the crank and Fuse gate geometry has been adjusted so panels sit beside passages and lift doors cover their intended openings
- a developer reset hotkey for clean interaction-test runs
- a small multi-room key route that is playable and starts shaping the horror vertical slice
- controller parity for new player-facing actions

The intended inventory direction is Resident Evil Requiem-inspired:

- item pickup can show a focused item-collected/examine screen with the world blurred behind it
- the inventory should become a grid-based case rather than a plain list
- every inventory item should define a slot footprint, stack limit, display name, description, item type, and any relevant combine recipes
- stackable items can occupy one footprint while holding a count up to their stack limit
- stackable items should merge, split, and preserve overflow predictably across inventory, future storage, and save/load
- keys and puzzle objects are reusable until their authored uses are exhausted, then they are removed from inventory with feedback
- ink ribbons remain consumable save resources and are not part of the reusable-key expiry rule
- locked chests should later reveal items such as puzzle objects, upgrade materials, or other pickups
- safe storage should let the player transfer items between inventory and a shared global box, retrievable from any storage point; the first prototype supports stack quantity transfer and save/load
- RmlUi is the chosen gameplay UI direction for themed inventory, item examine, save/storage, and puzzle UI, with ImGui kept for editor/debug tooling and as a temporary safety preview
- ImGui should remain useful for editor/debug tooling and short-term gameplay prototyping, but it should not become the final survival-horror menu presentation layer
- `Engine.UI` now provides the gameplay UI integration seam, with a first `GameplayUiLayer` and RmlUi backend facade
- RmlUi content assets live under `Game/Content/UI`, with staged inventory styling and generated runtime RML for the native bridge path
- the RmlUi inventory and pickup/examine presentation now has a darker survival-horror case layout, item showcase cards, and placeholder icon slots ready for final item art
- item icon generation prompts are tracked in `Game/Content/UI/IconPrompts.md`
- focused use-item panels for doors, puzzle slots, and typewriters now use the same icon-ready presentation path, falling back to text glyphs until item PNGs exist under `Content/UI/Icons`
- inventory icon PNGs are now included in the game output, and the lookup accepts the current art filenames such as `Crank.png` for the `CrankHandle` item id
- the expected native bridge contract is implemented by the first `Native/HS2RmlUiBridge` DLL, and `Engine.UI` binds those exports when the DLL is present
- native-to-managed render-command handoff now produces RmlUi overlay data from the C++ bridge
- the first managed Veldrid draw consumer exists for RmlUi render commands, using dynamic buffers, scissor rectangles, indexed draws, uploaded native textures, and a fallback white texture
- dedicated RmlUi UI shaders and managed texture-id lookup are in place, with PNG texture data now exported by the bridge and uploaded by `Engine.UI`
- native RmlUi presentation has a diagnostic test document mode via `HS2_RMLUI_TEST_DOCUMENT=1`
- the RmlUi overlay renderer now tracks submitted draw commands and keeps the ImGui preview visible as a safety net until a native overlay frame has been submitted
- the diagnostic native RmlUi text path is visible in-game; native inspection confirms the diagnostic image emits a textured draw command, so any remaining missing icon is likely in managed texture presentation
- the managed RmlUi overlay renderer now batches each full frame into one vertex/index upload and draws commands by offset, avoiding repeated same-offset dynamic-buffer rewrites while the command list is still being built
- the first native RmlUi diagnostic has been confirmed in playtest with styled card geometry and an inventory icon image visible in-game
- native RmlUi now exposes hovered `data-slot` elements back to managed code so inventory slot hover/click can reuse the existing gameplay selection logic
- native RmlUi hover/click routing now covers pause rows, save/load slots, inventory action rows, inventory slots, use-item rows, and both sides of item-box storage, with separate native slot ranges where panels can overlap
- generated RmlUi now includes first-pass inventory action menu, split picker, and discard confirmation panels instead of leaving those states visible only through ImGui
- the managed RmlUi renderer now validates native render data before GPU submission and rejects unsafe frames instead of trusting bad geometry/indices
- item-collected presentation now waits for the pickup/confirm button to be released, applies a short confirm grace delay, and stops the gameplay update frame after pickup so the card cannot be eaten by the collection press
- native inventory styling now uses stronger solid backplates and clearer slot borders so the grid is readable in-game
- generated native inventory slots now use absolute positions based on grid slot index instead of relying on RmlUi inline-flow layout
- the game now publishes inventory, pickup modal, prompt, message, selection, and save-count state to `Engine.UI`
- the gameplay overlay now includes a small center crosshair while normal gameplay control is active, with explicit pixel positioning and a renderer-side fallback so it is not dependent on RmlUi percentage layout support
- the generated RmlUi document path targets `Content/UI/Runtime/gameplay_ui.rml` and can live-refresh through the implemented `hs2_rmlui_set_document_body` bridge export
- an `Engine.UI` ImGui-backed preview renderer remains available as a fallback when native RmlUi presentation is disabled, but native mode no longer draws the old gameplay ImGui preview unless the emergency modal preview flag is explicitly enabled

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

- locks, chests, puzzle slots, and puzzle doors now support authored interaction data, but older name-prefix conventions still exist as a compatibility fallback
- the inventory UI has inspect/combine/use/move/split/discard/storage flows, but it is still prototype presentation rather than a finished survival-horror inventory screen
- inventory items now have data-model support for slot footprints, stack limits, categories, descriptions, stable slot indices, movement, swapping, rotation, stack merging, quantity-picked splitting, data-driven combine recipes with preview text, discard protection, filtered use-item selection, shared storage transfer, and rotated save/load
- the item-collected/examine screen has a native RmlUi path with item PNG support, but it still needs final Resident Evil-style visual polish and full validation
- RmlUi is set up at the project/asset/backend-seam level with generated gameplay RML, and the first native `HS2RmlUiBridge.dll` renders through the managed Veldrid overlay when native presentation is enabled
- the ImGui preview renderer remains a fallback/safety path when native RmlUi is disabled; native RmlUi mode no longer draws the old gameplay preview by default
- the native bridge now implements context creation, document loading, live generated-document refresh, render-command production, PNG texture export, and a temporary pixel-font fallback
- the first Veldrid render-command consumer now uploads native RGBA texture data and binds RmlUi texture ids, but still needs richer input routing, final font/assets decisions, and RmlUi-side mouse/controller focus events
- native RmlUi rendering can now be tested with `HS2_RMLUI_NATIVE_PRESENTATION=1`; `HS2_RMLUI_TEST_DOCUMENT=1` forces a known diagnostic panel before testing generated inventory RML
- the diagnostic document is render-only validation; real button/menu testing should use `HS2_RMLUI_NATIVE_PRESENTATION=1` without `HS2_RMLUI_TEST_DOCUMENT`
- if native RmlUi shows the main inventory but a submenu does not appear, the likely fix is exposing that gameplay state through `GameplayUiState` and generated RML rather than changing the renderer
- generated native RmlUi is still treated as an opt-in validation path; if it produces unsafe render data, the overlay renderer should reject the frame and avoid GPU-driver hangs while falling back to the safe preview path
- pickup/examine cards now use native RmlUi by default again, with explicit image dimensions for item PNGs and `HS2_RMLUI_FORCE_PREVIEW_MODALS=1` available as an emergency fallback
- native pickup/examine cards are now smaller, use clearer text spacing, and the current item PNGs have had baked checkerboard backgrounds removed
- pickup/examine card text now uses fixed native RmlUi rows rather than paragraph/header flow while the temporary pixel-font/layout path is still maturing
- focused use-item panels for locks, puzzle slots, and typewriters now use a fixed native RmlUi layout so they should appear reliably when interacting with authored use targets
- interaction prompts, game messages, inventory action/split/discard overlays, and item-box storage now have native RmlUi presentation paths; the dev spawn textbox remains ImGui because it is a test/debug tool
- look-at prompts now use fixed native RmlUi positioning plus a short prompt grace timer so small-item raycast flicker does not hide them immediately
- native RmlUi presentation still needs visual validation across pickup cards, prompt/message cards, inventory grid, action menus, split/discard panels, use-item panels, and storage before ImGui gameplay preview can be retired
- safe storage boxes and inventory/storage transfer are implemented as a first pass, but need final RmlUi visual polish and multi-storage-point validation
- locked chests currently disappear after granting prototype rewards rather than playing an authored open animation/state
- save/load now supports four local JSON save slots with overwrite confirmation, richer metadata, and a first pause-menu load screen, but there is not yet a main-menu load screen or broader player profile system
- `F6` reset is currently keyboard-only as a developer hotkey until a controller debug chord is chosen
- collision for rotated capsules versus boxes is orientation-aware, but still approximate rather than a full rigid-body contact manifold solution
- some support, picking, and ray logic may still rely on AABB-style fallbacks or broadphase approximations
- there are existing unrelated build blockers in the wider workspace, so a clean full `Game` build is not currently the validation signal
- parts of the current prop rotation system are still visual/runtime approximations layered over simplified collision

## Immediate Next Steps

- validate multi-lock key expiry, especially the Service Key staying useful across the save-office door and two supply chests before being removed
- validate save reload behavior after keys are collected, locks are opened, and a key has expired
- validate pickup/examine overlay flow on key pickups, ink ribbons, and chest rewards; native RmlUi pickup cards should show cleaned item PNGs, stay visible until a fresh confirm press, and return camera control immediately after closing
- validate chest rewards for the Service Key supply chests: Scrap and Crank Handle
- validate inventory pause, mouse hover descriptions, and controller/keyboard grid navigation in game
- validate native RmlUi inventory pause behavior: opening inventory should stop camera/movement input and show a visible cursor for mouse hover
- validate inventory rotation using the Crank Handle: `R` / controller `Y` should swap between `1x2` and `2x1`, reject invalid placements, and persist through typewriter save/load
- validate stack behavior: spawned Ink Ribbons and Scrap should merge up to max stack, keep overflow in the source stack, split chosen quantities with `Q` / controller `LeftShoulder`, and save/load counts cleanly
- validate the `T` developer spawn prompt with `spawn ink x3`, `spawn scrap x20`, `spawn gunpowder x3`, `spawn bullets x12`, `spawn crank`, and `spawn testkey`
- validate the first action menu: `E` / controller `X` opens item actions, `Move` starts grid movement, `Split` opens the quantity picker, and `Combine` highlights valid targets before letting Scrap + Gunpowder create Bullets
- validate combine previews: choosing `Combine` on Scrap or Gunpowder should show the `Craft Bullets` result preview when the valid target is selected
- validate `StorageBox_SaveOffice`: transfer items to/from storage, save, reload, and confirm stored items persist
- validate partial storage transfer for Ink Ribbons, Scrap, Gunpowder, and Bullets
- validate focused native RmlUi use-item lists for locked doors, locked chests, puzzle slots, and typewriter saving
- validate native RmlUi prompt/message cards when looking at pickups, locked doors, typewriters, puzzle slots, storage boxes, and physics props
- validate look-at prompts remain stable while aiming at small item pickups and do not flash the old ImGui prompt for only one frame
- validate native RmlUi storage at `StorageBox_SaveOffice`, including switching sides and quantity transfer
- validate `Examine` opening the focused item details panel from inventory
- validate inventory discard confirmation on disposable stacks and protected feedback on keys, the Crank Handle, and the Fuse
- validate the clearer storage/action detail panels with mouse, keyboard, and controller navigation
- add more data-driven combine recipes once puzzle/material needs are known
- polish prompt/readability feedback for locked doors, locked chests, expired keys, ink ribbon count, and typewriter state
- validate the native RmlUi bridge in the interaction test level with `HS2_RMLUI_NATIVE_PRESENTATION=1`, especially pickup cards, inventory grid, use-item panels, prompts, storage, and item icons
- validate the native diagnostic panel first with `HS2_RMLUI_NATIVE_PRESENTATION=1` and `HS2_RMLUI_TEST_DOCUMENT=1`, confirming the styled card and `InkRibbon.png` icon both render
- validate the generated native RmlUi gameplay UI with `HS2_RMLUI_NATIVE_PRESENTATION=1` and no diagnostic flag, especially `I` inventory open/close, `E` / controller `X` action menu, mouse hover/click slot selection, and item move/merge/swap
- validate `hs2_rmlui_set_document_body` live refresh during rapid gameplay state changes such as opening inventory, selecting items, and picking up objects
- decide whether to keep the temporary pixel-font fallback for prototyping or add a proper FreeType/bitmap-font asset path for the final UI
- extend RmlUi mouse/controller focus navigation through the new `Engine.UI` layer once the rendered presentation is stable
- extend slot-indexed inventory movement into final RmlUi item action menus, filtered usable-item screens, proper quantity picker styling, and storage transfer presentation
- tune interaction prompt readability and raycast distance
- continue migrating interaction behavior from name-prefix fallbacks into authored `Interaction` data, then expose that cleanly in editor tooling
- finish the proper native RmlUi inventory screen with item descriptions, selected-item focus, icons, action panels, quantity panels, and controller/mouse parity
- expand shared safe storage beyond the first save-office box once the single storage point is validated
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
