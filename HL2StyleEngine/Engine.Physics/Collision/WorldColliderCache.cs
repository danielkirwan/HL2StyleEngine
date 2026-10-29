using System.Numerics;

namespace Engine.Physics.Collision;

// One entry per owning entity state. Geometry only: never retain contacts or support results.
// Like the physics solver, instances and optional diagnostics are simulation-thread confined.
public struct WorldColliderCache
{
    private WorldCollider _value;
    private Quaternion _rawRotation;
    private bool _valid;

    public static bool ReuseEnabled { get; set; } = true;
    public static bool CollectStatistics { get; set; }
    public static long Requests { get; private set; }
    public static long Builds { get; private set; }
    public static void ResetStatistics() { Requests = 0; Builds = 0; }

    public WorldCollider Box(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        bool hit = _valid && _value.Shape == WorldColliderShape.Box && _value.Center == center &&
            _value.HalfExtents == halfExtents && _rawRotation == rotation;
        if (Reuse(hit)) return _value;
        return Store(WorldCollider.Box(center, halfExtents, rotation), rotation);
    }

    public WorldCollider Sphere(Vector3 center, float radius)
    {
        bool hit = _valid && _value.Shape == WorldColliderShape.Sphere && _value.Center == center && _value.Radius == radius;
        if (Reuse(hit)) return _value;
        return Store(WorldCollider.Sphere(center, radius), Quaternion.Identity);
    }

    public WorldCollider Capsule(Vector3 center, float radius, float height, Quaternion rotation)
    {
        bool hit = _valid && _value.Shape == WorldColliderShape.Capsule && _value.Center == center &&
            _value.Radius == radius && _value.Height == height && _rawRotation == rotation;
        if (Reuse(hit)) return _value;
        return Store(WorldCollider.Capsule(center, radius, height, rotation), rotation);
    }

    public WorldCollider Mesh(MeshCollisionMesh mesh)
    {
        // MeshCollisionMesh is immutable; reposing/rebuilding creates another instance.
        if (Reuse(_valid && _value.Shape == WorldColliderShape.Mesh && ReferenceEquals(_value.MeshData, mesh))) return _value;
        return Store(WorldCollider.Mesh(mesh), Quaternion.Identity);
    }

    public void Clear() { _valid = false; _value = default; _rawRotation = default; }

    private static bool Reuse(bool hit)
    {
        if (CollectStatistics) { Requests++; if (!ReuseEnabled || !hit) Builds++; }
        return ReuseEnabled && hit;
    }

    private WorldCollider Store(WorldCollider value, Quaternion rawRotation)
    {
        _value = value;
        // Key the original quaternion, not the normalized result in WorldCollider.
        _rawRotation = rawRotation;
        _valid = true;
        return value;
    }
}
