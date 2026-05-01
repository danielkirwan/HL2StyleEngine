# Work Log

Last updated: 2026-05-01

This file is the running handover for active work, recent changes, and the next tasks.

## Current Focus

Current focus is now the first playable Resident Evil-inspired interaction loop for the horror prototype.

The immediate goal is to harden the separate interaction test level now that the playable area, keys, doors, ink-ribbon typewriter saving, inventory feedback, and small explore-to-unlock room chain are working in playtest.

The box stacking and pile stability work is parked at a good-enough prototype point for now so the project can regain visible gameplay momentum.

The current gameplay issues being worked first are:

- held objects should collide while being carried
- boxes should fall and settle less like scripted orientation correction
- overhanging boxes on moving platforms should be able to fall off when their center of mass is no longer supported
- thrown boxes should stop freezing in implausible corner-balanced poses
- stacked boxes should settle with more weight and less low-speed jitter or sliding on each other
- clean box-on-box drops should settle flat instead of freezing slightly angled on broad support
- key items should be collectable through the same context interaction button used by physics pickup
- locked doors should check inventory and open when the required key is held
- typewriter save points should require ink ribbons and persist prototype progress
- the interaction test level should be resettable for clean playtest runs
- non-ink keys should stay useful across linked locks, then auto-remove once no matching locks remain

## Current Status Snapshot

- dynamic prop runtime rotation work is in progress
- box, sphere, and capsule debug collider drawing is now shape-aware
- rotation-aware collider plumbing has been introduced for dynamic boxes and capsules
- world collision now has a first contact-manifold layer carrying contact points as well as normal and penetration
- dynamic bodies now retain strongest world-contact and best support-contact manifolds from the physics step
- box toppling and world-collision spin now prefer manifold contact data before falling back to support-AABB heuristics
- `Engine.Physics` has a passing build signal
- the wider `Game` project still does not have a clean full-build signal because of unrelated workspace issues
- the roadmap now includes a proper standalone level editor for building maps, while keeping the runtime in-scene editor for live playtest adjustments
- the longer-term game target remains a grounded horror game built on top of this HL2-style movement and interaction foundation
- the current gameplay prototype now has a playable interaction test level for key-door-save flow
- the user has confirmed the playable area is good and keys can be found and used
- RmlUi is now the preferred direction for final gameplay UI, while ImGui remains the current prototype/editor UI layer

## 2026-05-01

### Summary

- added inventory item movement across the slot-indexed grid
- added `InventoryContainer.CanMoveStackToSlot(...)` and `MoveStackToSlot(...)` so item footprint validation happens in the inventory model rather than the UI
- added inventory move state to gameplay UI data: moving flag, source slot, target slot, and target validity
- wired keyboard/controller confirm to inventory movement: `E` / controller `X` picks up the selected item, moves the target with existing navigation, and places it if the item fits
- wired mouse movement support: clicking an item starts moving it, hovering/clicking a destination slot targets placement, and clicking a valid destination places it
- made `I` / controller `Back` cancel an active item move before closing the inventory
- updated the Engine.UI preview renderer to highlight the moving source slot, valid destination slots, and invalid destination slots
- updated generated RML and inventory RCSS with matching move-valid / move-invalid / moving-source state classes for the future native RmlUi path
- copied the validated isolated build into `Game/bin/Debug/net8.0` so the interaction test level can immediately test inventory movement
- made multi-slot item footprints visible in the Engine.UI preview, so a 1x2 item now occupies both grid slots instead of only showing `1x2` text
- added covered-slot data to gameplay UI inventory items so generated RML and the preview can distinguish item origin slots from footprint-covered slots
- added move-or-swap support in `InventoryContainer`; dragging an item over another item can swap their positions when both item footprints fit after the exchange
- updated move feedback so valid swaps show as allowed and display a swap hint instead of an invalid placement message
- added inventory item rotation for non-square items using `R` / controller `Y`
- persisted each inventory stack's rotated state in prototype save data
- updated movement, placement, and swap validation so rotated footprints are checked by the inventory model
- improved multi-slot selection so covered cells resolve back to the item origin when browsing, while moving items can still target individual destination cells
- updated the gameplay UI state, preview renderer, and generated RML hints to show the held item's rotated footprint during placement

### Why

- the inventory needs real case-management behavior before shared storage, item transfer, or item action menus will feel useful
- movement validation belongs in the inventory model so ImGui preview, generated RML, future native RmlUi, and safe storage all share the same rules
- keeping movement on the existing confirm/cancel controls preserves controller parity without adding surprise new bindings
- Resident Evil-style inventory readability depends on item footprints being visible, especially before final item art exists
- swap behavior avoids busywork when reorganizing the case and keeps the prototype closer to modern survival-horror inventory feel
- rotation is needed before larger puzzle items, weapons, and storage transfer can share the same case-management rules
- controller navigation should feel like selecting items, not accidentally selecting unreachable covered cells

### Files

- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Inventory\InventoryContainer.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Inventory\InventoryItemStack.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Inventory\InventoryItemSaveData.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\GameplayUiState.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\GameplayUiImGuiPreviewRenderer.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\Rml\RmlUiDocumentBuilder.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Content\UI\Inventory\inventory.rcss`
- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`

### Validation

