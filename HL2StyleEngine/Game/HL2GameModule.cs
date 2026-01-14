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
    private readonly List<Aabb> _colliders = new();
    private readonly List<BoxInstance> _level = new();

    private bool _editorEnabled;
    private int _selectedBoxIndex = -1;
    private bool _levelDirty;

    private string _levelPath = "";
    private LevelFile _levelFile = null!;

    private bool _prevLeftMouseDown;
    private bool _prevRightMouseDown;

    private bool _dragging;
    private Vector3 _dragOffset;
    private float _dragPlaneY;

    private float _editorMoveSpeed = 5f;
    private float _editorFastSpeed = 12f;

    public InputState InputState => _inputState;

    public void Initialize(EngineContext context)
    {
        _ctx = context;

        _world = new BasicWorldRenderer(_ctx.Renderer.GraphicsDevice, _ctx.Renderer.WorldOutputDescription, shaderDirRelativeToApp: "Shaders");

        _levelPath = Path.Combine(AppContext.BaseDirectory, "Content", "Levels", "room01.json");
        _levelFile = LevelIO.LoadOrCreate(_levelPath, SimpleLevel.BuildRoom01File);
        RebuildRuntimeWorldFromLevelFile();

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

        if (_inputState.WasPressed(Key.F2))
        {
            _editorEnabled = !_editorEnabled;
            _dragging = false;

            if (_editorEnabled)
                _ui.OpenUI(); 
        }

        if (_toggleUi.Pressed) _ui.ToggleUI();
        if (_forceGame.Pressed) _ui.CloseUI();

        bool leftDown = _inputState.LeftMouseDown;
        bool rightDown = _inputState.RightMouseDown;

        bool leftPressed = leftDown && !_prevLeftMouseDown;
        bool leftReleased = !leftDown && _prevLeftMouseDown;

        if (_editorEnabled)
        {
            EditorUpdate(dt, leftDown, rightDown, leftPressed, leftReleased);

            _wishDir = Vector3.Zero;
            _wishSpeed = 0f;
            _jumpPressedThisFrame = false;

            _prevLeftMouseDown = leftDown;
            _prevRightMouseDown = rightDown;
            return;
        }

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

        _prevLeftMouseDown = leftDown;
        _prevRightMouseDown = rightDown;
    }

    private void EditorUpdate(float dt, bool leftDown, bool rightDown, bool leftPressed, bool leftReleased)
    {
        var io = ImGui.GetIO();
        bool uiWantsMouse = io.WantCaptureMouse;
        bool uiWantsKeyboard = io.WantCaptureKeyboard;

        if (rightDown && !uiWantsMouse)
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
        if (uiWantsMouse)
            return; 

        if (leftPressed)
        {
            if (TryPickBoxUnderMouse(out int hitIndex, out Vector3 hitPoint))
            {
                _selectedBoxIndex = hitIndex;

                _dragPlaneY = hitPoint.Y;

                Vector3 boxPos = _levelFile.Boxes[_selectedBoxIndex].Position;
                _dragOffset = hitPoint - boxPos;

                _dragging = true;
            }
            else
            {
                _selectedBoxIndex = -1;
                _dragging = false;
            }
        }

        if (_dragging && leftDown && _selectedBoxIndex >= 0)
        {
            if (TryGetMouseRay(out var ray))
            {
                if (EditorPicking.RayIntersectsPlane(ray, Vector3.UnitY, _dragPlaneY, out float t))
                {
                    Vector3 hit = ray.GetPoint(t);
                    Vector3 newPos = hit - _dragOffset;

                    var def = _levelFile.Boxes[_selectedBoxIndex];
                    def.Position = newPos;

                    _levelDirty = true;
                    ApplyBoxDefToRuntime(_selectedBoxIndex);
                }
            }
        }

        if (_dragging && leftReleased)
            _dragging = false;
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

        _motor.Step(fixedDt, _wishDir, _wishSpeed, _colliders);

        _camera.Position = _motor.Position + new Vector3(0, _movement.EyeHeight, 0);
    }

    public void DrawImGui()
    {
        DrawDebugWindow();

        if (_editorEnabled)
            DrawLevelEditorWindow();
    }

    private void DrawDebugWindow()
    {
        ImGui.Begin("Debug");
        ImGui.Separator();
        ImGui.Text($"FPS: {_fps:F1}");

        ImGui.Separator();
        ImGui.Text(_ui.IsUIOpen ? "UI MODE (cursor visible)" : "GAME MODE (mouse captured)");
        ImGui.Text("TAB: Toggle UI mode");
        ImGui.Text("F1: Force GAME mode");
        ImGui.Text("F2: Toggle EDITOR mode");

        ImGui.Separator();
        ImGui.Text($"Editor: {(_editorEnabled ? "ON" : "OFF")}  Dirty: {(_levelDirty ? "YES" : "NO")}");
        ImGui.Text($"Level: {_levelPath}");
        ImGui.Text($"Selected: {_selectedBoxIndex}");

        ImGui.End();
    }

    private void DrawLevelEditorWindow()
    {
        ImGui.Begin("Level Editor");

        if (ImGui.Button("Save"))
        {
            LevelIO.Save(_levelPath, _levelFile);
            _levelDirty = false;
        }
        ImGui.SameLine();

        if (ImGui.Button("Reload"))
        {
            _levelFile = LevelIO.Load(_levelPath);
            _levelDirty = false;
            _selectedBoxIndex = Math.Clamp(_selectedBoxIndex, -1, _levelFile.Boxes.Count - 1);
            RebuildRuntimeWorldFromLevelFile();
        }
        ImGui.SameLine();

        if (ImGui.Button("Add Box"))
        {
            _levelFile.Boxes.Add(new BoxDef
            {
                Name = $"Box_{_levelFile.Boxes.Count}",
                Position = new Vector3(0, 0.5f, 0),
                Size = new Vector3(1, 1, 1),
                Color = new Vector4(0.6f, 0.6f, 0.6f, 1f)
            });

            _selectedBoxIndex = _levelFile.Boxes.Count - 1;
            _levelDirty = true;
            RebuildRuntimeWorldFromLevelFile();
        }
        ImGui.SameLine();

        bool canDelete = _selectedBoxIndex >= 0 && _selectedBoxIndex < _levelFile.Boxes.Count;
        if (!canDelete) ImGui.BeginDisabled();
        if (ImGui.Button("Delete Selected"))
        {
            _levelFile.Boxes.RemoveAt(_selectedBoxIndex);
            _selectedBoxIndex = Math.Clamp(_selectedBoxIndex, -1, _levelFile.Boxes.Count - 1);
            _levelDirty = true;
            RebuildRuntimeWorldFromLevelFile();
        }
        if (!canDelete) ImGui.EndDisabled();

        ImGui.Separator();

        ImGui.BeginChild("box_list", new Vector2(260, 0), ImGuiChildFlags.Borders);

        for (int i = 0; i < _levelFile.Boxes.Count; i++)
        {
            var b = _levelFile.Boxes[i];
            bool selected = i == _selectedBoxIndex;
            string label = $"{i:00}  {b.Name}##{b.Id}";

            if (ImGui.Selectable(label, selected))
                _selectedBoxIndex = i;
        }

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("box_inspector", new Vector2(0, 0), ImGuiChildFlags.Borders);

        if (_selectedBoxIndex < 0 || _selectedBoxIndex >= _levelFile.Boxes.Count)
        {
            ImGui.Text("Select a box to edit.");
            ImGui.EndChild();
            ImGui.End();
            return;
        }

        var box = _levelFile.Boxes[_selectedBoxIndex];

        string name = box.Name ?? "";
        if (ImGui.InputText("Name", ref name, 128))
        {
            box.Name = name;
            MarkDirtyAndRebuild();
        }

        Vector3 pos = box.Position;
        if (ImGui.DragFloat3("Position", ref pos, 0.05f))
        {
            box.Position = pos;
            MarkDirtyAndApplySelected();
        }

        Vector3 size = box.Size;
        if (ImGui.DragFloat3("Size", ref size, 0.05f))
        {
            size.X = MathF.Max(0.01f, size.X);
            size.Y = MathF.Max(0.01f, size.Y);
            size.Z = MathF.Max(0.01f, size.Z);

            box.Size = size;
            MarkDirtyAndApplySelected();
        }

        Vector4 col = box.Color;
        if (ImGui.ColorEdit4("Color", ref col))
        {
            box.Color = col;
            MarkDirtyAndApplySelected();
        }

        ImGui.Separator();
        ImGui.Text($"Id: {box.Id}");
        ImGui.Text("Editor controls:");
        ImGui.BulletText("RMB: look around");
        ImGui.BulletText("WASD: move   Q/E: down/up   Shift: faster");
        ImGui.BulletText("LMB on box: select + drag");

        ImGui.EndChild();
        ImGui.End();
    }

    private void MarkDirtyAndRebuild()
    {
        _levelDirty = true;
        RebuildRuntimeWorldFromLevelFile();
    }

    private void MarkDirtyAndApplySelected()
    {
        _levelDirty = true;

        if (_selectedBoxIndex >= 0 && _selectedBoxIndex < _levelFile.Boxes.Count)
            ApplyBoxDefToRuntime(_selectedBoxIndex);
        else
            RebuildRuntimeWorldFromLevelFile();
    }

    private void RebuildRuntimeWorldFromLevelFile()
    {
        _level.Clear();
        _colliders.Clear();

        foreach (var def in _levelFile.Boxes)
        {
            var inst = def.ToBoxInstance();
            _level.Add(inst);

            var half = inst.Size * 0.5f;
            _colliders.Add(new Aabb(inst.Position - half, inst.Position + half));
        }
    }

    private void ApplyBoxDefToRuntime(int index)
    {
        if (index < 0 || index >= _levelFile.Boxes.Count)
            return;

        if (_level.Count != _levelFile.Boxes.Count || _colliders.Count != _levelFile.Boxes.Count)
        {
            RebuildRuntimeWorldFromLevelFile();
            return;
        }

        var inst = _levelFile.Boxes[index].ToBoxInstance();
        _level[index] = inst;

        var half = inst.Size * 0.5f;
        _colliders[index] = new Aabb(inst.Position - half, inst.Position + half);
    }

    private bool TryGetMouseRay(out EditorPicking.Ray ray)
    {
        ray = default;

        float w = _ctx.Window.Window.Width;
        float h = _ctx.Window.Window.Height;
        if (w <= 1 || h <= 1)
            return false;

        float aspect = h > 0 ? w / h : 16f / 9f;

        var view = Matrix4x4.CreateLookAt(
            _camera.Position,
            _camera.Position + _camera.Forward,
            Vector3.UnitY);

        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 3f, aspect, 0.05f, 500f);

        Vector2 mousePx = _inputState.MousePosition;

        ray = EditorPicking.ScreenPointToRay(mousePx, w, h, view, proj);
        return true;
    }

    private bool TryPickBoxUnderMouse(out int hitIndex, out Vector3 hitPoint)
    {
        hitIndex = -1;
        hitPoint = default;

        if (!TryGetMouseRay(out var ray))
            return false;

        float bestT = float.PositiveInfinity;
        int bestIndex = -1;

        for (int i = 0; i < _colliders.Count; i++)
        {
            Vector3 mn = _colliders[i].Min;
            Vector3 mx = _colliders[i].Max;

            if (EditorPicking.RayIntersectsAabb(ray, mn, mx, out float t))
            {
                if (t < bestT)
                {
                    bestT = t;
                    bestIndex = i;
                }
            }
        }

        if (bestIndex == -1)
            return false;

        hitIndex = bestIndex;
        hitPoint = ray.GetPoint(bestT);
        return true;
    }

    public void RenderWorld(Engine.Render.Renderer renderer)
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

        for (int i = 0; i < _level.Count; i++)
        {
            var b = _level[i];

            Vector4 color = b.Color;
            if (_editorEnabled && i == _selectedBoxIndex)
                color = new Vector4(1f, 1f, 0.1f, 1f);

            _world.DrawBox(renderer.CommandList, b.ModelMatrix, color);
        }
    }

    public void Dispose()
    {
        _world?.Dispose();
    }
}
