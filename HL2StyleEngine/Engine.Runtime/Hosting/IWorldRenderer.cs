using Engine.Render;

namespace Engine.Runtime.Hosting;

public interface IWorldRenderer
{
    /// <summary>
    /// Called every frame during rendering, after BeginFrame, before ImGui.
    /// Use renderer.CommandList to record draw calls.
    /// </summary>
    void RenderWorld(Renderer renderer);
}