- `Engine.UI` compile check succeeded
- `Engine.Runtime` compile check succeeded
- isolated `Game` compile check succeeded
- copied the validated `Game.dll`, `Engine.UI.dll`, and `Engine.Runtime.dll` into the normal `Game/bin/Debug/net8.0` output for immediate playtesting
- `Engine.UI` compile check succeeded after adding footprint-covered slots and swap hints
- isolated `Game` compile check succeeded after adding inventory swap behavior
- copied the validated footprint/swap build into the normal `Game/bin/Debug/net8.0` output for immediate playtesting
- `Engine.UI` compile check succeeded after adding rotation UI state and hints
- `Engine.Runtime` compile check succeeded after the UI state change
- isolated `Game` compile check succeeded after adding inventory rotation and rotated save data

### Next

- playtest inventory movement: pick up keys, ink ribbons, Scrap, and the 1x2 Crank Handle, then move them to valid and invalid slots
- playtest swapping: drag a key or ribbon onto Scrap/Crank Handle and confirm both items trade positions only when both footprints fit
- playtest rotation: move the Crank Handle, press `R` / controller `Y`, and confirm it can place as `2x1` only where that footprint fits
- verify rotated slot positions persist after saving at the typewriter and reloading
- verify `I` / controller `Back` cancels an active move and only closes inventory on the next press
- verify moved slot positions persist after saving at the typewriter and reloading
- add item action menus and stack split/merge rules before shared safe storage

## 2026-04-29

### Summary

- recorded RmlUi as the preferred long-term gameplay UI interface direction for inventory, item examine screens, and safe storage
- kept ImGui as the current short-term prototype/editor UI layer until RmlUi integration is scoped
- tightened inventory and item-collected modal input so opening them forces UI mouse mode, clears mouse delta, blocks gameplay/camera input, and draws a visible ImGui cursor
- removed the inventory window close button path so inventory state cannot desync from mouse capture; it should close through `I` / controller `Back`
- added an `Engine.UI` project as the gameplay UI integration seam
- added a first `GameplayUiLayer` and `RmlUiBackend` facade that probes for a future native bridge named `HS2RmlUiBridge`
- added a runtime overlay render hook so gameplay UI can draw after the world is resolved and before ImGui editor/debug UI
- staged starter RmlUi inventory assets under `Game/Content/UI/Inventory`
- wired `Game` to create/update/render the gameplay UI layer and report RmlUi backend status in the debug window
- added the managed native binding layer for the expected `HS2RmlUiBridge` exports
- added a native C ABI header for `HS2RmlUiBridge`
- updated the RmlUi backend so, when the bridge exists, it binds exports, creates an RmlUi context, loads `Inventory/inventory.rml`, and forwards frame size, mouse position, update, and render calls
- added the native render-command ABI for RmlUi overlay data: vertices, indices, texture ids, scissor rectangles, translations, and viewport size
- added managed render-data structs and bound `hs2_rmlui_get_render_data` / `hs2_rmlui_release_render_data`
- added a first `RmlUiOverlayRenderer` scaffold that receives native render data and reports command counts until real Veldrid draw consumption is implemented
- exposed the RmlUi render status in the debug window
- upgraded `RmlUiOverlayRenderer` into a first Veldrid draw consumer with dynamic vertex/index buffers, fallback white texture, scissor-enabled pipeline state, pixel-to-NDC conversion, and indexed draw submission
- kept the first draw path on existing present-pass shaders, so it validates geometry/scissor/draw flow but does not yet apply RmlUi vertex colors or native texture ids
- added dedicated `RmlUiVS` and `RmlUiPS` shaders and compiled them to `.cso`
- changed the RmlUi overlay vertex path to carry unpacked per-vertex color into the UI shader
- added a texture-id registry shape in the overlay renderer so native RmlUi texture ids can later bind real Veldrid texture views, with unknown ids falling back to white
- added a gameplay UI state model in `Engine.UI` so the game publishes inventory, pickup modal, prompt, message, selection, and save-count data without the UI backend reaching into game internals
- added a generated RML document path for live gameplay UI state under `Content/UI/Runtime/gameplay_ui.rml`
- updated the RmlUi backend to write generated RML, load the generated runtime document, and optionally live-refresh it through a new `hs2_rmlui_set_document_body` bridge export when the native DLL supports it
- changed the gameplay HUD so ImGui now acts as a fallback presentation only; once RmlUi is ready, gameplay inventory/pickup/prompt rendering is owned by the `Engine.UI` path
- upgraded inventory stacks to carry stable grid slot indices and save/load those slot positions
- changed inventory add/load behavior to find valid free grid footprints for item slot sizes, so the RmlUi grid receives real placement data instead of list positions
- refreshed the staged inventory RCSS to target the generated RmlUi gameplay document and its inventory, prompt, and collected-item panels
- added an `Engine.UI` ImGui-backed RmlUi preview renderer so the test level can show the new gameplay UI layout before the native RmlUi bridge DLL exists
- routed gameplay HUD drawing through `GameplayUiLayer.DrawPreview(...)`, so inventory, item-collected overlays, interaction prompts, and game messages now use the shared `GameplayUiState` presentation path in play mode
- kept old game-side ImGui HUD code only as a last fallback; the preview path returns selected/hovered slots back to the game so mouse descriptions still update
- copied the validated isolated UI preview build into the normal `Game/bin/Debug/net8.0` output so the user can test it immediately in the interaction test level

### Why

