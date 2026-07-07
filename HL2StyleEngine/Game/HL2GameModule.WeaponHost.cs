using Engine.Editor.Level;
using Engine.Input;
using Engine.Input.Actions;
using Engine.Input.Devices;
using Engine.Physics.Collision;
using Engine.Physics.Dynamics;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Entities.Interfaces;
using Game.Inventory;
using Game.Weapons;
using System.Numerics;
using Veldrid;

namespace Game;

public sealed partial class HL2GameModule
{
    private void EnsurePrototypeWeaponLoadout(bool includeStarterAmmo)
    {
        _weaponSystem.EnsureDefaultPrototypeLoadout(includeStarterAmmo);
        MoveWeaponSystemItemsOutOfContainer(_inventory, fillMagazineFromAmmo: false);
        MoveWeaponSystemItemsOutOfContainer(_storage, fillMagazineFromAmmo: false);
    }

    private void MoveWeaponSystemItemsOutOfContainer(InventoryContainer container, bool fillMagazineFromAmmo)
    {
        foreach (InventoryItemStack stack in container.Stacks.ToList())
        {
            if (!_weaponSystem.TryGrantInventoryItem(stack.ItemId, stack.Count, fillMagazineFromAmmo))
                continue;

            container.RemoveStack(stack.ItemId);
        }
    }
    private bool TryUseWeaponInventoryItem(string itemId)
        => _weaponSystem.TryEquipInventoryItem(this, itemId, showMessage: true);

    private WeaponInputSnapshot BuildWeaponInputSnapshot()
        => new(
            primaryPressed: WeaponPrimaryPressedThisFrame(),
            primaryHeld: WeaponPrimaryHeld(),
            secondaryPressed: WeaponSecondaryPressedThisFrame(),
            categorySlotPressed: WeaponCategorySlotPressedThisFrame());

    private int WeaponCategorySlotPressedThisFrame()
    {
        if (_inputState.WasPressed(Key.Number1) || _inputState.WasPressed(Key.Keypad1) || _inputState.GetGamepadPressed(GamepadButton.DpadUp))
            return 1;
        if (_inputState.WasPressed(Key.Number2) || _inputState.WasPressed(Key.Keypad2) || _inputState.GetGamepadPressed(GamepadButton.DpadRight))
            return 2;
        if (_inputState.WasPressed(Key.Number3) || _inputState.WasPressed(Key.Keypad3) || _inputState.GetGamepadPressed(GamepadButton.DpadDown))
            return 3;
        if (_inputState.WasPressed(Key.Number4) || _inputState.WasPressed(Key.Keypad4) || _inputState.GetGamepadPressed(GamepadButton.DpadLeft))
            return 4;

        return 0;
    }
    private bool WeaponPrimaryPressedThisFrame()
        => _inputState.LeftMousePressedThisFrame ||
           RightTriggerPressedThisFrame();

    private bool WeaponPrimaryHeld()
        => _inputState.LeftMouseDown ||
           _inputState.GetAxis(GamepadAxis.TriggerRight) > 0.5f;

    private bool WeaponSecondaryPressedThisFrame()
        => _inputState.RightMousePressedThisFrame ||
           _inputState.GetGamepadPressed(GamepadButton.LeftShoulder);

    private bool RightTriggerPressedThisFrame()
    {
        float v = _inputState.GetAxis(GamepadAxis.TriggerRight);
        bool down = v > 0.5f;

        bool pressed = down && !_prevRightTriggerDown;
        _prevRightTriggerDown = down;

        return pressed;
    }

    Vector3 IWeaponHost.CameraPosition => _camera.Position;
    Vector3 IWeaponHost.CameraForward => _camera.Forward;
    bool IWeaponHost.HasHeldObject => _held != null;
    bool IWeaponHost.HeldObjectGrabbedByGravityGun => _heldByGravityGun;

    float IWeaponHost.HoldDistance
    {
        get => _holdDistance;
        set => _holdDistance = MathF.Max(0.5f, value);
    }

    bool IWeaponHost.HasInventoryItem(string itemId)
        => _inventory.Contains(itemId);

