using System;
using System.Numerics;

namespace Engine.Physics.Collision;

public sealed class MeshCollisionMesh
{
    public const float DefaultCollisionSkin = 0.08f;
    private static readonly MeshCollisionTriangle[] EmptyTriangles = Array.Empty<MeshCollisionTriangle>();
    private readonly List<Node> _nodes = new();
    private readonly int[] _indices;
    public static bool AccelerationEnabled { get; set; } = true;
    public static long QueryCount, BoundsTests, CandidateTriangles;
    public static bool CollectStatistics { get; set; }
    private readonly record struct Node(Aabb Bounds, int Start, int Count, int Left, int Right);

    public MeshCollisionMesh(IReadOnlyList<MeshCollisionTriangle> triangles, float collisionSkin = DefaultCollisionSkin)
    {
        CollisionSkin = MathF.Max(0f, collisionSkin);
        _indices = Enumerable.Range(0, triangles.Count).ToArray();

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
        Build(0, copied.Length);
    }

    private int Build(int start, int count)
    {
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            var bounds = Triangles[_indices[i]].Bounds;
            min = Vector3.Min(min, bounds.Min); max = Vector3.Max(max, bounds.Max);
        }
        int index = _nodes.Count;
        _nodes.Add(new Node(new Aabb(min, max), start, count, -1, -1));
        if (count <= 8) return index;
        Vector3 extent = max - min;
        int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        Array.Sort(_indices, start, count, Comparer<int>.Create((a, b) =>
            Component(Triangles[a].Center, axis).CompareTo(Component(Triangles[b].Center, axis))));
        int left = Build(start, count / 2), right = Build(start + count / 2, count - count / 2);
        _nodes[index] = new Node(new Aabb(min, max), start, 0, left, right);
        return index;
    }

    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    public void Query(Aabb bounds, List<int> candidates)
    {
        candidates.Clear();
        if (CollectStatistics) QueryCount++;
        Vector3 skin = new(CollisionSkin);
        var expanded = new Aabb(bounds.Min - skin, bounds.Max + skin);
        if (AccelerationEnabled && _nodes.Count > 0) Visit(0);
        else for (int i = 0; i < Triangles.Count; i++) Test(i);
        // Preserve the old triangle order: nearly equal contacts use deterministic tie-breaking.
        candidates.Sort();
        if (CollectStatistics) CandidateTriangles += candidates.Count;

        void Visit(int index)
        {
            var node = _nodes[index];
            if (CollectStatistics) BoundsTests++;
            if (!node.Bounds.Overlaps(expanded)) return;
            if (node.Count > 0) for (int i = node.Start; i < node.Start + node.Count; i++) Test(_indices[i]);
            else { Visit(node.Left); Visit(node.Right); }
        }
        void Test(int index)
        {
            if (CollectStatistics) BoundsTests++;
            if (Triangles[index].Bounds.Overlaps(expanded)) candidates.Add(index);
        }
    }

    public void QueryRay(Ray ray, float tMin, float tMax, List<int> candidates)
    {
        candidates.Clear();
        if (CollectStatistics) QueryCount++;
        if (AccelerationEnabled && _nodes.Count > 0) Visit(0);
        else for (int i = 0; i < Triangles.Count; i++) Test(i);
        candidates.Sort();
        if (CollectStatistics) CandidateTriangles += candidates.Count;

        void Visit(int index)
        {
            var node = _nodes[index];
            if (CollectStatistics) BoundsTests++;
            if (!Raycast.RayIntersectsAabb(ray, node.Bounds, tMin, tMax, out _)) return;
            if (node.Count > 0) for (int i = node.Start; i < node.Start + node.Count; i++) Test(_indices[i]);
            else { Visit(node.Left); Visit(node.Right); }
        }
        void Test(int index)
        {
            if (CollectStatistics) BoundsTests++;
            if (Raycast.RayIntersectsAabb(ray, Triangles[index].Bounds, tMin, tMax, out _)) candidates.Add(index);
        }
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
