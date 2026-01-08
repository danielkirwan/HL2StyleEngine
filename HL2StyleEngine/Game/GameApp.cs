using Engine.Core.Time;
using Engine.Platform;
using Engine.Render;
using ImGuiNET;
using Veldrid;

namespace Game;

public sealed class GameApp : IDisposable
{
    private readonly GameWindow _window;
    private readonly Renderer _renderer;
    private readonly ImGuiRenderer _imgui;
    private readonly Engine.Core.Time.FixedTimestep _fixed = new();

    private float _fps;

    public GameApp()
    {
        _window = new GameWindow(1280, 720, "HL2-Style Engine (Starter)");
        _renderer = new Renderer(_window.Window);

        _imgui = new ImGuiRenderer(
            _renderer.GraphicsDevice,
            _renderer.GraphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
            _window.Window.Width,
            _window.Window.Height);
    }

    public void Run()
    {
        // Initialize timing
        _prevTime = _sw.Elapsed.TotalSeconds;

        while (_window.Window.Exists)
        {
            // Pump window events + get input snapshot for this frame
            var snapshot = _window.Window.PumpEvents();
            if (!_window.Window.Exists) break;

            RunOneFrame(snapshot);
        }
    }


    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    private double _prevTime = 0;

    private void RunOneFrame(Veldrid.InputSnapshot snapshot)
    {
        double t = _sw.Elapsed.TotalSeconds;
        float dt = (float)(t - _prevTime);
        _prevTime = t;

        // Clamp huge dt (breakpoints, window drag, etc.)
        if (dt > 0.1f) dt = 0.1f;

        Time.DeltaTime = dt;
        _fps = dt > 0 ? 1f / dt : 0;

        _fixed.Update(dt, FixedUpdate);

        // Update ImGui input + frame
        _imgui.Update(dt, snapshot);

        ImGui.Begin("Debug");
        ImGui.Text($"FPS: {_fps:F1}");
        ImGui.Text($"Time: {Time.TotalTime:F2}");
        ImGui.End();

        _renderer.BeginFrame();
        _imgui.Render(_renderer.GraphicsDevice, _renderer.CommandList);
        _renderer.EndFrame();
    }

    private void FixedUpdate()
    {
        // Physics + HL2 movement later
    }

    public void Dispose()
    {
        _imgui.Dispose();
        _renderer.Dispose();
    }
}
