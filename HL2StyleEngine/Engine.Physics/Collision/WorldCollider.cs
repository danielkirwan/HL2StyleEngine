using System.Numerics;

namespace Engine.Physics.Collision;

public enum WorldColliderShape
{
    Box = 0,
    Sphere = 1,
    Capsule = 2,
    Mesh = 3
}

public readonly struct WorldCollider
{
    public readonly WorldColliderShape Shape;
    public readonly Vector3 Center;
    public readonly Vector3 HalfExtents;
    public readonly float Radius;
    public readonly float Height;
    public readonly Quaternion Rotation;
    public readonly MeshCollisionMesh? MeshData;

    private WorldCollider(WorldColliderShape shape, Vector3 center, Vector3 halfExtents, float radius, float height, Quaternion rotation, MeshCollisionMesh? meshData = null)
    {
        Shape = shape;
        Center = center;
        HalfExtents = halfExtents;
        Radius = radius;
        Height = height;
        Rotation = Quaternion.Normalize(rotation);
        MeshData = meshData;
    }

    public static WorldCollider Box(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        => new(WorldColliderShape.Box, center, halfExtents, 0f, halfExtents.Y * 2f, rotation);

    public static WorldCollider Sphere(Vector3 center, float radius)
        => new(WorldColliderShape.Sphere, center, new Vector3(radius), radius, radius * 2f, Quaternion.Identity);

    public static WorldCollider Capsule(Vector3 center, float radius, float height, Quaternion rotation)
        => new(WorldColliderShape.Capsule, center, new Vector3(radius, MathF.Max(height * 0.5f, radius), radius), radius, height, rotation);

    public static WorldCollider Mesh(MeshCollisionMesh mesh)
        => new(WorldColliderShape.Mesh, mesh.Bounds.Center, (mesh.Bounds.Max - mesh.Bounds.Min) * 0.5f, 0f, 0f, Quaternion.Identity, mesh);

    public Vector3 AxisX => Vector3.Transform(Vector3.UnitX, Rotation);
    public Vector3 AxisY => Vector3.Transform(Vector3.UnitY, Rotation);
    public Vector3 AxisZ => Vector3.Transform(Vector3.UnitZ, Rotation);

    public float CapsuleSegmentHalf => MathF.Max(0f, Height * 0.5f - Radius);

    public void GetCapsuleSegment(out Vector3 a, out Vector3 b)
    {
        Vector3 offset = AxisY * CapsuleSegmentHalf;
        a = Center - offset;
        b = Center + offset;
    }

    public Aabb GetAabb()
    {
        if (Shape == WorldColliderShape.Mesh && MeshData != null)
            return MeshData.Bounds;

        if (Shape == WorldColliderShape.Box)
        {
            Vector3 ax = Abs(AxisX) * HalfExtents.X;
            Vector3 ay = Abs(AxisY) * HalfExtents.Y;
            Vector3 az = Abs(AxisZ) * HalfExtents.Z;
            return Aabb.FromCenterExtents(Center, ax + ay + az);
        }

        if (Shape == WorldColliderShape.Capsule)
        {
            Vector3 segment = Abs(AxisY) * CapsuleSegmentHalf;
            return Aabb.FromCenterExtents(Center, segment + new Vector3(Radius));
        }

        return Shape switch
        {
            WorldColliderShape.Sphere => Aabb.FromCenterExtents(Center, new Vector3(Radius)),
            _ => Aabb.FromCenterExtents(Center, Vector3.Zero)
        };
    }

    private static Vector3 Abs(Vector3 v)
        => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
}