- the survival-horror menus need a more themed gameplay UI layer soon, and RmlUi looks like the best free candidate to evaluate before the inventory grows much further
- playtesting showed that opening inventory with `I` could still let mouse movement affect the camera and leave the cursor hard to see
- modal menus should feel like Resident Evil-style pause screens: world/input paused, visible cursor, mouse hover inspection, and controller navigation
- RmlUi is C++ first, so the engine needs a clean native-bridge seam instead of coupling gameplay code directly to interop and renderer details
- gameplay UI should render in an overlay pass separate from world rendering and before ImGui, so final menus can sit above the game but below editor/debug tooling
- defining the bridge contract early keeps the native C++ work small and testable instead of letting RmlUi interop leak into game code
- the managed backend should be able to detect missing or incompatible bridge DLLs gracefully while the prototype continues using ImGui
- the native bridge and managed Veldrid renderer need a stable handoff format before either side can be implemented safely
- keeping render data lifetime explicit avoids dangling native pointers while still letting the bridge own CPU-side RmlUi geometry buffers
- implementing the managed draw consumer before the native DLL makes the next bridge slice easier to test: when native commands arrive, the C# side can already submit them
- using the existing shader pair keeps this slice low-risk while the dedicated UI shader/texture path is still being defined
- dedicated UI shaders are needed because RmlUi output depends on per-vertex color as well as texture sampling
- texture-id lookup keeps the draw loop stable while native texture upload support is added separately
- moving gameplay UI state into `Engine.UI` is the important swap-over step: game logic should publish data, not draw final menus directly with ImGui
- generated RML lets the current inventory/pickup modal be represented in the chosen UI technology before the C++ bridge is fully implemented
- keeping ImGui as an automatic fallback avoids breaking playtests while the native bridge work continues
- real slot indices are needed before the inventory can become a Resident Evil-style case with stable item positions, footprints, stack counts, controller focus, and eventual storage transfer
- the native RmlUi bridge is not present in the workspace yet, so a preview renderer lets the user test the new UI data/layout flow in-game instead of waiting for the C++ integration
- keeping the preview renderer inside `Engine.UI` means the game is already calling the same UI-layer seam that the final RmlUi renderer will own

### Files

- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\GameplayUiState.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\GameplayUiImGuiPreviewRenderer.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\Engine.UI.csproj`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\GameplayUiLayer.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\RmlUiBackend.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\RmlUiFrameContext.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\Native\RmlUiNativeApi.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\Native\RmlUiRenderData.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\Rml\RmlUiDocumentBuilder.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.UI\Rendering\RmlUiOverlayRenderer.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Native\HS2RmlUiBridge\HS2RmlUiBridge.h`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Native\HS2RmlUiBridge\README.md`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Runtime\Hosting\IOverlayRenderer.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Runtime\Hosting\EngineHost.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Inventory\InventoryContainer.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Inventory\InventoryItemSaveData.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Inventory\InventoryItemStack.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\UIModeController.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Content\UI\Inventory\inventory.rml`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\Content\UI\Inventory\inventory.rcss`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Render\Shaders\RmlUiVS.hlsl`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Render\Shaders\RmlUiVS.cso`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Render\Shaders\RmlUiPS.hlsl`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Render\Shaders\RmlUiPS.cso`
- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`

### Validation

- `Game` compile check succeeded after the modal input/cursor change
- targeted restore succeeded after adding the new `Engine.UI` project reference
- `Engine.UI` compile check succeeded
- `Engine.Runtime` compile check succeeded after adding the overlay hook
- `Game` compile check succeeded after wiring the RmlUi setup
- `Engine.UI` compile check succeeded after adding native bridge binding
- `Game` compile check succeeded after adding the managed bridge binding
- `Engine.UI` compile check succeeded after adding render-command binding
- `Game` compile check succeeded after pre-seeding the isolated output with the new `Engine.UI` reference assembly for the existing `--no-dependencies` validation path
- `Engine.UI` compile check succeeded after adding the first Veldrid draw consumer
- `Engine.Runtime` compile check succeeded after the overlay hook remained compatible
- `Game` compile check succeeded after the Veldrid draw consumer update
- `fxc.exe` compiled the dedicated RmlUi vertex and pixel shaders
- `Engine.UI` compile check succeeded after switching to dedicated UI shaders and texture-id lookup
- `Game` compile check succeeded after the UI shader renderer update
- `Engine.UI` compile check succeeded after adding gameplay UI state and generated RML document support
- `Engine.Runtime` compile check succeeded after the overlay hook remained compatible
- `Game` compile check succeeded after routing inventory/pickup/prompt state through `Engine.UI` and adding slot-indexed inventory placement
- `Engine.UI` compile check succeeded after adding the ImGui-backed RmlUi preview renderer
- `Engine.Runtime` compile check succeeded after the preview path stayed compatible
- isolated `Game` compile check succeeded after routing play-mode HUD drawing through the `Engine.UI` preview
- copied the validated `Game.dll`, `Engine.UI.dll`, and `Engine.Runtime.dll` into the normal `Game/bin/Debug/net8.0` output for immediate playtesting because the normal full build remains blocked by generated-file write permissions
- in-game validation needed: press `I`, confirm camera/movement do not respond, cursor is visible, mouse hover still selects items, and `I` / controller `Back` closes cleanly

### Next

- test the interaction test level: press `I` / controller `Back`, confirm the new `Engine.UI` preview inventory appears, selection moves, descriptions update, item-collected screens show, and prompts/messages still appear
- validate the fixed inventory pause/cursor behavior in game
- validate inventory save/load with the new `SlotIndex` field, especially older saves that do not have slot indices yet
- validate that inventory selection still moves cleanly with WASD, D-pad, and left stick after slot-indexed placement
- implement the C++ `HS2RmlUiBridge` DLL against the new header and export the expected C ABI
- add native texture upload/delete exports and connect them to the managed texture registry
- implement `hs2_rmlui_set_document_body` in the native bridge so generated gameplay UI RML can refresh live without reloading the whole document
- implement the native render-command producer inside `HS2RmlUiBridge`
- wire RmlUi input routing for mouse, keyboard text, and controller focus navigation
- load the generated `Content/UI/Runtime/gameplay_ui.rml` document once the bridge is active
- keep ImGui inventory only as a fallback while RmlUi replaces the presentation layer
- build true grid item placement and shared safe storage after the modal input path is confirmed solid

