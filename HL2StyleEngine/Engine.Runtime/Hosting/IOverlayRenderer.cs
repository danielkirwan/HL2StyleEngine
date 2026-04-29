using Engine.Render;

namespace Engine.Runtime.Hosting;

public interface IOverlayRenderer
{
    /// <summary>Draw game-facing overlay UI after the world has resolved to the swapchain and before ImGui.</summary>
    void RenderOverlay(Renderer renderer);
}
