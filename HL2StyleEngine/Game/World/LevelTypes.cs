using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.World;

public sealed class LevelFile
{
    public int Version { get; set; } = 1;
    public List<BoxDef> Boxes { get; set; } = new();
}

public sealed class BoxDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Box";

    public SerVec3 Position { get; set; } = new(0, 0, 0);
    public SerVec3 Size { get; set; } = new(1, 1, 1);
    public SerVec4 Color { get; set; } = new(0.6f, 0.6f, 0.6f, 1f);

    public BoxInstance ToBoxInstance()
    {
        return new BoxInstance(
            position: (Vector3)Position,
            size: (Vector3)Size,
            color: (Vector4)Color);
    }
}

public readonly struct SerVec3
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }

    public SerVec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public static implicit operator Vector3(SerVec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator SerVec3(Vector3 v) => new(v.X, v.Y, v.Z);
}

public readonly struct SerVec4
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float W { get; init; }

    public SerVec4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

    public static implicit operator Vector4(SerVec4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator SerVec4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);
}
