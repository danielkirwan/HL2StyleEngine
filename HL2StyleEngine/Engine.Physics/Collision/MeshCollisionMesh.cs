using System;
using System.Numerics;

namespace Engine.Physics.Collision;

public sealed class MeshCollisionMesh
{
    public const float DefaultCollisionSkin = 0.08f;
    private static readonly MeshCollisionTriangle[] EmptyTriangles = Array.Empty<MeshCollisionTriangle>();

    public MeshCollisionMesh(IReadOnlyList<MeshCollisionTriangle> triangles, float collisionSkin = DefaultCollisionSkin)
    {
        CollisionSkin = MathF.Max(0f, collisionSkin);

        if (triangles.Count <= 0)
        {
            Triangles = EmptyTriangles;
            Bounds = Aabb.FromCenterExtents(Vector3.Zero, Vector3.Zero);
            return;
        }

        MeshCollisionTriangle[] copied = new MeshCollisionTriangle[triangles.Count];
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        for (int i = 0; i < triangles.Count; i++)
        {
            copied[i] = triangles[i];
            min = Vector3.Min(min, triangles[i].Bounds.Min);
            max = Vector3.Max(max, triangles[i].Bounds.Max);
        }

        Triangles = copied;
        Vector3 skin = new(CollisionSkin);
        Bounds = new Aabb(min - skin, max + skin);
    }

    public IReadOnlyList<MeshCollisionTriangle> Triangles { get; }
    public float CollisionSkin { get; }
    public Aabb Bounds { get; }
    public bool IsValid => Triangles.Count > 0;
}

public readonly struct MeshCollisionTriangle
{
    public readonly Vector3 A;
    public readonly Vector3 B;
    public readonly Vector3 C;
    public readonly Vector3 Center;
    public readonly Vector3 Normal;
    public readonly Aabb Bounds;

    public MeshCollisionTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        A = a;
        B = b;
        C = c;
        Center = (a + b + c) / 3f;

        Vector3 normal = Vector3.Cross(b - a, c - a);
        float lenSq = normal.LengthSquared();
        Normal = lenSq > 1e-10f ? normal / MathF.Sqrt(lenSq) : Vector3.UnitY;

        Vector3 min = Vector3.Min(a, Vector3.Min(b, c));
        Vector3 max = Vector3.Max(a, Vector3.Max(b, c));
        Bounds = new Aabb(min, max);
    }
}
