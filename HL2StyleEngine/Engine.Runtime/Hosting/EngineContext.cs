using Engine.Platform;
using Engine.Render;

namespace Engine.Runtime.Hosting;

public sealed class EngineContext
{
    public GameWindow Window { get; }
    public Renderer Renderer { get; }

    public EngineContext(GameWindow window, Renderer renderer)
    {
        Window = window;
        Renderer = renderer;
    }
}
