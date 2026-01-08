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
    private readonly InputState _input = new();
    private readonly FpsCamera _camera = new(new System.Numerics.Vector3(0, 1.8f, -5f));
    private bool _mouseCaptured = false;

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
        _prevTime = _sw.Elapsed.TotalSeconds;

        while (_window.Window.Exists)
        {
            var snapshot = _window.Window.PumpEvents();
            if (!_window.Window.Exists) break;

            RunOneFrame(snapshot);
        }
    }



    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    private double _prevTime = 0;

    private void RunOneFrame(InputSnapshot snapshot)
    {
        double t = _sw.Elapsed.TotalSeconds;
        float dt = (float)(t - _prevTime);
        _prevTime = t;

        if (dt > 0.1f) dt = 0.1f;

        Time.DeltaTime = dt;
        _fps = dt > 0 ? 1f / dt : 0;

        _input.Update(snapshot);
        if (_input.WasPressed(Veldrid.Key.F1))
        {
            _mouseCaptured = !_mouseCaptured;
            _window.SetMouseCaptured(_mouseCaptured);

            _window.WarpMouseToCenter();
            _input.OverrideMouseDelta(System.Numerics.Vector2.Zero);
        }


        _fixed.Update(dt, FixedUpdate);

        _imgui.Update(dt, snapshot);
        var io = ImGuiNET.ImGui.GetIO();

        bool uiWantsKeyboard = io.WantCaptureKeyboard;
        bool uiWantsMouse = io.WantCaptureMouse;

        if (_mouseCaptured && !uiWantsMouse)
        {
            var center = _window.GetWindowCenter();
            var delta = _input.MousePosition - center; 

            _camera.AddLook(delta);

            _window.WarpMouseToCenter();

            _input.OverrideMouseDelta(System.Numerics.Vector2.Zero);
        }
        else if (!_mouseCaptured && !uiWantsMouse && _input.RightMouseDown)
        {
            _camera.AddLook(_input.MouseDelta);
        }

        if (!uiWantsKeyboard)
        {
            System.Numerics.Vector3 move = System.Numerics.Vector3.Zero;

            if (_input.IsDown(Veldrid.Key.W)) move += _camera.Forward;
            if (_input.IsDown(Veldrid.Key.S)) move -= _camera.Forward;
            if (_input.IsDown(Veldrid.Key.D)) move += _camera.Right;
            if (_input.IsDown(Veldrid.Key.A)) move -= _camera.Right;

            if (_input.IsDown(Veldrid.Key.Space)) move += _camera.Up;
            if (_input.IsDown(Veldrid.Key.ControlLeft) || _input.IsDown(Veldrid.Key.ControlRight)) move -= _camera.Up;

            _camera.Move(move, dt);
        }

        ImGuiNET.ImGui.Begin("Debug");
        ImGuiNET.ImGui.Text($"FPS: {_fps:F1}");
        ImGuiNET.ImGui.Text($"Time: {Engine.Core.Time.Time.TotalTime:F2}");

        ImGuiNET.ImGui.Separator();
        ImGuiNET.ImGui.Text("Hold RMB to look");
        ImGuiNET.ImGui.Text("WASD to move, Space/Ctrl up/down");

        ImGuiNET.ImGui.Separator();
        ImGuiNET.ImGui.Text($"Cam Pos: {_camera.Position.X:F2}, {_camera.Position.Y:F2}, {_camera.Position.Z:F2}");
        ImGuiNET.ImGui.Text($"Yaw: {_camera.Yaw:F2} rad  Pitch: {_camera.Pitch:F2} rad");
        ImGuiNET.ImGui.End();

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
