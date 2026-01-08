using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Engine.Render;

public sealed class Renderer : IDisposable
{
    public GraphicsDevice GraphicsDevice { get; }
    public CommandList CommandList { get; }

    public Renderer(Sdl2Window window)
    {
        GraphicsDevice = VeldridStartup.CreateGraphicsDevice(
            window,
            new GraphicsDeviceOptions(
                debug: true,
                swapchainDepthFormat: null,
                syncToVerticalBlank: true));

        CommandList = GraphicsDevice.ResourceFactory.CreateCommandList();
    }

    public void BeginFrame()
    {
        CommandList.Begin();
        CommandList.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
        CommandList.ClearColorTarget(0, RgbaFloat.Black);
    }

    public void EndFrame()
    {
        CommandList.End();
        GraphicsDevice.SubmitCommands(CommandList);
        GraphicsDevice.SwapBuffers();
    }

    public void Dispose()
    {
        CommandList.Dispose();
        GraphicsDevice.Dispose();
    }
}