## 2026-04-27

### Summary

- confirmed the expanded playable area works well enough for the current interaction pass
- kept the generated interaction test level as the active default runtime level
- closed door and wall blockout gaps found during playtesting
- updated `Wall_SaveOffice_Side_South` to the editor-tested position `(-3.05, 1.7, -4.75)` and size `(0.35, 3.4, 3.4)`
- added reusable non-ink item expiry for locked objects: an inventory item stays while any matching lock remains unopened, then auto-removes with feedback after the last matching lock opens
- added two Service Key supply chests so the Service Key now exercises a three-lock path
- kept ink ribbons separate from the reusable key/item expiry system
- captured the intended Resident Evil Requiem-inspired inventory direction: pickup examine screen, grid inventory, item slot footprints, stack limits, descriptions, chest rewards, and shared safe storage
- added the first item data model with item definitions, item types, slot footprints, stack limits, saveable item stacks, and an inventory container sized for an 8x4 grid
- moved the working key and ink ribbon prototype onto the item container while keeping the existing simple inventory overlay functional
- added a first item-collected overlay for pickups and chest rewards, dismissed with the existing `E` / controller `X` confirm action
- turned the Service Key supply chests into prototype reward containers that grant Scrap and a Crank Handle
- upgraded the temporary inventory overlay into a simple grid-slot view with stack counts, item footprints, and selected-item descriptions
- changed inventory into a modal paused state: opening it releases the mouse, blocks gameplay interaction/movement, and skips fixed-step gameplay simulation until closed
- added mouse hover selection for inventory grid items so descriptions update under the cursor
- added keyboard/controller grid navigation using WASD, D-pad, and left stick to move the highlighted inventory slot

### Why

- the interaction loop is now playable enough that the next work should focus on progression feel, readability, and converting prototype rules into authored data
- wall and door blockers need slight overlap in this blockout style so the player capsule cannot squeeze through seams
- Resident Evil-style keys can open several authored locks before becoming useless, so expiry should be based on remaining matching locks rather than a hard-coded counter
- ink ribbons are consumable save resources, not reusable lock items, so they should not be removed by the key-expiry rule
- chests are planned as item reward containers, so the next inventory work needs a real item model before chest rewards or storage can be implemented cleanly
- shared safe storage depends on the same item stacking and slot-footprint rules as the player inventory
- building the item model before the UI means chests, item pickup screens, inventory grid layout, and safe storage can all share the same item definitions
- the first reward items prove the item catalog can support both stackable materials and larger puzzle objects
- inventory should behave like a proper survival-horror menu: the world pauses, the mouse can inspect items, and controller navigation can move across the grid without requiring a mouse

### Files

- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\World\SimpleLevel.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`

### Validation

- user playtest confirmed the playable area is good and keys can be found and used
- `Game` compile check succeeded after the interaction/save/layout changes
- `Game` compile check succeeded after adding the item data model and wiring current keys/ink ribbons through it
- `Game` compile check succeeded after adding pickup examine overlay, chest rewards, and the first grid-style inventory overlay
- `Game` compile check succeeded after adding paused inventory state, mouse hover descriptions, and controller grid navigation
- `git diff --check` passed with only existing line-ending warnings
- in-game validation still needed for the three-lock Service Key expiry path and save reload edge cases

### Next

- validate the Service Key expiry path: save-office door plus two supply chests, with inventory removal only after the last matching lock opens
- validate typewriter save reload after keys are consumed, doors/chests are opened, and the Service Key has expired
- improve prompt/readability polish for keys, doors, chests, ink ribbons, and typewriter state
- validate pickup examine overlay flow for keys, ink ribbons, Scrap, and Crank Handle
- validate the Service Key chest rewards and key expiry after all Service Key locks are opened
- improve the grid inventory into true item placement/movement instead of the current simple slot view
- evaluate a longer-term UI library before the inventory grows much beyond the current ImGui prototype
- add shared safe storage after grid inventory and stack transfer rules exist
- convert the name-prefix prototype interaction rules into authored interaction/lock/item data
- add real inventory item inspection/description UI rather than only a simple item list
- decide whether `F6` remains keyboard-only or gets a controller debug chord after controller layout is chosen
- continue leaving deep box-stack physics parked unless it blocks the interaction route

## 2026-04-26

### Summary

- parked box-stack tuning at the current prototype quality level and moved active work to interaction/game-loop progress
- changed the default runtime level to `interaction_test.json` and added a generated interaction test layout with a key desk, locked door, and save room
- added context interaction on `E` / controller `X`, prioritizing key/door/save-point use before falling back to physics pickup/drop
- added a simple inventory overlay on `I` / controller `Back`
- added runtime feedback messages for collecting the key, trying the locked door, unlocking it, and using the save point
- expanded the interaction test into a multi-room key chain with Rusted, Service, and Archive keys
- added ink ribbon pickups and changed the typewriter save point so saving consumes one ink ribbon
- added a prototype save file that restores inventory, collected items, opened doors, ink ribbon count, save count, and player camera position
- added `F6` as a developer reset hotkey that clears the prototype save and rewrites the interaction test level to its default layout
- added dynamic crates in the expanded interaction rooms so the level still supports prop interaction testing while exploring
- widened the generated locked-door blockers so they overlap adjacent wall pieces and do not leave player-sized side gaps
- tightened the interaction-test template refresh so older generated JSON is rewritten if it still has the too-small door blockers
- extended the utility-room wall beside the Service Key area so it overlaps the foyer divider instead of leaving a gap back toward the starting room
- added reusable non-ink item expiry for locked objects: an inventory item stays while any matching lock remains unopened, then auto-removes with feedback after the last matching lock opens
- added two Service Key supply chests to the interaction level so the Service Key is linked to three locks: the save-office door plus two chests
- updated `Wall_SaveOffice_Side_South` to the editor-tested position and size from the latest playtest screenshot
- added a stable dynamic box top-face contact classification for box-on-box support
- damped gentle box landings on stable lower-box top faces so the lower box absorbs less lateral shove and upward bounce is capped more tightly
- suppressed collision-spin injection for boxes contacting a stable supported lower-box top face, leaving the existing support/topple path to decide whether the upper box settles or tips
- changed stable box top-face detection to use actual top-face overlap instead of relying on the dynamic contact normal being mostly vertical

### Why

- continued box tuning was producing diminishing returns and slowing visible game progress
- the project needs a Resident Evil-inspired interaction loop to start feeling like the intended horror game rather than a physics lab
- `E` / controller `X` was approved as the shared context interaction button, with interactables taking priority over physics pickup
- the separate interaction test level keeps the existing physics test room intact while giving the key-door-save flow a focused place to evolve
- Resident Evil-style typewriter saves need a scarce resource, so ink ribbons are now tracked separately from key inventory
- the existing generated runtime level file could keep loading the old one-room test, so the interaction template refreshes when the expanded markers are missing
- a fast reset hotkey keeps playtesting friction low while the lock-and-key layout is still being iterated
- door blockers need to overlap the wall blockout slightly because exact-width seams can leave collision slits that the player capsule can squeeze through
- adjacent wall pieces in the blockout need the same overlap treatment as doors, otherwise diagonal/edge visibility can expose real navigation gaps
- Resident Evil-style keys often remain useful across multiple locks, so key expiry should be based on remaining authored locks rather than a hard-coded use counter
- ink ribbons are intentionally excluded from key expiry because they are consumable save resources, not reusable lock items
- boxes dropped onto other boxes could still enter a rotation loop because the dynamic contact solver kept adding impact spin before the floor-like support/topple logic could settle the pose
- the lower box's top face should behave closer to the floor or moving platform when it is already stable and strongly supported
- off-balance boxes should still fall over from support geometry, not from repeated artificial contact spin
- tilted or corner box-on-box contacts can produce diagonal manifold normals even when the upper box is resting on the lower box's top face, so the previous suppression could miss the exact cases that needed it

### Files

- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\World\SimpleLevel.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`

### Validation

- `Game` compile check succeeded with `--no-dependencies` into a temporary output folder
- `Game` compile check succeeded after top-face detection was decoupled from the dynamic contact normal
- `Game` compile check succeeded after adding the interaction test level and context interaction loop
- `Game` compile check succeeded after adding ink-ribbon saves, prototype save persistence, reset hotkey, and the expanded interaction layout
- `Game` compile check succeeded after closing the door/wall gaps in the generated interaction level
- `Game` compile check succeeded after extending the Service Key utility-room wall
- `Game` compile check succeeded after adding reusable key expiry and Service Key multi-lock test chests
- `Game` compile check succeeded after updating the save-office south wall placement
- `git diff --check` passed with only existing line-ending warnings
- in-game validation still needed for tilted drops, stacked-box release, and harder side throws onto a box stack
- in-game validation still needed for the expanded key route, inventory overlay, ink ribbon pickup, and typewriter save behavior

### Next

- run the interaction test level and validate `E` / `X` priority: key/door/save first, physics pickup second
- tune prompt visibility and interaction distances once the first playtest pass lands
- validate the expanded route in game: Rusted Key, Service Key, typewriter save with ink, Archive Key, and Archive door
- validate Service Key behavior: it should stay after opening the save-office door and first supply chest, then disappear after the final Service Key lock is opened
- decide whether the developer reset hotkey should stay keyboard-only or receive a controller debug chord after controller layout is chosen
- turn the hard-coded name-prefix interaction prototype into proper level-authored interaction components if the flow feels good
- re-test tilted drops onto a flat lower box and confirm the upper box no longer spins continuously around its corners
- check that real off-balance box drops still tip off naturally rather than being over-damped into place
- tune the stable top-face damping thresholds if hard throws feel too muted or gentle drops still bounce
- continue multi-box pile stability and mixed box/capsule stack tuning

## 2026-04-25

### Summary

