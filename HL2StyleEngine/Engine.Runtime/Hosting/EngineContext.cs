using Engine.Platform;
using Engine.Render;

namespace Engine.Runtime.Hosting;

public sealed class EngineContext
{
    public GameWindow Window { get; }
    public Renderer Renderer { get; }
    public ImGuiLayer? ImGui { get; }
    public Engine.Core.Diagnostics.LoadProfile? Loading { get; }

    public EngineContext(GameWindow window, Renderer renderer, ImGuiLayer? imGui = null,
        Engine.Core.Diagnostics.LoadProfile? loading = null)
    {
        Window = window;
        Renderer = renderer;
        ImGui = imGui;
        Loading = loading;
    }
}
