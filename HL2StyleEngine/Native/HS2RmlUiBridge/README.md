# HS2RmlUiBridge

This folder defines and implements the native C ABI expected by `Engine.UI`.

The bridge should wrap the C++ RmlUi library and export the functions declared in `HS2RmlUiBridge.h`. The managed side loads a native library named `HS2RmlUiBridge` and binds these exports at runtime.

## Current Responsibilities

- initialize and shut down RmlUi
- own the RmlUi context
- load `.rml` documents relative to the game `Content/UI` root
- refresh generated gameplay documents through `hs2_rmlui_set_document_body` when live inventory/prompt state changes
- forward input from `Engine.Input`
- call RmlUi update/render
- collect render geometry into `hs2_rmlui_render_data` so the managed Veldrid overlay renderer can draw it
- keep render data valid until `hs2_rmlui_release_render_data` is called
- load PNGs through `lodepng`
- export dirty texture data through `hs2_rmlui_get_texture_data` so managed Veldrid can own GPU texture upload
- provide a small temporary pixel-font fallback while the project decides on FreeType or a proper bitmap-font asset path

The managed Veldrid renderer consumes CPU-owned command buffers containing vertices, indices, texture ids, scissor rectangles, and per-command translation. Texture ownership stays on the managed renderer side for now, with the bridge only exporting RGBA image data and stable RmlUi texture ids.

## Current Gameplay Overlay Limitation

The native bridge is not currently trusted for the combat HUD. `GameplayUiLayer` forces health/suit, ammo, fallback crosshair, weapon selector, and loading overlay through the ImGui preview renderer while native RmlUi text/layout rendering is being validated.

Before switching those overlays back to native RmlUi, verify that RCSS positioning, borders/backgrounds, generated text images or font textures, and per-frame document refresh render identically to the ImGui gameplay overlay.