- updated the handover docs to reflect the current game target, the current box-stack polish stage, and the latest stack-settling changes
- tuned dynamic box-on-box stability so dynamic contacts now merge back into retained support state instead of acting like one-frame impacts only
- added low-speed resting-contact damping and suppressed repeated collision spin for settled vertical box-on-box contacts
- biased dynamic contact separation so a strongly supported lower box gets disturbed less than the upper box resting on it
- promoted near-flat centered box-on-box support manifolds into face-support patches when the raw manifold was too sparse
- added a conservative low-speed face-support settle assist so broad stable support can flatten the last small visible tilt instead of freezing slightly crooked
- extended low-speed stack recognition from box-box only to support-like box/capsule contacts
- reduced disturbance of already-supported lower props in stacks by biasing correction and damping tiny residual lower-body motion during resting support contacts
- added a small box-stack and pile test cluster to `room01.json` with centered, offset, and loose-pile box arrangements
- fixed a pickup regression where grabbing the top box from a settled stack could inject fast spin into both the held box and the supporting box
- replaced the bad sparse-support escape assist with support classification that treats tilted one-point/two-point box contacts as unstable instead of stable rest
- tightened held-object vertical support contacts so carried boxes slide off stacks without friction-spinning the supported box underneath
- damped strongly-supported lower bodies during box-on-box support impacts so stack landings transfer less bounce and angular energy into the lower prop
- backed out a destabilizing box-box contact rejection experiment after it caused scene-wide contact jitter
- updated game-mode prop rendering to draw dynamic boxes and capsules from the same `Physics.Rotation` quaternion used by collision
- constrained the box-on-box flat-settle promotion so it only treats real broad contact patches as face support
- made one-point and two-point box support contacts unstable even when the box is near a cube-stable orientation
- clamped box-box contact manifold points to the supporting box face so dynamic box support patches cannot extend outside the lower box
- prevented edge/corner-supported boxes from being considered stack rest poses and added a small gravity-style topple nudge for sparse lowest-corner support
- added a dynamic box top-face support path so a flat, already-supported lower box can behave like a floor/platform for boxes resting above it
- fixed a startup crash in bottom-corner support detection by capping awkward tilted support patches to the four lowest candidate corners
- locked carried props to the orientation they had at pickup and stopped held-object contact spin/integration from rotating the carried object while the camera moves

### Why

- the md handover files needed to reflect the current project direction and latest physics tuning so a new chat can onboard quickly
- the top box in a clean drop onto another box was still trying to flip, drift onto corners, or freeze a few degrees off-flat
- the collision foundation is now strong enough that the highest-value work is tuning supported stacks and piles rather than adding another large physics rewrite
- mixed box/capsule piles were not getting the same low-speed support treatment as box-on-box contacts
- settled lower props in multi-body stacks could still absorb too much correction or tiny velocity from objects resting above them
- the level needed repeatable in-scene fixtures for validating clean stack settling, off-balance tipping, and small pile behavior
- the held-object dynamic contact path was still treating settled support contacts like fresh impacts, applying mass-split correction and collision spin to both bodies
- a corner contact directly under the box center could produce zero support lever, leaving the topple torque with no axis and allowing the box to perch on a point after being dropped
- the first attempt at fixing that used a pose-directed escape torque, which made pickup/drop behavior worse by reintroducing artificial spin
- carried boxes should still collide, but vertical support-like held contacts should not behave like high-friction moving shelves or impact impulses
- boxes landing on other boxes were still converting vertical support impacts into spin, especially when the lower box already had strong support from the floor or stack below
- the screenshot showed a visible gap/floating rest, which points toward box-box contact response or manifold projection over-separating before visible geometry touches
- dynamic box colliders are built from `Physics.Rotation`, so the likely fault is the approximate contact manifold/response rather than rotation not being passed into colliders
- rejecting suspected bad box-box contacts made contacts flicker frame-to-frame, which caused whole-scene shaking
- visible props were still being drawn by round-tripping physics rotation through `Transform.RotationEulerDeg`, so boxes could appear to snap or rest on a corner even when collision was using a different quaternion
- thrown and pickup-dropped boxes could still land on another box with only a corner or edge touching, then get promoted into fake full-face support and settle there
- sparse support contacts under a box center are physically unstable, so they should keep falling instead of being accepted as rest just because the box is near a valid face orientation
- floor and moving-platform support already behave better because they are static/world surfaces with bounded support; dynamic box-box contacts were projecting support points onto a plane without clipping them back to the lower box face
- a perfectly centered edge/corner contact can become an unstable mathematical equilibrium in the simplified solver, so it needs an explicit tiny topple bias instead of being damped as settled stack contact
- tilted boxes on top of other boxes were bouncing between stack-settle and sparse-topple rules; using the lower box's stable top face as a floor-like support surface makes box-on-box rest follow the same support path as floor/platform rest
- some tilted box poses can put more than four OBB corners within the support tolerance, but the helper only had a four-corner output buffer
- pickup only cleared angular velocity once, so the held-object update could still reintroduce rotation through world collision spin, dynamic-body contact spin, or normal angular integration

### Files

- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\ShapeCollision.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\bin\Debug\net8.0\Content\Levels\room01.json`

### Validation

- `Engine.Physics` build succeeded
- `room01.json` parsed successfully with 29 entities
- full `Game` build succeeded
- `git diff --check` passed, with only the existing line-ending warning for `HL2GameModule.cs`
- `Game` compile check succeeded with `--no-dependencies` into a temporary output folder while the running game kept the normal output DLLs locked
- `Engine.Physics` build succeeded
- `Game` compile check succeeded after the sparse support/topple change
- `Game` compile check succeeded after adding dynamic box top-face support
- `Game` compile check succeeded after fixing the bottom-corner overflow
- `Game` compile check succeeded after locking held-object orientation during pickup carry
- normal full `Game` build was blocked in this pass by the running `Game` process holding output DLLs open
- in-game validation improved the clean box-on-box resting case, but pile stability and mixed-stack behavior still need more testing

### Next

- re-test picking up a tilted/stacked box and turning the camera left/right to confirm the held object keeps its pickup orientation
- re-test corner-rest cases with the render path now using the same quaternion as collision
- re-test throwing and pickup-dropping boxes onto other boxes to confirm sparse corner/edge contacts topple instead of auto-correcting into corner rest
- re-test tilted drops onto a flat lower box and confirm the upper box settles like it does on the floor/moving platform instead of looping around corners
- tune multi-box pile stability and mixed box/capsule stacks
- reduce disturbance of already-supported lower props in piles and stacks
- re-test picking up the top box from the centered stack and confirm the lower boxes stay quiet
- re-test dropping carried boxes from awkward rotations and confirm tilted sparse support is unstable without causing new spin
- validate that clean box-on-box drops settle flat while genuine off-balance drops can still tip naturally
- validate mixed box/capsule piles in-game, especially low-speed capsule contacts so rolling is not over-damped
- continue keeping the handover docs current as the physics tuning moves into pile/polish work

## 2026-04-17

### Summary

- tuned box-stack stability so dynamic prop contacts now merge back into each body's retained contact/support state instead of only using world support manifolds
- added low-speed resting-contact damping for support-like dynamic-dynamic contacts so stacked props bleed tiny lateral slide instead of endlessly re-exciting
- suppressed repeated collision spin for low-speed vertical box-on-box resting contacts and biased separation to move the upper box more than a strongly supported lower box
- promoted near-flat centered box-on-box support manifolds into face-support patches so clean stacked drops are less likely to degrade into corner support
- added a conservative low-speed face-support settle assist for boxes so broad stable support can flatten out the last small resting tilt instead of freezing slightly crooked
- updated held-object dynamic contacts so the free body on the other side of the collision also retains the merged dynamic contact/support manifold
- increased the dynamic-dynamic solver loop from 2 to 3 iterations per substep for a more stable pile/stack settle
- added a `ContactManifold` type so collision can carry contact points instead of only normal plus penetration
- updated `ShapeCollision` to produce manifold data for box, sphere, and capsule pairs, including projected support patches for box-box contact and support-feature contacts for capsule-box contact
- updated `StaticCollision` to preserve both the strongest contact manifold and the best upward-facing support manifold during dynamic world resolution
- updated dynamic body step state so boxes, spheres, and capsules retain last contact and last support manifolds
- updated game-side angular response so box toppling prefers the support manifold, and collision spin uses manifold contact offsets before falling back to shape-normal heuristics
- updated dynamic-dynamic prop contact to retain the contact manifold and use its contact point for collision spin instead of the default fallback offset
- updated moving-platform support matching so dynamic bodies can identify a supporting platform from the last support manifold before falling back to the old center-over-AABB test
- updated held-object fixed update so held props resolve against static/kinematic world first, then use explicit manifold-based dynamic-body contacts to push and react against free props
- removed the held-object exclusion from collision spin so held props can pick up angular response from real contacts too
- consolidated runtime prop support lookups behind a shared support-state helper that prefers support manifolds and only falls back to surface AABB scans when contact data is unavailable
- adjusted player moving-platform carry timing so direct platform support is applied before `SourcePlayerMotor.Step(...)`, while the late carry remains for non-platform support bodies
- replaced the remaining player push and pickup ray interaction fallbacks with shape-aware checks so boxes use OBB ray hits, capsules use capsule ray hits, and player-vs-prop push/stand checks use actual shape contact instead of broad AABB overlap
- kept the older support-AABB path as a fallback where manifold support is not yet available
- added a stable face-support settle path for boxes so flat multi-point floor contacts damp angular motion instead of re-triggering floor jiggle after throws
- created a shape-aware `WorldCollider` representation that includes collider rotation
- added a `ShapeCollision` layer to resolve contacts using actual shape data rather than assuming everything is an AABB
- updated dynamic box and capsule stepping to pass runtime rotation into collision resolution
- updated runtime collider generation and dynamic-vs-dynamic contact to use the shape-aware collider path
- updated debug drawing so spheres and capsules display as spheres and capsules instead of box wireframes
- removed the visual-only capsule floor offset workaround after the requirement was clarified to be true rotated colliders, not presentation compensation
- updated held-object collision to resolve against dynamic bodies as well as the world
- changed support checks to use body center-of-mass projection rather than any-overlap support
- narrowed stable-pose settling so it only helps near-stable poses instead of steering large parts of a fall
- added a small topple assist for nearly-stationary supported boxes/capsules that are far from a stable pose
- replaced capsule/righting pose steering with gravity-like topple torque from the current support/contact patch
- fixed the inferred world-collision normal used for angular response so floor impacts no longer treat the floor normal backwards
- extended moving-platform carry support from box-only to box and capsule bodies
- biased box-box contact normal selection toward the more vertical axis in near-flat floor contacts
- started shifting the prop behavior toward VPhysics/Havok-style principles by using actual collision normals for angular response instead of guessed velocity-change normals
- removed the box pose-slerp cleanup so resting orientation is driven more by collision/support and less by authored stable targets
- reduced angular damping and increased gravity-topple torque so overbalanced boxes/capsules are more likely to keep falling instead of perching
- replaced box stability/topple logic based on whole-floor support AABBs with a support-polygon check built from the box's actual lowest support corners

### Why

- stacked boxes were still missing persistent support information when that support came from another dynamic prop rather than the world
- low-speed prop stacks were still relying too much on one-frame impulses and could keep slipping or lightly chattering even after the broader contact-manifold refactor
- dropping one box onto another could still make the top box continuously try to flip while the supported lower box drifted, because the solver was still treating settling support contacts too much like fresh impacts
- even after that, a clean dropped box could still end up on a corner because a sparse 2-point vertical manifold was being read as edge support instead of a broad face contact
- even with the promoted face-support patch, a box could still visually freeze a few degrees off-flat because the stable-support path only damped rotation and did not settle out the remaining tilt
- the next physics stage needs real contact points so support, toppling, and spin can come from contact data instead of guessed pivots and broad support AABBs
- the remaining corner-rest and capsule-support issues are now more about missing contact features than about basic collider shape mismatch
- flat thrown boxes were still jiggling on the floor because stable support contacts could keep feeding tiny topple/spin corrections instead of settling
- thrown props still needed manifold-driven prop-to-prop angular transfer so object impacts could feel more weighted and less like center-to-center impulses
- moving-platform carry was still using a separate support heuristic instead of the new support-contact data
- held objects were still using a special movement path that could collide, but did not fully participate in the same contact-driven push/reaction behavior as free dynamic props
- several runtime prop decisions were still reaching directly into `supportAabb`-based helpers instead of going through one consistent support-evaluation path
- the player was still only inheriting direct moving-platform motion after the motor step, which could reintroduce visible sliding on platforms
- player push/stand detection and pickup targeting were still using broad AABB assumptions even after the collider/runtime path had become shape-aware
- sphere and capsule collisions still looked and behaved like boxes
- debug visuals were misleading because they did not match the intended collider shapes
- capsules looked like they floated or stayed overly upright
- the project needs real collision fidelity progress, not temporary visual tricks, to get closer to the intended Half-Life 2 style physical feel
- carried props were not respecting dynamic-body collision
- platform support logic was too permissive, allowing unrealistic edge support
- rotation settling was visually overpowering gravity/toppling behavior
- some boxes could freeze in corner-balanced poses instead of falling onto a face
- capsules were still visibly trying to return to an upright or authored stable pose
- flat floor impacts could still inject incorrect angular cues because the inferred collision normal in the spin path was reversed
- capsules were not inheriting moving-platform motion because the platform carry path was still box-only
- the previous collision/settling path still relied too much on steering toward preferred orientations instead of contact-driven behavior
- box balancing decisions were still being made against the entire floor/platform support AABB instead of the actual corner/edge contact patch

### Files

- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\WorldCollider.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\ContactManifold.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\ShapeCollision.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\StaticCollision.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Dynamics\BoxBody.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Dynamics\CapsuleBody.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Dynamics\SphereBody.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Editor\Editor\LevelEditorController.cs`

