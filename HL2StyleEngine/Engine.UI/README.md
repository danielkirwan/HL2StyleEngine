# Engine.UI

`Engine.UI` is the gameplay UI integration point.

The gameplay UI supports a native RmlUi bridge and a managed Veldrid overlay renderer, with an ImGui fallback. Combat HUD overlays still deliberately use the stable ImGui path.

## Current Setup

- `GameplayUiLayer` is the game-facing UI layer.
- `GameplayUiState` is the data handoff from game logic into the gameplay UI layer.
- `RmlUiBackend` probes for a native bridge named `HS2RmlUiBridge`.
- `RmlUiFrameContext` carries input, renderer, viewport size, and frame time.
- `RmlUiNativeApi` binds the expected `HS2RmlUiBridge` C ABI at runtime.
- `RmlUiDocumentBuilder` generates a runtime RML document from the current gameplay HUD, ammo HUD, crosshair-centered weapon selector, loading overlay, inventory, pickup modal, prompt, and message state.
- `RmlUiOverlayRenderer` is the managed Veldrid-side consumer for native render commands.
- The first Veldrid consumer path creates dynamic vertex/index buffers, a fallback white texture, scissor-enabled pipeline state, vertex-color UI shaders, texture-id lookup, and indexed draw calls.
- Game RML/RCSS assets live under `Game/Content/UI` and are copied to the output folder.
- Runtime gameplay RML is generated into `Content/UI/Runtime/gameplay_ui.rml`.
- The ImGui preview renderer currently owns the gameplay HUD, ammo HUD, fallback crosshair, centered weapon selector, and loading overlay whenever those overlays are visible, even if native RmlUi presentation is enabled. This keeps combat UI stable while native RmlUi rendering is still being validated.

## Current Gameplay Overlay Choice

Combat-facing overlays currently use the ImGui preview renderer by design: health/suit, ammo, fallback crosshair, weapon selector, and loading overlay force preview rendering through `GameplayUiLayer.ShouldForcePreviewForState`. This avoids the native RmlUi text/layout issues seen in the weapon selector while the bridge is still being validated.

RmlUi should still be kept current for generated document coverage, but gameplay-combat HUD polish should be made in `GameplayUiImGuiPreviewRenderer` first until native RmlUi presentation can render the same layout reliably.

## Applied Gameplay UI Fixes

The weapon selector/HUD pass is paused again after these fixes:

- Ammo HUD initialization now loads an empty clip from reserve when an ammo weapon is equipped or already active, so clip/reserve values appear in the correct slots before the first shot.
- Health/suit and ammo HUD blocks now use the same translucent dark yellow/black background colors and yellow borders as the weapon-switching rectangles. HUD borders are inset by one pixel to avoid ImGui clipping on the top/left edges.

## Inventory Layout And Overflow (2026-10-05)

The inventory now uses thin continuous grids, existing PNG item thumbnails, compact counts and placement outlines over a darkened world. `InventoryLayout` supplies shared responsive geometry and canonical slot IDs for both native RML and `InventoryPreviewRenderer`: an 8-by-4 main grid and separate 8-by-4 overflow grid, side by side at 960 pixels and above, stacked below that width. The game handles stack/split/merge and returns overflow to world pickups on close; it also persists runtime drops. Weapons/ammo remain outside this inventory.

Native styles live in `Game/Content/UI/Inventory/grid.rcss`. The ImGui fallback receives a cached image resolver from `ImGuiLayer`; it does not change world texture loading. Grid hover permits another active cell during dragging so destination detection remains available. Mouse capture is reapplied only on UI mode transitions rather than clearing pointer movement every UI frame. Existing Combine interaction tests are retained, with overflow drag checks for both renderers. See `Game/PERSISTENCE_AND_INVENTORY.md` for behaviour, verification and the remaining human playtest checklist.

## Native Visibility Correction (2026-10-06)

The inventory's RCSS colours must use alpha in the 0..255 range, not browser CSS's 0..1 range. The October 5 styles made the backgrounds effectively transparent. `grid.rcss` now uses alpha 158 for the full-screen shade, 102 for cell fills, 138 for cell borders and 204 for item fills. See the [RmlUi colour syntax](https://mikke89.github.io/RmlUiDoc/pages/rcss/syntax.html).

`RmlUiDocumentBuilder.AppendInventory` explicitly sizes the block overlay to the current viewport because percentage sizing previously resolved to content-sized bounds. Keep this in step with the shared `InventoryLayout`. No native bridge, world material or combat HUD changes were required.

`Tools/LevelAuthoring` command `validate-inventory-visuals` inspects the packaged native bridge's render commands at three viewport sizes. It checks backdrop coverage and visible backgrounds/borders for every cell, beyond the existing hit-test/input checks. A live native fixture confirmed restored rendering; this does not replace a full manual inventory/controller playtest.

## Inventory Input Follow-Up (2026-09-08)

- Native inventory action clicks are handled after the current document has been rebuilt and hit-tested. Do not act on the previous frame's hovered row in keyboard navigation updates.
- Action-menu hover only changes selection on pointer activity; a stationary cursor must not block keyboard/controller navigation or E/X confirmation.
- Inventory grid selection ignores action-menu IDs, open action/split/discard overlays and the frame that executes an item action. Hovering another slot while the menu is open must not replace the Combine source.
- `InputState` preserves press and release edges independently, including a complete click within one frame. The fallback action menu accepts the press edge as well as ImGui activation. Inventory quick clicks finish their move/drop lifecycle in the same frame when appropriate.
- `Tools/LevelAuthoring` command `validate-inventory-input` checks native document hit tests and fallback ImGui draw/input handling without GPU submission. It covers Combine menu/target clicks, both cable ingredient orders, keyboard/controller selection, invalid targets, cancellation and ammo crafting.

## HS2Editor UI Authoring Target

The standalone `HS2Editor` app now has a first-pass UI manager for assets under `Content/UI`. It can create, open, edit, save, and source-preview `.rml` and `.rcss` files. A later milestone should replace the source/text preview with a real RmlUi visual preview, then add a visual layout canvas, selectable elements, property/style inspection, font/image asset picking, and live preview against sample gameplay UI state.
## Native Bridge Contract

The bridge exposes a small C ABI around RmlUi:

- initialize/shutdown RmlUi
- create and resize a context
- load `.rml` documents and `.rcss` stylesheets
- forward mouse, keyboard, text, and controller focus input
- expose render command buffers containing vertices, indices, texture ids, scissor rectangles, and translations
- optionally support `hs2_rmlui_set_document_body` so generated gameplay RML can refresh without unloading/reloading the document
- keep render command data valid until the managed side calls the release function

RmlUi rendering belongs in the overlay pass after the world has resolved to the swapchain and before ImGui debug/editor UI is rendered.

## Texture Support And Remaining Work

The bridge loads PNGs and exports texture data to the managed renderer, which uploads it to Veldrid. Inventory images now render in both native and fallback presentation. The current native font path remains a temporary pixel-font implementation; the editor still needs a true visual UI authoring preview. Unknown texture IDs retain a fallback white texture. Windows x64 bridge packaging is documented in `Native/HS2RmlUiBridge/README.md`.
