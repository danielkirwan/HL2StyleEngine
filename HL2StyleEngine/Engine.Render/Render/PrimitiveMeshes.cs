using System.Numerics;
using Veldrid;

namespace Engine.Render;

public static class PrimitiveMeshes
{
    public static (VertexPositionColor[] v, ushort[] i) MakeGroundPlane(float halfSize = 50f)
    {
        var c = new RgbaFloat(0.25f, 0.25f, 0.25f, 1f);

        var v = new[]
        {
            new VertexPositionColor(new Vector3(-halfSize, 0, -halfSize), c),
            new VertexPositionColor(new Vector3( halfSize, 0, -halfSize), c),
            new VertexPositionColor(new Vector3( halfSize, 0,  halfSize), c),
            new VertexPositionColor(new Vector3(-halfSize, 0,  halfSize), c),
        };

        var i = new ushort[]
        {
            0, 1, 2,
            0, 2, 3
        };

        return (v, i);
    }

    public static (VertexPositionColor[] v, ushort[] i) MakeCube(float half = 0.5f, Vector3 center = default)
    {
        // 8 corners
        Vector3 p000 = center + new Vector3(-half, -half, -half);
        Vector3 p001 = center + new Vector3(-half, -half, half);
        Vector3 p010 = center + new Vector3(-half, half, -half);
        Vector3 p011 = center + new Vector3(-half, half, half);
        Vector3 p100 = center + new Vector3(half, -half, -half);
        Vector3 p101 = center + new Vector3(half, -half, half);
        Vector3 p110 = center + new Vector3(half, half, -half);
        Vector3 p111 = center + new Vector3(half, half, half);

        // Give corners slightly different colors so you can read orientation
        var r = new RgbaFloat(1f, 0.2f, 0.2f, 1f);
        var g = new RgbaFloat(0.2f, 1f, 0.2f, 1f);
        var b = new RgbaFloat(0.2f, 0.4f, 1f, 1f);
        var w = new RgbaFloat(0.9f, 0.9f, 0.9f, 1f);

        var v = new[]
        {
            new VertexPositionColor(p000, w),
            new VertexPositionColor(p001, r),
            new VertexPositionColor(p010, g),
            new VertexPositionColor(p011, b),
            new VertexPositionColor(p100, r),
            new VertexPositionColor(p101, g),
            new VertexPositionColor(p110, b),
            new VertexPositionColor(p111, w),
        };

        // 12 triangles (two per face)
        var i = new ushort[]
        {
            // -X
            0,2,3, 0,3,1,
            // +X
            4,5,7, 4,7,6,
            // -Y
            0,1,5, 0,5,4,
            // +Y
            2,6,7, 2,7,3,
            // -Z
            0,4,6, 0,6,2,
            // +Z
            1,3,7, 1,7,5
        };

        return (v, i);
    }
}
