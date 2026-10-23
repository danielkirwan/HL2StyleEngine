using System.Numerics;

namespace Engine.Physics.Collision;

public sealed partial class MeshCollisionMesh
{
    private MeshCollisionMesh(MeshCollisionTriangle[] triangles, int[] indices, List<Node> nodes, float skin)
    {
        Triangles = triangles;
        _indices = indices;
        _nodes = nodes;
        CollisionSkin = skin;
        Bounds = new Aabb(nodes[0].Bounds.Min - new Vector3(skin), nodes[0].Bounds.Max + new Vector3(skin));
    }

    internal void WriteCooked(BinaryWriter writer)
    {
        writer.Write(CollisionSkin);
        writer.Write(Triangles.Count);
        foreach (var triangle in Triangles)
        {
            WriteVector(writer, triangle.A); WriteVector(writer, triangle.B); WriteVector(writer, triangle.C);
        }
        foreach (int index in _indices) writer.Write(index);
        writer.Write(_nodes.Count);
        foreach (var node in _nodes)
        {
            WriteVector(writer, node.Bounds.Min); WriteVector(writer, node.Bounds.Max);
            writer.Write(node.Start); writer.Write(node.Count); writer.Write(node.Left); writer.Write(node.Right);
        }
    }

    internal static MeshCollisionMesh ReadCooked(BinaryReader reader)
    {
        float skin = reader.ReadSingle();
        if (!float.IsFinite(skin) || skin < 0) throw new InvalidDataException("Invalid collision skin.");
        int count = reader.ReadInt32();
        if (count < 1 || count > 2_000_000 || count * 40L + 4 > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid collision triangle count.");
        var triangles = new MeshCollisionTriangle[count];
        for (int i = 0; i < count; i++)
            triangles[i] = new(ReadVector(reader), ReadVector(reader), ReadVector(reader));
        var indices = new int[count];
        var seen = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int index = reader.ReadInt32();
            if ((uint)index >= count || seen[index]) throw new InvalidDataException("Invalid collision index permutation.");
            indices[i] = index; seen[index] = true;
        }
        int nodeCount = reader.ReadInt32();
        if (nodeCount < 1 || nodeCount > count * 2 || nodeCount * 40L != reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid collision tree size.");
        var nodes = new List<Node>(nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            var min = ReadVector(reader); var max = ReadVector(reader);
            if (min.X > max.X || min.Y > max.Y || min.Z > max.Z) throw new InvalidDataException("Invalid collision bounds.");
            nodes.Add(new(new Aabb(min, max), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        }

        // Validate topology and conservative containment before trusting cached query pruning.
        var parents = new int[nodeCount];
        var depths = new int[nodeCount];
        Array.Clear(seen);
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (depths[i] > 64 || (i > 0 && parents[i] != 1)) throw new InvalidDataException("Invalid collision tree topology.");
            if (node.Count > 0)
            {
                if (node.Count > 8 || node.Start < 0 || node.Start > count - node.Count || node.Left != -1 || node.Right != -1)
                    throw new InvalidDataException("Invalid collision leaf.");
                for (int j = node.Start; j < node.Start + node.Count; j++)
                {
                    if (seen[j] || !Contains(node.Bounds, triangles[indices[j]].Bounds))
                        throw new InvalidDataException("Collision leaf omits or duplicates geometry.");
                    seen[j] = true;
                }
            }
            else
            {
                if (node.Count != 0 || node.Left <= i || node.Right <= i || node.Left >= nodeCount || node.Right >= nodeCount ||
                    node.Left == node.Right || !Contains(node.Bounds, nodes[node.Left].Bounds) || !Contains(node.Bounds, nodes[node.Right].Bounds))
                    throw new InvalidDataException("Invalid collision branch.");
                if (++parents[node.Left] != 1 || ++parents[node.Right] != 1) throw new InvalidDataException("Shared collision tree child.");
                depths[node.Left] = depths[node.Right] = depths[i] + 1;
            }
        }
        if (seen.Any(value => !value)) throw new InvalidDataException("Incomplete collision tree.");
        return new MeshCollisionMesh(triangles, indices, nodes, skin);
    }

    private static bool Contains(Aabb outer, Aabb inner)
        => outer.Min.X <= inner.Min.X && outer.Min.Y <= inner.Min.Y && outer.Min.Z <= inner.Min.Z &&
           outer.Max.X >= inner.Max.X && outer.Max.Y >= inner.Max.Y && outer.Max.Z >= inner.Max.Z;

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }

    private static Vector3 ReadVector(BinaryReader reader)
    {
        Vector3 value = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || value.LengthSquared() > 1e20f)
            throw new InvalidDataException("Invalid collision coordinate.");
        return value;
    }
}
