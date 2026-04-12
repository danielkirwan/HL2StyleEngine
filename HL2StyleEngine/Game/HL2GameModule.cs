using Editor.Editor;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Input;
using Engine.Input.Actions;
using Engine.Input.Devices;
using Engine.Physics.Collision;
using Engine.Physics.Dynamics;
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
    private bool _prevRightTriggerDown;

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

    private Entity? _held;
    private float _holdDistance = 2.0f;

    private float _holdStiffness = 60f;
    private float _holdDamping = 12f;   

    private float _throwSpeed = 12f;
    private float _pickupMaxMass = 25f;

    private int _physicsMaxSubsteps = 12;
    private float _physicsMaxStep = 1f / 120f;

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

            if (_editorEnabled)
            {
                _ui.OpenUI();
            }
            else
            {
                // Leaving editor → entering play
                RebuildRuntimeWorld();
                ApplySpawnFromEditor(forceResetVelocity: true);
            }
        }

        if (!_editorEnabled)
        {
            TryPickupDropThrow();
        }

        // Reload level at runtime
        if (!_editorEnabled && _inputState.WasPressed(Key.F5))
        {
            _editor.LoadOrCreate(_editor.LevelPath, SimpleLevel.BuildRoom01File);
            RebuildRuntimeWorld();
            ApplySpawnFromEditor(forceResetVelocity: true);
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

            if (_inputState.WasPressed(Key.E))
                Console.WriteLine("[Pickup] E pressed");

            if (_inputState.LeftMouseDown) // or WasPressed if you add it
                Console.WriteLine("[Pickup] LMB down");
        }
    }

    private static bool IsSphereShape(string? shape)
        => string.Equals(shape, "Sphere", StringComparison.OrdinalIgnoreCase);

    private static RuntimeShapeKind GetRigidBodyShape(LevelEntityDef def)
        => IsSphereShape(def.Shape) ? RuntimeShapeKind.Sphere : RuntimeShapeKind.Box;

    private static float GetScaledSphereRadius(LevelEntityDef def, bool clampScale)
    {
        float radius = clampScale ? MathF.Max(0.01f, def.Radius) : def.Radius;
        Vector3 scale = Abs((Vector3)def.LocalScale);

        if (clampScale)
            scale = ClampMin(scale, 0.01f);

        float scaleMax = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));
        return radius * scaleMax;
    }

    private static bool HasPhysicsBody(Entity entity)
        => entity.Physics.BoxBody != null || entity.Physics.SphereBody != null;

    private static RuntimeShapeKind GetPhysicsBodyShape(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return RuntimeShapeKind.Box;
        if (entity.Physics.SphereBody != null) return RuntimeShapeKind.Sphere;
        return RuntimeShapeKind.None;
    }

    private static bool TryGetPhysicsBodyAabb(Entity entity, out Aabb aabb)
    {
        if (entity.Physics.BoxBody != null)
        {
            aabb = entity.Physics.BoxBody.GetAabb();
            return true;
        }

        if (entity.Physics.SphereBody != null)
        {
            aabb = entity.Physics.SphereBody.GetAabb();
            return true;
        }

        aabb = default;
        return false;
    }

    private static bool TryGetPhysicsBodyCenter(Entity entity, out Vector3 center)
    {
        if (entity.Physics.BoxBody != null)
        {
            center = entity.Physics.BoxBody.Center;
            return true;
        }

        if (entity.Physics.SphereBody != null)
        {
            center = entity.Physics.SphereBody.Center;
            return true;
        }

        center = default;
        return false;
    }

    private static void SetPhysicsBodyCenter(Entity entity, Vector3 center)
    {
        if (entity.Physics.BoxBody != null)
            entity.Physics.BoxBody.Center = center;

        if (entity.Physics.SphereBody != null)
            entity.Physics.SphereBody.Center = center;
    }

    private static Vector3 GetPhysicsBodyVelocity(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.Velocity;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.Velocity;
        return Vector3.Zero;
    }

    private static void SetPhysicsBodyVelocity(Entity entity, Vector3 velocity)
    {
        if (entity.Physics.BoxBody != null)
            entity.Physics.BoxBody.Velocity = velocity;

        if (entity.Physics.SphereBody != null)
            entity.Physics.SphereBody.Velocity = velocity;
    }

    private static bool IsPhysicsBodyKinematic(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.IsKinematic;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.IsKinematic;
        return false;
    }

    private static float GetPhysicsBodyMass(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.Mass;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.Mass;
        return 0f;
    }

    private void StepPhysicsBody(Entity entity, float dt)
    {
        if (entity.Physics.BoxBody != null)
        {
            entity.Physics.BoxBody.Step(dt, _runtimeWorldColliders, gravityY: _movement.Gravity);
            return;
        }

        if (entity.Physics.SphereBody != null)
            entity.Physics.SphereBody.Step(dt, _runtimeWorldColliders, gravityY: _movement.Gravity);
    }

    private bool TryGetSupportingPlatformForBody(
        BoxBody body,
        bool usePreviousPlatformPosition,
        out Entity? platform)
    {
        platform = null;

        Vector3 center = body.Center;
        Vector3 extents = body.HalfExtents;

        float feetY = center.Y - extents.Y;

        const float yTolerance = 0.08f;

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!e.Collider.IsMovingPlatform) continue;

            Vector3 p = usePreviousPlatformPosition
                ? e.Collider.PreviousPosition
                : e.Transform.Position;
            Vector3 half = e.Collider.HalfExtents;

            float topY = p.Y + half.Y;

            if (MathF.Abs(feetY - topY) > yTolerance)
                continue;

            bool overlapX =
                (center.X + extents.X) >= (p.X - half.X) &&
                (center.X - extents.X) <= (p.X + half.X);

            bool overlapZ =
                (center.Z + extents.Z) >= (p.Z - half.Z) &&
                (center.Z - extents.Z) <= (p.Z + half.Z);

            if (!overlapX || !overlapZ)
                continue;

            platform = e;
            return true;
        }

        return false;
    }

    private static bool ShouldStickToMovingPlatform(Entity entity)
        => entity.Collider.Shape == RuntimeShapeKind.Box;

    private void CarryDynamicBoxesOnMovingPlatforms()
    {
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (e.Physics.BoxBody == null) continue;
            if (e.IsHeld) continue;
            if (e.Physics.MotionType != MotionType.Dynamic) continue;
            if (!ShouldStickToMovingPlatform(e)) continue;

            if (!TryGetSupportingPlatformForBody(
                    e.Physics.BoxBody,
                    usePreviousPlatformPosition: true,
                    out var previousSupport))
            {
                continue;
            }

            e.Physics.BoxBody.Center += previousSupport!.Collider.Delta;
            e.Transform.Position = e.Physics.BoxBody.Center;
        }
    }


    private bool TryGetGroundSupportDelta(out Vector3 delta)
    {
        delta = Vector3.Zero;

        Vector3 extents = new Vector3(_motor.Radius, _motor.HalfHeight, _motor.Radius);
        Vector3 center = _motor.Position + new Vector3(0f, _motor.HalfHeight, 0f);
        float feetY = center.Y - extents.Y;

        // If we’re going up, don’t “stick”
        if (_motor.Velocity.Y > 0.001f)
            return false;

        const float yTolerance = 0.18f; // thin platform => larger tolerance
        float bestTopY = float.NegativeInfinity;

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!e.Collider.Enabled || !e.Collider.IsSolid) continue;
            if (e.IsHeld) continue;

            Aabb aabb = e.Collider.GetAabb(e.Transform.Position);
            float topY = aabb.Max.Y;

            // Feet must be close to top surface
            if (feetY < topY - yTolerance || feetY > topY + yTolerance)
                continue;

            bool overlapX = (center.X + extents.X) >= aabb.Min.X && (center.X - extents.X) <= aabb.Max.X;
            bool overlapZ = (center.Z + extents.Z) >= aabb.Min.Z && (center.Z - extents.Z) <= aabb.Max.Z;

            if (!overlapX || !overlapZ)
                continue;

            if (topY < bestTopY)
                continue;

            bestTopY = topY;
            delta = e.Collider.Delta;
        }

        return bestTopY > float.NegativeInfinity;
    }

    private void RebuildRuntimeWorld()
    {
        _runtimeEntities.Clear();
        _runtimeWorldColliders.Clear();

        foreach (var def in _editor.LevelFile.Entities)
        {
            var e = new Entity(def.Id, def.Type, def.Name ?? def.Type);

            e.Transform.Position = def.LocalPosition;
            e.Transform.RotationEulerDeg = def.LocalRotationEulerDeg;
            e.Transform.Scale = def.LocalScale;
            e.Render.Shape = RuntimeShapeKind.Box;
            e.Render.Size = GetScaledSize(def, clampComponents: false);
            e.Render.Radius = GetScaledSphereRadius(def, clampScale: false);
            e.Render.Color = (Vector4)def.Color;

            e.CanPickUp = def.CanPickUp;
            e.Physics.MotionType = def.MotionType;
            e.Physics.BoxBody = null;
            e.Physics.SphereBody = null;

            foreach (var s in def.Scripts)
            {
                if (_scriptRegistry.TryCreate(s.Type, e, out var comp))
                {
                    if (comp is IComponentWithJson jsonComp)
                        jsonComp.ApplyJson(s.Json);

                    e.Components.Add(comp);
                }
            }

            bool isRigidBody = def.Type == EntityTypes.RigidBody;
            bool isSphere = isRigidBody && GetRigidBodyShape(def) == RuntimeShapeKind.Sphere;
            bool isBoxPhysicsShape =
                (def.Type == EntityTypes.Box) ||
                (isRigidBody && !isSphere);

            if (isSphere)
            {
                float radius = GetScaledSphereRadius(def, clampScale: true);
                e.Render.Shape = RuntimeShapeKind.Sphere;
                e.Render.Size = new Vector3(radius * 2f);
                e.Render.Radius = radius;
            }

            if (isSphere && def.MotionType == MotionType.Dynamic)
            {
                float radius = GetScaledSphereRadius(def, clampScale: true);

                e.Physics.SphereBody = new SphereBody(e.Transform.Position, radius)
                {
                    Mass = def.Mass,
                    UseGravity = true,
                    IsKinematic = false,
                    Friction = def.Friction,
                    Restitution = def.Restitution,
                    LinearDamping = 0.02f
                };
            }
            else if (isSphere && def.MotionType == MotionType.Kinematic)
            {
                float radius = GetScaledSphereRadius(def, clampScale: true);

                e.Physics.SphereBody = new SphereBody(e.Transform.Position, radius)
                {
                    Mass = def.Mass,
                    UseGravity = false,
                    IsKinematic = true,
                    Friction = def.Friction,
                    Restitution = def.Restitution
                };
            }
            else if (isBoxPhysicsShape && def.MotionType == MotionType.Dynamic)
            {
                Vector3 worldSize = GetScaledSize(def, clampComponents: true);
                Vector3 half = worldSize * 0.5f;

                e.Physics.BoxBody = new BoxBody(e.Transform.Position, half)
                {
                    Mass = def.Mass,
                    UseGravity = true,
                    IsKinematic = false,
                    Friction = def.Friction,
                    Restitution = def.Restitution,
                    LinearDamping = 0.02f
                };
            }
            else if (isBoxPhysicsShape && def.MotionType == MotionType.Kinematic)
            {
                Vector3 worldSize = GetScaledSize(def, clampComponents: true);
                Vector3 half = worldSize * 0.5f;

                e.Physics.BoxBody = new BoxBody(e.Transform.Position, half)
                {
                    Mass = def.Mass,
                    UseGravity = false,
                    IsKinematic = true,
                    Friction = def.Friction,
                    Restitution = def.Restitution
                };
            }

            bool hasMovingPlatform =
                def.Scripts.Any(sd => sd.Type == "MovingPlatform");

            if (isSphere)
            {
                float radius = GetScaledSphereRadius(def, clampScale: true);
                e.Collider.Shape = RuntimeShapeKind.Sphere;
                e.Collider.Radius = radius;
                e.Collider.Size = new Vector3(radius * 2f);
                e.Collider.IsMovingPlatform = false;
                e.Collider.PreviousPosition = e.Transform.Position;
                e.Collider.Delta = Vector3.Zero;
            }
            else if (isBoxPhysicsShape || hasMovingPlatform)
            {
                e.Collider.Shape = RuntimeShapeKind.Box;
                e.Collider.Size = GetScaledSize(def, clampComponents: true);
                e.Collider.Radius = 0.5f;
                e.Collider.IsMovingPlatform = hasMovingPlatform;
                e.Collider.PreviousPosition = e.Transform.Position;
                e.Collider.Delta = Vector3.Zero;
                e.Render.Color = hasMovingPlatform ? new Vector4(1f, 0.2f, 1f, 1f) : (Vector4)def.Color;
            }

            _runtimeEntities.Add(e);
        }
    }

    private void BuildRuntimeCollidersThisFrame(bool includeDynamicBodies, bool includeHeldBodies)
    {
        _runtimeWorldColliders.Clear();

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!e.Collider.Enabled || !e.Collider.IsSolid)
                continue;

            bool isDynamic = (e.Physics.MotionType == MotionType.Dynamic);

            // Only exclude real dynamics here.
            if (!includeDynamicBodies && isDynamic)
                continue;

            if (!includeHeldBodies && e.IsHeld)
                continue;

            _runtimeWorldColliders.Add(e.Collider.GetAabb(e.Transform.Position));
        }
    }

    private void SnapshotRuntimeColliderPrevPositions()
    {
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!e.Collider.Enabled) continue;
            e.Collider.PreviousPosition = e.Transform.Position;
        }
    }

    private void ComputeRuntimeColliderDeltasFromPrev()
    {
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!e.Collider.Enabled) continue;

            Vector3 cur = e.Transform.Position;
            e.Collider.Delta = cur - e.Collider.PreviousPosition;
        }
    }

    private static Vector3 GetScaledSize(LevelEntityDef def, bool clampComponents)
    {
        Vector3 size = (Vector3)def.Size;
        Vector3 scale = (Vector3)def.LocalScale;

        if (clampComponents)
        {
            size = ClampMin(size, 0.01f);
            scale = ClampMin(scale, 0.01f);
        }

        return Mul(size, scale);
    }

    private static Vector3 ClampMin(Vector3 value, float min)
        => new(
            MathF.Max(min, value.X),
            MathF.Max(min, value.Y),
            MathF.Max(min, value.Z));

    private static Vector3 Abs(Vector3 value)
        => new(MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z));

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

        // 0) Platforms: snapshot prev positions (fixed step)
        SnapshotRuntimeColliderPrevPositions();

        // 1) Update components (MovingPlatform sets Transform.Position here)
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var ent = _runtimeEntities[i];
            for (int j = 0; j < ent.Components.Count; j++)
                ent.Components[j].Update(fixedDt);
        }

        // 2) If an entity is kinematic, drive its body from its transform
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!HasPhysicsBody(e)) continue;

            if (e.Physics.MotionType == MotionType.Kinematic)
            {
                SetPhysicsBodyCenter(e, e.Transform.Position);
                SetPhysicsBodyVelocity(e, Vector3.Zero);
            }
        }

        // 3) Compute platform deltas from prev -> current
        ComputeRuntimeColliderDeltasFromPrev();

        // 4) Carry supported dynamic boxes once per fixed tick.
        CarryDynamicBoxesOnMovingPlatforms();

        float remaining = fixedDt;

        for (int step = 0; step < _physicsMaxSubsteps && remaining > 0f; step++)
        {
            // If we're on the last allowed substep, eat ALL remaining time.
            float dt = (step == _physicsMaxSubsteps - 1)
                ? remaining
                : MathF.Min(_physicsMaxStep, remaining);

            remaining -= dt;

            BuildRuntimeCollidersThisFrame(includeDynamicBodies: false, includeHeldBodies: true);

            for (int i = 0; i < _runtimeEntities.Count; i++)
            {
                var e = _runtimeEntities[i];
                if (!HasPhysicsBody(e)) continue;
                if (e.IsHeld) continue;
                if (e.Physics.MotionType != MotionType.Dynamic) continue;

                StepPhysicsBody(e, dt);

                if (ShouldStickToMovingPlatform(e) &&
                    e.Physics.BoxBody != null &&
                    TryGetSupportingPlatformForBody(
                        e.Physics.BoxBody,
                        usePreviousPlatformPosition: false,
                        out _))
                {
                    // Keep boxes settled on a supporting platform after the carry step.
                    if (e.Physics.BoxBody.Velocity.Y < 0f)
                        e.Physics.BoxBody.Velocity = new Vector3(e.Physics.BoxBody.Velocity.X, 0f, e.Physics.BoxBody.Velocity.Z);
                }

                if (TryGetPhysicsBodyCenter(e, out Vector3 center))
                    e.Transform.Position = center;
            }

            ResolveDynamicDynamic(iterations: 2);
        }


        //ResolveDynamicDynamic(iterations: 3);

        // 6) Update held object (spring + world collision)
        FixedUpdateHeldObject(fixedDt);

        // 7) Player collides with EVERYTHING (STATIC + KINEMATIC + DYNAMIC), except held
        BuildRuntimeCollidersThisFrame(includeDynamicBodies: true, includeHeldBodies: false);

        _motor.Step(fixedDt, _wishDir, _wishSpeed, _runtimeWorldColliders);

        // 8) Push dynamic boxes Half-Life style
        PushDynamicBodiesFromPlayer(fixedDt);

        // Refresh final support-body deltas after dynamics/player interaction.
        ComputeRuntimeColliderDeltasFromPrev();

        // 9) Player carry on moving supports (platform directly or a body riding it)
        if (_motor.Grounded && TryGetGroundSupportDelta(out var platDelta))
        {
            _motor.Position += platDelta;

            Vector3 extents = new Vector3(_motor.Radius, _motor.HalfHeight, _motor.Radius);
            Vector3 center = _motor.Position + new Vector3(0f, _motor.HalfHeight, 0f);

            var (newCenter, newVel, grounded) = Engine.Physics.Collision.StaticCollision.ResolvePlayerAabb(
                center, _motor.Velocity, extents, _runtimeWorldColliders);

            _motor.Velocity = newVel;
            _motor.Position = newCenter - new Vector3(0f, _motor.HalfHeight, 0f);
        }

        _editor.TickTriggers(_motor.Position);
        _camera.Position = _motor.Position + new Vector3(0, _movement.EyeHeight, 0);
    }



    private void PushDynamicBodiesFromPlayer(float dt)
    {
        Vector3 playerExt = new Vector3(_motor.Radius, _motor.HalfHeight, _motor.Radius);
        Vector3 playerCenter = _motor.Position + new Vector3(0f, _motor.HalfHeight, 0f);
        var playerAabb = Aabb.FromCenterExtents(playerCenter, playerExt);

        Vector3 playerLatVel = new Vector3(_motor.Velocity.X, 0f, _motor.Velocity.Z);
        float speed = playerLatVel.Length();
        if (speed < 0.05f) return;

        Vector3 playerDir = playerLatVel / speed;

        const float pushStrength = 6.0f; // tune
        const float maxAddedSpeed = 5.0f; // tune

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!HasPhysicsBody(e)) continue;
            if (e.Physics.MotionType != MotionType.Dynamic) continue;
            if (e.IsHeld) continue;

            if (!TryGetPhysicsBodyAabb(e, out var bodyAabb) ||
                !TryGetPhysicsBodyCenter(e, out Vector3 bodyCenter))
            {
                continue;
            }

            if (!playerAabb.Overlaps(bodyAabb))
                continue;

            if (IsPlayerStandingOnDynamicBody(playerCenter, playerExt, bodyAabb))
                continue;

            // push direction from player -> body in XZ
            Vector3 toBody = bodyCenter - playerCenter;
            toBody.Y = 0f;
            if (toBody.LengthSquared() < 0.0001f) continue;
            toBody = Vector3.Normalize(toBody);

            // only push if player moving somewhat toward it
            float toward = Vector3.Dot(playerDir, toBody);
            if (toward <= 0.1f) continue;

            Vector3 add = toBody * (pushStrength * toward);

            Vector3 v = GetPhysicsBodyVelocity(e);

            Vector3 newLat = new Vector3(v.X, 0f, v.Z) + add;

            float newLatSpeed = newLat.Length();
            if (newLatSpeed > maxAddedSpeed)
                newLat = newLat / newLatSpeed * maxAddedSpeed;

            SetPhysicsBodyVelocity(e, new Vector3(newLat.X, v.Y, newLat.Z));
        }
    }

    private bool IsPlayerStandingOnDynamicBody(Vector3 playerCenter, Vector3 playerExtents, Aabb bodyAabb)
    {
        if (_motor.Velocity.Y > 0.05f)
            return false;

        float feetY = playerCenter.Y - playerExtents.Y;
        float bodyTopY = bodyAabb.Max.Y;

        const float yTolerance = 0.18f;

        if (feetY < bodyTopY - yTolerance || feetY > bodyTopY + yTolerance)
            return false;

        bool overlapX =
            (playerCenter.X + playerExtents.X) >= bodyAabb.Min.X &&
            (playerCenter.X - playerExtents.X) <= bodyAabb.Max.X;

        bool overlapZ =
            (playerCenter.Z + playerExtents.Z) >= bodyAabb.Min.Z &&
            (playerCenter.Z - playerExtents.Z) <= bodyAabb.Max.Z;

        if (!overlapX || !overlapZ)
            return false;

        return playerCenter.Y >= bodyAabb.Center.Y;
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
        ImGui.Text($"Runtime Renderables: {_runtimeEntities.Count(e => e.Render.Enabled)}");
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

                DrawPrimitive(renderer, d.IsSphere ? RuntimeShapeKind.Sphere : RuntimeShapeKind.Box, d.Position, d.Size, d.Rotation, color);
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
                        bool isSphere = e.Type == EntityTypes.RigidBody && IsSphereShape(e.Shape);
                        Vector3 size = isSphere
                            ? new Vector3(MathF.Max(0.01f, e.Radius) * MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z))) * 2f)
                            : Mul((Vector3)e.Size, scale);
                        Quaternion colliderRot = isSphere ? Quaternion.Identity : rot;

                        bool selected = (i == _editor.SelectedEntityIndex);

                        Vector4 col = selected
                            ? new Vector4(0.2f, 1f, 0.2f, 1f)
                            : new Vector4(0.1f, 0.8f, 0.1f, 1f);

                        DrawWireObb(renderer, pos, size, colliderRot, col, _editor.ColliderLineThickness);

                        if (_editor.ShowColliderCorners)
                        {
                            float cs = selected ? _editor.CornerSize * 1.25f : _editor.CornerSize;
                            DrawObbCorners(renderer, pos, size, colliderRot, col, cs);
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
            if (!ent.Render.Enabled) continue;

            // Position comes from runtime (MovingPlatform updates this)
            Vector3 pos = ent.Transform.Position;

            // Rotation & scale from runtime (currently mostly defaults for your boxes, but supported)
            Vector3 eulerDeg = ent.Transform.RotationEulerDeg;
            Quaternion rot = Quaternion.CreateFromYawPitchRoll(
                eulerDeg.Y * (MathF.PI / 180f),
                eulerDeg.X * (MathF.PI / 180f),
                eulerDeg.Z * (MathF.PI / 180f));

            Vector3 size = ent.Render.Size;
            Vector4 col = ent.Render.Color;

            DrawPrimitive(renderer, ent.Render.Shape, pos, size, rot, col);
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

    private void DrawPrimitive(Renderer renderer, RuntimeShapeKind shape, Vector3 pos, Vector3 size, Quaternion rot, Vector4 color)
    {
        var model = Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);

        if (shape == RuntimeShapeKind.Sphere)
            _world.DrawSphere(renderer.CommandList, model, color);
        else
            _world.DrawBox(renderer.CommandList, model, color);
    }

    private static bool RayIntersectsEntityCollider(in Engine.Physics.Collision.Ray ray, Entity entity, float tMin, float tMax, out float hitT)
    {
        if (!entity.Collider.Enabled)
        {
            hitT = 0f;
            return false;
        }

        if (entity.Collider.Shape == RuntimeShapeKind.Sphere)
        {
            return Engine.Physics.Collision.Raycast.RayIntersectsSphere(
                ray,
                entity.Transform.Position,
                entity.Collider.Radius,
                tMin,
                tMax,
                out hitT);
        }

        return Engine.Physics.Collision.Raycast.RayIntersectsAabb(
            ray,
            entity.Collider.GetAabb(entity.Transform.Position),
            tMin,
            tMax,
            out hitT);
    }

    private Entity? RaycastPickable(float maxDist)
    {
        Vector3 origin = _camera.Position;
        Vector3 dir = Vector3.Normalize(_camera.Forward);

        var ray = new Engine.Physics.Collision.Ray(origin, dir);

        Entity? best = null;
        float bestT = maxDist;

        Console.WriteLine($"[Pickup.Raycast] origin={origin} dir={dir} maxDist={maxDist}");
        Console.WriteLine($"[Pickup.Raycast] runtimeColliders={_runtimeEntities.Count(e => e.Collider.Enabled)}");

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!e.Collider.Enabled) continue;

            if (!e.CanPickUp)
                continue;


            if (!HasPhysicsBody(e))
            {
                Console.WriteLine($"[Pickup.Raycast] {e.Name} CanPickUp=true but has no runtime body (not pickable)");
                continue;
            }

            if (e.IsHeld) continue;
            if (IsPhysicsBodyKinematic(e)) continue;
            if (GetPhysicsBodyMass(e) > _pickupMaxMass) continue;

            if (RayIntersectsEntityCollider(ray, e, 0.05f, bestT, out float tHit))
            {
                Console.WriteLine($"[Pickup.Raycast] HIT {e.Name} at t={tHit}");
                bestT = tHit;
                best = e;
            }
        }

        return best;
    }

    private void TryPickupDropThrow()
    {
        
        bool throwPressed =
            _inputState.LeftMousePressedThisFrame ||
            RightTriggerPressedThisFrame();

        if (_held != null && throwPressed)
        {
            ThrowHeld();
            return;
        }

        bool pickupPressed =
            _inputState.WasPressed(Key.E) ||
            _inputState.GetGamepadPressed(GamepadButton.X);

        if (pickupPressed)
        {
            Console.WriteLine($"[Pickup] Tick (held={(_held != null ? _held.Name : "null")})  editor={_editorEnabled} mouseCaptured={_ui.IsMouseCaptured}");
            if (_held != null)
                DropHeld();
            else
            {
                Console.WriteLine("[Pickup] Trying to raycast for pickable...");

                var hit = RaycastPickable(maxDist: 3.0f);

                if (hit == null)
                {
                    Console.WriteLine("[Pickup] RaycastPickable returned NULL (no hit)");
                }
                else
                {
                    Console.WriteLine($"[Pickup] Raycast hit entity: {hit.Name} canPickUp={hit.CanPickUp} bodyShape={GetPhysicsBodyShape(hit)} pos={hit.Transform.Position}");
                    PickUp(hit);
                }
            }
        }
    }
    private void PickUp(Entity e)
    {
        Console.WriteLine($"[Pickup] PickUp({e.Name}) BEFORE: bodyShape={GetPhysicsBodyShape(e)} vel={GetPhysicsBodyVelocity(e)}");

        if (!HasPhysicsBody(e)) return;

        _held = e;
        e.IsHeld = true;

        if (e.Physics.BoxBody != null)
        {
            e.Physics.BoxBody.IsKinematic = true;
            e.Physics.BoxBody.UseGravity = false;
            e.Physics.BoxBody.Velocity = Vector3.Zero;
        }

        if (e.Physics.SphereBody != null)
        {
            e.Physics.SphereBody.IsKinematic = true;
            e.Physics.SphereBody.UseGravity = false;
            e.Physics.SphereBody.Velocity = Vector3.Zero;
        }

        Console.WriteLine($"[Pickup] PickUp({e.Name}) AFTER: bodyShape={GetPhysicsBodyShape(e)} vel={GetPhysicsBodyVelocity(e)}");

    }

    private void DropHeld()
    {
        if (_held == null) return;
        var e = _held;

        Console.WriteLine($"[Pickup] Drop({_held.Name})");


        if (e.Physics.BoxBody != null)
        {
            e.Physics.BoxBody.IsKinematic = false;
            e.Physics.BoxBody.UseGravity = true;
        }

        if (e.Physics.SphereBody != null)
        {
            e.Physics.SphereBody.IsKinematic = false;
            e.Physics.SphereBody.UseGravity = true;
        }

        e.IsHeld = false;
        _held = null;
    }

    private void ThrowHeld()
    {
        if (_held == null) return;
        var e = _held;

        Vector3 dir = Vector3.Normalize(_camera.Forward);

        if (e.Physics.BoxBody != null)
        {
            e.Physics.BoxBody.IsKinematic = false;
            e.Physics.BoxBody.UseGravity = true;

            e.Physics.BoxBody.Velocity = dir * _throwSpeed + _motor.Velocity * 0.5f;
        }

        if (e.Physics.SphereBody != null)
        {
            e.Physics.SphereBody.IsKinematic = false;
            e.Physics.SphereBody.UseGravity = true;

            e.Physics.SphereBody.Velocity = dir * _throwSpeed + _motor.Velocity * 0.5f;
        }

        e.IsHeld = false;
        _held = null;
    }

    private bool RightTriggerPressedThisFrame()
    {
        float v = _inputState.GetAxis(GamepadAxis.TriggerRight);
        bool down = v > 0.5f; 

        bool pressed = down && !_prevRightTriggerDown;
        _prevRightTriggerDown = down;

        return pressed;
    }


    private void FixedUpdateHeldObject(float dt)
    {
        if (_held == null) return;
        if (!HasPhysicsBody(_held)) return;

        Vector3 desired = _camera.Position + Vector3.Normalize(_camera.Forward) * _holdDistance;

        Vector3 x = _held.Physics.BoxBody?.Center ?? _held.Physics.SphereBody!.Center;
        Vector3 v = GetPhysicsBodyVelocity(_held);

        Vector3 toTarget = desired - x;

        Vector3 accel = toTarget * _holdStiffness - v * _holdDamping;

        v += accel * dt;
        Vector3 newCenter = x + v * dt;

        BuildRuntimeCollidersThisFrame(includeDynamicBodies: false, includeHeldBodies: true);

        Vector3 resolvedCenter;
        Vector3 resolvedVel;

        if (_held.Physics.BoxBody != null)
        {
            (resolvedCenter, resolvedVel, _) = Engine.Physics.Collision.StaticCollision.ResolveDynamicAabb(
                newCenter,
                v,
                _held.Physics.BoxBody.HalfExtents,
                _runtimeWorldColliders);
        }
        else
        {
            (resolvedCenter, resolvedVel, _) = Engine.Physics.Collision.StaticCollision.ResolveDynamicSphere(
                newCenter,
                v,
                _held.Physics.SphereBody!.Radius,
                _runtimeWorldColliders);
        }

        SetPhysicsBodyCenter(_held, resolvedCenter);
        SetPhysicsBodyVelocity(_held, resolvedVel);

        _held.Transform.Position = resolvedCenter;
    }

    private static bool ComputeAabbContact(Aabb a, Aabb b, out Vector3 normal, out float penetration)
    {
        normal = Vector3.Zero;
        penetration = 0f;

        if (!a.Overlaps(b))
            return false;

        Vector3 aCenter = a.Center;
        Vector3 bCenter = b.Center;

        float aEx = (a.Max.X - a.Min.X) * 0.5f;
        float aEy = (a.Max.Y - a.Min.Y) * 0.5f;
        float aEz = (a.Max.Z - a.Min.Z) * 0.5f;

        float bEx = (b.Max.X - b.Min.X) * 0.5f;
        float bEy = (b.Max.Y - b.Min.Y) * 0.5f;
        float bEz = (b.Max.Z - b.Min.Z) * 0.5f;

        Vector3 d = bCenter - aCenter;

        float ox = (aEx + bEx) - MathF.Abs(d.X);
        float oy = (aEy + bEy) - MathF.Abs(d.Y);
        float oz = (aEz + bEz) - MathF.Abs(d.Z);

        // smallest axis
        if (ox <= oy && ox <= oz)
        {
            penetration = ox;
            normal = (d.X >= 0f) ? Vector3.UnitX : -Vector3.UnitX;
        }
        else if (oy <= ox && oy <= oz)
        {
            penetration = oy;
            normal = (d.Y >= 0f) ? Vector3.UnitY : -Vector3.UnitY;
        }
        else
        {
            penetration = oz;
            normal = (d.Z >= 0f) ? Vector3.UnitZ : -Vector3.UnitZ;
        }

        return penetration > 0f;
    }

    private static bool ComputeSphereContact(Vector3 aCenter, float aRadius, Vector3 bCenter, float bRadius, out Vector3 normal, out float penetration)
    {
        Vector3 delta = bCenter - aCenter;
        float distSq = delta.LengthSquared();
        float radiusSum = aRadius + bRadius;

        if (distSq <= 1e-8f)
        {
            normal = Vector3.UnitY;
            penetration = radiusSum;
            return true;
        }

        float dist = MathF.Sqrt(distSq);
        penetration = radiusSum - dist;
        if (penetration <= 0f)
        {
            normal = Vector3.Zero;
            penetration = 0f;
            return false;
        }

        normal = delta / dist;
        return true;
    }

    private static bool ComputeSphereAabbContact(Vector3 sphereCenter, float sphereRadius, Aabb box, out Vector3 normal, out float penetration)
        => Engine.Physics.Collision.StaticCollision.TryResolveSphereAabb(sphereCenter, sphereRadius, box, out normal, out penetration);

    private static float GetBodyRestitution(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.Restitution;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.Restitution;
        return 0f;
    }

    private static float GetBodyFriction(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.Friction;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.Friction;
        return 0f;
    }

    private static bool TryGetDynamicContact(Entity a, Entity b, out Vector3 normal, out float penetration)
    {
        normal = Vector3.Zero;
        penetration = 0f;

        RuntimeShapeKind shapeA = GetPhysicsBodyShape(a);
        RuntimeShapeKind shapeB = GetPhysicsBodyShape(b);

        if (!TryGetPhysicsBodyAabb(a, out var aAabb) ||
            !TryGetPhysicsBodyAabb(b, out var bAabb) ||
            !aAabb.Overlaps(bAabb))
        {
            return false;
        }

        if (shapeA == RuntimeShapeKind.Box && shapeB == RuntimeShapeKind.Box)
            return ComputeAabbContact(aAabb, bAabb, out normal, out penetration);

        if (shapeA == RuntimeShapeKind.Sphere && shapeB == RuntimeShapeKind.Sphere)
        {
            return ComputeSphereContact(
                a.Physics.SphereBody!.Center,
                a.Physics.SphereBody.Radius,
                b.Physics.SphereBody!.Center,
                b.Physics.SphereBody.Radius,
                out normal,
                out penetration);
        }

        if (shapeA == RuntimeShapeKind.Sphere && shapeB == RuntimeShapeKind.Box)
        {
            return ComputeSphereAabbContact(
                a.Physics.SphereBody!.Center,
                a.Physics.SphereBody.Radius,
                bAabb,
                out normal,
                out penetration);
        }

        if (shapeA == RuntimeShapeKind.Box && shapeB == RuntimeShapeKind.Sphere &&
            ComputeSphereAabbContact(
                b.Physics.SphereBody!.Center,
                b.Physics.SphereBody.Radius,
                aAabb,
                out Vector3 sphereToBox,
                out penetration))
        {
            normal = -sphereToBox;
            return true;
        }

        return false;
    }

    private void ResolveDynamicDynamic(int iterations = 3)
    {
        // gather indices for dynamic bodies (skip held)
        Span<int> dyn = stackalloc int[_runtimeEntities.Count];
        int dynCount = 0;

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!HasPhysicsBody(e)) continue;
            if (e.IsHeld) continue;
            if (e.Physics.MotionType != MotionType.Dynamic) continue;
            dyn[dynCount++] = i;
        }

        if (dynCount <= 1) return;

        for (int it = 0; it < iterations; it++)
        {
            for (int ai = 0; ai < dynCount; ai++)
            {
                var A = _runtimeEntities[dyn[ai]];

                for (int bi = ai + 1; bi < dynCount; bi++)
                {
                    var B = _runtimeEntities[dyn[bi]];
                    if (!TryGetDynamicContact(A, B, out var n, out float pen))
                        continue;

                    float invA = GetPhysicsBodyMass(A) > 0f ? 1f / GetPhysicsBodyMass(A) : 0f;
                    float invB = GetPhysicsBodyMass(B) > 0f ? 1f / GetPhysicsBodyMass(B) : 0f;
                    float invSum = invA + invB;
                    if (invSum <= 0f) continue;

                    Vector3 corr = n * pen;
                    Vector3 aCenter = A.Transform.Position;
                    Vector3 bCenter = B.Transform.Position;
                    TryGetPhysicsBodyCenter(A, out aCenter);
                    TryGetPhysicsBodyCenter(B, out bCenter);

                    SetPhysicsBodyCenter(A, aCenter - corr * (invA / invSum));
                    SetPhysicsBodyCenter(B, bCenter + corr * (invB / invSum));

                    Vector3 aVelocity = GetPhysicsBodyVelocity(A);
                    Vector3 bVelocity = GetPhysicsBodyVelocity(B);
                    Vector3 rv = bVelocity - aVelocity;
                    float relN = Vector3.Dot(rv, n);

                    // only if moving into each other
                    if (relN < 0f)
                    {
                        float e = MathF.Min(GetBodyRestitution(A), GetBodyRestitution(B));

                        float j = -(1f + e) * relN / invSum;
                        Vector3 impulse = j * n;

                        aVelocity -= impulse * invA;
                        bVelocity += impulse * invB;
                        SetPhysicsBodyVelocity(A, aVelocity);
                        SetPhysicsBodyVelocity(B, bVelocity);

                        // simple friction
                        Vector3 rv2 = bVelocity - aVelocity;
                        Vector3 t = rv2 - Vector3.Dot(rv2, n) * n;

                        float tLen = t.Length();
                        if (tLen > 1e-6f)
                        {
                            t /= tLen;

                            float mu = MathF.Min(GetBodyFriction(A), GetBodyFriction(B));
                            float jt = -Vector3.Dot(rv2, t) / invSum;

                            float maxF = mu * j;
                            jt = Math.Clamp(jt, -maxF, +maxF);

                            Vector3 frImpulse = jt * t;
                            aVelocity -= frImpulse * invA;
                            bVelocity += frImpulse * invB;
                            SetPhysicsBodyVelocity(A, aVelocity);
                            SetPhysicsBodyVelocity(B, bVelocity);
                        }
                    }

                    if (TryGetPhysicsBodyCenter(A, out aCenter))
                        A.Transform.Position = aCenter;
                    if (TryGetPhysicsBodyCenter(B, out bCenter))
                        B.Transform.Position = bCenter;
                }
            }
        }
    }



    public void Dispose()
    {
        _world?.Dispose();
    }
}
