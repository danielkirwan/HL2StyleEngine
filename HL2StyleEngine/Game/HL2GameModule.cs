using Engine.Runtime.Hosting;
using ImGuiNET;
using Veldrid;
using Game.World;
using System.Collections.Generic;
using System.Numerics;
using System.Data.Common;


namespace Game;

public sealed class HL2GameModule : IGameModule, IWorldRenderer
{
    private EngineContext _ctx = null!;

    private readonly InputState _input = new();
    private readonly FpsCamera _camera = new(new System.Numerics.Vector3(0, 1.8f, -5f));
    private UIModeController _ui = null!;

    private readonly SourceMovementSettings _move = new();
    private SourcePlayerMotor _motor = null!;

    private System.Numerics.Vector3 _wishDir;
    private float _wishSpeed;
    private bool _jumpPressedThisFrame;

    private float _fps;
    private Engine.Render.BasicWorldRenderer _world = null!;

    private List<BoxInstance> _level = new();

    public void Initialize(EngineContext context)
    {
        _ctx = context;
        _world = new Engine.Render.BasicWorldRenderer(_ctx.Renderer.GraphicsDevice, shaderDirRelativeToApp: "Shaders");
        _level = SimpleLevel.BuildRoom01();

        _ui = new UIModeController(_ctx.Window, _input, startInGameplay: true);

        // Feet start at ground plane, camera is EyeHeight above
        _motor = new SourcePlayerMotor(_move, startFeetPos: new System.Numerics.Vector3(0, 0, -5f));
        _camera.Position = _motor.Position + new System.Numerics.Vector3(0, _move.EyeHeight, 0);

        _input.OverrideMouseDelta(System.Numerics.Vector2.Zero);
    }

    public void Update(float dt, InputSnapshot snapshot)
    {
        _fps = dt > 0 ? 1f / dt : 0f;

        _input.Update(snapshot);

        // UI toggle
        if (_input.WasPressed(Key.Tab))
            _ui.ToggleUI();

        if (_input.WasPressed(Key.F1))
            _ui.CloseUI();

        var io = ImGui.GetIO();
        bool uiWantsKeyboard = io.WantCaptureKeyboard;
        bool uiWantsMouse = io.WantCaptureMouse;

        if (_ui.IsMouseCaptured && !uiWantsMouse)
        {
            var md = _ctx.Window.ConsumeRelativeMouseDelta();
            _camera.AddLook(md);
        }


        _wishDir = System.Numerics.Vector3.Zero;
        _wishSpeed = 0f;

        if (_ui.IsMouseCaptured && !uiWantsKeyboard)
        {
            var forward = _camera.Forward;
            forward.Y = 0;
            forward = forward.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize(forward) : System.Numerics.Vector3.UnitZ;

            var right = _camera.Right;
            right.Y = 0;
            right = right.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize(right) : System.Numerics.Vector3.UnitX;

            if (_input.IsDown(Key.W)) _wishDir += forward;
            if (_input.IsDown(Key.S)) _wishDir -= forward;
            if (_input.IsDown(Key.D)) _wishDir += right;
            if (_input.IsDown(Key.A)) _wishDir -= right;

            if (_wishDir.LengthSquared() > 0.0001f)
                _wishDir = System.Numerics.Vector3.Normalize(_wishDir);

            _wishSpeed = _move.MaxSpeed;

            if (_input.WasPressed(Key.Space))
                _jumpPressedThisFrame = true;
        }
    }

    public void FixedUpdate(float fixedDt)
    {
        if (!_ui.IsMouseCaptured)
        {
            _jumpPressedThisFrame = false;
            return;
        }

        if (_jumpPressedThisFrame)
        {
            _motor.PressJump();
            _jumpPressedThisFrame = false;
        }

        _motor.Step(fixedDt, _wishDir, _wishSpeed);

        // Camera follows motor feet + eye height
        _camera.Position = _motor.Position + new System.Numerics.Vector3(0, _move.EyeHeight, 0);
    }

    public void DrawImGui()
    {
        float aspect = _ctx.Window.Window.Height > 0
            ? _ctx.Window.Window.Width / (float)_ctx.Window.Window.Height
            : 16f / 9f;



        ImGui.Begin("Debug");
        ImGui.Text($"Captured: {_ui.IsMouseCaptured}  WantMouse: {ImGui.GetIO().WantCaptureMouse}");

        ImGui.Text($"FPS: {_fps:F1}");

        ImGui.Separator();
        ImGui.Text(_ui.IsUIOpen ? "UI MODE (cursor visible)" : "GAME MODE (mouse captured)");
        ImGui.Text("TAB: Toggle UI mode");
        ImGui.Text("F1: Force GAME mode");

        ImGui.Separator();
        ImGui.Text($"Grounded: {_motor.Grounded}");
        ImGui.Text($"Feet Pos: {_motor.Position.X:F2}, {_motor.Position.Y:F2}, {_motor.Position.Z:F2}");
        ImGui.Text($"Vel: {_motor.Velocity.X:F2}, {_motor.Velocity.Y:F2}, {_motor.Velocity.Z:F2}");

        ImGui.Separator();
        ImGui.Text($"Yaw: {_camera.Yaw:F2} rad  Pitch: {_camera.Pitch:F2} rad");
        ImGui.End();
    }
    public void RenderWorld(Engine.Render.Renderer renderer)
    {
        if (_world is null)
            return;

        float aspect = _ctx.Window.Window.Height > 0
            ? _ctx.Window.Window.Width / (float)_ctx.Window.Window.Height
            : 16f / 9f;

        var view = Matrix4x4.CreateLookAt(_camera.Position,_camera.Position + _camera.Forward,Vector3.UnitY);

        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, aspect, 0.05f, 500f);
        var viewProj = view * proj;

        _world.BeginFrame();
        _world.UpdateCamera(viewProj);

        foreach (var b in _level)
        {
            _world.DrawBox(renderer.CommandList, b.ModelMatrix, b.Color);
        }
    }

    public void Dispose()
    {
        _world?.Dispose();
    }
}