    int IWeaponHost.GetInventoryItemCount(string itemId)
        => _inventory.GetCount(itemId);

    bool IWeaponHost.TryConsumeInventoryItem(string itemId, int count)
        => _inventory.RemoveCount(itemId, count);

    Entity? IWeaponHost.RaycastPickable(float maxDist, float? maxMass)
        => RaycastPickable(maxDist, maxMass);

    bool IWeaponHost.TryRaycastWeaponTarget(float maxDist, out WeaponTargetHit hit)
    {
        Vector3 origin = _camera.Position;
        Vector3 dir = Vector3.Normalize(_camera.Forward);
        return TryRaycastWeaponTarget(origin, dir, maxDist, minDist: 0.1f, out hit);
    }

    private bool TryRaycastWeaponTarget(Vector3 origin, Vector3 direction, float maxDist, float minDist, out WeaponTargetHit hit)
    {
        if (direction.LengthSquared() < 0.0001f)
        {
            hit = default;
            return false;
        }

        Vector3 dir = Vector3.Normalize(direction);
        var ray = new Ray(origin, dir);

        Entity? target = null;
        float distance = maxDist;
        Vector3 hitPoint = origin + dir * maxDist;

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            Entity entity = _runtimeEntities[i];
            if (ReferenceEquals(entity, _held))
                continue;
            if (!entity.Collider.Enabled || !entity.Render.Enabled || entity.Collider.Shape == RuntimeShapeKind.None)
                continue;
            if (entity.Type == EntityTypes.TriggerVolume ||
                entity.Type == EntityTypes.PointLight ||
                entity.Type == EntityTypes.PlayerSpawn)
            {
                continue;
            }

            if (RayIntersectsEntityCollider(ray, entity, minDist, distance, out float tHit))
            {
                distance = tHit;
                target = entity;
                hitPoint = origin + dir * tHit;
            }
        }

        if (target == null)
        {
            hit = default;
            return false;
        }

