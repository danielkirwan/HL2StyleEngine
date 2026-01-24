using System.Numerics;
using System.Text.Json.Serialization;

namespace Engine.Core.Serialization;

public readonly struct SerVec3
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }

    [JsonConstructor]
    public SerVec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public static implicit operator Vector3(SerVec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator SerVec3(Vector3 v) => new(v.X, v.Y, v.Z);
    public override string ToString() => $"<{X}, {Y}, {Z}>";

}

public readonly struct SerVec4
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float W { get; init; }

    [JsonConstructor]
    public SerVec4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

    public static implicit operator Vector4(SerVec4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator SerVec4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);
    public override string ToString() => $"<{X}, {Y}, {Z}, {W}>";

}
