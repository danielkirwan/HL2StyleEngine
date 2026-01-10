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

    public EngineContext Context { get; }

    public EngineHost(int width, int height, string title)
    {
        _window = new GameWindow(width, height, title);
        Console.WriteLine("ImGui.NET loaded from: " + typeof(ImGuiNET.ImGui).Assembly.Location);
        Console.WriteLine("ImGui.NET version: " + typeof(ImGuiNET.ImGui).Assembly.GetName().Version);

        Console.WriteLine("Veldrid.ImGui loaded from: " + typeof(Veldrid.ImGuiRenderer).Assembly.Location);
        Console.WriteLine("Veldrid.ImGui version: " + typeof(Veldrid.ImGuiRenderer).Assembly.GetName().Version);

        // Create GraphicsDevice here (Runtime owns this, not Render)
        var gdOptions = new GraphicsDeviceOptions(
            debug: true,
            swapchainDepthFormat: null,
            syncToVerticalBlank: true);

        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(
            _window.Window,
            gdOptions,
            GraphicsBackend.Direct3D11); // Windows-only for now

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
            InputSnapshot snapshot = _window.Window.PumpEvents();
            if (!_window.Window.Exists) break;

            // dt
            double t = _sw.Elapsed.TotalSeconds;
            float dt = (float)(t - _prevTime);
            _prevTime = t;

            // Clamp huge dt (breakpoints/window drag)
            if (dt > 0.1f) dt = 0.1f;

            Time.DeltaTime = dt;

            // Per-frame update
            module.Update(dt, snapshot);

            // Fixed updates
            _fixed.Update(dt, () => module.FixedUpdate(Time.FixedDeltaTime));

            // ImGui update + game UI
            _imgui.Update(dt, snapshot);
            module.DrawImGui();

            // Render
            _renderer.BeginFrame();
            _imgui.Render(_renderer.GraphicsDevice, _renderer.CommandList);
            _renderer.EndFrame();
        }
    }

    public void Dispose()
    {
        _imgui.Dispose();
        _renderer.Dispose();
        // _graphicsDevice is disposed by Renderer.Dispose()
    }
}
