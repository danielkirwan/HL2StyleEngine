using System;
using System.Diagnostics;
using Engine.Core.Time;
using Engine.Platform;
using Engine.Render;
using Veldrid;
using Veldrid.StartupUtilities;

namespace Engine.Runtime.Hosting;

public sealed class EngineHost : IDisposable
{
    private readonly GameWindow _window;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Renderer _renderer;
    private readonly ImGuiLayer _imgui;
    private readonly FixedTimestep _fixed = new();

    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private double _prevTime;
    private int _lastW;
    private int _lastH;


    public EngineContext Context { get; }

    public EngineHost(int width, int height, string title)
    {
        _window = new GameWindow(width, height, title);
        Console.WriteLine("ImGui.NET loaded from: " + typeof(ImGuiNET.ImGui).Assembly.Location);
        Console.WriteLine("ImGui.NET version: " + typeof(ImGuiNET.ImGui).Assembly.GetName().Version);

        Console.WriteLine("Veldrid.ImGui loaded from: " + typeof(Veldrid.ImGuiRenderer).Assembly.Location);
        Console.WriteLine("Veldrid.ImGui version: " + typeof(Veldrid.ImGuiRenderer).Assembly.GetName().Version);
        _lastW = _window.Window.Width;
        _lastH = _window.Window.Height;


        var gdOptions = new GraphicsDeviceOptions(
         debug: true,
         swapchainDepthFormat: PixelFormat.R32_Float, 
         syncToVerticalBlank: true);

        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(
            _window.Window,
            gdOptions,
            GraphicsBackend.Direct3D11); 

        _renderer = new Renderer(_graphicsDevice);

        _imgui = new ImGuiLayer(
            _renderer.GraphicsDevice,
            _renderer.GraphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
            _window.Window.Width,
            _window.Window.Height);

        Context = new EngineContext(_window, _renderer);
    }

    public void Run(IGameModule module)
    {
        module.Initialize(Context);

        _prevTime = _sw.Elapsed.TotalSeconds;

        while (_window.Window.Exists)
        {
            InputSnapshot snapshot = _window.PumpEvents();

            if (module is IInputConsumer consumer)
            {
                if (consumer.InputState.RelativeMouseMode)
                    consumer.InputState.SetRelativeMouseDelta(_window.ConsumeRelativeMouseDelta());
            }



            int w = _window.Window.Width;
            int h = _window.Window.Height;

            if ((w != _lastW || h != _lastH) && w > 0 && h > 0)
            {
                _lastW = w;
                _lastH = h;

                _graphicsDevice.MainSwapchain.Resize((uint)w, (uint)h);
                _imgui.WindowResized(w, h);
            }


            if (!_window.Window.Exists) break;

            double t = _sw.Elapsed.TotalSeconds;
            float dt = (float)(t - _prevTime);
            _prevTime = t;

            if (dt > 0.1f) dt = 0.1f;

            Time.DeltaTime = dt;

            module.Update(dt, snapshot);

            _fixed.Update(dt, () => module.FixedUpdate(Time.FixedDeltaTime));

            _imgui.Update(dt, snapshot);
            module.DrawImGui();

            _renderer.BeginFrame();

            if (module is IWorldRenderer world)
            {
                world.RenderWorld(_renderer);
            }

            _imgui.Render(_renderer.GraphicsDevice, _renderer.CommandList);
            _renderer.EndFrame();

        }
    }

    public void Dispose()
    {
        _imgui.Dispose();
        _renderer.Dispose();
    }
}
