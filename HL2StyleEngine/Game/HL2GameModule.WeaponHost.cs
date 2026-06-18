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
        foreach (WeaponLoadoutItem item in WeaponDefinitions.DefaultPrototypeLoadout)
        {
            InventoryItemDefinition definition = ItemCatalog.Get(item.ItemId);
            if (definition.Type == InventoryItemType.Weapon)
            {
                if (!_inventory.Contains(item.ItemId))
                    _inventory.Add(item.ItemId, item.Count);
                continue;
            }

            if (includeStarterAmmo && _inventory.GetCount(item.ItemId) <= 0)
                _inventory.Add(item.ItemId, item.Count);
        }
    }

    private bool TryUseWeaponInventoryItem(string itemId)
        => _weaponSystem.TryEquipInventoryItem(this, itemId, showMessage: true);

    private WeaponInputSnapshot BuildWeaponInputSnapshot()
        => new(
            primaryPressed: WeaponPrimaryPressedThisFrame(),
            secondaryPressed: WeaponSecondaryPressedThisFrame(),
            switchPressed: WeaponSwitchPressedThisFrame());

    private bool WeaponSwitchPressedThisFrame()
        => _inputState.WasPressed(Key.G) ||
           _inputState.GetGamepadPressed(GamepadButton.RightShoulder);

    private bool WeaponPrimaryPressedThisFrame()
        => _inputState.LeftMousePressedThisFrame ||
           RightTriggerPressedThisFrame();

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

            if (RayIntersectsEntityCollider(ray, entity, 0.1f, distance, out float tHit))
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

    void IWeaponHost.ApplyWeaponImpulse(Entity entity, Vector3 impulse, Vector3 hitPoint, float spinScale)
    {
        if (!HasPhysicsBody(entity) || entity.Physics.MotionType != MotionType.Dynamic || IsPhysicsBodyKinematic(entity))
            return;

        float mass = MathF.Max(0.1f, GetPhysicsBodyMass(entity));
        Vector3 velocity = GetPhysicsBodyVelocity(entity);
        SetPhysicsBodyVelocity(entity, velocity + impulse / mass);

        if (!TryGetPhysicsBodyCenter(entity, out Vector3 center))
            return;

        Vector3 offset = hitPoint - center;
        Vector3 torque = Vector3.Cross(offset, impulse);
        if (torque.LengthSquared() <= 1e-6f)
            return;

        float radius = MathF.Max(0.1f, GetBodyRotationRadius(entity));
        AddAngularVelocity(entity, Vector3.Normalize(torque) * (impulse.Length() / mass / radius) * spinScale);
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
}