        hit = new WeaponTargetHit(target, hitPoint, distance);
        return true;
    }

    bool IWeaponHost.ApplyWeaponImpulse(Entity entity, Vector3 impulse, Vector3 hitPoint, float spinScale)
    {
        if (!HasPhysicsBody(entity) || entity.Physics.MotionType != MotionType.Dynamic || IsPhysicsBodyKinematic(entity))
            return false;

        float mass = MathF.Max(0.1f, GetPhysicsBodyMass(entity));
        Vector3 velocity = GetPhysicsBodyVelocity(entity);
        SetPhysicsBodyVelocity(entity, velocity + impulse / mass);

        if (!TryGetPhysicsBodyCenter(entity, out Vector3 center))
            return true;

        Vector3 offset = hitPoint - center;
        Vector3 torque = Vector3.Cross(offset, impulse);
        if (torque.LengthSquared() <= 1e-6f)
            return true;

        float radius = MathF.Max(0.1f, GetBodyRotationRadius(entity));
        AddAngularVelocity(entity, Vector3.Normalize(torque) * (impulse.Length() / mass / radius) * spinScale);
        return true;
    }

    void IWeaponHost.ApplyWeaponDamage(Entity entity, float amount, string damageKind)
        => ApplyEntityDamage(entity, amount, damageKind);

    bool IWeaponHost.TryApplyGravityGunAttraction(
        Entity entity,
        Vector3 targetPoint,
        float dt,
        float pullAcceleration,
        float maxPullSpeed,
        out Vector3 center,
        out float mass,
        out float distanceToTarget)
    {
        center = default;
        mass = 0f;
        distanceToTarget = 0f;

        if (dt <= 0f ||
            !HasPhysicsBody(entity) ||
            entity.Physics.MotionType != MotionType.Dynamic ||
            IsPhysicsBodyKinematic(entity) ||
            entity.IsHeld ||
            !TryGetPhysicsBodyCenter(entity, out center))
        {
            return false;
        }

        mass = MathF.Max(0.1f, GetPhysicsBodyMass(entity));
        Vector3 toTarget = targetPoint - center;
        distanceToTarget = toTarget.Length();
        if (distanceToTarget <= 0.001f)
            return true;

        Vector3 pullDir = toTarget / distanceToTarget;
        Vector3 velocity = GetPhysicsBodyVelocity(entity);
        float currentTowardSpeed = Vector3.Dot(velocity, pullDir);
        Vector3 lateralVelocity = velocity - pullDir * currentTowardSpeed;

        // Ten-ish units is the prototype "normal prop" mass. Heavier props still move, but ramp up slower.
        float massScale = Math.Clamp(MathF.Sqrt(10f / mass), 0.28f, 1.25f);
        float targetTowardSpeed = MathF.Min(maxPullSpeed * massScale, MathF.Max(1.5f, distanceToTarget * 3.0f));
        float maxDelta = pullAcceleration * massScale * dt;
        float newTowardSpeed = MoveScalarTowards(currentTowardSpeed, targetTowardSpeed, maxDelta);

        float lateralDamping = Math.Clamp(1.0f - 4.0f * dt, 0.55f, 1.0f);
        Vector3 newVelocity = lateralVelocity * lateralDamping + pullDir * newTowardSpeed;
        SetPhysicsBodyVelocity(entity, newVelocity);

        entity.Physics.AngularVelocity *= Math.Clamp(1.0f - 2.5f * dt * massScale, 0.65f, 1.0f);
        return true;
    }

    private static float MoveScalarTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
            return target;

        return current + MathF.Sign(target - current) * maxDelta;
    }

    bool IWeaponHost.TryGetHeldObjectCenter(out Vector3 center)
    {
        if (_held == null)
        {
            center = default;
            return false;
        }

        return TryGetPhysicsBodyCenter(_held, out center);
    }

    bool IWeaponHost.TryGetEntityCenter(Entity entity, out Vector3 center)
        => TryGetPhysicsBodyCenter(entity, out center);

    void IWeaponHost.PickUpWithWeapon(Entity entity, bool grabbedByGravityGun)
        => PickUp(entity, grabbedByGravityGun);

    void IWeaponHost.DropHeldObject()
        => DropHeld();

    void IWeaponHost.ThrowHeldObject(float speed)
        => ThrowHeld(speed);

    string IWeaponHost.GetEntityDisplayName(Entity entity)
        => PrettifyToken(entity.Name);

    void IWeaponHost.ShowWeaponMessage(string message, float seconds)
        => ShowGameMessage(message, seconds);

    void IWeaponHost.DrawWeaponBox(Renderer renderer, Vector3 position, Vector3 size, Quaternion rotation, Vector4 color)
        => DrawEditorBox(renderer, position, size, rotation, color);

    void IWeaponHost.DrawWeaponSphere(Renderer renderer, Vector3 position, float radius, Vector4 color)
    {
        Matrix4x4 model =
            Matrix4x4.CreateScale(new Vector3(radius * 2f)) *
            Matrix4x4.CreateTranslation(position);
        _world.DrawSphere(renderer.CommandList, model, color);
    }

    void IWeaponHost.DrawWeaponBeam(Renderer renderer, Vector3 start, Vector3 end, float thickness, Vector4 color)
        => DrawEdgeBox(renderer, start, end, thickness, color);

    bool IWeaponHost.TryDrawWeaponModel(Renderer renderer, string modelAssetPath, Matrix4x4 transform, Vector4 tint)
        => !IsBlockoutPracticeLevel() && TryDrawModel(renderer, modelAssetPath, transform, tint, "weapon model");

    private bool TryDrawWorldModel(Renderer renderer, Entity entity, Vector3 position, Vector3 size, Quaternion rotation, Vector4 tint)
    {
        if (string.IsNullOrWhiteSpace(entity.Render.ModelAssetPath))
            return false;

        if (!TryGetReadyModel(entity.Render.ModelAssetPath, "world model", out WeaponModelCacheEntry? entry) || entry?.Model == null)
            return false;

        Matrix4x4 transform = CreateBoundsFitTransform(entry.Bounds, position, size, rotation);
        _world.DrawModel(renderer.CommandList, entry.Model, transform, tint);
        return true;
    }

    private bool TryDrawModel(Renderer renderer, string modelAssetPath, Matrix4x4 transform, Vector4 tint, string modelKind)
    {
        if (!TryGetReadyModel(modelAssetPath, modelKind, out WeaponModelCacheEntry? entry) || entry?.Model == null)
            return false;

        _world.DrawModel(renderer.CommandList, entry.Model, transform, tint);
        return true;
    }

    private bool TryGetReadyModel(string modelAssetPath, string modelKind, out WeaponModelCacheEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(modelAssetPath))
            return false;

        string path = ResolveModelAssetPath(modelAssetPath);

        if (_weaponModelCache.TryGetValue(path, out entry))
        {
            if (entry.Model != null)
                return true;

            if (entry.Failed)
                return false;

            if (entry.LoadTask is { IsCompleted: true } task)
            {
                if (task.IsCompletedSuccessfully)
                {
                    try
                    {
                        LoadedModel loaded = task.Result;
                        entry.Bounds = CalculateModelBounds(loaded);
                        entry.Model = _world.CreateRenderModel(loaded);
                        entry.LoadTask = null;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        entry.Failed = true;
                        entry.Error = ex.Message;
                        entry.LoadTask = null;
                        ShowGameMessage($"Could not prepare {modelKind}: {Path.GetFileName(modelAssetPath)} ({ex.Message})", 2.5f);
                    }
                }
                else
                {
                    entry.Failed = true;
                    entry.Error = task.Exception?.GetBaseException().Message ?? "model load failed";
                    entry.LoadTask = null;
                    ShowGameMessage($"Could not load {modelKind}: {Path.GetFileName(modelAssetPath)} ({entry.Error})", 2.5f);
                }
            }

            return false;
        }

        if (!File.Exists(path))
        {
            entry = new WeaponModelCacheEntry
            {
                Failed = true,
                Error = "file not found"
            };
            _weaponModelCache[path] = entry;
            return false;
        }

        entry = new WeaponModelCacheEntry
        {
            LoadTask = Task.Run(() => GlbModelLoader.Load(path))
        };
        _weaponModelCache[path] = entry;
        return false;
    }

    private static Matrix4x4 CreateBoundsFitTransform(ModelBounds bounds, Vector3 position, Vector3 size, Quaternion rotation)
    {
        if (!bounds.Valid)
        {
            return Matrix4x4.CreateScale(size) *
                   Matrix4x4.CreateFromQuaternion(rotation) *
                   Matrix4x4.CreateTranslation(position);
        }

        Vector3 boundsSize = new(
            MathF.Max(0.0001f, bounds.Size.X),
            MathF.Max(0.0001f, bounds.Size.Y),
            MathF.Max(0.0001f, bounds.Size.Z));
        Vector3 scale = new(
            MathF.Abs(size.X) / boundsSize.X,
            MathF.Abs(size.Y) / boundsSize.Y,
            MathF.Abs(size.Z) / boundsSize.Z);

        return Matrix4x4.CreateTranslation(-bounds.Center) *
               Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateFromQuaternion(rotation) *
               Matrix4x4.CreateTranslation(position);
    }

    private static ModelBounds CalculateModelBounds(LoadedModel model)
    {
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        bool hasVertex = false;

        foreach (LoadedModelPart part in model.Parts)
        {
            foreach (Vector3 position in part.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
                hasVertex = true;
            }
        }

        return hasVertex
            ? new ModelBounds(true, min, max)
            : new ModelBounds(false, Vector3.Zero, Vector3.Zero);
    }

    private static bool IsGlbModelPath(string? modelAssetPath)
        => !string.IsNullOrWhiteSpace(modelAssetPath) &&
           string.Equals(Path.GetExtension(modelAssetPath), ".glb", StringComparison.OrdinalIgnoreCase);

    private static string ResolveModelAssetPath(string modelAssetPath)
        => Path.IsPathRooted(modelAssetPath)
            ? modelAssetPath
            : Path.Combine(AppContext.BaseDirectory, modelAssetPath.Replace('/', Path.DirectorySeparatorChar));
}
