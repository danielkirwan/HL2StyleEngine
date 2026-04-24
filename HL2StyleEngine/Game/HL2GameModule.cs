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
    private readonly struct BoxSupportState
    {
        public readonly bool Valid;
        public readonly bool Stable;
        public readonly int CornerCount;
        public readonly int PolygonCount;
        public readonly float MinCornerY;
        public readonly float MaxCornerY;
        public readonly Vector3 PatchCenter;
        public readonly Vector3 Pivot;
        public readonly Vector3 Lever;

        public BoxSupportState(
            bool valid,
            bool stable,
            int cornerCount,
            int polygonCount,
            float minCornerY,
            float maxCornerY,
            Vector3 patchCenter,
            Vector3 pivot,
            Vector3 lever)
        {
            Valid = valid;
            Stable = stable;
            CornerCount = cornerCount;
            PolygonCount = polygonCount;
            MinCornerY = minCornerY;
            MaxCornerY = maxCornerY;
            PatchCenter = patchCenter;
            Pivot = pivot;
            Lever = lever;
        }
    }

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

    private readonly List<WorldCollider> _runtimeWorldColliders = new();

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

        }
    }

    private static bool IsSphereShape(string? shape)
        => string.Equals(shape, "Sphere", StringComparison.OrdinalIgnoreCase);

    private static bool IsCapsuleShape(string? shape)
        => string.Equals(shape, "Capsule", StringComparison.OrdinalIgnoreCase);

    private static RuntimeShapeKind GetRigidBodyShape(LevelEntityDef def)
        => IsCapsuleShape(def.Shape)
            ? RuntimeShapeKind.Capsule
            : IsSphereShape(def.Shape)
                ? RuntimeShapeKind.Sphere
                : RuntimeShapeKind.Box;

    private static float GetScaledSphereRadius(LevelEntityDef def, bool clampScale)
    {
        float radius = clampScale ? MathF.Max(0.01f, def.Radius) : def.Radius;
        Vector3 scale = Abs((Vector3)def.LocalScale);

        if (clampScale)
            scale = ClampMin(scale, 0.01f);

        float scaleMax = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));
        return radius * scaleMax;
    }

    private static float GetScaledCapsuleRadius(LevelEntityDef def, bool clampScale)
    {
        float radius = clampScale ? MathF.Max(0.01f, def.Radius) : def.Radius;
        Vector3 scale = Abs((Vector3)def.LocalScale);

        if (clampScale)
            scale = ClampMin(scale, 0.01f);

        float scaleXZ = MathF.Max(scale.X, scale.Z);
        return radius * scaleXZ;
    }

    private static float GetScaledCapsuleHeight(LevelEntityDef def, bool clampScale)
    {
        float radius = GetScaledCapsuleRadius(def, clampScale);
        float height = clampScale ? MathF.Max(0.01f, def.Height) : def.Height;
        float scaleY = MathF.Abs(((Vector3)def.LocalScale).Y);

        if (clampScale)
            scaleY = MathF.Max(0.01f, scaleY);

        height *= scaleY;
        return MathF.Max(height, radius * 2f);
    }

    private static bool HasPhysicsBody(Entity entity)
        => entity.Physics.BoxBody != null || entity.Physics.SphereBody != null || entity.Physics.CapsuleBody != null;

    private static RuntimeShapeKind GetPhysicsBodyShape(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return RuntimeShapeKind.Box;
        if (entity.Physics.SphereBody != null) return RuntimeShapeKind.Sphere;
        if (entity.Physics.CapsuleBody != null) return RuntimeShapeKind.Capsule;
        return RuntimeShapeKind.None;
    }

    private static bool TryGetPhysicsBodyAabb(Entity entity, out Aabb aabb)
    {
        if (TryCreatePhysicsWorldCollider(entity, out var collider))
        {
            aabb = collider.GetAabb();
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

        if (entity.Physics.CapsuleBody != null)
        {
            center = entity.Physics.CapsuleBody.Center;
            return true;
        }

        center = default;
        return false;
    }

    private static bool TryGetLastContactManifold(Entity entity, out ContactManifold manifold)
    {
        if (entity.Physics.BoxBody?.LastContactManifold.HasContact == true)
        {
            manifold = entity.Physics.BoxBody.LastContactManifold;
            return true;
        }

        if (entity.Physics.SphereBody?.LastContactManifold.HasContact == true)
        {
            manifold = entity.Physics.SphereBody.LastContactManifold;
            return true;
        }

        if (entity.Physics.CapsuleBody?.LastContactManifold.HasContact == true)
        {
            manifold = entity.Physics.CapsuleBody.LastContactManifold;
            return true;
        }

        manifold = default;
        return false;
    }

    private static bool TryGetLastSupportManifold(Entity entity, out ContactManifold manifold)
    {
        if (entity.Physics.BoxBody?.LastSupportManifold.HasContact == true)
        {
            manifold = entity.Physics.BoxBody.LastSupportManifold;
            return true;
        }

        if (entity.Physics.SphereBody?.LastSupportManifold.HasContact == true)
        {
            manifold = entity.Physics.SphereBody.LastSupportManifold;
            return true;
        }

        if (entity.Physics.CapsuleBody?.LastSupportManifold.HasContact == true)
        {
            manifold = entity.Physics.CapsuleBody.LastSupportManifold;
            return true;
        }

        manifold = default;
        return false;
    }

    private static Quaternion GetColliderRotation(Entity entity)
    {
        RuntimeShapeKind shape = GetPhysicsBodyShape(entity);
        if ((shape == RuntimeShapeKind.Box || shape == RuntimeShapeKind.Capsule) && HasPhysicsBody(entity))
            return entity.Physics.Rotation;

        Vector3 eulerDeg = entity.Transform.RotationEulerDeg;
        return Quaternion.CreateFromYawPitchRoll(
            eulerDeg.Y * (MathF.PI / 180f),
            eulerDeg.X * (MathF.PI / 180f),
            eulerDeg.Z * (MathF.PI / 180f));
    }

    private static WorldCollider CreateWorldCollider(EntityColliderState collider, Vector3 position, Quaternion rotation)
    {
        return collider.Shape switch
        {
            RuntimeShapeKind.Sphere => WorldCollider.Sphere(position, collider.Radius),
            RuntimeShapeKind.Capsule => WorldCollider.Capsule(position, collider.Radius, collider.Height, rotation),
            _ => WorldCollider.Box(position, collider.HalfExtents, rotation)
        };
    }

    private static WorldCollider CreateWorldCollider(Entity entity)
        => CreateWorldCollider(entity.Collider, entity.Transform.Position, GetColliderRotation(entity));

    private static bool TryCreatePhysicsWorldCollider(Entity entity, out WorldCollider collider)
    {
        if (entity.Physics.BoxBody != null)
        {
            collider = WorldCollider.Box(entity.Physics.BoxBody.Center, entity.Physics.BoxBody.HalfExtents, entity.Physics.Rotation);
            return true;
        }

        if (entity.Physics.SphereBody != null)
        {
            collider = WorldCollider.Sphere(entity.Physics.SphereBody.Center, entity.Physics.SphereBody.Radius);
            return true;
        }

        if (entity.Physics.CapsuleBody != null)
        {
            collider = WorldCollider.Capsule(entity.Physics.CapsuleBody.Center, entity.Physics.CapsuleBody.Radius, entity.Physics.CapsuleBody.Height, entity.Physics.Rotation);
            return true;
        }

        collider = default;
        return false;
    }

    private static void SetPhysicsBodyCenter(Entity entity, Vector3 center)
    {
        if (entity.Physics.BoxBody != null)
            entity.Physics.BoxBody.Center = center;

        if (entity.Physics.SphereBody != null)
            entity.Physics.SphereBody.Center = center;

        if (entity.Physics.CapsuleBody != null)
            entity.Physics.CapsuleBody.Center = center;
    }

    private static Vector3 GetPhysicsBodyVelocity(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.Velocity;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.Velocity;
        if (entity.Physics.CapsuleBody != null) return entity.Physics.CapsuleBody.Velocity;
        return Vector3.Zero;
    }

    private static void SetPhysicsBodyVelocity(Entity entity, Vector3 velocity)
    {
        if (entity.Physics.BoxBody != null)
            entity.Physics.BoxBody.Velocity = velocity;

        if (entity.Physics.SphereBody != null)
            entity.Physics.SphereBody.Velocity = velocity;

        if (entity.Physics.CapsuleBody != null)
            entity.Physics.CapsuleBody.Velocity = velocity;
    }

    private static void SetBodyContactState(
        Entity entity,
        bool hadContact,
        Vector3 contactNormal,
        ContactManifold contactManifold,
        ContactManifold supportManifold)
    {
        if (entity.Physics.BoxBody != null)
        {
            entity.Physics.BoxBody.HadContact = hadContact;
            entity.Physics.BoxBody.LastContactNormal = contactNormal;
            entity.Physics.BoxBody.LastContactManifold = contactManifold;
            entity.Physics.BoxBody.LastSupportManifold = supportManifold;
        }

        if (entity.Physics.SphereBody != null)
        {
            entity.Physics.SphereBody.HadContact = hadContact;
            entity.Physics.SphereBody.LastContactNormal = contactNormal;
            entity.Physics.SphereBody.LastContactManifold = contactManifold;
            entity.Physics.SphereBody.LastSupportManifold = supportManifold;
        }

        if (entity.Physics.CapsuleBody != null)
        {
            entity.Physics.CapsuleBody.HadContact = hadContact;
            entity.Physics.CapsuleBody.LastContactNormal = contactNormal;
            entity.Physics.CapsuleBody.LastContactManifold = contactManifold;
            entity.Physics.CapsuleBody.LastSupportManifold = supportManifold;
        }
    }

    private static bool IsPhysicsBodyKinematic(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.IsKinematic;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.IsKinematic;
        if (entity.Physics.CapsuleBody != null) return entity.Physics.CapsuleBody.IsKinematic;
        return false;
    }

    private static float GetPhysicsBodyMass(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.Mass;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.Mass;
        if (entity.Physics.CapsuleBody != null) return entity.Physics.CapsuleBody.Mass;
        return 0f;
    }

    private static Quaternion EulerDegToQuat(Vector3 eulerDeg)
    {
        float yaw = MathF.PI / 180f * eulerDeg.Y;
        float pitch = MathF.PI / 180f * eulerDeg.X;
        float roll = MathF.PI / 180f * eulerDeg.Z;

        return Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
    }

    private static Vector3 QuatToEulerDeg(Quaternion q)
    {
        var m = Matrix4x4.CreateFromQuaternion(q);

        float sy = -m.M23;
        float cy = MathF.Sqrt(MathF.Max(0f, 1f - sy * sy));

        float pitch;
        float yaw;
        float roll;

        if (cy > 0.0001f)
        {
            pitch = MathF.Asin(sy);
            yaw = MathF.Atan2(m.M13, m.M33);
            roll = MathF.Atan2(m.M21, m.M22);
        }
        else
        {
            pitch = MathF.Asin(sy);
            yaw = MathF.Atan2(-m.M31, m.M11);
            roll = 0f;
        }

        const float rad2deg = 180f / MathF.PI;
        return new Vector3(pitch * rad2deg, yaw * rad2deg, roll * rad2deg);
    }

    private static float GetBodyRotationRadius(Entity entity)
    {
        return GetPhysicsBodyShape(entity) switch
        {
            RuntimeShapeKind.Sphere => MathF.Max(0.05f, entity.Physics.SphereBody?.Radius ?? entity.Render.Radius),
            RuntimeShapeKind.Capsule => MathF.Max(0.05f, entity.Physics.CapsuleBody?.Radius ?? entity.Render.Radius),
            RuntimeShapeKind.Box => MathF.Max(0.05f, entity.Render.Size.Length() / 3f),
            _ => 0.5f
        };
    }

    private static void SyncEntityRotationFromPhysics(Entity entity)
    {
        entity.Physics.Rotation = Quaternion.Normalize(entity.Physics.Rotation);
        entity.Transform.RotationEulerDeg = QuatToEulerDeg(entity.Physics.Rotation);
    }

    private static void AddAngularVelocity(Entity entity, Vector3 delta)
    {
        if (!HasPhysicsBody(entity))
            return;

        entity.Physics.AngularVelocity += delta;

        const float maxAngularSpeed = 10f;
        float speed = entity.Physics.AngularVelocity.Length();
        if (speed > maxAngularSpeed)
            entity.Physics.AngularVelocity *= maxAngularSpeed / speed;
    }

    private static float NormalSign(float value)
    {
        if (value > 1e-4f) return 1f;
        if (value < -1e-4f) return -1f;
        return 0f;
    }

    private static Quaternion ClampRotationAroundRest(Quaternion current, Quaternion rest, float maxAngleRadians)
    {
        Quaternion relative = Quaternion.Normalize(current * Quaternion.Conjugate(rest));
        if (relative.W < 0f)
            relative = new Quaternion(-relative.X, -relative.Y, -relative.Z, -relative.W);

        float clampedW = Math.Clamp(relative.W, -1f, 1f);
        float angle = 2f * MathF.Acos(clampedW);
        if (angle <= maxAngleRadians || angle < 1e-5f)
            return Quaternion.Normalize(current);

        float sinHalf = MathF.Sqrt(MathF.Max(0f, 1f - clampedW * clampedW));
        Vector3 axis = sinHalf > 1e-5f
            ? new Vector3(relative.X, relative.Y, relative.Z) / sinHalf
            : Vector3.UnitY;

        Quaternion limited = Quaternion.CreateFromAxisAngle(axis, maxAngleRadians);
        return Quaternion.Normalize(limited * rest);
    }

    private static bool TryNormalize(Vector3 v, out Vector3 normalized)
    {
        float lenSq = v.LengthSquared();
        if (lenSq < 1e-8f)
        {
            normalized = Vector3.Zero;
            return false;
        }

        normalized = v / MathF.Sqrt(lenSq);
        return true;
    }

    private static Vector3 ProjectOntoPlane(Vector3 v, Vector3 normal)
        => v - normal * Vector3.Dot(v, normal);

    private static Quaternion GetNearestBoxStableRotation(Quaternion current)
    {
        ReadOnlySpan<Vector3> candidates =
        [
            Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitZ, -Vector3.UnitZ
        ];

        Vector3 localUp = Vector3.UnitY;
        float bestUpDot = float.NegativeInfinity;
        for (int i = 0; i < candidates.Length; i++)
        {
            Vector3 worldAxis = Vector3.Transform(candidates[i], current);
            float d = Vector3.Dot(worldAxis, Vector3.UnitY);
            if (d > bestUpDot)
            {
                bestUpDot = d;
                localUp = candidates[i];
            }
        }

        Vector3 localForward = Vector3.UnitZ;
        float bestForwardScore = float.NegativeInfinity;
        Vector3 desiredForward = Vector3.UnitZ;

        for (int i = 0; i < candidates.Length; i++)
        {
            Vector3 candidate = candidates[i];
            if (MathF.Abs(Vector3.Dot(candidate, localUp)) > 0.999f)
                continue;

            Vector3 worldAxis = Vector3.Transform(candidate, current);
            Vector3 planar = ProjectOntoPlane(worldAxis, Vector3.UnitY);
            float score = planar.LengthSquared();
            if (score > bestForwardScore && TryNormalize(planar, out var planarDir))
            {
                bestForwardScore = score;
                localForward = candidate;
                desiredForward = planarDir;
            }
        }

        Quaternion qUp = FromToRotation(localUp, Vector3.UnitY);
        Vector3 rotatedForward = ProjectOntoPlane(Vector3.Transform(localForward, qUp), Vector3.UnitY);
        if (!TryNormalize(rotatedForward, out var rotatedForwardDir))
            return Quaternion.Normalize(qUp);

        Quaternion qTwist = FromToRotation(rotatedForwardDir, desiredForward);
        return Quaternion.Normalize(qTwist * qUp);
    }

    private static Quaternion GetNearestCapsuleStableRotation(Quaternion current)
    {
        Vector3 axis = Vector3.Transform(Vector3.UnitY, current);
        float upDot = Vector3.Dot(axis, Vector3.UnitY);
        float absUpDot = MathF.Abs(upDot);

        if (absUpDot >= 0.72f)
        {
            Vector3 forward = ProjectOntoPlane(Vector3.Transform(Vector3.UnitZ, current), Vector3.UnitY);
            if (!TryNormalize(forward, out var forwardDir))
            {
                forward = ProjectOntoPlane(Vector3.Transform(Vector3.UnitX, current), Vector3.UnitY);
                if (!TryNormalize(forward, out forwardDir))
                    forwardDir = Vector3.UnitZ;
            }

            return Quaternion.Normalize(FromToRotation(Vector3.UnitZ, forwardDir));
        }

        Vector3 horizontalAxis = ProjectOntoPlane(axis, Vector3.UnitY);
        if (!TryNormalize(horizontalAxis, out var sideDir))
            sideDir = Vector3.UnitX;

        return Quaternion.Normalize(FromToRotation(Vector3.UnitY, sideDir));
    }

    private static bool IsNearStableBoxPose(Entity entity, float minAlignment = 0.995f)
    {
        Quaternion stable = GetNearestBoxStableRotation(entity.Physics.Rotation);
        float alignment = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(entity.Physics.Rotation), stable));
        return alignment >= minAlignment;
    }

    private static Quaternion AlignQuaternionShortestPath(Quaternion current, Quaternion target)
    {
        current = Quaternion.Normalize(current);
        target = Quaternion.Normalize(target);
        if (Quaternion.Dot(current, target) < 0f)
            target = new Quaternion(-target.X, -target.Y, -target.Z, -target.W);

        return target;
    }

    private static bool IsBodyCenterSupportedBySurface(
        Vector3 bodyCenter,
        Aabb bodyAabb,
        Aabb supportAabb,
        float yTolerance,
        float edgeInset)
    {
        float topY = supportAabb.Max.Y;
        if (bodyAabb.Min.Y < topY - yTolerance || bodyAabb.Min.Y > topY + yTolerance)
            return false;

        float insetX = MathF.Min(edgeInset, MathF.Max(0f, (supportAabb.Max.X - supportAabb.Min.X) * 0.5f - 0.001f));
        float insetZ = MathF.Min(edgeInset, MathF.Max(0f, (supportAabb.Max.Z - supportAabb.Min.Z) * 0.5f - 0.001f));

        return bodyCenter.X >= supportAabb.Min.X + insetX &&
               bodyCenter.X <= supportAabb.Max.X - insetX &&
               bodyCenter.Z >= supportAabb.Min.Z + insetZ &&
               bodyCenter.Z <= supportAabb.Max.Z - insetZ;
    }

    private static bool DoesBodyTouchSupportSurface(
        Aabb bodyAabb,
        Aabb supportAabb,
        float yTolerance,
        float overlapInset)
    {
        float topY = supportAabb.Max.Y;
        if (bodyAabb.Min.Y < topY - yTolerance || bodyAabb.Min.Y > topY + yTolerance)
            return false;

        return (bodyAabb.Max.X - overlapInset) >= supportAabb.Min.X &&
               (bodyAabb.Min.X + overlapInset) <= supportAabb.Max.X &&
               (bodyAabb.Max.Z - overlapInset) >= supportAabb.Min.Z &&
               (bodyAabb.Min.Z + overlapInset) <= supportAabb.Max.Z;
    }

    private bool TryGetSupportingSurface(Entity entity, bool requireCenterSupport, out Aabb supportAabb)
    {
        supportAabb = default;

        if (!TryGetPhysicsBodyAabb(entity, out var bodyAabb) ||
            !TryGetPhysicsBodyCenter(entity, out Vector3 bodyCenter))
        {
            return false;
        }

        const float yTolerance = 0.08f;
        const float edgeInset = 0.02f;
        const float overlapInset = 0.01f;

        bool found = false;
        float bestTopY = float.NegativeInfinity;

        for (int i = 0; i < _runtimeWorldColliders.Count; i++)
        {
            Aabb candidate = _runtimeWorldColliders[i].GetAabb();
            if (!DoesBodyTouchSupportSurface(bodyAabb, candidate, yTolerance, overlapInset))
                continue;

            if (requireCenterSupport &&
                !IsBodyCenterSupportedBySurface(bodyCenter, bodyAabb, candidate, yTolerance, edgeInset))
            {
                continue;
            }

            float topY = candidate.Max.Y;
            if (!found || topY > bestTopY)
            {
                supportAabb = candidate;
                bestTopY = topY;
                found = true;
            }
        }

        return found;
    }

    private static Vector3 GetBoxSupportPatchCenter(WorldCollider collider)
    {
        GetBoxSupportPatchInfo(collider, out Vector3 center, out _, out _, out _);
        return center;
    }

    private static void GetBoxSupportPatchInfo(
        WorldCollider collider,
        out Vector3 center,
        out int cornerCount,
        out float minCornerY,
        out float maxCornerY)
    {
        Vector3 hx = collider.AxisX * collider.HalfExtents.X;
        Vector3 hy = collider.AxisY * collider.HalfExtents.Y;
        Vector3 hz = collider.AxisZ * collider.HalfExtents.Z;

        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = collider.Center - hx - hy - hz;
        corners[1] = collider.Center + hx - hy - hz;
        corners[2] = collider.Center + hx - hy + hz;
        corners[3] = collider.Center - hx - hy + hz;
        corners[4] = collider.Center - hx + hy - hz;
        corners[5] = collider.Center + hx + hy - hz;
        corners[6] = collider.Center + hx + hy + hz;
        corners[7] = collider.Center - hx + hy + hz;

        float minY = float.PositiveInfinity;
        float highestY = float.NegativeInfinity;
        for (int i = 0; i < corners.Length; i++)
        {
            minY = MathF.Min(minY, corners[i].Y);
            highestY = MathF.Max(highestY, corners[i].Y);
        }

        float patchTolerance = MathF.Max(0.03f, MathF.Max(collider.HalfExtents.X, collider.HalfExtents.Z) * 0.14f);
        Vector3 sum = Vector3.Zero;
        int count = 0;

        for (int i = 0; i < corners.Length; i++)
        {
            if (corners[i].Y <= minY + patchTolerance)
            {
                sum += corners[i];
                count++;
            }
        }

        center = count > 0 ? sum / count : collider.Center - collider.AxisY * collider.HalfExtents.Y;
        cornerCount = count;
        minCornerY = minY;
        maxCornerY = highestY;
    }

    private static int BuildBoxBottomCorners(WorldCollider collider, Span<Vector3> corners, out float minCornerY, out float maxCornerY)
    {
        Vector3 hx = collider.AxisX * collider.HalfExtents.X;
        Vector3 hy = collider.AxisY * collider.HalfExtents.Y;
        Vector3 hz = collider.AxisZ * collider.HalfExtents.Z;

        Span<Vector3> all = stackalloc Vector3[8];
        all[0] = collider.Center - hx - hy - hz;
        all[1] = collider.Center + hx - hy - hz;
        all[2] = collider.Center + hx - hy + hz;
        all[3] = collider.Center - hx - hy + hz;
        all[4] = collider.Center - hx + hy - hz;
        all[5] = collider.Center + hx + hy - hz;
        all[6] = collider.Center + hx + hy + hz;
        all[7] = collider.Center - hx + hy + hz;

        minCornerY = float.PositiveInfinity;
        maxCornerY = float.NegativeInfinity;
        for (int i = 0; i < all.Length; i++)
        {
            minCornerY = MathF.Min(minCornerY, all[i].Y);
            maxCornerY = MathF.Max(maxCornerY, all[i].Y);
        }

        float patchTolerance = MathF.Max(0.03f, MathF.Max(collider.HalfExtents.X, collider.HalfExtents.Z) * 0.14f);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].Y <= minCornerY + patchTolerance)
                corners[count++] = all[i];
        }

        return count;
    }

    private static bool NearlyEqual(Vector2 a, Vector2 b, float tolerance = 0.01f)
        => Vector2.DistanceSquared(a, b) <= tolerance * tolerance;

    private static int AddUniquePoint(Span<Vector2> points, int count, Vector2 point)
    {
        for (int i = 0; i < count; i++)
        {
            if (NearlyEqual(points[i], point))
                return count;
        }

        points[count] = point;
        return count + 1;
    }

    private static float Cross2D(Vector2 a, Vector2 b, Vector2 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static void Swap(ref Vector2 a, ref Vector2 b)
    {
        (a, b) = (b, a);
    }

    private static int BuildConvexHull(Span<Vector2> points, int count, Span<Vector2> hull)
    {
        if (count <= 1)
        {
            if (count == 1)
                hull[0] = points[0];
            return count;
        }

        for (int i = 0; i < count - 1; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (points[j].X < points[i].X ||
                    (MathF.Abs(points[j].X - points[i].X) < 1e-6f && points[j].Y < points[i].Y))
                {
                    Swap(ref points[i], ref points[j]);
                }
            }
        }

        int hullCount = 0;
        for (int i = 0; i < count; i++)
        {
            while (hullCount >= 2 && Cross2D(hull[hullCount - 2], hull[hullCount - 1], points[i]) <= 1e-6f)
                hullCount--;

            hull[hullCount++] = points[i];
        }

        int lowerCount = hullCount;
        for (int i = count - 2; i >= 0; i--)
        {
            while (hullCount > lowerCount && Cross2D(hull[hullCount - 2], hull[hullCount - 1], points[i]) <= 1e-6f)
                hullCount--;

            hull[hullCount++] = points[i];
        }

        if (hullCount > 1)
            hullCount--;

        return hullCount;
    }

    private static Vector2 ClosestPointOnSegment2D(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abLenSq = ab.LengthSquared();
        if (abLenSq < 1e-8f)
            return a;

        float t = Vector2.Dot(point - a, ab) / abLenSq;
        t = Math.Clamp(t, 0f, 1f);
        return a + ab * t;
    }

    private static bool PointInConvexPolygonXZ(Vector2 point, Span<Vector2> polygon, int count)
    {
        if (count < 3)
            return false;

        float sign = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % count];
            float cross = Cross2D(a, b, point);
            if (MathF.Abs(cross) <= 0.01f)
                continue;

            if (sign == 0f)
                sign = MathF.Sign(cross);
            else if (MathF.Sign(cross) != sign)
                return false;
        }

        return true;
    }

    private static Vector2 ClosestPointOnPolygonEdgesXZ(Vector2 point, Span<Vector2> polygon, int count)
    {
        Vector2 best = polygon[0];
        float bestDistSq = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % count];
            Vector2 candidate = ClosestPointOnSegment2D(point, a, b);
            float distSq = Vector2.DistanceSquared(point, candidate);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = candidate;
            }
        }

        return best;
    }

    private static bool TryBuildBoxSupportState(WorldCollider collider, Vector3 bodyCenter, Aabb supportAabb, out BoxSupportState state)
    {
        state = default;
        if (collider.Shape != WorldColliderShape.Box)
            return false;

        Span<Vector3> bottomCorners = stackalloc Vector3[4];
        int cornerCount = BuildBoxBottomCorners(collider, bottomCorners, out float minCornerY, out float maxCornerY);
        if (cornerCount <= 0)
            return false;

        Span<Vector2> unique = stackalloc Vector2[4];
        int uniqueCount = 0;
        Vector3 patchSum = Vector3.Zero;

        for (int i = 0; i < cornerCount; i++)
        {
            Vector3 corner = bottomCorners[i];
            float clampedX = Math.Clamp(corner.X, supportAabb.Min.X, supportAabb.Max.X);
            float clampedZ = Math.Clamp(corner.Z, supportAabb.Min.Z, supportAabb.Max.Z);
            Vector2 pointXZ = new(clampedX, clampedZ);
            uniqueCount = AddUniquePoint(unique, uniqueCount, pointXZ);
            patchSum += new Vector3(clampedX, supportAabb.Max.Y, clampedZ);
        }

        if (uniqueCount <= 0)
            return false;

        Vector3 patchCenter = patchSum / cornerCount;
        Vector2 comXZ = new(bodyCenter.X, bodyCenter.Z);
        Vector2 pivotXZ;
        bool stable;

        if (uniqueCount == 1)
        {
            pivotXZ = unique[0];
            stable = Vector2.DistanceSquared(comXZ, pivotXZ) <= 0.01f * 0.01f;
        }
        else if (uniqueCount == 2)
        {
            pivotXZ = ClosestPointOnSegment2D(comXZ, unique[0], unique[1]);
            stable = Vector2.DistanceSquared(comXZ, pivotXZ) <= 0.02f * 0.02f;
        }
        else
        {
            Span<Vector2> hull = stackalloc Vector2[8];
            int hullCount = BuildConvexHull(unique, uniqueCount, hull);
            if (hullCount <= 0)
                return false;

            stable = PointInConvexPolygonXZ(comXZ, hull, hullCount);
            pivotXZ = stable ? comXZ : ClosestPointOnPolygonEdgesXZ(comXZ, hull, hullCount);
            uniqueCount = hullCount;
        }

        Vector3 pivot = new(pivotXZ.X, supportAabb.Max.Y, pivotXZ.Y);
        Vector3 lever = bodyCenter - pivot;
        lever.Y = 0f;

        state = new BoxSupportState(
            valid: true,
            stable: stable,
            cornerCount: cornerCount,
            polygonCount: uniqueCount,
            minCornerY: minCornerY,
            maxCornerY: maxCornerY,
            patchCenter: patchCenter,
            pivot: pivot,
            lever: lever);
        return true;
    }

    private static bool TryBuildSupportStateFromContactManifold(Vector3 bodyCenter, ContactManifold manifold, out BoxSupportState state)
    {
        state = default;
        if (!manifold.HasContact)
            return false;

        Span<Vector2> unique = stackalloc Vector2[4];
        int uniqueCount = 0;
        Vector3 patchSum = Vector3.Zero;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < manifold.ContactCount; i++)
        {
            Vector3 point = manifold.GetPoint(i);
            uniqueCount = AddUniquePoint(unique, uniqueCount, new Vector2(point.X, point.Z));
            patchSum += point;
            minY = MathF.Min(minY, point.Y);
            maxY = MathF.Max(maxY, point.Y);
        }

        if (uniqueCount <= 0)
            return false;

        Vector3 patchCenter = patchSum / manifold.ContactCount;
        Vector2 comXZ = new(bodyCenter.X, bodyCenter.Z);
        Vector2 pivotXZ;
        bool stable;
        int polygonCount = uniqueCount;

        if (uniqueCount == 1)
        {
            pivotXZ = unique[0];
            stable = Vector2.DistanceSquared(comXZ, pivotXZ) <= 0.01f * 0.01f;
        }
        else if (uniqueCount == 2)
        {
            pivotXZ = ClosestPointOnSegment2D(comXZ, unique[0], unique[1]);
            stable = Vector2.DistanceSquared(comXZ, pivotXZ) <= 0.02f * 0.02f;
        }
        else
        {
            Span<Vector2> hull = stackalloc Vector2[8];
            int hullCount = BuildConvexHull(unique, uniqueCount, hull);
            if (hullCount <= 0)
                return false;

            stable = PointInConvexPolygonXZ(comXZ, hull, hullCount);
            pivotXZ = stable ? comXZ : ClosestPointOnPolygonEdgesXZ(comXZ, hull, hullCount);
            polygonCount = hullCount;
        }

        Vector3 pivot = new(pivotXZ.X, patchCenter.Y, pivotXZ.Y);
        Vector3 lever = bodyCenter - pivot;
        lever.Y = 0f;

        state = new BoxSupportState(
            valid: true,
            stable: stable,
            cornerCount: manifold.ContactCount,
            polygonCount: polygonCount,
            minCornerY: minY,
            maxCornerY: maxY,
            patchCenter: patchCenter,
            pivot: pivot,
            lever: lever);
        return true;
    }

    private static bool TryBuildPromotedFlatBoxSupportState(
        Entity entity,
        WorldCollider collider,
        Vector3 bodyCenter,
        ContactManifold manifold,
        out BoxSupportState state)
    {
        state = default;

        if (collider.Shape != WorldColliderShape.Box || !manifold.HasContact)
            return false;

        if (manifold.SurfaceNormal.Y < 0.75f || !IsNearStableBoxPose(entity, minAlignment: 0.97f))
            return false;

        Span<Vector3> bottomCorners = stackalloc Vector3[4];
        int cornerCount = BuildBoxBottomCorners(collider, bottomCorners, out float minCornerY, out float maxCornerY);
        if (cornerCount < 4)
            return false;

        float planeY = manifold.GetAveragePoint().Y;
        float flatRange = maxCornerY - minCornerY;
        float flatTolerance = MathF.Max(0.06f, MathF.Max(collider.HalfExtents.X, collider.HalfExtents.Z) * 0.1f);
        if (flatRange > flatTolerance)
            return false;

        Vector3 patchCenter = Vector3.Zero;
        Span<Vector2> polygon = stackalloc Vector2[4];
        int polygonCount = 0;
        for (int i = 0; i < cornerCount; i++)
        {
            Vector3 point = new(bottomCorners[i].X, planeY, bottomCorners[i].Z);
            patchCenter += point;
            polygonCount = AddUniquePoint(polygon, polygonCount, new Vector2(point.X, point.Z));
        }

        if (polygonCount < 3)
            return false;

        patchCenter /= cornerCount;
        Vector2 comXZ = new(bodyCenter.X, bodyCenter.Z);
        Vector2 patchCenterXZ = new(patchCenter.X, patchCenter.Z);
        float centeredTolerance = MathF.Max(0.08f, MathF.Max(collider.HalfExtents.X, collider.HalfExtents.Z) * 0.35f);
        if (Vector2.DistanceSquared(comXZ, patchCenterXZ) > centeredTolerance * centeredTolerance)
            return false;

        Span<Vector2> hull = stackalloc Vector2[8];
        int hullCount = BuildConvexHull(polygon, polygonCount, hull);
        if (hullCount < 3)
            return false;

        bool stable = PointInConvexPolygonXZ(comXZ, hull, hullCount);
        Vector2 pivotXZ = stable ? comXZ : ClosestPointOnPolygonEdgesXZ(comXZ, hull, hullCount);
        Vector3 pivot = new(pivotXZ.X, planeY, pivotXZ.Y);
        Vector3 lever = bodyCenter - pivot;
        lever.Y = 0f;

        state = new BoxSupportState(
            valid: true,
            stable: stable,
            cornerCount: cornerCount,
            polygonCount: hullCount,
            minCornerY: minCornerY,
            maxCornerY: maxCornerY,
            patchCenter: patchCenter,
            pivot: pivot,
            lever: lever);
        return true;
    }

    private static bool TryBuildFallbackSupportState(Entity entity, WorldCollider collider, Vector3 bodyCenter, Aabb supportAabb, out BoxSupportState state)
    {
        if (collider.Shape == WorldColliderShape.Box &&
            TryBuildBoxSupportState(collider, bodyCenter, supportAabb, out state))
        {
            return true;
        }

        Vector3 supportPatchCenter = collider.Shape switch
        {
            WorldColliderShape.Capsule => GetCapsuleSupportPatchCenter(collider),
            WorldColliderShape.Sphere => collider.Center - Vector3.UnitY * collider.Radius,
            _ => collider.Center
        };

        Vector3 pivot = new(
            Math.Clamp(supportPatchCenter.X, supportAabb.Min.X, supportAabb.Max.X),
            supportAabb.Max.Y,
            Math.Clamp(supportPatchCenter.Z, supportAabb.Min.Z, supportAabb.Max.Z));

        Vector3 lever = bodyCenter - pivot;
        lever.Y = 0f;
        bool stable = lever.LengthSquared() <= 0.02f * 0.02f;

        state = new BoxSupportState(
            valid: true,
            stable: stable,
            cornerCount: 1,
            polygonCount: 1,
            minCornerY: pivot.Y,
            maxCornerY: pivot.Y,
            patchCenter: pivot,
            pivot: pivot,
            lever: lever);
        return true;
    }

    private bool TryGetSupportState(Entity entity, bool requireStableSupport, out BoxSupportState supportState, out Aabb supportAabb)
    {
        supportState = default;
        supportAabb = default;

        if (!TryCreatePhysicsWorldCollider(entity, out var collider) ||
            !TryGetPhysicsBodyCenter(entity, out Vector3 bodyCenter))
        {
            return false;
        }

        if (TryGetLastSupportManifold(entity, out ContactManifold supportManifold) &&
            (TryBuildPromotedFlatBoxSupportState(entity, collider, bodyCenter, supportManifold, out supportState) ||
             TryBuildSupportStateFromContactManifold(bodyCenter, supportManifold, out supportState)))
        {
            return !requireStableSupport || supportState.Stable;
        }

        if (!TryGetSupportingSurface(entity, requireCenterSupport: requireStableSupport, out supportAabb))
            return false;

        return TryBuildFallbackSupportState(entity, collider, bodyCenter, supportAabb, out supportState);
    }

    private static Vector3 GetCapsuleSupportPatchCenter(WorldCollider collider)
    {
        collider.GetCapsuleSegment(out Vector3 a, out Vector3 b);
        const float patchTolerance = 0.03f;

        Vector3 segmentSupportCenter;
        if (MathF.Abs(a.Y - b.Y) <= patchTolerance)
            segmentSupportCenter = (a + b) * 0.5f;
        else
            segmentSupportCenter = a.Y < b.Y ? a : b;

        return segmentSupportCenter - Vector3.UnitY * collider.Radius;
    }

    private bool TryApplyGravityToppleTorque(Entity entity, float dt, ref Vector3 angularVelocity)
    {
        if (!TryGetSupportState(entity, requireStableSupport: false, out BoxSupportState supportState, out _))
        {
            return false;
        }

        Vector3 lever = supportState.Lever;

        float leverLenSq = lever.LengthSquared();
        if (leverLenSq < 1e-6f)
            return false;

        Vector3 torqueAxis = Vector3.Cross(lever, -Vector3.UnitY);
        if (!TryNormalize(torqueAxis, out torqueAxis))
            return false;

        float leverLen = MathF.Sqrt(leverLenSq);
        float radius = MathF.Max(0.05f, GetBodyRotationRadius(entity));
        float strengthScale = GetPhysicsBodyShape(entity) switch
        {
            RuntimeShapeKind.Box => 1.1f,
            RuntimeShapeKind.Capsule => 0.8f,
            RuntimeShapeKind.Sphere => 0.35f,
            _ => 0.5f
        };

        float angularAccel = _movement.Gravity * (leverLen / radius) * strengthScale;
        angularVelocity += torqueAxis * (angularAccel * dt);
        return true;
    }

    private bool TryApplyBoxSupportToppleTorque(Entity entity, float dt, ref Vector3 angularVelocity, out BoxSupportState supportState)
    {
        supportState = default;

        if (GetPhysicsBodyShape(entity) != RuntimeShapeKind.Box)
        {
            return false;
        }

        Vector3 halfExtents = entity.Physics.BoxBody?.HalfExtents ?? entity.Render.Size * 0.5f;

        if (!TryGetSupportState(entity, requireStableSupport: false, out supportState, out _))
        {
            return false;
        }

        float leverLenSq = supportState.Lever.LengthSquared();
        bool faceSupported = supportState.PolygonCount >= 3 && supportState.CornerCount >= 3;
        bool nearStablePose = IsNearStableBoxPose(entity, minAlignment: 0.985f);

        if (supportState.Stable && faceSupported && nearStablePose)
        {
            Vector3 linearVelocity = GetPhysicsBodyVelocity(entity);
            float lateralSpeedSq = linearVelocity.X * linearVelocity.X + linearVelocity.Z * linearVelocity.Z;
            float settleDamping = lateralSpeedSq < 0.36f
                ? Math.Clamp(1f - dt * 14f, 0f, 1f)
                : Math.Clamp(1f - dt * 8f, 0f, 1f);

            angularVelocity *= settleDamping;
            if (angularVelocity.LengthSquared() < 0.0225f)
                angularVelocity = Vector3.Zero;

            float verticalSpeed = MathF.Abs(linearVelocity.Y);
            float angularSpeedSq = angularVelocity.LengthSquared();
            if (lateralSpeedSq < 0.16f && verticalSpeed < 0.2f && angularSpeedSq < 0.36f)
            {
                Quaternion stableRotation = AlignQuaternionShortestPath(
                    entity.Physics.Rotation,
                    GetNearestBoxStableRotation(entity.Physics.Rotation));

                float alignment = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(entity.Physics.Rotation), stableRotation));
                if (alignment < 0.99995f)
                {
                    float settleRate = supportState.PolygonCount >= 4 ? 10f : 7f;
                    float t = Math.Clamp(dt * settleRate, 0f, 1f);
                    entity.Physics.Rotation = Quaternion.Normalize(Quaternion.Slerp(entity.Physics.Rotation, stableRotation, t));

                    if (alignment > 0.9995f)
                    {
                        entity.Physics.Rotation = stableRotation;
                        angularVelocity = Vector3.Zero;
                    }
                }
            }

            return true;
        }

        float leverTolerance = faceSupported && nearStablePose
            ? MathF.Max(0.02f, MathF.Max(halfExtents.X, halfExtents.Z) * 0.08f)
            : 0.001f;

        if (leverLenSq < leverTolerance * leverTolerance)
            return true;

        Vector3 torqueAxis = Vector3.Cross(supportState.Lever, -Vector3.UnitY);
        if (!TryNormalize(torqueAxis, out torqueAxis))
            return true;

        float leverLen = MathF.Sqrt(leverLenSq);
        float radius = MathF.Max(0.05f, GetBodyRotationRadius(entity));
        float strengthScale = supportState.CornerCount switch
        {
            1 => 4.5f,
            2 => 3.25f,
            _ => 1.35f
        };

        if (!supportState.Stable)
            strengthScale *= 1.5f;

        float angularAccel = _movement.Gravity * (leverLen / radius) * strengthScale;
        angularVelocity += torqueAxis * (angularAccel * dt);
        return true;
    }

    private static Vector3 GetCollisionContactOffset(Entity entity, Vector3 surfaceNormal, ContactManifold manifold)
    {
        surfaceNormal = surfaceNormal.LengthSquared() > 1e-6f
            ? Vector3.Normalize(surfaceNormal)
            : Vector3.UnitY;

        if (manifold.HasContact && TryGetPhysicsBodyCenter(entity, out Vector3 bodyCenter))
        {
            Vector3 manifoldOffset = manifold.GetAveragePoint() - bodyCenter;
            if (manifoldOffset.LengthSquared() > 1e-6f)
                return manifoldOffset;
        }

        RuntimeShapeKind shape = GetPhysicsBodyShape(entity);
        if (shape == RuntimeShapeKind.Sphere)
            return -surfaceNormal * GetBodyRotationRadius(entity);

        if (shape == RuntimeShapeKind.Capsule)
        {
            float radius = GetBodyRotationRadius(entity);
            float height = entity.Physics.CapsuleBody?.Height ?? entity.Render.Height;
            float cylinderHalf = MathF.Max(0f, height * 0.5f - radius);
            Vector3 axis = Vector3.Transform(Vector3.UnitY, entity.Physics.Rotation);
            Vector3 velocity = GetPhysicsBodyVelocity(entity);
            Vector3 sideSense = Vector3.Cross(axis, surfaceNormal);
            float axisSign = 1f;

            if (sideSense.LengthSquared() > 1e-6f)
            {
                float sense = Vector3.Dot(velocity, sideSense);
                if (MathF.Abs(sense) > 1e-4f)
                    axisSign = MathF.Sign(sense);
            }

            return axis * (cylinderHalf * axisSign) - surfaceNormal * radius;
        }

        if (shape == RuntimeShapeKind.Box)
        {
            Quaternion rotation = entity.Physics.Rotation;
            Quaternion invRotation = Quaternion.Conjugate(rotation);
            Vector3 localNormal = Vector3.Transform(surfaceNormal, invRotation);
            Vector3 halfExtents = entity.Render.Size * 0.5f;
            float ax = MathF.Abs(localNormal.X);
            float ay = MathF.Abs(localNormal.Y);
            float az = MathF.Abs(localNormal.Z);
            Vector3 localOffset;

            if (ax >= ay && ax >= az)
                localOffset = new Vector3(NormalSign(-localNormal.X) * halfExtents.X, 0f, 0f);
            else if (ay >= ax && ay >= az)
                localOffset = new Vector3(0f, NormalSign(-localNormal.Y) * halfExtents.Y, 0f);
            else
                localOffset = new Vector3(0f, 0f, NormalSign(-localNormal.Z) * halfExtents.Z);

            return Vector3.Transform(localOffset, rotation);
        }

        return -surfaceNormal * GetBodyRotationRadius(entity);
    }

    private void ApplyCollisionSpin(Entity entity, Vector3 surfaceNormal, Vector3 velocityDelta, ContactManifold manifold, float intensityScale = 1f)
    {
        if (!HasPhysicsBody(entity) || entity.Physics.MotionType != MotionType.Dynamic)
            return;

        if (velocityDelta.LengthSquared() < 0.0025f)
            return;

        Vector3 contactOffset = GetCollisionContactOffset(entity, surfaceNormal, manifold);
        Vector3 torque = Vector3.Cross(contactOffset, velocityDelta);
        float torqueLenSq = torque.LengthSquared();
        if (torqueLenSq < 1e-6f)
            return;

        Vector3 axis = torque / MathF.Sqrt(torqueLenSq);
        float radius = MathF.Max(0.05f, contactOffset.Length());
        float shapeScale = GetPhysicsBodyShape(entity) switch
        {
            RuntimeShapeKind.Box => 0.55f,
            RuntimeShapeKind.Capsule => 0.45f,
            RuntimeShapeKind.Sphere => 0.35f,
            _ => 1f
        };

        AddAngularVelocity(entity, axis * (velocityDelta.Length() / radius) * shapeScale * intensityScale);
    }

    private void ApplyWorldCollisionSpin(Entity entity, Vector3 predictedVelocity, Vector3 actualVelocity, bool hadContact, Vector3 contactNormal, ContactManifold manifold)
    {
        if (!hadContact || contactNormal.LengthSquared() < 1e-6f)
            return;

        Vector3 velocityDelta = actualVelocity - predictedVelocity;
        if (velocityDelta.LengthSquared() < 0.01f)
            return;

        Vector3 surfaceNormal = Vector3.Normalize(-contactNormal);

        RuntimeShapeKind shape = GetPhysicsBodyShape(entity);
        if ((shape == RuntimeShapeKind.Box || shape == RuntimeShapeKind.Capsule) && surfaceNormal.Y > 0.85f)
        {
            Vector3 tangentialDelta = ProjectOntoPlane(velocityDelta, surfaceNormal);
            Vector3 lateralVelocity = new(actualVelocity.X, 0f, actualVelocity.Z);
            bool stableFloorBox = shape == RuntimeShapeKind.Box &&
                                  manifold.ContactCount >= 3 &&
                                  IsNearStableBoxPose(entity, minAlignment: 0.985f) &&
                                  lateralVelocity.LengthSquared() < 1.44f;

            if (stableFloorBox)
                return;

            bool suppressFlatFloorSpin = tangentialDelta.LengthSquared() < 0.04f &&
                                         lateralVelocity.LengthSquared() < 0.36f;

            if (suppressFlatFloorSpin)
            {
                return;
            }
        }

        ApplyCollisionSpin(entity, surfaceNormal, velocityDelta, manifold, intensityScale: 0.8f);
    }

    private bool IsBodySupportedByWorld(Entity entity)
    {
        return TryGetSupportState(entity, requireStableSupport: true, out _, out _);
    }

    private void IntegrateEntityRotation(Entity entity, float dt)
    {
        if (!HasPhysicsBody(entity))
            return;

        Vector3 angularVelocity = entity.Physics.AngularVelocity;
        RuntimeShapeKind shape = GetPhysicsBodyShape(entity);
        bool touchingSupport = TryGetSupportState(entity, requireStableSupport: false, out _, out _);
        float damping = MathF.Max(0f, 1f - entity.Physics.AngularDamping * dt);
        angularVelocity *= damping;

        if (shape == RuntimeShapeKind.Capsule && touchingSupport)
        {
            Vector3 axis = Vector3.Transform(Vector3.UnitY, entity.Physics.Rotation);
            float axisUp = MathF.Abs(Vector3.Dot(axis, Vector3.UnitY));
            if (axisUp < 0.45f)
            {
                Vector3 lateral = new(GetPhysicsBodyVelocity(entity).X, 0f, GetPhysicsBodyVelocity(entity).Z);
                float lateralSpeed = lateral.Length();
                if (lateralSpeed > 0.08f && TryNormalize(axis, out var axisDir))
                {
                    Vector3 rollDirection = Vector3.Cross(axisDir, -Vector3.UnitY);
                    if (TryNormalize(rollDirection, out var rollDir))
                    {
                        float radius = MathF.Max(0.05f, entity.Physics.CapsuleBody?.Radius ?? entity.Render.Radius);
                        float signedSpeed = Vector3.Dot(lateral, rollDir);
                        Vector3 targetAngular = axisDir * (signedSpeed / radius);
                        float rollFollow = Math.Clamp(dt * 5f, 0f, 1f);
                        angularVelocity += (targetAngular - angularVelocity) * rollFollow;
                    }
                }
            }
        }

        if (touchingSupport && (shape == RuntimeShapeKind.Box || shape == RuntimeShapeKind.Capsule))
        {
            if (shape == RuntimeShapeKind.Box)
            {
                TryApplyBoxSupportToppleTorque(entity, dt, ref angularVelocity, out _);
            }
            else
            {
                TryApplyGravityToppleTorque(entity, dt, ref angularVelocity);
            }
        }

        if (angularVelocity.LengthSquared() < 0.0025f)
            angularVelocity = Vector3.Zero;

        float angularSpeed = angularVelocity.Length();
        if (angularSpeed > 1e-5f)
        {
            Vector3 axis = angularVelocity / angularSpeed;
            Quaternion delta = Quaternion.CreateFromAxisAngle(axis, angularSpeed * dt);
            entity.Physics.Rotation = Quaternion.Normalize(delta * entity.Physics.Rotation);
        }

        entity.Physics.AngularVelocity = angularVelocity;
        SyncEntityRotationFromPhysics(entity);
    }

    private Vector3 PredictVelocityWithoutCollision(Vector3 velocity, bool useGravity, float linearDamping, float dt)
    {
        if (useGravity)
            velocity = new Vector3(velocity.X, velocity.Y - _movement.Gravity * dt, velocity.Z);

        if (linearDamping > 0f)
        {
            float k = MathF.Max(0f, 1f - linearDamping * dt);
            velocity *= k;
        }

        return velocity;
    }

    private void StepPhysicsBody(Entity entity, float dt)
    {
        if (entity.Physics.BoxBody != null)
        {
            Vector3 predictedVelocity = PredictVelocityWithoutCollision(
                entity.Physics.BoxBody.Velocity,
                entity.Physics.BoxBody.UseGravity,
                entity.Physics.BoxBody.LinearDamping,
                dt);

            entity.Physics.BoxBody.Step(dt, _runtimeWorldColliders, entity.Physics.Rotation, gravityY: _movement.Gravity);
            ApplyWorldCollisionSpin(
                entity,
                predictedVelocity,
                entity.Physics.BoxBody.Velocity,
                entity.Physics.BoxBody.HadContact,
                entity.Physics.BoxBody.LastContactNormal,
                entity.Physics.BoxBody.LastContactManifold);
            IntegrateEntityRotation(entity, dt);
            return;
        }

        if (entity.Physics.SphereBody != null)
        {
            Vector3 predictedVelocity = PredictVelocityWithoutCollision(
                entity.Physics.SphereBody.Velocity,
                entity.Physics.SphereBody.UseGravity,
                entity.Physics.SphereBody.LinearDamping,
                dt);

            entity.Physics.SphereBody.Step(dt, _runtimeWorldColliders, gravityY: _movement.Gravity);
            ApplyWorldCollisionSpin(
                entity,
                predictedVelocity,
                entity.Physics.SphereBody.Velocity,
                entity.Physics.SphereBody.HadContact,
                entity.Physics.SphereBody.LastContactNormal,
                entity.Physics.SphereBody.LastContactManifold);
            IntegrateEntityRotation(entity, dt);
            return;
        }

        if (entity.Physics.CapsuleBody != null)
        {
            Vector3 predictedVelocity = PredictVelocityWithoutCollision(
                entity.Physics.CapsuleBody.Velocity,
                entity.Physics.CapsuleBody.UseGravity,
                entity.Physics.CapsuleBody.LinearDamping,
                dt);

            entity.Physics.CapsuleBody.Step(dt, _runtimeWorldColliders, entity.Physics.Rotation, gravityY: _movement.Gravity);
            ApplyWorldCollisionSpin(
                entity,
                predictedVelocity,
                entity.Physics.CapsuleBody.Velocity,
                entity.Physics.CapsuleBody.HadContact,
                entity.Physics.CapsuleBody.LastContactNormal,
                entity.Physics.CapsuleBody.LastContactManifold);
            IntegrateEntityRotation(entity, dt);
        }
    }

    private bool TryGetSupportingPlatformForBody(
        Entity entity,
        bool usePreviousPlatformPosition,
        out Entity? platform)
    {
        platform = null;

        if (!TryGetPhysicsBodyCenter(entity, out Vector3 center) ||
            !TryGetPhysicsBodyAabb(entity, out Aabb bodyAabb))
        {
            return false;
        }

        const float yTolerance = 0.08f;
        const float edgeInset = 0.02f;
        const float supportSurfaceTolerance = 0.12f;
        const float supportLateralTolerance = 0.05f;

        bool hasSupportManifold = TryGetLastSupportManifold(entity, out ContactManifold supportManifold);

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!e.Collider.IsMovingPlatform) continue;

            Vector3 p = usePreviousPlatformPosition
                ? e.Collider.PreviousPosition
                : e.Transform.Position;
            Quaternion rot = GetColliderRotation(e);
            WorldCollider supportCollider = CreateWorldCollider(e.Collider, p, rot);

            if (hasSupportManifold)
            {
                if (!DoesSupportManifoldMatchPlatform(
                        supportManifold,
                        supportCollider,
                        supportSurfaceTolerance,
                        supportLateralTolerance))
                {
                    continue;
                }
            }
            else
            {
                Aabb supportAabb = supportCollider.GetAabb();
                if (!IsBodyCenterSupportedBySurface(center, bodyAabb, supportAabb, yTolerance, edgeInset))
                    continue;
            }

            platform = e;
            return true;
        }

        return false;
    }

    private static bool DoesSupportManifoldMatchPlatform(
        ContactManifold manifold,
        WorldCollider platformCollider,
        float surfaceTolerance,
        float lateralTolerance)
    {
        if (!manifold.HasContact)
            return false;

        if (platformCollider.Shape != WorldColliderShape.Box)
            return false;

        Quaternion inv = Quaternion.Conjugate(platformCollider.Rotation);
        int supportingPoints = 0;

        for (int i = 0; i < manifold.ContactCount; i++)
        {
            Vector3 localPoint = Vector3.Transform(manifold.GetPoint(i) - platformCollider.Center, inv);
            bool onTopFace = MathF.Abs(localPoint.Y - platformCollider.HalfExtents.Y) <= surfaceTolerance;
            bool insideLateral =
                MathF.Abs(localPoint.X) <= platformCollider.HalfExtents.X + lateralTolerance &&
                MathF.Abs(localPoint.Z) <= platformCollider.HalfExtents.Z + lateralTolerance;

            if (onTopFace && insideLateral)
                supportingPoints++;
        }

        return supportingPoints > 0;
    }

    private static bool ShouldStickToMovingPlatform(Entity entity)
        => entity.Collider.Shape == RuntimeShapeKind.Box ||
           entity.Collider.Shape == RuntimeShapeKind.Capsule;

    private void CarryDynamicBoxesOnMovingPlatforms()
    {
        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!HasPhysicsBody(e)) continue;
            if (e.IsHeld) continue;
            if (e.Physics.MotionType != MotionType.Dynamic) continue;
            if (!ShouldStickToMovingPlatform(e)) continue;

            if (!TryGetSupportingPlatformForBody(
                    e,
                    usePreviousPlatformPosition: true,
                    out var previousSupport))
            {
                continue;
            }

            if (!TryGetPhysicsBodyCenter(e, out Vector3 center))
                continue;

            center += previousSupport!.Collider.Delta;
            SetPhysicsBodyCenter(e, center);
            e.Transform.Position = center;
        }
    }


    private bool TryGetGroundSupportDelta(out Vector3 delta)
        => TryGetGroundSupport(out _, out delta);

    private bool TryGetGroundSupport(out Entity? supportEntity, out Vector3 delta)
    {
        supportEntity = null;
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

            Aabb aabb = CreateWorldCollider(e).GetAabb();
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
            supportEntity = e;
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
            e.Physics.RestRotation = EulerDegToQuat(def.LocalRotationEulerDeg);
            e.Physics.Rotation = EulerDegToQuat(def.LocalRotationEulerDeg);
            e.Physics.AngularVelocity = Vector3.Zero;
            e.Physics.AngularDamping = 2.5f;
            e.Render.Shape = RuntimeShapeKind.Box;
            e.Render.Size = GetScaledSize(def, clampComponents: false);
            e.Render.Radius = GetScaledSphereRadius(def, clampScale: false);
            e.Render.Height = GetScaledCapsuleHeight(def, clampScale: false);
            e.Render.Color = (Vector4)def.Color;

            e.CanPickUp = def.CanPickUp;
            e.Physics.MotionType = def.MotionType;
            e.Physics.BoxBody = null;
            e.Physics.SphereBody = null;
            e.Physics.CapsuleBody = null;

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
            RuntimeShapeKind rigidBodyShape = isRigidBody ? GetRigidBodyShape(def) : RuntimeShapeKind.Box;
            bool isSphere = isRigidBody && rigidBodyShape == RuntimeShapeKind.Sphere;
            bool isCapsule = isRigidBody && rigidBodyShape == RuntimeShapeKind.Capsule;
            bool isBoxPhysicsShape =
                (def.Type == EntityTypes.Box) ||
                (isRigidBody && rigidBodyShape == RuntimeShapeKind.Box);

            if (isSphere)
            {
                float radius = GetScaledSphereRadius(def, clampScale: true);
                e.Render.Shape = RuntimeShapeKind.Sphere;
                e.Render.Size = new Vector3(radius * 2f);
                e.Render.Radius = radius;
            }
            else if (isCapsule)
            {
                float radius = GetScaledCapsuleRadius(def, clampScale: true);
                float height = GetScaledCapsuleHeight(def, clampScale: true);
                e.Render.Shape = RuntimeShapeKind.Capsule;
                e.Render.Size = new Vector3(radius * 2f, height, radius * 2f);
                e.Render.Radius = radius;
                e.Render.Height = height;
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
            else if (isCapsule && def.MotionType == MotionType.Dynamic)
            {
                float radius = GetScaledCapsuleRadius(def, clampScale: true);
                float height = GetScaledCapsuleHeight(def, clampScale: true);

                e.Physics.CapsuleBody = new CapsuleBody(e.Transform.Position, radius, height)
                {
                    Mass = def.Mass,
                    UseGravity = true,
                    IsKinematic = false,
                    Friction = def.Friction,
                    Restitution = def.Restitution,
                    LinearDamping = 0.02f
                };
            }
            else if (isCapsule && def.MotionType == MotionType.Kinematic)
            {
                float radius = GetScaledCapsuleRadius(def, clampScale: true);
                float height = GetScaledCapsuleHeight(def, clampScale: true);

                e.Physics.CapsuleBody = new CapsuleBody(e.Transform.Position, radius, height)
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
                e.Collider.Height = radius * 2f;
                e.Collider.IsMovingPlatform = false;
                e.Collider.PreviousPosition = e.Transform.Position;
                e.Collider.Delta = Vector3.Zero;
            }
            else if (isCapsule)
            {
                float radius = GetScaledCapsuleRadius(def, clampScale: true);
                float height = GetScaledCapsuleHeight(def, clampScale: true);
                e.Collider.Shape = RuntimeShapeKind.Capsule;
                e.Collider.Radius = radius;
                e.Collider.Height = height;
                e.Collider.Size = new Vector3(radius * 2f, height, radius * 2f);
                e.Collider.IsMovingPlatform = false;
                e.Collider.PreviousPosition = e.Transform.Position;
                e.Collider.Delta = Vector3.Zero;
            }
            else if (isBoxPhysicsShape || hasMovingPlatform)
            {
                e.Collider.Shape = RuntimeShapeKind.Box;
                e.Collider.Size = GetScaledSize(def, clampComponents: true);
                e.Collider.Radius = 0.5f;
                e.Collider.Height = e.Collider.Size.Y;
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

            _runtimeWorldColliders.Add(CreateWorldCollider(e));
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
                e.Physics.RestRotation = EulerDegToQuat(e.Transform.RotationEulerDeg);
                e.Physics.Rotation = EulerDegToQuat(e.Transform.RotationEulerDeg);
                e.Physics.AngularVelocity = Vector3.Zero;
            }
        }

        // 3) Compute platform deltas from prev -> current
        ComputeRuntimeColliderDeltasFromPrev();

        // 4) Carry supported dynamic boxes once per fixed tick.
        CarryDynamicBoxesOnMovingPlatforms();

        // 4b) Carry the player early when directly supported by a moving platform.
        if (TryGetGroundSupport(out Entity? earlySupport, out var earlyDelta) &&
            earlySupport?.Collider.IsMovingPlatform == true)
        {
            _motor.Position += earlyDelta;
        }

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
                    TryGetSupportingPlatformForBody(
                        e,
                        usePreviousPlatformPosition: false,
                        out _))
                {
                    // Keep boxes settled on a supporting platform after the carry step.
                    Vector3 velocity = GetPhysicsBodyVelocity(e);
                    if (velocity.Y < 0f)
                        SetPhysicsBodyVelocity(e, new Vector3(velocity.X, 0f, velocity.Z));
                }

                if (TryGetPhysicsBodyCenter(e, out Vector3 center))
                    e.Transform.Position = center;
            }

            ResolveDynamicDynamic(iterations: 3);
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
        if (TryGetGroundSupport(out Entity? lateSupport, out var platDelta) &&
            lateSupport?.Collider.IsMovingPlatform != true)
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
        WorldCollider playerCollider = WorldCollider.Box(playerCenter, playerExt, Quaternion.Identity);

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

            if (!TryCreatePhysicsWorldCollider(e, out WorldCollider bodyCollider) ||
                !TryGetPhysicsBodyCenter(e, out Vector3 bodyCenter))
            {
                continue;
            }

            if (!ShapeCollision.TryResolve(playerCollider, bodyCollider, out ContactManifold playerContact))
                continue;

            if (IsPlayerStandingOnDynamicBody(playerCenter, playerExt, e, playerContact))
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

    private bool IsPlayerStandingOnDynamicBody(Vector3 playerCenter, Vector3 playerExtents, Entity body, ContactManifold contact)
    {
        if (_motor.Velocity.Y > 0.05f)
            return false;

        if (!contact.HasContact)
            return false;

        if (contact.SurfaceNormal.Y < 0.45f)
            return false;

        if (!TryCreatePhysicsWorldCollider(body, out WorldCollider bodyCollider))
            return false;

        float feetY = playerCenter.Y - playerExtents.Y;
        float bodyTopY = float.NegativeInfinity;
        for (int i = 0; i < contact.ContactCount; i++)
            bodyTopY = MathF.Max(bodyTopY, contact.GetPoint(i).Y);

        const float yTolerance = 0.18f;

        if (feetY < bodyTopY - yTolerance || feetY > bodyTopY + yTolerance)
            return false;

        Vector3 localPoint = Vector3.Transform(contact.GetAveragePoint() - bodyCollider.Center, Quaternion.Conjugate(bodyCollider.Rotation));
        bool insideLateral = bodyCollider.Shape switch
        {
            WorldColliderShape.Box =>
                MathF.Abs(localPoint.X) <= bodyCollider.HalfExtents.X + 0.05f &&
                MathF.Abs(localPoint.Z) <= bodyCollider.HalfExtents.Z + 0.05f,
            WorldColliderShape.Capsule =>
                (new Vector2(localPoint.X, localPoint.Z)).LengthSquared() <=
                (bodyCollider.Radius + 0.05f) * (bodyCollider.Radius + 0.05f),
            WorldColliderShape.Sphere =>
                (new Vector2(localPoint.X, localPoint.Z)).LengthSquared() <=
                (bodyCollider.Radius + 0.05f) * (bodyCollider.Radius + 0.05f),
            _ => false
        };

        if (!insideLateral)
            return false;

        return playerCenter.Y >= bodyCollider.Center.Y;
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

                RuntimeShapeKind shape =
                    d.IsSphere
                        ? (d.Size.Y > d.Size.X + 0.0001f ? RuntimeShapeKind.Capsule : RuntimeShapeKind.Sphere)
                        : RuntimeShapeKind.Box;
                float radius = d.Size.X * 0.5f;
                float height = d.Size.Y;

                DrawPrimitive(renderer, shape, d.Position, d.Size, d.Rotation, color, radius, height);
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
                        bool isCapsule = e.Type == EntityTypes.RigidBody && IsCapsuleShape(e.Shape);
                        float capsuleRadius = MathF.Max(0.01f, e.Radius) * MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Z));
                        float capsuleHeight = MathF.Max(MathF.Max(0.01f, e.Height) * MathF.Max(0.01f, MathF.Abs(scale.Y)), capsuleRadius * 2f);
                        Vector3 size = isSphere
                            ? new Vector3(MathF.Max(0.01f, e.Radius) * MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z))) * 2f)
                            : isCapsule
                                ? new Vector3(
                                    capsuleRadius * 2f,
                                    capsuleHeight,
                                    capsuleRadius * 2f)
                                : Mul((Vector3)e.Size, scale);
                        Quaternion colliderRot = isSphere ? Quaternion.Identity : rot;

                        bool selected = (i == _editor.SelectedEntityIndex);

                        Vector4 col = selected
                            ? new Vector4(0.2f, 1f, 0.2f, 1f)
                            : new Vector4(0.1f, 0.8f, 0.1f, 1f);

                        if (isSphere)
                        {
                            float radius = size.X * 0.5f;
                            DrawWireSphere(renderer, pos, radius, Quaternion.Identity, col, _editor.ColliderLineThickness);
                        }
                        else if (isCapsule)
                        {
                            DrawWireCapsule(renderer, pos, capsuleRadius, capsuleHeight, colliderRot, col, _editor.ColliderLineThickness);
                        }
                        else
                        {
                            DrawWireObb(renderer, pos, size, colliderRot, col, _editor.ColliderLineThickness);
                        }

                        if (_editor.ShowColliderCorners && !isSphere && !isCapsule)
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

            DrawPrimitive(renderer, ent.Render.Shape, pos, size, rot, col, ent.Render.Radius, ent.Render.Height);
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

    private void DrawWireSphere(Renderer renderer, Vector3 center, float radius, Quaternion rot, Vector4 color, float thickness)
    {
        const int segments = 24;
        DrawWireCircle(renderer, center, radius, rot, Vector3.UnitX, Vector3.UnitY, color, thickness, segments);
        DrawWireCircle(renderer, center, radius, rot, Vector3.UnitX, Vector3.UnitZ, color, thickness, segments);
        DrawWireCircle(renderer, center, radius, rot, Vector3.UnitY, Vector3.UnitZ, color, thickness, segments);
    }

    private void DrawWireCapsule(Renderer renderer, Vector3 center, float radius, float height, Quaternion rot, Vector4 color, float thickness)
    {
        const int segments = 24;
        float clampedRadius = MathF.Max(0.01f, radius);
        float clampedHeight = MathF.Max(height, clampedRadius * 2f);
        float halfStraight = MathF.Max(0f, clampedHeight * 0.5f - clampedRadius);

        Vector3 up = Vector3.Transform(Vector3.UnitY, rot);
        Vector3 right = Vector3.Transform(Vector3.UnitX, rot);
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, rot);

        Vector3 topCenter = center + up * halfStraight;
        Vector3 bottomCenter = center - up * halfStraight;

        DrawWireCircle(renderer, topCenter, clampedRadius, Quaternion.Identity, right, forward, color, thickness, segments);
        DrawWireCircle(renderer, bottomCenter, clampedRadius, Quaternion.Identity, right, forward, color, thickness, segments);

        DrawEdgeBox(renderer, topCenter + right * clampedRadius, bottomCenter + right * clampedRadius, thickness, color);
        DrawEdgeBox(renderer, topCenter - right * clampedRadius, bottomCenter - right * clampedRadius, thickness, color);
        DrawEdgeBox(renderer, topCenter + forward * clampedRadius, bottomCenter + forward * clampedRadius, thickness, color);
        DrawEdgeBox(renderer, topCenter - forward * clampedRadius, bottomCenter - forward * clampedRadius, thickness, color);

        DrawWireArc(renderer, topCenter, clampedRadius, right, up, color, thickness, segments / 2, 0f, MathF.PI);
        DrawWireArc(renderer, topCenter, clampedRadius, forward, up, color, thickness, segments / 2, 0f, MathF.PI);
        DrawWireArc(renderer, bottomCenter, clampedRadius, right, -up, color, thickness, segments / 2, 0f, MathF.PI);
        DrawWireArc(renderer, bottomCenter, clampedRadius, forward, -up, color, thickness, segments / 2, 0f, MathF.PI);
    }

    private void DrawWireCircle(
        Renderer renderer,
        Vector3 center,
        float radius,
        Quaternion rot,
        Vector3 axisA,
        Vector3 axisB,
        Vector4 color,
        float thickness,
        int segments)
    {
        Vector3 basisA = Vector3.Transform(Vector3.Normalize(axisA), rot);
        Vector3 basisB = Vector3.Transform(Vector3.Normalize(axisB), rot);
        DrawWireArc(renderer, center, radius, basisA, basisB, color, thickness, segments, 0f, MathF.Tau);
    }

    private void DrawWireArc(
        Renderer renderer,
        Vector3 center,
        float radius,
        Vector3 axisA,
        Vector3 axisB,
        Vector4 color,
        float thickness,
        int segments,
        float startAngle,
        float endAngle)
    {
        Vector3 basisA = Vector3.Normalize(axisA);
        Vector3 basisB = Vector3.Normalize(axisB);
        int count = Math.Max(4, segments);
        float step = (endAngle - startAngle) / count;

        Vector3 prev = center + (basisA * MathF.Cos(startAngle) + basisB * MathF.Sin(startAngle)) * radius;
        for (int i = 1; i <= count; i++)
        {
            float angle = startAngle + step * i;
            Vector3 next = center + (basisA * MathF.Cos(angle) + basisB * MathF.Sin(angle)) * radius;
            DrawEdgeBox(renderer, prev, next, thickness, color);
            prev = next;
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

    private void DrawPrimitive(Renderer renderer, RuntimeShapeKind shape, Vector3 pos, Vector3 size, Quaternion rot, Vector4 color, float radius = 0.5f, float height = 1f)
    {
        if (shape == RuntimeShapeKind.Capsule)
        {
            DrawCapsulePrimitive(renderer, pos, rot, radius, height, color);
            return;
        }

        var model = Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);

        if (shape == RuntimeShapeKind.Sphere)
            _world.DrawSphere(renderer.CommandList, model, color);
        else
            _world.DrawBox(renderer.CommandList, model, color);
    }

    private void DrawCapsulePrimitive(Renderer renderer, Vector3 pos, Quaternion rot, float radius, float height, Vector4 color)
    {
        float clampedRadius = MathF.Max(0.01f, radius);
        float clampedHeight = MathF.Max(height, clampedRadius * 2f);
        float cylinderHeight = MathF.Max(0f, clampedHeight - clampedRadius * 2f);

        if (cylinderHeight > 0.0001f)
        {
            var cylinderModel =
                Matrix4x4.CreateScale(new Vector3(clampedRadius * 2f, cylinderHeight, clampedRadius * 2f)) *
                Matrix4x4.CreateFromQuaternion(rot) *
                Matrix4x4.CreateTranslation(pos);
            _world.DrawCylinder(renderer.CommandList, cylinderModel, color);
        }

        Vector3 capOffset = Vector3.Transform(
            Vector3.UnitY * MathF.Max(0f, clampedHeight * 0.5f - clampedRadius),
            rot);
        var sphereScale = Matrix4x4.CreateScale(new Vector3(clampedRadius * 2f));

        var topModel = sphereScale * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos + capOffset);
        var bottomModel = sphereScale * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos - capOffset);

        _world.DrawSphere(renderer.CommandList, topModel, color);
        _world.DrawSphere(renderer.CommandList, bottomModel, color);
    }

    private static bool RayIntersectsEntityCollider(in Engine.Physics.Collision.Ray ray, Entity entity, float tMin, float tMax, out float hitT)
    {
        if (!entity.Collider.Enabled)
        {
            hitT = 0f;
            return false;
        }

        WorldCollider collider = CreateWorldCollider(entity);

        if (entity.Collider.Shape == RuntimeShapeKind.Sphere)
        {
            return Engine.Physics.Collision.Raycast.RayIntersectsSphere(
                ray,
                collider.Center,
                collider.Radius,
                tMin,
                tMax,
                out hitT);
        }

        if (entity.Collider.Shape == RuntimeShapeKind.Capsule)
        {
            return Engine.Physics.Collision.Raycast.RayIntersectsCapsule(
                ray,
                collider.Center,
                collider.Radius,
                collider.Height,
                collider.Rotation,
                tMin,
                tMax,
                out hitT);
        }

        return Engine.Physics.Collision.Raycast.RayIntersectsObb(
            ray,
            collider.Center,
            collider.HalfExtents,
            collider.Rotation,
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

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var e = _runtimeEntities[i];
            if (!e.Collider.Enabled) continue;

            if (!e.CanPickUp)
                continue;


            if (!HasPhysicsBody(e))
            {
                continue;
            }

            if (e.IsHeld) continue;
            if (IsPhysicsBodyKinematic(e)) continue;
            if (GetPhysicsBodyMass(e) > _pickupMaxMass) continue;

            if (RayIntersectsEntityCollider(ray, e, 0.05f, bestT, out float tHit))
            {
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
            if (_held != null)
                DropHeld();
            else
            {
                var hit = RaycastPickable(maxDist: 3.0f);

                if (hit != null)
                {
                    PickUp(hit);
                }
            }
        }
    }
    private void PickUp(Entity e)
    {
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

        if (e.Physics.CapsuleBody != null)
        {
            e.Physics.CapsuleBody.IsKinematic = true;
            e.Physics.CapsuleBody.UseGravity = false;
            e.Physics.CapsuleBody.Velocity = Vector3.Zero;
        }

        e.Physics.AngularVelocity = Vector3.Zero;

    }

    private void DropHeld()
    {
        if (_held == null) return;
        var e = _held;

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

        if (e.Physics.CapsuleBody != null)
        {
            e.Physics.CapsuleBody.IsKinematic = false;
            e.Physics.CapsuleBody.UseGravity = true;
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

        if (e.Physics.CapsuleBody != null)
        {
            e.Physics.CapsuleBody.IsKinematic = false;
            e.Physics.CapsuleBody.UseGravity = true;

            e.Physics.CapsuleBody.Velocity = dir * _throwSpeed + _motor.Velocity * 0.5f;
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

        Vector3 x = _held.Physics.BoxBody?.Center ?? _held.Physics.SphereBody?.Center ?? _held.Physics.CapsuleBody!.Center;
        Vector3 v = GetPhysicsBodyVelocity(_held);

        Vector3 toTarget = desired - x;

        Vector3 accel = toTarget * _holdStiffness - v * _holdDamping;

        v += accel * dt;
        Vector3 newCenter = x + v * dt;

        BuildRuntimeCollidersThisFrame(includeDynamicBodies: false, includeHeldBodies: false);

        Vector3 resolvedCenter;
        Vector3 resolvedVel;
        bool hadContact;
        Vector3 contactNormal;
        ContactManifold contactManifold;
        ContactManifold supportManifold;

        if (_held.Physics.BoxBody != null)
        {
            (resolvedCenter, resolvedVel, _, hadContact, contactNormal, contactManifold, supportManifold) = Engine.Physics.Collision.StaticCollision.ResolveDynamicAabb(
                newCenter,
                v,
                _held.Physics.BoxBody.HalfExtents,
                _held.Physics.Rotation,
                _runtimeWorldColliders);
        }
        else
        {
            if (_held.Physics.SphereBody != null)
            {
                (resolvedCenter, resolvedVel, _, hadContact, contactNormal, contactManifold, supportManifold) = Engine.Physics.Collision.StaticCollision.ResolveDynamicSphere(
                    newCenter,
                    v,
                    _held.Physics.SphereBody.Radius,
                    _runtimeWorldColliders);
            }
            else
            {
                (resolvedCenter, resolvedVel, _, hadContact, contactNormal, contactManifold, supportManifold) = Engine.Physics.Collision.StaticCollision.ResolveDynamicCapsule(
                    newCenter,
                    v,
                    _held.Physics.CapsuleBody!.Radius,
                    _held.Physics.CapsuleBody.Height,
                    _held.Physics.Rotation,
                    _runtimeWorldColliders);
            }
        }

        SetPhysicsBodyCenter(_held, resolvedCenter);
        SetPhysicsBodyVelocity(_held, resolvedVel);
        SetBodyContactState(_held, hadContact, contactNormal, contactManifold, supportManifold);
        ApplyWorldCollisionSpin(_held, v, resolvedVel, hadContact, contactNormal, contactManifold);

        _held.Transform.Position = resolvedCenter;

        ResolveHeldObjectDynamicContacts(
            _held,
            ref hadContact,
            ref contactNormal,
            ref contactManifold,
            ref supportManifold);

        SetBodyContactState(_held, hadContact, contactNormal, contactManifold, supportManifold);
        IntegrateEntityRotation(_held, dt);

        if (TryGetPhysicsBodyCenter(_held, out Vector3 finalCenter))
            _held.Transform.Position = finalCenter;
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
        if (entity.Physics.CapsuleBody != null) return entity.Physics.CapsuleBody.Restitution;
        return 0f;
    }

    private static float GetBodyFriction(Entity entity)
    {
        if (entity.Physics.BoxBody != null) return entity.Physics.BoxBody.Friction;
        if (entity.Physics.SphereBody != null) return entity.Physics.SphereBody.Friction;
        if (entity.Physics.CapsuleBody != null) return entity.Physics.CapsuleBody.Friction;
        return 0f;
    }

    private static bool TryGetDynamicContact(Entity a, Entity b, out ContactManifold manifold)
    {
        manifold = default;

        if (!TryCreatePhysicsWorldCollider(a, out var aCollider) ||
            !TryCreatePhysicsWorldCollider(b, out var bCollider))
        {
            return false;
        }

        return ShapeCollision.TryResolve(aCollider, bCollider, out manifold);
    }

    private static bool ShouldReplaceSupportManifold(ContactManifold candidate, ContactManifold current)
    {
        if (!candidate.HasContact)
            return false;

        float candidateUp = candidate.SurfaceNormal.Y;
        if (candidateUp < 0.35f)
            return false;

        if (!current.HasContact)
            return true;

        float currentUp = current.SurfaceNormal.Y;
        if (candidateUp > currentUp + 0.05f)
            return true;

        if (MathF.Abs(candidateUp - currentUp) <= 0.05f &&
            candidate.Penetration > current.Penetration)
        {
            return true;
        }

        return false;
    }

    private static ContactManifold FlipManifold(ContactManifold manifold)
    {
        if (!manifold.HasContact)
            return default;

        return new ContactManifold(
            normal: -manifold.Normal,
            penetration: manifold.Penetration,
            contactCount: manifold.ContactCount,
            point0: manifold.Point0,
            point1: manifold.Point1,
            point2: manifold.Point2,
            point3: manifold.Point3);
    }

    private static void MergeBodyContactState(Entity entity, ContactManifold manifold)
    {
        if (!manifold.HasContact)
            return;

        TryGetLastContactManifold(entity, out ContactManifold currentContact);
        TryGetLastSupportManifold(entity, out ContactManifold currentSupport);

        ContactManifold bestContact = currentContact;
        if (!bestContact.HasContact || manifold.Penetration > bestContact.Penetration)
            bestContact = manifold;

        ContactManifold bestSupport = currentSupport;
        if (ShouldReplaceSupportManifold(manifold, bestSupport))
            bestSupport = manifold;

        SetBodyContactState(
            entity,
            hadContact: true,
            contactNormal: bestContact.HasContact ? bestContact.Normal : Vector3.Zero,
            contactManifold: bestContact,
            supportManifold: bestSupport);
    }

    private static void ApplyRestingContactDamping(
        ref Vector3 aVelocity,
        ref Vector3 bVelocity,
        Vector3 normal,
        float invA,
        float invB,
        ContactManifold manifold)
    {
        float invSum = invA + invB;
        if (invSum <= 0f || !manifold.HasContact)
            return;

        float verticality = MathF.Abs(normal.Y);
        if (verticality < 0.55f)
            return;

        Vector3 relativeVelocity = bVelocity - aVelocity;
        float relN = Vector3.Dot(relativeVelocity, normal);
        if (MathF.Abs(relN) > 1.25f)
            return;

        Vector3 tangent = relativeVelocity - relN * normal;
        float tangentLen = tangent.Length();
        if (tangentLen < 0.02f)
            return;

        tangent /= tangentLen;

        float settleStrength = manifold.ContactCount switch
        {
            >= 3 => 0.65f,
            2 => 0.5f,
            _ => 0.3f
        };

        settleStrength *= Math.Clamp((verticality - 0.55f) / 0.45f, 0f, 1f);
        float jt = -Vector3.Dot(relativeVelocity, tangent) / invSum;
        jt *= settleStrength;

        Vector3 dampingImpulse = jt * tangent;
        aVelocity -= dampingImpulse * invA;
        bVelocity += dampingImpulse * invB;
    }

    private static bool HasStrongSupportContact(Entity entity)
    {
        return TryGetLastSupportManifold(entity, out ContactManifold support) &&
               support.HasContact &&
               support.SurfaceNormal.Y >= 0.75f;
    }

    private static bool IsRestingStackContact(Entity a, Entity b, ContactManifold manifold, Vector3 relativeVelocity)
    {
        if (!manifold.HasContact)
            return false;

        if (GetPhysicsBodyShape(a) != RuntimeShapeKind.Box || GetPhysicsBodyShape(b) != RuntimeShapeKind.Box)
            return false;

        float verticality = MathF.Abs(manifold.Normal.Y);
        if (verticality < 0.7f || manifold.ContactCount < 2)
            return false;

        float normalSpeed = MathF.Abs(Vector3.Dot(relativeVelocity, manifold.Normal));
        Vector3 tangent = relativeVelocity - Vector3.Dot(relativeVelocity, manifold.Normal) * manifold.Normal;
        float tangentSpeedSq = tangent.LengthSquared();

        return normalSpeed <= 1.75f &&
               tangentSpeedSq <= 1.0f &&
               (IsNearStableBoxPose(a, minAlignment: 0.975f) || IsNearStableBoxPose(b, minAlignment: 0.975f));
    }

    private static void GetDynamicContactCorrectionWeights(
        Entity a,
        Entity b,
        ContactManifold manifold,
        float invA,
        float invB,
        out float aWeight,
        out float bWeight)
    {
        float invSum = invA + invB;
        if (invSum <= 0f)
        {
            aWeight = 0.5f;
            bWeight = 0.5f;
            return;
        }

        aWeight = invA / invSum;
        bWeight = invB / invSum;

        if (MathF.Abs(manifold.Normal.Y) < 0.7f || manifold.ContactCount < 2)
            return;

        if (manifold.Normal.Y > 0.7f && HasStrongSupportContact(a))
        {
            aWeight = MathF.Min(aWeight, 0.08f);
            bWeight = 1f - aWeight;
            return;
        }

        if (manifold.Normal.Y < -0.7f && HasStrongSupportContact(b))
        {
            bWeight = MathF.Min(bWeight, 0.08f);
            aWeight = 1f - bWeight;
        }
    }

    private void ResolveHeldObjectDynamicContacts(
        Entity held,
        ref bool hadContact,
        ref Vector3 contactNormal,
        ref ContactManifold contactManifold,
        ref ContactManifold supportManifold)
    {
        if (!HasPhysicsBody(held))
            return;

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            var other = _runtimeEntities[i];
            if (ReferenceEquals(other, held)) continue;
            if (!HasPhysicsBody(other)) continue;
            if (other.IsHeld) continue;
            if (other.Physics.MotionType != MotionType.Dynamic) continue;

            if (!TryGetDynamicContact(held, other, out ContactManifold manifold))
                continue;

            Vector3 n = manifold.Normal;
            float pen = manifold.Penetration;

            float invHeld = GetPhysicsBodyMass(held) > 0f ? 1f / GetPhysicsBodyMass(held) : 0f;
            float invOther = GetPhysicsBodyMass(other) > 0f ? 1f / GetPhysicsBodyMass(other) : 0f;
            float invSum = invHeld + invOther;
            if (invSum <= 0f)
                continue;

            if (!TryGetPhysicsBodyCenter(held, out Vector3 heldCenter) ||
                !TryGetPhysicsBodyCenter(other, out Vector3 otherCenter))
            {
                continue;
            }

            Vector3 corr = n * pen;
            heldCenter -= corr * (invHeld / invSum);
            otherCenter += corr * (invOther / invSum);
            SetPhysicsBodyCenter(held, heldCenter);
            SetPhysicsBodyCenter(other, otherCenter);

            Vector3 heldVelocity = GetPhysicsBodyVelocity(held);
            Vector3 otherVelocity = GetPhysicsBodyVelocity(other);
            Vector3 heldVelocityBefore = heldVelocity;
            Vector3 otherVelocityBefore = otherVelocity;
            Vector3 rv = otherVelocity - heldVelocity;
            float relN = Vector3.Dot(rv, n);

            if (relN < 0f)
            {
                float e = MathF.Min(GetBodyRestitution(held), GetBodyRestitution(other));
                float j = -(1f + e) * relN / invSum;
                Vector3 impulse = j * n;

                heldVelocity -= impulse * invHeld;
                otherVelocity += impulse * invOther;

                Vector3 rv2 = otherVelocity - heldVelocity;
                Vector3 t = rv2 - Vector3.Dot(rv2, n) * n;
                float tLen = t.Length();
                if (tLen > 1e-6f)
                {
                    t /= tLen;

                    float mu = MathF.Min(GetBodyFriction(held), GetBodyFriction(other));
                    float jt = -Vector3.Dot(rv2, t) / invSum;
                    float maxF = mu * j;
                    jt = Math.Clamp(jt, -maxF, +maxF);

                    Vector3 frImpulse = jt * t;
                    heldVelocity -= frImpulse * invHeld;
                    otherVelocity += frImpulse * invOther;
                }

                SetPhysicsBodyVelocity(held, heldVelocity);
                SetPhysicsBodyVelocity(other, otherVelocity);
            }

            hadContact = true;
            if (manifold.Penetration >= contactManifold.Penetration)
            {
                contactNormal = n;
                contactManifold = manifold;
            }

            if (ShouldReplaceSupportManifold(manifold, supportManifold))
                supportManifold = manifold;

            ApplyCollisionSpin(held, -n, heldVelocity - heldVelocityBefore, manifold, intensityScale: 1.05f);
            ApplyCollisionSpin(other, n, otherVelocity - otherVelocityBefore, manifold, intensityScale: 1.05f);
            MergeBodyContactState(other, FlipManifold(manifold));

            held.Transform.Position = heldCenter;
            other.Transform.Position = otherCenter;
        }
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
                    if (!TryGetDynamicContact(A, B, out ContactManifold manifold))
                        continue;

                    Vector3 n = manifold.Normal;
                    float pen = manifold.Penetration;

                    float invA = GetPhysicsBodyMass(A) > 0f ? 1f / GetPhysicsBodyMass(A) : 0f;
                    float invB = GetPhysicsBodyMass(B) > 0f ? 1f / GetPhysicsBodyMass(B) : 0f;
                    float invSum = invA + invB;
                    if (invSum <= 0f) continue;

                    Vector3 corr = n * pen;
                    GetDynamicContactCorrectionWeights(A, B, manifold, invA, invB, out float aCorrectionWeight, out float bCorrectionWeight);
                    Vector3 aCenter = A.Transform.Position;
                    Vector3 bCenter = B.Transform.Position;
                    TryGetPhysicsBodyCenter(A, out aCenter);
                    TryGetPhysicsBodyCenter(B, out bCenter);

                    SetPhysicsBodyCenter(A, aCenter - corr * aCorrectionWeight);
                    SetPhysicsBodyCenter(B, bCenter + corr * bCorrectionWeight);

                    Vector3 aVelocity = GetPhysicsBodyVelocity(A);
                    Vector3 bVelocity = GetPhysicsBodyVelocity(B);
                    Vector3 aVelocityBefore = aVelocity;
                    Vector3 bVelocityBefore = bVelocity;
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

                    ApplyRestingContactDamping(
                        ref aVelocity,
                        ref bVelocity,
                        n,
                        invA,
                        invB,
                        manifold);
                    SetPhysicsBodyVelocity(A, aVelocity);
                    SetPhysicsBodyVelocity(B, bVelocity);

                    bool suppressStackSpin = IsRestingStackContact(A, B, manifold, bVelocityBefore - aVelocityBefore);
                    if (!suppressStackSpin)
                    {
                        ApplyCollisionSpin(A, -n, aVelocity - aVelocityBefore, manifold, intensityScale: 1.1f);
                        ApplyCollisionSpin(B, n, bVelocity - bVelocityBefore, manifold, intensityScale: 1.1f);
                    }
                    MergeBodyContactState(A, manifold);
                    MergeBodyContactState(B, FlipManifold(manifold));

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