### Validation

- `Engine.Physics` build succeeded
- full `Game` build is still blocked by unrelated existing workspace issues in `Engine.Editor`, so there is not yet a clean end-to-end build signal for this work

### Remaining Risks

- the new manifold layer is still approximate for several shape pairs and is not yet a full rigid-body contact manifold solver
- capsule support now carries support-feature points against boxes, but dynamic feel still needs in-game validation for side-rest and rolling cases
- moving-platform support and held-object behavior still use older support/collision heuristics in places and are not fully unified with the new manifold path yet
- rotated capsule versus box contact is still an approximation, not a full rigid-body manifold/contact solution
- some picking, support, or ray paths may still use AABB fallbacks or broadphase approximations
- gameplay feel still needs in-game validation, especially for capsules tipping, rolling, and settling
- held objects may now block against dynamic bodies without yet transferring satisfying push forces
- the new topple assist is still an approximation and may need tuning if it feels too weak or too directed
- gravity-topple behavior is still driven from simplified support patch estimation rather than a full contact manifold
- flat-floor box behavior may still need more tuning if the linear contact solver keeps choosing a marginally tilted response in edge cases
- validation is currently blocked by a local dotnet restore/build permission error in the temporary obj path, so this pass still needs in-game confirmation
- the new box support-polygon logic still assumes a horizontal support plane and should be validated on floors and moving platforms before extending it further

### Next

- validate box-on-floor, box-on-box, and edge-rest cases with the new support manifold path
- validate side-resting capsule contacts and moving-platform carry after the capsule support-feature manifold change
- replace more `supportAabb`-based runtime decisions with contact-manifold-derived support where possible
- validate the new manifold-driven dynamic-dynamic angular transfer on thrown box and box-stack impacts
- validate moving-platform carry/support using the new support-manifold matching path
- validate held-object contact behavior against walls, corners, dynamic props, and moving platforms
- validate the shared support-state path on free props, held props, and moving-platform-supported props so the remaining fallbacks can be reduced further
- validate pickup targeting on rotated boxes and capsules now that ray hits are shape-aware instead of AABB-based
- validate player push/stand behavior against rotated or tipped props now that the player interaction path is shape-aware
- test dropped boxes, edge contacts, and floor settling behavior
- test low-side capsule impacts and side-rest behavior
- audit remaining AABB fallbacks where exact rotated shape behavior matters
- improve support and settling logic if capsules or boxes still look unstable
- scope the standalone level editor as a first-class workflow instead of relying only on the runtime scene editor
- preserve the runtime in-scene editor as a tuning and playtest tool rather than replacing it
- test carrying an object into static geometry and into another dynamic body
- test a box hanging off a moving platform with its center over and then beyond the support area
- test thrown box-on-box impacts for remaining corner-freeze cases
- test a capsule tipped partly onto its side and confirm it now continues to fall instead of righting itself
- test a capsule resting on a moving platform and confirm it is carried with the platform
- test a flat box drop and confirm it no longer bounces up into a visible angled pose on clean floor contact
- test a thrown box-on-box impact and confirm the losing box now topples off corners instead of hanging in a diamond pose
- test a capsule on its side and confirm it no longer tries to stand upright after settling
- test single-corner and two-corner box rests and confirm `stable` now flips false when the COM projection is outside the actual support polygon

## Notes For Future Chats

When continuing work:

- read this file first
- then read `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- if you make meaningful progress, update this file before ending the session
