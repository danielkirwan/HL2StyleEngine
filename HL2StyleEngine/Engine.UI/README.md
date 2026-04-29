# Engine.UI

`Engine.UI` is the gameplay UI integration point.

The intended final gameplay UI backend is RmlUi. RmlUi is a C++ library, so this project currently provides the managed seam and content workflow while the native bridge and Veldrid renderer are still pending.

## Current Setup

- `GameplayUiLayer` is the game-facing UI layer.
- `GameplayUiState` is the data handoff from game logic into the gameplay UI layer.
- `RmlUiBackend` probes for a native bridge named `HS2RmlUiBridge`.
- `RmlUiFrameContext` carries input, renderer, viewport size, and frame time.
- `RmlUiNativeApi` binds the expected `HS2RmlUiBridge` C ABI at runtime.
- `RmlUiDocumentBuilder` generates a runtime RML document from the current inventory, pickup modal, prompt, and message state.
- `RmlUiOverlayRenderer` is the managed Veldrid-side consumer for native render commands.
- The first Veldrid consumer path creates dynamic vertex/index buffers, a fallback white texture, scissor-enabled pipeline state, vertex-color UI shaders, texture-id lookup, and indexed draw calls.
- Game RML/RCSS assets live under `Game/Content/UI` and are copied to the output folder.
- Runtime gameplay RML is generated into `Content/UI/Runtime/gameplay_ui.rml`.

## Native Bridge Still Needed

The bridge should eventually expose a small C ABI around RmlUi:

- initialize/shutdown RmlUi
- create and resize a context
- load `.rml` documents and `.rcss` stylesheets
- forward mouse, keyboard, text, and controller focus input
- expose render command buffers containing vertices, indices, texture ids, scissor rectangles, and translations
- optionally support `hs2_rmlui_set_document_body` so generated gameplay RML can refresh without unloading/reloading the document
- keep render command data valid until the managed side calls the release function

RmlUi rendering belongs in the overlay pass after the world has resolved to the swapchain and before ImGui debug/editor UI is rendered.

## Current Rendering Limitation

The current managed draw path supports vertex color and texture-id lookup, but native font/image texture upload is not implemented yet. Unknown texture ids fall back to a white texture, so the next renderer slice should add bridge exports for texture creation/destruction and populate the texture registry from native RmlUi texture handles.
