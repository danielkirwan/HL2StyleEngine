using Editor.Editor;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Input;
using Engine.Input.Actions;
using Engine.Input.Devices;
using Engine.Physics.Collision;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Entities.Interfaces;
using Engine.Runtime.Hosting;
using Game.World;
using Game.World.MovingPlatform;
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
    private readonly ScriptRegistry _scriptRegistry = new();
    private readonly List<Entity> _runtimeEntities = new();

    private readonly List<Aabb> _runtimeWorldColliders = new();
    private readonly Dictionary<string, LevelEntityDef> _defById = new();

    private sealed class RuntimeRenderBox
    {
        public Entity Entity = null!;
        public Vector3 Size;        
        public Vector4 Color;
        public Vector3 Delta;
        public Vector3 PrevPos;
        public bool IsMovingPlatform;
        public bool IsSolid;
        public bool IsRigidBody;
    }
    private readonly List<RuntimeRenderBox> _runtimeRenderBoxes = new();

    private bool _runtimeDirty = true; 

    public InputState InputState => _inputState;

    public void Initialize(EngineContext context)
    {
        _ctx = context;

        _world = new BasicWorldRenderer(
            _ctx.Renderer.GraphicsDevice,
            _ctx.Renderer.WorldOutputDescription,
            shaderDirRelativeToApp: "Shaders");

        BuildActions();
        _scriptRegistry.Register<MovingPlatformParams>("MovingPlatform",e => new MovingPlatform(e));
        _editor.SetScriptRegistry(_scriptRegistry);
        _ui = new UIModeController(_ctx.Window, _inputState, startInGameplay: true);

        _motor = new SourcePlayerMotor(_movement, startFeetPos: new Vector3(0, 0, -5f));

        string levelPath = Path.Combine(AppContext.BaseDirectory, "Content", "Levels", "room01.json");
        _editor.LoadOrCreate(levelPath, SimpleLevel.BuildRoom01File);
        RebuildRuntimeWorld();
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

            if (!_editorEnabled)
                _runtimeDirty = true;
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

        if (_editorEnabled && _editor.Dirty)
        {
            RebuildRuntimeWorld();
            _runtimeDirty = false;
        }

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

    private bool TryGetGroundPlatformDelta(out Vector3 delta)
    {
        delta = Vector3.Zero;

        Vector3 extents = new Vector3(_motor.Radius, _motor.HalfHeight, _motor.Radius);
        Vector3 center = _motor.Position + new Vector3(0f, _motor.HalfHeight, 0f);
        float feetY = center.Y - extents.Y;

        // If we’re going up, don’t “stick”
        if (_motor.Velocity.Y > 0.001f)
            return false;

        const float yTolerance = 0.18f; // thin platform => larger tolerance

        for (int i = 0; i < _runtimeRenderBoxes.Count; i++)
        {
            var rr = _runtimeRenderBoxes[i];
            if (!rr.IsMovingPlatform) continue;

            Vector3 p = rr.Entity.Transform.Position;
            Vector3 half = rr.Size * 0.5f;
            float topY = p.Y + half.Y;

            // Feet must be close to top surface
            if (feetY < topY - yTolerance || feetY > topY + yTolerance)
                continue;

            bool overlapX = (center.X + extents.X) >= (p.X - half.X) && (center.X - extents.X) <= (p.X + half.X);
            bool overlapZ = (center.Z + extents.Z) >= (p.Z - half.Z) && (center.Z - extents.Z) <= (p.Z + half.Z);

            if (!overlapX || !overlapZ)
                continue;

            delta = rr.Delta;
            return true;
        }

        return false;
    }

    private void RebuildRuntimeWorld()
    {
        _runtimeEntities.Clear();
        _runtimeRenderBoxes.Clear();
        _runtimeWorldColliders.Clear();
        _defById.Clear();

        foreach (var def in _editor.LevelFile.Entities)
            _defById[def.Id] = def;

        foreach (var def in _editor.LevelFile.Entities)
        {
            var e = new Entity(def.Id, def.Type, def.Name ?? def.Type);

            e.Transform.Position = def.LocalPosition;
            e.Transform.RotationEulerDeg = def.LocalRotationEulerDeg;
            e.Transform.Scale = def.LocalScale;

            e.BoxSize = (Vector3)def.Size;
            e.BoxColor = (Vector4)def.Color;

            // Attach scripts
            foreach (var s in def.Scripts)
            {
                if (_scriptRegistry.TryCreate(s.Type, e, out var comp))
                {
                    if (comp is IComponentWithJson jsonComp)
                        jsonComp.ApplyJson(s.Json);

                    e.Components.Add(comp);
                }
            }

            // -----------------------------
            // Create a dynamic body ONLY for pickup boxes (for now)
            // -----------------------------
            if (def.Type == EntityTypes.Box && def.CanPickUp)
            {
                // AABB is center-based, your Transform.Position is also treated as center in rendering.
                // So body.Center = entity position.
                Vector3 scale = (Vector3)def.LocalScale;
                Vector3 size = (Vector3)def.Size;

                size.X = MathF.Max(0.01f, size.X);
                size.Y = MathF.Max(0.01f, size.Y);
                size.Z = MathF.Max(0.01f, size.Z);

                scale.X = MathF.Max(0.01f, scale.X);
                scale.Y = MathF.Max(0.01f, scale.Y);
                scale.Z = MathF.Max(0.01f, scale.Z);

                Vector3 worldSize = Mul(size, scale);
                Vector3 half = worldSize * 0.5f;

                e.Body = new Engine.Physics.Dynamics.BoxBody(e.Transform.Position, half)
                {
                    Mass = def.Mass,
                    UseGravity = true,
                    IsKinematic = false
                };
            }
            else
            {
                e.Body = null;
            }

            _runtimeEntities.Add(e);

            // -----------------------------
            // Render list (what you draw as boxes)
            // -----------------------------
            bool isBoxLike =
                def.Type == EntityTypes.Box ||
                def.Type == EntityTypes.RigidBody;

            bool hasMovingPlatform =
                def.Scripts.Any(sd => sd.Type == "MovingPlatform");

            if (isBoxLike || hasMovingPlatform)
            {
                Vector3 size = (Vector3)def.Size;
                Vector3 scale = (Vector3)def.LocalScale;

                size.X = MathF.Max(0.01f, size.X);
                size.Y = MathF.Max(0.01f, size.Y);
                size.Z = MathF.Max(0.01f, size.Z);

                scale.X = MathF.Max(0.01f, scale.X);
                scale.Y = MathF.Max(0.01f, scale.Y);
                scale.Z = MathF.Max(0.01f, scale.Z);

                var rr = new RuntimeRenderBox
                {
                    Entity = e,
                    Size = Mul(size, scale),
                    Color = hasMovingPlatform ? new Vector4(1f, 0.2f, 1f, 1f) : (Vector4)def.Color,
                    IsMovingPlatform = hasMovingPlatform,
                    PrevPos = e.Transform.Position,
                    Delta = Vector3.Zero
                };
                _runtimeRenderBoxes.Add(rr);
            }
        }
    }



    private void BuildRuntimeCollidersThisFrame(bool includeDynamicBodies)
    {
        _runtimeWorldColliders.Clear();

        for (int i = 0; i < _runtimeRenderBoxes.Count; i++)
        {
            var rr = _runtimeRenderBoxes[i];

            // If it's a dynamic pickup box, do NOT add it to static world colliders.
            if (!includeDynamicBodies && rr.Entity.Body != null)
                continue;

            Vector3 pos = rr.Entity.Transform.Position;
            Vector3 half = rr.Size * 0.5f;

            _runtimeWorldColliders.Add(new Engine.Physics.Collision.Aabb(pos - half, pos + half));
        }
    }



    private void SnapshotPlatformPrevPositions()
    {
        for (int i = 0; i < _runtimeRenderBoxes.Count; i++)
        {
            var rr = _runtimeRenderBoxes[i];
            if (!rr.IsMovingPlatform) continue;
            rr.PrevPos = rr.Entity.Transform.Position;
        }
    }

    private void ComputePlatformDeltasFromPrev()
    {
        for (int i = 0; i < _runtimeRenderBoxes.Count; i++)
        {
            var rr = _runtimeRenderBoxes[i];
            if (!rr.IsMovingPlatform) continue;

            Vector3 cur = rr.Entity.Transform.Position;
            rr.Delta = cur - rr.PrevPos;
        }
    }


    private static Vector3 Mul(Vector3 a, Vector3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    private void EditorHotkeys()
    {
        var io = ImGui.GetIO();
        if (io.WantCaptureKeyboard)
            return;

        bool ctrl = _inputState.IsDown(Key.ControlLeft) || _inputState.IsDown(Key.ControlRight);
        bool shift = _inputState.IsDown(Key.ShiftLeft) || _inputState.IsDown(Key.ShiftRight);

        if (ctrl && _inputState.WasPressed(Key.D))
            _editor.DuplicateSelected();

        if (ctrl && _inputState.WasPressed(Key.Z))
        {
            if (shift) _editor.Redo();
            else _editor.Undo();
        }

        if (ctrl && _inputState.WasPressed(Key.Y))
            _editor.Redo();

        if (_inputState.WasPressed(Key.F) && _editor.SelectedEntityIndex >= 0)
            _editor.RequestFrameSelection();
    }


    private void EditorCameraUpdate(float dt)
    {
        var io = ImGui.GetIO();
        bool uiWantsMouse = io.WantCaptureMouse;
        bool uiWantsKeyboard = io.WantCaptureKeyboard;

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
        var io = ImGui.GetIO();
        bool uiWantsMouse = io.WantCaptureMouse;

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

        // 1) Platforms: snapshot prev positions (fixed step)
        SnapshotPlatformPrevPositions();

        // 2) Update components in FIXED time (moving platforms should be deterministic)
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var ent = _runtimeEntities[i];
            for (int j = 0; j < ent.Components.Count; j++)
                ent.Components[j].Update(fixedDt);
        }

        // 3) Compute platform deltas from prev -> current (for carry)
        ComputePlatformDeltasFromPrev();

        // 4) Build world colliders EXCLUDING dynamic bodies
        BuildRuntimeCollidersThisFrame(includeDynamicBodies: false);

        // 5) Step dynamic pickup boxes vs world
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (e.Body == null) continue;

            e.Body.Step(fixedDt, _runtimeWorldColliders, gravityY: _movement.Gravity);


            // copy back to transform
            e.Transform.Position = e.Body.Center;
        }

        // 6) Rebuild colliders again (still excluding dynamics)
        // (platforms may have moved; dynamics moved too but we still exclude them)
        BuildRuntimeCollidersThisFrame(includeDynamicBodies: false);

        // 7) Step player
        _motor.Step(fixedDt, _wishDir, _wishSpeed, _runtimeWorldColliders);

        // 8) Carry on moving platform (your existing approach)
        if (_motor.Grounded && TryGetGroundPlatformDelta(out var platDelta))
        {
            _motor.Position += platDelta;

            // Re-resolve after carry to avoid tiny penetration pops
            Vector3 extents = new Vector3(_motor.Radius, _motor.HalfHeight, _motor.Radius);
            Vector3 center = _motor.Position + new Vector3(0f, _motor.HalfHeight, 0f);

            var (newCenter, newVel, grounded) = Engine.Physics.Collision.StaticCollision.ResolvePlayerAabb(
                center, _motor.Velocity, extents, _runtimeWorldColliders);

            _motor.Velocity = newVel;
            _motor.Position = newCenter - new Vector3(0f, _motor.HalfHeight, 0f);
            // (Optional) force grounded true if you want:
            // if (grounded) ...
        }

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
            if (_editor.FrameSelectionRequested)
            {
                if (_editor.TryGetSelectedWorldPosition(out var target))
                {
                    Vector3 forward = _camera.Forward;
                    forward.Y = 0f;

                    if (forward.LengthSquared() < 0.0001f)
                        forward = Vector3.UnitZ;
                    else
                        forward = Vector3.Normalize(forward);

                    const float dist = 5f;
                    const float height = 2.0f;

                    Vector3 desiredPos = target - forward * dist + Vector3.UnitY * height;
                    _camera.Position = desiredPos;

                    Vector3 lookDir = target - _camera.Position;
                    if (lookDir.LengthSquared() > 0.0001f)
                    {
                        lookDir = Vector3.Normalize(lookDir);

                        _camera.Yaw = MathF.Atan2(lookDir.X, lookDir.Z);
                        _camera.Pitch = MathF.Asin(-lookDir.Y);

                        float limit = 1.55334f;
                        _camera.Pitch = Math.Clamp(_camera.Pitch, -limit, limit);
                    }
                }

                _editor.ConsumeFrameRequest();
            }
        }
    }

    private void DrawDebugWindow()
    {
        ImGui.Begin("Debug");
        ImGui.Text($"FPS: {_fps:F1}");
        ImGui.Text($"Editor: {(_editorEnabled ? "ON" : "OFF")}");
        ImGui.Text($"Dirty: {(_editor.Dirty ? "YES" : "NO")}");
        ImGui.Text($"Level: {_editor.LevelPath}");
        //Console.WriteLine(_editor.LevelPath); 
        ImGui.Text($"Selected: {_editor.SelectedEntityIndex}");
        if (!string.IsNullOrWhiteSpace(_editor.LastTriggerEvent))
            ImGui.Text($"Last Trigger: {_editor.LastTriggerEvent}");
        ImGui.Text($"Runtime Entities: {_runtimeEntities.Count}");
        ImGui.Text($"Runtime RenderBoxes: {_runtimeRenderBoxes.Count}");
        ImGui.Text($"Runtime Colliders: {_runtimeWorldColliders.Count}");

        if (_runtimeEntities.Count > 0)
        {
            var e0 = _runtimeEntities[0];
            ImGui.Text($"E0 pos: {e0.Transform.Position}");
        }
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
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);

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

        // -----------------------------
        // EDITOR MODE: draw editor view
        // -----------------------------
        if (_editorEnabled)
        {
            for (int i = 0; i < _editor.DrawBoxes.Count; i++)
            {
                var d = _editor.DrawBoxes[i];

                Vector4 color = d.Color;
                if (i == _editor.SelectedEntityIndex)
                    color = new Vector4(1f, 1f, 0.1f, 1f);

                DrawEditorBox(renderer, d.Position, d.Size, d.Rotation, color);
            }

            if (_editor.HasGizmo(out var xLine, out var xHandle,
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

            if (_editor.ShowColliders)
            {
                for (int i = 0; i < _editor.LevelFile.Entities.Count; i++)
                {
                    var e = _editor.LevelFile.Entities[i];

                    bool hasCollider = e.Type == EntityTypes.Box || e.Type == EntityTypes.RigidBody;
                    if (!hasCollider) continue;

                    if (_editor.TryGetEntityWorldTRS(i, out var pos, out var rot, out var scale))
                    {
                        Vector3 size = Mul((Vector3)e.Size, scale);

                        bool selected = (i == _editor.SelectedEntityIndex);

                        Vector4 col = selected
                            ? new Vector4(0.2f, 1f, 0.2f, 1f)
                            : new Vector4(0.1f, 0.8f, 0.1f, 1f);

                        DrawWireObb(renderer, pos, size, rot, col, _editor.ColliderLineThickness);

                        if (_editor.ShowColliderCorners)
                        {
                            float cs = selected ? _editor.CornerSize * 1.25f : _editor.CornerSize;
                            DrawObbCorners(renderer, pos, size, rot, col, cs);
                        }
                    }
                }
            }

            if (_editor.ShowPhysicsAabbs)
            {
                Vector4 aabbCol = new Vector4(0.2f, 0.9f, 1f, 1f);
                float t = _editor.ColliderLineThickness;

                foreach (var aabb in _editor.SolidColliders)
                {
                    Vector3 center = (aabb.Min + aabb.Max) * 0.5f;
                    Vector3 size = (aabb.Max - aabb.Min);
                    DrawWireObb(renderer, center, size, Quaternion.Identity, aabbCol, t);
                }
            }

            return;
        }

        // -------------------------------------
        // GAME MODE: draw runtime entities view
        // -------------------------------------
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var ent = _runtimeEntities[i];

            // Position comes from runtime (MovingPlatform updates this)
            Vector3 pos = ent.Transform.Position;

            // Rotation & scale from runtime (currently mostly defaults for your boxes, but supported)
            Vector3 eulerDeg = ent.Transform.RotationEulerDeg;
            Quaternion rot = Quaternion.CreateFromYawPitchRoll(
                eulerDeg.Y * (MathF.PI / 180f),
                eulerDeg.X * (MathF.PI / 180f),
                eulerDeg.Z * (MathF.PI / 180f));

            Vector3 size = Mul(ent.BoxSize, ent.Transform.Scale);
            Vector4 col = ent.BoxColor;

            DrawEditorBox(renderer, pos, size, rot, col);
        }
    }


    private void DrawObbCorners(Renderer renderer, Vector3 center, Vector3 size, Quaternion rot, Vector4 color, float cornerSize)
    {
        Vector3 he = size * 0.5f;

        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(-he.X, -he.Y, -he.Z);
        corners[1] = new Vector3(he.X, -he.Y, -he.Z);
        corners[2] = new Vector3(he.X, -he.Y, he.Z);
        corners[3] = new Vector3(-he.X, -he.Y, he.Z);
        corners[4] = new Vector3(-he.X, he.Y, -he.Z);
        corners[5] = new Vector3(he.X, he.Y, -he.Z);
        corners[6] = new Vector3(he.X, he.Y, he.Z);
        corners[7] = new Vector3(-he.X, he.Y, he.Z);

        Vector3 cube = new Vector3(cornerSize, cornerSize, cornerSize);

        for (int i = 0; i < 8; i++)
        {
            Vector3 w = Vector3.Transform(corners[i], rot) + center;
            DrawEditorBox(renderer, w, cube, Quaternion.Identity, color);
        }
    }

    private void DrawWireObb(Renderer renderer, Vector3 center, Vector3 size, Quaternion rot, Vector4 color, float thickness)
    {
        Vector3 he = size * 0.5f;

        Span<Vector3> c = stackalloc Vector3[8];
        c[0] = new Vector3(-he.X, -he.Y, -he.Z);
        c[1] = new Vector3(he.X, -he.Y, -he.Z);
        c[2] = new Vector3(he.X, -he.Y, he.Z);
        c[3] = new Vector3(-he.X, -he.Y, he.Z);
        c[4] = new Vector3(-he.X, he.Y, -he.Z);
        c[5] = new Vector3(he.X, he.Y, -he.Z);
        c[6] = new Vector3(he.X, he.Y, he.Z);
        c[7] = new Vector3(-he.X, he.Y, he.Z);

        for (int i = 0; i < 8; i++)
            c[i] = Vector3.Transform(c[i], rot) + center;

        Span<(int a, int b)> edges = stackalloc (int, int)[12]
        {
            (0,1),(1,2),(2,3),(3,0),
            (4,5),(5,6),(6,7),(7,4),
            (0,4),(1,5),(2,6),(3,7)
        };

        for (int i = 0; i < edges.Length; i++)
        {
            var (a, b) = edges[i];
            DrawEdgeBox(renderer, c[a], c[b], thickness, color);
        }
    }

    private void DrawEdgeBox(Renderer renderer, Vector3 a, Vector3 b, float thickness, Vector4 color)
    {
        Vector3 mid = (a + b) * 0.5f;
        Vector3 dir = b - a;
        float len = dir.Length();
        if (len < 0.0001f) return;

        dir /= len;

        Quaternion rot = FromToRotation(Vector3.UnitZ, dir);

        Vector3 size = new Vector3(thickness, thickness, len);
        DrawEditorBox(renderer, mid, size, rot, color);
    }

    private static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);

        float dot = Vector3.Dot(from, to);

        if (dot > 0.9999f)
            return Quaternion.Identity;

        if (dot < -0.9999f)
        {
            Vector3 axis = Vector3.Cross(from, Vector3.UnitX);
            if (axis.LengthSquared() < 0.0001f)
                axis = Vector3.Cross(from, Vector3.UnitY);

            axis = Vector3.Normalize(axis);
            return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
        }

        Vector3 cross = Vector3.Cross(from, to);
        float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        cross = Vector3.Normalize(cross);

        return Quaternion.CreateFromAxisAngle(cross, angle);
    }

    private void DrawEditorBox(Renderer renderer, Vector3 pos, Vector3 size, Quaternion rot, Vector4 color)
    {
        var model = Matrix4x4.CreateScale(size) *  Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);

        _world.DrawBox(renderer.CommandList, model, color);
    }

    private void BuildRuntimeCollidersThisFrame(Entity? exclude = null)
    {
        _runtimeWorldColliders.Clear();

        for (int i = 0; i < _runtimeRenderBoxes.Count; i++)
        {
            var rr = _runtimeRenderBoxes[i];
            if (exclude != null && rr.Entity == exclude) continue;

            Vector3 pos = rr.Entity.Transform.Position;
            Vector3 half = rr.Size * 0.5f;

            _runtimeWorldColliders.Add(new Aabb(pos - half, pos + half));
        }
    }


    public void Dispose()
    {
        _world?.Dispose();
    }
}
