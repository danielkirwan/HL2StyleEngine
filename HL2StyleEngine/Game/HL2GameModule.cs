using Engine.Physics.Collision;
using Engine.Runtime.Hosting;
using Game.World;
using ImGuiNET;
using System.Numerics;
using Veldrid;

using Engine.Input;
using Engine.Input.Actions;
using Engine.Input.Devices;

namespace Game;

public sealed class HL2GameModule : IGameModule, IWorldRenderer
{
    private EngineContext _ctx = null!;

    private readonly InputState _inputState = new();
    private ActionMap _map = null!;
    private InputSystem _inputSystem = null!;

    private InputAction _toggleUi = null!;
    private InputAction _forceGame = null!;
    private InputAction _moveF = null!;
    private InputAction _moveB = null!;
    private InputAction _moveL = null!;
    private InputAction _moveR = null!;
    private InputAction _jump = null!;

    private readonly FpsCamera _camera = new(new Vector3(0, 1.8f, -5f));
    private UIModeController _ui = null!;

    private readonly SourceMovementSettings _move = new();
    private SourcePlayerMotor _motor = null!;

    private Vector3 _wishDir;
    private float _wishSpeed;
    private bool _jumpPressedThisFrame;

    private float _fps;

    private Engine.Render.BasicWorldRenderer _world = null!;
    private List<Aabb> _colliders = new();
    private List<BoxInstance> _level = new();

    public void Initialize(EngineContext context)
    {
        _ctx = context;

        _world = new Engine.Render.BasicWorldRenderer(_ctx.Renderer.GraphicsDevice, shaderDirRelativeToApp: "Shaders");
        _level = SimpleLevel.BuildRoom01();

        _colliders.Clear();
        foreach (var b in _level)
        {
            var half = b.Size * 0.5f;
            _colliders.Add(new Aabb(b.Position - half, b.Position + half));
        }

        BuildActions();

        _ui = new UIModeController(_ctx.Window, _inputSystem, startInGameplay: true);

        _motor = new SourcePlayerMotor(_move, startFeetPos: new Vector3(0, 0, -5f));
        _camera.Position = _motor.Position + new Vector3(0, _move.EyeHeight, 0);
    }

    private void BuildActions()
    {
        _map = new ActionMap();

        _toggleUi = _map.AddAction("ToggleUI");
        _forceGame = _map.AddAction("ForceGame");
        _moveF = _map.AddAction("MoveForward");
        _moveB = _map.AddAction("MoveBack");
        _moveL = _map.AddAction("MoveLeft");
        _moveR = _map.AddAction("MoveRight");
        _jump = _map.AddAction("Jump");

        _map.BindKey(_toggleUi, Key.Tab);
        _map.BindKey(_forceGame, Key.F1);

        _map.BindKey(_moveF, Key.W);
        _map.BindKey(_moveB, Key.S);
        _map.BindKey(_moveL, Key.A);
        _map.BindKey(_moveR, Key.D);

        _map.BindKey(_jump, Key.Space);

        _inputSystem = new InputSystem(_inputState, _map);
    }

    public void Update(float dt, InputSnapshot snapshot)
    {
        _fps = dt > 0 ? 1f / dt : 0f;

        _inputState.Update(snapshot);

        _inputSystem.Update();

        if (_toggleUi.Pressed)
            _ui.ToggleUI();

        if (_forceGame.Pressed)
            _ui.CloseUI();

        var io = ImGui.GetIO();
        bool uiWantsKeyboard = io.WantCaptureKeyboard;
        bool uiWantsMouse = io.WantCaptureMouse;

        if (_ui.IsMouseCaptured && !uiWantsMouse)
        {
            var md = _ctx.Window.ConsumeRelativeMouseDelta();
            _camera.AddLook(md);
        }

        _wishDir = Vector3.Zero;
        _wishSpeed = 0f;

        if (_ui.IsMouseCaptured && !uiWantsKeyboard)
        {
            var forward = _camera.Forward;
            forward.Y = 0;
            if (forward.LengthSquared() > 0) forward = Vector3.Normalize(forward);

            var right = _camera.Right;
            right.Y = 0;
            if (right.LengthSquared() > 0) right = Vector3.Normalize(right);

            if (_moveF.Down) _wishDir += forward;
            if (_moveB.Down) _wishDir -= forward;
            if (_moveR.Down) _wishDir += right;
            if (_moveL.Down) _wishDir -= right;
                
            if (_wishDir.LengthSquared() > 0.0001f)
                _wishDir = Vector3.Normalize(_wishDir);

            _wishSpeed = _move.MaxSpeed;

            if (_jump.Pressed)
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

        _motor.Step(fixedDt, _wishDir, _wishSpeed, _colliders);

        _camera.Position = _motor.Position + new Vector3(0, _move.EyeHeight, 0);
    }

    public void DrawImGui()
    {
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

        ImGui.Separator();
        ImGui.Text($"Actions: W({_moveF.Down}) A({_moveL.Down}) S({_moveB.Down}) D({_moveR.Down}) Jump({_jump.Pressed})");

        ImGui.End();
    }

    public void RenderWorld(Engine.Render.Renderer renderer)
    {
        if (_world is null)
            return;

        float aspect = _ctx.Window.Window.Height > 0
            ? _ctx.Window.Window.Width / (float)_ctx.Window.Window.Height
            : 16f / 9f;

        var view = Matrix4x4.CreateLookAt(
            _camera.Position,
            _camera.Position + _camera.Forward,
            Vector3.UnitY);

        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 3f, aspect, 0.05f, 500f);

        var viewProj = view * proj;

        _world.BeginFrame();
        _world.UpdateCamera(viewProj);

        foreach (var b in _level)
            _world.DrawBox(renderer.CommandList, b.ModelMatrix, b.Color);
    }

    public void Dispose()
    {
        _world?.Dispose();
    }
}
