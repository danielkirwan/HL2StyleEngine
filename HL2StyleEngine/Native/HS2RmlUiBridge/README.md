# HS2RmlUiBridge

This folder defines the native C ABI expected by `Engine.UI`.

The bridge should wrap the C++ RmlUi library and export the functions declared in `HS2RmlUiBridge.h`. The managed side loads a native library named `HS2RmlUiBridge` and binds these exports at runtime.

## Planned Responsibilities

- initialize and shut down RmlUi
- own the RmlUi context
- load `.rml` documents relative to the game `Content/UI` root
- refresh generated gameplay documents through `hs2_rmlui_set_document_body` when live inventory/prompt state changes
- forward input from `Engine.Input`
- call RmlUi update/render
- collect render geometry into `hs2_rmlui_render_data` so the managed Veldrid overlay renderer can draw it
- keep render data valid until `hs2_rmlui_release_render_data` is called

The managed Veldrid renderer already consumes CPU-owned command buffers containing vertices, indices, texture ids, scissor rectangles, and per-command translation. The first native implementation should focus on producing those buffers from RmlUi and supporting generated-document refresh. Texture upload and material binding can start with the managed fallback texture before font/image texture ownership is expanded.
