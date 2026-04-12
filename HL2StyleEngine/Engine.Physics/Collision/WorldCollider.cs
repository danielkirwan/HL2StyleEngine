using System.Numerics;

namespace Engine.Physics.Collision;

public enum WorldColliderShape
{
    Box = 0,
    Sphere = 1,
    Capsule = 2
}

public readonly struct WorldCollider
{
    public readonly WorldColliderShape Shape;
    public readonly Vector3 Center;
    public readonly Vector3 HalfExtents;
    public readonly float Radius;
    public readonly float Height;

    private WorldCollider(WorldColliderShape shape, Vector3 center, Vector3 halfExtents, float radius, float height)
    {
        Shape = shape;
        Center = center;
        HalfExtents = halfExtents;
        Radius = radius;
        Height = height;
    }

    public static WorldCollider Box(Vector3 center, Vector3 halfExtents)
        => new(WorldColliderShape.Box, center, halfExtents, 0f, halfExtents.Y * 2f);

    public static WorldCollider Sphere(Vector3 center, float radius)
        => new(WorldColliderShape.Sphere, center, new Vector3(radius), radius, radius * 2f);

    public static WorldCollider Capsule(Vector3 center, float radius, float height)
        => new(WorldColliderShape.Capsule, center, new Vector3(radius, MathF.Max(height * 0.5f, radius), radius), radius, height);

    public Aabb GetAabb()
    {
        return Shape switch
        {
            WorldColliderShape.Box => Aabb.FromCenterExtents(Center, HalfExtents),
            WorldColliderShape.Sphere => Aabb.FromCenterExtents(Center, new Vector3(Radius)),
            WorldColliderShape.Capsule => Aabb.FromCenterExtents(
                Center,
                new Vector3(Radius, MathF.Max(Height * 0.5f, Radius), Radius)),
            _ => Aabb.FromCenterExtents(Center, Vector3.Zero)
        };
    }
}
