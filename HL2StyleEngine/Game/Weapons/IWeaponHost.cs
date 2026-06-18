using Engine.Render;
using Engine.Runtime.Entities;
using System.Numerics;

namespace Game.Weapons;

public interface IWeaponHost
{
    Vector3 CameraPosition { get; }
    Vector3 CameraForward { get; }
    bool HasHeldObject { get; }
    bool HeldObjectGrabbedByGravityGun { get; }
    float HoldDistance { get; set; }

    bool HasInventoryItem(string itemId);
    int GetInventoryItemCount(string itemId);
    bool TryConsumeInventoryItem(string itemId, int count);

    Entity? RaycastPickable(float maxDist, float? maxMass);
    bool TryRaycastWeaponTarget(float maxDist, out WeaponTargetHit hit);
    void ApplyWeaponImpulse(Entity entity, Vector3 impulse, Vector3 hitPoint, float spinScale);

    bool TryGetHeldObjectCenter(out Vector3 center);
    bool TryGetEntityCenter(Entity entity, out Vector3 center);
    void PickUpWithWeapon(Entity entity, bool grabbedByGravityGun);
    void DropHeldObject();
    void ThrowHeldObject(float speed);

    string GetEntityDisplayName(Entity entity);
    void ShowWeaponMessage(string message, float seconds);

    void DrawWeaponBox(Renderer renderer, Vector3 position, Vector3 size, Quaternion rotation, Vector4 color);
    void DrawWeaponSphere(Renderer renderer, Vector3 position, float radius, Vector4 color);
    void DrawWeaponBeam(Renderer renderer, Vector3 start, Vector3 end, float thickness, Vector4 color);
}
