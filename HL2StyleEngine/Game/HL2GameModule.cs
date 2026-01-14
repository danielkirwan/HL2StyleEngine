using Engine.Input;
using Engine.Input.Actions;
using Engine.Input.Devices;
using Engine.Physics.Collision;
using Engine.Render;
using Engine.Runtime.Hosting;
using Game.World;
using ImGuiNET;
using System.Numerics;
using Veldrid;

namespace Game;

public sealed class HL2GameModule : IGameModule, IWorldRenderer, IInputConsumer
{
    private EngineContext _ctx = null!;

    private readonly InputState _inputState = new();
    private ActionMap _map = null!;
    private InputSystem _inputSystem = null!;

    private InputAction _toggleUi = null!;
    private InputAction _forceGame = null!;
    private InputAction _jump = null!;
    private InputAction _move = null!; 
    private InputAction _look = null!;

    private readonly FpsCamera _camera = new(new Vector3(0, 1.8f, -5f));
    private UIModeController _ui = null!;

    private readonly SourceMovementSettings _movement = new();
    private SourcePlayerMotor _motor = null!;

    private Vector3 _wishDir;
    private float _wishSpeed;
    private bool _jumpPressedThisFrame;

    private float _fps;

    private Engine.Render.BasicWorldRenderer _world = null!;
    private List<Aabb> _colliders = new();
    private List<BoxInstance> _level = new();

    public InputState InputState => _inputState;

    public void Initialize(EngineContext context)
    {
        _ctx = context;

        _world = new BasicWorldRenderer(_ctx.Renderer.GraphicsDevice, _ctx.Renderer.WorldOutputDescription, shaderDirRelativeToApp: "Shaders");

        _level = SimpleLevel.BuildRoom01();

        _colliders.Clear();
        foreach (var b in _level)
        {
            var half = b.Size * 0.5f;
            _colliders.Add(new Aabb(b.Position - half, b.Position + half));
        }

        BuildActions();

        _ui = new UIModeController(_ctx.Window, _inputState, startInGameplay: true);

        _motor = new SourcePlayerMotor(_movement, startFeetPos: new Vector3(0, 0, -5f));
        _camera.Position = _motor.Position + new Vector3(0, _movement.EyeHeight, 0);
    }

    private void BuildActions()
    {
        _map = new ActionMap();

        _toggleUi = _map.AddAction("ToggleUI");
        _forceGame = _map.AddAction("ForceGame");
        _jump = _map.AddAction("Jump");

        _move = _map.AddAction("Move"); 
        _look = _map.AddAction("Look"); 

        _map.BindKey(_toggleUi, Key.Tab);
        _map.BindKey(_forceGame, Key.F1);
        _map.BindKey(_jump, Key.Space);

        _map.BindGamepadButton(_toggleUi, GamepadButton.Start);
        _map.BindGamepadButton(_jump, GamepadButton.A);

        _map.BindGamepadStick(_move, GamepadStick.Left, deadzone: 0.2f, scale: 1f, invertY: true);
        _map.BindGamepadStick(_look, GamepadStick.Right, deadzone: 0.2f, scale: 1f, invertY: false);

        _inputSystem = new InputSystem(_inputState, _map);
    }

    public void Update(float dt, InputSnapshot snapshot)
    {
        _fps = dt > 0 ? 1f / dt : 0f;

        _inputState.Update(snapshot);
        _inputSystem.Update();

        if (_toggleUi.Pressed) _ui.ToggleUI();
        if (_forceGame.Pressed) _ui.CloseUI();

        var io = ImGui.GetIO();
        bool uiWantsKeyboard = io.WantCaptureKeyboard;
        bool uiWantsMouse = io.WantCaptureMouse;

        if (_ui.IsMouseCaptured)
        {
            if (!uiWantsMouse)
            {
                _camera.AddLook(_inputState.MouseDelta);
            }

            Vector2 stick = _look.Value2D; 
            const float lookRadPerSec = 3.0f; 
            _camera.Yaw -= stick.X * lookRadPerSec * dt;
            _camera.Pitch -= stick.Y * lookRadPerSec * dt;

            float limit = 1.55334f;
            _camera.Pitch = Math.Clamp(_camera.Pitch, -limit, limit);
        }

        _wishDir = Vector3.Zero;
        _wishSpeed = 0f;

        if (_ui.IsMouseCaptured && !uiWantsKeyboard)
        {
            Vector2 move2 = Vector2.Zero;

            if (_inputState.ActiveDevice == ActiveInputDevice.Gamepad)
            {
                move2 = _move.Value2D; 
            }
            else
            {
                if (_inputState.IsDown(Key.W)) move2.Y += 1f;
                if (_inputState.IsDown(Key.S)) move2.Y -= 1f;
                if (_inputState.IsDown(Key.D)) move2.X += 1f;
                if (_inputState.IsDown(Key.A)) move2.X -= 1f;

                if (move2.LengthSquared() > 1f) move2 = Vector2.Normalize(move2);
            }

            var forward = _camera.Forward; forward.Y = 0;
            if (forward.LengthSquared() > 0) forward = Vector3.Normalize(forward);

            var right = _camera.Right; right.Y = 0;
            if (right.LengthSquared() > 0) right = Vector3.Normalize(right);

            _wishDir = forward * move2.Y + right * move2.X;

            if (_wishDir.LengthSquared() > 0.0001f)
                _wishDir = Vector3.Normalize(_wishDir);

            _wishSpeed = _movement.MaxSpeed;

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

        _camera.Position = _motor.Position + new Vector3(0, _movement.EyeHeight, 0);
    }

    public void DrawImGui()
    {
        ImGui.Begin("Debug");
        ImGui.Separator();
        ImGui.Text($"HasGamepad: {_inputState.HasGamepad}");
        ImGui.Text($"Pad LeftStick raw: {_inputState.GetStick(Engine.Input.Actions.GamepadStick.Left, invertY: false)}");
        ImGui.Text($"Pad RightStick raw: {_inputState.GetStick(Engine.Input.Actions.GamepadStick.Right, invertY: false)}");
        ImGui.Text($"Axes: LX={_inputState.GetAxis(Engine.Input.Actions.GamepadAxis.LeftX):F2} LY={_inputState.GetAxis(Engine.Input.Actions.GamepadAxis.LeftY):F2} RX={_inputState.GetAxis(Engine.Input.Actions.GamepadAxis.RightX):F2} RY={_inputState.GetAxis(Engine.Input.Actions.GamepadAxis.RightY):F2}");


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
        ImGui.Text($"HasGamepad: {_inputState.HasGamepad}");
        ImGui.Text($"ActiveDevice: {_inputState.ActiveDevice}");
        ImGui.Text($"LeftStick: {_inputState.GetStick(GamepadStick.Left, invertY: true)} RightStick: {_inputState.GetStick(GamepadStick.Right, invertY: true)}");

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
