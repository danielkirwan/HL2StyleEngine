using System.Numerics;
using Engine.Physics.Collision;

internal static class CollisionBoundsChecks
{
    public static void Run()
    {
        Random random = new(173);
        for (int i = 0; i < 1000; i++)
        {
            Vector3 center = Vector(-100f, 100f);
            Vector3 extents = Vector(0.01f, 20f);
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll(Angle(), Angle(), Angle());
            WorldCollider box = WorldCollider.Box(center, extents, rotation);
            Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = extents * new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                Vector3 point = center + Vector3.Transform(local, box.Rotation);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
            Check(box.GetAabb(), new Aabb(min, max));
            Vector3 moved = center + new Vector3(5f, -3f, 2f);
            Check(WorldCollider.Box(moved, extents, rotation).GetAabb(),
                new Aabb(min + moved - center, max + moved - center));
            Check(box.GetAabb(), new Aabb(min, max));

            float radius = extents.X;
            Check(WorldCollider.Sphere(center, radius).GetAabb(),
                Aabb.FromCenterExtents(center, new Vector3(radius)));
            WorldCollider capsule = WorldCollider.Capsule(center, radius, extents.Y * 2f, rotation);
            capsule.GetCapsuleSegment(out Vector3 a, out Vector3 b);
            Check(capsule.GetAabb(), new Aabb(Vector3.Min(a, b) - new Vector3(radius),
                Vector3.Max(a, b) + new Vector3(radius)));
        }
        MeshCollisionMesh mesh = new(new[] {
            new MeshCollisionTriangle(new Vector3(-2, 0, -2), new Vector3(2, 0, -2), new Vector3(0, 0, 2))
        });
        Check(WorldCollider.Mesh(mesh).GetAabb(), mesh.Bounds);
        Check(default(WorldCollider).GetAabb(), default);
        if (!ShapeCollision.TryResolve(WorldCollider.Box(new Vector3(0, .4f, 0), new Vector3(.5f), Quaternion.Identity),
                WorldCollider.Mesh(mesh), out ContactManifold contact) || !contact.HasContact)
            throw new InvalidOperationException("Box/mesh contact was lost.");
        Console.WriteLine("Collider bounds: 1,000 rotated/scaled/moved box, sphere and capsule cases, mesh contact and default bounds passed.");

        float Angle() => (float)random.NextDouble() * MathF.Tau;
        Vector3 Vector(float low, float high) => new(
            low + (float)random.NextDouble() * (high - low),
            low + (float)random.NextDouble() * (high - low),
            low + (float)random.NextDouble() * (high - low));
    }

    private static void Check(Aabb actual, Aabb expected)
    {
        if (Vector3.Distance(actual.Min, expected.Min) > 0.0001f ||
            Vector3.Distance(actual.Max, expected.Max) > 0.0001f)
            throw new InvalidOperationException($"Bounds differ: {actual.Min}/{actual.Max} vs {expected.Min}/{expected.Max}");
    }
}
