using Engine.Input.Devices;
using Engine.Render;

namespace Engine.UI;

public readonly record struct RmlUiFrameContext(
    Renderer Renderer,
    InputState Input,
    int ViewportWidth,
    int ViewportHeight,
    float DeltaTime);
