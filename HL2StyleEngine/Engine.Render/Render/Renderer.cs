using System;
using Veldrid;

namespace Engine.Render;

public sealed class Renderer : IDisposable
{
    public GraphicsDevice GraphicsDevice { get; }
    public ResourceFactory Factory => GraphicsDevice.ResourceFactory;
    public CommandList CommandList { get; }

    public Renderer(GraphicsDevice graphicsDevice)
    {
        GraphicsDevice = graphicsDevice;
        CommandList = Factory.CreateCommandList();
    }

    public void BeginFrame()
    {
        CommandList.Begin();

        // Use swapchain framebuffer (now includes depth)
        CommandList.SetFramebuffer(GraphicsDevice.MainSwapchain.Framebuffer);

        CommandList.ClearColorTarget(0, RgbaFloat.Black);

        // Clear depth (only valid because swapchain has a depth target now)
        CommandList.ClearDepthStencil(1f);
    }

    public void EndFrame()
    {
        CommandList.End();
        GraphicsDevice.SubmitCommands(CommandList);
        GraphicsDevice.SwapBuffers(GraphicsDevice.MainSwapchain);
        //GraphicsDevice.WaitForIdle(); //Add back for debugging
    }

    public void Dispose()
    {
        CommandList.Dispose();
        GraphicsDevice.Dispose();
    }
}
