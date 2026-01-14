using Editor.Editor;
using Engine.Editor.Editor;
using Engine.Input;
using Engine.Input.Actions;
using Engine.Input.Devices;
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

    private BasicWorldRenderer _world = null!;

    private bool _editorEnabled;
    private bool _prevLeftMouseDown;

    private readonly LevelEditorController _editor = new();

    private float _editorMoveSpeed = 5f;
    private float _editorFastSpeed = 12f;
    private bool _mouseOverEditorUi;
    private bool _keyboardOverEditorUi;

    public InputState InputState => _inputState;

    public void Initialize(EngineContext context)
    {
        _ctx = context;

        _world = new BasicWorldRenderer(
            _ctx.Renderer.GraphicsDevice,
            _ctx.Renderer.WorldOutputDescription,
            shaderDirRelativeToApp: "Shaders");

        BuildActions();

        _ui = new UIModeController(_ctx.Window, _inputState, startInGameplay: true);

        _motor = new SourcePlayerMotor(_movement, startFeetPos: new Vector3(0, 0, -5f));

        string levelPath = Path.Combine(AppContext.BaseDirectory, "Content", "Levels", "room01.json");
        _editor.LoadOrCreate(levelPath, SimpleLevel.BuildRoom01File);

        ApplySpawnFromEditor(forceResetVelocity: true);
    }

    private void ApplySpawnFromEditor(bool forceResetVelocity)
    {
        if (_editor.TryGetPlayerSpawn(out Vector3 feetPos, out float yawDeg))
        {
            _motor.Position = feetPos;
            if (forceResetVelocity) _motor.Velocity = Vector3.Zero;

            _camera.Yaw = MathF.PI / 180f * yawDeg;
            _camera.Pitch = 0f;
        }

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

        if (_inputState.WasPressed(Key.F2))
        {
            _editorEnabled = !_editorEnabled;
            if (_editorEnabled) _ui.OpenUI();
        }

        if (_toggleUi.Pressed) _ui.ToggleUI();
        if (_forceGame.Pressed) _ui.CloseUI();

        if (_editorEnabled)
        {
            EditorCameraUpdate(dt);
            EditorMouseUpdate();
            EditorHotkeys();

            _wishDir = Vector3.Zero;
            _wishSpeed = 0f;
            _jumpPressedThisFrame = false;
            return;
        }

        // Gameplay camera + movement
        var io = ImGui.GetIO();
        bool uiWantsKeyboard = io.WantCaptureKeyboard;
        bool uiWantsMouse = io.WantCaptureMouse;

        if (_ui.IsMouseCaptured)
        {
            if (!uiWantsMouse)
                _camera.AddLook(_inputState.MouseDelta);

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
            if (_wishDir.LengthSquared() > 0.0001f) _wishDir = Vector3.Normalize(_wishDir);

            _wishSpeed = _movement.MaxSpeed;

            if (_jump.Pressed)
                _jumpPressedThisFrame = true;
        }
    }

    private void EditorHotkeys()
    {
        var io = ImGui.GetIO();
        if (io.WantCaptureKeyboard)
            return;

        bool ctrl = _inputState.IsDown(Key.ControlLeft) || _inputState.IsDown(Key.ControlRight);

        if (ctrl && _inputState.WasPressed(Key.D))
            _editor.DuplicateSelected();
    }

    private void EditorCameraUpdate(float dt)
    {
        bool uiWantsMouse = _mouseOverEditorUi;
        bool uiWantsKeyboard = _keyboardOverEditorUi;

        if (_inputState.RightMouseDown && !uiWantsMouse)
        {
            _camera.AddLook(_inputState.MouseDelta);
            float limit = 1.55334f;
            _camera.Pitch = Math.Clamp(_camera.Pitch, -limit, limit);
        }

        if (!uiWantsKeyboard)
        {
            float speed = (_inputState.IsDown(Key.ShiftLeft) || _inputState.IsDown(Key.ShiftRight))
                ? _editorFastSpeed
                : _editorMoveSpeed;

            Vector3 move = Vector3.Zero;

            if (_inputState.IsDown(Key.W)) move += _camera.Forward;
            if (_inputState.IsDown(Key.S)) move -= _camera.Forward;
            if (_inputState.IsDown(Key.D)) move += _camera.Right;
            if (_inputState.IsDown(Key.A)) move -= _camera.Right;

            if (_inputState.IsDown(Key.E)) move += Vector3.UnitY;
            if (_inputState.IsDown(Key.Q)) move -= Vector3.UnitY;

            if (move.LengthSquared() > 0.0001f)
            {
                move = Vector3.Normalize(move);
                _camera.Position += move * speed * dt;
            }
        }
    }

    private void EditorMouseUpdate()
    {
        bool uiWantsMouse = _mouseOverEditorUi;

        bool leftDown = _inputState.LeftMouseDown;
        bool leftPressed = leftDown && !_prevLeftMouseDown;
        bool leftReleased = !leftDown && _prevLeftMouseDown;

        bool ctrlDown = _inputState.IsDown(Key.ControlLeft) || _inputState.IsDown(Key.ControlRight);

        if (!uiWantsMouse)
        {
            if (leftPressed)
                _editor.OnMousePressed(GetMouseRay(), ctrlDown);

            if (leftDown)
                _editor.OnMouseHeld(GetMouseRay(), leftDown: true, ctrlDown);

            if (leftReleased)
                _editor.OnMouseReleased();
        }
        else
        {
            if (leftReleased)
                _editor.OnMouseReleased();
        }

        _prevLeftMouseDown = leftDown;
    }

    private EditorPicking.Ray GetMouseRay()
    {
        float w = _ctx.Window.Window.Width;
        float h = _ctx.Window.Window.Height;

        float aspect = h > 0 ? w / h : 16f / 9f;

        var view = Matrix4x4.CreateLookAt(
            _camera.Position,
            _camera.Position + _camera.Forward,
            Vector3.UnitY);

        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 3f, aspect, 0.05f, 500f);

        Vector2 mousePx = _inputState.MousePosition;

        return EditorPicking.ScreenPointToRay(mousePx, w, h, view, proj);
    }

    public void FixedUpdate(float fixedDt)
    {
        if (_editorEnabled)
            return;

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

        _motor.Step(fixedDt, _wishDir, _wishSpeed, _editor.SolidColliders);

        _editor.TickTriggers(_motor.Position);

        _camera.Position = _motor.Position + new Vector3(0, _movement.EyeHeight, 0);
    }

    public void DrawImGui()
    {
        _mouseOverEditorUi = false;
        _keyboardOverEditorUi = false;
        DrawMainDockspaceHost(); 

        DrawDebugWindow();

        if (_editorEnabled)
        {
            _editor.DrawToolbarPanel(ref _mouseOverEditorUi, ref _keyboardOverEditorUi);
            _editor.DrawHierarchyPanel(ref _mouseOverEditorUi, ref _keyboardOverEditorUi);
            _editor.DrawInspectorPanel(ref _mouseOverEditorUi, ref _keyboardOverEditorUi);
        }
    }


    private void DrawDebugWindow()
    {
        ImGui.Begin("Debug");
        ImGui.Text($"FPS: {_fps:F1}");
        ImGui.Text($"Editor: {(_editorEnabled ? "ON" : "OFF")}");
        ImGui.Text($"Dirty: {(_editor.Dirty ? "YES" : "NO")}");
        ImGui.Text($"Level: {_editor.LevelPath}");
        ImGui.Text($"Selected: {_editor.SelectedEntityIndex}");
        if (!string.IsNullOrWhiteSpace(_editor.LastTriggerEvent))
            ImGui.Text($"Last Trigger: {_editor.LastTriggerEvent}");
        ImGui.End();
    }

    private static void DrawMainDockspaceHost()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags hostFlags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoInputs |    
            ImGuiWindowFlags.MenuBar;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, 0); 

        ImGui.Begin("MainDockspaceHost", hostFlags);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);

        uint dockspaceId = ImGui.GetID("MainDockspace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero,
            ImGuiDockNodeFlags.PassthruCentralNode);
        ImGui.End();
    }




    public void RenderWorld(Renderer renderer)
    {
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

        for (int i = 0; i < _editor.DrawBoxes.Count; i++)
        {
            var d = _editor.DrawBoxes[i];

            Vector4 color = d.Color;
            if (_editorEnabled && i == _editor.SelectedEntityIndex)
                color = new Vector4(1f, 1f, 0.1f, 1f);

            DrawEditorBox(renderer, d.Position, d.Size, d.Rotation, color);
        }

        if (_editorEnabled && _editor.HasGizmo(out var xLine, out var xHandle,
                                              out var yLine, out var yHandle,
                                              out var zLine, out var zHandle))
        {
            DrawEditorBox(renderer, xLine.Position, xLine.Size, xLine.Rotation, xLine.Color);
            DrawEditorBox(renderer, xHandle.Position, xHandle.Size, xHandle.Rotation, xHandle.Color);

            DrawEditorBox(renderer, yLine.Position, yLine.Size, yLine.Rotation, yLine.Color);
            DrawEditorBox(renderer, yHandle.Position, yHandle.Size, yHandle.Rotation, yHandle.Color);

            DrawEditorBox(renderer, zLine.Position, zLine.Size, zLine.Rotation, zLine.Color);
            DrawEditorBox(renderer, zHandle.Position, zHandle.Size, zHandle.Rotation, zHandle.Color);
        }
    }

    private void DrawEditorBox(Renderer renderer, Vector3 pos, Vector3 size, Quaternion rot, Vector4 color)
    {
        var model =
            Matrix4x4.CreateScale(size) *
            Matrix4x4.CreateFromQuaternion(rot) *
            Matrix4x4.CreateTranslation(pos);

        _world.DrawBox(renderer.CommandList, model, color);
    }

    public void Dispose()
    {
        _world?.Dispose();
    }
}
