using Engine.Platform;
using Engine.Render;

namespace Engine.Runtime.Hosting;

public sealed class EngineContext
{
    public GameWindow Window { get; }
    public Renderer Renderer { get; }
    public ImGuiLayer? ImGui { get; }

    public EngineContext(GameWindow window, Renderer renderer, ImGuiLayer? imGui = null)
    {
        Window = window;
        Renderer = renderer;
        ImGui = imGui;
    }
}
